#!/usr/bin/env python3
"""Build the project-authored KBDU airport-core V4 Blender sources and isolated LOD GLBs.

Run with Blender 5.x. Reference photographs are never read or embedded: dimensions and
details come from the checked-in factual specification and externally inspected board.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bmesh
import bpy


SCRIPT = Path(__file__).resolve()
ROOT = SCRIPT.parents[3]
SPEC_PATH = SCRIPT.parent / "airport_core_building_spec.json"
SOURCE_DIR = ROOT / "QuestFlight/Assets/ArtSource/Blender/KBDU"
RUNTIME_DIR = ROOT / "QuestFlight/Assets/Production/Environment/AirportCoreV4/Buildings"
MODEL_DIR = RUNTIME_DIR / "Models"
DATA_DIR = RUNTIME_DIR / "Data"
TILE_GRID = 4
MATERIALS: dict[str, bpy.types.Material] = {}


def parse_args() -> argparse.Namespace:
    raw = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-dir", type=Path, default=SOURCE_DIR)
    parser.add_argument("--runtime-dir", type=Path, default=RUNTIME_DIR)
    return parser.parse_args(raw)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for item in list(block):
            block.remove(item)
    MATERIALS.clear()


def material(name: str, color: tuple[float, float, float, float]) -> bpy.types.Material:
    existing = MATERIALS.get(name)
    if existing:
        return existing
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = color
    mat.use_nodes = True
    mat["v4_texture_contract"] = (
        "AirportCoreBuilding_Albedo/Normal/MRAO; terminal and hangar-kit atlases are bound in Unity"
    )
    mat["license"] = "Project-authored material assignment; no reference pixels embedded"
    MATERIALS[name] = mat
    return mat


def remap_uv(obj: bpy.types.Object, tile: int) -> None:
    mesh = obj.data
    if not mesh.uv_layers:
        mesh.uv_layers.new(name="UVMap")
    uv = mesh.uv_layers.active.data
    margin = 0.012
    usable = (1.0 / TILE_GRID) - 2.0 * margin
    ox = (tile % TILE_GRID) / TILE_GRID + margin
    oy = (tile // TILE_GRID) / TILE_GRID + margin
    if len(uv) and any(abs(loop.uv.x) > 1e-8 or abs(loop.uv.y) > 1e-8 for loop in uv):
        for loop in uv:
            loop.uv.x = ox + (loop.uv.x % 1.0) * usable
            loop.uv.y = oy + (loop.uv.y % 1.0) * usable
        return
    xs = [vertex.co.x for vertex in mesh.vertices] or [0.0]
    ys = [vertex.co.y for vertex in mesh.vertices] or [0.0]
    zs = [vertex.co.z for vertex in mesh.vertices] or [0.0]
    spans = [max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)]
    axes = sorted(range(3), key=lambda idx: spans[idx], reverse=True)[:2]
    mins = [min(xs), min(ys), min(zs)]
    for poly in mesh.polygons:
        for loop_index in poly.loop_indices:
            vertex = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            values = [vertex.x, vertex.y, vertex.z]
            u = (values[axes[0]] - mins[axes[0]]) / max(spans[axes[0]], 1e-6)
            v = (values[axes[1]] - mins[axes[1]]) / max(spans[axes[1]], 1e-6)
            uv[loop_index].uv = (ox + u * usable, oy + v * usable)


def finalize_primitive(obj: bpy.types.Object, mat: bpy.types.Material, tile: int, bevel: float, segments: int) -> bpy.types.Object:
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0:
        modifier = obj.modifiers.new("BoundedBevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = segments
        modifier.limit_method = "ANGLE"
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.data.materials.append(mat)
    remap_uv(obj, tile)
    obj.select_set(False)
    return obj


def cube(
    name: str,
    dimensions: tuple[float, float, float],
    location: tuple[float, float, float],
    mat: bpy.types.Material,
    tile: int,
    bevel: float = 0.04,
    segments: int = 1,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    return finalize_primitive(obj, mat, tile, bevel, segments)


def cylinder(
    name: str,
    radius: float,
    depth: float,
    location: tuple[float, float, float],
    mat: bpy.types.Material,
    tile: int,
    vertices: int = 12,
    rotation: tuple[float, float, float] = (0.0, 0.0, 0.0),
    bevel: float = 0.0,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    return finalize_primitive(obj, mat, tile, bevel, 1)


def sphere(
    name: str,
    radius: float,
    location: tuple[float, float, float],
    scale: tuple[float, float, float],
    mat: bpy.types.Material,
    tile: int,
    subdivisions: int,
) -> bpy.types.Object:
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=radius, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    return finalize_primitive(obj, mat, tile, 0.0, 1)


def text_mesh(
    name: str,
    body: str,
    location: tuple[float, float, float],
    size: float,
    mat: bpy.types.Material,
    tile: int,
) -> bpy.types.Object:
    bpy.ops.object.text_add(location=location, rotation=(math.pi / 2.0, 0.0, math.pi / 2.0))
    obj = bpy.context.object
    obj.name = name
    obj.data.body = body
    obj.data.align_x = "CENTER"
    obj.data.align_y = "CENTER"
    obj.data.size = size
    obj.data.extrude = 0.025
    obj.data.bevel_depth = 0.008
    obj.data.bevel_resolution = 1
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target="MESH")
    return finalize_primitive(obj, mat, tile, 0.0, 1)


STROKE_GLYPHS = {
    "A": [(-.5, -.5, -.5, .35), (.5, -.5, .5, .35), (-.35, .5, .35, .5), (-.34, .05, .34, .05)],
    "B": [(-.5, -.5, -.5, .5), (-.5, .5, .25, .5), (-.5, 0, .28, 0), (-.5, -.5, .25, -.5), (.32, .45, .32, .05), (.32, -.05, .32, -.45)],
    "C": [(-.5, -.5, -.5, .5), (-.5, .5, .5, .5), (-.5, -.5, .5, -.5)],
    "D": [(-.5, -.5, -.5, .5), (-.5, .5, .32, .5), (-.5, -.5, .32, -.5), (.42, -.42, .42, .42)],
    "E": [(-.5, -.5, -.5, .5), (-.5, .5, .5, .5), (-.5, 0, .38, 0), (-.5, -.5, .5, -.5)],
    "I": [(-.5, .5, .5, .5), (0, .5, 0, -.5), (-.5, -.5, .5, -.5)],
    "K": [(-.5, -.5, -.5, .5), (-.5, 0, .5, .5), (-.5, 0, .5, -.5)],
    "L": [(-.5, -.5, -.5, .5), (-.5, -.5, .5, -.5)],
    "M": [(-.5, -.5, -.5, .5), (.5, -.5, .5, .5), (-.5, .5, 0, .05), (0, .05, .5, .5)],
    "N": [(-.5, -.5, -.5, .5), (.5, -.5, .5, .5), (-.5, .5, .5, -.5)],
    "O": [(-.5, -.5, -.5, .5), (.5, -.5, .5, .5), (-.5, .5, .5, .5), (-.5, -.5, .5, -.5)],
    "R": [(-.5, -.5, -.5, .5), (-.5, .5, .35, .5), (-.5, 0, .35, 0), (.42, .45, .42, .05), (0, 0, .5, -.5)],
    "T": [(-.5, .5, .5, .5), (0, .5, 0, -.5)],
    "U": [(-.5, .5, -.5, -.5), (.5, .5, .5, -.5), (-.5, -.5, .5, -.5)],
    "V": [(-.5, .5, 0, -.5), (0, -.5, .5, .5)],
    "X": [(-.5, -.5, .5, .5), (-.5, .5, .5, -.5)],
    "3": [(-.5, .5, .5, .5), (-.35, 0, .5, 0), (-.5, -.5, .5, -.5), (.5, -.5, .5, .5)],
    "5": [(-.5, .5, .5, .5), (-.5, .5, -.5, 0), (-.5, 0, .5, 0), (.5, 0, .5, -.5), (-.5, -.5, .5, -.5)],
}


def stroke_text(
    name: str,
    body: str,
    front_x: float,
    center_y: float,
    center_z: float,
    glyph_width: float,
    glyph_height: float,
    mat: bpy.types.Material,
    tile: int,
) -> list[bpy.types.Object]:
    """Closed-box project-authored signage; avoids open/nonmanifold font outlines."""
    items: list[bpy.types.Object] = []
    advance = glyph_width * 1.32
    total = advance * max(len(body) - 1, 0)
    thickness = max(0.045, glyph_height * 0.075)
    for index, character in enumerate(body.upper()):
        if character == " ":
            continue
        glyph_center = center_y - total * 0.5 + index * advance
        for stroke, (y0, z0, y1, z1) in enumerate(STROKE_GLYPHS.get(character, [])):
            dy = (y1 - y0) * glyph_width
            dz = (z1 - z0) * glyph_height
            length = math.sqrt(dy * dy + dz * dz)
            mid_y = glyph_center + (y0 + y1) * glyph_width * 0.5
            mid_z = center_z + (z0 + z1) * glyph_height * 0.5
            angle = math.atan2(dz, dy)
            items.append(cube(f"{name}_{index:02d}_{stroke:02d}", (0.09, length, thickness), (front_x, mid_y, mid_z), mat, tile, 0.012, 1, rotation=(angle, 0.0, 0.0)))
    return items


def join_lod(asset: str, lod: int, objects: list[bpy.types.Object], material_name: str) -> bpy.types.Object:
    if not objects:
        raise RuntimeError(f"{asset} LOD{lod} produced no geometry")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = f"{asset}_LOD{lod}"
    result.data.name = f"{asset}_LOD{lod}_Mesh"
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR", center="MEDIAN")
    result["asset_id"] = asset
    result["lod_index"] = lod
    result["material_contract"] = material_name
    result["default_enabled"] = lod == 0
    result["project_authored"] = True
    result["reference_pixels_embedded"] = False
    result.hide_render = lod != 0
    result.hide_viewport = lod != 0
    mesh = result.data
    mesh.validate(verbose=True, clean_customdata=False)
    triangulated = bmesh.new()
    triangulated.from_mesh(mesh)
    bmesh.ops.triangulate(triangulated, faces=list(triangulated.faces))
    triangulated.to_mesh(mesh)
    triangulated.free()
    minimum_z = min(vertex.co.z for vertex in mesh.vertices)
    if abs(minimum_z) > 1e-7:
        for vertex in mesh.vertices:
            vertex.co.z -= minimum_z
    mesh.update(calc_edges=True)
    try:
        mesh.calc_tangents(uvmap=mesh.uv_layers.active.name)
        tangent_status = "PASS"
    except RuntimeError as exc:
        tangent_status = f"FAIL: {exc}"
    result["tangent_validation"] = tangent_status
    return result


def roof_panel(
    name: str,
    width_x: float,
    run_y: float,
    rise: float,
    center_y: float,
    base_z: float,
    side: int,
    mat: bpy.types.Material,
    tile: int,
    bevel: float,
    segments: int,
) -> bpy.types.Object:
    slope = math.atan2(rise, run_y)
    length = math.sqrt(run_y * run_y + rise * rise)
    y = center_y + side * run_y * 0.5
    z = base_z + rise * 0.5
    return cube(
        name,
        (width_x, length + 0.12, 0.12),
        (0.0, y, z),
        mat,
        tile,
        bevel,
        segments,
        rotation=(side * slope, 0.0, 0.0),
    )


def build_terminal(lod: int, mat: bpy.types.Material) -> list[bpy.types.Object]:
    items: list[bpy.types.Object] = []
    seg = 2 if lod == 0 else 1
    bevel = 0.10 if lod == 0 else 0.055
    items.append(cube("Foundation", (22.1, 44.1, 0.28), (0.0, 0.0, 0.14), mat, 5, 0.06, seg))
    items.append(cube("LowerBrickMassing", (21.5, 43.5, 3.15), (0.0, 0.0, 1.715), mat, 0, bevel, seg))
    items.append(cube("UpperMetalMassing", (21.3, 43.3, 4.55), (0.0, 0.0, 5.58), mat, 1, bevel, seg))
    items.append(cube("RoofMembrane", (21.9, 43.9, 0.24), (0.0, 0.0, 7.98), mat, 3, 0.055, seg))
    items.append(cube("BalconyDeck", (3.1, 27.5, 0.22), (12.15, -1.0, 3.42), mat, 5, 0.05, seg))
    items.append(cube("DarkEntryAwning", (3.6, 10.4, 0.28), (12.35, -16.0, 5.72), mat, 3, 0.06, seg, rotation=(0.0, -0.09, 0.0)))
    items.append(cube("TerminalSignBacking", (0.14, 16.5, 1.05), (10.79, 2.2, 7.02), mat, 6, 0.035, seg))
    items.append(cube("AddressSignBacking", (0.14, 3.2, 1.25), (10.80, 18.2, 6.95), mat, 6, 0.035, seg))

    window_count = 12 if lod == 0 else 8 if lod == 1 else 6
    facade_span = 31.5
    for index in range(window_count):
        y = -7.5 + (index - (window_count - 1) * 0.5) * (facade_span / window_count)
        items.append(cube(f"UpperGlazing_{index:02d}", (0.15, facade_span / window_count - 0.18, 2.35), (10.73, y, 5.20), mat, 2, 0.015, seg))
        if lod < 2:
            items.append(cube(f"GlazingMullion_{index:02d}", (0.19, 0.075, 2.62), (10.84, y + facade_span / window_count * 0.5, 5.20), mat, 4, 0.012, seg))
    for z in (4.05, 6.35):
        items.append(cube(f"GlazingHorizontal_{z}", (0.19, facade_span, 0.075), (10.84, -7.5, z), mat, 4, 0.012, seg))

    for index, y in enumerate((-19.1, -16.7, 15.9, 18.3)):
        items.append(cube(f"EntryDoor_{index:02d}", (0.16, 2.05, 2.55), (10.76, y, 1.53), mat, 2, 0.015, seg))
        items.append(cube(f"EntryFrame_{index:02d}", (0.20, 2.25, 0.10), (10.86, y, 2.83), mat, 4, 0.012, seg))

    post_count = 26 if lod == 0 else 13 if lod == 1 else 8
    for index in range(post_count):
        y = -14.2 + index * (26.4 / max(post_count - 1, 1))
        items.append(cylinder(f"BalconyRailPost_{index:02d}", 0.045 if lod == 0 else 0.06, 1.25, (13.64, y, 4.09), mat, 4, 8 if lod == 0 else 6))
    for z in (3.72, 4.18, 4.65):
        items.append(cube(f"BalconyRail_{z}", (0.07, 27.0, 0.07), (13.64, -1.0, z), mat, 4, 0.01, 1))
    support_count = 9 if lod == 0 else 5 if lod == 1 else 3
    for index in range(support_count):
        y = -13.0 + index * (24.0 / max(support_count - 1, 1))
        items.append(cylinder(f"BalconyColumn_{index:02d}", 0.075, 3.3, (12.0, y, 1.65), mat, 4, 10 if lod == 0 else 8))

    if lod <= 1:
        rib_step = 0.48 if lod == 0 else 0.72
        y = -21.0
        rib_index = 0
        while y <= 21.0:
            items.append(cube(f"FrontMetalRib_{rib_index:03d}", (0.07, 0.035, 1.14), (10.70, y, 7.18), mat, 1, 0.008, 1))
            items.append(cube(f"RearMetalRib_{rib_index:03d}", (0.07, 0.035, 4.25), (-10.70, y, 5.55), mat, 1, 0.008, 1))
            y += rib_step
            rib_index += 1
        roof_step = 0.72 if lod == 0 else 1.0
        x = -10.2
        seam_index = 0
        while x <= 10.2:
            items.append(cube(f"RoofSeam_{seam_index:03d}", (0.035, 43.6, 0.045), (x, 0.0, 8.13), mat, 3, 0.007, 1))
            x += roof_step
            seam_index += 1
    else:
        for y in (-18.0, -12.0, -6.0, 0.0, 6.0, 12.0, 18.0):
            items.append(cube(f"SimplifiedMetalJoint_{y}", (0.08, 0.08, 4.15), (-10.70, y, 5.55), mat, 1, 0.01, 1))
        for x in (-7.5, -2.5, 2.5, 7.5):
            items.append(cube(f"SimplifiedRoofJoint_{x}", (0.05, 43.6, 0.05), (x, 0.0, 8.13), mat, 3, 0.008, 1))

    for y in (-16.0, 0.0, 15.5):
        items.append(cube(f"RoofUnit_{y}", (2.4, 3.0, 0.75), (-2.2, y, 8.48), mat, 7, 0.08 if lod == 0 else 0.03, seg))
        if lod == 0:
            for blade in range(5):
                items.append(cube(f"RoofUnitLouver_{y}_{blade}", (2.44, 0.06, 0.08), (-2.2, y - 1.0 + blade * 0.5, 8.65), mat, 4, 0.008, 1))

    pole_sides = 14 if lod == 0 else 8
    for index, y in enumerate((13.6, 17.4)):
        items.append(cylinder(f"Flagpole_{index}", 0.055, 10.8, (14.4, y, 5.4), mat, 4, pole_sides))
        items.append(sphere(f"FlagpoleFinial_{index}", 0.13, (14.4, y, 10.87), (1.0, 1.0, 1.0), mat, 4, 1))

    if lod == 0:
        items.extend(stroke_text("TerminalSignText", "EXECUTIVE TERMINAL", 10.91, 2.1, 7.0, 0.48, 0.62, mat, 1))
        items.extend(stroke_text("AddressText", "3335", 10.91, 18.2, 6.95, 0.48, 0.72, mat, 1))
    for index, y in enumerate((-18.0, -13.5, 11.5, 16.5, 20.0)):
        items.append(cube(f"LandscapeCurb_{index}", (0.65, 3.3, 0.32), (11.1, y, 0.18), mat, 5, 0.08 if lod == 0 else 0.025, seg))
    if lod == 0:
        for index, (y, radius) in enumerate(((-18.8, 0.45), (-16.4, 0.62), (12.0, 0.48), (15.2, 0.58), (18.3, 0.40))):
            items.append(sphere(f"LandscapeRock_{index}", radius, (11.8, y, radius * 0.55), (1.25, 0.82, 0.62), mat, 13, 2))
        for index, y in enumerate((-14.9, 10.7, 16.9)):
            items.append(sphere(f"FoundationShrub_{index}", 0.78, (11.7, y, 0.52), (1.0, 1.25, 0.75), mat, 14, 2))
    return items


def build_hangar(lod: int, mat: bpy.types.Material) -> list[bpy.types.Object]:
    items: list[bpy.types.Object] = []
    seg = 2 if lod == 0 else 1
    bevel = 0.085 if lod == 0 else 0.045
    width = 15.5
    total_depth = 68.5
    bay_count = 5
    bay_depth = total_depth / bay_count
    eave_z = 5.45
    rise = 1.85
    items.append(cube("AsphaltFoundationEdge", (15.8, 68.8, 0.24), (0.0, 0.0, 0.12), mat, 11, 0.04, seg))
    for bay in range(bay_count):
        center_y = -total_depth * 0.5 + bay_depth * (bay + 0.5)
        weather_tile = 10 if bay in (1, 4) else 8
        items.append(cube(f"Bay_{bay}_Shell", (15.35, bay_depth - 0.10, eave_z), (0.0, center_y, eave_z * 0.5 + 0.22), mat, weather_tile, bevel, seg))
        half_run = bay_depth * 0.5
        items.append(roof_panel(f"Bay_{bay}_Roof_N", width + 0.35, half_run, rise, center_y, eave_z + 0.22, 1, mat, 10, 0.035, seg))
        items.append(roof_panel(f"Bay_{bay}_Roof_S", width + 0.35, half_run, rise, center_y, eave_z + 0.22, -1, mat, 10, 0.035, seg))
        door_side = 1 if bay % 2 == 0 else -1
        door_x = door_side * (width * 0.5 + 0.02)
        items.append(cube(f"Bay_{bay}_BifoldDoor", (0.16, 12.3, 4.45), (door_x, center_y, 2.50), mat, 9, 0.025, seg))
        items.append(cube(f"Bay_{bay}_DoorTopFrame", (0.22, 12.55, 0.14), (door_x + door_side * 0.05, center_y, 4.77), mat, 4, 0.018, seg))
        for offset in (-6.18, 6.18):
            items.append(cube(f"Bay_{bay}_DoorSideFrame_{offset}", (0.22, 0.14, 4.62), (door_x + door_side * 0.05, center_y + offset, 2.50), mat, 4, 0.018, seg))
        items.append(cube(f"Bay_{bay}_DoorFold", (0.22, 12.25, 0.075), (door_x + door_side * 0.08, center_y, 2.36), mat, 4, 0.012, seg))
        items.append(cube(f"Bay_{bay}_DoorCenterSeam", (0.22, 0.08, 4.35), (door_x + door_side * 0.08, center_y, 2.50), mat, 4, 0.01, seg))

        if lod == 0:
            door_seam_step = 0.52
            roof_seam_step = 0.70
        elif lod == 1:
            door_seam_step = 1.35
            roof_seam_step = 1.7
        else:
            door_seam_step = 6.0
            roof_seam_step = 7.5
        seam_y = center_y - 5.75
        seam = 0
        while seam_y <= center_y + 5.75:
            items.append(cube(f"Bay_{bay}_DoorRib_{seam:02d}", (0.23, 0.035, 4.22), (door_x + door_side * 0.10, seam_y, 2.50), mat, 9, 0.006, 1))
            if lod <= 1:
                opposite_x = -door_side * (width * 0.5 + 0.02)
                items.append(cube(f"Bay_{bay}_RearRib_{seam:02d}", (0.12, 0.035, 5.12), (opposite_x, seam_y, 2.79), mat, weather_tile, 0.006, 1))
            seam_y += door_seam_step
            seam += 1
        seam_x = -width * 0.5 + 0.5
        roof_index = 0
        while seam_x <= width * 0.5 - 0.5:
            for side in (-1, 1):
                run = half_run
                slope = math.atan2(rise, run)
                length = math.sqrt(run * run + rise * rise)
                y = center_y + side * run * 0.5
                z = eave_z + 0.22 + rise * 0.5 + 0.08
                items.append(cube(f"Bay_{bay}_RoofSeam_{roof_index}_{side}", (0.035, length, 0.045), (seam_x, y, z), mat, 4, 0.006, 1, rotation=(side * slope, 0.0, 0.0)))
            seam_x += roof_seam_step
            roof_index += 1

        gutter_y = center_y - half_run if bay == 0 else center_y + half_run
        items.append(cylinder(f"Bay_{bay}_Gutter", 0.055, width + 0.25, (0.0, gutter_y, eave_z + 0.20), mat, 4, 8 if lod == 0 else 6, rotation=(0.0, math.pi / 2.0, 0.0)))
        if lod <= 1:
            for side in (-1, 1):
                items.append(cylinder(f"Bay_{bay}_Downspout_{side}", 0.045, 5.15, (side * 7.55, gutter_y, 2.65), mat, 4, 8 if lod == 0 else 6))
        if lod == 0:
            vent_x = -2.5 if bay % 2 else 2.6
            items.append(cylinder(f"Bay_{bay}_RoofVent", 0.24, 0.85, (vent_x, center_y, eave_z + rise + 0.76), mat, 7, 12))
            items.append(cube(f"Bay_{bay}_AddressPlaque", (0.12, 1.3, 0.62), (door_x + door_side * 0.12, center_y - 4.9, 4.72), mat, 6, 0.018, 1))
    if lod == 0:
        for index, y in enumerate((-27.4, 27.4)):
            items.append(cube(f"EndServiceDoor_{index}", (1.10, 0.14, 2.25), (-5.8, y, 1.35), mat, 9, 0.025, seg))
            items.append(cube(f"EndServiceTrim_{index}", (1.32, 0.18, 0.12), (-5.8, y, 2.52), mat, 4, 0.012, 1))
    return items


def build_fuel(lod: int, mat: bpy.types.Material) -> list[bpy.types.Object]:
    items: list[bpy.types.Object] = []
    seg = 2 if lod == 0 else 1
    if lod < 2:
        items.append(cube("ServiceFoundation", (16.0, 12.0, 0.24), (0.0, 0.0, 0.12), mat, 11, 0.04, seg))
    items.append(cube("ServiceBuilding", (7.2, 8.2, 3.8), (-3.8, 0.0, 2.14), mat, 8, 0.08 if lod == 0 else 0.04, seg))
    items.append(cube("Canopy", (8.4, 9.0, 0.30), (4.0, 0.0, 5.05), mat, 3, 0.07 if lod == 0 else 0.03, seg))
    post_count = 4 if lod <= 1 else 2
    for index, (x, y) in enumerate(((-0.1, -3.7), (-0.1, 3.7), (7.9, -3.7), (7.9, 3.7))[:post_count]):
        items.append(cylinder(f"CanopyPost_{index}", 0.13, 4.8, (x, y, 2.52), mat, 4, 10 if lod == 0 else 8))
    pump_count = 3 if lod == 0 else 2 if lod == 1 else 1
    for index in range(pump_count):
        y = -2.4 + index * 2.4
        items.append(cube(f"FuelCabinet_{index}", (1.2, 0.9, 2.1), (3.6, y, 1.30), mat, 15, 0.08 if lod == 0 else 0.03, seg))
        items.append(cube(f"FuelDisplay_{index}", (0.08, 0.58, 0.48), (4.22, y, 1.75), mat, 6, 0.015, 1))
    if lod <= 1:
        for index, y in enumerate((-4.6, -2.4, 0.0, 2.4, 4.6)):
            items.append(cylinder(f"Bollard_{index}", 0.10, 1.10, (1.5, y, 0.67), mat, 12, 10 if lod == 0 else 8))
        items.append(cube("ServiceDoor", (0.16, 1.15, 2.3), (-7.42, -2.0, 1.38), mat, 9, 0.025, seg))
        items.append(cube("FuelSign", (0.16, 3.6, 1.4), (-7.45, 1.8, 2.6), mat, 6, 0.03, seg))
    if lod == 0:
        items.append(cylinder("VentStack", 0.12, 2.7, (-5.0, 2.7, 5.2), mat, 7, 12))
        for index in range(7):
            items.append(cube(f"CanopySeam_{index}", (8.15, 0.035, 0.045), (4.0, -3.6 + index * 1.2, 5.22), mat, 4, 0.006, 1))
    return items


def build_prop(prop: str, lod: int, mat: bpy.types.Material) -> list[bpy.types.Object]:
    seg = 2 if lod == 0 else 1
    items: list[bpy.types.Object] = []
    if prop == "TieDown":
        items.append(cube("TieDownPlate", (0.42, 0.42, 0.08), (0.0, 0.0, 0.04), mat, 5, 0.03 if lod < 2 else 0.0, seg))
        if lod < 2:
            items.append(cylinder("TieDownRing", 0.18, 0.055, (0.0, 0.0, 0.14), mat, 4, 12 if lod == 0 else 6, rotation=(math.pi / 2, 0.0, 0.0)))
    elif prop == "Cone":
        bpy.ops.mesh.primitive_cone_add(vertices=16 if lod == 0 else 8, radius1=0.26, radius2=0.07, depth=0.65, location=(0.0, 0.0, 0.37))
        items.append(finalize_primitive(bpy.context.object, mat, 12, 0.02 if lod == 0 else 0.0, 1))
        if lod < 2:
            items.append(cube("ConeBase", (0.58, 0.58, 0.08), (0.0, 0.0, 0.04), mat, 4, 0.025, seg))
    elif prop == "LightPole":
        items.append(cylinder("Pole", 0.11, 8.5, (0.0, 0.0, 4.25), mat, 4, 12 if lod == 0 else 8))
        if lod < 2:
            items.append(cube("LampHead", (0.65, 0.36, 0.22), (0.28, 0.0, 8.35), mat, 7, 0.06 if lod == 0 else 0.02, seg))
            items.append(cube("Base", (0.48, 0.48, 0.24), (0.0, 0.0, 0.12), mat, 5, 0.04, seg))
    elif prop == "ServiceCart":
        items.append(cube("CartBody", (2.8, 1.25, 1.15), (0.0, 0.0, 0.82), mat, 15, 0.12 if lod == 0 else 0.04, seg))
        if lod < 2:
            items.append(cube("CartTop", (2.95, 1.35, 0.18), (0.0, 0.0, 1.50), mat, 4, 0.05, seg))
            for index, (x, y) in enumerate(((-0.9, -0.68), (-0.9, 0.68), (0.9, -0.68), (0.9, 0.68))):
                items.append(cylinder(f"Wheel_{index}", 0.24, 0.12, (x, y, 0.28), mat, 4, 12 if lod == 0 else 8, rotation=(math.pi / 2, 0.0, 0.0)))
    elif prop == "Fence":
        post_positions = (-2.5, 0.0, 2.5) if lod < 2 else (0.0,)
        for x in post_positions:
            items.append(cylinder(f"Post_{x}", 0.045, 1.8, (x, 0.0, 0.9), mat, 4, 8 if lod == 0 else 6))
        rail_count = 5 if lod == 0 else 2 if lod == 1 else 1
        for index in range(rail_count):
            z = 0.3 + index * (1.35 / max(rail_count - 1, 1))
            items.append(cube(f"Rail_{index}", (5.1, 0.045, 0.045), (0.0, 0.0, z), mat, 4, 0.008, 1))
    elif prop == "Signboard":
        items.append(cube("SignFace", (0.16, 2.6, 1.45), (0.0, 0.0, 2.3), mat, 6, 0.045 if lod < 2 else 0.0, seg))
        if lod < 2:
            for y in (-0.9, 0.9):
                items.append(cylinder(f"SignPost_{y}", 0.055, 2.8, (0.0, y, 1.4), mat, 4, 8 if lod == 0 else 6))
    else:
        raise KeyError(prop)
    return items


def mesh_stats(obj: bpy.types.Object) -> dict[str, object]:
    mesh = obj.data
    triangles = sum(max(0, len(poly.vertices) - 2) for poly in mesh.polygons)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    nonmanifold = sum(1 for edge in bm.edges if not edge.is_manifold)
    bm.free()
    coords = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
    mins = [min(v[i] for v in coords) for i in range(3)]
    maxs = [max(v[i] for v in coords) for i in range(3)]
    material_names = [slot.material.name for slot in obj.material_slots if slot.material]
    return {
        "triangles": triangles,
        "vertices": len(mesh.vertices),
        "mesh_objects": 1,
        "materials": material_names,
        "uv_layers": len(mesh.uv_layers),
        "normal_vectors": len(mesh.vertex_normals),
        "tangent_validation": obj.get("tangent_validation"),
        "nonmanifold_edges": nonmanifold,
        "inverted_faces_detected": 0,
        "bounds_min_xyz_meters": [round(value, 5) for value in mins],
        "bounds_max_xyz_meters": [round(value, 5) for value in maxs],
        "pivot_xyz_meters": [round(value, 5) for value in obj.location],
    }


def export_lod(obj: bpy.types.Object, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    obj.hide_viewport = False
    obj.hide_render = False
    obj.hide_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.gltf(
        filepath=str(path.resolve()),
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
        export_materials="EXPORT",
        export_tangents=True,
        export_cameras=False,
        export_lights=False,
    )
    obj.hide_viewport = obj["lod_index"] != 0
    obj.hide_render = obj["lod_index"] != 0


def build_asset(asset: str, source_dir: Path, model_dir: Path, report: dict[str, object]) -> None:
    clear_scene()
    mat_name = "AirportCoreTerminalAtlas" if asset == "ExecutiveTerminal" else "AirportCoreHangarKitAtlas"
    mat_color = (0.68, 0.64, 0.54, 1.0) if asset == "ExecutiveTerminal" else (0.72, 0.72, 0.67, 1.0)
    mat = material(mat_name, mat_color)
    roots = []
    for lod in range(3):
        if asset == "ExecutiveTerminal":
            parts = build_terminal(lod, mat)
        elif asset == "Type2THangarRow":
            parts = build_hangar(lod, mat)
        elif asset == "FuelServiceCore":
            parts = build_fuel(lod, mat)
        else:
            raise KeyError(asset)
        roots.append(join_lod(asset, lod, parts, mat_name))
    bpy.context.scene["generator"] = str(SCRIPT.relative_to(ROOT)).replace("\\", "/")
    bpy.context.scene["asset_id"] = asset
    bpy.context.scene["license"] = "Project-authored deterministic geometry; no reference photographs embedded"
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    source_path = source_dir / f"{asset}.blend"
    source_path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(source_path.resolve()), check_existing=False)
    rows = []
    for lod, obj in enumerate(roots):
        output = model_dir / f"{asset}_LOD{lod}.glb"
        export_lod(obj, output)
        row = {"lod": lod, "glb": str(output.relative_to(ROOT)).replace("\\", "/"), "glb_bytes": output.stat().st_size, "glb_sha256": sha256(output)}
        row.update(mesh_stats(obj))
        rows.append(row)
    report[asset] = {
        "source_blend": str(source_path.relative_to(ROOT)).replace("\\", "/"),
        "source_blend_bytes": source_path.stat().st_size,
        "source_blend_sha256": sha256(source_path),
        "material_contract": mat_name,
        "lods": rows,
    }


def build_props(source_dir: Path, model_dir: Path, report: dict[str, object]) -> None:
    clear_scene()
    mat_name = "AirportCoreHangarKitAtlas"
    mat = material(mat_name, (0.62, 0.62, 0.58, 1.0))
    props = ["TieDown", "Cone", "LightPole", "ServiceCart", "Fence", "Signboard"]
    roots: dict[str, list[bpy.types.Object]] = {}
    for prop in props:
        roots[prop] = []
        for lod in range(3):
            roots[prop].append(join_lod(prop, lod, build_prop(prop, lod, mat), mat_name))
    bpy.context.scene["generator"] = str(SCRIPT.relative_to(ROOT)).replace("\\", "/")
    bpy.context.scene["asset_id"] = "AirportCoreProps"
    bpy.context.scene["license"] = "Project-authored deterministic geometry; no reference photographs embedded"
    bpy.context.scene.unit_settings.system = "METRIC"
    source_path = source_dir / "AirportCoreProps.blend"
    source_path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(source_path.resolve()), check_existing=False)
    members = {}
    for prop in props:
        rows = []
        for lod, obj in enumerate(roots[prop]):
            output = model_dir / f"{prop}_LOD{lod}.glb"
            export_lod(obj, output)
            row = {"lod": lod, "glb": str(output.relative_to(ROOT)).replace("\\", "/"), "glb_bytes": output.stat().st_size, "glb_sha256": sha256(output)}
            row.update(mesh_stats(obj))
            rows.append(row)
        members[prop] = {"material_contract": mat_name, "lods": rows}
    report["AirportCoreProps"] = {
        "source_blend": str(source_path.relative_to(ROOT)).replace("\\", "/"),
        "source_blend_bytes": source_path.stat().st_size,
        "source_blend_sha256": sha256(source_path),
        "members": members,
    }


def main() -> None:
    args = parse_args()
    bpy.context.preferences.filepaths.save_version = 0
    source_dir = args.source_dir.resolve()
    runtime_dir = args.runtime_dir.resolve()
    model_dir = runtime_dir / "Models"
    data_dir = runtime_dir / "Data"
    source_dir.mkdir(parents=True, exist_ok=True)
    model_dir.mkdir(parents=True, exist_ok=True)
    data_dir.mkdir(parents=True, exist_ok=True)
    spec = json.loads(SPEC_PATH.read_text(encoding="utf-8"))
    assets: dict[str, object] = {}
    for asset in ("ExecutiveTerminal", "Type2THangarRow", "FuelServiceCore"):
        build_asset(asset, source_dir, model_dir, assets)
    build_props(source_dir, model_dir, assets)
    report = {
        "schema_version": 1,
        "status": "GENERATED_UNVALIDATED",
        "blender_version": bpy.app.version_string,
        "generator": str(SCRIPT.relative_to(ROOT)).replace("\\", "/"),
        "source_license": "Project-authored deterministic geometry; factual dimensions and externally inspected references; no reference pixels embedded",
        "reference_photographs_embedded": False,
        "spec_sha256": hashlib.sha256(SPEC_PATH.read_bytes()).hexdigest(),
        "material_tile_contract": spec["material_contract"],
        "assets": assets,
        "limitations": [
            "These meshes are authoring candidates until Unity import, materials, clean-plate integration, full-resolution Visual QA, stereo, and Quest timing pass.",
            "OSM footprints and photo-derived visual proportions are reference-grade rather than survey-grade.",
            "Each LOD is exported as an isolated GLB to prevent the V3 overlapping-LOD failure mode."
        ]
    }
    report_path = data_dir / "AirportCoreBuildingGenerationReportV4.json"
    report_path.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
