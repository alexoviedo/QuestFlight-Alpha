#!/usr/bin/env python3
"""Apply the authored airport-aircraft placement contract to V2 context data."""

from __future__ import annotations

import base64
import json
import math
import struct
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
PLACEMENTS = Path(__file__).with_name("airport_aircraft_placements_v1.json")
TERRAIN = (
    ROOT
    / "QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_terrain_rings.json"
)
RECIPES = (
    ROOT / "QuestFlight/Assets/Production/Environment/ReconstructionV2/KBDU_ContextBatchRecipeV2.json",
    ROOT / "tools/kbdu_environment_reconstruction/KBDU_ContextBatchRecipeV2.json",
)
HLOD_CONFIG = ROOT / "tools/kbdu_hlod_v2/config.json"


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


def main() -> None:
    contract = json.loads(PLACEMENTS.read_text(encoding="utf-8"))
    sampler = TerrainSampler(json.loads(TERRAIN.read_text(encoding="utf-8")))
    rows = []
    for group in contract["groups"]:
        for source in group["placements"]:
            x = float(source["x"])
            z = float(source["z"])
            rows.append(
                {
                    "model_family": group["family"],
                    "x_meters": x,
                    "base_y_meters": round(sampler.sample(x, z), 3),
                    "z_meters": z,
                    "yaw_degrees": float(source["yaw"]),
                    "color_rgba": source["color"],
                    "placement_v2": {
                        "stable_id": source["id"],
                        "stand_group": group["id"],
                        "terrain_height_meters": round(sampler.sample(x, z), 3),
                        "ground_clearance_meters": 0.025,
                        "orthophoto_alignment": "manual_2019_NAIP_aircraft_silhouette",
                    },
                }
            )
    batch = [{"id": "parked_aircraft_airport", "placements": rows}]
    for path in RECIPES:
        document = json.loads(path.read_text(encoding="utf-8"))
        document["parked_aircraft_batches"] = batch
        path.write_text(
            json.dumps(document, separators=(",", ":"), sort_keys=True) + "\n",
            encoding="utf-8",
            newline="\n",
        )

    config = json.loads(HLOD_CONFIG.read_text(encoding="utf-8"))
    config["runtime_hlod"]["parked_aircraft_estimates"] = {
        "LOD0": {"triangles": 11783, "vertices": 14166},
        "LOD1": {"triangles": 204, "vertices": 408},
        "LOD2": {"triangles": 204, "vertices": 408},
    }
    config["runtime_hlod"]["parked_aircraft_screen_relative_heights"] = [0.008, 0.005, 0.002]
    HLOD_CONFIG.write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({"placements": len(rows), "groups": len(contract["groups"])}, indent=2))


if __name__ == "__main__":
    main()
