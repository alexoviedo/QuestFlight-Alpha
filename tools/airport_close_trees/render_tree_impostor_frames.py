"""Render eight transparent, world-upright tree impostor frames in Blender."""

from __future__ import annotations

import argparse
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, required=True)
    return parser.parse_args(argv)


def look_at(camera, target):
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()


def main():
    args = parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    source = bpy.data.objects.get("tree_small_02_LOD1")
    if source is None:
        raise RuntimeError("Missing Poly Haven tree_small_02_LOD1 source object")
    for obj in bpy.context.scene.objects:
        obj.hide_render = obj != source and obj.type not in {"CAMERA", "LIGHT"}
    source.hide_render = False

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.film_transparent = True
    scene.render.resolution_x = 384
    scene.render.resolution_y = 512
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"
    scene.render.image_settings.compression = 35
    scene.view_settings.look = "Medium High Contrast"

    world = scene.world or bpy.data.worlds.new("QuestTreeImpostorWorld")
    scene.world = world
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.56, 0.66, 0.78, 1.0)
    background.inputs["Strength"].default_value = 0.72

    camera_data = bpy.data.cameras.new("QuestTreeImpostorCamera")
    camera = bpy.data.objects.new("QuestTreeImpostorCamera", camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera_data.type = "ORTHO"

    sun_data = bpy.data.lights.new("QuestTreeImpostorSun", "SUN")
    sun = bpy.data.objects.new("QuestTreeImpostorSun", sun_data)
    scene.collection.objects.link(sun)
    sun_data.energy = 1.35
    sun_data.angle = math.radians(18.0)
    sun.rotation_euler = (math.radians(38.0), math.radians(-24.0), math.radians(-32.0))

    corners = [source.matrix_world @ Vector(corner) for corner in source.bound_box]
    minimum = Vector((min(p.x for p in corners), min(p.y for p in corners), min(p.z for p in corners)))
    maximum = Vector((max(p.x for p in corners), max(p.y for p in corners), max(p.z for p in corners)))
    center = (minimum + maximum) * 0.5
    extent = maximum - minimum
    camera_data.ortho_scale = max(extent.z * 1.10, max(extent.x, extent.y) * 1.15)
    radius = max(extent) * 2.8

    for index in range(8):
        angle = math.tau * index / 8.0
        camera.location = center + Vector((math.sin(angle) * radius, -math.cos(angle) * radius, extent.z * 0.03))
        look_at(camera, center)
        scene.render.filepath = str(args.output_dir / f"tree_impostor_{index:02d}.png")
        bpy.ops.render.render(write_still=True)
        print(f"QF_IMPOSTOR_FRAME {index} {scene.render.filepath}")


if __name__ == "__main__":
    main()
