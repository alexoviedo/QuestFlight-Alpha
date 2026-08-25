#!/usr/bin/env python3
"""Compile a small real-data directed-lane graph for the Traffic V2 Gate-1 proof.

This is deliberately not the Phase-3 source gate.  It consumes one immutable
external freeze offline, selects three benchmark-camera areas, and emits a
compact lane-chain graph suitable for an end-to-end trajectory proof.  It does
not claim full-map coverage, legal intersection turns, demand calibration, or
product acceptance.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import time
from collections import Counter
from pathlib import Path
from typing import Any, Iterable

from pyproj import Transformer


SCHEMA = "questflight.traffic-v2.walking-skeleton-lanes.v1"
LANE_WIDTH_METERS = 3.5
ELIGIBLE_HIGHWAYS = frozenset(
    {
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
        "residential",
        "living_street",
        "unclassified",
    }
)
CLASS_SPEED_MPS = {
    "motorway": 29.1,
    "motorway_link": 15.6,
    "trunk": 24.6,
    "trunk_link": 15.6,
    "primary": 20.1,
    "primary_link": 13.4,
    "secondary": 17.9,
    "secondary_link": 12.3,
    "tertiary": 15.6,
    "tertiary_link": 11.2,
    "residential": 11.2,
    "living_street": 6.7,
    "unclassified": 11.2,
}
ZONE_SPECS = (
    {
        "id": "airport",
        "camera_id": "airport_boulevard",
        "radius_meters": 1200.0,
    },
    {
        "id": "divided_highway",
        "camera_id": "diagonal_highway_approach",
        "radius_meters": 1200.0,
    },
    {
        "id": "residential",
        "camera_id": "residential",
        "radius_meters": 900.0,
    },
)


class Gate1Error(RuntimeError):
    """A fail-closed walking-skeleton input or semantic error."""


def canonical_json_bytes(value: Any) -> bytes:
    return (
        json.dumps(
            value,
            allow_nan=False,
            ensure_ascii=False,
            separators=(",", ":"),
            sort_keys=True,
        )
        + "\n"
    ).encode("utf-8")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(canonical_json_bytes(value))


def strict_positive_int(value: Any, *, maximum: int = 12) -> int | None:
    if value is None or isinstance(value, bool):
        return None
    text = str(value).strip()
    if not text.isdigit():
        return None
    parsed = int(text)
    return parsed if 1 <= parsed <= maximum else None


def effective_access(tags: dict[str, Any]) -> tuple[bool, str]:
    for key in tags:
        lowered = key.lower()
        if "conditional" in lowered and (
            lowered.startswith("access")
            or lowered.startswith("vehicle")
            or lowered.startswith("motor_vehicle")
            or lowered.startswith("motorcar")
        ):
            return False, "unresolved_conditional_motor_access"
        if ":lanes" in lowered and (
            lowered.startswith("access")
            or lowered.startswith("vehicle")
            or lowered.startswith("motor_vehicle")
            or lowered.startswith("motorcar")
        ):
            return False, "unresolved_lane_specific_motor_access"
    for key in ("motorcar", "motor_vehicle", "vehicle", "access"):
        if key not in tags or not str(tags[key]).strip():
            continue
        value = str(tags[key]).strip().lower()
        if value in {"yes", "designated", "permissive", "destination", "customers", "delivery"}:
            return True, f"allowed_by_{key}"
        if value in {"no", "private", "agricultural", "forestry", "emergency"}:
            return False, f"excluded_by_{key}_{value}"
        return False, f"unmapped_{key}_{value}"
    return True, "allowed_by_osm_default"


def driving_direction(tags: dict[str, Any]) -> tuple[str | None, str]:
    for key in (
        "oneway:motorcar:conditional",
        "oneway:motor_vehicle:conditional",
        "oneway:vehicle:conditional",
        "oneway:conditional",
    ):
        if key in tags and str(tags[key]).strip():
            return None, "unresolved_conditional_oneway"
    raw: Any = None
    authority = "osm_default"
    for key in ("oneway:motorcar", "oneway:motor_vehicle", "oneway:vehicle", "oneway"):
        if key in tags and str(tags[key]).strip():
            raw = tags[key]
            authority = key
            break
    if raw is None:
        if tags.get("highway") == "motorway" or str(tags.get("junction", "")).lower() == "roundabout":
            return "forward", "osm_implicit_oneway"
        return "both", authority
    value = str(raw).strip().lower()
    if value in {"yes", "1", "true"}:
        return "forward", authority
    if value in {"-1", "reverse"}:
        return "reverse", authority
    if value in {"no", "0", "false"}:
        return "both", authority
    return None, f"unmapped_{authority}_{value}"


def lane_counts(tags: dict[str, Any], direction: str) -> tuple[int, int] | None:
    if any(key.startswith("lanes") and "conditional" in key for key in tags):
        return None
    total = strict_positive_int(tags.get("lanes"))
    forward = strict_positive_int(tags.get("lanes:forward"))
    backward = strict_positive_int(tags.get("lanes:backward"))
    both_ways = strict_positive_int(tags.get("lanes:both_ways")) or 0
    if direction == "forward":
        if backward or both_ways:
            return None
        return (forward or total or 1, 0)
    if direction == "reverse":
        if forward or both_ways:
            return None
        return (0, backward or total or 1)
    if forward is not None or backward is not None:
        if forward is None or backward is None:
            return None
        if total is not None and forward + backward + both_ways > total:
            return None
        return (forward, backward)
    if total is None:
        return (1, 1)
    if total == 1:
        return (1, 1)
    remaining = total - both_ways
    if remaining < 2 or remaining % 2:
        return None
    return (remaining // 2, remaining // 2)


def parse_speed_mps(tags: dict[str, Any], road_class: str) -> float:
    raw = tags.get("maxspeed")
    if raw is not None:
        text = str(raw).strip().lower()
        try:
            if text.endswith("mph"):
                return round(float(text[:-3].strip()) * 0.44704, 3)
            if text.replace(".", "", 1).isdigit():
                return round(float(text) / 3.6, 3)
        except ValueError:
            pass
    return CLASS_SPEED_MPS[road_class]


def polyline_length(points: list[tuple[float, float]]) -> float:
    return sum(math.dist(first, second) for first, second in zip(points, points[1:]))


def offset_polyline(points: list[tuple[float, float]], offset: float) -> list[list[float]]:
    if len(points) < 2:
        raise Gate1Error("Lane polyline has fewer than two points")
    directions: list[tuple[float, float]] = []
    for first, second in zip(points, points[1:]):
        dx, dz = second[0] - first[0], second[1] - first[1]
        length = math.hypot(dx, dz)
        if length <= 1e-6:
            directions.append((0.0, 0.0))
        else:
            directions.append((dx / length, dz / length))
    result: list[list[float]] = []
    for index, (x, z) in enumerate(points):
        candidates = []
        if index:
            candidates.append(directions[index - 1])
        if index < len(directions):
            candidates.append(directions[index])
        dx = sum(item[0] for item in candidates)
        dz = sum(item[1] for item in candidates)
        length = math.hypot(dx, dz)
        if length <= 1e-6:
            dx, dz = next((item for item in candidates if item != (0.0, 0.0)), (1.0, 0.0))
            length = math.hypot(dx, dz)
        dx, dz = dx / length, dz / length
        left_x, left_z = -dz, dx
        result.append([round(x + left_x * offset, 3), round(z + left_z * offset, 3)])
    return result


def contiguous_ranges(values: list[str | None]) -> Iterable[tuple[str, int, int]]:
    start = 0
    while start < len(values):
        zone = values[start]
        if zone is None:
            start += 1
            continue
        end = start + 1
        while end < len(values) and values[end] == zone:
            end += 1
        yield zone, start, end
        start = end


def load_zones(camera_contract_path: Path) -> tuple[list[dict[str, Any]], str]:
    contract = read_json(camera_contract_path)
    views = {value["id"]: value for value in contract["views"]}
    zones = []
    for spec in ZONE_SPECS:
        view = views.get(spec["camera_id"])
        if view is None:
            raise Gate1Error(f"Missing benchmark camera {spec['camera_id']}")
        position = view["resolved_position_meters"]
        zones.append(
            {
                **spec,
                "center_xz_meters": [round(float(position["x"]), 3), round(float(position["z"]), 3)],
            }
        )
    return zones, sha256_file(camera_contract_path)


def choose_zone(x: float, z: float, zones: list[dict[str, Any]]) -> str | None:
    matches = []
    for zone in zones:
        cx, cz = zone["center_xz_meters"]
        distance = math.hypot(x - cx, z - cz)
        if distance <= float(zone["radius_meters"]):
            matches.append((distance / float(zone["radius_meters"]), zone["id"]))
    return min(matches)[1] if matches else None


def compile_lanes(
    external_run: Path,
    camera_contract_path: Path,
    expected_freeze_sha256: str,
) -> dict[str, Any]:
    freeze_path = external_run / "freeze_manifest.json"
    if sha256_file(freeze_path) != expected_freeze_sha256:
        raise Gate1Error("External source freeze SHA-256 does not match the approved Gate-1 input")
    freeze = read_json(freeze_path)
    zones, camera_hash = load_zones(camera_contract_path)
    osm = next((value for value in freeze["sources"] if value["id"] == "osm_bounded_roads"), None)
    if osm is None or osm.get("status") != "FROZEN":
        raise Gate1Error("Frozen OSM road source is missing")
    raw_path = external_run / osm["raw"]["path"]
    if raw_path.stat().st_size != int(osm["raw"]["bytes"]) or sha256_file(raw_path) != osm["raw"]["sha256"]:
        raise Gate1Error("Frozen OSM road bytes do not match the source manifest")
    payload = read_json(raw_path)
    nodes = {
        int(value["id"]): (float(value["lon"]), float(value["lat"]))
        for value in payload["elements"]
        if value.get("type") == "node"
    }
    transformer = Transformer.from_crs(
        "EPSG:4326", freeze["coordinate_contract"]["projection"], always_xy=True
    )
    ways: dict[int, dict[str, Any]] = {}
    duplicate_occurrences = 0
    for value in payload["elements"]:
        if value.get("type") != "way":
            continue
        way_id = int(value["id"])
        normalized = {"nodes": value.get("nodes") or [], "tags": value.get("tags") or {}}
        previous = ways.get(way_id)
        if previous is not None:
            duplicate_occurrences += 1
            if canonical_json_bytes(previous) != canonical_json_bytes(normalized):
                raise Gate1Error(f"OSM way/{way_id} has inconsistent duplicate occurrences")
            continue
        ways[way_id] = normalized

    exclusion_counts: Counter[str] = Counter()
    chains: list[dict[str, Any]] = []
    for way_id in sorted(ways):
        value = ways[way_id]
        tags = value["tags"]
        road_class = str(tags.get("highway", ""))
        if road_class not in ELIGIBLE_HIGHWAYS:
            exclusion_counts["non_motor_road_class"] += 1
            continue
        access_allowed, access_reason = effective_access(tags)
        if not access_allowed:
            exclusion_counts[access_reason] += 1
            continue
        direction, direction_authority = driving_direction(tags)
        if direction is None:
            exclusion_counts[direction_authority] += 1
            continue
        counts = lane_counts(tags, direction)
        if counts is None:
            exclusion_counts["unresolved_lane_count_or_directional_components"] += 1
            continue
        node_ids = [int(node_id) for node_id in value["nodes"]]
        if len(node_ids) < 2 or any(node_id not in nodes for node_id in node_ids):
            exclusion_counts["missing_or_short_geometry"] += 1
            continue
        points = [transformer.transform(*nodes[node_id]) for node_id in node_ids]
        assignments = [
            choose_zone((first[0] + second[0]) * 0.5, (first[1] + second[1]) * 0.5, zones)
            for first, second in zip(points, points[1:])
        ]
        for zone_id, first_segment, segment_end in contiguous_ranges(assignments):
            sub_points = points[first_segment : segment_end + 1]
            sub_nodes = node_ids[first_segment : segment_end + 1]
            if polyline_length(sub_points) < 40.0:
                exclusion_counts["selected_fragment_under_40m"] += 1
                continue
            for travel_direction, lane_total in (("forward", counts[0]), ("reverse", counts[1])):
                if lane_total <= 0:
                    continue
                oriented_points = sub_points if travel_direction == "forward" else list(reversed(sub_points))
                oriented_nodes = sub_nodes if travel_direction == "forward" else list(reversed(sub_nodes))
                two_way = counts[0] > 0 and counts[1] > 0
                for lane_index in range(lane_total):
                    offset = (
                        -(lane_index + 0.5) * LANE_WIDTH_METERS
                        if two_way
                        else (lane_index - (lane_total - 1) * 0.5) * LANE_WIDTH_METERS
                    )
                    lane_points = offset_polyline(oriented_points, offset)
                    length = polyline_length([(point[0], point[1]) for point in lane_points])
                    edge_id = (
                        f"osm:{way_id}:{first_segment}-{segment_end}:"
                        f"{travel_direction}:lane-{lane_index}"
                    )
                    chains.append(
                        {
                            "access_authority": access_reason,
                            "direction": travel_direction,
                            "direction_authority": direction_authority,
                            "edge_id": edge_id,
                            "from_node_id": f"osm:{oriented_nodes[0]}",
                            "lane_count_by_direction": {"forward": counts[0], "reverse": counts[1]},
                            "lane_index_from_right": lane_index,
                            "lane_width_meters": LANE_WIDTH_METERS,
                            "length_meters": round(length, 3),
                            "name": tags.get("name"),
                            "points_xz_meters": lane_points,
                            "road_class": road_class,
                            "source_id": "osm_bounded_roads",
                            "source_way_id": way_id,
                            "speed_limit_mps": parse_speed_mps(tags, road_class),
                            "to_node_id": f"osm:{oriented_nodes[-1]}",
                            "zone_id": zone_id,
                        }
                    )
    chains.sort(key=lambda value: value["edge_id"])
    by_zone = Counter(value["zone_id"] for value in chains)
    by_class = Counter(value["road_class"] for value in chains)
    if any(by_zone[zone["id"]] == 0 for zone in zones):
        raise Gate1Error(f"A required walking-skeleton zone has no directed lanes: {dict(by_zone)}")
    if not any(
        value["zone_id"] == "divided_highway"
        and value["road_class"] in {"motorway", "trunk", "primary"}
        and sum(value["lane_count_by_direction"].values()) >= 2
        for value in chains
    ):
        raise Gate1Error("Divided-highway zone has no suitable multi-lane major-road evidence")
    bounds = freeze["coordinate_contract"]["bounds_xz_meters"]
    for chain in chains:
        for x, z in chain["points_xz_meters"]:
            if not (bounds[0] <= x <= bounds[2] and bounds[1] <= z <= bounds[3]):
                raise Gate1Error(f"Lane edge {chain['edge_id']} escapes the accepted map bounds")
    return {
        "accepted_source_sha": freeze["accepted_source_sha"],
        "benchmark_camera_contract_sha256": camera_hash,
        "bounds_xz_meters": bounds,
        "directed_lane_chains": chains,
        "exclusion_counts": dict(sorted(exclusion_counts.items())),
        "lane_chain_count_by_class": dict(sorted(by_class.items())),
        "lane_chain_count_by_zone": dict(sorted(by_zone.items())),
        "osm_duplicate_occurrences_deduplicated": duplicate_occurrences,
        "schema": SCHEMA,
        "source_freeze_sha256": expected_freeze_sha256,
        "status": "GATE1_WALKING_SKELETON_ONLY",
        "turn_authority_status": "DEFERRED_GATE2",
        "zones": zones,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--external-run", type=Path, required=True)
    parser.add_argument("--camera-contract", type=Path, required=True)
    parser.add_argument("--expected-freeze-sha256", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        started = time.perf_counter()
        result = compile_lanes(
            args.external_run.resolve(),
            args.camera_contract.resolve(),
            args.expected_freeze_sha256,
        )
        write_json(args.output.resolve(), result)
        print(
            canonical_json_bytes(
                {
                    "elapsed_seconds": round(time.perf_counter() - started, 6),
                    "lane_chains": len(result["directed_lane_chains"]),
                    "output": str(args.output.resolve()),
                    "sha256": sha256_file(args.output.resolve()),
                    "status": "PASS_GATE1_LANE_COMPILE",
                }
            ).decode("utf-8"),
            end="",
        )
        return 0
    except (Gate1Error, OSError, ValueError, KeyError, json.JSONDecodeError) as error:
        print(canonical_json_bytes({"error": str(error), "status": "FAIL_CLOSED"}).decode(), end="")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
