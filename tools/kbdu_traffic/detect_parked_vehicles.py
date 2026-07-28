#!/usr/bin/env python3
"""Detect source-visible parked vehicles in native City of Boulder imagery.

This is an offline authoring tool.  It never ships its detector, source tiles,
or caches.  The committed output is a compact pose/evidence manifest.

The detector deliberately works from the native 2020 9-inch cache (LOD 19),
not the downsampled Unity runtime textures.  Every OSM building in the 12 km
product area receives a source-imagery search envelope.  Candidate vehicles
are rejected when their centers overlap or approach OSM roads/highways,
runways/taxiways, water, or building footprints.  Accepted vehicle orientation
is recovered from the source pixels and written in the product-local AEQD
frame.
"""

from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import io
import json
import math
import os
import threading
import time
from collections import OrderedDict, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

import cv2
import numpy as np
import pyproj
import requests
from PIL import Image

from geodeep.detection import execute_detection, extract_bsc
from geodeep.inference import create_session
from geodeep.models import get_model_file


ROOT = Path(__file__).resolve().parents[2]
SOURCE_PATH = Path(__file__).with_name("traffic_sources_v1.json")
CONTEXT_PATH = (
    ROOT
    / "QuestFlight/Assets/Production/Environment/ReconstructionV2"
    / "KBDU_ReferenceContextV2.json"
)
AERIAL_CONFIG_PATH = ROOT / "tools/aerial_surface/config.json"
AERIAL_MANIFEST_PATH = (
    ROOT
    / "QuestFlight/Assets/Production/Environment/AerialSurfaceV1/Ground"
    / "KBDU_AerialSurfaceV1_SourceManifest.json"
)

LOD = 19
TILE_PIXELS = 256
MODEL_PIXELS = 640
SOURCE_CROP_PIXELS = 280
SOURCE_CROP_MARGIN = (SOURCE_CROP_PIXELS - TILE_PIXELS) // 2
BUILDING_SEARCH_RADIUS_METERS = 100.0
BUILDING_ASSOCIATION_RADIUS_METERS = 130.0
SEMANTIC_CELL_METERS = 250.0
DETECTION_CONFIDENCE = 0.18
DEDUPE_DISTANCE_METERS = 2.4
PRODUCT_HALF_EXTENT_METERS = 6000.0
MIN_VEHICLE_LENGTH_METERS = 2.7
MAX_VEHICLE_LENGTH_METERS = 8.5
MIN_VEHICLE_WIDTH_METERS = 1.1
MAX_VEHICLE_WIDTH_METERS = 3.8
USER_AGENT = (
    "QuestFlight/1.0 parked-vehicle offline authoring "
    "(City of Boulder CC0 imagery; not for navigation)"
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def atomic_write(path: Path, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_bytes(payload)
    os.replace(temporary, path)


def clamp(value: float, low: float, high: float) -> float:
    return max(low, min(high, value))


def point_segment_distance(
    px: float,
    pz: float,
    ax: float,
    az: float,
    bx: float,
    bz: float,
) -> float:
    dx = bx - ax
    dz = bz - az
    denominator = dx * dx + dz * dz
    if denominator <= 1.0e-9:
        return math.hypot(px - ax, pz - az)
    t = clamp(((px - ax) * dx + (pz - az) * dz) / denominator, 0.0, 1.0)
    return math.hypot(px - (ax + dx * t), pz - (az + dz * t))


def polygon_signed_distance(points: np.ndarray, x: float, z: float) -> float:
    contour = points.astype(np.float32).reshape((-1, 1, 2))
    return float(cv2.pointPolygonTest(contour, (float(x), float(z)), True))


@dataclass(frozen=True)
class SemanticFeature:
    category: str
    points: np.ndarray
    width: float
    osm_id: int
    tags: dict
    centroid_x: float
    centroid_z: float
    min_x: float
    min_z: float
    max_x: float
    max_z: float


class SemanticIndex:
    def __init__(self, context: dict) -> None:
        quantization = float(context["coordinate_quantization_meters"])
        self.features: list[SemanticFeature] = []
        self.cells: dict[tuple[int, int], list[int]] = defaultdict(list)
        self.building_indices: list[int] = []
        for source in context["openstreetmap"]["features"]:
            category = str(source["category"])
            if category not in {"building", "road", "aeroway", "water"}:
                continue
            encoded = source.get("points_q", ())
            if len(encoded) < 4:
                continue
            points = np.asarray(
                [
                    (
                        encoded[index] * quantization,
                        encoded[index + 1] * quantization,
                    )
                    for index in range(0, len(encoded), 2)
                ],
                dtype=np.float64,
            )
            min_x = float(np.min(points[:, 0]))
            max_x = float(np.max(points[:, 0]))
            min_z = float(np.min(points[:, 1]))
            max_z = float(np.max(points[:, 1]))
            feature = SemanticFeature(
                category=category,
                points=points,
                width=float(source.get("render_width_meters", 0.0)),
                osm_id=int(source.get("source", {}).get("osm_id", 0)),
                tags=dict(source.get("tags", {})),
                centroid_x=float(np.mean(points[:, 0])),
                centroid_z=float(np.mean(points[:, 1])),
                min_x=min_x,
                min_z=min_z,
                max_x=max_x,
                max_z=max_z,
            )
            feature_index = len(self.features)
            self.features.append(feature)
            if category == "building":
                self.building_indices.append(feature_index)
            padding = 150.0 if category == "building" else max(10.0, feature.width)
            cell_min_x = math.floor((min_x - padding) / SEMANTIC_CELL_METERS)
            cell_max_x = math.floor((max_x + padding) / SEMANTIC_CELL_METERS)
            cell_min_z = math.floor((min_z - padding) / SEMANTIC_CELL_METERS)
            cell_max_z = math.floor((max_z + padding) / SEMANTIC_CELL_METERS)
            for cell_z in range(cell_min_z, cell_max_z + 1):
                for cell_x in range(cell_min_x, cell_max_x + 1):
                    self.cells[(cell_x, cell_z)].append(feature_index)

    def nearby(self, x: float, z: float) -> Iterable[SemanticFeature]:
        cell_x = math.floor(x / SEMANTIC_CELL_METERS)
        cell_z = math.floor(z / SEMANTIC_CELL_METERS)
        emitted: set[int] = set()
        for dz in (-1, 0, 1):
            for dx in (-1, 0, 1):
                for index in self.cells.get((cell_x + dx, cell_z + dz), ()):
                    if index in emitted:
                        continue
                    emitted.add(index)
                    yield self.features[index]

    def classify_candidate(self, x: float, z: float) -> dict:
        nearest_building: SemanticFeature | None = None
        nearest_building_distance = math.inf
        nearest_road_distance = math.inf
        for feature in self.nearby(x, z):
            if (
                x < feature.min_x - 160.0
                or x > feature.max_x + 160.0
                or z < feature.min_z - 160.0
                or z > feature.max_z + 160.0
            ):
                continue
            if feature.category in {"building", "water"}:
                signed = polygon_signed_distance(feature.points, x, z)
                if feature.category == "water" and signed >= -2.0:
                    return {"accepted": False, "reason": "WATER"}
                if feature.category == "building":
                    distance = max(0.0, -signed)
                    if signed >= -3.5:
                        return {"accepted": False, "reason": "BUILDING"}
                    centroid_distance = math.hypot(
                        x - feature.centroid_x,
                        z - feature.centroid_z,
                    )
                    if centroid_distance < nearest_building_distance:
                        nearest_building_distance = centroid_distance
                        nearest_building = feature
            elif feature.category in {"road", "aeroway"}:
                distance = min(
                    point_segment_distance(
                        x,
                        z,
                        float(start[0]),
                        float(start[1]),
                        float(end[0]),
                        float(end[1]),
                    )
                    for start, end in zip(feature.points, feature.points[1:])
                )
                clearance = (
                    max(3.0, feature.width * 0.5 + 2.75)
                    if feature.category == "road"
                    else max(7.0, feature.width * 0.5 + 5.0)
                )
                if distance <= clearance:
                    return {
                        "accepted": False,
                        "reason": (
                            "ROAD_OR_HIGHWAY"
                            if feature.category == "road"
                            else "AEROWAY"
                        ),
                    }
                if feature.category == "road":
                    nearest_road_distance = min(nearest_road_distance, distance)
        if (
            nearest_building is None
            or nearest_building_distance > BUILDING_ASSOCIATION_RADIUS_METERS
        ):
            return {"accepted": False, "reason": "NO_BUILDING_CONTEXT"}
        return {
            "accepted": True,
            "building_osm_id": nearest_building.osm_id,
            "building_distance_meters": nearest_building_distance,
            "road_clearance_meters": nearest_road_distance,
        }


class TileFrame:
    def __init__(self, config: dict, service: dict) -> None:
        self.config = config
        origin = config["origin"]
        local_crs = pyproj.CRS.from_proj4(
            f"+proj=aeqd +lat_0={float(origin['latitude_degrees']):.12f} "
            f"+lon_0={float(origin['longitude_degrees']):.12f} "
            "+datum=WGS84 +units=m +no_defs"
        )
        self.local_to_web = pyproj.Transformer.from_crs(
            local_crs,
            "EPSG:3857",
            always_xy=True,
        )
        self.web_to_local = pyproj.Transformer.from_crs(
            "EPSG:3857",
            local_crs,
            always_xy=True,
        )
        tile_info = service["tileInfo"]
        lod = next(
            item for item in tile_info["lods"] if int(item["level"]) == LOD
        )
        self.resolution = float(lod["resolution"])
        self.origin_x = float(tile_info["origin"]["x"])
        self.origin_y = float(tile_info["origin"]["y"])
        self.span = TILE_PIXELS * self.resolution
        latitude = math.radians(float(origin["latitude_degrees"]))
        self.ground_meters_per_pixel = self.resolution * math.cos(latitude)

    def world_to_tile(self, x: float, z: float) -> tuple[int, int]:
        web_x, web_y = self.local_to_web.transform(x, z)
        column = math.floor((web_x - self.origin_x) / self.span)
        row = math.floor((self.origin_y - web_y) / self.span)
        return int(row), int(column)

    def tile_pixel_to_world(
        self,
        row: int,
        column: int,
        pixel_x: float,
        pixel_y: float,
    ) -> tuple[float, float]:
        web_x = self.origin_x + (column * TILE_PIXELS + pixel_x) * self.resolution
        web_y = self.origin_y - (row * TILE_PIXELS + pixel_y) * self.resolution
        x, z = self.web_to_local.transform(web_x, web_y)
        return float(x), float(z)


class TileCache:
    def __init__(self, root: Path, url_template: str) -> None:
        self.root = root
        self.url_template = url_template
        self.local = threading.local()
        self.memory_lock = threading.Lock()
        self.memory: OrderedDict[tuple[int, int], np.ndarray] = OrderedDict()
        self.memory_limit = 256

    def path(self, row: int, column: int) -> Path:
        return self.root / f"l{LOD:02d}" / f"r{row:08d}_c{column:08d}.jpg"

    def download(self, coordinate: tuple[int, int]) -> tuple[int, int]:
        row, column = coordinate
        path = self.path(row, column)
        if path.is_file():
            return coordinate
        session = getattr(self.local, "session", None)
        if session is None:
            session = requests.Session()
            session.headers.update({"User-Agent": USER_AGENT})
            self.local.session = session
        url = self.url_template.format(level=LOD, row=row, column=column)
        last_error: Exception | None = None
        for attempt in range(5):
            try:
                response = session.get(url, timeout=45)
                response.raise_for_status()
                payload = response.content
                with Image.open(io.BytesIO(payload)) as image:
                    image.load()
                    if image.size != (TILE_PIXELS, TILE_PIXELS):
                        raise ValueError(f"Unexpected tile dimensions: {image.size}")
                atomic_write(path, payload)
                return coordinate
            except Exception as error:  # pragma: no cover - network retry path
                last_error = error
                time.sleep(0.35 * (attempt + 1))
        raise RuntimeError(f"Unable to acquire tile {LOD}/{row}/{column}") from last_error

    def load(self, row: int, column: int) -> np.ndarray:
        key = (row, column)
        with self.memory_lock:
            image = self.memory.get(key)
            if image is not None:
                self.memory.move_to_end(key)
                return image
        image = cv2.imread(str(self.path(row, column)), cv2.IMREAD_COLOR)
        if image is None or image.shape[:2] != (TILE_PIXELS, TILE_PIXELS):
            raise ValueError(f"Unreadable source tile: {self.path(row, column)}")
        with self.memory_lock:
            self.memory[key] = image
            self.memory.move_to_end(key)
            while len(self.memory) > self.memory_limit:
                self.memory.popitem(last=False)
        return image

    def inference_crop(self, row: int, column: int) -> np.ndarray:
        mosaic = np.zeros((TILE_PIXELS * 3, TILE_PIXELS * 3, 3), np.uint8)
        for offset_y in (-1, 0, 1):
            for offset_x in (-1, 0, 1):
                image = self.load(row + offset_y, column + offset_x)
                y = (offset_y + 1) * TILE_PIXELS
                x = (offset_x + 1) * TILE_PIXELS
                mosaic[y : y + TILE_PIXELS, x : x + TILE_PIXELS] = image
        start = TILE_PIXELS - SOURCE_CROP_MARGIN
        end = start + SOURCE_CROP_PIXELS
        return mosaic[start:end, start:end]


def selected_tiles(
    semantic: SemanticIndex,
    frame: TileFrame,
) -> tuple[set[tuple[int, int]], dict[int, tuple[int, int]]]:
    tiles: set[tuple[int, int]] = set()
    building_tiles: dict[int, tuple[int, int]] = {}
    radius_tiles = max(
        1,
        math.ceil(
            BUILDING_SEARCH_RADIUS_METERS
            / (TILE_PIXELS * frame.ground_meters_per_pixel)
        ),
    )
    for index in semantic.building_indices:
        building = semantic.features[index]
        row, column = frame.world_to_tile(
            building.centroid_x,
            building.centroid_z,
        )
        building_tiles[building.osm_id] = (row, column)
        for tile_row in range(row - radius_tiles, row + radius_tiles + 1):
            for tile_column in range(
                column - radius_tiles,
                column + radius_tiles + 1,
            ):
                tiles.add((tile_row, tile_column))

    # Boulder Reservoir parking is a deliberately reviewed isolated developed
    # context; source-visible cars there must not depend on a single building
    # centroid falling close enough to the parking envelope.
    reservoir_min = frame.world_to_tile(-650.0, 2700.0)
    reservoir_max = frame.world_to_tile(1100.0, 3950.0)
    for row in range(
        min(reservoir_min[0], reservoir_max[0]) - 1,
        max(reservoir_min[0], reservoir_max[0]) + 2,
    ):
        for column in range(
            min(reservoir_min[1], reservoir_max[1]) - 1,
            max(reservoir_min[1], reservoir_max[1]) + 2,
        ):
            tiles.add((row, column))
    return tiles, building_tiles


def halo(tiles: set[tuple[int, int]]) -> set[tuple[int, int]]:
    output = set(tiles)
    for row, column in list(tiles):
        for offset_y in (-1, 0, 1):
            for offset_x in (-1, 0, 1):
                output.add((row + offset_y, column + offset_x))
    return output


def car_axis_from_source(
    crop: np.ndarray,
    center_x: float,
    center_y: float,
    bbox: tuple[float, float, float, float],
    meters_per_pixel: float,
) -> tuple[float, float, float, float]:
    x1, y1, x2, y2 = bbox
    radius = int(
        clamp(
            max(x2 - x1, y2 - y1) * 0.75,
            12.0,
            28.0,
        )
    )
    center_column = int(round(center_x))
    center_row = int(round(center_y))
    left = max(0, center_column - radius)
    right = min(crop.shape[1], center_column + radius + 1)
    top = max(0, center_row - radius)
    bottom = min(crop.shape[0], center_row + radius + 1)
    patch = crop[top:bottom, left:right]
    if patch.size == 0:
        return center_x - 8.0, center_y, center_x + 8.0, center_y

    lab = cv2.cvtColor(patch, cv2.COLOR_BGR2LAB).astype(np.float32)
    edge = max(2, min(5, min(patch.shape[:2]) // 5))
    background_pixels = np.concatenate(
        (
            lab[:edge].reshape((-1, 3)),
            lab[-edge:].reshape((-1, 3)),
            lab[:, :edge].reshape((-1, 3)),
            lab[:, -edge:].reshape((-1, 3)),
        ),
        axis=0,
    )
    background = np.median(background_pixels, axis=0)
    distance = np.linalg.norm(lab - background, axis=2)
    local_center = np.asarray(
        [center_column - left, center_row - top],
        dtype=np.float32,
    )
    best: tuple[float, np.ndarray] | None = None
    for threshold in (11.0, 15.0, 20.0, 27.0, 35.0):
        mask = np.where(distance >= threshold, 255, 0).astype(np.uint8)
        mask = cv2.morphologyEx(
            mask,
            cv2.MORPH_CLOSE,
            np.ones((3, 3), np.uint8),
        )
        contours, _ = cv2.findContours(
            mask,
            cv2.RETR_EXTERNAL,
            cv2.CHAIN_APPROX_SIMPLE,
        )
        for contour in contours:
            (cx, cy), (side_a, side_b), _ = cv2.minAreaRect(contour)
            length = max(side_a, side_b) * meters_per_pixel
            width = min(side_a, side_b) * meters_per_pixel
            if not (
                MIN_VEHICLE_LENGTH_METERS <= length <= MAX_VEHICLE_LENGTH_METERS
                and MIN_VEHICLE_WIDTH_METERS <= width <= MAX_VEHICLE_WIDTH_METERS
            ):
                continue
            center_error = math.hypot(
                cx - float(local_center[0]),
                cy - float(local_center[1]),
            )
            if center_error > 7.0:
                continue
            rectangle = cv2.minAreaRect(contour)
            score = (
                center_error
                + abs(length - 4.7) * 0.4
                + abs(width - 1.9) * 0.5
            )
            if best is None or score < best[0]:
                best = (score, cv2.boxPoints(rectangle))
    if best is None:
        # The detector box remains valid evidence for center/scale even when
        # source contrast cannot reliably distinguish the hood from shadow.
        dx = max(6.0, (x2 - x1) * 0.35)
        return center_x - dx, center_y, center_x + dx, center_y
    box = best[1]
    edges = [
        (box[index], box[(index + 1) % 4])
        for index in range(4)
    ]
    start, end = max(
        edges,
        key=lambda edge_points: float(
            np.linalg.norm(edge_points[1] - edge_points[0])
        ),
    )
    axis = end - start
    axis_length = float(np.linalg.norm(axis))
    if axis_length < 1.0:
        return center_x - 8.0, center_y, center_x + 8.0, center_y
    axis /= axis_length
    half = max(6.0, axis_length * 0.5)
    return (
        center_x - float(axis[0]) * half,
        center_y - float(axis[1]) * half,
        center_x + float(axis[0]) * half,
        center_y + float(axis[1]) * half,
    )


def observed_paint_index(crop: np.ndarray, x: float, y: float) -> int:
    cx = int(round(x))
    cy = int(round(y))
    patch = crop[
        max(0, cy - 3) : min(crop.shape[0], cy + 4),
        max(0, cx - 3) : min(crop.shape[1], cx + 4),
    ]
    if patch.size == 0:
        return 3
    bgr = np.median(patch.reshape((-1, 3)), axis=0).astype(np.uint8)
    hsv = cv2.cvtColor(bgr.reshape((1, 1, 3)), cv2.COLOR_BGR2HSV)[0, 0]
    hue, saturation, value = map(int, hsv)
    if value >= 180 and saturation < 45:
        return 0  # white
    if value <= 55:
        return 1  # black
    if saturation < 35:
        return 3 if value >= 125 else 2  # silver / gray
    if hue < 10 or hue >= 170:
        return 5  # red
    if 90 <= hue < 140:
        return 4  # blue
    if 35 <= hue < 90:
        return 6  # green
    return 7  # brown / warm neutral


def infer_tile(
    coordinate: tuple[int, int],
    cache: TileCache,
    session,
    model_config: dict,
    frame: TileFrame,
    semantic: SemanticIndex,
) -> tuple[list[dict], dict[str, int]]:
    row, column = coordinate
    crop = cache.inference_crop(row, column)
    resized = cv2.resize(
        crop,
        (MODEL_PIXELS, MODEL_PIXELS),
        interpolation=cv2.INTER_CUBIC,
    )
    raw = execute_detection(resized, session, model_config)
    boxes, scores, _ = extract_bsc(raw, model_config)
    scale = MODEL_PIXELS / SOURCE_CROP_PIXELS
    accepted: list[dict] = []
    rejected: dict[str, int] = defaultdict(int)
    for box, score in zip(boxes, scores):
        x1, y1, x2, y2 = [float(value) / scale for value in box]
        center_x_crop = 0.5 * (x1 + x2)
        center_y_crop = 0.5 * (y1 + y2)
        center_x_tile = center_x_crop - SOURCE_CROP_MARGIN
        center_y_tile = center_y_crop - SOURCE_CROP_MARGIN
        if not (
            0.0 <= center_x_tile < TILE_PIXELS
            and 0.0 <= center_y_tile < TILE_PIXELS
        ):
            continue
        x, z = frame.tile_pixel_to_world(
            row,
            column,
            center_x_tile,
            center_y_tile,
        )
        if not (
            -PRODUCT_HALF_EXTENT_METERS <= x <= PRODUCT_HALF_EXTENT_METERS
            and -PRODUCT_HALF_EXTENT_METERS <= z <= PRODUCT_HALF_EXTENT_METERS
        ):
            rejected["OUTSIDE_PRODUCT_IMAGERY"] += 1
            continue
        semantic_result = semantic.classify_candidate(x, z)
        if not semantic_result["accepted"]:
            rejected[str(semantic_result["reason"])] += 1
            continue
        axis = car_axis_from_source(
            crop,
            center_x_crop,
            center_y_crop,
            (x1, y1, x2, y2),
            frame.ground_meters_per_pixel,
        )
        axis_start_x, axis_start_z = frame.tile_pixel_to_world(
            row,
            column,
            axis[0] - SOURCE_CROP_MARGIN,
            axis[1] - SOURCE_CROP_MARGIN,
        )
        axis_end_x, axis_end_z = frame.tile_pixel_to_world(
            row,
            column,
            axis[2] - SOURCE_CROP_MARGIN,
            axis[3] - SOURCE_CROP_MARGIN,
        )
        heading = (
            math.degrees(
                math.atan2(
                    axis_end_x - axis_start_x,
                    axis_end_z - axis_start_z,
                )
            )
            % 180.0
        )
        if (
            int(semantic_result["building_osm_id"])
            + row
            + column
        ) % 2:
            heading = (heading + 180.0) % 360.0
        accepted.append(
            {
                "source_tile": [LOD, row, column],
                "source_center_pixel": [
                    round(center_x_tile, 3),
                    round(center_y_tile, 3),
                ],
                "source_axis_pixels": [
                    round(axis[0] - SOURCE_CROP_MARGIN, 3),
                    round(axis[1] - SOURCE_CROP_MARGIN, 3),
                    round(axis[2] - SOURCE_CROP_MARGIN, 3),
                    round(axis[3] - SOURCE_CROP_MARGIN, 3),
                ],
                "world_position_meters": [round(x, 4), round(z, 4)],
                "yaw_degrees": round(heading, 3),
                "confidence": round(float(score), 4),
                "building_osm_id": int(semantic_result["building_osm_id"]),
                "building_distance_meters": round(
                    float(semantic_result["building_distance_meters"]),
                    2,
                ),
                "road_clearance_meters": round(
                    min(
                        9999.0,
                        float(semantic_result["road_clearance_meters"]),
                    ),
                    2,
                ),
                "observed_paint_index": observed_paint_index(
                    crop,
                    center_x_crop,
                    center_y_crop,
                ),
            }
        )
    return accepted, dict(rejected)


def deduplicate(records: list[dict]) -> list[dict]:
    records.sort(
        key=lambda item: (
            -float(item["confidence"]),
            item["world_position_meters"][1],
            item["world_position_meters"][0],
        )
    )
    cell_size = DEDUPE_DISTANCE_METERS
    cells: dict[tuple[int, int], list[dict]] = defaultdict(list)
    accepted: list[dict] = []
    for record in records:
        x, z = map(float, record["world_position_meters"])
        cell_x = math.floor(x / cell_size)
        cell_z = math.floor(z / cell_size)
        duplicate = False
        for dz in (-1, 0, 1):
            for dx in (-1, 0, 1):
                for other in cells.get((cell_x + dx, cell_z + dz), ()):
                    other_x, other_z = map(
                        float,
                        other["world_position_meters"],
                    )
                    if math.hypot(x - other_x, z - other_z) < cell_size:
                        duplicate = True
                        break
                if duplicate:
                    break
            if duplicate:
                break
        if duplicate:
            continue
        cells[(cell_x, cell_z)].append(record)
        accepted.append(record)
    return sorted(
        accepted,
        key=lambda item: (
            int(item["building_osm_id"]),
            item["world_position_meters"][1],
            item["world_position_meters"][0],
        ),
    )


def assign_ids(records: list[dict]) -> list[dict]:
    by_building: dict[int, int] = defaultdict(int)
    output: list[dict] = []
    for record in records:
        building = int(record["building_osm_id"])
        by_building[building] += 1
        item = dict(record)
        item.update(
            {
                "id": f"city2020-b{building}-v{by_building[building]:03d}",
                "lot": f"osm-building-{building}",
                "source_image": "city2020_l19",
                "detection_method": (
                    "NATIVE_9IN_GEODEEP_SEMANTIC_FILTER_ORIENTED_SOURCE_FIT"
                ),
            }
        )
        output.append(item)
    return output


def write_overview(
    records: list[dict],
    semantic: SemanticIndex,
    destination: Path,
) -> None:
    size = 4096
    canvas = np.full((size, size, 3), 236, np.uint8)
    def pixel(x: float, z: float) -> tuple[int, int]:
        return (
            int(round((x + 6000.0) / 12000.0 * (size - 1))),
            int(round((6000.0 - z) / 12000.0 * (size - 1))),
        )
    for index in semantic.building_indices:
        feature = semantic.features[index]
        if not (
            -6000 <= feature.centroid_x <= 6000
            and -6000 <= feature.centroid_z <= 6000
        ):
            continue
        cv2.circle(
            canvas,
            pixel(feature.centroid_x, feature.centroid_z),
            1,
            (170, 170, 170),
            -1,
        )
    for record in records:
        x, z = record["world_position_meters"]
        cv2.circle(canvas, pixel(x, z), 2, (0, 0, 220), -1)
    cv2.imwrite(str(destination), canvas)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--artifact-dir", type=Path, required=True)
    parser.add_argument("--cache-dir", type=Path, required=True)
    parser.add_argument("--write-source", action="store_true")
    parser.add_argument("--download-workers", type=int, default=24)
    parser.add_argument("--inference-workers", type=int, default=4)
    parser.add_argument(
        "--limit-tiles",
        type=int,
        default=0,
        help="Diagnostic-only deterministic tile limit; zero scans all.",
    )
    args = parser.parse_args()
    args.artifact_dir.mkdir(parents=True, exist_ok=True)
    args.cache_dir.mkdir(parents=True, exist_ok=True)

    config = json.loads(AERIAL_CONFIG_PATH.read_text(encoding="utf-8"))
    aerial_manifest = json.loads(
        AERIAL_MANIFEST_PATH.read_text(encoding="utf-8")
    )
    context = json.loads(CONTEXT_PATH.read_text(encoding="utf-8"))
    response = requests.get(
        config["source"]["service_metadata_url"],
        headers={"User-Agent": USER_AGENT},
        timeout=45,
    )
    response.raise_for_status()
    service = response.json()
    frame = TileFrame(config, service)
    semantic = SemanticIndex(context)
    scan_tiles, building_tiles = selected_tiles(semantic, frame)
    ordered_scan_tiles = sorted(scan_tiles)
    if args.limit_tiles > 0:
        ordered_scan_tiles = ordered_scan_tiles[: args.limit_tiles]
        scan_tiles = set(ordered_scan_tiles)
    required_tiles = halo(scan_tiles)
    cache = TileCache(args.cache_dir, config["source"]["tile_url_template"])

    print(
        f"Native parked scan plan: buildings={len(building_tiles)}, "
        f"scan_tiles={len(scan_tiles)}, cache_tiles={len(required_tiles)}, "
        f"ground_resolution={frame.ground_meters_per_pixel:.6f} m/px",
        flush=True,
    )
    download_started = time.time()
    with concurrent.futures.ThreadPoolExecutor(
        max_workers=max(1, args.download_workers)
    ) as executor:
        for index, _ in enumerate(
            executor.map(cache.download, sorted(required_tiles)),
            start=1,
        ):
            if index % 1000 == 0:
                print(
                    f"Downloaded/verified {index}/{len(required_tiles)} tiles",
                    flush=True,
                )
    download_seconds = time.time() - download_started

    model_path = Path(get_model_file("cars"))
    session, model_config = create_session(str(model_path), max_threads=4)
    model_config["det_conf"] = DETECTION_CONFIDENCE
    raw_records: list[dict] = []
    rejection_counts: dict[str, int] = defaultdict(int)
    inference_started = time.time()
    worker = lambda coordinate: infer_tile(  # noqa: E731
        coordinate,
        cache,
        session,
        model_config,
        frame,
        semantic,
    )
    with concurrent.futures.ThreadPoolExecutor(
        max_workers=max(1, args.inference_workers)
    ) as executor:
        for index, (tile_records, tile_rejections) in enumerate(
            executor.map(worker, ordered_scan_tiles),
            start=1,
        ):
            raw_records.extend(tile_records)
            for reason, count in tile_rejections.items():
                rejection_counts[reason] += count
            if index % 1000 == 0:
                print(
                    f"Inferred {index}/{len(scan_tiles)} tiles; "
                    f"raw accepted={len(raw_records)}",
                    flush=True,
                )
    inference_seconds = time.time() - inference_started

    records = assign_ids(deduplicate(raw_records))
    counts_by_building: dict[int, int] = defaultdict(int)
    for record in records:
        counts_by_building[int(record["building_osm_id"])] += 1
    represented_buildings = set(counts_by_building)
    buildings_in_product = {
        osm_id
        for osm_id, coordinate in building_tiles.items()
        if coordinate in scan_tiles or args.limit_tiles == 0
    }
    coverage = {
        "buildings_in_12km_product_area": len(buildings_in_product),
        "buildings_with_source_visible_vehicle": len(
            represented_buildings & buildings_in_product
        ),
        "buildings_without_source_visible_vehicle": len(
            buildings_in_product - represented_buildings
        ),
        "source_visible_parked_vehicles": len(records),
        "vehicles_per_represented_building": {
            "minimum": min(counts_by_building.values(), default=0),
            "maximum": max(counts_by_building.values(), default=0),
            "mean": round(
                sum(counts_by_building.values())
                / max(1, len(counts_by_building)),
                3,
            ),
        },
    }
    report = {
        "schema": "questflight.parked-vehicle-native-scan-v2",
        "status": "COMPLETE" if args.limit_tiles == 0 else "DIAGNOSTIC_LIMITED",
        "source": {
            "provider": aerial_manifest["source"]["provider"],
            "producer": aerial_manifest["source"]["producer"],
            "acquisition_period": aerial_manifest["source"][
                "acquisition_period"
            ],
            "source_resolution_meters": aerial_manifest["source"][
                "source_resolution_meters"
            ],
            "license_spdx": aerial_manifest["source"]["license_spdx"],
            "service_url": aerial_manifest["source"]["service_url"],
            "level": LOD,
            "ground_resolution_meters_per_pixel": round(
                frame.ground_meters_per_pixel,
                9,
            ),
        },
        "detector": {
            "model": model_path.name,
            "model_sha256": sha256(model_path),
            "model_runtime_only_external": True,
            "authoring_tool": "GeoDeep 0.9.13",
            "authoring_tool_url": "https://github.com/uav4geo/GeoDeep",
            "authoring_tool_license": "AGPL-3.0-only",
            "confidence_threshold": DETECTION_CONFIDENCE,
            "orientation": (
                "source-pixel local color-distance component fit; "
                "detector bbox fallback when unresolved"
            ),
        },
        "scope": {
            "policy": (
                "native source scan within 100 m of every OSM building across "
                "the 12 km product area, plus the complete Boulder Reservoir "
                "developed parking envelope"
            ),
            "scan_tiles": len(scan_tiles),
            "cache_tiles": len(required_tiles),
            "raw_cache_policy": "external-only; never committed",
        },
        "semantic_rejections": dict(sorted(rejection_counts.items())),
        "coverage": coverage,
        "timing_seconds": {
            "download_or_verify": round(download_seconds, 3),
            "inference": round(inference_seconds, 3),
        },
        "records": records,
    }
    (args.artifact_dir / "parked_vehicle_native_scan_report.json").write_text(
        json.dumps(report, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    (args.artifact_dir / "parked_vehicle_building_coverage.json").write_text(
        json.dumps(
            {
                "schema": "questflight.parked-vehicle-building-coverage-v1",
                "coverage": coverage,
                "counts_by_building_osm_id": {
                    str(key): value
                    for key, value in sorted(counts_by_building.items())
                },
                "unrepresented_building_osm_ids": sorted(
                    buildings_in_product - represented_buildings
                ),
                "interpretation": (
                    "An unrepresented building has no detector-supported "
                    "source-visible parked vehicle; no synthetic car is added."
                ),
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    write_overview(
        records,
        semantic,
        args.artifact_dir / "parked_vehicle_map_overview.png",
    )

    if args.write_source:
        source = json.loads(SOURCE_PATH.read_text(encoding="utf-8"))
        source["parked_imagery_sources"] = {
            "city2020_l19": {
                "service_url": aerial_manifest["source"]["service_url"],
                "tile_url_template": aerial_manifest["source"][
                    "tile_url_template"
                ],
                "level": LOD,
                "source_resolution_meters": aerial_manifest["source"][
                    "source_resolution_meters"
                ],
                "ground_resolution_meters_per_pixel": round(
                    frame.ground_meters_per_pixel,
                    9,
                ),
                "license_spdx": aerial_manifest["source"]["license_spdx"],
                "raw_cache_policy": "external-only; never committed",
            }
        }
        source["parked_detection"] = {
            "schema": "questflight.parked-vehicle-native-scan-v2",
            "scope": report["scope"]["policy"],
            "source_visible_vehicle_count": len(records),
            "buildings_scanned": len(buildings_in_product),
            "buildings_with_source_visible_vehicle": len(
                represented_buildings & buildings_in_product
            ),
            "unresolved_policy": "NO_SYNTHETIC_CAR_WITHOUT_SOURCE_EVIDENCE",
            "tool": "tools/kbdu_traffic/detect_parked_vehicles.py",
            "external_authoring_dependency": {
                "name": "GeoDeep",
                "version": "0.9.13",
                "url": "https://github.com/uav4geo/GeoDeep",
                "license": "AGPL-3.0-only",
                "runtime_dependency": False,
                "committed_model": False,
            },
        }
        source["parked_vehicles"] = records
        SOURCE_PATH.write_text(
            json.dumps(source, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8",
        )

    print(json.dumps(coverage, indent=2), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
