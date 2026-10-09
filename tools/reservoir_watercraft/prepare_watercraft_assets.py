r"""Prepare compact reservoir-watercraft derivatives in Blender.

Raw downloaded models remain external. This offline tool strips textures and
source hierarchy, reduces the selected geometry, assigns semantic vertex colors,
and exports one-submesh FBX intermediates for focused Unity serialization.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


SEMANTIC = {
    "primary": (1.0, 0.0, 0.0, 1.0),
    "dark": (0.0, 1.0, 0.0, 1.0),
    "hardware": (0.0, 0.0, 1.0, 1.0),
    "canvas": (1.0, 1.0, 0.0, 1.0),
    "accent": (1.0, 0.0, 1.0, 1.0),
    "trailer": (0.0, 1.0, 1.0, 1.0),
}


def parse_args() -> argparse.Namespace:
    values = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--derivative", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--preview-dir", type=Path, required=True)
    return parser.parse_args(values)


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.images):
        for item in list(collection):
            if item.users == 0:
                collection.remove(item)


def import_source(path: Path) -> None:
    suffix = path.suffix.casefold()
    if suffix == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(path))
    elif suffix == ".obj":
        bpy.ops.wm.obj_import(filepath=str(path))
    elif suffix in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=str(path))
    elif suffix == ".blend":
        bpy.ops.wm.open_mainfile(filepath=str(path), load_ui=False)
    else:
        raise ValueError(f"Unsupported source format: {path}")


def mesh_objects() -> list[bpy.types.Object]:
    return sorted(
        (item for item in bpy.context.scene.objects if item.type == "MESH"),
        key=lambda item: item.name,
    )


def transformed_bounds(objects: list[bpy.types.Object]) -> tuple[Vector, Vector]:
    low = Vector((math.inf, math.inf, math.inf))
    high = Vector((-math.inf, -math.inf, -math.inf))
    for obj in objects:
        for corner in obj.bound_box:
            point = obj.matrix_world @ Vector(corner)
            for axis in range(3):
                low[axis] = min(low[axis], point[axis])
                high[axis] = max(high[axis], point[axis])
    return low, high


def retain_named(names: set[str]) -> None:
    for obj in mesh_objects():
        if obj.name not in names:
            bpy.data.objects.remove(obj, do_unlink=True)


def keep_largest_loose_component(obj: bpy.types.Object) -> bpy.types.Object:
    """Discard disconnected accessories while preserving the primary hull."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="LOOSE")
    bpy.ops.object.mode_set(mode="OBJECT")
    separated = [item for item in bpy.context.selected_objects if item.type == "MESH"]
    for item in separated:
        item.data.calc_loop_triangles()
    retained = max(separated, key=lambda item: len(item.data.loop_triangles))
    for item in separated:
        if item != retained:
            bpy.data.objects.remove(item, do_unlink=True)
    return retained


def delete_material_faces(obj: bpy.types.Object, rejected: set[str]) -> None:
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="DESELECT")
    bpy.ops.object.mode_set(mode="OBJECT")
    rejected_indices = {
        index
        for index, slot in enumerate(obj.material_slots)
        if slot.material is not None and slot.material.name in rejected
    }
    for polygon in obj.data.polygons:
        polygon.select = polygon.material_index in rejected_indices
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.delete(type="FACE")
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.select_set(False)


def retain_material_faces(obj: bpy.types.Object, retained: set[str]) -> None:
    rejected = {
        slot.material.name
        for slot in obj.material_slots
        if slot.material is not None and slot.material.name not in retained
    }
    delete_material_faces(obj, rejected)


def source_semantic(obj: bpy.types.Object, material_name: str) -> str:
    obj_name = obj.name.casefold()
    name = material_name.casefold()
    if name in SEMANTIC:
        return name
    if "trailer" in name:
        return "trailer"
    if any(token in name for token in ("glass", "alpha")):
        return "dark"
    if any(token in name for token in ("mirror", "mast")):
        return "hardware"
    if "sail" in name or "sail" in obj_name:
        return "canvas"
    if any(token in obj_name for token in ("mast", "missc", "keel", "helm")):
        return "hardware"
    if "grip" in name or "padding" in name:
        return "dark"
    if "handle" in name or "paddle" in name or "paddle" in obj_name:
        return "hardware"
    return "primary"


def assign_semantics(obj: bpy.types.Object) -> None:
    mesh = obj.data
    attribute = mesh.color_attributes.get("WatercraftSemantic")
    if attribute is None:
        attribute = mesh.color_attributes.new(
            name="WatercraftSemantic", type="BYTE_COLOR", domain="CORNER"
        )
    materials = [
        slot.material.name if slot.material is not None else "primary"
        for slot in obj.material_slots
    ]
    for polygon in mesh.polygons:
        material = (
            materials[polygon.material_index]
            if polygon.material_index < len(materials)
            else "primary"
        )
        color = SEMANTIC[source_semantic(obj, material)]
        for loop_index in polygon.loop_indices:
            attribute.data[loop_index].color = color
    mesh.color_attributes.active_color = attribute
    mesh.color_attributes.render_color_index = mesh.color_attributes.find(attribute.name)


def decimate(objects: list[bpy.types.Object], target_triangles: int) -> None:
    total = 0
    for obj in objects:
        obj.data.calc_loop_triangles()
        total += len(obj.data.loop_triangles)
    if total <= target_triangles:
        return
    ratio = max(0.001, min(1.0, target_triangles / total))
    for obj in objects:
        obj.data.calc_loop_triangles()
        if len(obj.data.loop_triangles) < 20:
            continue
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        modifier = obj.modifiers.new(name="QuestWatercraftReduction", type="DECIMATE")
        modifier.decimate_type = "COLLAPSE"
        modifier.ratio = ratio
        modifier.use_collapse_triangulate = True
        modifier.delimit = {"MATERIAL", "SHARP"}
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj.select_set(False)


def join(objects: list[bpy.types.Object], name: str) -> bpy.types.Object:
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    result = bpy.context.view_layer.objects.active
    result.name = name
    result.data.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return result


def normalize(
    obj: bpy.types.Object,
    width_axis: int,
    up_axis: int,
    length_axis: int,
    width: float,
    height: float,
    length: float,
    reverse_length: bool = False,
) -> None:
    low, high = transformed_bounds([obj])
    size = high - low
    center = (low + high) * 0.5
    for vertex in obj.data.vertices:
        point = vertex.co.copy()
        x = (point[width_axis] - center[width_axis]) * width / size[width_axis]
        y = (point[up_axis] - low[up_axis]) * height / size[up_axis]
        z = (point[length_axis] - center[length_axis]) * length / size[length_axis]
        vertex.co = (x, y, -z if reverse_length else z)
    obj.data.update()


def add_cube(name: str, location: tuple[float, float, float], scale: tuple[float, float, float], semantic: str) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cube_add(location=location)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    material = bpy.data.materials.new(semantic)
    obj.data.materials.append(material)
    assign_semantics(obj)
    return obj


def add_cylinder(name: str, location: tuple[float, float, float], radius: float, depth: float, semantic: str, rotation: tuple[float, float, float] = (0.0, 0.0, 0.0)) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=8, radius=radius, depth=depth, location=location, rotation=rotation
    )
    obj = bpy.context.object
    obj.name = name
    material = bpy.data.materials.new(semantic)
    obj.data.materials.append(material)
    assign_semantics(obj)
    return obj


def add_extruded_polygon(
    name: str,
    points: list[tuple[float, float]],
    bottom: float,
    top: float,
    semantic: str,
) -> bpy.types.Object:
    vertices = [(x, bottom, z) for x, z in points] + [(x, top, z) for x, z in points]
    count = len(points)
    faces = [tuple(range(count - 1, -1, -1)), tuple(range(count, count * 2))]
    for index in range(count):
        nxt = (index + 1) % count
        faces.append((index, nxt, count + nxt, count + index))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(bpy.data.materials.new(semantic))
    assign_semantics(obj)
    return obj


def add_triangular_sail(
    name: str,
    points_yz: list[tuple[float, float]],
    thickness: float,
    semantic: str,
) -> bpy.types.Object:
    points = [(-thickness, y, z) for y, z in points_yz] + [
        (thickness, y, z) for y, z in points_yz
    ]
    faces = [(0, 2, 1), (3, 4, 5), (0, 1, 4, 3), (1, 2, 5, 4), (2, 0, 3, 5)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(points, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(bpy.data.materials.new(semantic))
    assign_semantics(obj)
    return obj


def add_section_hull(
    name: str,
    stations: list[tuple[float, float, float, float]],
    semantic: str = "primary",
) -> bpy.types.Object:
    """Create a compact hard-chine hull from z/half-width/bottom/top stations."""
    vertices: list[tuple[float, float, float]] = []
    for z, half_width, bottom, top in stations:
        vertices.extend(
            [
                (0.0, bottom, z),
                (-half_width * 0.82, bottom + (top - bottom) * 0.18, z),
                (-half_width, top, z),
                (half_width, top, z),
                (half_width * 0.82, bottom + (top - bottom) * 0.18, z),
            ]
        )
    faces: list[tuple[int, ...]] = []
    ring = 5
    for station in range(len(stations) - 1):
        start = station * ring
        nxt = (station + 1) * ring
        for side in range(ring):
            following = (side + 1) % ring
            # Stations in the authored derivatives run bow-to-stern (descending
            # z). Match the end caps' outward winding, including the keel strip.
            faces.append((start + side, start + following, nxt + following, nxt + side))
    faces.append(tuple(range(ring - 1, -1, -1)))
    last = (len(stations) - 1) * ring
    faces.append(tuple(last + index for index in range(ring)))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(bpy.data.materials.new(semantic))
    assign_semantics(obj)
    return obj


def add_flat_oval(
    name: str,
    location: tuple[float, float, float],
    radius_x: float,
    radius_z: float,
    thickness: float,
    semantic: str,
) -> bpy.types.Object:
    obj = add_cylinder(
        name,
        location,
        1.0,
        thickness,
        semantic,
        (math.pi / 2.0, 0.0, 0.0),
    )
    obj.scale.x = radius_x
    obj.scale.y = radius_z
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return obj


def voxel_remesh(obj: bpy.types.Object, depth: int = 7) -> None:
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    modifier = obj.modifiers.new(name="QuestWatercraftSilhouette", type="REMESH")
    modifier.mode = "VOXEL"
    modifier.octree_depth = depth
    modifier.scale = 0.9
    modifier.use_remove_disconnected = True
    modifier.threshold = 1.0
    modifier.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.select_set(False)


def add_modern_kayak_cockpit() -> list[bpy.types.Object]:
    cockpit = add_cylinder(
        "MoldedCockpit", (0.0, 0.39, -0.15), 0.29, 0.035, "dark", (math.pi / 2.0, 0.0, 0.0)
    )
    cockpit.scale.y = 1.7
    bpy.context.view_layer.objects.active = cockpit
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return [cockpit]


def configure_derivative(name: str) -> tuple[list[bpy.types.Object], tuple[int, int, int, float, float, float, bool], int]:
    objects = mesh_objects()
    if name == "DINGHY_SAIL_UP":
        target = (0, 2, 1, 1.45, 5.2, 4.2, True)
        return objects, target, 1050
    if name == "DINGHY_STORED":
        # Removing the rig from the downloaded source leaves non-manifold deck
        # fragments. Use a source-dimensioned clean hull silhouette instead.
        for obj in mesh_objects():
            bpy.data.objects.remove(obj, do_unlink=True)
        hull = add_section_hull(
            "Laser47_SourceMeasuredStoredHull",
            [
                (2.10, 0.04, 0.24, 0.48),
                (1.55, 0.48, 0.05, 0.55),
                (0.55, 0.69, 0.01, 0.57),
                (-0.95, 0.72, 0.03, 0.52),
                (-1.85, 0.61, 0.10, 0.46),
                (-2.10, 0.52, 0.18, 0.42),
            ],
        )
        cockpit = add_flat_oval("OpenCockpit", (0.0, 0.58, -0.42), 0.46, 0.78, 0.035, "dark")
        return [hull, cockpit], (0, 1, 2, 1.45, 0.58, 4.2, False), 850
    if name in ("MOORED_DAY_SAILER", "DAY_SAILER_UNDERWAY"):
        for obj in mesh_objects():
            bpy.data.objects.remove(obj, do_unlink=True)
        hull = add_section_hull(
            name + "_SourceMeasuredHull",
            [
                (3.20, 0.08, 0.48, 0.92),
                (2.45, 0.73, 0.10, 1.04),
                (1.15, 1.03, 0.02, 1.08),
                (-1.35, 1.10, 0.03, 1.02),
                (-2.70, 0.92, 0.12, 0.92),
                (-3.20, 0.62, 0.28, 0.84),
            ],
        )
        additions = [
            add_flat_oval("Cockpit", (0.0, 1.09, -0.80), 0.66, 1.05, 0.045, "dark"),
            add_cube("LowCabin", (0.0, 1.16, 0.58), (0.54, 0.14, 0.68), "primary"),
            add_cube("FinKeel", (0.0, -0.24, 0.02), (0.08, 0.34, 0.72), "hardware"),
            add_cylinder("Mast", (0.0, 4.05, 0.35), 0.045, 6.2, "hardware", (math.pi / 2.0, 0.0, 0.0)),
            add_cylinder(
                "BoomCover",
                (0.0, 1.86, -0.45),
                0.09,
                2.45,
                "canvas",
                (0.0, 0.0, 0.0),
            ),
        ]
        if name == "DAY_SAILER_UNDERWAY":
            additions.extend(
                [
                    add_triangular_sail(
                        "MainSail", [(1.92, 0.15), (6.9, 0.30), (1.92, -1.95)], 0.025, "canvas"
                    ),
                    add_triangular_sail(
                        "Jib", [(6.55, 0.25), (1.92, 2.35), (1.92, 0.25)], 0.025, "accent"
                    ),
                ]
            )
        return [hull] + additions, (0, 1, 2, 2.2, 7.25, 6.4, False), 1450
    if name in ("POWERBOAT_AFLOAT", "POWERBOAT_TRAILERED"):
        for obj in mesh_objects():
            bpy.data.objects.remove(obj, do_unlink=True)
        hull = add_section_hull(
            name + "_SourceMeasuredHull",
            [
                (3.50, 0.06, 0.52, 1.02),
                (2.65, 0.73, 0.12, 1.18),
                (1.35, 1.10, 0.02, 1.20),
                (-1.45, 1.22, 0.04, 1.12),
                (-3.05, 1.10, 0.14, 1.02),
                (-3.50, 0.98, 0.28, 0.96),
            ],
        )
        additions = [
            add_flat_oval("Cockpit", (0.0, 1.22, -0.62), 0.86, 1.42, 0.055, "dark"),
            add_cube("Windshield", (0.0, 1.46, 0.34), (0.86, 0.20, 0.065), "dark"),
            add_cylinder("WakeTowerLeft", (-0.74, 1.92, -0.42), 0.035, 1.12, "hardware", (math.pi / 2.0, 0.0, 0.0)),
            add_cylinder("WakeTowerRight", (0.74, 1.92, -0.42), 0.035, 1.12, "hardware", (math.pi / 2.0, 0.0, 0.0)),
            add_cylinder("WakeTowerTop", (0.0, 2.47, -0.42), 0.04, 1.48, "hardware", (0.0, math.pi / 2.0, 0.0)),
        ]
        if name == "POWERBOAT_TRAILERED":
            additions.extend(
                [
                    add_cube("TrailerLeft", (-0.62, 0.23, -0.10), (0.055, 0.055, 3.65), "trailer"),
                    add_cube("TrailerRight", (0.62, 0.23, -0.10), (0.055, 0.055, 3.65), "trailer"),
                    add_cube("TrailerAxle", (0.0, 0.34, -0.95), (1.15, 0.05, 0.05), "trailer"),
                    add_cube("TowTongue", (0.0, 0.18, 4.05), (0.06, 0.06, 0.75), "trailer"),
                    add_cylinder("WheelLeft", (-1.18, 0.38, -0.95), 0.38, 0.20, "dark", (0.0, math.pi / 2.0, 0.0)),
                    add_cylinder("WheelRight", (1.18, 0.38, -0.95), 0.38, 0.20, "dark", (0.0, math.pi / 2.0, 0.0)),
                ]
            )
        return [hull] + additions, (0, 1, 2, 2.55, 2.75, 8.2, False), 1950 if name == "POWERBOAT_TRAILERED" else 1450
    if name in ("TOURING_KAYAK_AFLOAT", "TOURING_KAYAK_STORED"):
        retain_named({"Kayak_LP"})
        hull = keep_largest_loose_component(mesh_objects()[0])
        bpy.context.view_layer.objects.active = hull
        hull.select_set(True)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        normalize(hull, 1, 2, 0, 0.72, 0.42, 4.5, False)
        objects = [hull]
        if name == "TOURING_KAYAK_AFLOAT":
            objects.extend(
                [
                    add_cylinder("DoublePaddleShaft", (0.52, 0.25, 0.0), 0.025, 2.45, "hardware"),
                    add_cube("PaddleBladeForward", (0.52, 0.25, 1.38), (0.13, 0.025, 0.24), "accent"),
                    add_cube("PaddleBladeAft", (0.52, 0.25, -1.38), (0.13, 0.025, 0.24), "accent"),
                ]
            )
        target = (1, 2, 0, 0.72, 0.42, 4.5, False)
        # The retained low-poly source hull already contains its cockpit opening.
        return objects, target, 330
    if name in ("SUP_AFLOAT", "SUP_STORED"):
        for obj in mesh_objects():
            bpy.data.objects.remove(obj, do_unlink=True)
        outline = [
            (-0.26, -1.62), (-0.40, -1.38), (-0.43, -0.95), (-0.43, 0.92),
            (-0.37, 1.30), (-0.20, 1.58), (0.20, 1.58), (0.37, 1.30),
            (0.43, 0.92), (0.43, -0.95), (0.40, -1.38), (0.26, -1.62),
        ]
        board = add_extruded_polygon("Board", outline, 0.0, 0.13, "primary")
        pad = add_cube("DeckPad", (0.0, 0.145, -0.05), (0.31, 0.018, 0.78), "dark")
        additions = [board, pad]
        if name == "SUP_AFLOAT":
            additions.extend(
                [
                    add_cylinder("PaddleShaft", (0.54, 0.22, -0.05), 0.025, 2.35, "hardware"),
                    add_cube("PaddleBlade", (0.54, 0.22, 1.23), (0.13, 0.025, 0.25), "accent"),
                ]
            )
        return additions, (
            0, 1, 2, 0.86, 0.22 if name == "SUP_AFLOAT" else 0.18, 3.25, False
        ), 190 if name == "SUP_AFLOAT" else 130
    raise ValueError(f"Unknown derivative: {name}")


def collapse_materials(obj: bpy.types.Object) -> None:
    for polygon in obj.data.polygons:
        polygon.material_index = 0
    obj.data.materials.clear()
    obj.data.materials.append(bpy.data.materials.new("WatercraftSemantic"))
    obj.data.update()


def export_fbx(obj: bpy.types.Object, path: Path) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        use_selection=True,
        use_mesh_modifiers=True,
        use_triangles=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="AUTO",
        embed_textures=False,
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        mesh_smooth_type="FACE",
        colors_type="LINEAR",
    )


def render_previews(obj: bpy.types.Object, directory: Path, name: str) -> list[str]:
    directory.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "VERTEX"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    if name.startswith("DINGHY"):
        palette = {
            "primary": (0.82, 0.84, 0.82, 1.0),
            "dark": (0.025, 0.032, 0.040, 1.0),
            "hardware": (0.24, 0.28, 0.31, 1.0),
            "canvas": (0.88, 0.88, 0.84, 1.0),
            "accent": (0.30, 0.035, 0.025, 1.0),
            "trailer": (0.30, 0.33, 0.34, 1.0),
        }
    elif "DAY_SAILER" in name:
        palette = {
            "primary": (0.86, 0.86, 0.80, 1.0),
            "dark": (0.018, 0.025, 0.035, 1.0),
            "hardware": (0.22, 0.25, 0.27, 1.0),
            "canvas": (0.018, 0.045, 0.12, 1.0) if name == "MOORED_DAY_SAILER" else (0.88, 0.88, 0.84, 1.0),
            # The source imagery does not support a brightly colored jib. Keep
            # both sail fields in plausible off-white/very-light-gray canvas.
            "accent": (0.78, 0.80, 0.79, 1.0),
            "trailer": (0.30, 0.33, 0.34, 1.0),
        }
    elif name.startswith("POWERBOAT"):
        palette = {
            "primary": (0.88, 0.88, 0.84, 1.0),
            "dark": (0.020, 0.028, 0.038, 1.0),
            "hardware": (0.28, 0.31, 0.33, 1.0),
            "canvas": (0.018, 0.045, 0.12, 1.0),
            "accent": (0.025, 0.060, 0.15, 1.0),
            "trailer": (0.32, 0.35, 0.36, 1.0),
        }
    elif "KAYAK" in name:
        palette = {
            "primary": (0.015, 0.30, 0.34, 1.0),
            "dark": (0.018, 0.022, 0.025, 1.0),
            "hardware": (0.18, 0.21, 0.23, 1.0),
            "canvas": (0.08, 0.10, 0.11, 1.0),
            "accent": (0.52, 0.14, 0.015, 1.0),
            "trailer": (0.30, 0.33, 0.34, 1.0),
        }
    else:
        palette = {
            "primary": (0.018, 0.32, 0.36, 1.0),
            "dark": (0.025, 0.032, 0.035, 1.0),
            "hardware": (0.22, 0.25, 0.27, 1.0),
            "canvas": (0.08, 0.10, 0.11, 1.0),
            "accent": (0.58, 0.18, 0.02, 1.0),
            "trailer": (0.30, 0.33, 0.34, 1.0),
        }
    attribute = obj.data.color_attributes.get("WatercraftSemantic")
    if attribute is not None:
        for entry in attribute.data:
            red, green, blue = entry.color[:3]
            bits = (red > 0.5, green > 0.5, blue > 0.5)
            semantic = {
                (True, False, False): "primary",
                (False, True, False): "dark",
                (False, False, True): "hardware",
                (True, True, False): "canvas",
                (True, False, True): "accent",
                (False, True, True): "trailer",
            }.get(bits, "primary")
            entry.color = palette[semantic]
    scene.render.resolution_x = 640
    scene.render.resolution_y = 480
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    low, high = transformed_bounds([obj])
    center = (low + high) * 0.5
    radius = max((high - low).length * 0.62, 2.0)
    camera_data = bpy.data.cameras.new("PreviewCamera")
    camera = bpy.data.objects.new("PreviewCamera", camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = radius * 1.9
    views = {
        "top": Vector((0.0, radius * 2.0, 0.001)),
        # Keep the profile view exactly level. The earlier elevated profile
        # projected the mast and hull into a misleading edge-on composition.
        "side": Vector((radius * 2.0, 0.0, 0.0)),
        "quarter": Vector((radius * 1.5, radius * 0.85, -radius * 1.5)),
    }
    outputs = []
    for label, offset in views.items():
        camera.location = center + offset
        # Blender's convenience tracking helper assumes Z-up, while these
        # derivatives deliberately use Unity coordinates (Y-up). Build the
        # camera basis explicitly so masts are vertical in profile renders.
        forward = (center - camera.location).normalized()
        right = forward.cross(Vector((0.0, 1.0, 0.0))).normalized()
        true_up = right.cross(forward).normalized()
        camera.rotation_euler = Matrix((right, true_up, -forward)).transposed().to_euler()
        path = directory / f"{name.lower()}_{label}.png"
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        outputs.append(str(path.resolve()))
    return outputs


def main() -> None:
    args = parse_args()
    clear_scene()
    import_source(args.source.resolve())
    selected, target, target_triangles = configure_derivative(args.derivative)
    # Day-sailer additions are already in Unity axes and normalized. Every other
    # derivative is normalized from its source axes after reduction.
    already_normalized = (
        "DAY_SAILER" in args.derivative
        or args.derivative.startswith("POWERBOAT")
        or args.derivative.startswith("SUP_")
        or args.derivative == "DINGHY_STORED"
        or args.derivative.startswith("TOURING_KAYAK_")
    )
    for obj in selected:
        assign_semantics(obj)
    decimate(selected, target_triangles)
    merged = join(selected, args.derivative)
    if not already_normalized:
        normalize(merged, *target)
    collapse_materials(merged)
    merged.data.calc_loop_triangles()
    merged.data.validate(clean_customdata=False)
    merged.data.update()
    export_fbx(merged, args.output.resolve())
    low, high = transformed_bounds([merged])
    previews = render_previews(merged, args.preview_dir.resolve(), args.derivative)
    report = {
        "schema": "questflight.reservoir-watercraft-derivative-v1",
        "derivative": args.derivative,
        "source": str(args.source.resolve()),
        "output": str(args.output.resolve()),
        "vertices": len(merged.data.vertices),
        "triangles": len(merged.data.loop_triangles),
        "material_slots": len(merged.data.materials),
        "color_attribute": merged.data.color_attributes.active_color_name,
        "bounds_min": [round(value, 6) for value in low],
        "bounds_max": [round(value, 6) for value in high],
        "bounds_size": [round(value, 6) for value in high - low],
        "target_triangle_budget": target_triangles,
        "previews": previews,
        "textures_retained": 0,
        "animations_retained": 0,
        "semantic_colors": SEMANTIC,
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
