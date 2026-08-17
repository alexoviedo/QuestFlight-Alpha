#!/usr/bin/env python3
"""Compile full-map Boulder vegetation into a compact Quest runtime database.

The source orthophotos, downloaded reference models, and intermediate masks stay
outside Git.  The committed derivative contains only deterministic, quantized
tree/shrub placements and a manifest.  Geometry remains the project-owned,
Quest-bounded three-LOD tree library compiled separately in Blender.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import math
import struct
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path

import cv2
import numpy as np
from PIL import Image
from scipy.spatial import cKDTree


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
GEOSPATIAL_TOOLS = REPOSITORY_ROOT / "tools" / "aerial_surface"
if str(GEOSPATIAL_TOOLS) not in sys.path:
    sys.path.insert(0, str(GEOSPATIAL_TOOLS))

from geospatial_frame import product_aeqd_to_utm13n_delta


MAGIC = b"QFFMVG2\0"
VERSION = 2
HEADER = struct.Struct("<8sIffffIII")
PAGE = struct.Struct("<II")
INSTANCE = struct.Struct("<HHiHHHBBBBB")
ANALYSIS_DOWNSAMPLE = 4
RUNTIME_PAGE_METERS = 1000.0
RUNTIME_GRID_MIN = -12000.0
RUNTIME_COLUMNS = 24
RUNTIME_ROWS = 24
MAX_INSTANCES_PER_RUNTIME_PAGE = 1600
ACCUMULATOR_TRIM_THRESHOLD = 2200
ACCUMULATOR_RETAIN_COUNT = 1800
RUNTIME_CELL_METERS = 15.0
MIN_COMPONENT_PIXELS = 7
DEFAULT_LIDAR_MANIFEST = (
    REPOSITORY_ROOT
    / "QuestFlight/Assets/Production/Environment/AerialSceneryV1/Lidar/"
    "KBDU_LidarManifestV1.json"
)


@dataclass(frozen=True)
class Placement:
    x: float
    y: float
    z: float
    height: float
    crown: float
    rotation: float
    red: int
    green: int
    blue: int
    seed: int
    kind: int
    confidence: float
    height_source: str = "orthophoto_allometry"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--imagery-root", type=Path, required=True)
    parser.add_argument("--clipmap-manifest", type=Path, required=True)
    parser.add_argument("--building-database", type=Path, required=True)
    parser.add_argument("--terrain-manifest", type=Path, required=True)
    parser.add_argument("--accepted-near-placements", type=Path)
    parser.add_argument(
        "--lidar-manifest",
        type=Path,
        default=DEFAULT_LIDAR_MANIFEST,
        help="Aligned compact 3DEP manifest used only as bounded height evidence.",
    )
    parser.add_argument("--output-binary", type=Path, required=True)
    parser.add_argument("--output-manifest", type=Path, required=True)
    parser.add_argument("--debug-directory", type=Path)
    parser.add_argument(
        "--only-pages",
        help="Optional semicolon-delimited high-page coordinates, e.g. 17,18;18,18",
    )
    return parser.parse_args()


class CanopyHeightSampler:
    """Sample the aligned compact 3DEP canopy-height derivative.

    The lidar raster has no reliable vegetation classes in this work unit, so it
    never creates a placement.  It can only supply height after the orthophoto
    canopy classifier has already established vegetation ownership.
    """

    def __init__(self, manifest_path: Path):
        document = json.loads(manifest_path.read_text(encoding="utf-8"))
        record = next(
            item
            for item in document["assets"]
            if item.get("semantic") == "surface_features_canopy_building"
        )
        source = Path(record["path"])
        if not source.is_absolute():
            source = REPOSITORY_ROOT / source
        if sha256(source) != record["sha256"]:
            raise ValueError(f"Canopy-height asset hash mismatch: {source}")
        rgba = np.asarray(Image.open(source).convert("RGBA"))
        self.height = rgba[:, :, 0].astype(np.float32) * 0.25
        self.density = rgba[:, :, 1].astype(np.float32) / 255.0
        self.mapping = document["coordinate_mapping"]
        local_affine = self.mapping["pixel_center_affine_local_meters"]
        self.x0 = float(local_affine["x_at_column0_row0"])
        self.z0 = float(local_affine["z_at_column0_row0"])
        self.dx = float(local_affine["x_per_column"])
        self.dz = float(local_affine["z_per_row"])
        self.manifest_path = manifest_path

    def sample(self, x: float, z: float, crown: float) -> tuple[float | None, float]:
        # The retained derivative is a 4.096 km square.  Reject the overwhelming
        # full-map majority before invoking the exact CRS transform.
        if abs(x) > 2200.0 or abs(z) > 2200.0:
            return None, 0.0
        x_utm, z_utm = product_aeqd_to_utm13n_delta(
            np.asarray(x, dtype=np.float64),
            np.asarray(z, dtype=np.float64),
            self.mapping,
        )
        column = int(round((float(x_utm) - self.x0) / self.dx))
        row = int(round((float(z_utm) - self.z0) / self.dz))
        if not (0 <= row < self.height.shape[0] and 0 <= column < self.height.shape[1]):
            return None, 0.0
        pixel_size = max(abs(self.dx), abs(self.dz))
        radius = int(np.clip(math.ceil(crown * 0.38 / pixel_size), 1, 4))
        row0 = max(0, row - radius)
        row1 = min(self.height.shape[0], row + radius + 1)
        column0 = max(0, column - radius)
        column1 = min(self.height.shape[1], column + radius + 1)
        heights = self.height[row0:row1, column0:column1]
        density = self.density[row0:row1, column0:column1]
        valid = (heights >= 2.0) & (density >= 0.04)
        support = int(np.count_nonzero(valid))
        if support == 0:
            return None, 0.0
        values = heights[valid]
        observed = float(np.quantile(values, 0.90))
        support_fraction = support / float(valid.size)
        confidence = float(
            np.clip(support_fraction * 1.35 + np.mean(density[valid]) * 0.45, 0.0, 1.0)
        )
        if observed < 2.0 or observed > 30.0 or confidence < 0.16:
            return None, confidence
        return observed, confidence


class TerrainSampler:
    def __init__(self, path: Path):
        document = json.loads(path.read_text(encoding="utf-8"))
        self.layers = []
        for item in document["layers"]:
            raw = base64.b64decode(item["height_cm_little_endian_base64"])
            values = np.frombuffer(raw, dtype="<i2").astype(np.float32)
            values = values.reshape((int(item["height"]), int(item["width"])))
            values *= float(item["height_quantization_meters"])
            self.layers.append(
                (
                    float(item["minimum_enu_meters"][0]),
                    float(item["minimum_enu_meters"][1]),
                    float(item["maximum_enu_meters"][0]),
                    float(item["maximum_enu_meters"][1]),
                    float(item["spacing_meters"]),
                    values,
                )
            )

    def sample(self, x: float, z: float) -> float:
        for minimum_x, minimum_z, maximum_x, maximum_z, spacing, values in self.layers:
            if minimum_x <= x <= maximum_x and minimum_z <= z <= maximum_z:
                column = np.clip((x - minimum_x) / spacing, 0.0, values.shape[1] - 1.0)
                row_from_south = np.clip(
                    (z - minimum_z) / spacing, 0.0, values.shape[0] - 1.0
                )
                # Terrain JSON rows are authored south-to-north.
                x0 = int(math.floor(column))
                z0 = int(math.floor(row_from_south))
                x1 = min(values.shape[1] - 1, x0 + 1)
                z1 = min(values.shape[0] - 1, z0 + 1)
                tx = column - x0
                tz = row_from_south - z0
                south = values[z0, x0] * (1.0 - tx) + values[z0, x1] * tx
                north = values[z1, x0] * (1.0 - tx) + values[z1, x1] * tx
                return float(south * (1.0 - tz) + north * tz)
        raise ValueError(f"Terrain sample outside full-map bounds: {x}, {z}")


class BuildingIndex:
    def __init__(self, path: Path):
        document = json.loads(path.read_text(encoding="utf-8"))
        self.page_size = float(document["page_size_meters"])
        self.quantization = float(document["quantization_meters"])
        self.pages: dict[tuple[int, int], list[np.ndarray]] = defaultdict(list)
        for page in document["pages"]:
            column = int(page["column"])
            row = int(page["row"])
            origin_x = RUNTIME_GRID_MIN + column * self.page_size
            origin_z = RUNTIME_GRID_MIN + row * self.page_size
            for building in page["buildings"]:
                coordinates = building["lod0_q"]
                polygon = np.asarray(
                    [
                        (
                            origin_x + coordinates[index] * self.quantization,
                            origin_z + coordinates[index + 1] * self.quantization,
                        )
                        for index in range(0, len(coordinates), 2)
                    ],
                    dtype=np.float32,
                )
                if len(polygon) >= 3:
                    self.pages[(column, row)].append(polygon)

    def polygons_for_bounds(
        self, minimum_x: float, minimum_z: float, maximum_x: float, maximum_z: float
    ) -> list[np.ndarray]:
        minimum_column = max(0, int(math.floor((minimum_x - RUNTIME_GRID_MIN) / self.page_size)))
        maximum_column = min(
            RUNTIME_COLUMNS - 1,
            int(math.floor((maximum_x - RUNTIME_GRID_MIN) / self.page_size)),
        )
        minimum_row = max(0, int(math.floor((minimum_z - RUNTIME_GRID_MIN) / self.page_size)))
        maximum_row = min(
            RUNTIME_ROWS - 1,
            int(math.floor((maximum_z - RUNTIME_GRID_MIN) / self.page_size)),
        )
        result = []
        for row in range(minimum_row, maximum_row + 1):
            for column in range(minimum_column, maximum_column + 1):
                result.extend(self.pages.get((column, row), ()))
        return result


def selected_pages(value: str | None) -> set[tuple[int, int]] | None:
    if not value:
        return None
    result = set()
    for token in value.split(";"):
        column, row = token.split(",")
        result.add((int(column), int(row)))
    return result


def local_standard_deviation(gray: np.ndarray, kernel: int = 7) -> np.ndarray:
    values = gray.astype(np.float32)
    mean = cv2.boxFilter(values, -1, (kernel, kernel), normalize=True)
    squared = cv2.boxFilter(values * values, -1, (kernel, kernel), normalize=True)
    return np.sqrt(np.maximum(0.0, squared - mean * mean))


def canopy_mask(image_bgr: np.ndarray, building_mask: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    hsv = cv2.cvtColor(image_bgr, cv2.COLOR_BGR2HSV)
    red = image_bgr[:, :, 2].astype(np.float32)
    green = image_bgr[:, :, 1].astype(np.float32)
    blue = image_bgr[:, :, 0].astype(np.float32)
    gray = cv2.cvtColor(image_bgr, cv2.COLOR_BGR2GRAY)
    texture = local_standard_deviation(gray)
    excess_green = green * 2.0 - red - blue
    hue = hsv[:, :, 0]
    saturation = hsv[:, :, 1]
    value = hsv[:, :, 2]

    green_family = (
        (hue >= 12)
        & (hue <= 100)
        & (green > 30.0)
        & (green >= blue * 0.92)
        & (value < 238)
    )
    # Seed only from the high local contrast characteristic of crown/branch/shadow
    # structure.  Ordinary grass can be green but is substantially smoother at this
    # 1.33 m analysis scale.  A bounded hysteresis growth recovers crown interiors
    # without allowing one noisy field pixel to flood an entire lawn.
    seed = green_family & (
        ((texture >= 12.5) & (excess_green >= -5.0) & (saturation >= 24))
        | ((texture >= 9.0) & (excess_green >= 8.0) & (saturation >= 34))
    )
    support = green_family & (texture >= 5.2) & (excess_green >= -7.0) & (saturation >= 20)
    binary = np.where(seed & (building_mask == 0), 255, 0).astype(np.uint8)
    support_binary = support & (building_mask == 0)
    growth_kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3))
    for _iteration in range(3):
        binary = cv2.dilate(binary, growth_kernel, iterations=1)
        binary = np.where(support_binary, binary, 0).astype(np.uint8)
    binary = cv2.morphologyEx(
        binary,
        cv2.MORPH_CLOSE,
        cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5)),
        iterations=1,
    )
    binary = cv2.morphologyEx(
        binary,
        cv2.MORPH_OPEN,
        cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3)),
        iterations=1,
    )
    binary[building_mask != 0] = 0
    confidence = (
        np.clip((texture - 4.0) / 14.0, 0.0, 1.0) * 0.55
        + np.clip((excess_green + 3.0) / 32.0, 0.0, 1.0) * 0.30
        + np.clip(saturation.astype(np.float32) / 150.0, 0.0, 1.0) * 0.15
    )
    return binary, confidence


def rasterize_buildings(
    index: BuildingIndex,
    minimum_x: float,
    minimum_z: float,
    maximum_x: float,
    maximum_z: float,
    shape: tuple[int, int],
) -> np.ndarray:
    height, width = shape
    mask = np.zeros(shape, dtype=np.uint8)
    scale_x = width / (maximum_x - minimum_x)
    scale_z = height / (maximum_z - minimum_z)
    for polygon in index.polygons_for_bounds(minimum_x, minimum_z, maximum_x, maximum_z):
        points = np.empty((len(polygon), 2), dtype=np.int32)
        points[:, 0] = np.rint((polygon[:, 0] - minimum_x) * scale_x).astype(np.int32)
        points[:, 1] = np.rint((maximum_z - polygon[:, 1]) * scale_z).astype(np.int32)
        cv2.fillPoly(mask, [points], 255)
    # Reject stems within roughly 1.5 m of a wall while still allowing crowns to overhang.
    return cv2.dilate(mask, np.ones((3, 3), dtype=np.uint8), iterations=1)


def lit_color(image_bgr: np.ndarray, mask: np.ndarray, x: int, y: int, radius: int) -> tuple[int, int, int]:
    y0 = max(0, y - radius)
    y1 = min(image_bgr.shape[0], y + radius + 1)
    x0 = max(0, x - radius)
    x1 = min(image_bgr.shape[1], x + radius + 1)
    colors = image_bgr[y0:y1, x0:x1].reshape(-1, 3)
    valid = mask[y0:y1, x0:x1].reshape(-1) != 0
    colors = colors[valid]
    if len(colors) == 0:
        colors = image_bgr[y : y + 1, x : x + 1].reshape(-1, 3)
    luminance = colors[:, 2] * 0.2126 + colors[:, 1] * 0.7152 + colors[:, 0] * 0.0722
    cutoff = np.quantile(luminance, 0.42)
    colors = colors[luminance >= cutoff]
    blue, green, red = np.median(colors, axis=0)
    # The shader applies scene lighting; source imagery color is therefore lifted only
    # enough to represent a fully lit crown instead of baking photographed shadow twice.
    maximum = max(float(red), float(green), float(blue), 1.0)
    lift = min(1.18, 164.0 / maximum)
    return (
        int(np.clip(round(red * lift), 28, 220)),
        int(np.clip(round(green * lift), 36, 220)),
        int(np.clip(round(blue * lift), 24, 205)),
    )


def hashed_u32(*values: int) -> int:
    result = 2166136261
    for value in values:
        result ^= value & 0xFFFFFFFF
        result = (result * 16777619) & 0xFFFFFFFF
    return result


def evidence_ranked_dimensions(
    crown: float,
    component_area_m2: float,
    local_edge_diameter: float,
    structure: float,
    saturation: float,
    lit_value: float,
    lidar_height: float | None,
    lidar_confidence: float,
) -> tuple[float, int, str]:
    """Derive physical dimensions only from local, reproducible evidence."""
    shrub = (
        component_area_m2 < 58.0
        and crown < 4.8
        and local_edge_diameter < 6.2
    )
    if shrub:
        ratio = 0.55 + structure * 0.22 + saturation * 0.10
        return float(np.clip(crown * ratio, 1.0, 4.2)), 2, "orthophoto_shrub"

    crown_maturity = float(np.clip((crown - 3.0) / 12.5, 0.0, 1.0))
    grove_scale = float(np.clip(math.log1p(component_area_m2) / math.log1p(2400.0), 0.0, 1.0))
    shadow_structure = structure * (1.0 - lit_value * 0.22)
    maturity = float(
        np.clip(
            crown_maturity * 0.43
            + shadow_structure * 0.31
            + saturation * 0.14
            + grove_scale * 0.12,
            0.0,
            1.0,
        )
    )
    # Ordinary urban/open-grown trees use a broad but conservative crown-height
    # relationship.  Only strong observed crown/shadow structure earns the tall,
    # narrow increment; a hash cannot turn an otherwise young tree into a mature one.
    tall_structure = max(0.0, structure - 0.68) / 0.32
    tonal_relief_evidence = float(np.clip((0.72 - lit_value) / 0.50, 0.0, 1.0))
    allometric_height = crown * (
        0.80
        + maturity * 0.35
        + tonal_relief_evidence * 0.60
        + tall_structure * 0.15
    )
    source = "orthophoto_context_allometry"
    if lidar_height is not None:
        # The source lacks vegetation classes, so even strong lidar remains bounded
        # height evidence rather than unconditional semantic truth.
        blend = float(np.clip(0.35 + lidar_confidence * 0.45, 0.35, 0.80))
        tree_height = allometric_height * (1.0 - blend) + lidar_height * blend
        source = "orthophoto_owned_lidar_blend"
    else:
        tree_height = allometric_height
    tree_height = float(np.clip(tree_height, 4.5, 27.5))
    kind = 1 if tree_height / max(crown, 0.01) >= 2.05 and crown <= 8.5 else 0
    return tree_height, kind, source


def page_placements(
    image_bgr: np.ndarray,
    mask: np.ndarray,
    confidence: np.ndarray,
    minimum_x: float,
    minimum_z: float,
    maximum_x: float,
    maximum_z: float,
    column: int,
    row: int,
    terrain: TerrainSampler,
    lidar: CanopyHeightSampler | None,
) -> list[Placement]:
    count, labels, statistics, _centroids = cv2.connectedComponentsWithStats(mask, 8)
    meters_per_pixel_x = (maximum_x - minimum_x) / mask.shape[1]
    meters_per_pixel_z = (maximum_z - minimum_z) / mask.shape[0]
    result: list[Placement] = []
    hsv = cv2.cvtColor(image_bgr, cv2.COLOR_BGR2HSV)

    for label in range(1, count):
        area_pixels = int(statistics[label, cv2.CC_STAT_AREA])
        if area_pixels < MIN_COMPONENT_PIXELS:
            continue
        x0 = int(statistics[label, cv2.CC_STAT_LEFT])
        y0 = int(statistics[label, cv2.CC_STAT_TOP])
        width = int(statistics[label, cv2.CC_STAT_WIDTH])
        height = int(statistics[label, cv2.CC_STAT_HEIGHT])
        component = np.where(labels[y0 : y0 + height, x0 : x0 + width] == label, 255, 0).astype(np.uint8)
        distance = cv2.distanceTransform(component, cv2.DIST_L2, 5)

        component_area_m2 = area_pixels * meters_per_pixel_x * meters_per_pixel_z
        target_count = max(1, int(round(component_area_m2 / 92.0)))
        # Tiny components are shrubs/isolated crowns; continuous groves get multiple
        # overlapping crowns rather than one opaque hull.
        candidate_mask = component != 0
        candidate_y, candidate_x = np.where(candidate_mask)
        ranked = []
        for local_x, local_y in zip(candidate_x.tolist(), candidate_y.tolist()):
            image_x = x0 + local_x
            image_y = y0 + local_y
            source_confidence = float(confidence[image_y, image_x])
            edge_distance = float(distance[local_y, local_x])
            hashed = hashed_u32(column, row, label, image_x, image_y)
            ranked.append((source_confidence + min(edge_distance, 5.0) * 0.035, hashed, image_x, image_y, edge_distance))
        ranked.sort(key=lambda item: (-item[0], item[1]))

        chosen: list[tuple[float, int, int, int, float]] = []
        minimum_spacing_pixels = max(3.0, 4.8 / max(meters_per_pixel_x, 0.01))
        bucket_size = max(1.0, minimum_spacing_pixels)
        buckets: dict[tuple[int, int], list[tuple[int, int]]] = defaultdict(list)
        for score, hashed, image_x, image_y, edge_distance in ranked:
            if edge_distance < 1.30:
                continue
            bucket_x = int(math.floor(image_x / bucket_size))
            bucket_y = int(math.floor(image_y / bucket_size))
            nearby = (
                point
                for adjacent_y in range(bucket_y - 1, bucket_y + 2)
                for adjacent_x in range(bucket_x - 1, bucket_x + 2)
                for point in buckets.get((adjacent_x, adjacent_y), ())
            )
            if any(
                (image_x - previous_x) ** 2 + (image_y - previous_y) ** 2
                < minimum_spacing_pixels ** 2
                for previous_x, previous_y in nearby
            ):
                continue
            chosen.append((score, hashed, image_x, image_y, edge_distance))
            buckets[(bucket_x, bucket_y)].append((image_x, image_y))
            if len(chosen) >= target_count:
                break

        if not chosen:
            continue
        points = np.asarray([(item[2], item[3]) for item in chosen], dtype=np.float64)
        if len(points) > 1:
            neighbor_pixels = cKDTree(points).query(points, k=2)[0][:, 1]
        else:
            equivalent_diameter = math.sqrt(4.0 * component_area_m2 / math.pi)
            neighbor_pixels = np.asarray(
                [equivalent_diameter / max(meters_per_pixel_x, 0.01)],
                dtype=np.float64,
            )
        territory_diameter = math.sqrt(
            4.0 * component_area_m2 / max(1, len(chosen)) / math.pi
        )

        for chosen_index, (score, hashed, image_x, image_y, edge_distance) in enumerate(chosen):
            neighbor_diameter = float(neighbor_pixels[chosen_index] * meters_per_pixel_x)
            local_edge_diameter = float(edge_distance * 2.0 * meters_per_pixel_x)
            preliminary_structure = float(confidence[image_y, image_x])
            preliminary_saturation = float(
                np.clip(hsv[image_y, image_x, 1] / 150.0, 0.0, 1.0)
            )
            preliminary_lit = float(
                np.clip(hsv[image_y, image_x, 2] / 210.0, 0.0, 1.0)
            )
            observed_shape_scale = float(
                np.clip(
                    0.74
                    + preliminary_structure * 0.24
                    + preliminary_saturation * 0.10
                    + (1.0 - preliminary_lit) * 0.08,
                    0.74,
                    1.16,
                )
            )
            # In a connected grove, distance to the outer mask edge measures the
            # grove, not the individual crown.  Neighbour spacing and per-tree
            # territory keep one large component from saturating every crown.
            crown = float(
                np.clip(
                    (
                        neighbor_diameter * 0.54
                        + min(local_edge_diameter * 1.18, territory_diameter * 1.45) * 0.28
                        + territory_diameter * 0.18
                    )
                    * observed_shape_scale,
                    2.2,
                    17.5,
                )
            )
            world_x = minimum_x + (image_x + 0.5) * meters_per_pixel_x
            world_z = maximum_z - (image_y + 0.5) * meters_per_pixel_z
            if not (-12000.0 <= world_x < 12000.0 and -12000.0 <= world_z < 12000.0):
                continue
            analysis_radius = max(
                2,
                int(round(crown / max(meters_per_pixel_x, 0.01) * 0.45)),
            )
            structure = preliminary_structure
            saturation = preliminary_saturation
            lit_value = preliminary_lit
            lidar_height, lidar_confidence = (
                lidar.sample(world_x, world_z, crown)
                if lidar is not None
                else (None, 0.0)
            )
            tree_height, kind, height_source = evidence_ranked_dimensions(
                crown,
                component_area_m2,
                local_edge_diameter,
                structure,
                saturation,
                lit_value,
                lidar_height,
                lidar_confidence,
            )
            red, green, blue = lit_color(
                image_bgr,
                mask,
                image_x,
                image_y,
                analysis_radius,
            )
            result.append(
                Placement(
                    x=world_x,
                    y=terrain.sample(world_x, world_z),
                    z=world_z,
                    height=tree_height,
                    crown=crown,
                    rotation=(hashed & 0xFFFF) * 360.0 / 65535.0,
                    red=red,
                    green=green,
                    blue=blue,
                    seed=(hashed >> 24) & 0xFF,
                    kind=kind,
                    confidence=score,
                    height_source=height_source,
                )
            )
    return result


def runtime_page(placement: Placement) -> tuple[int, int]:
    return (
        int(math.floor((placement.x - RUNTIME_GRID_MIN) / RUNTIME_PAGE_METERS)),
        int(math.floor((placement.z - RUNTIME_GRID_MIN) / RUNTIME_PAGE_METERS)),
    )


def runtime_cell(placement: Placement) -> tuple[int, int]:
    return (
        int(math.floor(placement.x / RUNTIME_CELL_METERS)),
        int(math.floor(placement.z / RUNTIME_CELL_METERS)),
    )


def placement_rank(placement: Placement) -> tuple[float, int]:
    return (
        placement.confidence,
        -hashed_u32(
            int(round(placement.x * 10)),
            int(round(placement.z * 10)),
            placement.seed,
        ),
    )


def accumulate_runtime_candidates(
    accumulators: dict[tuple[int, int], dict[tuple[int, int], Placement]],
    placements: list[Placement],
) -> None:
    for placement in placements:
        page = runtime_page(placement)
        if not (0 <= page[0] < RUNTIME_COLUMNS and 0 <= page[1] < RUNTIME_ROWS):
            continue
        cells = accumulators[page]
        cell = runtime_cell(placement)
        previous = cells.get(cell)
        if previous is None or placement_rank(placement) > placement_rank(previous):
            cells[cell] = placement
        if len(cells) > ACCUMULATOR_TRIM_THRESHOLD:
            retained = sorted(cells.values(), key=placement_rank, reverse=True)[
                :ACCUMULATOR_RETAIN_COUNT
            ]
            cells.clear()
            cells.update((runtime_cell(item), item) for item in retained)


def accepted_near_placements(path: Path) -> tuple[float, list[Placement]]:
    document = json.loads(path.read_text(encoding="utf-8"))
    radius = float(document.get("near_radius_meters", 750.0))
    result = []
    for item in document["instances"]:
        crown = float(item["crown_diameter"])
        height = float(item["height"])
        result.append(
            Placement(
                x=float(item["x"]),
                y=float(item["y"]),
                z=float(item["z"]),
                height=height,
                crown=crown,
                rotation=float(item["rotation_degrees"]),
                red=int(np.clip(round(float(item["tint_r"]) * 255.0), 0, 255)),
                green=int(np.clip(round(float(item["tint_g"]) * 255.0), 0, 255)),
                blue=int(np.clip(round(float(item["tint_b"]) * 255.0), 0, 255)),
                seed=int(np.clip(round(float(item["seed"]) * 255.0), 0, 255)),
                kind=1 if height > 15.5 and crown < 8.0 else 0,
                confidence=10.0,
                height_source="accepted_near",
            )
        )
    return radius, result


def reduce_runtime_page(placements: list[Placement]) -> list[Placement]:
    # One strong candidate per 15 m cell prevents clusters of near-identical stems,
    # yet preserves connected crown coverage.  The already headset-accepted airport
    # placements are pinned verbatim and may intentionally share a cell.
    pinned = [item for item in placements if item.confidence >= 9.0]
    best_by_cell: dict[tuple[int, int], Placement] = {}
    for placement in placements:
        if placement.confidence >= 9.0:
            continue
        cell = runtime_cell(placement)
        previous = best_by_cell.get(cell)
        if previous is None or placement.confidence > previous.confidence:
            best_by_cell[cell] = placement
    candidates = pinned + list(best_by_cell.values())
    candidates.sort(
        key=lambda item: (
            -item.confidence,
            hashed_u32(int(round(item.x * 10)), int(round(item.z * 10)), item.seed),
        )
    )
    return candidates[:MAX_INSTANCES_PER_RUNTIME_PAGE]


def write_database(path: Path, pages: dict[tuple[int, int], list[Placement]]) -> tuple[int, list[int]]:
    ordered_pages = []
    flattened = []
    for row in range(RUNTIME_ROWS):
        for column in range(RUNTIME_COLUMNS):
            page_items = pages.get((column, row), [])
            ordered_pages.append((len(flattened), len(page_items)))
            flattened.extend(page_items)

    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("wb") as stream:
        stream.write(
            HEADER.pack(
                MAGIC,
                VERSION,
                RUNTIME_GRID_MIN,
                RUNTIME_GRID_MIN,
                RUNTIME_PAGE_METERS,
                0.1,
                RUNTIME_COLUMNS,
                RUNTIME_ROWS,
                len(flattened),
            )
        )
        for offset, count in ordered_pages:
            stream.write(PAGE.pack(offset, count))
        for placement in flattened:
            column, row = runtime_page(placement)
            local_x = int(np.clip(round((placement.x - (RUNTIME_GRID_MIN + column * RUNTIME_PAGE_METERS)) * 10.0), 0, 10000))
            local_z = int(np.clip(round((placement.z - (RUNTIME_GRID_MIN + row * RUNTIME_PAGE_METERS)) * 10.0), 0, 10000))
            stream.write(
                INSTANCE.pack(
                    local_x,
                    local_z,
                    int(round(placement.y * 100.0)),
                    int(np.clip(round(placement.height * 100.0), 1, 65535)),
                    int(np.clip(round(placement.crown * 100.0), 1, 65535)),
                    int(round(placement.rotation / 360.0 * 65535.0)) & 0xFFFF,
                    placement.red,
                    placement.green,
                    placement.blue,
                    placement.seed,
                    placement.kind,
                )
            )
    return len(flattened), [count for _offset, count in ordered_pages]


def write_debug(
    directory: Path,
    asset_name: str,
    image: np.ndarray,
    mask: np.ndarray,
    placements: list[Placement],
    minimum_x: float,
    minimum_z: float,
    maximum_x: float,
    maximum_z: float,
) -> None:
    overlay = image.copy()
    cyan = np.zeros_like(image)
    cyan[:, :, 0] = 255
    cyan[:, :, 1] = 235
    alpha = (mask.astype(np.float32) / 255.0 * 0.42)[:, :, None]
    overlay = np.clip(overlay * (1.0 - alpha) + cyan * alpha, 0, 255).astype(np.uint8)
    scale_x = overlay.shape[1] / (maximum_x - minimum_x)
    scale_z = overlay.shape[0] / (maximum_z - minimum_z)
    for placement in placements:
        pixel_x = int(round((placement.x - minimum_x) * scale_x))
        pixel_y = int(round((maximum_z - placement.z) * scale_z))
        color = (0, 190, 255) if placement.kind == 2 else (20, 255, 20)
        cv2.circle(overlay, (pixel_x, pixel_y), 2, color, -1, cv2.LINE_AA)
    directory.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(directory / f"{asset_name}_vegetation_debug.png"), overlay)


def main() -> None:
    args = parse_args()
    clipmap = json.loads(args.clipmap_manifest.read_text(encoding="utf-8"))
    high = clipmap["tiers"]["high"]
    only = selected_pages(args.only_pages)
    terrain = TerrainSampler(args.terrain_manifest)
    buildings = BuildingIndex(args.building_database)
    lidar = (
        CanopyHeightSampler(args.lidar_manifest)
        if args.lidar_manifest is not None and args.lidar_manifest.exists()
        else None
    )
    pages = [
        page
        for page in clipmap["pages"]
        if page["tier"] == "high"
        and (only is None or (int(page["column"]), int(page["row"])) in only)
    ]
    pages.sort(key=lambda item: (int(item["row"]), int(item["column"])))

    accumulators: dict[tuple[int, int], dict[tuple[int, int], Placement]] = defaultdict(dict)
    raw_candidate_count = 0
    debug_records = []
    for ordinal, page in enumerate(pages, 1):
        source = args.imagery_root / f"{page['asset_name']}.jpg"
        image = cv2.imread(str(source), cv2.IMREAD_COLOR)
        if image is None:
            raise FileNotFoundError(source)
        gutter = int(page["gutter_pixels"])
        inner = image[gutter:-gutter, gutter:-gutter]
        analysis = cv2.resize(
            inner,
            (inner.shape[1] // ANALYSIS_DOWNSAMPLE, inner.shape[0] // ANALYSIS_DOWNSAMPLE),
            interpolation=cv2.INTER_AREA,
        )
        minimum_x = float(high["grid_min_x"]) + int(page["column"]) * float(high["tile_inner_size_meters"])
        minimum_z = float(high["grid_min_z"]) + int(page["row"]) * float(high["tile_inner_size_meters"])
        maximum_x = minimum_x + float(high["tile_inner_size_meters"])
        maximum_z = minimum_z + float(high["tile_inner_size_meters"])
        building_mask = rasterize_buildings(
            buildings, minimum_x, minimum_z, maximum_x, maximum_z, analysis.shape[:2]
        )
        mask, confidence = canopy_mask(analysis, building_mask)
        placements = page_placements(
            analysis,
            mask,
            confidence,
            minimum_x,
            minimum_z,
            maximum_x,
            maximum_z,
            int(page["column"]),
            int(page["row"]),
            terrain,
            lidar,
        )
        raw_candidate_count += len(placements)
        accumulate_runtime_candidates(accumulators, placements)
        if args.debug_directory is not None and (
            only is not None or (int(page["column"]), int(page["row"])) in {(17, 18), (18, 18), (19, 18)}
        ):
            write_debug(
                args.debug_directory,
                page["asset_name"],
                analysis,
                mask,
                placements,
                minimum_x,
                minimum_z,
                maximum_x,
                maximum_z,
            )
            debug_records.append(page["asset_name"])
        if ordinal % 32 == 0 or ordinal == len(pages):
            retained = sum(len(items) for items in accumulators.values())
            print(
                f"processed {ordinal}/{len(pages)} source pages; "
                f"candidates={raw_candidate_count}; retained={retained}",
                flush=True,
            )

    accepted_near_count = 0
    if args.accepted_near_placements is not None and only is None:
        radius, accepted = accepted_near_placements(args.accepted_near_placements)
        radius_squared = radius * radius
        for cells in accumulators.values():
            rejected = [
                cell
                for cell, item in cells.items()
                if item.x * item.x + item.z * item.z <= radius_squared
            ]
            for cell in rejected:
                del cells[cell]
        for index, item in enumerate(accepted):
            page = runtime_page(item)
            if 0 <= page[0] < RUNTIME_COLUMNS and 0 <= page[1] < RUNTIME_ROWS:
                # A synthetic key prevents the normal 15 m de-duplication from
                # altering the headset-accepted near-airport distribution.
                accumulators[page][(1_000_000_000 + index, -1)] = item
        accepted_near_count = len(accepted)

    reduced = {
        page: reduce_runtime_page(list(items.values()))
        for page, items in accumulators.items()
        if items
    }
    total, page_counts = write_database(args.output_binary, reduced)

    all_items = [item for items in reduced.values() for item in items]
    kinds = {kind: sum(item.kind == kind for item in all_items) for kind in (0, 1, 2)}
    height_sources = Counter(item.height_source for item in all_items)
    heights = np.asarray([item.height for item in all_items], dtype=np.float64)
    crowns = np.asarray([item.crown for item in all_items], dtype=np.float64)

    def distribution(values: np.ndarray) -> dict[str, float | int]:
        if len(values) == 0:
            return {"count": 0}
        return {
            "count": int(len(values)),
            "minimum": round(float(np.min(values)), 3),
            "p05": round(float(np.quantile(values, 0.05)), 3),
            "p25": round(float(np.quantile(values, 0.25)), 3),
            "median": round(float(np.quantile(values, 0.50)), 3),
            "p75": round(float(np.quantile(values, 0.75)), 3),
            "p95": round(float(np.quantile(values, 0.95)), 3),
            "maximum": round(float(np.max(values)), 3),
            "unique_centimeter_values": int(len(np.unique(np.rint(values * 100.0)))),
        }
    manifest = {
        "schema": "questflight.full-map-vegetation-clipmap.v2",
        "contract": {
            "coordinate_frame": "KBDU local AEQD; Unity +X east, +Y up, +Z north",
            "bounds_meters": [-12000.0, -12000.0, 12000.0, 12000.0],
            "runtime_page_meters": RUNTIME_PAGE_METERS,
            "columns": RUNTIME_COLUMNS,
            "rows": RUNTIME_ROWS,
            "maximum_instances_per_page": MAX_INSTANCES_PER_RUNTIME_PAGE,
            "geometry": "overlapping individual crown/trunk LODs; no opaque grove hull",
            "far_handoff": "2020 orthophoto; no individual geometry below useful projected size",
        },
        "processing": {
            "generator": "tools/full_map_vegetation/build_full_map_vegetation.py",
            "analysis_meters_per_pixel": float(high["meters_per_texel"]) * ANALYSIS_DOWNSAMPLE,
            "semantic_method": "RGB vegetation/texture segmentation plus authoritative building-footprint stem exclusion",
            "placement_method": "connected-canopy segmentation with deterministic mask-constrained blue-noise centers",
            "height_method": "orthophoto-owned local-crown evidence; exact aligned 3DEP blend where supported; deterministic context allometry elsewhere; no hash-derived height",
            "color_method": "robust median of the brighter source-canopy samples to avoid double-baked shadow",
            "sample_only": only is not None,
        },
        "sources": {
            "clipmap_manifest": {
                "path": str(args.clipmap_manifest),
                "sha256": sha256(args.clipmap_manifest),
            },
            "building_database": {
                "path": str(args.building_database),
                "sha256": sha256(args.building_database),
            },
            "terrain_manifest": {
                "path": str(args.terrain_manifest),
                "sha256": sha256(args.terrain_manifest),
            },
            "accepted_near_placements": (
                {
                    "path": str(args.accepted_near_placements),
                    "sha256": sha256(args.accepted_near_placements),
                }
                if args.accepted_near_placements is not None
                else None
            ),
            "lidar_manifest": (
                {
                    "path": str(args.lidar_manifest),
                    "sha256": sha256(args.lidar_manifest),
                    "authority": "bounded height evidence only; orthophoto retains semantic ownership",
                }
                if lidar is not None
                else None
            ),
            "imagery": {
                "publisher": clipmap["source"]["item"].get("owner", "City of Boulder"),
                "acquisition": "2020",
                "native_resolution_meters": 0.2286,
                "raw_imagery_committed": False,
            },
        },
        "statistics": {
            "source_pages_processed": len(pages),
            "raw_candidates": raw_candidate_count + accepted_near_count,
            "runtime_instances": total,
            "ordinary_trees": kinds[0],
            "tall_narrow_trees": kinds[1],
            "shrubs": kinds[2],
            "nonempty_runtime_pages": sum(count > 0 for count in page_counts),
            "maximum_runtime_page_instances": max(page_counts, default=0),
            "mean_nonempty_runtime_page_instances": (
                round(sum(page_counts) / max(1, sum(count > 0 for count in page_counts)), 2)
            ),
            "binary_bytes": args.output_binary.stat().st_size,
            "binary_sha256": sha256(args.output_binary),
            "debug_pages": debug_records,
            "accepted_near_instances_preserved": accepted_near_count,
            "height_sources": dict(sorted(height_sources.items())),
            "height_distribution_meters": distribution(heights),
            "crown_distribution_meters": distribution(crowns),
            "legacy_height_cap_23_5_count": int(np.count_nonzero(np.isclose(heights, 23.5))),
            "legacy_crown_cap_15_5_count": int(np.count_nonzero(np.isclose(crowns, 15.5))),
        },
        "limitations": [
            "RGB orthophoto segmentation is semantic placement evidence, not an individual-tree survey.",
            "Aligned 3DEP height evidence is bounded to its retained coverage; deterministic orthophoto/context allometry owns uncovered or unsupported crowns.",
            "The 3DEP work unit has no vegetation classes in the product bounds, so lidar never creates a vegetation placement.",
            "Raw orthophotos and downloaded high-poly reference assets remain external and uncommitted.",
        ],
    }
    args.output_manifest.parent.mkdir(parents=True, exist_ok=True)
    args.output_manifest.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest["statistics"], indent=2))


if __name__ == "__main__":
    main()
