#!/usr/bin/env python3
"""Compile bounded KBDU parking and road evidence into a Quest traffic manifest."""

from __future__ import annotations

import base64
import hashlib
import json
import math
import struct
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SOURCES = Path(__file__).with_name("traffic_sources_v1.json")
TERRAIN = ROOT / "QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_terrain_rings.json"
OUTPUT = ROOT / "QuestFlight/Assets/Production/Environment/TrafficV1/Data/KBDU_TrafficManifestV1.json"
SAMPLE_SPACING_METERS = 5.0
GROUND_CLEARANCE_METERS = 0.035
SELECTED_SPEEDS_MPS = {
    "Diagonal Highway": 19.0,
    "Valmont Road": 14.0,
    "Airport Boulevard": 9.0,
    "Jay Road": 13.0,
    "55th Street": 12.0,
    "North 61st Street": 12.0,
}
DEFAULT_SPEEDS_MPS = {
    "trunk": 20.0,
    "primary": 17.0,
    "secondary": 14.0,
    "tertiary": 11.0,
    "residential": 8.0,
}
FLEET_STYLE_WEIGHTS = [0.24, 0.14, 0.34, 0.20, 0.08]
PAINT_COLOR_WEIGHTS = [0.26, 0.22, 0.19, 0.14, 0.09, 0.07, 0.02, 0.01]


def deterministic_unit(route_index: int, vehicle_index: int, salt: int) -> float:
    """Return a stable [0, 1] driver trait without runtime randomness."""
    value = (
        (route_index + 1) * 73856093
        ^ (vehicle_index + 1) * 19349663
        ^ (salt + 1) * 83492791
    ) & 0xFFFFFFFF
    value ^= value >> 16
    value = (value * 0x7FEB352D) & 0xFFFFFFFF
    value ^= value >> 15
    value = (value * 0x846CA68B) & 0xFFFFFFFF
    value ^= value >> 16
    return (value & 0x00FFFFFF) / float(0x01000000)


def profile_value(bounds: list[float], unit: float) -> float:
    return float(bounds[0]) + (float(bounds[1]) - float(bounds[0])) * unit


def weighted_index(weights: list[float], unit: float) -> int:
    total = sum(weights)
    threshold = max(0.0, min(0.999999, unit)) * total
    cumulative = 0.0
    for index, weight in enumerate(weights):
        cumulative += weight
        if threshold < cumulative:
            return index
    return len(weights) - 1


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


class TerrainSampler:
    def __init__(self, terrain: dict):
        self.origin = float(terrain["origin"]["elevation_msl_meters"])
        quantization = float(terrain["height_quantization_meters"])
        self.layers = []
        for layer in sorted(terrain["layers"], key=lambda value: int(value["priority"])):
            payload = base64.b64decode(layer["height_dm_little_endian_base64"], validate=True)
            count = int(layer["width"]) * int(layer["height"])
            values = struct.unpack("<" + "h" * count, payload)
            heights = [self.origin + value * quantization for value in values]
            self.layers.append((layer, heights))

    def sample(self, x: float, z: float) -> float:
        for layer, values in self.layers:
            if not (
                float(layer["min_x_meters"]) <= x <= float(layer["max_x_meters"])
                and float(layer["min_z_meters"]) <= z <= float(layer["max_z_meters"])
            ):
                continue
            if layer["kind"] == "ring" and max(abs(x), abs(z)) < float(
                layer.get("inner_radius_meters", 0.0)
            ):
                continue
            spacing = float(layer["spacing_meters"])
            fx = (x - float(layer["min_x_meters"])) / spacing
            fz = (z - float(layer["min_z_meters"])) / spacing
            x0 = max(0, min(int(layer["width"]) - 2, math.floor(fx)))
            z0 = max(0, min(int(layer["height"]) - 2, math.floor(fz)))
            tx = max(0.0, min(1.0, fx - x0))
            tz = max(0.0, min(1.0, fz - z0))
            width = int(layer["width"])
            a = values[z0 * width + x0]
            b = values[z0 * width + x0 + 1]
            c = values[(z0 + 1) * width + x0]
            d = values[(z0 + 1) * width + x0 + 1]
            return (
                (a * (1.0 - tx) + b * tx) * (1.0 - tz)
                + (c * (1.0 - tx) + d * tx) * tz
                - self.origin
            )
        raise ValueError(f"No terrain layer contains ({x}, {z})")


def pixel_to_world(pixel: list[int], orthophoto: dict) -> tuple[float, float]:
    width, height = orthophoto["dimensions_pixels"]
    min_x, min_z, max_x, max_z = orthophoto["world_bounds_meters"]
    column, row = pixel
    return (
        min_x + column / width * (max_x - min_x),
        max_z - row / height * (max_z - min_z),
    )


def heading_from_axis(axis: list[int]) -> float:
    east = float(axis[0] - axis[2])
    north = float(axis[3] - axis[1])
    if abs(east) + abs(north) < 1.0:
        raise ValueError("Vehicle source axis is degenerate")
    return math.degrees(math.atan2(east, north)) % 360.0


def resample(points: list[tuple[float, float]], spacing: float) -> list[tuple[float, float]]:
    output = [points[0]]
    carry = 0.0
    for start, end in zip(points, points[1:]):
        dx = end[0] - start[0]
        dz = end[1] - start[1]
        length = math.hypot(dx, dz)
        if length < 0.001:
            continue
        distance = spacing - carry if carry > 0.0 else spacing
        while distance < length:
            t = distance / length
            output.append((start[0] + dx * t, start[1] + dz * t))
            distance += spacing
        carry = max(0.0, length - (distance - spacing))
    if math.dist(output[-1], points[-1]) > 0.25:
        output.append(points[-1])
    return output


def offset_polyline(points: list[tuple[float, float]], offset: float) -> list[tuple[float, float]]:
    result = []
    for index, point in enumerate(points):
        previous = points[max(0, index - 1)]
        following = points[min(len(points) - 1, index + 1)]
        dx = following[0] - previous[0]
        dz = following[1] - previous[1]
        length = max(0.001, math.hypot(dx, dz))
        result.append((point[0] - dz / length * offset, point[1] + dx / length * offset))
    return result


def lane_offset_for_road(road: dict, contract: dict) -> float:
    width = float(road["render_width_meters"])
    requested = max(
        float(contract["minimum_meters"]),
        min(
            float(contract["maximum_meters"]),
            width * float(contract["road_width_fraction"]),
        ),
    )
    edge_safe = (
        width * 0.5
        - float(contract["vehicle_half_width_meters"])
        - float(contract["edge_clearance_meters"])
    )
    if edge_safe < float(contract["minimum_meters"]):
        raise ValueError(
            f"Road {road['source']['osm_id']} is too narrow for safe traffic"
        )
    return min(requested, edge_safe)


def route_record(
    stable_id: str,
    road: dict,
    direction: int,
    lane_offset_meters: float,
    lane_points: list[tuple[float, float]],
    sampler: TerrainSampler,
) -> dict:
    points = lane_points if direction > 0 else list(reversed(lane_points))
    samples = []
    cumulative = 0.0
    prior = None
    for x, z in points:
        if prior is not None:
            cumulative += math.dist(prior, (x, z))
        samples.append(
            {
                "x": round(x, 3),
                "y": round(sampler.sample(x, z) + GROUND_CLEARANCE_METERS, 3),
                "z": round(z, 3),
                "distance": round(cumulative, 3),
            }
        )
        prior = (x, z)
    road_name = road["tags"].get("name") or f"unnamed-{road['source']['osm_id']}"
    highway_class = road["tags"]["highway"]
    return {
        "id": stable_id,
        "road_name": road_name,
        "osm_way_id": int(road["source"]["osm_id"]),
        "highway_class": road["tags"]["highway"],
        "direction": "forward" if direction > 0 else "reverse",
        "lane_offset_meters": round(lane_offset_meters, 3),
        "road_width_meters": float(road["render_width_meters"]),
        "speed_mps": SELECTED_SPEEDS_MPS.get(
            road_name, DEFAULT_SPEEDS_MPS[highway_class]
        ),
        "length_meters": round(cumulative, 3),
        "samples": samples,
    }


def main() -> None:
    source = json.loads(SOURCES.read_text(encoding="utf-8"))
    orthophoto = source["orthophoto"]
    orthophoto_path = ROOT / orthophoto["path"]
    if sha256(orthophoto_path) != orthophoto["sha256"]:
        raise ValueError("Active traffic orthophoto hash changed")
    if f"guid: {orthophoto['guid']}" not in Path(str(orthophoto_path) + ".meta").read_text(
        encoding="utf-8"
    ):
        raise ValueError("Active traffic orthophoto GUID changed")

    terrain_document = json.loads(TERRAIN.read_text(encoding="utf-8"))
    sampler = TerrainSampler(terrain_document)
    road_path = ROOT / source["road_source"]["path"]
    context = json.loads(road_path.read_text(encoding="utf-8"))
    quantization = float(context["coordinate_quantization_meters"])
    network = source["road_source"]["network_compile"]
    min_x, min_z, max_x, max_z = [
        float(value) for value in network["world_bounds_meters"]
    ]
    minimum_length = float(network["minimum_segment_length_meters"])
    allowed_classes = set(network["allowed_highway_classes"])
    roads = [
        feature
        for feature in context["openstreetmap"]["features"]
        if feature.get("category") == "road"
        and feature.get("geometry_type") == "polyline"
        and feature.get("tags", {}).get("highway") in allowed_classes
        and len(feature.get("points_q", [])) >= 4
    ]
    bounded_roads = []
    for road in roads:
        quantized = road["points_q"]
        center = [
            (quantized[index] * quantization, quantized[index + 1] * quantization)
            for index in range(0, len(quantized), 2)
        ]
        length = sum(math.dist(start, end) for start, end in zip(center, center[1:]))
        road_min_x = min(point[0] for point in center)
        road_max_x = max(point[0] for point in center)
        road_min_z = min(point[1] for point in center)
        road_max_z = max(point[1] for point in center)
        intersects = not (
            road_max_x < min_x
            or road_min_x > max_x
            or road_max_z < min_z
            or road_min_z > max_z
        )
        if intersects and length >= minimum_length:
            bounded_roads.append(road)
    roads = bounded_roads
    if len(roads) < 150:
        raise ValueError(f"Compiled road network is unexpectedly sparse: {len(roads)}")

    routes = []
    lane_offset_contract = network["lane_offset"]
    for road in sorted(roads, key=lambda value: int(value["source"]["osm_id"])):
        tags = road["tags"]
        if tags.get("highway") not in allowed_classes:
            raise ValueError(f"Forbidden road class selected: {tags.get('highway')}")
        quantized = road["points_q"]
        center = [
            (quantized[index] * quantization, quantized[index + 1] * quantization)
            for index in range(0, len(quantized), 2)
        ]
        if len(center) < 2:
            raise ValueError("Selected road has no driveable polyline")
        stable_road_name = tags.get("name") or f"unnamed-{road['source']['osm_id']}"
        lane_offset_meters = lane_offset_for_road(
            road, lane_offset_contract
        )
        for direction in (1, -1):
            # Colorado drives on the right: forward traffic occupies the right
            # side of the source polyline, reverse traffic the opposite side.
            lane = offset_polyline(
                center, -direction * lane_offset_meters
            )
            sampled = resample(lane, SAMPLE_SPACING_METERS)
            routes.append(
                route_record(
                    f"{stable_road_name.lower().replace(' ', '-')}-{road['source']['osm_id']}-"
                    + ("forward" if direction > 0 else "reverse"),
                    road,
                    direction,
                    lane_offset_meters,
                    sampled,
                    sampler,
                )
            )

    parked = []
    for index, vehicle in enumerate(source["parked_vehicles"]):
        x, z = pixel_to_world(vehicle["source_center_pixel"], orthophoto)
        archetype = weighted_index(
            FLEET_STYLE_WEIGHTS,
            deterministic_unit(index, index, 30),
        )
        paint_index = weighted_index(
            PAINT_COLOR_WEIGHTS,
            deterministic_unit(index, index, 31),
        )
        parked.append(
            {
                "id": vehicle["id"],
                "lot": vehicle["lot"],
                "archetype": archetype,
                "paint_index": paint_index,
                "x": round(x, 4),
                "y": round(sampler.sample(x, z) + GROUND_CLEARANCE_METERS, 3),
                "z": round(z, 4),
                "yaw_degrees": round(heading_from_axis(vehicle["source_axis_pixels"]), 3),
                "source_pixel": vehicle["source_center_pixel"],
                "source_axis_pixels": vehicle["source_axis_pixels"],
            }
        )

    dynamic = []
    spacing_by_class = {
        key: float(value)
        for key, value in network["vehicle_spacing_meters"].items()
    }
    maximum_per_route = int(network["maximum_vehicles_per_route"])
    following = network["car_following"]
    platoons = network["initial_platoons"]
    vehicle_lengths = [float(value) for value in following["vehicle_length_meters"]]
    for route_index, route in enumerate(routes):
        vehicle_count = max(
            1,
            min(
                maximum_per_route,
                int(route["length_meters"] / spacing_by_class[route["highway_class"]]),
            ),
        )
        route_vehicles = []
        for vehicle_index in range(vehicle_count):
            archetype = weighted_index(
                FLEET_STYLE_WEIGHTS,
                deterministic_unit(route_index, vehicle_index, 7),
            )
            if (
                route["highway_class"] in {"trunk", "primary"}
                and vehicle_index > 0
                and vehicle_index % 11 == 0
            ):
                archetype = 4
            paint_index = weighted_index(
                PAINT_COLOR_WEIGHTS,
                deterministic_unit(route_index, vehicle_index, 8),
            )
            is_truck = archetype in {3, 4}
            desired_speed_bounds = following[
                "truck_desired_speed_scale" if is_truck else "desired_speed_scale"
            ]
            headway_bounds = following[
                "truck_time_headway_seconds" if is_truck else "time_headway_seconds"
            ]
            acceleration_bounds = following[
                "truck_maximum_acceleration_mps2"
                if is_truck
                else "maximum_acceleration_mps2"
            ]
            route_vehicles.append(
                {
                    "id": f"moving-{route_index + 1:02d}-{vehicle_index + 1:02d}",
                    "route_index": route_index,
                    "archetype": archetype,
                    "paint_index": paint_index,
                    "desired_speed_scale": round(
                        profile_value(
                            desired_speed_bounds,
                            deterministic_unit(route_index, vehicle_index, 0),
                        ),
                        5,
                    ),
                    "time_headway_seconds": round(
                        profile_value(
                            headway_bounds,
                            deterministic_unit(route_index, vehicle_index, 1),
                        ),
                        5,
                    ),
                    "minimum_gap_meters": round(
                        profile_value(
                            following["minimum_gap_meters"],
                            deterministic_unit(route_index, vehicle_index, 2),
                        ),
                        5,
                    ),
                    "maximum_acceleration_mps2": round(
                        profile_value(
                            acceleration_bounds,
                            deterministic_unit(route_index, vehicle_index, 3),
                        ),
                        5,
                    ),
                    "comfortable_deceleration_mps2": round(
                        profile_value(
                            following["comfortable_deceleration_mps2"],
                            deterministic_unit(route_index, vehicle_index, 4),
                        ),
                        5,
                    ),
                    "emergency_deceleration_mps2": round(
                        profile_value(
                            following["emergency_deceleration_mps2"],
                            deterministic_unit(route_index, vehicle_index, 5),
                        ),
                        5,
                    ),
                    "acceleration_response_seconds": round(
                        profile_value(
                            following["acceleration_response_seconds"],
                            deterministic_unit(route_index, vehicle_index, 6),
                        ),
                        5,
                    ),
                    "vehicle_length_meters": vehicle_lengths[archetype],
                }
            )

        if vehicle_count == 1:
            route_vehicles[0]["initial_distance_meters"] = round(
                deterministic_unit(route_index, 0, 20) *
                route["length_meters"],
                4,
            )
        else:
            minimum_intervals = []
            gap_weights = []
            minimum_bumper_gap = float(
                network["minimum_initial_bumper_gap_meters"]
            )
            minimum_platoon_size = int(platoons["minimum_platoon_size"])
            maximum_platoon_size = int(platoons["maximum_platoon_size"])
            platoon_size = minimum_platoon_size + int(
                deterministic_unit(route_index, 0, 21) *
                (maximum_platoon_size - minimum_platoon_size + 1)
            )
            platoon_size = min(maximum_platoon_size, platoon_size)
            platoon_phase = route_index % platoon_size
            for vehicle_index, vehicle in enumerate(route_vehicles):
                leader = route_vehicles[(vehicle_index + 1) % vehicle_count]
                initial_speed = (
                    route["speed_mps"] * vehicle["desired_speed_scale"]
                )
                speed_aware_gap = initial_speed * min(
                    1.1,
                    vehicle["time_headway_seconds"] * 0.65,
                )
                net_gap = max(minimum_bumper_gap, speed_aware_gap)
                minimum_intervals.append(
                    net_gap
                    + 0.5
                    * (
                        vehicle["vehicle_length_meters"]
                        + leader["vehicle_length_meters"]
                    )
                )
                is_close_follower = (
                    (vehicle_index + platoon_phase) % platoon_size
                    != platoon_size - 1
                )
                bounds = (
                    [
                        platoons["minimum_close_weight"],
                        platoons["maximum_close_weight"],
                    ]
                    if is_close_follower
                    else [
                        platoons["minimum_open_weight"],
                        platoons["maximum_open_weight"],
                    ]
                )
                gap_weights.append(
                    profile_value(
                        bounds,
                        deterministic_unit(route_index, vehicle_index, 22),
                    )
                )

            remaining = route["length_meters"] - sum(minimum_intervals)
            if remaining < -0.01:
                raise ValueError(
                    f"Route {route['id']} is too short for its safety envelope"
                )
            total_weight = sum(gap_weights)
            intervals = [
                minimum_interval
                + max(0.0, remaining) * weight / total_weight
                for minimum_interval, weight
                in zip(minimum_intervals, gap_weights)
            ]
            distance = (
                deterministic_unit(route_index, 0, 23) *
                route["length_meters"]
            )
            for vehicle_index, vehicle in enumerate(route_vehicles):
                vehicle["initial_distance_meters"] = round(
                    distance % route["length_meters"],
                    4,
                )
                distance += intervals[vehicle_index]

        dynamic.extend(route_vehicles)

    output = {
        "schema": "questflight.kbdu-traffic-v1",
        "coordinate_frame": source["coordinate_frame"],
        "status": "STREAMED_MAJOR_ROAD_NETWORK",
        "source": {
            "orthophoto": orthophoto,
            "orthophoto_sha256": sha256(orthophoto_path),
            "road_path": source["road_source"]["path"],
            "road_sha256": sha256(road_path),
            "terrain_path": str(TERRAIN.relative_to(ROOT)).replace("\\", "/"),
            "terrain_sha256": sha256(TERRAIN),
            "osm_license": source["road_source"]["license"],
            "osm_attribution": source["road_source"]["attribution"],
            "vehicle_assets": source["vehicle_assets"],
        },
        "runtime_contract": {
            "maximum_near_instances": 128,
            "maximum_draw_calls": 5,
            "maximum_visible_triangles": 130000,
            "maximum_distance_meters": 2500.0,
            "steady_state_gc_allocations": 0,
            "per_vehicle_gameobjects": 0,
            "realtime_vehicle_shadows": 0,
            "car_following_model": following["model"],
            "simulation_step_seconds": float(
                following["simulation_step_seconds"]
            ),
            "minimum_initial_bumper_gap_meters": float(
                network["minimum_initial_bumper_gap_meters"]
            ),
            "minimum_runtime_bumper_gap_meters": float(
                following["minimum_gap_meters"][0]
            ),
        },
        "parked": parked,
        "routes": routes,
        "moving": dynamic,
    }
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(
        json.dumps(output, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print(
        json.dumps(
            {
                "parked": len(parked),
                "routes": len(routes),
                "dynamic": len(dynamic),
                "route_samples": sum(len(route["samples"]) for route in routes),
            },
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
