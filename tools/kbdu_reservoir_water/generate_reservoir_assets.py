#!/usr/bin/env python3
"""Generate deterministic, Quest-bounded Boulder Reservoir water textures.

The packed depth channel is an artistic distance-to-shore approximation derived
from the already-authored OSM shoreline mesh.  It is not surveyed bathymetry.
Only Python's standard library is required.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
import re
import struct
import zlib
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Sequence


REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_MESH = REPO_ROOT / (
    "QuestFlight/Assets/Production/Environment/Generated/"
    "BoulderReservoir_OSM_AuthoritativeWater_Mesh.asset"
)
DEFAULT_REFERENCE = REPO_ROOT / (
    "QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/"
    "kbdu_reference_context.json"
)
DEFAULT_OUTPUT_DIR = REPO_ROOT / (
    "QuestFlight/Assets/Production/Environment/Textures/Reservoir"
)
DEFAULT_MANIFEST = Path(__file__).with_name("asset_manifest.json")
TEXTURE_SIZE = 512
SOURCE_OSM_ID = 35597714
SOURCE_MESH_GUID = "9adf30d07a69af340bd274a2db6be902"


@dataclass(frozen=True)
class ReservoirMesh:
    vertices: tuple[tuple[float, float, float], ...]
    normals: tuple[tuple[float, float, float], ...]
    uvs: tuple[tuple[float, float], ...]
    triangles: tuple[tuple[int, int, int], ...]
    boundary: tuple[int, ...]
    center_xz: tuple[float, float]
    extent_xz: tuple[float, float]
    source_sha256: str


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _required_match(pattern: str, text: str, label: str) -> re.Match[str]:
    match = re.search(pattern, text, flags=re.MULTILINE)
    if match is None:
        raise ValueError(f"Unity mesh is missing {label}")
    return match


def _recover_boundary(triangles: Sequence[tuple[int, int, int]]) -> tuple[int, ...]:
    edge_counts: Counter[tuple[int, int]] = Counter()
    for triangle in triangles:
        for a, b in ((triangle[0], triangle[1]), (triangle[1], triangle[2]), (triangle[2], triangle[0])):
            edge_counts[tuple(sorted((a, b)))] += 1
    boundary_edges = [edge for edge, count in edge_counts.items() if count == 1]
    adjacency: dict[int, list[int]] = defaultdict(list)
    for a, b in boundary_edges:
        adjacency[a].append(b)
        adjacency[b].append(a)
    if not adjacency or any(len(neighbors) != 2 for neighbors in adjacency.values()):
        raise ValueError("Authoritative water mesh boundary is not one or more closed loops")

    start = min(adjacency)
    loop = [start]
    previous = -1
    current = start
    while True:
        candidates = adjacency[current]
        next_vertex = candidates[0] if candidates[0] != previous else candidates[1]
        if next_vertex == start:
            break
        if next_vertex in loop:
            raise ValueError("Authoritative water mesh boundary revisits a vertex")
        loop.append(next_vertex)
        previous, current = current, next_vertex
    if len(loop) != len(adjacency):
        raise ValueError("Authoritative water mesh has multiple boundary loops")
    return tuple(loop)


def parse_unity_mesh(path: Path) -> ReservoirMesh:
    text = path.read_text(encoding="utf-8")
    submesh_count = len(re.findall(r"^  - serializedVersion: 2$", text, flags=re.MULTILINE))
    if submesh_count != 1:
        raise ValueError(f"Expected one reservoir submesh, found {submesh_count}")
    if int(_required_match(r"^  m_IndexFormat: (\d+)$", text, "index format").group(1)) != 0:
        raise ValueError("Reservoir validator currently requires UInt16 indices")

    vertex_section = text.index("  m_VertexData:")
    vertex_text = text[vertex_section:]
    vertex_count = int(_required_match(r"^    m_VertexCount: (\d+)$", vertex_text, "vertex count").group(1))
    data_size = int(_required_match(r"^    m_DataSize: (\d+)$", vertex_text, "vertex data size").group(1))
    raw_hex = _required_match(r"^    _typelessdata: ([0-9a-fA-F]+)$", vertex_text, "vertex payload").group(1)
    raw_vertices = bytes.fromhex(raw_hex)
    if len(raw_vertices) != data_size or data_size % vertex_count:
        raise ValueError("Reservoir vertex payload length/stride is invalid")
    stride = data_size // vertex_count
    if stride != 32:
        raise ValueError(f"Expected position/normal/UV stride 32, found {stride}")

    vertices = tuple(struct.unpack_from("<fff", raw_vertices, index * stride) for index in range(vertex_count))
    normals = tuple(struct.unpack_from("<fff", raw_vertices, index * stride + 12) for index in range(vertex_count))
    uvs = tuple(struct.unpack_from("<ff", raw_vertices, index * stride + 24) for index in range(vertex_count))

    index_count = int(_required_match(r"^    indexCount: (\d+)$", text, "index count").group(1))
    raw_indices = bytes.fromhex(_required_match(r"^  m_IndexBuffer: ([0-9a-fA-F]+)$", text, "index buffer").group(1))
    if len(raw_indices) != index_count * 2 or index_count % 3:
        raise ValueError("Reservoir index buffer length is invalid")
    indices = struct.unpack(f"<{index_count}H", raw_indices)
    if max(indices) >= vertex_count:
        raise ValueError("Reservoir index buffer references an invalid vertex")
    triangles = tuple(tuple(indices[offset : offset + 3]) for offset in range(0, index_count, 3))
    boundary = _recover_boundary(triangles)

    xs = [vertex[0] for vertex in vertices]
    zs = [vertex[2] for vertex in vertices]
    center_xz = ((min(xs) + max(xs)) * 0.5, (min(zs) + max(zs)) * 0.5)
    extent_xz = ((max(xs) - min(xs)) * 0.5, (max(zs) - min(zs)) * 0.5)
    return ReservoirMesh(
        vertices=vertices,
        normals=normals,
        uvs=uvs,
        triangles=triangles,
        boundary=boundary,
        center_xz=center_xz,
        extent_xz=extent_xz,
        source_sha256=sha256(path),
    )


def _orientation(a: tuple[float, float], b: tuple[float, float], c: tuple[float, float]) -> float:
    return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])


def count_self_intersections(points: Sequence[tuple[float, float]]) -> int:
    count = 0
    segment_count = len(points)
    for first in range(segment_count):
        a = points[first]
        b = points[(first + 1) % segment_count]
        for second in range(first + 1, segment_count):
            if second in (first, (first + 1) % segment_count) or first == (second + 1) % segment_count:
                continue
            c = points[second]
            d = points[(second + 1) % segment_count]
            ab_c = _orientation(a, b, c)
            ab_d = _orientation(a, b, d)
            cd_a = _orientation(c, d, a)
            cd_b = _orientation(c, d, b)
            if ab_c * ab_d < -1e-8 and cd_a * cd_b < -1e-8:
                count += 1
    return count


def _boulder_reservoir_source(reference_path: Path) -> tuple[dict, list[tuple[float, float]], float]:
    document = json.loads(reference_path.read_text(encoding="utf-8"))
    matching = [
        feature
        for feature in document["openstreetmap"]["features"]
        if feature.get("tags", {}).get("name") == "Boulder Reservoir"
    ]
    if len(matching) != 1:
        raise ValueError(f"Expected one Boulder Reservoir source polygon, found {len(matching)}")
    feature = matching[0]
    if feature["source"].get("osm_id") != SOURCE_OSM_ID or feature.get("geometry_type") != "polygon":
        raise ValueError("Boulder Reservoir OSM provenance changed unexpectedly")
    quantization = float(document["coordinate_quantization_meters"])
    packed = feature["points_q"]
    points = [(packed[index] * quantization, packed[index + 1] * quantization) for index in range(0, len(packed), 2)]
    if points[0] != points[-1]:
        raise ValueError("Boulder Reservoir source polygon is not closed")
    return feature, points[:-1], quantization


def _chaikin_once(points: Sequence[tuple[float, float]]) -> list[tuple[float, float]]:
    smoothed: list[tuple[float, float]] = []
    for index, first in enumerate(points):
        second = points[(index + 1) % len(points)]
        smoothed.append((first[0] * 0.75 + second[0] * 0.25, first[1] * 0.75 + second[1] * 0.25))
        smoothed.append((first[0] * 0.25 + second[0] * 0.75, first[1] * 0.25 + second[1] * 0.75))
    return smoothed


def validate_geometry(mesh: ReservoirMesh, reference_path: Path) -> dict:
    feature, source_points, quantization = _boulder_reservoir_source(reference_path)
    expected = _chaikin_once(source_points)
    authored = [(vertex[0], vertex[2]) for vertex in mesh.vertices]
    maximum_delta = max(math.dist(first, second) for first, second in zip(expected, authored))
    if len(source_points) != 127 or len(authored) != 254 or len(mesh.boundary) != 254:
        raise ValueError("Boulder Reservoir source/smoothed shoreline vertex contract changed")
    if maximum_delta > 1e-4:
        raise ValueError(f"Authored water mesh no longer matches its smoothed OSM shoreline ({maximum_delta:.6f} m)")
    intersections = count_self_intersections(authored)
    if intersections:
        raise ValueError(f"Authored water shoreline has {intersections} self-intersection(s)")

    y_values = [vertex[1] for vertex in mesh.vertices]
    if max(y_values) - min(y_values) > 1e-5:
        raise ValueError("Reservoir surface is not one immutable plane")
    if any(normal[1] < 0.999 for normal in mesh.normals):
        raise ValueError("Reservoir surface normals are not stable/upward")

    degenerate_triangles = 0
    area = 0.0
    for a_index, b_index, c_index in mesh.triangles:
        a = authored[a_index]
        b = authored[b_index]
        c = authored[c_index]
        twice_area = abs(_orientation(a, b, c))
        if twice_area <= 1e-5:
            degenerate_triangles += 1
        area += twice_area * 0.5
    if degenerate_triangles:
        raise ValueError(f"Reservoir mesh has {degenerate_triangles} degenerate triangle(s)")

    perimeter = sum(math.dist(authored[index], authored[(index + 1) % len(authored)]) for index in range(len(authored)))
    return {
        "authoritative_mesh_guid": SOURCE_MESH_GUID,
        "osm_id": feature["source"]["osm_id"],
        "osm_type": feature["source"]["osm_type"],
        "source_vertex_count": len(source_points),
        "smoothed_boundary_vertex_count": len(mesh.boundary),
        "triangle_count": len(mesh.triangles),
        "submesh_count": 1,
        "self_intersection_count": intersections,
        "maximum_osm_to_mesh_delta_meters": round(maximum_delta, 7),
        "plane_height_local_meters": round(y_values[0], 7),
        "surface_area_square_meters": round(area, 3),
        "shoreline_perimeter_meters": round(perimeter, 3),
        "center_xz_meters": [round(value, 7) for value in mesh.center_xz],
        "extent_xz_meters": [round(value, 7) for value in mesh.extent_xz],
        "coordinate_quantization_meters": quantization,
    }


def _png_chunk(kind: bytes, payload: bytes) -> bytes:
    checksum = zlib.crc32(kind)
    checksum = zlib.crc32(payload, checksum) & 0xFFFFFFFF
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", checksum)


def encode_rgb_png(width: int, height: int, pixels: bytes) -> bytes:
    if len(pixels) != width * height * 3:
        raise ValueError("RGB payload has the wrong length")
    scanlines = bytearray()
    row_bytes = width * 3
    for row in range(height):
        scanlines.append(0)  # deterministic PNG filter: None
        scanlines.extend(pixels[row * row_bytes : (row + 1) * row_bytes])
    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    return (
        b"\x89PNG\r\n\x1a\n"
        + _png_chunk(b"IHDR", header)
        + _png_chunk(b"IDAT", zlib.compress(bytes(scanlines), level=9))
        + _png_chunk(b"IEND", b"")
    )


def _spectral_waves(
    seed: int,
    count: int,
    minimum_frequency: int,
    maximum_frequency: int,
    amplitude_falloff: float,
) -> tuple[tuple[int, int, float, float], ...]:
    """Build a deterministic, tileable, directionally varied wave spectrum."""
    generator = random.Random(seed)
    waves: list[tuple[int, int, float, float]] = []
    used: set[tuple[int, int]] = set()
    while len(waves) < count:
        frequency_u = generator.randint(-maximum_frequency, maximum_frequency)
        frequency_v = generator.randint(-maximum_frequency, maximum_frequency)
        magnitude = math.hypot(frequency_u, frequency_v)
        if magnitude < minimum_frequency or magnitude > maximum_frequency:
            continue
        canonical = (frequency_u, frequency_v)
        if canonical < (0, 0):
            canonical = (-frequency_u, -frequency_v)
        if canonical in used:
            continue
        used.add(canonical)
        amplitude = generator.uniform(0.72, 1.28) / math.pow(magnitude, amplitude_falloff)
        waves.append((frequency_u, frequency_v, amplitude, generator.uniform(0.0, math.tau)))
    return tuple(waves)


def _normal_map(size: int, waves: Sequence[tuple[int, int, float, float]], strength: float) -> bytes:
    pixels = bytearray(size * size * 3)
    tau = math.tau
    maximum_slope = sum(
        abs(amplitude) * tau * math.hypot(frequency_u, frequency_v)
        for frequency_u, frequency_v, amplitude, _ in waves
    )
    if maximum_slope <= 0.0:
        raise ValueError("Normal-map spectrum has no slope")
    for row in range(size):
        v = (row + 0.5) / size
        for column in range(size):
            u = (column + 0.5) / size
            slope_u = 0.0
            slope_v = 0.0
            for frequency_u, frequency_v, amplitude, phase in waves:
                cosine = math.cos(tau * (frequency_u * u + frequency_v * v) + phase)
                slope_u += amplitude * tau * frequency_u * cosine
                slope_v += amplitude * tau * frequency_v * cosine
            nx = -slope_u / maximum_slope * strength
            ny = -slope_v / maximum_slope * strength
            nz = 1.0
            inverse_length = 1.0 / math.sqrt(nx * nx + ny * ny + nz * nz)
            rgb = (
                int(round((nx * inverse_length * 0.5 + 0.5) * 255.0)),
                int(round((ny * inverse_length * 0.5 + 0.5) * 255.0)),
                int(round((nz * inverse_length * 0.5 + 0.5) * 255.0)),
            )
            offset = (row * size + column) * 3
            pixels[offset : offset + 3] = bytes(max(0, min(255, value)) for value in rgb)
    return bytes(pixels)


def _scanline_inside_mask(
    points: Sequence[tuple[float, float]],
    center_xz: tuple[float, float],
    extent_xz: tuple[float, float],
    size: int,
) -> bytearray:
    minimum_x = center_xz[0] - extent_xz[0]
    minimum_z = center_xz[1] - extent_xz[1]
    width = extent_xz[0] * 2.0
    height = extent_xz[1] * 2.0
    inside = bytearray(size * size)
    for row in range(size):
        # PNG row zero is north/top; Unity UV v=1 addresses the same edge.
        world_z = minimum_z + (1.0 - (row + 0.5) / size) * height
        intersections: list[float] = []
        for index, first in enumerate(points):
            second = points[(index + 1) % len(points)]
            if (first[1] <= world_z < second[1]) or (second[1] <= world_z < first[1]):
                fraction = (world_z - first[1]) / (second[1] - first[1])
                intersections.append(first[0] + fraction * (second[0] - first[0]))
        intersections.sort()
        for pair in range(0, len(intersections) - 1, 2):
            left = max(0, math.ceil((intersections[pair] - minimum_x) / width * size - 0.5))
            right = min(size - 1, math.floor((intersections[pair + 1] - minimum_x) / width * size - 0.5))
            if right >= left:
                inside[row * size + left : row * size + right + 1] = b"\x01" * (right - left + 1)
    return inside


def _inside_distance(inside: Sequence[int], size: int) -> list[float]:
    infinity = float(size * size)
    distance = [infinity if value else 0.0 for value in inside]
    diagonal = math.sqrt(2.0)
    for row in range(size):
        for column in range(size):
            index = row * size + column
            if not inside[index]:
                continue
            best = distance[index]
            if column:
                best = min(best, distance[index - 1] + 1.0)
            if row:
                best = min(best, distance[index - size] + 1.0)
                if column:
                    best = min(best, distance[index - size - 1] + diagonal)
                if column + 1 < size:
                    best = min(best, distance[index - size + 1] + diagonal)
            distance[index] = best
    for row in range(size - 1, -1, -1):
        for column in range(size - 1, -1, -1):
            index = row * size + column
            if not inside[index]:
                continue
            best = distance[index]
            if column + 1 < size:
                best = min(best, distance[index + 1] + 1.0)
            if row + 1 < size:
                best = min(best, distance[index + size] + 1.0)
                if column:
                    best = min(best, distance[index + size - 1] + diagonal)
                if column + 1 < size:
                    best = min(best, distance[index + size + 1] + diagonal)
            distance[index] = best
    return distance


def _smoothstep(edge0: float, edge1: float, value: float) -> float:
    value = max(0.0, min(1.0, (value - edge0) / (edge1 - edge0)))
    return value * value * (3.0 - 2.0 * value)


def _packed_depth_shore_roughness(
    points: Sequence[tuple[float, float]],
    center_xz: tuple[float, float],
    extent_xz: tuple[float, float],
    size: int,
) -> tuple[bytes, dict]:
    inside = _scanline_inside_mask(points, center_xz, extent_xz, size)
    distance = _inside_distance(inside, size)
    maximum_distance = max(distance[index] for index, value in enumerate(inside) if value)
    depth_distance_pixels = min(112.0, maximum_distance)
    shore_width_pixels = 9.0
    pixels = bytearray(size * size * 3)
    depth_values: list[int] = []
    shore_values: list[int] = []
    for row in range(size):
        v = 1.0 - (row + 0.5) / size
        for column in range(size):
            index = row * size + column
            if not inside[index]:
                continue
            u = (column + 0.5) / size
            depth = _smoothstep(0.0, depth_distance_pixels, distance[index])
            shore = 1.0 - _smoothstep(0.5, shore_width_pixels, distance[index])
            roughness_noise = (
                0.50
                + 0.22 * math.sin(math.tau * (3.0 * u + 2.0 * v) + 0.37)
                + 0.16 * math.cos(math.tau * (7.0 * u - 5.0 * v) + 1.11)
                + 0.08 * math.sin(math.tau * (13.0 * u + 11.0 * v) + 2.03)
            )
            roughness = max(0.0, min(1.0, 0.28 + 0.48 * roughness_noise + 0.12 * (1.0 - depth)))
            values = (
                int(round(depth * 255.0)),
                int(round(shore * 255.0)),
                int(round(roughness * 255.0)),
            )
            offset = index * 3
            pixels[offset : offset + 3] = bytes(values)
            depth_values.append(values[0])
            shore_values.append(values[1])
    return bytes(pixels), {
        "inside_pixel_count": sum(inside),
        "coverage_fraction": round(sum(inside) / (size * size), 7),
        "maximum_inside_distance_pixels": round(maximum_distance, 5),
        "depth_distance_pixels": depth_distance_pixels,
        "shore_band_width_pixels": shore_width_pixels,
        "depth_channel_range": [min(depth_values), max(depth_values)],
        "shore_channel_range": [min(shore_values), max(shore_values)],
    }


def _relative_or_name(path: Path) -> str:
    try:
        return path.resolve().relative_to(REPO_ROOT.resolve()).as_posix()
    except ValueError:
        return path.name


def _astc_6x6_full_mip_bytes(size: int) -> int:
    total = 0
    level = size
    while True:
        blocks = math.ceil(level / 6)
        total += blocks * blocks * 16
        if level == 1:
            return total
        level = max(1, level // 2)


def generate(mesh_path: Path, reference_path: Path, output_dir: Path, manifest_path: Path) -> dict:
    mesh = parse_unity_mesh(mesh_path)
    geometry = validate_geometry(mesh, reference_path)
    output_dir.mkdir(parents=True, exist_ok=True)

    broad_pixels = _normal_map(
        TEXTURE_SIZE,
        _spectral_waves(0xB01D3E, 28, 1, 11, 2.05),
        strength=0.62,
    )
    ripple_pixels = _normal_map(
        TEXTURE_SIZE,
        _spectral_waves(0x51A11E, 52, 6, 34, 1.85),
        strength=0.46,
    )
    boundary_points = [(mesh.vertices[index][0], mesh.vertices[index][2]) for index in range(len(mesh.vertices))]
    packed_pixels, mask_statistics = _packed_depth_shore_roughness(
        boundary_points, mesh.center_xz, mesh.extent_xz, TEXTURE_SIZE
    )

    assets = (
        ("BoulderReservoir_BroadWave_NM.png", broad_pixels, "project-authored tileable broad-wave tangent normal"),
        ("BoulderReservoir_Ripple_NM.png", ripple_pixels, "project-authored tileable fine-ripple tangent normal"),
        (
            "BoulderReservoir_DepthShoreRoughness.png",
            packed_pixels,
            "R=artistic distance-to-shore depth, G=shore band, B=roughness variation",
        ),
    )
    derived_assets = []
    for filename, pixels, purpose in assets:
        path = output_dir / filename
        path.write_bytes(encode_rgb_png(TEXTURE_SIZE, TEXTURE_SIZE, pixels))
        derived_assets.append(
            {
                "path": _relative_or_name(path),
                "bytes": path.stat().st_size,
                "sha256": sha256(path),
                "width": TEXTURE_SIZE,
                "height": TEXTURE_SIZE,
                "purpose": purpose,
            }
        )

    manifest = {
        "schema_version": 1,
        "label": "Boulder Reservoir Quest opaque-water derivatives",
        "generator": {
            "path": _relative_or_name(Path(__file__)),
            "sha256": sha256(Path(__file__)),
            "runtime_dependencies": [],
            "python_dependencies": "standard library only",
        },
        "source": {
            "authoritative_mesh_path": _relative_or_name(mesh_path),
            "authoritative_mesh_guid": SOURCE_MESH_GUID,
            "authoritative_mesh_sha256": mesh.source_sha256,
            "reference_context_path": _relative_or_name(reference_path),
            "reference_context_sha256": sha256(reference_path),
            "openstreetmap_id": SOURCE_OSM_ID,
            "attribution": "© OpenStreetMap contributors",
            "license": "ODbL-1.0",
            "license_url": "https://www.openstreetmap.org/copyright",
        },
        "authorship_and_license": {
            "normal_maps": "Project-authored deterministic mathematical textures; repository project license.",
            "packed_mask": (
                "Project-authored rendered derivative of the existing OSM shoreline; "
                "retain © OpenStreetMap contributors attribution under ODbL-1.0."
            ),
            "prohibited_sources_confirmation": "No Google Maps, Google Earth, Street View, or surveyed bathymetry used.",
        },
        "limitations": [
            "The depth channel is an artistic distance-to-shore bowl and is not surveyed bathymetry.",
            "The shoreline is the existing simplified/quantized OSM-derived product boundary and is not for navigation.",
            "The selected sky cubemap is injected during integration and is not generated by this tool.",
        ],
        "geometry_validation": geometry,
        "packed_mask_statistics": mask_statistics,
        "derived_assets": derived_assets,
        "estimated_android_astc_6x6_bytes_with_full_mips": _astc_6x6_full_mip_bytes(TEXTURE_SIZE) * 3,
        "runtime_cost_contract": {
            "water_surface_renderers": 1,
            "water_surface_materials": 1,
            "water_surface_draw_slots": 1,
            "water_surface_triangles": len(mesh.triangles),
            "incremental_texture_samples_per_fragment": 4,
            "passes": 1,
            "transparent_slots": 0,
        },
    }
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return manifest


def parse_args(argv: Iterable[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mesh", type=Path, default=DEFAULT_MESH)
    parser.add_argument("--reference", type=Path, default=DEFAULT_REFERENCE)
    parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT_DIR)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    return parser.parse_args(argv)


def main(argv: Iterable[str] | None = None) -> int:
    args = parse_args(argv)
    manifest = generate(args.mesh.resolve(), args.reference.resolve(), args.output_dir.resolve(), args.manifest.resolve())
    geometry = manifest["geometry_validation"]
    print(
        "Generated Boulder Reservoir water assets: "
        f"{geometry['triangle_count']} triangles, {geometry['smoothed_boundary_vertex_count']} shoreline vertices, "
        f"{len(manifest['derived_assets'])} x {TEXTURE_SIZE}px textures"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
