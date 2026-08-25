#!/usr/bin/env python3
"""Author the deterministic Traffic V2 Gate-1 trajectory walking skeleton.

The output is deliberately external and non-product.  It proves the smallest
offline chain from frozen real roads through directed lane chains and timed
vehicle trajectories.  It does not claim calibrated demand, intersection-turn
authority, full-map coverage, Unity integration, or headset acceptance.
"""

from __future__ import annotations

import argparse
import bisect
import hashlib
import json
import math
import subprocess
import sys
import time
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageFont


SCHEMA = "questflight.traffic-v2.walking-skeleton-performance.v1"
REPORT_SCHEMA = "questflight.traffic-v2.walking-skeleton-report.v1"
HASH_SCHEMA = "questflight.traffic-v2.walking-skeleton-hashes.v1"
SCHEDULE_DURATION_SECONDS = 900.0
ROUTES_PER_ZONE = 4
ZONE_ORDER = ("airport", "divided_highway", "residential")
ROUTE_COLORS = (
    (20, 121, 190),
    (214, 73, 51),
    (43, 148, 87),
    (180, 109, 20),
)
CLASS_COLORS = {
    "motorway": (91, 74, 145),
    "motorway_link": (116, 99, 164),
    "trunk": (117, 83, 157),
    "trunk_link": (141, 112, 178),
    "primary": (193, 78, 70),
    "primary_link": (210, 111, 98),
    "secondary": (216, 139, 50),
    "secondary_link": (229, 166, 90),
    "tertiary": (194, 174, 66),
    "tertiary_link": (213, 198, 106),
    "residential": (128, 139, 150),
    "living_street": (145, 157, 168),
    "unclassified": (153, 163, 173),
}


class Gate1Error(RuntimeError):
    """A fail-closed Gate-1 trajectory or artifact error."""


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


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(canonical_json_bytes(value))


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def file_record(path: Path) -> dict[str, Any]:
    return {"bytes": path.stat().st_size, "name": path.name, "sha256": sha256_file(path)}


def polyline_metrics(points: list[list[float]]) -> tuple[list[float], float]:
    if len(points) < 2:
        raise Gate1Error("Trajectory route has fewer than two points")
    cumulative = [0.0]
    for first, second in zip(points, points[1:]):
        distance = math.dist(first, second)
        if distance <= 1e-6:
            continue
        cumulative.append(cumulative[-1] + distance)
    if len(cumulative) != len(points) or cumulative[-1] < 40.0:
        raise Gate1Error("Trajectory route contains duplicate points or is under 40 meters")
    return cumulative, cumulative[-1]


def sample_polyline(
    points: list[list[float]], cumulative: list[float], distance: float
) -> tuple[float, float, float]:
    distance = min(max(distance, 0.0), cumulative[-1])
    index = min(max(bisect.bisect_right(cumulative, distance) - 1, 0), len(points) - 2)
    first, second = points[index], points[index + 1]
    segment = cumulative[index + 1] - cumulative[index]
    ratio = 0.0 if segment <= 1e-9 else (distance - cumulative[index]) / segment
    x = first[0] + (second[0] - first[0]) * ratio
    z = first[1] + (second[1] - first[1]) * ratio
    heading = math.degrees(math.atan2(second[0] - first[0], second[1] - first[1])) % 360.0
    return x, z, heading


def angular_difference(first: float, second: float) -> float:
    return abs((first - second + 180.0) % 360.0 - 180.0)


def chain_heading(chain: dict[str, Any]) -> float:
    points = chain["points_xz_meters"]
    return math.degrees(
        math.atan2(points[-1][0] - points[0][0], points[-1][1] - points[0][1])
    ) % 360.0


def select_routes(lane_graph: dict[str, Any]) -> list[dict[str, Any]]:
    selected: list[dict[str, Any]] = []
    chains = lane_graph["directed_lane_chains"]
    for zone_id in ZONE_ORDER:
        candidates = [
            value
            for value in chains
            if value["zone_id"] == zone_id
            and value["lane_index_from_right"] == 0
            and value["length_meters"] >= 250.0
        ]
        if zone_id == "airport":
            preferred = {"Airport Boulevard", "Airport Road", "Valmont Road", "Independence Road"}
            candidates.sort(
                key=lambda value: (
                    value.get("name") not in preferred,
                    value["road_class"] not in {"secondary", "tertiary", "residential"},
                    -value["length_meters"],
                    value["edge_id"],
                )
            )
        elif zone_id == "residential":
            candidates.sort(
                key=lambda value: (
                    value["road_class"] != "residential",
                    value.get("name") is None,
                    -value["length_meters"],
                    value["edge_id"],
                )
            )
        else:
            major = [
                value
                for value in candidates
                if value["road_class"] in {"motorway", "trunk", "primary"}
                and "Diagonal Highway" in str(value.get("name") or "")
            ]
            major.sort(key=lambda value: (-value["length_meters"], value["edge_id"]))
            opposing_pair: tuple[dict[str, Any], dict[str, Any]] | None = None
            for index, first in enumerate(major):
                for second in major[index + 1 :]:
                    if first["source_way_id"] == second["source_way_id"]:
                        continue
                    if angular_difference(chain_heading(first), chain_heading(second)) >= 120.0:
                        opposing_pair = (first, second)
                        break
                if opposing_pair:
                    break
            if opposing_pair is None:
                raise Gate1Error("No real opposing Diagonal Highway carriageway pair was found")
            candidates = [*opposing_pair] + [
                value for value in major if value not in opposing_pair
            ] + [value for value in candidates if value not in major]

        zone_selected: list[dict[str, Any]] = []
        seen_edges: set[str] = set()
        for candidate in candidates:
            if candidate["edge_id"] in seen_edges:
                continue
            zone_selected.append(candidate)
            seen_edges.add(candidate["edge_id"])
            if len(zone_selected) == ROUTES_PER_ZONE:
                break
        if len(zone_selected) != ROUTES_PER_ZONE:
            raise Gate1Error(f"Zone {zone_id} has only {len(zone_selected)} suitable real routes")
        selected.extend(zone_selected)
    return selected


def motion_parameters(length: float, desired_speed: float, acceleration: float, braking: float) -> dict[str, float]:
    peak = desired_speed
    acceleration_distance = peak * peak / (2.0 * acceleration)
    braking_distance = peak * peak / (2.0 * braking)
    if acceleration_distance + braking_distance > length:
        peak = math.sqrt(2.0 * length * acceleration * braking / (acceleration + braking))
        acceleration_distance = peak * peak / (2.0 * acceleration)
        braking_distance = peak * peak / (2.0 * braking)
    cruise_distance = max(0.0, length - acceleration_distance - braking_distance)
    acceleration_time = peak / acceleration
    cruise_time = cruise_distance / peak if peak > 0.0 else 0.0
    braking_time = peak / braking
    return {
        "acceleration_distance": acceleration_distance,
        "acceleration_time": acceleration_time,
        "braking": braking,
        "braking_distance": braking_distance,
        "braking_time": braking_time,
        "cruise_distance": cruise_distance,
        "cruise_time": cruise_time,
        "peak_speed": peak,
        "total_time": acceleration_time + cruise_time + braking_time,
    }


def motion_at(local_time: float, parameters: dict[str, float], acceleration: float) -> tuple[float, float]:
    acceleration_time = parameters["acceleration_time"]
    cruise_time = parameters["cruise_time"]
    peak = parameters["peak_speed"]
    if local_time <= acceleration_time:
        return 0.5 * acceleration * local_time * local_time, acceleration * local_time
    if local_time <= acceleration_time + cruise_time:
        elapsed = local_time - acceleration_time
        return parameters["acceleration_distance"] + peak * elapsed, peak
    elapsed = min(local_time - acceleration_time - cruise_time, parameters["braking_time"])
    distance = (
        parameters["acceleration_distance"]
        + parameters["cruise_distance"]
        + peak * elapsed
        - 0.5 * parameters["braking"] * elapsed * elapsed
    )
    return distance, max(0.0, peak - parameters["braking"] * elapsed)


def build_trajectory(
    chain: dict[str, Any],
    route_index: int,
    start_time: float,
    vehicle_class: str,
) -> dict[str, Any]:
    points = chain["points_xz_meters"]
    cumulative, length = polyline_metrics(points)
    factors = (0.82, 0.91, 0.76, 0.87)
    heavy = vehicle_class == "heavy_truck"
    desired = min(chain["speed_limit_mps"] * factors[route_index % len(factors)], 22.0 if heavy else 27.0)
    acceleration = 0.9 if heavy else 1.8
    braking = 1.1 if heavy else 2.1
    parameters = motion_parameters(length, desired, acceleration, braking)
    duration = parameters["total_time"]
    keyframes = []
    local_times = [float(value) for value in range(int(math.floor(duration)) + 1)]
    if not math.isclose(local_times[-1], duration, abs_tol=1e-6):
        local_times.append(duration)
    for local_time in local_times:
        distance, speed = motion_at(local_time, parameters, acceleration)
        x, z, heading = sample_polyline(points, cumulative, distance)
        keyframes.append(
            {
                "distance_meters": round(min(distance, length), 3),
                "heading_degrees": round(heading, 3),
                "speed_mps": round(speed, 3),
                "time_seconds": round(start_time + local_time, 3),
                "x_meters": round(x, 3),
                "z_meters": round(z, 3),
            }
        )
    return {
        "active_interval_seconds": [round(start_time, 3), round(start_time + duration, 3)],
        "driver_profile": {
            "acceleration_mps2": acceleration,
            "comfortable_braking_mps2": braking,
            "desired_speed_factor": factors[route_index % len(factors)],
        },
        "keyframes": keyframes,
        "loop_behavior": "NO_WRAP_SINGLE_ORIGIN_TO_DESTINATION_TRIP",
        "persistent_vehicle_id": f"gate1:{chain['zone_id']}:{route_index:02d}",
        "route": {
            "edge_ids": [chain["edge_id"]],
            "end_node_id": chain["to_node_id"],
            "length_meters": round(length, 3),
            "road_class": chain["road_class"],
            "road_name": chain.get("name"),
            "source_way_ids": [chain["source_way_id"]],
            "start_node_id": chain["from_node_id"],
        },
        "vehicle_class": vehicle_class,
        "zone_id": chain["zone_id"],
    }


def interpolate_trajectory(trajectory: dict[str, Any], time_seconds: float) -> tuple[float, float] | None:
    start, end = trajectory["active_interval_seconds"]
    if time_seconds < start or time_seconds > end:
        return None
    keyframes = trajectory["keyframes"]
    times = [value["time_seconds"] for value in keyframes]
    index = min(max(bisect.bisect_right(times, time_seconds) - 1, 0), len(keyframes) - 2)
    first, second = keyframes[index], keyframes[index + 1]
    span = second["time_seconds"] - first["time_seconds"]
    ratio = 0.0 if span <= 1e-9 else (time_seconds - first["time_seconds"]) / span
    return (
        first["x_meters"] + (second["x_meters"] - first["x_meters"]) * ratio,
        first["z_meters"] + (second["z_meters"] - first["z_meters"]) * ratio,
    )


def minimum_sampled_separation(trajectories: list[dict[str, Any]]) -> dict[str, Any]:
    maximum_time = max(value["active_interval_seconds"][1] for value in trajectories)
    minimum = math.inf
    pair: list[str] | None = None
    sample_time = 0.0
    while sample_time <= maximum_time + 1e-6:
        active = []
        for trajectory in trajectories:
            position = interpolate_trajectory(trajectory, sample_time)
            if position is not None:
                active.append((trajectory["persistent_vehicle_id"], position))
        for index, first in enumerate(active):
            for second in active[index + 1 :]:
                distance = math.dist(first[1], second[1])
                if distance < minimum:
                    minimum = distance
                    pair = [first[0], second[0]]
        sample_time += 0.5
    return {
        "minimum_center_separation_meters": None if math.isinf(minimum) else round(minimum, 3),
        "minimum_pair": pair,
        "sample_interval_seconds": 0.5,
        "status": "PASS_WALKING_SKELETON_SAMPLE" if math.isinf(minimum) or minimum >= 8.0 else "FAIL",
    }


def render_overlay(
    lane_graph: dict[str, Any], selected: list[dict[str, Any]], output_path: Path
) -> None:
    width, height = 1800, 700
    image = Image.new("RGB", (width, height), (245, 247, 250))
    draw = ImageDraw.Draw(image)
    font = ImageFont.load_default()
    draw.text((20, 14), "TRAFFIC V2 GATE 1 - REAL FROZEN ROADS / DIRECTED LANES / TIMED ROUTES", fill=(22, 31, 44), font=font)
    draw.text((20, 31), "WALKING SKELETON ONLY - NOT FULL-MAP, UNITY, DEMAND, COLLISION, OR HEADSET ACCEPTANCE", fill=(155, 40, 38), font=font)
    zones = {value["id"]: value for value in lane_graph["zones"]}
    selected_ids = {value["edge_id"] for value in selected}
    for panel_index, zone_id in enumerate(ZONE_ORDER):
        left = 15 + panel_index * 595
        top = 62
        panel_width, panel_height = 575, 620
        draw.rectangle((left, top, left + panel_width, top + panel_height), fill=(255, 255, 255), outline=(78, 89, 102), width=2)
        zone = zones[zone_id]
        cx, cz = zone["center_xz_meters"]
        radius = float(zone["radius_meters"])
        scale = min((panel_width - 30) / (2.0 * radius), (panel_height - 70) / (2.0 * radius))

        def screen(point: list[float]) -> tuple[float, float]:
            return (
                left + panel_width * 0.5 + (point[0] - cx) * scale,
                top + 35 + (panel_height - 55) * 0.5 - (point[1] - cz) * scale,
            )

        zone_chains = [value for value in lane_graph["directed_lane_chains"] if value["zone_id"] == zone_id]
        for chain in zone_chains:
            points = [screen(point) for point in chain["points_xz_meters"]]
            color = CLASS_COLORS.get(chain["road_class"], (170, 176, 183))
            draw.line(points, fill=color, width=1)
        highlighted = [value for value in selected if value["zone_id"] == zone_id]
        for route_index, chain in enumerate(highlighted):
            points = [screen(point) for point in chain["points_xz_meters"]]
            color = ROUTE_COLORS[route_index % len(ROUTE_COLORS)]
            draw.line(points, fill=color, width=5)
            middle = max(0, min(len(points) - 2, len(points) // 2 - 1))
            first, second = points[middle], points[middle + 1]
            angle = math.atan2(second[1] - first[1], second[0] - first[0])
            tip = second
            size = 8.0
            arrow = [
                tip,
                (tip[0] - size * math.cos(angle - 0.55), tip[1] - size * math.sin(angle - 0.55)),
                (tip[0] - size * math.cos(angle + 0.55), tip[1] - size * math.sin(angle + 0.55)),
            ]
            draw.polygon(arrow, fill=color)
        draw.text((left + 10, top + 9), f"{zone_id.replace('_', ' ').upper()}  lanes={len(zone_chains)}  selected={len(highlighted)}", fill=(20, 29, 40), font=font)
        label_y = top + panel_height - 46
        for route_index, chain in enumerate(highlighted):
            label = f"{route_index + 1}: {chain.get('name') or '<unnamed>'} / {chain['road_class']} / {chain['direction']}"
            draw.text((left + 10, label_y + route_index * 10), label, fill=ROUTE_COLORS[route_index], font=font)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(output_path, format="PNG", compress_level=9, optimize=False)


def compile_gate1(
    repo_root: Path,
    external_run: Path,
    output_dir: Path,
    expected_freeze_sha256: str,
) -> dict[str, Any]:
    lane_script = repo_root / "tools/traffic_v2/geospatial/compile_walking_skeleton_lanes.py"
    camera_contract = repo_root / "tools/traffic_v2/schemas/traffic_v2_benchmark_camera_contract_v1.json"
    lane_path = output_dir / "traffic_v2_gate1_lane_graph.json"
    manifest_path = output_dir / "traffic_v2_gate1_trajectory_manifest.json"
    overlay_path = output_dir / "traffic_v2_gate1_overlay.png"
    report_path = output_dir / "traffic_v2_gate1_report.json"
    hashes_path = output_dir / "traffic_v2_gate1_hashes.json"
    output_dir.mkdir(parents=True, exist_ok=True)
    command = [
        sys.executable,
        "-I",
        "-B",
        str(lane_script),
        "--external-run",
        str(external_run),
        "--camera-contract",
        str(camera_contract),
        "--expected-freeze-sha256",
        expected_freeze_sha256,
        "--output",
        str(lane_path),
    ]
    process = subprocess.run(command, capture_output=True, text=True, timeout=60, check=False)
    if process.returncode != 0:
        raise Gate1Error(f"Lane compiler failed: {process.stdout}{process.stderr}")
    lane_graph = read_json(lane_path)
    selected = select_routes(lane_graph)
    trajectories = []
    zone_cursors = {zone: 0.0 for zone in ZONE_ORDER}
    class_cycle = ("passenger_sedan", "passenger_suv", "pickup", "passenger_sedan")
    zone_route_indices = {zone: 0 for zone in ZONE_ORDER}
    for chain in selected:
        zone = chain["zone_id"]
        route_index = zone_route_indices[zone]
        vehicle_class = (
            "heavy_truck"
            if zone == "divided_highway" and route_index == ROUTES_PER_ZONE - 1
            else class_cycle[route_index]
        )
        trajectory = build_trajectory(chain, route_index, zone_cursors[zone], vehicle_class)
        zone_cursors[zone] = trajectory["active_interval_seconds"][1] + 5.0
        zone_route_indices[zone] += 1
        trajectories.append(trajectory)
    if max(zone_cursors.values()) > SCHEDULE_DURATION_SECONDS:
        raise Gate1Error("Walking-skeleton trips do not fit inside the non-looping 15-minute clock")
    separation = minimum_sampled_separation(trajectories)
    if separation["status"] != "PASS_WALKING_SKELETON_SAMPLE":
        raise Gate1Error(f"Sampled walking-skeleton vehicle separation failed: {separation}")
    manifest = {
        "accepted_source_sha": lane_graph["accepted_source_sha"],
        "lane_graph": file_record(lane_path),
        "limitations": [
            "GATE1_OFFLINE_WALKING_SKELETON_ONLY",
            "NOT_DEMAND_CALIBRATED",
            "NO_INTERSECTION_TURN_AUTHORITY",
            "NO_UNITY_RUNTIME_OR_RENDERING_CLAIM",
            "NO_CONTINUOUS_SWEPT_COLLISION_CLAIM",
            "NO_FULL_MAP_COVERAGE_CLAIM",
            "NO_HEADSET_OR_PERFORMANCE_CLAIM",
        ],
        "model_bindings_status": "DEFERRED_GATE2",
        "schedule_duration_seconds": SCHEDULE_DURATION_SECONDS,
        "schedule_looping": False,
        "schema": SCHEMA,
        "source_freeze_sha256": expected_freeze_sha256,
        "status": "GATE1_WALKING_SKELETON_ONLY",
        "trajectories": trajectories,
    }
    write_json(manifest_path, manifest)
    render_overlay(lane_graph, selected, overlay_path)
    report = {
        "artifacts": {
            "lane_graph": file_record(lane_path),
            "overlay": file_record(overlay_path),
            "trajectory_manifest": file_record(manifest_path),
        },
        "checks": {
            "all_routes_bind_one_real_osm_way": all(
                len(value["route"]["source_way_ids"]) == 1 for value in trajectories
            ),
            "all_routes_have_no_wrap": all(
                value["loop_behavior"] == "NO_WRAP_SINGLE_ORIGIN_TO_DESTINATION_TRIP"
                for value in trajectories
            ),
            "directed_lane_chain_count": len(lane_graph["directed_lane_chains"]),
            "osm_duplicate_occurrences_deduplicated": lane_graph[
                "osm_duplicate_occurrences_deduplicated"
            ],
            "opposing_highway_heading_difference_degrees": round(
                angular_difference(chain_heading(selected[4]), chain_heading(selected[5])), 3
            ),
            "sampled_vehicle_separation": separation,
            "selected_route_count_by_zone": {
                zone: sum(value["zone_id"] == zone for value in trajectories) for zone in ZONE_ORDER
            },
            "trajectory_count": len(trajectories),
        },
        "lane_compiler_source_sha256": sha256_file(lane_script),
        "limitations": manifest["limitations"],
        "performance_compiler_source_sha256": sha256_file(Path(__file__)),
        "schema": REPORT_SCHEMA,
        "status": "PASS_GATE1_OFFLINE_ARTIFACT",
    }
    if report["checks"]["opposing_highway_heading_difference_degrees"] < 120.0:
        raise Gate1Error("Selected divided-highway routes are not an opposing carriageway pair")
    if not all(
        value is True
        for key, value in report["checks"].items()
        if key.startswith("all_routes_")
    ):
        raise Gate1Error("A route-source or no-wrap invariant failed")
    write_json(report_path, report)
    hashes = {
        "files": [
            file_record(path)
            for path in (lane_path, manifest_path, overlay_path, report_path)
        ],
        "schema": HASH_SCHEMA,
        "status": "PASS_ALL_GATE1_ARTIFACTS_BOUND",
    }
    write_json(hashes_path, hashes)
    return {
        "files": [*hashes["files"], file_record(hashes_path)],
        "lane_chains": len(lane_graph["directed_lane_chains"]),
        "opposing_highway_heading_difference_degrees": report["checks"][
            "opposing_highway_heading_difference_degrees"
        ],
        "sampled_minimum_separation_meters": separation["minimum_center_separation_meters"],
        "status": "PASS_GATE1_OFFLINE_ARTIFACT",
        "trajectories": len(trajectories),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, required=True)
    parser.add_argument("--external-run", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--expected-freeze-sha256", required=True)
    args = parser.parse_args()
    started = time.perf_counter()
    try:
        result = compile_gate1(
            args.repo_root.resolve(),
            args.external_run.resolve(),
            args.output_dir.resolve(),
            args.expected_freeze_sha256,
        )
        result["elapsed_seconds"] = round(time.perf_counter() - started, 6)
        print(canonical_json_bytes(result).decode(), end="")
        return 0
    except (Gate1Error, OSError, ValueError, KeyError, json.JSONDecodeError, subprocess.SubprocessError) as error:
        print(canonical_json_bytes({"error": str(error), "status": "FAIL_CLOSED"}).decode(), end="")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
