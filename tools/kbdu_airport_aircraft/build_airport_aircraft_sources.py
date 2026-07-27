#!/usr/bin/env python3
"""Build fixed-detail parked C172 and sailplane scenery sources in Blender.

Usage:
  blender --background --python build_airport_aircraft_sources.py -- <FFGL.blend>

The CC0 sailplane source is intentionally external. Only optimized derivatives,
this deterministic generator, and a complete provenance manifest enter Git.
"""

from __future__ import annotations

import hashlib
import json
import math
import sys
from pathlib import Path

import bpy


ROOT = Path(__file__).resolve().parents[2]
C172_SOURCE = ROOT / (
    "QuestFlight/Assets/Resources/QuestFlightLab/ImportedAssets/"
    "Cessna172KogThorns/cessna172.glb"
)
OUTPUT_DIR = ROOT / "QuestFlight/Assets/Production/Environment/AirportParkedAircraftV1/Models"
DATA_DIR = ROOT / "QuestFlight/Assets/Production/Environment/AirportParkedAircraftV1/Data"
C172_OUTPUT = OUTPUT_DIR / "KBDU_ParkedC172_FixedDetail.glb"
SAILPLANE_OUTPUT = OUTPUT_DIR / "KBDU_ParkedSailplane_FixedDetail.glb"
MANIFEST_OUTPUT = DATA_DIR / "AirportParkedAircraftSourceManifestV1.json"
C172_SHA256 = "a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872"
SAILPLANE_BLEND_SHA256 = "c367729605b0d67d1d989d18be1ca92b0279037006110858f1e7fc00909cd077"
TARGETS = {
    # Recover serialized-scene headroom without changing the close-view planform,
    # materials, placement count, or orthophoto alignment. The removed triangles
    # are sub-pixel exterior shell detail on a static background aircraft.
    "ParkedC172_LOD0": 400,
    "ParkedC172_LOD1": 350,
    "ParkedSailplane_LOD0": 280,
    "ParkedSailplane_LOD1": 240,
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)


def set_point_color(obj: bpy.types.Object, rgba: tuple[float, float, float, float]) -> None:
    colors = obj.data.color_attributes.get("COLOR_0")
    if colors is None:
        colors = obj.data.color_attributes.new(name="COLOR_0", type="BYTE_COLOR", domain="POINT")
    for value in colors.data:
        value.color = rgba


def duplicate_join(sources: list[tuple[bpy.types.Object, tuple[float, float, float, float]]], name: str) -> bpy.types.Object:
    duplicates: list[bpy.types.Object] = []
    for source, color in sources:
        duplicate = source.copy()
        duplicate.data = source.data.copy()
        bpy.context.collection.objects.link(duplicate)
        duplicate.parent = None
        duplicate.matrix_world = source.matrix_world.copy()
        set_point_color(duplicate, color)
        duplicates.append(duplicate)
    bpy.ops.object.select_all(action="DESELECT")
    for duplicate in duplicates:
        duplicate.select_set(True)
    bpy.context.view_layer.objects.active = duplicates[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = name
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    result.data.materials.clear()
    result.data.validate(clean_customdata=False)
    return result


def normalize_grounded(source: bpy.types.Object, expected_span: tuple[float, float]) -> None:
    xs = [vertex.co.x for vertex in source.data.vertices]
    ys = [vertex.co.y for vertex in source.data.vertices]
    zs = [vertex.co.z for vertex in source.data.vertices]
    center_x = (min(xs) + max(xs)) * 0.5
    center_y = (min(ys) + max(ys)) * 0.5
    minimum_z = min(zs)
    for vertex in source.data.vertices:
        vertex.co.x -= center_x
        vertex.co.y -= center_y
        vertex.co.z -= minimum_z
    source.data.update()
    span = max(max(xs) - min(xs), max(ys) - min(ys))
    if not expected_span[0] <= span <= expected_span[1]:
        raise RuntimeError(f"{source.name} has unsafe full-size span/length: {span:.3f} m")


def build_lod(source: bpy.types.Object, name: str, target: int) -> bpy.types.Object:
    lod = source.copy()
    lod.data = source.data.copy()
    bpy.context.collection.objects.link(lod)
    lod.name = name
    lod.data.name = name + "_Mesh"
    lod.data.calc_loop_triangles()
    source_triangles = len(lod.data.loop_triangles)
    modifier = lod.modifiers.new("DeterministicExteriorDecimation", "DECIMATE")
    modifier.decimate_type = "COLLAPSE"
    modifier.ratio = min(1.0, target / source_triangles)
    modifier.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = lod
    bpy.ops.object.select_all(action="DESELECT")
    lod.select_set(True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    triangulate = lod.modifiers.new("RuntimeTriangles", "TRIANGULATE")
    bpy.ops.object.modifier_apply(modifier=triangulate.name)
    minimum_z = min(vertex.co.z for vertex in lod.data.vertices)
    for vertex in lod.data.vertices:
        vertex.co.z -= minimum_z
    lod.data.validate(clean_customdata=False)
    lod.data.update(calc_edges=True)
    return lod


def export_lods(lods: list[bpy.types.Object], output: Path) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    for lod in lods:
        lod.select_set(True)
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(output),
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
        export_materials="NONE",
        export_extras=False,
        export_cameras=False,
        export_lights=False,
        export_animations=False,
        export_attributes=True,
    )


def mesh_record(obj: bpy.types.Object) -> dict:
    obj.data.calc_loop_triangles()
    values = [vertex.co for vertex in obj.data.vertices]
    return {
        "name": obj.name,
        "vertices": len(values),
        "triangles": len(obj.data.loop_triangles),
        "bounds_meters": {
            "minimum": [min(value[index] for value in values) for index in range(3)],
            "maximum": [max(value[index] for value in values) for index in range(3)],
        },
        "grounded_local_z": abs(min(value.z for value in values)) <= 0.001,
        "vertex_color_attribute": obj.data.color_attributes.get("COLOR_0") is not None,
    }


def sailplane_contact_record(obj: bpy.types.Object) -> dict:
    values = [vertex.co.copy() for vertex in obj.data.vertices]
    half_span = max(abs(value.x) for value in values)
    gear = min(
        (value for value in values if abs(value.x) <= half_span * 0.25),
        key=lambda value: value.z,
    )
    left = min(
        (value for value in values if value.x <= -half_span * 0.90),
        key=lambda value: value.z,
    )
    right = min(
        (value for value in values if value.x >= half_span * 0.90),
        key=lambda value: value.z,
    )

    def roll_to_contact(tip) -> float:
        value = math.degrees(math.atan2(-(tip.z - gear.z), tip.x - gear.x))
        if value > 90.0:
            value -= 180.0
        if value < -90.0:
            value += 180.0
        return value

    left_roll = roll_to_contact(left)
    right_roll = roll_to_contact(right)
    if not 6.0 <= abs(left_roll) <= 10.0 or not 6.0 <= abs(right_roll) <= 10.0:
        raise RuntimeError(
            f"Sailplane wheel/wingtip resting solve is implausible: {left_roll}/{right_roll}"
        )
    return {
        "method": "LOD0 centerline-lowest gear vertex plus outer-10-percent lowest wingtip vertex",
        "gear_contact_blender_xyz_meters": list(gear),
        "left_wingtip_contact_blender_xyz_meters": list(left),
        "right_wingtip_contact_blender_xyz_meters": list(right),
        "left_wing_down_roll_degrees": left_roll,
        "right_wing_down_roll_degrees": right_roll,
        "authored_symmetric_roll_degrees": round(
            (abs(left_roll) + abs(right_roll)) * 0.5, 2
        ),
    }


def build_c172() -> tuple[list[bpy.types.Object], list[dict]]:
    clear_scene()
    bpy.ops.import_scene.gltf(filepath=str(C172_SOURCE))
    sources: list[tuple[bpy.types.Object, tuple[float, float, float, float]]] = []
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        if obj.name.startswith("Cessna_Exterior_Body"):
            sources.append((obj, (0.84, 0.87, 0.89, 1.0)))
        elif obj.name.startswith("Cessna_Exterior_Glass"):
            sources.append((obj, (0.12, 0.22, 0.29, 1.0)))
    if len(sources) != 28:
        raise RuntimeError(f"Approved C172 exterior source set changed: {len(sources)}/28")
    joined = duplicate_join(sources, "C172ExteriorSource")
    normalize_grounded(joined, (9.0, 15.0))
    lods = [
        build_lod(joined, "ParkedC172_LOD0", TARGETS["ParkedC172_LOD0"]),
        build_lod(joined, "ParkedC172_LOD1", TARGETS["ParkedC172_LOD1"]),
    ]
    records = [mesh_record(lod) for lod in lods]
    export_lods(lods, C172_OUTPUT)
    return lods, records


def append_sailplane_objects(source_blend: Path) -> list[tuple[bpy.types.Object, tuple[float, float, float, float]]]:
    names = ("GLIDER", "GEAR", "GLASS")
    with bpy.data.libraries.load(str(source_blend), link=False) as (available, requested):
        requested.objects = [name for name in names if name in available.objects]
    objects = [obj for obj in requested.objects if obj is not None]
    if sorted(obj.name for obj in objects) != sorted(names):
        raise RuntimeError(f"CC0 sailplane exterior object set changed: {[obj.name for obj in objects]}")
    for obj in objects:
        bpy.context.collection.objects.link(obj)
    colors = {
        "GLIDER": (0.89, 0.90, 0.91, 1.0),
        "GEAR": (0.11, 0.12, 0.13, 1.0),
        "GLASS": (0.10, 0.20, 0.27, 1.0),
    }
    return [(obj, colors[obj.name]) for obj in objects]


def build_sailplane(source_blend: Path) -> tuple[list[bpy.types.Object], list[dict]]:
    clear_scene()
    joined = duplicate_join(append_sailplane_objects(source_blend), "SailplaneExteriorSource")
    normalize_grounded(joined, (14.0, 16.5))
    lods = [
        build_lod(joined, "ParkedSailplane_LOD0", TARGETS["ParkedSailplane_LOD0"]),
        build_lod(joined, "ParkedSailplane_LOD1", TARGETS["ParkedSailplane_LOD1"]),
    ]
    records = [mesh_record(lod) for lod in lods]
    export_lods(lods, SAILPLANE_OUTPUT)
    return lods, records


def main() -> None:
    args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    if len(args) != 1:
        raise RuntimeError("Expected exactly one external FFGL.blend path after --")
    sailplane_source = Path(args[0]).resolve()
    if sha256(C172_SOURCE) != C172_SHA256:
        raise RuntimeError("Approved CC BY C172 source is missing or hash-mismatched")
    if not sailplane_source.is_file() or sha256(sailplane_source) != SAILPLANE_BLEND_SHA256:
        raise RuntimeError("Pinned CC0 sailplane source is missing or hash-mismatched")

    _c172_lods, c172_records = build_c172()
    _sailplane_lods, sailplane_records = build_sailplane(sailplane_source)
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    manifest = {
        "schema_version": 1,
        "coordinate_contract": "Blender +Z up; output pivot centered in plan and minimum up=0; Unity imports +Y up.",
        "sources": [
            {
                "family": "powered_c172",
                "path": C172_SOURCE.relative_to(ROOT).as_posix(),
                "bytes": C172_SOURCE.stat().st_size,
                "sha256": C172_SHA256,
                "creator": "KOG_THORNS",
                "url": "https://sketchfab.com/3d-models/cessna172-d1b15841c29c43d0862667300bad55a4",
                "license_spdx": "CC-BY-4.0",
            },
            {
                "family": "sailplane",
                "external_filename": "FFGL.blend",
                "bytes": sailplane_source.stat().st_size,
                "sha256": SAILPLANE_BLEND_SHA256,
                "creator": "app4soft / EGPJET",
                "url": "https://opengameart.org/content/glider-flying",
                "license_spdx": "CC0-1.0",
                "raw_source_committed": False,
            },
        ],
        "change_notice": (
            "Static scenery derivatives retain exterior shell, gear, and canopy only; interiors, pilot, "
            "animation, source materials, textures, and interaction were removed. Geometry was joined, "
            "ground-pivoted, vertex-colored, triangulated, and deterministically decimated."
        ),
        "generator": {
            "path": Path(__file__).resolve().relative_to(ROOT).as_posix(),
            "blender_version": bpy.app.version_string,
        },
        "outputs": [
            {
                "family": "powered_c172",
                "path": C172_OUTPUT.relative_to(ROOT).as_posix(),
                "bytes": C172_OUTPUT.stat().st_size,
                "sha256": sha256(C172_OUTPUT),
                "lods": c172_records,
            },
            {
                "family": "sailplane",
                "path": SAILPLANE_OUTPUT.relative_to(ROOT).as_posix(),
                "bytes": SAILPLANE_OUTPUT.stat().st_size,
                "sha256": sha256(SAILPLANE_OUTPUT),
                "lods": sailplane_records,
                "ground_rest_contact": sailplane_contact_record(_sailplane_lods[0]),
            },
        ],
        "runtime_contract": {
            "shared_material_count": 1,
            "transparent_materials": 0,
            "animation": False,
            "collision": False,
            "interaction": False,
        },
    }
    MANIFEST_OUTPUT.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()
