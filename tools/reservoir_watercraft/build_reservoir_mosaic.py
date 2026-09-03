#!/usr/bin/env python3
"""Build bounded Boulder Reservoir source-imagery evidence.

The tool reuses the pinned City of Boulder level-19 tile service and the same
AEQD/product frame as the parked-vehicle authoring tools. Raw tiles and output
images are external artifacts; nothing produced by this script is a runtime
dependency.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import urllib.request
import urllib.error
from pathlib import Path

import cv2
import numpy as np
import pyproj


ROOT = Path(__file__).resolve().parents[2]
CONFIG_PATH = ROOT / "tools/aerial_surface/config.json"
CONTEXT_PATH = (
    ROOT
    / "QuestFlight/Assets/Production/Environment/ReconstructionV2"
    / "KBDU_ReferenceContextV2.json"
)
LEVEL = 19
TILE_PIXELS = 256


class TileFrame:
    def __init__(self, config: dict, service: dict) -> None:
        origin = config["origin"]
        local_crs = pyproj.CRS.from_proj4(
            f"+proj=aeqd +lat_0={float(origin['latitude_degrees']):.12f} "
            f"+lon_0={float(origin['longitude_degrees']):.12f} "
            "+datum=WGS84 +units=m +no_defs"
        )
        self.local_to_web = pyproj.Transformer.from_crs(
            local_crs, "EPSG:3857", always_xy=True
        )
        self.web_to_local = pyproj.Transformer.from_crs(
            "EPSG:3857", local_crs, always_xy=True
        )
        tile_info = service["tileInfo"]
        lod = next(
            item for item in tile_info["lods"] if int(item["level"]) == LEVEL
        )
        self.resolution = float(lod["resolution"])
        self.origin_x = float(tile_info["origin"]["x"])
        self.origin_y = float(tile_info["origin"]["y"])

    def world_to_global_pixel(self, x: float, z: float) -> tuple[float, float]:
        web_x, web_y = self.local_to_web.transform(x, z)
        return (
            (web_x - self.origin_x) / self.resolution,
            (self.origin_y - web_y) / self.resolution,
        )

    def global_pixel_to_world(self, x: float, y: float) -> tuple[float, float]:
        web_x = self.origin_x + x * self.resolution
        web_y = self.origin_y - y * self.resolution
        local_x, local_z = self.web_to_local.transform(web_x, web_y)
        return float(local_x), float(local_z)


def load_service(config: dict, destination: Path) -> dict:
    if destination.is_file():
        return json.loads(destination.read_text(encoding="utf-8"))
    request = urllib.request.Request(
        config["source"]["service_metadata_url"],
        headers={"User-Agent": "QuestFlight/1.0 reservoir-watercraft-authoring"},
    )
    try:
        with urllib.request.urlopen(request, timeout=45) as response:
            payload = response.read()
    except urllib.error.HTTPError:
        # The pinned ArcGIS service intermittently returns HTTP 500 for its
        # metadata endpoint while continuing to serve its standard WebMercator
        # cache. Keep authoring deterministic from the cache contract already
        # frozen by KBDU_AerialSurfaceV1_SourceManifest.json.
        resolution = 156543.03392804097 / (2**LEVEL)
        fallback = {
            "tileInfo": {
                "origin": {"x": -20037508.342787, "y": 20037508.342787},
                "lods": [{"level": LEVEL, "resolution": resolution}],
            },
            "metadata_status": "PINNED_STANDARD_WEB_MERCATOR_FALLBACK_AFTER_HTTP_500",
        }
        payload = (json.dumps(fallback, indent=2) + "\n").encode("utf-8")
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(payload)
    return json.loads(payload)


def reservoir_polygon(context: dict) -> list[tuple[float, float]]:
    quantization = float(context["coordinate_quantization_meters"])
    matches = [
        feature
        for feature in context["openstreetmap"]["features"]
        if feature.get("category") == "water"
        and feature.get("geometry_type") == "polygon"
        and feature.get("tags", {}).get("name") == "Boulder Reservoir"
    ]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one Boulder Reservoir polygon, found {len(matches)}")
    values = matches[0]["points_q"]
    return [
        (float(values[index]) * quantization, float(values[index + 1]) * quantization)
        for index in range(0, len(values), 2)
    ]


def tile_path(cache: Path, row: int, column: int) -> Path:
    return cache / f"l{LEVEL:02d}" / f"r{row:08d}_c{column:08d}.jpg"


def acquire_tile(cache: Path, template: str, row: int, column: int) -> Path:
    path = tile_path(cache, row, column)
    if path.is_file():
        return path
    url = template.format(level=LEVEL, row=row, column=column)
    request = urllib.request.Request(
        url, headers={"User-Agent": "QuestFlight/1.0 reservoir-watercraft-authoring"}
    )
    with urllib.request.urlopen(request, timeout=45) as response:
        payload = response.read()
    image = cv2.imdecode(np.frombuffer(payload, np.uint8), cv2.IMREAD_COLOR)
    if image is None or image.shape[:2] != (TILE_PIXELS, TILE_PIXELS):
        raise RuntimeError(f"Invalid source tile {LEVEL}/{row}/{column}")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)
    return path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--artifact-dir", type=Path, required=True)
    parser.add_argument("--cache-dir", type=Path, required=True)
    parser.add_argument(
        "--world-bounds",
        type=float,
        nargs=4,
        default=(-1000.0, 3300.0, 1500.0, 5300.0),
        metavar=("MIN_X", "MIN_Z", "MAX_X", "MAX_Z"),
    )
    args = parser.parse_args()
    args.artifact_dir.mkdir(parents=True, exist_ok=True)
    config = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
    context = json.loads(CONTEXT_PATH.read_text(encoding="utf-8"))
    service_path = args.artifact_dir / "city_2020_service_metadata.json"
    service = load_service(config, service_path)
    frame = TileFrame(config, service)

    min_x, min_z, max_x, max_z = args.world_bounds
    sample_world = [
        (min_x, min_z),
        (min_x, max_z),
        (max_x, min_z),
        (max_x, max_z),
    ]
    sample_pixels = [frame.world_to_global_pixel(x, z) for x, z in sample_world]
    global_min_x = math.floor(min(point[0] for point in sample_pixels))
    global_max_x = math.ceil(max(point[0] for point in sample_pixels))
    global_min_y = math.floor(min(point[1] for point in sample_pixels))
    global_max_y = math.ceil(max(point[1] for point in sample_pixels))
    column_min = global_min_x // TILE_PIXELS
    column_max = global_max_x // TILE_PIXELS
    row_min = global_min_y // TILE_PIXELS
    row_max = global_max_y // TILE_PIXELS

    canvas = np.empty(
        (
            (row_max - row_min + 1) * TILE_PIXELS,
            (column_max - column_min + 1) * TILE_PIXELS,
            3,
        ),
        dtype=np.uint8,
    )
    template = config["source"]["tile_url_template"]
    tile_hashes: list[str] = []
    for row in range(row_min, row_max + 1):
        for column in range(column_min, column_max + 1):
            path = acquire_tile(args.cache_dir, template, row, column)
            payload = path.read_bytes()
            tile_hashes.append(hashlib.sha256(payload).hexdigest())
            image = cv2.imdecode(np.frombuffer(payload, np.uint8), cv2.IMREAD_COLOR)
            y = (row - row_min) * TILE_PIXELS
            x = (column - column_min) * TILE_PIXELS
            canvas[y : y + TILE_PIXELS, x : x + TILE_PIXELS] = image

    crop_left = global_min_x - column_min * TILE_PIXELS
    crop_top = global_min_y - row_min * TILE_PIXELS
    crop = canvas[
        crop_top : crop_top + (global_max_y - global_min_y),
        crop_left : crop_left + (global_max_x - global_min_x),
    ].copy()
    mosaic_path = args.artifact_dir / "reservoir_city2020_l19_mosaic.jpg"
    cv2.imwrite(str(mosaic_path), crop, [cv2.IMWRITE_JPEG_QUALITY, 94])

    overlay = crop.copy()
    polygon_pixels = []
    for x, z in reservoir_polygon(context):
        px, py = frame.world_to_global_pixel(x, z)
        polygon_pixels.append(
            [int(round(px - global_min_x)), int(round(py - global_min_y))]
        )
    cv2.polylines(
        overlay,
        [np.asarray(polygon_pixels, dtype=np.int32)],
        True,
        (0, 255, 255),
        5,
        cv2.LINE_AA,
    )
    overlay_path = args.artifact_dir / "reservoir_city2020_l19_shoreline_overlay.jpg"
    cv2.imwrite(str(overlay_path), overlay, [cv2.IMWRITE_JPEG_QUALITY, 94])

    report = {
        "schema": "questflight.reservoir-source-mosaic-v1",
        "source": {
            "provider": config["source"]["provider"],
            "arcgis_item_id": config["source"]["arcgis_item_id"],
            "acquisition_period": config["source"]["acquisition_period"],
            "license_spdx": config["source"]["license_spdx"],
            "level": LEVEL,
            "service_metadata_sha256": hashlib.sha256(
                service_path.read_bytes()
            ).hexdigest(),
        },
        "world_bounds_meters": list(args.world_bounds),
        "global_pixel_bounds": [
            global_min_x,
            global_min_y,
            global_max_x,
            global_max_y,
        ],
        "tile_bounds": [column_min, row_min, column_max, row_max],
        "tile_count": len(tile_hashes),
        "tile_set_sha256": hashlib.sha256(
            "\n".join(sorted(tile_hashes)).encode("ascii")
        ).hexdigest(),
        "dimensions_pixels": [int(crop.shape[1]), int(crop.shape[0])],
        "mosaic_path": str(mosaic_path.resolve()),
        "shoreline_overlay_path": str(overlay_path.resolve()),
        "pixel_to_world_contract": (
            "global pixel = report global_pixel_bounds minimum + mosaic pixel; "
            "apply the pinned level-19 Web Mercator to local AEQD transform"
        ),
    }
    (args.artifact_dir / "reservoir_mosaic_report.json").write_text(
        json.dumps(report, indent=2) + "\n", encoding="utf-8"
    )
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
