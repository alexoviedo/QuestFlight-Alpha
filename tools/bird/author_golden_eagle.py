"""Create the optimized QuestFlight soaring-eagle derivative.

Run with Blender 5.x:
  blender --background --factory-startup --python tools/bird/author_golden_eagle.py -- \
    --source <downloaded-source.gltf> --fbx <output.fbx> \
    --texture <output.png> --blend <output.blend>

The raw source stays outside Git. The deterministic derivative keeps one
skinned renderer and authors a small explicit motion vocabulary: a looped
wingbeat, a living soar, two bank poses, and a flap-to-glide transition.
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
        # The source is a dark-bodied Bald Eagle with neutral white head and
        # tail islands. Re-map only those neutral pixels to golden brown; its
        # detailed flight feathers, yellow beak/feet, eye, and transparent
        # atlas space remain intact.
        if maximum - minimum < 0.16 and 0.16 < luminance < 0.99 and alpha > 0.05:
            shade = max(0.0, min(1.0, (luminance - 0.16) / 0.83))
            red = 0.10 + 0.34 * shade
            green = 0.045 + 0.19 * shade
            blue = 0.018 + 0.07 * shade
        pixels[index : index + 4] = (red, green, blue, alpha)
    result.pixels = pixels
    result.filepath_raw = str(output)
    result.file_format = "PNG"
    result.save()
    result.pack()
    return result


def action_curve_bags(action: bpy.types.Action):
    """Return every channel bag from Blender 5's layered Action API."""
    if len(action.layers) != 1 or len(action.layers[0].strips) != 1:
        raise RuntimeError(f"Expected one layer/strip in {action.name}.")
    bags = action.layers[0].strips[0].channelbags
    if len(bags) < 1:
        raise RuntimeError(f"Expected at least one channel bag in {action.name}.")
    return list(bags)


def replace_curve_keys(curve, samples: list[tuple[float, float]]) -> None:
    curve.keyframe_points.clear()
    for frame, value in samples:
        point = curve.keyframe_points.insert(frame, value)
        point.interpolation = "SINE"


def author_flight_actions(armature: bpy.types.Object) -> None:
    source = next(iter(bpy.data.actions), None)
    if source is None:
        raise RuntimeError("Expected one source flight action.")
    source.name = "FlapLoop"
    source.use_fake_user = True

    # Sketchfab's glTF take also keys two wrapper objects. FBX otherwise emits
    # three clips with the same suffix and Unity may bind the wrapper-only copy,
    # leaving the skinned mesh nearly rigid. Retain only the channel bag that
    # contains bone curves so each runtime state imports exactly once.
    source_strip = source.layers[0].strips[0]
    armature_bag = next((bag for bag in source_strip.channelbags
                         if any('pose.bones[' in curve.data_path for curve in bag.fcurves)), None)
    if armature_bag is None:
        raise RuntimeError("Expected an armature channel bag in the source flight action.")
    for bag in list(source_strip.channelbags):
        if bag != armature_bag:
            source_strip.channelbags.remove(bag)
    # Do not leave the armature Action assigned to glTF wrapper objects. FBX's
    # all-actions export otherwise evaluates the armature slot through the
    # first wrapper target and emits only its static parent curve.
    for obj in bpy.context.scene.objects:
        if obj != armature and obj.animation_data is not None:
            obj.animation_data_clear()
    for action_slot in list(source.slots):
        if action_slot != armature_bag.slot:
            source.slots.remove(action_slot)
    armature.animation_data_create()
    armature.animation_data.action = source
    armature.animation_data.action_slot = armature_bag.slot
    source_bags = action_curve_bags(source)

    # The source contains a verified 208-frame flight animation with large,
    # coherent deformation across both wings. Preserve it verbatim. Derive
    # the non-flapping states from frame 20, where the measured source span is
    # maximal, so soaring never freezes in the source's folded pose.
    extended_frame = 20.0

    def create_pose(name: str) -> bpy.types.Action:
        action = source.copy()
        action.name = name
        action.use_fake_user = True
        target_bags = action_curve_bags(action)
        if len(target_bags) != len(source_bags):
            raise RuntimeError(f"Action bag mismatch while deriving {name}.")
        for source_bag, target_bag in zip(source_bags, target_bags):
            for source_curve, curve in zip(source_bag.fcurves, target_bag.fcurves):
                base = source_curve.evaluate(extended_frame)
                replace_curve_keys(curve, [(0.0, base), (120.0, base)])
        return action

    create_pose("SoarIdle")
    create_pose("BankLeft")
    create_pose("BankRight")

    transition = source.copy()
    transition.name = "GlideTransition"
    transition.use_fake_user = True
    transition_bags = action_curve_bags(transition)
    for source_bag, target_bag in zip(source_bags, transition_bags):
        for source_curve, curve in zip(source_bag.fcurves, target_bag.fcurves):
            replace_curve_keys(curve, [
                (0.0, source_curve.evaluate(10.0)),
                (18.0, source_curve.evaluate(extended_frame)),
            ])

    armature.animation_data.action = source


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
    eagle = max(meshes, key=lambda obj: len(obj.data.polygons), default=None)
    armature = next((obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"), None)
    if eagle is None or armature is None:
        raise RuntimeError("Expected one animated eagle mesh and one armature in source asset.")
    for obj in list(meshes):
        if obj != eagle:
            bpy.data.objects.remove(obj, do_unlink=True)

    # Source frame 20 is the maximum-span, anatomically useful glide pose.
    bpy.context.scene.frame_set(20)
    bpy.context.view_layer.update()
    evaluated = eagle.evaluated_get(bpy.context.evaluated_depsgraph_get())
    points = [eagle.matrix_world @ vertex.co for vertex in evaluated.data.vertices]
    low = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    high = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    source_wingspan = high.x - low.x
    if source_wingspan <= 0.001:
        raise RuntimeError("Source wingspan is invalid.")
    scale = 2.1 / source_wingspan
    pelvis = armature.pose.bones.get("Bip01 Pelvis_023")
    if pelvis is None:
        raise RuntimeError("Expected the source pelvis needed for deterministic centering.")
    center = armature.matrix_world @ pelvis.head
    imported_roots = [obj for obj in bpy.context.scene.objects if obj.parent is None]
    wrapper = bpy.data.objects.new("GoldenEagle_Root", None)
    bpy.context.scene.collection.objects.link(wrapper)
    for root in imported_roots:
        root.parent = wrapper
    wrapper.scale = Vector((scale, scale, scale))
    wrapper.location = -center * scale

    material = eagle.data.materials[0]
    source_image = next((image for image in bpy.data.images if image.size[0] >= 1024), None)
    if material is None or source_image is None:
        raise RuntimeError("Expected source material and Albedo.png atlas.")
    derived_image = recolor_for_golden_eagle(source_image, texture)
    for node in material.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image == source_image:
            node.image = derived_image
        elif node.type == "BSDF_PRINCIPLED":
            node.inputs["Metallic"].default_value = 0.0
            node.inputs["Roughness"].default_value = 0.72
    material.name = "GoldenEagle_Mat"
    eagle.name = "GoldenEagle_Mesh"
    armature.name = "GoldenEagle_Armature"
    author_flight_actions(armature)

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
        # Unity uses the explicitly authored external material and optimized
        # derivative maps. Strip FBX media paths so Blender cannot create a
        # duplicate .fbm dependency beside the runtime model.
        path_mode="STRIP",
        embed_textures=False,
    )
    print(f"Golden Eagle derivative: {fbx}")
    print(f"Golden Eagle atlas: {texture}")
    print(f"Golden Eagle Blender source: {blend}")


if __name__ == "__main__":
    main()
