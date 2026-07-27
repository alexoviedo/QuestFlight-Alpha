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


def main() -> None:
    data = json.loads(MANIFEST.read_text(encoding="utf-8"))
    require(data["schema"] == "questflight.kbdu-traffic-v1", "schema mismatch")
    require(data["status"] == "BOUNDED_AUTHORED_BENCHMARK", "status mismatch")
    require(len(data["parked"]) == 30, "expected 30 photographed parked cars")
    require(len(data["routes"]) == 12, "expected six bidirectional road corridors")
    require(len(data["moving"]) == 12, "expected one moving car per route")
    require(data["runtime_contract"]["maximum_draw_calls"] == 4, "draw budget changed")
    require(data["runtime_contract"]["maximum_visible_triangles"] == 8000, "triangle budget changed")
    require(data["runtime_contract"]["per_vehicle_gameobjects"] == 0, "per-car objects forbidden")
    require(data["runtime_contract"]["realtime_vehicle_shadows"] == 0, "realtime shadows forbidden")

    ids = set()
    for parked in data["parked"]:
        require(parked["id"] not in ids, "duplicate parked id")
        ids.add(parked["id"])
        require(0 <= parked["archetype"] < 3, "invalid parked archetype")
        require(len(parked["source_pixel"]) == 2, "parked source pixel missing")
        require(len(parked["source_axis_pixels"]) == 4, "parked source axis missing")
        require(-2000 <= parked["x"] <= 2000 and -2000 <= parked["z"] <= 2000, "parked outside near image")

    for route_index, route in enumerate(data["routes"]):
        samples = route["samples"]
        require(len(samples) >= 20, f"route {route_index} has insufficient samples")
        require(route["road_width_meters"] >= 5.5, "road too narrow for compiled two-lane traffic")
        require(route["lane_offset_meters"] + 1.0 <= route["road_width_meters"] * 0.5, "lane leaves paved corridor")
        previous = -1.0
        for sample in samples:
            require(sample["distance"] > previous, "route distance is not strictly increasing")
            previous = sample["distance"]
        require(abs(previous - route["length_meters"]) < 0.01, "route length mismatch")
        require(route["speed_mps"] <= 20.0, "route speed exceeds bounded contract")
        require(route["highway_class"] in {"trunk", "secondary", "tertiary", "residential"}, "forbidden road class")

    for moving in data["moving"]:
        require(0 <= moving["route_index"] < len(data["routes"]), "moving route index invalid")
        require(0 <= moving["archetype"] < 3, "moving archetype invalid")
        route = data["routes"][moving["route_index"]]
        require(0 <= moving["initial_distance_meters"] < route["length_meters"], "moving offset invalid")
        require(0.85 <= moving["speed_scale"] <= 1.10, "moving speed scale invalid")

    print(
        json.dumps(
            {
                "classification": "PASS",
                "parked": len(data["parked"]),
                "routes": len(data["routes"]),
                "dynamic": len(data["moving"]),
                "route_samples": sum(len(route["samples"]) for route in data["routes"]),
            },
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
