#!/usr/bin/env python3
"""Build the deterministic Grade-Separated Road Authority V1 product JSON."""

from __future__ import annotations

import argparse
import base64
import json
import math
import re
import struct
from pathlib import Path
from typing import Any

from shapely.geometry import LineString, Point, box
from shapely.ops import substring

from inventory_grade_separated_roads import load_json, osm_line


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_TERRAIN = (
    ROOT
    / "QuestFlight/Assets/Production/Environment/ReconstructionV2/"
    "KBDU_ProductionTerrainSurfaceV2.json"
)
DEFAULT_TERRAIN_ASSET = (
    ROOT
    / "QuestFlight/Assets/Production/Environment/ReconstructionV2/Generated/"
    "KBDU_ProductionTerrainSurfaceV2.asset"
)
DEFAULT_CONTEXT = (
    ROOT
    / "QuestFlight/Assets/Production/Environment/ReconstructionV2/"
    "KBDU_ReferenceContextV2.json"
)
DEFAULT_TRAFFIC = (
    ROOT
    / "QuestFlight/Assets/Production/Environment/TrafficV2/Data/"
    "KBDU_TrafficV2FullMapManifest.json"
)
DEFAULT_OUTPUT = (
    ROOT
    / "QuestFlight/Assets/Production/Environment/GradeSeparatedRoadsV1/Data/"
    "KBDU_GradeSeparatedRoadAuthorityV1.json"
)
SAMPLE_SPACING_METERS = 5.0
DECK_THICKNESS_METERS = 0.60
CLEARANCE_SAFETY_METERS = 0.10
FALLBACK_CLEARANCE_METERS = 17.0 * 0.3048
MAXIMUM_ACCEPTED_GRADE = 0.06
MAXIMUM_APPROACH_EXTENSION_METERS = 300.0
MINIMUM_APPROACH_EXTENSION_METERS = 200.0
CONTACT_OFFSETS_METERS = [0.0] * 6
CONTACT_HALF_LENGTHS_METERS = [2.3, 2.175, 2.35, 2.675, 2.525, 8.1]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", type=Path, required=True)
    parser.add_argument("--osm", type=Path, required=True)
    parser.add_argument("--cdot", type=Path, required=True)
    parser.add_argument("--terrain", type=Path, default=DEFAULT_TERRAIN)
    parser.add_argument("--terrain-asset", type=Path, default=DEFAULT_TERRAIN_ASSET)
    parser.add_argument("--context", type=Path, default=DEFAULT_CONTEXT)
    parser.add_argument("--traffic", type=Path, default=DEFAULT_TRAFFIC)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--diagnostic-output", type=Path)
    return parser.parse_args()


class TerrainSampler:
    _VECTOR = re.compile(
        r"\{x:\s*([^,]+),\s*y:\s*([^,]+),\s*z:\s*([^}]+)\}"
    )

    def __init__(self, document: dict[str, Any], production_asset: Path | None = None):
        self.layers: list[tuple[int, dict[str, Any], tuple[int, ...]]] = []
        for index, layer in enumerate(document["layers"]):
            count = int(layer["width"]) * int(layer["height"])
            payload = base64.b64decode(layer["height_cm_little_endian_base64"], validate=True)
            values = struct.unpack("<" + "h" * count, payload)
            self.layers.append((index * 10, layer, values))
        self.patches = self._load_transition_patches(production_asset) if production_asset else []
        self._cache: dict[tuple[float, float], tuple[float, tuple[float, float, float]]] = {}

    @classmethod
    def _load_transition_patches(cls, path: Path) -> list[dict[str, Any]]:
        """Read only the query patches from Unity's deterministic text asset.

        Terrain Defect Repair V1 added the rendered/query transition geometry to the
        ProductionTerrainSurfaceAsset without rewriting the retained source-grid JSON.
        Consuming this compact section keeps offline road endpoints numerically aligned
        with the same production surface used by traffic and authored embankments.
        """
        if not path.is_file():
            raise FileNotFoundError(f"Production terrain surface asset is missing: {path}")
        patches: list[dict[str, Any]] = []
        current: dict[str, Any] | None = None
        section = ""
        in_patches = False
        with path.open("r", encoding="utf-8") as stream:
            for raw_line in stream:
                line = raw_line.rstrip("\r\n")
                if line == "  transitionPatches:":
                    in_patches = True
                    continue
                if not in_patches:
                    continue
                if line == "  regions:":
                    break
                if line.startswith("  - stableId: "):
                    if current is not None:
                        patches.append(current)
                    current = {
                        "stable_id": line.split(": ", 1)[1],
                        "vertices": [],
                        "normals": [],
                        "triangles": (),
                    }
                    section = ""
                    continue
                if current is None:
                    continue
                stripped = line.strip()
                if stripped.startswith("priority: "):
                    current["priority"] = int(stripped.split(": ", 1)[1])
                elif stripped.startswith("innerHalfExtentMeters: "):
                    current["inner"] = float(stripped.split(": ", 1)[1])
                elif stripped.startswith("outerHalfExtentMeters: "):
                    current["outer"] = float(stripped.split(": ", 1)[1])
                elif stripped == "vertices:":
                    section = "vertices"
                elif stripped == "normals:":
                    section = "normals"
                elif stripped.startswith("triangles: "):
                    encoded = stripped.split(": ", 1)[1]
                    payload = bytes.fromhex(encoded)
                    current["triangles"] = struct.unpack("<" + "I" * (len(payload) // 4), payload)
                    section = ""
                elif stripped.startswith("- {x:") and section in ("vertices", "normals"):
                    match = cls._VECTOR.search(stripped)
                    if not match:
                        raise RuntimeError(f"Malformed terrain patch vector in {path}: {stripped}")
                    current[section].append(tuple(float(match.group(i)) for i in range(1, 4)))
        if current is not None:
            patches.append(current)
        for patch in patches:
            if (
                "priority" not in patch
                or "inner" not in patch
                or "outer" not in patch
                or not patch["vertices"]
                or len(patch["vertices"]) != len(patch["normals"])
                or len(patch["triangles"]) % 3 != 0
            ):
                raise RuntimeError(f"Incomplete terrain transition patch in {path}: {patch['stable_id']}")
        return patches

    @staticmethod
    def _normalized(value: tuple[float, float, float]) -> tuple[float, float, float]:
        length = math.sqrt(sum(component * component for component in value))
        if length < 0.001:
            return 0.0, 1.0, 0.0
        return tuple(component / length for component in value)

    def _sample_grid(
        self, layer: dict[str, Any], values: tuple[int, ...], x: float, z: float
    ) -> tuple[float, tuple[float, float, float]]:
        minimum = layer["minimum_enu_meters"]
        spacing = float(layer["spacing_meters"])
        fx = (x - float(minimum[0])) / spacing
        fz = (z - float(minimum[1])) / spacing
        width = int(layer["width"])
        height = int(layer["height"])
        x0 = min(width - 2, max(0, math.floor(fx)))
        z0 = min(height - 2, max(0, math.floor(fz)))
        tx = min(1.0, max(0.0, fx - x0))
        tz = min(1.0, max(0.0, fz - z0))
        scale = float(layer["height_quantization_meters"])
        h00 = values[z0 * width + x0] * scale
        h10 = values[z0 * width + x0 + 1] * scale
        h01 = values[(z0 + 1) * width + x0] * scale
        h11 = values[(z0 + 1) * width + x0 + 1] * scale
        sampled_height = (
            (h00 * (1.0 - tx) + h10 * tx) * (1.0 - tz)
            + (h01 * (1.0 - tx) + h11 * tx) * tz
        )

        def grid_normal(gx: int, gz: int) -> tuple[float, float, float]:
            left = max(0, gx - 1)
            right = min(width - 1, gx + 1)
            down = max(0, gz - 1)
            up = min(height - 1, gz + 1)
            dx = (values[gz * width + right] - values[gz * width + left]) * scale / max(
                (right - left) * spacing, 0.001
            )
            dz = (values[up * width + gx] - values[down * width + gx]) * scale / max(
                (up - down) * spacing, 0.001
            )
            return self._normalized((-dx, 1.0, -dz))

        normals = (
            grid_normal(x0, z0),
            grid_normal(x0 + 1, z0),
            grid_normal(x0, z0 + 1),
            grid_normal(x0 + 1, z0 + 1),
        )
        blended = tuple(
            ((normals[0][axis] * (1.0 - tx) + normals[1][axis] * tx) * (1.0 - tz)
             + (normals[2][axis] * (1.0 - tx) + normals[3][axis] * tx) * tz)
            for axis in range(3)
        )
        return sampled_height, self._normalized(blended)

    def _sample_patch(
        self, patch: dict[str, Any], x: float, z: float
    ) -> tuple[float, tuple[float, float, float]] | None:
        radius = max(abs(x), abs(z))
        if radius < patch["inner"] - 0.0001 or radius > patch["outer"] + 0.0001:
            return None
        vertices = patch["vertices"]
        normals = patch["normals"]
        best: tuple[float, tuple[float, float, float]] | None = None
        epsilon = 0.001
        triangles = patch["triangles"]
        for offset in range(0, len(triangles), 3):
            ia, ib, ic = triangles[offset:offset + 3]
            a, b, c = vertices[ia], vertices[ib], vertices[ic]
            denominator = (b[2] - c[2]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[2] - c[2])
            if abs(denominator) < 1e-8:
                continue
            wa = ((b[2] - c[2]) * (x - c[0]) + (c[0] - b[0]) * (z - c[2])) / denominator
            wb = ((c[2] - a[2]) * (x - c[0]) + (a[0] - c[0]) * (z - c[2])) / denominator
            wc = 1.0 - wa - wb
            if wa < -epsilon or wb < -epsilon or wc < -epsilon:
                continue
            height = wa * a[1] + wb * b[1] + wc * c[1]
            if best is not None and height <= best[0]:
                continue
            normal = self._normalized(tuple(
                wa * normals[ia][axis] + wb * normals[ib][axis] + wc * normals[ic][axis]
                for axis in range(3)
            ))
            best = height, normal
        return best

    def _sample_with_normal(self, x: float, z: float) -> tuple[float, tuple[float, float, float]]:
        key = x, z
        if key in self._cache:
            return self._cache[key]
        selected_priority = math.inf
        selected: tuple[float, tuple[float, float, float]] | None = None
        for priority, layer, values in self.layers:
            minimum = layer["minimum_enu_meters"]
            maximum = layer["maximum_enu_meters"]
            if priority >= selected_priority or not (
                minimum[0] <= x <= maximum[0] and minimum[1] <= z <= maximum[1]
            ):
                continue
            selected_priority = priority
            selected = self._sample_grid(layer, values, x, z)
        for patch in self.patches:
            if patch["priority"] >= selected_priority:
                continue
            sample = self._sample_patch(patch, x, z)
            if sample is None:
                continue
            selected_priority = patch["priority"]
            selected = sample
        if selected is None:
            raise ValueError(f"Terrain does not contain ({x:.3f}, {z:.3f})")
        self._cache[key] = selected
        return selected

    def sample(self, x: float, z: float) -> float:
        return self._sample_with_normal(x, z)[0]

    def normal(self, x: float, z: float) -> tuple[float, float, float]:
        return self._sample_with_normal(x, z)[1]


def parse_clearance(value: Any) -> float | None:
    if value is None:
        return None
    match = re.fullmatch(r"\s*(\d+)\s*'\s*(\d+)\s*\"\s*", str(value))
    if not match:
        return None
    return (int(match.group(1)) * 12 + int(match.group(2))) * 0.0254


def merged_cdot(cdot: dict[str, Any]) -> dict[str, dict[str, Any]]:
    grouped: dict[str, list[dict[str, Any]]] = {}
    for feature in cdot["features"]:
        attributes = feature["attributes"]
        grouped.setdefault(str(attributes["STRID"]), []).append(attributes)
    output: dict[str, dict[str, Any]] = {}
    for stable_id, records in grouped.items():
        clearances = [
            value
            for record in records
            for value in (
                parse_clearance(record.get("CLRMINNE")),
                parse_clearance(record.get("CLRMINSW")),
            )
            if value is not None
        ]
        primary = next((record for record in records if record.get("PrimaryRoutelog") == 1), records[0])
        output[stable_id] = {
            "records": records,
            "primary": primary,
            "minimum_clearance_meters": min(clearances) if clearances else None,
            "clearance_values_meters": sorted(set(round(value, 4) for value in clearances)),
        }
    return output


def resample(line: LineString, spacing: float) -> list[tuple[float, float]]:
    distances = [index * spacing for index in range(int(line.length // spacing) + 1)]
    if not distances or line.length - distances[-1] > 0.01:
        distances.append(line.length)
    elif abs(distances[-1] - line.length) > 0.001:
        distances[-1] = line.length
    return [tuple(line.interpolate(distance).coords[0]) for distance in distances]


def route_identity_matches(a: dict[str, Any], b: dict[str, Any]) -> bool:
    for key in ("ref", "name"):
        if a.get(key) and b.get(key) and a[key] == b[key]:
            return True
    return False


def extend_bridge_line(
    osm_id: int,
    osm_elements: dict[int, dict[str, Any]],
    osm_lines: dict[int, LineString],
) -> tuple[LineString, float, float]:
    original = osm_lines[osm_id]
    coordinates = list(original.coords)
    tags = osm_elements[osm_id].get("tags", {})
    used = {osm_id}

    def extend(start: bool) -> None:
        added = 0.0
        while added < MAXIMUM_APPROACH_EXTENSION_METERS:
            endpoint = Point(coordinates[0] if start else coordinates[-1])
            inside = Point(coordinates[1] if start else coordinates[-2])
            desired = (endpoint.x - inside.x, endpoint.y - inside.y)
            desired_length = max(0.001, math.hypot(*desired))
            best: tuple[float, int, list[tuple[float, float]]] | None = None
            for candidate_id, candidate in osm_lines.items():
                if candidate_id in used:
                    continue
                candidate_tags = osm_elements[candidate_id].get("tags", {})
                if candidate_tags.get("highway") is None or not route_identity_matches(tags, candidate_tags):
                    continue
                candidate_coordinates = list(candidate.coords)
                for reverse in (False, True):
                    oriented = list(reversed(candidate_coordinates)) if reverse else candidate_coordinates
                    if Point(oriented[0]).distance(endpoint) > 2.0 or len(oriented) < 2:
                        continue
                    outgoing = (oriented[1][0] - oriented[0][0], oriented[1][1] - oriented[0][1])
                    outgoing_length = max(0.001, math.hypot(*outgoing))
                    cosine = (desired[0] * outgoing[0] + desired[1] * outgoing[1]) / (
                        desired_length * outgoing_length
                    )
                    if cosine < math.cos(math.radians(60.0)):
                        continue
                    score = cosine - Point(oriented[0]).distance(endpoint) * 0.01
                    if best is None or score > best[0]:
                        best = score, candidate_id, oriented
            if best is None:
                break
            _, candidate_id, oriented = best
            used.add(candidate_id)
            before = LineString(coordinates).length
            if start:
                coordinates[:0] = list(reversed(oriented[1:]))
            else:
                coordinates.extend(oriented[1:])
            added += max(0.0, LineString(coordinates).length - before)

    extend(True)
    extend(False)
    extended = LineString(coordinates)
    original_start = extended.project(Point(original.coords[0]))
    original_end = extended.project(Point(original.coords[-1]))
    bridge_minimum = min(original_start, original_end)
    bridge_maximum = max(original_start, original_end)
    trim_start = max(0.0, bridge_minimum - MAXIMUM_APPROACH_EXTENSION_METERS)
    trim_end = min(extended.length, bridge_maximum + MAXIMUM_APPROACH_EXTENSION_METERS)
    trimmed = substring(extended, trim_start, trim_end)
    trimmed_coordinates = list(trimmed.coords)
    bridge_start = bridge_minimum - trim_start
    bridge_end = bridge_maximum - trim_start

    def safe_extrapolation(point: tuple[float, float], direction: tuple[float, float], requested: float) -> float:
        length = max(0.001, math.hypot(*direction))
        unit = (direction[0] / length, direction[1] / length)
        distance = requested
        while distance > 0.0:
            candidate = (point[0] + unit[0] * distance, point[1] + unit[1] * distance)
            if max(abs(candidate[0]), abs(candidate[1])) <= 11999.0:
                return distance
            distance -= 5.0
        return 0.0

    if bridge_start < MINIMUM_APPROACH_EXTENSION_METERS:
        point = trimmed_coordinates[0]
        inside = trimmed_coordinates[1]
        requested = MINIMUM_APPROACH_EXTENSION_METERS - bridge_start
        distance = safe_extrapolation(
            point, (point[0] - inside[0], point[1] - inside[1]), requested
        )
        if distance > 0.0:
            tangent = (point[0] - inside[0], point[1] - inside[1])
            length = math.hypot(*tangent)
            trimmed_coordinates.insert(
                0,
                (point[0] + tangent[0] / length * distance, point[1] + tangent[1] / length * distance),
            )
            bridge_start += distance
            bridge_end += distance
    remaining = LineString(trimmed_coordinates).length - bridge_end
    if remaining < MINIMUM_APPROACH_EXTENSION_METERS:
        point = trimmed_coordinates[-1]
        inside = trimmed_coordinates[-2]
        requested = MINIMUM_APPROACH_EXTENSION_METERS - remaining
        distance = safe_extrapolation(
            point, (point[0] - inside[0], point[1] - inside[1]), requested
        )
        if distance > 0.0:
            tangent = (point[0] - inside[0], point[1] - inside[1])
            length = math.hypot(*tangent)
            trimmed_coordinates.append(
                (point[0] + tangent[0] / length * distance, point[1] + tangent[1] / length * distance)
            )
    result = LineString(trimmed_coordinates)
    clipped = result.intersection(box(-11999.0, -11999.0, 11999.0, 11999.0))
    if clipped.geom_type == "MultiLineString":
        original_midpoint = original.interpolate(0.5, normalized=True)
        clipped = min(clipped.geoms, key=lambda child: child.distance(original_midpoint))
    if clipped.geom_type != "LineString" or clipped.length < original.length:
        raise RuntimeError(f"Could not retain in-world bridge line for OSM {osm_id}")
    original_midpoint = original.interpolate(0.5, normalized=True)
    if max(abs(original_midpoint.x), abs(original_midpoint.y)) < 6000.0:
        inner = clipped.intersection(box(-5999.0, -5999.0, 5999.0, 5999.0))
        if inner.geom_type == "MultiLineString":
            inner = min(inner.geoms, key=lambda child: child.distance(original_midpoint))
        if inner.geom_type == "LineString" and inner.length >= original.length:
            clipped = inner
    return (
        clipped,
        min(clipped.project(Point(original.coords[0])), clipped.project(Point(original.coords[-1]))),
        max(clipped.project(Point(original.coords[0])), clipped.project(Point(original.coords[-1]))),
    )


def context_widths(context: dict[str, Any]) -> dict[int, float]:
    widths: dict[int, float] = {}
    for feature in context["openstreetmap"]["features"]:
        if feature.get("category") != "road":
            continue
        osm_id = int(feature.get("source", {}).get("osm_id", -1))
        if osm_id >= 0:
            widths[osm_id] = float(feature.get("render_width_meters", 7.0))
    return widths


def corridor_lines(traffic: dict[str, Any]) -> dict[str, LineString]:
    output: dict[str, LineString] = {}
    for corridor in traffic["corridors"]:
        values = corridor["points_xz_meters"]
        output[corridor["corridor_id"]] = LineString(list(zip(values[::2], values[1::2])))
    return output


def local_bearing(line: LineString, point: Point) -> float:
    distance = line.project(point)
    delta = min(5.0, max(0.5, line.length * 0.1))
    a = line.interpolate(max(0.0, distance - delta))
    b = line.interpolate(min(line.length, distance + delta))
    return math.atan2(b.y - a.y, b.x - a.x)


def angle_difference(a: float, b: float) -> float:
    difference = abs(a - b) % math.pi
    return min(difference, math.pi - difference)


def resolve_route_ownership(
    upper_ids: list[str],
    lower_ids: list[str],
    upper_line: LineString,
    lower_lines: list[LineString],
    crossing: Point,
    corridors: dict[str, LineString],
) -> tuple[list[str], list[str]]:
    upper = set(upper_ids)
    lower = set(lower_ids)
    for route_id in upper & lower:
        route = corridors[route_id]
        route_bearing = local_bearing(route, crossing)
        upper_score = angle_difference(route_bearing, local_bearing(upper_line, crossing))
        lower_score = min(
            angle_difference(route_bearing, local_bearing(line, crossing)) for line in lower_lines
        )
        if upper_score + math.radians(2.0) < lower_score:
            lower.remove(route_id)
        else:
            upper.remove(route_id)
    return sorted(upper), sorted(lower)


def aligned_corridors(
    reference: LineString,
    corridors: dict[str, LineString],
    maximum_distance: float = 12.0,
    maximum_angle_degrees: float = 15.0,
) -> list[str]:
    matches: list[str] = []
    for route_id, corridor in corridors.items():
        if corridor.distance(reference) > maximum_distance:
            continue
        point = reference.interpolate(reference.project(corridor.centroid))
        if angle_difference(local_bearing(corridor, point), local_bearing(reference, point)) > math.radians(
            maximum_angle_degrees
        ):
            continue
        matches.append(route_id)
    return sorted(matches)


def profile_normals(
    points: list[tuple[float, float]],
    heights: list[float],
    terrain: TerrainSampler,
) -> list[tuple[float, float, float]]:
    normals: list[tuple[float, float, float]] = []
    for index, point in enumerate(points):
        if index == 0 or index == len(points) - 1:
            normals.append(terrain.normal(*point))
            continue
        previous = points[index - 1]
        following = points[index + 1]
        tangent = (
            following[0] - previous[0],
            heights[index + 1] - heights[index - 1],
            following[1] - previous[1],
        )
        horizontal = math.hypot(tangent[0], tangent[2])
        if horizontal < 0.001:
            normals.append((0.0, 1.0, 0.0))
            continue
        right = (tangent[2] / horizontal, 0.0, -tangent[0] / horizontal)
        normal = (
            -tangent[1] * right[2],
            tangent[2] * right[0] - tangent[0] * right[2],
            tangent[1] * right[0],
        )
        length = math.sqrt(sum(value * value for value in normal))
        normal = tuple(value / length for value in normal)
        if normal[1] < 0.0:
            normal = tuple(-value for value in normal)
        normals.append(normal)
    return normals


def reconcile_approach_overlaps(profiles: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Join coincident approach endpoints without moving terrain, bridge crowns or source XZ.

    A 4% upward envelope joins an existing terrain-matched endpoint to the continuing
    approach. Preserve every slope break in mesh authoring; the existing 6% gate remains.
    """
    import bisect
    originals = []
    for profile in profiles:
        xyz = list(zip(*[iter(profile["samples_xyz_meters"])] * 3))
        stations = [0.0]
        for a, b in zip(xyz, xyz[1:]):
            stations.append(stations[-1] + math.hypot(b[0] - a[0], b[2] - a[2]))
        originals.append((profile, xyz, stations, LineString([(x, z) for x, _, z in xyz])))
    report = []
    for target, xyz, stations, line in originals:
        def sample(distance):
            index = min(len(xyz) - 2, max(0, bisect.bisect_right(stations, distance) - 1))
            t = (distance - stations[index]) / (stations[index + 1] - stations[index])
            return tuple(a + (b - a) * t for a, b in zip(xyz[index], xyz[index + 1]))
        anchors = []
        for source, source_xyz, _, _ in originals:
            if source is target or not set(source["upper_route_ids"]).intersection(target["upper_route_ids"]):
                continue
            for endpoint in (source_xyz[0], source_xyz[-1]):
                point = Point(endpoint[0], endpoint[2])
                distance = line.project(point)
                if line.distance(point) > 0.05 or not 1.0 < distance < line.length - 1.0:
                    continue
                old = sample(distance)[1]
                if endpoint[1] <= old + 0.01:
                    continue
                if target["bridge_interval_meters"][0] <= distance <= target["bridge_interval_meters"][1]:
                    raise RuntimeError("Overlap repair may not alter a bridge crown: " + target["span_id"])
                anchors.append((distance, endpoint[1]))
                report.append(dict(source=source["span_id"], target=target["span_id"],
                                   station_meters=distance, raise_meters=endpoint[1] - old))
        if not anchors:
            continue
        distances = sorted(set(stations + [d for d, _ in anchors]))
        points, changed = [], set()
        for index, distance in enumerate(distances):
            x, old_y, z = sample(distance)
            y = max(old_y, *(height - 0.04 * abs(distance - anchor) for anchor, height in anchors))
            if y > old_y + 0.00001:
                changed.update(range(max(0, index - 1), min(len(distances), index + 2)))
            if index in (0, len(distances) - 1) and abs(y - old_y) > 0.00001:
                raise RuntimeError("Overlap repair would move a terrain-matched endpoint")
            if target["bridge_interval_meters"][0] <= distance <= target["bridge_interval_meters"][1] and y > old_y + 0.00001:
                raise RuntimeError("Overlap repair would raise a bridge crown")
            points.append(tuple(round(v, 4) for v in (x, y, z)))
        normals = [tuple(target["sample_normals_xyz"][:3])]
        for before, after in zip(points, points[2:]):
            dx, dy, dz = (b - a for a, b in zip(before, after))
            n = (-dy * dx, dx * dx + dz * dz, -dy * dz)
            length = math.sqrt(sum(v * v for v in n))
            normals.append(tuple(round(v / length, 6) for v in n))
        normals.append(tuple(target["sample_normals_xyz"][-3:]))
        maximum_grade = max(abs(b[1] - a[1]) / math.hypot(b[0] - a[0], b[2] - a[2])
                            for a, b in zip(points, points[1:]))
        if maximum_grade > MAXIMUM_ACCEPTED_GRADE:
            raise RuntimeError("Overlap repair exceeds authored grade limit")
        target["samples_xyz_meters"] = [v for point in points for v in point]
        target["sample_normals_xyz"] = [v for normal in normals for v in normal]
        target["maximum_grade"] = round(maximum_grade, 6)
        critical = set()
        for i in sorted(changed):
            if i == 0 or i == len(points) - 1:
                critical.add(i)
                continue
            before = (points[i][1] - points[i - 1][1]) / (distances[i] - distances[i - 1])
            after = (points[i + 1][1] - points[i][1]) / (distances[i + 1] - distances[i])
            if abs(after - before) > 0.0002:
                critical.add(i)
        critical.update(distances.index(d) for d, _ in anchors)
        target["approach_join_distances_meters"] = [round(distances[i], 4) for i in sorted(critical)]
    return report


def main() -> int:
    args = parse_args()
    inventory = load_json(args.inventory)
    osm = load_json(args.osm)
    cdot = merged_cdot(load_json(args.cdot))
    terrain = TerrainSampler(load_json(args.terrain), args.terrain_asset)
    widths = context_widths(load_json(args.context))
    traffic_document = load_json(args.traffic)
    corridors = corridor_lines(traffic_document)
    osm_elements = {int(element["id"]): element for element in osm["elements"] if element.get("type") == "way"}
    osm_lines = {osm_id: osm_line(element) for osm_id, element in osm_elements.items() if element.get("geometry")}

    # CDOT E-16-HW is visible in the 2020 imagery, but its SH-157 ramp segment
    # is absent from the frozen raw OSM extract even though the compiled traffic
    # manifest retains the OSM-derived centerline.  Promote only that proven
    # segment and its contiguous approach pieces; US-36 remains the lower road.
    e16_hw_ids = (41840963, 41840964, 17014937, 41840966, 41840965)
    e16_hw_tags = {
        "highway": "motorway_link", "name": "SH 157 Ramp", "ref": "CO 157",
        "lanes": "2", "oneway": "yes",
    }
    for osm_id in e16_hw_ids:
        route_id = f"canonical:osm_bounded_roads:way/{osm_id}:0:0:0"
        if route_id not in corridors:
            raise RuntimeError(f"E-16-HW retained traffic centerline is missing: {route_id}")
        osm_lines[osm_id] = corridors[route_id]
        osm_elements[osm_id] = {"id": osm_id, "type": "way", "tags": dict(e16_hw_tags)}
    osm_elements[41840966]["tags"].update({"bridge": "yes", "layer": "1"})
    if not any(group["structure_id"] == "E-16-HW" for group in inventory["accepted_structures"]):
        lower_ids = (130922338, 318130765)
        upper_line = osm_lines[41840966]
        crossing_values = (
            (130922338, -535.0678, -5986.9203),
            (318130765, -533.5760, -5972.8050),
        )
        inventory["accepted_structures"].append({
            "span_id": "KBDU-GSR-E-16-HW",
            "structure_id": "E-16-HW",
            "upper_osm_way_ids": [41840966],
            "upper_route_ids": aligned_corridors(upper_line, corridors),
            "lower_osm_way_ids": list(lower_ids),
            "lower_route_ids": sorted({
                route_id for lower_id in lower_ids
                for route_id in aligned_corridors(osm_lines[lower_id], corridors)
            }),
            "members": [{
                "osm_way_id": 41840966,
                "crossings": [
                    {"lower_osm_id": lower_id, "crossing_xz_meters": [x, z]}
                    for lower_id, x, z in crossing_values
                ],
            }],
        })
        inventory["accepted_structures"].sort(key=lambda group: group["structure_id"])

    profiles: list[dict[str, Any]] = []
    diagnostics: list[dict[str, Any]] = []
    all_upper_routes: set[str] = set()
    all_lower_routes: set[str] = set()
    for group in inventory["accepted_structures"]:
        structure_id = group["structure_id"]
        source = cdot[structure_id]
        clearance = source["minimum_clearance_meters"]
        clearance_authority = "CDOT_STRUCTURE_INVENTORY_MINIMUM_CLEARANCE"
        if clearance is None:
            clearance = FALLBACK_CLEARANCE_METERS
            clearance_authority = "CDOT_STRUCTURE_ID_PLUS_CONSERVATIVE_17FT_CLEARANCE"

        group_crossings = [crossing for member in group["members"] for crossing in member["crossings"]]
        crossing_points = [Point(*crossing["crossing_xz_meters"]) for crossing in group_crossings]
        lower_lines = [osm_lines[crossing["lower_osm_id"]] for crossing in group_crossings]
        required_top = max(
            terrain.sample(point.x, point.y) + clearance + DECK_THICKNESS_METERS + CLEARANCE_SAFETY_METERS
            for point in crossing_points
        )
        extended_members: dict[int, tuple[LineString, float, float]] = {}
        group_anchor_heights: list[float] = []
        for member in group["members"]:
            member_osm_id = int(member["osm_way_id"])
            if member_osm_id == 41840966:
                ordered_coordinates: list[tuple[float, float]] = []
                for approach_id in e16_hw_ids:
                    coordinates = list(osm_lines[approach_id].coords)
                    if ordered_coordinates:
                        if Point(coordinates[-1]).distance(Point(ordered_coordinates[-1])) < Point(
                            coordinates[0]
                        ).distance(Point(ordered_coordinates[-1])):
                            coordinates.reverse()
                        ordered_coordinates.extend(coordinates[1:])
                    else:
                        ordered_coordinates.extend(coordinates)
                complete = LineString(ordered_coordinates)
                original = osm_lines[member_osm_id]
                bridge_start = complete.project(Point(original.coords[0]))
                bridge_end = complete.project(Point(original.coords[-1]))
                trim_start = max(0.0, bridge_start - MAXIMUM_APPROACH_EXTENSION_METERS)
                trim_end = min(complete.length, bridge_end + MAXIMUM_APPROACH_EXTENSION_METERS)
                line = substring(complete, trim_start, trim_end)
                extended = (line, bridge_start - trim_start, bridge_end - trim_start)
            else:
                extended = extend_bridge_line(member_osm_id, osm_elements, osm_lines)
            extended_members[member_osm_id] = extended
            member_line = extended[0]
            member_coordinates = list(member_line.coords)
            group_anchor_heights.extend((
                terrain.sample(*member_coordinates[0]),
                terrain.sample(*member_coordinates[-1]),
            ))
        # A CDOT structure is one physical crown even when OSM represents its
        # parallel carriageways as separate, longitudinally offset ways.
        group_crown_height = max([required_top, *group_anchor_heights])
        for member in group["members"]:
            osm_id = int(member["osm_way_id"])
            bridge_line = osm_lines[osm_id]
            line, bridge_start_distance, bridge_end_distance = extended_members[osm_id]
            center_crossing = min(crossing_points, key=bridge_line.distance)
            profile_upper_routes = sorted(set(group["upper_route_ids"]) | set(aligned_corridors(line, corridors)))
            upper_routes, lower_routes = resolve_route_ownership(
                profile_upper_routes,
                group["lower_route_ids"],
                bridge_line,
                lower_lines,
                center_crossing,
                corridors,
            )
            all_upper_routes.update(upper_routes)
            all_lower_routes.update(lower_routes)

            points = resample(line, SAMPLE_SPACING_METERS)
            distances = [line.project(Point(point)) for point in points]
            # The complete source bridge interval is one physical deck.  Holding it at
            # the shared structure crown makes parallel carriageways numerically
            # coincident in Y and confines the grade transition to the authored
            # approaches outside the OSM bridge geometry.  A shorter crossing-only
            # plateau caused adjacent carriageways to disagree by centimetres when
            # their source bridge endpoints were longitudinally offset.
            plateau_start = bridge_start_distance
            plateau_end = bridge_end_distance
            start_height = terrain.sample(*points[0])
            end_height = terrain.sample(*points[-1])
            crown_height = group_crown_height
            heights: list[float] = []
            for distance in distances:
                if distance <= plateau_start:
                    ratio = distance / max(plateau_start, 0.001)
                    height = start_height + (crown_height - start_height) * ratio
                elif distance >= plateau_end:
                    ratio = (distance - plateau_end) / max(line.length - plateau_end, 0.001)
                    height = crown_height + (end_height - crown_height) * ratio
                else:
                    height = crown_height
                heights.append(height)
            heights[0] = start_height
            heights[-1] = end_height
            normals = profile_normals(points, heights, terrain)
            maximum_grade = max(
                abs(heights[index + 1] - heights[index])
                / max(0.001, math.dist(points[index], points[index + 1]))
                for index in range(len(points) - 1)
            )
            if maximum_grade > MAXIMUM_ACCEPTED_GRADE:
                raise RuntimeError(
                    f"{structure_id}/{osm_id} requires grade {maximum_grade:.4f}, "
                    f"above {MAXIMUM_ACCEPTED_GRADE:.4f}"
                )
            flattened_points = [round(value, 4) for xyz in zip(
                (point[0] for point in points), heights, (point[1] for point in points)
            ) for value in xyz]
            flattened_normals = [round(value, 6) for normal in normals for value in normal]
            tags = osm_elements[osm_id].get("tags", {})
            width = float(widths.get(osm_id, max(7.0, float(tags.get("lanes", 2)) * 3.5)))
            profile = {
                "span_id": f"KBDU-GSR-{structure_id}-OSM-{osm_id}",
                "source_osm_way_id": str(osm_id),
                "source_cdot_structure_id": structure_id,
                "source_imagery_year": 2020,
                "source_year_built": int(source["primary"]["YEARBUILT"]),
                "source_facility": source["primary"]["FACILITY"],
                "source_feature_intersected": source["primary"]["FEATINT"],
                "source_bridge": str(tags.get("bridge", "")),
                "source_layer": str(tags.get("layer", "")),
                "source_tunnel": str(tags.get("tunnel", "")),
                "source_covered": str(tags.get("covered", "")),
                "source_embankment": str(tags.get("embankment", "")),
                "source_cutting": str(tags.get("cutting", "")),
                "source_bridge_name": str(tags.get("bridge:name", "")),
                "source_bridge_structure": str(tags.get("bridge:structure", "")),
                "source_lanes": str(tags.get("lanes", "")),
                "source_width": str(tags.get("width", "")),
                "vertical_authority": clearance_authority,
                "minimum_clearance_meters": round(clearance, 4),
                "clearance_evidence_values_meters": source["clearance_values_meters"],
                "deck_thickness_meters": DECK_THICKNESS_METERS,
                "surface_width_meters": round(width, 3),
                "profile_length_meters": round(line.length, 4),
                "bridge_interval_meters": [
                    round(bridge_start_distance, 4),
                    round(bridge_end_distance, 4),
                ],
                "maximum_grade": round(maximum_grade, 6),
                "minimum_xz_distance_meters": round(width * 0.5 + 3.0, 3),
                "upper_route_ids": upper_routes,
                "lower_route_ids": lower_routes,
                "samples_xyz_meters": flattened_points,
                "sample_normals_xyz": flattened_normals,
                "suppressed_old_visual_interval_meters": [
                    round(bridge_start_distance, 4),
                    round(bridge_end_distance, 4),
                ],
                "old_visual_suppression": "OPAQUE_DECK_OCCLUSION_OF_ORTHOPHOTO_DRAPE",
                "validation_height_tolerance_meters": 0.01,
            }
            profiles.append(profile)
            underside = crown_height - DECK_THICKNESS_METERS
            lower_maximum = max(terrain.sample(point.x, point.y) for point in crossing_points)
            diagnostics.append(
                {
                    "span_id": profile["span_id"],
                    "endpoint_heights_meters": [round(start_height, 4), round(end_height, 4)],
                    "crown_height_meters": round(crown_height, 4),
                    "minimum_modeled_clearance_meters": round(underside - lower_maximum, 4),
                    "maximum_grade": round(maximum_grade, 6),
                    "sample_count": len(points),
                }
            )

    overlap_joins = reconcile_approach_overlaps(profiles)
    by_span = {profile["span_id"]: profile for profile in profiles}
    for entry in diagnostics:
        profile = by_span[entry["span_id"]]
        entry["maximum_grade"] = profile["maximum_grade"]
        entry["sample_count"] = len(profile["samples_xyz_meters"]) // 3
    authority = {
        "schema": "questflight.grade-separated-road-authority.v1",
        "status": "FULL_MAP_GRADE_SEPARATED_ROAD_AUTHORITY_V1",
        "coordinate_frame": "KBDU local tangent ENU; Unity +X=east, +Y=up, +Z=north",
        "source_imagery_year": 2020,
        "source_osm_snapshot_date": "2026-07-10",
        "source_cdot_item_id": "4f9f1c42e2d74d71b025ffb7aa538c1c",
        "source_cdot_layer_url": "https://dtdapps.codot.gov/server/rest/services/Webapps/open_data_sde/FeatureServer/31",
        "source_cdot_license": "CC BY-SA 4.0",
        "source_osm_license": "ODbL 1.0",
        "sample_spacing_meters": SAMPLE_SPACING_METERS,
        "vehicle_contact_offsets_meters": CONTACT_OFFSETS_METERS,
        "vehicle_contact_half_lengths_meters": CONTACT_HALF_LENGTHS_METERS,
        "vehicle_contact_authority": (
            "CURRENT_NEAR_ARCHETYPE_MESH_LOCAL_MINIMUM_Y_EQUALS_ZERO_AND_LOCAL_Z_EXTENTS"
        ),
        "approach_overlap_reconciliation": overlap_joins,
        "profiles": sorted(profiles, key=lambda value: value["span_id"]),
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(authority, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    diagnostic = {
        "schema": "questflight.grade-separated-road-authority-build-report.v1",
        "profile_count": len(profiles),
        "unique_structure_count": len({profile["source_cdot_structure_id"] for profile in profiles}),
        "unique_upper_route_count": len(all_upper_routes),
        "unique_lower_route_count": len(all_lower_routes),
        "maximum_grade": max(item["maximum_grade"] for item in diagnostics),
        "minimum_modeled_clearance_meters": min(
            item["minimum_modeled_clearance_meters"] for item in diagnostics
        ),
        "profiles": diagnostics,
    }
    if args.diagnostic_output:
        args.diagnostic_output.parent.mkdir(parents=True, exist_ok=True)
        args.diagnostic_output.write_text(
            json.dumps(diagnostic, indent=2, sort_keys=True) + "\n", encoding="utf-8"
        )
    print(json.dumps({key: value for key, value in diagnostic.items() if key != "profiles"}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
