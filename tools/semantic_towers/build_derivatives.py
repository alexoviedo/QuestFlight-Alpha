"""Build optimized, source-derived semantic tower FBX assets with Blender.

Raw source files stay outside the repository. The committed outputs contain only
the pruned lattice section and cellular panel geometry needed by the product,
plus project-authored guy wires and industrial-stack geometry.
"""

from __future__ import annotations

import argparse
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def parse_args() -> argparse.Namespace:
    separator = sys.argv.index("--") if "--" in sys.argv else len(sys.argv)
    parser = argparse.ArgumentParser()
    parser.add_argument("--cell-blend", type=Path, required=True)
    parser.add_argument("--radio-fbx", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    return parser.parse_args(sys.argv[separator + 1 :])


def world_geometry(obj: bpy.types.Object) -> tuple[list[Vector], list[tuple[int, ...]]]:
    vertices = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    faces = [tuple(polygon.vertices) for polygon in obj.data.polygons]
    return vertices, faces


def append_geometry(
    destination_vertices: list[tuple[float, float, float]],
    destination_faces: list[tuple[int, ...]],
    vertices: list[Vector],
    faces: list[tuple[int, ...]],
) -> None:
    offset = len(destination_vertices)
    destination_vertices.extend((vertex.x, vertex.y, vertex.z) for vertex in vertices)
    destination_faces.extend(tuple(offset + index for index in face) for face in faces)


def add_wire(
    vertices: list[tuple[float, float, float]],
    faces: list[tuple[int, ...]],
    start: Vector,
    end: Vector,
    radius: float,
    sides: int = 4,
) -> None:
    direction = (end - start).normalized()
    reference = Vector((0.0, 0.0, 1.0))
    if abs(direction.dot(reference)) > 0.95:
        reference = Vector((1.0, 0.0, 0.0))
    tangent = direction.cross(reference).normalized()
    bitangent = direction.cross(tangent).normalized()
    offset = len(vertices)
    for point in (start, end):
        for side in range(sides):
            angle = 2.0 * math.pi * side / sides
            radial = tangent * math.cos(angle) * radius + bitangent * math.sin(angle) * radius
            value = point + radial
            vertices.append((value.x, value.y, value.z))
    for side in range(sides):
        following = (side + 1) % sides
        faces.append((offset + side, offset + following, offset + sides + following, offset + sides + side))


def create_mesh_object(
    name: str,
    vertices: list[tuple[float, float, float]],
    faces: list[tuple[int, ...]],
    vertex_colors: list[tuple[float, float, float, float]] | None = None,
    vertex_uvs: list[tuple[float, float]] | None = None,
) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update(calc_edges=True)
    if vertex_colors is not None:
        colors = mesh.color_attributes.new(name="Col", type="BYTE_COLOR", domain="CORNER")
        for loop in mesh.loops:
            colors.data[loop.index].color = vertex_colors[loop.vertex_index]
    if vertex_uvs is not None:
        uvs = mesh.uv_layers.new(name="SemanticLight")
        for loop in mesh.loops:
            uvs.data[loop.index].uv = vertex_uvs[loop.vertex_index]
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def add_beacon_octahedron(
    vertices: list[tuple[float, float, float]],
    faces: list[tuple[int, ...]],
    center: Vector,
    radius: float,
) -> range:
    """Add a small opaque fixture; caller assigns its encoded light family."""
    offset = len(vertices)
    vertices.extend(
        [
            (center.x, center.y, center.z + radius),
            (center.x, center.y, center.z - radius),
            (center.x + radius, center.y, center.z),
            (center.x - radius, center.y, center.z),
            (center.x, center.y + radius, center.z),
            (center.x, center.y - radius, center.z),
        ]
    )
    faces.extend(
        [
            (offset, offset + 2, offset + 4),
            (offset, offset + 4, offset + 3),
            (offset, offset + 3, offset + 5),
            (offset, offset + 5, offset + 2),
            (offset + 1, offset + 4, offset + 2),
            (offset + 1, offset + 3, offset + 4),
            (offset + 1, offset + 5, offset + 3),
            (offset + 1, offset + 2, offset + 5),
        ]
    )
    return range(offset, offset + 6)


def build_lattice(
    name: str,
    source_tower: bpy.types.Object,
    panel_objects: list[bpy.types.Object],
    sections: int,
    include_panels: bool,
) -> bpy.types.Object:
    height = 50.9
    base_width = 2.35
    top_width = 1.05
    source_vertices, source_faces = world_geometry(source_tower)
    min_x = min(vertex.x for vertex in source_vertices)
    max_x = max(vertex.x for vertex in source_vertices)
    min_y = min(vertex.y for vertex in source_vertices)
    max_y = max(vertex.y for vertex in source_vertices)
    min_z = min(vertex.z for vertex in source_vertices)
    max_z = max(vertex.z for vertex in source_vertices)
    source_width = max(max_x - min_x, max_y - min_y)
    source_height = max_z - min_z
    center_x = (min_x + max_x) * 0.5
    center_y = (min_y + max_y) * 0.5
    segment_height = height / sections
    vertices: list[tuple[float, float, float]] = []
    faces: list[tuple[int, ...]] = []
    for section in range(sections):
        transformed = []
        for vertex in source_vertices:
            local_z = (vertex.z - min_z) / source_height
            z = (section + local_z) * segment_height
            width = base_width + (top_width - base_width) * (z / height)
            scale = width / source_width
            transformed.append(Vector(((vertex.x - center_x) * scale, (vertex.y - center_y) * scale, z)))
        append_geometry(vertices, faces, transformed, source_faces)

    if include_panels:
        # The CC0 source contains three linked panel-array meshes. Keep their
        # authored layout but reduce it to a compact array suitable for this site.
        for panel in panel_objects:
            panel_vertices, panel_faces = world_geometry(panel)
            transformed = [
                Vector((vertex.x * 0.45, vertex.y * 0.45, height - 1.65 + (vertex.z - 28.45) * 0.70))
                for vertex in panel_vertices
            ]
            append_geometry(vertices, faces, transformed, panel_faces)

    # OSM identifies this structure as guyed lattice. Six coarse four-sided
    # cables retain that semantic silhouette without a transparent line shader.
    for height_fraction, anchor_radius in ((0.55, 16.0), (0.84, 24.0)):
        for direction_index in range(3):
            angle = math.radians(20.0 + direction_index * 120.0)
            start = Vector((0.0, 0.0, height * height_fraction))
            end = Vector((math.cos(angle) * anchor_radius, math.sin(angle) * anchor_radius, 0.15))
            add_wire(vertices, faces, start, end, 0.035 if include_panels else 0.055)

    # FCC ASR 1024061 prescribes AC 70/7460-1J chapters 3, 4, 5, and 13.
    # Bake a top flashing red beacon and two opposite steady-burning midpoint
    # fixtures into the single submesh. The shared shader keeps them dark in
    # the fixed daytime presentation and exposes an editor diagnostic mode.
    ordinary_vertex_count = len(vertices)
    top_fixture = add_beacon_octahedron(vertices, faces, Vector((0.0, 0.0, 50.35)), 0.55)
    middle_a = add_beacon_octahedron(vertices, faces, Vector((1.05, 0.0, 25.45)), 0.40)
    middle_b = add_beacon_octahedron(vertices, faces, Vector((-1.05, 0.0, 25.45)), 0.40)

    colors: list[tuple[float, float, float, float]] = []
    uvs: list[tuple[float, float]] = []
    for index, vertex in enumerate(vertices):
        radial = math.hypot(vertex[0], vertex[1])
        if index >= ordinary_vertex_count:
            colors.append((0.18, 0.015, 0.01, 1.0))
            family = 1.0 if index in top_fixture else 2.0
            uvs.append((family, 0.37))
        elif radial > 4.0:
            colors.append((0.18, 0.20, 0.21, 0.0))
            uvs.append((0.0, 0.0))
        elif vertex[2] > height - 2.6:
            colors.append((0.88, 0.90, 0.89, 0.0))
            uvs.append((0.0, 0.0))
        else:
            # Five-metre alternating bands retain the source-required aviation
            # orange/white treatment without a texture or material split.
            band = int(max(0.0, vertex[2]) / 5.0)
            colors.append((0.90, 0.25, 0.035, 0.0) if band % 2 == 0 else (0.88, 0.90, 0.89, 0.0))
            uvs.append((0.0, 0.0))
    return create_mesh_object(name, vertices, faces, colors, uvs)


def build_stack(name: str, sides: int, rings: int) -> bpy.types.Object:
    height = 113.4
    base_radius = 4.65
    top_radius = 2.45
    vertices: list[tuple[float, float, float]] = []
    faces: list[tuple[int, ...]] = []
    for ring in range(rings + 1):
        fraction = ring / rings
        z = height * fraction
        radius = base_radius + (top_radius - base_radius) * fraction
        for side in range(sides):
            angle = 2.0 * math.pi * side / sides
            vertices.append((math.cos(angle) * radius, math.sin(angle) * radius, z))
    for ring in range(rings):
        for side in range(sides):
            following = (side + 1) % sides
            lower = ring * sides + side
            upper = (ring + 1) * sides + side
            faces.append((lower, lower - side + following, upper - side + following, upper))
    vertices.extend(((0.0, 0.0, 0.0), (0.0, 0.0, height)))
    bottom_center = len(vertices) - 2
    top_center = len(vertices) - 1
    for side in range(sides):
        following = (side + 1) % sides
        faces.append((bottom_center, following, side))
        top = rings * sides
        faces.append((top_center, top + side, top + following))
    colors = [(0.56, 0.55, 0.52, 0.0)] * len(vertices)
    uvs = [(0.0, 0.0)] * len(vertices)
    return create_mesh_object(name, vertices, faces, colors, uvs)


def export_selected(path: Path, objects: list[bpy.types.Object]) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=str(path),
        use_selection=True,
        object_types={"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="STRIP",
        embed_textures=False,
    )


def main() -> None:
    args = parse_args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(args.radio_fbx.resolve()))
    source_tower = next(obj for obj in bpy.context.scene.objects if obj.name == "Tower")

    with bpy.data.libraries.load(str(args.cell_blend.resolve()), link=False) as (source, destination):
        requested = [name for name in ("Plane", "Plane.001", "Plane.002") if name in source.objects]
        destination.objects = requested
    panel_objects = [obj for obj in destination.objects if obj is not None]
    for obj in panel_objects:
        bpy.context.scene.collection.objects.link(obj)

    lattice_lod0 = build_lattice("SemanticLatticeTowerV1_LOD0", source_tower, panel_objects, 26, True)
    lattice_lod1 = build_lattice("SemanticLatticeTowerV1_LOD1", source_tower, panel_objects, 13, False)
    export_selected(args.output_dir / "SemanticLatticeTowerV1.fbx", [lattice_lod0, lattice_lod1])

    stack_lod0 = build_stack("SemanticIndustrialStackV1_LOD0", 16, 6)
    stack_lod1 = build_stack("SemanticIndustrialStackV1_LOD1", 8, 2)
    export_selected(args.output_dir / "SemanticIndustrialStackV1.fbx", [stack_lod0, stack_lod1])


if __name__ == "__main__":
    main()
