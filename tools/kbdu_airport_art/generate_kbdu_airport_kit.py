#!/usr/bin/env python3
"""Generate a deterministic, shared-material KBDU airport kit with three LODs in Blender."""

from __future__ import annotations

import hashlib
import json
import math
import os
import sys
from pathlib import Path

import bpy
from mathutils import Vector


SEED = 20260713
ROOT = Path(__file__).resolve().parents[2]
DEFAULT_ASSET_DIR = ROOT / "QuestFlight" / "Assets" / "Production" / "Environment" / "AirportKit"
DEFAULT_SOURCE_DIR = ROOT / "tools" / "kbdu_airport_art" / "source"
DEFAULT_MANIFEST = ROOT / "tools" / "kbdu_airport_art" / "kbdu_airport_art_manifest.json"


def parse_args() -> dict[str, Path]:
    arguments = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    values = {
        "asset_dir": DEFAULT_ASSET_DIR,
        "source_dir": DEFAULT_SOURCE_DIR,
        "manifest": DEFAULT_MANIFEST,
        "preview_dir": Path(os.environ.get("QUESTFLIGHT_AIRPORT_PREVIEW_DIR", ROOT.parent / "airport_art_preview")),
    }
    for index in range(0, len(arguments), 2):
        key = arguments[index].lstrip("-").replace("-", "_")
        if key not in values or index + 1 >= len(arguments):
            raise ValueError(f"Unknown/incomplete argument: {arguments[index:]}")
        values[key] = Path(arguments[index + 1]).resolve()
    return values


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def record(path: Path) -> dict:
    return {"path": path.relative_to(ROOT).as_posix(), "bytes": path.stat().st_size, "sha256": sha256(path)}


def create_shared_atlas(path: Path) -> bpy.types.Image:
    path.parent.mkdir(parents=True, exist_ok=True)
    width = height = 512
    palette = [
        (0.42, 0.47, 0.48),  # wall
        (0.27, 0.31, 0.32),  # roof
        (0.34, 0.38, 0.39),  # door
        (0.78, 0.60, 0.18),  # accent
        (0.32, 0.34, 0.34),  # fence
        (0.88, 0.42, 0.05),  # sign
        (0.30, 0.31, 0.30),  # dark utility
        (0.56, 0.55, 0.51),  # concrete
    ]
    pixels: list[float] = []
    for y in range(height):
        for x in range(width):
            cell = min(7, (x * 4 // width) + (y * 2 // height) * 4)
            base = palette[cell]
            local_x = x % (width // 4)
            local_y = y % (height // 2)
            fine_noise = ((((x * 73856093) ^ (y * 19349663) ^ SEED) & 255) / 255.0 - 0.5) * 0.018
            broad_noise = math.sin((x + y * 0.37 + cell * 19.0) * 0.071) * 0.008
            variation = fine_noise + broad_noise
            grime = 0.0

            # Each surface keeps a full 128x256 atlas cell. These bounded cues
            # survive mip reduction without requiring another material or map.
            if cell == 0:  # metal wall: vertical corrugation and lower-edge grime
                variation += math.sin(local_x * math.tau / 10.0) * 0.012
                seam = local_x % 24
                if seam <= 1:
                    variation -= 0.042
                elif seam == 2:
                    variation += 0.022
                if local_y % 86 <= 1:
                    variation -= 0.018
                grime = (1.0 - local_y / 255.0) ** 3 * 0.048
            elif cell == 1:  # roof: standing seams and soft weather streaks
                seam = local_x % 18
                if seam <= 1:
                    variation -= 0.046
                elif seam == 2:
                    variation += 0.026
                variation += math.sin(local_y * 0.055 + local_x * 0.13) * 0.006
                grime = (0.35 + 0.65 * (local_y / 255.0)) * 0.012
            elif cell == 2:  # hangar doors: panel grid, center joint, bottom dirt
                vertical = local_x % 32
                horizontal = local_y % 58
                if vertical <= 1 or horizontal <= 1:
                    variation -= 0.040
                elif vertical == 2 or horizontal == 2:
                    variation += 0.018
                if abs(local_x - 64) <= 1:
                    variation -= 0.035
                grime = (1.0 - local_y / 255.0) ** 2 * 0.052
            elif cell in (3, 4, 6):
                variation += math.sin(local_x * 0.18 + local_y * 0.035) * 0.007
                grime = (1.0 - local_y / 255.0) ** 2 * 0.018
            elif cell == 7:  # concrete: broad mottling without high-frequency speckle
                variation += math.sin(local_x * 0.045) * math.sin(local_y * 0.052) * 0.018

            pixels.extend(
                (
                    max(0.0, min(1.0, base[0] + variation - grime * 0.75)),
                    max(0.0, min(1.0, base[1] + variation - grime * 0.88)),
                    max(0.0, min(1.0, base[2] + variation - grime)),
                    1.0,
                )
            )
    image = bpy.data.images.new("KBDU_AirportSharedAtlas", width=width, height=height, alpha=False)
    image.pixels.foreach_set(pixels)
    image.filepath_raw = str(path)
    image.file_format = "PNG"
    image.save()
    return image


def material(name: str, image: bpy.types.Image) -> bpy.types.Material:
    value = bpy.data.materials.new(name)
    value.diffuse_color = (0.5, 0.5, 0.5, 1.0)
    value.use_nodes = True
    node = value.node_tree.nodes.get("Principled BSDF")
    texture = value.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image = image
    value.node_tree.links.new(texture.outputs["Color"], node.inputs["Base Color"])
    node.inputs["Metallic"].default_value = 0.12
    node.inputs["Roughness"].default_value = 0.56
    value.use_backface_culling = True
    return value


def set_uv_cell(obj: bpy.types.Object, cell: int) -> None:
    mesh = obj.data
    uv_layer = mesh.uv_layers.active or mesh.uv_layers.new(name="SharedAtlasUV")
    column = cell % 4
    row = cell // 4
    u0, v0 = column / 4 + 0.025, row / 2 + 0.05
    u1, v1 = (column + 1) / 4 - 0.025, (row + 1) / 2 - 0.05
    for polygon in mesh.polygons:
        for offset, loop_index in enumerate(polygon.loop_indices):
            corners = ((u0, v0), (u1, v0), (u1, v1), (u0, v1))
            uv_layer.data[loop_index].uv = corners[offset % 4]


def cube(
    name: str,
    dimensions: tuple[float, float, float],
    location: tuple[float, float, float],
    parent: bpy.types.Object,
    assigned_surface: tuple[bpy.types.Material, int],
    bevel: float = 0.0,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cube_add(location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(assigned_surface[0])
    set_uv_cell(obj, assigned_surface[1])
    obj.parent = parent
    if bevel > 0.0:
        modifier = obj.modifiers.new("BoundedBevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


def mesh_object(
    name: str,
    vertices: list[tuple[float, float, float]],
    faces: list[tuple[int, ...]],
    face_materials: list[int],
    parent: bpy.types.Object,
    surfaces: list[tuple[bpy.types.Material, int]],
) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    mesh.materials.append(surfaces[0][0])
    uv_layer = mesh.uv_layers.new(name="SharedAtlasUV")
    for polygon, surface_index in zip(mesh.polygons, face_materials):
        polygon.material_index = 0
        cell = surfaces[surface_index][1]
        column, row = cell % 4, cell // 4
        u0, v0 = column / 4 + 0.025, row / 2 + 0.05
        u1, v1 = (column + 1) / 4 - 0.025, (row + 1) / 2 - 0.05
        corners = ((u0, v0), (u1, v0), (u1, v1), (u0, v1))
        for offset, loop_index in enumerate(polygon.loop_indices):
            uv_layer.data[loop_index].uv = corners[offset % 4]
    for polygon in mesh.polygons:
        polygon.use_smooth = False
    return obj


def gable_body(name: str, width: float, depth: float, wall: float, ridge: float, parent: bpy.types.Object, mats: dict) -> bpy.types.Object:
    profile = [(-width / 2, 0.0), (width / 2, 0.0), (width / 2, wall), (0.0, ridge), (-width / 2, wall)]
    vertices = [(x, -depth / 2, z) for x, z in profile] + [(x, depth / 2, z) for x, z in profile]
    faces = [(0, 1, 2, 3, 4), (9, 8, 7, 6, 5), (0, 5, 6, 1), (1, 6, 7, 2), (2, 7, 8, 3), (3, 8, 9, 4), (4, 9, 5, 0)]
    return mesh_object(name, vertices, faces, [0, 0, 0, 0, 1, 1, 0], parent, [mats["wall"], mats["roof"]])


def shed_body(name: str, width: float, depth: float, low: float, high: float, parent: bpy.types.Object, mats: dict) -> bpy.types.Object:
    vertices = [
        (-width / 2, -depth / 2, 0), (width / 2, -depth / 2, 0), (width / 2, -depth / 2, low), (-width / 2, -depth / 2, high),
        (-width / 2, depth / 2, 0), (width / 2, depth / 2, 0), (width / 2, depth / 2, low), (-width / 2, depth / 2, high),
    ]
    faces = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    return mesh_object(name, vertices, faces, [0, 0, 0, 0, 1, 0], parent, [mats["wall"], mats["roof"]])


def quonset_body(name: str, width: float, depth: float, wall: float, segments: int, parent: bpy.types.Object, mats: dict) -> bpy.types.Object:
    radius = width / 2
    profile = [(-radius, 0.0), (radius, 0.0), (radius, wall)]
    for index in range(1, segments + 1):
        angle = math.pi * index / segments
        profile.append((math.cos(angle) * radius, wall + math.sin(angle) * radius))
    # Explicit double-wound triangle fans make both end caps robust to importer
    # winding/culling differences. With back-face culling, only one coincident
    # winding survives from either direction, so there is no front-facing z-fight.
    cap_center_z = wall + radius * 0.34
    count = len(profile)
    front_profile = [(x, -depth / 2, z) for x, z in profile]
    rear_profile = [(x, depth / 2, z) for x, z in profile]
    vertices = (
        front_profile
        + rear_profile
        + [(0.0, -depth / 2, cap_center_z), (0.0, depth / 2, cap_center_z)]
        # Separate vertices retain opposite cap normals through glTF vertex
        # welding while remaining exactly coplanar for back-face culling.
        + front_profile
        + rear_profile
        + [(0.0, -depth / 2, cap_center_z), (0.0, depth / 2, cap_center_z)]
    )
    front_center = count * 2
    rear_center = front_center + 1
    inward_front_start = rear_center + 1
    inward_rear_start = inward_front_start + count
    inward_front_center = inward_rear_start + count
    inward_rear_center = inward_front_center + 1
    faces = []
    face_materials = []
    for index in range(count):
        next_index = (index + 1) % count
        faces.append((front_center, index, next_index))
        faces.append((inward_front_center, inward_front_start + next_index, inward_front_start + index))
        faces.append((rear_center, count + next_index, count + index))
        faces.append((inward_rear_center, inward_rear_start + index, inward_rear_start + next_index))
        face_materials.extend((0, 0, 0, 0))
    for index in range(count):
        next_index = (index + 1) % count
        # The profile is counter-clockwise in X/Z, so this winding points the
        # extruded shell outward rather than into the hangar volume.
        faces.append((index, count + index, count + next_index, next_index))
        face_materials.append(1 if index >= 2 and index < count - 1 else 0)
    obj = mesh_object(name, vertices, faces, face_materials, parent, [mats["wall"], mats["roof"]])
    # Project the shared wall cell once across each full cap. Mapping every fan
    # triangle independently creates radial texture repetition that can resemble
    # separated geometry at distance.
    wall_u0, wall_v0 = 0.025, 0.05
    wall_u1, wall_v1 = 0.25 - 0.025, 0.5 - 0.05
    uv_layer = obj.data.uv_layers.active
    for polygon in obj.data.polygons:
        ys = [obj.data.vertices[index].co.y for index in polygon.vertices]
        if max(ys) - min(ys) > 1e-5:
            continue
        for loop_index in polygon.loop_indices:
            coordinate = obj.data.vertices[obj.data.loops[loop_index].vertex_index].co
            uv_layer.data[loop_index].uv = (
                wall_u0 + ((coordinate.x + radius) / width) * (wall_u1 - wall_u0),
                wall_v0 + (coordinate.z / (wall + radius)) * (wall_v1 - wall_v0),
            )
    return obj


def root_object(asset_id: str, lod: int, grid_location: tuple[float, float, float]) -> bpy.types.Object:
    obj = bpy.data.objects.new(f"{asset_id}_LOD{lod}", None)
    bpy.context.collection.objects.link(obj)
    obj.location = grid_location
    obj["questflight_asset"] = asset_id
    obj["questflight_lod"] = lod
    return obj


def door_and_trim(parent: bpy.types.Object, width: float, depth: float, height: float, mats: dict, detail: int) -> None:
    cube(parent.name + "_Door", (width * 0.62, 0.12, height * 0.66), (0, -depth / 2 - 0.07, height * 0.33), parent, mats["door"])
    if detail >= 1:
        beam = 0.18
        cube(parent.name + "_DoorTop", (width * 0.66, 0.16, beam), (0, -depth / 2 - 0.09, height * 0.68), parent, mats["accent"])
        for x in (-width * 0.33, width * 0.33):
            cube(parent.name + f"_DoorSide_{x:+.1f}", (beam, 0.16, height * 0.70), (x, -depth / 2 - 0.09, height * 0.35), parent, mats["accent"])


def build_hangar(asset_id: str, kind: str, width: float, depth: float, wall: float, ridge: float, position: tuple[float, float, float], mats: dict) -> None:
    for lod in range(3):
        root = root_object(asset_id, lod, position)
        if kind == "gable":
            gable_body(root.name + "_Body", width, depth, wall, ridge, root, mats)
        elif kind == "quonset":
            quonset_body(root.name + "_Body", width, depth, 1.4, (14, 8, 4)[lod], root, mats)
        else:
            shed_body(root.name + "_Body", width, depth, wall - 1.0, ridge, root, mats)
        door_and_trim(root, width, depth, wall, mats, 2 - lod)
        if lod == 0:
            for x in (-width * 0.37, width * 0.37):
                cube(root.name + f"_Bollard_{x:+.1f}", (0.24, 0.24, 1.1), (x, -depth / 2 - 0.8, 0.55), root, mats["accent"], 0.04)


def build_service(asset_id: str, position: tuple[float, float, float], mats: dict) -> None:
    for lod in range(3):
        root = root_object(asset_id, lod, position)
        cube(root.name + "_Office", (9, 7, 4.2), (0, 0, 2.1), root, mats["wall"], 0.12 if lod == 0 else 0.0)
        cube(root.name + "_Canopy", (15, 8, 0.45), (5.5, -0.5, 4.0), root, mats["roof"])
        posts = 3 if lod == 0 else 2
        for index in range(posts):
            x = 1.0 + index * (9.0 / max(1, posts - 1))
            cube(root.name + f"_CanopyPost_{index}", (0.24, 0.24, 4.0), (x, -3.2, 2.0), root, mats["accent"])
        if lod < 2:
            segments = 12 if lod == 0 else 8
            bpy.ops.mesh.primitive_cylinder_add(vertices=segments, radius=1.25, depth=5.5, location=(6.5, 2.3, 1.25), rotation=(0, math.pi / 2, 0))
            tank = bpy.context.object
            tank.name = root.name + "_FuelTank"
            tank.data.materials.append(mats["accent"][0])
            set_uv_cell(tank, mats["accent"][1])
            tank.parent = root


def build_fence_sign(asset_id: str, position: tuple[float, float, float], mats: dict) -> None:
    for lod in range(3):
        root = root_object(asset_id, lod, position)
        post_count = (6, 4, 2)[lod]
        for index in range(post_count):
            x = -10 + 20 * index / max(1, post_count - 1)
            cube(root.name + f"_Post_{index}", (0.10, 0.10, 2.2), (x, 0, 1.1), root, mats["fence"])
        if lod < 2:
            cube(root.name + "_TopRail", (20, 0.08, 0.08), (0, 0, 2.05), root, mats["fence"])
            cube(root.name + "_MidRail", (20, 0.06, 0.06), (0, 0, 1.05), root, mats["fence"])
        cube(root.name + "_TaxiSign", (3.2, 0.22, 1.0), (0, -0.4, 0.7), root, mats["sign"])
        if lod == 0:
            for x in (-1.35, 1.35):
                cube(root.name + f"_SignPost_{x:+.1f}", (0.10, 0.10, 0.7), (x, -0.35, 0.35), root, mats["accent"])


def build_ramp_props(asset_id: str, position: tuple[float, float, float], mats: dict) -> None:
    for lod in range(3):
        root = root_object(asset_id, lod, position)
        cube(root.name + "_Crate", (2.4, 1.6, 1.2), (-2.6, 0, 0.6), root, mats["wall"], 0.08 if lod == 0 else 0.0)
        cube(root.name + "_Barrier", (3.5, 0.25, 0.35), (1.4, 0, 1.1), root, mats["accent"])
        for x in ((-0.1, 2.9) if lod == 0 else (1.4,)):
            cube(root.name + f"_BarrierPost_{x}", (0.18, 0.18, 2.0), (x, 0, 1.0), root, mats["accent"])
        if lod < 2:
            for index in range(3 if lod == 0 else 1):
                x = -0.8 + index * 0.9
                bpy.ops.mesh.primitive_cone_add(vertices=8 if lod == 0 else 5, radius1=0.28, radius2=0.08, depth=0.75, location=(x, -1.5, 0.375))
                cone = bpy.context.object
                cone.name = root.name + f"_Cone_{index}"
                cone.data.materials.append(mats["sign"][0])
                set_uv_cell(cone, mats["sign"][1])
                cone.parent = root


def configure_scene(atlas_path: Path) -> dict:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in tuple(bpy.data.materials):
        bpy.data.materials.remove(block)
    atlas = create_shared_atlas(atlas_path)
    shared = material("KBDU_AirportSharedAtlas", atlas)
    mats = {name: (shared, index) for index, name in enumerate(("wall", "roof", "door", "accent", "fence", "sign"))}
    build_hangar("Hangar_A_Gable", "gable", 26, 22, 7.0, 10.0, (-48, 16, 0), mats)
    build_hangar("Hangar_B_Wide", "gable", 34, 26, 8.0, 11.5, (-15, 16, 0), mats)
    build_hangar("Hangar_C_Quonset", "quonset", 24, 24, 7.0, 10.0, (20, 16, 0), mats)
    build_service("Fuel_Service", (-35, -20, 0), mats)
    build_fence_sign("Fence_Signage", (0, -20, 0), mats)
    build_ramp_props("Ramp_Props", (35, -20, 0), mats)
    return mats


def mesh_report() -> dict:
    by_lod = {"LOD0": 0, "LOD1": 0, "LOD2": 0}
    renderer_count = {"LOD0": 0, "LOD1": 0, "LOD2": 0}
    for obj in bpy.data.objects:
        if obj.type != "MESH" or obj.parent is None or "questflight_lod" not in obj.parent:
            continue
        obj.data.calc_loop_triangles()
        key = f"LOD{int(obj.parent['questflight_lod'])}"
        by_lod[key] += len(obj.data.loop_triangles)
        renderer_count[key] += 1
    return {
        "triangles_by_lod": by_lod,
        "mesh_renderers_by_lod": renderer_count,
        "shared_materials": sorted(material.name for material in bpy.data.materials),
        "asset_roots": sorted(obj.name for obj in bpy.data.objects if "questflight_asset" in obj),
    }


def validate_quonset_caps() -> dict:
    report: dict[str, dict[str, int | bool]] = {}
    for lod in range(3):
        root_name = f"Hangar_C_Quonset_LOD{lod}"
        root = bpy.data.objects.get(root_name)
        if root is None:
            raise RuntimeError(f"Quonset validation root is missing: {root_name}")
        bodies = [obj for obj in root.children if obj.type == "MESH" and obj.name.endswith("_Body")]
        if len(bodies) != 1:
            raise RuntimeError(f"Quonset validation expected one body below {root_name}: {len(bodies)}")
        mesh = bodies[0].data
        mesh.update()
        minimum_y = min(vertex.co.y for vertex in mesh.vertices)
        maximum_y = max(vertex.co.y for vertex in mesh.vertices)
        counts = {
            "front_outward_triangles": 0,
            "front_inward_triangles": 0,
            "rear_outward_triangles": 0,
            "rear_inward_triangles": 0,
            "outward_shell_faces": 0,
            "shell_faces": 0,
        }
        for polygon in mesh.polygons:
            ys = [mesh.vertices[index].co.y for index in polygon.vertices]
            at_front = all(abs(value - minimum_y) < 1e-5 for value in ys)
            at_rear = all(abs(value - maximum_y) < 1e-5 for value in ys)
            if at_front:
                key = "front_outward_triangles" if polygon.normal.y < -0.9 else "front_inward_triangles"
                counts[key] += max(1, len(polygon.vertices) - 2)
            elif at_rear:
                key = "rear_outward_triangles" if polygon.normal.y > 0.9 else "rear_inward_triangles"
                counts[key] += max(1, len(polygon.vertices) - 2)
            else:
                counts["shell_faces"] += 1
                radial = Vector((polygon.center.x, 0.0, polygon.center.z - 1.4))
                if radial.length > 1e-5 and polygon.normal.dot(radial.normalized()) > 0.2:
                    counts["outward_shell_faces"] += 1
        passed = (
            counts["front_outward_triangles"] == counts["front_inward_triangles"] > 0
            and counts["rear_outward_triangles"] == counts["rear_inward_triangles"] > 0
            and counts["outward_shell_faces"] == counts["shell_faces"] > 0
        )
        counts["passed"] = passed
        if not passed:
            raise RuntimeError(f"Quonset front/rear cap or shell winding validation failed: {root_name} {counts}")
        report[f"LOD{lod}"] = counts
    return report


def render_preview(preview_dir: Path) -> Path:
    preview_dir.mkdir(parents=True, exist_ok=True)
    for obj in bpy.data.objects:
        if obj.type == "MESH" and obj.parent and obj.parent.get("questflight_lod", 0) != 0:
            obj.hide_render = True
    bpy.ops.object.light_add(type="SUN", location=(0, 0, 50))
    sun = bpy.context.object
    sun.rotation_euler = (math.radians(32), 0, math.radians(-28))
    sun.data.energy = 2.0
    bpy.ops.object.light_add(type="AREA", location=(-10, -35, 45))
    area = bpy.context.object
    area.data.energy = 1800
    area.data.shape = "DISK"
    area.data.size = 25
    bpy.ops.object.camera_add(location=(92, -125, 92))
    camera = bpy.context.object
    direction = Vector((2, 0, 3)) - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    camera.data.lens = 54
    bpy.context.scene.camera = camera
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.world.color = (0.08, 0.10, 0.13)
    path = preview_dir / "kbdu_airport_kit_lod0_preview.png"
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    for obj in (sun, area, camera):
        bpy.data.objects.remove(obj, do_unlink=True)
    return path


def render_quonset_cap_previews(preview_dir: Path) -> list[Path]:
    preview_dir.mkdir(parents=True, exist_ok=True)
    hide_state = {obj: obj.hide_render for obj in bpy.data.objects if obj.type == "MESH"}
    for obj in hide_state:
        obj.hide_render = not (obj.parent is not None and obj.parent.name == "Hangar_C_Quonset_LOD0")

    bpy.ops.object.light_add(type="SUN", location=(20, 16, 50))
    sun = bpy.context.object
    sun.rotation_euler = (math.radians(28), 0, math.radians(-24))
    sun.data.energy = 2.1
    bpy.ops.object.light_add(type="AREA", location=(35, -8, 28))
    area = bpy.context.object
    area.data.energy = 1500
    area.data.shape = "DISK"
    area.data.size = 18
    bpy.ops.object.camera_add(location=(20, -28, 8.5))
    camera = bpy.context.object
    camera.data.lens = 58
    scene = bpy.context.scene
    scene.camera = camera
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1200
    scene.render.resolution_y = 800
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.world.color = (0.10, 0.12, 0.14)
    target = Vector((20, 16, 6.2))
    previews: list[Path] = []
    for label, location in (("front", (20, -28, 8.5)), ("rear", (20, 60, 8.5))):
        camera.location = location
        area.location = (35, -8, 28) if label == "front" else (5, 44, 28)
        camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
        path = preview_dir / f"kbdu_quonset_lod0_{label}_cap_preview.png"
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        previews.append(path)

    for obj in (sun, area, camera):
        bpy.data.objects.remove(obj, do_unlink=True)
    for obj, hidden in hide_state.items():
        obj.hide_render = hidden
    return previews


def export_assets(asset_dir: Path, source_dir: Path) -> tuple[Path, Path]:
    asset_dir.mkdir(parents=True, exist_ok=True)
    source_dir.mkdir(parents=True, exist_ok=True)
    for obj in bpy.data.objects:
        obj.select_set(obj.type == "MESH" or "questflight_asset" in obj)
    glb_path = asset_dir / "KBDU_AirportKit.glb"
    bpy.ops.export_scene.gltf(
        filepath=str(glb_path),
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
        # Unity assigns one external ASTC atlas material to every imported mesh. Omitting
        # embedded glTF materials avoids a duplicate texture/material dependency.
        export_materials="NONE",
        export_extras=True,
        export_cameras=False,
        export_lights=False,
        export_animations=False,
    )
    blend_path = source_dir / "KBDU_AirportKit.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path), compress=True)
    return glb_path, blend_path


def main() -> None:
    if os.environ.get("PYTHONHASHSEED") != "0":
        raise RuntimeError(
            "Set PYTHONHASHSEED=0 before launching Blender so glTF buffer ordering is reproducible."
        )
    paths = parse_args()
    atlas_path = paths["asset_dir"] / "KBDU_AirportSharedAtlas.png"
    configure_scene(atlas_path)
    report = mesh_report()
    quonset_validation = validate_quonset_caps()
    preview = render_preview(paths["preview_dir"])
    cap_previews = render_quonset_cap_previews(paths["preview_dir"])
    glb_path, blend_path = export_assets(paths["asset_dir"], paths["source_dir"])
    manifest = {
        "schema_version": 1,
        "creator": "T2 QuestFlight project-authored deterministic Blender generator",
        "license": "Project-owned original work; no external mesh or texture assets",
        "tool": {
            "name": "Blender",
            "version": bpy.app.version_string,
            "build_hash": bpy.app.build_hash.decode("ascii"),
            "python_hash_seed": 0,
            "license": "GNU GPL; generated project-owned output is not a Blender program derivative",
        },
        "generator": record(Path(__file__).resolve()),
        "outputs": [record(glb_path), record(atlas_path), record(blend_path)],
        "geometry": report,
        "validation": {
            "quonset_caps_and_shell": quonset_validation,
            "backface_culling_enabled_in_source_preview": True,
        },
        "integration": {
            "lod_thresholds": [0.58, 0.24, 0.06],
            "shared_material_count": len(report["shared_materials"]),
            "gpu_instancing_compatible": True,
            "notes": "Static meshes have no armatures, animation, runtime generators, or camera-relative behavior.",
            "atlas_surface_cues": [
                "subtle wall corrugation and panel seams",
                "standing roof seams and bounded weather streaks",
                "hangar-door panel joints and lower-edge grime",
                "single shared 512x512 albedo atlas; no additional material or texture",
            ],
        },
        "preview_external": f"external-artifact/kbdu_airport_art/{preview.name}",
        "validation_previews_external": [
            f"external-artifact/kbdu_airport_art/{path.name}" for path in cap_previews
        ],
    }
    paths["manifest"].parent.mkdir(parents=True, exist_ok=True)
    paths["manifest"].write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(manifest, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
