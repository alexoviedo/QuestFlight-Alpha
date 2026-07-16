#!/usr/bin/env python3
"""Create deterministic OSM building batches, terrain-seated vegetation cues, and a shared atlas."""

from __future__ import annotations

import argparse
import base64
import json
import math
import struct
from pathlib import Path

from PIL import Image, ImageDraw

from orthophoto_common import REPO_ROOT, atomic_write_json, file_record, sha256_file


DEFAULT_CONTEXT = (
    REPO_ROOT
    / "QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_reference_context.json"
)
DEFAULT_TERRAIN = (
    REPO_ROOT
    / "QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_terrain_rings.json"
)
DEFAULT_OUTPUT_DIR = REPO_ROOT / "QuestFlight/Assets/Production/Environment/PhotorealTerrainV1"


def decode_terrain_layer(layer: dict, quantization: float, origin_elevation: float) -> list[float]:
    payload = base64.b64decode(layer["height_dm_little_endian_base64"], validate=True)
    count = int(layer["width"]) * int(layer["height"])
    values = struct.unpack("<" + "h" * count, payload)
    return [origin_elevation + value * quantization for value in values]


class TerrainSampler:
    def __init__(self, terrain: dict):
        self.origin = float(terrain["origin"]["elevation_msl_meters"])
        quantization = float(terrain["height_quantization_meters"])
        self.layers = []
        for layer in sorted(terrain["layers"], key=lambda value: int(value["priority"])):
            self.layers.append((layer, decode_terrain_layer(layer, quantization, self.origin)))

    def sample(self, x: float, z: float) -> float:
        for layer, values in self.layers:
            if not (
                float(layer["min_x_meters"]) <= x <= float(layer["max_x_meters"])
                and float(layer["min_z_meters"]) <= z <= float(layer["max_z_meters"])
            ):
                continue
            if layer["kind"] == "ring":
                radius = max(abs(x), abs(z))
                if radius < float(layer.get("inner_radius_meters", 0.0)):
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
            elevation = (a * (1.0 - tx) + b * tx) * (1.0 - tz) + (c * (1.0 - tx) + d * tx) * tz
            return elevation - self.origin
        raise ValueError(f"No terrain layer contains ({x}, {z})")


def feature_center(feature: dict, quantization: float) -> tuple[float, float]:
    values = feature["points_q"]
    xs = values[0::2]
    zs = values[1::2]
    return sum(xs) / len(xs) * quantization, sum(zs) / len(zs) * quantization


def quadrant(x: float, z: float) -> str:
    return ("e" if x >= 0.0 else "w") + ("n" if z >= 0.0 else "s")


def signed_cell(value: float, cell_size: float) -> str:
    cell = math.floor(value / cell_size)
    return ("p" if cell >= 0 else "n") + f"{abs(cell):02d}"


def stable_key(index: int, x: float, z: float) -> int:
    return ((index + 1) * 2654435761 ^ round(x * 31.0) ^ (round(z * 31.0) << 1)) & 0xFFFFFFFF


def build_atlas(path: Path) -> None:
    size = 1024
    tile = size // 4
    image = Image.new("RGB", (size, size), (148, 146, 140))
    draw = ImageDraw.Draw(image)
    facade_colors = [
        (164, 164, 157), (180, 174, 159), (139, 151, 155), (172, 157, 139),
        (153, 145, 131), (186, 183, 170), (133, 142, 145), (159, 164, 150),
    ]
    roof_colors = [(93, 96, 98), (113, 104, 92), (125, 128, 126), (91, 103, 107)]
    for index in range(8):
        column = index % 4
        row = index // 4
        x0, y0 = column * tile, row * tile
        draw.rectangle((x0, y0, x0 + tile - 1, y0 + tile - 1), fill=facade_colors[index])
        for y in range(y0 + 24, y0 + tile, 48):
            draw.line((x0, y, x0 + tile, y), fill=tuple(max(0, c - 13) for c in facade_colors[index]), width=3)
        for x in range(x0 + 28, x0 + tile, 52):
            draw.rectangle((x, y0 + 90, min(x + 18, x0 + tile - 1), y0 + 126), fill=(71, 88, 94))
    for index in range(4):
        column = index
        for row in (2, 3):
            x0, y0 = column * tile, row * tile
            color = roof_colors[index]
            draw.rectangle((x0, y0, x0 + tile - 1, y0 + tile - 1), fill=color)
            for offset in range(-tile, tile * 2, 24):
                draw.line((x0 + offset, y0, x0 + offset + tile, y0 + tile), fill=tuple(min(255, c + 9) for c in color), width=2)
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG", optimize=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--context", type=Path, default=DEFAULT_CONTEXT)
    parser.add_argument("--terrain", type=Path, default=DEFAULT_TERRAIN)
    parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT_DIR)
    args = parser.parse_args()

    context = json.loads(args.context.read_text(encoding="utf-8"))
    terrain = json.loads(args.terrain.read_text(encoding="utf-8"))
    sampler = TerrainSampler(terrain)
    quantization = float(context["coordinate_quantization_meters"])
    features = context["openstreetmap"]["features"]
    building_batches: dict[str, list[dict]] = {}
    building_triangle_lod0 = 0
    building_triangle_lod1 = 0
    for index, feature in enumerate(features):
        if feature["category"] != "building" or feature["geometry_type"] != "polygon":
            continue
        x, z = feature_center(feature, quantization)
        distance = math.hypot(x, z)
        zone = "near" if distance <= 2000.0 else "mid"
        # 1.25 km cells keep renderer count bounded while giving LOD/culling a
        # materially tighter bound than one renderer for an entire quadrant.
        batch_id = f"buildings_{zone}_x{signed_cell(x, 1250.0)}_z{signed_cell(z, 1250.0)}"
        points = feature["points_q"]
        point_count = len(points) // 2
        if point_count > 1 and points[:2] == points[-2:]:
            point_count -= 1
        if point_count < 3:
            continue
        source_id = int(feature.get("source", {}).get("osm_id", index))
        building_batches.setdefault(batch_id, []).append(
            {
                "feature_index": index,
                "base_y_meters": round(sampler.sample(x, z), 3),
                "facade_tile": source_id % 8,
                "roof_tile": 8 + source_id % 4,
            }
        )
        building_triangle_lod0 += (point_count - 2) + point_count * 2
        building_triangle_lod1 += 10

    candidates: list[tuple[int, float, float, str]] = []
    for index, feature in enumerate(features):
        if feature["category"] not in ("water", "road"):
            continue
        points = feature["points_q"]
        stride = 8 if feature["category"] == "water" else 14
        for point_index in range(0, len(points) // 2, stride):
            x = points[point_index * 2] * quantization
            z = points[point_index * 2 + 1] * quantization
            distance = math.hypot(x, z)
            if distance > 6000.0 or (abs(x) < 1000.0 and abs(z) < 500.0):
                continue
            candidates.append((stable_key(index * 10000 + point_index, x, z), x, z, feature["category"]))
    candidates.sort()
    accepted: list[dict] = []
    for key, x, z, source_category in candidates:
        zone = "near" if math.hypot(x, z) <= 2000.0 else "mid"
        zone_count = sum(1 for value in accepted if value["zone"] == zone)
        limit = 144 if zone == "near" else 96
        spacing = 90.0 if zone == "near" else 180.0
        if zone_count >= limit:
            continue
        if any((x - value["x_meters"]) ** 2 + (z - value["z_meters"]) ** 2 < spacing**2 for value in accepted):
            continue
        accepted.append(
            {
                "x_meters": round(x, 3),
                "base_y_meters": round(sampler.sample(x, z), 3),
                "z_meters": round(z, 3),
                "height_meters": round(7.5 + (key % 41) * 0.1, 2),
                "crown_radius_meters": round(2.6 + ((key >> 8) % 21) * 0.08, 2),
                "zone": zone,
                "batch_id": f"vegetation_{zone}_{quadrant(x, z)}",
                "source_category": source_category,
            }
        )

    atlas_path = args.output_dir / "KBDU_ContextAtlas.png"
    recipe_path = args.output_dir / "KBDU_ContextBatchRecipe.json"
    manifest_path = args.output_dir / "KBDU_ContextAssetsManifest.json"
    build_atlas(atlas_path)
    batches = [
        {"id": batch_id, "buildings": records}
        for batch_id, records in sorted(building_batches.items())
    ]
    vegetation_batches = []
    for batch_id in sorted({value["batch_id"] for value in accepted}):
        vegetation_batches.append(
            {"id": batch_id, "placements": [value for value in accepted if value["batch_id"] == batch_id]}
        )
    recipe = {
        "schema_version": 1,
        "coordinate_frame": "KBDU local tangent ENU; Unity +X=east, +Y=up, +Z=north",
        "source_context_path": args.context.relative_to(REPO_ROOT).as_posix(),
        "coordinate_quantization_meters": quantization,
        "atlas_grid": [4, 4],
        "building_batches": batches,
        "vegetation_batches": vegetation_batches,
        "integration": {
            "building_lod_screen_relative_heights": [0.08, 0.025],
            "vegetation_lod_screen_relative_heights": [0.10, 0.035],
            "lod_crossfade": False,
            "near_cull_distance_meters": 2600.0,
            "mid_cull_distance_meters": 6200.0,
            "one_renderer_per_building": False,
            "one_shared_building_material": True,
            "one_shared_vegetation_material": True,
            "building_spatial_batch_cell_meters": 1250.0,
        },
    }
    atomic_write_json(recipe_path, recipe)
    authoring_source_paths = [
        Path(__file__).resolve(),
        REPO_ROOT / "QuestFlight/Assets/Editor/KbduPhotorealTerrainAssetAuthoring.cs",
        args.output_dir / "KBDU_TerrainPyramid.shader",
        args.output_dir / "KBDU_OsmBuildingContext.shader",
        args.output_dir / "KBDU_OpaqueVegetationContext.shader",
    ]
    generated_mesh_paths = sorted((args.output_dir / "Generated").glob("*.asset"))
    generated_unity_paths = generated_mesh_paths + [
        path
        for path in (
            args.output_dir / "KBDU_BatchedContext.prefab",
            args.output_dir / "KBDU_TerrainPyramid.mat",
            args.output_dir / "KBDU_OsmBuildings.mat",
            args.output_dir / "KBDU_ContextVegetation.mat",
        )
        if path.is_file()
    ]
    expected_mesh_count = (len(batches) + len(vegetation_batches)) * 2
    manifest = {
        "schema_version": 1,
        "label": "KBDU deterministic batched 3D context / not for navigation",
        "source": {
            "database": "OpenStreetMap bounded derivative",
            "source_path": args.context.relative_to(REPO_ROOT).as_posix(),
            "source_sha256": sha256_file(args.context),
            "license": "Open Data Commons Open Database License 1.0",
            "copyright": "© OpenStreetMap contributors",
            "copyright_url": "https://www.openstreetmap.org/copyright",
            "terrain_path": args.terrain.relative_to(REPO_ROOT).as_posix(),
            "terrain_sha256": sha256_file(args.terrain),
        },
        "project_authored_assets": {
            "atlas": file_record(atlas_path, relative_to=REPO_ROOT),
            "recipe": file_record(recipe_path, relative_to=REPO_ROOT),
            "license": "Project-authored; repository license",
        },
        "authoring_sources": [
            file_record(path, relative_to=REPO_ROOT) for path in authoring_source_paths if path.is_file()
        ],
        "generated_unity_assets": {
            "complete": len(generated_mesh_paths) == expected_mesh_count,
            "expected_mesh_count": expected_mesh_count,
            "observed_mesh_count": len(generated_mesh_paths),
            "files": [file_record(path, relative_to=REPO_ROOT) for path in generated_unity_paths],
            "license_boundary": (
                "Building meshes are OSM Produced Works; vegetation meshes, shaders, materials, atlas, "
                "and serialized hierarchy are project-authored. Exact OSM/source terms remain above."
            ),
        },
        "budgets": {
            "building_count": sum(len(batch["buildings"]) for batch in batches),
            "building_batch_count": len(batches),
            "building_lod0_triangle_estimate": building_triangle_lod0,
            "building_lod1_triangle_estimate": building_triangle_lod1,
            "vegetation_instance_count": len(accepted),
            "vegetation_batch_count": len(vegetation_batches),
            "vegetation_lod0_triangle_estimate": len(accepted) * 24,
            "vegetation_lod1_triangle_estimate": len(accepted) * 8,
            "active_draw_call_delta": len(batches) + len(vegetation_batches),
            "material_delta": 2,
        },
        "limitations": [
            "OSM completeness and tagged heights vary; deterministic source defaults remain visual cues, not surveyed structures.",
            "Vegetation positions are deterministic cues near mapped roads/water, not a botanical inventory.",
            "Parked aircraft are not emitted: the cleared aircraft source needs a separately reviewed static LOD before duplication.",
            "Lead scene integration, culling setup, stereo inspection, and Quest timing remain required.",
        ],
    }
    atomic_write_json(manifest_path, manifest)
    print(atlas_path)
    print(recipe_path)
    print(manifest_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
