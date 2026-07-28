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
PASSENGER_FLEET_STYLE_WEIGHTS = [0.24, 0.14, 0.34, 0.20, 0.08, 0.0]
TRUNK_FLEET_STYLE_WEIGHTS = [0.20, 0.10, 0.28, 0.18, 0.08, 0.16]
PAINT_COLOR_WEIGHTS = [0.26, 0.22, 0.19, 0.14, 0.09, 0.07, 0.02, 0.01]
AMBIGUOUS_WARM_NEUTRAL_WEIGHTS = [0.36, 0.12, 0.28, 0.24]


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


def calibrate_observed_paint(vehicle: dict, vehicle_index: int) -> int:
    """Map native aerial paint evidence to the runtime market palette.

    Index 7 is deliberately an uncertainty bucket: warm illumination,
    pavement bleed, beige, and oxidized neutral paint are not separable in a
    seven-pixel median sample.  Rendering all of those detections as brown
    would be less faithful than resolving the uncertainty into the
    market-dominant neutral colors with a stable per-source-record hash.
    """

    paint_index = int(vehicle["observed_paint_index"])
    if not 0 <= paint_index < len(PAINT_COLOR_WEIGHTS):
        raise ValueError("Native parked vehicle paint index is invalid")
    if paint_index != 7:
        return paint_index
    identity = str(vehicle.get("id", vehicle_index)).encode("utf-8")
    unit = int.from_bytes(
        hashlib.sha256(identity).digest()[:4],
        "little",
    ) / float(1 << 32)
    return weighted_index(AMBIGUOUS_WARM_NEUTRAL_WEIGHTS, unit)


def fleet_weights(highway_class: str | None) -> list[float]:
    return (
        TRUNK_FLEET_STYLE_WEIGHTS
        if highway_class == "trunk"
        else PASSENGER_FLEET_STYLE_WEIGHTS
    )


def choose_local_signature(
    route_index: int,
    vehicle_index: int,
    highway_class: str | None,
    recent: list[tuple[int, int]],
) -> tuple[int, int]:
    """Select a market-weighted signature without conspicuous local clones."""

    weights = fleet_weights(highway_class)
    for attempt in range(12):
        archetype = weighted_index(
            weights,
            deterministic_unit(route_index, vehicle_index, 7 + attempt * 17),
        )
        paint_index = weighted_index(
            PAINT_COLOR_WEIGHTS,
            deterministic_unit(route_index, vehicle_index, 8 + attempt * 19),
        )
        signature = (archetype, paint_index)
        if signature in recent[-3:]:
            continue
        if sum(1 for _, paint in recent[-4:] if paint == paint_index) >= 2:
            continue
        return signature

    # Deterministic fallback guarantees that even an unusually unlucky hash
    # sequence cannot place an exact model/color clone directly behind itself.
    archetype = weighted_index(
        weights,
        deterministic_unit(route_index, vehicle_index, 211),
    )
    paint_index = weighted_index(
        PAINT_COLOR_WEIGHTS,
        deterministic_unit(route_index, vehicle_index, 223),
    )
    if recent and (archetype, paint_index) == recent[-1]:
        paint_index = (paint_index + 1 + vehicle_index) % len(PAINT_COLOR_WEIGHTS)
    return archetype, paint_index


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
        "direction_policy": (
            "SOURCE_WAY_ONEWAY_TRUNK"
            if highway_class == "trunk"
            else "RIGHT_HAND_BIDIRECTIONAL"
        ),
        "lane_offset_meters": round(lane_offset_meters, 3),
        "road_width_meters": float(road["render_width_meters"]),
        "speed_mps": SELECTED_SPEEDS_MPS.get(
            road_name, DEFAULT_SPEEDS_MPS[highway_class]
        ),
        "length_meters": round(cumulative, 3),
        "samples": samples,
    }


def build_diagonal_highway_truck_circuit(
    routes: list[dict],
    sampler: TerrainSampler,
) -> dict:
    """Build a closed loop from both photographed divided-highway carriageways.

    Dynamic routes are normally open OSM way fragments. Modulo-wrapping those
    fragments is visually tolerable for tiny distant cars but makes a 16 m
    semi appear to teleport hundreds of meters. The three source ways below
    overlap as opposing Diagonal Highway carriageways around the product
    flight area, allowing a real-road closed circuit with only short median
    turnarounds at its remote ends.
    """

    by_way = {route["osm_way_id"]: route for route in routes}
    required = (398080519, 355298123, 398080554)
    if any(way_id not in by_way for way_id in required):
        raise ValueError("Required Diagonal Highway truck-circuit ways are missing")
    outbound = by_way[398080519]
    return_a = by_way[355298123]
    return_b = by_way[398080554]

    def points(route: dict) -> list[tuple[float, float]]:
        return [(sample["x"], sample["z"]) for sample in route["samples"]]

    outbound_points = points(outbound)
    return_points_a = points(return_a)
    return_points_b = points(return_b)
    outbound_start_target = return_points_b[-1]
    outbound_end_target = return_points_a[0]
    start_index = min(
        range(len(outbound_points)),
        key=lambda index: math.dist(
            outbound_points[index],
            outbound_start_target,
        ),
    )
    end_index = min(
        range(len(outbound_points)),
        key=lambda index: math.dist(
            outbound_points[index],
            outbound_end_target,
        ),
    )
    if start_index >= end_index:
        raise ValueError("Diagonal Highway truck-circuit overlap is reversed")
    circuit = list(outbound_points[start_index : end_index + 1])

    def append_connected(path: list[tuple[float, float]]) -> None:
        connector = resample([circuit[-1], path[0]], SAMPLE_SPACING_METERS)
        circuit.extend(connector[1:])
        circuit.extend(path[1:])

    append_connected(return_points_a)
    append_connected(return_points_b)
    closure = resample([circuit[-1], circuit[0]], SAMPLE_SPACING_METERS)
    circuit.extend(closure[1:])
    if math.dist(circuit[0], circuit[-1]) > 0.01:
        raise ValueError("Diagonal Highway truck circuit did not close")

    synthetic_road = {
        "tags": {
            "name": "Diagonal Highway",
            "highway": "trunk",
        },
        "source": {"osm_id": 398080519},
        "render_width_meters": outbound["road_width_meters"],
    }
    record = route_record(
        "diagonal-highway-dedicated-truck-circuit",
        synthetic_road,
        1,
        outbound["lane_offset_meters"],
        circuit,
        sampler,
    )
    record["direction_policy"] = "DEDICATED_DIVIDED_HIGHWAY_TRUCK_CIRCUIT"
    record["closed_loop"] = True
    return record


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
        # The compact reconstruction context does not retain OSM's oneway tag.
        # Its trunk highways are nevertheless encoded as separate physical
        # carriageway ways, following OSM's divided-highway convention. Compile
        # only the source-way direction for those carriageways; ordinary roads
        # continue to receive right-hand forward and reverse lanes.
        directions = (1,) if tags.get("highway") == "trunk" else (1, -1)
        for direction in directions:
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
    routes.append(build_diagonal_highway_truck_circuit(routes, sampler))

    parked = []
    parked_signatures_by_lot: dict[str, list[tuple[int, int]]] = {}
    for index, vehicle in enumerate(source["parked_vehicles"]):
        source_image = vehicle.get("source_image", "near")
        world_position = vehicle.get("world_position_meters")
        if world_position is not None:
            if len(world_position) != 2:
                raise ValueError("Native parked vehicle world position is invalid")
            x, z = map(float, world_position)
            yaw_degrees = float(vehicle["yaw_degrees"]) % 360.0
        else:
            parked_imagery = source.get("parked_imagery_sources", {})
            vehicle_orthophoto = parked_imagery.get(source_image, orthophoto)
            if not vehicle_orthophoto:
                raise ValueError(
                    f"Unknown parked-vehicle source image: {source_image}"
                )
            x, z = pixel_to_world(
                vehicle["source_center_pixel"],
                vehicle_orthophoto,
            )
            yaw_degrees = heading_from_axis(vehicle["source_axis_pixels"])
        recent = parked_signatures_by_lot.setdefault(vehicle["lot"], [])
        archetype, paint_index = choose_local_signature(
            index,
            index,
            None,
            recent,
        )
        if "observed_paint_index" in vehicle:
            paint_index = calibrate_observed_paint(vehicle, index)
            if recent and (archetype, paint_index) == recent[-1]:
                archetype = (archetype + 1 + index) % (
                    len(PASSENGER_FLEET_STYLE_WEIGHTS) - 1
                )
        recent.append((archetype, paint_index))
        parked.append(
            {
                "id": vehicle["id"],
                "archetype": archetype,
                "paint_index": paint_index,
                "x": round(x, 4),
                "y": round(sampler.sample(x, z) + GROUND_CLEARANCE_METERS, 3),
                "z": round(z, 4),
                "yaw_degrees": round(yaw_degrees, 3),
            }
        )

    parked_cell_size_meters = 500.0
    parked.sort(
        key=lambda vehicle: (
            math.floor(vehicle["z"] / parked_cell_size_meters),
            math.floor(vehicle["x"] / parked_cell_size_meters),
            vehicle["id"],
        )
    )
    parked_cells = []
    cell_start = 0
    while cell_start < len(parked):
        cell_x = math.floor(
            parked[cell_start]["x"] / parked_cell_size_meters
        )
        cell_z = math.floor(
            parked[cell_start]["z"] / parked_cell_size_meters
        )
        cell_end = cell_start + 1
        while cell_end < len(parked):
            next_x = math.floor(
                parked[cell_end]["x"] / parked_cell_size_meters
            )
            next_z = math.floor(
                parked[cell_end]["z"] / parked_cell_size_meters
            )
            if next_x != cell_x or next_z != cell_z:
                break
            cell_end += 1
        parked_cells.append(
            {
                "cell_x": cell_x,
                "cell_z": cell_z,
                "start": cell_start,
                "count": cell_end - cell_start,
                "min_x": round(cell_x * parked_cell_size_meters, 3),
                "min_z": round(cell_z * parked_cell_size_meters, 3),
                "max_x": round((cell_x + 1) * parked_cell_size_meters, 3),
                "max_z": round((cell_z + 1) * parked_cell_size_meters, 3),
            }
        )
        cell_start = cell_end

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
        is_truck_circuit = (
            route["direction_policy"]
            == "DEDICATED_DIVIDED_HIGHWAY_TRUCK_CIRCUIT"
        )
        vehicle_count = 10 if is_truck_circuit else max(
            1,
            min(
                maximum_per_route,
                int(route["length_meters"] / spacing_by_class[route["highway_class"]]),
            ),
        )
        route_vehicles = []
        route_signatures: list[tuple[int, int]] = []
        for vehicle_index in range(vehicle_count):
            archetype, paint_index = choose_local_signature(
                route_index,
                vehicle_index,
                route["highway_class"],
                route_signatures,
            )
            if is_truck_circuit:
                archetype = 5
                paint_index = (route_index + vehicle_index * 3) % len(
                    PAINT_COLOR_WEIGHTS
                )
            elif archetype == 5:
                # Open OSM way fragments must never carry a long vehicle that
                # makes their unavoidable cyclic recycle visually obvious.
                archetype = (
                    route_index + vehicle_index * 3
                ) % (len(PASSENGER_FLEET_STYLE_WEIGHTS) - 1)
            route_signatures.append((archetype, paint_index))
            is_truck = archetype in {3, 4, 5}
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

        if (
            len(route_signatures) > 1
            and route_signatures[-1] == route_signatures[0]
        ):
            route_vehicles[-1]["paint_index"] = (
                route_vehicles[-1]["paint_index"] + 1
            ) % len(PAINT_COLOR_WEIGHTS)

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
            "parked_authority_path": str(
                SOURCES.relative_to(ROOT)
            ).replace("\\", "/"),
            "parked_authority_sha256": sha256(SOURCES),
            "parked_detection": source.get("parked_detection", {}),
        },
        "runtime_contract": {
            "representation": "DETAIL_NEAR_FIXED_PROXY_FAR",
            "maximum_near_instances": 64,
            "maximum_far_instances": 1800,
            "near_enter_distance_meters": 800.0,
            "near_exit_distance_meters": 1000.0,
            "far_enter_distance_meters": 4200.0,
            "far_exit_distance_meters": 4500.0,
            "parked_cell_size_meters": parked_cell_size_meters,
            "maximum_draw_calls": 9,
            "maximum_visible_triangles": 130000,
            "visibility_distance_policy": (
                "SPATIALLY_INDEXED_EXACT_POSES_WITH_HYSTERETIC_FAR_CUTOFF"
            ),
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
        "parked_cells": parked_cells,
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
