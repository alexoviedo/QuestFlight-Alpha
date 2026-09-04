#!/usr/bin/env python3
"""Inventory grade-separated road spans from frozen OSM, CDOT, and Traffic V2.

This is an offline authoring diagnostic.  It never modifies product assets and
keeps the bridge decision reproducible from pinned source files.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any, Iterable

from shapely.geometry import LineString, Point

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/kbdu_environment"))

from kbdu_common import geodetic_to_enu  # noqa: E402


DEFAULT_TRAFFIC = (
    ROOT
    / "QuestFlight/Assets/Production/Environment/TrafficV2/Data/"
    "KBDU_TrafficV2FullMapManifest.json"
)
ORIGIN_LATITUDE = 40.03936527
ORIGIN_LONGITUDE = -105.22608958
ORIGIN_ELEVATION_METERS = 5288.0 * 0.3048
WORLD_HALF_EXTENT_METERS = 12000.0
ROAD_CLASSES = {
    "motorway",
    "motorway_link",
    "trunk",
    "trunk_link",
    "primary",
    "primary_link",
    "secondary",
    "secondary_link",
    "tertiary",
    "tertiary_link",
    "unclassified",
    "residential",
    "service",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--osm", type=Path, required=True)
    parser.add_argument("--cdot", type=Path, required=True)
    parser.add_argument("--traffic", type=Path, default=DEFAULT_TRAFFIC)
    parser.add_argument("--imagery-year", type=int, default=2020)
    parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args()


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8-sig") as handle:
        return json.load(handle)


def project(latitude: float, longitude: float) -> tuple[float, float]:
    east, north, _ = geodetic_to_enu(
        latitude,
        longitude,
        ORIGIN_ELEVATION_METERS,
        ORIGIN_LATITUDE,
        ORIGIN_LONGITUDE,
        ORIGIN_ELEVATION_METERS,
    )
    return east, north


def osm_line(element: dict[str, Any]) -> LineString:
    return LineString(
        [project(float(item["lat"]), float(item["lon"])) for item in element["geometry"]]
    )


def is_upper_bridge(tags: dict[str, str]) -> bool:
    bridge = tags.get("bridge", "").lower()
    layer = int(tags.get("layer", "0") or 0)
    return (
        tags.get("highway") in ROAD_CLASSES
        and tags.get("tunnel", "no") not in {"yes", "building_passage"}
        and (bridge not in {"", "no"} or layer > 0)
    )


def bearing_degrees(line: LineString, distance: float) -> float:
    delta = min(5.0, max(0.5, line.length * 0.1))
    a = line.interpolate(max(0.0, distance - delta))
    b = line.interpolate(min(line.length, distance + delta))
    return math.degrees(math.atan2(b.y - a.y, b.x - a.x)) % 180.0


def angle_difference(a: float, b: float) -> float:
    difference = abs(a - b) % 180.0
    return min(difference, 180.0 - difference)


def representative_points(geometry: Any) -> Iterable[Point]:
    if geometry.is_empty:
        return []
    if geometry.geom_type == "Point":
        return [geometry]
    if geometry.geom_type == "MultiPoint":
        return list(geometry.geoms)
    if geometry.geom_type in {"LineString", "LinearRing"}:
        return [geometry.interpolate(0.5, normalized=True)]
    if hasattr(geometry, "geoms"):
        points: list[Point] = []
        for child in geometry.geoms:
            points.extend(representative_points(child))
        return points
    return []


def crossing_records(
    upper: dict[str, Any], roads: list[dict[str, Any]]
) -> list[dict[str, Any]]:
    line = upper["line"]
    output: list[dict[str, Any]] = []
    for lower in roads:
        if lower["osm_id"] == upper["osm_id"]:
            continue
        lower_tags = lower["tags"]
        if is_upper_bridge(lower_tags):
            continue
        if line.distance(lower["line"]) > 1.5:
            continue
        intersection = line.intersection(lower["line"])
        for point in representative_points(intersection):
            upper_distance = line.project(point)
            lower_distance = lower["line"].project(point)
            angle = angle_difference(
                bearing_degrees(line, upper_distance),
                bearing_degrees(lower["line"], lower_distance),
            )
            if angle < 25.0:
                continue
            output.append(
                {
                    "lower_osm_id": lower["osm_id"],
                    "lower_tags": lower_tags,
                    "crossing_xz_meters": [round(point.x, 3), round(point.y, 3)],
                    "crossing_angle_degrees": round(angle, 3),
                }
            )
    unique: dict[tuple[int, int, int], dict[str, Any]] = {}
    for record in output:
        x, z = record["crossing_xz_meters"]
        key = (record["lower_osm_id"], round(x), round(z))
        unique[key] = record
    return sorted(unique.values(), key=lambda value: (value["lower_osm_id"], value["crossing_xz_meters"]))


def traffic_matches(
    traffic: list[dict[str, Any]],
    reference_line: LineString,
    maximum_distance: float,
    maximum_angle: float,
) -> list[str]:
    matches: list[str] = []
    reference_bearing = bearing_degrees(reference_line, reference_line.length * 0.5)
    for corridor in traffic:
        line = corridor["line"]
        if line.distance(reference_line) > maximum_distance:
            continue
        reference_point = reference_line.interpolate(reference_line.project(line.centroid))
        corridor_distance = line.project(reference_point)
        if angle_difference(reference_bearing, bearing_degrees(line, corridor_distance)) > maximum_angle:
            continue
        matches.append(corridor["corridor_id"])
    return sorted(set(matches))


def main() -> int:
    args = parse_args()
    osm = load_json(args.osm)
    cdot = load_json(args.cdot)
    traffic_manifest = load_json(args.traffic)

    roads: list[dict[str, Any]] = []
    for element in osm.get("elements", []):
        tags = element.get("tags", {})
        if element.get("type") != "way" or tags.get("highway") not in ROAD_CLASSES:
            continue
        geometry = element.get("geometry")
        if not geometry or len(geometry) < 2:
            continue
        line = osm_line(element)
        min_x, min_z, max_x, max_z = line.bounds
        if (
            max_x < -WORLD_HALF_EXTENT_METERS
            or min_x > WORLD_HALF_EXTENT_METERS
            or max_z < -WORLD_HALF_EXTENT_METERS
            or min_z > WORLD_HALF_EXTENT_METERS
        ):
            continue
        roads.append(
            {
                "osm_id": int(element["id"]),
                "tags": {key: str(value) for key, value in tags.items()},
                "line": line,
            }
        )

    cdot_by_id: dict[str, dict[str, Any]] = {}
    for feature in cdot.get("features", []):
        attributes = feature["attributes"]
        stable_id = str(attributes.get("STRID") or attributes.get("STRUCTURE_NUM"))
        if stable_id in cdot_by_id:
            continue
        latitude = float(attributes.get("PRECISE_LAT") or feature["geometry"]["y"])
        longitude = float(attributes.get("PRECISE_LON") or feature["geometry"]["x"])
        x, z = project(latitude, longitude)
        cdot_by_id[stable_id] = {
            "structure_id": stable_id,
            "point": Point(x, z),
            "attributes": attributes,
        }

    traffic: list[dict[str, Any]] = []
    for corridor in traffic_manifest["corridors"]:
        coordinates = list(zip(corridor["points_xz_meters"][::2], corridor["points_xz_meters"][1::2]))
        traffic.append(
            {
                "corridor_id": corridor["corridor_id"],
                "line": LineString(coordinates),
            }
        )

    accepted: list[dict[str, Any]] = []
    rejected: list[dict[str, Any]] = []
    upper_candidates = [road for road in roads if is_upper_bridge(road["tags"])]
    for upper in sorted(upper_candidates, key=lambda value: value["osm_id"]):
        line = upper["line"]
        crossings = crossing_records(upper, roads)
        nearest_cdot = min(cdot_by_id.values(), key=lambda value: line.distance(value["point"]))
        cdot_distance = line.distance(nearest_cdot["point"])
        year_text = str(nearest_cdot["attributes"].get("YEARBUILT") or "")
        year = int(year_text) if year_text.isdigit() else None
        reasons: list[str] = []
        if not crossings:
            reasons.append("NO_UNAMBIGUOUS_LOWER_ROAD")
        if cdot_distance > 100.0:
            reasons.append("NO_MATCHING_CDOT_STRUCTURE_WITHIN_100M")
        if year is not None and year > args.imagery_year:
            reasons.append("POSTDATES_SCENE_IMAGERY")

        upper_routes = traffic_matches(traffic, line, 12.0, 22.5)
        lower_routes: list[str] = []
        for crossing in crossings:
            lower = next(road for road in roads if road["osm_id"] == crossing["lower_osm_id"])
            crossing_point = Point(*crossing["crossing_xz_meters"])
            lower_interval = lower["line"].intersection(crossing_point.buffer(100.0))
            if lower_interval.is_empty:
                lower_interval = lower["line"]
            if lower_interval.geom_type == "MultiLineString":
                lower_interval = max(lower_interval.geoms, key=lambda child: child.length)
            lower_routes.extend(traffic_matches(traffic, lower_interval, 12.0, 22.5))
        lower_routes = sorted(set(lower_routes))
        if not upper_routes:
            reasons.append("NO_ACTIVE_UPPER_TRAFFIC_CORRIDOR")
        if crossings and not lower_routes:
            reasons.append("NO_ACTIVE_LOWER_TRAFFIC_CORRIDOR")

        attributes = nearest_cdot["attributes"]
        record = {
            "osm_way_id": upper["osm_id"],
            "osm_tags": upper["tags"],
            "bounds_xz_meters": [round(value, 3) for value in line.bounds],
            "length_meters": round(line.length, 3),
            "crossings": crossings,
            "upper_route_ids": upper_routes,
            "lower_route_ids": lower_routes,
            "cdot": {
                "structure_id": nearest_cdot["structure_id"],
                "distance_to_osm_meters": round(cdot_distance, 3),
                "facility": attributes.get("FACILITY"),
                "feature_intersected": attributes.get("FEATINT"),
                "year_built": year,
                "structure_length_feet": attributes.get("STRLENGTH"),
                "clearance_min_ne_feet_inches": attributes.get("CLRMINNE"),
                "clearance_min_sw_feet_inches": attributes.get("CLRMINSW"),
                "on_under": attributes.get("ON_UNDER"),
            },
        }
        if reasons:
            record["rejection_reasons"] = reasons
            rejected.append(record)
        else:
            record["span_id"] = f"KBDU-GSR-{nearest_cdot['structure_id']}"
            accepted.append(record)

    groups: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for record in accepted:
        groups[record["cdot"]["structure_id"]].append(record)
    accepted_groups = [
        {
            "span_id": f"KBDU-GSR-{structure_id}",
            "structure_id": structure_id,
            "upper_osm_way_ids": sorted(record["osm_way_id"] for record in records),
            "upper_route_ids": sorted({route for record in records for route in record["upper_route_ids"]}),
            "lower_osm_way_ids": sorted(
                {crossing["lower_osm_id"] for record in records for crossing in record["crossings"]}
            ),
            "lower_route_ids": sorted({route for record in records for route in record["lower_route_ids"]}),
            "members": records,
        }
        for structure_id, records in sorted(groups.items())
    ]

    report = {
        "schema": "questflight.grade-separated-road-inventory.v1",
        "inputs": {
            "osm": str(args.osm.resolve()),
            "cdot": str(args.cdot.resolve()),
            "traffic": str(args.traffic.resolve()),
            "imagery_year": args.imagery_year,
        },
        "metrics": {
            "osm_road_count_in_world": len(roads),
            "osm_upper_bridge_way_count": len(upper_candidates),
            "cdot_unique_bridge_count": len(cdot_by_id),
            "traffic_corridor_count": len(traffic),
            "accepted_way_count": len(accepted),
            "accepted_structure_count": len(accepted_groups),
            "rejected_way_count": len(rejected),
        },
        "accepted_structures": accepted_groups,
        "rejected_upper_ways": rejected,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps(report["metrics"], indent=2, sort_keys=True))
    for group in accepted_groups:
        member = group["members"][0]
        cdot_record = member["cdot"]
        print(
            f"ACCEPT {group['span_id']}: OSM={group['upper_osm_way_ids']} "
            f"upper_routes={len(group['upper_route_ids'])} lower={group['lower_osm_way_ids']} "
            f"lower_routes={len(group['lower_route_ids'])} "
            f"{cdot_record['facility']} over {cdot_record['feature_intersected']}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
