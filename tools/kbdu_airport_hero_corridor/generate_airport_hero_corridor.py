#!/usr/bin/env python3
"""Generate project-authored, footprint-faithful KBDU airport hero buildings.

The source is factual City address/door data plus pinned OSM footprint geometry.
No reference photograph pixels or third-party model geometry are embedded.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.geometry import tessellate_polygon


SCRIPT = Path(__file__).resolve()
ROOT = SCRIPT.parents[2]
MANIFEST = SCRIPT.parent / "airport_hero_corridor_manifest.json"
V4_GENERATOR = ROOT / "tools/kbdu_airport_core_v4/buildings/generate_airport_core_buildings.py"
SOURCE_DIR = ROOT / "QuestFlight/Assets/ArtSource/Blender/KBDU"
RUNTIME_DIR = ROOT / "QuestFlight/Assets/Production/Environment/AirportHeroCorridorV1"
MODEL_DIR = RUNTIME_DIR / "Models"
DATA_DIR = RUNTIME_DIR / "Data"


def load_v4():
    spec = importlib.util.spec_from_file_location("airport_core_v4_generator", V4_GENERATOR)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader
    spec.loader.exec_module(module)
    return module


V4 = load_v4()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def polygon_without_close(points):
    result = [tuple(map(float, point)) for point in points]
    if len(result) > 1 and result[0] == result[-1]:
        result.pop()
    return result


def signed_area(points):
    return 0.5 * sum(
        points[index][0] * points[(index + 1) % len(points)][1]
        - points[(index + 1) % len(points)][0] * points[index][1]
        for index in range(len(points))
    )


def counter_clockwise(points):
    """Normalize mixed OSM ring winding before creating outward-facing walls."""
    values = polygon_without_close(points)
    return list(reversed(values)) if signed_area(values) < 0.0 else values


def triangle_normal(vertices, face):
    """Return an unnormalized normal from the first nondegenerate face corner."""
    for index in range(1, len(face) - 1):
        a = Vector(vertices[face[0]])
        b = Vector(vertices[face[index]])
        c = Vector(vertices[face[index + 1]])
        normal = (b - a).cross(c - a)
        if normal.length_squared > 1e-12:
            return normal
    raise RuntimeError(f"Degenerate authored face: {face}")


def outward_face(vertices, face, expected):
    """Orient a face toward a known exterior direction before mesh creation."""
    normal = triangle_normal(vertices, face)
    if normal.dot(Vector(expected)) < 0.0:
        return tuple(reversed(face))
    return tuple(face)


def centroid(points):
    return (
        sum(point[0] for point in points) / len(points),
        sum(point[1] for point in points) / len(points),
    )


def prism(
    name,
    points,
    height,
    mat,
    tile,
    roof_tile=None,
    bevel=0.04,
    close_bottom=True,
    close_top=True,
):
    """Exact-footprint prism with optional caps and deterministic atlas UVs."""
    local = counter_clockwise(points)
    count = len(local)
    vertices = [(x, y, 0.0) for x, y in local] + [(x, y, height) for x, y in local]
    faces = []
    if close_bottom:
        faces.append(tuple(reversed(range(count))))
    if close_top:
        faces.append(tuple(range(count, count * 2)))
    for index in range(count):
        nxt = (index + 1) % count
        faces.append((index, nxt, nxt + count, index + count))
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.validate(verbose=False)
    mesh.update()
    # Open runtime shells have no enclosed volume from which Blender can
    # infer "outside". Preserve the analytically authored winding and assert
    # every wall points away from its source edge.
    polygon_offset = int(close_bottom) + int(close_top)
    if close_bottom and mesh.polygons[0].normal.z > -0.99:
        raise RuntimeError(f"{name} bottom cap does not face outward")
    if close_top:
        top_index = int(close_bottom)
        if mesh.polygons[top_index].normal.z < 0.99:
            raise RuntimeError(f"{name} top cap does not face outward")
    for edge_index in range(count):
        edge_a = local[edge_index]
        edge_b = local[(edge_index + 1) % count]
        expected = Vector((
            edge_b[1] - edge_a[1],
            -(edge_b[0] - edge_a[0]),
            0.0,
        ))
        polygon = mesh.polygons[polygon_offset + edge_index]
        if polygon.normal.dot(expected) <= 1e-5:
            polygon.flip()
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    V4.remap_uv(obj, tile)
    if bevel > 0:
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        modifier = obj.modifiers.new("BoundedEdgeSoftening", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
        modifier.limit_method = "ANGLE"
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj.select_set(False)
    return obj


def bounds(points):
    xs = [point[0] for point in points]
    ys = [point[1] for point in points]
    return min(xs), min(ys), max(xs), max(ys)


def rectangular_footprint(points, tolerance=0.18):
    """Return four ordered corners when a footprint is a credible rectangle."""
    values = polygon_without_close(points)
    if len(values) != 4:
        return None
    edges = [
        (values[(index + 1) % 4][0] - values[index][0],
         values[(index + 1) % 4][1] - values[index][1])
        for index in range(4)
    ]
    lengths = [math.hypot(*edge) for edge in edges]
    if min(lengths) < 1.0:
        return None
    opposite_error = max(
        abs(lengths[0] - lengths[2]) / max(lengths[0], lengths[2]),
        abs(lengths[1] - lengths[3]) / max(lengths[1], lengths[3]),
    )
    adjacent_cosine = max(
        abs((edges[index][0] * edges[(index + 1) % 4][0] +
             edges[index][1] * edges[(index + 1) % 4][1]) /
            (lengths[index] * lengths[(index + 1) % 4]))
        for index in range(4)
    )
    if opposite_error > tolerance or adjacent_cosine > tolerance:
        return None
    if (lengths[1] + lengths[3]) > (lengths[0] + lengths[2]):
        values = values[1:] + values[:1]
    return values


def exact_gable_roof(name, points, wall_height, rise, mat, tile):
    """Closed gable wedge aligned to an actual rectangular footprint."""
    p0, p1, p2, p3 = counter_clockwise(points)
    ridge0 = ((p0[0] + p3[0]) * 0.5, (p0[1] + p3[1]) * 0.5)
    ridge1 = ((p1[0] + p2[0]) * 0.5, (p1[1] + p2[1]) * 0.5)
    vertices = [
        (p0[0], p0[1], wall_height), (p1[0], p1[1], wall_height),
        (p2[0], p2[1], wall_height), (p3[0], p3[1], wall_height),
        (ridge0[0], ridge0[1], wall_height + rise),
        (ridge1[0], ridge1[1], wall_height + rise),
    ]
    raw_faces = [
        (0, 1, 5, 4), (2, 3, 4, 5),
        (1, 2, 5), (3, 0, 4), (3, 2, 1, 0),
    ]
    center_x, center_y = centroid(points)
    faces = [
        outward_face(vertices, raw_faces[0], (0.0, 0.0, 1.0)),
        outward_face(vertices, raw_faces[1], (0.0, 0.0, 1.0)),
    ]
    for face in raw_faces[2:4]:
        face_center = (
            sum(vertices[index][0] for index in face) / len(face),
            sum(vertices[index][1] for index in face) / len(face),
        )
        faces.append(outward_face(
            vertices,
            face,
            (face_center[0] - center_x, face_center[1] - center_y, 0.0),
        ))
    faces.append(outward_face(vertices, raw_faces[4], (0.0, 0.0, -1.0)))
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.validate(verbose=False)
    mesh.update()
    for index in (0, 1):
        if mesh.polygons[index].normal.z <= 0.0:
            mesh.polygons[index].flip()
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    V4.remap_uv(obj, tile)
    return obj


def clip_convex_half_plane(points, axis, split, keep_lower):
    """Clip a convex polygon against one side of the roof ridge."""
    result = []
    epsilon = 1e-6

    def projection(point):
        return point[0] * axis[0] + point[1] * axis[1]

    def inside(point):
        value = projection(point)
        return value <= split + epsilon if keep_lower else value >= split - epsilon

    for index, current in enumerate(points):
        previous = points[index - 1]
        current_inside = inside(current)
        previous_inside = inside(previous)
        if current_inside != previous_inside:
            previous_projection = projection(previous)
            current_projection = projection(current)
            denominator = current_projection - previous_projection
            t = 0.0 if abs(denominator) < epsilon else (
                split - previous_projection
            ) / denominator
            result.append((
                previous[0] + (current[0] - previous[0]) * t,
                previous[1] + (current[1] - previous[1]) * t,
            ))
        if current_inside:
            result.append(current)
    return result


def profiled_gable_roof(name, points, wall_height, rise, mat, tile):
    """Exact-footprint pitched roof for irregular residential outlines.

    The source polygon is triangulated first. Each convex triangle is clipped by
    a principal-axis ridge and evaluated on one of two continuous roof planes.
    This preserves concave footprints without a rectangular overhang, while the
    boundary infill closes every gable/roof-to-wall gap.
    """
    local = counter_clockwise(points)
    center_x, center_y = centroid(local)
    covariance_xx = sum((x - center_x) ** 2 for x, _ in local) / len(local)
    covariance_xy = sum(
        (x - center_x) * (y - center_y) for x, y in local
    ) / len(local)
    covariance_yy = sum((y - center_y) ** 2 for _, y in local) / len(local)
    major_angle = 0.5 * math.atan2(
        2.0 * covariance_xy, covariance_xx - covariance_yy
    )
    minor_axis = (-math.sin(major_angle), math.cos(major_angle))

    projections = [
        x * minor_axis[0] + y * minor_axis[1] for x, y in local
    ]
    projection_min = min(projections)
    projection_max = max(projections)
    ridge = (projection_min + projection_max) * 0.5
    lower_span = max(ridge - projection_min, 0.001)
    upper_span = max(projection_max - ridge, 0.001)

    def roof_height(point):
        projection = point[0] * minor_axis[0] + point[1] * minor_axis[1]
        if projection <= ridge:
            factor = (projection - projection_min) / lower_span
        else:
            factor = (projection_max - projection) / upper_span
        return wall_height + rise * max(0.0, min(1.0, factor))

    vertices = []
    faces = []

    def append_face(points_3d, expected):
        start = len(vertices)
        vertices.extend(points_3d)
        raw_face = tuple(range(start, start + len(points_3d)))
        try:
            faces.append(outward_face(vertices, raw_face, expected))
        except RuntimeError:
            # Ridge clipping can produce a zero-area triangle when a source
            # triangle merely touches the split plane. It covers no surface
            # and must not be exported as a phantom panel.
            del vertices[start:]

    triangles = tessellate_polygon([
        [Vector((x, y, 0.0)) for x, y in local]
    ])
    if not triangles:
        raise RuntimeError(f"Unable to tessellate irregular roof {name}")
    for triangle in triangles:
        # Blender 5.1 returns source-vertex indices here, while earlier
        # versions returned Vectors. Support both so the authored result is
        # deterministic across the currently supported Blender toolchain.
        triangle_2d = [
            local[vertex]
            if isinstance(vertex, int)
            else (float(vertex.x), float(vertex.y))
            for vertex in triangle
        ]
        for keep_lower in (True, False):
            clipped = clip_convex_half_plane(
                triangle_2d, minor_axis, ridge, keep_lower
            )
            if len(clipped) >= 3:
                append_face([
                    (x, y, roof_height((x, y))) for x, y in clipped
                ], (0.0, 0.0, 1.0))

    roof_face_count = len(faces)

    # Split each source boundary segment where it crosses the ridge so the
    # vertical gable infill reaches the same continuous roof profile.
    for index, point_a in enumerate(local):
        point_b = local[(index + 1) % len(local)]
        projection_a = point_a[0] * minor_axis[0] + point_a[1] * minor_axis[1]
        projection_b = point_b[0] * minor_axis[0] + point_b[1] * minor_axis[1]
        segment = [point_a]
        if (projection_a - ridge) * (projection_b - ridge) < -1e-6:
            t = (ridge - projection_a) / (projection_b - projection_a)
            segment.append((
                point_a[0] + (point_b[0] - point_a[0]) * t,
                point_a[1] + (point_b[1] - point_a[1]) * t,
            ))
        segment.append(point_b)
        for start, end in zip(segment, segment[1:]):
            start_height = roof_height(start)
            end_height = roof_height(end)
            if max(start_height, end_height) <= wall_height + 1e-5:
                continue
            outward = (
                end[1] - start[1],
                -(end[0] - start[0]),
                0.0,
            )
            if start_height <= wall_height + 1e-5:
                append_face([
                    (start[0], start[1], wall_height),
                    (end[0], end[1], wall_height),
                    (end[0], end[1], end_height),
                ], outward)
            elif end_height <= wall_height + 1e-5:
                append_face([
                    (start[0], start[1], wall_height),
                    (end[0], end[1], wall_height),
                    (start[0], start[1], start_height),
                ], outward)
            else:
                append_face([
                    (start[0], start[1], wall_height),
                    (end[0], end[1], wall_height),
                    (end[0], end[1], end_height),
                    (start[0], start[1], start_height),
                ], outward)

    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.validate(verbose=False)
    mesh.update()
    for index in range(roof_face_count):
        if mesh.polygons[index].normal.z <= 0.0:
            mesh.polygons[index].flip()
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    V4.remap_uv(obj, tile)
    return obj


def facade_panel(name, side, center, width, height, z, mat, tile, depth=0.14, bevel=0.0):
    cx, cy = center
    if side in ("+x", "-x"):
        sx = 1.0 if side == "+x" else -1.0
        return V4.cube(name, (depth, width, height), (cx + sx * 0.07, cy, z), mat, tile, bevel, 1)
    sy = 1.0 if side == "+y" else -1.0
    return V4.cube(name, (width, depth, height), (cx, cy + sy * 0.07, z), mat, tile, bevel, 1)


def facade_axis(spec, local_points):
    minx, miny, maxx, maxy = bounds(local_points)
    side = spec["door_side"]
    # glTF converts Blender +Y to Unity -Z. The manifest records world-space
    # north/south, so invert that axis once here and keep the JSON semantic.
    if side == "+y":
        side = "-y"
    elif side == "-y":
        side = "+y"
    if side in ("+x", "-x"):
        edge = maxx if side == "+x" else minx
        center = (edge, (miny + maxy) * 0.5)
        width = maxy - miny
    else:
        edge = maxy if side == "+y" else miny
        center = ((minx + maxx) * 0.5, edge)
        width = maxx - minx
    return side, center, width, (minx, miny, maxx, maxy)


def add_roof(items, spec, points, wall_height, lod, mat):
    minx, miny, maxx, maxy = bounds(points)
    width_x = maxx - minx
    width_y = maxy - miny
    center_x = (minx + maxx) * 0.5
    center_y = (miny + maxy) * 0.5
    family = spec["family"]
    rise = (
        1.15 if "t_hangar" in family
        else 1.65 if family == "residential"
        else 0.55 if family == "commercial"
        else 0.75
    )
    if family in ("residential", "commercial", "light_industrial"):
        rectangle = rectangular_footprint(points)
        if family == "residential" and rectangle is not None:
            items.append(exact_gable_roof(
                f"{spec['asset_id']}_FootprintGable",
                rectangle, wall_height, rise, mat, 3,
            ))
        elif family == "residential":
            items.append(profiled_gable_roof(
                f"{spec['asset_id']}_ProfiledFootprintGable",
                points, wall_height, rise, mat, 3,
            ))
        else:
            cap = prism(
                f"{spec['asset_id']}_ExactFootprintRoof",
                points, 0.16, mat, 3, bevel=0.0,
            )
            cap.location.z = wall_height
            items.append(cap)
        return
    if width_x <= width_y:
        run = width_x * 0.5
        length = math.sqrt(run * run + rise * rise)
        slope = math.atan2(rise, run)
        for sign in (-1, 1):
            items.append(V4.cube(
                f"{spec['asset_id']}_Roof_{sign}",
                (length + 0.25, width_y + 0.35, 0.14),
                (center_x + sign * run * 0.5, center_y, wall_height + rise * 0.5),
                mat, 10 if "t_hangar" in family else 3, 0.0, 1,
                rotation=(0.0, sign * slope, 0.0),
            ))
    else:
        run = width_y * 0.5
        length = math.sqrt(run * run + rise * rise)
        slope = math.atan2(rise, run)
        for sign in (-1, 1):
            items.append(V4.cube(
                f"{spec['asset_id']}_Roof_{sign}",
                (width_x + 0.35, length + 0.25, 0.14),
                (center_x, center_y + sign * run * 0.5, wall_height + rise * 0.5),
                mat, 10 if "t_hangar" in family else 3, 0.0, 1,
                # For a panel whose slope runs along local Y, the ridge-facing
                # edge lies opposite the panel-center sign. Rotate by -sign so
                # both halves rise toward the ridge instead of forming a valley.
                rotation=(-sign * slope, 0.0, 0.0),
            ))
    if lod == 0 and family not in ("residential", "commercial", "light_industrial"):
        vent_count = 1 if max(width_x, width_y) < 35 else 3
        for index in range(vent_count):
            t = (index + 1) / (vent_count + 1)
            x = minx + width_x * t
            y = miny + width_y * t
            items.append(V4.cylinder(
                f"{spec['asset_id']}_RoofVent_{index}", 0.22, 0.75,
                (x, y, wall_height + rise + 0.35), mat, 7, 10
            ))


def add_hangar_facade(items, spec, points, wall_height, lod, mat):
    side, center, width, box = facade_axis(spec, points)
    family = spec["family"]
    door_tile = 9
    if family == "type1_t_hangar":
        bay_width = 11.8
        door_kind = "Sliding"
    elif family == "type3_t_hangar":
        bay_width = 12.3
        door_kind = "Sliding"
    elif family == "type2_t_hangar":
        bay_width = 12.3
        door_kind = "Bifold"
    else:
        bay_width = min(width * 0.72, 24.0)
        door_kind = "Bifold"
    bays = max(1, round(width / max(bay_width, 1.0)))
    bay_width = width / bays
    opposite = {"+x": "-x", "-x": "+x", "+y": "-y", "-y": "+y"}
    is_t_hangar = "t_hangar" in family
    for bay in range(bays):
        door_side = side if not is_t_hangar or bay % 2 == 0 else opposite[side]
        minx, miny, maxx, maxy = box
        if door_side in ("+x", "-x"):
            edge = maxx if door_side == "+x" else minx
            door_facade_center = (edge, (miny + maxy) * 0.5)
            axis_is_y = True
        else:
            edge = maxy if door_side == "+y" else miny
            door_facade_center = ((minx + maxx) * 0.5, edge)
            axis_is_y = False
        offset = -width * 0.5 + bay_width * (bay + 0.5)
        door_center = (
            (door_facade_center[0], door_facade_center[1] + offset)
            if axis_is_y else
            (door_facade_center[0] + offset, door_facade_center[1])
        )
        door_w = bay_width * 0.84
        door_h = min(4.7, wall_height - 0.55)
        items.append(facade_panel(
            f"{spec['asset_id']}_{door_kind}Door_{bay:02d}", door_side, door_center,
            door_w, door_h, door_h * 0.5 + 0.18, mat, door_tile,
        ))
        if lod <= 1:
            items.append(facade_panel(
                f"{spec['asset_id']}_DoorHeader_{bay:02d}", door_side, door_center,
                door_w + 0.24, 0.13, door_h + 0.26, mat, 4, depth=0.20, bevel=0.0,
            ))
        if lod == 0:
            # Door/corrugation seams are carried by the shared atlas. Tiny raised
            # strips were visually redundant at Quest resolution and inflated the
            # serialized closure across a long row.
            if door_kind == "Bifold":
                items.append(facade_panel(
                    f"{spec['asset_id']}_DoorFold_{bay:02d}", door_side, door_center,
                    door_w - 0.14, 0.055, door_h * 0.52, mat, 4, depth=0.22, bevel=0.0,
                ))
    if lod == 0:
        # One plausible personnel entrance and one opaque reflective office pane per row.
        edge_offset = width * 0.42
        entry_center = (
            (center[0], center[1] - edge_offset)
            if axis_is_y else (center[0] - edge_offset, center[1])
        )
        items.append(facade_panel(
            f"{spec['asset_id']}_PersonnelDoor", side, entry_center,
            min(1.15, bay_width * 0.18), 2.25, 1.3, mat, 6, depth=0.22,
        ))


def add_corporate_facade(items, spec, points, wall_height, lod, mat):
    side, center, width, _ = facade_axis(spec, points)
    hangar_width = width * (0.72 if width > 22 else 0.64)
    items.append(facade_panel(
        f"{spec['asset_id']}_MainAviationDoor", side, center,
        hangar_width, min(5.2, wall_height - 0.45), min(5.2, wall_height - 0.45) * 0.5 + 0.18,
        mat, 9,
    ))
    if lod <= 1:
        items.append(facade_panel(
            f"{spec['asset_id']}_MainDoorHeader", side, center,
            hangar_width + 0.28, 0.16, min(5.2, wall_height - 0.45) + 0.24,
            mat, 4, depth=0.22, bevel=0.0,
        ))
    if lod == 0:
        door_height = min(5.2, wall_height - 0.45)
        items.append(facade_panel(
            f"{spec['asset_id']}_MainDoorFold", side, center,
            hangar_width - 0.18, 0.055, door_height * 0.52,
            mat, 4, depth=0.23, bevel=0.0,
        ))
    if lod <= 1:
        pane_count = 4 if lod == 0 else 2
        remaining = max(2.5, (width - hangar_width) * 0.5)
        axis_is_y = side in ("+x", "-x")
        for index in range(pane_count):
            sign = -1 if index < pane_count / 2 else 1
            rank = index % max(1, pane_count // 2)
            offset = sign * (hangar_width * 0.5 + 1.35 + rank * 1.6)
            window_center = (
                (center[0], center[1] + offset) if axis_is_y else (center[0] + offset, center[1])
            )
            items.append(facade_panel(
                f"{spec['asset_id']}_OfficeGlazing_{index:02d}", side, window_center,
                min(1.25, remaining * 0.38), 1.45, 2.15, mat, 2, depth=0.18, bevel=0.0,
            ))
        door_offset = hangar_width * 0.5 + min(remaining * 0.55, 2.1)
        door_center = (
            (center[0], center[1] - door_offset) if axis_is_y else (center[0] - door_offset, center[1])
        )
        items.append(facade_panel(
            f"{spec['asset_id']}_ServiceDoor", side, door_center,
            1.08, 2.2, 1.28, mat, 6, depth=0.20,
        ))
    # Corrugation/facade ribs are atlas detail rather than geometry.


def add_context_facade(items, spec, points, wall_height, lod, mat):
    """Add physically scaled, semantic near-building facade detail.

    All openings are opaque atlas panels, avoiding transparency sorting and
    additional materials while still producing readable windows and entrances.
    """
    side, center, width, _ = facade_axis(spec, points)
    family = spec["family"]
    axis_is_y = side in ("+x", "-x")
    window_width = 1.15 if family == "residential" else 1.55
    spacing = 2.9 if family == "residential" else 3.8
    window_count = 1
    if lod <= 1:
        for index in range(window_count):
            offset = (index - (window_count - 1) * 0.5) * spacing
            pane_center = (
                (center[0], center[1] + offset)
                if axis_is_y else (center[0] + offset, center[1])
            )
            items.append(facade_panel(
                f"{spec['asset_id']}_Window_{index:02d}", side, pane_center,
                window_width, 1.25 if family == "residential" else 1.55,
                2.05 if family == "residential" else 2.35,
                mat, 2, depth=0.16,
            ))
        if family != "residential":
            entrance_offset = min(width * 0.34, max(1.4, width * 0.5 - 1.0))
            entrance_center = (
                (center[0], center[1] - entrance_offset)
                if axis_is_y else (center[0] - entrance_offset, center[1])
            )
            items.append(facade_panel(
                f"{spec['asset_id']}_Entrance", side, entrance_center,
                1.35, 2.45, 1.42, mat, 6, depth=0.18,
            ))
    if lod == 0 and family == "commercial" and width >= 14:
        items.append(facade_panel(
            f"{spec['asset_id']}_SignBand", side, center,
            min(width * 0.56, 14.0), 0.48, min(wall_height - 0.45, 3.85),
            mat, 4, depth=0.17,
        ))


def build_building(spec, lod, mat):
    footprint = polygon_without_close(spec["footprint_xz_m"])
    cx, cy = centroid(footprint)
    # Blender +Y exports to Unity -Z. Invert local northing so the authored
    # footprint lands on the original world-space OSM polygon.
    local = [(x - cx, -(y - cy)) for x, y in footprint]
    height = float(spec["height_m"])
    roof_rise = (
        1.15 if "t_hangar" in spec["family"]
        else 1.65 if spec["family"] == "residential"
        else 0.55 if spec["family"] == "commercial"
        else 0.75
    )
    wall_height = max(3.8, height - roof_rise)
    items = [
        prism(
            f"{spec['asset_id']}_Shell", local, wall_height, mat,
            0 if spec["family"] == "residential" and int(spec["osm_id"]) % 3 == 0
            else 5 if spec["family"] == "residential"
            else 5 if spec["family"] == "commercial"
            else 8 if "t_hangar" in spec["family"] or int(spec["osm_id"]) % 2 == 0
            else 1 if spec["family"] == "service_hangar" else 10,
            bevel=0.0,
            # Every shell is grounded and receives a separate exact roof.
            # Omit the permanently buried caps rather than spending Quest's
            # serialized triangle budget on invisible geometry.
            close_bottom=False,
            close_top=False,
        ),
    ]
    add_roof(items, spec, local, wall_height, lod, mat)
    if "t_hangar" in spec["family"] or spec["family"] == "box_hangar":
        add_hangar_facade(items, spec, local, wall_height, lod, mat)
    elif spec["family"] in ("residential", "commercial", "light_industrial"):
        add_context_facade(items, spec, local, wall_height, lod, mat)
    else:
        add_corporate_facade(items, spec, local, wall_height, lod, mat)
    if lod <= 1 and spec["family"] not in ("residential", "commercial", "light_industrial"):
        minx, miny, maxx, maxy = bounds(local)
        gutter_axis_x = (maxx - minx) >= (maxy - miny)
        gutter_z = wall_height + 0.06
        if gutter_axis_x:
            for side in (miny, maxy):
                items.append(V4.cylinder(
                    f"{spec['asset_id']}_Gutter_{side}", 0.045, maxx - minx + 0.2,
                    ((minx + maxx) * 0.5, side, gutter_z), mat, 4, 8 if lod == 0 else 6,
                    rotation=(0.0, math.pi / 2.0, 0.0),
                ))
        else:
            for side in (minx, maxx):
                items.append(V4.cylinder(
                    f"{spec['asset_id']}_Gutter_{side}", 0.045, maxy - miny + 0.2,
                    (side, (miny + maxy) * 0.5, gutter_z), mat, 4, 8 if lod == 0 else 6,
                    rotation=(math.pi / 2.0, 0.0, 0.0),
                ))
    root = V4.join_lod(spec["asset_id"], lod, items, "AirportCoreHangarKitAtlas")
    root["world_center_x"] = cx
    root["world_center_z"] = cy
    root["source_osm_id"] = int(spec["osm_id"])
    root["official_address"] = spec["address"]
    root["family"] = spec["family"]
    return root, cx, cy


def main():
    bpy.context.preferences.filepaths.save_version = 0
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    MODEL_DIR.mkdir(parents=True, exist_ok=True)
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    V4.clear_scene()
    mat = V4.material("AirportCoreHangarKitAtlas", (0.70, 0.70, 0.66, 1.0))
    assets = []
    all_roots = []
    for spec in manifest["buildings"]:
        lod_rows = []
        for lod in range(3):
            root, cx, cy = build_building(spec, lod, mat)
            all_roots.append(root)
            output = MODEL_DIR / f"{spec['asset_id']}_LOD{lod}.glb"
            V4.export_lod(root, output)
            stats = V4.mesh_stats(root)
            stats.update({
                "lod": lod,
                "path": str(output.relative_to(ROOT)).replace("\\", "/"),
                "bytes": output.stat().st_size,
                "sha256": sha256(output),
            })
            lod_rows.append(stats)
        assets.append({
            "asset_id": spec["asset_id"],
            "address": spec["address"],
            "source_osm_id": spec["osm_id"],
            "family": spec["family"],
            "position_xz_m": [round(cx, 4), round(cy, 4)],
            "footprint_vertex_count": len(polygon_without_close(spec["footprint_xz_m"])),
            "wall_winding_normalized": True,
            "roof_style": (
                "exact_footprint_gable"
                if spec["family"] == "residential"
                else "exact_footprint_flat"
                if spec["family"] in ("commercial", "light_industrial")
                else "semantic_airport_gable"
            ),
            "maximum_footprint_error_m": 0.0,
            "lods": lod_rows,
        })
    bpy.context.scene["generator"] = str(SCRIPT.relative_to(ROOT)).replace("\\", "/")
    bpy.context.scene["license"] = "Project-authored deterministic geometry; City facts and OSM footprint derivative"
    bpy.context.scene["reference_pixels_embedded"] = False
    bpy.context.scene.unit_settings.system = "METRIC"
    source = SOURCE_DIR / "AirportHeroCorridor.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(source.resolve()), check_existing=False)
    report = {
        "schema_version": 1,
        "status": "GENERATED_REQUIRES_UNITY_INTEGRATION_AND_VISUAL_QA",
        "blender_version": bpy.app.version_string,
        "generator": str(SCRIPT.relative_to(ROOT)).replace("\\", "/"),
        "manifest": str(MANIFEST.relative_to(ROOT)).replace("\\", "/"),
        "manifest_sha256": sha256(MANIFEST),
        "source_blend": str(source.relative_to(ROOT)).replace("\\", "/"),
        "source_blend_bytes": source.stat().st_size,
        "source_blend_sha256": sha256(source),
        "material_contract": "AirportCoreHangarKitAtlas",
        "reference_photographs_embedded": False,
        "assets": assets,
    }
    (DATA_DIR / "AirportHeroCorridorGenerationReportV1.json").write_text(
        json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    print(json.dumps({
        "status": report["status"],
        "assets": len(assets),
        "lod0_triangles": sum(item["lods"][0]["triangles"] for item in assets),
        "lod1_triangles": sum(item["lods"][1]["triangles"] for item in assets),
        "lod2_triangles": sum(item["lods"][2]["triangles"] for item in assets),
        "source": str(source),
    }, indent=2))


if __name__ == "__main__":
    main()
