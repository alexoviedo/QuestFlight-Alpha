#!/usr/bin/env python3
"""Build bounded Quest runtime terrain samples and a normal map from an external 1 m 3DEP DEM."""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import math
import struct
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUTPUT = ROOT / "QuestFlight" / "Assets" / "Production" / "Environment" / "ReconstructionV2"
DEFAULT_LEGACY_TERRAIN = (
    ROOT / "QuestFlight" / "Assets" / "Resources" / "QuestFlightLab" /
    "Environment" / "KBDU" / "kbdu_terrain_rings.json"
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def smoothstep(edge0: float, edge1: float, value: float) -> float:
    if edge0 == edge1:
        return float(value >= edge1)
    t = max(0.0, min(1.0, (value - edge0) / (edge1 - edge0)))
    return t * t * (3.0 - 2.0 * t)


def runway_blended_height(east: float, north: float, raw_relative: float, runway: dict[str, Any]) -> float:
    ax, az = runway["end_08_enu_meters"]
    bx, bz = runway["end_26_enu_meters"]
    abx, abz = bx - ax, bz - az
    length_squared = abx * abx + abz * abz
    unclamped_t = ((east - ax) * abx + (north - az) * abz) / length_squared
    t = max(0.0, min(1.0, unclamped_t))
    closest_x, closest_z = ax + t * abx, az + t * abz
    distance = math.hypot(east - closest_x, north - closest_z)
    core = float(runway["half_width_meters"]) + float(runway["core_margin_meters"])
    outer = float(runway["half_width_meters"]) + float(runway["outer_margin_meters"])
    end_distance = 0.0
    if unclamped_t < 0.0:
        end_distance = -unclamped_t * math.sqrt(length_squared)
    elif unclamped_t > 1.0:
        end_distance = (unclamped_t - 1.0) * math.sqrt(length_squared)
    radial = math.hypot(distance, end_distance)
    weight = 1.0 - smoothstep(core, outer, radial)
    plane = float(runway["end_08_height_relative_meters"]) + t * (
        float(runway["end_26_height_relative_meters"]) - float(runway["end_08_height_relative_meters"])
    )
    return raw_relative + weight * (plane - raw_relative)


def bilinear_source(source: np.ndarray, east: float, north: float, half_extent: float) -> float:
    height, width = source.shape
    x = max(0.0, min(width - 1.0, (east + half_extent) / (2.0 * half_extent) * (width - 1)))
    y = max(0.0, min(height - 1.0, (half_extent - north) / (2.0 * half_extent) * (height - 1)))
    x0, y0 = int(math.floor(x)), int(math.floor(y))
    x1, y1 = min(x0 + 1, width - 1), min(y0 + 1, height - 1)
    tx, ty = x - x0, y - y0
    top = float(source[y0, x0]) * (1.0 - tx) + float(source[y0, x1]) * tx
    bottom = float(source[y1, x0]) * (1.0 - tx) + float(source[y1, x1]) * tx
    return top * (1.0 - ty) + bottom * ty


def build_layer(
    source: np.ndarray,
    layer_id: str,
    zone: str,
    minimum: tuple[float, float],
    maximum: tuple[float, float],
    spacing: float,
    origin_elevation: float,
    runway: dict[str, Any],
) -> tuple[dict[str, Any], np.ndarray]:
    width = int(round((maximum[0] - minimum[0]) / spacing)) + 1
    height = int(round((maximum[1] - minimum[1]) / spacing)) + 1
    values = np.empty((height, width), dtype=np.float32)
    half_extent = 2000.0
    for z_index in range(height):
        north = minimum[1] + z_index * spacing
        for x_index in range(width):
            east = minimum[0] + x_index * spacing
            raw_relative = bilinear_source(source, east, north, half_extent) - origin_elevation
            values[z_index, x_index] = runway_blended_height(east, north, raw_relative, runway)

    quantization = 0.01
    quantized = np.rint(values / quantization).astype("<i2")
    payload = base64.b64encode(quantized.tobytes(order="C")).decode("ascii")
    layer = {
        "id": layer_id,
        "zone": zone,
        "minimum_enu_meters": [minimum[0], minimum[1]],
        "maximum_enu_meters": [maximum[0], maximum[1]],
        "spacing_meters": spacing,
        "width": width,
        "height": height,
        "height_quantization_meters": quantization,
        "height_cm_little_endian_base64": payload,
        "minimum_height_relative_meters": round(float(values.min()), 3),
        "maximum_height_relative_meters": round(float(values.max()), 3),
        "source_resolution_meters": 1.0,
    }
    return layer, values


def bilinear_runtime_grid(
    values: np.ndarray,
    minimum: tuple[float, float],
    spacing: float,
    east: float,
    north: float,
) -> float:
    height, width = values.shape
    grid_x = max(0.0, min(width - 1.0, (east - minimum[0]) / spacing))
    grid_z = max(0.0, min(height - 1.0, (north - minimum[1]) / spacing))
    x0, z0 = int(math.floor(grid_x)), int(math.floor(grid_z))
    x1, z1 = min(x0 + 1, width - 1), min(z0 + 1, height - 1)
    tx, tz = grid_x - x0, grid_z - z0
    low = float(values[z0, x0]) * (1.0 - tx) + float(values[z0, x1]) * tx
    high = float(values[z1, x0]) * (1.0 - tx) + float(values[z1, x1]) * tx
    return low * (1.0 - tz) + high * tz


def resample_runtime_grid(values: np.ndarray, spacing: float) -> np.ndarray:
    """Bilinearly reconstruct a regular grid at endpoint-aligned 1 m spacing."""
    source_z = np.arange(values.shape[0], dtype=np.float64) * spacing
    source_x = np.arange(values.shape[1], dtype=np.float64) * spacing
    target_z = np.arange(int(round(source_z[-1])) + 1, dtype=np.float64)
    target_x = np.arange(int(round(source_x[-1])) + 1, dtype=np.float64)
    horizontal = np.empty((values.shape[0], target_x.size), dtype=np.float32)
    for row in range(values.shape[0]):
        horizontal[row] = np.interp(target_x, source_x, values[row])
    result = np.empty((target_z.size, target_x.size), dtype=np.float32)
    for column in range(target_x.size):
        result[:, column] = np.interp(target_z, source_z, horizontal[:, column])
    return result


def runway_blended_array(raw_relative: np.ndarray, minimum: tuple[float, float], runway: dict[str, Any]) -> np.ndarray:
    east_grid, north_grid = np.meshgrid(
        minimum[0] + np.arange(raw_relative.shape[1], dtype=np.float32),
        minimum[1] + np.arange(raw_relative.shape[0], dtype=np.float32))
    ax, az = (float(value) for value in runway["end_08_enu_meters"])
    bx, bz = (float(value) for value in runway["end_26_enu_meters"])
    abx, abz = bx - ax, bz - az
    length_squared = abx * abx + abz * abz
    unclamped_t = ((east_grid - ax) * abx + (north_grid - az) * abz) / length_squared
    t = np.clip(unclamped_t, 0.0, 1.0)
    closest_x, closest_z = ax + t * abx, az + t * abz
    distance = np.hypot(east_grid - closest_x, north_grid - closest_z)
    length = math.sqrt(length_squared)
    end_distance = np.where(unclamped_t < 0.0, -unclamped_t * length,
                            np.where(unclamped_t > 1.0, (unclamped_t - 1.0) * length, 0.0))
    radial = np.hypot(distance, end_distance)
    core = float(runway["half_width_meters"]) + float(runway["core_margin_meters"])
    outer = float(runway["half_width_meters"]) + float(runway["outer_margin_meters"])
    normalized = np.clip((radial - core) / (outer - core), 0.0, 1.0)
    weight = 1.0 - normalized * normalized * (3.0 - 2.0 * normalized)
    plane = float(runway["end_08_height_relative_meters"]) + t * (
        float(runway["end_26_height_relative_meters"]) - float(runway["end_08_height_relative_meters"])
    )
    return raw_relative + weight * (plane - raw_relative)


def geometric_error_metrics(
    source: np.ndarray,
    origin_elevation: float,
    near_values: np.ndarray,
    airport_values: np.ndarray,
    airport_minimum: tuple[float, float],
    airport_maximum: tuple[float, float],
    near_spacing: float,
    airport_spacing: float,
    runway: dict[str, Any],
) -> dict[str, float]:
    source_relative = source[::-1].astype(np.float32) - origin_elevation
    near_quantized = np.rint(near_values / 0.01).astype(np.float32) * 0.01
    airport_quantized = np.rint(airport_values / 0.01).astype(np.float32) * 0.01
    near_error = np.abs(resample_runtime_grid(near_quantized, near_spacing) - source_relative)
    axis = np.arange(source.shape[0], dtype=np.float32) - 2000.0
    airport_x = (axis >= airport_minimum[0]) & (axis <= airport_maximum[0])
    airport_z = (axis >= airport_minimum[1]) & (axis <= airport_maximum[1])
    non_airport = np.ones(source.shape, dtype=bool)
    non_airport[np.ix_(airport_z, airport_x)] = False

    x0, x1 = int(round(airport_minimum[0] + 2000.0)), int(round(airport_maximum[0] + 2000.0)) + 1
    z0, z1 = int(round(airport_minimum[1] + 2000.0)), int(round(airport_maximum[1] + 2000.0)) + 1
    airport_target = runway_blended_array(source_relative[z0:z1, x0:x1], airport_minimum, runway)
    airport_error = np.abs(resample_runtime_grid(airport_quantized, airport_spacing) - airport_target)

    seam_errors: list[float] = []
    for east in np.arange(airport_minimum[0], airport_maximum[0] + 0.1, airport_spacing):
        for north in (airport_minimum[1], airport_maximum[1]):
            seam_errors.append(abs(
                bilinear_runtime_grid(airport_quantized, airport_minimum, airport_spacing, east, north) -
                bilinear_runtime_grid(near_quantized, (-2000.0, -2000.0), near_spacing, east, north)))
    for north in np.arange(airport_minimum[1], airport_maximum[1] + 0.1, airport_spacing):
        for east in (airport_minimum[0], airport_maximum[0]):
            seam_errors.append(abs(
                bilinear_runtime_grid(airport_quantized, airport_minimum, airport_spacing, east, north) -
                bilinear_runtime_grid(near_quantized, (-2000.0, -2000.0), near_spacing, east, north)))
    return {
        "near_maximum_error_meters": round(float(near_error[non_airport].max()), 4),
        "near_average_error_meters": round(float(near_error[non_airport].mean()), 4),
        "airport_maximum_error_meters": round(float(airport_error.max()), 4),
        "airport_average_error_meters": round(float(airport_error.mean()), 4),
        "sealed_boundary_maximum_error_meters": round(float(max(seam_errors)), 4),
    }


def seal_inner_layer_to_outer(
    layer: dict[str, Any],
    values: np.ndarray,
    outer_values: np.ndarray,
    outer_minimum: tuple[float, float],
    outer_spacing: float,
    blend_width_meters: float,
) -> None:
    """Make the shared boundary exact and fade the correction into the detailed inner grid."""
    minimum = tuple(float(value) for value in layer["minimum_enu_meters"])
    maximum = tuple(float(value) for value in layer["maximum_enu_meters"])
    spacing = float(layer["spacing_meters"])
    for z_index in range(values.shape[0]):
        north = minimum[1] + z_index * spacing
        for x_index in range(values.shape[1]):
            east = minimum[0] + x_index * spacing
            edge_distance = min(east - minimum[0], maximum[0] - east,
                                north - minimum[1], maximum[1] - north)
            outer_weight = 1.0 - smoothstep(0.0, blend_width_meters, edge_distance)
            if outer_weight <= 0.0:
                continue
            outer_height = bilinear_runtime_grid(
                outer_values, outer_minimum, outer_spacing, east, north)
            values[z_index, x_index] = values[z_index, x_index] * (1.0 - outer_weight) + outer_height * outer_weight

    quantization = float(layer["height_quantization_meters"])
    quantized = np.rint(values / quantization).astype("<i2")
    layer["height_cm_little_endian_base64"] = base64.b64encode(
        quantized.tobytes(order="C")).decode("ascii")
    layer["minimum_height_relative_meters"] = round(float(values.min()), 3)
    layer["maximum_height_relative_meters"] = round(float(values.max()), 3)


def build_normal_map(source: np.ndarray, size: int, half_extent: float, target: Path) -> None:
    image = Image.fromarray(source.astype(np.float32)).resize((size, size), Image.Resampling.BILINEAR)
    heights = np.asarray(image, dtype=np.float32)
    meters_per_pixel = (half_extent * 2.0) / (size - 1)
    derivative_south, derivative_east = np.gradient(heights, meters_per_pixel, meters_per_pixel)
    normal_x = -derivative_east
    normal_y = np.ones_like(heights)
    normal_z = derivative_south
    length = np.sqrt(normal_x * normal_x + normal_y * normal_y + normal_z * normal_z)
    packed = np.stack((normal_x / length, normal_y / length, normal_z / length), axis=-1)
    packed = np.clip(np.rint((packed * 0.5 + 0.5) * 255.0), 0, 255).astype(np.uint8)
    Image.fromarray(packed).save(target, format="PNG", optimize=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--config", type=Path, default=Path(__file__).with_name("config.json"))
    parser.add_argument("--legacy-terrain", type=Path, default=DEFAULT_LEGACY_TERRAIN)
    args = parser.parse_args()
    config = json.loads(args.config.read_text(encoding="utf-8"))
    receipt_path = args.raw_dir / "fetch_receipt.json"
    dem_path = args.raw_dir / "near_4km_dem_f32.tif"
    if not receipt_path.exists() or not dem_path.exists():
        raise RuntimeError("Missing external 3DEP receipt or TIFF; run fetch_3dep_dem.py first")
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    legacy_terrain = json.loads(args.legacy_terrain.read_text(encoding="utf-8"))
    if receipt["raw_dem"]["sha256"] != sha256(dem_path):
        raise RuntimeError("External 3DEP TIFF hash does not match its receipt")

    with Image.open(dem_path) as image:
        source = np.asarray(image, dtype=np.float32)
    expected = int(config["near_zone"]["source_size_pixels"])
    if source.shape != (expected, expected) or not np.isfinite(source).all():
        raise RuntimeError(f"Unexpected or nonfinite DEM array: {source.shape}")

    output_dir = args.output_dir.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    origin = float(config["origin"]["elevation_msl_meters"])
    runway = config["runway_08_26"]
    near, near_values = build_layer(
        source, "near_4km_v2", "Near", (-2000.0, -2000.0), (2000.0, 2000.0),
        float(config["near_zone"]["runtime_spacing_meters"]), origin, runway)
    airport_minimum = tuple(float(value) for value in config["airport_zone"]["minimum_enu_meters"])
    airport_maximum = tuple(float(value) for value in config["airport_zone"]["maximum_enu_meters"])
    airport, airport_values = build_layer(
        source, "airport_5m_v2", "Airport", airport_minimum, airport_maximum,
        float(config["airport_zone"]["runtime_spacing_meters"]), origin, runway)
    seal_inner_layer_to_outer(
        airport,
        airport_values,
        near_values,
        (-2000.0, -2000.0),
        float(config["near_zone"]["runtime_spacing_meters"]),
        40.0,
    )
    error_metrics = geometric_error_metrics(
        source, origin, near_values, airport_values, airport_minimum, airport_maximum,
        float(config["near_zone"]["runtime_spacing_meters"]),
        float(config["airport_zone"]["runtime_spacing_meters"]), runway)

    terrain_json_path = output_dir / "KBDU_ProductionTerrainSurfaceV2.json"
    retained_layers = []
    for retained_id, retained_zone in (("mid_12km", "Mid"), ("far_24km", "Far")):
        source_layer = next(layer for layer in legacy_terrain["layers"] if layer["id"] == retained_id)
        retained_layers.append({
            "id": retained_id + "_retained_v2",
            "zone": retained_zone,
            "minimum_enu_meters": [source_layer["min_x_meters"], source_layer["min_z_meters"]],
            "maximum_enu_meters": [source_layer["max_x_meters"], source_layer["max_z_meters"]],
            "spacing_meters": source_layer["spacing_meters"],
            "width": source_layer["width"],
            "height": source_layer["height"],
            "height_quantization_meters": legacy_terrain["height_quantization_meters"],
            "height_cm_little_endian_base64": source_layer["height_dm_little_endian_base64"],
            "minimum_height_relative_meters": round(
                source_layer["minimum_render_elevation_msl_meters"] - origin, 3),
            "maximum_height_relative_meters": round(
                source_layer["maximum_render_elevation_msl_meters"] - origin, 3),
            "source_resolution_meters": source_layer["spacing_meters"],
            "source_fidelity_disclosure": "Retained frozen 2026-07-10 USGS 3DEP samples; no added source detail.",
        })

    document = {
        "schema_version": 2,
        "coordinate_frame": "KBDU local tangent ENU; Unity +X=east, +Z=north",
        "origin": config["origin"],
        "source": {
            **config["source"],
            "raw_dem_sha256": receipt["raw_dem"]["sha256"],
            "request_sha256": receipt["request"]["request_sha256"],
            "retrieved_utc": receipt["retrieved_utc"],
            "raw_data_committed": False,
        },
        "layers": [airport, near, *retained_layers],
        "runway_blend": runway,
        "geometric_error_against_1m_source": error_metrics,
        "runtime_triangle_estimate": 2 * ((near["width"] - 1) * (near["height"] - 1) +
                                           (airport["width"] - 1) * (airport["height"] - 1)),
    }
    terrain_json_path.write_text(json.dumps(document, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    normal_path = output_dir / "KBDU_NearTerrainV2_Normal.png"
    build_normal_map(source, int(config["near_zone"]["normal_map_size_pixels"]), 2000.0, normal_path)
    manifest = {
        "schema_version": 1,
        "source_endpoint": config["source"]["service_url"],
        "source_license": config["source"]["license"],
        "source_credit": config["source"]["credit"],
        "source_raw_sha256": receipt["raw_dem"]["sha256"],
        "source_raw_bytes": receipt["raw_dem"]["bytes"],
        "source_resolution_meters": 1.0,
        "runtime_layers": [
            {"id": near["id"], "spacing_meters": near["spacing_meters"], "samples": near["width"] * near["height"]},
            {"id": airport["id"], "spacing_meters": airport["spacing_meters"], "samples": airport["width"] * airport["height"]},
        ],
        "runtime_triangle_estimate": document["runtime_triangle_estimate"],
        "geometric_error_against_1m_source": error_metrics,
        "derived_assets": [
            {"path": terrain_json_path.relative_to(ROOT).as_posix(), "bytes": terrain_json_path.stat().st_size, "sha256": sha256(terrain_json_path)},
            {"path": normal_path.relative_to(ROOT).as_posix(), "bytes": normal_path.stat().st_size, "sha256": sha256(normal_path)},
        ],
        "raw_data_policy": "Raw DEM and service responses remain in the external artifact cache and are not committed.",
    }
    manifest_path = output_dir / "KBDU_ProductionTerrainSurfaceV2_SourceManifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(terrain_json_path)
    print(normal_path)
    print(manifest_path)
    print(f"Runtime terrain estimate: {document['runtime_triangle_estimate']:,} triangles")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
