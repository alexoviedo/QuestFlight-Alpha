#!/usr/bin/env python3
"""Generate deterministic natural-color/OSM vegetation masks, placements, and procedural LOD meshes."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import struct
import tempfile
import zlib
from pathlib import Path
from typing import Any, Callable


SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[1]
DEFAULT_CONFIG = SCRIPT_DIR / "config.json"
DEFAULT_CONTEXT = SCRIPT_DIR / "KBDU_ReferenceContextV2.json"
DEFAULT_PARKING = SCRIPT_DIR / "airport_parking_stand_manifest.json"
DEFAULT_OUTPUT_DIR = SCRIPT_DIR

MASK_SIZE = 1024
HALF_EXTENT_METERS = 6000.0
METERS_PER_PIXEL = HALF_EXTENT_METERS * 2.0 / MASK_SIZE
ACTIVE_RADIUS_METERS = 1500.0
LIKELIHOOD_THRESHOLD = 190
MAX_PLACEMENTS = 700
MINIMUM_SPACING_METERS = 24.0

EXCLUSION_CODES = {
    "none": 0,
    "building": 32,
    "road": 64,
    "open_water": 96,
    "apron": 128,
    "taxiway": 160,
    "runway_safety": 192,
    "parking": 224,
}

SPECIES = {
    "plains_cottonwood": {
        "canonical_height_meters": 11.0,
        "height_range_meters": [8.0, 14.0],
        "crown_range_meters": [3.0, 5.0],
        "foliage_material": "foliage_deciduous_shared",
        "form": "broad_deciduous",
    },
    "narrowleaf_willow": {
        "canonical_height_meters": 6.0,
        "height_range_meters": [4.0, 8.0],
        "crown_range_meters": [2.5, 4.0],
        "foliage_material": "foliage_deciduous_shared",
        "form": "riparian_deciduous",
    },
    "rocky_mountain_juniper": {
        "canonical_height_meters": 5.5,
        "height_range_meters": [4.0, 7.0],
        "crown_range_meters": [1.8, 3.0],
        "foliage_material": "foliage_conifer_shared",
        "form": "compact_conifer",
    },
    "rabbitbrush_shrub": {
        "canonical_height_meters": 1.7,
        "height_range_meters": [1.0, 2.4],
        "crown_range_meters": [1.0, 2.0],
        "foliage_material": "foliage_shrub_shared",
        "form": "multi_lobe_shrub",
    },
}


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def write_bytes(path: Path, value: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as handle:
        temporary = Path(handle.name)
        handle.write(value)
    temporary.replace(path)


def write_json(path: Path, value: Any, *, compact: bool = False) -> None:
    text = json.dumps(value, sort_keys=True, separators=(",", ":")) if compact else json.dumps(value, indent=2, sort_keys=True)
    write_bytes(path, (text + "\n").encode("utf-8"))


def read_ppm(path: Path) -> tuple[int, int, bytes]:
    payload = path.read_bytes()
    marker = payload.find(b"\n255\n")
    if not payload.startswith(b"P6\n") or marker < 0:
        raise ValueError(f"Expected binary P6 PPM input: {path}")
    dimensions = payload[3 : payload.find(b"\n", 3)].decode("ascii").split()
    width, height = map(int, dimensions)
    pixels = payload[marker + 5 :]
    if len(pixels) != width * height * 3:
        raise ValueError("PPM payload length does not match its dimensions")
    return width, height, pixels


def png_chunk(name: bytes, payload: bytes) -> bytes:
    return struct.pack(">I", len(payload)) + name + payload + struct.pack(">I", zlib.crc32(name + payload) & 0xFFFFFFFF)


def write_grayscale_png(path: Path, width: int, height: int, pixels: bytes | bytearray) -> None:
    rows = b"".join(b"\x00" + bytes(pixels[y * width : (y + 1) * width]) for y in range(height))
    payload = b"\x89PNG\r\n\x1a\n"
    payload += png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 0, 0, 0, 0))
    payload += png_chunk(b"IDAT", zlib.compress(rows, level=9))
    payload += png_chunk(b"IEND", b"")
    write_bytes(path, payload)


def decode_points(feature: dict[str, Any], quantization: float) -> list[tuple[float, float]]:
    values = feature["points_q"]
    return [(values[index] * quantization, values[index + 1] * quantization) for index in range(0, len(values), 2)]


def to_pixel(x: float, z: float) -> tuple[float, float]:
    return (x + HALF_EXTENT_METERS) / (2.0 * HALF_EXTENT_METERS) * MASK_SIZE, (HALF_EXTENT_METERS - z) / (2.0 * HALF_EXTENT_METERS) * MASK_SIZE


def to_world(px: int, py: int) -> tuple[float, float]:
    return -HALF_EXTENT_METERS + (px + 0.5) * METERS_PER_PIXEL, HALF_EXTENT_METERS - (py + 0.5) * METERS_PER_PIXEL


def apply_code(mask: bytearray, x: int, y: int, code: int) -> None:
    if 0 <= x < MASK_SIZE and 0 <= y < MASK_SIZE:
        index = y * MASK_SIZE + x
        mask[index] = max(mask[index], code)


def draw_disk(mask: bytearray, x: float, y: float, radius: int, code: int) -> None:
    center_x, center_y = round(x), round(y)
    radius_squared = radius * radius
    for py in range(center_y - radius, center_y + radius + 1):
        for px in range(center_x - radius, center_x + radius + 1):
            if (px - center_x) ** 2 + (py - center_y) ** 2 <= radius_squared:
                apply_code(mask, px, py, code)


def draw_segment(mask: bytearray, first: tuple[float, float], second: tuple[float, float], radius: int, code: int) -> None:
    dx, dy = second[0] - first[0], second[1] - first[1]
    steps = max(1, math.ceil(max(abs(dx), abs(dy))))
    for step in range(steps + 1):
        amount = step / steps
        draw_disk(mask, first[0] + dx * amount, first[1] + dy * amount, radius, code)


def fill_polygon(mask: bytearray, points: list[tuple[float, float]], radius: int, code: int) -> None:
    if len(points) > 1 and points[0] == points[-1]:
        points = points[:-1]
    if len(points) < 3:
        return
    minimum_y = max(0, math.floor(min(point[1] for point in points)))
    maximum_y = min(MASK_SIZE - 1, math.ceil(max(point[1] for point in points)))
    for py in range(minimum_y, maximum_y + 1):
        scan_y = py + 0.5
        intersections = []
        for first, second in zip(points, points[1:] + points[:1]):
            if (first[1] > scan_y) == (second[1] > scan_y):
                continue
            intersections.append(first[0] + (second[0] - first[0]) * (scan_y - first[1]) / (second[1] - first[1]))
        intersections.sort()
        for first, second in zip(intersections[0::2], intersections[1::2]):
            for px in range(max(0, math.floor(first)), min(MASK_SIZE - 1, math.ceil(second)) + 1):
                apply_code(mask, px, py, code)
    for first, second in zip(points, points[1:] + points[:1]):
        draw_segment(mask, first, second, radius, code)


def rasterize_exclusions(context: dict[str, Any], parking: dict[str, Any]) -> tuple[bytearray, bytearray]:
    exclusion = bytearray(MASK_SIZE * MASK_SIZE)
    water_proximity = bytearray(MASK_SIZE * MASK_SIZE)
    quantization = float(context["coordinate_quantization_meters"])
    for feature in context["openstreetmap"]["features"]:
        category = feature["category"]
        tags = feature.get("tags") or {}
        points_world = decode_points(feature, quantization)
        if not points_world:
            continue
        if all(math.hypot(x, z) > HALF_EXTENT_METERS * 1.45 for x, z in points_world):
            continue
        points = [to_pixel(x, z) for x, z in points_world]
        polygon = feature["geometry_type"] == "polygon"
        if category == "building":
            code, buffer_meters = EXCLUSION_CODES["building"], 4.0
        elif category == "road":
            code, buffer_meters = EXCLUSION_CODES["road"], 8.0 + float(feature.get("render_width_meters") or 0.0) * 0.5
        elif category == "water":
            code, buffer_meters = EXCLUSION_CODES["open_water"], 5.0
        elif category == "aeroway":
            aeroway = str(tags.get("aeroway") or "")
            if aeroway == "runway":
                code, buffer_meters = EXCLUSION_CODES["runway_safety"], 30.0
            elif aeroway in ("taxiway", "taxilane"):
                code, buffer_meters = EXCLUSION_CODES["taxiway"], 12.0
            elif aeroway == "apron":
                code, buffer_meters = EXCLUSION_CODES["apron"], 5.0
            else:
                continue
        else:
            continue
        radius = max(1, math.ceil(buffer_meters / METERS_PER_PIXEL))
        if polygon:
            fill_polygon(exclusion, points, radius, code)
        else:
            for first, second in zip(points, points[1:]):
                draw_segment(exclusion, first, second, radius, code)
        if category == "water":
            proximity_radius = max(radius, math.ceil(80.0 / METERS_PER_PIXEL))
            if polygon:
                fill_polygon(water_proximity, points, proximity_radius, 255)
            else:
                for first, second in zip(points, points[1:]):
                    draw_segment(water_proximity, first, second, proximity_radius, 255)
    parking_radius = max(1, math.ceil(8.0 / METERS_PER_PIXEL))
    for stand in parking["stands"]:
        point = to_pixel(stand["local_enu_meters"]["x"], stand["local_enu_meters"]["z"])
        draw_disk(exclusion, point[0], point[1], parking_radius, EXCLUSION_CODES["parking"])
    return exclusion, water_proximity


def likelihood_mask(pixels: bytes) -> bytearray:
    result = bytearray(MASK_SIZE * MASK_SIZE)
    for y in range(MASK_SIZE):
        for x in range(MASK_SIZE):
            index = y * MASK_SIZE + x
            source = index * 3
            red, green, blue = pixels[source : source + 3]
            luminance = (red * 3.0 + green * 5.0 + blue * 2.0) / 10.0
            excess_green = max(0.0, 2.0 * green - red - blue)
            saturation = max(red, green, blue) - min(red, green, blue)
            texture = 0.0
            comparisons = 0
            if x + 1 < MASK_SIZE:
                other = source + 3
                texture += abs(red - pixels[other]) + abs(green - pixels[other + 1]) + abs(blue - pixels[other + 2])
                comparisons += 3
            if y + 1 < MASK_SIZE:
                other = source + MASK_SIZE * 3
                texture += abs(red - pixels[other]) + abs(green - pixels[other + 1]) + abs(blue - pixels[other + 2])
                comparisons += 3
            texture = texture / comparisons if comparisons else 0.0
            score = excess_green * 1.7 + max(0.0, 150.0 - luminance) * 0.35 + saturation * 0.15 + texture * 1.25
            result[index] = max(0, min(255, round(score)))
    return result


def scaled_range(bounds: list[float], fraction: float) -> float:
    return round(float(bounds[0]) + (float(bounds[1]) - float(bounds[0])) * fraction, 3)


def choose_species(digest: bytes, riparian: bool) -> str:
    value = digest[0]
    if riparian:
        return "plains_cottonwood" if value < 150 else "narrowleaf_willow"
    if value < 130:
        return "rabbitbrush_shrub"
    if value < 205:
        return "plains_cottonwood"
    return "rocky_mountain_juniper"


def select_placements(likelihood: bytearray, exclusion: bytearray, water_proximity: bytearray, source_hash: str) -> tuple[list[dict[str, Any]], dict[str, int]]:
    candidates = []
    counters = {"below_threshold": 0, "excluded": 0, "outside_active_radius": 0, "spacing_rejected": 0, "cap_rejected": 0}
    for py in range(MASK_SIZE):
        for px in range(MASK_SIZE):
            index = py * MASK_SIZE + px
            score = likelihood[index]
            if score < LIKELIHOOD_THRESHOLD:
                counters["below_threshold"] += 1
                continue
            x, z = to_world(px, py)
            if math.hypot(x, z) > ACTIVE_RADIUS_METERS:
                counters["outside_active_radius"] += 1
                continue
            if exclusion[index]:
                counters["excluded"] += 1
                continue
            digest = hashlib.sha256(f"{source_hash}|{px}|{py}".encode("ascii")).digest()
            candidates.append((-score, digest, px, py))
    candidates.sort(key=lambda value: (value[0], value[1]))
    spacing_pixels = MINIMUM_SPACING_METERS / METERS_PER_PIXEL
    cell_size = max(1.0, spacing_pixels)
    accepted_grid: dict[tuple[int, int], list[tuple[float, float]]] = {}
    accepted = []
    for negative_score, digest, px, py in candidates:
        gx, gy = math.floor(px / cell_size), math.floor(py / cell_size)
        too_close = False
        for neighbor_y in range(gy - 1, gy + 2):
            for neighbor_x in range(gx - 1, gx + 2):
                for other_x, other_y in accepted_grid.get((neighbor_x, neighbor_y), []):
                    if (px - other_x) ** 2 + (py - other_y) ** 2 < spacing_pixels**2:
                        too_close = True
                        break
                if too_close:
                    break
            if too_close:
                break
        if too_close:
            counters["spacing_rejected"] += 1
            continue
        if len(accepted) >= MAX_PLACEMENTS:
            counters["cap_rejected"] += 1
            continue
        index = py * MASK_SIZE + px
        x, z = to_world(px, py)
        species_id = choose_species(digest, bool(water_proximity[index]))
        species = SPECIES[species_id]
        height = scaled_range(species["height_range_meters"], digest[1] / 255.0)
        crown = scaled_range(species["crown_range_meters"], digest[2] / 255.0)
        identifier = hashlib.sha256(f"vegetation-v2|{species_id}|{px}|{py}".encode("ascii")).hexdigest()[:20]
        accepted.append(
            {
                "crown_radius_meters": crown,
                "heading_degrees": round(int.from_bytes(digest[3:5], "big") / 65535.0 * 360.0, 3),
                "height_meters": height,
                "likelihood_mask_pixel": [px, py],
                "likelihood_score": -negative_score,
                "local_enu_meters": {"x": round(x, 3), "z": round(z, 3)},
                "source_class": "natural_color_canopy_likelihood_osm_exclusion_v2",
                "species_id": species_id,
                "stable_id": "vegetation-v2-" + identifier,
            }
        )
        accepted_grid.setdefault((gx, gy), []).append((px, py))
    return sorted(accepted, key=lambda value: value["stable_id"]), counters


class ObjMesh:
    def __init__(self) -> None:
        self.vertices: list[tuple[float, float, float]] = []
        self.faces: list[tuple[str, tuple[int, int, int]]] = []

    def vertex(self, value: tuple[float, float, float]) -> int:
        self.vertices.append(value)
        return len(self.vertices)

    def triangle(self, material: str, a: int, b: int, c: int) -> None:
        self.faces.append((material, (a, b, c)))


def add_cylinder(mesh: ObjMesh, radius: float, height: float, sides: int) -> None:
    bottom = [mesh.vertex((math.cos(index * math.tau / sides) * radius, 0.0, math.sin(index * math.tau / sides) * radius)) for index in range(sides)]
    top = [mesh.vertex((math.cos(index * math.tau / sides) * radius, height, math.sin(index * math.tau / sides) * radius)) for index in range(sides)]
    for index in range(sides):
        following = (index + 1) % sides
        mesh.triangle("bark_shared", bottom[index], top[index], top[following])
        mesh.triangle("bark_shared", bottom[index], top[following], bottom[following])


def add_ellipsoid(mesh: ObjMesh, center: tuple[float, float, float], radius: tuple[float, float, float], segments: int, rings: int, material: str) -> None:
    bottom = mesh.vertex((center[0], center[1] - radius[1], center[2]))
    bands = []
    for ring in range(1, rings):
        latitude = -math.pi * 0.5 + math.pi * ring / rings
        band = []
        for segment in range(segments):
            longitude = math.tau * segment / segments
            band.append(mesh.vertex((center[0] + math.cos(latitude) * math.cos(longitude) * radius[0], center[1] + math.sin(latitude) * radius[1], center[2] + math.cos(latitude) * math.sin(longitude) * radius[2])))
        bands.append(band)
    top = mesh.vertex((center[0], center[1] + radius[1], center[2]))
    first = bands[0]
    for segment in range(segments):
        mesh.triangle(material, bottom, first[(segment + 1) % segments], first[segment])
    for lower, upper in zip(bands, bands[1:]):
        for segment in range(segments):
            following = (segment + 1) % segments
            mesh.triangle(material, lower[segment], lower[following], upper[following])
            mesh.triangle(material, lower[segment], upper[following], upper[segment])
    last = bands[-1]
    for segment in range(segments):
        mesh.triangle(material, last[segment], last[(segment + 1) % segments], top)


def species_lobes(species_id: str, lod: int) -> list[tuple[tuple[float, float, float], tuple[float, float, float]]]:
    if species_id == "rabbitbrush_shrub":
        base = [((-0.22, 0.62, 0.0), (0.46, 0.58, 0.42)), ((0.22, 0.58, 0.08), (0.43, 0.54, 0.40)), ((0.0, 0.78, -0.18), (0.40, 0.52, 0.38)), ((0.02, 0.50, 0.22), (0.42, 0.44, 0.40))]
    elif species_id == "rocky_mountain_juniper":
        base = [((0.0, 0.63, 0.0), (0.42, 0.62, 0.42)), ((0.0, 1.12, 0.0), (0.33, 0.52, 0.33)), ((0.0, 1.50, 0.0), (0.20, 0.38, 0.20))]
    elif species_id == "narrowleaf_willow":
        base = [((-0.25, 1.05, 0.0), (0.48, 0.72, 0.42)), ((0.23, 1.02, 0.06), (0.46, 0.70, 0.42)), ((0.0, 1.35, -0.16), (0.45, 0.68, 0.40)), ((0.02, 0.82, 0.22), (0.42, 0.58, 0.40))]
    else:
        base = [((0.0, 1.42, 0.0), (0.62, 0.60, 0.58)), ((-0.38, 1.28, 0.08), (0.48, 0.48, 0.44)), ((0.36, 1.30, 0.04), (0.50, 0.50, 0.45)), ((0.08, 1.58, -0.30), (0.46, 0.44, 0.43)), ((-0.08, 1.10, 0.32), (0.44, 0.42, 0.42))]
    return base if lod == 0 else base[: min(3, len(base))] if lod == 1 else [base[0]]


def build_species_mesh(species_id: str, lod: int) -> ObjMesh:
    mesh = ObjMesh()
    shrub = species_id == "rabbitbrush_shrub"
    add_cylinder(mesh, 0.06 if shrub else 0.10, 0.55 if shrub else 1.10, 12 if lod == 0 else 8 if lod == 1 else 6)
    segments, rings = ((12, 7) if lod == 0 else (8, 4) if lod == 1 else (6, 2))
    material = SPECIES[species_id]["foliage_material"]
    for center, radius in species_lobes(species_id, lod):
        add_ellipsoid(mesh, center, radius, segments, rings, material)
    return mesh


def write_obj(path: Path, species_id: str, lod: int, mesh: ObjMesh) -> None:
    lines = ["# Project-owned deterministic KBDU vegetation mesh", "mtllib KBDU_VegetationShared.mtl", f"o {species_id}_LOD{lod}"]
    lines.extend(f"v {x:.6f} {y:.6f} {z:.6f}" for x, y, z in mesh.vertices)
    active_material = None
    for material, face in mesh.faces:
        if material != active_material:
            lines.append(f"usemtl {material}")
            active_material = material
        lines.append(f"f {face[0]} {face[1]} {face[2]}")
    write_bytes(path, ("\n".join(lines) + "\n").encode("ascii"))


def write_mesh_library(output_dir: Path) -> dict[str, Any]:
    mesh_dir = output_dir / "vegetation_meshes"
    mtl_path = mesh_dir / "KBDU_VegetationShared.mtl"
    material_text = """# Project-owned shared opaque vegetation material family
newmtl bark_shared
Kd 0.24 0.17 0.10
Ns 4
illum 1
newmtl foliage_deciduous_shared
Kd 0.24 0.38 0.16
Ns 3
illum 1
newmtl foliage_conifer_shared
Kd 0.18 0.30 0.16
Ns 3
illum 1
newmtl foliage_shrub_shared
Kd 0.40 0.43 0.20
Ns 2
illum 1
"""
    write_bytes(mtl_path, material_text.encode("ascii"))
    species_records = []
    for species_id, definition in SPECIES.items():
        lods = []
        for lod in range(3):
            mesh = build_species_mesh(species_id, lod)
            path = mesh_dir / f"{species_id}_LOD{lod}.obj"
            write_obj(path, species_id, lod, mesh)
            lods.append({"lod": lod, "path": path.relative_to(output_dir).as_posix(), "vertices": len(mesh.vertices), "triangles": len(mesh.faces), "bytes": path.stat().st_size, "sha256": sha256_file(path)})
        species_records.append({"id": species_id, **definition, "lods": lods})
    return {
        "label": "KBDU project-owned procedural regional vegetation library",
        "license": "Project-authored; repository license",
        "material_contract": {
            "opaque_only": True,
            "path": mtl_path.relative_to(output_dir).as_posix(),
            "shared_material_slots": ["bark_shared", "foliage_deciduous_shared", "foliage_conifer_shared", "foliage_shrub_shared"],
            "sha256": sha256_file(mtl_path),
        },
        "schema_version": 1,
        "species": species_records,
        "stereo_geometry_contract": {
            "camera_facing_billboards": False,
            "closed_crown_volume": True,
            "top_and_oblique_views_supported": True,
        },
    }


def generate(
    ppm_path: Path,
    context_path: Path,
    parking_path: Path,
    decode_receipt_path: Path,
    output_dir: Path,
) -> list[Path]:
    width, height, pixels = read_ppm(ppm_path)
    if (width, height) != (MASK_SIZE, MASK_SIZE):
        raise ValueError(f"Expected {MASK_SIZE} square PPM, observed {width}x{height}")
    context = json.loads(context_path.read_text(encoding="utf-8"))
    parking = json.loads(parking_path.read_text(encoding="utf-8"))
    decode_receipt = json.loads(decode_receipt_path.read_text(encoding="utf-8"))
    if sha256_file(ppm_path) != decode_receipt["output_sha256"]:
        raise ValueError("Decoded PPM hash does not match its external receipt")
    likelihood = likelihood_mask(pixels)
    exclusion, water_proximity = rasterize_exclusions(context, parking)
    suitability = bytearray(score if not exclusion[index] else 0 for index, score in enumerate(likelihood))
    placements, rejection = select_placements(likelihood, exclusion, water_proximity, decode_receipt["source_sha256"])
    likelihood_path = output_dir / "vegetation_likelihood_mask_v2.png"
    exclusion_path = output_dir / "vegetation_exclusion_mask_v2.png"
    suitability_path = output_dir / "vegetation_suitability_mask_v2.png"
    write_grayscale_png(likelihood_path, width, height, likelihood)
    write_grayscale_png(exclusion_path, width, height, exclusion)
    write_grayscale_png(suitability_path, width, height, suitability)
    species_library = write_mesh_library(output_dir)
    species_path = output_dir / "vegetation_species_library_v2.json"
    write_json(species_path, species_library)
    source = {
        "coordinate_frame": "KBDU local tangent ENU; +X=east, +Z=north",
        "label": "KBDU V2 deterministic natural-color vegetation placement source",
        "legacy_candidate_count_replaced": 246,
        "mask_contract": {
            "active_3d_radius_meters": ACTIVE_RADIUS_METERS,
            "dimensions": [width, height],
            "exclusion_codes": EXCLUSION_CODES,
            "exclusion_path": exclusion_path.name,
            "exclusion_sha256": sha256_file(exclusion_path),
            "likelihood_path": likelihood_path.name,
            "likelihood_sha256": sha256_file(likelihood_path),
            "likelihood_threshold": LIKELIHOOD_THRESHOLD,
            "meters_per_pixel": METERS_PER_PIXEL,
            "suitability_path": suitability_path.name,
            "suitability_sha256": sha256_file(suitability_path),
        },
        "placements": placements,
        "schema_version": 1,
        "source": {
            "classification": "natural-color excess-green/darkness/saturation/local-texture fallback; no NIR channel available",
            "image_license": decode_receipt["source_license"],
            "image_path": decode_receipt["source_path"],
            "image_sha256": decode_receipt["source_sha256"],
            "osm_context_path": context_path.relative_to(REPO_ROOT).as_posix() if context_path.is_relative_to(REPO_ROOT) else context_path.name,
            "osm_context_sha256": sha256_file(context_path),
            "osm_license": "ODbL-1.0; © OpenStreetMap contributors",
        },
    }
    source_path = output_dir / "vegetation_source_v2.json"
    write_json(source_path, source, compact=True)
    counts_by_species: dict[str, int] = {}
    counts_by_band = {"0-600m": 0, "600-1500m": 0}
    for placement in placements:
        counts_by_species[placement["species_id"]] = counts_by_species.get(placement["species_id"], 0) + 1
        point = placement["local_enu_meters"]
        counts_by_band["0-600m" if math.hypot(point["x"], point["z"]) < 600.0 else "600-1500m"] += 1
    excluded_counts = {name: exclusion.count(code) for name, code in EXCLUSION_CODES.items() if code}
    triangles_by_species = {
        value["id"]: {f"LOD{lod['lod']}": lod["triangles"] for lod in value["lods"]}
        for value in species_library["species"]
    }
    instance_triangle_estimates = {
        lod: sum(counts_by_species.get(species_id, 0) * values[lod] for species_id, values in triangles_by_species.items())
        for lod in ("LOD0", "LOD1", "LOD2")
    }
    coverage = {
        "accepted_placement_count": len(placements),
        "active_3d_radius_meters": ACTIVE_RADIUS_METERS,
        "candidate_rejection_counts": rejection,
        "counts_by_distance_band": counts_by_band,
        "counts_by_species": dict(sorted(counts_by_species.items())),
        "exclusion_pixel_counts": excluded_counts,
        "far_zone_policy": "orthophoto_only beyond 1500 m",
        "label": "KBDU V2 vegetation source/mask/coverage evidence",
        "legacy_candidate_count_replaced": 246,
        "mask_pixels_above_threshold": sum(value >= LIKELIHOOD_THRESHOLD for value in likelihood),
        "mask_pixels_suitable_after_exclusion": sum(value >= LIKELIHOOD_THRESHOLD for value in suitability),
        "minimum_spacing_meters": MINIMUM_SPACING_METERS,
        "shared_material_slot_count": len(species_library["material_contract"]["shared_material_slots"]),
        "instance_triangle_estimates_by_lod": instance_triangle_estimates,
        "schema_version": 1,
        "status": "PASS" if 200 <= len(placements) <= MAX_PLACEMENTS and len(counts_by_species) == len(SPECIES) else "FAIL",
    }
    coverage_path = output_dir / "vegetation_coverage_report.json"
    write_json(coverage_path, coverage)
    return [likelihood_path, exclusion_path, suitability_path, source_path, species_path, coverage_path] + sorted((output_dir / "vegetation_meshes").glob("*"))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ppm", required=True, type=Path)
    parser.add_argument("--decode-receipt", required=True, type=Path)
    parser.add_argument("--context", type=Path, default=DEFAULT_CONTEXT)
    parser.add_argument("--parking", type=Path, default=DEFAULT_PARKING)
    parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT_DIR)
    args = parser.parse_args()
    paths = generate(args.ppm.resolve(), args.context.resolve(), args.parking.resolve(), args.decode_receipt.resolve(), args.output_dir.resolve())
    print(json.dumps({"generated": [str(path) for path in paths]}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
