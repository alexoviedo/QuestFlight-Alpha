#!/usr/bin/env python3
"""Validate traffic provenance, lanes, terrain grounding, and runtime budgets."""

from __future__ import annotations

import json
import math
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / "QuestFlight/Assets/Production/Environment/TrafficV1/Data/KBDU_TrafficManifestV1.json"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def desired_gap(vehicle: dict, speed: float, closing_speed: float) -> float:
    denominator = 2.0 * math.sqrt(
        max(
            0.001,
            vehicle["maximum_acceleration_mps2"]
            * vehicle["comfortable_deceleration_mps2"],
        )
    )
    dynamic = (
        max(0.0, speed) * vehicle["time_headway_seconds"]
        + max(0.0, speed) * closing_speed / denominator
    )
    return vehicle["minimum_gap_meters"] + max(0.0, dynamic)


def following_acceleration(
    vehicle: dict,
    speed: float,
    leader_speed: float,
    desired_speed: float,
    gap: float,
) -> float:
    normalized = max(0.0, speed) / max(0.1, desired_speed)
    target_gap = desired_gap(vehicle, speed, speed - leader_speed)
    acceleration = vehicle["maximum_acceleration_mps2"] * (
        1.0 - normalized**4 - (target_gap / max(0.05, gap)) ** 2
    )
    return max(
        -vehicle["emergency_deceleration_mps2"],
        min(vehicle["maximum_acceleration_mps2"], acceleration),
    )


def simulate_car_following(data: dict, duration_seconds: float = 120.0) -> dict:
    vehicles = data["moving"]
    routes = data["routes"]
    step = data["runtime_contract"]["simulation_step_seconds"]
    distances = [item["initial_distance_meters"] for item in vehicles]
    speeds = [
        routes[item["route_index"]]["speed_mps"] * item["desired_speed_scale"]
        for item in vehicles
    ]
    accelerations = [0.0] * len(vehicles)
    route_groups: dict[int, list[int]] = {}
    for index, vehicle in enumerate(vehicles):
        route_groups.setdefault(vehicle["route_index"], []).append(index)
    for indices in route_groups.values():
        indices.sort(key=lambda index: distances[index])

    initial_gaps: dict[tuple[int, int], float] = {}
    minimum_gap = float("inf")
    for route_index, indices in route_groups.items():
        if len(indices) <= 1:
            continue
        length = routes[route_index]["length_meters"]
        for offset, follower_index in enumerate(indices):
            leader_index = indices[(offset + 1) % len(indices)]
            center_gap = (distances[leader_index] - distances[follower_index]) % length
            gap = center_gap - 0.5 * (
                vehicles[follower_index]["vehicle_length_meters"]
                + vehicles[leader_index]["vehicle_length_meters"]
            )
            initial_gaps[(follower_index, leader_index)] = gap
            minimum_gap = min(minimum_gap, gap)

    for _ in range(round(duration_seconds / step)):
        proposed_speeds = [0.0] * len(vehicles)
        proposed_advances = [0.0] * len(vehicles)
        for route_index, indices in route_groups.items():
            route = routes[route_index]
            for offset, vehicle_index in enumerate(indices):
                vehicle = vehicles[vehicle_index]
                speed = speeds[vehicle_index]
                target_speed = route["speed_mps"] * vehicle["desired_speed_scale"]
                if len(indices) <= 1:
                    normalized = max(0.0, speed) / max(0.1, target_speed)
                    target_acceleration = vehicle["maximum_acceleration_mps2"] * (
                        1.0 - normalized**4
                    )
                else:
                    leader_index = indices[(offset + 1) % len(indices)]
                    leader = vehicles[leader_index]
                    center_gap = (
                        distances[leader_index] - distances[vehicle_index]
                    ) % route["length_meters"]
                    gap = max(
                        0.05,
                        center_gap
                        - 0.5
                        * (
                            vehicle["vehicle_length_meters"]
                            + leader["vehicle_length_meters"]
                        ),
                    )
                    target_acceleration = following_acceleration(
                        vehicle,
                        speed,
                        speeds[leader_index],
                        target_speed,
                        gap,
                    )
                response = max(step, vehicle["acceleration_response_seconds"])
                maximum_delta = (
                    vehicle["maximum_acceleration_mps2"]
                    + vehicle["emergency_deceleration_mps2"]
                ) / response * step
                delta = max(
                    -maximum_delta,
                    min(maximum_delta, target_acceleration - accelerations[vehicle_index]),
                )
                acceleration = accelerations[vehicle_index] + delta
                proposed_speeds[vehicle_index] = max(0.0, speed + acceleration * step)
                proposed_advances[vehicle_index] = proposed_speeds[vehicle_index] * step

            for _ in range(len(indices) if len(indices) > 1 else 0):
                changed = False
                for offset, vehicle_index in enumerate(indices):
                    leader_index = indices[(offset + 1) % len(indices)]
                    vehicle = vehicles[vehicle_index]
                    leader = vehicles[leader_index]
                    center_gap = (
                        distances[leader_index] - distances[vehicle_index]
                    ) % route["length_meters"]
                    gap = center_gap - 0.5 * (
                        vehicle["vehicle_length_meters"]
                        + leader["vehicle_length_meters"]
                    )
                    maximum_advance = max(
                        0.0,
                        gap
                        - vehicle["minimum_gap_meters"]
                        + proposed_advances[leader_index],
                    )
                    if proposed_advances[vehicle_index] > maximum_advance + 0.0001:
                        proposed_advances[vehicle_index] = maximum_advance
                        proposed_speeds[vehicle_index] = maximum_advance / step
                        changed = True
                if not changed:
                    break

        for index, vehicle in enumerate(vehicles):
            previous_speed = speeds[index]
            route_length = routes[vehicle["route_index"]]["length_meters"]
            distances[index] = (
                distances[index] + proposed_advances[index]
            ) % route_length
            speeds[index] = proposed_speeds[index]
            accelerations[index] = max(
                -vehicle["emergency_deceleration_mps2"],
                min(
                    vehicle["maximum_acceleration_mps2"],
                    (speeds[index] - previous_speed) / step,
                ),
            )

        for route_index, indices in route_groups.items():
            if len(indices) <= 1:
                continue
            length = routes[route_index]["length_meters"]
            for offset, follower_index in enumerate(indices):
                leader_index = indices[(offset + 1) % len(indices)]
                center_gap = (
                    distances[leader_index] - distances[follower_index]
                ) % length
                gap = center_gap - 0.5 * (
                    vehicles[follower_index]["vehicle_length_meters"]
                    + vehicles[leader_index]["vehicle_length_meters"]
                )
                minimum_gap = min(minimum_gap, gap)

    changed_gaps = 0
    for (follower_index, leader_index), initial_gap in initial_gaps.items():
        route_index = vehicles[follower_index]["route_index"]
        center_gap = (
            distances[leader_index] - distances[follower_index]
        ) % routes[route_index]["length_meters"]
        final_gap = center_gap - 0.5 * (
            vehicles[follower_index]["vehicle_length_meters"]
            + vehicles[leader_index]["vehicle_length_meters"]
        )
        if abs(final_gap - initial_gap) >= 1.0:
            changed_gaps += 1
    return {
        "duration_seconds": duration_seconds,
        "minimum_bumper_gap_meters": minimum_gap,
        "changed_following_gaps": changed_gaps,
        "final_speed_min_mps": min(speeds),
        "final_speed_max_mps": max(speeds),
    }


def main() -> None:
    data = json.loads(MANIFEST.read_text(encoding="utf-8"))
    require(data["schema"] == "questflight.kbdu-traffic-v1", "schema mismatch")
    require(data["status"] == "STREAMED_MAJOR_ROAD_NETWORK", "status mismatch")
    require(len(data["parked"]) == 40, "expected 40 photographed parked cars")
    require(len(data["routes"]) >= 300, "major-road route network is too sparse")
    require(len(data["moving"]) >= 600, "streamed traffic population is too sparse")
    require(
        data["runtime_contract"]["representation"]
        == "DETAIL_NEAR_FIXED_PROXY_FAR",
        "traffic LOD representation changed",
    )
    require(data["runtime_contract"]["maximum_near_instances"] == 96, "near instance budget changed")
    require(data["runtime_contract"]["maximum_far_instances"] == 900, "far instance budget changed")
    require(data["runtime_contract"]["near_enter_distance_meters"] == 800.0, "near enter distance changed")
    require(data["runtime_contract"]["near_exit_distance_meters"] == 1000.0, "near exit distance changed")
    require(
        data["runtime_contract"]["visibility_distance_policy"]
        == "FULL_COMPILED_NETWORK",
        "hard traffic visibility boundary reintroduced",
    )
    require(data["runtime_contract"]["maximum_draw_calls"] == 8, "draw budget changed")
    require(data["runtime_contract"]["maximum_visible_triangles"] == 130000, "triangle budget changed")
    require(data["runtime_contract"]["per_vehicle_gameobjects"] == 0, "per-car objects forbidden")
    require(data["runtime_contract"]["realtime_vehicle_shadows"] == 0, "realtime shadows forbidden")
    require(
        data["runtime_contract"]["car_following_model"]
        == "IDM_INSPIRED_DETERMINISTIC",
        "car-following model mismatch",
    )
    require(
        data["runtime_contract"]["simulation_step_seconds"] == 0.1,
        "traffic simulation must remain fixed at 10 Hz",
    )

    ids = set()
    for parked in data["parked"]:
        require(parked["id"] not in ids, "duplicate parked id")
        ids.add(parked["id"])
        require(0 <= parked["archetype"] < 6, "invalid parked archetype")
        require(0 <= parked["paint_index"] < 8, "invalid parked paint")
        require(len(parked["source_pixel"]) == 2, "parked source pixel missing")
        require(len(parked["source_axis_pixels"]) == 4, "parked source axis missing")
        require(-2000 <= parked["x"] <= 2000 and -2000 <= parked["z"] <= 2000, "parked outside near image")

    for route_index, route in enumerate(data["routes"]):
        samples = route["samples"]
        require(len(samples) >= 20, f"route {route_index} has insufficient samples")
        require(route["road_width_meters"] >= 5.5, "road too narrow for compiled two-lane traffic")
        require(
            route["lane_offset_meters"] + 1.125 + 0.35
            <= route["road_width_meters"] * 0.5 + 0.001,
            "lane leaves the vehicle-width and edge-clearance envelope",
        )
        previous = -1.0
        for sample in samples:
            require(sample["distance"] > previous, "route distance is not strictly increasing")
            previous = sample["distance"]
        require(abs(previous - route["length_meters"]) < 0.01, "route length mismatch")
        require(route["speed_mps"] <= 20.0, "route speed exceeds bounded contract")
        require(route["highway_class"] in {"trunk", "primary", "secondary", "tertiary", "residential"}, "forbidden road class")

    for moving in data["moving"]:
        require(0 <= moving["route_index"] < len(data["routes"]), "moving route index invalid")
        require(0 <= moving["archetype"] < 6, "moving archetype invalid")
        require(0 <= moving["paint_index"] < 8, "moving paint invalid")
        route = data["routes"][moving["route_index"]]
        require(0 <= moving["initial_distance_meters"] < route["length_meters"], "moving offset invalid")
        require(0.8 <= moving["desired_speed_scale"] <= 1.12, "desired speed invalid")
        require(0.9 <= moving["time_headway_seconds"] <= 2.4, "headway invalid")
        require(2.0 <= moving["minimum_gap_meters"] <= 4.0, "minimum gap invalid")
        require(0.8 <= moving["maximum_acceleration_mps2"] <= 2.4, "acceleration invalid")
        require(1.8 <= moving["comfortable_deceleration_mps2"] <= 3.0, "deceleration invalid")
        require(5.5 <= moving["emergency_deceleration_mps2"] <= 7.5, "emergency deceleration invalid")
        require(0.25 <= moving["acceleration_response_seconds"] <= 0.55, "response time invalid")
        require(4.3 <= moving["vehicle_length_meters"] <= 16.3, "vehicle length invalid")

    fleet = data["parked"] + data["moving"]
    archetype_counts = [
        sum(1 for vehicle in fleet if vehicle["archetype"] == index)
        for index in range(6)
    ]
    paint_counts = [
        sum(1 for vehicle in fleet if vehicle["paint_index"] == index)
        for index in range(8)
    ]
    require(min(archetype_counts[:5]) >= 30, "passenger body-style variation is too sparse")
    require(archetype_counts[5] >= 10, "highway semi-trailer population is too sparse")
    require(min(paint_counts) >= 5, "fleet paint variation is too sparse")
    require(
        sum(paint_counts[:4]) / len(fleet) >= 0.72,
        "neutral real-market paint colors must remain dominant",
    )
    require(
        paint_counts[4] / len(fleet) <= 0.13,
        "blue vehicles are overrepresented",
    )

    trunk_route_indices = {
        index
        for index, route in enumerate(data["routes"])
        if route["highway_class"] == "trunk"
    }
    require(
        sum(
            1
            for moving in data["moving"]
            if moving["route_index"] in trunk_route_indices
        )
        >= 140,
        "highway traffic population is too sparse",
    )
    require(
        all(
            route["direction"] == "forward"
            and route["direction_policy"]
            in {
                "SOURCE_WAY_ONEWAY_TRUNK",
                "DEDICATED_DIVIDED_HIGHWAY_TRUCK_CIRCUIT",
            }
            for route in data["routes"]
            if route["highway_class"] == "trunk"
        ),
        "each divided-highway carriageway must follow its source-way direction only",
    )
    require(
        all(
            data["routes"][moving["route_index"]]["direction_policy"]
            == "DEDICATED_DIVIDED_HIGHWAY_TRUCK_CIRCUIT"
            for moving in data["moving"]
            if moving["archetype"] == 5
        ),
        "semi-trailers must remain confined to the closed divided-highway circuit",
    )
    truck_circuits = [
        route
        for route in data["routes"]
        if route["direction_policy"]
        == "DEDICATED_DIVIDED_HIGHWAY_TRUCK_CIRCUIT"
    ]
    require(len(truck_circuits) == 1, "expected one dedicated truck circuit")
    circuit = truck_circuits[0]
    require(circuit.get("closed_loop") is True, "truck circuit is not marked closed")
    require(
        math.dist(
            (
                circuit["samples"][0]["x"],
                circuit["samples"][0]["z"],
            ),
            (
                circuit["samples"][-1]["x"],
                circuit["samples"][-1]["z"],
            ),
        )
        <= 0.01,
        "truck circuit has a visible endpoint recycle jump",
    )

    moving_by_route: dict[int, list[dict]] = {}
    for moving in data["moving"]:
        moving_by_route.setdefault(moving["route_index"], []).append(moving)
    following_gap = data["runtime_contract"]["minimum_initial_bumper_gap_meters"]
    variable_gap_routes = 0
    for route_index, vehicles in moving_by_route.items():
        if len(vehicles) < 2:
            continue
        offsets = sorted(
            vehicle["initial_distance_meters"] for vehicle in vehicles
        )
        length = data["routes"][route_index]["length_meters"]
        ordered = sorted(vehicles, key=lambda vehicle: vehicle["initial_distance_meters"])
        for index, vehicle in enumerate(ordered):
            leader = ordered[(index + 1) % len(ordered)]
            require(
                (vehicle["archetype"], vehicle["paint_index"])
                != (leader["archetype"], leader["paint_index"]),
                "an exact model/color clone follows another local vehicle",
            )
        gaps = []
        for index, vehicle in enumerate(ordered):
            leader = ordered[(index + 1) % len(ordered)]
            center_gap = (
                leader["initial_distance_meters"] - vehicle["initial_distance_meters"]
            ) % length
            gaps.append(
                center_gap
                - 0.5
                * (
                    vehicle["vehicle_length_meters"]
                    + leader["vehicle_length_meters"]
                )
            )
        require(
            min(gaps) >= following_gap - 0.01,
            "initial bumper spacing violates the collision-free contract",
        )
        if max(gaps) - min(gaps) >= 10.0:
            variable_gap_routes += 1
    require(
        variable_gap_routes >= 100,
        "traffic is still initialized as a visibly regular spacing lattice",
    )

    require(
        len(
            {
                (
                    moving["desired_speed_scale"],
                    moving["time_headway_seconds"],
                    moving["minimum_gap_meters"],
                )
                for moving in data["moving"]
            }
        )
        >= 100,
        "driver profiles lack deterministic variation",
    )
    probe = data["moving"][0]
    require(
        desired_gap(probe, 20.0, 0.0) > desired_gap(probe, 8.0, 0.0),
        "desired gap does not grow with speed",
    )
    require(
        desired_gap(probe, 15.0, 5.0) > desired_gap(probe, 15.0, 0.0),
        "desired gap does not respond to closing speed",
    )
    simulation = simulate_car_following(data)
    require(
        simulation["minimum_bumper_gap_meters"]
        >= data["runtime_contract"]["minimum_runtime_bumper_gap_meters"] - 0.001,
        "car-following simulation violated the runtime bumper gap",
    )
    require(
        simulation["changed_following_gaps"] >= 100,
        "car-following gaps remain visually static",
    )

    print(
        json.dumps(
            {
                "classification": "PASS",
                "parked": len(data["parked"]),
                "routes": len(data["routes"]),
                "dynamic": len(data["moving"]),
                "route_samples": sum(len(route["samples"]) for route in data["routes"]),
                "variable_initial_gap_routes": variable_gap_routes,
                "car_following": simulation,
            },
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
