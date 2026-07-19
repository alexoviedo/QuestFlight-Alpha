#!/usr/bin/env python3
"""Build bounded, deterministic vegetation batches from validated lidar products.

The production path accepts only the frozen Aerial Scenery V1 lidar schema and
verifies every consumed asset hash.  Synthetic input is guarded by an explicit
test-only switch and is labelled so it cannot masquerade as product data.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import struct
import zlib
from collections import defaultdict, deque
from dataclasses import dataclass, replace
from pathlib import Path
from typing import Any, Iterable


LIDAR_SCHEMA = "questflight.aerial-scenery-v1.lidar.v1"
DISTRIBUTION_SCHEMA = "questflight.aerial-scenery-v1.vegetation-distribution.v1"
REPO_ROOT = Path(__file__).resolve().parents[3]


def portable_path(path: Path) -> str:
    resolved = path.resolve()
    try:
        return resolved.relative_to(REPO_ROOT).as_posix()
    except ValueError:
        return resolved.as_posix()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def stable_digest(seed: str, *values: object) -> bytes:
    joined = "|".join([seed, *(str(value) for value in values)])
    return hashlib.sha256(joined.encode("utf-8")).digest()


def clamp(value: float, minimum: float, maximum: float) -> float:
    return max(minimum, min(maximum, value))


@dataclass(frozen=True)
class PngRaster:
    width: int
    height: int
    bit_depth: int
    color_type: int
    pixels: bytes

    def rgba8(self, column: int, row: int) -> tuple[int, int, int, int]:
        if self.bit_depth != 8 or self.color_type != 6:
            raise ValueError("Raster is not RGBA8")
        offset = (row * self.width + column) * 4
        return tuple(self.pixels[offset : offset + 4])  # type: ignore[return-value]

    def gray8(self, column: int, row: int) -> int:
        if self.bit_depth != 8 or self.color_type != 0:
            raise ValueError("Raster is not grayscale R8")
        return self.pixels[row * self.width + column]

    def gray16(self, column: int, row: int) -> int:
        if self.bit_depth != 16 or self.color_type != 0:
            raise ValueError("Raster is not grayscale R16")
        offset = (row * self.width + column) * 2
        return struct.unpack_from(">H", self.pixels, offset)[0]


def paeth(left: int, above: int, upper_left: int) -> int:
    prediction = left + above - upper_left
    left_distance = abs(prediction - left)
    above_distance = abs(prediction - above)
    upper_left_distance = abs(prediction - upper_left)
    if left_distance <= above_distance and left_distance <= upper_left_distance:
        return left
    if above_distance <= upper_left_distance:
        return above
    return upper_left


def read_png(path: Path) -> PngRaster:
    payload = path.read_bytes()
    if not payload.startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError(f"Not a PNG: {path}")
    cursor = 8
    header: tuple[int, int, int, int, int, int, int] | None = None
    compressed = bytearray()
    while cursor < len(payload):
        length = struct.unpack_from(">I", payload, cursor)[0]
        name = payload[cursor + 4 : cursor + 8]
        data = payload[cursor + 8 : cursor + 8 + length]
        expected_crc = struct.unpack_from(">I", payload, cursor + 8 + length)[0]
        if zlib.crc32(name + data) & 0xFFFFFFFF != expected_crc:
            raise ValueError(f"PNG chunk CRC mismatch in {path}")
        cursor += 12 + length
        if name == b"IHDR":
            header = struct.unpack(">IIBBBBB", data)
        elif name == b"IDAT":
            compressed.extend(data)
        elif name == b"IEND":
            break
    if header is None:
        raise ValueError(f"PNG is missing IHDR: {path}")
    width, height, bit_depth, color_type, compression, filtering, interlace = header
    if compression != 0 or filtering != 0 or interlace != 0:
        raise ValueError("Only non-interlaced baseline PNG compression/filtering is supported")
    if (bit_depth, color_type) == (8, 6):
        bytes_per_pixel = 4
    elif (bit_depth, color_type) == (8, 0):
        bytes_per_pixel = 1
    elif (bit_depth, color_type) == (16, 0):
        bytes_per_pixel = 2
    else:
        raise ValueError(f"Unsupported PNG format bit_depth={bit_depth}, color_type={color_type}")
    stride = width * bytes_per_pixel
    raw = zlib.decompress(bytes(compressed))
    if len(raw) != height * (stride + 1):
        raise ValueError("PNG decompressed byte count does not match IHDR")
    decoded = bytearray(height * stride)
    source = 0
    for row in range(height):
        filter_type = raw[source]
        source += 1
        row_start = row * stride
        previous_start = (row - 1) * stride
        for index in range(stride):
            encoded = raw[source + index]
            left = decoded[row_start + index - bytes_per_pixel] if index >= bytes_per_pixel else 0
            above = decoded[previous_start + index] if row > 0 else 0
            upper_left = decoded[previous_start + index - bytes_per_pixel] if row > 0 and index >= bytes_per_pixel else 0
            if filter_type == 0:
                value = encoded
            elif filter_type == 1:
                value = encoded + left
            elif filter_type == 2:
                value = encoded + above
            elif filter_type == 3:
                value = encoded + ((left + above) // 2)
            elif filter_type == 4:
                value = encoded + paeth(left, above, upper_left)
            else:
                raise ValueError(f"Unsupported PNG row filter {filter_type}")
            decoded[row_start + index] = value & 0xFF
        source += stride
    return PngRaster(width, height, bit_depth, color_type, bytes(decoded))


def png_chunk(name: bytes, data: bytes) -> bytes:
    return struct.pack(">I", len(data)) + name + data + struct.pack(">I", zlib.crc32(name + data) & 0xFFFFFFFF)


def write_rgba8_png(path: Path, width: int, height: int, pixels: bytes) -> None:
    """Deterministic test-fixture helper; production generation never calls it."""
    if len(pixels) != width * height * 4:
        raise ValueError("RGBA8 payload length mismatch")
    rows = b"".join(b"\x00" + pixels[row * width * 4 : (row + 1) * width * 4] for row in range(height))
    payload = b"\x89PNG\r\n\x1a\n"
    payload += png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    payload += png_chunk(b"IDAT", zlib.compress(rows, 9))
    payload += png_chunk(b"IEND", b"")
    path.write_bytes(payload)


def write_gray16_png(path: Path, width: int, height: int, values: Iterable[int]) -> None:
    """Deterministic test-fixture helper; production generation never calls it."""
    packed = b"".join(struct.pack(">H", int(value)) for value in values)
    if len(packed) != width * height * 2:
        raise ValueError("R16 payload length mismatch")
    rows = b"".join(b"\x00" + packed[row * width * 2 : (row + 1) * width * 2] for row in range(height))
    payload = b"\x89PNG\r\n\x1a\n"
    payload += png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 16, 0, 0, 0, 0))
    payload += png_chunk(b"IDAT", zlib.compress(rows, 9))
    payload += png_chunk(b"IEND", b"")
    path.write_bytes(payload)


def resolve_asset_path(repo_root: Path, manifest_path: Path, logical_path: str) -> Path:
    candidate = Path(logical_path)
    if candidate.is_absolute():
        return candidate.resolve()
    normalized = logical_path.replace("\\", "/")
    if normalized.startswith("QuestFlight/Assets/"):
        return (repo_root / normalized).resolve()
    if normalized.startswith("Assets/"):
        return (repo_root / "QuestFlight" / normalized).resolve()
    return (manifest_path.parent / candidate).resolve()


def require_asset(manifest: dict[str, Any], semantic: str) -> dict[str, Any]:
    assets = manifest.get("assets")
    if not isinstance(assets, list):
        raise ValueError("Lidar manifest assets must be a list")
    matches = [record for record in assets if isinstance(record, dict) and record.get("semantic") == semantic]
    if len(matches) != 1:
        raise ValueError(f"Expected exactly one lidar asset with semantic {semantic!r}; found {len(matches)}")
    record = matches[0]
    for key in ("path", "bytes", "sha256", "dimensions", "encoding"):
        if key not in record:
            raise ValueError(f"Lidar asset {semantic!r} is missing {key!r}")
    return record


@dataclass(frozen=True)
class Mapping:
    width: int
    height: int
    x0: float
    z0: float
    x_per_column: float
    x_per_row: float
    z_per_column: float
    z_per_row: float
    source_elevation_m: float

    def local_xz(self, column: int, row: int) -> tuple[float, float]:
        return (
            self.x0 + self.x_per_column * column + self.x_per_row * row,
            self.z0 + self.z_per_column * column + self.z_per_row * row,
        )


@dataclass(frozen=True)
class Inputs:
    manifest: dict[str, Any]
    manifest_path: Path
    manifest_sha256: str
    surface_record: dict[str, Any]
    surface_path: Path
    surface: PngRaster
    dtm_record: dict[str, Any]
    dtm_path: Path
    dtm: PngRaster
    normal_record: dict[str, Any]
    normal_path: Path
    normal: PngRaster
    canopy_normal_record: dict[str, Any]
    canopy_normal_path: Path
    canopy_normal: PngRaster
    mapping: Mapping
    dtm_scale: float
    dtm_offset: float


def load_inputs(repo_root: Path, manifest_path: Path, config: dict[str, Any]) -> Inputs:
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("schema") != LIDAR_SCHEMA:
        raise ValueError(f"Unsupported lidar schema: {manifest.get('schema')!r}")
    input_config = config["lidar_input"]
    surface_record = require_asset(manifest, input_config["surface_features_semantic"])
    dtm_record = require_asset(manifest, input_config["dtm_semantic"])
    normal_record = require_asset(manifest, input_config["terrain_validity_semantic"])
    canopy_normal_record = require_asset(manifest, input_config["canopy_normal_semantic"])
    surface_path = resolve_asset_path(repo_root, manifest_path, surface_record["path"])
    dtm_path = resolve_asset_path(repo_root, manifest_path, dtm_record["path"])
    normal_path = resolve_asset_path(repo_root, manifest_path, normal_record["path"])
    canopy_normal_path = resolve_asset_path(repo_root, manifest_path, canopy_normal_record["path"])
    for record, path in (
        (surface_record, surface_path),
        (dtm_record, dtm_path),
        (normal_record, normal_path),
        (canopy_normal_record, canopy_normal_path),
    ):
        if not path.is_file():
            raise FileNotFoundError(path)
        if path.stat().st_size != int(record["bytes"]):
            raise ValueError(f"Lidar asset byte count mismatch: {path}")
        if sha256(path) != record["sha256"]:
            raise ValueError(f"Lidar asset SHA-256 mismatch: {path}")

    surface = read_png(surface_path)
    dtm = read_png(dtm_path)
    normal = read_png(normal_path)
    canopy_normal = read_png(canopy_normal_path)
    if (surface.bit_depth, surface.color_type) != (8, 6):
        raise ValueError("Surface-features asset must be RGBA8 PNG")
    if (dtm.bit_depth, dtm.color_type) != (16, 0):
        raise ValueError("DTM asset must be grayscale R16 PNG")
    if (normal.bit_depth, normal.color_type) != (8, 6):
        raise ValueError("Terrain surface normal/validity asset must be RGBA8 PNG")
    if (canopy_normal.bit_depth, canopy_normal.color_type) != (8, 6):
        raise ValueError("Canopy height normal/occlusion asset must be RGBA8 PNG")
    if (surface.width, surface.height) != (dtm.width, dtm.height) or (surface.width, surface.height) != (
        normal.width, normal.height
    ) or (surface.width, surface.height) != (canopy_normal.width, canopy_normal.height):
        raise ValueError("Surface-features, DTM, terrain validity, and canopy-normal dimensions differ")
    for record in (surface_record, dtm_record, normal_record, canopy_normal_record):
        dimensions = record["dimensions"]
        if (int(dimensions["width_pixels"]), int(dimensions["height_pixels"])) != (surface.width, surface.height):
            raise ValueError(f"Manifest dimensions mismatch for {record['semantic']}")

    coordinate = manifest.get("coordinate_mapping") or {}
    grid = coordinate.get("grid") or {}
    origin = coordinate.get("origin") or {}
    affine = coordinate.get("pixel_center_affine_local_meters") or {}
    axes = coordinate.get("unity_axes") or {}
    if int(grid.get("product_crs_epsg", -1)) != 32613:
        raise ValueError("Lidar product CRS must be EPSG:32613")
    if (int(grid.get("width_pixels", -1)), int(grid.get("height_pixels", -1))) != (surface.width, surface.height):
        raise ValueError("Coordinate-mapping grid dimensions mismatch")
    if axes != {"x": "east", "y": "up", "z": "north"}:
        raise ValueError(f"Unsupported Unity axis mapping: {axes!r}")
    required_affine = (
        "x_at_column0_row0", "z_at_column0_row0", "x_per_column", "x_per_row", "z_per_column", "z_per_row"
    )
    if any(key not in affine for key in required_affine):
        raise ValueError("Lidar manifest is missing numeric local pixel-center affine terms")
    if abs(float(affine["x_per_row"])) > 1e-9 or abs(float(affine["z_per_column"])) > 1e-9:
        raise ValueError("Rotated/skewed lidar grids are not accepted by Vegetation V1")
    if float(affine["x_per_column"]) <= 0 or float(affine["z_per_row"]) >= 0:
        raise ValueError("Lidar raster must advance east by columns and south by rows")

    encoding = dtm_record["encoding"]
    decoded = encoding.get("decoded_source_z_meters") or {}
    if encoding.get("container") != "PNG" or encoding.get("channel") != "R" or int(encoding.get("bits_per_channel", 0)) != 16:
        raise ValueError("DTM encoding contract mismatch")
    if encoding.get("code_zero_semantic") != "nodata_reserved":
        raise ValueError("DTM code zero must be reserved for nodata")
    mapping = Mapping(
        surface.width,
        surface.height,
        float(affine["x_at_column0_row0"]),
        float(affine["z_at_column0_row0"]),
        float(affine["x_per_column"]),
        float(affine["x_per_row"]),
        float(affine["z_per_column"]),
        float(affine["z_per_row"]),
        float(origin["source_elevation_msl_meters"]),
    )
    return Inputs(
        manifest,
        manifest_path,
        sha256(manifest_path),
        surface_record,
        surface_path,
        surface,
        dtm_record,
        dtm_path,
        dtm,
        normal_record,
        normal_path,
        normal,
        canopy_normal_record,
        canopy_normal_path,
        canopy_normal,
        mapping,
        float(decoded["scale"]),
        float(decoded["offset"]),
    )


@dataclass(frozen=True)
class Candidate:
    column: int
    row: int
    coarse_column: int
    coarse_row: int
    component: int
    x: float
    y: float
    z: float
    hag_m: float
    density: float
    occlusion: float
    score: float


def eligible_candidates(
    inputs: Inputs, config: dict[str, Any]
) -> tuple[list[Candidate], dict[str, int | float], bytearray]:
    sampling = config["sampling"]
    block = int(sampling["coarse_cell_pixels"])
    minimum_hag = float(sampling["minimum_canopy_hag_m"])
    minimum_density = float(sampling["minimum_vegetation_density"])
    maximum_building_hag = float(sampling["maximum_building_hag_m"])
    best_by_cell: dict[tuple[int, int], Candidate] = {}
    counters = defaultdict(int)
    eligible_mask = bytearray(inputs.surface.width * inputs.surface.height)
    for row in range(inputs.surface.height):
        for column in range(inputs.surface.width):
            canopy_code, density_code, building_code, occlusion_code = inputs.surface.rgba8(column, row)
            dtm_code = inputs.dtm.gray16(column, row)
            if dtm_code == 0:
                counters["dtm_nodata"] += 1
                continue
            hag = canopy_code * 0.25
            density = density_code / 255.0
            building_hag = building_code * 0.25
            occlusion = occlusion_code / 255.0
            if hag < minimum_hag:
                counters["below_hag"] += 1
                continue
            if density < minimum_density:
                counters["below_density"] += 1
                continue
            if building_hag > maximum_building_hag:
                counters["building_excluded"] += 1
                continue
            eligible_mask[row * inputs.surface.width + column] = 1
            counters["eligible_pixels"] += 1
            x, z = inputs.mapping.local_xz(column, row)
            source_z = dtm_code * inputs.dtm_scale + inputs.dtm_offset
            y = source_z - inputs.mapping.source_elevation_m
            score = density * 0.52 + occlusion * 0.20 + min(1.0, hag / 20.0) * 0.28
            cell = (row // block, column // block)
            candidate = Candidate(column, row, cell[1], cell[0], -1, x, y, z, hag, density, occlusion, score)
            incumbent = best_by_cell.get(cell)
            if incumbent is None or (candidate.score, -candidate.row, -candidate.column) > (
                incumbent.score, -incumbent.row, -incumbent.column
            ):
                best_by_cell[cell] = candidate
    counters["eligible_pixels_reduced_to_cells"] = len(best_by_cell)

    occupied = set(best_by_cell)
    component_by_cell: dict[tuple[int, int], int] = {}
    component = 0
    for start in sorted(occupied):
        if start in component_by_cell:
            continue
        queue = deque([start])
        component_by_cell[start] = component
        while queue:
            row, column = queue.popleft()
            for row_offset in (-1, 0, 1):
                for column_offset in (-1, 0, 1):
                    if row_offset == 0 and column_offset == 0:
                        continue
                    neighbor = (row + row_offset, column + column_offset)
                    if neighbor in occupied and neighbor not in component_by_cell:
                        component_by_cell[neighbor] = component
                        queue.append(neighbor)
        component += 1
    counters["grove_components"] = component
    pixel_area = abs(
        inputs.mapping.x_per_column * inputs.mapping.z_per_row
        - inputs.mapping.x_per_row * inputs.mapping.z_per_column
    )
    counters["eligible_canopy_area_m2"] = round(counters["eligible_pixels"] * pixel_area, 3)
    candidates = [replace(candidate, component=component_by_cell[cell]) for cell, candidate in best_by_cell.items()]
    candidates.sort(key=lambda item: (item.component, item.row, item.column))
    return candidates, dict(sorted(counters.items())), eligible_mask


def select_spaced(
    candidates: list[Candidate],
    maximum: int,
    minimum_spacing: float,
    seed: str,
    tier: int,
) -> list[Candidate]:
    grouped: dict[int, list[Candidate]] = defaultdict(list)
    for candidate in candidates:
        grouped[candidate.component].append(candidate)
    for component, values in grouped.items():
        values.sort(key=lambda item: (
            -item.score,
            stable_digest(seed, "tree", tier, component, item.row, item.column),
        ))
    component_order = sorted(grouped, key=lambda component: (
        -grouped[component][0].score,
        stable_digest(seed, "component", tier, component),
    ))
    accepted: list[Candidate] = []
    minimum_squared = minimum_spacing * minimum_spacing
    round_index = 0
    while len(accepted) < maximum:
        progressed = False
        for component in component_order:
            values = grouped[component]
            if round_index >= len(values):
                continue
            progressed = True
            candidate = values[round_index]
            if all((candidate.x - other.x) ** 2 + (candidate.z - other.z) ** 2 >= minimum_squared for other in accepted):
                accepted.append(candidate)
                if len(accepted) == maximum:
                    break
        if not progressed:
            break
        round_index += 1
    return accepted


def source_archetype(source_object: str) -> str:
    if "Cottonwood" in source_object:
        return "cottonwood"
    if "WillowRiparian" in source_object:
        return "willow_riparian"
    if "SparseConifer" in source_object:
        return "sparse_conifer"
    if "Shrub" in source_object:
        return "regional_shrub"
    if "Deciduous" in source_object:
        return "general_deciduous"
    raise ValueError(f"Unknown vegetation source archetype: {source_object}")


def choose_tree_source(candidate: Candidate, tier: int, seed: str, stable_input_hash: str) -> str:
    digest = stable_digest(seed, stable_input_hash, "regional-archetype", tier, candidate.row, candidate.column)
    selector = digest[0]
    if candidate.hag_m <= 4.5:
        archetype = "regional_shrub"
    elif candidate.density >= 0.72 and candidate.hag_m <= 10.5 and selector < 150:
        archetype = "willow_riparian"
    elif candidate.hag_m >= 12.0 and selector < 182:
        archetype = "cottonwood"
    elif candidate.density <= 0.54 and selector < 92:
        archetype = "sparse_conifer"
    elif selector < 34:
        archetype = "sparse_conifer"
    elif selector < 80:
        archetype = "cottonwood"
    elif selector < 126:
        archetype = "willow_riparian"
    elif selector < 150:
        archetype = "regional_shrub"
    else:
        archetype = "general_deciduous"
    names = {
        "cottonwood": "AerialTree_Cottonwood",
        "willow_riparian": "AerialTree_WillowRiparian",
        "general_deciduous": "AerialTree_Deciduous",
        "sparse_conifer": "AerialTree_SparseConifer",
        "regional_shrub": "AerialShrub_Regional",
    }
    return f"{names[archetype]}_T{tier}"


def tree_instance(candidate: Candidate, tier: int, source_object: str, seed: str, stable_input_hash: str, config: dict[str, Any]) -> dict[str, Any]:
    digest = stable_digest(seed, stable_input_hash, "tree", tier, candidate.row, candidate.column)
    fraction0 = int.from_bytes(digest[0:2], "big") / 65535.0
    fraction1 = int.from_bytes(digest[2:4], "big") / 65535.0
    archetype = source_archetype(source_object)
    canonical = float(config["sampling"]["canonical_heights_m"][archetype])
    scale_y = clamp(candidate.hag_m / canonical, 0.62, 1.48)
    if archetype == "sparse_conifer":
        scale_x = scale_y * (0.80 + fraction0 * 0.12)
        scale_z = scale_y * (0.80 + fraction1 * 0.12)
        tint = [0.84 + fraction0 * 0.08, 0.91 + fraction1 * 0.06, 0.86 + fraction0 * 0.05, 1.0]
    elif archetype == "regional_shrub":
        scale_x = scale_y * (1.10 + fraction0 * 0.18)
        scale_z = scale_y * (1.06 + fraction1 * 0.18)
        tint = [0.94 + fraction0 * 0.05, 0.92 + fraction1 * 0.05, 0.80 + fraction0 * 0.08, 1.0]
    else:
        crown = 0.82 + candidate.density * 0.28
        scale_x = scale_y * crown * (0.92 + fraction0 * 0.16)
        scale_z = scale_y * crown * (0.92 + fraction1 * 0.16)
        tint = [0.90 + fraction0 * 0.08, 0.92 + fraction1 * 0.07, 0.82 + fraction0 * 0.10, 1.0]
    return {
        "stable_id": "vegetation-v1-" + digest.hex()[:20],
        "source_object": source_object,
        "archetype": archetype,
        "x": round(candidate.x, 3),
        "y": round(candidate.y, 3),
        "z": round(candidate.z, 3),
        "yaw_degrees": round(int.from_bytes(digest[4:6], "big") / 65535.0 * 360.0, 3),
        "scale_x": round(scale_x, 4),
        "scale_y": round(scale_y, 4),
        "scale_z": round(scale_z, 4),
        "tint_r": round(tint[0], 4),
        "tint_g": round(tint[1], 4),
        "tint_b": round(tint[2], 4),
        "tint_a": 1.0,
        "grove_component": candidate.component,
        "source_pixel_column": candidate.column,
        "source_pixel_row": candidate.row,
        "canopy_hag_m": round(candidate.hag_m, 3),
        "vegetation_density": round(candidate.density, 5),
    }


def cluster_candidates(candidates: list[Candidate], cell_m: float) -> list[tuple[int, list[Candidate]]]:
    buckets: dict[tuple[int, int, int], list[Candidate]] = defaultdict(list)
    for candidate in candidates:
        buckets[(candidate.component, math.floor(candidate.x / cell_m), math.floor(candidate.z / cell_m))].append(candidate)
    ordered = sorted(buckets.items(), key=lambda item: (item[0][0], item[0][1], item[0][2]))
    return [(index, values) for index, (_, values) in enumerate(ordered)]


def select_clusters(
    candidates: list[Candidate], maximum: int, cell_m: float, seed: str
) -> list[tuple[int, list[Candidate]]]:
    if maximum <= 0 or not candidates:
        return []
    # A previous eight-centroid K-means pass stretched ten-tree source meshes
    # across landscape-scale ellipses.  That produced visibly enormous branches
    # over roads and buildings.  Tier 2 is now compiled from bounded 96 m source
    # cells.  Each selected cell becomes one fixed upper-canopy aggregate in the
    # Unity editor bake; no source tree is scaled to cover a whole district.
    buckets = cluster_candidates(candidates, cell_m)

    def bucket_rank(record: tuple[int, list[Candidate]]) -> tuple[float, float, int, bytes]:
        index, members = record
        total_score = sum(member.score for member in members)
        mean_score = total_score / len(members)
        component_count = len({member.component for member in members})
        return (
            -(len(members) * (0.65 + mean_score)),
            -mean_score,
            -component_count,
            stable_digest(seed, "bounded-cluster-cell", index),
        )

    ordered = sorted(buckets, key=bucket_rank)
    # Reserve one quarter of the budget for distinct connected groves so a
    # single long riparian component cannot consume every aggregate cell.
    diversity_budget = min(maximum // 4, len({members[0].component for _, members in ordered}))
    diverse: list[tuple[int, list[Candidate]]] = []
    seen_components: set[int] = set()
    for record in ordered:
        component = record[1][0].component
        if component in seen_components:
            continue
        diverse.append(record)
        seen_components.add(component)
        if len(diverse) == diversity_budget:
            break
    diverse_ids = {record[0] for record in diverse}
    selected = diverse + [record for record in ordered if record[0] not in diverse_ids][
        : maximum - len(diverse)
    ]
    return [
        (index, sorted(members, key=lambda item: (item.component, item.row, item.column)))
        for index, members in selected
    ]


def cluster_instance(
    index: int,
    members: list[Candidate],
    seed: str,
    stable_input_hash: str,
    config: dict[str, Any],
) -> dict[str, Any]:
    weight_sum = sum(max(0.01, member.score) for member in members)
    x = sum(member.x * member.score for member in members) / weight_sum
    y = sum(member.y * member.score for member in members) / weight_sum
    z = sum(member.z * member.score for member in members) / weight_sum
    density = sum(member.density for member in members) / len(members)
    hag = sum(member.hag_m for member in members) / len(members)
    weighted = [max(0.01, member.score) for member in members]
    covariance_xx = sum(weight * (member.x - x) ** 2 for member, weight in zip(members, weighted)) / weight_sum
    covariance_zz = sum(weight * (member.z - z) ** 2 for member, weight in zip(members, weighted)) / weight_sum
    covariance_xz = sum(
        weight * (member.x - x) * (member.z - z) for member, weight in zip(members, weighted)
    ) / weight_sum
    yaw_radians = 0.5 * math.atan2(2.0 * covariance_xz, covariance_xx - covariance_zz)
    cosine = math.cos(yaw_radians)
    sine = math.sin(yaw_radians)
    half_trace = (covariance_xx + covariance_zz) * 0.5
    eigen_delta = math.sqrt(max(0.0, ((covariance_xx - covariance_zz) * 0.5) ** 2 + covariance_xz ** 2))
    standard_major = math.sqrt(max(1.0, half_trace + eigen_delta))
    standard_minor = math.sqrt(max(1.0, half_trace - eigen_delta))
    normalized_radii = sorted(
        math.sqrt(
            (((member.x - x) * cosine + (member.z - z) * sine) / standard_major) ** 2
            + ((-(member.x - x) * sine + (member.z - z) * cosine) / standard_minor) ** 2
        )
        for member in members
    )
    quantile_position = (len(normalized_radii) - 1) * 0.75
    lower_index = math.floor(quantile_position)
    upper_index = math.ceil(quantile_position)
    blend = quantile_position - lower_index
    radial_quantile = normalized_radii[lower_index] * (1.0 - blend) + normalized_radii[upper_index] * blend
    maximum_radius = float(config["sampling"]["grove_cluster_max_radius_m"])
    radius_major = min(maximum_radius, max(8.0, standard_major * radial_quantile + 6.0))
    radius_minor = min(maximum_radius, max(8.0, standard_minor * radial_quantile + 6.0))
    footprint_width = float(config["sampling"]["grove_source_footprint_width_m"])
    footprint_depth = float(config["sampling"]["grove_source_footprint_depth_m"])
    scale_x = radius_major * 2.0 / footprint_width
    scale_z = radius_minor * 2.0 / footprint_depth
    components = sorted({member.component for member in members})
    digest = stable_digest(seed, stable_input_hash, "cluster", index, ",".join(map(str, components)))
    return {
        "stable_id": "vegetation-v1-" + digest.hex()[:20],
        "source_object": "AerialGroveCluster_T2",
        "archetype": "fixed_upper_canopy_cell_aggregate",
        "x": round(x, 3),
        "y": round(y, 3),
        "z": round(z, 3),
        "yaw_degrees": round(math.degrees(yaw_radians) % 180.0, 3),
        "scale_x": round(scale_x, 4),
        "scale_y": round(clamp(hag / 6.4, 0.65, 1.65), 4),
        "scale_z": round(scale_z, 4),
        "tint_r": round(0.88 + digest[2] / 255.0 * 0.08, 4),
        "tint_g": round(0.92 + digest[3] / 255.0 * 0.06, 4),
        "tint_b": round(0.82 + digest[4] / 255.0 * 0.09, 4),
        "tint_a": 1.0,
        "grove_component": components[0],
        "grove_components": components,
        "source_member_count": len(members),
        "footprint_radius_major_m": round(radius_major, 3),
        "footprint_radius_minor_m": round(radius_minor, 3),
        "footprint_yaw_degrees": round(math.degrees(yaw_radians) % 180.0, 3),
        "footprint_area_m2": round(math.pi * radius_major * radius_minor, 3),
        "mean_canopy_hag_m": round(hag, 3),
        "mean_vegetation_density": round(density, 5),
    }


def point_in_any_cluster(x: float, z: float, clusters: list[dict[str, Any]]) -> bool:
    for cluster in clusters:
        radius_major = float(cluster["footprint_radius_major_m"])
        radius_minor = float(cluster["footprint_radius_minor_m"])
        if radius_major <= 0.0 or radius_minor <= 0.0:
            continue
        yaw = math.radians(float(cluster["footprint_yaw_degrees"]))
        delta_x = x - float(cluster["x"])
        delta_z = z - float(cluster["z"])
        major = delta_x * math.cos(yaw) + delta_z * math.sin(yaw)
        minor = -delta_x * math.sin(yaw) + delta_z * math.cos(yaw)
        if (major / radius_major) ** 2 + (minor / radius_minor) ** 2 <= 1.0:
            return True
    return False


def coverage_metrics(
    inputs: Inputs,
    eligible_mask: bytearray,
    tier2_record: dict[str, Any],
    clusters: list[dict[str, Any]],
) -> dict[str, Any]:
    pixel_area = abs(
        inputs.mapping.x_per_column * inputs.mapping.z_per_row
        - inputs.mapping.x_per_row * inputs.mapping.z_per_column
    )
    tier2_canopy_pixels = 0
    tier2_footprint_pixels = 0
    tier2_intersection_pixels = 0
    tier2_union_pixels = 0
    eligible_pixels = 0
    tier3_covered_pixels = 0
    tier3_invalid_pixels = 0
    for row in range(inputs.surface.height):
        for column in range(inputs.surface.width):
            offset = row * inputs.surface.width + column
            x, z = inputs.mapping.local_xz(column, row)
            canopy = bool(eligible_mask[offset])
            if canopy:
                eligible_pixels += 1
                features = inputs.surface.rgba8(column, row)
                validity = inputs.normal.rgba8(column, row)[3]
                direct_feature_coverage = max(features[0], features[1], features[3]) > 0
                if direct_feature_coverage and (validity & 0x01):
                    tier3_covered_pixels += 1
                else:
                    tier3_invalid_pixels += 1
            radius = math.hypot(x, z)
            minimum = float(tier2_record["minimum_radius_m"])
            maximum = tier2_record["maximum_radius_m"]
            in_range = radius >= minimum and (maximum is None or radius < float(maximum))
            if not in_range:
                continue
            footprint = point_in_any_cluster(x, z, clusters)
            if canopy:
                tier2_canopy_pixels += 1
            if footprint:
                tier2_footprint_pixels += 1
            if canopy and footprint:
                tier2_intersection_pixels += 1
            if canopy or footprint:
                tier2_union_pixels += 1

    def ratio(numerator: int, denominator: int) -> float:
        return round(numerator / denominator, 6) if denominator else 1.0

    represented_components = sorted({
        int(component)
        for cluster in clusters
        for component in cluster.get("grove_components", [])
    })
    return {
        "method": "pixel-center raster agreement against validated eligible canopy mask",
        "pixel_area_m2": round(pixel_area, 6),
        "eligible_canopy_pixels": eligible_pixels,
        "eligible_canopy_area_m2": round(eligible_pixels * pixel_area, 3),
        "tier2": {
            "eligible_canopy_pixels": tier2_canopy_pixels,
            "canopy_mask_area_m2": round(tier2_canopy_pixels * pixel_area, 3),
            "cluster_footprint_union_pixels": tier2_footprint_pixels,
            "cluster_footprint_union_area_m2": round(tier2_footprint_pixels * pixel_area, 3),
            "cluster_footprint_analytic_sum_area_m2": round(sum(float(item["footprint_area_m2"]) for item in clusters), 3),
            "canopy_intersection_pixels": tier2_intersection_pixels,
            "canopy_intersection_area_m2": round(tier2_intersection_pixels * pixel_area, 3),
            "canopy_mask_coverage_ratio": ratio(tier2_intersection_pixels, tier2_canopy_pixels),
            "footprint_precision_ratio": ratio(tier2_intersection_pixels, tier2_footprint_pixels),
            "intersection_over_union": ratio(tier2_intersection_pixels, tier2_union_pixels),
            "represented_coarse_components": represented_components,
            "cluster_count": len(clusters),
        },
        "tier3": {
            "scope": "source_input_availability_before_final_spatial_handoff",
            "coverage_source": "surface_features_canopy_building texels checked against terrain validity bit0",
            "eligible_canopy_covered_pixels": tier3_covered_pixels,
            "eligible_canopy_missing_or_invalid_pixels": tier3_invalid_pixels,
            "eligible_canopy_coverage_ratio": ratio(tier3_covered_pixels, eligible_pixels),
            "final_shader_weight": "AerialVegetation_HandoffMask_R8: eligible-canopy gate intersected with Tier0-2 geometry ownership",
            "is_final_shader_coverage": False,
            "duplicate_or_resampled_canopy_texture": False,
            "chm_derived_canopy_normal_available": True,
            "chm_normal_semantic": inputs.canopy_normal_record["semantic"],
            "terrain_normal_rgb_used_as_canopy_normal": False,
            "normal_limitation": (
                "Canopy normal derives from a smoothed heuristic CHM because direct LAS vegetation classes are absent; "
                "terrain-normal RGB remains validity-only and is never mislabeled as canopy normal."
            ),
        },
    }


def require_product_provenance(manifest: dict[str, Any]) -> None:
    provenance = manifest.get("license_and_provenance") or {}
    source = manifest.get("source") or {}
    canopy_inference = ((manifest.get("inference") or {}).get("canopy") or {})
    if provenance.get("public_domain_derivative") is not True:
        raise ValueError("Production vegetation requires a public-domain lidar derivative provenance record")
    if provenance.get("raw_data_committed") is not False:
        raise ValueError("Production lidar provenance must affirm that raw data is not committed")
    if not source.get("dataset_id") or not source.get("data_inventory_sha256"):
        raise ValueError("Production lidar source inventory identity is incomplete")
    if not canopy_inference.get("method"):
        raise ValueError("Production lidar canopy inference method is undocumented")
    grid = (manifest.get("coordinate_mapping") or {}).get("grid") or {}
    if (int(grid.get("width_pixels", -1)), int(grid.get("height_pixels", -1))) != (1024, 1024):
        raise ValueError("Production Vegetation V1 requires the frozen 1024x1024 lidar grid")


def write_coverage_overlay(
    path: Path,
    inputs: Inputs,
    eligible_mask: bytearray,
    tier2_record: dict[str, Any],
    clusters: list[dict[str, Any]],
    tree_instances: list[dict[str, Any]],
) -> None:
    pixels = bytearray(inputs.surface.width * inputs.surface.height * 4)
    minimum = float(tier2_record["minimum_radius_m"])
    maximum = tier2_record["maximum_radius_m"]
    for row in range(inputs.surface.height):
        for column in range(inputs.surface.width):
            offset = row * inputs.surface.width + column
            rgba = offset * 4
            x, z = inputs.mapping.local_xz(column, row)
            radius = math.hypot(x, z)
            in_tier2 = radius >= minimum and (maximum is None or radius < float(maximum))
            canopy = bool(eligible_mask[offset])
            footprint = in_tier2 and point_in_any_cluster(x, z, clusters)
            if canopy and footprint:
                color = (34, 220, 188, 255)
            elif canopy:
                color = (38, 112, 54, 255)
            elif footprint:
                color = (214, 133, 34, 255)
            else:
                color = (18, 24, 29, 255)
            pixels[rgba:rgba + 4] = bytes(color)
    for instance in tree_instances:
        column = round((float(instance["x"]) - inputs.mapping.x0) / inputs.mapping.x_per_column)
        row = round((float(instance["z"]) - inputs.mapping.z0) / inputs.mapping.z_per_row)
        for row_offset in range(-2, 3):
            for column_offset in range(-2, 3):
                candidate_column = column + column_offset
                candidate_row = row + row_offset
                if 0 <= candidate_column < inputs.surface.width and 0 <= candidate_row < inputs.surface.height:
                    rgba = (candidate_row * inputs.surface.width + candidate_column) * 4
                    pixels[rgba:rgba + 4] = bytes((241, 235, 92, 255))
    path.parent.mkdir(parents=True, exist_ok=True)
    write_rgba8_png(path, inputs.surface.width, inputs.surface.height, bytes(pixels))


def tier_config(config: dict[str, Any], tier: int) -> dict[str, Any]:
    matches = [record for record in config["tiers"] if int(record["tier"]) == tier]
    if len(matches) != 1:
        raise ValueError(f"Expected exactly one configuration record for tier {tier}")
    return matches[0]


def in_tier(candidate: Candidate, record: dict[str, Any]) -> bool:
    radius = math.hypot(candidate.x, candidate.z)
    minimum = float(record["minimum_radius_m"])
    maximum = record["maximum_radius_m"]
    return radius >= minimum and (maximum is None or radius < float(maximum))


def build_distribution(
    inputs: Inputs,
    config: dict[str, Any],
    *,
    allow_synthetic_test_input: bool,
) -> dict[str, Any]:
    if not allow_synthetic_test_input:
        require_product_provenance(inputs.manifest)
    candidates, rejection_counters, eligible_mask = eligible_candidates(inputs, config)
    seed = str(config["deterministic_seed"])
    spacing = float(config["sampling"]["minimum_tree_spacing_m"])
    stable_input_hash = (
        inputs.surface_record["sha256"]
        + inputs.dtm_record["sha256"]
        + inputs.normal_record["sha256"]
        + inputs.canopy_normal_record["sha256"]
    )
    batches: list[dict[str, Any]] = []

    tier0 = tier_config(config, 0)
    selected0 = select_spaced(
        [candidate for candidate in candidates if in_tier(candidate, tier0)],
        int(tier0["maximum_instances"]), spacing, seed, 0,
    )
    tier0_instances = [
        tree_instance(
            candidate,
            0,
            choose_tree_source(candidate, 0, seed, stable_input_hash),
            seed,
            stable_input_hash,
            config,
        )
        for candidate in selected0
    ]
    batches.append({
        "batch_id": "tier0_editor_baked",
        "tier": 0,
        "source_mode": "heterogeneous_fixed_orientation_editor_bake",
        "instances": tier0_instances,
    })

    tier1 = tier_config(config, 1)
    selected1 = select_spaced(
        [candidate for candidate in candidates if in_tier(candidate, tier1)],
        int(tier1["maximum_instances"]), spacing, seed, 1,
    )
    tier1_instances = [
        tree_instance(
            candidate,
            1,
            choose_tree_source(candidate, 1, seed, stable_input_hash),
            seed,
            stable_input_hash,
            config,
        )
        for candidate in selected1
    ]
    batches.append({
        "batch_id": "tier1_editor_baked",
        "tier": 1,
        "source_mode": "heterogeneous_fixed_orientation_editor_bake",
        "instances": tier1_instances,
    })

    tier2 = tier_config(config, 2)
    selected2 = select_clusters(
        [candidate for candidate in candidates if in_tier(candidate, tier2)],
        int(tier2["maximum_instances"]),
        float(config["sampling"]["grove_cluster_cell_m"]),
        seed,
    )
    tier2_instances = [cluster_instance(index, members, seed, stable_input_hash, config) for index, members in selected2]
    batches.append({
        "batch_id": "tier2_editor_baked_fixed_groves",
        "tier": 2,
        "source_mode": "fixed_upper_canopy_cell_aggregate_editor_bake",
        "instances": tier2_instances,
    })

    coverage = coverage_metrics(inputs, eligible_mask, tier2, tier2_instances)

    output_kind = "TEST_ONLY_SYNTHETIC" if allow_synthetic_test_input else "VALIDATED_LIDAR_DERIVATIVE"
    result = {
        "schema": DISTRIBUTION_SCHEMA,
        "status": output_kind,
        "source_kind": "synthetic_test_fixture" if allow_synthetic_test_input else "validated_lidar_manifest",
        "coordinate_frame": config["coordinate_contract"],
        "input": {
            "lidar_schema": inputs.manifest["schema"],
            "lidar_manifest_path": portable_path(inputs.manifest_path),
            "lidar_manifest_sha256": inputs.manifest_sha256,
            "surface_features_semantic": inputs.surface_record["semantic"],
            "surface_features_sha256": inputs.surface_record["sha256"],
            "dtm_semantic": inputs.dtm_record["semantic"],
            "dtm_sha256": inputs.dtm_record["sha256"],
            "terrain_normal_validity_semantic": inputs.normal_record["semantic"],
            "terrain_normal_validity_sha256": inputs.normal_record["sha256"],
            "canopy_normal_occlusion_semantic": inputs.canopy_normal_record["semantic"],
            "canopy_normal_occlusion_sha256": inputs.canopy_normal_record["sha256"],
        },
        "tier_ranges": config["tiers"],
        "runtime_budget": config["runtime_budget"],
        "batch_count": len(batches),
        "instance_count": sum(len(batch["instances"]) for batch in batches),
        "batches": batches,
        "coverage_agreement": coverage,
        "cluster_footprints": [
            {
                "stable_id": instance["stable_id"],
                "radius_major_m": instance["footprint_radius_major_m"],
                "radius_minor_m": instance["footprint_radius_minor_m"],
                "yaw_degrees": instance["footprint_yaw_degrees"],
                "analytic_area_m2": instance["footprint_area_m2"],
            }
            for instance in tier2_instances
        ],
        "statistics": {
            "coarse_candidates": len(candidates),
            "tier0_selected": len(selected0),
            "tier1_selected": len(selected1),
            "tier2_selected": len(selected2),
            "archetype_counts": dict(sorted(
                (archetype, sum(1 for instance in tier0_instances + tier1_instances if instance["archetype"] == archetype))
                for archetype in {instance["archetype"] for instance in tier0_instances + tier1_instances}
            )),
            "rejection_counters": rejection_counters,
        },
        "runtime_policy": {
            "one_gameobject_per_tree": False,
            "batch_renderer_gameobjects": 1,
            "ordinary_vertical_billboards": 0,
            "alpha_blended_material_slots": 0,
            "source_draws_maximum": 3,
            "runtime_batches_after_editor_bake": 3,
            "fixed_orientation_atlas_cards": True,
            "camera_facing": False,
            "lod_crossfade": False,
            "alpha_mode": "clip",
            "tier3_geometry_instances": 0,
            "tier3_final_weight": "R8 eligible-canopy and Tier0-2 geometry ownership mask",
            "tier3_scalar_handoff": False,
        },
    }
    return result


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, required=True)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--lidar-manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument(
        "--coverage-overlay",
        type=Path,
        help="Optional external QA PNG: canopy mask, Tier2 footprint intersection, and Tier0/1 placement points.",
    )
    parser.add_argument(
        "--allow-synthetic-test-input",
        action="store_true",
        help="Tests only. Labels output TEST_ONLY_SYNTHETIC; never use for a product asset.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    repo_root = args.repo_root.resolve()
    config_path = args.config.resolve()
    manifest_path = args.lidar_manifest.resolve()
    output_path = args.output.resolve()
    config = json.loads(config_path.read_text(encoding="utf-8"))
    if config.get("schema") != "questflight.aerial-scenery-v1.vegetation-config.v1":
        raise ValueError("Unsupported vegetation config schema")
    inputs = load_inputs(repo_root, manifest_path, config)
    distribution = build_distribution(
        inputs,
        config,
        allow_synthetic_test_input=args.allow_synthetic_test_input,
    )
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(distribution, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    if args.coverage_overlay:
        _, _, eligible_mask = eligible_candidates(inputs, config)
        tier2 = tier_config(config, 2)
        clusters = next(batch["instances"] for batch in distribution["batches"] if int(batch["tier"]) == 2)
        trees = [
            instance
            for batch in distribution["batches"]
            if int(batch["tier"]) in (0, 1)
            for instance in batch["instances"]
        ]
        write_coverage_overlay(args.coverage_overlay.resolve(), inputs, eligible_mask, tier2, clusters, trees)
    print(json.dumps({
        "status": distribution["status"],
        "output": str(output_path),
        "instance_count": distribution["instance_count"],
        "batch_counts": {batch["batch_id"]: len(batch["instances"]) for batch in distribution["batches"]},
        "coverage_agreement": distribution["coverage_agreement"],
        "coverage_overlay": str(args.coverage_overlay.resolve()) if args.coverage_overlay else None,
    }, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
