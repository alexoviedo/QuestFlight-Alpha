#!/usr/bin/env python3
"""Validate the committed Photoreal Aerial Surface V1 asset contract.

This is a read-only batch validator.  It requires only committed compact assets;
raw EPT/LAZ data and network access are neither used nor allowed.
"""

from __future__ import annotations

import argparse
import json
import math
import subprocess
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
    decode_points,
    sampled_hausdorff_meters,
    zone_records,
    FLAG_ZONE_2_TO_4_KM,
    MAXIMUM_HAUSDORFF_ERROR_BY_BAND,
    HORIZONTAL_QUANTIZATION_METERS,
    signed_area2,
)
from build_canopy_surfaces import (
    GENERATED_DIR as CANOPY_GENERATED_DIR,
    GEOMETRY_MASK_NAME,
    GEOMETRY_OUTER_RADIUS_METERS,
    LAYER_NAME as CANOPY_LAYER_NAME,
    MANIFEST_NAME as CANOPY_MANIFEST_NAME,
    MAXIMUM_MESH_TRIANGLES,
    ObjBatch,
    mesh_batch_key,
    near_tree_instances,
    NEAR_TREE_TRIANGLES,
    NEAR_INSTANCE_MANIFEST_NAME,
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
    canonical_json_bytes,
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

# This historical source record was produced from CRLF text; Git now explicitly
# checks JSON out as LF. Preserve the historical identity, and admit only its
# exact reconstructed bytes. Generated artifacts and binary inputs stay strict.
REVIEWED_CRLF_SOURCE_RECORDS = {
    "tools/kbdu_airport_hero_corridor/airport_hero_corridor_manifest.json": (
        64_536,
        "5f17f15ffe592e78f4eefa4e1b320dcdf408873c536e479dbe769e1fafc43779",
    ),
}
HISTORICAL_TOPOLOGY_COMMON_IDENTITY = (
    "tools/scenery/lidar/common.py",
    16_443,
    "e4fea841f6f2601bd9b49162a1c47c55406f75358e3e7c97b4a6c155c09c99ed",
)
HISTORICAL_TOPOLOGY_SOURCE_COMMIT = "65d35377e16da50b3a7d8d4fb6200a1860272ba3"
HISTORICAL_CANOPY_BUILDING_IDENTITY = (
    "QuestFlight/Assets/Production/Environment/PhotorealAerialSurfaceV1/Buildings/ProductionBuildingSurfaceLayer_Manifest.json",
    46_708,
    "7869dad7df1bf31e5fdbb4794bf5420805e47a9cb480205de92294a8b8993a32",
)


def validate_horizontal_frame_contract(recorded: Any, recomputed: dict[str, Any]) -> dict[str, Any]:
    """Compare fitted double-precision results without requiring identical BLAS rounding.

    CRS, origins, formulas, specifications, types and structure remain exact.
    Only the enumerated fitted leaves admit rounding, many orders below the
    unchanged geometric acceptance limits. Serialized frames must also agree.
    """
    differences: list[dict[str, Any]] = []

    def tolerance(path: tuple[Any, ...]) -> float | None:
        if path[0] == "diagnostic_affine_product_from_utm_delta":
            if len(path) == 4 and path[1] == "matrix_xz" and path[2] in (0, 1) and path[3] in (0, 1):
                return 1e-14
            if len(path) == 3 and path[1] == "offset_xz_meters" and path[2] in (0, 1):
                return 1e-11
            if path == ("diagnostic_affine_product_from_utm_delta", "maximum_residual_meters"):
                return 1e-11
        if len(path) == 2 and path[0] == "runtime_affine_coefficients":
            key = path[1]
            if key in {"position_maximum_residual_meters", "product_to_utm_x_offset_meters", "product_to_utm_z_offset_meters"}:
                return 1e-11
            if key == "utm_normal_to_product_rotation_degrees_counterclockwise":
                return 1e-12
            if key in RUNTIME_AFFINE_KEYS - {
                "position_maximum_allowed_residual_meters", "position_maximum_residual_meters",
                "product_to_utm_x_offset_meters", "product_to_utm_z_offset_meters",
                "utm_normal_to_product_rotation_degrees_counterclockwise",
            }:
                return 1e-14
        return None

    def compare(actual: Any, expected: Any, path: tuple[Any, ...] = ()) -> None:
        label = ".".join(str(part) for part in path)
        if isinstance(expected, dict):
            require(isinstance(actual, dict) and actual.keys() == expected.keys(), f"Horizontal-frame structure changed: {label}")
            for key, value in expected.items():
                compare(actual[key], value, (*path, key))
        elif isinstance(expected, list):
            require(isinstance(actual, list) and len(actual) == len(expected), f"Horizontal-frame structure changed: {label}")
            for index, value in enumerate(expected):
                compare(actual[index], value, (*path, index))
        else:
            epsilon = tolerance(path) if path else None
            if epsilon is not None:
                require(type(actual) is float and isinstance(expected, (float, np.floating)), f"Horizontal-frame fitted scalar type changed: {label}")
                require(math.isfinite(actual) and math.isfinite(expected), f"Horizontal-frame nonfinite scalar: {label}")
                delta = abs(actual - expected)
                require(delta <= epsilon, f"Horizontal-frame fitted coefficient changed: {label}")
                if delta:
                    differences.append({"path": label, "recorded": actual, "recomputed": float(expected), "absolute_delta": delta, "roundoff_limit": epsilon})
            else:
                require(type(actual) is type(expected) and actual == expected, f"Horizontal-frame exact field changed: {label}")

    compare(recorded, recomputed)
    return {"fitted_roundoff_differences": differences, "physical_residual_limits_changed": False}


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


def verify_record(
    record: dict[str, Any],
    source_text_verifications: list[dict[str, Any]] | None = None,
) -> Path:
    path = resolve_logical(str(record["path"]))
    require(path.is_file(), f"Recorded file is missing: {path}")
    reviewed_identity = REVIEWED_CRLF_SOURCE_RECORDS.get(str(record["path"]))
    if reviewed_identity == (int(record["bytes"]), str(record["sha256"])):
        actual = path.read_bytes()
        actual_hash = sha256_bytes(actual)
        exact = (len(actual), actual_hash) == reviewed_identity
        reconstructed = actual.replace(b"\n", b"\r\n")
        normalized = (
            b"\r" not in actual
            and b"\x00" not in actual
            and (len(reconstructed), sha256_bytes(reconstructed)) == reviewed_identity
        )
        require(exact or normalized, f"Reviewed historical source text changed: {path}")
        if source_text_verifications is not None:
            source_text_verifications.append({
                "path": str(record["path"]),
                "recorded_bytes": int(record["bytes"]),
                "recorded_sha256": str(record["sha256"]),
                "actual_bytes": len(actual),
                "actual_sha256": actual_hash,
                "verification_method": "exact_bytes" if exact else "exact_recorded_crlf_reconstruction_from_git_lf",
            })
        return path
    require(path.stat().st_size == int(record["bytes"]), f"Recorded byte count changed: {path}")
    require(sha256_file(path) == str(record["sha256"]), f"Recorded SHA-256 changed: {path}")
    return path


def verify_historical_topology_common(record: dict[str, Any]) -> tuple[Path, dict[str, Any]]:
    return _verify_audited_authoring_input(record, HISTORICAL_TOPOLOGY_COMMON_IDENTITY, "terrain.sources.historical_topology_common")


def verify_historical_canopy_building(record: dict[str, Any]) -> tuple[Path, dict[str, Any]]:
    return _verify_audited_authoring_input(record, HISTORICAL_CANOPY_BUILDING_IDENTITY, "canopy.sources.building_manifest")


def _verify_audited_authoring_input(record: dict[str, Any], expected_identity: tuple, role: str) -> tuple[Path, dict[str, Any]]:
    identity = (str(record["path"]), int(record["bytes"]), str(record["sha256"]))
    require(identity == expected_identity, f"Historical authoring input identity changed: {role}")
    path = resolve_logical(identity[0])
    require(path.is_file(), f"Current authoring source is missing: {path}")
    try:
        result = subprocess.run(
            ["git", "show", f"{HISTORICAL_TOPOLOGY_SOURCE_COMMIT}:{identity[0]}"],
            cwd=REPOSITORY_ROOT, capture_output=True, timeout=10, check=False,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise ValidationFailure("Exact historical topology source Git object is unavailable") from exc
    require(result.returncode == 0, "Exact historical topology source Git object is unavailable")
    require(
        (len(result.stdout), sha256_bytes(result.stdout)) == identity[1:],
        "Historical topology source Git object does not match its recorded byte identity",
    )
    actual = path.read_bytes()
    return path, {
        "path": identity[0],
        "historical_input_role": role,
        "recorded_bytes": identity[1],
        "recorded_sha256": identity[2],
        "historical_source_commit": HISTORICAL_TOPOLOGY_SOURCE_COMMIT,
        "verification_method": "exact_historical_authoring_source_git_object",
        "current_source_bytes": len(actual),
        "current_source_sha256": sha256_bytes(actual),
        "current_source_matches_historical_record": actual == result.stdout,
        "current_source_claimed_to_have_generated_existing_terrain": False,
        "current_input_claimed_to_have_generated_existing_derivatives": False,
    }


def verify_manifest_records(
    manifests: Iterable[dict[str, Any]],
    source_text_verifications: list[dict[str, Any]] | None = None,
    historical_source_verifications: list[dict[str, Any]] | None = None,
) -> tuple[int, set[Path]]:
    unique: dict[str, dict[str, Any]] = {}
    historical_inputs: list[tuple[dict[str, Any], Any]] = []
    for manifest in manifests:
        historical_record: dict[str, Any] | None = None
        if manifest.get("schema") == EXPECTED_SCHEMAS["terrain"]:
            historical_record = manifest.get("sources", {}).get("historical_topology_common")
            require(isinstance(historical_record, dict), "Required historical topology source record is missing")
            historical_inputs.append((historical_record, verify_historical_topology_common))
        elif manifest.get("schema") == EXPECTED_SCHEMAS["canopy"]:
            historical_record = manifest.get("sources", {}).get("building_manifest")
            require(isinstance(historical_record, dict), "Required historical canopy building input is missing")
            historical_inputs.append((historical_record, verify_historical_canopy_building))
        for record in nested_file_records(manifest):
            if record is historical_record:
                continue
            if str(record["path"]) in unique:
                previous = unique[str(record["path"])]
                require((previous["bytes"], previous["sha256"]) == (record["bytes"], record["sha256"]),
                        "Conflicting byte identity for a shared manifest input")
            unique[str(record["path"])] = record
    paths: set[Path] = set()
    for record in unique.values():
        path = verify_record(record, source_text_verifications)
        paths.add(path.resolve())
    for record, verifier in historical_inputs:
        path, evidence = verifier(record)
        if historical_source_verifications is not None:
            historical_source_verifications.append(evidence)
        paths.add(path.resolve())
    return len(unique) + len(historical_inputs), paths


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


def validate_building_simplification(simplification, measured_by_band):
    require(simplification.get("maximum_allowed_hausdorff_meters") == MAXIMUM_HAUSDORFF_ERROR_BY_BAND,
            "Building per-band simplification limits changed")
    require(set(measured_by_band) == set(MAXIMUM_HAUSDORFF_ERROR_BY_BAND),
            "Building per-band simplification evidence is incomplete")
    for band, maximum in measured_by_band.items():
        require(math.isfinite(maximum) and 0 <= maximum <= MAXIMUM_HAUSDORFF_ERROR_BY_BAND[band] + 1e-9,
                f"Building simplification error exceeds {MAXIMUM_HAUSDORFF_ERROR_BY_BAND[band]:g} m in {band}")
    recorded = simplification.get("sampled_hausdorff_maximum_meters_by_band")
    if recorded is not None:
        require(set(recorded) == set(measured_by_band) and all(
            math.isclose(float(recorded[band]), maximum, abs_tol=1e-8, rel_tol=0)
            for band, maximum in measured_by_band.items()), "Building per-band measured maxima changed")
    require(math.isclose(float(simplification["sampled_hausdorff_maximum_meters"]), max(measured_by_band.values()),
                         abs_tol=1e-8, rel_tol=0), "Building aggregate measured maximum changed")


def measure_building_simplification(records, features, zones):
    measured = {band: 0.0 for band in MAXIMUM_HAUSDORFF_ERROR_BY_BAND}
    for record in records:
        index = int(record["feature_index"])
        require(index in zones and 0 <= index < len(features), "Building simplification source identity is invalid")
        band = zones[index]
        require(band in measured, "Unknown building ownership band")
        require(bool(record["flags"] & FLAG_ZONE_2_TO_4_KM) == (band == "2-4km"),
                "Building binary ownership band disagrees with the source recipe")
        value = sampled_hausdorff_meters(decode_points(features[index]), record["points"])
        measured[band] = max(measured[band], value)
    return measured


def validate_building_coverage(layer: dict, manifest: dict, context: dict, recipe: dict,
                              source_coverage: dict, core_heroes: dict, corridor_heroes: dict) -> dict:
    """Conserve exact source footprint identities across authored hero/nonhero owners."""
    features = context["openstreetmap"]["features"]
    zones: dict[int, str] = {}
    for band in MAXIMUM_HAUSDORFF_ERROR_BY_BAND:
        source_indices = zone_records(recipe, band)
        require(len(source_indices) == source_coverage["zones"][band]["source_footprints"], f"{band} source recipe denominator changed")
        require(not (zones.keys() & source_indices.keys()), "Building source bands overlap")
        zones.update({index: band for index in source_indices})
    hero_ids = {int(row["source_osm_id"]) for row in core_heroes["replacements"]}
    hero_ids.update(int(row["osm_id"]) for row in corridor_heroes["buildings"])
    hero_indices = {index for index in zones if int(features[index]["source"]["osm_id"]) in hero_ids}
    require(len(hero_indices) == len(hero_ids), "Every source hero replacement must map to exactly one owned footprint")
    expected_heroes = [{
        "area_square_meters": abs(signed_area2(decode_points(features[index]))) * HORIZONTAL_QUANTIZATION_METERS ** 2 * 0.5,
        "feature_index": index,
        "osm_id": int(features[index]["source"]["osm_id"]),
        "owner": "ProductionHeroObjectLayer",
        "zone": zones[index],
    } for index in sorted(hero_indices)]
    coverage = manifest["coverage"]
    require(coverage["hero_exclusions"] == expected_heroes, "Hero exclusion identities/footprints differ from pinned source owners")
    actual_indices = [int(record["feature_index"]) for record in layer["records"]]
    require(len(actual_indices) == len(set(actual_indices)), "Duplicate building identity in compiled payload")
    require(set(actual_indices) == zones.keys() - hero_indices, "Compiled buildings do not exactly conserve nonhero source footprints")
    for record in layer["records"]:
        band = zones[int(record["feature_index"])]
        require(bool(record["flags"] & FLAG_ZONE_2_TO_4_KM) == (band == "2-4km"), "Compiled building band differs from source recipe")
    for band in MAXIMUM_HAUSDORFF_ERROR_BY_BAND:
        original_count = sum(zone == band for zone in zones.values())
        hero_count = sum(zones[index] == band for index in hero_indices)
        nonhero_count = original_count - hero_count
        expected = {
            "coverage_fraction": 1.0, "hero_exclusion_count": hero_count,
            "original_source_footprint_count": original_count, "represented_footprint_count": nonhero_count,
            "source_footprint_count": nonhero_count, "status": "PASS",
        }
        require(coverage["per_band"][band] == expected, f"{band} coverage report differs from exact source conservation")
    require(coverage["hero_exclusion_count"] == len(hero_indices), "Hero coverage aggregate changed")
    require(coverage["hero_exclusion_area_square_meters"] == sum(item["area_square_meters"] for item in expected_heroes), "Hero area aggregate changed")
    require(coverage["owned_source_footprint_count"] == coverage["source_footprint_count"] == len(actual_indices), "Nonhero coverage aggregate changed")
    return {"per_band": coverage["per_band"], "exact_source_footprint_conservation": True}


def validate_canopy_tier_inventory(manifest: dict, obj_inventory: dict, instances: dict,
                                  expected_static: dict, expected_instances: list[dict]) -> dict:
    require(
        0 < expected_static["triangle_count"] <= MAXIMUM_MESH_TRIANGLES,
        "Source-derived static canopy exceeds its unchanged hard ceiling",
    )
    require(
        obj_inventory["triangle_count"] == manifest["budgets"]["mesh_batch_total_triangles"] == expected_static["triangle_count"]
        and obj_inventory["vertex_count"] == manifest["budgets"]["mesh_batch_total_vertices"] == expected_static["vertex_count"],
        "Canopy static topology does not conserve exact compiled source geometry",
    )
    require(instances["schema"] == "questflight.airport-close-tree-instances.v1", "Close-tree instance schema changed")
    require(len(instances["instances"]) == len(expected_instances), "Close-tree instance source count changed")
    instance_roundoff: list[dict] = []
    for index, (actual, expected) in enumerate(zip(instances["instances"], expected_instances)):
        require(actual.keys() == expected.keys(), "Close-tree instance field structure changed")
        for field, value in expected.items():
            if field in {"x", "z", "crown_diameter"}:
                require(type(actual[field]) is float and isinstance(value, (float, np.floating))
                        and math.isfinite(actual[field]) and math.isfinite(value), "Close-tree reconstructed scalar type/nonfinite value changed")
                delta = abs(actual[field] - value)
                require(delta <= 1e-9, "Close-tree source position/diameter changed beyond numerical replay roundoff")
                if delta:
                    instance_roundoff.append({"instance": index, "field": field,
                                              "absolute_delta_meters": delta, "roundoff_limit_meters": 1e-9})
            else:
                require(type(actual[field]) is type(value) and actual[field] == value,
                        "Close-tree source instance exact field/order changed")
    count = len(expected_instances)
    require(0 < count <= 1100, "Close-tree source instance count exceeds its authored ceiling")
    require(
        instances["instance_count"] == manifest["topology"]["runtime_close_tree_instance_count"]
        == manifest["budgets"]["runtime_sparse_tree_instances_authored"] == count,
        "Close-tree instance inventory differs from source ownership",
    )
    require(manifest["topology"]["removed_near_card_triangle_count"] == count * NEAR_TREE_TRIANGLES,
            "Removed static near cards are not conserved by the authored close-tree tier")
    return {**expected_static, "source_derived_close_tree_instance_count": count, "source_tier_geometry_conserved": True,
            "static_hard_triangle_ceiling": MAXIMUM_MESH_TRIANGLES,
            "close_tree_reconstructed_roundoff_differences": instance_roundoff,
            "obsolete_static_30000_floor_applied": False}


def validate_terrain_replay(derived: dict, recorded_frame: dict, obj_payload: bytes, report: dict) -> dict:
    # The source fingerprint incorporates serialized fitted coefficients. The
    # independently checked historical frame, not new BLAS rounding, identifies
    # these existing outputs. Geometry is recomputed and remains byte-exact.
    validate_horizontal_frame_contract(recorded_frame, derived["frame_contract"])
    source_contract = {
        "coordinate_mapping": derived["mapping"],
        "digital_terrain_model": asset_by_semantic(derived["source"], "digital_terrain_model"),
        "horizontal_frame_contract": recorded_frame,
    }
    historical_fingerprint = sha256_bytes(canonical_json_bytes(source_contract))
    require(report.get("source_contract_sha256") == historical_fingerprint, "Terrain historical source fingerprint does not bind its exact inputs")
    replay = {**derived, "source_contract_sha256": historical_fingerprint}
    expected_payload = product_terrain_obj_text(replay).encode("utf-8")
    require(obj_payload == expected_payload, "Product terrain OBJ geometry or exact historical source identity changed")
    expected_report = {**derived["report"], "horizontal_frame_contract": recorded_frame,
                       "source_contract_sha256": historical_fingerprint}
    differences: list[dict] = []

    def compare(actual: Any, expected: Any, path: tuple = ()) -> None:
        label = ".".join(str(part) for part in path)
        if isinstance(expected, dict):
            require(isinstance(actual, dict) and actual.keys() == expected.keys(), f"Terrain report structure changed: {label}")
            for key, value in expected.items():
                compare(actual[key], value, (*path, key))
        elif isinstance(expected, list):
            require(isinstance(actual, list) and len(actual) == len(expected), f"Terrain report structure changed: {label}")
            for index, value in enumerate(expected):
                compare(actual[index], value, (*path, index))
        else:
            computed_reduction = (
                len(path) == 4 and path[0] == "comparison"
                and path[1] in {"mesh_vs_authored_target", "mesh_vs_sampled_bare_earth_before_runway_preservation"}
                or len(path) == 3 and path[:2] == ("geometric_integration_eligibility", "observed_outside_declared_outliers")
            ) and path[-1] in {"mean_signed_meters", "mean_absolute_meters", "rmse_meters"}
            if computed_reduction:
                require(type(actual) is float and type(expected) is float and math.isfinite(actual) and math.isfinite(expected), f"Terrain report reduction type/nonfinite value changed: {label}")
                delta = abs(actual - expected)
                require(delta <= 1e-11, f"Terrain report computed reduction changed: {label}")
                if delta:
                    differences.append({"path": label, "recorded": actual, "recomputed": expected,
                                        "absolute_delta_meters": delta, "roundoff_limit_meters": 1e-11})
            else:
                require(type(actual) is type(expected) and actual == expected, f"Terrain report exact field changed: {label}")

    compare(report, expected_report)
    return {"exact_obj_bytes_verified": len(obj_payload), "exact_obj_sha256": sha256_bytes(obj_payload),
            "historical_source_contract_sha256": historical_fingerprint,
            "current_recomputed_source_contract_sha256": derived["source_contract_sha256"],
            "computed_reduction_roundoff_differences": differences,
            "positions_faces_bounds_maxima_percentiles_thresholds_anchors_exact": True,
            "assets_or_producer_lineage_changed": False}


def validate(root: Path = RUNTIME_ROOT, *, ownership_manifest_path: Path | None = None) -> dict[str, Any]:
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
    if ownership_manifest_path is not None:
        from refresh_ownership_metadata import prepare_refresh
        proposed_path = ownership_manifest_path.resolve()
        require(not proposed_path.is_relative_to(REPOSITORY_ROOT.resolve()), "Ownership proposal must be external")
        proposed, _ = prepare_refresh(
            manifest_paths["ownership"].read_bytes(), manifest_paths["building"].read_bytes(),
        )
        require(proposed_path.read_bytes() == proposed, "External ownership proposal is not the exact reviewed metadata-only refresh")
        manifest_paths["ownership"] = proposed_path
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

    source_text_verifications: list[dict[str, Any]] = []
    historical_source_verifications: list[dict[str, Any]] = []
    record_count, recorded_paths = verify_manifest_records(
        manifests.values(), source_text_verifications, historical_source_verifications,
    )
    source = load_json(SOURCE_MANIFEST)
    mapping = source["coordinate_mapping"]
    expected_frame_contract = horizontal_frame_contract(mapping)
    recorded_frame_contract = manifests["dtm_dsm"].get("horizontal_frame_contract")
    frame_verification = validate_horizontal_frame_contract(recorded_frame_contract, expected_frame_contract)
    for name in ("dtm_dsm", "classification", "building", "canopy", "ownership", "terrain"):
        require(
            manifests[name].get("horizontal_frame_contract") == recorded_frame_contract,
            f"{name} serialized horizontal-frame transform contract differs from the other manifests",
        )
    exact_contract = expected_frame_contract["exact_conversion"]
    affine_contract = recorded_frame_contract["diagnostic_affine_product_from_utm_delta"]
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
    runtime_affine = recorded_frame_contract.get("runtime_affine_coefficients", {})
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
    require(float(per_band["2-4km"]["coverage_fraction"]) >= 0.98, "Mid building coverage below 98%")
    simplification = building_manifest["topology"]["footprint_simplification"]
    require(float(simplification["area_ratio_maximum"]) <= 1.05, "Building footprint inflation exceeds 5%")
    building_context = load_json(resolve_logical(building_manifest["sources"]["osm_context"]["path"]))
    building_recipe = load_json(resolve_logical(building_manifest["sources"]["placement_recipe"]["path"]))
    building_coverage_verification = validate_building_coverage(
        building_layer, building_manifest, building_context, building_recipe,
        load_json(resolve_logical(building_manifest["sources"]["coverage_report"]["path"])),
        load_json(resolve_logical(building_manifest["sources"]["hero_replacements"]["path"])),
        load_json(resolve_logical(building_manifest["sources"]["airport_hero_corridor_replacements"]["path"])),
    )
    zones = {index: band for band in MAXIMUM_HAUSDORFF_ERROR_BY_BAND
             for index in zone_records(building_recipe, band)}
    measured_by_band = measure_building_simplification(building_layer["records"],
        building_context["openstreetmap"]["features"], zones)
    validate_building_simplification(simplification, measured_by_band)
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
    source_batches = {key: ObjBatch(key) for key in ((-1, -1), (-1, 0), (0, -1), (0, 0))}
    for grove in groves:
        source_batches[mesh_batch_key(float(grove["x"]), float(grove["z"]))].add_grove(grove)
    expected_static_inventory = {
        "triangle_count": sum(len(batch.faces) for batch in source_batches.values()),
        "vertex_count": sum(len(batch.vertices) for batch in source_batches.values()),
    }
    expected_close_tree_instances = [instance for grove in groves if float(grove.get("near_detail", 0.0)) > 0.5
                                     for instance in near_tree_instances(grove)]
    canopy_tier_verification = validate_canopy_tier_inventory(
        canopy_manifest, canopy_obj, load_json(root / "Canopy" / NEAR_INSTANCE_MANIFEST_NAME),
        expected_static_inventory, expected_close_tree_instances,
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
    terrain_report_path = (TERRAIN_OUTPUT_DIR / TERRAIN_REPORT_NAME).resolve()
    terrain_report = load_json(terrain_report_path)
    require(terrain_report.get("schema") == TERRAIN_REPORT_SCHEMA, "Product terrain report schema changed")
    terrain_replay_verification = validate_terrain_replay(
        expected_terrain, recorded_frame_contract, terrain_path.read_bytes(), terrain_report,
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
        "building_coverage_verification": building_coverage_verification,
        "canopy": {
            **canopy_obj,
            "combined_source_assignment_percent": 100.0,
            "geometry_area_ratio": float(area_ratio),
            "geometry_component_count": len(canopy_layer["components"]),
            "median_height_agreement_percent": float(canopy_manifest["median_height_agreement_percent"]),
        },
        "canopy_tier_verification": canopy_tier_verification,
        "terrain_replay_verification": terrain_replay_verification,
        "file_record_count_verified": record_count,
        "recorded_path_count": len(recorded_paths),
        "source_text_record_verifications": source_text_verifications,
        "historical_authoring_source_verifications": historical_source_verifications,
        "horizontal_frame_contract_verification": frame_verification,
        "ownership_manifest_validation_path": str(manifest_paths["ownership"]),
        "external_ownership_proposal_used": ownership_manifest_path is not None,
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
    parser.add_argument("--ownership-manifest", type=Path, help="Exact external metadata-only proposal; never activated by this validator")
    parser.add_argument("--json-output", type=Path)
    parser.add_argument("--quiet", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        report = validate(args.root, ownership_manifest_path=args.ownership_manifest)
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
