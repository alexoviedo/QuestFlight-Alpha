"""Create the optimized QuestFlight soaring-eagle derivative.

Run with Blender 5.x:
  blender --background --factory-startup --python tools/bird/author_golden_eagle.py -- \
    --source <downloaded-source.glb> --fbx <output.fbx> \
    --texture <output.png> --blend <output.blend>

The raw source stays outside Git. The deterministic derivative keeps its one
skinned renderer, one material, one atlas, and one flight animation.
"""

from __future__ import annotations

import argparse
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def parse_args() -> argparse.Namespace:
    args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True)
    parser.add_argument("--fbx", required=True)
    parser.add_argument("--texture", required=True)
    parser.add_argument("--blend", required=True)
    return parser.parse_args(args)


def bounds(objects: list[bpy.types.Object]) -> tuple[Vector, Vector]:
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    low = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    high = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    return low, high


def recolor_for_golden_eagle(source: bpy.types.Image, output: Path) -> bpy.types.Image:
    result = source.copy()
    result.name = "GoldenEagle_Albedo"
    pixels = list(result.pixels)
    for index in range(0, len(pixels), 4):
        red, green, blue, alpha = pixels[index : index + 4]
        maximum = max(red, green, blue)
        minimum = min(red, green, blue)
        luminance = red * 0.2126 + green * 0.7152 + blue * 0.0722
        # The source's white/gray head and flight-feather islands are neutral.
        # Re-map only those pixels into the brown/gold Golden Eagle range;
        # saturated beak/feet, black eye, and existing brown plumage remain.
        if maximum - minimum < 0.14 and 0.16 < luminance < 0.88:
            shade = max(0.0, min(1.0, (luminance - 0.16) / 0.72))
            red = 0.11 + 0.34 * shade
            green = 0.055 + 0.20 * shade
            blue = 0.025 + 0.075 * shade
        pixels[index : index + 4] = (red, green, blue, alpha)
    result.pixels = pixels
    result.filepath_raw = str(output)
    result.file_format = "PNG"
    result.save()
    result.pack()
    return result


def tint_plumage_vertex_colors(mesh_object: bpy.types.Object) -> None:
    color_attribute = mesh_object.data.color_attributes.get("Color")
    if color_attribute is None or color_attribute.domain != "CORNER":
        raise RuntimeError("Expected the source's corner-domain Color attribute.")
    group_names = {group.index: group.name for group in mesh_object.vertex_groups}
    for loop in mesh_object.data.loops:
        memberships = [group_names[item.group] for item in mesh_object.data.vertices[loop.vertex_index].groups]
        if any(name.startswith(("beak", "eye", "foot", "shin", "thigh", "pelvis", "t_"))
               for name in memberships):
            color = (1.0, 1.0, 1.0, 1.0)
        elif any(name.startswith(("head", "neck")) for name in memberships):
            color = (0.60, 0.40, 0.20, 1.0)
        elif any(name.startswith(("Wing", "w_feather", "t_feather")) for name in memberships):
            color = (0.56, 0.40, 0.28, 1.0)
        else:
            color = (0.78, 0.62, 0.48, 1.0)
        color_attribute.data[loop.index].color = color


def main() -> None:
    args = parse_args()
    source = Path(args.source).resolve()
    fbx = Path(args.fbx).resolve()
    texture = Path(args.texture).resolve()
    blend = Path(args.blend).resolve()
    for output in (fbx, texture, blend):
        output.parent.mkdir(parents=True, exist_ok=True)

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.gltf(filepath=str(source))

    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    hawk = next((obj for obj in meshes if obj.name == "Hawk"), None)
    armature = next((obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"), None)
    if hawk is None or armature is None:
        raise RuntimeError("Expected one Hawk mesh and one armature in source asset.")
    for obj in list(meshes):
        if obj != hawk:
            bpy.data.objects.remove(obj, do_unlink=True)

    low, high = bounds([hawk])
    source_wingspan = high.x - low.x
    if source_wingspan <= 0.001:
        raise RuntimeError("Source wingspan is invalid.")
    scale = 2.1 / source_wingspan
    top_roots = [obj for obj in bpy.context.scene.objects if obj.parent is None]
    for root in top_roots:
        root.scale = Vector((scale, scale, scale))
        # Source longitudinal axis is +Z; Unity visual forward is +Z after
        # Blender's -Y-forward FBX conversion.
        root.rotation_euler.x += math.radians(90.0)

    material = hawk.data.materials[0]
    source_image = bpy.data.images.get("Albedo.png")
    if material is None or source_image is None:
        raise RuntimeError("Expected source material and Albedo.png atlas.")
    derived_image = recolor_for_golden_eagle(source_image, texture)
    tint_plumage_vertex_colors(hawk)
    for node in material.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image == source_image:
            node.image = derived_image
        elif node.type == "BSDF_PRINCIPLED":
            node.inputs["Metallic"].default_value = 0.0
            node.inputs["Roughness"].default_value = 0.72
    material.name = "GoldenEagle_Mat"
    hawk.name = "GoldenEagle_Mesh"
    armature.name = "GoldenEagle_Armature"
    for action in bpy.data.actions:
        action.name = "Fly"

    bpy.context.scene.render.fps = 30
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=str(fbx),
        use_selection=True,
        object_types={"ARMATURE", "MESH", "EMPTY"},
        apply_scale_options="FBX_SCALE_ALL",
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0,
        axis_forward="-Z",
        axis_up="Y",
        path_mode="COPY",
        embed_textures=False,
    )
    print(f"Golden Eagle derivative: {fbx}")
    print(f"Golden Eagle atlas: {texture}")
    print(f"Golden Eagle Blender source: {blend}")


if __name__ == "__main__":
    main()
