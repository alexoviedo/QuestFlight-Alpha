#!/usr/bin/env python3
"""Generate the compact near-geometry/lidar-canopy-map ownership mask.

The output is a deterministic north-up R8 PNG on the frozen lidar grid.  Zero
means authored Tier0/Tier1 geometry owns the pixel; 255 means the Tier2/Tier3
lidar canopy terrain shading owns it. A spatial feather replaces the invalid scalar handoff previously
proposed for a lidar square that contains no samples at a 4,000 m cardinal
radius.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import struct
import zlib
from pathlib import Path
from typing import Any, Callable

from generate_vegetation_distribution import eligible_candidates, load_inputs


MASK_SCHEMA = "questflight.aerial-scenery-v1.vegetation-handoff-mask.v1"
DISTRIBUTION_SCHEMA = "questflight.aerial-scenery-v1.vegetation-distribution.v1"
LIDAR_SCHEMA = "questflight.aerial-scenery-v1.lidar.v1"
GEOMETRY_SCHEMA = "questflight.aerial-scenery-v1.vegetation-geometry.v1"
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


def load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"Expected a JSON object: {path}")
    return value


def png_chunk(kind: bytes, payload: bytes) -> bytes:
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)


def write_r8_png(path: Path, width: int, height: int, pixels: bytes) -> None:
    if len(pixels) != width * height:
        raise ValueError("R8 pixel payload length mismatch")
    scanlines = b"".join(
        b"\x00" + pixels[row * width:(row + 1) * width]
        for row in range(height)
    )
    payload = (
        b"\x89PNG\r\n\x1a\n"
        + png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 0, 0, 0, 0))
        + png_chunk(b"IDAT", zlib.compress(scanlines, 9))
        + png_chunk(b"IEND", b"")
    )
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)


def write_rgba8_png(path: Path, width: int, height: int, pixels: bytes) -> None:
    if len(pixels) != width * height * 4:
        raise ValueError("RGBA8 pixel payload length mismatch")
    stride = width * 4
    scanlines = b"".join(
        b"\x00" + pixels[row * stride:(row + 1) * stride]
        for row in range(height)
    )
    payload = (
        b"\x89PNG\r\n\x1a\n"
        + png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
        + png_chunk(b"IDAT", zlib.compress(scanlines, 9))
        + png_chunk(b"IEND", b"")
    )
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)


def require_close(actual: float, expected: float, label: str, tolerance: float = 1e-6) -> None:
    if abs(actual - expected) > tolerance:
        raise ValueError(f"{label} must be {expected}, got {actual}")


def validate_inputs(
    distribution: dict[str, Any],
    geometry: dict[str, Any],
    lidar: dict[str, Any],
    lidar_path: Path,
) -> tuple[int, int, dict[str, float]]:
    if distribution.get("schema") != DISTRIBUTION_SCHEMA:
        raise ValueError("Unsupported vegetation distribution schema")
    if distribution.get("status") != "VALIDATED_LIDAR_DERIVATIVE" or distribution.get("source_kind") != "validated_lidar_manifest":
        raise ValueError("Product handoff mask refuses a synthetic or unvalidated distribution")
    if geometry.get("schema") != GEOMETRY_SCHEMA:
        raise ValueError("Unsupported vegetation geometry schema")
    if lidar.get("schema") != LIDAR_SCHEMA:
        raise ValueError("Unsupported lidar schema")
    embedded_manifest_hash = distribution.get("input", {}).get("lidar_manifest_sha256")
    actual_manifest_hash = sha256(lidar_path)
    if embedded_manifest_hash != actual_manifest_hash:
        raise ValueError("Distribution is not pinned to the supplied lidar manifest SHA-256")

    grid = lidar["coordinate_mapping"]["grid"]
    affine = lidar["coordinate_mapping"]["pixel_center_affine_local_meters"]
    width = int(grid["width_pixels"])
    height = int(grid["height_pixels"])
    if (width, height) != (1024, 1024):
        raise ValueError("Vegetation handoff mask is frozen at 1024x1024")
    require_close(float(grid["pixel_size_meters"]), 4.0, "pixel size")
    require_close(float(affine["x_at_column0_row0"]), -2046.0, "column-zero X")
    require_close(float(affine["z_at_column0_row0"]), 2046.0, "row-zero Z")
    require_close(float(affine["x_per_column"]), 4.0, "X per column")
    require_close(float(affine["z_per_row"]), -4.0, "Z per row")
    require_close(float(affine["x_per_row"]), 0.0, "X per row")
    require_close(float(affine["z_per_column"]), 0.0, "Z per column")
    return width, height, {key: float(value) for key, value in affine.items()}


def quantized_handoff(outside_distance_m: float, zero_padding_m: float, feather_m: float) -> int:
    if outside_distance_m <= zero_padding_m:
        return 0
    if outside_distance_m >= zero_padding_m + feather_m:
        return 255
    t = (outside_distance_m - zero_padding_m) / feather_m
    smooth = t * t * (3.0 - 2.0 * t)
    return int(smooth * 255.0 + 0.5)


def pixel_window(
    center_x: float,
    center_z: float,
    radius_m: float,
    width: int,
    height: int,
    affine: dict[str, float],
) -> tuple[range, range]:
    x0 = affine["x_at_column0_row0"]
    z0 = affine["z_at_column0_row0"]
    dx = affine["x_per_column"]
    dz = affine["z_per_row"]
    columns = sorted(((center_x - radius_m - x0) / dx, (center_x + radius_m - x0) / dx))
    rows = sorted(((center_z - radius_m - z0) / dz, (center_z + radius_m - z0) / dz))
    column_min = max(0, math.floor(columns[0]))
    column_max = min(width - 1, math.ceil(columns[1]))
    row_min = max(0, math.floor(rows[0]))
    row_max = min(height - 1, math.ceil(rows[1]))
    return range(row_min, row_max + 1), range(column_min, column_max + 1)


def rasterize_shape(
    mask: bytearray,
    width: int,
    height: int,
    affine: dict[str, float],
    center_x: float,
    center_z: float,
    radius_bound_m: float,
    outside_distance: Callable[[float, float], float],
    zero_padding_m: float,
    feather_m: float,
) -> None:
    rows, columns = pixel_window(center_x, center_z, radius_bound_m, width, height, affine)
    x0 = affine["x_at_column0_row0"]
    z0 = affine["z_at_column0_row0"]
    dx = affine["x_per_column"]
    dz = affine["z_per_row"]
    for row in rows:
        z = z0 + row * dz
        offset = row * width
        for column in columns:
            x = x0 + column * dx
            value = quantized_handoff(outside_distance(x, z), zero_padding_m, feather_m)
            pixel = offset + column
            if value < mask[pixel]:
                mask[pixel] = value


def build_mask(
    distribution: dict[str, Any],
    geometry: dict[str, Any],
    width: int,
    height: int,
    affine: dict[str, float],
    zero_padding_m: float,
    feather_m: float,
) -> tuple[bytearray, list[dict[str, Any]]]:
    if zero_padding_m < 0.0 or feather_m <= 0.0:
        raise ValueError("Ownership zero padding must be nonnegative and feather must be positive")
    objects = {record["name"]: record for record in geometry["objects"]}
    mask = bytearray([255]) * (width * height)
    records: list[dict[str, Any]] = []
    batch_counts: dict[int, int] = {}

    batches = sorted(distribution["batches"], key=lambda record: int(record["tier"]))
    for batch in batches:
        tier = int(batch["tier"])
        instances = batch.get("instances", [])
        batch_counts[tier] = len(instances)
        for instance in instances:
            center_x = float(instance["x"])
            center_z = float(instance["z"])
            yaw = math.radians(float(instance["yaw_degrees"]))
            cosine = math.cos(yaw)
            sine = math.sin(yaw)
            stable_id = str(instance["stable_id"])
            if tier in (0, 1):
                source_object = str(instance["source_object"])
                source = objects.get(source_object)
                if source is None:
                    raise ValueError(f"Geometry report lacks source object {source_object}")
                bounds_min = [float(value) for value in source["bounds_min"]]
                bounds_max = [float(value) for value in source["bounds_max"]]
                # Blender X/Y are the exported Unity horizontal axes. Centering
                # the conservative box at the placement origin and taking the
                # maximum absolute extent remains valid across the FBX axis sign.
                half_x = max(abs(bounds_min[0]), abs(bounds_max[0])) * float(instance["scale_x"])
                half_z = max(abs(bounds_min[1]), abs(bounds_max[1])) * float(instance["scale_z"])

                def rectangle_distance(x: float, z: float, *, hx: float = half_x, hz: float = half_z) -> float:
                    delta_x = x - center_x
                    delta_z = z - center_z
                    local_x = delta_x * cosine + delta_z * sine
                    local_z = -delta_x * sine + delta_z * cosine
                    qx = abs(local_x) - hx
                    qz = abs(local_z) - hz
                    return math.hypot(max(qx, 0.0), max(qz, 0.0)) + min(max(qx, qz), 0.0)

                radius_bound = math.hypot(half_x, half_z) + zero_padding_m + feather_m
                rasterize_shape(
                    mask, width, height, affine, center_x, center_z, radius_bound,
                    rectangle_distance, zero_padding_m, feather_m,
                )
                records.append({
                    "stable_id": stable_id,
                    "tier": tier,
                    "shape": "conservative_oriented_source_aabb",
                    "source_object": source_object,
                    "center_x_m": center_x,
                    "center_z_m": center_z,
                    "yaw_degrees": float(instance["yaw_degrees"]),
                    "half_extent_x_m": round(half_x, 4),
                    "half_extent_z_m": round(half_z, 4),
                })
            else:
                raise ValueError(f"Unexpected geometry batch tier {tier}")

    if batch_counts != {0: 3, 1: 19} or len(records) != 22:
        raise ValueError(f"Ownership mask requires exact Tier0/Tier1 3/19 placement counts, got {batch_counts}")
    return mask, records


def mip_chain_texels(width: int, height: int) -> int:
    total = 0
    while True:
        total += width * height
        if width == 1 and height == 1:
            return total
        width = max(1, width // 2)
        height = max(1, height // 2)


def apply_eligible_canopy_gate(mask: bytearray, eligible_mask: bytes | bytearray) -> None:
    """Make R8 the final canopy weight by zeroing every ineligible texel."""
    if len(mask) != len(eligible_mask):
        raise ValueError("Eligible-canopy mask dimensions do not match the ownership mask")
    for index, eligible in enumerate(eligible_mask):
        if not eligible:
            mask[index] = 0


def write_preview(path: Path, width: int, height: int, mask: bytes) -> None:
    pixels = bytearray(width * height * 4)
    for index, value in enumerate(mask):
        if value == 0:
            color = (18, 42, 78, 255)
        elif value == 255:
            color = (42, 132, 74, 255)
        else:
            t = value / 255.0
            color = (
                int(224.0 - t * 126.0 + 0.5),
                int(130.0 + t * 34.0 + 0.5),
                int(32.0 + t * 46.0 + 0.5),
                255,
            )
        base = index * 4
        pixels[base:base + 4] = bytes(color)
    write_rgba8_png(path, width, height, bytes(pixels))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, required=True)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--distribution", type=Path, required=True)
    parser.add_argument("--geometry-report", type=Path, required=True)
    parser.add_argument("--lidar-manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--preview", type=Path)
    parser.add_argument("--zero-padding-m", type=float, default=16.0)
    parser.add_argument("--feather-m", type=float, default=64.0)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    repo_root = args.repo_root.resolve()
    config_path = args.config.resolve()
    distribution_path = args.distribution.resolve()
    geometry_path = args.geometry_report.resolve()
    lidar_path = args.lidar_manifest.resolve()
    output_path = args.output.resolve()
    report_path = args.report.resolve()
    distribution = load_json(distribution_path)
    geometry = load_json(geometry_path)
    lidar = load_json(lidar_path)
    config = load_json(config_path)
    if config.get("schema") != "questflight.aerial-scenery-v1.vegetation-config.v1":
        raise ValueError("Unsupported vegetation config schema")
    width, height, affine = validate_inputs(distribution, geometry, lidar, lidar_path)
    mask, footprints = build_mask(
        distribution,
        geometry,
        width,
        height,
        affine,
        args.zero_padding_m,
        args.feather_m,
    )
    lidar_inputs = load_inputs(repo_root, lidar_path, config)
    _, eligible_counters, eligible_mask = eligible_candidates(lidar_inputs, config)
    geometry_handoff_nonzero_before_eligibility = sum(value > 0 for value in mask)
    apply_eligible_canopy_gate(mask, eligible_mask)
    write_r8_png(output_path, width, height, bytes(mask))
    if args.preview:
        write_preview(args.preview.resolve(), width, height, bytes(mask))

    zero_pixels = sum(value == 0 for value in mask)
    full_pixels = sum(value == 255 for value in mask)
    transition_pixels = len(mask) - zero_pixels - full_pixels
    eligible_pixels = sum(bool(value) for value in eligible_mask)
    ineligible_pixels = len(mask) - eligible_pixels
    eligible_geometry_owned_pixels = sum(
        bool(eligible_mask[index]) and value == 0
        for index, value in enumerate(mask)
    )
    pixel_area = abs(affine["x_per_column"] * affine["z_per_row"])
    report = {
        "schema": MASK_SCHEMA,
        "status": "VALIDATED_LIDAR_DERIVATIVE",
        "semantic": "tier0_1_geometry_to_lidar_canopy_map_handoff",
        "source_and_license": {
            "source": "Deterministic project-authored derivative of the validated vegetation distribution and source bounds",
            "license": "Project-authored",
            "external_assets": [],
        },
        "inputs": {
            "distribution_path": portable_path(distribution_path),
            "distribution_sha256": sha256(distribution_path),
            "geometry_report_path": portable_path(geometry_path),
            "geometry_report_sha256": sha256(geometry_path),
            "config_path": portable_path(config_path),
            "config_sha256": sha256(config_path),
            "lidar_manifest_path": portable_path(lidar_path),
            "lidar_manifest_sha256": sha256(lidar_path),
            "surface_features_semantic": distribution["input"]["surface_features_semantic"],
            "surface_features_sha256": distribution["input"]["surface_features_sha256"],
            "dtm_semantic": distribution["input"]["dtm_semantic"],
            "dtm_sha256": distribution["input"]["dtm_sha256"],
        },
        "output": {
            "path": portable_path(output_path),
            "sha256": sha256(output_path),
            "png_bytes": output_path.stat().st_size,
            "dimensions": [width, height],
            "encoding": "PNG R8 linear UNorm; north-up file rows",
            "runtime_property": "_AerialCanopyHandoffMask",
            "runtime_uncompressed_base_bytes": width * height,
            "mipmaps": False,
            "runtime_uncompressed_bytes": width * height,
            "theoretical_unrequested_full_mip_chain_bytes": mip_chain_texels(width, height),
        },
        "coordinate_contract": {
            "local_bounds_edges_m": [-2048.0, -2048.0, 2048.0, 2048.0],
            "pixel_center_affine_local_meters": affine,
            "unity_bottom_left_uv": "((x + 2048) / 4096, (z + 2048) / 4096)",
            "extra_v_flip": False,
            "outside_bounds_coverage": 0,
        },
        "ownership_semantics": {
            "code_0": "Pixel is outside validated eligible canopy or Tier0-1 authored geometry owns it",
            "code_255": "Pixel is validated eligible canopy fully owned by the lidar canopy terrain-shading representation",
            "zero_padding_m": args.zero_padding_m,
            "smoothstep_feather_m": args.feather_m,
            "combination": "minimum quantized handoff value across all authored footprints",
            "tier0_tier1_footprint": "conservative oriented rectangle from the source mesh horizontal AABB and authored transform",
            "tier2_footprint": "none; Tier2 has zero geometry and therefore does not carve the canopy map",
            "scalar_handoff_uniform": None,
            "tier3_spatial_domain": "Tier2/Tier3 lidar canopy terrain shading inside the validated lidar square",
            "eligible_canopy_gate": {
                "dtm_code": "nonzero",
                "minimum_canopy_hag_m": config["sampling"]["minimum_canopy_hag_m"],
                "minimum_vegetation_density": config["sampling"]["minimum_vegetation_density"],
                "maximum_building_hag_m": config["sampling"]["maximum_building_hag_m"],
                "source": "same decoded surface-features and DTM eligibility function used by the frozen distribution",
                "linear_sampling_note": "base-level R8 is sampled bilinearly; no mip chain can reintroduce excluded distant texels",
            },
        },
        "statistics": {
            "footprint_count": len(footprints),
            "tier_counts": {
                "0": sum(record["tier"] == 0 for record in footprints),
                "1": sum(record["tier"] == 1 for record in footprints),
                "2": sum(record["tier"] == 2 for record in footprints),
            },
            "eligible_canopy_pixels": eligible_pixels,
            "eligible_canopy_area_m2": eligible_pixels * pixel_area,
            "ineligible_excluded_pixels": ineligible_pixels,
            "ineligible_excluded_area_m2": ineligible_pixels * pixel_area,
            "eligible_geometry_owned_zero_pixels": eligible_geometry_owned_pixels,
            "eligible_geometry_owned_zero_area_m2": eligible_geometry_owned_pixels * pixel_area,
            "total_final_weight_zero_pixels": zero_pixels,
            "total_final_weight_zero_area_m2": zero_pixels * pixel_area,
            "transition_pixels": transition_pixels,
            "transition_area_m2": transition_pixels * pixel_area,
            "tier3_owned_full_pixels": full_pixels,
            "tier3_owned_full_area_m2": full_pixels * pixel_area,
            "tier3_nonzero_eligible_coverage_fraction": round(
                (transition_pixels + full_pixels) / eligible_pixels if eligible_pixels else 0.0,
                8,
            ),
            "geometry_handoff_nonzero_pixels_before_eligible_canopy_gate": geometry_handoff_nonzero_before_eligibility,
            "eligible_canopy_exclusion_counters": eligible_counters,
        },
        "footprints": footprints,
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({
        "status": report["status"],
        "output": str(output_path),
        "sha256": report["output"]["sha256"],
        "png_bytes": report["output"]["png_bytes"],
        "runtime_uncompressed_bytes": report["output"]["runtime_uncompressed_bytes"],
        "eligible_canopy_pixels": eligible_pixels,
        "ineligible_excluded_pixels": ineligible_pixels,
        "eligible_geometry_owned_zero_pixels": eligible_geometry_owned_pixels,
        "transition_pixels": transition_pixels,
        "tier3_owned_full_pixels": full_pixels,
        "preview": str(args.preview.resolve()) if args.preview else None,
    }, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
