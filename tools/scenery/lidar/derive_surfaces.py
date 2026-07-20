#!/usr/bin/env python3
"""Derive compact DTM/DSM/height/normal textures from a frozen EPT snapshot."""

from __future__ import annotations

import argparse
import collections
import json
import math
from pathlib import Path
from typing import Any

import laspy
import numpy as np
from PIL import Image
from pyproj import Transformer
from scipy.ndimage import (
    binary_closing,
    binary_dilation,
    distance_transform_edt,
    gaussian_filter,
    label,
    maximum_filter,
    minimum_filter,
)

from common import (
    GridContract,
    compute_grid_contract,
    default_config_path,
    file_record,
    load_config,
    load_json,
    repository_root,
    sha256_file,
    write_folder_meta,
    write_json,
    write_text_meta,
    write_texture_meta,
)


LOGICAL_ASSET_DIR = "QuestFlight/Assets/Production/Environment/AerialSceneryV1/Lidar"
MANIFEST_NAME = "KBDU_LidarManifestV1.json"
DTM_NAME = "KBDU_Lidar_DTM_R16.png"
DSM_NAME = "KBDU_Lidar_DSM_R16.png"
FEATURES_NAME = "KBDU_Lidar_SurfaceFeatures_RGBA8.png"
NORMAL_NAME = "KBDU_Lidar_SurfaceNormal_RGBA8.png"
CANOPY_NORMAL_NAME = "KBDU_Lidar_CanopyNormalOcclusion_RGBA8.png"
BUILDING_CONFIDENCE_NAME = "KBDU_Lidar_BuildingConfidence_R8.png"


def verify_snapshot(snapshot_root: Path, manifest: dict[str, Any]) -> None:
    if manifest.get("schema_version") != 1:
        raise ValueError("Unsupported raw snapshot manifest schema")
    problems: list[str] = []
    for group in ("metadata_files", "hierarchy_files", "data_files"):
        for record in manifest.get(group, []):
            path = snapshot_root / record["path"]
            if not path.is_file():
                problems.append(f"missing {record['path']}")
            elif path.stat().st_size != int(record["bytes"]):
                problems.append(f"size mismatch {record['path']}")
            elif sha256_file(path) != record["sha256"]:
                problems.append(f"hash mismatch {record['path']}")
    if problems:
        raise RuntimeError("Snapshot verification failed: " + "; ".join(problems[:20]))


def source_mask(
    x: np.ndarray,
    y: np.ndarray,
    z: np.ndarray,
    bounds: tuple[float, float, float, float, float, float],
) -> np.ndarray:
    return (
        (x >= bounds[0])
        & (x <= bounds[3])
        & (y >= bounds[1])
        & (y <= bounds[4])
        & (z >= bounds[2])
        & (z <= bounds[5])
    )


def transformed_points(
    laz: laspy.LasData,
    transformer: Transformer,
    contract: GridContract,
    source_bounds: tuple[float, float, float, float, float, float],
) -> dict[str, np.ndarray]:
    x = np.asarray(laz.x)
    y = np.asarray(laz.y)
    z = np.asarray(laz.z, dtype=np.float32)
    classification = np.asarray(laz.classification, dtype=np.uint8)
    prefilter = source_mask(x, y, z, source_bounds)
    if not np.any(prefilter):
        return {}
    x = x[prefilter]
    y = y[prefilter]
    z = z[prefilter]
    classification = classification[prefilter]
    easting, northing = transformer.transform(x, y)
    inside, rows, columns = contract.flat_indices(easting, northing)
    if not np.any(inside):
        return {}
    extra_names = set(laz.point_format.extra_dimension_names)
    if "ClassFlags" in extra_names:
        class_flags = np.asarray(laz["ClassFlags"], dtype=np.uint8)[prefilter][inside]
    else:
        class_flags = np.zeros(rows.shape, dtype=np.uint8)
    return {
        "class_flags": class_flags,
        "classification": classification[inside],
        "flat_index": (rows * contract.width + columns).astype(np.int64),
        "number_of_returns": np.asarray(laz.number_of_returns, dtype=np.uint8)[prefilter][inside],
        "return_number": np.asarray(laz.return_number, dtype=np.uint8)[prefilter][inside],
        "z": z[inside],
    }


def direct_surfaces(
    config: dict[str, Any],
    contract: GridContract,
    snapshot_root: Path,
    snapshot: dict[str, Any],
) -> tuple[dict[str, np.ndarray], dict[str, Any]]:
    classification_config = config["classification"]
    ground_class = int(classification_config["ground_class"])
    vegetation_classes = np.asarray(classification_config["vegetation_classes"], dtype=np.uint8)
    building_class = int(classification_config["building_class"])
    noise_classes = np.asarray(classification_config["noise_classes"], dtype=np.uint8)
    withheld_mask = int(classification_config["withheld_class_flag_mask"])
    source_epsg = int(config["product_grid"]["source_crs_epsg"])
    product_epsg = int(config["product_grid"]["product_crs_epsg"])
    transformer = Transformer.from_crs(source_epsg, product_epsg, always_xy=True)
    source_bounds = tuple(float(value) for value in snapshot["selection"]["ept_3857_query_bounds_xyz"])
    cell_count = contract.width * contract.height
    ground_sum = np.zeros(cell_count, dtype=np.float64)
    ground_count = np.zeros(cell_count, dtype=np.uint32)
    dsm = np.full(cell_count, -np.inf, dtype=np.float32)
    all_count = np.zeros(cell_count, dtype=np.uint32)
    vegetation_max = np.full(cell_count, -np.inf, dtype=np.float32)
    vegetation_count = np.zeros(cell_count, dtype=np.uint32)
    building_max = np.full(cell_count, -np.inf, dtype=np.float32)
    building_count = np.zeros(cell_count, dtype=np.uint32)
    class_counts: collections.Counter[int] = collections.Counter()
    decoded_points = 0
    inside_points = 0
    valid_points = 0

    for file_index, record in enumerate(snapshot["data_files"], 1):
        laz = laspy.read(snapshot_root / record["path"])
        if len(laz.points) != int(record["declared_points"]):
            raise RuntimeError(
                f"{record['path']} decoded {len(laz.points)} points, expected {record['declared_points']}"
            )
        decoded_points += len(laz.points)
        points = transformed_points(laz, transformer, contract, source_bounds)
        if not points:
            continue
        index = points["flat_index"]
        z = points["z"]
        point_class = points["classification"]
        class_flags = points["class_flags"]
        inside_points += len(index)
        unique, counts = np.unique(point_class, return_counts=True)
        class_counts.update({int(key): int(value) for key, value in zip(unique, counts)})
        valid = (~np.isin(point_class, noise_classes)) & ((class_flags & withheld_mask) == 0)
        if np.any(valid):
            valid_index = index[valid]
            valid_z = z[valid]
            valid_points += len(valid_index)
            np.add.at(all_count, valid_index, 1)
            np.maximum.at(dsm, valid_index, valid_z)
        ground = valid & (point_class == ground_class)
        if np.any(ground):
            ground_index = index[ground]
            np.add.at(ground_sum, ground_index, z[ground])
            np.add.at(ground_count, ground_index, 1)
        vegetation = valid & np.isin(point_class, vegetation_classes)
        if np.any(vegetation):
            vegetation_index = index[vegetation]
            np.add.at(vegetation_count, vegetation_index, 1)
            np.maximum.at(vegetation_max, vegetation_index, z[vegetation])
        building = valid & (point_class == building_class)
        if np.any(building):
            building_index = index[building]
            np.add.at(building_count, building_index, 1)
            np.maximum.at(building_max, building_index, z[building])
        if file_index % 200 == 0 or file_index == len(snapshot["data_files"]):
            print(
                f"surface pass {file_index}/{len(snapshot['data_files'])}: "
                f"decoded={decoded_points} inside={inside_points}",
                flush=True,
            )

    if decoded_points != int(snapshot["declared_point_count"]):
        raise RuntimeError(
            f"Decoded {decoded_points} source points, expected {snapshot['declared_point_count']}"
        )
    ground_direct = ground_count > 0
    if not np.any(ground_direct):
        raise RuntimeError("The bounded source has no classified ground returns")
    dtm = np.empty(cell_count, dtype=np.float32)
    dtm[ground_direct] = (
        ground_sum[ground_direct] / ground_count[ground_direct]
    ).astype(np.float32)
    dtm = dtm.reshape(contract.height, contract.width)
    ground_direct_2d = ground_direct.reshape(contract.height, contract.width)
    distance_pixels, nearest = distance_transform_edt(
        ~ground_direct_2d, return_indices=True
    )
    dtm[~ground_direct_2d] = dtm[
        tuple(axis[~ground_direct_2d] for axis in nearest)
    ]
    dsm = dsm.reshape(contract.height, contract.width)
    dsm_missing = ~np.isfinite(dsm)
    dsm[dsm_missing] = dtm[dsm_missing]
    dsm_below_dtm = dsm < dtm
    dsm[dsm_below_dtm] = dtm[dsm_below_dtm]
    dsm_adjusted_to_dtm = dsm_missing | dsm_below_dtm
    arrays = {
        "all_count": all_count.reshape(contract.height, contract.width),
        "building_count": building_count.reshape(contract.height, contract.width),
        "building_max": building_max.reshape(contract.height, contract.width),
        "dsm": dsm,
        "dsm_adjusted_to_dtm": dsm_adjusted_to_dtm,
        "dsm_below_dtm": dsm_below_dtm,
        "dsm_missing": dsm_missing,
        "dtm": dtm,
        "ground_count": ground_count.reshape(contract.height, contract.width),
        "ground_direct": ground_direct_2d,
        "ground_fill_distance_pixels": distance_pixels.astype(np.float32),
        "vegetation_count": vegetation_count.reshape(contract.height, contract.width),
        "vegetation_max": vegetation_max.reshape(contract.height, contract.width),
    }
    statistics = {
        "by_class_inside_exact_bounds": dict(sorted(class_counts.items())),
        "decoded": decoded_points,
        "inside_exact_product_bounds": inside_points,
        "valid_surface": valid_points,
    }
    return arrays, statistics


def return_structure(
    config: dict[str, Any],
    contract: GridContract,
    snapshot_root: Path,
    snapshot: dict[str, Any],
    dtm: np.ndarray,
) -> dict[str, np.ndarray]:
    source_epsg = int(config["product_grid"]["source_crs_epsg"])
    product_epsg = int(config["product_grid"]["product_crs_epsg"])
    transformer = Transformer.from_crs(source_epsg, product_epsg, always_xy=True)
    source_bounds = tuple(float(value) for value in snapshot["selection"]["ept_3857_query_bounds_xyz"])
    unclassified = int(config["classification"]["unclassified_surface_class"])
    inference = config["height_inference"]
    minimum_height = float(inference["elevated_min_height_meters"])
    maximum_height = float(inference["maximum_height_above_ground_meters"])
    cell_count = contract.width * contract.height
    elevated_count = np.zeros(cell_count, dtype=np.uint32)
    multi_return_count = np.zeros(cell_count, dtype=np.uint32)
    first_return_count = np.zeros(cell_count, dtype=np.uint32)
    dtm_flat = dtm.reshape(-1)
    for file_index, record in enumerate(snapshot["data_files"], 1):
        laz = laspy.read(snapshot_root / record["path"])
        points = transformed_points(laz, transformer, contract, source_bounds)
        if not points:
            continue
        index = points["flat_index"]
        height = points["z"] - dtm_flat[index]
        elevated = (
            (points["classification"] == unclassified)
            & (height >= minimum_height)
            & (height <= maximum_height)
        )
        if np.any(elevated):
            elevated_index = index[elevated]
            np.add.at(elevated_count, elevated_index, 1)
            np.add.at(
                multi_return_count,
                elevated_index,
                points["number_of_returns"][elevated] > 1,
            )
            np.add.at(
                first_return_count,
                elevated_index,
                points["return_number"][elevated] == 1,
            )
        if file_index % 250 == 0 or file_index == len(snapshot["data_files"]):
            print(f"return pass {file_index}/{len(snapshot['data_files'])}", flush=True)
    shape = (contract.height, contract.width)
    return {
        "elevated_count": elevated_count.reshape(shape),
        "first_return_count": first_return_count.reshape(shape),
        "multi_return_count": multi_return_count.reshape(shape),
    }


def infer_heights(
    config: dict[str, Any],
    arrays: dict[str, np.ndarray],
    returns: dict[str, np.ndarray],
) -> dict[str, np.ndarray]:
    inference = config["height_inference"]
    maximum_height = float(inference["maximum_height_above_ground_meters"])
    dtm = arrays["dtm"]
    normalized_surface = np.clip(arrays["dsm"] - dtm, 0.0, maximum_height)
    elevated_count = returns["elevated_count"]
    multi_count = returns["multi_return_count"]
    multi_fraction = np.zeros_like(dtm)
    has_elevated = elevated_count > 0
    multi_fraction[has_elevated] = multi_count[has_elevated] / elevated_count[has_elevated]
    roughness = maximum_filter(normalized_surface, 3) - minimum_filter(normalized_surface, 3)
    elevated_candidate = (
        (normalized_surface >= float(inference["elevated_min_height_meters"]))
        & (elevated_count >= int(inference["elevated_min_returns_per_pixel"]))
    )
    building_seed = (
        elevated_candidate
        & (normalized_surface >= float(inference["building_seed_min_height_meters"]))
        & (multi_fraction < float(inference["building_seed_max_multi_return_fraction"]))
        & (roughness < float(inference["building_seed_max_roughness_meters"]))
    )
    heuristic_building = binary_dilation(building_seed, iterations=1)
    heuristic_building &= elevated_candidate
    heuristic_building &= multi_fraction < float(
        inference["building_growth_max_multi_return_fraction"]
    )
    heuristic_building = binary_closing(
        heuristic_building, structure=np.ones((3, 3), dtype=bool), iterations=1
    )
    labels, component_count = label(heuristic_building)
    component_sizes = np.bincount(labels.reshape(-1))
    keep = component_sizes >= int(inference["building_min_component_pixels"])
    if len(keep):
        keep[0] = False
    heuristic_building = keep[labels]
    heuristic_canopy = (
        elevated_candidate
        & ~heuristic_building
        & (multi_fraction >= float(inference["canopy_min_multi_return_fraction"]))
    )

    direct_vegetation = np.isfinite(arrays["vegetation_max"])
    direct_building = np.isfinite(arrays["building_max"])
    building_mask = direct_building | heuristic_building
    canopy_mask = (direct_vegetation | heuristic_canopy) & ~building_mask
    canopy_height = np.zeros_like(dtm)
    direct_canopy_height = np.clip(arrays["vegetation_max"] - dtm, 0.0, maximum_height)
    canopy_height[direct_vegetation] = direct_canopy_height[direct_vegetation]
    canopy_height[heuristic_canopy & ~direct_vegetation] = normalized_surface[
        heuristic_canopy & ~direct_vegetation
    ]
    building_height = np.zeros_like(dtm)
    direct_building_height = np.clip(arrays["building_max"] - dtm, 0.0, maximum_height)
    building_height[direct_building] = direct_building_height[direct_building]
    building_height[heuristic_building & ~direct_building] = normalized_surface[
        heuristic_building & ~direct_building
    ]

    canopy_density = np.zeros_like(dtm)
    source_support = arrays["all_count"] > 0
    direct_density = np.zeros_like(dtm)
    direct_density[source_support] = (
        arrays["vegetation_count"][source_support] / arrays["all_count"][source_support]
    )
    heuristic_density = np.zeros_like(dtm)
    heuristic_density[source_support] = (
        multi_count[source_support] / arrays["all_count"][source_support]
    )
    canopy_density[direct_vegetation] = direct_density[direct_vegetation]
    canopy_density[heuristic_canopy & ~direct_vegetation] = heuristic_density[
        heuristic_canopy & ~direct_vegetation
    ]
    canopy_density = np.clip(canopy_density, 0.0, 1.0)
    canopy_occlusion = np.clip(
        canopy_density * np.sqrt(np.clip(canopy_height / 20.0, 0.0, 1.0)), 0.0, 1.0
    )

    support = np.clip(elevated_count.astype(np.float32) / 8.0, 0.0, 1.0)
    single_return_fraction = 1.0 - multi_fraction
    planarity = np.clip(
        1.0 - roughness / float(inference["building_seed_max_roughness_meters"]),
        0.0,
        1.0,
    )
    ground_support = np.exp(
        -(arrays["ground_fill_distance_pixels"] * float(config["product_grid"]["pixel_size_meters"]))
        / 12.0
    )
    heuristic_confidence = (
        support * single_return_fraction * planarity * ground_support * heuristic_building
    )
    direct_confidence = (
        np.clip(arrays["building_count"].astype(np.float32) / 8.0, 0.0, 1.0)
        * ground_support
        * direct_building
    )
    building_confidence = np.maximum(heuristic_confidence, direct_confidence)
    plausible = (building_height >= 1.0) & (building_height < maximum_height)
    building_confidence *= plausible
    return {
        "building_confidence": building_confidence,
        "building_height": building_height,
        "building_mask": building_mask,
        "canopy_density": canopy_density,
        "canopy_height": canopy_height,
        "canopy_mask": canopy_mask,
        "canopy_occlusion": canopy_occlusion,
        "component_count_before_size_filter": np.asarray(component_count),
        "heuristic_building": heuristic_building,
        "heuristic_canopy": heuristic_canopy,
        "multi_fraction": multi_fraction,
        "normalized_surface": normalized_surface,
        "roughness": roughness,
    }


def quantize_absolute_height(
    values: np.ndarray, offset: float, scale: float
) -> np.ndarray:
    codes = np.rint((values - offset) / scale)
    if float(np.min(codes)) < 1.0 or float(np.max(codes)) > 65535.0:
        raise RuntimeError(
            f"Absolute height codes [{float(np.min(codes))}, {float(np.max(codes))}] exceed R16"
        )
    return codes.astype(np.uint16)


def build_normal_texture(
    config: dict[str, Any], arrays: dict[str, np.ndarray], inferred: dict[str, np.ndarray]
) -> np.ndarray:
    pixel_size = float(config["product_grid"]["pixel_size_meters"])
    smooth = gaussian_filter(arrays["dtm"], sigma=0.75, mode="nearest")
    height_east_gradient = np.gradient(smooth, pixel_size, axis=1)
    height_south_gradient = np.gradient(smooth, pixel_size, axis=0)
    normal_x = -height_east_gradient
    normal_y = np.ones_like(normal_x)
    normal_z = height_south_gradient
    magnitude = np.sqrt(normal_x**2 + normal_y**2 + normal_z**2)
    normal_x /= magnitude
    normal_y /= magnitude
    normal_z /= magnitude
    texture = np.empty((*smooth.shape, 4), dtype=np.uint8)
    texture[..., 0] = np.rint(np.clip(normal_x * 0.5 + 0.5, 0.0, 1.0) * 255).astype(
        np.uint8
    )
    texture[..., 1] = np.rint(np.clip(normal_y * 0.5 + 0.5, 0.0, 1.0) * 255).astype(
        np.uint8
    )
    texture[..., 2] = np.rint(np.clip(normal_z * 0.5 + 0.5, 0.0, 1.0) * 255).astype(
        np.uint8
    )
    any_surface = arrays["all_count"] > 0
    validity = (
        any_surface.astype(np.uint8)
        | (arrays["ground_direct"].astype(np.uint8) << 1)
        | (inferred["canopy_mask"].astype(np.uint8) << 2)
        | (inferred["building_mask"].astype(np.uint8) << 3)
        | ((~arrays["ground_direct"]).astype(np.uint8) << 4)
        | (arrays["dsm_adjusted_to_dtm"].astype(np.uint8) << 5)
        | (inferred["heuristic_canopy"].astype(np.uint8) << 6)
        | (inferred["heuristic_building"].astype(np.uint8) << 7)
    )
    texture[..., 3] = validity
    return texture


def build_canopy_normal_texture(
    config: dict[str, Any], inferred: dict[str, np.ndarray]
) -> np.ndarray:
    """Encode normals derived from the canopy-height model, plus its occlusion proxy."""
    pixel_size = float(config["product_grid"]["pixel_size_meters"])
    canopy_mask = inferred["canopy_mask"]
    smooth = gaussian_filter(inferred["canopy_height"], sigma=0.75, mode="nearest")
    height_east_gradient = np.gradient(smooth, pixel_size, axis=1)
    height_south_gradient = np.gradient(smooth, pixel_size, axis=0)
    normal_x = -height_east_gradient
    normal_y = np.ones_like(normal_x)
    normal_z = height_south_gradient
    magnitude = np.sqrt(normal_x**2 + normal_y**2 + normal_z**2)
    normal_x /= magnitude
    normal_y /= magnitude
    normal_z /= magnitude
    normal_x[~canopy_mask] = 0.0
    normal_y[~canopy_mask] = 1.0
    normal_z[~canopy_mask] = 0.0
    texture = np.empty((*smooth.shape, 4), dtype=np.uint8)
    texture[..., 0] = np.rint(np.clip(normal_x * 0.5 + 0.5, 0.0, 1.0) * 255).astype(
        np.uint8
    )
    texture[..., 1] = np.rint(np.clip(normal_y * 0.5 + 0.5, 0.0, 1.0) * 255).astype(
        np.uint8
    )
    texture[..., 2] = np.rint(np.clip(normal_z * 0.5 + 0.5, 0.0, 1.0) * 255).astype(
        np.uint8
    )
    texture[..., 3] = np.rint(
        np.clip(inferred["canopy_occlusion"], 0.0, 1.0) * 255
    ).astype(np.uint8)
    return texture


def save_png(path: Path, array: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(array).save(path, format="PNG", compress_level=9, optimize=False)


def asset_record(
    output_root: Path,
    name: str,
    semantic: str,
    width: int,
    height: int,
    encoding: dict[str, Any],
) -> dict[str, Any]:
    path = output_root / name
    value = file_record(path, f"{LOGICAL_ASSET_DIR}/{name}")
    value.update(
        {
            "dimensions": {"height_pixels": height, "width_pixels": width},
            "encoding": encoding,
            "semantic": semantic,
        }
    )
    return value


def coordinate_mapping(config: dict[str, Any], contract: GridContract) -> dict[str, Any]:
    pixel_center_easting = contract.min_easting + 0.5 * contract.pixel_size
    pixel_center_northing = contract.max_northing - 0.5 * contract.pixel_size
    origin = config["origin"]
    return {
        "grid": {
            "height_pixels": contract.height,
            "pixel_size_meters": contract.pixel_size,
            "product_bounds_utm13n_meters": [
                contract.min_easting,
                contract.min_northing,
                contract.max_easting,
                contract.max_northing,
            ],
            "product_crs_epsg": int(config["product_grid"]["product_crs_epsg"]),
            "source_crs_epsg": int(config["product_grid"]["source_crs_epsg"]),
            "width_pixels": contract.width,
        },
        "origin": {
            "source_elevation_msl_meters": float(origin["elevation_msl_meters"]),
            "utm13n_easting_meters": contract.origin_easting,
            "utm13n_northing_meters": contract.origin_northing,
            "wgs84_latitude_degrees": float(origin["latitude_degrees"]),
            "wgs84_longitude_degrees": float(origin["longitude_degrees"]),
        },
        "pixel_center_affine_local_meters": {
            "x_at_column0_row0": pixel_center_easting - contract.origin_easting,
            "x_per_column": contract.pixel_size,
            "x_per_row": 0.0,
            "z_at_column0_row0": pixel_center_northing - contract.origin_northing,
            "z_per_column": 0.0,
            "z_per_row": -contract.pixel_size,
        },
        "pixel_center_affine_utm13n_meters": {
            "easting_at_column0_row0": pixel_center_easting,
            "easting_per_column": contract.pixel_size,
            "easting_per_row": 0.0,
            "northing_at_column0_row0": pixel_center_northing,
            "northing_per_column": 0.0,
            "northing_per_row": -contract.pixel_size,
        },
        "unity_axes": {"x": "east", "y": "up", "z": "north"},
        "vertical_mapping": {
            "associated_wesm_original_workunit": {
                "geoid": config["source"]["wesm"]["record"]["geoid"],
                "vertical_crs_epsg": config["source"]["wesm"]["record"][
                    "vertical_crs_epsg"
                ],
            },
            "local_y_formula": (
                "decoded_source_z_meters - coordinate_mapping.origin.source_elevation_msl_meters"
            ),
            "source_metadata_status": (
                "EPT supplies metre-valued Z but does not identify a vertical CRS; the exact-workunit "
                "WESM record identifies the original workunit CRS, retained separately above."
            ),
            "source_vertical_crs": None,
        },
    }


def source_pipeline_records(config_path: Path) -> list[dict[str, Any]]:
    root = repository_root()
    paths = [
        config_path,
        Path(__file__).resolve(),
        Path(__file__).with_name("common.py"),
        root / "tools" / "scenery" / "conda-lock-win-64.txt",
        root / "tools" / "scenery" / "toolchain.json",
    ]
    return [file_record(path, path.relative_to(root).as_posix()) for path in paths]


def derive(
    config_path: Path,
    snapshot_root: Path,
    output_root: Path,
    write_unity_meta: bool = False,
) -> dict[str, Any]:
    config = load_config(config_path)
    contract = compute_grid_contract(config)
    snapshot_path = snapshot_root / "raw_snapshot_manifest.json"
    snapshot = load_json(snapshot_path)
    verify_snapshot(snapshot_root, snapshot)
    if snapshot.get("source_dataset") != config["source"]["dataset_id"]:
        raise ValueError("Snapshot source dataset does not match config")
    expected_bounds = np.asarray(
        [
            contract.min_easting,
            contract.min_northing,
            contract.max_easting,
            contract.max_northing,
        ]
    )
    if not np.allclose(snapshot["selection"]["utm13n_bounds_meters"], expected_bounds, atol=1e-6):
        raise ValueError("Snapshot product bounds do not match config")
    output_root.mkdir(parents=True, exist_ok=True)
    arrays, point_statistics = direct_surfaces(
        config, contract, snapshot_root, snapshot
    )
    returns = return_structure(
        config, contract, snapshot_root, snapshot, arrays["dtm"]
    )
    inferred = infer_heights(config, arrays, returns)
    quantization = config["quantization"]
    absolute_offset = float(quantization["absolute_height_offset_meters"])
    absolute_scale = float(quantization["absolute_height_scale_meters"])
    hag_scale = float(quantization["height_above_ground_scale_meters"])
    dtm_codes = quantize_absolute_height(arrays["dtm"], absolute_offset, absolute_scale)
    dsm_codes = quantize_absolute_height(arrays["dsm"], absolute_offset, absolute_scale)
    features = np.stack(
        [
            np.rint(inferred["canopy_height"] / hag_scale),
            np.rint(np.clip(inferred["canopy_density"], 0.0, 1.0) * 255),
            np.rint(inferred["building_height"] / hag_scale),
            np.rint(np.clip(inferred["canopy_occlusion"], 0.0, 1.0) * 255),
        ],
        axis=-1,
    ).clip(0, 255).astype(np.uint8)
    normals = build_normal_texture(config, arrays, inferred)
    canopy_normals = build_canopy_normal_texture(config, inferred)
    building_confidence = np.rint(
        np.clip(inferred["building_confidence"], 0.0, 1.0) * 255
    ).astype(np.uint8)
    save_png(output_root / DTM_NAME, dtm_codes)
    save_png(output_root / DSM_NAME, dsm_codes)
    save_png(output_root / FEATURES_NAME, features)
    save_png(output_root / NORMAL_NAME, normals)
    save_png(output_root / CANOPY_NORMAL_NAME, canopy_normals)
    save_png(output_root / BUILDING_CONFIDENCE_NAME, building_confidence)
    width, height = contract.width, contract.height
    assets = [
        asset_record(
            output_root,
            DTM_NAME,
            "digital_terrain_model",
            width,
            height,
            {
                "bits_per_channel": 16,
                "channel": "R",
                "code_zero_semantic": "nodata_reserved",
                "container": "PNG",
                "decoded_source_z_meters": {
                    "offset": absolute_offset,
                    "scale": absolute_scale,
                },
            },
        ),
        asset_record(
            output_root,
            DSM_NAME,
            "digital_surface_model",
            width,
            height,
            {
                "bits_per_channel": 16,
                "channel": "R",
                "code_zero_semantic": "nodata_reserved",
                "container": "PNG",
                "decoded_source_z_meters": {
                    "offset": absolute_offset,
                    "scale": absolute_scale,
                },
            },
        ),
        asset_record(
            output_root,
            FEATURES_NAME,
            "surface_features_canopy_building",
            width,
            height,
            {
                "bits_per_channel": 8,
                "channels": {
                    "A": "canopy_occlusion_proxy_0_to_1",
                    "B": f"building_height_above_ground_{hag_scale}_meters_per_code",
                    "G": "canopy_multi_return_density_proxy_0_to_1",
                    "R": f"canopy_height_above_ground_{hag_scale}_meters_per_code",
                },
                "container": "PNG",
            },
        ),
        asset_record(
            output_root,
            NORMAL_NAME,
            "terrain_surface_normal_and_validity",
            width,
            height,
            {
                "bits_per_channel": 8,
                "channels": {
                    "A": {
                        "bit_0": "any_valid_surface_return",
                        "bit_1": "direct_classified_ground_support",
                        "bit_2": "canopy_present",
                        "bit_3": "building_present",
                        "bit_4": "dtm_nearest_fill_used",
                        "bit_5": "dsm_filled_or_floored_to_dtm",
                        "bit_6": "canopy_return_structure_heuristic_used",
                        "bit_7": "building_return_structure_heuristic_used",
                    },
                    "B": "normal_z_north_encoded_minus1_to1",
                    "G": "normal_y_up_encoded_minus1_to1",
                    "R": "normal_x_east_encoded_minus1_to1",
                },
                "container": "PNG",
            },
        ),
        asset_record(
            output_root,
            CANOPY_NORMAL_NAME,
            "canopy_height_normal_and_occlusion",
            width,
            height,
            {
                "bits_per_channel": 8,
                "channels": {
                    "A": "canopy_occlusion_proxy_0_to_1",
                    "B": "canopy_height_normal_z_north_encoded_minus1_to1",
                    "G": "canopy_height_normal_y_up_encoded_minus1_to1",
                    "R": "canopy_height_normal_x_east_encoded_minus1_to1",
                },
                "container": "PNG",
                "derivation": (
                    "Normals from the Gaussian-smoothed canopy-height-above-ground raster; "
                    "non-canopy pixels encode up and zero occlusion."
                ),
                "source_classification_status": "heuristic because no LAS vegetation classes are present",
            },
        ),
        asset_record(
            output_root,
            BUILDING_CONFIDENCE_NAME,
            "building_height_confidence",
            width,
            height,
            {
                "bits_per_channel": 8,
                "channel": "R",
                "container": "PNG",
                "decoded_confidence": {"offset": 0.0, "scale": 1.0 / 255.0},
            },
        ),
    ]
    direct_ground = arrays["ground_direct"]
    any_surface = arrays["all_count"] > 0
    statistics = {
        "coverage": {
            "any_valid_surface_fraction": float(np.mean(any_surface)),
            "building_fraction": float(np.mean(inferred["building_mask"])),
            "canopy_fraction": float(np.mean(inferred["canopy_mask"])),
            "direct_ground_fraction": float(np.mean(direct_ground)),
            "dsm_filled_from_dtm_fraction": float(np.mean(arrays["dsm_missing"])),
            "dsm_floored_to_dtm_fraction": float(np.mean(arrays["dsm_below_dtm"])),
            "dtm_nearest_filled_fraction": float(np.mean(~direct_ground)),
        },
        "point_counts": {
            "declared": int(snapshot["declared_point_count"]),
            **point_statistics,
        },
        "ranges_meters": {
            "building_height_above_ground": [
                float(np.min(inferred["building_height"])),
                float(np.max(inferred["building_height"])),
            ],
            "canopy_height_above_ground": [
                float(np.min(inferred["canopy_height"])),
                float(np.max(inferred["canopy_height"])),
            ],
            "dsm_source_z": [float(np.min(arrays["dsm"])), float(np.max(arrays["dsm"]))],
            "dtm_source_z": [float(np.min(arrays["dtm"])), float(np.max(arrays["dtm"]))],
            "maximum_ground_fill_distance": float(
                np.max(arrays["ground_fill_distance_pixels"]) * contract.pixel_size
            ),
        },
    }
    source_classes = statistics["point_counts"]["by_class_inside_exact_bounds"]
    has_direct_canopy_classes = any(
        str(item) in source_classes or item in source_classes
        for item in config["classification"]["vegetation_classes"]
    )
    has_direct_building_class = (
        str(config["classification"]["building_class"]) in source_classes
        or config["classification"]["building_class"] in source_classes
    )
    manifest = {
        "assets": assets,
        "building_height_quality": {
            "eligible_for_authoritative_override": False,
            "meets_10_percent_vertical_error_gate": False,
            "reason": (
                "No surveyed truth/error study is available; source building class is absent at KBDU "
                "and the conservative channel uses an unclassified-return structure heuristic."
            ),
            "vertical_error_fraction_bound": None,
        },
        "coordinate_mapping": coordinate_mapping(config, contract),
        "inference": {
            "building": {
                "direct_class_available_inside_bounds": has_direct_building_class,
                "method": "classified building returns when present; otherwise planar predominantly single-return elevated-region heuristic",
                "thresholds": {
                    key: value
                    for key, value in config["height_inference"].items()
                    if key.startswith("building_") or key.startswith("elevated_")
                },
            },
            "canopy": {
                "direct_classes_available_inside_bounds": has_direct_canopy_classes,
                "method": "classified vegetation returns when present; otherwise elevated multi-return heuristic",
                "thresholds": {
                    key: value
                    for key, value in config["height_inference"].items()
                    if key.startswith("canopy_") or key.startswith("elevated_")
                },
            },
        },
        "label": config["label"],
        "license_and_provenance": {
            "attribution": "U.S. Geological Survey 3D Elevation Program (3DEP)",
            "license": config["source"]["license_name"],
            "license_url": config["source"]["license_url"],
            "public_domain_derivative": True,
            "raw_data_committed": False,
            "raw_data_policy": snapshot.get(
                "raw_data_policy",
                "External only; this manifest and all EPT source files are prohibited from Git.",
            ),
        },
        "limitations": [
            "Not for navigation; source collection date, accuracy, and current conditions differ.",
            "EPT/STAC identify EPSG:3857 horizontally but do not identify a vertical CRS; the exact-workunit WESM row identifies the original workunit as EPSG:5703/GEOID18.",
            "WESM records QL 1 and a Meets LPC status, but no numeric achieved accuracy or pulse density is carried into this product or inferred from that category.",
            "Depth-8 EPT is a nominal 2.0693359375 m hierarchy selection, not the full-density source cloud.",
            "The KBDU subset has no classified vegetation or building returns; canopy/building channels are conservative return-structure heuristics.",
            "Nearest fill is exposed by validity bits and must not be mistaken for directly observed ground.",
            "No source or scenario evidence supports 0.05 m real-world operational accuracy.",
            "No standalone Quest performance claim is made by this offline data pipeline.",
        ],
        "schema": "questflight.aerial-scenery-v1.lidar.v1",
        "source": {
            "classification_observation": {
                "building_class_code": config["classification"]["building_class"],
                "building_class_present_inside_exact_bounds": has_direct_building_class,
                "counts_by_numeric_las_class_inside_exact_bounds": source_classes,
                "vegetation_class_codes": config["classification"]["vegetation_classes"],
                "vegetation_classes_present_inside_exact_bounds": [
                    item
                    for item in config["classification"]["vegetation_classes"]
                    if str(item) in source_classes or item in source_classes
                ],
            },
            "data_file_count": int(snapshot["data_file_count"]),
            "data_inventory_sha256": snapshot["data_inventory_sha256"],
            "dataset_id": snapshot["source_dataset"],
            "ept_url": config["source"]["ept_url"],
            "metadata_availability": {
                "ept_stac_accuracy_metrics": None,
                "ept_stac_horizontal_crs_epsg": config["product_grid"]["source_crs_epsg"],
                "ept_stac_vertical_crs": None,
                "nominal_pulse_density_points_per_square_meter": None,
                "nominal_pulse_spacing_meters": None,
                "note": (
                    "EPT/STAC omit vertical CRS and accuracy/QL/density values. The exact-workunit "
                    "WESM row supplies original-workunit CRS, QL, collection dates, and product status, "
                    "but no numeric pulse-density or achieved-accuracy metric is inferred from QL."
                ),
                "wesm_achieved_accuracy_metrics": None,
                "wesm_original_workunit_vertical_crs_epsg": config["source"]["wesm"][
                    "record"
                ]["vertical_crs_epsg"],
            },
            "nominal_ept_resolution_meters": snapshot["selection"][
                "nominal_ept_resolution_meters"
            ],
            "raw_snapshot_manifest_sha256": sha256_file(snapshot_path),
            "retrieved_utc": snapshot["retrieved_utc"],
            "stac_item_url": snapshot["stac_item_url"],
            "target_depth": snapshot["selection"]["target_depth"],
            "wesm": config["source"]["wesm"],
        },
        "source_pipeline": source_pipeline_records(config_path),
        "statistics": statistics,
    }
    product_bytes = sum(int(asset["bytes"]) for asset in assets)
    budget = int(config["derivative_budgets"]["maximum_product_bytes"])
    if product_bytes > budget:
        raise RuntimeError(f"Raster derivatives {product_bytes} bytes exceed budget {budget}")
    manifest["statistics"]["raster_product_bytes"] = product_bytes
    write_json(output_root / MANIFEST_NAME, manifest)
    if write_unity_meta:
        write_folder_meta(output_root, LOGICAL_ASSET_DIR)
        note = "USGS 3DEP public-domain bounded derivative; see adjacent KBDU_LidarManifestV1.json"
        for name in (
            DTM_NAME,
            DSM_NAME,
            FEATURES_NAME,
            NORMAL_NAME,
            CANOPY_NORMAL_NAME,
            BUILDING_CONFIDENCE_NAME,
        ):
            write_texture_meta(output_root / name, f"{LOGICAL_ASSET_DIR}/{name}", note)
        write_text_meta(
            output_root / MANIFEST_NAME,
            f"{LOGICAL_ASSET_DIR}/{MANIFEST_NAME}",
            "USGS 3DEP KBDU lidar derivative manifest / not for navigation",
        )
    return manifest


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, default=default_config_path())
    parser.add_argument("--snapshot", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--write-unity-meta", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    manifest = derive(
        args.config.resolve(),
        args.snapshot.resolve(),
        args.output.resolve(),
        write_unity_meta=args.write_unity_meta,
    )
    print(
        json.dumps(
            {
                "assets": len(manifest["assets"]),
                "building_height_authoritative": manifest["building_height_quality"][
                    "eligible_for_authoritative_override"
                ],
                "raster_product_bytes": manifest["statistics"]["raster_product_bytes"],
                "status": "DERIVED",
            },
            indent=2,
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
