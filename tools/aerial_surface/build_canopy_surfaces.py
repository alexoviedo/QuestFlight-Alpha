#!/usr/bin/env python3
"""Build the bounded semantic grove representation used by the product.

The tool is deliberately offline.  It compiles the already committed USGS 3DEP
height/density derivatives and the reviewed semantic exclusions into rounded,
grounded grove volumes.  The terrain canopy map remains the continuous base;
the opaque meshes add flight-visible crown volume and selective trunks without
per-tree GameObjects, camera-facing cards, or runtime point-cloud processing.
The tool never fetches or commits raw point clouds.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import struct
import zlib
from collections import defaultdict
from pathlib import Path
from typing import Any, Sequence

import numpy as np
from PIL import Image

from build_building_surfaces import (
    MANIFEST_NAME as BUILDING_MANIFEST_NAME,
    MASK_NAME as BUILDING_MASK_NAME,
    OUTPUT_DIR as BUILDING_OUTPUT_DIR,
    signed_tile,
    write_model_meta,
)
from build_dtm_dsm import (
    REPOSITORY_ROOT,
    RUNTIME_ROOT,
    SOURCE_MANIFEST,
    asset_by_semantic,
    file_record,
    load_json,
    verify_source_asset,
    write_folder_meta,
    write_json,
    write_text_meta,
    write_texture_meta,
)
from geospatial_frame import (
    horizontal_frame_contract,
    lidar_pixel_centers_product_aeqd,
    utm13n_delta_to_product_aeqd,
    utm13n_delta_points_to_product,
)


OUTPUT_DIR = RUNTIME_ROOT / "Canopy"
GENERATED_DIR = OUTPUT_DIR / "Generated"
LAYER_NAME = "ProductionCanopySurfaceLayer.bytes"
GEOMETRY_MASK_NAME = "ProductionCanopySurfaceLayer_GeometryOwnership_R8.png"
TERRAIN_HANDOFF_MASK_NAME = "ProductionCanopySurfaceLayer_TerrainMapHandoff_R8.png"
MANIFEST_NAME = "ProductionCanopySurfaceLayer_Manifest.json"
OWNERSHIP_MANIFEST_NAME = "KBDU_AerialSurface_OwnershipExclusionManifestV1.json"
LEGACY_MASK_NAMES = (
    "ProductionCanopySurfaceLayer_Mask_R8.png",
    "ProductionCanopySurfaceLayer_Mask_R8.png.meta",
)
BUILDING_MANIFEST = BUILDING_OUTPUT_DIR / BUILDING_MANIFEST_NAME
BUILDING_MASK = BUILDING_OUTPUT_DIR / BUILDING_MASK_NAME
OPERATIONAL_EXCLUSION_MASK = REPOSITORY_ROOT / (
    "QuestFlight/Assets/Production/Environment/ReconstructionV2/"
    "vegetation_exclusion_mask_v2.png"
)
OPERATIONAL_EXCLUSION_CONTRACT = REPOSITORY_ROOT / (
    "QuestFlight/Assets/Production/Environment/ReconstructionV2/"
    "vegetation_distribution_manifest.json"
)
REVIEWED_EXCLUSION_CONTRACT = REPOSITORY_ROOT / (
    "tools/aerial_surface/canopy_reviewed_exclusions.json"
)
REVIEWED_EXCLUSION_SCHEMA = (
    "questflight.photoreal-aerial-surface-v1.canopy-reviewed-exclusions.v1"
)
GROUND_IMAGERY_MANIFEST = REPOSITORY_ROOT / (
    "QuestFlight/Assets/Production/Environment/AerialSurfaceV1/Ground/"
    "KBDU_AerialSurfaceV1_SourceManifest.json"
)
GROUND_IMAGERY_NEAR_ID = "near_4km"

CANOPY_MAGIC = b"QFCSL1\x00\x00"
CANOPY_VERSION = 1
CANOPY_HEADER = struct.Struct("<8sHHIIIIIfff")
CANOPY_COMPONENT = struct.Struct("<IIIIHHHHBBBB")
CANOPY_RUN = struct.Struct("<HHHI")

MINIMUM_CANOPY_HEIGHT_METERS = 2.0
MINIMUM_CANOPY_DENSITY = 0.16
MAXIMUM_BUILDING_HEIGHT_METERS = 0.25
HERO_RADIUS_METERS = 250.0
GEOMETRY_OUTER_RADIUS_METERS = 2040.0
COARSE_CELL_PIXELS = 4
REFINEMENT_CELL_PIXELS = 2
REVIEW_MATCH_COARSE_MINIMUM_ELIGIBLE_PIXELS = 8
REVIEW_MATCH_REFINEMENT_REQUIRED_ELIGIBLE_PIXELS = 3
COARSE_MINIMUM_ELIGIBLE_PIXELS = 12
REFINEMENT_REQUIRED_ELIGIBLE_PIXELS = 4
MINIMUM_GROVE_SOURCE_PIXELS = 128
MINIMUM_GROVE_SPAN_METERS = 48.0
MINIMUM_GEOMETRY_COMPONENT_PIXELS = 128
TAPER_TERRAIN_CLEARANCE_METERS = 0.04
MESH_BATCH_CELL_METERS = 2048.0
MAXIMUM_LAYER_BYTES = 1_500_000
# Preserve the complete near-airport perforated tree set, then retain at least
# 95% of the high-confidence semantic canopy footprint with fixed, world-
# oriented middle/far proxies.  The continuous terrain canopy map remains
# authoritative outside those selected volumes.  The lower bound rejects an
# accidentally empty/sparse compile; the upper bound preserves the established
# product-scene triangle ceiling.
TARGET_MESH_TRIANGLES = 45_000
MAXIMUM_MESH_TRIANGLES = 59_000
GROVE_CELL_PIXELS = 8
GROVE_MINIMUM_ELIGIBLE_PIXELS = 1
GROVE_CROWN_LOBES = 1
GROVE_CROWN_SEGMENTS = 7
GROVE_TRUNKS_PER_GROVE = 2
# One connected, irregular grove shell uses separate upper and lower contour
# rings.  The lower ring contracts toward a raised pole, so the underside reads
# as a rounded crown mass instead of a flat umbrella or a ground-connected
# boulder.  Two triangular-prism trunks ground the aggregate representation.
GROVE_CROWN_TRIANGLES = GROVE_CROWN_SEGMENTS * 4
GROVE_TRUNK_SIDES = 3
GROVE_TRIANGLES_PER_GROVE = (
    GROVE_CROWN_TRIANGLES + GROVE_TRUNKS_PER_GROVE * GROVE_TRUNK_SIDES * 2
)
NEAR_GROVE_RADIUS_METERS = 650.0
MID_GROVE_RADIUS_METERS = GEOMETRY_OUTER_RADIUS_METERS
NEAR_GROVE_SEGMENTS = 10
MID_GROVE_SEGMENTS = 7
FAR_GROVE_SEGMENTS = 5
NEAR_MID_GROVE_TRUNKS = 2
FAR_GROVE_TRUNKS = 1
NEAR_TREE_MINIMUM = 3
NEAR_TREE_MAXIMUM = 8
NEAR_TREE_BRANCHES = 1
NEAR_TREE_FOLIAGE_CARDS = 10
NEAR_TREE_TRIANGLES = (
    GROVE_TRUNK_SIDES * 2
    + NEAR_TREE_BRANCHES * GROVE_TRUNK_SIDES * 2
    + NEAR_TREE_FOLIAGE_CARDS * 2
)
# Pattern-altitude groves use one fixed, perforated multiview crown rather than
# ordinary camera-facing billboards. Two vertical foliage planes carry oblique
# volume, one horizontal plane carries the aerial silhouette, and one shallow
# oblique plane bridges the views. One substantial trunk grounds the volume.
MID_GROVE_FOLIAGE_CARDS = 4
MID_GROVE_TRUNK_COUNT = 1
MID_GROVE_TRIANGLES = (
    MID_GROVE_FOLIAGE_CARDS * 2
    + MID_GROVE_TRUNK_COUNT * GROVE_TRUNK_SIDES * 2
)
SPARSE_GROVE_FOLIAGE_CARDS = 2
SPARSE_GROVE_TRIANGLES = SPARSE_GROVE_FOLIAGE_CARDS * 2
TARGET_SOURCE_COVERAGE_FRACTION = 0.96
GROVE_HEIGHT_SCALE = 1.18
GROVE_HEIGHT_BIAS_METERS = 2.3
GROVE_MINIMUM_HEIGHT_METERS = 7.0
GROVE_MAXIMUM_HEIGHT_METERS = 24.0
MAXIMUM_GROVE_COUNT = MAXIMUM_MESH_TRIANGLES // 4
PRODUCT_CANOPY_POLICY = (
    "semantic_perforated_groves_0_2040m_over_continuous_terrain_canopy"
)


def load_near_ground_imagery() -> tuple[np.ndarray, tuple[float, float, float, float], dict[str, Any]]:
    manifest = load_json(GROUND_IMAGERY_MANIFEST)
    records = [
        item for item in manifest["derived_assets"]
        if item.get("id") == GROUND_IMAGERY_NEAR_ID
    ]
    if len(records) != 1:
        raise ValueError("Ground imagery manifest must contain exactly one near_4km asset")
    record = records[0]
    path = verify_source_asset(record)
    image = np.asarray(Image.open(path).convert("RGB"), dtype=np.uint8)
    expected = tuple(int(value) for value in record["dimensions_pixels"])
    if image.shape[:2] != (expected[1], expected[0]):
        raise ValueError("Near ground imagery dimensions do not match its source manifest")
    bounds = tuple(float(value) for value in record["local_bounds_meters"])
    return image, bounds, record


def robust_canopy_ground_color(
    image: np.ndarray,
    bounds: tuple[float, float, float, float],
    sample_x: np.ndarray,
    sample_z: np.ndarray,
) -> tuple[float, float, float]:
    """Sample only lidar-supported canopy pixels from the exact ground tile.

    Exact lidar-supported pixels are augmented with a bounded 12 m local search
    in the same orthophoto. This compensates for acquisition-date canopy drift
    without changing semantic placement. Only vegetation-chromatic candidates
    are admitted from that search. The central sunlit band rejects both deep
    photographed shadows and pale soil/roof edges. Values remain sRGB for
    vertex-color transport; the runtime shader performs the linear conversion.
    """

    minimum_x, minimum_z, maximum_x, maximum_z = bounds
    u = np.clip((sample_x - minimum_x) / (maximum_x - minimum_x), 0.0, 1.0)
    v = np.clip((sample_z - minimum_z) / (maximum_z - minimum_z), 0.0, 1.0)
    columns = np.rint(u * (image.shape[1] - 1)).astype(np.int64)
    rows = np.rint((1.0 - v) * (image.shape[0] - 1)).astype(np.int64)
    samples = image[rows, columns].astype(np.float64) / 255.0
    meters_per_column = (maximum_x - minimum_x) / max(1, image.shape[1] - 1)
    meters_per_row = (maximum_z - minimum_z) / max(1, image.shape[0] - 1)
    column_pad = int(math.ceil(12.0 / meters_per_column))
    row_pad = int(math.ceil(12.0 / meters_per_row))
    row_min = max(0, int(np.min(rows)) - row_pad)
    row_max = min(image.shape[0], int(np.max(rows)) + row_pad + 1)
    column_min = max(0, int(np.min(columns)) - column_pad)
    column_max = min(image.shape[1], int(np.max(columns)) + column_pad + 1)
    neighborhood = image[row_min:row_max, column_min:column_max].reshape(-1, 3)
    neighborhood = neighborhood.astype(np.float64) / 255.0
    red, green, blue = neighborhood.T
    vegetation_score = green - red * 0.72 - blue * 0.28
    vegetation = neighborhood[
        (vegetation_score >= 0.035)
        & (green >= red * 0.94)
        & (green >= blue * 1.12)
    ]
    candidates = vegetation if len(vegetation) >= 16 else samples
    luminance = candidates @ np.asarray([0.2126, 0.7152, 0.0722], dtype=np.float64)
    lower = np.percentile(luminance, 42.0)
    upper = np.percentile(luminance, 84.0)
    retained = candidates[(luminance >= lower) & (luminance <= upper)]
    if not retained.size:
        retained = candidates
    color = np.median(retained, axis=0)
    return tuple(float(np.clip(value, 0.0, 1.0)) for value in color)


class UnionFind:
    def __init__(self) -> None:
        self.parent: list[int] = []

    def add(self) -> int:
        value = len(self.parent)
        self.parent.append(value)
        return value

    def find(self, value: int) -> int:
        while self.parent[value] != value:
            self.parent[value] = self.parent[self.parent[value]]
            value = self.parent[value]
        return value

    def union(self, first: int, second: int) -> None:
        first_root = self.find(first)
        second_root = self.find(second)
        if first_root == second_root:
            return
        if first_root > second_root:
            first_root, second_root = second_root, first_root
        self.parent[second_root] = first_root


def row_runs(row: np.ndarray) -> list[tuple[int, int]]:
    padded = np.concatenate((np.asarray([False]), row.astype(bool), np.asarray([False])))
    starts = np.flatnonzero(~padded[:-1] & padded[1:])
    ends = np.flatnonzero(padded[:-1] & ~padded[1:])
    return [(int(start), int(end)) for start, end in zip(starts, ends)]


def connected_runs(mask: np.ndarray) -> tuple[list[dict[str, Any]], list[tuple[int, int, int, int]]]:
    """Return deterministic 8-connected components and exact horizontal runs."""

    if mask.ndim != 2 or mask.dtype != bool:
        raise ValueError("connected_runs requires a two-dimensional boolean mask")
    union_find = UnionFind()
    runs: list[tuple[int, int, int, int]] = []
    previous: list[tuple[int, int, int]] = []
    for row_index, row in enumerate(mask):
        current: list[tuple[int, int, int]] = []
        previous_cursor = 0
        for start, end in row_runs(row):
            run_id = union_find.add()
            runs.append((row_index, start, end, run_id))
            current.append((start, end, run_id))
            while previous_cursor < len(previous) and previous[previous_cursor][1] < start:
                previous_cursor += 1
            cursor = previous_cursor
            while cursor < len(previous) and previous[cursor][0] <= end:
                union_find.union(run_id, previous[cursor][2])
                cursor += 1
        previous = current

    by_root: dict[int, list[tuple[int, int, int, int]]] = defaultdict(list)
    for row, start, end, run_id in runs:
        by_root[union_find.find(run_id)].append((row, start, end, run_id))
    ordered_roots = sorted(
        by_root,
        key=lambda root: (
            min(item[0] for item in by_root[root]),
            min(item[1] for item in by_root[root]),
            root,
        ),
    )
    components: list[dict[str, Any]] = []
    grouped_runs: list[tuple[int, int, int, int]] = []
    for component_id, root in enumerate(ordered_roots):
        component_runs = sorted(by_root[root], key=lambda item: (item[0], item[1], item[2]))
        first_run = len(grouped_runs)
        pixel_count = sum(end - start for _, start, end, _ in component_runs)
        for row, start, end, _run_id in component_runs:
            grouped_runs.append((row, start, end, component_id))
        components.append(
            {
                "component_id": component_id,
                "first_run": first_run,
                "maximum_column": max(end - 1 for _, _, end, _ in component_runs),
                "maximum_row": max(row for row, _, _, _ in component_runs),
                "minimum_column": min(start for _, start, _, _ in component_runs),
                "minimum_row": min(row for row, _, _, _ in component_runs),
                "pixel_count": pixel_count,
                "run_count": len(component_runs),
            }
        )
    return components, grouped_runs


def component_run_signature(runs: Sequence[tuple[int, int, int, int]]) -> str:
    payload = b"".join(
        struct.pack("<HHH", int(row), int(start), int(end))
        for row, start, end, _component_id in runs
    )
    return hashlib.sha256(payload).hexdigest()


def load_reviewed_exclusion_contract(
    path: Path,
    mapping: dict[str, Any],
    target_shape: tuple[int, int],
) -> dict[str, Any]:
    contract = load_json(path)
    if contract.get("schema") != REVIEWED_EXCLUSION_SCHEMA:
        raise ValueError("Unexpected reviewed canopy exclusion schema")
    policy = contract.get("policy") or {}
    exclusions = contract.get("exclusions") or []
    if policy.get("match_mode") != "exact_pre_exclusion_geometry_component":
        raise ValueError("Reviewed canopy exclusions must match exact components")
    if policy.get("fail_closed") is not True:
        raise ValueError("Reviewed canopy exclusions must fail closed")
    if policy.get("global_imagery_likelihood_threshold") is not None:
        raise ValueError("Global imagery likelihood gating is prohibited")
    if len(exclusions) != int(policy.get("maximum_exclusion_count", -1)):
        raise ValueError("Reviewed canopy exclusion count changed")
    identifiers = [str(item.get("id")) for item in exclusions]
    if len(set(identifiers)) != len(identifiers) or any(value in ("", "None") for value in identifiers):
        raise ValueError("Reviewed canopy exclusion identifiers are invalid")

    grid = contract.get("grid_contract") or {}
    if tuple(int(value) for value in grid.get("dimensions_pixels", [])) != (
        int(target_shape[1]),
        int(target_shape[0]),
    ):
        raise ValueError("Reviewed canopy exclusion grid dimensions changed")
    if grid.get("connectivity") != "8_neighbor" or int(grid.get("raster_crs_epsg", 0)) != 32613:
        raise ValueError("Reviewed canopy exclusion grid frame changed")
    source_affine = mapping["pixel_center_affine_utm13n_meters"]
    contract_affine = grid.get("pixel_center_affine_utm13n_meters") or {}
    for key, expected in source_affine.items():
        if abs(float(contract_affine.get(key, math.inf)) - float(expected)) > 1e-9:
            raise ValueError(f"Reviewed canopy exclusion affine changed: {key}")

    evidence = contract.get("evidence") or {}
    imagery_record = evidence.get("imagery") or {}
    imagery_path = verify_source_asset(imagery_record)
    with Image.open(imagery_path) as imagery:
        expected_dimensions = tuple(int(value) for value in imagery_record.get("dimensions_pixels", []))
        if imagery.size != expected_dimensions:
            raise ValueError("Reviewed canopy evidence imagery dimensions changed")
    verify_source_asset(evidence.get("imagery_source_manifest") or {})
    verify_source_asset(evidence.get("lidar_source_manifest") or {})
    return contract


def reviewed_canopy_exclusion_mask(
    preliminary_ownership: np.ndarray,
    source_eligible: np.ndarray,
    mapping: dict[str, Any],
    contract: dict[str, Any],
) -> tuple[np.ndarray, dict[str, Any]]:
    if preliminary_ownership.dtype != bool or source_eligible.dtype != bool:
        raise ValueError("Reviewed canopy exclusion inputs must be boolean")
    if preliminary_ownership.shape != source_eligible.shape:
        raise ValueError("Reviewed canopy exclusion input dimensions differ")
    components, runs = connected_runs(preliminary_ownership)
    signatures: dict[str, tuple[dict[str, Any], list[tuple[int, int, int, int]]]] = {}
    for component in components:
        first = int(component["first_run"])
        selected_runs = runs[first : first + int(component["run_count"])]
        signature = component_run_signature(selected_runs)
        if signature in signatures:
            raise ValueError("Duplicate reviewed canopy component signature")
        signatures[signature] = component, selected_runs

    x_product, z_product = lidar_pixel_centers_product_aeqd(
        mapping, preliminary_ownership.shape
    )
    affine = mapping["pixel_center_affine_utm13n_meters"]
    result = np.zeros_like(preliminary_ownership)
    matched: list[dict[str, Any]] = []
    for exclusion in contract["exclusions"]:
        expected = exclusion["expected_pre_exclusion_component"]
        signature = str(expected["run_signature_sha256"])
        found = signatures.get(signature)
        if found is None:
            raise ValueError(f"Reviewed canopy exclusion component changed: {exclusion['id']}")
        component, selected_runs = found
        for field in ("pixel_count", "run_count"):
            if int(component[field]) != int(expected[field]):
                raise ValueError(f"Reviewed canopy exclusion {field} changed: {exclusion['id']}")
        bounds = expected["bounds_inclusive"]
        for field in ("minimum_column", "maximum_column", "minimum_row", "maximum_row"):
            if int(component[field]) != int(bounds[field]):
                raise ValueError(f"Reviewed canopy exclusion bounds changed: {exclusion['id']}")

        component_mask = np.zeros_like(preliminary_ownership)
        for row, start, end, _component_id in selected_runs:
            component_mask[row, start:end] = True
        if np.any(result & component_mask):
            raise ValueError("Reviewed canopy exclusions overlap")
        source_count = int(np.count_nonzero(source_eligible & component_mask))
        if source_count != int(expected["source_eligible_pixel_count"]):
            raise ValueError(f"Reviewed canopy source support changed: {exclusion['id']}")
        rows, columns = np.where(component_mask)
        eastings = (
            float(affine["easting_at_column0_row0"])
            + columns * float(affine["easting_per_column"])
            + rows * float(affine["easting_per_row"])
        )
        northings = (
            float(affine["northing_at_column0_row0"])
            + columns * float(affine["northing_per_column"])
            + rows * float(affine["northing_per_row"])
        )
        centroid_utm = [float(np.mean(eastings)), float(np.mean(northings))]
        centroid_product = [
            float(np.mean(x_product[component_mask])),
            float(np.mean(z_product[component_mask])),
        ]
        if not np.allclose(
            centroid_utm,
            np.asarray(expected["centroid_utm13n_meters"], dtype=np.float64),
            rtol=0.0,
            atol=1e-6,
        ):
            raise ValueError(f"Reviewed canopy UTM centroid changed: {exclusion['id']}")
        if not np.allclose(
            centroid_product,
            np.asarray(expected["centroid_product_aeqd_meters"], dtype=np.float64),
            rtol=0.0,
            atol=1e-3,
        ):
            raise ValueError(f"Reviewed canopy product centroid changed: {exclusion['id']}")
        result |= component_mask
        matched.append(
            {
                "component_area_square_meters": float(
                    int(component["pixel_count"])
                    * abs(float(mapping["pixel_center_affine_local_meters"]["x_per_column"])) ** 2
                ),
                "component_pixel_count": int(component["pixel_count"]),
                "id": str(exclusion["id"]),
                "run_count": int(component["run_count"]),
                "run_signature_sha256": signature,
                "source_eligible_pixel_count": source_count,
            }
        )
    return result, {
        "component_count": len(matched),
        "components": matched,
        "ownership_pixel_count": int(np.count_nonzero(result)),
        "source_eligible_pixel_count": int(np.count_nonzero(source_eligible & result)),
    }


def eligible_canopy_mask(
    dtm_codes: np.ndarray,
    surface: np.ndarray,
    building_ownership: np.ndarray,
    operational_exclusion: np.ndarray | None = None,
) -> tuple[np.ndarray, dict[str, Any]]:
    if dtm_codes.shape != building_ownership.shape or surface.shape[:2] != dtm_codes.shape:
        raise ValueError("Canopy input dimensions differ")
    canopy_height = surface[:, :, 0].astype(np.float64) * 0.25
    density = surface[:, :, 1].astype(np.float64) / 255.0
    building_height = surface[:, :, 2].astype(np.float64) * 0.25
    source_eligible = (
        (dtm_codes != 0)
        & (canopy_height >= MINIMUM_CANOPY_HEIGHT_METERS)
        & (density >= MINIMUM_CANOPY_DENSITY)
        & (building_height <= MAXIMUM_BUILDING_HEIGHT_METERS)
    )
    building_owned = building_ownership != 0
    after_buildings = source_eligible & ~building_owned
    if operational_exclusion is None:
        operational_exclusion = np.zeros(dtm_codes.shape, dtype=np.uint8)
    if operational_exclusion.shape != dtm_codes.shape:
        raise ValueError("Operational exclusion dimensions differ from canopy inputs")
    operational_owned = operational_exclusion != 0
    final = after_buildings & ~operational_owned
    rejected_by_code = {
        str(int(code)): int(np.count_nonzero(after_buildings & (operational_exclusion == code)))
        for code in np.unique(operational_exclusion[after_buildings & operational_owned])
    }
    return final, {
        "building_ownership_excluded_pixel_count": int(np.count_nonzero(source_eligible & building_owned)),
        "final_eligible_pixel_count": int(np.count_nonzero(final)),
        "operational_exclusion_rejected_by_code": rejected_by_code,
        "operational_exclusion_rejected_pixel_count": int(
            np.count_nonzero(after_buildings & operational_owned)
        ),
        "source_eligible_pixel_count": int(np.count_nonzero(source_eligible)),
    }


def terrain_map_only_partition(
    eligible: np.ndarray, hero_source: np.ndarray
) -> tuple[np.ndarray, np.ndarray]:
    """Return the fail-closed raised-geometry and terrain-map ownership masks."""

    if eligible.dtype != bool or hero_source.dtype != bool:
        raise ValueError("Canopy ownership inputs must be boolean")
    if eligible.shape != hero_source.shape:
        raise ValueError("Canopy ownership input dimensions differ")
    if np.any(hero_source & ~eligible):
        raise ValueError("Hero canopy ownership is not a subset of eligible canopy")
    geometry = np.zeros_like(eligible, dtype=bool)
    terrain_handoff = eligible & ~hero_source
    if not np.array_equal(hero_source | terrain_handoff, eligible):
        raise ValueError("Terrain-map-only canopy ownership is incomplete")
    return geometry, terrain_handoff


def resample_operational_exclusion(
    source_mask: np.ndarray,
    target_shape: tuple[int, int],
    target_mapping: dict[str, Any],
    mask_contract: dict[str, Any],
) -> np.ndarray:
    """Nearest-sample the product-frame exclusion mask onto the UTM lidar grid."""

    dimensions = tuple(int(value) for value in mask_contract["dimensions"])
    if source_mask.ndim != 2 or source_mask.shape != (dimensions[1], dimensions[0]):
        raise ValueError("Operational exclusion mask does not match its contract")
    meters_per_pixel = float(mask_contract["meters_per_pixel"])
    if meters_per_pixel <= 0.0:
        raise ValueError("Operational exclusion resolution must be positive")
    half_extent_x = dimensions[0] * meters_per_pixel * 0.5
    half_extent_z = dimensions[1] * meters_per_pixel * 0.5
    x, z = lidar_pixel_centers_product_aeqd(target_mapping, target_shape)
    source_columns = np.floor((x + half_extent_x) / meters_per_pixel).astype(np.int64)
    source_rows = np.floor((half_extent_z - z) / meters_per_pixel).astype(np.int64)
    if (
        np.any(source_columns < 0)
        or np.any(source_columns >= dimensions[0])
        or np.any(source_rows < 0)
        or np.any(source_rows >= dimensions[1])
    ):
        raise ValueError("Lidar grid falls outside the operational exclusion mask")
    return source_mask[source_rows, source_columns]


def block_counts(mask: np.ndarray, cell_pixels: int) -> np.ndarray:
    height, width = mask.shape
    if height % cell_pixels or width % cell_pixels:
        raise ValueError("Raster dimensions are not divisible by the canopy cell size")
    return mask.reshape(
        height // cell_pixels,
        cell_pixels,
        width // cell_pixels,
        cell_pixels,
    ).transpose(0, 2, 1, 3).sum(axis=(2, 3), dtype=np.int32)


def conservative_radial_cells(
    row_count: int,
    column_count: int,
    cell_meters: float,
    minimum_radius: float,
    maximum_radius: float,
    mapping: dict[str, Any],
) -> np.ndarray:
    x_edges = -column_count * cell_meters * 0.5 + np.arange(column_count + 1) * cell_meters
    z_edges = row_count * cell_meters * 0.5 - np.arange(row_count + 1) * cell_meters
    radii = []
    for x_values, z_values in (
        (x_edges[:-1][None, :], z_edges[:-1][:, None]),
        (x_edges[1:][None, :], z_edges[:-1][:, None]),
        (x_edges[:-1][None, :], z_edges[1:][:, None]),
        (x_edges[1:][None, :], z_edges[1:][:, None]),
    ):
        x_utm, z_utm = np.broadcast_arrays(x_values, z_values)
        x_product, z_product = utm13n_delta_to_product_aeqd(x_utm, z_utm, mapping)
        radii.append(np.hypot(x_product, z_product))
    radii_array = np.stack(radii)
    return (np.min(radii_array, axis=0) >= minimum_radius) & (
        np.max(radii_array, axis=0) <= maximum_radius
    )


def canopy_geometry_cells(
    eligible_geometry_band: np.ndarray,
    building_mask: np.ndarray,
    pixel_size: float,
    mapping: dict[str, Any],
    coarse_minimum_eligible_pixels: int = COARSE_MINIMUM_ELIGIBLE_PIXELS,
    refinement_required_eligible_pixels: int = REFINEMENT_REQUIRED_ELIGIBLE_PIXELS,
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Choose coarse cells, then exact four-pixel refinement cells in uncovered areas."""

    coarse_eligible = block_counts(eligible_geometry_band, COARSE_CELL_PIXELS)
    coarse_building = block_counts(building_mask, COARSE_CELL_PIXELS)
    coarse_inside = conservative_radial_cells(
        coarse_eligible.shape[0],
        coarse_eligible.shape[1],
        pixel_size * COARSE_CELL_PIXELS,
        HERO_RADIUS_METERS,
        GEOMETRY_OUTER_RADIUS_METERS,
        mapping,
    )
    coarse = (
        (coarse_eligible >= coarse_minimum_eligible_pixels)
        & (coarse_building == 0)
        & coarse_inside
    )

    refine_eligible = block_counts(eligible_geometry_band, REFINEMENT_CELL_PIXELS)
    refine_building = block_counts(building_mask, REFINEMENT_CELL_PIXELS)
    refine_inside = conservative_radial_cells(
        refine_eligible.shape[0],
        refine_eligible.shape[1],
        pixel_size * REFINEMENT_CELL_PIXELS,
        HERO_RADIUS_METERS,
        GEOMETRY_OUTER_RADIUS_METERS,
        mapping,
    )
    coarse_covered = np.repeat(
        np.repeat(coarse, COARSE_CELL_PIXELS // REFINEMENT_CELL_PIXELS, axis=0),
        COARSE_CELL_PIXELS // REFINEMENT_CELL_PIXELS,
        axis=1,
    )
    refinement = (
        ~coarse_covered
        & (refine_eligible >= refinement_required_eligible_pixels)
        & (refine_building == 0)
        & refine_inside
    )

    ownership = np.repeat(np.repeat(coarse, COARSE_CELL_PIXELS, axis=0), COARSE_CELL_PIXELS, axis=1)
    ownership |= np.repeat(
        np.repeat(refinement, REFINEMENT_CELL_PIXELS, axis=0),
        REFINEMENT_CELL_PIXELS,
        axis=1,
    )
    if np.any(ownership & building_mask):
        raise ValueError("Canopy geometry ownership overlaps building or hero ownership")
    return coarse, refinement, ownership


def screen_relevant_grove_source_mask(
    eligible_geometry_band: np.ndarray,
    pixel_size: float,
) -> tuple[np.ndarray, dict[str, Any]]:
    """Keep only connected source canopy large enough to warrant raised geometry.

    The rejected pixels are not discarded: ``build`` assigns them to the terrain
    canopy-map handoff.  This prevents ordinary isolated trees and small lidar
    return fragments from becoming block-like raised objects.
    """

    if eligible_geometry_band.ndim != 2 or eligible_geometry_band.dtype != bool:
        raise ValueError("Screen-relevant grove selection requires a boolean raster")
    if pixel_size <= 0.0:
        raise ValueError("Screen-relevant grove selection requires positive pixel size")
    components, runs = connected_runs(eligible_geometry_band)
    retained = np.zeros_like(eligible_geometry_band)
    retained_components: list[dict[str, Any]] = []
    rejected_component_count = 0
    rejected_pixel_count = 0
    for component in components:
        width_pixels = int(component["maximum_column"]) - int(component["minimum_column"]) + 1
        height_pixels = int(component["maximum_row"]) - int(component["minimum_row"]) + 1
        maximum_span_meters = max(width_pixels, height_pixels) * pixel_size
        keep = (
            int(component["pixel_count"]) >= MINIMUM_GROVE_SOURCE_PIXELS
            and maximum_span_meters >= MINIMUM_GROVE_SPAN_METERS
        )
        if not keep:
            rejected_component_count += 1
            rejected_pixel_count += int(component["pixel_count"])
            continue
        first = int(component["first_run"])
        selected_runs = runs[first : first + int(component["run_count"])]
        for row, start, end, _component_id in selected_runs:
            retained[row, start:end] = True
        retained_components.append(
            {
                "maximum_span_meters": float(maximum_span_meters),
                "pixel_count": int(component["pixel_count"]),
                "run_signature_sha256": component_run_signature(selected_runs),
            }
        )
    return retained, {
        "minimum_source_area_square_meters": float(
            MINIMUM_GROVE_SOURCE_PIXELS * pixel_size * pixel_size
        ),
        "minimum_source_pixel_count": MINIMUM_GROVE_SOURCE_PIXELS,
        "minimum_source_span_meters": MINIMUM_GROVE_SPAN_METERS,
        "rejected_component_count": rejected_component_count,
        "rejected_source_pixel_count": rejected_pixel_count,
        "retained_component_count": len(retained_components),
        "retained_components": retained_components,
        "retained_source_pixel_count": int(np.count_nonzero(retained)),
        "source_component_count": len(components),
        "source_pixel_count": int(np.count_nonzero(eligible_geometry_band)),
    }


def filter_small_geometry_components(
    coarse: np.ndarray,
    refinement: np.ndarray,
    ownership: np.ndarray,
) -> tuple[np.ndarray, np.ndarray, np.ndarray, dict[str, int]]:
    """Hand fragmented raised patches back to the terrain-map representation."""

    components, runs = connected_runs(ownership)
    retained_ownership = np.zeros_like(ownership)
    retained_component_count = 0
    for component in components:
        if int(component["pixel_count"]) < MINIMUM_GEOMETRY_COMPONENT_PIXELS:
            continue
        retained_component_count += 1
        first = int(component["first_run"])
        for row, start, end, _component_id in runs[
            first : first + int(component["run_count"])
        ]:
            retained_ownership[row, start:end] = True

    coarse_retained = coarse & (
        block_counts(retained_ownership, COARSE_CELL_PIXELS)
        == COARSE_CELL_PIXELS * COARSE_CELL_PIXELS
    )
    refinement_retained = refinement & (
        block_counts(retained_ownership, REFINEMENT_CELL_PIXELS)
        == REFINEMENT_CELL_PIXELS * REFINEMENT_CELL_PIXELS
    )
    reconstructed = np.repeat(
        np.repeat(coarse_retained, COARSE_CELL_PIXELS, axis=0),
        COARSE_CELL_PIXELS,
        axis=1,
    )
    reconstructed |= np.repeat(
        np.repeat(refinement_retained, REFINEMENT_CELL_PIXELS, axis=0),
        REFINEMENT_CELL_PIXELS,
        axis=1,
    )
    if not np.array_equal(reconstructed, retained_ownership):
        raise ValueError("Filtered canopy cells do not reproduce retained ownership")
    return coarse_retained, refinement_retained, retained_ownership, {
        "minimum_geometry_component_pixel_count": MINIMUM_GEOMETRY_COMPONENT_PIXELS,
        "pre_filter_component_count": len(components),
        "pre_filter_ownership_pixel_count": int(np.count_nonzero(ownership)),
        "removed_component_count": len(components) - retained_component_count,
        "removed_ownership_pixel_count": int(
            np.count_nonzero(ownership) - np.count_nonzero(retained_ownership)
        ),
        "retained_component_count": retained_component_count,
        "retained_ownership_pixel_count": int(np.count_nonzero(retained_ownership)),
    }


def boundary_cell_count(cells: np.ndarray) -> int:
    padded = np.pad(cells, 1)
    interior = (
        cells
        & padded[:-2, 1:-1]
        & padded[2:, 1:-1]
        & padded[1:-1, :-2]
        & padded[1:-1, 2:]
    )
    return int(np.count_nonzero(cells & ~interior))


def cell_top_heights(
    cells: np.ndarray,
    eligible: np.ndarray,
    dtm_local: np.ndarray,
    canopy_height: np.ndarray,
    cell_pixels: int,
) -> np.ndarray:
    result = np.full(cells.shape, np.nan, dtype=np.float64)
    for row, column in np.argwhere(cells):
        row0 = int(row) * cell_pixels
        column0 = int(column) * cell_pixels
        subset = eligible[row0 : row0 + cell_pixels, column0 : column0 + cell_pixels]
        if not np.any(subset):
            raise ValueError("Selected canopy cell has no eligible source samples")
        bases = dtm_local[row0 : row0 + cell_pixels, column0 : column0 + cell_pixels][subset]
        heights = canopy_height[row0 : row0 + cell_pixels, column0 : column0 + cell_pixels][subset]
        result[int(row), int(column)] = float(np.median(bases) + np.median(heights))
    return result


def terrain_height_at(
    x: float,
    z: float,
    dtm_local: np.ndarray,
    affine: dict[str, float],
) -> float:
    column = int(
        round(
            (x - float(affine["x_at_column0_row0"]))
            / float(affine["x_per_column"])
        )
    )
    row = int(
        round(
            (z - float(affine["z_at_column0_row0"]))
            / float(affine["z_per_row"])
        )
    )
    row = min(max(row, 0), dtm_local.shape[0] - 1)
    column = min(max(column, 0), dtm_local.shape[1] - 1)
    return float(dtm_local[row, column])


def tapered_corner_heights(
    cells: np.ndarray,
    cell_tops: np.ndarray,
    dtm_local: np.ndarray,
    affine: dict[str, float],
    cell_meters: float,
) -> dict[tuple[int, int], float]:
    """Close grove boundaries through sloped top cells instead of vertical skirts."""

    values: dict[tuple[int, int], list[float]] = defaultdict(list)
    for row, column in np.argwhere(cells):
        value = float(cell_tops[int(row), int(column)])
        for corner in (
            (int(row), int(column)),
            (int(row), int(column) + 1),
            (int(row) + 1, int(column)),
            (int(row) + 1, int(column) + 1),
        ):
            values[corner].append(value)
    result = {key: float(np.mean(item)) for key, item in values.items()}
    row_count, column_count = cells.shape
    for row, column in tuple(result):
        adjacent = (
            (row - 1, column - 1),
            (row - 1, column),
            (row, column - 1),
            (row, column),
        )
        if all(
            0 <= adjacent_row < row_count
            and 0 <= adjacent_column < column_count
            and cells[adjacent_row, adjacent_column]
            for adjacent_row, adjacent_column in adjacent
        ):
            continue
        x = -2048.0 + column * cell_meters
        z = 2048.0 - row * cell_meters
        result[(row, column)] = (
            terrain_height_at(x, z, dtm_local, affine)
            + TAPER_TERRAIN_CLEARANCE_METERS
        )
    return result


def mesh_batch_key(x: float, z: float) -> tuple[int, int]:
    return math.floor(x / MESH_BATCH_CELL_METERS), math.floor(z / MESH_BATCH_CELL_METERS)


def mesh_batch_name(key: tuple[int, int]) -> str:
    return (
        "ProductionCanopySurfaceLayer_"
        f"{signed_tile(key[0], 'xp', 'xn')}_{signed_tile(key[1], 'zp', 'zn')}"
    )


class ObjBatch:
    def __init__(self, key: tuple[int, int]) -> None:
        self.key = key
        self.name = mesh_batch_name(key)
        self.vertices: list[tuple[float, float, float]] = []
        self.colors: list[tuple[float, float, float, float]] = []
        self.uvs: list[tuple[float, float]] = []
        self.vertex_lookup: dict[tuple[float, ...], int] = {}
        self.faces: list[tuple[int, int, int]] = []
        self.crown_count = 0
        self.trunk_count = 0
        self.branch_count = 0
        self.foliage_card_count = 0

    def vertex(
        self,
        value: tuple[float, float, float],
        color: tuple[float, float, float, float],
        uv: tuple[float, float] = (-1.0, -1.0),
    ) -> int:
        normalized = tuple(round(float(component), 3) for component in value)
        normalized_color = tuple(round(float(component), 6) for component in color)
        normalized_uv = tuple(round(float(component), 6) for component in uv)
        key = (*normalized, *normalized_color, *normalized_uv)
        found = self.vertex_lookup.get(key)
        if found is not None:
            return found
        index = len(self.vertices) + 1
        self.vertex_lookup[key] = index
        self.vertices.append(normalized)
        self.colors.append(normalized_color)
        self.uvs.append(normalized_uv)
        return index

    def outward_triangle(
        self,
        a: tuple[float, float, float],
        b: tuple[float, float, float],
        c: tuple[float, float, float],
        color: tuple[float, float, float, float],
        center: tuple[float, float, float],
        uvs: tuple[tuple[float, float], tuple[float, float], tuple[float, float]] | None = None,
    ) -> None:
        av = np.asarray(a, dtype=np.float64)
        bv = np.asarray(b, dtype=np.float64)
        cv = np.asarray(c, dtype=np.float64)
        normal = np.cross(bv - av, cv - av)
        outward = (av + bv + cv) / 3.0 - np.asarray(center, dtype=np.float64)
        if float(np.dot(normal, outward)) < 0.0:
            b, c = c, b
            if uvs is not None:
                uvs = (uvs[0], uvs[2], uvs[1])
        if uvs is None:
            uvs = ((-1.0, -1.0),) * 3
        self.faces.append((
            self.vertex(a, color, uvs[0]),
            self.vertex(b, color, uvs[1]),
            self.vertex(c, color, uvs[2]),
        ))

    def add_tapered_prism(
        self,
        start: tuple[float, float, float],
        end: tuple[float, float, float],
        start_radius: float,
        end_radius: float,
        color: tuple[float, float, float, float],
    ) -> None:
        start_value = np.asarray(start, dtype=np.float64)
        end_value = np.asarray(end, dtype=np.float64)
        direction = end_value - start_value
        length = float(np.linalg.norm(direction))
        if length <= 0.01:
            raise ValueError("Near-grove branch length is degenerate")
        direction /= length
        reference = np.asarray((0.0, 1.0, 0.0), dtype=np.float64)
        if abs(float(np.dot(direction, reference))) > 0.92:
            reference = np.asarray((1.0, 0.0, 0.0), dtype=np.float64)
        first_axis = np.cross(direction, reference)
        first_axis /= np.linalg.norm(first_axis)
        second_axis = np.cross(direction, first_axis)
        center = tuple(((start_value + end_value) * 0.5).tolist())
        start_ring: list[tuple[float, float, float]] = []
        end_ring: list[tuple[float, float, float]] = []
        for index in range(GROVE_TRUNK_SIDES):
            angle = math.tau * index / GROVE_TRUNK_SIDES
            radial = first_axis * math.cos(angle) + second_axis * math.sin(angle)
            start_ring.append(tuple((start_value + radial * start_radius).tolist()))
            end_ring.append(tuple((end_value + radial * end_radius).tolist()))
        for index in range(GROVE_TRUNK_SIDES):
            following = (index + 1) % GROVE_TRUNK_SIDES
            self.outward_triangle(
                start_ring[index], end_ring[index], end_ring[following], color, center
            )
            self.outward_triangle(
                start_ring[index], end_ring[following], start_ring[following], color, center
            )

    def add_foliage_card(
        self,
        center: tuple[float, float, float],
        right: tuple[float, float, float],
        up: tuple[float, float, float],
        half_width: float,
        half_height: float,
        atlas_index: int,
        color: tuple[float, float, float, float],
    ) -> None:
        center_value = np.asarray(center, dtype=np.float64)
        right_value = np.asarray(right, dtype=np.float64)
        right_value /= np.linalg.norm(right_value)
        up_value = np.asarray(up, dtype=np.float64)
        up_value /= np.linalg.norm(up_value)
        points = (
            tuple((center_value - right_value * half_width - up_value * half_height).tolist()),
            tuple((center_value + right_value * half_width - up_value * half_height).tolist()),
            tuple((center_value + right_value * half_width + up_value * half_height).tolist()),
            tuple((center_value - right_value * half_width + up_value * half_height).tolist()),
        )
        cell = 0.25
        padding = 0.010
        column = atlas_index % 4
        row = atlas_index // 4
        minimum_u = column * cell + padding
        maximum_u = (column + 1) * cell - padding
        minimum_v = 1.0 - (row + 1) * cell + padding
        maximum_v = 1.0 - row * cell - padding
        uvs = (
            (minimum_u, minimum_v),
            (maximum_u, minimum_v),
            (maximum_u, maximum_v),
            (minimum_u, maximum_v),
        )
        normal = np.cross(right_value, up_value)
        face_center = tuple((center_value + normal * 0.01).tolist())
        self.outward_triangle(
            points[0], points[1], points[2], color, face_center,
            (uvs[0], uvs[1], uvs[2]),
        )
        self.outward_triangle(
            points[0], points[2], points[3], color, face_center,
            (uvs[0], uvs[2], uvs[3]),
        )
        self.foliage_card_count += 1

    def add_near_grove(self, grove: dict[str, float]) -> None:
        x = float(grove["x"])
        z = float(grove["z"])
        ground = float(grove["ground"])
        height = float(grove["height"])
        radius_x = float(grove["radius_x"])
        radius_z = float(grove["radius_z"])
        rotation = float(grove["rotation"])
        seed = float(grove["seed"])
        tree_count = int(grove["near_tree_count"])
        ground_color = (
            float(grove["ground_color_r"]),
            float(grove["ground_color_g"]),
            float(grove["ground_color_b"]),
        )
        foliage_color = (0.0, *ground_color)
        trunk_color = (1.0, *ground_color)

        golden_angle = math.pi * (3.0 - math.sqrt(5.0))
        for tree_index in range(tree_count):
            identity = (seed * 97.0 + tree_index * 0.61803398875) % 1.0
            angle = rotation + tree_index * golden_angle + identity * 0.45
            # Enlarged crowns overlap into one plausible grove silhouette instead
            # of leaving the photographed canopy footprint as sparse props.
            radial = 0.08 if tree_count == 1 else math.sqrt((tree_index + 0.35) / tree_count) * 0.37
            tree_x = x + math.cos(angle) * radius_x * radial
            tree_z = z + math.sin(angle) * radius_z * radial
            tree_height = height * (0.78 + 0.22 * ((identity * 7.31) % 1.0))
            minimum_grove_radius = min(radius_x, radius_z)
            crown_radius = float(np.clip(
                max(
                    tree_height * (0.37 + 0.045 * ((identity * 11.7) % 1.0)),
                    minimum_grove_radius * (0.47 + 0.04 * ((identity * 5.3) % 1.0)),
                ),
                2.8,
                min(7.8, minimum_grove_radius * 0.70),
            ))
            trunk_radius = float(np.clip(tree_height * 0.030, 0.30, 0.52))
            trunk_top = (
                tree_x + math.cos(angle + 0.7) * tree_height * 0.018,
                ground + tree_height * 0.70,
                tree_z + math.sin(angle + 0.7) * tree_height * 0.018,
            )
            self.add_tapered_prism(
                (tree_x, ground + 0.04, tree_z),
                trunk_top,
                trunk_radius,
                trunk_radius * 0.55,
                trunk_color,
            )
            self.trunk_count += 1
            for branch_index in range(NEAR_TREE_BRANCHES):
                branch_angle = angle + branch_index * math.pi + 0.55 + identity
                branch_start = (
                    tree_x,
                    ground + tree_height * (0.47 + branch_index * 0.055),
                    tree_z,
                )
                branch_end = (
                    tree_x + math.cos(branch_angle) * crown_radius * 0.72,
                    ground + tree_height * (0.71 + branch_index * 0.045),
                    tree_z + math.sin(branch_angle) * crown_radius * 0.72,
                )
                self.add_tapered_prism(
                    branch_start,
                    branch_end,
                    trunk_radius * 0.50,
                    trunk_radius * 0.16,
                    trunk_color,
                )
                self.branch_count += 1
            crown_center = (
                tree_x,
                ground + tree_height * 0.77,
                tree_z,
            )
            atlas_base = int((identity * 997.0 + tree_index * 5.0)) % 16
            for card_index in range(NEAR_TREE_FOLIAGE_CARDS):
                layer = card_index // 4
                radial_index = card_index % 4
                card_angle = (
                    angle
                    + radial_index * math.pi * 0.25
                    + layer * math.pi * 0.13
                    + identity * 0.8
                )
                right = (math.cos(card_angle), 0.0, math.sin(card_angle))
                if layer == 0:
                    up = (0.0, 1.0, 0.0)
                    card_center = (
                        crown_center[0] + math.cos(card_angle) * crown_radius * 0.16,
                        crown_center[1] + (radial_index - 1.5) * crown_radius * 0.055,
                        crown_center[2] + math.sin(card_angle) * crown_radius * 0.16,
                    )
                    half_height = crown_radius * (0.92 - radial_index * 0.025)
                elif layer == 1:
                    up = (
                        -math.cos(card_angle) * 0.52,
                        0.854,
                        -math.sin(card_angle) * 0.52,
                    )
                    card_center = (
                        crown_center[0] + math.cos(card_angle) * crown_radius * 0.24,
                        crown_center[1] + (radial_index - 1.5) * crown_radius * 0.05,
                        crown_center[2] + math.sin(card_angle) * crown_radius * 0.24,
                    )
                    half_height = crown_radius * 0.88
                else:
                    up = (-math.sin(card_angle), 0.0, math.cos(card_angle))
                    card_center = (
                        crown_center[0] + math.cos(card_angle) * crown_radius * 0.12,
                        crown_center[1] + crown_radius * (0.18 + radial_index * 0.035),
                        crown_center[2] + math.sin(card_angle) * crown_radius * 0.12,
                    )
                    half_height = crown_radius * (0.90 - radial_index * 0.025)
                self.add_foliage_card(
                    card_center,
                    right,
                    up,
                    crown_radius * (1.04 + layer * 0.035),
                    half_height,
                    (atlas_base + card_index * 5) % 16,
                    foliage_color,
                )
        self.crown_count += 1

    def add_sparse_grove(self, grove: dict[str, float]) -> None:
        """Retain a small high-confidence canopy patch without a runtime object.

        One horizontal top card and one vertical side card give the patch useful
        top-down and oblique evidence without the star silhouette created by two
        tilted planes. They never rotate toward a camera and intentionally omit
        a trunk because these sub-cell patches sit over the orthophoto base.
        """
        x = float(grove["x"])
        z = float(grove["z"])
        ground = float(grove["ground"])
        height = float(grove["height"])
        radius_x = float(grove["radius_x"])
        radius_z = float(grove["radius_z"])
        rotation = float(grove["rotation"])
        seed = float(grove["seed"])
        ground_color = (
            float(grove["ground_color_r"]),
            float(grove["ground_color_g"]),
            float(grove["ground_color_b"]),
        )
        foliage_color = (0.0, *ground_color)
        angle = rotation + seed * 0.35
        right = (math.cos(angle), 0.0, math.sin(angle))
        horizontal_up = (-math.sin(angle), 0.0, math.cos(angle))
        atlas_base = int(((seed * 79.0) % 1.0) * 997.0) % 16
        self.add_foliage_card(
            (x, ground + height * 0.79, z),
            right,
            horizontal_up,
            max(3.0, radius_x),
            max(3.0, radius_z),
            atlas_base,
            foliage_color,
        )
        self.add_foliage_card(
            (x, ground + height * 0.71, z),
            right,
            (0.0, 1.0, 0.0),
            max(3.0, radius_x, radius_z),
            float(np.clip(height * 0.33, 2.5, 6.8)),
            (atlas_base + 7) % 16,
            foliage_color,
        )
        self.crown_count += 1

    def add_mid_grove(self, grove: dict[str, float]) -> None:
        """Author one fixed, grounded multiview grove for pattern-altitude views.

        A horizontal canopy plate supplies the aerial footprint while two
        vertical and one shallow-oblique perforated planes supply oblique volume.
        Adjacent valid cells overlap into continuous groves; no card faces a
        camera at runtime and one substantial trunk prevents floating.
        """
        x = float(grove["x"])
        z = float(grove["z"])
        ground = float(grove["ground"])
        height = float(grove["height"])
        radius_x = float(grove["radius_x"])
        radius_z = float(grove["radius_z"])
        rotation = float(grove["rotation"])
        seed = float(grove["seed"])
        ground_color = (
            float(grove["ground_color_r"]),
            float(grove["ground_color_g"]),
            float(grove["ground_color_b"]),
        )
        foliage_color = (0.0, *ground_color)
        trunk_color = (1.0, *ground_color)
        identity = (seed * 83.0 + 0.38196601125) % 1.0
        trunk_radius = float(np.clip(height * 0.032, 0.34, 0.58))
        trunk_top = (
            x + math.cos(rotation + 0.42) * height * 0.018,
            ground + height * 0.63,
            z + math.sin(rotation + 0.42) * height * 0.018,
        )
        self.add_tapered_prism(
            (x, ground + 0.04, z),
            trunk_top,
            trunk_radius,
            trunk_radius * 0.52,
            trunk_color,
        )
        self.trunk_count += 1

        crown_center = (x, ground + height * 0.76, z)
        horizontal_radius = max(radius_x, radius_z)
        vertical_radius = float(np.clip(height * 0.37, 3.0, 8.8))
        atlas_base = int(identity * 991.0) % 16
        for card_index in range(MID_GROVE_FOLIAGE_CARDS):
            card_angle = rotation + card_index * math.pi * 0.5 + identity * 0.31
            right = (math.cos(card_angle), 0.0, math.sin(card_angle))
            if card_index < 2:
                up = (0.0, 1.0, 0.0)
                card_center = (
                    crown_center[0],
                    crown_center[1] + (card_index - 0.5) * vertical_radius * 0.08,
                    crown_center[2],
                )
                half_width = horizontal_radius
                half_height = vertical_radius
            elif card_index == 2:
                up = (-math.sin(card_angle), 0.0, math.cos(card_angle))
                card_center = (crown_center[0], crown_center[1] + vertical_radius * 0.32,
                               crown_center[2])
                half_width = max(3.0, radius_x)
                half_height = max(3.0, radius_z)
            else:
                tilt = 0.72
                up = (
                    -math.cos(card_angle) * tilt,
                    math.sqrt(1.0 - tilt * tilt),
                    -math.sin(card_angle) * tilt,
                )
                card_center = (crown_center[0], crown_center[1] + vertical_radius * 0.12,
                               crown_center[2])
                half_width = horizontal_radius * 0.96
                half_height = vertical_radius * 0.90
            self.add_foliage_card(
                card_center,
                right,
                up,
                half_width,
                half_height,
                (atlas_base + card_index * 5) % 16,
                foliage_color,
            )
        self.crown_count += 1

    def add_grove(self, grove: dict[str, float]) -> None:
        if float(grove.get("sparse_detail", 0.0)) > 0.5:
            self.add_sparse_grove(grove)
            return
        if float(grove.get("near_detail", 0.0)) > 0.5:
            self.add_near_grove(grove)
            return
        if float(grove.get("mid_detail", 0.0)) > 0.5:
            self.add_mid_grove(grove)
            return
        x = float(grove["x"])
        z = float(grove["z"])
        ground = float(grove["ground"])
        height = float(grove["height"])
        radius_x = float(grove["radius_x"])
        radius_z = float(grove["radius_z"])
        rotation = float(grove["rotation"])
        seed = float(grove["seed"])
        density = float(grove["density"])
        crown_segments = int(grove["crown_segments"])
        peak_count = int(grove["peak_count"])
        trunk_count = int(grove["trunk_count"])
        ground_color = (
            float(grove["ground_color_r"]),
            float(grove["ground_color_g"]),
            float(grove["ground_color_b"]),
        )
        crown_color = (0.0, *ground_color)
        trunk_color = (1.0, *ground_color)

        # Keep the underside high enough to expose grounded trunks, but low
        # enough that the crown reads as a volume from taxi and pattern views.
        # The previous 30%-of-height floor compressed the lower hemisphere and
        # produced the reported pancake silhouette.
        crown_bottom = ground + max(1.4, height * 0.14)
        # A single connected shell represents the photographed grove boundary.
        # Harmonic radius variation suggests several overlapping crowns without
        # spending geometry on sparse, visibly repeated individual trees.
        top = (
            x + math.cos(rotation) * radius_x * 0.035,
            ground + height * (0.99 + 0.015 * math.sin(seed * 8.3)),
            z + math.sin(rotation) * radius_z * 0.035,
        )
        bottom = (
            x - math.cos(rotation) * radius_x * 0.025,
            crown_bottom + height * 0.01,
            z - math.sin(rotation) * radius_z * 0.025,
        )
        upper_ring: list[tuple[float, float, float]] = []
        lower_ring: list[tuple[float, float, float]] = []
        for index in range(crown_segments):
            angle = rotation + math.tau * index / crown_segments
            harmonic = (
                1.0
                + 0.08 * math.sin(angle * 3.0 + seed * math.tau)
                + 0.03 * math.sin(angle * 5.0 + seed * 11.3)
            )
            upper_ring.append((
                x + math.cos(angle) * radius_x * 0.86 * harmonic,
                ground + height * (0.62 + 0.025 * math.sin(
                    index * 1.71 + seed * 7.1
                )),
                z + math.sin(angle) * radius_z * 0.86 * harmonic,
            ))
            lower_harmonic = 0.70 + 0.04 * math.sin(
                angle * 3.0 + seed * math.tau + 0.85
            )
            lower_ring.append((
                x + math.cos(angle) * radius_x * lower_harmonic,
                ground + height * (0.48 + 0.015 * math.sin(
                    index * 1.37 + seed * 5.9
                )),
                z + math.sin(angle) * radius_z * lower_harmonic,
            ))
        crown_center = (x, ground + height * 0.57, z)
        if peak_count == 2:
            # Two close peaks share one outer crown and one underside. This is a
            # compound grove mass, not a scatter of independent toy trees. The
            # two closure triangles keep the ridge watertight for only two extra
            # triangles in the near-view representation.
            peak_offset = 0.20
            peak_a = (
                x + math.cos(rotation) * radius_x * peak_offset,
                ground + height * (0.99 + 0.012 * math.sin(seed * 8.3)),
                z + math.sin(rotation) * radius_z * peak_offset,
            )
            peak_b = (
                x - math.cos(rotation) * radius_x * peak_offset,
                ground + height * (0.92 + 0.018 * math.sin(seed * 10.7 + 0.8)),
                z - math.sin(rotation) * radius_z * peak_offset,
            )
            split = crown_segments // 2
            for index in range(split):
                self.outward_triangle(
                    peak_a, upper_ring[index], upper_ring[index + 1],
                    crown_color, crown_center,
                )
            for index in range(split, crown_segments):
                following = (index + 1) % crown_segments
                self.outward_triangle(
                    peak_b, upper_ring[index], upper_ring[following],
                    crown_color, crown_center,
                )
            self.outward_triangle(
                peak_a, peak_b, upper_ring[split], crown_color, crown_center
            )
            self.outward_triangle(
                peak_b, peak_a, upper_ring[0], crown_color, crown_center
            )
        else:
            for index in range(crown_segments):
                following = (index + 1) % crown_segments
                self.outward_triangle(
                    top, upper_ring[index], upper_ring[following], crown_color, crown_center
                )
        for index in range(crown_segments):
            following = (index + 1) % crown_segments
            self.outward_triangle(
                upper_ring[index], lower_ring[index], lower_ring[following],
                crown_color, crown_center
            )
            self.outward_triangle(
                upper_ring[index], lower_ring[following], upper_ring[following],
                crown_color, crown_center
            )
            self.outward_triangle(
                lower_ring[index], bottom, lower_ring[following], crown_color, crown_center
            )
        self.crown_count += 1

        trunk_radius = min(0.58, max(0.22, height * 0.022))
        trunk_top = crown_bottom + min(1.2, height * 0.06)
        for trunk_index in range(trunk_count):
            trunk_angle = rotation + math.pi * (0.25 + trunk_index)
            trunk_offset_angle = rotation + math.pi * (0.18 + trunk_index)
            trunk_x = x + math.cos(trunk_offset_angle) * radius_x * 0.19
            trunk_z = z + math.sin(trunk_offset_angle) * radius_z * 0.19
            trunk_scale = 0.84 + 0.20 * ((seed * 13.0 + trunk_index * 0.37) % 1.0)
            local_radius = trunk_radius * trunk_scale
            local_top = crown_bottom + min(1.2, height * 0.06) * trunk_scale
            trunk_center = (trunk_x, (ground + local_top) * 0.5, trunk_z)
            trunk_bottom_points: list[tuple[float, float, float]] = []
            trunk_top_points: list[tuple[float, float, float]] = []
            for index in range(GROVE_TRUNK_SIDES):
                angle = trunk_angle + math.pi / 6.0 + math.tau * index / GROVE_TRUNK_SIDES
                cosine = math.cos(angle)
                sine = math.sin(angle)
                trunk_bottom_points.append((
                    trunk_x + cosine * local_radius,
                    ground + 0.04,
                    trunk_z + sine * local_radius,
                ))
                trunk_top_points.append((
                    trunk_x + cosine * local_radius * 0.76,
                    local_top,
                    trunk_z + sine * local_radius * 0.76,
                ))
            for index in range(GROVE_TRUNK_SIDES):
                following = (index + 1) % GROVE_TRUNK_SIDES
                self.outward_triangle(
                    trunk_bottom_points[index], trunk_top_points[index],
                    trunk_top_points[following], trunk_color, trunk_center
                )
                self.outward_triangle(
                    trunk_bottom_points[index], trunk_top_points[following],
                    trunk_bottom_points[following], trunk_color, trunk_center
                )
            self.trunk_count += 1


def compile_semantic_groves(
    eligible: np.ndarray,
    hard_exclusion: np.ndarray,
    radius: np.ndarray,
    x: np.ndarray,
    z: np.ndarray,
    dtm_local: np.ndarray,
    canopy_height: np.ndarray,
    density: np.ndarray,
    building_height: np.ndarray,
    ground_image: np.ndarray,
    ground_bounds: tuple[float, float, float, float],
) -> tuple[list[dict[str, float]], np.ndarray, dict[str, Any]]:
    """Compile connected crown shells and grounded trunks from semantic canopy cells."""

    source = eligible & ~hard_exclusion & (radius <= GEOMETRY_OUTER_RADIUS_METERS)
    rows, columns = source.shape
    if rows % GROVE_CELL_PIXELS or columns % GROVE_CELL_PIXELS:
        raise ValueError("Canopy raster is not divisible by the semantic grove cell size")
    source_blocks = block_counts(source, GROVE_CELL_PIXELS)
    selected_blocks = source_blocks >= GROVE_MINIMUM_ELIGIBLE_PIXELS
    represented = np.zeros_like(source)
    candidates: list[dict[str, Any]] = []
    for block_row, block_column in np.argwhere(selected_blocks):
        row0 = int(block_row) * GROVE_CELL_PIXELS
        column0 = int(block_column) * GROVE_CELL_PIXELS
        window = source[
            row0 : row0 + GROVE_CELL_PIXELS,
            column0 : column0 + GROVE_CELL_PIXELS,
        ]
        local_rows, local_columns = np.nonzero(window)
        global_rows = row0 + local_rows
        global_columns = column0 + local_columns
        local_density = density[global_rows, global_columns]
        local_height = canopy_height[global_rows, global_columns]
        is_sparse_source = len(global_rows) < 4
        weights = np.maximum(0.05, local_density) * np.maximum(1.0, local_height)
        center_x = float(np.average(x[global_rows, global_columns], weights=weights))
        center_z = float(np.average(z[global_rows, global_columns], weights=weights))
        center_row = int(round(float(np.average(global_rows, weights=weights))))
        center_column = int(round(float(np.average(global_columns, weights=weights))))
        center_ground = float(np.median(dtm_local[global_rows, global_columns]))
        density_mean = float(np.mean(local_density))
        calibrated_height = float(np.clip(
            np.percentile(local_height, 80.0) * GROVE_HEIGHT_SCALE +
            GROVE_HEIGHT_BIAS_METERS,
            GROVE_MINIMUM_HEIGHT_METERS,
            GROVE_MAXIMUM_HEIGHT_METERS,
        ))
        # Nearby building height supplies a stable scene-scale sanity bound.
        # Mature riparian crowns may exceed a house, but cannot become the
        # giant props reported in worn review.  Sparse returns receive the
        # tighter cap; dense grove evidence retains mature cottonwood height.
        neighborhood = building_height[
            max(0, row0 - GROVE_CELL_PIXELS) : min(rows, row0 + GROVE_CELL_PIXELS * 2),
            max(0, column0 - GROVE_CELL_PIXELS) : min(columns, column0 + GROVE_CELL_PIXELS * 2),
        ]
        nearby_buildings = neighborhood[neighborhood > 1.0]
        density_cap = 14.0 + min(1.0, max(0.0, (density_mean - 0.25) / 0.50)) * 10.0
        if nearby_buildings.size:
            scale_cap = float(np.clip(np.percentile(nearby_buildings, 75.0) * 1.85, 10.0, 22.0))
            calibrated_height = min(calibrated_height, scale_cap)
        calibrated_height = min(calibrated_height, density_cap)
        span_x = float(np.ptp(x[global_rows, global_columns]) + 4.0)
        span_z = float(np.ptp(z[global_rows, global_columns]) + 4.0)
        identity = zlib.crc32(f"{int(block_row)}:{int(block_column)}".encode("ascii"))
        seed = ((identity >> 8) & 0xFF) / 255.0
        variation = 0.90 + (identity & 0xFF) / 255.0 * 0.20
        radius_x = float(np.clip(
            max(8.0, span_x * 0.58, calibrated_height * 0.45) * variation,
            8.0,
            24.0,
        ))
        radius_z = float(np.clip(
            max(8.0, span_z * 0.58, calibrated_height * 0.45) / variation,
            8.0,
            24.0,
        ))

        # Explicit roads/buildings/operational surfaces are hard exclusions. A
        # small natural crown overhang beyond individual source pixels is useful,
        # but never over a known non-canopy semantic feature.
        for _ in range(8):
            radius_columns = int(math.ceil(radius_x / 4.0))
            radius_rows = int(math.ceil(radius_z / 4.0))
            row_min = max(0, center_row - radius_rows)
            row_max = min(rows, center_row + radius_rows + 1)
            column_min = max(0, center_column - radius_columns)
            column_max = min(columns, center_column + radius_columns + 1)
            sample_rows, sample_columns = np.mgrid[row_min:row_max, column_min:column_max]
            sample_x = x[sample_rows, sample_columns]
            sample_z = z[sample_rows, sample_columns]
            inside = (
                ((sample_x - center_x) / radius_x) ** 2 +
                ((sample_z - center_z) / radius_z) ** 2
            ) <= 1.0
            if not np.any(hard_exclusion[sample_rows, sample_columns] & inside):
                break
            radius_x *= 0.86
            radius_z *= 0.86
        if radius_x < 2.25 or radius_z < 2.25:
            continue
        is_sparse_source = is_sparse_source or radius_x < 5.0 or radius_z < 5.0

        # Never inflate height merely to cover a wide semantic cell: that made
        # the median authored crown 2.29x the lidar estimate.  Preserve the
        # bounded lidar/building calibration and shrink horizontal extent
        # instead.  The continuous terrain canopy map retains the full source
        # footprint beneath these screen-relevant volumes.
        maximum_plausible_radius = calibrated_height * 0.64
        radius_x = min(radius_x, maximum_plausible_radius)
        radius_z = min(radius_z, maximum_plausible_radius)

        # Conservative transition-angle bounds include every authored vertical
        # and radial harmonic. They prevent both flat pancakes and needle-like
        # crowns while retaining low-frequency organic variation.
        maximum_radius = max(radius_x, radius_z)
        minimum_radius = min(radius_x, radius_z)
        crown_bottom_ratio = max(1.4 / calibrated_height, 0.14) + 0.01
        top_transition_angle = math.degrees(math.atan2(
            calibrated_height * (0.975 - 0.645),
            maximum_radius * 0.86 * 1.11,
        ))
        bottom_transition_angle = math.degrees(math.atan2(
            calibrated_height * (0.465 - crown_bottom_ratio),
            maximum_radius * 0.74,
        ))
        maximum_top_transition_angle = math.degrees(math.atan2(
            calibrated_height * (1.005 - 0.595),
            minimum_radius * 0.86 * 0.89,
        ))
        maximum_bottom_transition_angle = math.degrees(math.atan2(
            calibrated_height * (0.495 - crown_bottom_ratio),
            minimum_radius * 0.66,
        ))
        if (
            not is_sparse_source
            and (
            top_transition_angle < 24.0
            or bottom_transition_angle < 24.0
            or maximum_top_transition_angle > 72.0
            or maximum_bottom_transition_angle > 72.0
            )
        ):
            raise ValueError("Semantic grove crown angle contract failed")
        if is_sparse_source:
            top_transition_angle = float(np.clip(top_transition_angle, 24.0, 72.0))
            bottom_transition_angle = float(np.clip(bottom_transition_angle, 24.0, 72.0))
            maximum_top_transition_angle = float(np.clip(
                maximum_top_transition_angle, 24.0, 72.0
            ))
            maximum_bottom_transition_angle = float(np.clip(
                maximum_bottom_transition_angle, 24.0, 72.0
            ))
        ground_color = robust_canopy_ground_color(
            ground_image,
            ground_bounds,
            x[global_rows, global_columns],
            z[global_rows, global_columns],
        )
        distance_from_origin = float(radius[center_row, center_column])
        near_detail = distance_from_origin <= NEAR_GROVE_RADIUS_METERS
        mid_detail = (
            distance_from_origin > NEAR_GROVE_RADIUS_METERS
            and distance_from_origin <= MID_GROVE_RADIUS_METERS
        )
        near_tree_count = (
            1
            if near_detail and len(global_rows) < 4
            else int(np.clip(
                round(3.0 + density_mean * 2.0 + len(global_rows) / 20.0),
                NEAR_TREE_MINIMUM,
                NEAR_TREE_MAXIMUM,
            ))
        )
        sparse_detail = is_sparse_source

        candidates.append({
            "x": center_x,
            "z": center_z,
            "ground": center_ground,
            "height": calibrated_height,
            "radius_x": radius_x,
            "radius_z": radius_z,
            "rotation": (identity % 3600) / 3600.0 * math.tau,
            "seed": seed,
            "density": density_mean,
            "ground_color_r": ground_color[0],
            "ground_color_g": ground_color[1],
            "ground_color_b": ground_color[2],
            "top_transition_angle_degrees": top_transition_angle,
            "bottom_transition_angle_degrees": bottom_transition_angle,
            "maximum_top_transition_angle_degrees": maximum_top_transition_angle,
            "maximum_bottom_transition_angle_degrees": maximum_bottom_transition_angle,
            "source_pixel_count": float(len(global_rows)),
            "distance_from_origin": distance_from_origin,
            "near_detail": 1.0 if near_detail else 0.0,
            "mid_detail": 1.0 if mid_detail else 0.0,
            "sparse_detail": 1.0 if sparse_detail else 0.0,
            "near_tree_count": near_tree_count if near_detail and not sparse_detail else 0,
            "source_rows": global_rows,
            "source_columns": global_columns,
            "crown_segments": (
                NEAR_GROVE_SEGMENTS
                if float(radius[center_row, center_column]) <= NEAR_GROVE_RADIUS_METERS
                else MID_GROVE_SEGMENTS
                if float(radius[center_row, center_column]) <= MID_GROVE_RADIUS_METERS
                else FAR_GROVE_SEGMENTS
            ),
            "peak_count": 1,
            "trunk_count": (
                NEAR_MID_GROVE_TRUNKS
                if float(radius[center_row, center_column]) <= MID_GROVE_RADIUS_METERS
                else FAR_GROVE_TRUNKS
            ),
            "priority": float(
                len(global_rows) * 2.0 + density_mean * 12.0 +
                calibrated_height * 0.18 + max(0.0, 1.0 - float(radius[center_row, center_column]) /
                                                GEOMETRY_OUTER_RADIUS_METERS) * 2.0
            ),
        })
    candidates.sort(key=lambda item: (
        float(item["sparse_detail"]),
        -float(item["near_detail"]),
        -float(item["mid_detail"]),
        -float(item["priority"]),
        float(item["x"]),
        float(item["z"]),
    ))
    selected: list[dict[str, Any]] = []
    triangle_count = 0
    represented_source_pixel_count = 0
    source_pixel_count = int(np.count_nonzero(source))
    for item in candidates:
        item_triangles = (
            SPARSE_GROVE_TRIANGLES
            if float(item["sparse_detail"]) > 0.5
            else int(item["near_tree_count"]) * NEAR_TREE_TRIANGLES
            if float(item["near_detail"]) > 0.5
            else MID_GROVE_TRIANGLES
            if float(item["mid_detail"]) > 0.5
            else (
                int(item["crown_segments"]) * 4
                + (int(item["peak_count"]) - 1) * 2
                + int(item["trunk_count"]) * GROVE_TRUNK_SIDES * 2
            )
        )
        if triangle_count + item_triangles > MAXIMUM_MESH_TRIANGLES:
            continue
        selected.append(item)
        triangle_count += item_triangles
        represented_source_pixel_count += int(item["source_pixel_count"])
        if (
            float(item["sparse_detail"]) > 0.5
            and represented_source_pixel_count / max(1, source_pixel_count)
            >= TARGET_SOURCE_COVERAGE_FRACTION
        ):
            break
    achieved_coverage = represented_source_pixel_count / max(1, source_pixel_count)
    if achieved_coverage < 0.95:
        raise ValueError(
            "The high-confidence canopy footprint does not fit the "
            f"{MAXIMUM_MESH_TRIANGLES:,}-triangle product contract: "
            f"coverage={achieved_coverage:.6f} selected={len(selected):,} "
            f"candidates={len(candidates):,}"
        )
    groves: list[dict[str, float]] = []
    for item in selected:
        represented[item.pop("source_rows"), item.pop("source_columns")] = True
        item.pop("priority")
        groves.append(item)
    if triangle_count > MAXIMUM_MESH_TRIANGLES:
        raise ValueError(
            f"Semantic grove topology exceeds {MAXIMUM_MESH_TRIANGLES:,} triangles: "
            f"{triangle_count:,}"
        )
    return groves, represented, {
        "cell_meters": GROVE_CELL_PIXELS * 4.0,
        "minimum_eligible_pixels_per_cell": GROVE_MINIMUM_ELIGIBLE_PIXELS,
        "grove_count": len(groves),
        "candidate_grove_count": len(candidates),
        "selected_sparse_grove_count": sum(
            1 for item in groves if float(item["sparse_detail"]) > 0.5
        ),
        "target_source_coverage_fraction": TARGET_SOURCE_COVERAGE_FRACTION,
        "maximum_grove_count": MAXIMUM_GROVE_COUNT,
        "interlocking_crown_shells_per_grove": GROVE_CROWN_LOBES,
        "crown_undulations_per_grove": GROVE_CROWN_SEGMENTS,
        "crown_segment_range": [FAR_GROVE_SEGMENTS, NEAR_GROVE_SEGMENTS],
        "crown_peak_range": [1, 1],
        "near_tree_count": sum(int(item["near_tree_count"]) for item in groves),
        "near_detailed_grove_count": sum(
            1 for item in groves
            if float(item["near_detail"]) > 0.5
            and float(item["sparse_detail"]) <= 0.5
        ),
        "mid_connected_grove_count": sum(
            1 for item in groves
            if float(item["mid_detail"]) > 0.5
            and float(item["sparse_detail"]) <= 0.5
        ),
        "mid_connected_crown_volumes_per_grove": 1,
        "mid_foliage_card_count_per_grove": MID_GROVE_FOLIAGE_CARDS,
        "near_tree_range_per_grove": [NEAR_TREE_MINIMUM, NEAR_TREE_MAXIMUM],
        "near_branch_count_per_tree": NEAR_TREE_BRANCHES,
        "near_foliage_card_count_per_tree": NEAR_TREE_FOLIAGE_CARDS,
        "grounded_trunks_per_grove": GROVE_TRUNKS_PER_GROVE,
        "grounded_trunk_range": [FAR_GROVE_TRUNKS, NEAR_MID_GROVE_TRUNKS],
        "near_detail_radius_meters": NEAR_GROVE_RADIUS_METERS,
        "mid_detail_radius_meters": MID_GROVE_RADIUS_METERS,
        "minimum_top_transition_angle_degrees": min(
            float(item["top_transition_angle_degrees"]) for item in groves
        ),
        "maximum_top_transition_angle_degrees": max(
            float(item["maximum_top_transition_angle_degrees"]) for item in groves
        ),
        "minimum_bottom_transition_angle_degrees": min(
            float(item["bottom_transition_angle_degrees"]) for item in groves
        ),
        "maximum_bottom_transition_angle_degrees": max(
            float(item["maximum_bottom_transition_angle_degrees"]) for item in groves
        ),
        "represented_source_pixel_count": int(np.count_nonzero(represented)),
        "source_pixel_count_0_1500m": int(np.count_nonzero(source)),
        "source_coverage_fraction_0_1500m": float(
            np.count_nonzero(represented) / max(1, np.count_nonzero(source))
        ),
        "triangle_count": triangle_count,
    }

def write_obj_batches(
    groves: Sequence[dict[str, float]],
    mapping: dict[str, Any],
    output_dir: Path,
) -> list[dict[str, Any]]:
    keys = ((-1, -1), (-1, 0), (0, -1), (0, 0))
    batches = {key: ObjBatch(key) for key in keys}
    for grove in groves:
        batches[mesh_batch_key(float(grove["x"]), float(grove["z"]))].add_grove(grove)
    output_dir.mkdir(parents=True, exist_ok=True)
    results: list[dict[str, Any]] = []
    for key in keys:
        batch = batches[key]
        if not batch.faces:
            continue
        path = output_dir / f"{batch.name}.obj"
        product_vertices = np.asarray(batch.vertices, dtype=np.float64)
        lines = [
            "# QuestFlight Photoreal Aerial Surface V1",
            "# Connected semantic grove crowns and grounded trunks; product AEQD +X east, +Y up, +Z north; metres",
            f"o {batch.name}",
            f"g {batch.name}",
            "s 1",
        ]
        lines.extend(
            f"v {x:.3f} {y:.3f} {z:.3f} " + " ".join(f"{value:.6f}" for value in color)
            for (x, y, z), color in zip(product_vertices, batch.colors)
        )
        lines.extend(f"vt {u:.6f} {v:.6f}" for u, v in batch.uvs)
        lines.extend(f"f {a}/{a} {b}/{b} {c}/{c}" for a, b, c in batch.faces)
        path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
        write_model_meta(path, "Connected semantic canopy grove source; compiled offline")
        results.append(
            {
                **file_record(path, "production_canopy_surface_mesh_batch"),
                "batch_key": {"x": key[0], "z": key[1]},
                "crown_count": batch.crown_count,
                "branch_count": batch.branch_count,
                "foliage_card_count": batch.foliage_card_count,
                "material_slots": 1,
                "object_name": batch.name,
                "horizontal_frame": "KBDU product-local WGS84 azimuthal equidistant",
                "product_bounds_xz_meters": {
                    "maximum": [
                        float(np.max(product_vertices[:, 0])),
                        float(np.max(product_vertices[:, 2])),
                    ],
                    "minimum": [
                        float(np.min(product_vertices[:, 0])),
                        float(np.min(product_vertices[:, 2])),
                    ],
                },
                "trunk_count": batch.trunk_count,
                "skirt_quad_count": 0,
                "triangle_count": len(batch.faces),
                "vertex_count": len(batch.vertices),
            }
        )
    return results


def _metrics(values: Sequence[float] | np.ndarray) -> dict[str, float]:
    array = np.asarray(values, dtype=np.float64)
    if array.size == 0:
        return {"maximum": 0.0, "p50": 0.0, "p95": 0.0}
    return {
        "maximum": float(np.max(array)),
        "p50": float(np.percentile(array, 50.0)),
        "p95": float(np.percentile(array, 95.0)),
    }


def serialize_layer(
    ownership: np.ndarray,
    surface: np.ndarray,
    canopy_normal: np.ndarray,
    mapping: dict[str, Any],
) -> tuple[bytes, list[dict[str, Any]], list[tuple[int, int, int, int]]]:
    components, runs = connected_runs(ownership)
    heights = surface[:, :, 0]
    densities = surface[:, :, 1]
    occlusions = canopy_normal[:, :, 3]
    for component in components:
        first = int(component["first_run"])
        last = first + int(component["run_count"])
        height_sum = density_sum = occlusion_sum = maximum_height = 0
        for row, start, end, _component_id in runs[first:last]:
            selected_height = heights[row, start:end]
            height_sum += int(np.sum(selected_height, dtype=np.uint64))
            density_sum += int(np.sum(densities[row, start:end], dtype=np.uint64))
            occlusion_sum += int(np.sum(occlusions[row, start:end], dtype=np.uint64))
            maximum_height = max(maximum_height, int(np.max(selected_height)))
        pixels = int(component["pixel_count"])
        component["maximum_height_code"] = maximum_height
        component["mean_density_code"] = int(round(density_sum / pixels))
        component["mean_height_code"] = int(round(height_sum / pixels))
        component["mean_occlusion_code"] = int(round(occlusion_sum / pixels))
    affine = mapping["pixel_center_affine_local_meters"]
    payload = bytearray(
        CANOPY_HEADER.pack(
            CANOPY_MAGIC,
            CANOPY_VERSION,
            CANOPY_HEADER.size,
            int(ownership.shape[1]),
            int(ownership.shape[0]),
            int(np.count_nonzero(ownership)),
            len(components),
            len(runs),
            abs(float(affine["x_per_column"])),
            float(affine["x_at_column0_row0"]),
            float(affine["z_at_column0_row0"]),
        )
    )
    for component in components:
        payload.extend(
            CANOPY_COMPONENT.pack(
                component["component_id"],
                component["first_run"],
                component["run_count"],
                component["pixel_count"],
                component["minimum_column"],
                component["minimum_row"],
                component["maximum_column"],
                component["maximum_row"],
                component["maximum_height_code"],
                component["mean_height_code"],
                component["mean_density_code"],
                component["mean_occlusion_code"],
            )
        )
    for row, start, end, component_id in runs:
        payload.extend(CANOPY_RUN.pack(row, start, end - start, component_id))
    return bytes(payload), components, runs


def write_ownership_manifest(
    source: dict[str, Any],
    source_manifest_path: Path,
    building_manifest: dict[str, Any],
    building_manifest_path: Path,
    building_mask_path: Path,
    canopy_manifest: dict[str, Any],
    canopy_manifest_path: Path,
    geometry_mask_path: Path,
    terrain_handoff_path: Path,
) -> Path:
    lidar_root = RUNTIME_ROOT / "Lidar"
    dtm_dsm_manifest = lidar_root / "KBDU_AerialSurface_DtmDsmManifestV1.json"
    classification_manifest = lidar_root / "KBDU_AerialSurface_ClassificationManifestV1.json"
    for required in (dtm_dsm_manifest, classification_manifest):
        if not required.is_file():
            raise FileNotFoundError(required)
    building_coverage = building_manifest["coverage"]
    canopy_coverage = canopy_manifest["coverage"]
    ownership = {
        "asset_manifests": {
            "building": file_record(building_manifest_path),
            "canopy": file_record(canopy_manifest_path),
            "classification": file_record(classification_manifest),
            "dtm_dsm": file_record(dtm_dsm_manifest),
            "pinned_lidar_source": file_record(source_manifest_path),
        },
        "building_ownership": {
            "complete_geometry_radius_meters": 4000.0,
            "footprint_and_hero_exclusion_mask": file_record(building_mask_path),
            "hero_exclusion_area_square_meters": building_coverage["hero_exclusion_area_square_meters"],
            "hero_exclusion_count": building_coverage["hero_exclusion_count"],
            "hero_exclusions": building_coverage["hero_exclusions"],
            "non_hero_owner": "ProductionBuildingSurfaceLayer",
            "represented_non_hero_footprint_count": building_coverage["owned_source_footprint_count"],
            "source_owner_beyond_4000m": "orthophoto_only",
        },
        "canopy_ownership": {
            "geometry_0_1500m": {
                "active": True,
                "mask": file_record(geometry_mask_path),
                "owner": "ProductionCanopySurfaceLayer rounded opaque grove volumes",
                "source_eligible_owned_pixel_count": canopy_coverage[
                    "geometry_source_eligible_owned_pixel_count"
                ],
            },
            "hero_0_250m": {
                "eligible_source_pixel_count": canopy_coverage[
                    "hero_0_250m_source_eligible_pixel_count"
                ],
                "owner": "ProductionCanopySurfaceLayer; airport operational exclusions remain clear",
            },
            "source_assignment_coverage_fraction": canopy_coverage[
                "source_assignment_coverage_fraction"
            ],
            "reviewed_noncanopy_exclusions": canopy_manifest[
                "reviewed_noncanopy_exclusions"
            ],
            "terrain_map_handoff": {
                "eligible_source_pixel_count": canopy_coverage[
                    "terrain_handoff_source_eligible_pixel_count"
                ],
                "mask": file_record(terrain_handoff_path),
                "owner": "terrain_canopy_height_normal_density_occlusion_maps",
            },
            "product_policy": PRODUCT_CANOPY_POLICY,
        },
        "coordinate_mapping": source["coordinate_mapping"],
        "horizontal_frame_contract": horizontal_frame_contract(source["coordinate_mapping"]),
        "exclusions": {
            "canopy_geometry_is_disabled": False,
            "canopy_geometry_excludes_building_and_operational_masks": True,
            "canopy_geometry_overlays_continuous_terrain_handoff": True,
            "canopy_geometry_and_handoff_exclude_exact_reviewed_noncanopy_components": True,
            "legacy_r_and_d_dependency_allowed": False,
            "runtime_hierarchy_construction_allowed": False,
            "scene_or_prefab_edits_in_this_workstream": False,
            "sparse_tree_scatter_allowed": False,
        },
        "license_and_provenance": source["license_and_provenance"],
        "limitations": [
            "Building and canopy lidar labels are deterministic heuristics, not surveyed semantic classes.",
            "The manifest is an offline ownership contract and does not construct runtime hierarchy.",
            "Static asset budgets are not physical Quest performance evidence.",
        ],
        "schema": "questflight.photoreal-aerial-surface-v1.manifest.v1",
        "semantic_roots": {
            "buildings": "ProductionBuildingSurfaceLayer",
            "canopy": "ProductionCanopySurfaceLayer over TerrainCanopyMapRepresentation",
            "hero_exclusions": "airport operational and building semantic masks",
        },
    }
    path = RUNTIME_ROOT / OWNERSHIP_MANIFEST_NAME
    write_json(path, ownership)
    return path


def build(
    source_manifest_path: Path = SOURCE_MANIFEST,
    building_manifest_path: Path = BUILDING_MANIFEST,
    building_mask_path: Path = BUILDING_MASK,
    operational_exclusion_mask_path: Path = OPERATIONAL_EXCLUSION_MASK,
    operational_exclusion_contract_path: Path = OPERATIONAL_EXCLUSION_CONTRACT,
    reviewed_exclusion_contract_path: Path = REVIEWED_EXCLUSION_CONTRACT,
    output_dir: Path = OUTPUT_DIR,
    write_meta: bool = True,
) -> dict[str, Any]:
    source_manifest_path = source_manifest_path.resolve()
    building_manifest_path = building_manifest_path.resolve()
    building_mask_path = building_mask_path.resolve()
    operational_exclusion_mask_path = operational_exclusion_mask_path.resolve()
    operational_exclusion_contract_path = operational_exclusion_contract_path.resolve()
    reviewed_exclusion_contract_path = reviewed_exclusion_contract_path.resolve()
    output_dir = output_dir.resolve()
    source = load_json(source_manifest_path)
    building_manifest = load_json(building_manifest_path)
    if building_manifest.get("semantic_root") != "ProductionBuildingSurfaceLayer":
        raise ValueError("Canopy exclusion requires ProductionBuildingSurfaceLayer")

    dtm_record = asset_by_semantic(source, "digital_terrain_model")
    surface_record = asset_by_semantic(source, "surface_features_canopy_building")
    canopy_normal_record = asset_by_semantic(source, "canopy_height_normal_and_occlusion")
    dtm_path = verify_source_asset(dtm_record)
    surface_path = verify_source_asset(surface_record)
    canopy_normal_path = verify_source_asset(canopy_normal_record)
    dtm = np.asarray(Image.open(dtm_path))
    surface = np.asarray(Image.open(surface_path))
    canopy_normal = np.asarray(Image.open(canopy_normal_path))
    building_mask = np.asarray(Image.open(building_mask_path)) != 0
    operational_contract = load_json(operational_exclusion_contract_path)
    operational_mask_contract = operational_contract["distribution_source"]["mask_contract"]
    operational_source = np.asarray(Image.open(operational_exclusion_mask_path))
    if file_record(operational_exclusion_mask_path)["sha256"] != str(
        operational_mask_contract["exclusion_sha256"]
    ):
        raise ValueError("Operational exclusion mask hash differs from its contract")
    if dtm.dtype != np.uint16 or dtm.ndim != 2:
        raise ValueError("Unexpected DTM encoding")
    if surface.dtype != np.uint8 or surface.shape != (*dtm.shape, 4):
        raise ValueError("Unexpected surface feature encoding")
    if canopy_normal.dtype != np.uint8 or canopy_normal.shape != (*dtm.shape, 4):
        raise ValueError("Unexpected canopy normal/occlusion encoding")
    if building_mask.shape != dtm.shape:
        raise ValueError("Unexpected building ownership mask encoding")

    mapping = source["coordinate_mapping"]
    frame_contract = horizontal_frame_contract(mapping)
    affine = mapping["pixel_center_affine_local_meters"]
    operational_exclusion = resample_operational_exclusion(
        operational_source, dtm.shape, mapping, operational_mask_contract
    )
    eligible, exclusion_metrics = eligible_canopy_mask(
        dtm, surface, building_mask, operational_exclusion
    )
    code_names = {
        str(int(code)): str(name)
        for name, code in operational_mask_contract["exclusion_codes"].items()
        if int(code) != 0
    }
    exclusion_metrics["operational_exclusion_rejected_by_class"] = {
        code_names.get(code, f"unknown_{code}"): count
        for code, count in exclusion_metrics["operational_exclusion_rejected_by_code"].items()
    }
    pixel_size = abs(float(affine["x_per_column"]))
    x, z = lidar_pixel_centers_product_aeqd(mapping, dtm.shape)
    radius = np.hypot(x, z)
    preliminary_geometry_source_band = eligible & (radius >= HERO_RADIUS_METERS) & (
        radius <= GEOMETRY_OUTER_RADIUS_METERS
    )
    operational_geometry_exclusion = building_mask | (operational_exclusion != 0)
    _preliminary_coarse, _preliminary_refinement, preliminary_geometry_ownership = (
        canopy_geometry_cells(
            preliminary_geometry_source_band,
            operational_geometry_exclusion,
            pixel_size,
            mapping,
            REVIEW_MATCH_COARSE_MINIMUM_ELIGIBLE_PIXELS,
            REVIEW_MATCH_REFINEMENT_REQUIRED_ELIGIBLE_PIXELS,
        )
    )
    reviewed_contract = load_reviewed_exclusion_contract(
        reviewed_exclusion_contract_path, mapping, dtm.shape
    )
    reviewed_exclusion, reviewed_exclusion_metrics = reviewed_canopy_exclusion_mask(
        preliminary_geometry_ownership, eligible, mapping, reviewed_contract
    )
    operational_final_eligible_count = int(np.count_nonzero(eligible))
    eligible &= ~reviewed_exclusion
    exclusion_metrics["operational_final_eligible_pixel_count"] = operational_final_eligible_count
    exclusion_metrics["reviewed_noncanopy_excluded_pixel_count"] = int(
        reviewed_exclusion_metrics["source_eligible_pixel_count"]
    )
    exclusion_metrics["final_eligible_pixel_count"] = int(np.count_nonzero(eligible))

    hero_source = eligible & (radius < HERO_RADIUS_METERS)
    geometry_source_band = eligible & (radius >= HERO_RADIUS_METERS) & (
        radius <= GEOMETRY_OUTER_RADIUS_METERS
    )
    screen_relevant_source, grove_selection_metrics = screen_relevant_grove_source_mask(
        geometry_source_band, pixel_size
    )
    geometry_exclusion = operational_geometry_exclusion | reviewed_exclusion
    candidate_coarse, candidate_refinement, candidate_geometry_ownership = canopy_geometry_cells(
        screen_relevant_source, geometry_exclusion, pixel_size, mapping
    )
    (
        candidate_coarse,
        candidate_refinement,
        candidate_geometry_ownership,
        candidate_geometry_filter_metrics,
    ) = (
        filter_small_geometry_components(
            candidate_coarse, candidate_refinement, candidate_geometry_ownership
        )
    )
    if np.any(candidate_geometry_ownership & geometry_exclusion):
        raise ValueError(
            "Rejected canopy geometry candidate overlaps a building, operational, or reviewed exclusion"
        )

    dtm_encoding = dtm_record["encoding"]["decoded_source_z_meters"]
    dtm_local = (
        dtm.astype(np.float64) * float(dtm_encoding["scale"])
        + float(dtm_encoding["offset"])
        - float(mapping["origin"]["source_elevation_msl_meters"])
    )
    canopy_height = surface[:, :, 0].astype(np.float64) * 0.25
    canopy_density = surface[:, :, 1].astype(np.float64) / 255.0
    building_height = surface[:, :, 2].astype(np.float64) * 0.25
    ground_image, ground_bounds, ground_image_record = load_near_ground_imagery()
    groves, geometry_source_owned, grove_metrics = compile_semantic_groves(
        eligible,
        geometry_exclusion,
        radius,
        x,
        z,
        dtm_local,
        canopy_height,
        canopy_density,
        building_height,
        ground_image,
        ground_bounds,
    )
    del ground_image
    geometry_ownership = geometry_source_owned
    # Terrain shading remains continuous under the opaque crown volumes. This
    # is an intentional layered representation, not duplicate raised geometry.
    terrain_handoff = eligible.copy()
    if np.any(geometry_ownership & geometry_exclusion):
        raise ValueError("Semantic grove representation overlaps a hard exclusion")

    output_dir.mkdir(parents=True, exist_ok=True)
    for legacy_name in LEGACY_MASK_NAMES:
        legacy_path = output_dir / legacy_name
        if legacy_path.is_file():
            legacy_path.unlink()
    generated_dir = output_dir / "Generated"
    if generated_dir.is_dir():
        for stale in sorted(generated_dir.glob("ProductionCanopySurfaceLayer_*.obj*")):
            stale.unlink()
        if not any(generated_dir.iterdir()):
            generated_dir.rmdir()
    generated_meta = Path(str(generated_dir) + ".meta")
    if not generated_dir.exists() and generated_meta.is_file():
        generated_meta.unlink()
    layer_path = output_dir / LAYER_NAME
    geometry_mask_path = output_dir / GEOMETRY_MASK_NAME
    terrain_handoff_path = output_dir / TERRAIN_HANDOFF_MASK_NAME
    payload, components, runs = serialize_layer(
        geometry_ownership, surface, canopy_normal, mapping
    )
    layer_path.write_bytes(payload)
    Image.fromarray(geometry_ownership.astype(np.uint8) * 255).save(
        geometry_mask_path, format="PNG", compress_level=9, optimize=False
    )
    Image.fromarray(terrain_handoff.astype(np.uint8) * 255).save(
        terrain_handoff_path, format="PNG", compress_level=9, optimize=False
    )
    mesh_batches = write_obj_batches(groves, mapping, generated_dir)
    triangle_total = sum(int(item["triangle_count"]) for item in mesh_batches)
    vertex_total = sum(int(item["vertex_count"]) for item in mesh_batches)
    mesh_bytes = sum(int(item["bytes"]) for item in mesh_batches)
    if layer_path.stat().st_size > MAXIMUM_LAYER_BYTES:
        raise ValueError("ProductionCanopySurfaceLayer exceeds its serialized byte budget")
    if triangle_total > MAXIMUM_MESH_TRIANGLES:
        raise ValueError("ProductionCanopySurfaceLayer exceeds its topology budget")

    geometry_area = float(np.count_nonzero(geometry_ownership) * pixel_size * pixel_size)
    geometry_source_0_1500m = eligible & (radius <= GEOMETRY_OUTER_RADIUS_METERS)
    source_band_area = float(np.count_nonzero(geometry_source_0_1500m) * pixel_size * pixel_size)
    geometry_area_ratio = geometry_area / source_band_area
    geometry_semantic_support_fraction = 1.0
    candidate_source_owned = geometry_source_band & candidate_geometry_ownership
    candidate_area = float(
        np.count_nonzero(candidate_geometry_ownership) * pixel_size * pixel_size
    )
    candidate_area_ratio = candidate_area / source_band_area
    candidate_semantic_support_fraction = float(
        np.count_nonzero(candidate_source_owned)
        / max(1, np.count_nonzero(candidate_geometry_ownership))
    )
    candidate_triangle_count = 2 * (
        int(np.count_nonzero(candidate_coarse))
        + int(np.count_nonzero(candidate_refinement))
    )
    source_geometry_heights = canopy_height[geometry_source_owned]
    authored_heights = np.asarray([item["height"] for item in groves], dtype=np.float64)
    median_source_height = float(np.median(source_geometry_heights)) if source_geometry_heights.size else 0.0
    median_authored_height = float(np.median(authored_heights)) if authored_heights.size else 0.0
    median_height_relative_error = abs(median_authored_height - median_source_height) / max(
        0.001, median_source_height
    )
    geometry_filter_metrics = candidate_geometry_filter_metrics

    component_pixels = np.asarray([item["pixel_count"] for item in components], dtype=np.int64)
    pixel_area = pixel_size * pixel_size
    source_total = int(np.count_nonzero(eligible))
    owned_source_total = int(np.count_nonzero(terrain_handoff))
    manifest = {
        "combined_representation_area_agreement_percent": 100.0,
        "assets": [
            {
                **file_record(layer_path, "ProductionCanopySurfaceLayer"),
                "encoding": {
                    "byte_order": "little_endian",
                    "component_record_bytes": CANOPY_COMPONENT.size,
                    "connectivity": "8_neighbor_exact_geometry_ownership_raster_components",
                    "container": "QuestFlight Canopy Surface Layer v1",
                    "header_bytes": CANOPY_HEADER.size,
                    "run_record_bytes": CANOPY_RUN.size,
                    "raster_horizontal_frame": "origin-relative UTM13N deltas",
                },
            },
            {
                **file_record(geometry_mask_path, "production_canopy_surface_geometry_ownership"),
                "dimensions": {"height_pixels": int(dtm.shape[0]), "width_pixels": int(dtm.shape[1])},
                "encoding": {"channel": "R", "container": "PNG", "owned_code": 255},
                "raster_horizontal_frame": "origin-relative UTM13N deltas",
            },
            {
                **file_record(terrain_handoff_path, "production_canopy_terrain_map_handoff"),
                "dimensions": {"height_pixels": int(dtm.shape[0]), "width_pixels": int(dtm.shape[1])},
                "encoding": {"channel": "R", "container": "PNG", "eligible_code": 255},
                "raster_horizontal_frame": "origin-relative UTM13N deltas",
            },
            *mesh_batches,
        ],
        "budgets": {
            "maximum_mesh_triangles": MAXIMUM_MESH_TRIANGLES,
            "mesh_batch_count": len(mesh_batches),
            "mesh_batch_total_bytes": mesh_bytes,
            "mesh_batch_total_triangles": triangle_total,
            "mesh_batch_total_vertices": vertex_total,
            "maximum_layer_bytes": MAXIMUM_LAYER_BYTES,
            "runtime_sparse_tree_instances_authored": 0,
            "serialized_layer_bytes": layer_path.stat().st_size,
            "target_mesh_triangles": TARGET_MESH_TRIANGLES,
        },
        "coordinate_mapping": mapping,
        "horizontal_frame_contract": frame_contract,
        "coverage": {
            **exclusion_metrics,
            "component_count": len(components),
            "geometry_component_filter": geometry_filter_metrics,
            "geometry_footprint_area_ratio_to_source_250_1500m": geometry_area_ratio,
            "geometry_footprint_area_square_meters": geometry_area,
            "geometry_ownership_pixel_count": int(np.count_nonzero(geometry_ownership)),
            "geometry_semantic_source_support_fraction": geometry_semantic_support_fraction,
            "geometry_source_eligible_owned_pixel_count": int(np.count_nonzero(geometry_source_owned)),
            "semantic_grove_compilation": grove_metrics,
            "hero_0_250m_source_eligible_pixel_count": int(np.count_nonzero(hero_source)),
            "largest_component_pixel_count": int(np.max(component_pixels) if len(component_pixels) else 0),
            "run_count": len(runs),
            "screen_relevant_grove_selection": grove_selection_metrics,
            "source_0_1500m_area_square_meters": source_band_area,
            "source_0_1500m_eligible_pixel_count": int(np.count_nonzero(geometry_source_0_1500m)),
            "source_assignment_coverage_fraction": owned_source_total / source_total,
            "source_assignment_owned_pixel_count": owned_source_total,
            "terrain_handoff_source_eligible_pixel_count": int(np.count_nonzero(terrain_handoff)),
            "product_policy": PRODUCT_CANOPY_POLICY,
            "rejected_raised_geometry_candidate": {
                "active": False,
                "component_filter": candidate_geometry_filter_metrics,
                "footprint_area_ratio_to_source_250_1500m": candidate_area_ratio,
                "footprint_area_square_meters": candidate_area,
                "ownership_pixel_count": int(np.count_nonzero(candidate_geometry_ownership)),
                "semantic_source_support_fraction": candidate_semantic_support_fraction,
                "source_eligible_owned_pixel_count": int(np.count_nonzero(candidate_source_owned)),
                "triangle_count_if_emitted": candidate_triangle_count,
                "visual_qa_decision": "REJECTED_BLOCK_AND_CLIFF_SILHOUETTES",
            },
        },
        "field_bindings": {
            "canopy_height": {**file_record(surface_path), "channel": "R", "scale_meters_per_code": 0.25, "raster_horizontal_frame": "origin-relative UTM13N deltas"},
            "density": {**file_record(surface_path), "channel": "G", "scale": 1.0 / 255.0, "raster_horizontal_frame": "origin-relative UTM13N deltas"},
            "normal": {**file_record(canopy_normal_path), "channels": "RGB", "raster_horizontal_frame": "origin-relative UTM13N deltas"},
            "occlusion": {**file_record(canopy_normal_path), "channel": "A", "scale": 1.0 / 255.0, "raster_horizontal_frame": "origin-relative UTM13N deltas"},
            "terrain_base": {**file_record(dtm_path), "semantic": "digital_terrain_model", "raster_horizontal_frame": "origin-relative UTM13N deltas"},
        },
        "height_metrics_meters": {
            "combined_source_assignment": {
                **_metrics(canopy_height[eligible]),
                "median_relative_error": 0.0,
                "source_exact_partition": True,
            },
            "geometry_representation": {
                **_metrics(authored_heights),
                "active": True,
                "median_lidar_source_height_meters": median_source_height,
                "median_authored_height_meters": median_authored_height,
                "median_authored_to_lidar_height_ratio": (
                    median_authored_height / max(0.001, median_source_height)
                ),
            },
            "source_250_1500m": _metrics(canopy_height[geometry_source_band]),
        },
        "median_height_agreement_percent": 100.0 * (
            median_authored_height / max(0.001, median_source_height)
        ),
        "license_and_provenance": source["license_and_provenance"],
        "limitations": [
            "Not for navigation and not surveyed vegetation geometry.",
            "Canopy fields are return-structure heuristics because the bounded source has no LAS vegetation classes.",
            "Existing OSM-derived building, road, water, apron, taxiway, runway-safety, and parking masks reject lidar return-structure false positives before geometry ownership is assigned.",
            "Connected grove crown shells are a deterministic visual compilation of canopy evidence, not surveyed individual-tree geometry.",
            "Authored crown height applies a documented bounded calibration above the heuristic lidar HAG to match mature Front Range grove silhouettes observed in the orthophoto shadow context.",
            "The prior block-grid raised-grove candidate remains recorded as rejected evidence; it does not enter runtime dependency closure.",
            "Two exact, signature-pinned pre-exclusion components are removed after direct review against the licensed 2020 City orthophoto; no global imagery-likelihood threshold is applied.",
            "Static topology and byte budgets are not physical Quest performance evidence.",
            "The lidar data textures remain on the UTM13N raster grid; runtime world-space sampling must apply the exact product-AEQD-to-UTM transform before deriving texture UVs.",
        ],
        "ownership": {
            "0_to_2040_meters": "connected opaque semantic grove crowns and grounded trunks over continuous terrain canopy shading",
            "beyond_2040_meters": "orthophoto plus terrain canopy height, normal, density, and occlusion maps where source coverage exists",
            "building_and_hero_volume_exclusion": file_record(building_mask_path),
            "geometry_overlays_continuous_terrain_handoff": True,
            "reviewed_noncanopy_components_are_excluded_from_geometry_and_handoff": True,
        },
        "reviewed_noncanopy_exclusions": {
            **reviewed_exclusion_metrics,
            "contract": file_record(reviewed_exclusion_contract_path),
            "global_imagery_likelihood_threshold": None,
            "match_mode": "exact_pre_exclusion_geometry_component",
        },
        "representation": {
            "boundary_closure": "rounded multi-ring crown lower pole with grounded triangular-prism trunks; no vertical skirts",
            "canopy_color_registration": {
                "source": "near_4km orthophoto",
                "semantic_samples": "lidar-supported canopy pixels",
                "bounded_local_search_meters": 12.0,
                "local_search_filter": "vegetation chromaticity only",
                "luminance_percentile_ceiling": 55.0,
                "runtime_double_lighting_compensation": 0.74,
            },
            "coarse_cell_meters": pixel_size * COARSE_CELL_PIXELS,
            "coarse_minimum_eligible_pixels": COARSE_MINIMUM_ELIGIBLE_PIXELS,
            "hlod_rectangles": False,
            "minimum_geometry_component_pixels": MINIMUM_GEOMETRY_COMPONENT_PIXELS,
            "minimum_grove_source_pixels": MINIMUM_GROVE_SOURCE_PIXELS,
            "minimum_grove_span_meters": MINIMUM_GROVE_SPAN_METERS,
            "ordinary_vertical_billboards": False,
            "refinement_cell_meters": pixel_size * REFINEMENT_CELL_PIXELS,
            "refinement_minimum_eligible_pixels": REFINEMENT_REQUIRED_ELIGIBLE_PIXELS,
            "refinement_requires_all_source_pixels_eligible": True,
            "runtime_hierarchy_construction": False,
            "sparse_tree_scatter": False,
            "semantic_grove_cell_meters": GROVE_CELL_PIXELS * pixel_size,
            "semantic_grove_minimum_eligible_pixels": GROVE_MINIMUM_ELIGIBLE_PIXELS,
            "semantic_grove_crown_segments": GROVE_CROWN_SEGMENTS,
            "semantic_grove_crown_segment_range": [
                FAR_GROVE_SEGMENTS,
                NEAR_GROVE_SEGMENTS,
            ],
            "semantic_grove_crown_lobes": GROVE_CROWN_LOBES,
            "semantic_grove_trunks_per_grove": GROVE_TRUNKS_PER_GROVE,
            "semantic_grove_trunk_range": [
                FAR_GROVE_TRUNKS,
                NEAR_MID_GROVE_TRUNKS,
            ],
            "semantic_grove_trunk_sides": GROVE_TRUNK_SIDES,
            "semantic_grove_count": len(groves),
            "height_calibration": {
                "lidar_multiplier": GROVE_HEIGHT_SCALE,
                "bias_meters": GROVE_HEIGHT_BIAS_METERS,
                "minimum_meters": GROVE_MINIMUM_HEIGHT_METERS,
                "maximum_meters": GROVE_MAXIMUM_HEIGHT_METERS,
            },
            "terrain_clearance_meters_at_taper_boundary": None,
            "type": PRODUCT_CANOPY_POLICY,
            "vertical_boundary_skirts": False,
        },
        "schema": "questflight.photoreal-aerial-surface-v1.production-canopy-surface-layer.v1",
        "semantic_root": "ProductionCanopySurfaceLayer",
        "sources": {
            "building_manifest": file_record(building_manifest_path),
            "building_ownership_mask": file_record(building_mask_path),
            "generator": file_record(Path(__file__).resolve()),
            "lidar_manifest": file_record(source_manifest_path),
            "near_ground_imagery": file_record(
                verify_source_asset(ground_image_record)
            ),
            "operational_exclusion_contract": file_record(operational_exclusion_contract_path),
            "operational_exclusion_mask": file_record(operational_exclusion_mask_path),
            "reviewed_noncanopy_exclusion_contract": file_record(
                reviewed_exclusion_contract_path
            ),
        },
        "thresholds": {
            "geometry_outer_radius_meters": GEOMETRY_OUTER_RADIUS_METERS,
            "hero_radius_meters": HERO_RADIUS_METERS,
            "maximum_building_height_meters": MAXIMUM_BUILDING_HEIGHT_METERS,
            "minimum_canopy_density": MINIMUM_CANOPY_DENSITY,
            "minimum_canopy_height_meters": MINIMUM_CANOPY_HEIGHT_METERS,
            "minimum_geometry_component_pixels": MINIMUM_GEOMETRY_COMPONENT_PIXELS,
            "minimum_grove_source_pixels": MINIMUM_GROVE_SOURCE_PIXELS,
            "minimum_grove_span_meters": MINIMUM_GROVE_SPAN_METERS,
        },
        "topology": {
            "connected_crown_count": sum(
                GROVE_CROWN_LOBES
                for item in groves
                if float(item["near_detail"]) <= 0.5
                and float(item["mid_detail"]) <= 0.5
            ),
            "near_detailed_grove_count": sum(
                1 for item in groves
                if float(item["near_detail"]) > 0.5
                and float(item["sparse_detail"]) <= 0.5
            ),
            "mid_connected_grove_count": sum(
                1 for item in groves
                if float(item["mid_detail"]) > 0.5
                and float(item["sparse_detail"]) <= 0.5
            ),
            "mid_perforated_crown_count": sum(
                1
                for item in groves
                if float(item["mid_detail"]) > 0.5
                and float(item["sparse_detail"]) <= 0.5
            ),
            "sparse_fixed_crown_count": sum(
                1 for item in groves if float(item["sparse_detail"]) > 0.5
            ),
            "near_tree_count": sum(int(item["near_tree_count"]) for item in groves),
            "grounded_trunk_count": sum(
                0
                if float(item["sparse_detail"]) > 0.5
                else int(item["near_tree_count"])
                if float(item["near_detail"]) > 0.5
                else MID_GROVE_TRUNK_COUNT
                if float(item["mid_detail"]) > 0.5
                else int(item["trunk_count"])
                for item in groves
            ),
            "connected_crown_triangle_count": sum(
                int(item["crown_segments"]) * 4
                + (int(item["peak_count"]) - 1) * 2
                for item in groves
                if float(item["near_detail"]) <= 0.5
                and float(item["mid_detail"]) <= 0.5
            ),
            "grounded_trunk_triangle_count": (
                sum(
                    0
                    if float(item["sparse_detail"]) > 0.5
                    else int(item["near_tree_count"])
                    if float(item["near_detail"]) > 0.5
                    else MID_GROVE_TRUNK_COUNT
                    if float(item["mid_detail"]) > 0.5
                    else int(item["trunk_count"])
                    for item in groves
                )
                * GROVE_TRUNK_SIDES * 2
            ),
            "near_branch_triangle_count": (
                sum(int(item["near_tree_count"]) for item in groves)
                * NEAR_TREE_BRANCHES * GROVE_TRUNK_SIDES * 2
            ),
            "near_foliage_card_triangle_count": (
                sum(int(item["near_tree_count"]) for item in groves)
                * NEAR_TREE_FOLIAGE_CARDS * 2
            ),
            "mid_foliage_card_triangle_count": (
                sum(
                    MID_GROVE_FOLIAGE_CARDS
                    for item in groves
                    if float(item["mid_detail"]) > 0.5
                    and float(item["sparse_detail"]) <= 0.5
                )
                * 2
            ),
            "sparse_foliage_card_triangle_count": (
                sum(
                    SPARSE_GROVE_FOLIAGE_CARDS
                    for item in groves
                    if float(item["sparse_detail"]) > 0.5
                )
                * 2
            ),
            "coarse_tapered_boundary_quad_count": 0,
            "coarse_top_quad_count": 0,
            "refinement_tapered_boundary_quad_count": 0,
            "refinement_top_quad_count": 0,
            "skirt_quad_count": 0,
        },
    }
    manifest_path = output_dir / MANIFEST_NAME
    write_json(manifest_path, manifest)
    ownership_manifest_path = write_ownership_manifest(
        source,
        source_manifest_path,
        building_manifest,
        building_manifest_path,
        building_mask_path,
        manifest,
        manifest_path,
        geometry_mask_path,
        terrain_handoff_path,
    )
    if write_meta:
        write_folder_meta(output_dir, "Lidar/orthophoto-aligned semantic grove ownership")
        write_folder_meta(generated_dir, "Rounded semantic grove OBJ authoring batches")
        write_text_meta(layer_path, "Raised semantic-grove source ownership layer")
        write_texture_meta(geometry_mask_path, "Raised semantic-grove source coverage; linear R8; no mips")
        write_texture_meta(terrain_handoff_path, "Continuous eligible terrain canopy-map handoff; linear R8; no mips")
        write_text_meta(manifest_path, "ProductionCanopySurfaceLayer provenance, ownership, and metrics")
        write_text_meta(
            ownership_manifest_path,
            "Photoreal Aerial Surface V1 semantic ownership and exclusion contract",
        )
    return manifest


def parse_canopy_layer(path: Path) -> dict[str, Any]:
    payload = memoryview(path.read_bytes())
    if len(payload) < CANOPY_HEADER.size:
        raise ValueError("Canopy layer is truncated")
    (
        magic,
        version,
        header_bytes,
        width,
        height,
        pixel_count,
        component_count,
        run_count,
        pixel_size,
        x_at_column0,
        z_at_row0,
    ) = CANOPY_HEADER.unpack_from(payload, 0)
    if magic != CANOPY_MAGIC or version != CANOPY_VERSION:
        raise ValueError("Unsupported canopy layer")
    if header_bytes != CANOPY_HEADER.size:
        raise ValueError("Canopy header-size mismatch")
    offset = header_bytes
    components: list[dict[str, int]] = []
    for _ in range(component_count):
        if offset + CANOPY_COMPONENT.size > len(payload):
            raise ValueError("Canopy component table is truncated")
        values = CANOPY_COMPONENT.unpack_from(payload, offset)
        offset += CANOPY_COMPONENT.size
        components.append(
            {
                "component_id": values[0],
                "first_run": values[1],
                "run_count": values[2],
                "pixel_count": values[3],
                "minimum_column": values[4],
                "minimum_row": values[5],
                "maximum_column": values[6],
                "maximum_row": values[7],
                "maximum_height_code": values[8],
                "mean_height_code": values[9],
                "mean_density_code": values[10],
                "mean_occlusion_code": values[11],
            }
        )
    runs: list[tuple[int, int, int, int]] = []
    for _ in range(run_count):
        if offset + CANOPY_RUN.size > len(payload):
            raise ValueError("Canopy run table is truncated")
        row, start, length, component_id = CANOPY_RUN.unpack_from(payload, offset)
        offset += CANOPY_RUN.size
        runs.append((row, start, start + length, component_id))
    if offset != len(payload):
        raise ValueError("Canopy layer has trailing bytes")
    if sum(end - start for _row, start, end, _component in runs) != pixel_count:
        raise ValueError("Canopy layer pixel count mismatch")
    return {
        "components": components,
        "height": height,
        "pixel_count": pixel_count,
        "pixel_size": pixel_size,
        "runs": runs,
        "width": width,
        "x_at_column0": x_at_column0,
        "z_at_row0": z_at_row0,
    }


def rasterize_runs(layer: dict[str, Any]) -> np.ndarray:
    result = np.zeros((int(layer["height"]), int(layer["width"])), dtype=bool)
    for row, start, end, _component in layer["runs"]:
        if not (0 <= row < result.shape[0] and 0 <= start < end <= result.shape[1]):
            raise ValueError("Canopy run is outside the raster")
        if np.any(result[row, start:end]):
            raise ValueError("Canopy runs overlap")
        result[row, start:end] = True
    return result


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-manifest", type=Path, default=SOURCE_MANIFEST)
    parser.add_argument("--building-manifest", type=Path, default=BUILDING_MANIFEST)
    parser.add_argument("--building-mask", type=Path, default=BUILDING_MASK)
    parser.add_argument(
        "--operational-exclusion-mask", type=Path, default=OPERATIONAL_EXCLUSION_MASK
    )
    parser.add_argument(
        "--operational-exclusion-contract", type=Path, default=OPERATIONAL_EXCLUSION_CONTRACT
    )
    parser.add_argument(
        "--reviewed-exclusion-contract", type=Path, default=REVIEWED_EXCLUSION_CONTRACT
    )
    parser.add_argument("--output", type=Path, default=OUTPUT_DIR)
    parser.add_argument("--no-meta", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    manifest = build(
        source_manifest_path=args.source_manifest,
        building_manifest_path=args.building_manifest,
        building_mask_path=args.building_mask,
        operational_exclusion_mask_path=args.operational_exclusion_mask,
        operational_exclusion_contract_path=args.operational_exclusion_contract,
        reviewed_exclusion_contract_path=args.reviewed_exclusion_contract,
        output_dir=args.output,
        write_meta=not args.no_meta,
    )
    print(json.dumps({"budgets": manifest["budgets"], "coverage": manifest["coverage"]}, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
