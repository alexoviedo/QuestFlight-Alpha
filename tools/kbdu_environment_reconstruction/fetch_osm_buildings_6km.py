#!/usr/bin/env python3
"""Fetch and normalize the current bounded 6 km KBDU OSM building source."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
import tempfile
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any


SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[1]
ENVIRONMENT_TOOLS = REPO_ROOT / "tools/kbdu_environment"
sys.path.insert(0, str(ENVIRONMENT_TOOLS))

from kbdu_common import bbox_for_half_extent, geodetic_to_enu, simplify_polyline  # noqa: E402


ENDPOINTS = (
    "https://overpass-api.de/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
)
DEFAULT_ENVIRONMENT_CONFIG = ENVIRONMENT_TOOLS / "config.json"
DEFAULT_OUTPUT = SCRIPT_DIR / "osm_buildings_6km_source.json"


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def write_bytes(path: Path, value: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as handle:
        temporary = Path(handle.name)
        handle.write(value)
    temporary.replace(path)


def write_json(path: Path, value: Any, *, compact: bool = False) -> None:
    text = (
        json.dumps(value, sort_keys=True, separators=(",", ":"))
        if compact
        else json.dumps(value, indent=2, sort_keys=True)
    )
    write_bytes(path, (text + "\n").encode("utf-8"))


def query_for_bbox(bbox: tuple[float, float, float, float], timeout: int) -> str:
    bbox_text = ",".join(f"{value:.8f}" for value in bbox)
    return f'''[out:json][timeout:{timeout}];
(
  way["building"]({bbox_text});
  relation["building"]({bbox_text});
);
out tags geom qt;
'''


def fetch(query: str, timeout: int) -> tuple[bytes, str]:
    request_body = urllib.parse.urlencode({"data": query}).encode("ascii")
    errors: list[str] = []
    for endpoint in ENDPOINTS:
        request = urllib.request.Request(
            endpoint,
            data=request_body,
            method="POST",
            headers={
                "Content-Type": "application/x-www-form-urlencoded",
                "User-Agent": "T2-QuestFlight-KBDU-Reconstruction/2.0",
            },
        )
        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                payload = response.read()
            document = json.loads(payload)
            if not isinstance(document.get("elements"), list):
                raise RuntimeError("response has no elements array")
            return payload, endpoint
        except Exception as error:  # endpoint failover evidence is reported by caller
            errors.append(f"{endpoint}: {type(error).__name__}: {error}")
    raise RuntimeError("All public Overpass endpoints failed: " + " | ".join(errors))


def polygon_area(points: list[tuple[float, float]]) -> float:
    return abs(sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(points, points[1:]))) * 0.5


def join_outer_members(members: list[list[dict[str, Any]]]) -> list[list[dict[str, Any]]]:
    """Join relation outer-way fragments by exact Overpass endpoint coordinates."""
    pending = [list(value) for value in members if len(value) >= 2]
    rings: list[list[dict[str, Any]]] = []

    def key(point: dict[str, Any]) -> tuple[float, float]:
        return round(float(point["lat"]), 7), round(float(point["lon"]), 7)

    while pending:
        ring = pending.pop(0)
        changed = True
        while changed and key(ring[0]) != key(ring[-1]):
            changed = False
            for index, candidate in enumerate(pending):
                if key(ring[-1]) == key(candidate[0]):
                    ring.extend(candidate[1:])
                elif key(ring[-1]) == key(candidate[-1]):
                    ring.extend(reversed(candidate[:-1]))
                elif key(ring[0]) == key(candidate[-1]):
                    ring = candidate[:-1] + ring
                elif key(ring[0]) == key(candidate[0]):
                    ring = list(reversed(candidate[1:])) + ring
                else:
                    continue
                pending.pop(index)
                changed = True
                break
        if key(ring[0]) == key(ring[-1]):
            rings.append(ring)
    return rings


def element_rings(element: dict[str, Any]) -> list[tuple[str, list[dict[str, Any]]]]:
    if element.get("type") == "way":
        geometry = element.get("geometry") or []
        return [("geometry:0", geometry)] if len(geometry) >= 4 else []
    outer_members = [
        member.get("geometry") or []
        for member in element.get("members") or []
        if str(member.get("role") or "outer") in ("", "outer")
    ]
    return [
        (f"outer:{index}", geometry)
        for index, geometry in enumerate(join_outer_members(outer_members))
    ]


def normalize(raw: dict[str, Any], config: dict[str, Any], endpoint: str, query: str, raw_hash: str) -> dict[str, Any]:
    origin = config["airport"]["origin"]
    quantization = 0.5
    records: list[dict[str, Any]] = []
    invalid = 0
    outside_radius = 0
    seen_geometry: set[tuple[tuple[int, int], ...]] = set()
    for element in raw["elements"]:
        for part, geometry in element_rings(element):
            points: list[tuple[float, float]] = []
            for point in geometry:
                if point.get("lat") is None or point.get("lon") is None:
                    continue
                east, north, _ = geodetic_to_enu(
                    float(point["lat"]),
                    float(point["lon"]),
                    origin["elevation_msl_meters"],
                    origin["latitude_degrees"],
                    origin["longitude_degrees"],
                    origin["elevation_msl_meters"],
                )
                points.append((east, north))
            if len(points) < 4 or math.dist(points[0], points[-1]) > 1.5:
                invalid += 1
                continue
            points[-1] = points[0]
            points = simplify_polyline(points, 1.0, closed=True)
            if len(points) < 4:
                invalid += 1
                continue
            quantized: list[tuple[int, int]] = []
            for x, z in points:
                value = (round(x / quantization), round(z / quantization))
                if not quantized or value != quantized[-1]:
                    quantized.append(value)
            if quantized[0] != quantized[-1]:
                quantized.append(quantized[0])
            if len(set(quantized[:-1])) < 3:
                invalid += 1
                continue
            geometry_key = tuple(quantized)
            if geometry_key in seen_geometry:
                continue
            seen_geometry.add(geometry_key)
            vertices = quantized[:-1]
            x = sum(value[0] for value in vertices) * quantization / len(vertices)
            z = sum(value[1] for value in vertices) * quantization / len(vertices)
            distance = math.hypot(x, z)
            if distance > 6000.0:
                outside_radius += 1
                continue
            points_meters = [(value[0] * quantization, value[1] * quantization) for value in quantized]
            area = polygon_area(points_meters)
            if area < 1.0:
                invalid += 1
                continue
            tags = element.get("tags") or {}
            records.append(
                {
                    "category": "building",
                    "geometry_type": "polygon",
                    "points_q": [coordinate for point in quantized for coordinate in point],
                    "significant_outer_zone": area >= 25.0,
                    "source": {
                        "osm_id": int(element["id"]),
                        "osm_type": str(element["type"]),
                        "part": part,
                    },
                    "tags": {
                        key: str(tags[key])
                        for key in ("building", "building:levels", "height", "name")
                        if key in tags
                    },
                }
            )
    records.sort(key=lambda value: (value["source"]["osm_type"], value["source"]["osm_id"], value["source"]["part"]))
    counts = {"0-2km": 0, "2-4km": 0, "4-6km": 0}
    significant = {"0-2km": 0, "2-4km": 0, "4-6km": 0}
    for record in records:
        coordinates = record["points_q"]
        vertices = list(zip(coordinates[0::2], coordinates[1::2]))[:-1]
        x = sum(value[0] for value in vertices) * quantization / len(vertices)
        z = sum(value[1] for value in vertices) * quantization / len(vertices)
        distance = math.hypot(x, z)
        zone = "0-2km" if distance < 2000.0 else "2-4km" if distance < 4000.0 else "4-6km"
        counts[zone] += 1
        if record["significant_outer_zone"]:
            significant[zone] += 1
    return {
        "attribution": "© OpenStreetMap contributors",
        "bbox_shape": "6 km radial filter inside a 12 km square query",
        "coordinate_frame": "KBDU local tangent ENU; +X=east, +Z=north",
        "coordinate_quantization_meters": quantization,
        "copyright_url": "https://www.openstreetmap.org/copyright",
        "evidence": {
            "invalid_or_unclosed_geometry_parts": invalid,
            "outside_radial_filter_parts": outside_radius,
            "raw_element_count": len(raw["elements"]),
            "represented_valid_buildings_by_zone": counts,
            "significant_buildings_area_at_least_25m2_by_zone": significant,
        },
        "features": records,
        "label": "KBDU current bounded 6 km OSM building derivative",
        "license": "ODbL-1.0",
        "origin": origin,
        "schema_version": 1,
        "source": {
            "endpoint": endpoint,
            "osm_base_timestamp": (raw.get("osm3s") or {}).get("timestamp_osm_base"),
            "query_sha256": sha256_bytes(query.encode("utf-8")),
            "raw_response_sha256": raw_hash,
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact-dir", type=Path, required=True)
    parser.add_argument("--environment-config", type=Path, default=DEFAULT_ENVIRONMENT_CONFIG)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--raw-input", type=Path)
    parser.add_argument("--raw-endpoint", default="https://overpass.kumi.systems/api/interpreter")
    parser.add_argument("--timeout", type=int, default=180)
    args = parser.parse_args()
    config = json.loads(args.environment_config.read_text(encoding="utf-8"))
    bbox = bbox_for_half_extent(config, 6000.0)
    query = query_for_bbox(bbox, args.timeout)
    if args.raw_input:
        payload = args.raw_input.resolve().read_bytes()
        endpoint = args.raw_endpoint
    else:
        payload, endpoint = fetch(query, args.timeout)
    raw = json.loads(payload)
    raw_hash = sha256_bytes(payload)
    artifact_dir = args.artifact_dir.resolve()
    write_bytes(artifact_dir / "osm_buildings_6km_raw.json", payload)
    write_bytes(artifact_dir / "osm_buildings_6km_query.txt", query.encode("utf-8"))
    source = normalize(raw, config, endpoint, query, raw_hash)
    write_json(args.output.resolve(), source, compact=True)
    write_json(
        artifact_dir / "osm_buildings_6km_fetch_receipt.json",
        {
            "bbox_wgs84_south_west_north_east": bbox,
            "derived_output": args.output.resolve().as_posix(),
            "endpoint": endpoint,
            "license": "ODbL-1.0",
            "osm_base_timestamp": source["source"]["osm_base_timestamp"],
            "query_sha256": source["source"]["query_sha256"],
            "raw_response_bytes": len(payload),
            "raw_response_sha256": raw_hash,
        },
    )
    print(json.dumps({"artifact_dir": str(artifact_dir), "derived_output": str(args.output.resolve()), "evidence": source["evidence"]}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
