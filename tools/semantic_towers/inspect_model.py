"""Emit deterministic Blender scene/object metadata for a tower source model."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def parse_args() -> argparse.Namespace:
    separator = sys.argv.index("--") if "--" in sys.argv else len(sys.argv)
    parser = argparse.ArgumentParser()
    parser.add_argument("model", type=Path)
    parser.add_argument("output", type=Path)
    return parser.parse_args(sys.argv[separator + 1 :])


def main() -> None:
    args = parse_args()
    model = args.model.resolve()
    if model.suffix.lower() == ".blend":
        bpy.ops.wm.open_mainfile(filepath=str(model))
    elif model.suffix.lower() == ".fbx":
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(model))
    else:
        raise ValueError(f"Unsupported model type: {model.suffix}")

    records = []
    for obj in sorted(bpy.context.scene.objects, key=lambda item: item.name):
        world_bounds = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box] if hasattr(obj, "bound_box") else []
        world_min = [min(point[index] for point in world_bounds) for index in range(3)] if world_bounds else []
        world_max = [max(point[index] for point in world_bounds) for index in range(3)] if world_bounds else []
        record = {
            "name": obj.name,
            "type": obj.type,
            "location": [round(value, 6) for value in obj.location],
            "dimensions": [round(value, 6) for value in obj.dimensions],
            "parent": obj.parent.name if obj.parent else None,
            "hidden_render": bool(obj.hide_render),
            "world_min": [round(value, 6) for value in world_min],
            "world_max": [round(value, 6) for value in world_max],
        }
        if obj.type == "MESH":
            record.update(
                vertices=len(obj.data.vertices),
                polygons=len(obj.data.polygons),
                materials=[slot.material.name if slot.material else None for slot in obj.material_slots],
            )
        records.append(record)

    payload = {
        "schema": "questflight.semantic-tower-source-inspection-v1",
        "source": str(model),
        "objects": records,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
