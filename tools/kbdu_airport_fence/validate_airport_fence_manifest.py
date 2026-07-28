#!/usr/bin/env python3
"""Fail-closed validation for the compact KBDU fence Produced Work."""

from __future__ import annotations

import json
import math
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
MANIFEST = (
    REPO_ROOT
    / "QuestFlight"
    / "Assets"
    / "Production"
    / "Environment"
    / "AirportFenceV1"
    / "Data"
    / "KBDU_AirportFenceSourceV1.json"
)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> None:
    data = json.loads(MANIFEST.read_text(encoding="utf-8"))
    require(data["schema_version"] == "1", "schema changed")
    require(data["stable_id"] == "kbdu-airport-perimeter-fence-v1", "stable ID changed")
    require(data["source"]["way_id"] == 77513664, "OSM way authority changed")
    require(data["source"]["license"] == "Open Database License 1.0", "license changed")
    require(len(data["sections"]) == 4, "expected four authored fence sections")
    require(data["runtime_contract"]["maximum_selected_draws"] == 4, "draw contract changed")
    require(data["runtime_contract"]["new_materials"] == 0, "fence must reuse a material")
    require(data["runtime_contract"]["new_textures"] == 0, "fence must not add textures")
    require(data["runtime_contract"]["colliders"] == 0, "fence must not add colliders")

    total_length = 0.0
    previous_end = None
    for section in data["sections"]:
        points = section["points"]
        require(len(points) >= 2, f"{section['stable_id']} has fewer than two points")
        require(1.0 <= section["height_meters"] <= 2.0, "implausible fence height")
        if previous_end is not None:
            require(
                points[0]["osm_node_id"] == previous_end,
                f"{section['stable_id']} is disconnected",
            )
        previous_end = points[-1]["osm_node_id"]
        for first, second in zip(points, points[1:]):
            dx = second["east_meters"] - first["east_meters"]
            dz = second["north_meters"] - first["north_meters"]
            length = math.hypot(dx, dz)
            require(length > 0.05, "zero-length source segment")
            total_length += length
    require(
        data["sections"][0]["points"][0]["osm_node_id"] == previous_end,
        "fence sections do not form a closed loop",
    )
    require(3000.0 <= total_length <= 6000.0, "airport perimeter length is implausible")

    print(
        json.dumps(
            {
                "classification": "PASS",
                "sections": len(data["sections"]),
                "perimeter_length_meters": round(total_length, 2),
                "maximum_selected_draws": 4,
                "new_materials": 0,
                "new_textures": 0,
            },
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
