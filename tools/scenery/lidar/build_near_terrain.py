#!/usr/bin/env python3
"""Build the bounded fixed near-terrain mesh and its evidence report.

The mesh is an offline derivative.  It does not create or repair any Unity
hierarchy and it does not claim that the source Z values meet an operational
vertical-accuracy tolerance.
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Any, Iterable

import numpy as np
from PIL import Image

from common import (
    canonical_json_bytes,
    compute_grid_contract,
    decode_height_image,
    default_config_path,
    file_record,
    load_config,
    load_json,
    repository_root,
    sample_north_up_grid,
    sha256_bytes,
    sha256_file,
    smoothstep,
    write_json,
    write_model_meta,
    write_text_meta,
)


LOGICAL_ASSET_DIR = "QuestFlight/Assets/Production/Environment/AerialSceneryV1/Lidar"
MANIFEST_NAME = "KBDU_LidarManifestV1.json"
MESH_NAME = "KBDU_Lidar_NearTerrain_Fixed.obj"
RUNWAY_INPUTS_NAME = "KBDU_Lidar_RunwayPlaneInputs.json"
ERROR_REPORT_NAME = "KBDU_Lidar_NearTerrain_ErrorReport.json"
HEATMAP_NAME = "KBDU_Lidar_NearTerrain_ErrorHeatmap.png"

RASTER_SEMANTICS = {
    "digital_terrain_model",
    "digital_surface_model",
    "surface_features_canopy_building",
    "terrain_surface_normal_and_validity",
    "canopy_height_normal_and_occlusion",
    "building_height_confidence",
}
NEAR_SEMANTICS = {
    "fixed_near_terrain_mesh",
    "runway_plane_preservation_inputs",
    "near_terrain_error_report",
}


def _segment(start: float, end: float, maximum_spacing: float) -> list[float]:
    if not end > start or not maximum_spacing > 0.0:
        raise ValueError("Axis segment bounds/spacing are invalid")
    intervals = int(math.ceil((end - start) / maximum_spacing))
    return np.linspace(start, end, intervals + 1, dtype=np.float64).tolist()


def _merge_axis(base: Iterable[float], forced: Iterable[float], tolerance: float = 0.25) -> np.ndarray:
    """Prefer exact forced coordinates and discard near-duplicate base lines."""
    selected: list[float] = []
    for value in forced:
        value = float(value)
        if not any(abs(value - previous) < tolerance for previous in selected):
            selected.append(value)
    for value in base:
        value = float(value)
        if not any(abs(value - previous) < tolerance for previous in selected):
            selected.append(value)
    result = np.asarray(sorted(selected), dtype=np.float64)
    if np.any(np.diff(result) <= 0.0):
        raise ValueError("Mesh axis is not strictly increasing")
    return result


def adaptive_axis(
    half_extent: float,
    core_half_extent: float,
    outer_spacing: float,
    core_spacing: float,
    forced: Iterable[float],
) -> np.ndarray:
    base = (
        _segment(-half_extent, -core_half_extent, outer_spacing)
        + _segment(-core_half_extent, core_half_extent, core_spacing)
        + _segment(core_half_extent, half_extent, outer_spacing)
    )
    forced_values = [-half_extent, half_extent, -core_half_extent, 0.0, core_half_extent]
    forced_values.extend(float(value) for value in forced if -half_extent < float(value) < half_extent)
    result = _merge_axis(base, forced_values)
    if result[0] != -half_extent or result[-1] != half_extent:
        raise AssertionError("Mesh border coordinates must be exact")
    return result


def find_primary_runway(reference: dict[str, Any], runway_id: str) -> dict[str, Any]:
    for runway in reference["faa"]["runways"]:
        if runway["runway_id"] == runway_id:
            return runway
    raise ValueError(f"Primary runway {runway_id!r} is absent from the pinned reference context")


def runway_basis(runway: dict[str, Any]) -> dict[str, Any]:
    first, second = runway["endpoints"]
    p0 = np.asarray([first["x_east_meters"], first["z_north_meters"]], dtype=np.float64)
    p1 = np.asarray([second["x_east_meters"], second["z_north_meters"]], dtype=np.float64)
    delta = p1 - p0
    length = float(np.linalg.norm(delta))
    if length <= 0.0:
        raise ValueError("Runway endpoints are coincident")
    along_unit = delta / length
    lateral_unit = np.asarray([-along_unit[1], along_unit[0]], dtype=np.float64)
    return {
        "p0": p0,
        "p1": p1,
        "length": length,
        "along_unit": along_unit,
        "lateral_unit": lateral_unit,
        "z0": float(first["usgs_elevation_msl_meters"]),
        "z1": float(second["usgs_elevation_msl_meters"]),
    }


def runway_coordinates(local_x: Any, local_z: Any, basis: dict[str, Any]) -> tuple[np.ndarray, np.ndarray]:
    x = np.asarray(local_x, dtype=np.float64)
    z = np.asarray(local_z, dtype=np.float64)
    dx = x - basis["p0"][0]
    dz = z - basis["p0"][1]
    along = dx * basis["along_unit"][0] + dz * basis["along_unit"][1]
    lateral = dx * basis["lateral_unit"][0] + dz * basis["lateral_unit"][1]
    return along, lateral


def preserved_surface(
    source_z: Any,
    local_x: Any,
    local_z: Any,
    basis: dict[str, Any],
    runway_width_meters: float,
    blend: dict[str, float],
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    source = np.asarray(source_z, dtype=np.float64)
    along, lateral = runway_coordinates(local_x, local_z, basis)
    plane = basis["z0"] + along * (basis["z1"] - basis["z0"]) / basis["length"]
    half_width = runway_width_meters * 0.5
    core_lateral = half_width + float(blend["core_lateral_margin_meters"])
    outer_lateral = half_width + float(blend["outer_lateral_margin_meters"])
    lateral_weight = 1.0 - smoothstep(core_lateral, outer_lateral, np.abs(lateral))
    beyond = np.maximum(np.maximum(-along, along - basis["length"]), 0.0)
    end_margin = float(blend["outer_end_margin_meters"])
    longitudinal_weight = 1.0 - smoothstep(0.0, end_margin, beyond)
    weight = lateral_weight * longitudinal_weight
    return source * (1.0 - weight) + plane * weight, weight, plane


def forced_breakline_coordinates(
    runway: dict[str, Any], basis: dict[str, Any], blend: dict[str, float]
) -> tuple[list[float], list[float]]:
    width_meters = float(runway["width_feet"]) * 0.3048
    offsets = [
        0.0,
        -(width_meters * 0.5 + float(blend["core_lateral_margin_meters"])),
        width_meters * 0.5 + float(blend["core_lateral_margin_meters"]),
        -(width_meters * 0.5 + float(blend["outer_lateral_margin_meters"])),
        width_meters * 0.5 + float(blend["outer_lateral_margin_meters"]),
    ]
    end_margin = float(blend["outer_end_margin_meters"])
    along_positions = [-end_margin, 0.0, basis["length"], basis["length"] + end_margin]
    points: list[np.ndarray] = [basis["p0"], basis["p1"]]
    for x, z in runway["polygon_xz_meters"]:
        points.append(np.asarray([x, z], dtype=np.float64))
    for along in along_positions:
        center = basis["p0"] + basis["along_unit"] * along
        for lateral in offsets:
            points.append(center + basis["lateral_unit"] * lateral)
    return [float(point[0]) for point in points], [float(point[1]) for point in points]


def reconstruct_mesh(
    x_axis: np.ndarray,
    z_axis: np.ndarray,
    source_z_vertices: np.ndarray,
    sample_x: Any,
    sample_z: Any,
    refined_cell_lookup: np.ndarray | None = None,
    refined_center_source_z: np.ndarray | None = None,
) -> np.ndarray:
    """Sample the exact triangles used by the OBJ, including optional cell centers."""
    x = np.asarray(sample_x, dtype=np.float64)
    z = np.asarray(sample_z, dtype=np.float64)
    ix = np.searchsorted(x_axis, x, side="right") - 1
    iz = np.searchsorted(z_axis, z, side="right") - 1
    ix = np.clip(ix, 0, len(x_axis) - 2)
    iz = np.clip(iz, 0, len(z_axis) - 2)
    x0, x1 = x_axis[ix], x_axis[ix + 1]
    z0, z1 = z_axis[iz], z_axis[iz + 1]
    tx = (x - x0) / (x1 - x0)
    tz = (z - z0) / (z1 - z0)
    h00 = source_z_vertices[iz, ix]
    h10 = source_z_vertices[iz, ix + 1]
    h01 = source_z_vertices[iz + 1, ix]
    h11 = source_z_vertices[iz + 1, ix + 1]
    lower = tx + tz <= 1.0
    lower_height = h00 + tx * (h10 - h00) + tz * (h01 - h00)
    upper_height = h11 + (1.0 - tx) * (h01 - h11) + (1.0 - tz) * (h10 - h11)
    result = np.where(lower, lower_height, upper_height)
    if refined_cell_lookup is None:
        return result
    if refined_center_source_z is None:
        raise ValueError("Refined cell lookup requires center heights")
    center_indices = refined_cell_lookup[iz, ix]
    refined = center_indices >= 0
    if not np.any(refined):
        return result
    center = refined_center_source_z[np.maximum(center_indices, 0)]
    south = refined & (tz <= np.minimum(tx, 1.0 - tx))
    north = refined & (tz >= np.maximum(tx, 1.0 - tx))
    west = refined & ~south & ~north & (tx <= np.minimum(tz, 1.0 - tz))
    east = refined & ~south & ~north & ~west
    result[south] = (
        h00[south] * (1.0 - tx[south] - tz[south])
        + h10[south] * (tx[south] - tz[south])
        + center[south] * (2.0 * tz[south])
    )
    result[north] = (
        h01[north] * (tz[north] - tx[north])
        + h11[north] * (tx[north] + tz[north] - 1.0)
        + center[north] * (2.0 * (1.0 - tz[north]))
    )
    result[west] = (
        h00[west] * (1.0 - tx[west] - tz[west])
        + h01[west] * (tz[west] - tx[west])
        + center[west] * (2.0 * tx[west])
    )
    result[east] = (
        h10[east] * (tx[east] - tz[east])
        + h11[east] * (tx[east] + tz[east] - 1.0)
        + center[east] * (2.0 * (1.0 - tx[east]))
    )
    return result


def sample_cell_indices(
    x_axis: np.ndarray, z_axis: np.ndarray, sample_x: Any, sample_z: Any
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    x = np.asarray(sample_x, dtype=np.float64)
    z = np.asarray(sample_z, dtype=np.float64)
    ix = np.clip(np.searchsorted(x_axis, x, side="right") - 1, 0, len(x_axis) - 2)
    iz = np.clip(np.searchsorted(z_axis, z, side="right") - 1, 0, len(z_axis) - 2)
    flat = iz * (len(x_axis) - 1) + ix
    return ix, iz, flat


def select_refined_cells(
    x_axis: np.ndarray,
    z_axis: np.ndarray,
    sample_x: Any,
    sample_z: Any,
    base_error: np.ndarray,
    capacity: int,
) -> tuple[np.ndarray, np.ndarray]:
    """Select deterministic worst-error cells for one bounded center split."""
    _, _, flat = sample_cell_indices(x_axis, z_axis, sample_x, sample_z)
    cell_count = (len(x_axis) - 1) * (len(z_axis) - 1)
    maximum = np.zeros(cell_count, dtype=np.float64)
    np.maximum.at(maximum, flat.reshape(-1), np.abs(base_error).reshape(-1))
    capacity = max(0, min(int(capacity), cell_count))
    cell_ids = np.arange(cell_count, dtype=np.int64)
    order = np.lexsort((cell_ids, -maximum))
    selected = order[:capacity]
    lookup = np.full(cell_count, -1, dtype=np.int32)
    lookup[selected] = np.arange(len(selected), dtype=np.int32)
    return lookup.reshape((len(z_axis) - 1, len(x_axis) - 1)), maximum


def cell_source_variation(
    x_axis: np.ndarray,
    z_axis: np.ndarray,
    grid_vertex_source_z: np.ndarray,
    sample_x: Any,
    sample_z: Any,
    sample_source_z: np.ndarray,
) -> tuple[np.ndarray, np.ndarray]:
    """Range per mesh cell using both raster samples and authored corner heights."""
    _, _, flat = sample_cell_indices(x_axis, z_axis, sample_x, sample_z)
    corners = np.stack(
        [
            grid_vertex_source_z[:-1, :-1],
            grid_vertex_source_z[:-1, 1:],
            grid_vertex_source_z[1:, :-1],
            grid_vertex_source_z[1:, 1:],
        ],
        axis=0,
    )
    low = np.min(corners, axis=0).reshape(-1).copy()
    high = np.max(corners, axis=0).reshape(-1).copy()
    np.minimum.at(low, flat.reshape(-1), sample_source_z.reshape(-1))
    np.maximum.at(high, flat.reshape(-1), sample_source_z.reshape(-1))
    variation = high - low
    return variation.reshape((len(z_axis) - 1, len(x_axis) - 1)), flat


def error_statistics(signed_error: np.ndarray, mask: np.ndarray | None = None) -> dict[str, Any]:
    values = np.asarray(signed_error, dtype=np.float64)
    if mask is not None:
        values = values[np.asarray(mask, dtype=bool)]
    values = values[np.isfinite(values)]
    if values.size == 0:
        raise ValueError("Error statistic region has no samples")
    absolute = np.abs(values)
    return {
        "maximum_absolute_meters": float(np.max(absolute)),
        "mean_absolute_meters": float(np.mean(absolute)),
        "mean_signed_meters": float(np.mean(values)),
        "p50_absolute_meters": float(np.percentile(absolute, 50)),
        "p95_absolute_meters": float(np.percentile(absolute, 95)),
        "p99_absolute_meters": float(np.percentile(absolute, 99)),
        "rmse_meters": float(np.sqrt(np.mean(values * values))),
        "sample_count": int(values.size),
        "within_0_05_meter_fraction": float(np.mean(absolute <= 0.05)),
    }


def write_obj(
    path: Path,
    x_axis: np.ndarray,
    z_axis: np.ndarray,
    local_y: np.ndarray,
    contract_sha: str,
    refined_cell_lookup: np.ndarray,
    refined_center_x: np.ndarray,
    refined_center_z: np.ndarray,
    refined_center_local_y: np.ndarray,
) -> None:
    grid_vertex_count = len(x_axis) * len(z_axis)
    vertex_count = grid_vertex_count + len(refined_center_x)
    base_triangle_count = (len(x_axis) - 1) * (len(z_axis) - 1) * 2
    triangle_count = base_triangle_count + len(refined_center_x) * 2
    lines = [
        "# QuestFlight KBDU fixed near-terrain derivative / not for navigation",
        f"# raster_contract_sha256 {contract_sha}",
        f"# vertices {vertex_count}",
        f"# triangles {triangle_count}",
        "# coordinates Unity local meters: +X east, +Y up, +Z north",
        "o KBDU_Lidar_NearTerrain_Fixed",
    ]
    for z_index, z in enumerate(z_axis):
        for x_index, x in enumerate(x_axis):
            lines.append(f"v {x:.6f} {local_y[z_index, x_index]:.6f} {z:.6f}")
    for x, z, y in zip(refined_center_x, refined_center_z, refined_center_local_y):
        lines.append(f"v {x:.6f} {y:.6f} {z:.6f}")
    width = len(x_axis)
    for z_index in range(len(z_axis) - 1):
        row = z_index * width
        next_row = (z_index + 1) * width
        for x_index in range(width - 1):
            southwest = row + x_index + 1
            southeast = southwest + 1
            northwest = next_row + x_index + 1
            northeast = northwest + 1
            center_index = int(refined_cell_lookup[z_index, x_index])
            if center_index >= 0:
                center = grid_vertex_count + center_index + 1
                lines.append(f"f {southwest} {northwest} {center}")
                lines.append(f"f {northwest} {northeast} {center}")
                lines.append(f"f {northeast} {southeast} {center}")
                lines.append(f"f {southeast} {southwest} {center}")
            else:
                lines.append(f"f {southwest} {northwest} {southeast}")
                lines.append(f"f {southeast} {northwest} {northeast}")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


def write_error_heatmap(path: Path, absolute_error: np.ndarray) -> None:
    stops = np.asarray([0.0, 0.01, 0.05, 0.25, 1.0], dtype=np.float64)
    colors = np.asarray(
        [[0, 20, 80], [0, 180, 100], [255, 220, 0], [255, 80, 0], [255, 0, 255]],
        dtype=np.float64,
    )
    values = np.clip(np.asarray(absolute_error, dtype=np.float64), stops[0], stops[-1])
    rgb = np.empty(values.shape + (3,), dtype=np.float64)
    for index in range(len(stops) - 1):
        mask = (values >= stops[index]) & (values <= stops[index + 1])
        t = (values[mask] - stops[index]) / (stops[index + 1] - stops[index])
        rgb[mask] = colors[index] * (1.0 - t[:, None]) + colors[index + 1] * t[:, None]
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(np.rint(rgb).astype(np.uint8)).save(
        path, format="PNG", optimize=True, compress_level=9
    )


def asset_record(
    path: Path, semantic: str, dimensions: dict[str, int], encoding: dict[str, Any]
) -> dict[str, Any]:
    record = file_record(path, f"{LOGICAL_ASSET_DIR}/{path.name}")
    record.update({"dimensions": dimensions, "encoding": encoding, "semantic": semantic})
    return record


def source_pipeline_records() -> list[dict[str, Any]]:
    root = repository_root()
    relative_paths = [
        "tools/scenery/conda-lock-win-64.txt",
        "tools/scenery/environment.yml",
        "tools/scenery/toolchain.json",
        "tools/scenery/lidar/build_near_terrain.py",
        "tools/scenery/lidar/common.py",
        "tools/scenery/lidar/config.json",
        "tools/scenery/lidar/derive_surfaces.py",
        "tools/scenery/lidar/ept_snapshot.py",
        "tools/scenery/lidar/validate_lidar.py",
    ]
    return [
        file_record(root / relative_path, relative_path)
        for relative_path in relative_paths
        if (root / relative_path).is_file()
    ]


def build(
    config_path: Path,
    manifest_path: Path,
    reference_path: Path,
    qa_output: Path,
    write_unity_meta: bool,
) -> dict[str, Any]:
    config = load_config(config_path)
    contract = compute_grid_contract(config)
    manifest = load_json(manifest_path)
    if manifest.get("schema") != "questflight.aerial-scenery-v1.lidar.v1":
        raise ValueError("Unsupported lidar product manifest schema")
    output_root = manifest_path.parent
    raster_assets = [asset for asset in manifest["assets"] if asset["semantic"] in RASTER_SEMANTICS]
    if {asset["semantic"] for asset in raster_assets} != RASTER_SEMANTICS:
        raise ValueError("Lidar product does not contain the complete raster semantic set")
    for asset in raster_assets:
        path = repository_root() / asset["path"]
        if not path.is_file() or sha256_file(path) != asset["sha256"]:
            raise ValueError(f"Raster asset verification failed: {asset['path']}")
    raster_contract = {
        "assets": raster_assets,
        "coordinate_mapping": manifest["coordinate_mapping"],
        "schema": manifest["schema"],
        "source": manifest["source"],
    }
    raster_contract_sha = sha256_bytes(canonical_json_bytes(raster_contract))
    dtm_record = next(asset for asset in raster_assets if asset["semantic"] == "digital_terrain_model")
    quantization = dtm_record["encoding"]["decoded_source_z_meters"]
    dtm_path = repository_root() / dtm_record["path"]
    dtm_codes = np.asarray(Image.open(dtm_path))
    if dtm_codes.shape != (contract.height, contract.width) or np.any(dtm_codes == 0):
        raise ValueError("Filled V1 DTM must have the exact grid shape and no nodata codes")
    dtm = decode_height_image(
        dtm_path, float(quantization["offset"]), float(quantization["scale"])
    ).astype(np.float64)

    reference = load_json(reference_path)
    mesh_config = config["near_mesh"]
    runway = find_primary_runway(reference, mesh_config["primary_runway_id"])
    basis = runway_basis(runway)
    blend = mesh_config["runway_blend"]
    forced_x, forced_z = forced_breakline_coordinates(runway, basis, blend)
    x_axis = adaptive_axis(
        contract.half_extent,
        float(mesh_config["operational_core_half_extent_east_meters"]),
        float(mesh_config["outer_spacing_meters"]),
        float(mesh_config["operational_core_spacing_meters"]),
        forced_x,
    )
    z_axis = adaptive_axis(
        contract.half_extent,
        float(mesh_config["operational_core_half_extent_north_meters"]),
        float(mesh_config["outer_spacing_meters"]),
        float(mesh_config["operational_core_spacing_meters"]),
        forced_z,
    )
    mesh_x, mesh_z = np.meshgrid(x_axis, z_axis)
    sampled_dtm = sample_north_up_grid(dtm, contract, mesh_x, mesh_z)
    runway_width_meters = float(runway["width_feet"]) * 0.3048
    authored_source_z, mesh_blend_weight, mesh_plane = preserved_surface(
        sampled_dtm, mesh_x, mesh_z, basis, runway_width_meters, blend
    )
    origin_source_z = float(config["origin"]["elevation_msl_meters"])
    local_y = np.round(authored_source_z - origin_source_z, 6)
    written_source_z = local_y + origin_source_z
    grid_vertex_count = int(mesh_x.size)
    base_triangle_count = int((len(x_axis) - 1) * (len(z_axis) - 1) * 2)
    maximum_triangles = int(config["derivative_budgets"]["maximum_total_near_mesh_triangles"])
    preferred_triangles = int(config["derivative_budgets"]["preferred_total_near_mesh_triangles"])
    if base_triangle_count > maximum_triangles:
        raise RuntimeError(
            f"Base near mesh has {base_triangle_count} triangles; budget is {maximum_triangles}"
        )

    columns = np.arange(contract.width, dtype=np.float64)
    rows = np.arange(contract.height, dtype=np.float64)
    pixel_x = -contract.half_extent + contract.pixel_size * (columns + 0.5)
    pixel_z = contract.half_extent - contract.pixel_size * (rows + 0.5)
    sample_x, sample_z = np.meshgrid(pixel_x, pixel_z)
    authored_target, target_weight, target_plane = preserved_surface(
        dtm, sample_x, sample_z, basis, runway_width_meters, blend
    )
    base_reconstructed = reconstruct_mesh(
        x_axis, z_axis, written_source_z, sample_x, sample_z
    )
    base_error = base_reconstructed - authored_target
    refinement_capacity = (maximum_triangles - base_triangle_count) // 2
    refined_cell_lookup, base_cell_max_error = select_refined_cells(
        x_axis,
        z_axis,
        sample_x,
        sample_z,
        base_error,
        refinement_capacity,
    )
    refined_count = int(np.count_nonzero(refined_cell_lookup >= 0))
    selected_flat = np.empty(refined_count, dtype=np.int64)
    flat_lookup = refined_cell_lookup.reshape(-1)
    selected_cell_ids = np.flatnonzero(flat_lookup >= 0)
    selected_flat[flat_lookup[selected_cell_ids]] = selected_cell_ids
    cell_width = len(x_axis) - 1
    refined_z_indices = selected_flat // cell_width
    refined_x_indices = selected_flat % cell_width
    refined_center_x = (
        x_axis[refined_x_indices] + x_axis[refined_x_indices + 1]
    ) * 0.5
    refined_center_z = (
        z_axis[refined_z_indices] + z_axis[refined_z_indices + 1]
    ) * 0.5
    refined_center_dtm = sample_north_up_grid(
        dtm, contract, refined_center_x, refined_center_z
    )
    refined_center_authored_z, refined_center_weight, _ = preserved_surface(
        refined_center_dtm,
        refined_center_x,
        refined_center_z,
        basis,
        runway_width_meters,
        blend,
    )
    refined_center_local_y = np.round(refined_center_authored_z - origin_source_z, 6)
    refined_center_written_z = refined_center_local_y + origin_source_z
    vertex_count = grid_vertex_count + refined_count
    triangle_count = base_triangle_count + refined_count * 2
    reconstructed = reconstruct_mesh(
        x_axis,
        z_axis,
        written_source_z,
        sample_x,
        sample_z,
        refined_cell_lookup,
        refined_center_written_z,
    )
    target_error = reconstructed - authored_target
    dtm_error = reconstructed - dtm
    source_variation, sample_cell_flat = cell_source_variation(
        x_axis,
        z_axis,
        written_source_z,
        sample_x,
        sample_z,
        authored_target,
    )
    outlier_variation_threshold = 1.5
    declared_source_variation_cells = source_variation > outlier_variation_threshold
    declared_outlier_mask = declared_source_variation_cells.reshape(-1)[
        sample_cell_flat.reshape(-1)
    ].reshape(authored_target.shape)
    remaining_mask = ~declared_outlier_mask
    remaining_statistics = error_statistics(target_error, remaining_mask)
    integration_thresholds = {
        "maximum_absolute_meters": 1.5,
        "mean_absolute_meters": 0.15,
        "p95_absolute_meters": 0.5,
    }
    geometric_integration_eligible = bool(
        remaining_statistics["maximum_absolute_meters"]
        <= integration_thresholds["maximum_absolute_meters"]
        and remaining_statistics["mean_absolute_meters"]
        <= integration_thresholds["mean_absolute_meters"]
        and remaining_statistics["p95_absolute_meters"]
        <= integration_thresholds["p95_absolute_meters"]
    )

    mesh_path = output_root / MESH_NAME
    runway_inputs_path = output_root / RUNWAY_INPUTS_NAME
    report_path = output_root / ERROR_REPORT_NAME
    heatmap_path = qa_output / HEATMAP_NAME
    write_obj(
        mesh_path,
        x_axis,
        z_axis,
        local_y,
        raster_contract_sha,
        refined_cell_lookup,
        refined_center_x,
        refined_center_z,
        refined_center_local_y,
    )

    runway_inputs = {
        "blend": {
            **blend,
            "method": "separable smoothstep of lateral distance and distance beyond runway endpoints",
        },
        "coordinate_frame": "KBDU local ENU meters; +X east, +Y up, +Z north",
        "label": config["label"],
        "plane": {
            "endpoint_0_source_z_meters": basis["z0"],
            "endpoint_1_source_z_meters": basis["z1"],
            "formula": "z0 + along_meters * (z1 - z0) / endpoint_distance_meters",
            "local_y_formula": "source_z_meters - origin_source_z_meters",
            "origin_source_z_meters": origin_source_z,
        },
        "primary_runway": {
            "endpoint_distance_meters": basis["length"],
            "endpoints": runway["endpoints"],
            "polygon_xz_meters": runway["polygon_xz_meters"],
            "runway_id": runway["runway_id"],
            "width_feet": runway["width_feet"],
            "width_meters": runway_width_meters,
        },
        "raster_contract_sha256": raster_contract_sha,
        "reference_input": file_record(
            reference_path, reference_path.relative_to(repository_root()).as_posix()
        ),
        "schema": "questflight.aerial-scenery-v1.runway-plane-inputs.v1",
        "source_accuracy": {
            "operational_0_05_meter_accuracy_supported": False,
            "reason": (
                "EPT/STAC omit vertical CRS and achieved accuracy metrics. The associated WESM row "
                "identifies original-workunit EPSG:5703/QL 1, but no surveyed runway truth/error study "
                "was applied to this derivative."
            ),
        },
    }
    write_json(runway_inputs_path, runway_inputs)

    along, lateral = runway_coordinates(sample_x, sample_z, basis)
    core_mask = (
        (np.abs(sample_x) <= float(mesh_config["operational_core_half_extent_east_meters"]))
        & (np.abs(sample_z) <= float(mesh_config["operational_core_half_extent_north_meters"]))
    )
    runway_mask = (
        (along >= 0.0)
        & (along <= basis["length"])
        & (np.abs(lateral) <= runway_width_meters * 0.5)
    )
    normal_record = next(
        asset for asset in raster_assets if asset["semantic"] == "terrain_surface_normal_and_validity"
    )
    validity = np.asarray(Image.open(repository_root() / normal_record["path"]))[:, :, 3]
    direct_ground_mask = (validity & 2) != 0
    write_error_heatmap(heatmap_path, np.abs(target_error))
    heatmap_record = file_record(
        heatmap_path, f"external-artifact/lidar/qa/{HEATMAP_NAME}"
    )
    heatmap_record.update(
        {
            "dimensions": {"height_pixels": contract.height, "width_pixels": contract.width},
            "encoding": {
                "container": "PNG",
                "error_stops_meters": [0.0, 0.01, 0.05, 0.25, 1.0],
                "semantic": "absolute_mesh_vs_authored_target_error",
            },
            "external_only": True,
        }
    )
    runway_plane_error = reconstructed - target_plane
    report = {
        "authored_surface": {
            "method": "quantized 4 m DTM sampled at zone-adaptive grid vertices, with a deterministic runway-plane blend",
            "runway_blend_vertex_fraction": float(np.mean(mesh_blend_weight > 0.0)),
            "runway_exact_plane_vertex_fraction": float(np.mean(mesh_blend_weight == 1.0)),
        },
        "border_lock": {
            "exact_4096_meter_square": bool(
                x_axis[0] == -2048.0
                and x_axis[-1] == 2048.0
                and z_axis[0] == -2048.0
                and z_axis[-1] == 2048.0
            ),
            "local_bounds_meters": [
                float(x_axis[0]),
                float(z_axis[0]),
                float(x_axis[-1]),
                float(z_axis[-1]),
            ],
        },
        "comparison": {
            "mesh_vs_authored_target": {
                "declared_outlier_pixels": error_statistics(
                    target_error, declared_outlier_mask
                ),
                "direct_ground_supported_pixels": error_statistics(target_error, direct_ground_mask),
                "global": error_statistics(target_error),
                "operational_core": error_statistics(target_error, core_mask),
                "remaining_after_declared_outliers": remaining_statistics,
                "runway_pavement": error_statistics(target_error, runway_mask),
            },
            "mesh_vs_quantized_dtm_before_runway_preservation": {
                "global": error_statistics(dtm_error),
                "operational_core": error_statistics(dtm_error, core_mask),
                "runway_pavement": error_statistics(dtm_error, runway_mask),
            },
            "runway_mesh_vs_input_plane": error_statistics(runway_plane_error, runway_mask),
        },
        "evaluation": {
            "sample_coordinates": "all 1024 x 1024 lidar DTM pixel centers",
            "sample_count": int(contract.width * contract.height),
            "triangle_interpolation": (
                "Exact OBJ triangles: unrefined cells use two triangles; selected cells use four "
                "corner-to-center triangles."
            ),
        },
        "geometric_integration_eligibility": {
            "declared_outliers": {
                "criterion": (
                    "Mesh cells whose authored corner-plus-pixel source-Z range exceeds 1.5 m; "
                    "these declared high-relief/discontinuity cells remain reported in global metrics."
                ),
                "pixel_count": int(np.count_nonzero(declared_outlier_mask)),
                "pixel_fraction": float(np.mean(declared_outlier_mask)),
                "source_variation_threshold_meters": outlier_variation_threshold,
                "cell_count": int(
                    np.count_nonzero(declared_source_variation_cells)
                ),
            },
            "observed_outside_declared_outliers": remaining_statistics,
            "status": (
                "ELIGIBLE"
                if geometric_integration_eligible
                else "COMPLETE_BUT_NOT_INTEGRATION_ELIGIBLE"
            ),
            "thresholds": integration_thresholds,
        },
        "label": config["label"],
        "mesh": {
            "adaptive_refinement": {
                "added_center_vertices": refined_count,
                "base_triangle_count": base_triangle_count,
                "final_triangle_count": triangle_count,
                "selection_metric": (
                    "maximum absolute base-mesh error at all DTM pixel centers in each cell; "
                    "descending with cell-index tie break"
                ),
                "triangle_delta": triangle_count - base_triangle_count,
                "worst_selected_base_cell_error_meters": float(
                    np.max(base_cell_max_error[selected_flat])
                ),
            },
            "axis": {
                "x_count": int(len(x_axis)),
                "x_max_spacing_meters": float(np.max(np.diff(x_axis))),
                "x_min_spacing_meters": float(np.min(np.diff(x_axis))),
                "z_count": int(len(z_axis)),
                "z_max_spacing_meters": float(np.max(np.diff(z_axis))),
                "z_min_spacing_meters": float(np.min(np.diff(z_axis))),
            },
            "preferred_triangle_budget": preferred_triangles,
            "preferred_triangle_budget_met": triangle_count <= preferred_triangles,
            "triangle_count": triangle_count,
            "vertex_count": vertex_count,
        },
        "operational_0_05_meter_tolerance": {
            "geometric_reconstruction_all_core_samples_within_tolerance": bool(
                np.max(np.abs(target_error[core_mask])) <= 0.05
            ),
            "real_world_accuracy_supported": False,
            "reason": (
                "Mesh-to-raster comparison is not real-world evidence; EPT vertical CRS, source accuracy, "
                "runway condition/currentness, and surveyed scenario truth are unavailable."
            ),
        },
        "qa_heatmap": heatmap_record,
        "raster_contract_sha256": raster_contract_sha,
        "runway_inputs": file_record(
            runway_inputs_path, f"{LOGICAL_ASSET_DIR}/{RUNWAY_INPUTS_NAME}"
        ),
        "schema": "questflight.aerial-scenery-v1.near-terrain-error.v1",
    }
    write_json(report_path, report)

    near_assets = [
        asset_record(
            mesh_path,
            "fixed_near_terrain_mesh",
            {"triangle_count": triangle_count, "vertex_count": vertex_count},
            {
                "container": "Wavefront OBJ",
                "coordinates": "Unity local meters; +X east, +Y up, +Z north",
                "height_source": "DTM plus documented runway-plane preservation blend",
                "index_base": 1,
            },
        ),
        asset_record(
            runway_inputs_path,
            "runway_plane_preservation_inputs",
            {"runway_count": 1},
            {"container": "canonical UTF-8 JSON", "schema_version": 1},
        ),
        asset_record(
            report_path,
            "near_terrain_error_report",
            {"sample_count": int(contract.width * contract.height)},
            {"container": "canonical UTF-8 JSON", "schema_version": 1},
        ),
    ]
    manifest["assets"] = raster_assets + near_assets
    manifest["near_terrain"] = {
        "error_report_semantic": "near_terrain_error_report",
        "fixed_mesh_semantic": "fixed_near_terrain_mesh",
        "geometric_integration_eligible": geometric_integration_eligible,
        "geometric_integration_status": (
            "ELIGIBLE"
            if geometric_integration_eligible
            else "COMPLETE_BUT_NOT_INTEGRATION_ELIGIBLE"
        ),
        "operational_0_05_meter_real_world_accuracy_supported": False,
        "preferred_triangle_budget_met": triangle_count <= preferred_triangles,
        "runway_inputs_semantic": "runway_plane_preservation_inputs",
    }
    manifest["source_pipeline"] = source_pipeline_records()
    manifest["statistics"]["near_terrain_product_bytes"] = sum(
        int(asset["bytes"]) for asset in near_assets
    )
    manifest["statistics"]["total_product_bytes"] = sum(
        int(asset["bytes"]) for asset in manifest["assets"]
    )
    maximum_product_bytes = int(config["derivative_budgets"]["maximum_product_bytes"])
    if manifest["statistics"]["total_product_bytes"] > maximum_product_bytes:
        raise RuntimeError(
            f"Lidar products use {manifest['statistics']['total_product_bytes']} bytes; "
            f"budget is {maximum_product_bytes}"
        )
    write_json(manifest_path, manifest)
    if write_unity_meta:
        note = "USGS 3DEP public-domain bounded derivative / not for navigation"
        write_model_meta(mesh_path, f"{LOGICAL_ASSET_DIR}/{MESH_NAME}", note)
        write_text_meta(
            runway_inputs_path,
            f"{LOGICAL_ASSET_DIR}/{RUNWAY_INPUTS_NAME}",
            "Pinned runway plane preservation inputs / not for navigation",
        )
        write_text_meta(
            report_path,
            f"{LOGICAL_ASSET_DIR}/{ERROR_REPORT_NAME}",
            "Near-terrain geometric comparison; not real-world accuracy evidence",
        )
    return manifest


def parse_args() -> argparse.Namespace:
    root = repository_root()
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, default=default_config_path())
    parser.add_argument(
        "--manifest",
        type=Path,
        default=root / LOGICAL_ASSET_DIR / MANIFEST_NAME,
    )
    parser.add_argument(
        "--reference-context",
        type=Path,
        default=root
        / "QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_reference_context.json",
    )
    parser.add_argument("--qa-output", type=Path, required=True)
    parser.add_argument("--write-unity-meta", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    manifest = build(
        args.config.resolve(),
        args.manifest.resolve(),
        args.reference_context.resolve(),
        args.qa_output.resolve(),
        args.write_unity_meta,
    )
    mesh = next(asset for asset in manifest["assets"] if asset["semantic"] == "fixed_near_terrain_mesh")
    print(
        json.dumps(
            {
                "mesh_bytes": mesh["bytes"],
                "preferred_triangle_budget_met": manifest["near_terrain"][
                    "preferred_triangle_budget_met"
                ],
                "status": "BUILT",
                "total_product_bytes": manifest["statistics"]["total_product_bytes"],
                "triangle_count": mesh["dimensions"]["triangle_count"],
                "vertex_count": mesh["dimensions"]["vertex_count"],
            },
            indent=2,
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
