#!/usr/bin/env python3
"""Compile complete 0-2 km OSM building surfaces as one compact offline asset."""

from __future__ import annotations

import argparse
import json
import math
import struct
import zlib
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Iterable, Sequence

import numpy as np
from PIL import Image

from build_dtm_dsm import (
    REPOSITORY_ROOT,
    RUNTIME_ROOT,
    SOURCE_MANIFEST,
    asset_by_semantic,
    file_record,
    load_json,
    logical_path,
    stable_guid,
    verify_source_asset,
    write_folder_meta,
    write_json,
    write_text_meta,
    write_texture_meta,
)
from geospatial_frame import horizontal_frame_contract, product_points_to_utm13n_delta


CONTEXT_PATH = REPOSITORY_ROOT / (
    "QuestFlight/Assets/Production/Environment/ReconstructionV2/KBDU_ReferenceContextV2.json"
)
RECIPE_PATH = REPOSITORY_ROOT / (
    "QuestFlight/Assets/Production/Environment/ReconstructionV2/KBDU_ContextBatchRecipeV2.json"
)
COVERAGE_REPORT_PATH = REPOSITORY_ROOT / (
    "tools/kbdu_environment_reconstruction/building_coverage_report.json"
)
HERO_REPLACEMENTS_PATH = REPOSITORY_ROOT / (
    "tools/kbdu_airport_core_v4/buildings/airport_core_replacement_footprints.json"
)
OUTPUT_DIR = RUNTIME_ROOT / "Buildings"
GENERATED_DIR = OUTPUT_DIR / "Generated"
LAYER_NAME = "ProductionBuildingSurfaceLayer.bytes"
MASK_NAME = "ProductionBuildingSurfaceLayer_FootprintMask_R8.png"
MANIFEST_NAME = "ProductionBuildingSurfaceLayer_Manifest.json"

BUILDING_MAGIC = b"QFBSL1\x00\x00"
BUILDING_VERSION = 1
BUILDING_HEADER = struct.Struct("<8sHHIIIIfff")
BUILDING_RECORD = struct.Struct("<IQIHHhHHHBBII")
POINT = struct.Struct("<hh")
INDEX = struct.Struct("<H")

FLAG_LIDAR_HEIGHT_USED = 1 << 0
FLAG_OSM_HEIGHT_TAG = 1 << 1
FLAG_OSM_LEVELS_TAG = 1 << 2
FLAG_SOURCE_WINDING_REVERSED = 1 << 3
FLAG_LIDAR_HEIGHT_HEURISTIC = 1 << 4
FLAG_ZONE_2_TO_4_KM = 1 << 5

HORIZONTAL_QUANTIZATION_METERS = 0.5
VERTICAL_QUANTIZATION_METERS = 0.01
OWNERSHIP_ZONES = ("0-2km", "2-4km")
MINIMUM_LIDAR_CONFIDENCE = 0.35
MINIMUM_LIDAR_SUPPORT = 0.25
MINIMUM_LIDAR_PIXELS = 2
MAXIMUM_LAYER_BYTES = 3_000_000
MAXIMUM_LAYER_TRIANGLES = 130_000
MESH_BATCH_CELL_METERS = 2000.0
MINIMUM_FOOTPRINT_AREA_RATIO = 0.90
MAXIMUM_FOOTPRINT_AREA_RATIO = 1.05
MAXIMUM_HAUSDORFF_ERROR_METERS = 10.0

# Shared AirportCore atlas tiles.  Context meshes carry these compact semantic
# selectors as vertex colors so every face of one source building resolves to
# one coherent facade family without creating per-building materials.
FACADE_TILE_BRICK = 0
FACADE_TILE_LIGHT_METAL = 1
FACADE_TILE_CONCRETE = 5
FACADE_TILE_CORRUGATED = 8
FACADE_TILE_WEATHERED_METAL = 10
SEMANTIC_RESIDENTIAL = 32
SEMANTIC_CIVIC = 64
SEMANTIC_COMMERCIAL = 96
SEMANTIC_INDUSTRIAL = 128
SEMANTIC_OTHER = 160


def signed_area2(points: Sequence[tuple[int, int]]) -> int:
    return sum(
        points[index][0] * points[(index + 1) % len(points)][1]
        - points[(index + 1) % len(points)][0] * points[index][1]
        for index in range(len(points))
    )


def polygon_centroid(points: Sequence[tuple[int, int]]) -> tuple[float, float]:
    area2 = signed_area2(points)
    if area2 == 0:
        raise ValueError("Degenerate polygon")
    x_sum = 0
    z_sum = 0
    for index, point in enumerate(points):
        following = points[(index + 1) % len(points)]
        cross = point[0] * following[1] - following[0] * point[1]
        x_sum += (point[0] + following[0]) * cross
        z_sum += (point[1] + following[1]) * cross
    return x_sum / (3.0 * area2), z_sum / (3.0 * area2)


def decode_points(feature: dict[str, Any]) -> list[tuple[int, int]]:
    values = feature.get("points_q") or []
    points = [
        (int(values[index]), int(values[index + 1]))
        for index in range(0, len(values) - 1, 2)
    ]
    if len(points) > 1 and points[0] == points[-1]:
        points.pop()
    cleaned: list[tuple[int, int]] = []
    for point in points:
        if not cleaned or point != cleaned[-1]:
            cleaned.append(point)
    if len(cleaned) < 3 or signed_area2(cleaned) == 0:
        raise ValueError("Building footprint is degenerate")
    if len(cleaned) > 32767:
        raise ValueError("Building footprint exceeds the compact index format")
    return cleaned


def cross(a: tuple[int, int], b: tuple[int, int], c: tuple[int, int]) -> int:
    return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])


def point_in_triangle_inclusive(
    point: tuple[int, int],
    a: tuple[int, int],
    b: tuple[int, int],
    c: tuple[int, int],
) -> bool:
    return cross(a, b, point) >= 0 and cross(b, c, point) >= 0 and cross(c, a, point) >= 0


def triangulate_polygon(points: Sequence[tuple[int, int]]) -> list[tuple[int, int, int]]:
    """Ear-clip a simple CCW polygon while retaining every boundary vertex."""

    if signed_area2(points) <= 0:
        raise ValueError("triangulate_polygon requires CCW input")
    remaining = list(range(len(points)))
    triangles: list[tuple[int, int, int]] = []
    guard = 0
    while len(remaining) > 3:
        clipped = False
        for position, current in enumerate(remaining):
            previous = remaining[position - 1]
            following = remaining[(position + 1) % len(remaining)]
            a, b, c = points[previous], points[current], points[following]
            if cross(a, b, c) <= 0:
                continue
            blocked = False
            for other in remaining:
                if other in (previous, current, following):
                    continue
                if point_in_triangle_inclusive(points[other], a, b, c):
                    blocked = True
                    break
            if blocked:
                continue
            triangles.append((previous, current, following))
            del remaining[position]
            clipped = True
            break
        guard += 1
        if not clipped or guard > len(points) * len(points):
            raise ValueError("Could not triangulate simple building footprint without distortion")
    if cross(points[remaining[0]], points[remaining[1]], points[remaining[2]]) <= 0:
        raise ValueError("Triangulation ended in a degenerate triangle")
    triangles.append((remaining[0], remaining[1], remaining[2]))
    if len(triangles) != len(points) - 2:
        raise AssertionError("Unexpected roof triangle count")
    return triangles


def point_on_segment(
    a: tuple[int, int], b: tuple[int, int], point: tuple[int, int]
) -> bool:
    return (
        cross(a, b, point) == 0
        and min(a[0], b[0]) <= point[0] <= max(a[0], b[0])
        and min(a[1], b[1]) <= point[1] <= max(a[1], b[1])
    )


def segments_intersect(
    a: tuple[int, int],
    b: tuple[int, int],
    c: tuple[int, int],
    d: tuple[int, int],
) -> bool:
    first = cross(a, b, c)
    second = cross(a, b, d)
    third = cross(c, d, a)
    fourth = cross(c, d, b)
    if first == 0 and point_on_segment(a, b, c):
        return True
    if second == 0 and point_on_segment(a, b, d):
        return True
    if third == 0 and point_on_segment(c, d, a):
        return True
    if fourth == 0 and point_on_segment(c, d, b):
        return True
    return (first > 0) != (second > 0) and (third > 0) != (fourth > 0)


def polygon_is_simple(points: Sequence[tuple[int, int]]) -> bool:
    count = len(points)
    for first in range(count):
        a = points[first]
        b = points[(first + 1) % count]
        for second in range(first + 1, count):
            if second in (first, (first + 1) % count):
                continue
            if (second + 1) % count in (first, (first + 1) % count):
                continue
            if segments_intersect(a, b, points[second], points[(second + 1) % count]):
                return False
    return True


def point_segment_distance_meters(
    point: tuple[float, float],
    start: tuple[float, float],
    end: tuple[float, float],
) -> float:
    px, pz = point
    ax, az = start
    bx, bz = end
    dx = bx - ax
    dz = bz - az
    denominator = dx * dx + dz * dz
    fraction = (
        max(0.0, min(1.0, ((px - ax) * dx + (pz - az) * dz) / denominator))
        if denominator > 0.0
        else 0.0
    )
    return math.hypot(px - (ax + fraction * dx), pz - (az + fraction * dz))


def boundary_samples(points: Sequence[tuple[int, int]]) -> list[tuple[float, float]]:
    values = [
        (point[0] * HORIZONTAL_QUANTIZATION_METERS, point[1] * HORIZONTAL_QUANTIZATION_METERS)
        for point in points
    ]
    samples: list[tuple[float, float]] = []
    for index, start in enumerate(values):
        end = values[(index + 1) % len(values)]
        length = math.dist(start, end)
        steps = max(1, int(math.ceil(length / 2.0)))
        for step in range(steps):
            fraction = step / steps
            samples.append(
                (
                    start[0] + (end[0] - start[0]) * fraction,
                    start[1] + (end[1] - start[1]) * fraction,
                )
            )
    return samples


def sampled_hausdorff_meters(
    first: Sequence[tuple[int, int]], second: Sequence[tuple[int, int]]
) -> float:
    first_m = [
        (point[0] * HORIZONTAL_QUANTIZATION_METERS, point[1] * HORIZONTAL_QUANTIZATION_METERS)
        for point in first
    ]
    second_m = [
        (point[0] * HORIZONTAL_QUANTIZATION_METERS, point[1] * HORIZONTAL_QUANTIZATION_METERS)
        for point in second
    ]

    def directed(
        samples: Sequence[tuple[float, float]], boundary: Sequence[tuple[float, float]]
    ) -> float:
        return max(
            min(
                point_segment_distance_meters(
                    sample, boundary[index], boundary[(index + 1) % len(boundary)]
                )
                for index in range(len(boundary))
            )
            for sample in samples
        )

    return max(
        directed(boundary_samples(first), second_m),
        directed(boundary_samples(second), first_m),
    )


def simplify_footprint(
    source: Sequence[tuple[int, int]],
) -> tuple[list[tuple[int, int]], dict[str, float | int]]:
    source_points = list(source)
    source_area2 = abs(signed_area2(source_points))
    candidates = [list(source_points)]
    current = list(source_points)
    while len(current) > 3:
        removed = False
        ranked = sorted(
            range(len(current)),
            key=lambda index: (
                abs(cross(current[index - 1], current[index], current[(index + 1) % len(current)])),
                index,
            ),
        )
        for index in ranked:
            proposed = current[:index] + current[index + 1 :]
            if signed_area2(proposed) == 0 or not polygon_is_simple(proposed):
                continue
            current = proposed
            candidates.append(list(current))
            removed = True
            break
        if not removed:
            break

    qualified: list[tuple[int, float, float, list[tuple[int, int]]]] = []
    for candidate in candidates:
        ratio = abs(signed_area2(candidate)) / source_area2
        if not MINIMUM_FOOTPRINT_AREA_RATIO <= ratio <= MAXIMUM_FOOTPRINT_AREA_RATIO:
            continue
        hausdorff = sampled_hausdorff_meters(source_points, candidate)
        if hausdorff <= MAXIMUM_HAUSDORFF_ERROR_METERS + 1e-9:
            qualified.append((len(candidate), ratio, hausdorff, candidate))
    if not qualified:
        raise ValueError("No error-bounded building footprint simplification exists")
    point_count, ratio, hausdorff, selected = min(
        qualified, key=lambda item: (item[0], item[2], abs(item[1] - 1.0))
    )
    return selected, {
        "area_ratio": ratio,
        "hausdorff_meters": hausdorff,
        "source_point_count": len(source_points),
        "surface_point_count": point_count,
    }


def closed_surface_indices(
    points: Sequence[tuple[int, int]], roof: Sequence[tuple[int, int, int]]
) -> list[int]:
    indices: list[int] = []
    # In local X/Z, CCW has a downward geometric normal. Reverse the top and
    # retain the CCW order for the floor.
    for a, b, c in roof:
        indices.extend((2 * a + 1, 2 * c + 1, 2 * b + 1))
        indices.extend((2 * a, 2 * b, 2 * c))
    for a in range(len(points)):
        b = (a + 1) % len(points)
        indices.extend((2 * a, 2 * b + 1, 2 * b))
        indices.extend((2 * a, 2 * a + 1, 2 * b + 1))
    return indices


def edge_multiplicity(indices: Sequence[int]) -> Counter[tuple[int, int]]:
    edges: Counter[tuple[int, int]] = Counter()
    for offset in range(0, len(indices), 3):
        triangle = indices[offset : offset + 3]
        for index in range(3):
            a = triangle[index]
            b = triangle[(index + 1) % 3]
            edges[(min(a, b), max(a, b))] += 1
    return edges


def point_in_polygon_grid(
    x: np.ndarray, z: np.ndarray, points_m: np.ndarray
) -> np.ndarray:
    result = np.zeros(x.shape, dtype=bool)
    previous = len(points_m) - 1
    for current in range(len(points_m)):
        x0, z0 = points_m[current]
        x1, z1 = points_m[previous]
        crossing = ((z0 > z) != (z1 > z)) & (
            x < (x1 - x0) * (z - z0) / (z1 - z0 + 1e-30) + x0
        )
        result ^= crossing
        previous = current
    return result


def polygon_pixels(
    points: Sequence[tuple[int, int]],
    width: int,
    height: int,
    x_at_column0: float,
    z_at_row0: float,
    x_step: float,
    z_step: float,
    points_meters: np.ndarray | None = None,
) -> tuple[np.ndarray, np.ndarray, bool]:
    points_m = (
        np.asarray(points, dtype=np.float64) * HORIZONTAL_QUANTIZATION_METERS
        if points_meters is None
        else np.asarray(points_meters, dtype=np.float64)
    )
    if points_m.shape != (len(points), 2):
        raise ValueError("Rasterized building points must have shape [point_count, 2]")
    minimum = np.min(points_m, axis=0)
    maximum = np.max(points_m, axis=0)
    column0 = max(0, int(math.ceil((minimum[0] - x_at_column0) / x_step)))
    column1 = min(width - 1, int(math.floor((maximum[0] - x_at_column0) / x_step)))
    # z_step is negative on the north-up grid.
    row0 = max(0, int(math.ceil((z_at_row0 - maximum[1]) / abs(z_step))))
    row1 = min(height - 1, int(math.floor((z_at_row0 - minimum[1]) / abs(z_step))))
    if column0 <= column1 and row0 <= row1:
        columns, rows = np.meshgrid(
            np.arange(column0, column1 + 1), np.arange(row0, row1 + 1)
        )
        x = x_at_column0 + columns * x_step
        z = z_at_row0 + rows * z_step
        inside = point_in_polygon_grid(x, z, points_m)
        if np.any(inside):
            return rows[inside], columns[inside], False
    centroid_x, centroid_z = polygon_centroid(points_m.tolist())
    column = int(round((centroid_x - x_at_column0) / x_step))
    row = int(round((centroid_z - z_at_row0) / z_step))
    if 0 <= column < width and 0 <= row < height:
        return np.asarray([row]), np.asarray([column]), True
    return np.asarray([], dtype=np.int64), np.asarray([], dtype=np.int64), False


def zone_records(recipe: dict[str, Any], zone: str) -> dict[int, dict[str, Any]]:
    prefix = f"buildings_{zone}_"
    records: dict[int, dict[str, Any]] = {}
    for batch in recipe.get("building_batches", []):
        if not str(batch.get("id", "")).startswith(prefix):
            continue
        for record in batch.get("buildings", []):
            feature_index = int(record["feature_index"])
            if feature_index in records:
                raise ValueError(f"Duplicate building recipe feature {feature_index}")
            records[feature_index] = record
    return records


def fallback_height_source(feature: dict[str, Any]) -> str:
    tags = feature.get("tags") or {}
    if tags.get("height") not in (None, ""):
        return "osm_height_tag"
    if tags.get("building:levels") not in (None, ""):
        return "osm_levels_derivative"
    return "deterministic_osm_identity_fallback"


def quantize_signed(value: float) -> int:
    code = int(round(value / VERTICAL_QUANTIZATION_METERS))
    if not -32768 <= code <= 32767:
        raise ValueError(f"Signed vertical value {value} is out of range")
    return code


def quantize_unsigned(value: float) -> int:
    code = int(round(value / VERTICAL_QUANTIZATION_METERS))
    if not 0 <= code <= 65535:
        raise ValueError(f"Unsigned vertical value {value} is out of range")
    return code


def _percentiles(values: Sequence[float]) -> dict[str, float]:
    array = np.asarray(values, dtype=np.float64)
    if array.size == 0:
        return {"maximum": 0.0, "p50": 0.0, "p95": 0.0}
    return {
        "maximum": float(np.max(array)),
        "p50": float(np.percentile(array, 50.0)),
        "p95": float(np.percentile(array, 95.0)),
    }


def signed_tile(value: int, positive: str, negative: str) -> str:
    return f"{negative}{abs(value):03d}" if value < 0 else f"{positive}{value:03d}"


def mesh_batch_key(points: Sequence[tuple[int, int]]) -> tuple[int, int]:
    centroid_q = polygon_centroid(points)
    x = centroid_q[0] * HORIZONTAL_QUANTIZATION_METERS
    z = centroid_q[1] * HORIZONTAL_QUANTIZATION_METERS
    return math.floor(x / MESH_BATCH_CELL_METERS), math.floor(z / MESH_BATCH_CELL_METERS)


def mesh_batch_name(key: tuple[int, int]) -> str:
    x, z = key
    return (
        "ProductionBuildingSurfaceLayer_"
        f"{signed_tile(x, 'xp', 'xn')}_{signed_tile(z, 'zp', 'zn')}"
    )


def facade_semantics(feature: dict[str, Any], osm_id: int, base_y: float) -> dict[str, int]:
    """Map OSM usage to a coherent, compact facade contract.

    The selector is authored once per source footprint and copied to every
    vertex.  The stable identity only chooses variation *within* a plausible
    family; it never mixes unrelated materials across one structure.
    """

    tags = feature.get("tags") or {}
    building = str(tags.get("building", "")).strip().lower()
    use = str(tags.get("building:use", tags.get("amenity", ""))).strip().lower()
    identity = zlib.crc32(f"{osm_id}:{building}:{use}".encode("utf-8")) & 0xFFFFFFFF

    industrial = {
        "hangar", "industrial", "warehouse", "shed", "garage", "garages",
        "service", "farm_auxiliary", "barn", "storage_tank",
    }
    commercial = {
        "commercial", "retail", "office", "hotel", "supermarket",
        "transportation", "train_station",
    }
    civic = {
        "civic", "public", "government", "school", "college", "university",
        "hospital", "dormitory", "church", "cathedral", "chapel",
    }
    residential = {
        "house", "detached", "semidetached_house", "apartments", "residential",
        "terrace", "bungalow", "cabin", "farm", "static_caravan",
    }

    if building in industrial or use in industrial:
        tile = (FACADE_TILE_CORRUGATED, FACADE_TILE_WEATHERED_METAL,
                FACADE_TILE_LIGHT_METAL)[identity % 3]
        semantic = SEMANTIC_INDUSTRIAL
    elif building in commercial or use in commercial:
        tile = FACADE_TILE_LIGHT_METAL if identity % 4 == 0 else FACADE_TILE_CONCRETE
        semantic = SEMANTIC_COMMERCIAL
    elif building in civic or use in civic:
        tile = FACADE_TILE_BRICK if identity % 3 else FACADE_TILE_CONCRETE
        semantic = SEMANTIC_CIVIC
    elif building in residential or use in residential:
        tile = FACADE_TILE_BRICK if identity % 4 else FACADE_TILE_CONCRETE
        semantic = SEMANTIC_RESIDENTIAL
    else:
        # Unclassified structures remain neutral rather than receiving a
        # visibly random industrial/residential mixture.
        tile = FACADE_TILE_CONCRETE
        semantic = SEMANTIC_OTHER

    return {
        "facade_tile": tile,
        "facade_semantic_code": semantic,
        "facade_seed_code": (identity >> 8) & 0xFF,
        "facade_base_height_code": int(round(
            max(0.0, min(1.0, (base_y + 64.0) / 128.0)) * 255.0
        )),
    }


def write_model_meta(path: Path, note: str) -> None:
    Path(str(path) + ".meta").write_text(
        f"""fileFormatVersion: 2
guid: {stable_guid(path)}
ModelImporter:
  serializedVersion: 24200
  internalIDToNameTable: []
  externalObjects: {{}}
  materials:
    materialImportMode: 0
    materialName: 0
    materialSearch: 1
    materialLocation: 1
  animations:
    legacyGenerateAnimations: 4
    bakeSimulation: 0
    resampleCurves: 1
    optimizeGameObjects: 0
    removeConstantScaleCurves: 0
    motionNodeName:
    animationCompression: 1
    animationRotationError: 0.5
    animationPositionError: 0.5
    animationScaleError: 0.5
    extraExposedTransformPaths: []
    extraUserProperties: []
    clipAnimations: []
    isReadable: 0
  meshes:
    lODScreenPercentages: []
    globalScale: 1
    meshCompression: 0
    addColliders: 0
    useSRGBMaterialColor: 1
    sortHierarchyByName: 1
    importPhysicalCameras: 0
    importVisibility: 0
    importBlendShapes: 0
    importCameras: 0
    importLights: 0
    nodeNameCollisionStrategy: 1
    fileIdsGeneration: 2
    swapUVChannels: 0
    generateSecondaryUV: 0
    useFileUnits: 1
    keepQuads: 0
    weldVertices: 1
    bakeAxisConversion: 0
    preserveHierarchy: 0
    skinWeightsMode: 0
    maxBonesPerVertex: 4
    minBoneWeight: 0.001
    optimizeBones: 1
    generateMeshLods: 0
    meshLodGenerationFlags: 0
    maximumMeshLod: -1
    meshOptimizationFlags: -1
    indexFormat: 0
    secondaryUVAngleDistortion: 8
    secondaryUVAreaDistortion: 15.000001
    secondaryUVHardAngle: 88
    secondaryUVMarginMethod: 1
    secondaryUVMinLightmapResolution: 40
    secondaryUVMinObjectScale: 1
    secondaryUVPackMargin: 4
    useFileScale: 1
    strictVertexDataChecks: 1
  tangentSpace:
    normalSmoothAngle: 60
    normalImportMode: 0
    tangentImportMode: 3
    normalCalculationMode: 4
    legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes: 0
    blendShapeNormalImportMode: 1
    normalSmoothingSource: 0
  referencedClips: []
  importAnimation: 0
  animationType: 0
  humanoidOversampling: 1
  avatarSetup: 0
  importBlendShapeDeformPercent: 1
  remapMaterialsIfMaterialImportModeIsNone: 0
  additionalBone: 0
  userData: {note}
  assetBundleName:
  assetBundleVariant:
""",
        encoding="utf-8",
        newline="\n",
    )


def write_obj_batches(
    records: Sequence[dict[str, Any]], output_dir: Path
) -> list[dict[str, Any]]:
    batches: dict[tuple[int, int], list[dict[str, Any]]] = defaultdict(list)
    for record in records:
        batches[mesh_batch_key(record["points"])].append(record)
    output_dir.mkdir(parents=True, exist_ok=True)
    results: list[dict[str, Any]] = []
    for key in sorted(batches):
        name = mesh_batch_name(key)
        path = output_dir / f"{name}.obj"
        lines = [
            "# QuestFlight Photoreal Aerial Surface V1",
            "# Closed OSM footprint prisms; +X east, +Y up, +Z north; metres",
            f"o {name}",
            f"g {name}",
            "s off",
        ]
        vertex_offset = 1
        triangle_count = 0
        vertex_count = 0
        for record in sorted(batches[key], key=lambda item: item["feature_index"]):
            base = record["base_code"] * VERTICAL_QUANTIZATION_METERS
            top = base + record["selected_height_code"] * VERTICAL_QUANTIZATION_METERS
            semantic_color = (
                record["facade_tile"] / 255.0,
                record["facade_seed_code"] / 255.0,
                record["facade_base_height_code"] / 255.0,
                record["facade_semantic_code"] / 255.0,
            )
            for x_q, z_q in record["points"]:
                x = x_q * HORIZONTAL_QUANTIZATION_METERS
                z = z_q * HORIZONTAL_QUANTIZATION_METERS
                color = " ".join(f"{value:.6f}" for value in semantic_color)
                lines.append(f"v {x:.3f} {base:.3f} {z:.3f} {color}")
                lines.append(f"v {x:.3f} {top:.3f} {z:.3f} {color}")
            indices = record["indices"]
            for offset in range(0, len(indices), 3):
                a, b, c = indices[offset : offset + 3]
                lines.append(
                    f"f {vertex_offset + a} {vertex_offset + b} {vertex_offset + c}"
                )
            count = len(record["points"]) * 2
            vertex_offset += count
            vertex_count += count
            triangle_count += len(indices) // 3
        path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
        results.append(
            {
                **file_record(path, "production_building_surface_mesh_batch"),
                "batch_key": {"x": key[0], "z": key[1]},
                "building_count": len(batches[key]),
                "material_slots": 1,
                "object_name": name,
                "triangle_count": triangle_count,
                "vertex_count": vertex_count,
            }
        )
    return results


def build(
    source_manifest_path: Path = SOURCE_MANIFEST,
    context_path: Path = CONTEXT_PATH,
    recipe_path: Path = RECIPE_PATH,
    coverage_report_path: Path = COVERAGE_REPORT_PATH,
    hero_replacements_path: Path = HERO_REPLACEMENTS_PATH,
    output_dir: Path = OUTPUT_DIR,
    write_meta: bool = True,
) -> dict[str, Any]:
    source_manifest_path = source_manifest_path.resolve()
    context_path = context_path.resolve()
    recipe_path = recipe_path.resolve()
    coverage_report_path = coverage_report_path.resolve()
    hero_replacements_path = hero_replacements_path.resolve()
    output_dir = output_dir.resolve()
    source = load_json(source_manifest_path)
    context = load_json(context_path)
    recipe = load_json(recipe_path)
    coverage = load_json(coverage_report_path)
    hero_replacements = load_json(hero_replacements_path)
    hero_osm_ids = {
        int(item["source_osm_id"]) for item in hero_replacements.get("replacements", [])
    }
    source_by_zone = {
        zone: int(coverage["zones"][zone]["source_footprints"])
        for zone in OWNERSHIP_ZONES
    }
    owned_recipe: dict[int, dict[str, Any]] = {}
    zone_by_feature: dict[int, str] = {}
    for zone in OWNERSHIP_ZONES:
        records_for_zone = zone_records(recipe, zone)
        if len(records_for_zone) != source_by_zone[zone]:
            raise ValueError(
                f"Pinned {zone} recipe has {len(records_for_zone)} buildings, "
                f"expected {source_by_zone[zone]}"
            )
        overlap = set(records_for_zone) & set(owned_recipe)
        if overlap:
            raise ValueError(f"Building zones overlap at feature {min(overlap)}")
        owned_recipe.update(records_for_zone)
        zone_by_feature.update({feature_index: zone for feature_index in records_for_zone})

    features = context["openstreetmap"]["features"]
    hero_feature_indices = {
        feature_index
        for feature_index in owned_recipe
        if int((features[feature_index].get("source") or {}).get("osm_id", -1))
        in hero_osm_ids
    }
    if len(hero_feature_indices) != len(hero_osm_ids):
        raise ValueError("Every active AirportCore hero replacement must map to one OSM footprint")
    hero_count_by_zone: Counter[str] = Counter(
        zone_by_feature[feature_index] for feature_index in hero_feature_indices
    )
    expected_by_zone = {
        zone: source_by_zone[zone] - hero_count_by_zone[zone] for zone in OWNERSHIP_ZONES
    }
    mapping = source["coordinate_mapping"]
    frame_contract = horizontal_frame_contract(mapping)
    grid = mapping["grid"]
    affine = mapping["pixel_center_affine_local_meters"]
    width = int(grid["width_pixels"])
    height = int(grid["height_pixels"])
    shape = (height, width)

    dtm_record = asset_by_semantic(source, "digital_terrain_model")
    surface_record = asset_by_semantic(source, "surface_features_canopy_building")
    confidence_record = asset_by_semantic(source, "building_height_confidence")
    dtm_path = verify_source_asset(dtm_record)
    surface_path = verify_source_asset(surface_record)
    confidence_path = verify_source_asset(confidence_record)
    dtm_codes = np.asarray(Image.open(dtm_path))
    surface = np.asarray(Image.open(surface_path))
    confidence = np.asarray(Image.open(confidence_path))
    if dtm_codes.shape != shape or dtm_codes.dtype != np.uint16:
        raise ValueError("Unexpected DTM encoding")
    if surface.shape != (*shape, 4) or surface.dtype != np.uint8:
        raise ValueError("Unexpected surface feature encoding")
    if confidence.shape != shape or confidence.dtype != np.uint8:
        raise ValueError("Unexpected confidence encoding")
    dtm_decode = dtm_record["encoding"]["decoded_source_z_meters"]
    dtm_meters = (
        dtm_codes.astype(np.float64) * float(dtm_decode["scale"])
        + float(dtm_decode["offset"])
    )
    origin_elevation = float(mapping["origin"]["source_elevation_msl_meters"])
    lidar_height = surface[:, :, 2].astype(np.float64) * 0.25

    mask = np.zeros(shape, dtype=np.uint8)
    records: list[dict[str, Any]] = []
    total_points = 0
    total_triangles = 0
    lidar_candidate_count = 0
    lidar_used_count = 0
    subpixel_fallback_count = 0
    fallback_counts: Counter[str] = Counter()
    base_deltas: list[float] = []
    height_deltas: list[float] = []
    represented_by_zone: Counter[str] = Counter()
    lidar_used_by_zone: Counter[str] = Counter()
    dtm_base_by_zone: Counter[str] = Counter()
    area_ratios: list[float] = []
    hausdorff_errors: list[float] = []
    source_point_total = 0
    hero_exclusions: list[dict[str, Any]] = []
    for feature_index in sorted(owned_recipe):
        if not 0 <= feature_index < len(features):
            raise ValueError(f"Building feature index {feature_index} is out of range")
        feature = features[feature_index]
        if feature.get("category") != "building":
            raise ValueError(f"Owned feature {feature_index} is not a building")
        source_points = decode_points(feature)
        source_points_product = (
            np.asarray(source_points, dtype=np.float64) * HORIZONTAL_QUANTIZATION_METERS
        )
        source_points_lidar = product_points_to_utm13n_delta(
            source_points_product, mapping
        )
        rows, columns, subpixel = polygon_pixels(
            source_points,
            width,
            height,
            float(affine["x_at_column0_row0"]),
            float(affine["z_at_column0_row0"]),
            float(affine["x_per_column"]),
            float(affine["z_per_row"]),
            source_points_lidar,
        )
        zone = zone_by_feature[feature_index]
        if subpixel:
            subpixel_fallback_count += 1
        if len(rows):
            mask[rows, columns] = 255
        source_identity = feature.get("source") or {}
        osm_id = int(source_identity.get("osm_id", 0))
        if feature_index in hero_feature_indices:
            hero_exclusions.append(
                {
                    "area_square_meters": abs(signed_area2(source_points))
                    * HORIZONTAL_QUANTIZATION_METERS
                    * HORIZONTAL_QUANTIZATION_METERS
                    * 0.5,
                    "feature_index": feature_index,
                    "osm_id": osm_id,
                    "owner": "ProductionHeroObjectLayer",
                    "zone": zone,
                }
            )
            continue

        simplified, simplification = simplify_footprint(source_points)
        reversed_winding = signed_area2(simplified) < 0
        points = list(reversed(simplified)) if reversed_winding else list(simplified)
        roof = triangulate_polygon(points)
        indices = closed_surface_indices(points, roof)
        if any(value != 2 for value in edge_multiplicity(indices).values()):
            raise ValueError(f"Building {feature_index} surface is not a closed 2-manifold")
        represented_by_zone[zone] += 1
        area_ratios.append(float(simplification["area_ratio"]))
        hausdorff_errors.append(float(simplification["hausdorff_meters"]))
        source_point_total += int(simplification["source_point_count"])
        legacy_base_y = float(owned_recipe[feature_index]["base_y_meters"])
        if len(rows):
            base_y = float(np.median(dtm_meters[rows, columns])) - origin_elevation
            base_deltas.append(base_y - legacy_base_y)
            dtm_base_by_zone[zone] += 1
        else:
            base_y = legacy_base_y

        fallback_height = float(feature["render_height_meters"])
        height_source = fallback_height_source(feature)
        fallback_counts[height_source] += 1
        pixel_heights = lidar_height[rows, columns] if len(rows) else np.asarray([])
        present = pixel_heights > 0.0
        selected_height = fallback_height
        observed_height = 0.0
        observed_confidence = 0.0
        support = 0.0
        lidar_used = False
        if np.any(present):
            lidar_candidate_count += 1
            observed_height = float(np.percentile(pixel_heights[present], 75.0))
            observed_confidence = float(np.median(confidence[rows[present], columns[present]]) / 255.0)
            support = float(np.count_nonzero(present) / len(rows))
            if (
                int(np.count_nonzero(present)) >= MINIMUM_LIDAR_PIXELS
                and observed_confidence >= MINIMUM_LIDAR_CONFIDENCE
                and support >= MINIMUM_LIDAR_SUPPORT
                and 2.0 <= observed_height <= 40.0
            ):
                selected_height = observed_height
                lidar_used = True
                lidar_used_count += 1
                lidar_used_by_zone[zone] += 1
        height_deltas.append(selected_height - fallback_height)

        tags = feature.get("tags") or {}
        flags = 0
        if lidar_used:
            flags |= FLAG_LIDAR_HEIGHT_USED | FLAG_LIDAR_HEIGHT_HEURISTIC
        if tags.get("height") not in (None, ""):
            flags |= FLAG_OSM_HEIGHT_TAG
        if tags.get("building:levels") not in (None, ""):
            flags |= FLAG_OSM_LEVELS_TAG
        if reversed_winding:
            flags |= FLAG_SOURCE_WINDING_REVERSED
        if zone == "2-4km":
            flags |= FLAG_ZONE_2_TO_4_KM
        part = str(source_identity.get("part", "geometry:0"))
        semantics = facade_semantics(feature, osm_id, base_y)
        records.append(
            {
                "base_code": quantize_signed(base_y),
                "confidence_code": int(round(observed_confidence * 255.0)),
                "fallback_height_code": quantize_unsigned(fallback_height),
                "feature_index": feature_index,
                "flags": flags,
                "indices": indices,
                "lidar_height_code": quantize_unsigned(observed_height),
                "osm_id": osm_id,
                "part_crc32": zlib.crc32(part.encode("utf-8")) & 0xFFFFFFFF,
                "points": points,
                "selected_height_code": quantize_unsigned(selected_height),
                "support_code": int(round(support * 255.0)),
                "zone": zone,
                **semantics,
            }
        )
        total_points += len(points)
        total_triangles += len(indices) // 3

    header = BUILDING_HEADER.pack(
        BUILDING_MAGIC,
        BUILDING_VERSION,
        BUILDING_HEADER.size,
        len(records),
        total_points,
        total_triangles,
        BUILDING_RECORD.size,
        HORIZONTAL_QUANTIZATION_METERS,
        VERTICAL_QUANTIZATION_METERS,
        4000.0,
    )
    payload = bytearray(header)
    for record in records:
        indices = record["indices"]
        points = record["points"]
        payload.extend(
            BUILDING_RECORD.pack(
                record["feature_index"],
                record["osm_id"],
                record["part_crc32"],
                len(points),
                record["flags"],
                record["base_code"],
                record["selected_height_code"],
                record["fallback_height_code"],
                record["lidar_height_code"],
                record["confidence_code"],
                record["support_code"],
                len(indices) // 3,
                len(indices),
            )
        )
        for x, z in points:
            payload.extend(POINT.pack(x, z))
        for index in indices:
            payload.extend(INDEX.pack(index))

    output_dir.mkdir(parents=True, exist_ok=True)
    layer_path = output_dir / LAYER_NAME
    mask_path = output_dir / MASK_NAME
    layer_path.write_bytes(payload)
    Image.fromarray(mask).save(mask_path, format="PNG", compress_level=9, optimize=False)
    mesh_batches = write_obj_batches(records, output_dir / "Generated")
    mesh_triangle_total = sum(int(item["triangle_count"]) for item in mesh_batches)
    mesh_vertex_total = sum(int(item["vertex_count"]) for item in mesh_batches)
    mesh_byte_total = sum(int(item["bytes"]) for item in mesh_batches)
    if mesh_triangle_total != total_triangles or mesh_vertex_total != total_points * 2:
        raise ValueError("Building OBJ batches do not match the semantic layer topology")
    if layer_path.stat().st_size > MAXIMUM_LAYER_BYTES:
        raise ValueError("ProductionBuildingSurfaceLayer exceeds its serialized byte budget")
    if total_triangles > MAXIMUM_LAYER_TRIANGLES:
        raise ValueError("ProductionBuildingSurfaceLayer exceeds its topology budget")

    abs_base_deltas = [abs(value) for value in base_deltas]
    manifest = {
        "assets": [
            {
                **file_record(layer_path, "ProductionBuildingSurfaceLayer"),
                "encoding": {
                    "byte_order": "little_endian",
                    "container": "QuestFlight Building Surface Layer v1",
                    "header_bytes": BUILDING_HEADER.size,
                    "record_header_bytes": BUILDING_RECORD.size,
                    "topology": "explicit closed roof, floor, and wall triangle indices",
                },
            },
            {
                **file_record(mask_path, "building_and_hero_surface_exclusion_mask"),
                "dimensions": {"height_pixels": height, "width_pixels": width},
                "encoding": {
                    "channel": "R",
                    "container": "PNG",
                    "excluded_from_canopy_code": 255,
                    "owners": ["ProductionBuildingSurfaceLayer", "ProductionHeroObjectLayer"],
                    "raster_horizontal_frame": "origin-relative UTM13N deltas",
                },
            },
            *mesh_batches,
        ],
        "budgets": {
            "mesh_batch_count": len(mesh_batches),
            "mesh_batch_total_bytes": mesh_byte_total,
            "mesh_batch_total_triangles": mesh_triangle_total,
            "mesh_batch_total_vertices": mesh_vertex_total,
            "maximum_layer_bytes": MAXIMUM_LAYER_BYTES,
            "maximum_layer_triangles": MAXIMUM_LAYER_TRIANGLES,
            "serialized_layer_bytes": layer_path.stat().st_size,
            "total_closed_surface_triangles": total_triangles,
        },
        "coordinate_mapping": {
            "geometry_horizontal_frame": "KBDU product-local WGS84 azimuthal equidistant",
            "horizontal_quantization_meters": HORIZONTAL_QUANTIZATION_METERS,
            "lidar_sampling_horizontal_frame": "origin-relative UTM13N deltas",
            "source": context["coordinate_frame"],
            "vertical_quantization_meters": VERTICAL_QUANTIZATION_METERS,
            "vertical_zero": f"source elevation {origin_elevation} m",
        },
        "horizontal_frame_contract": frame_contract,
        "coverage": {
            "coverage_fraction_of_owned_source_footprints": 1.0,
            "owned_source_footprint_count": len(records),
            "owned_zones": list(OWNERSHIP_ZONES),
            "per_band": {
                zone: {
                    "coverage_fraction": represented_by_zone[zone] / expected_by_zone[zone],
                    "hero_exclusion_count": hero_count_by_zone[zone],
                    "original_source_footprint_count": source_by_zone[zone],
                    "represented_footprint_count": represented_by_zone[zone],
                    "source_footprint_count": expected_by_zone[zone],
                    "status": "PASS"
                    if represented_by_zone[zone] == expected_by_zone[zone]
                    else "FAIL",
                }
                for zone in OWNERSHIP_ZONES
            },
            "raster_ownership_pixel_count": int(np.count_nonzero(mask)),
            "source_footprint_count": sum(expected_by_zone.values()),
            "hero_exclusion_count": len(hero_exclusions),
            "hero_exclusion_area_square_meters": sum(
                float(item["area_square_meters"]) for item in hero_exclusions
            ),
            "hero_exclusions": hero_exclusions,
            "simplified_geometry_point_count": total_points,
            "source_geometry_point_count": source_point_total,
            "subpixel_centroid_mask_fallback_count": subpixel_fallback_count,
        },
        "height_policy": {
            "building_height_quality_gate": source["building_height_quality"],
            "fallback_counts": dict(sorted(fallback_counts.items())),
            "lidar_candidate_count": lidar_candidate_count,
            "lidar_height_selection": "75th percentile of positive footprint pixels",
            "lidar_sampling_transform": "exact product AEQD to origin-relative UTM13N delta",
            "lidar_height_used_count": lidar_used_count,
            "lidar_height_used_count_by_band": {
                zone: lidar_used_by_zone[zone] for zone in OWNERSHIP_ZONES
            },
            "minimum_lidar_confidence": MINIMUM_LIDAR_CONFIDENCE,
            "minimum_lidar_pixel_count": MINIMUM_LIDAR_PIXELS,
            "minimum_lidar_support_fraction": MINIMUM_LIDAR_SUPPORT,
            "non_authoritative_lidar_height_use": True,
            "selected_minus_fallback_height_meters": _percentiles(
                [abs(value) for value in height_deltas]
            ),
            "surveyed_vertical_error_fraction_bound": None,
        },
        "facade_semantics": {
            "encoding": {
                "vertex_color_r": "AirportCore shared atlas tile index / 255",
                "vertex_color_g": "stable per-building variation seed / 255",
                "vertex_color_b": "building base height mapped from [-64,+64] m to [0,1]",
                "vertex_color_a": "semantic usage code / 255",
            },
            "material_policy": "one coherent usage-derived facade family per OSM footprint",
            "atlas_tiles": {
                "brick_masonry": FACADE_TILE_BRICK,
                "light_metal_panel": FACADE_TILE_LIGHT_METAL,
                "concrete_stucco": FACADE_TILE_CONCRETE,
                "corrugated_metal": FACADE_TILE_CORRUGATED,
                "weathered_metal": FACADE_TILE_WEATHERED_METAL,
            },
            "semantic_counts": dict(sorted(Counter(
                str(record["facade_semantic_code"]) for record in records
            ).items())),
        },
        "license_and_provenance": {
            "footprints": "OpenStreetMap ODbL-1.0 derivative; © OpenStreetMap contributors",
            "height_and_ground_rasters": source["license_and_provenance"],
            "raw_lidar_committed": False,
            "raw_osm_committed": False,
        },
        "limitations": [
            "Not for navigation and not surveyed building geometry.",
            "Lidar building heights are return-structure heuristics; the source has no LAS building class and no surveyed vertical-error study.",
            "Runtime prisms use a deterministic error-bounded footprint simplification (area ratio 0.90-1.05 and sampled Hausdorff <=10 m) to satisfy the selected-scene triangle ceiling; the source identity and error metrics remain in the manifest.",
            "Active AirportCore hero replacement footprints are excluded and remain owned by ProductionHeroObjectLayer.",
            "The runtime binary is an offline-authored data asset and is not integrated into the product scene by this workstream.",
            "Serialized size and total topology are static budgets, not physical Quest performance evidence.",
        ],
        "placement_comparison": {
            "absolute_dtm_base_minus_legacy_recipe_meters": _percentiles(abs_base_deltas),
            "comparison_is_not_survey_error": True,
            "dtm_base_count_by_band": {
                zone: dtm_base_by_zone[zone] for zone in OWNERSHIP_ZONES
            },
            "fallback_recipe_base_count": len(records) - len(base_deltas),
        },
        "schema": "questflight.photoreal-aerial-surface-v1.production-building-surface-layer.v1",
        "semantic_root": "ProductionBuildingSurfaceLayer",
        "mesh_batches": mesh_batches,
        "material_expectation": {
            "embedded_materials": 0,
            "material_slots_per_batch": 1,
            "policy": "Lead integration assigns one existing opaque shared building material; OBJ import must not create materials.",
        },
        "sources": {
            "coverage_report": file_record(coverage_report_path),
            "hero_replacements": file_record(hero_replacements_path),
            "lidar_manifest": file_record(source_manifest_path),
            "osm_context": file_record(context_path),
            "placement_recipe": file_record(recipe_path),
        },
        "topology": {
            "closed_manifold_edge_multiplicity": 2,
            "footprint_simplification": {
                "area_ratio_maximum": float(np.max(area_ratios)),
                "area_ratio_minimum": float(np.min(area_ratios)),
                "area_ratio_p50": float(np.percentile(area_ratios, 50.0)),
                "area_ratio_p95": float(np.percentile(area_ratios, 95.0)),
                "maximum_allowed_hausdorff_meters": MAXIMUM_HAUSDORFF_ERROR_METERS,
                "sampled_hausdorff_maximum_meters": float(np.max(hausdorff_errors)),
                "sampled_hausdorff_p50_meters": float(np.percentile(hausdorff_errors, 50.0)),
                "sampled_hausdorff_p95_meters": float(np.percentile(hausdorff_errors, 95.0)),
                "maximum_allowed_inflation_ratio": MAXIMUM_FOOTPRINT_AREA_RATIO - 1.0,
                "maximum_allowed_area_loss_ratio": 1.0 - MINIMUM_FOOTPRINT_AREA_RATIO,
                "vertex_reduction_fraction": 1.0 - total_points / source_point_total,
            },
            "record_count": len(records),
            "total_surface_triangles": total_triangles,
            "total_vertices_when_expanded": total_points * 2,
        },
    }
    manifest_path = output_dir / MANIFEST_NAME
    write_json(manifest_path, manifest)
    if write_meta:
        write_folder_meta(output_dir, "Footprint-preserving complete OSM building surface layer")
        write_folder_meta(
            output_dir / "Generated",
            "Bounded Unity-native OBJ batches for ProductionBuildingSurfaceLayer",
        )
        write_text_meta(
            layer_path,
            "ProductionBuildingSurfaceLayer compact closed-surface binary; OSM ODbL + USGS derivative",
        )
        write_texture_meta(
            mask_path,
            "Exact building plus active-hero exclusion mask used to prevent canopy duplication; linear R8; no mips",
        )
        write_text_meta(manifest_path, "ProductionBuildingSurfaceLayer provenance and metrics")
        for item in mesh_batches:
            write_model_meta(
                REPOSITORY_ROOT / item["path"],
                "Closed OSM building prism batch; no embedded material; assign one opaque shared material",
            )
    return manifest


def parse_building_layer(path: Path) -> dict[str, Any]:
    payload = memoryview(path.read_bytes())
    if len(payload) < BUILDING_HEADER.size:
        raise ValueError("Building layer is truncated")
    (
        magic,
        version,
        header_bytes,
        record_count,
        total_points,
        total_triangles,
        record_bytes,
        horizontal_scale,
        vertical_scale,
        ownership_radius,
    ) = BUILDING_HEADER.unpack_from(payload, 0)
    if magic != BUILDING_MAGIC or version != BUILDING_VERSION:
        raise ValueError("Unsupported building layer")
    if header_bytes != BUILDING_HEADER.size or record_bytes != BUILDING_RECORD.size:
        raise ValueError("Building layer struct-size mismatch")
    offset = header_bytes
    records: list[dict[str, Any]] = []
    for _ in range(record_count):
        if offset + BUILDING_RECORD.size > len(payload):
            raise ValueError("Building record is truncated")
        values = BUILDING_RECORD.unpack_from(payload, offset)
        offset += BUILDING_RECORD.size
        point_count = values[3]
        triangle_count = values[11]
        index_count = values[12]
        if index_count != triangle_count * 3:
            raise ValueError("Building triangle/index count mismatch")
        points: list[tuple[int, int]] = []
        for _point in range(point_count):
            points.append(POINT.unpack_from(payload, offset))
            offset += POINT.size
        indices: list[int] = []
        for _index in range(index_count):
            (index,) = INDEX.unpack_from(payload, offset)
            indices.append(index)
            offset += INDEX.size
        records.append(
            {
                "base_code": values[5],
                "confidence_code": values[9],
                "fallback_height_code": values[7],
                "feature_index": values[0],
                "flags": values[4],
                "indices": indices,
                "lidar_height_code": values[8],
                "osm_id": values[1],
                "part_crc32": values[2],
                "points": points,
                "selected_height_code": values[6],
                "support_code": values[10],
                "triangle_count": triangle_count,
            }
        )
    if offset != len(payload):
        raise ValueError("Building layer has trailing bytes")
    if sum(len(record["points"]) for record in records) != total_points:
        raise ValueError("Building point total mismatch")
    if sum(record["triangle_count"] for record in records) != total_triangles:
        raise ValueError("Building triangle total mismatch")
    return {
        "horizontal_scale": horizontal_scale,
        "ownership_radius": ownership_radius,
        "records": records,
        "total_points": total_points,
        "total_triangles": total_triangles,
        "vertical_scale": vertical_scale,
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-manifest", type=Path, default=SOURCE_MANIFEST)
    parser.add_argument("--context", type=Path, default=CONTEXT_PATH)
    parser.add_argument("--recipe", type=Path, default=RECIPE_PATH)
    parser.add_argument("--coverage-report", type=Path, default=COVERAGE_REPORT_PATH)
    parser.add_argument("--hero-replacements", type=Path, default=HERO_REPLACEMENTS_PATH)
    parser.add_argument("--output", type=Path, default=OUTPUT_DIR)
    parser.add_argument("--no-meta", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    manifest = build(
        source_manifest_path=args.source_manifest,
        context_path=args.context,
        recipe_path=args.recipe,
        coverage_report_path=args.coverage_report,
        hero_replacements_path=args.hero_replacements,
        output_dir=args.output,
        write_meta=not args.no_meta,
    )
    print(
        json.dumps(
            {
                "budgets": manifest["budgets"],
                "coverage": manifest["coverage"],
                "height_policy": manifest["height_policy"],
            },
            indent=2,
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
