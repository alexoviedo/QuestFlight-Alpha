#!/usr/bin/env python3
"""Render geometry-only external previews from an already opened V4 source .blend."""
from __future__ import annotations

import argparse
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


VIEWS = {
    "terminal_front": ((58.0, -58.0, 24.0), (0.0, 0.0, 4.2), 56.0),
    "terminal_aerial": ((65.0, -72.0, 78.0), (0.0, 0.0, 2.8), 54.0),
    "hangar_front": ((42.0, -13.0, 15.0), (0.0, -4.0, 3.2), 53.0),
    "hangar_aerial": ((52.0, -70.0, 72.0), (0.0, 0.0, 2.5), 52.0),
    "hangar_door": ((26.0, -23.0, 8.5), (0.0, -20.0, 2.8), 62.0),
    "fuel_oblique": ((28.0, -26.0, 16.0), (0.0, 0.0, 2.3), 58.0),
}


def parse_args() -> argparse.Namespace:
    raw = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--view", choices=sorted(VIEWS), required=True)
    parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args(raw)


def point_camera(camera: bpy.types.Object, target: tuple[float, float, float]) -> None:
    direction = Vector(target) - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def main() -> None:
    args = parse_args()
    position, target, lens = VIEWS[args.view]
    for obj in bpy.context.scene.objects:
        if obj.type == "MESH" and obj.get("lod_index", 0) != 0:
            obj.hide_render = True
        elif obj.type == "MESH":
            obj.hide_render = False
    bpy.ops.mesh.primitive_plane_add(size=180.0, location=(0.0, 0.0, -0.025))
    ground = bpy.context.object
    ground.name = "PreviewGround_NotExported"
    mat = bpy.data.materials.new("PreviewGround")
    mat.diffuse_color = (0.18, 0.20, 0.22, 1.0)
    ground.data.materials.append(mat)
    bpy.ops.object.camera_add(location=position)
    camera = bpy.context.object
    camera.name = "PreviewCamera_NotExported"
    camera.data.lens = lens
    point_camera(camera, target)
    scene = bpy.context.scene
    scene.camera = camera
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "BOTH"
    scene.display.shading.curvature_ridge_factor = 1.8
    scene.display.shading.curvature_valley_factor = 1.2
    scene.display.shading.background_type = "VIEWPORT"
    scene.display.shading.background_color = (0.075, 0.09, 0.12)
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    args.output.parent.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = str(args.output.resolve())
    bpy.ops.render.render(write_still=True)
    print(f"Rendered {args.view} to {args.output}")


if __name__ == "__main__":
    main()
