#!/usr/bin/env python3
"""Validate the committed Photoreal Aerial Surface V1 asset contract.

This is a read-only batch validator.  It requires only committed compact assets;
raw EPT/LAZ data and network access are neither used nor allowed.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import Any, Iterable

import numpy as np
from PIL import Image

from build_building_surfaces import (
    GENERATED_DIR as BUILDING_GENERATED_DIR,
    LAYER_NAME as BUILDING_LAYER_NAME,
    MANIFEST_NAME as BUILDING_MANIFEST_NAME,
    MASK_NAME as BUILDING_MASK_NAME,
    OUTPUT_DIR as BUILDING_OUTPUT_DIR,
    edge_multiplicity,
    parse_building_layer,
)
from build_canopy_surfaces import (
    GENERATED_DIR as CANOPY_GENERATED_DIR,
    GEOMETRY_MASK_NAME,
    GEOMETRY_OUTER_RADIUS_METERS,
    LAYER_NAME as CANOPY_LAYER_NAME,
    MANIFEST_NAME as CANOPY_MANIFEST_NAME,
    MINIMUM_STATIC_MESH_TRIANGLES,
    MAXIMUM_MESH_TRIANGLES,
    OUTPUT_DIR as CANOPY_OUTPUT_DIR,
    PRODUCT_CANOPY_POLICY,
    REVIEW_MATCH_COARSE_MINIMUM_ELIGIBLE_PIXELS,
    REVIEW_MATCH_REFINEMENT_REQUIRED_ELIGIBLE_PIXELS,
    TARGET_MESH_TRIANGLES,
    TERRAIN_HANDOFF_MASK_NAME,
    canopy_geometry_cells,
    compile_semantic_groves,
    eligible_canopy_mask,
    filter_small_geometry_components,
    load_near_ground_imagery,
    parse_canopy_layer,
    rasterize_runs,
    load_reviewed_exclusion_contract,
    reviewed_canopy_exclusion_mask,
    resample_operational_exclusion,
    screen_relevant_grove_source_mask,
)
from build_dtm_dsm import (
    DEFAULT_OUTPUT as LIDAR_OUTPUT_DIR,
    MANIFEST_NAME as DTM_DSM_MANIFEST_NAME,
    NDSM_NAME,
    REPOSITORY_ROOT,
    RUNTIME_ROOT,
    SOURCE_MANIFEST,
    asset_by_semantic,
    build_ndsm_codes,
    load_json,
    resolve_logical,
    sha256_bytes,
    sha256_file,
    verify_source_asset,
)
from build_product_terrain import (
    MANIFEST_NAME as TERRAIN_MANIFEST_NAME,
    MAXIMUM_TRIANGLES as TERRAIN_MAXIMUM_TRIANGLES,
    MESH_NAME as TERRAIN_MESH_NAME,
    MESH_OBJECT_NAME as TERRAIN_MESH_OBJECT_NAME,
    OUTPUT_DIR as TERRAIN_OUTPUT_DIR,
    REPORT_NAME as TERRAIN_REPORT_NAME,
    REPORT_SCHEMA as TERRAIN_REPORT_SCHEMA,
    derive as derive_product_terrain,
    obj_text as product_terrain_obj_text,
)
from classify_surface import CLASS_TEXTURE_NAME, MANIFEST_NAME as CLASSIFICATION_MANIFEST_NAME, classify
from geospatial_frame import (
    horizontal_frame_contract,
    lidar_pixel_centers_product_aeqd,
    utm13n_delta_to_product_aeqd,
)


OWNERSHIP_MANIFEST_NAME = "KBDU_AerialSurface_OwnershipExclusionManifestV1.json"
ROOT_META_GUID = "b0fe91d928504e3e89264a3c377ebf12"
EXPECTED_SCHEMAS = {
    "dtm_dsm": "questflight.photoreal-aerial-surface-v1.dtm-dsm.v1",
    "classification": "questflight.photoreal-aerial-surface-v1.classification.v1",
    "building": "questflight.photoreal-aerial-surface-v1.production-building-surface-layer.v1",
    "canopy": "questflight.photoreal-aerial-surface-v1.production-canopy-surface-layer.v1",
    "ownership": "questflight.photoreal-aerial-surface-v1.manifest.v1",
    "terrain": "questflight.photoreal-aerial-surface-v1.product-aeqd-bare-earth.v1",
}
RUNTIME_AFFINE_KEYS = {
    "position_maximum_allowed_residual_meters",
    "position_maximum_residual_meters",
    "product_to_utm_x_from_product_x",
    "product_to_utm_x_from_product_z",
    "product_to_utm_x_offset_meters",
    "product_to_utm_z_from_product_x",
    "product_to_utm_z_from_product_z",
    "product_to_utm_z_offset_meters",
    "utm_normal_to_product_rotation_degrees_counterclockwise",
    "utm_normal_to_product_x_from_utm_x",
    "utm_normal_to_product_x_from_utm_z",
    "utm_normal_to_product_z_from_utm_x",
    "utm_normal_to_product_z_from_utm_z",
}


class ValidationFailure(ValueError):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValidationFailure(message)


def load_image(path: Path, shape: tuple[int, ...], dtype: np.dtype[Any]) -> np.ndarray:
    values = np.asarray(Image.open(path))
    require(values.shape == shape, f"Unexpected image shape at {path}: {values.shape}")
    require(values.dtype == dtype, f"Unexpected image dtype at {path}: {values.dtype}")
    return values


def nested_file_records(value: Any) -> Iterable[dict[str, Any]]:
    if isinstance(value, dict):
        if {"path", "sha256", "bytes"}.issubset(value):
            yield value
        for child in value.values():
            yield from nested_file_records(child)
    elif isinstance(value, list):
        for child in value:
            yield from nested_file_records(child)


def verify_record(record: dict[str, Any]) -> Path:
    path = resolve_logical(str(record["path"]))
    require(path.is_file(), f"Recorded file is missing: {path}")
    require(path.stat().st_size == int(record["bytes"]), f"Recorded byte count changed: {path}")
    require(sha256_file(path) == str(record["sha256"]), f"Recorded SHA-256 changed: {path}")
    return path


def verify_manifest_records(manifests: Iterable[dict[str, Any]]) -> tuple[int, set[Path]]:
    unique: dict[str, dict[str, Any]] = {}
    for manifest in manifests:
        for record in nested_file_records(manifest):
            unique[str(record["path"])] = record
    paths = {verify_record(record).resolve() for record in unique.values()}
    return len(unique), paths


def parse_obj(path: Path) -> dict[str, Any]:
    objects: list[str] = []
    groups: list[str] = []
    vertices: list[tuple[float, float, float]] = []
    colors: list[tuple[float, float, float, float] | None] = []
    faces: list[tuple[int, int, int]] = []
    forbidden: list[str] = []
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if line.startswith("o "):
            objects.append(line[2:].strip())
        elif line.startswith("g "):
            groups.append(line[2:].strip())
        elif line.startswith("v "):
            fields = line.split()
            require(len(fields) in (4, 7, 8), f"Unsupported vertex record in {path}")
            vertices.append((float(fields[1]), float(fields[2]), float(fields[3])))
            colors.append(
                (
                    float(fields[4]),
                    float(fields[5]),
                    float(fields[6]),
                    float(fields[7]) if len(fields) == 8 else 1.0,
                )
                if len(fields) > 4
                else None
            )
        elif line.startswith("f "):
            fields = line.split()[1:]
            require(len(fields) == 3, f"Non-triangle face in {path}")
            indices = tuple(int(field.split("/")[0]) for field in fields)
            require(all(1 <= index <= len(vertices) for index in indices), f"OBJ index out of range: {path}")
            faces.append(indices)
        elif line.startswith("mtllib ") or line.startswith("usemtl "):
            forbidden.append(line)
    return {
        "faces": faces,
        "colors": colors,
        "forbidden_material_lines": forbidden,
        "groups": groups,
        "objects": objects,
        "vertices": vertices,
    }


def validate_obj_records(
    records: list[dict[str, Any]],
    expected_prefix: str,
    expected_count: int,
    radial_bounds: tuple[float, float] | None = None,
) -> dict[str, int | float]:
    require(len(records) == expected_count, f"Expected {expected_count} {expected_prefix} OBJ records")
    triangle_total = vertex_total = 0
    minimum_radius = math.inf
    maximum_radius = 0.0
    seen_names: set[str] = set()
    for record in sorted(records, key=lambda item: str(item["path"])):
        path = verify_record(record)
        require(path.suffix.lower() == ".obj", f"Mesh batch is not OBJ: {path}")
        parsed = parse_obj(path)
        stem = path.stem
        require(stem.startswith(expected_prefix), f"Unexpected mesh batch name: {stem}")
        require(parsed["objects"] == [stem], f"OBJ must have exactly one matching object: {path}")
        require(parsed["groups"] == [stem], f"OBJ must have exactly one matching group: {path}")
        require(not parsed["forbidden_material_lines"], f"OBJ embeds a material binding: {path}")
        require(stem not in seen_names, f"Duplicate mesh batch identity: {stem}")
        seen_names.add(stem)
        triangles = len(parsed["faces"])
        vertices = len(parsed["vertices"])
        require(triangles == int(record["triangle_count"]), f"OBJ triangle count changed: {path}")
        require(vertices == int(record["vertex_count"]), f"OBJ vertex count changed: {path}")
        if expected_prefix == "ProductionBuildingSurfaceLayer_":
            require(all(color is not None for color in parsed["colors"]),
                    f"Building OBJ lacks semantic facade colors: {path}")
            allowed_tiles = {0, 1, 5, 8, 10}
            decoded_tiles = {
                int(round(float(color[0]) * 255.0))
                for color in parsed["colors"] if color is not None
            }
            require(decoded_tiles <= allowed_tiles,
                    f"Building OBJ uses an unsupported facade tile: {path}")
            require(all(
                0.0 <= channel <= 1.0
                for color in parsed["colors"] if color is not None
                for channel in color
            ), f"Building semantic color is outside [0,1]: {path}")
        if "product_bounds_xz_meters" in record:
            require(
                record.get("horizontal_frame")
                == "KBDU product-local WGS84 azimuthal equidistant",
                f"OBJ horizontal frame changed: {path}",
            )
            vertex_array = np.asarray(parsed["vertices"], dtype=np.float64)
            recorded_bounds = record["product_bounds_xz_meters"]
            actual_minimum = np.min(vertex_array[:, (0, 2)], axis=0)
            actual_maximum = np.max(vertex_array[:, (0, 2)], axis=0)
            require(
                np.allclose(
                    actual_minimum,
                    np.asarray(recorded_bounds["minimum"], dtype=np.float64),
                    rtol=0.0,
                    atol=0.001,
                ),
                f"OBJ product minimum bounds changed: {path}",
            )
            require(
                np.allclose(
                    actual_maximum,
                    np.asarray(recorded_bounds["maximum"], dtype=np.float64),
                    rtol=0.0,
                    atol=0.001,
                ),
                f"OBJ product maximum bounds changed: {path}",
            )
        triangle_total += triangles
        vertex_total += vertices
        if radial_bounds is not None:
            for x, _y, z in parsed["vertices"]:
                radius = math.hypot(x, z)
                minimum_radius = min(minimum_radius, radius)
                maximum_radius = max(maximum_radius, radius)
    if radial_bounds is not None:
        require(minimum_radius >= radial_bounds[0] - 1e-6, "Canopy mesh enters hero-tree ownership radius")
        require(maximum_radius <= radial_bounds[1] + 1e-6, "Canopy mesh exceeds geometry ownership radius")
    return {
        "maximum_vertex_radius_meters": maximum_radius,
        "minimum_vertex_radius_meters": minimum_radius if minimum_radius != math.inf else 0.0,
        "triangle_count": triangle_total,
        "vertex_count": vertex_total,
    }


def validate(root: Path = RUNTIME_ROOT) -> dict[str, Any]:
    root = root.resolve()
    require(root == RUNTIME_ROOT.resolve(), "Validator root must be the pinned PhotorealAerialSurfaceV1 directory")
    manifest_paths = {
        "dtm_dsm": root / "Lidar" / DTM_DSM_MANIFEST_NAME,
        "classification": root / "Lidar" / CLASSIFICATION_MANIFEST_NAME,
        "building": root / "Buildings" / BUILDING_MANIFEST_NAME,
        "canopy": root / "Canopy" / CANOPY_MANIFEST_NAME,
        "ownership": root / OWNERSHIP_MANIFEST_NAME,
        "terrain": root / "Terrain" / TERRAIN_MANIFEST_NAME,
    }
    manifests = {name: load_json(path) for name, path in manifest_paths.items()}
    for name, expected in EXPECTED_SCHEMAS.items():
        require(manifests[name].get("schema") == expected, f"Unexpected {name} schema")
    require(manifests["building"].get("semantic_root") == "ProductionBuildingSurfaceLayer", "Building semantic root changed")
    require(manifests["canopy"].get("semantic_root") == "ProductionCanopySurfaceLayer", "Canopy semantic root changed")
    semantic_roots = manifests["ownership"].get("semantic_roots", {})
    require(
        semantic_roots.get("hero_exclusions")
        == "airport operational and building semantic masks",
        "Hero exclusion owner changed",
    )

    record_count, recorded_paths = verify_manifest_records(manifests.values())
    source = load_json(SOURCE_MANIFEST)
    mapping = source["coordinate_mapping"]
    expected_frame_contract = horizontal_frame_contract(mapping)
    for name in ("dtm_dsm", "classification", "building", "canopy", "ownership", "terrain"):
        require(
            manifests[name].get("horizontal_frame_contract") == expected_frame_contract,
            f"{name} horizontal-frame transform contract changed",
        )
    exact_contract = expected_frame_contract["exact_conversion"]
    affine_contract = expected_frame_contract["diagnostic_affine_product_from_utm_delta"]
    require(
        float(exact_contract["maximum_round_trip_residual_meters"])
        <= float(exact_contract["maximum_allowed_round_trip_residual_meters"]),
        "Exact horizontal-frame round-trip residual exceeds its contract",
    )
    require(
        float(affine_contract["maximum_residual_meters"])
        <= float(affine_contract["maximum_allowed_residual_meters"]),
        "Diagnostic horizontal-frame affine residual exceeds its contract",
    )
    runtime_affine = expected_frame_contract.get("runtime_affine_coefficients", {})
    require(set(runtime_affine) == RUNTIME_AFFINE_KEYS, "Runtime affine coefficient keys changed")
    require(
        all(isinstance(value, (int, float)) for value in runtime_affine.values()),
        "Runtime affine contract contains a non-scalar coefficient",
    )
    require(
        float(runtime_affine["position_maximum_residual_meters"])
        <= float(runtime_affine["position_maximum_allowed_residual_meters"]),
        "Runtime product-to-UTM position affine exceeds its residual contract",
    )
    origin = mapping["origin"]
    bounds = np.asarray(mapping["grid"]["product_bounds_utm13n_meters"], dtype=np.float64)
    sample_x_utm = np.asarray(
        [
            bounds[0] - float(origin["utm13n_easting_meters"]),
            bounds[2] - float(origin["utm13n_easting_meters"]),
            bounds[0] - float(origin["utm13n_easting_meters"]),
            bounds[2] - float(origin["utm13n_easting_meters"]),
            0.0,
        ]
    )
    sample_z_utm = np.asarray(
        [
            bounds[1] - float(origin["utm13n_northing_meters"]),
            bounds[1] - float(origin["utm13n_northing_meters"]),
            bounds[3] - float(origin["utm13n_northing_meters"]),
            bounds[3] - float(origin["utm13n_northing_meters"]),
            0.0,
        ]
    )
    sample_x_product, sample_z_product = utm13n_delta_to_product_aeqd(
        sample_x_utm, sample_z_utm, mapping
    )
    runtime_x_utm = (
        float(runtime_affine["product_to_utm_x_from_product_x"]) * sample_x_product
        + float(runtime_affine["product_to_utm_x_from_product_z"]) * sample_z_product
        + float(runtime_affine["product_to_utm_x_offset_meters"])
    )
    runtime_z_utm = (
        float(runtime_affine["product_to_utm_z_from_product_x"]) * sample_x_product
        + float(runtime_affine["product_to_utm_z_from_product_z"]) * sample_z_product
        + float(runtime_affine["product_to_utm_z_offset_meters"])
    )
    runtime_sample_residual = np.hypot(
        runtime_x_utm - sample_x_utm, runtime_z_utm - sample_z_utm
    )
    require(
        float(np.max(runtime_sample_residual))
        <= float(runtime_affine["position_maximum_allowed_residual_meters"]),
        "Runtime product-to-UTM coefficients fail exact corner samples",
    )
    normal_rotation = np.asarray(
        [
            [
                runtime_affine["utm_normal_to_product_x_from_utm_x"],
                runtime_affine["utm_normal_to_product_x_from_utm_z"],
            ],
            [
                runtime_affine["utm_normal_to_product_z_from_utm_x"],
                runtime_affine["utm_normal_to_product_z_from_utm_z"],
            ],
        ],
        dtype=np.float64,
    )
    require(
        np.allclose(normal_rotation.T @ normal_rotation, np.identity(2), rtol=0.0, atol=1e-12),
        "Runtime UTM-to-product normal rotation is not orthonormal",
    )
    require(
        math.isclose(float(np.linalg.det(normal_rotation)), 1.0, rel_tol=0.0, abs_tol=1e-12),
        "Runtime UTM-to-product normal rotation changes handedness",
    )
    require(
        math.isclose(
            math.degrees(math.atan2(normal_rotation[1, 0], normal_rotation[0, 0])),
            float(runtime_affine["utm_normal_to_product_rotation_degrees_counterclockwise"]),
            rel_tol=0.0,
            abs_tol=1e-12,
        ),
        "Runtime UTM-to-product normal rotation angle changed",
    )
    dtm_path = verify_source_asset(asset_by_semantic(source, "digital_terrain_model"))
    dsm_path = verify_source_asset(asset_by_semantic(source, "digital_surface_model"))
    surface_path = verify_source_asset(asset_by_semantic(source, "surface_features_canopy_building"))
    terrain_normal_path = verify_source_asset(
        asset_by_semantic(source, "terrain_surface_normal_and_validity")
    )
    shape = (
        int(source["coordinate_mapping"]["grid"]["height_pixels"]),
        int(source["coordinate_mapping"]["grid"]["width_pixels"]),
    )
    dtm = load_image(dtm_path, shape, np.dtype(np.uint16))
    dsm = load_image(dsm_path, shape, np.dtype(np.uint16))
    ndsm = load_image(root / "Lidar" / NDSM_NAME, shape, np.dtype(np.uint16))
    expected_ndsm, negative_count = build_ndsm_codes(dtm, dsm)
    require(np.array_equal(ndsm, expected_ndsm), "Committed nDSM does not equal clipped DSM-DTM")
    require(negative_count == 0, "Pinned DSM has negative deltas")

    features = load_image(surface_path, (*shape, 4), np.dtype(np.uint8))
    terrain_normal = load_image(terrain_normal_path, (*shape, 4), np.dtype(np.uint8))
    surface_class = load_image(root / "Lidar" / CLASS_TEXTURE_NAME, shape, np.dtype(np.uint8))
    require(np.array_equal(surface_class, classify(ndsm, features, terrain_normal)), "Surface classification raster changed")

    building_manifest = manifests["building"]
    building_layer = parse_building_layer(root / "Buildings" / BUILDING_LAYER_NAME)
    require(building_layer["total_triangles"] == int(building_manifest["budgets"]["total_closed_surface_triangles"]), "Building binary triangle count changed")
    require(building_layer["ownership_radius"] == 4000.0, "Building binary ownership radius changed")
    hero_indices = {int(item["feature_index"]) for item in building_manifest["coverage"]["hero_exclusions"]}
    feature_indices: set[int] = set()
    for record in building_layer["records"]:
        indices = record["indices"]
        require(all(0 <= index < len(record["points"]) * 2 for index in indices), "Building index out of range")
        require(all(value == 2 for value in edge_multiplicity(indices).values()), "Building record is not a closed 2-manifold")
        feature_indices.add(int(record["feature_index"]))
    require(not (hero_indices & feature_indices), "Hero replacement footprint leaked into building binary")
    per_band = building_manifest["coverage"]["per_band"]
    require(per_band["0-2km"]["represented_footprint_count"] == 1829, "Near building denominator changed")
    require(per_band["0-2km"]["hero_exclusion_count"] == 2, "Hero replacement count changed")
    require(per_band["2-4km"]["represented_footprint_count"] == 7863, "Mid building coverage changed")
    require(float(per_band["2-4km"]["coverage_fraction"]) >= 0.98, "Mid building coverage below 98%")
    simplification = building_manifest["topology"]["footprint_simplification"]
    require(float(simplification["area_ratio_maximum"]) <= 1.05, "Building footprint inflation exceeds 5%")
    require(float(simplification["sampled_hausdorff_maximum_meters"]) <= 10.0, "Building simplification error exceeds 10 m")
    require(int(building_manifest["budgets"]["mesh_batch_total_triangles"]) <= 130_000, "Building topology exceeds hard ceiling")
    building_records = [item for item in building_manifest["assets"] if item.get("semantic") == "production_building_surface_mesh_batch"]
    building_obj = validate_obj_records(building_records, "ProductionBuildingSurfaceLayer_", 16)

    building_mask = load_image(root / "Buildings" / BUILDING_MASK_NAME, shape, np.dtype(np.uint8)) != 0
    canopy_manifest = manifests["canopy"]
    geometry_mask = load_image(root / "Canopy" / GEOMETRY_MASK_NAME, shape, np.dtype(np.uint8)) != 0
    terrain_handoff = load_image(root / "Canopy" / TERRAIN_HANDOFF_MASK_NAME, shape, np.dtype(np.uint8)) != 0
    canopy_layer = parse_canopy_layer(root / "Canopy" / CANOPY_LAYER_NAME)
    require(np.array_equal(rasterize_runs(canopy_layer), geometry_mask), "Canopy binary does not reproduce geometry mask")
    require(
        np.all(geometry_mask <= terrain_handoff),
        "Canopy geometry leaves the continuous terrain handoff",
    )
    require(not np.any(geometry_mask & building_mask), "Canopy geometry overlaps building/hero mask")

    affine = mapping["pixel_center_affine_local_meters"]
    operational_mask_record = canopy_manifest["sources"]["operational_exclusion_mask"]
    operational_contract_record = canopy_manifest["sources"]["operational_exclusion_contract"]
    reviewed_contract_record = canopy_manifest["sources"][
        "reviewed_noncanopy_exclusion_contract"
    ]
    operational_mask_path = resolve_logical(operational_mask_record["path"])
    operational_contract_path = resolve_logical(operational_contract_record["path"])
    reviewed_contract_path = resolve_logical(reviewed_contract_record["path"])
    require(
        sha256_file(operational_mask_path) == operational_mask_record["sha256"],
        "Canopy operational exclusion mask hash changed",
    )
    require(
        sha256_file(operational_contract_path) == operational_contract_record["sha256"],
        "Canopy operational exclusion contract hash changed",
    )
    operational_contract = load_json(operational_contract_path)["distribution_source"]["mask_contract"]
    operational_source = np.asarray(Image.open(operational_mask_path))
    operational_exclusion = resample_operational_exclusion(
        operational_source, shape, mapping, operational_contract
    )
    eligible, _metrics = eligible_canopy_mask(
        dtm, features, building_mask, operational_exclusion
    )
    require(
        int(_metrics["operational_exclusion_rejected_pixel_count"]) > 0,
        "Canopy operational exclusions rejected no false-positive candidates",
    )
    require(
        not np.any(geometry_mask & (operational_exclusion != 0)),
        "Canopy geometry overlaps an operational exclusion",
    )
    product_x, product_z = lidar_pixel_centers_product_aeqd(mapping, shape)
    radius = np.hypot(product_x, product_z)
    preliminary_geometry_band = eligible & (radius >= 250.0) & (
        radius <= GEOMETRY_OUTER_RADIUS_METERS
    )
    operational_geometry_exclusion = building_mask | (operational_exclusion != 0)
    _pre_coarse, _pre_refinement, preliminary_geometry = canopy_geometry_cells(
        preliminary_geometry_band,
        operational_geometry_exclusion,
        abs(float(affine["x_per_column"])),
        mapping,
        REVIEW_MATCH_COARSE_MINIMUM_ELIGIBLE_PIXELS,
        REVIEW_MATCH_REFINEMENT_REQUIRED_ELIGIBLE_PIXELS,
    )
    reviewed_contract = load_reviewed_exclusion_contract(
        reviewed_contract_path, mapping, shape
    )
    reviewed_exclusion, reviewed_metrics = reviewed_canopy_exclusion_mask(
        preliminary_geometry, eligible, mapping, reviewed_contract
    )
    require(
        sha256_file(reviewed_contract_path) == reviewed_contract_record["sha256"],
        "Reviewed canopy exclusion contract hash changed",
    )
    require(
        int(reviewed_metrics["component_count"]) == 2,
        "Reviewed canopy exclusion component count changed",
    )
    require(
        int(reviewed_metrics["source_eligible_pixel_count"])
        == int(canopy_manifest["coverage"]["reviewed_noncanopy_excluded_pixel_count"]),
        "Reviewed canopy excluded source count changed",
    )
    require(not np.any(geometry_mask & reviewed_exclusion), "Canopy geometry overlaps a reviewed exclusion")
    require(not np.any(terrain_handoff & reviewed_exclusion), "Canopy handoff overlaps a reviewed exclusion")
    eligible &= ~reviewed_exclusion
    hero_source = eligible & (radius < 250.0)
    geometry_band = eligible & (radius >= 250.0) & (
        radius <= GEOMETRY_OUTER_RADIUS_METERS
    )
    screen_relevant_source, expected_grove_metrics = screen_relevant_grove_source_mask(
        geometry_band, abs(float(affine["x_per_column"]))
    )
    candidate_coarse, candidate_refinement, candidate_geometry = canopy_geometry_cells(
        screen_relevant_source,
        operational_geometry_exclusion | reviewed_exclusion,
        abs(float(affine["x_per_column"])),
        mapping,
    )
    (
        candidate_coarse,
        candidate_refinement,
        candidate_geometry,
        expected_filter_metrics,
    ) = (
        filter_small_geometry_components(
            candidate_coarse, candidate_refinement, candidate_geometry
        )
    )
    dtm_record = asset_by_semantic(source, "digital_terrain_model")
    dtm_encoding = dtm_record["encoding"]["decoded_source_z_meters"]
    dtm_local = (
        dtm.astype(np.float64) * float(dtm_encoding["scale"])
        + float(dtm_encoding["offset"])
        - float(mapping["origin"]["source_elevation_msl_meters"])
    )
    canopy_height = features[:, :, 0].astype(np.float64) * 0.25
    canopy_density = features[:, :, 1].astype(np.float64) / 255.0
    building_height = features[:, :, 2].astype(np.float64) * 0.25
    ground_image, ground_bounds, _ground_record = load_near_ground_imagery()
    groves, expected_geometry, compiled_grove_metrics = compile_semantic_groves(
        eligible,
        operational_geometry_exclusion | reviewed_exclusion,
        radius,
        product_x,
        product_z,
        dtm_local,
        canopy_height,
        canopy_density,
        building_height,
        ground_image,
        ground_bounds,
    )
    del ground_image
    require(np.array_equal(expected_geometry, geometry_mask), "Canopy geometry ownership is not deterministic")
    require(
        int(canopy_layer["pixel_count"]) == int(np.count_nonzero(geometry_mask)),
        "Canopy binary ownership count changed",
    )
    require(canopy_layer["components"], "Canopy binary contains no connected components")
    require(canopy_layer["runs"], "Canopy binary contains no ownership runs")
    expected_handoff = eligible
    require(
        np.array_equal(expected_handoff, terrain_handoff),
        "Continuous canopy terrain-map handoff is not deterministic",
    )
    require(
        np.all(geometry_mask <= terrain_handoff),
        "Canopy geometry is not layered over the continuous terrain handoff",
    )
    assigned = terrain_handoff
    require(np.array_equal(assigned, eligible), "Canopy source ownership is incomplete")
    source_in_geometry_radius = eligible & (radius <= GEOMETRY_OUTER_RADIUS_METERS)
    area_ratio = float(
        np.count_nonzero(geometry_mask)
        / max(1, np.count_nonzero(source_in_geometry_radius))
    )
    require(
        canopy_manifest["coverage"]["screen_relevant_grove_selection"]
        == expected_grove_metrics,
        "Screen-relevant canopy source selection is not deterministic",
    )
    active_filter = canopy_manifest["coverage"]["geometry_component_filter"]
    require(
        active_filter == expected_filter_metrics,
        "Canopy geometry component filter is not deterministic",
    )
    require(
        canopy_manifest["coverage"]["semantic_grove_compilation"]
        == compiled_grove_metrics,
        "Semantic grove compilation metrics are not deterministic",
    )
    rejected_candidate = canopy_manifest["coverage"]["rejected_raised_geometry_candidate"]
    require(rejected_candidate["active"] is False, "Rejected raised canopy candidate became active")
    require(
        rejected_candidate["component_filter"] == expected_filter_metrics,
        "Rejected canopy candidate component filter is not deterministic",
    )
    require(
        int(rejected_candidate["ownership_pixel_count"])
        == int(np.count_nonzero(candidate_geometry)),
        "Rejected canopy candidate ownership count changed",
    )
    require(
        int(rejected_candidate["triangle_count_if_emitted"])
        == 2
        * (
            int(np.count_nonzero(candidate_coarse))
            + int(np.count_nonzero(candidate_refinement))
        ),
        "Rejected canopy candidate topology metric changed",
    )
    require(
        abs(
            float(canopy_manifest["coverage"]["geometry_semantic_source_support_fraction"])
            - 1.0
        )
        <= 1e-12,
        "Canopy geometry semantic support metric changed",
    )
    require(float(canopy_manifest["combined_representation_area_agreement_percent"]) == 100.0, "Combined canopy assignment agreement changed")
    source_geometry_heights = canopy_height[expected_geometry]
    expected_median_source_height = float(np.median(source_geometry_heights))
    expected_median_authored_height = float(np.median([
        float(item["height"]) for item in groves
    ]))
    expected_height_agreement = 100.0 * (
        expected_median_authored_height / max(0.001, expected_median_source_height)
    )
    require(
        math.isclose(
            float(canopy_manifest["median_height_agreement_percent"]),
            expected_height_agreement,
            rel_tol=0.0,
            abs_tol=1e-12,
        ),
        "Semantic grove height calibration changed",
    )
    canopy_records = [item for item in canopy_manifest["assets"] if item.get("semantic") == "production_canopy_surface_mesh_batch"]
    canopy_obj = validate_obj_records(
        canopy_records, "ProductionCanopySurfaceLayer_", 4
    )
    require(int(canopy_manifest["budgets"]["mesh_batch_count"]) == 4, "Canopy mesh batch count changed")
    require(
        MINIMUM_STATIC_MESH_TRIANGLES
        <= int(canopy_manifest["budgets"]["mesh_batch_total_triangles"])
        <= MAXIMUM_MESH_TRIANGLES,
        "Canopy triangle budget changed",
    )
    grove_metrics = canopy_manifest["coverage"]["semantic_grove_compilation"]
    require(
        float(grove_metrics["source_coverage_fraction_0_1500m"]) >= 0.95,
        "Semantic canopy geometry coverage fell below 95%",
    )
    require(
        int(grove_metrics["selected_sparse_grove_count"]) > 0,
        "Sparse fixed canopy proxies are missing",
    )
    require(
        int(canopy_manifest["budgets"]["mesh_batch_total_triangles"])
        == int(canopy_obj["triangle_count"]),
        "Canopy OBJ triangle inventory changed",
    )
    require(
        int(canopy_manifest["budgets"]["mesh_batch_total_vertices"])
        == int(canopy_obj["vertex_count"]),
        "Canopy OBJ vertex inventory changed",
    )
    require(
        int(canopy_manifest["topology"]["skirt_quad_count"]) == 0,
        "Canopy vertical boundary skirts remain",
    )
    topology = canopy_manifest["topology"]
    require(int(topology["mid_connected_grove_count"]) > 0, "Mid connected groves are missing")
    require(int(topology["mid_perforated_crown_count"]) > 0, "Mid perforated crowns are missing")
    require(int(topology["grounded_trunk_count"]) > 0, "Grounded canopy trunks are missing")
    require(
        int(topology["near_branch_triangle_count"]) == 0
        and int(topology["near_foliage_card_triangle_count"]) == 0,
        "Obsolete near foliage-card topology remains in the static canopy",
    )
    require(
        int(topology["runtime_close_tree_instance_count"]) >= 100,
        "Source-derived runtime close-tree instances are missing",
    )
    require(
        sum(
            int(topology[key])
            for key in (
                "connected_crown_triangle_count",
                "grounded_trunk_triangle_count",
                "near_branch_triangle_count",
                "near_foliage_card_triangle_count",
                "mid_foliage_card_triangle_count",
                "sparse_foliage_card_triangle_count",
            )
        ) == int(canopy_obj["triangle_count"]),
        "Canopy topology categories do not equal the OBJ triangle inventory",
    )
    require(
        canopy_manifest["representation"]["type"]
        == PRODUCT_CANOPY_POLICY,
        "Canopy product policy changed",
    )

    terrain_manifest = manifests["terrain"]
    require(
        terrain_manifest.get("semantic_root") == "ProductionTerrainSurfaceLayer",
        "Product terrain semantic root changed",
    )
    terrain_records = [
        item
        for item in terrain_manifest["assets"]
        if item.get("semantic") == "product_aeqd_bare_earth_fixed_mesh"
    ]
    require(len(terrain_records) == 1, "Expected one product-AEQD terrain mesh")
    terrain_path = verify_record(terrain_records[0])
    require(terrain_path == (TERRAIN_OUTPUT_DIR / TERRAIN_MESH_NAME).resolve(), "Product terrain mesh path changed")
    terrain_parsed = parse_obj(terrain_path)
    require(
        terrain_parsed["objects"] == [TERRAIN_MESH_OBJECT_NAME]
        and terrain_parsed["groups"] == [TERRAIN_MESH_OBJECT_NAME],
        "Product terrain OBJ identity changed",
    )
    require(not terrain_parsed["forbidden_material_lines"], "Product terrain OBJ embeds a material")
    require(
        len(terrain_parsed["faces"]) == TERRAIN_MAXIMUM_TRIANGLES,
        "Product terrain fixed triangle budget changed",
    )
    require(
        len(terrain_parsed["vertices"]) == int(terrain_manifest["budgets"]["vertex_count"]),
        "Product terrain vertex count changed",
    )
    terrain_vertices = np.asarray(terrain_parsed["vertices"], dtype=np.float64)
    require(
        np.array_equal(np.min(terrain_vertices[:, (0, 2)], axis=0), np.asarray([-2048.0, -2048.0]))
        and np.array_equal(np.max(terrain_vertices[:, (0, 2)], axis=0), np.asarray([2048.0, 2048.0])),
        "Product terrain border is not locked to the exact 4096 m square",
    )
    expected_terrain = derive_product_terrain()
    expected_terrain_payload = product_terrain_obj_text(expected_terrain).encode("utf-8")
    require(
        sha256_bytes(expected_terrain_payload) == sha256_file(terrain_path),
        "Product terrain OBJ is not the deterministic exact-projection derivative",
    )
    terrain_report_path = (TERRAIN_OUTPUT_DIR / TERRAIN_REPORT_NAME).resolve()
    terrain_report = load_json(terrain_report_path)
    require(terrain_report.get("schema") == TERRAIN_REPORT_SCHEMA, "Product terrain report schema changed")
    require(
        terrain_report == expected_terrain["report"],
        "Product terrain report is not the deterministic exact-projection evidence",
    )
    require(
        terrain_report["border_lock"]["exact_4096_meter_product_square"] is True,
        "Product terrain report does not prove the border lock",
    )
    require(
        terrain_report["geometric_integration_eligibility"]["status"] == "ELIGIBLE",
        "Product terrain reconstruction exceeds its declared geometric gate",
    )
    require(
        float(
            terrain_report["comparison"]["runway_mesh_vs_input_plane"][
                "maximum_absolute_meters"
            ]
        )
        <= 1.0e-5,
        "Product-coordinate runway preservation exceeds its reconstruction gate",
    )
    historical_terrain = terrain_manifest["sources"]["historical_fixed_near_terrain"]
    require(
        historical_terrain["path"].endswith("AerialSceneryV1/Lidar/KBDU_Lidar_NearTerrain_Fixed.obj")
        and historical_terrain["sha256"]
        == "4689de66d4b47bdfc1a3ea6e8c33e906d4308bbb77317f23e3dc5185b5cefce8",
        "Historical AerialSceneryV1 near terrain changed",
    )

    root_meta = Path(str(root) + ".meta")
    require(root_meta.is_file(), "PhotorealAerialSurfaceV1 folder meta is missing")
    require(f"guid: {ROOT_META_GUID}" in root_meta.read_text(encoding="utf-8"), "PhotorealAerialSurfaceV1 root GUID changed")
    for directory in (
        root / "Lidar",
        root / "Buildings",
        BUILDING_GENERATED_DIR,
        root / "Canopy",
        CANOPY_GENERATED_DIR,
        root / "Terrain",
    ):
        require(Path(str(directory) + ".meta").is_file(), f"Folder meta is missing: {directory}")
    require(CANOPY_GENERATED_DIR.is_dir(), "Canopy OBJ batch directory is missing")
    require(
        len(tuple(CANOPY_GENERATED_DIR.glob("ProductionCanopySurfaceLayer_*.obj"))) == 4,
        "Canopy OBJ batch inventory changed",
    )
    for directory in (root / "Lidar", root / "Buildings", root / "Canopy", root / "Terrain"):
        for path in directory.rglob("*"):
            if path.is_file() and path.suffix != ".meta":
                require(Path(str(path) + ".meta").is_file(), f"Asset meta is missing: {path}")
    forbidden = [path for path in root.rglob("*") if path.is_file() and path.suffix.lower() in {".ept", ".las", ".laz"}]
    require(not forbidden, "Raw EPT/LAS/LAZ entered the committed runtime asset root")
    require(not (root / "Canopy" / "ProductionCanopySurfaceLayer_Mask_R8.png").exists(), "Obsolete canopy mask remains")

    ownership = manifests["ownership"]
    require(ownership["exclusions"]["runtime_hierarchy_construction_allowed"] is False, "Ownership contract permits runtime hierarchy construction")
    require(ownership["exclusions"]["sparse_tree_scatter_allowed"] is False, "Ownership contract permits sparse scatter")
    require(ownership["exclusions"]["canopy_geometry_is_disabled"] is False, "Ownership contract disables canopy geometry")
    require(
        ownership["exclusions"]["canopy_geometry_overlays_continuous_terrain_handoff"] is True,
        "Ownership contract removed continuous canopy terrain shading",
    )
    require(
        ownership["canopy_ownership"]["product_policy"]
        == PRODUCT_CANOPY_POLICY,
        "Ownership contract changed canopy product policy",
    )
    return {
        "building": {**building_obj, "record_count": len(building_layer["records"])},
        "canopy": {
            **canopy_obj,
            "combined_source_assignment_percent": 100.0,
            "geometry_area_ratio": float(area_ratio),
            "geometry_component_count": len(canopy_layer["components"]),
            "median_height_agreement_percent": float(canopy_manifest["median_height_agreement_percent"]),
        },
        "file_record_count_verified": record_count,
        "recorded_path_count": len(recorded_paths),
        "horizontal_frame": {
            "diagnostic_affine_maximum_residual_meters": float(
                affine_contract["maximum_residual_meters"]
            ),
            "exact_round_trip_maximum_residual_meters": float(
                exact_contract["maximum_round_trip_residual_meters"]
            ),
            "maximum_product_offset_from_utm_delta_meters": float(
                expected_frame_contract["maximum_product_offset_from_utm_delta_meters"]
            ),
            "runtime_normal_rotation_degrees_counterclockwise": float(
                runtime_affine["utm_normal_to_product_rotation_degrees_counterclockwise"]
            ),
            "runtime_position_affine_maximum_residual_meters": float(
                runtime_affine["position_maximum_residual_meters"]
            ),
        },
        "root": root.as_posix(),
        "schemas": EXPECTED_SCHEMAS,
        "status": "PASS",
        "terrain": {
            "border_locked": True,
            "geometric_integration_status": terrain_report[
                "geometric_integration_eligibility"
            ]["status"],
            "runway_maximum_plane_error_meters": float(
                terrain_report["comparison"]["runway_mesh_vs_input_plane"][
                    "maximum_absolute_meters"
                ]
            ),
            "triangle_count": len(terrain_parsed["faces"]),
            "vertex_count": len(terrain_parsed["vertices"]),
        },
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=RUNTIME_ROOT)
    parser.add_argument("--json-output", type=Path)
    parser.add_argument("--quiet", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        report = validate(args.root)
    except Exception as error:
        failure = {"error": str(error), "status": "FAIL", "type": type(error).__name__}
        if args.json_output:
            args.json_output.write_text(json.dumps(failure, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        print(json.dumps(failure, indent=2, sort_keys=True), file=sys.stderr)
        return 1
    if args.json_output:
        args.json_output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    if not args.quiet:
        print(json.dumps(report, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
