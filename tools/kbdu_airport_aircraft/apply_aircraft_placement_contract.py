#!/usr/bin/env python3
"""Apply the authored airport-aircraft placement contract to V2 context data."""

from __future__ import annotations

import base64
import hashlib
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
LOD0_SOURCE_STATS = {
    "powered_c172": {"triangles": 400, "vertices": 625},
    "sailplane": {"triangles": 279, "vertices": 287},
}
PROXY_TRIANGLES_PER_AIRCRAFT = 6
PROXY_VERTICES_PER_AIRCRAFT = 12


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def validate_orthophoto(contract: dict) -> dict:
    source = contract["orthophoto"]
    path = ROOT / source["path"]
    if not path.is_file():
        raise ValueError(f"Active placement orthophoto is missing: {path}")
    if sha256(path) != source["sha256"]:
        raise ValueError("Active placement orthophoto hash changed; retrace before authoring")
    meta = Path(str(path) + ".meta").read_text(encoding="utf-8")
    if f"guid: {source['guid']}" not in meta:
        raise ValueError("Active placement orthophoto GUID changed; retrace before authoring")
    width, height = (int(value) for value in source["dimensions_pixels"])
    if width != 8192 or height != 8192:
        raise ValueError("Airport-aircraft placement contract requires the active 8192 near image")
    if "row 0=north" not in source["orientation"]:
        raise ValueError("Airport-aircraft image orientation is not explicit")
    return source


def pixel_to_world(pixel: list[int], orthophoto: dict) -> tuple[float, float]:
    width, height = orthophoto["dimensions_pixels"]
    min_x, min_z, max_x, max_z = orthophoto["world_bounds_meters"]
    column, row = pixel
    x = min_x + column / width * (max_x - min_x)
    z = max_z - row / height * (max_z - min_z)
    return x, z


def heading_from_axis(axis: list[int]) -> float:
    if len(axis) != 4:
        raise ValueError("Aircraft source axis must contain nose x/y then tail x/y")
    east = float(axis[0] - axis[2])
    north = float(axis[3] - axis[1])
    if abs(east) + abs(north) < 1.0:
        raise ValueError("Aircraft source axis is degenerate")
    return math.degrees(math.atan2(east, north)) % 360.0


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
    if int(contract.get("schema_version", 0)) != 2:
        raise ValueError("Expected airport-aircraft placement schema v2")
    orthophoto = validate_orthophoto(contract)
    sampler = TerrainSampler(json.loads(TERRAIN.read_text(encoding="utf-8")))
    rows = []
    for group in contract["groups"]:
        for source in group["placements"]:
            x, z = pixel_to_world(source["source_center_pixel"], orthophoto)
            yaw = heading_from_axis(source["source_axis_pixels"])
            roll = 0.0
            wing_down_side = source.get("wing_down_side", "none")
            if group["family"] == "sailplane":
                resting_pose = group["resting_pose"]
                roll_magnitude = float(resting_pose["derived_roll_degrees"])
                if wing_down_side not in ("left", "right"):
                    raise ValueError(f"Sailplane {source['id']} has no valid wing-down side")
                roll = roll_magnitude if wing_down_side == "left" else -roll_magnitude
            rows.append(
                {
                    "model_family": group["family"],
                    "x_meters": round(x, 4),
                    "base_y_meters": round(sampler.sample(x, z), 3),
                    "z_meters": round(z, 4),
                    "yaw_degrees": round(yaw, 3),
                    "pitch_degrees": 0.0,
                    "roll_degrees": roll,
                    "wing_down_side": wing_down_side,
                    "color_rgba": source["color"],
                    "placement_v2": {
                        "stable_id": source["id"],
                        "stand_group": group["id"],
                        "terrain_height_meters": round(sampler.sample(x, z), 3),
                        "ground_clearance_meters": 0.025,
                        "orthophoto_alignment": "active_2020_city_aircraft_silhouette",
                        "orthophoto_path": orthophoto["path"],
                        "orthophoto_guid": orthophoto["guid"],
                        "orthophoto_sha256": orthophoto["sha256"],
                        "orthophoto_source_pixel": source["source_center_pixel"],
                        "orthophoto_source_axis_pixels": source["source_axis_pixels"],
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
    family_counts = {
        family: sum(row["model_family"] == family for row in rows)
        for family in LOD0_SOURCE_STATS
    }
    lod0_triangles = sum(
        family_counts[family] * stats["triangles"]
        for family, stats in LOD0_SOURCE_STATS.items()
    )
    lod0_vertices = sum(
        family_counts[family] * stats["vertices"]
        for family, stats in LOD0_SOURCE_STATS.items()
    )
    proxy_triangles = len(rows) * PROXY_TRIANGLES_PER_AIRCRAFT
    proxy_vertices = len(rows) * PROXY_VERTICES_PER_AIRCRAFT
    config["runtime_hlod"]["parked_aircraft_estimates"] = {
        "LOD0": {"triangles": lod0_triangles, "vertices": lod0_vertices},
        "LOD1": {"triangles": proxy_triangles, "vertices": proxy_vertices},
        "LOD2": {"triangles": proxy_triangles, "vertices": proxy_vertices},
    }
    config["runtime_hlod"]["parked_aircraft_screen_relative_heights"] = [0.008, 0.005, 0.002]
    HLOD_CONFIG.write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({"placements": len(rows), "groups": len(contract["groups"])}, indent=2))


if __name__ == "__main__":
    main()
