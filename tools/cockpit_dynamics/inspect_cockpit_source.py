"""Inspect the imported cockpit source in Blender without modifying it.

Run with:
  blender --background --factory-startup --python inspect_cockpit_source.py -- <glb> <json>

The report is deterministic and records world-space bounds, hierarchy,
materials, and mesh statistics used by the cockpit identity manifest.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def _arguments() -> tuple[Path, Path, Path | None]:
    separator = sys.argv.index("--")
    args = sys.argv[separator + 1 :]
    if len(args) not in (2, 3):
        raise SystemExit("Expected: <source.glb> <output.json> [image-output-directory]")
    images = Path(args[2]).resolve() if len(args) == 3 else None
    return Path(args[0]).resolve(), Path(args[1]).resolve(), images


def _rounded(values) -> list[float]:
    return [round(float(value), 6) for value in values]


def _world_bounds(obj: bpy.types.Object) -> dict[str, list[float]] | None:
    if obj.type != "MESH":
        return None
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    minimum = [min(point[index] for point in points) for index in range(3)]
    maximum = [max(point[index] for point in points) for index in range(3)]
    return {
        "minimum": _rounded(minimum),
        "maximum": _rounded(maximum),
        "center": _rounded((minimum[index] + maximum[index]) * 0.5 for index in range(3)),
        "size": _rounded(maximum[index] - minimum[index] for index in range(3)),
    }


def _material_images(material: bpy.types.Material | None) -> list[str]:
    if material is None or material.node_tree is None:
        return []
    return sorted(
        {
            node.image.name
            for node in material.node_tree.nodes
            if node.type == "TEX_IMAGE" and node.image is not None
        }
    )


def _base_color_image(material: bpy.types.Material | None) -> str | None:
    if material is None or material.node_tree is None:
        return None
    for node in material.node_tree.nodes:
        if node.type != "BSDF_PRINCIPLED":
            continue
        input_socket = node.inputs.get("Base Color")
        if input_socket is None or not input_socket.is_linked:
            continue
        source = input_socket.links[0].from_node
        if source.type == "TEX_IMAGE" and source.image is not None:
            return source.image.name
    return None


def _uv_bounds(obj: bpy.types.Object) -> dict[str, list[float]] | None:
    if obj.type != "MESH" or obj.data.uv_layers.active is None:
        return None
    coordinates = [loop.uv for loop in obj.data.uv_layers.active.data]
    if not coordinates:
        return None
    minimum = [min(value[index] for value in coordinates) for index in range(2)]
    maximum = [max(value[index] for value in coordinates) for index in range(2)]
    return {"minimum": _rounded(minimum), "maximum": _rounded(maximum)}


def _mesh_components(obj: bpy.types.Object) -> list[dict[str, object]]:
    if obj.type != "MESH" or not any(
        key in obj.name for key in ("Meter_01", "Paddle", "Steering", "Dynamic_Switch")
    ):
        return []
    mesh = obj.data
    triangles_by_vertex: list[list[int]] = [[] for _ in mesh.vertices]
    polygons = list(mesh.polygons)
    for polygon_index, polygon in enumerate(polygons):
        for vertex_index in polygon.vertices:
            triangles_by_vertex[vertex_index].append(polygon_index)
    visited = [False] * len(polygons)
    records = []
    for seed in range(len(polygons)):
        if visited[seed]:
            continue
        visited[seed] = True
        queue = [seed]
        component_polygons: list[int] = []
        component_vertices: set[int] = set()
        while queue:
            polygon_index = queue.pop()
            component_polygons.append(polygon_index)
            for vertex_index in polygons[polygon_index].vertices:
                component_vertices.add(vertex_index)
                for neighbor in triangles_by_vertex[vertex_index]:
                    if not visited[neighbor]:
                        visited[neighbor] = True
                        queue.append(neighbor)
        world_points = [
            obj.matrix_world @ mesh.vertices[index].co for index in component_vertices
        ]
        minimum = [min(point[index] for point in world_points) for index in range(3)]
        maximum = [max(point[index] for point in world_points) for index in range(3)]
        records.append(
            {
                "vertexCount": len(component_vertices),
                "triangleCount": sum(
                    max(0, len(polygons[index].vertices) - 2)
                    for index in component_polygons
                ),
                "worldBounds": {
                    "minimum": _rounded(minimum),
                    "maximum": _rounded(maximum),
                    "center": _rounded(
                        (minimum[index] + maximum[index]) * 0.5 for index in range(3)
                    ),
                    "size": _rounded(
                        maximum[index] - minimum[index] for index in range(3)
                    ),
                },
            }
        )
    return sorted(
        records,
        key=lambda item: (
            -item["worldBounds"]["center"][2],
            item["worldBounds"]["center"][0],
        ),
    )


def main() -> None:
    source, destination, image_destination = _arguments()
    if not source.is_file():
        raise SystemExit(f"Source does not exist: {source}")

    bpy.ops.import_scene.gltf(filepath=str(source))
    image_records = []
    if image_destination is not None:
        image_destination.mkdir(parents=True, exist_ok=True)
        for index, image in enumerate(sorted(bpy.data.images, key=lambda item: item.name)):
            if image.name == "Render Result" or image.size[0] <= 0 or image.size[1] <= 0:
                continue
            file_name = f"{index:02d}_{image.name.replace('/', '_').replace(chr(92), '_')}.png"
            destination_path = image_destination / file_name
            image.save(filepath=str(destination_path), quality=100)
            image_records.append(
                {
                    "name": image.name,
                    "width": int(image.size[0]),
                    "height": int(image.size[1]),
                    "inspectionCopy": destination_path.as_posix(),
                }
            )
    objects = []
    for obj in sorted(bpy.context.scene.objects, key=lambda item: item.name):
        mesh = obj.data if obj.type == "MESH" else None
        objects.append(
            {
                "name": obj.name,
                "type": obj.type,
                "parent": obj.parent.name if obj.parent else None,
                "worldPosition": _rounded(obj.matrix_world.translation),
                "worldBounds": _world_bounds(obj),
                "uvBounds": _uv_bounds(obj),
                "vertexCount": len(mesh.vertices) if mesh else 0,
                "triangleCount": (
                    sum(len(polygon.vertices) - 2 for polygon in mesh.polygons)
                    if mesh
                    else 0
                ),
                "components": _mesh_components(obj),
                "materials": [
                    {
                        "name": slot.material.name if slot.material else None,
                        "images": _material_images(slot.material),
                        "baseColorImage": _base_color_image(slot.material),
                    }
                    for slot in obj.material_slots
                ],
            }
        )

    report = {
        "schema": "questflight.cockpit-source-inspection.v1",
        "source": source.as_posix(),
        "animationCount": len(bpy.data.actions),
        "objectCount": len(objects),
        "images": image_records,
        "objects": objects,
    }
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
