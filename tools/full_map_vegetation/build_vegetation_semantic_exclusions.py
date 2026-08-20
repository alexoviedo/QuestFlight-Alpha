#!/usr/bin/env python3
"""Compile hard vegetation exclusions for water and athletic playing surfaces.

The runtime never reads this mask.  It is an offline authority used to reject
tree/shrub centers, constrain grove aggregates, and clear the existing far
canopy map.  Raw OpenStreetMap responses remain outside Git.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from collections import Counter
from pathlib import Path
from typing import Any, Iterable

import cv2
import numpy as np
from PIL import Image
from shapely.geometry import LineString, Polygon, box
from shapely.ops import polygonize, unary_union

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPOSITORY_ROOT))

from tools.kbdu_environment.kbdu_common import bbox_for_half_extent, geodetic_to_enu


WORLD_MIN = -12_000.0
WORLD_SIZE = 24_000.0
OUTPUT_SIZE = 4096
WATER_THRESHOLD = 32
OVERPASS_ENDPOINTS = (
    "https://overpass-api.de/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
)
FIELD_SPORTS = (
    "american_football|baseball|cricket|field_hockey|lacrosse|multi|rugby|"
    "rugby_league|rugby_union|soccer|softball"
)
FIELD_SPORT_VALUES = set(FIELD_SPORTS.split("|"))
NON_SURFACE_LEISURE = {"park", "recreation_ground", "sports_centre", "stadium"}


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--water-mask", type=Path, required=True)
    parser.add_argument("--raw-cache", type=Path, required=True)
    parser.add_argument("--output-mask", type=Path, required=True)
    parser.add_argument("--output-manifest", type=Path, required=True)
    parser.add_argument("--debug-preview", type=Path)
    parser.add_argument("--timeout", type=int, default=180)
    return parser.parse_args()


def sports_query(config: dict[str, Any], timeout: int) -> str:
    south, west, north, east = bbox_for_half_extent(config, 12_000.0)
    bounds = f"{south:.8f},{west:.8f},{north:.8f},{east:.8f}"
    return f'''[out:json][timeout:{timeout}];
(
  way["leisure"="pitch"]({bounds});
  relation["leisure"="pitch"]({bounds});
  way["leisure"="track"]({bounds});
  relation["leisure"="track"]({bounds});
  way["sport"~"^({FIELD_SPORTS})$"]({bounds});
  relation["sport"~"^({FIELD_SPORTS})$"]({bounds});
);
out tags geom qt;
'''


def fetch_or_load(query: str, cache: Path, timeout: int) -> tuple[dict[str, Any], str]:
    if cache.exists():
        return json.loads(cache.read_text(encoding="utf-8")), "pinned-local-cache"
    cache.parent.mkdir(parents=True, exist_ok=True)
    body = urllib.parse.urlencode({"data": query}).encode("ascii")
    errors = []
    for endpoint in OVERPASS_ENDPOINTS:
        for attempt in range(3):
            request = urllib.request.Request(
                endpoint,
                data=body,
                headers={"User-Agent": "QuestFlight-offline-semantic-compiler/1.0"},
                method="POST",
            )
            try:
                with urllib.request.urlopen(request, timeout=timeout + 30) as response:
                    payload = response.read()
                document = json.loads(payload)
                cache.write_bytes(payload)
                return document, endpoint
            except (OSError, urllib.error.URLError, json.JSONDecodeError) as error:
                errors.append(f"{endpoint} attempt {attempt + 1}: {error}")
                time.sleep(2 ** attempt)
    raise RuntimeError("Unable to fetch OSM athletic surfaces: " + " | ".join(errors))


def local_point(
    value: dict[str, Any], origin: dict[str, float]
) -> tuple[float, float]:
    east, north, _up = geodetic_to_enu(
        float(value["lat"]),
        float(value["lon"]),
        0.0,
        float(origin["latitude_degrees"]),
        float(origin["longitude_degrees"]),
        float(origin["elevation_msl_meters"]),
    )
    return east, north


def closed_polygon(points: Iterable[tuple[float, float]]) -> Polygon | None:
    values = list(points)
    if len(values) < 3:
        return None
    if values[0] != values[-1]:
        values.append(values[0])
    polygon = Polygon(values)
    if not polygon.is_valid:
        polygon = polygon.buffer(0)
    if polygon.is_empty or polygon.area < 80.0:
        return None
    return polygon


def relation_polygons(
    element: dict[str, Any], origin: dict[str, float]
) -> list[Polygon]:
    outer_lines: list[LineString] = []
    inner_lines: list[LineString] = []
    for member in element.get("members", []):
        geometry = member.get("geometry") or []
        if len(geometry) < 2:
            continue
        line = LineString(local_point(point, origin) for point in geometry)
        if member.get("role") == "inner":
            inner_lines.append(line)
        else:
            outer_lines.append(line)
    if not outer_lines:
        return []
    outers = list(polygonize(unary_union(outer_lines)))
    inners = list(polygonize(unary_union(inner_lines))) if inner_lines else []
    result = []
    for outer in outers:
        value = outer
        for inner in inners:
            if value.intersects(inner):
                value = value.difference(inner)
        if value.is_empty:
            continue
        if value.geom_type == "Polygon":
            result.append(value)
        elif value.geom_type == "MultiPolygon":
            result.extend(value.geoms)
    return [value for value in result if value.area >= 80.0]


def element_polygons(
    element: dict[str, Any], origin: dict[str, float]
) -> list[Polygon]:
    if element.get("type") == "way":
        geometry = element.get("geometry") or []
        polygon = closed_polygon(local_point(point, origin) for point in geometry)
        return [polygon] if polygon is not None else []
    if element.get("type") == "relation":
        return relation_polygons(element, origin)
    return []


def semantic_role(tags: dict[str, Any], area_square_meters: float) -> str | None:
    """Accept only actual playing surfaces, never a containing recreation parcel."""
    leisure = str(tags.get("leisure") or "")
    sport = str(tags.get("sport") or "")
    if leisure == "pitch":
        return "playing_surface"
    if leisure == "track":
        return "athletic_track" if sport in {"athletics", "running"} else None
    if leisure in NON_SURFACE_LEISURE:
        return None
    if sport in FIELD_SPORT_VALUES and area_square_meters <= 40_000.0:
        return "playing_surface"
    return None


def pixel_polygon(polygon: Polygon) -> np.ndarray:
    points = []
    for east, north in polygon.exterior.coords:
        column = round((east - WORLD_MIN) / WORLD_SIZE * (OUTPUT_SIZE - 1))
        row = round((WORLD_MIN + WORLD_SIZE - north) / WORLD_SIZE * (OUTPUT_SIZE - 1))
        points.append((column, row))
    return np.asarray(points, dtype=np.int32)


def main() -> None:
    args = parse_args()
    config = json.loads(args.config.read_text(encoding="utf-8"))
    query = sports_query(config, args.timeout)
    osm, endpoint = fetch_or_load(query, args.raw_cache, args.timeout)
    origin = config["airport"]["origin"]
    world = box(WORLD_MIN, WORLD_MIN, WORLD_MIN + WORLD_SIZE, WORLD_MIN + WORLD_SIZE)

    polygons: list[tuple[Polygon, dict[str, Any], str, str]] = []
    seen: set[tuple[str, int]] = set()
    skipped = Counter()
    for element in osm.get("elements", []):
        key = (str(element.get("type")), int(element.get("id", 0)))
        if key in seen:
            continue
        seen.add(key)
        tags = element.get("tags") or {}
        values = element_polygons(element, origin)
        if not values:
            skipped["missing_or_nonpolygon_geometry"] += 1
            continue
        for value in values:
            clipped = value.intersection(world)
            if clipped.is_empty:
                skipped["outside_product_bounds"] += 1
                continue
            parts = [clipped] if clipped.geom_type == "Polygon" else list(clipped.geoms)
            for part in parts:
                if part.area < 80.0:
                    skipped["below_80_square_meters"] += 1
                    continue
                role = semantic_role(tags, float(part.area))
                if role is None:
                    skipped["not_an_actual_playing_surface"] += 1
                    continue
                polygons.append((part, tags, role, f"osm:{key[0]}/{key[1]}"))

    water_source = np.asarray(Image.open(args.water_mask).convert("RGB"))
    water_channels = cv2.resize(
        water_source, (OUTPUT_SIZE, OUTPUT_SIZE), interpolation=cv2.INTER_LINEAR
    )
    # The reflection mask is feathered.  Thresholding its stable interior avoids
    # converting natural riparian tree lines into an artificial bare shoreline.
    water = (np.max(water_channels, axis=2) >= WATER_THRESHOLD).astype(np.uint8) * 255
    sports = np.zeros((OUTPUT_SIZE, OUTPUT_SIZE), dtype=np.uint8)
    records = []
    role_counts = Counter()
    for polygon, tags, role, source_id in polygons:
        cv2.fillPoly(sports, [pixel_polygon(polygon)], 255, lineType=cv2.LINE_8)
        role_counts[role] += 1
        minimum_x, minimum_z, maximum_x, maximum_z = polygon.bounds
        records.append({
            "source_id": source_id,
            "role": role,
            "sport": tags.get("sport"),
            "leisure": tags.get("leisure"),
            "name": tags.get("name"),
            "area_square_meters": round(float(polygon.area), 2),
            "bounds_enu_meters": [
                round(minimum_x, 2), round(minimum_z, 2),
                round(maximum_x, 2), round(maximum_z, 2),
            ],
        })

    packed = np.zeros((OUTPUT_SIZE, OUTPUT_SIZE, 3), dtype=np.uint8)
    packed[:, :, 0] = water
    packed[:, :, 1] = sports
    args.output_mask.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(packed).save(args.output_mask, optimize=True)

    if args.debug_preview is not None:
        preview = cv2.resize(packed, (1024, 1024), interpolation=cv2.INTER_AREA)
        args.debug_preview.parent.mkdir(parents=True, exist_ok=True)
        cv2.imwrite(str(args.debug_preview), preview[:, :, ::-1])

    manifest = {
        "schema": "questflight.full-map-vegetation-semantic-exclusion.v1",
        "contract": {
            "coordinate_frame": "KBDU local ENU; +X east, +Z north",
            "bounds_meters": [WORLD_MIN, WORLD_MIN, WORLD_MIN + WORLD_SIZE, WORLD_MIN + WORLD_SIZE],
            "texture_size": [OUTPUT_SIZE, OUTPUT_SIZE],
            "meters_per_texel": WORLD_SIZE / OUTPUT_SIZE,
            "row_zero": "north",
            "channels": {
                "r": "hard water interior vegetation exclusion",
                "g": "verified athletic playing-surface vegetation exclusion",
                "b": "reserved",
            },
            "runtime_dependency": False,
        },
        "processing": {
            "generator": "tools/full_map_vegetation/build_vegetation_semantic_exclusions.py",
            "water_threshold": WATER_THRESHOLD,
            "sports_policy": (
                "actual OSM pitch/track/field-sport polygons only; parks, sports-centre parcels, "
                "stadium parcels, and golf courses are not blanket exclusions"
            ),
            "shoreline_policy": "strong feathered-mask interior only; no outward dilation",
        },
        "sources": {
            "water_mask": {"path": str(args.water_mask), "sha256": sha256(args.water_mask)},
            "environment_config": {"path": str(args.config), "sha256": sha256(args.config)},
            "osm_query_sha256": sha256_bytes(query.encode("utf-8")),
            "osm_response_sha256": sha256(args.raw_cache),
            "osm_endpoint_or_cache": endpoint,
            "osm_base_timestamp": (osm.get("osm3s") or {}).get("timestamp_osm_base"),
            "raw_osm_committed": False,
            "license": "ODbL-1.0",
        },
        "statistics": {
            "water_excluded_texels": int(np.count_nonzero(water)),
            "sports_excluded_texels": int(np.count_nonzero(sports)),
            "sports_polygon_count": len(polygons),
            "sports_role_counts": dict(sorted(role_counts.items())),
            "skipped": dict(sorted(skipped.items())),
            "mask_bytes": args.output_mask.stat().st_size,
            "mask_sha256": sha256(args.output_mask),
        },
        "sports_surfaces": records,
    }
    args.output_manifest.parent.mkdir(parents=True, exist_ok=True)
    args.output_manifest.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest["statistics"], indent=2))


if __name__ == "__main__":
    main()
