#!/usr/bin/env python3
"""Inspect a Blender/FBX vegetation source without importing it into Unity.

Run with Blender in background mode.  Everything after ``--`` is interpreted as
``SOURCE OUTPUT_JSON``.  The report is deliberately deterministic and contains
only geometry/material facts needed by the offline vegetation compiler.
"""

from __future__ import annotations

import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def _arguments() -> tuple[Path, Path]:
    try:
        separator = sys.argv.index("--")
    except ValueError as exc:
        raise SystemExit("Expected -- SOURCE OUTPUT_JSON") from exc
    args = sys.argv[separator + 1 :]
    if len(args) != 2:
        raise SystemExit("Expected -- SOURCE OUTPUT_JSON")
    return Path(args[0]).resolve(), Path(args[1]).resolve()


def _load(source: Path) -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if source.suffix.lower() == ".blend":
        bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False)
        return
    if source.suffix.lower() == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(source), use_anim=False)
        return
    raise SystemExit(f"Unsupported source format: {source.suffix}")


def _world_bounds(obj: bpy.types.Object) -> tuple[list[float], list[float]]:
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    minimum = [min(point[index] for point in points) for index in range(3)]
    maximum = [max(point[index] for point in points) for index in range(3)]
    return minimum, maximum


def _round_vector(values: list[float]) -> list[float]:
    return [round(float(value), 5) for value in values]


def main() -> None:
    source, output = _arguments()
    _load(source)

    dependencies = []
    for image in bpy.data.images:
        if image.source == "FILE" and image.filepath:
            dependencies.append(Path(bpy.path.abspath(image.filepath)).name)

    object_reports = []
    scene_minimum = [math.inf, math.inf, math.inf]
    scene_maximum = [-math.inf, -math.inf, -math.inf]
    total_vertices = 0
    total_triangles = 0
    for obj in sorted(bpy.data.objects, key=lambda item: item.name.casefold()):
        if obj.type != "MESH" or obj.hide_render:
            continue
        evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = evaluated.to_mesh()
        mesh.calc_loop_triangles()
        triangles = len(mesh.loop_triangles)
        vertices = len(mesh.vertices)
        minimum, maximum = _world_bounds(obj)
        for index in range(3):
            scene_minimum[index] = min(scene_minimum[index], minimum[index])
            scene_maximum[index] = max(scene_maximum[index], maximum[index])
        materials = [slot.material.name if slot.material else "" for slot in obj.material_slots]
        object_reports.append(
            {
                "name": obj.name,
                "vertices": vertices,
                "triangles": triangles,
                "dimensions": _round_vector(list(obj.dimensions)),
                "bounds_min": _round_vector(minimum),
                "bounds_max": _round_vector(maximum),
                "materials": materials,
            }
        )
        total_vertices += vertices
        total_triangles += triangles
        evaluated.to_mesh_clear()

    report = {
        "schema": "questflight.vegetation-source-inspection.v1",
        "source_name": source.name,
        "source_bytes": source.stat().st_size,
        "mesh_objects": len(object_reports),
        "vertices": total_vertices,
        "triangles": total_triangles,
        "bounds_min": _round_vector(scene_minimum) if object_reports else [],
        "bounds_max": _round_vector(scene_maximum) if object_reports else [],
        "dimensions": _round_vector(
            [scene_maximum[index] - scene_minimum[index] for index in range(3)]
        ) if object_reports else [],
        "materials": sorted(material.name for material in bpy.data.materials),
        "texture_dependencies": sorted(set(dependencies), key=str.casefold),
        "objects": object_reports,
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key != "objects"}))


if __name__ == "__main__":
    main()
