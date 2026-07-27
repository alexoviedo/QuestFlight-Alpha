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
LANE_OFFSET_METERS = 1.75
SELECTED_SPEEDS_MPS = {
    "Diagonal Highway": 19.0,
    "Valmont Road": 14.0,
    "Jay Road": 13.0,
    "55th Street": 12.0,
    "North 61st Street": 12.0,
}


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


def route_record(
    stable_id: str,
    road: dict,
    direction: int,
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
    return {
        "id": stable_id,
        "road_name": road["tags"]["name"],
        "osm_way_id": int(road["source"]["osm_id"]),
        "highway_class": road["tags"]["highway"],
        "direction": "forward" if direction > 0 else "reverse",
        "lane_offset_meters": LANE_OFFSET_METERS,
        "road_width_meters": float(road["render_width_meters"]),
        "speed_mps": SELECTED_SPEEDS_MPS[road["tags"]["name"]],
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
    selected_ids = set(source["road_source"]["selected_osm_way_ids"])
    roads = [
        feature
        for feature in context["openstreetmap"]["features"]
        if feature.get("category") == "road"
        and int(feature["source"]["osm_id"]) in selected_ids
    ]
    if {int(road["source"]["osm_id"]) for road in roads} != selected_ids:
        raise ValueError("One or more allowlisted OSM road features are missing")

    routes = []
    for road in sorted(roads, key=lambda value: int(value["source"]["osm_id"])):
        tags = road["tags"]
        if tags.get("highway") not in {"trunk", "secondary", "tertiary", "residential"}:
            raise ValueError(f"Forbidden road class selected: {tags.get('highway')}")
        quantized = road["points_q"]
        center = [
            (quantized[index] * quantization, quantized[index + 1] * quantization)
            for index in range(0, len(quantized), 2)
        ]
        if len(center) < 2:
            raise ValueError("Selected road has no driveable polyline")
        for direction in (1, -1):
            lane = offset_polyline(center, direction * LANE_OFFSET_METERS)
            sampled = resample(lane, SAMPLE_SPACING_METERS)
            routes.append(
                route_record(
                    f"{tags['name'].lower().replace(' ', '-')}-{road['source']['osm_id']}-"
                    + ("forward" if direction > 0 else "reverse"),
                    road,
                    direction,
                    sampled,
                    sampler,
                )
            )

    parked = []
    for index, vehicle in enumerate(source["parked_vehicles"]):
        x, z = pixel_to_world(vehicle["source_center_pixel"], orthophoto)
        parked.append(
            {
                "id": vehicle["id"],
                "lot": vehicle["lot"],
                "archetype": int(vehicle["archetype"]),
                "x": round(x, 4),
                "y": round(sampler.sample(x, z) + GROUND_CLEARANCE_METERS, 3),
                "z": round(z, 4),
                "yaw_degrees": round(heading_from_axis(vehicle["source_axis_pixels"]), 3),
                "source_pixel": vehicle["source_center_pixel"],
                "source_axis_pixels": vehicle["source_axis_pixels"],
            }
        )

    dynamic = []
    for index, route in enumerate(routes):
        dynamic.append(
            {
                "id": f"moving-{index + 1:02d}",
                "route_index": index,
                "archetype": index % 3,
                "initial_distance_meters": round((0.17 + (index % 4) * 0.19) * route["length_meters"], 3),
                "speed_scale": [0.92, 1.0, 1.07][index % 3],
            }
        )

    output = {
        "schema": "questflight.kbdu-traffic-v1",
        "coordinate_frame": source["coordinate_frame"],
        "status": "BOUNDED_AUTHORED_BENCHMARK",
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
            "maximum_near_instances": 24,
            "maximum_mid_instances": 48,
            "maximum_draw_calls": 4,
            "maximum_visible_triangles": 8000,
            "maximum_distance_meters": 1500.0,
            "near_distance_meters": 550.0,
            "lod_hysteresis_meters": 50.0,
            "steady_state_gc_allocations": 0,
            "per_vehicle_gameobjects": 0,
            "realtime_vehicle_shadows": 0,
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
