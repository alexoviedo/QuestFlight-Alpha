#!/usr/bin/env python3
"""Build the fixed product-AEQD bare-earth terrain derivative.

The pinned lidar DTM is a north-up UTM13N raster.  This builder authors every
mesh X/Z coordinate in the product AEQD frame, converts each height query to
UTM13N with the exact shared projection contract, and applies runway
preservation only in product coordinates.  It never changes the historical
AerialSceneryV1 terrain assets or any Unity hierarchy.
"""

from __future__ import annotations

import argparse
import math
import sys
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image

from build_building_surfaces import CONTEXT_PATH, write_model_meta
from build_dtm_dsm import (
    REPOSITORY_ROOT,
    RUNTIME_ROOT,
    SOURCE_MANIFEST,
    asset_by_semantic,
    canonical_json_bytes,
    file_record,
    load_json,
    sha256_bytes,
    verify_source_asset,
    write_folder_meta,
    write_json,
    write_text_meta,
)
from geospatial_frame import (
    horizontal_frame_contract,
    product_aeqd_to_utm13n_delta,
)


SCENERY_LIDAR_TOOLS = REPOSITORY_ROOT / "tools/scenery/lidar"
if str(SCENERY_LIDAR_TOOLS) not in sys.path:
    sys.path.insert(0, str(SCENERY_LIDAR_TOOLS))

from build_near_terrain import (  # noqa: E402
    adaptive_axis,
    cell_source_variation,
    error_statistics,
    find_primary_runway,
    forced_breakline_coordinates,
    preserved_surface,
    reconstruct_mesh,
    runway_basis,
    runway_coordinates,
    select_refined_cells,
)
from common import compute_grid_contract, load_config, sample_north_up_grid  # noqa: E402


SCENERY_CONFIG = SCENERY_LIDAR_TOOLS / "config.json"
OUTPUT_DIR = RUNTIME_ROOT / "Terrain"
MESH_NAME = "KBDU_ProductAeqd_BareEarth_Fixed.obj"
MANIFEST_NAME = "KBDU_ProductAeqd_BareEarth_Manifest.json"
REPORT_NAME = "KBDU_ProductAeqd_BareEarth_ErrorReport.json"
MESH_OBJECT_NAME = "KBDU_ProductAeqd_BareEarth_Fixed"
SCHEMA = "questflight.photoreal-aerial-surface-v1.product-aeqd-bare-earth.v1"
REPORT_SCHEMA = "questflight.photoreal-aerial-surface-v1.product-aeqd-bare-earth-error.v1"
MAXIMUM_TRIANGLES = 92_000
OUTLIER_VARIATION_THRESHOLD_METERS = 1.5


def sample_product_dtm(
    dtm: np.ndarray,
    grid_contract: Any,
    mapping: dict[str, Any],
    product_x: Any,
    product_z: Any,
) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    """Sample the UTM raster at exact product-AEQD coordinates."""

    x_utm, z_utm = product_aeqd_to_utm13n_delta(product_x, product_z, mapping)
    x_utm = np.asarray(x_utm, dtype=np.float64)
    z_utm = np.asarray(z_utm, dtype=np.float64)
    sampled = sample_north_up_grid(dtm, grid_contract, x_utm, z_utm)
    affine = mapping["pixel_center_affine_local_meters"]
    x_first = float(affine["x_at_column0_row0"])
    x_last = x_first + (dtm.shape[1] - 1) * float(affine["x_per_column"])
    z_first = float(affine["z_at_column0_row0"])
    z_last = z_first + (dtm.shape[0] - 1) * float(affine["z_per_row"])
    clamped = (
        (x_utm < min(x_first, x_last))
        | (x_utm > max(x_first, x_last))
        | (z_utm < min(z_first, z_last))
        | (z_utm > max(z_first, z_last))
    )
    return np.asarray(sampled), x_utm, z_utm, clamped


def _error_thresholds() -> dict[str, float]:
    return {
        "maximum_absolute_meters": 1.5,
        "mean_absolute_meters": 0.15,
        "p95_absolute_meters": 0.5,
    }


def _anchor_indices(
    x_axis: np.ndarray,
    z_axis: np.ndarray,
    basis: dict[str, Any],
) -> list[tuple[int, int]]:
    candidates = [
        (0, 0),
        (0, len(x_axis) - 1),
        (len(z_axis) - 1, 0),
        (len(z_axis) - 1, len(x_axis) - 1),
        (
            int(np.argmin(np.abs(z_axis))),
            int(np.argmin(np.abs(x_axis))),
        ),
        (
            int(np.argmin(np.abs(z_axis - basis["p0"][1]))),
            int(np.argmin(np.abs(x_axis - basis["p0"][0]))),
        ),
        (
            int(np.argmin(np.abs(z_axis - basis["p1"][1]))),
            int(np.argmin(np.abs(x_axis - basis["p1"][0]))),
        ),
    ]
    return list(dict.fromkeys(candidates))


def derive(
    source_manifest_path: Path = SOURCE_MANIFEST,
    context_path: Path = CONTEXT_PATH,
    scenery_config_path: Path = SCENERY_CONFIG,
) -> dict[str, Any]:
    """Compute deterministic terrain topology, heights, and evidence in memory."""

    source_manifest_path = source_manifest_path.resolve()
    context_path = context_path.resolve()
    scenery_config_path = scenery_config_path.resolve()
    source = load_json(source_manifest_path)
    mapping = source["coordinate_mapping"]
    frame_contract = horizontal_frame_contract(mapping)
    config = load_config(scenery_config_path)
    grid_contract = compute_grid_contract(config)
    grid = mapping["grid"]
    if (
        int(grid["width_pixels"]) != grid_contract.width
        or int(grid["height_pixels"]) != grid_contract.height
        or not math.isclose(
            float(grid["pixel_size_meters"]), grid_contract.pixel_size, abs_tol=1e-12
        )
    ):
        raise ValueError("Pinned lidar manifest and scenery grid contract disagree")
    origin = mapping["origin"]
    if not (
        math.isclose(
            float(origin["utm13n_easting_meters"]),
            grid_contract.origin_easting,
            abs_tol=1e-6,
        )
        and math.isclose(
            float(origin["utm13n_northing_meters"]),
            grid_contract.origin_northing,
            abs_tol=1e-6,
        )
    ):
        raise ValueError("Pinned lidar origins disagree")

    dtm_record = asset_by_semantic(source, "digital_terrain_model")
    historical_terrain_record = asset_by_semantic(source, "fixed_near_terrain_mesh")
    dtm_path = verify_source_asset(dtm_record)
    verify_source_asset(historical_terrain_record)
    dtm_codes = np.asarray(Image.open(dtm_path))
    if (
        dtm_codes.shape != (grid_contract.height, grid_contract.width)
        or dtm_codes.dtype != np.uint16
        or np.any(dtm_codes == 0)
    ):
        raise ValueError("Product terrain requires the filled pinned R16 DTM")
    decode = dtm_record["encoding"]["decoded_source_z_meters"]
    dtm = (
        dtm_codes.astype(np.float64) * float(decode["scale"])
        + float(decode["offset"])
    )

    reference = load_json(context_path)
    mesh_config = config["near_mesh"]
    runway = find_primary_runway(reference, str(mesh_config["primary_runway_id"]))
    basis = runway_basis(runway)
    blend = mesh_config["runway_blend"]
    forced_x, forced_z = forced_breakline_coordinates(runway, basis, blend)
    half_extent = float(grid_contract.half_extent)
    x_axis = adaptive_axis(
        half_extent,
        float(mesh_config["operational_core_half_extent_east_meters"]),
        float(mesh_config["outer_spacing_meters"]),
        float(mesh_config["operational_core_spacing_meters"]),
        forced_x,
    )
    z_axis = adaptive_axis(
        half_extent,
        float(mesh_config["operational_core_half_extent_north_meters"]),
        float(mesh_config["outer_spacing_meters"]),
        float(mesh_config["operational_core_spacing_meters"]),
        forced_z,
    )
    mesh_x, mesh_z = np.meshgrid(x_axis, z_axis)
    sampled_dtm, mesh_x_utm, mesh_z_utm, mesh_clamped = sample_product_dtm(
        dtm, grid_contract, mapping, mesh_x, mesh_z
    )
    runway_width_meters = float(runway["width_feet"]) * 0.3048
    authored_source_z, mesh_blend_weight, _mesh_plane = preserved_surface(
        sampled_dtm, mesh_x, mesh_z, basis, runway_width_meters, blend
    )
    origin_source_z = float(origin["source_elevation_msl_meters"])
    local_y = np.round(authored_source_z - origin_source_z, 6)
    written_source_z = local_y + origin_source_z

    base_triangle_count = int((len(x_axis) - 1) * (len(z_axis) - 1) * 2)
    maximum_triangles = int(
        config["derivative_budgets"]["maximum_total_near_mesh_triangles"]
    )
    if maximum_triangles != MAXIMUM_TRIANGLES or base_triangle_count > maximum_triangles:
        raise ValueError("Product terrain topology budget changed")

    product_pixel_axis = (
        -half_extent
        + grid_contract.pixel_size
        * (np.arange(grid_contract.width, dtype=np.float64) + 0.5)
    )
    sample_x, sample_z = np.meshgrid(product_pixel_axis, product_pixel_axis)
    target_dtm, sample_x_utm, sample_z_utm, sample_clamped = sample_product_dtm(
        dtm, grid_contract, mapping, sample_x, sample_z
    )
    authored_target, _target_weight, target_plane = preserved_surface(
        target_dtm, sample_x, sample_z, basis, runway_width_meters, blend
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
    flat_lookup = refined_cell_lookup.reshape(-1)
    selected_cell_ids = np.flatnonzero(flat_lookup >= 0)
    selected_flat = np.empty(refined_count, dtype=np.int64)
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
    refined_center_dtm, _center_x_utm, _center_z_utm, center_clamped = (
        sample_product_dtm(
            dtm,
            grid_contract,
            mapping,
            refined_center_x,
            refined_center_z,
        )
    )
    refined_center_authored_z, _refined_weight, _refined_plane = preserved_surface(
        refined_center_dtm,
        refined_center_x,
        refined_center_z,
        basis,
        runway_width_meters,
        blend,
    )
    refined_center_local_y = np.round(
        refined_center_authored_z - origin_source_z, 6
    )
    refined_center_written_z = refined_center_local_y + origin_source_z
    triangle_count = base_triangle_count + refined_count * 2
    vertex_count = int(mesh_x.size + refined_count)
    if triangle_count != MAXIMUM_TRIANGLES:
        raise ValueError("Fixed product terrain must consume the exact 92k triangle budget")

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
    dtm_error = reconstructed - target_dtm
    source_variation, sample_cell_flat = cell_source_variation(
        x_axis,
        z_axis,
        written_source_z,
        sample_x,
        sample_z,
        authored_target,
    )
    declared_source_variation_cells = (
        source_variation > OUTLIER_VARIATION_THRESHOLD_METERS
    )
    declared_outlier_mask = declared_source_variation_cells.reshape(-1)[
        sample_cell_flat.reshape(-1)
    ].reshape(authored_target.shape)
    remaining_statistics = error_statistics(target_error, ~declared_outlier_mask)
    thresholds = _error_thresholds()
    geometric_integration_eligible = bool(
        remaining_statistics["maximum_absolute_meters"]
        <= thresholds["maximum_absolute_meters"]
        and remaining_statistics["mean_absolute_meters"]
        <= thresholds["mean_absolute_meters"]
        and remaining_statistics["p95_absolute_meters"]
        <= thresholds["p95_absolute_meters"]
    )

    along, lateral = runway_coordinates(sample_x, sample_z, basis)
    core_mask = (
        np.abs(sample_x)
        <= float(mesh_config["operational_core_half_extent_east_meters"])
    ) & (
        np.abs(sample_z)
        <= float(mesh_config["operational_core_half_extent_north_meters"])
    )
    runway_mask = (
        (along >= 0.0)
        & (along <= basis["length"])
        & (np.abs(lateral) <= runway_width_meters * 0.5)
    )
    runway_plane_error = reconstructed - target_plane

    anchor_records = []
    width = len(x_axis)
    for z_index, x_index in _anchor_indices(x_axis, z_axis, basis):
        anchor_records.append(
            {
                "authored_local_y_meters": float(local_y[z_index, x_index]),
                "product_x_meters": float(x_axis[x_index]),
                "product_z_meters": float(z_axis[z_index]),
                "runway_blend_weight": float(mesh_blend_weight[z_index, x_index]),
                "source_dtm_msl_meters": float(sampled_dtm[z_index, x_index]),
                "utm_x_delta_meters": float(mesh_x_utm[z_index, x_index]),
                "utm_z_delta_meters": float(mesh_z_utm[z_index, x_index]),
                "vertex_index_zero_based": int(z_index * width + x_index),
            }
        )

    source_contract = {
        "coordinate_mapping": mapping,
        "digital_terrain_model": dtm_record,
        "horizontal_frame_contract": frame_contract,
    }
    source_contract_sha = sha256_bytes(canonical_json_bytes(source_contract))
    report = {
        "authored_surface": {
            "height_sampling": "exact product AEQD to origin-relative UTM13N followed by bilinear DTM sampling",
            "runway_blend_coordinate_frame": "product AEQD",
            "runway_blend_vertex_fraction": float(np.mean(mesh_blend_weight > 0.0)),
            "runway_exact_plane_vertex_fraction": float(np.mean(mesh_blend_weight == 1.0)),
        },
        "border_lock": {
            "exact_4096_meter_product_square": bool(
                x_axis[0] == -2048.0
                and x_axis[-1] == 2048.0
                and z_axis[0] == -2048.0
                and z_axis[-1] == 2048.0
            ),
            "product_bounds_meters": [
                float(x_axis[0]),
                float(z_axis[0]),
                float(x_axis[-1]),
                float(z_axis[-1]),
            ],
        },
        "comparison": {
            "mesh_vs_authored_target": {
                "global": error_statistics(target_error),
                "operational_core": error_statistics(target_error, core_mask),
                "remaining_after_declared_outliers": remaining_statistics,
                "runway_pavement": error_statistics(target_error, runway_mask),
            },
            "mesh_vs_sampled_bare_earth_before_runway_preservation": {
                "global": error_statistics(dtm_error),
                "operational_core": error_statistics(dtm_error, core_mask),
                "runway_pavement": error_statistics(dtm_error, runway_mask),
            },
            "runway_mesh_vs_input_plane": error_statistics(
                runway_plane_error, runway_mask
            ),
        },
        "evaluation": {
            "product_sample_grid": "1024 x 1024 regular AEQD pixel centres over [-2048,2048]^2",
            "sample_count": int(sample_x.size),
            "triangle_interpolation": "Exact OBJ triangles including deterministic corner-to-center refinement",
        },
        "geometric_integration_eligibility": {
            "declared_outliers": {
                "cell_count": int(np.count_nonzero(declared_source_variation_cells)),
                "criterion": "authored corner-plus-product-sample source-Z range exceeds 1.5 m",
                "pixel_count": int(np.count_nonzero(declared_outlier_mask)),
                "pixel_fraction": float(np.mean(declared_outlier_mask)),
                "source_variation_threshold_meters": OUTLIER_VARIATION_THRESHOLD_METERS,
            },
            "observed_outside_declared_outliers": remaining_statistics,
            "status": (
                "ELIGIBLE"
                if geometric_integration_eligible
                else "COMPLETE_BUT_NOT_INTEGRATION_ELIGIBLE"
            ),
            "thresholds": thresholds,
        },
        "height_sampling": {
            "mesh_grid_clamped_to_lidar_pixel_center_domain_count": int(
                np.count_nonzero(mesh_clamped)
            ),
            "product_evaluation_clamped_to_lidar_pixel_center_domain_count": int(
                np.count_nonzero(sample_clamped)
            ),
            "refined_centers_clamped_to_lidar_pixel_center_domain_count": int(
                np.count_nonzero(center_clamped)
            ),
            "source_raster_frame": "origin-relative UTM13N deltas",
            "target_geometry_frame": "product AEQD",
        },
        "horizontal_frame_contract": frame_contract,
        "mesh": {
            "adaptive_refinement": {
                "added_center_vertices": refined_count,
                "base_triangle_count": base_triangle_count,
                "final_triangle_count": triangle_count,
                "selection_metric": "maximum absolute base-mesh error at product sample centres; descending with cell-index tie break",
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
            "triangle_count": triangle_count,
            "vertex_count": vertex_count,
        },
        "operational_0_05_meter_tolerance": {
            "geometric_reconstruction_all_core_samples_within_tolerance": bool(
                np.max(np.abs(target_error[core_mask])) <= 0.05
            ),
            "real_world_accuracy_supported": False,
            "reason": "Mesh-to-raster reconstruction is not surveyed real-world accuracy evidence.",
        },
        "runway_preservation": {
            "blend": blend,
            "coordinate_frame": "product AEQD",
            "endpoint_distance_meters": float(basis["length"]),
            "primary_runway_id": str(runway["runway_id"]),
            "width_meters": runway_width_meters,
        },
        "schema": REPORT_SCHEMA,
        "source_contract_sha256": source_contract_sha,
        "verification_anchors": anchor_records,
    }
    return {
        "context_path": context_path,
        "frame_contract": frame_contract,
        "historical_terrain_record": historical_terrain_record,
        "local_y": local_y,
        "mapping": mapping,
        "refined_cell_lookup": refined_cell_lookup,
        "refined_center_local_y": refined_center_local_y,
        "refined_center_x": refined_center_x,
        "refined_center_z": refined_center_z,
        "report": report,
        "scenery_config_path": scenery_config_path,
        "source": source,
        "source_contract_sha256": source_contract_sha,
        "source_manifest_path": source_manifest_path,
        "triangle_count": triangle_count,
        "vertex_count": vertex_count,
        "x_axis": x_axis,
        "z_axis": z_axis,
    }


def obj_text(derived: dict[str, Any]) -> str:
    x_axis = derived["x_axis"]
    z_axis = derived["z_axis"]
    local_y = derived["local_y"]
    refined_cell_lookup = derived["refined_cell_lookup"]
    refined_center_x = derived["refined_center_x"]
    refined_center_z = derived["refined_center_z"]
    refined_center_local_y = derived["refined_center_local_y"]
    grid_vertex_count = len(x_axis) * len(z_axis)
    lines = [
        "# QuestFlight Photoreal Aerial Surface V1 product-AEQD bare-earth derivative",
        "# Not for navigation; no surveyed vertical-accuracy claim",
        f"# source_contract_sha256 {derived['source_contract_sha256']}",
        f"# vertices {derived['vertex_count']}",
        f"# triangles {derived['triangle_count']}",
        "# coordinates product AEQD metres: +X east, +Y up, +Z north",
        f"o {MESH_OBJECT_NAME}",
        f"g {MESH_OBJECT_NAME}",
        "s off",
    ]
    for z_index, z in enumerate(z_axis):
        for x_index, x in enumerate(x_axis):
            lines.append(f"v {x:.6f} {local_y[z_index, x_index]:.6f} {z:.6f}")
    for x, z, y in zip(
        refined_center_x, refined_center_z, refined_center_local_y
    ):
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
                lines.extend(
                    (
                        f"f {southwest} {northwest} {center}",
                        f"f {northwest} {northeast} {center}",
                        f"f {northeast} {southeast} {center}",
                        f"f {southeast} {southwest} {center}",
                    )
                )
            else:
                lines.append(f"f {southwest} {northwest} {southeast}")
                lines.append(f"f {southeast} {northwest} {northeast}")
    return "\n".join(lines) + "\n"


def build(
    source_manifest_path: Path = SOURCE_MANIFEST,
    context_path: Path = CONTEXT_PATH,
    scenery_config_path: Path = SCENERY_CONFIG,
    output_dir: Path = OUTPUT_DIR,
    write_meta: bool = True,
) -> dict[str, Any]:
    output_dir = output_dir.resolve()
    derived = derive(source_manifest_path, context_path, scenery_config_path)
    output_dir.mkdir(parents=True, exist_ok=True)
    mesh_path = output_dir / MESH_NAME
    report_path = output_dir / REPORT_NAME
    manifest_path = output_dir / MANIFEST_NAME
    mesh_path.write_text(obj_text(derived), encoding="utf-8", newline="\n")
    write_json(report_path, derived["report"])
    manifest = {
        "assets": [
            {
                **file_record(mesh_path, "product_aeqd_bare_earth_fixed_mesh"),
                "dimensions": {
                    "triangle_count": int(derived["triangle_count"]),
                    "vertex_count": int(derived["vertex_count"]),
                },
                "encoding": {
                    "container": "Wavefront OBJ",
                    "coordinates": "product AEQD metres; +X east, +Y up, +Z north",
                    "height_source": "exact AEQD-to-UTM sampled DTM plus product-coordinate runway preservation",
                    "index_base": 1,
                },
            },
            {
                **file_record(report_path, "product_aeqd_bare_earth_error_report"),
                "dimensions": {"sample_count": 1024 * 1024},
                "encoding": {
                    "container": "canonical UTF-8 JSON",
                    "schema_version": 1,
                },
            },
        ],
        "budgets": {
            "fixed_topology": True,
            "maximum_triangles": MAXIMUM_TRIANGLES,
            "triangle_count": int(derived["triangle_count"]),
            "vertex_count": int(derived["vertex_count"]),
        },
        "coordinate_mapping": {
            "geometry_horizontal_frame": "KBDU product-local WGS84 azimuthal equidistant",
            "height_raster_horizontal_frame": "origin-relative UTM13N deltas",
            "vertical_zero_msl_meters": float(
                derived["mapping"]["origin"]["source_elevation_msl_meters"]
            ),
        },
        "derivation": {
            "border_locked_product_bounds_meters": [-2048.0, -2048.0, 2048.0, 2048.0],
            "height_query": "exact product AEQD to UTM13N delta before bilinear DTM sampling",
            "historical_aerial_scenery_v1_terrain_modified": False,
            "runway_preservation_coordinate_frame": "product AEQD",
            "runtime_hierarchy_construction": False,
        },
        "horizontal_frame_contract": derived["frame_contract"],
        "license_and_provenance": derived["source"]["license_and_provenance"],
        "limitations": [
            "Not for navigation and not surveyed terrain truth.",
            "The source is the pinned 4 m quantized lidar DTM; this derivative does not restore full-density lidar detail.",
            "Border samples outside the UTM pixel-centre domain are deterministically clamped and counted in the error report.",
            "Static topology and reconstruction error are not physical Quest performance evidence.",
            "The OBJ must be consumed by coordinate-preserving native mesh authoring; no source X mirror is authored.",
            "This workstream does not integrate or construct any Unity runtime hierarchy.",
        ],
        "report": file_record(report_path),
        "schema": SCHEMA,
        "semantic_root": "ProductionTerrainSurfaceLayer",
        "sources": {
            "builder": file_record(Path(__file__).resolve()),
            "historical_fixed_near_terrain": derived["historical_terrain_record"],
            "historical_topology_builder": file_record(
                SCENERY_LIDAR_TOOLS / "build_near_terrain.py"
            ),
            "historical_topology_common": file_record(
                SCENERY_LIDAR_TOOLS / "common.py"
            ),
            "lidar_manifest": file_record(derived["source_manifest_path"]),
            "product_reference_context": file_record(derived["context_path"]),
            "scenery_grid_config": file_record(derived["scenery_config_path"]),
        },
    }
    write_json(manifest_path, manifest)
    if write_meta:
        write_folder_meta(
            output_dir,
            "Fixed product-AEQD bare-earth terrain derivative; USGS public domain; not for navigation",
        )
        write_model_meta(
            mesh_path,
            "Product-AEQD bare-earth terrain; coordinate-preserving native mesh authoring required",
        )
        write_text_meta(report_path, "Product-AEQD bare-earth terrain reconstruction evidence")
        write_text_meta(manifest_path, "Product-AEQD bare-earth terrain provenance and contract")
    return manifest


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-manifest", type=Path, default=SOURCE_MANIFEST)
    parser.add_argument("--context", type=Path, default=CONTEXT_PATH)
    parser.add_argument("--scenery-config", type=Path, default=SCENERY_CONFIG)
    parser.add_argument("--output", type=Path, default=OUTPUT_DIR)
    parser.add_argument("--no-meta", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    manifest = build(
        args.source_manifest,
        args.context,
        args.scenery_config,
        args.output,
        not args.no_meta,
    )
    print(
        {
            "manifest": (args.output / MANIFEST_NAME).as_posix(),
            "triangle_count": manifest["budgets"]["triangle_count"],
            "vertex_count": manifest["budgets"]["vertex_count"],
        }
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
