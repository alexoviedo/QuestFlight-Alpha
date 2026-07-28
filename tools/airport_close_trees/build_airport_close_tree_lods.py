#!/usr/bin/env python3
"""Compile the CC0 source tree into three silhouette-compatible Quest LODs.

The raw Poly Haven blend remains outside Git.  LOD0 retains the source leaf
geometry and UVs, LOD1 uses overlapping source-positioned foliage clumps, and
LOD2 is a center-eye-facing multiview impostor quad.  Unity consumes the compact
binary and creates native Mesh assets offline.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import struct
import sys
from pathlib import Path

import bpy
from mathutils import Vector


MAGIC = b"QFCTI2\0\0"
VERSION = 2
HEADER = struct.Struct("<8sII")
MESH_HEADER = struct.Struct("<32sIII")
VERTEX = struct.Struct("<3f3f4B2f")
INDEX = struct.Struct("<H")

SOURCE_OBJECT = "tree_small_02_geometry_nodes"
LEAF_MATERIAL = "tree_small_02_leaves"
LEAF_COLLECTION = "tree_small_02_leaves_LOD0"

LOD0_NAME = "AirportCloseTree_LOD0"
LOD1_NAME = "AirportCloseTree_LOD1"
LOD2_NAME = "AirportCloseTree_LOD2"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-blend", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--hero-leaf-atlas", type=Path, required=True)
    parser.add_argument("--impostor-atlas", type=Path, required=True)
    return parser.parse_args(argv)


def configure_source(branch_probability: float, twig_probability: float):
    source = bpy.data.objects.get(SOURCE_OBJECT)
    if source is None:
        raise RuntimeError(f"Missing source object {SOURCE_OBJECT}")
    modifier = source.modifiers.get("GeometryNodes")
    collection = bpy.data.collections.get(LEAF_COLLECTION)
    if modifier is None or collection is None:
        raise RuntimeError("Poly Haven source geometry-node inputs are incomplete")

    # This profile preserves actual source foliage while bounding the evaluated
    # source to a deterministic crown that is practical to compile.
    settings = {
        "Input_9": 6,
        "Input_34": 3,
        "Input_17": branch_probability,
        "Input_16": twig_probability,
        "Input_14": 2,
        "Input_24": 5,
        "Input_26": 3,
        "Input_28": 3,
        "Input_32": collection,
        "Input_37": True,
    }
    for identifier, value in settings.items():
        modifier[identifier] = value
    source.hide_render = False
    source.hide_viewport = False
    source.update_tag()
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    depsgraph.update()
    evaluated = source.evaluated_get(depsgraph)
    evaluated_mesh = evaluated.to_mesh()
    if evaluated_mesh is None:
        raise RuntimeError("Could not evaluate Poly Haven source geometry")
    mesh = evaluated_mesh.copy()
    evaluated.to_mesh_clear()
    mesh.transform(source.matrix_world)
    mesh.calc_loop_triangles()
    return mesh


def material_name(mesh, polygon) -> str:
    index = int(polygon.material_index)
    if index < 0 or index >= len(mesh.materials) or mesh.materials[index] is None:
        return ""
    return mesh.materials[index].name


def normalized_frame(source_mesh):
    coordinates = [vertex.co for vertex in source_mesh.vertices]
    minimum_z = min(value.z for value in coordinates)
    maximum_z = max(value.z for value in coordinates)
    height = maximum_z - minimum_z
    if height <= 0.001:
        raise RuntimeError("Source tree has no usable height")
    base = [value for value in coordinates if value.z <= minimum_z + height * 0.08]
    center_x = sum(value.x for value in base) / max(1, len(base))
    center_y = sum(value.y for value in base) / max(1, len(base))
    return minimum_z, height, center_x, center_y


def to_unity(value: Vector, frame):
    minimum_z, height, center_x, center_y = frame
    return (
        (value.x - center_x) / height,
        (value.z - minimum_z) / height,
        (value.y - center_y) / height,
    )


def normal_to_unity(value: Vector):
    converted = Vector((value.x, value.z, value.y)).normalized()
    return tuple(converted)


def append_tapered_wood(vertices, indices, start_source: Vector,
                        end_source: Vector, start_radius: float,
                        end_radius: float, sides: int, frame):
    axis = end_source - start_source
    if axis.length_squared < 1e-6:
        return
    axis.normalize()
    tangent = axis.cross(Vector((0.0, 0.0, 1.0)))
    if tangent.length_squared < 0.01:
        tangent = axis.cross(Vector((0.0, 1.0, 0.0)))
    tangent.normalize()
    bitangent = axis.cross(tangent).normalized()
    first = len(vertices)
    for center, radius in ((start_source, start_radius), (end_source, end_radius)):
        for side in range(sides):
            angle = math.tau * side / sides
            outward = tangent * math.cos(angle) + bitangent * math.sin(angle)
            vertices.append((
                to_unity(center + outward * radius, frame),
                normal_to_unity(outward),
                (255, 126, 92, 52),
                (-1.0, -1.0),
            ))
    for side in range(sides):
        following = (side + 1) % sides
        a = first + side
        b = first + following
        c = first + sides + following
        d = first + sides + side
        indices.extend((a, c, b, a, d, c))


def leaf_centers(source_mesh):
    result = []
    for triangle in source_mesh.loop_triangles:
        polygon = source_mesh.polygons[triangle.polygon_index]
        if material_name(source_mesh, polygon) != LEAF_MATERIAL:
            continue
        points = [source_mesh.vertices[index].co for index in triangle.vertices]
        result.append((sum(points, Vector()) / 3.0, polygon.normal.normalized()))
    return result


def append_source_informed_wood(vertices, indices, samples, branch_count, sides, frame):
    minimum_z, height, center_x, center_y = frame
    trunk_base = Vector((center_x, center_y, minimum_z))
    trunk_top = Vector((center_x, center_y, minimum_z + height * 0.70))
    append_tapered_wood(
        vertices, indices, trunk_base, trunk_top,
        height * 0.036, height * 0.012, sides + 1, frame,
    )
    candidates = []
    for center, _normal in samples:
        relative = center - trunk_base
        normalized_height = relative.z / height
        if normalized_height < 0.46:
            continue
        radial = math.hypot(relative.x, relative.y)
        candidates.append((
            radial + normalized_height * height * 0.24,
            math.atan2(relative.y, relative.x),
            center.copy(),
        ))
    candidates.sort(reverse=True, key=lambda item: item[0])
    selected = []
    for _score, angle, center in candidates:
        if any(abs(math.atan2(math.sin(angle - other), math.cos(angle - other))) <
               math.tau / (branch_count * 1.65) for other, _point in selected):
            continue
        selected.append((angle, center))
        if len(selected) == branch_count:
            break
    for branch_index, (_angle, target) in enumerate(selected):
        start_height = 0.37 + 0.25 * (
            branch_index / max(1, len(selected) - 1)
        )
        start = Vector((center_x, center_y, minimum_z + height * start_height))
        append_tapered_wood(
            vertices, indices, start, start.lerp(target, 0.80),
            height * max(0.006, 0.014 - branch_index * 0.00045),
            height * 0.0045, sides, frame,
        )


def append_source_leaf_geometry(vertices, indices, source_mesh, frame):
    uv_layer = source_mesh.uv_layers.active
    if uv_layer is None:
        raise RuntimeError("Source leaf geometry has no UV layer")
    count = 0
    for triangle in source_mesh.loop_triangles:
        polygon = source_mesh.polygons[triangle.polygon_index]
        if material_name(source_mesh, polygon) != LEAF_MATERIAL:
            continue
        start = len(vertices)
        converted_normal = normal_to_unity(polygon.normal)
        for loop_index, vertex_index in zip(triangle.loops, triangle.vertices):
            uv = uv_layer.data[loop_index].uv
            vertices.append((
                to_unity(source_mesh.vertices[vertex_index].co, frame),
                converted_normal,
                (0, 75, 118, 48),
                (float(uv.x) + 4.0, float(uv.y)),
            ))
        # Blender-to-Unity axis conversion reverses winding.
        indices.extend((start, start + 2, start + 1))
        count += 1
    return count


def select_clump_centers(samples, frame, count):
    minimum_z, height, center_x, center_y = frame
    voxels = {}
    voxel = height * 0.105
    for center, normal in samples:
        key = tuple(math.floor(value / voxel) for value in center)
        if key not in voxels:
            voxels[key] = (center.copy(), normal.copy())
    candidates = list(voxels.values())
    candidates.sort(key=lambda item: (
        round((item[0].z - minimum_z) / height, 3),
        math.atan2(item[0].y - center_y, item[0].x - center_x),
        math.hypot(item[0].x - center_x, item[0].y - center_y),
    ))
    if len(candidates) <= count:
        return candidates
    return [
        candidates[round(index * (len(candidates) - 1) / (count - 1))]
        for index in range(count)
    ]


def append_clump_cards(vertices, indices, samples, frame):
    _minimum_z, height, _center_x, _center_y = frame
    width = height * 0.145
    vertical = height * 0.115
    for sample_index, (center, _source_normal) in enumerate(samples):
        atlas_cell = (sample_index * 5 + sample_index // 3) % 16
        cell_x = atlas_cell % 4
        cell_y = atlas_cell // 4
        inset = 0.014
        u0 = cell_x * 0.25 + inset + 2.0
        v0 = cell_y * 0.25 + inset
        u1 = (cell_x + 1) * 0.25 - inset + 2.0
        v1 = (cell_y + 1) * 0.25 - inset
        for plane in range(2):
            angle = math.tau * (plane * 0.25 + (sample_index % 7) * 0.019)
            tangent = Vector((math.cos(angle), math.sin(angle), 0.0))
            up = Vector((0.0, 0.0, 1.0))
            normal = tangent.cross(up).normalized()
            points = (
                center - tangent * width - up * vertical,
                center - tangent * width + up * vertical,
                center + tangent * width + up * vertical,
                center + tangent * width - up * vertical,
            )
            start = len(vertices)
            converted_normal = normal_to_unity(normal)
            for point, uv in zip(points, ((u0, v0), (u0, v1), (u1, v1), (u1, v0))):
                vertices.append((
                    to_unity(point, frame),
                    converted_normal,
                    (0, 72, 116, 46),
                    uv,
                ))
            indices.extend((start, start + 2, start + 1,
                            start, start + 3, start + 2))


def compile_lod0(source_mesh, density_mesh, frame):
    vertices = []
    indices = []
    samples = leaf_centers(source_mesh)
    append_source_informed_wood(vertices, indices, samples, 12, 5, frame)
    source_leaf_triangles = append_source_leaf_geometry(
        vertices, indices, source_mesh, frame
    )
    supplemental = select_clump_centers(leaf_centers(density_mesh), frame, 46)
    append_clump_cards(vertices, indices, supplemental, frame)
    return mesh_record(LOD0_NAME, vertices, indices, {
        "representation": "source-leaf-geometry-plus-volumetric-clumps",
        "source_leaf_triangles": source_leaf_triangles,
        "supplemental_foliage_clumps": len(supplemental),
    })


def compile_lod1(source_mesh, frame):
    vertices = []
    indices = []
    samples = select_clump_centers(leaf_centers(source_mesh), frame, 34)
    append_source_informed_wood(vertices, indices, samples, 8, 4, frame)
    append_clump_cards(vertices, indices, samples, frame)
    return mesh_record(LOD1_NAME, vertices, indices, {
        "representation": "volumetric-crossed-foliage-clumps",
        "foliage_clumps": len(samples),
    })


def compile_lod2():
    # Unit-height, centered upright quad. Unity rotates it around world Y using
    # the XR center-eye position and selects one of eight baked azimuth frames.
    positions = (
        (-0.50, 0.0, 0.0),
        (-0.50, 1.0, 0.0),
        (0.50, 1.0, 0.0),
        (0.50, 0.0, 0.0),
    )
    uvs = ((6.0, 0.0), (6.0, 1.0), (7.0, 1.0), (7.0, 0.0))
    vertices = [
        (position, (0.0, 0.0, 1.0), (0, 74, 116, 48), uv)
        for position, uv in zip(positions, uvs)
    ]
    return mesh_record(LOD2_NAME, vertices, [0, 2, 1, 0, 3, 2], {
        "representation": "eight-view-center-eye-impostor",
        "azimuth_frames": 8,
    })


def mesh_record(name, vertices, indices, metadata):
    if len(vertices) > 65535:
        raise RuntimeError(f"{name} exceeds UInt16 vertex contract")
    result = {
        "name": name,
        "vertices": vertices,
        "indices": indices,
        "triangle_count": len(indices) // 3,
    }
    result.update(metadata)
    return result


def write_binary(path: Path, lods):
    payload = bytearray(HEADER.pack(MAGIC, VERSION, len(lods)))
    for lod in lods:
        name = lod["name"].encode("ascii")
        payload.extend(MESH_HEADER.pack(
            name + b"\0" * (32 - len(name)),
            len(lod["vertices"]),
            len(lod["indices"]),
            lod["triangle_count"],
        ))
        for position, normal, color, uv in lod["vertices"]:
            payload.extend(VERTEX.pack(*position, *normal, *color, *uv))
        for index in lod["indices"]:
            payload.extend(INDEX.pack(index))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)


def main() -> int:
    args = parse_args()
    for required in (args.source_blend, args.hero_leaf_atlas, args.impostor_atlas):
        if not required.is_file():
            raise FileNotFoundError(required)
    source_mesh = configure_source(0.04, 0.09)
    density_mesh = configure_source(0.18, 0.18)
    frame = normalized_frame(source_mesh)
    lods = [
        compile_lod0(source_mesh, density_mesh, frame),
        compile_lod1(density_mesh, frame),
        compile_lod2(),
    ]
    write_binary(args.output, lods)
    manifest = {
        "schema": "questflight.airport-tree-impostor-source.v2",
        "runtime_asset": (
            "QuestFlight/Assets/Production/Environment/"
            "PhotorealAerialSurfaceV1/Canopy/EditorSource/"
            "AirportCloseTreeLods.bytes"
        ),
        "source": {
            "asset": "Tree Small 02",
            "author": "Poly Haven",
            "url": "https://polyhaven.com/a/tree_small_02",
            "license": "CC0 1.0 Universal",
            "license_url": "https://polyhaven.com/license",
            "source_blend_sha256": sha256(args.source_blend),
            "raw_source_committed": False,
        },
        "processing": {
            "blender_version": bpy.app.version_string,
            "generator": "tools/airport_close_trees/build_airport_close_tree_lods.py",
            "xr_center_eye_impostor": True,
            "projected_size_lod": True,
            "persistent_hysteresis": True,
            "runtime_material_growth": 0,
            "runtime_texture_growth_assets": 2,
        },
        "textures": {
            "hero_leaf_rgba": {
                "path": args.hero_leaf_atlas.as_posix(),
                "sha256": sha256(args.hero_leaf_atlas),
            },
            "eight_view_impostor_rgba": {
                "path": args.impostor_atlas.as_posix(),
                "sha256": sha256(args.impostor_atlas),
                "grid": [4, 2],
            },
        },
        "lods": [
            {
                key: value
                for key, value in lod.items()
                if key not in {"vertices", "indices"}
            }
            | {"vertices": len(lod["vertices"])}
            for lod in lods
        ],
        "output": {
            "bytes": args.output.stat().st_size,
            "sha256": sha256(args.output),
        },
        "limitations": [
            "The optimized derivative is a visual archetype, not a surveyed individual tree.",
            "Position, height, crown diameter, and tint remain driven by the committed semantic placement manifest.",
            "The far impostor is intentionally restricted to projected sizes where stereo depth loss is not resolvable.",
        ],
    }
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    args.manifest.write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(manifest["lods"], indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
