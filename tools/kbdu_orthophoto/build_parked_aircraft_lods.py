#!/usr/bin/env python3
"""Build two static exterior LODs from the approved CC BY Cessna asset.

Run with Blender in background mode. The output deliberately excludes cockpit,
interior, glass, animation, textures, and materials; Unity assigns one shared
opaque vertex-color material to the offline-batched parked instances.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

import bpy


ROOT = Path(__file__).resolve().parents[2]
SOURCE = (
    ROOT
    / "QuestFlight/Assets/Resources/QuestFlightLab/ImportedAssets/"
    "Cessna172KogThorns/cessna172.glb"
)
OUTPUT = (
    ROOT
    / "QuestFlight/Assets/Production/Environment/PhotorealTerrainV1/"
    "KBDU_ParkedAircraft_LODs.glb"
)
MANIFEST = (
    ROOT
    / "QuestFlight/Assets/Production/Environment/PhotorealTerrainV1/"
    "KBDU_ParkedAircraftSourceManifest.json"
)
SOURCE_SHA256 = "a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872"
TARGET_TRIANGLES = {"ParkedAircraft_LOD0": 3200, "ParkedAircraft_LOD1": 900}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)


def joined_exterior() -> bpy.types.Object:
    bpy.ops.import_scene.gltf(filepath=str(SOURCE))
    sources = sorted(
        (
            obj
            for obj in bpy.context.scene.objects
            if obj.type == "MESH" and obj.name.startswith("Cessna_Exterior_Body")
        ),
        key=lambda obj: obj.name,
    )
    if len(sources) != 27:
        raise RuntimeError(f"Approved exterior mesh count changed: {len(sources)}/27")

    duplicates: list[bpy.types.Object] = []
    for source in sources:
        world_matrix = source.matrix_world.copy()
        duplicate = source.copy()
        duplicate.data = source.data.copy()
        bpy.context.collection.objects.link(duplicate)
        duplicate.parent = None
        duplicate.matrix_world = world_matrix
        duplicates.append(duplicate)

    bpy.ops.object.select_all(action="DESELECT")
    for duplicate in duplicates:
        duplicate.select_set(True)
    bpy.context.view_layer.objects.active = duplicates[0]
    bpy.ops.object.join()
    joined = bpy.context.object
    joined.name = "ApprovedExteriorSource"
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    minimum_x = min(vertex.co.x for vertex in joined.data.vertices)
    maximum_x = max(vertex.co.x for vertex in joined.data.vertices)
    minimum_y = min(vertex.co.y for vertex in joined.data.vertices)
    maximum_y = max(vertex.co.y for vertex in joined.data.vertices)
    minimum_z = min(vertex.co.z for vertex in joined.data.vertices)
    center_x = (minimum_x + maximum_x) * 0.5
    center_y = (minimum_y + maximum_y) * 0.5
    for vertex in joined.data.vertices:
        vertex.co.x -= center_x
        vertex.co.y -= center_y
        vertex.co.z -= minimum_z
    joined.data.update()
    extent = max(
        maximum_x - minimum_x,
        maximum_y - minimum_y,
        max(vertex.co.z for vertex in joined.data.vertices),
    )
    if not 9.0 <= extent <= 15.0:
        raise RuntimeError(f"Parked-aircraft source scale is unsafe: {extent:.3f} m")
    return joined


def build_lod(source: bpy.types.Object, name: str, target_triangles: int) -> bpy.types.Object:
    lod = source.copy()
    lod.data = source.data.copy()
    bpy.context.collection.objects.link(lod)
    lod.name = name
    lod.data.name = name + "_Mesh"
    lod.data.calc_loop_triangles()
    source_triangles = len(lod.data.loop_triangles)
    ratio = min(1.0, target_triangles / source_triangles)
    modifier = lod.modifiers.new("DeterministicExteriorDecimation", "DECIMATE")
    modifier.decimate_type = "COLLAPSE"
    modifier.ratio = ratio
    modifier.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = lod
    bpy.ops.object.select_all(action="DESELECT")
    lod.select_set(True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    triangulate = lod.modifiers.new("RuntimeTriangles", "TRIANGULATE")
    bpy.ops.object.modifier_apply(modifier=triangulate.name)
    lod.data.materials.clear()
    lod.data.validate(clean_customdata=False)
    lod.data.update(calc_edges=True)
    return lod


def mesh_record(obj: bpy.types.Object) -> dict:
    obj.data.calc_loop_triangles()
    coordinates = [vertex.co for vertex in obj.data.vertices]
    return {
        "name": obj.name,
        "vertices": len(obj.data.vertices),
        "triangles": len(obj.data.loop_triangles),
        "bounds_meters": {
            "minimum": [min(value[index] for value in coordinates) for index in range(3)],
            "maximum": [max(value[index] for value in coordinates) for index in range(3)],
        },
    }


def main() -> None:
    if not SOURCE.is_file() or sha256(SOURCE) != SOURCE_SHA256:
        raise RuntimeError("Approved CC BY aircraft source is missing or hash-mismatched")
    clear_scene()
    source = joined_exterior()
    source.data.calc_loop_triangles()
    source_triangles = len(source.data.loop_triangles)
    lods = [build_lod(source, name, target) for name, target in TARGET_TRIANGLES.items()]
    bpy.data.objects.remove(source, do_unlink=True)

    bpy.ops.object.select_all(action="DESELECT")
    for lod in lods:
        lod.select_set(True)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(OUTPUT),
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
        export_materials="NONE",
        export_extras=False,
        export_cameras=False,
        export_lights=False,
        export_animations=False,
    )

    manifest = {
        "schema_version": 1,
        "source": {
            "path": SOURCE.relative_to(ROOT).as_posix(),
            "bytes": SOURCE.stat().st_size,
            "sha256": SOURCE_SHA256,
            "creator": "KOG_THORNS",
            "canonical_url": "https://sketchfab.com/3d-models/cessna172-d1b15841c29c43d0862667300bad55a4",
            "license": "Creative Commons Attribution 4.0 International",
            "license_spdx": "CC-BY-4.0",
            "license_url": "https://creativecommons.org/licenses/by/4.0/",
        },
        "change_notice": (
            "Project-generated static scenery adaptation: retained only exterior body geometry; "
            "removed cockpit, interior, glass, textures, materials, branding presentation, and animation; "
            "joined and decimated into two exterior-only opaque LOD meshes."
        ),
        "generator": {
            "path": Path(__file__).resolve().relative_to(ROOT).as_posix(),
            "sha256": sha256(Path(__file__).resolve()),
            "blender_version": bpy.app.version_string,
        },
        "source_exterior_triangles": source_triangles,
        "lods": [mesh_record(lod) for lod in lods],
        "output": {
            "path": OUTPUT.relative_to(ROOT).as_posix(),
            "bytes": OUTPUT.stat().st_size,
            "sha256": sha256(OUTPUT),
        },
        "runtime_contract": {
            "parked_instance_count": 3,
            "interior_included": False,
            "glass_or_transparency_included": False,
            "textures_included": False,
            "materials_included": False,
            "animation_included": False,
            "active_aircraft_hierarchy_dependency": False,
            "unity_instances_are_offline_combined_per_lod": True,
            "shared_runtime_material_count": 1,
        },
        "limitations": [
            "This scenery adaptation is a visual scale cue, not a flyable or interactive aircraft.",
            "CC BY covers model copyright, not manufacturer trademarks or endorsement.",
            "The product must retain KOG_THORNS attribution and this change notice.",
        ],
    }
    MANIFEST.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(manifest, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
