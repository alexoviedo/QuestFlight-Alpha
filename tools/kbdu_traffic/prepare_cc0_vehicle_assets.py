r"""Inspect and prepare the CC0 traffic vehicle source meshes in Blender.

This script is intentionally executed by Blender rather than at product runtime.
Raw source FBX files live in the external artifact cache.  The Unity project only
receives the optimized, normalized derivatives produced by this script.

Report mode example:

  blender --background --python prepare_cc0_vehicle_assets.py -- \
    --mode report --source "C:\external\Sedan.fbx" \
    --report "C:\artifacts\sedan.json"
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def parse_args() -> argparse.Namespace:
    arguments = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", choices=("report", "prepare"), default="report")
    parser.add_argument("--source", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument("--output")
    parser.add_argument("--name", default="KBDU_TrafficVehicle")
    parser.add_argument("--target-width", type=float)
    parser.add_argument("--target-length", type=float)
    parser.add_argument("--target-height", type=float)
    parser.add_argument("--decimate-ratio", type=float, default=0.58)
    return parser.parse_args(arguments)


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (
        bpy.data.meshes,
        bpy.data.curves,
        bpy.data.materials,
        bpy.data.images,
    ):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def import_source(source: Path) -> None:
    suffix = source.suffix.lower()
    if suffix == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(source))
    elif suffix == ".obj":
        bpy.ops.wm.obj_import(filepath=str(source))
    elif suffix == ".blend":
        raise ValueError("Use an interchange source (.fbx or .obj), not a .blend file.")
    else:
        raise ValueError(f"Unsupported vehicle source: {source}")


def transformed_bounds(mesh_objects: list[bpy.types.Object]) -> tuple[Vector, Vector]:
    minimum = Vector((math.inf, math.inf, math.inf))
    maximum = Vector((-math.inf, -math.inf, -math.inf))
    for obj in mesh_objects:
        for corner in obj.bound_box:
            point = obj.matrix_world @ Vector(corner)
            minimum.x = min(minimum.x, point.x)
            minimum.y = min(minimum.y, point.y)
            minimum.z = min(minimum.z, point.z)
            maximum.x = max(maximum.x, point.x)
            maximum.y = max(maximum.y, point.y)
            maximum.z = max(maximum.z, point.z)
    return minimum, maximum


def inspect_source(source: Path) -> dict:
    clear_scene()
    import_source(source)
    mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if not mesh_objects:
        raise RuntimeError(f"No mesh objects were imported from {source}")

    total_vertices = 0
    total_triangles = 0
    objects = []
    material_names: set[str] = set()
    for obj in sorted(mesh_objects, key=lambda item: item.name):
        mesh = obj.data
        mesh.calc_loop_triangles()
        total_vertices += len(mesh.vertices)
        total_triangles += len(mesh.loop_triangles)
        object_materials = [
            slot.material.name if slot.material is not None else "<missing>"
            for slot in obj.material_slots
        ]
        material_names.update(object_materials)
        objects.append(
            {
                "name": obj.name,
                "vertices": len(mesh.vertices),
                "triangles": len(mesh.loop_triangles),
                "materials": object_materials,
                "location": [round(value, 6) for value in obj.location],
                "rotation_degrees": [
                    round(math.degrees(value), 4) for value in obj.rotation_euler
                ],
                "scale": [round(value, 6) for value in obj.scale],
            }
        )

    minimum, maximum = transformed_bounds(mesh_objects)
    size = maximum - minimum
    return {
        "schema": "questflight.cc0-vehicle-source-inspection-v1",
        "source": str(source),
        "object_count": len(mesh_objects),
        "total_vertices": total_vertices,
        "total_triangles": total_triangles,
        "bounds_min": [round(value, 6) for value in minimum],
        "bounds_max": [round(value, 6) for value in maximum],
        "bounds_size": [round(value, 6) for value in size],
        "materials": sorted(material_names),
        "objects": objects,
    }


def semantic_color(material_name: str) -> tuple[float, float, float, float]:
    """Encode an exclusive material class in the imported mesh color.

    Runtime decoding:
      red=paint, green=glass, blue=tire, yellow=wheel/trim,
      magenta=headlight and cyan=tail light.
    """

    normalized = material_name.casefold()
    if "window" in normalized or "glass" in normalized:
        return (0.0, 1.0, 0.0, 1.0)
    if "tire" in normalized or "tyre" in normalized:
        return (0.0, 0.0, 1.0, 1.0)
    if "wheel" in normalized or "rim" in normalized:
        return (1.0, 1.0, 0.0, 1.0)
    if "headlight" in normalized or "head light" in normalized:
        return (1.0, 0.0, 1.0, 1.0)
    if (
        "rear light" in normalized
        or "taillight" in normalized
        or "tail light" in normalized
    ):
        return (0.0, 1.0, 1.0, 1.0)
    return (1.0, 0.0, 0.0, 1.0)


def assign_semantic_colors(obj: bpy.types.Object) -> None:
    mesh = obj.data
    attribute = mesh.color_attributes.get("TrafficSemantic")
    if attribute is None:
        attribute = mesh.color_attributes.new(
            name="TrafficSemantic", type="BYTE_COLOR", domain="CORNER"
        )
    material_names = [
        material.name if material is not None else "body"
        for material in mesh.materials
    ]
    for polygon in mesh.polygons:
        name = (
            material_names[polygon.material_index]
            if polygon.material_index < len(material_names)
            else "body"
        )
        color = semantic_color(name)
        for loop_index in polygon.loop_indices:
            attribute.data[loop_index].color = color
    mesh.color_attributes.active_color = attribute
    mesh.color_attributes.render_color_index = mesh.color_attributes.find(
        attribute.name
    )


def apply_decimation(obj: bpy.types.Object, ratio: float) -> None:
    if ratio >= 0.999:
        return
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    modifier = obj.modifiers.new(name="QuestTrafficReduction", type="DECIMATE")
    modifier.decimate_type = "COLLAPSE"
    modifier.ratio = max(0.2, min(1.0, ratio))
    modifier.use_collapse_triangulate = True
    modifier.delimit = {"MATERIAL", "SEAM", "SHARP", "UV"}
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.select_set(False)


def merge_and_normalize(
    mesh_objects: list[bpy.types.Object],
    name: str,
    target_width: float,
    target_length: float,
    target_height: float,
) -> bpy.types.Object:
    bpy.ops.object.select_all(action="DESELECT")
    for obj in mesh_objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = mesh_objects[0]
    bpy.ops.object.join()
    merged = bpy.context.view_layer.objects.active
    merged.name = name
    merged.data.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    minimum, maximum = transformed_bounds([merged])
    source_size = maximum - minimum
    scale = Vector(
        (
            target_width / source_size.x,
            target_length / source_size.y,
            target_height / source_size.z,
        )
    )
    center_x = (minimum.x + maximum.x) * 0.5
    center_y = (minimum.y + maximum.y) * 0.5
    for vertex in merged.data.vertices:
        width = (vertex.co.x - center_x) * scale.x
        length = (vertex.co.y - center_y) * scale.y
        height = (vertex.co.z - minimum.z) * scale.z
        # Unity product convention is +Y up and +Z vehicle-forward.  RGSDev
        # sources are +Z up with the vehicle nose along -Y.
        vertex.co.x = width
        vertex.co.y = height
        vertex.co.z = -length

    # Semantic data now carries every surface class; one material slot guarantees
    # one Unity submesh and therefore one instanced draw per body style.
    for polygon in merged.data.polygons:
        polygon.material_index = 0
    merged.data.materials.clear()
    semantic_material = bpy.data.materials.new("TrafficSemantic")
    merged.data.materials.append(semantic_material)
    merged.data.update()
    return merged


def prepare_source(
    source: Path,
    output: Path,
    name: str,
    target_width: float,
    target_length: float,
    target_height: float,
    ratio: float,
) -> dict:
    clear_scene()
    import_source(source)
    mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if not mesh_objects:
        raise RuntimeError(f"No mesh objects were imported from {source}")
    for obj in mesh_objects:
        assign_semantic_colors(obj)
        apply_decimation(obj, ratio)
    merged = merge_and_normalize(
        mesh_objects,
        name,
        target_width,
        target_length,
        target_height,
    )

    bpy.ops.object.select_all(action="DESELECT")
    merged.select_set(True)
    bpy.context.view_layer.objects.active = merged
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=str(output),
        use_selection=True,
        use_mesh_modifiers=True,
        use_triangles=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="AUTO",
        embed_textures=False,
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        mesh_smooth_type="FACE",
        colors_type="LINEAR",
    )
    result = inspect_exported_object(merged)
    result.update(
        {
            "schema": "questflight.cc0-vehicle-derivative-v1",
            "source": str(source),
            "output": str(output),
            "name": name,
            "decimate_ratio": ratio,
            "target_dimensions_meters": [
                target_width,
                target_length,
                target_height,
            ],
        }
    )
    return result


def inspect_exported_object(obj: bpy.types.Object) -> dict:
    mesh = obj.data
    mesh.calc_loop_triangles()
    minimum, maximum = transformed_bounds([obj])
    semantic_counts: dict[str, int] = {}
    attribute = mesh.color_attributes.get("TrafficSemantic")
    if attribute is not None:
        for polygon in mesh.polygons:
            color = attribute.data[polygon.loop_indices[0]].color
            key = ",".join(f"{component:.2f}" for component in color[:3])
            semantic_counts[key] = semantic_counts.get(key, 0) + max(
                1, len(polygon.vertices) - 2
            )
    return {
        "vertices": len(mesh.vertices),
        "triangles": len(mesh.loop_triangles),
        "bounds_min": [round(value, 6) for value in minimum],
        "bounds_max": [round(value, 6) for value in maximum],
        "bounds_size": [round(value, 6) for value in maximum - minimum],
        "submesh_material_slots": len(mesh.materials),
        "semantic_triangle_counts": semantic_counts,
    }


def main() -> None:
    args = parse_args()
    source = Path(args.source).resolve()
    report = Path(args.report).resolve()
    if not source.is_file():
        raise FileNotFoundError(source)
    if args.mode == "report":
        result = inspect_source(source)
    else:
        required = (
            args.output,
            args.target_width,
            args.target_length,
            args.target_height,
        )
        if any(value is None for value in required):
            raise ValueError(
                "Prepare mode requires --output and all target dimensions."
            )
        result = prepare_source(
            source,
            Path(args.output).resolve(),
            args.name,
            args.target_width,
            args.target_length,
            args.target_height,
            args.decimate_ratio,
        )
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
