#!/usr/bin/env python3
"""Generate the deterministic Aerial Scenery V1 building spatial contract.

The output deliberately contains footprint parts, never grid-cell rectangles,
convex hulls, or runtime repair instructions.  Lidar-derived heights are used
only when the supplied derivative explicitly passes the configured relative
vertical-error gate and carries per-building identity records.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Iterable


def load_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def canonical_bytes(value: dict[str, Any]) -> bytes:
    return (json.dumps(value, separators=(",", ":"), sort_keys=True) + "\n").encode("utf-8")


def decode_points_q(feature: dict[str, Any]) -> list[tuple[int, int]]:
    values = feature.get("points_q") or []
    points = [(int(values[index]), int(values[index + 1])) for index in range(0, len(values) - 1, 2)]
    if len(points) > 1 and points[0] == points[-1]:
        points.pop()
    if len(points) < 3 or signed_area2(points) == 0:
        raise ValueError("Building footprint is degenerate")
    return points


def flatten_points(points: Iterable[tuple[int, int]]) -> list[int]:
    return [coordinate for point in points for coordinate in point]


def signed_area2(points: list[tuple[int, int]]) -> int:
    return sum(
        points[index][0] * points[(index + 1) % len(points)][1]
        - points[(index + 1) % len(points)][0] * points[index][1]
        for index in range(len(points))
    )


def bounds_q(points: list[tuple[int, int]]) -> tuple[int, int, int, int]:
    return (
        min(point[0] for point in points),
        min(point[1] for point in points),
        max(point[0] for point in points),
        max(point[1] for point in points),
    )


def polygon_centroid_q(points: list[tuple[int, int]]) -> tuple[float, float]:
    area2 = signed_area2(points)
    numerator_x = 0
    numerator_z = 0
    for index, point in enumerate(points):
        following = points[(index + 1) % len(points)]
        cross = point[0] * following[1] - following[0] * point[1]
        numerator_x += (point[0] + following[0]) * cross
        numerator_z += (point[1] + following[1]) * cross
    return numerator_x / (3.0 * area2), numerator_z / (3.0 * area2)


def orientation(a: tuple[int, int], b: tuple[int, int], c: tuple[int, int]) -> int:
    return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])


def point_on_segment(a: tuple[int, int], b: tuple[int, int], point: tuple[int, int]) -> bool:
    return (
        orientation(a, b, point) == 0
        and min(a[0], b[0]) <= point[0] <= max(a[0], b[0])
        and min(a[1], b[1]) <= point[1] <= max(a[1], b[1])
    )


def segments_intersect(
    a: tuple[int, int], b: tuple[int, int], c: tuple[int, int], d: tuple[int, int]
) -> bool:
    first = orientation(a, b, c)
    second = orientation(a, b, d)
    third = orientation(c, d, a)
    fourth = orientation(c, d, b)
    if ((first > 0 and second < 0) or (first < 0 and second > 0)) and (
        (third > 0 and fourth < 0) or (third < 0 and fourth > 0)
    ):
        return True
    return (
        (first == 0 and point_on_segment(a, b, c))
        or (second == 0 and point_on_segment(a, b, d))
        or (third == 0 and point_on_segment(c, d, a))
        or (fourth == 0 and point_on_segment(c, d, b))
    )


def point_in_polygon(point: tuple[int, int], polygon: list[tuple[int, int]]) -> bool:
    inside = False
    x, z = point
    for index, a in enumerate(polygon):
        b = polygon[(index + 1) % len(polygon)]
        if point_on_segment(a, b, point):
            return True
        if (a[1] > z) != (b[1] > z):
            intersection_x = (b[0] - a[0]) * (z - a[1]) / (b[1] - a[1]) + a[0]
            if x < intersection_x:
                inside = not inside
    return inside


def polygons_intersect(first: list[tuple[int, int]], second: list[tuple[int, int]]) -> bool:
    first_bounds = bounds_q(first)
    second_bounds = bounds_q(second)
    if (
        first_bounds[2] < second_bounds[0]
        or second_bounds[2] < first_bounds[0]
        or first_bounds[3] < second_bounds[1]
        or second_bounds[3] < first_bounds[1]
    ):
        return False
    for first_index, a in enumerate(first):
        b = first[(first_index + 1) % len(first)]
        for second_index, c in enumerate(second):
            d = second[(second_index + 1) % len(second)]
            if segments_intersect(a, b, c, d):
                return True
    return point_in_polygon(first[0], second) or point_in_polygon(second[0], first)


def polygon_is_simple(points: list[tuple[int, int]]) -> bool:
    if len(set(points)) != len(points):
        return False
    for first_index, a in enumerate(points):
        b = points[(first_index + 1) % len(points)]
        for second_index in range(first_index + 1, len(points)):
            if (
                second_index == first_index
                or (second_index + 1) % len(points) == first_index
                or (first_index + 1) % len(points) == second_index
            ):
                continue
            c = points[second_index]
            d = points[(second_index + 1) % len(points)]
            if segments_intersect(a, b, c, d):
                return False
    return True


def simplify_mid_footprint(
    source: list[tuple[int, int]], maximum_vertices: int, maximum_area_error_ratio: float
) -> list[tuple[int, int]]:
    """Remove low-area vertices without changing bounds, topology, or area by > gate."""
    if len(source) <= maximum_vertices:
        return list(source)
    original_area2 = abs(signed_area2(source))
    source_bounds = bounds_q(source)
    protected = {
        index
        for index, point in enumerate(source)
        if point[0] in (source_bounds[0], source_bounds[2])
        or point[1] in (source_bounds[1], source_bounds[3])
    }
    points = list(source)
    source_indices = list(range(len(source)))
    minimum_ratio = 1.0 - maximum_area_error_ratio
    maximum_ratio = 1.0 + maximum_area_error_ratio
    while len(points) > maximum_vertices:
        candidates = sorted(
            (
                abs(orientation(points[index - 1], points[index], points[(index + 1) % len(points)])),
                source_indices[index],
                index,
            )
            for index in range(len(points))
            if source_indices[index] not in protected
        )
        removed = False
        for _, _, index in candidates:
            candidate = points[:index] + points[index + 1 :]
            ratio = abs(signed_area2(candidate)) / original_area2
            if not (minimum_ratio <= ratio <= maximum_ratio):
                continue
            if bounds_q(candidate) != source_bounds or not polygon_is_simple(candidate):
                continue
            points = candidate
            source_indices.pop(index)
            removed = True
            break
        if not removed:
            break
    return points


def stable_id(feature: dict[str, Any]) -> str:
    source = feature.get("source") or {}
    part = re.sub(r"[^a-zA-Z0-9_-]+", "-", str(source.get("part", "geometry-0"))).strip("-")
    return f"osm-{source.get('osm_type', 'unknown')}-{int(source.get('osm_id', -1))}-{part}"


def fallback_height_source(feature: dict[str, Any]) -> str:
    tags = feature.get("tags") or {}
    if tags.get("height") not in (None, ""):
        return "context_osm_height_tag"
    if tags.get("building:levels") not in (None, ""):
        return "context_osm_levels_derivative"
    return "context_deterministic_osm_id_fallback"


def recursive_value(value: Any, key: str) -> Any:
    if isinstance(value, dict):
        if key in value:
            return value[key]
        for child in value.values():
            found = recursive_value(child, key)
            if found is not None:
                return found
    elif isinstance(value, list):
        for child in value:
            found = recursive_value(child, key)
            if found is not None:
                return found
    return None


def _confidence_number(value: Any) -> float | None:
    if isinstance(value, (int, float)) and math.isfinite(float(value)):
        result = float(value)
        return result / 255.0 if result > 1.0 and result <= 255.0 else result
    if isinstance(value, str):
        return {"high": 1.0, "medium": 0.6, "low": 0.2}.get(value.strip().lower())
    return None


def lidar_identity_records(document: dict[str, Any]) -> list[dict[str, Any]]:
    for key in ("building_heights", "height_records", "records"):
        value = recursive_value(document, key)
        if isinstance(value, list) and all(isinstance(item, dict) for item in value):
            return value
    return []


def qualified_lidar_heights(
    document: dict[str, Any] | None,
    minimum_confidence: float,
    maximum_relative_error: float,
) -> tuple[dict[str, dict[str, float]], dict[str, Any]]:
    if document is None:
        return {}, {
            "status": "NOT_PROVIDED_FALLBACK_ACTIVE",
            "schema_valid": False,
            "eligible_for_authoritative_override": False,
            "meets_10_percent_vertical_error_gate": False,
            "vertical_error_fraction_bound": None,
            "identity_record_count": 0,
            "qualified_identity_record_count": 0,
        }
    schema_valid = document.get("schema") == "questflight.aerial-scenery-v1.lidar.v1"
    authoritative = recursive_value(document, "eligible_for_authoritative_override") is True
    error_gate = recursive_value(document, "meets_10_percent_vertical_error_gate") is True
    gate = schema_valid and authoritative and error_gate
    global_error = recursive_value(document, "vertical_error_fraction_bound")
    if not isinstance(global_error, (int, float)) or not math.isfinite(float(global_error)):
        global_error = None
    records = lidar_identity_records(document)
    result: dict[str, dict[str, float]] = {}
    if gate and global_error is not None and float(global_error) <= maximum_relative_error:
        for item in records:
            identity = item.get("stable_id")
            height = item.get("height_meters", item.get("building_height_meters"))
            confidence = _confidence_number(item.get("confidence"))
            error = item.get("vertical_error_fraction", global_error)
            if (
                isinstance(identity, str)
                and isinstance(height, (int, float))
                and math.isfinite(float(height))
                and float(height) > 0.0
                and confidence is not None
                and confidence >= minimum_confidence
                and isinstance(error, (int, float))
                and math.isfinite(float(error))
                and float(error) <= maximum_relative_error
            ):
                result[identity] = {
                    "height_meters": float(height),
                    "confidence": confidence,
                    "vertical_error_fraction": float(error),
                }
    if not gate or global_error is None or float(global_error) > maximum_relative_error:
        status = "REJECTED_GLOBAL_VERTICAL_ERROR_GATE_FALLBACK_ACTIVE"
    elif not records:
        status = "QUALIFIED_RASTER_WITHOUT_BUILDING_IDENTITY_FALLBACK_ACTIVE"
    else:
        status = "QUALIFIED_IDENTITY_HEIGHTS_AVAILABLE"
    return result, {
        "status": status,
        "schema_valid": schema_valid,
        "eligible_for_authoritative_override": authoritative,
        "meets_10_percent_vertical_error_gate": error_gate,
        "vertical_error_fraction_bound": global_error,
        "identity_record_count": len(records),
        "qualified_identity_record_count": len(result),
    }


class UnionFind:
    def __init__(self, count: int) -> None:
        self.parent = list(range(count))

    def find(self, value: int) -> int:
        while self.parent[value] != value:
            self.parent[value] = self.parent[self.parent[value]]
            value = self.parent[value]
        return value

    def union(self, first: int, second: int) -> None:
        first_root = self.find(first)
        second_root = self.find(second)
        if first_root != second_root:
            self.parent[max(first_root, second_root)] = min(first_root, second_root)


def build_components(
    records: list[dict[str, Any]], quantization: float, index_cell_meters: float
) -> tuple[list[dict[str, Any]], int]:
    active = [record for record in records if record["geometry_points_q"]]
    polygons = [
        list(zip(record["geometry_points_q"][0::2], record["geometry_points_q"][1::2]))
        for record in active
    ]
    if not active:
        return [], 0
    bin_size_q = max(1, int(round(index_cell_meters / quantization)))
    bins: dict[tuple[str, int, int], list[int]] = defaultdict(list)
    for index, (record, polygon) in enumerate(zip(active, polygons)):
        minimum_x, minimum_z, maximum_x, maximum_z = bounds_q(polygon)
        for cell_x in range(minimum_x // bin_size_q, maximum_x // bin_size_q + 1):
            for cell_z in range(minimum_z // bin_size_q, maximum_z // bin_size_q + 1):
                bins[(record["tier"], cell_x, cell_z)].append(index)
    candidates: set[tuple[int, int]] = set()
    for values in bins.values():
        values = sorted(set(values))
        for first_index, first in enumerate(values):
            for second in values[first_index + 1 :]:
                candidates.add((first, second))
    union_find = UnionFind(len(active))
    actual_edges: list[tuple[int, int]] = []
    for first, second in sorted(candidates):
        if active[first]["tier"] != active[second]["tier"]:
            continue
        if polygons_intersect(polygons[first], polygons[second]):
            union_find.union(first, second)
            actual_edges.append((first, second))
    grouped: dict[int, list[int]] = defaultdict(list)
    for index in range(len(active)):
        grouped[union_find.find(index)].append(index)
    edges_by_root: dict[int, list[tuple[int, int]]] = defaultdict(list)
    for first, second in actual_edges:
        edges_by_root[union_find.find(first)].append((first, second))

    multi_part_components: list[dict[str, Any]] = []
    for root, member_indices in sorted(grouped.items(), key=lambda item: min(active[index]["feature_index"] for index in item[1])):
        member_indices.sort(key=lambda index: active[index]["feature_index"])
        local = UnionFind(len(member_indices))
        local_index = {active[index]["feature_index"]: position for position, index in enumerate(member_indices)}
        spanning_edges: list[int] = []
        for first, second in sorted(
            edges_by_root.get(root, []),
            key=lambda edge: (active[edge[0]]["feature_index"], active[edge[1]]["feature_index"]),
        ):
            first_feature = active[first]["feature_index"]
            second_feature = active[second]["feature_index"]
            first_local = local_index[first_feature]
            second_local = local_index[second_feature]
            if local.find(first_local) != local.find(second_local):
                local.union(first_local, second_local)
                spanning_edges.extend((first_feature, second_feature))
        feature_indices = [active[index]["feature_index"] for index in member_indices]
        if len(feature_indices) > 1:
            multi_part_components.append(
                {
                    "connectivity_edges": spanning_edges,
                    "feature_indices": feature_indices,
                }
            )
    return multi_part_components, len(grouped)


def _tier(radius: float, config: dict[str, Any], landmark: bool) -> tuple[str, str]:
    tiers = config["tier_radii_meters"]
    if radius < float(tiers["hero_core_max_exclusive"]):
        return "hero_core", "TRUE_SOURCE_FOOTPRINT"
    if radius < float(tiers["near_max_exclusive"]):
        return "near", "TRUE_SOURCE_FOOTPRINT"
    if radius < float(tiers["mid_max_exclusive"]):
        return "mid", "SIMPLIFIED_FOOTPRINT_PART"
    if landmark:
        return "far_landmark", "SIMPLIFIED_ALLOWLISTED_LANDMARK_FOOTPRINT"
    return "far_orthophoto", "ORTHOPHOTO_ONLY_NO_BUILDING_GEOMETRY"


def mid_geometry_is_useful(
    radius: float, source_area_square_meters: float, config: dict[str, Any]
) -> bool:
    """Keep only mid buildings whose projected size justifies a closed shell.

    Small structures beyond the unconditional radius are more faithful in the
    orthophoto than as roof-only cards.  This decision is deterministic and is
    made before batching, so it cannot produce a scale-changing runtime LOD.
    """
    policy = config["mid_geometry_selection"]
    return (
        radius < float(policy["unconditional_radius_meters"])
        or source_area_square_meters
        >= float(policy["minimum_footprint_area_square_meters_beyond_radius"])
    )


def roof_profile(
    feature: dict[str, Any], geometry_points: list[tuple[int, int]], config: dict[str, Any]
) -> str:
    """Select a conservative footprint-derived gable, hip, or flat profile."""
    building_class = str((feature.get("tags") or {}).get("building", "yes")).lower()
    allowed = {str(value).lower() for value in config["shell_contract"]["gable_building_classes"]}
    if building_class not in allowed:
        return "flat"
    convex = all(
        orientation(
            geometry_points[index - 1],
            geometry_points[index],
            geometry_points[(index + 1) % len(geometry_points)],
        )
        * signed_area2(geometry_points)
        > 0
        for index in range(len(geometry_points))
    )
    if len(geometry_points) != 4:
        return "hip" if convex and len(geometry_points) <= 8 else "flat"
    lengths2 = [
        (geometry_points[(index + 1) % 4][0] - geometry_points[index][0]) ** 2
        + (geometry_points[(index + 1) % 4][1] - geometry_points[index][1]) ** 2
        for index in range(4)
    ]
    if min(lengths2) <= 0:
        return "flat"
    # Opposite sides should agree reasonably and adjacent sides should meet at
    # near-right angles.  Irregular footprints retain a flat, closed roof.
    opposite_ratio = max(
        max(lengths2[0], lengths2[2]) / min(lengths2[0], lengths2[2]),
        max(lengths2[1], lengths2[3]) / min(lengths2[1], lengths2[3]),
    )
    right_angle_errors = []
    for index in range(4):
        previous = geometry_points[index - 1]
        current = geometry_points[index]
        following = geometry_points[(index + 1) % 4]
        first = (previous[0] - current[0], previous[1] - current[1])
        second = (following[0] - current[0], following[1] - current[1])
        denominator = math.sqrt((first[0] ** 2 + first[1] ** 2) * (second[0] ** 2 + second[1] ** 2))
        right_angle_errors.append(abs(first[0] * second[0] + first[1] * second[1]) / denominator)
    if opposite_ratio <= 1.35 and max(right_angle_errors) <= 0.25:
        return "gable"
    return "hip" if convex else "flat"


def deterministic_tile_choice(values: list[int], feature_index: int, salt: int) -> int:
    if not values:
        raise ValueError("Material tile palette must not be empty")
    mixed = (
        ((feature_index + 1) * 2654435761)
        ^ ((salt + 1) * 2246822519)
        ^ 3266489917
    ) & 0xFFFFFFFF
    mixed ^= mixed >> 16
    mixed = (mixed * 2246822519) & 0xFFFFFFFF
    mixed ^= mixed >> 13
    return int(values[mixed % len(values)])


def material_tiles(
    feature: dict[str, Any],
    feature_index: int,
    profile: str,
    source_facade_tile: int,
    source_roof_tile: int,
    config: dict[str, Any],
) -> tuple[int, int]:
    """Map the old context recipe to the existing AirportCore V4 PBR cells.

    The source recipe tile remains part of the deterministic salt, but its old
    repeated-window atlas semantics never enter the runtime contract.
    """
    contract = config["material_contract"]
    building_class = str((feature.get("tags") or {}).get("building", "yes")).lower()
    masonry = {str(value).lower() for value in contract["masonry_building_classes"]}
    metal = {str(value).lower() for value in contract["metal_building_classes"]}
    if building_class in masonry:
        facade_palette = contract["masonry_facade_tiles"]
        pitched_palette = contract["pitched_masonry_roof_tiles"]
    elif building_class in metal:
        facade_palette = contract["metal_facade_tiles"]
        pitched_palette = contract["pitched_metal_roof_tiles"]
    else:
        facade_palette = contract["generic_facade_tiles"]
        pitched_palette = contract["pitched_metal_roof_tiles"]
    facade = deterministic_tile_choice(
        [int(value) for value in facade_palette], feature_index, source_facade_tile
    )
    roof_palette = (
        contract["flat_roof_tiles"] if profile == "flat" else pitched_palette
    )
    roof = deterministic_tile_choice(
        [int(value) for value in roof_palette], feature_index, source_roof_tile + 31
    )
    return facade, roof


def build_contract(
    context: dict[str, Any],
    recipe: dict[str, Any],
    hero_replacements: dict[str, Any],
    config: dict[str, Any],
    source_hashes: dict[str, str],
    lidar_document: dict[str, Any] | None = None,
    lidar_path: str | None = None,
    lidar_sha256: str | None = None,
) -> dict[str, Any]:
    features = context["openstreetmap"]["features"]
    quantization = float(context["coordinate_quantization_meters"])
    building_records = [
        record for batch in recipe["building_batches"] for record in batch["buildings"]
    ]
    by_feature = {int(record["feature_index"]): record for record in building_records}
    feature_indices = [index for index, feature in enumerate(features) if feature.get("category") == "building"]
    if len(by_feature) != len(building_records) or set(by_feature) != set(feature_indices):
        raise ValueError("Logical recipe must cover every source building feature exactly once")

    lidar_heights, lidar_summary = qualified_lidar_heights(
        lidar_document,
        float(config["lidar_height_contract"]["minimum_confidence"]),
        float(config["lidar_height_contract"]["maximum_relative_error"]),
    )
    lidar_summary["input_path"] = lidar_path
    lidar_summary["input_sha256"] = lidar_sha256
    lidar_summary["fallback"] = (
        "Keep the serialized terrain-sampled base_y and the pinned context render height. "
        "Never infer a surveyed accuracy claim from the 4 m raster."
    )

    replacement_ids = {
        int(item["source_osm_id"]) for item in hero_replacements.get("replacements", [])
    }
    landmark_ids = {
        (str(item["osm_type"]), int(item["osm_id"]), str(item.get("part", "geometry:0")))
        for item in config.get("far_landmark_allowlist", [])
    }
    output_records: list[dict[str, Any]] = []
    far_orthophoto_feature_indices: list[int] = []
    hero_replacement_source_feature_indices: list[int] = []
    tier_counts: Counter[str] = Counter()
    tier_geometry_counts: Counter[str] = Counter()
    height_source_counts: Counter[str] = Counter()
    lidar_override_count = 0
    maximum_area_error = float(config["mid_simplification"]["maximum_area_error_ratio"])
    for feature_index in feature_indices:
        feature = features[feature_index]
        source_points = decode_points_q(feature)
        source_bounds = bounds_q(source_points)
        source_area = abs(signed_area2(source_points)) * quantization * quantization * 0.5
        centroid_q = polygon_centroid_q(source_points)
        pivot = [round(centroid_q[0] * quantization, 3), round(centroid_q[1] * quantization, 3)]
        radius = math.hypot(pivot[0], pivot[1])
        source = feature.get("source") or {}
        landmark = (
            str(source.get("osm_type")),
            int(source.get("osm_id", -1)),
            str(source.get("part", "geometry:0")),
        ) in landmark_ids
        tier, _representation = _tier(radius, config, landmark)
        if tier == "mid" and not mid_geometry_is_useful(radius, source_area, config):
            tier = "far_orthophoto"
            _representation = "ORTHOPHOTO_ONLY_PROJECTED_SIZE_CUTOFF"
        tier_counts[tier] += 1
        if tier in ("hero_core", "near"):
            geometry_points = list(source_points)
        elif tier in ("mid", "far_landmark"):
            geometry_points = simplify_mid_footprint(
                source_points,
                int(config["mid_simplification"]["maximum_vertices"]),
                maximum_area_error,
            )
        else:
            geometry_points = []
        geometry_area = (
            abs(signed_area2(geometry_points)) * quantization * quantization * 0.5
            if geometry_points
            else 0.0
        )
        inflation = geometry_area / source_area if geometry_points else 0.0
        if geometry_points:
            if bounds_q(geometry_points) != source_bounds:
                raise ValueError(f"Geometry bounds changed for feature {feature_index}")
            if not (
                1.0 - maximum_area_error - 1e-9
                <= inflation
                <= 1.0 + maximum_area_error + 1e-9
            ):
                raise ValueError(f"Footprint area error exceeded the gate for feature {feature_index}")
            if not polygon_is_simple(geometry_points):
                raise ValueError(f"Generated footprint is not simple for feature {feature_index}")
        identity = stable_id(feature)
        fallback_height = round(float(feature["render_height_meters"]), 3)
        selected_height = fallback_height
        height_source = fallback_height_source(feature)
        lidar_used = False
        lidar_confidence = -1.0
        lidar_error = -1.0
        lidar_height = -1.0
        if identity in lidar_heights:
            lidar = lidar_heights[identity]
            selected_height = round(float(lidar["height_meters"]), 3)
            lidar_height = float(lidar["height_meters"])
            lidar_confidence = float(lidar["confidence"])
            lidar_error = max(
                float(lidar["vertical_error_fraction"]),
                abs(selected_height - lidar_height) / lidar_height,
            )
            if lidar_error > float(config["lidar_height_contract"]["maximum_relative_error"]):
                raise ValueError(f"Selected lidar height exceeded the relative error gate: {identity}")
            height_source = "qualified_identity_lidar_height"
            lidar_used = True
            lidar_override_count += 1
        height_source_counts[height_source] += 1
        recipe_record = by_feature[feature_index]
        hero_replacement_source = int(source.get("osm_id", -1)) in replacement_ids
        if hero_replacement_source:
            hero_replacement_source_feature_indices.append(feature_index)
        if tier == "far_orthophoto":
            far_orthophoto_feature_indices.append(feature_index)
            continue
        profile = roof_profile(feature, geometry_points, config)
        facade_tile, roof_tile = material_tiles(
            feature,
            feature_index,
            profile,
            int(recipe_record["facade_tile"]),
            int(recipe_record["roof_tile"]),
            config,
        )
        roof_rise = 0.0
        if profile != "flat":
            bounds = bounds_q(geometry_points)
            short_span = min(bounds[2] - bounds[0], bounds[3] - bounds[1]) * quantization
            shell = config["shell_contract"]
            roof_rise = min(
                float(shell["maximum_gable_rise_meters"]),
                max(
                    float(shell["minimum_gable_rise_meters"]),
                    short_span * float(shell["gable_rise_width_ratio"]),
                ),
                selected_height * 0.35,
            )
        record = {
            "base_y_meters": round(float(recipe_record["base_y_meters"]), 3),
            "facade_tile": facade_tile,
            "feature_index": feature_index,
            "footprint_area_ratio": round(inflation, 6),
            "geometry_points_q": flatten_points(geometry_points),
            "height_meters": selected_height,
            "roof_profile": profile,
            "roof_rise_meters": round(roof_rise, 3),
            "roof_tile": roof_tile,
            "tier": tier,
        }
        if lidar_used:
            record.update(
                {
                    "lidar_confidence": round(lidar_confidence, 6),
                    "lidar_height_meters": round(lidar_height, 3),
                    "lidar_height_used": True,
                    "lidar_selected_relative_error": round(lidar_error, 6),
                }
            )
        output_records.append(record)
        tier_geometry_counts[tier] += 1

    multi_part_components, component_count = build_components(
        output_records, quantization, float(config["component_index_cell_meters"])
    )
    max_inflation = max(
        (record["footprint_area_ratio"] for record in output_records),
        default=0.0,
    )
    return {
        "schema_version": 1,
        "label": "KBDU Aerial Scenery V1 footprint-preserving building spatial contract",
        "coordinate_frame": context["coordinate_frame"],
        "coordinate_quantization_meters": quantization,
        "authored_scale_xyz": [1.0, 1.0, 1.0],
        "source_context_path": config["source_context_path"],
        "source_context_sha256": source_hashes["context"],
        "source_recipe_path": config["source_recipe_path"],
        "source_recipe_sha256": source_hashes["recipe"],
        "source_hero_replacements_path": config["hero_replacements_path"],
        "source_hero_replacements_sha256": source_hashes["hero_replacements"],
        "source_config_sha256": source_hashes["config"],
        "source_generator_path": "tools/scenery/buildings/generate_building_spatial_contract.py",
        "source_generator_sha256": source_hashes["generator"],
        "material_contract": {
            "atlas_grid": config["material_contract"]["atlas_grid"],
            "atlas_source_manifest_path": config["material_contract"]["atlas_source_manifest_path"],
            "atlas_source_manifest_sha256": source_hashes["material_manifest"],
            "albedo_path": config["material_contract"]["albedo_path"],
            "albedo_sha256": source_hashes["material_albedo"],
            "normal_path": config["material_contract"]["normal_path"],
            "normal_sha256": source_hashes["material_normal"],
            "mrao_path": config["material_contract"]["mrao_path"],
            "mrao_sha256": source_hashes["material_mrao"],
            "mrao_channels": config["material_contract"]["mrao_channels"],
            "allowed_facade_tiles": sorted(set(
                int(value)
                for key in (
                    "masonry_facade_tiles", "metal_facade_tiles", "generic_facade_tiles"
                )
                for value in config["material_contract"][key]
            )),
            "allowed_roof_tiles": sorted(set(
                int(value)
                for key in (
                    "flat_roof_tiles", "pitched_masonry_roof_tiles", "pitched_metal_roof_tiles"
                )
                for value in config["material_contract"][key]
            )),
            "incremental_texture_bytes": int(
                config["material_contract"]["incremental_texture_bytes"]
            ),
            "incremental_texture_reason": config["material_contract"][
                "incremental_texture_reason"
            ],
        },
        "source_and_license": {
            "derivative_source": "Pinned KBDU Reconstruction V2 OpenStreetMap building footprints and serialized terrain foundations",
            "openstreetmap_attribution": context["openstreetmap"].get("attribution"),
            "openstreetmap_copyright_url": context["openstreetmap"].get("copyright_url"),
            "openstreetmap_license": context["openstreetmap"].get("license"),
            "project_authored_derivative": True,
        },
        "tier_radii_meters": config["tier_radii_meters"],
        "spatial_contract": {
            "component_rule": "Only represented footprint parts that intersect or touch may share a component; batching never fills the space between parts.",
            "component_storage_rule": "Only multi-part components are serialized; every represented feature not listed is an implicit singleton component.",
            "far_rule": "Ordinary buildings beyond 4 km and sub-threshold structures beyond the unconditional mid-shell radius have no mesh geometry; the orthophoto is authoritative there.",
            "foundation_rule": "Every rendered tier keeps the terrain-sampled recipe base_y without lidar or LOD mutation.",
            "mid_rule": "Useful projected-size mid geometry uses a fixed closed shell; vertex-removal simplification preserves source X/Z bounds, simple topology, and +/-10 percent area.",
            "near_rule": "Hero-core and near closed shells use the quantized source footprint byte-for-byte.",
            "pivot_rule": "The source polygon centroid and bounds are recomputed exactly from the pinned quantized footprint; unit scale, base_y, selected height, and batch bounds are immutable because runtime scale-changing LODs are forbidden.",
            "roof_rule": "Conservative four-corner residential footprints may use a bounded deterministic gable; all other footprints retain a closed flat roof at the same maximum height.",
            "material_rule": "Every shell uses deterministic plausible brick, concrete, corrugated-metal, worn-metal, dark-roof, or asphalt-roof cells from the already-resident AirportCore V4 HangarKit PBR atlas; the obsolete repeated-window context atlas is forbidden.",
        },
        "height_input": lidar_summary,
        "far_landmark_allowlist": config.get("far_landmark_allowlist", []),
        "far_orthophoto_feature_indices": far_orthophoto_feature_indices,
        "hero_replacement_source_feature_indices": hero_replacement_source_feature_indices,
        "records": output_records,
        "multi_part_components": multi_part_components,
        "compaction": {
            "far_payload": "Feature index only; footprint, pivot, bounds, foundation, and fallback height remain reproducible from the pinned context and recipe.",
            "record_payload": "Only represented geometry and Unity authoring inputs are serialized; stable identity, source bounds, pivot, unit scale, fallback provenance, and default lidar fields are derived or stored once.",
        },
        "statistics": {
            "component_count": component_count,
            "geometry_building_count": sum(tier_geometry_counts.values()),
            "height_fallback_count": len(feature_indices) - lidar_override_count,
            "height_source_counts": [
                {"count": height_source_counts[source], "source": source}
                for source in sorted(height_source_counts)
            ],
            "lidar_height_override_count": lidar_override_count,
            "maximum_footprint_area_ratio": round(max_inflation, 6),
            "serialized_record_count": len(output_records),
            "source_building_count": len(feature_indices),
            "tier_counts": [
                {
                    "geometry_count": tier_geometry_counts[tier],
                    "record_count": tier_counts[tier],
                    "tier": tier,
                }
                for tier in ("hero_core", "near", "mid", "far_landmark", "far_orthophoto")
            ],
        },
        "limitations": [
            "OSM footprints and tag-derived fallback heights are reference-grade, not survey-grade.",
            "The current 4 m lidar height raster has no surveyed vertical-error study and cannot satisfy the 10 percent height gate.",
            "Small context buildings beyond 2 km intentionally remain orthophoto-only instead of becoming floating roof cards.",
            "This offline contract does not establish Quest performance, visual acceptance, or certified fidelity.",
        ],
    }


def build_batch_replacement_manifest(
    contract: dict[str, Any],
    logical_recipe: dict[str, Any],
    runtime_recipe: dict[str, Any],
    config: dict[str, Any],
    runtime_recipe_sha256: str,
) -> dict[str, Any]:
    logical = {batch["id"]: batch for batch in logical_recipe["building_batches"]}
    spatial = {record["feature_index"]: record for record in contract["records"]}
    far_orthophoto = set(contract["far_orthophoto_feature_indices"])
    hero_replacement_sources = set(contract["hero_replacement_source_feature_indices"])
    rows: list[dict[str, Any]] = []
    all_legacy_groups: list[str] = []
    all_legacy_renderers: list[str] = []
    all_new_renderers: list[str] = []
    for batch in runtime_recipe["building_runtime_batches"]:
        legacy_id = str(batch["id"])
        all_legacy_groups.append(legacy_id)
        legacy_renderers = [f"{legacy_id}/LOD{lod}" for lod in range(3)]
        all_legacy_renderers.extend(legacy_renderers)
        feature_indices = [
            int(item["feature_index"])
            for logical_id in batch["logical_batch_ids"]
            for item in logical[logical_id]["buildings"]
        ]
        unknown = set(feature_indices) - set(spatial) - far_orthophoto
        if unknown:
            raise ValueError(f"Building batch references unknown compact-contract features: {sorted(unknown)[:5]}")
        records = [spatial[index] for index in feature_indices if index in spatial]
        represented = [
            record for record in records if record["feature_index"] not in hero_replacement_sources
        ]
        tier_counts = Counter(
            spatial[index]["tier"] if index in spatial else "far_orthophoto"
            for index in feature_indices
        )
        represented_tier_counts = Counter(record["tier"] for record in represented)
        if represented and batch["zone"] == "4-6km" and all(
            record["tier"] == "mid" for record in represented
        ):
            # The legacy recipe used a vertex-average batch position. Three
            # footprints straddle its 4 km boundary but have a true polygon
            # area centroid just inside the mid tier. Name those recovery
            # groups as mid content so no apparent far group enters the prefab.
            cell_suffix = "_".join(legacy_id.split("_")[-2:])
            group_id = f"AerialBuildingsV1__mid_boundary_{cell_suffix}"
        else:
            group_id = f"AerialBuildingsV1__{legacy_id}" if represented else None
        shell_triangles = 0
        shell_vertices = 0
        for record in represented:
            point_count = len(record["geometry_points_q"]) // 2
            if record["roof_profile"] == "gable":
                if point_count != 4:
                    raise ValueError("Gable profile requires exactly four footprint points")
                shell_triangles += 14
                shell_vertices += 30
            elif record["roof_profile"] == "hip":
                shell_triangles += 3 * point_count
                shell_vertices += 7 * point_count
            else:
                shell_triangles += 3 * point_count - 2
                shell_vertices += 5 * point_count
        if represented:
            represented_bounds = [
                bounds_q(
                    list(zip(record["geometry_points_q"][0::2], record["geometry_points_q"][1::2]))
                )
                for record in represented
            ]
            minimum_x = min(value[0] for value in represented_bounds)
            minimum_z = min(value[1] for value in represented_bounds)
            maximum_x = max(value[2] for value in represented_bounds)
            maximum_z = max(value[3] for value in represented_bounds)
            minimum_y = min(record["base_y_meters"] for record in represented)
            maximum_y = max(record["base_y_meters"] + record["height_meters"] for record in represented)
            stable_bounds = [
                round(minimum_x * contract["coordinate_quantization_meters"], 3),
                round(minimum_y, 3),
                round(minimum_z * contract["coordinate_quantization_meters"], 3),
                round(maximum_x * contract["coordinate_quantization_meters"], 3),
                round(maximum_y, 3),
                round(maximum_z * contract["coordinate_quantization_meters"], 3),
            ]
        else:
            stable_bounds = []
        if not represented:
            lods = []
        else:
            lods = [
                {
                    "level": "FIXED",
                    "mesh_mode": "CLOSED_TRUE_FOOTPRINT_SHELL_FIXED_RENDERER",
                    "screen_relative_height": 0.0,
                    "triangles": shell_triangles,
                    "vertices": shell_vertices,
                }
            ]
        new_renderers = [f"{group_id}/LOD{lod}" for lod in range(len(lods))] if group_id else []
        all_new_renderers.extend(new_renderers)
        rows.append(
            {
                "integration_action": (
                    "DISABLE_LEGACY_AND_ADD_AERIAL_PREFAB_GROUP"
                    if represented
                    else "DISABLE_LEGACY_ORTHOPHOTO_ONLY_NO_REPLACEMENT_RENDERER"
                ),
                "legacy_group_id_to_disable": legacy_id,
                "legacy_renderer_ids_to_disable": legacy_renderers,
                "logical_batch_ids": list(batch["logical_batch_ids"]),
                "new_group_id": group_id,
                "new_renderer_ids": new_renderers,
                "represented_feature_indices": [record["feature_index"] for record in represented],
                "represented_tier_counts": [
                    {"count": represented_tier_counts[tier], "tier": tier}
                    for tier in ("hero_core", "near", "mid", "far_landmark", "far_orthophoto")
                ],
                "source_building_count": len(feature_indices),
                "stable_bounds_min_max_xyz_meters": stable_bounds,
                "tier_counts": [
                    {"count": tier_counts[tier], "tier": tier}
                    for tier in ("hero_core", "near", "mid", "far_landmark", "far_orthophoto")
                ],
                "zone": batch["zone"],
                "lods": lods,
            }
        )
    rendered = [row for row in rows if row["new_group_id"]]
    return {
        "schema_version": 1,
        "label": "KBDU Aerial Scenery V1 legacy-building replacement map",
        "source_spatial_contract_path": config["output_manifest_path"],
        "source_spatial_contract_sha256": hashlib.sha256(canonical_bytes(contract)).hexdigest(),
        "source_legacy_runtime_recipe_path": config["legacy_runtime_recipe_path"],
        "source_legacy_runtime_recipe_sha256": runtime_recipe_sha256,
        "replacement_prefab_path": "Assets/Production/Environment/AerialSceneryV1/Buildings/KBDU_AerialBuildingsV1.prefab",
        "generated_mesh_root": "Assets/Production/Environment/AerialSceneryV1/Buildings/Generated",
        "legacy_group_ids_to_disable": all_legacy_groups,
        "legacy_renderer_ids_to_disable": all_legacy_renderers,
        "new_renderer_ids": all_new_renderers,
        "batches": rows,
        "statistics": {
            "hero_replacement_source_count_omitted": len(hero_replacement_sources),
            "legacy_batch_count": len(rows),
            "legacy_renderer_count_to_disable": len(all_legacy_renderers),
            "orthophoto_only_batch_count": len(rows) - len(rendered),
            "replacement_group_count": len(rendered),
            "replacement_renderer_count": len(all_new_renderers),
            "fixed_mid_group_count": sum(len(row["lods"]) == 1 for row in rendered),
            "near_reduction_lod_group_count": sum(len(row["lods"]) == 2 for row in rendered),
            "ordinary_far_geometry_count": sum(
                next(
                    item["count"]
                    for item in row["represented_tier_counts"]
                    if item["tier"] == "far_orthophoto"
                )
                for row in rows
            ),
            "replacement_serialized_triangles": sum(
                lod["triangles"] for row in rendered for lod in row["lods"]
            ),
            "replacement_worst_case_triangles": sum(
                max(lod["triangles"] for lod in row["lods"]) for row in rendered
            ),
        },
        "integration_rules": [
            "Lead integration must disable every listed legacy group before enabling the standalone replacement prefab.",
            "The replacement prefab is explicitly authored offline; no runtime bootstrap or repair is permitted.",
            "Authored hero replacements remain authoritative, so their two generic OSM source footprints are omitted.",
            "Ordinary far batches have no replacement renderer and rely on the existing orthophoto/terrain.",
            "Every represented context batch uses one fixed closed-shell renderer and no LODGroup; no runtime representation may collapse to a roof card or change scale.",
            "Small structures beyond the unconditional mid-shell radius remain orthophoto-only; significant projected-size buildings retain closed true-footprint shells.",
            "Legacy 4-6 km cells containing true-centroid mid-boundary footprints are named as mid-boundary recovery groups; they contain no ordinary-far geometry.",
        ],
    }


def render_report(contract: dict[str, Any]) -> str:
    statistics = contract["statistics"]
    lines = [
        "# Aerial Scenery V1 building spatial contract",
        "",
        "**Offline contract result:** `PASS`",
        "",
        f"- Source buildings covered once: {statistics['source_building_count']:,}",
        f"- Buildings with authored geometry: {statistics['geometry_building_count']:,}",
        f"- Serialized represented records: {statistics['serialized_record_count']:,}",
        f"- Far orthophoto compact feature indices: {len(contract['far_orthophoto_feature_indices']):,}",
        f"- Contiguous footprint components: {statistics['component_count']:,}",
        f"- Serialized multi-part component records: {len(contract['multi_part_components']):,}",
        f"- Maximum footprint area ratio: {statistics['maximum_footprint_area_ratio']:.6f} (limit 1.100000)",
        f"- Qualified lidar height overrides: {statistics['lidar_height_override_count']:,}",
        f"- Deterministic height fallbacks: {statistics['height_fallback_count']:,}",
        f"- Height input status: `{contract['height_input']['status']}`",
        f"- Height input SHA-256: `{contract['height_input']['input_sha256'] or 'not provided'}`",
        "",
        "| Tier | Records | Geometry |",
        "|---|---:|---:|",
    ]
    for row in statistics["tier_counts"]:
        lines.append(f"| {row['tier']} | {row['record_count']:,} | {row['geometry_count']:,} |")
    lines += [
        "",
        "This is deterministic source/geometry evidence, not headset-performance or survey-accuracy evidence.",
    ]
    return "\n".join(lines) + "\n"


def main(argv: list[str] | None = None) -> int:
    script = Path(__file__).resolve()
    repo_default = script.parents[3]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=repo_default)
    parser.add_argument("--config", type=Path, default=script.with_name("config.json"))
    parser.add_argument("--output", type=Path)
    parser.add_argument("--batch-output", type=Path)
    parser.add_argument("--report-output", type=Path)
    parser.add_argument("--lidar-manifest", type=Path)
    args = parser.parse_args(argv)
    root = args.repo_root.resolve()
    config = load_json(args.config)
    context_path = root / config["source_context_path"]
    recipe_path = root / config["source_recipe_path"]
    runtime_recipe_path = root / config["legacy_runtime_recipe_path"]
    replacements_path = root / config["hero_replacements_path"]
    material_manifest_path = root / config["material_contract"]["atlas_source_manifest_path"]
    material_albedo_path = root / config["material_contract"]["albedo_path"]
    material_normal_path = root / config["material_contract"]["normal_path"]
    material_mrao_path = root / config["material_contract"]["mrao_path"]
    output = args.output or root / config["output_manifest_path"]
    batch_output = args.batch_output or root / config["output_batch_replacement_manifest_path"]
    lidar_path = args.lidar_manifest
    if lidar_path is None and config.get("optional_lidar_manifest_path"):
        candidate = root / config["optional_lidar_manifest_path"]
        lidar_path = candidate if candidate.is_file() else None
    lidar_document = load_json(lidar_path) if lidar_path is not None else None
    lidar_relative = None
    if lidar_path is not None:
        try:
            lidar_relative = lidar_path.resolve().relative_to(root).as_posix()
        except ValueError:
            lidar_relative = str(lidar_path.resolve())
    contract = build_contract(
        load_json(context_path),
        load_json(recipe_path),
        load_json(replacements_path),
        config,
        {
            "context": sha256(context_path),
            "recipe": sha256(recipe_path),
            "hero_replacements": sha256(replacements_path),
            "config": sha256(args.config),
            "generator": sha256(script),
            "material_manifest": sha256(material_manifest_path),
            "material_albedo": sha256(material_albedo_path),
            "material_normal": sha256(material_normal_path),
            "material_mrao": sha256(material_mrao_path),
        },
        lidar_document,
        lidar_relative,
        sha256(lidar_path) if lidar_path is not None else None,
    )
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(canonical_bytes(contract))
    batch_manifest = build_batch_replacement_manifest(
        contract,
        load_json(recipe_path),
        load_json(runtime_recipe_path),
        config,
        sha256(runtime_recipe_path),
    )
    batch_output.parent.mkdir(parents=True, exist_ok=True)
    batch_output.write_bytes(canonical_bytes(batch_manifest))
    if args.report_output:
        args.report_output.parent.mkdir(parents=True, exist_ok=True)
        args.report_output.write_text(render_report(contract), encoding="utf-8")
    print(output)
    print(batch_output)
    print(render_report(contract), end="")
    return 0


if __name__ == "__main__":
    sys.exit(main())
