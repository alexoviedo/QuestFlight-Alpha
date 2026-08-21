#!/usr/bin/env python3
"""Refine parked-vehicle poses against native City of Boulder 2020 imagery.

The V1 scanner recovered useful centers but its unresolved orientation fallback
was a horizontal line.  This offline-only pass replaces that fallback with an
oriented aerial-vehicle detector, propagates only strong local row consensus,
checks the complete vehicle footprint against product semantics, and resolves
collisions with rotated rectangles.  Source tiles, detector weights, and review
captures remain external to Git; the compact pose/provenance manifest is the
only product input that changes.
"""

from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import json
import math
import os
import threading
from collections import Counter, OrderedDict, defaultdict
from pathlib import Path

import cv2
import numpy as np
import requests
from PIL import Image
from ultralytics import YOLO

from detect_parked_vehicles import (
    AERIAL_CONFIG_PATH,
    AERIAL_MANIFEST_PATH,
    CONTEXT_PATH,
    LOD,
    PRODUCT_HALF_EXTENT_METERS,
    SemanticIndex,
    TileFrame,
    point_segment_distance,
    polygon_signed_distance,
)


ROOT = Path(__file__).resolve().parents[2]
SOURCE_PATH = Path(__file__).with_name("traffic_sources_v1.json")
SEMANTIC_MASK_PATH = (
    ROOT
    / "QuestFlight/Assets/Production/Environment/FullMapVegetationV3"
    / "KBDU_VegetationSemanticExclusionV1.png"
)
TILE_PIXELS = 256
CROP_PIXELS = 128
CROP_HALF = CROP_PIXELS // 2
MODEL_INPUT_PIXELS = 512
MODEL_CONFIDENCE = 0.01
MAXIMUM_CENTER_CORRECTION_PIXELS = 10.0
MINIMUM_LENGTH_METERS = 2.7
MAXIMUM_LENGTH_METERS = 8.5
MINIMUM_WIDTH_METERS = 0.9
MAXIMUM_WIDTH_METERS = 3.8
CONSERVATIVE_LENGTH_METERS = 5.35
CONSERVATIVE_WIDTH_METERS = 2.0
FOOTPRINT_MARGIN_METERS = 0.15
ROW_NEIGHBOR_METERS = 18.0
ROW_ANGLE_TOLERANCE_DEGREES = 12.0
MODEL_URL = (
    "https://github.com/ultralytics/assets/releases/download/v8.3.0/"
    "yolo11n-obb.pt"
)
USER_AGENT = "QuestFlight/2.0 parked-vehicle OBB offline refinement"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def angular_distance(a: float, b: float, period: float = 360.0) -> float:
    return abs((a - b + period * 0.5) % period - period * 0.5)


class NativeTileCache:
    def __init__(self, root: Path, url_template: str) -> None:
        self.root = root
        self.url_template = url_template
        self.local = threading.local()
        self.decoded: OrderedDict[tuple[int, int], np.ndarray] = OrderedDict()
        self.decoded_limit = 384

    def path(self, row: int, column: int) -> Path:
        return self.root / f"l{LOD:02d}" / f"r{row:08d}_c{column:08d}.jpg"

    def _session(self) -> requests.Session:
        session = getattr(self.local, "session", None)
        if session is None:
            session = requests.Session()
            session.headers.update({"User-Agent": USER_AGENT})
            self.local.session = session
        return session

    def download(self, coordinate: tuple[int, int]) -> None:
        row, column = coordinate
        path = self.path(row, column)
        if path.exists() and path.stat().st_size > 256:
            return
        url = self.url_template.format(level=LOD, row=row, column=column)
        last_error: Exception | None = None
        for _ in range(4):
            try:
                response = self._session().get(url, timeout=45)
                response.raise_for_status()
                payload = response.content
                image = cv2.imdecode(np.frombuffer(payload, np.uint8), cv2.IMREAD_COLOR)
                if image is None or image.shape[:2] != (TILE_PIXELS, TILE_PIXELS):
                    raise ValueError(f"Invalid source tile {LOD}/{row}/{column}")
                path.parent.mkdir(parents=True, exist_ok=True)
                temporary = path.with_suffix(".tmp")
                temporary.write_bytes(payload)
                os.replace(temporary, path)
                return
            except Exception as error:  # pragma: no cover - network diagnostics
                last_error = error
        raise RuntimeError(f"Unable to acquire {LOD}/{row}/{column}") from last_error

    def image(self, row: int, column: int) -> np.ndarray:
        key = (row, column)
        image = self.decoded.pop(key, None)
        if image is None:
            image = cv2.imread(str(self.path(row, column)), cv2.IMREAD_COLOR)
            if image is None:
                raise ValueError(f"Unreadable cached tile {row}/{column}")
        self.decoded[key] = image
        if len(self.decoded) > self.decoded_limit:
            self.decoded.popitem(last=False)
        return image

    def crop(self, record: dict) -> np.ndarray:
        _, row, column = map(int, record["source_tile"])
        center_x, center_y = map(float, record["source_center_pixel"])
        global_left = math.floor(column * TILE_PIXELS + center_x - CROP_HALF)
        global_top = math.floor(row * TILE_PIXELS + center_y - CROP_HALF)
        output = np.empty((CROP_PIXELS, CROP_PIXELS, 3), np.uint8)
        first_column = math.floor(global_left / TILE_PIXELS)
        last_column = math.floor((global_left + CROP_PIXELS - 1) / TILE_PIXELS)
        first_row = math.floor(global_top / TILE_PIXELS)
        last_row = math.floor((global_top + CROP_PIXELS - 1) / TILE_PIXELS)
        for source_row in range(first_row, last_row + 1):
            for source_column in range(first_column, last_column + 1):
                tile = self.image(source_row, source_column)
                source_left = max(0, global_left - source_column * TILE_PIXELS)
                source_top = max(0, global_top - source_row * TILE_PIXELS)
                source_right = min(
                    TILE_PIXELS,
                    global_left + CROP_PIXELS - source_column * TILE_PIXELS,
                )
                source_bottom = min(
                    TILE_PIXELS,
                    global_top + CROP_PIXELS - source_row * TILE_PIXELS,
                )
                target_left = source_column * TILE_PIXELS + source_left - global_left
                target_top = source_row * TILE_PIXELS + source_top - global_top
                width = source_right - source_left
                height = source_bottom - source_top
                output[
                    target_top : target_top + height,
                    target_left : target_left + width,
                ] = tile[source_top:source_bottom, source_left:source_right]
        return output


class ProductSemanticMask:
    def __init__(self, path: Path) -> None:
        packed = np.asarray(Image.open(path).convert("RGB"), dtype=np.uint8)
        self.water = packed[:, :, 0] >= 128
        self.sports = packed[:, :, 1] >= 128

    def excluded(self, x: float, z: float) -> str | None:
        size = self.water.shape[0]
        column = min(size - 1, max(0, int((x + 12000.0) / 24000.0 * size)))
        row = min(size - 1, max(0, int((12000.0 - z) / 24000.0 * size)))
        if self.water[row, column]:
            return "SEMANTIC_WATER"
        if self.sports[row, column]:
            return "ATHLETIC_SURFACE"
        return None


def required_tiles(records: list[dict]) -> set[tuple[int, int]]:
    result: set[tuple[int, int]] = set()
    for record in records:
        _, row, column = map(int, record["source_tile"])
        center_x, center_y = map(float, record["source_center_pixel"])
        rows = [row]
        columns = [column]
        if center_y < CROP_HALF:
            rows.append(row - 1)
        if center_y > TILE_PIXELS - CROP_HALF:
            rows.append(row + 1)
        if center_x < CROP_HALF:
            columns.append(column - 1)
        if center_x > TILE_PIXELS - CROP_HALF:
            columns.append(column + 1)
        result.update((source_row, source_column) for source_row in rows for source_column in columns)
    return result


def normalize_source_pixel(
    row: int, column: int, pixel_x: float, pixel_y: float
) -> tuple[int, int, float, float]:
    global_x = column * TILE_PIXELS + pixel_x
    global_y = row * TILE_PIXELS + pixel_y
    normalized_column = math.floor(global_x / TILE_PIXELS)
    normalized_row = math.floor(global_y / TILE_PIXELS)
    return (
        normalized_row,
        normalized_column,
        global_x - normalized_column * TILE_PIXELS,
        global_y - normalized_row * TILE_PIXELS,
    )


def heading_from_source_axis(
    frame: TileFrame,
    row: int,
    column: int,
    center_x: float,
    center_y: float,
    axis_x: float,
    axis_y: float,
) -> float:
    start_x, start_z = frame.tile_pixel_to_world(
        row, column, center_x - axis_x, center_y - axis_y
    )
    end_x, end_z = frame.tile_pixel_to_world(
        row, column, center_x + axis_x, center_y + axis_y
    )
    return math.degrees(math.atan2(end_x - start_x, end_z - start_z)) % 180.0


def best_vehicle_box(result) -> tuple[np.ndarray, float] | None:
    if result.obb is None:
        return None
    best: tuple[float, np.ndarray, float] | None = None
    for box, class_index, confidence in zip(
        result.obb.xywhr.cpu().numpy(),
        result.obb.cls.cpu().numpy(),
        result.obb.conf.cpu().numpy(),
    ):
        if int(class_index) not in (9, 10):
            continue
        center_error = math.hypot(
            float(box[0]) - CROP_HALF, float(box[1]) - CROP_HALF
        )
        length = max(float(box[2]), float(box[3]))
        width = min(float(box[2]), float(box[3]))
        meters_per_pixel = 0.228595274
        if center_error > MAXIMUM_CENTER_CORRECTION_PIXELS:
            continue
        if not (
            MINIMUM_LENGTH_METERS <= length * meters_per_pixel <= MAXIMUM_LENGTH_METERS
            and MINIMUM_WIDTH_METERS <= width * meters_per_pixel <= MAXIMUM_WIDTH_METERS
        ):
            continue
        score = float(confidence) - center_error * 0.006
        if best is None or score > best[0]:
            best = (score, box, float(confidence))
    return None if best is None else (best[1], best[2])


def refine_direct(
    record: dict, result, frame: TileFrame
) -> dict | None:
    match = best_vehicle_box(result)
    if match is None:
        return None
    box, orientation_confidence = match
    _, source_row, source_column = map(int, record["source_tile"])
    old_center_x, old_center_y = map(float, record["source_center_pixel"])
    center_x = old_center_x + float(box[0]) - CROP_HALF
    center_y = old_center_y + float(box[1]) - CROP_HALF
    source_row, source_column, center_x, center_y = normalize_source_pixel(
        source_row, source_column, center_x, center_y
    )
    width = float(box[2])
    height = float(box[3])
    angle = float(box[4])
    if width >= height:
        axis_x, axis_y = math.cos(angle), math.sin(angle)
        length_pixels = width
    else:
        axis_x, axis_y = -math.sin(angle), math.cos(angle)
        length_pixels = height
    heading = heading_from_source_axis(
        frame, source_row, source_column, center_x, center_y, axis_x, axis_y
    )
    old_heading = float(record["yaw_degrees"]) % 360.0
    if angular_distance(heading, old_heading) > angular_distance(heading + 180.0, old_heading):
        heading += 180.0
    x, z = frame.tile_pixel_to_world(
        source_row, source_column, center_x, center_y
    )
    half_axis_x = axis_x * length_pixels * 0.5
    half_axis_y = axis_y * length_pixels * 0.5
    refined = dict(record)
    refined.update(
        {
            "source_tile": [LOD, source_row, source_column],
            "source_center_pixel": [round(center_x, 3), round(center_y, 3)],
            "source_axis_pixels": [
                round(center_x - half_axis_x, 3),
                round(center_y - half_axis_y, 3),
                round(center_x + half_axis_x, 3),
                round(center_y + half_axis_y, 3),
            ],
            "world_position_meters": [round(x, 4), round(z, 4)],
            "yaw_degrees": round(heading % 360.0, 3),
            "orientation_source": "YOLO11N_OBB_DIRECT",
            "orientation_confidence": round(orientation_confidence, 5),
            "detection_method": "NATIVE_9IN_YOLO11_OBB_SEMANTIC_FOOTPRINT_V2",
        }
    )
    return refined


def row_consensus(
    record: dict, direct_by_lot: dict[str, list[dict]]
) -> dict | None:
    x, z = map(float, record["world_position_meters"])
    nearby = []
    for other in direct_by_lot.get(str(record["lot"]), ()):
        other_x, other_z = map(float, other["world_position_meters"])
        if math.hypot(x - other_x, z - other_z) <= ROW_NEIGHBOR_METERS:
            nearby.append(other)
    if len(nearby) < 2:
        return None
    best_cluster: list[dict] = []
    for pivot in nearby:
        pivot_heading = float(pivot["yaw_degrees"]) % 180.0
        cluster = [
            other
            for other in nearby
            if angular_distance(
                float(other["yaw_degrees"]) % 180.0,
                pivot_heading,
                180.0,
            )
            <= ROW_ANGLE_TOLERANCE_DEGREES
        ]
        if len(cluster) > len(best_cluster):
            best_cluster = cluster
    if len(best_cluster) < 2 or len(best_cluster) < math.ceil(len(nearby) * 0.6):
        return None
    vector_x = sum(
        math.cos(math.radians(float(item["yaw_degrees"]) * 2.0))
        * max(0.01, float(item["orientation_confidence"]))
        for item in best_cluster
    )
    vector_y = sum(
        math.sin(math.radians(float(item["yaw_degrees"]) * 2.0))
        * max(0.01, float(item["orientation_confidence"]))
        for item in best_cluster
    )
    concentration = math.hypot(vector_x, vector_y) / sum(
        max(0.01, float(item["orientation_confidence"]))
        for item in best_cluster
    )
    if concentration < 0.92:
        return None
    heading = math.degrees(math.atan2(vector_y, vector_x)) * 0.5 % 180.0
    old_heading = float(record["yaw_degrees"]) % 360.0
    if angular_distance(heading, old_heading) > angular_distance(heading + 180.0, old_heading):
        heading += 180.0
    refined = dict(record)
    refined.update(
        {
            "yaw_degrees": round(heading % 360.0, 3),
            "orientation_source": "LOCAL_PARKING_ROW_CONSENSUS",
            "orientation_confidence": round(concentration, 5),
            "detection_method": "NATIVE_9IN_YOLO11_OBB_SEMANTIC_FOOTPRINT_V2",
        }
    )
    return refined


def footprint(record: dict, margin: float = FOOTPRINT_MARGIN_METERS) -> list[tuple[float, float]]:
    x, z = map(float, record["world_position_meters"])
    angle = math.radians(float(record["yaw_degrees"]))
    forward = (math.sin(angle), math.cos(angle))
    side = (math.cos(angle), -math.sin(angle))
    half_length = CONSERVATIVE_LENGTH_METERS * 0.5 + margin
    half_width = CONSERVATIVE_WIDTH_METERS * 0.5 + margin
    return [
        (
            x + forward[0] * half_length * along + side[0] * half_width * across,
            z + forward[1] * half_length * along + side[1] * half_width * across,
        )
        for along, across in ((-1, -1), (-1, 1), (1, 1), (1, -1))
    ]


def semantic_footprint_reason(
    record: dict,
    semantic: SemanticIndex,
    product_mask: ProductSemanticMask,
) -> str | None:
    x, z = map(float, record["world_position_meters"])
    points = [(x, z), *footprint(record, 0.05)]
    for point_x, point_z in points:
        reason = product_mask.excluded(point_x, point_z)
        if reason:
            return reason
    for feature in semantic.nearby(x, z):
        if feature.category in {"building", "water"}:
            for point_x, point_z in points:
                signed = polygon_signed_distance(feature.points, point_x, point_z)
                if signed >= -0.15:
                    return "BUILDING_FOOTPRINT" if feature.category == "building" else "WATER_FOOTPRINT"
        elif feature.category in {"road", "aeroway"}:
            clearance = (
                max(2.5, feature.width * 0.5 + 0.25)
                if feature.category == "road"
                else max(6.0, feature.width * 0.5 + 1.0)
            )
            for point_x, point_z in points:
                distance = min(
                    point_segment_distance(
                        point_x,
                        point_z,
                        float(start[0]),
                        float(start[1]),
                        float(end[0]),
                        float(end[1]),
                    )
                    for start, end in zip(feature.points, feature.points[1:])
                )
                if distance <= clearance:
                    return "ACTIVE_ROAD_FOOTPRINT" if feature.category == "road" else "AEROWAY_FOOTPRINT"
    return None


def rectangles_overlap(a: dict, b: dict) -> bool:
    def frame(record: dict):
        angle = math.radians(float(record["yaw_degrees"]))
        return (math.sin(angle), math.cos(angle)), (math.cos(angle), -math.sin(angle))

    ax, az = map(float, a["world_position_meters"])
    bx, bz = map(float, b["world_position_meters"])
    forward_a, side_a = frame(a)
    forward_b, side_b = frame(b)
    delta = (bx - ax, bz - az)
    half_length = CONSERVATIVE_LENGTH_METERS * 0.5 + FOOTPRINT_MARGIN_METERS
    half_width = CONSERVATIVE_WIDTH_METERS * 0.5 + FOOTPRINT_MARGIN_METERS
    for axis in (forward_a, side_a, forward_b, side_b):
        distance = abs(delta[0] * axis[0] + delta[1] * axis[1])
        radius_a = half_length * abs(forward_a[0] * axis[0] + forward_a[1] * axis[1]) + half_width * abs(side_a[0] * axis[0] + side_a[1] * axis[1])
        radius_b = half_length * abs(forward_b[0] * axis[0] + forward_b[1] * axis[1]) + half_width * abs(side_b[0] * axis[0] + side_b[1] * axis[1])
        if distance >= radius_a + radius_b:
            return False
    return True


def resolve_collisions(records: list[dict], rejection_counts: Counter) -> list[dict]:
    rank = {"YOLO11N_OBB_DIRECT": 2, "LOCAL_PARKING_ROW_CONSENSUS": 1}
    ordered = sorted(
        records,
        key=lambda item: (
            -rank.get(str(item.get("orientation_source")), 0),
            -float(item.get("orientation_confidence", 0.0)),
            -float(item.get("confidence", 0.0)),
            str(item["id"]),
        ),
    )
    cell_size = 7.0
    cells: dict[tuple[int, int], list[dict]] = defaultdict(list)
    accepted = []
    for record in ordered:
        x, z = map(float, record["world_position_meters"])
        cell_x, cell_z = math.floor(x / cell_size), math.floor(z / cell_size)
        conflict = False
        for delta_z in (-1, 0, 1):
            for delta_x in (-1, 0, 1):
                if any(
                    rectangles_overlap(record, other)
                    for other in cells.get((cell_x + delta_x, cell_z + delta_z), ())
                ):
                    conflict = True
                    break
            if conflict:
                break
        if conflict:
            rejection_counts["ORIENTED_FOOTPRINT_CONFLICT"] += 1
            continue
        cells[(cell_x, cell_z)].append(record)
        accepted.append(record)
    return sorted(
        accepted,
        key=lambda item: (
            int(item["building_osm_id"]),
            float(item["world_position_meters"][1]),
            float(item["world_position_meters"][0]),
        ),
    )


def write_contact_sheet(records: list[dict], cache: NativeTileCache, destination: Path) -> None:
    by_tile: dict[tuple[int, int], list[dict]] = defaultdict(list)
    for record in records:
        _, row, column = map(int, record["source_tile"])
        by_tile[(row, column)].append(record)
    selected = sorted(by_tile, key=lambda key: (-len(by_tile[key]), key))[:16]
    panels = []
    for row, column in selected:
        panel = cache.image(row, column).copy()
        for record in by_tile[(row, column)]:
            center_x, center_y = map(float, record["source_center_pixel"])
            axis = list(map(float, record["source_axis_pixels"]))
            color = (40, 230, 40) if record["orientation_source"] == "YOLO11N_OBB_DIRECT" else (40, 180, 255)
            cv2.line(panel, (round(axis[0]), round(axis[1])), (round(axis[2]), round(axis[3])), color, 2, cv2.LINE_AA)
            cv2.circle(panel, (round(center_x), round(center_y)), 2, (255, 255, 255), -1)
        cv2.putText(panel, f"{row}/{column} n={len(by_tile[(row,column)])}", (5, 16), cv2.FONT_HERSHEY_SIMPLEX, 0.38, (0, 0, 0), 2, cv2.LINE_AA)
        cv2.putText(panel, f"{row}/{column} n={len(by_tile[(row,column)])}", (5, 16), cv2.FONT_HERSHEY_SIMPLEX, 0.38, (255, 255, 255), 1, cv2.LINE_AA)
        panels.append(panel)
    while len(panels) < 16:
        panels.append(np.zeros((256, 256, 3), np.uint8))
    sheet = cv2.vconcat([cv2.hconcat(panels[index:index + 4]) for index in range(0, 16, 4)])
    cv2.imwrite(str(destination), sheet)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--artifact-dir", type=Path, required=True)
    parser.add_argument("--cache-dir", type=Path, required=True)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--source-model", type=Path)
    parser.add_argument("--device", default="cpu")
    parser.add_argument("--batch-size", type=int, default=64)
    parser.add_argument("--download-workers", type=int, default=24)
    parser.add_argument("--limit", type=int, default=0)
    parser.add_argument("--write-source", action="store_true")
    args = parser.parse_args()
    args.artifact_dir.mkdir(parents=True, exist_ok=True)

    source = json.loads(SOURCE_PATH.read_text(encoding="utf-8"))
    records = list(source["parked_vehicles"])
    if args.limit > 0:
        records = records[: args.limit]
    # Tile-order inference avoids repeatedly decoding the same 256 px sources.
    # Output order is restored from stable building/id evidence after filtering.
    records.sort(
        key=lambda item: (
            int(item["source_tile"][1]),
            int(item["source_tile"][2]),
            float(item["source_center_pixel"][1]),
            float(item["source_center_pixel"][0]),
        )
    )
    config = json.loads(AERIAL_CONFIG_PATH.read_text(encoding="utf-8"))
    aerial_manifest = json.loads(AERIAL_MANIFEST_PATH.read_text(encoding="utf-8"))
    context = json.loads(CONTEXT_PATH.read_text(encoding="utf-8"))
    service_response = requests.get(
        config["source"]["service_metadata_url"],
        headers={"User-Agent": USER_AGENT},
        timeout=45,
    )
    service_response.raise_for_status()
    frame = TileFrame(config, service_response.json())
    semantic = SemanticIndex(context)
    product_mask = ProductSemanticMask(SEMANTIC_MASK_PATH)
    cache = NativeTileCache(args.cache_dir, config["source"]["tile_url_template"])

    needed = sorted(required_tiles(records))
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.download_workers) as executor:
        for completed, _ in enumerate(executor.map(cache.download, needed), 1):
            if completed % 1000 == 0:
                print(f"downloaded {completed}/{len(needed)}", flush=True)

    model = YOLO(str(args.model))
    direct: list[dict] = []
    unresolved: list[dict] = []
    for start in range(0, len(records), args.batch_size):
        batch = records[start : start + args.batch_size]
        crops = [cache.crop(record) for record in batch]
        results = model.predict(
            crops,
            imgsz=MODEL_INPUT_PIXELS,
            conf=MODEL_CONFIDENCE,
            classes=[9, 10],
            device=args.device,
            batch=args.batch_size,
            verbose=False,
        )
        for record, result in zip(batch, results):
            refined = refine_direct(record, result, frame)
            if refined is None:
                unresolved.append(record)
            else:
                direct.append(refined)
        if (start // args.batch_size) % 20 == 0:
            print(
                f"inference {min(start + len(batch), len(records))}/{len(records)} "
                f"direct={len(direct)} unresolved={len(unresolved)}",
                flush=True,
            )

    direct_by_lot: dict[str, list[dict]] = defaultdict(list)
    for record in direct:
        direct_by_lot[str(record["lot"])].append(record)
    consensus = []
    for record in unresolved:
        refined = row_consensus(record, direct_by_lot)
        if refined is not None:
            consensus.append(refined)

    rejection_counts: Counter = Counter()
    candidates = []
    for record in [*direct, *consensus]:
        reason = semantic_footprint_reason(record, semantic, product_mask)
        if reason:
            rejection_counts[reason] += 1
            continue
        candidates.append(record)
    accepted = resolve_collisions(candidates, rejection_counts)
    accepted_ids = {record["id"] for record in accepted}
    reservoir_count = sum(
        -650.0 <= float(record["world_position_meters"][0]) <= 1100.0
        and 2700.0 <= float(record["world_position_meters"][1]) <= 3950.0
        for record in accepted
    )
    if args.limit <= 0 and reservoir_count < 20:
        raise RuntimeError("Refinement removed required Boulder Reservoir parking evidence")
    minimum_retained = min(8000, max(1, int(len(records) * 0.25)))
    if len(accepted) < minimum_retained:
        raise RuntimeError(f"Refinement retained too few poses: {len(accepted)}")

    model_hash = sha256(args.source_model or args.model)
    inference_model_hash = sha256(args.model)
    source["parked_vehicles"] = accepted
    source["parked_detection"] = {
        "schema": "questflight.parked-vehicle-native-scan-v3",
        "source": "City of Boulder native 2020 nine-inch imagery",
        "source_level": LOD,
        "orientation_authority": "YOLO11N_OBB_DIRECT_OR_LOCAL_ROW_CONSENSUS",
        "semantic_policy": "FULL_FOOTPRINT_EXCLUDES_ROAD_AEROWAY_BUILDING_WATER_AND_ATHLETIC_SURFACES",
        "collision_policy": "CONSERVATIVE_ROTATED_PASSENGER_FOOTPRINT_WITH_0_15M_MARGIN",
        "model": "yolo11n-obb / DOTA oriented small-vehicle class / offline authoring only",
        "model_url": MODEL_URL,
        "model_sha256": model_hash,
        "inference_model_sha256": inference_model_hash,
        "tool": "tools/kbdu_traffic/refine_parked_vehicle_placements.py",
        "input_records": len(records),
        "direct_obb_records": len(direct),
        "row_consensus_records": len(consensus),
        "accepted_records": len(accepted),
        "rejections": dict(sorted(rejection_counts.items())),
        "orientation_fallback_records": 0,
    }
    destination = args.artifact_dir / "traffic_sources_v2_candidate.json"
    destination.write_text(
        json.dumps(source, indent=2) + "\n", encoding="utf-8", newline="\n"
    )
    report = {
        "schema": "questflight.parked-vehicle-placement-audit-v2",
        "input_records": len(records),
        "direct_obb_records": len(direct),
        "row_consensus_records": len(consensus),
        "semantic_and_collision_candidates": len(candidates),
        "accepted_records": len(accepted),
        "reservoir_records": reservoir_count,
        "rejections": dict(sorted(rejection_counts.items())),
        "model_sha256": model_hash,
        "inference_model_sha256": inference_model_hash,
        "accepted_id_sha256": hashlib.sha256(
            "\n".join(sorted(accepted_ids)).encode("utf-8")
        ).hexdigest(),
    }
    (args.artifact_dir / "parked_vehicle_refinement_report.json").write_text(
        json.dumps(report, indent=2) + "\n", encoding="utf-8", newline="\n"
    )
    write_contact_sheet(
        accepted,
        cache,
        args.artifact_dir / "parked_vehicle_refinement_contact_sheet.png",
    )
    if args.write_source and args.limit > 0:
        raise ValueError("A diagnostic --limit run may not update the source authority")
    if args.write_source:
        SOURCE_PATH.write_text(
            json.dumps(source, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )
    print(json.dumps(report, indent=2), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
