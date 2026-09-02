#!/usr/bin/env python3
"""Restore omitted KBDU buildings without disturbing authored airport models.

This is a deterministic post-process for the accepted, facade-enriched full-map
database. It uses the already committed OSM snapshot solely for the airport gap
and the exact industrial corridor right of the Runway 08 departure path. It
excludes exact hero replacements, residential edge features, and structures
below the full-map area threshold.
"""

from __future__ import annotations

import argparse
import json
import math
import pathlib
from collections import Counter, defaultdict

from shapely.geometry import Polygon
from shapely.geometry.polygon import orient

from build_full_map_building_database import (
    TerrainSurfaceSampler,
    quantized_ring,
    roof_profile,
    sha256,
    simplify_ring,
    stable_u32,
)
from enrich_building_facades_v2 import (
    MATERIAL_NAMES,
    REGION_NAMES,
    RULE_NAMES,
    SEMANTIC_NAMES,
    geometry_hash,
    stable_pick,
    style_for,
)


def load(path: pathlib.Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def compact(path: pathlib.Path, value: dict) -> None:
    path.write_text(json.dumps(value, separators=(",", ":"), ensure_ascii=False), encoding="utf-8")


def pretty(path: pathlib.Path, value: dict) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def source_identity(feature: dict) -> str:
    source = feature["source"]
    return f"OSM:{source['osm_type']}:{source['osm_id']}:{source.get('part', 'geometry:0')}"


def polygon_for(feature: dict, quantum: float) -> Polygon:
    values = feature.get("points_q") or []
    points = [(float(values[index]) * quantum, float(values[index + 1]) * quantum)
              for index in range(0, len(values) - 1, 2)]
    if len(points) > 1 and points[0] == points[-1]:
        points.pop()
    polygon = Polygon(points)
    if not polygon.is_valid:
        polygon = polygon.buffer(0)
    if not isinstance(polygon, Polygon) or polygon.is_empty:
        raise ValueError(f"Invalid polygon for {source_identity(feature)}")
    return orient(polygon, sign=1.0)


def select_features(config: dict, context: dict, replacements: dict) -> tuple[
        list[tuple[dict, Polygon]], dict, set[str], set[str]]:
    contract = config["airport_gap_fill"]
    quantum = float(context["coordinate_quantization_meters"])
    features = context["openstreetmap"]["features"]
    aerodromes = [feature for feature in features
                  if feature.get("category") == "aeroway"
                  and (feature.get("tags") or {}).get("aeroway") == "aerodrome"
                  and (feature.get("tags") or {}).get("name") == contract["aerodrome_name"]]
    if len(aerodromes) != 1:
        raise ValueError(f"Expected one pinned KBDU aerodrome boundary, found {len(aerodromes)}")
    aerodrome = polygon_for(aerodromes[0], quantum)
    hero_ids = {int(item["source_osm_id"]) for item in replacements["replacements"]}
    right_departure_ids = {int(value) for value in contract["right_runway_08_departure_source_ids"]}
    excluded_classes = {value.lower() for value in contract["excluded_building_classes"]}
    minimum_area = float(contract["minimum_component_area_square_meters"])
    selected: list[tuple[dict, Polygon]] = []
    aerodrome_source_ids: set[str] = set()
    right_departure_source_ids: set[str] = set()
    counts = Counter()
    for feature in features:
        if feature.get("category") != "building":
            continue
        polygon = polygon_for(feature, quantum)
        source_id = int(feature["source"]["osm_id"])
        inside_aerodrome = aerodrome.covers(polygon.representative_point())
        inside_right_departure = source_id in right_departure_ids
        if not inside_aerodrome and not inside_right_departure:
            continue
        building_class = str((feature.get("tags") or {}).get("building") or "yes").lower()
        if source_id in hero_ids:
            counts["hero_replacement"] += 1
        elif building_class in excluded_classes:
            counts["residential_edge"] += 1
        elif polygon.area < minimum_area:
            counts["below_area_threshold"] += 1
        else:
            selected.append((feature, polygon))
            identity = source_identity(feature)
            if inside_aerodrome:
                aerodrome_source_ids.add(identity)
            if inside_right_departure:
                right_departure_source_ids.add(identity)
    selected.sort(key=lambda pair: source_identity(pair[0]))
    expected_aerodrome = int(contract["expected_aerodrome_building_count"])
    if len(aerodrome_source_ids) != expected_aerodrome:
        raise ValueError(
            f"Aerodrome gap-fill selection changed: expected {expected_aerodrome}, "
            f"found {len(aerodrome_source_ids)}")
    expected_right = int(contract["expected_right_departure_building_count"])
    expected_right_ids = {
        f"OSM:way:{source_id}:geometry:0" for source_id in right_departure_ids
    }
    if len(right_departure_ids) != expected_right or right_departure_source_ids != expected_right_ids:
        raise ValueError(
            f"Runway 08 right-departure selection changed: expected {expected_right}, "
            f"found {len(right_departure_source_ids)}")
    expected = int(contract["expected_building_count"])
    if len(selected) != expected:
        raise ValueError(f"Airport gap-fill selection changed: expected {expected}, found {len(selected)}")
    return (selected, dict(sorted(counts.items())), aerodrome_source_ids,
            right_departure_source_ids)


def semantic_for(feature: dict) -> str:
    tags = feature.get("tags") or {}
    value = str(tags.get("building") or "").lower()
    return "commercial" if value == "commercial" or tags.get("name") else "industrial"


def fallback_palette(semantic: str) -> tuple[int, ...]:
    if semantic == "commercial":
        return (3, 4, 5, 6, 7, 9, 9, 12, 13)
    return (8, 9, 9, 10, 10, 11, 13, 14)


def make_record(config: dict, database: dict, terrain: TerrainSurfaceSampler,
                feature: dict, polygon: Polygon, record_id: int) -> tuple[int, int, dict]:
    extent = float(database["map_half_extent_meters"])
    size = float(database["page_size_meters"])
    quantum = float(database["quantization_meters"])
    center = polygon.representative_point()
    column = min(int(database["columns"]) - 1, max(0, int((center.x + extent) // size)))
    row = min(int(database["rows"]) - 1, max(0, int((center.y + extent) // size)))
    page_min_x = -extent + column * size
    page_min_z = -extent + row * size
    identity = source_identity(feature)
    seed = stable_u32(f"{identity}:0")
    semantic = semantic_for(feature)
    class_name = "commercial" if semantic == "commercial" else "industrial"
    profile = roof_profile(class_name, polygon, seed)
    height = max(float(config["height_contract"]["minimum_meters"]),
                 float(feature.get("render_height_meters") or
                       config["height_contract"]["fallback_by_class_meters"][class_name]))
    rectangle = polygon.minimum_rotated_rectangle
    rectangle_points = list(rectangle.exterior.coords)[:-1]
    spans = [math.dist(rectangle_points[index], rectangle_points[(index + 1) % 4]) for index in range(4)]
    short_span = max(1.0, min(spans))
    roof_rise = 0.0 if profile == "flat" else min(
        float(config["roof_contract"]["maximum_rise_meters"]),
        max(float(config["roof_contract"]["minimum_rise_meters"]),
            short_span * float(config["roof_contract"]["industrial_pitch_rise_ratio"])))
    base_y, _ = terrain.sample(center.x, center.y)
    material = stable_pick(seed, fallback_palette(semantic), 29)
    stories = max(1.0, min(20.0, height / 3.15))
    record = {
        "id": record_id,
        "source_id": identity,
        "component": 0,
        "class_id": 1 if semantic == "commercial" else 2,
        "roof_id": {"flat": 0, "gable": 1, "hip": 2}[profile],
        "height_dm": round(height * 10),
        "roof_rise_dm": round(roof_rise * 10),
        "base_y_dm": round(base_y * 10),
        "area_m2": round(polygon.area, 1),
        "seed": seed,
        "landmark": False,
        "lod0_q": quantized_ring(simplify_ring(polygon, 0.08), page_min_x, page_min_z, quantum),
        "lod1_q": quantized_ring(simplify_ring(
            polygon, float(config["roof_contract"]["lod1_simplification_meters"])), page_min_x, page_min_z, quantum),
        "lod2_q": quantized_ring(simplify_ring(
            polygon, float(config["roof_contract"]["lod2_simplification_meters"])), page_min_x, page_min_z, quantum),
        "material_id": material,
        "secondary_material_id": -1,
        "material_confidence": 0,
        "facade_style_id": style_for(semantic),
        "stories_x2": int(max(2, min(40, round(stories * 2.0)))),
        "built_year": 0,
        "semantic_class_id": SEMANTIC_NAMES.index(semantic),
        "semantic_region_id": REGION_NAMES.index("airport_service"),
        "semantic_confidence": 1,
        "parcel_role_id": 0,
        "material_rule_id": RULE_NAMES.index("deterministic_geometry_fallback"),
    }
    return column, row, record


def update_facade_manifest(database: dict, manifest: dict, context_path: pathlib.Path,
                           context_logical_path: str, expected_count: int,
                           added_source_ids: set[str]) -> None:
    gap_records = [
        (page, building)
        for page in database["pages"]
        for building in page.get("buildings", [])
        if building["source_id"].startswith("OSM:")
    ]
    if len(gap_records) != expected_count:
        raise ValueError(f"Expected {expected_count} serialized airport gap records, found {len(gap_records)}")
    prior = int(manifest.get("airport_gap_fill_building_count", 0))
    additions = gap_records if prior == 0 else [
        pair for pair in gap_records if pair[1]["source_id"] in added_source_ids
    ]
    if prior + len(additions) != expected_count:
        raise ValueError(
            "Facade manifest cannot reconcile airport gap-fill count: "
            f"prior={prior}, additions={len(additions)}, expected={expected_count}")
    if additions:
        fields = [
            ("material_counts", "material_id", MATERIAL_NAMES),
            ("style_counts", "facade_style_id", None),
            ("semantic_counts", "semantic_class_id", SEMANTIC_NAMES),
            ("region_counts", "semantic_region_id", REGION_NAMES),
            ("rule_counts", "material_rule_id", RULE_NAMES),
            ("material_confidence_counts", "material_confidence", None),
            ("parcel_role_counts", "parcel_role_id", None),
        ]
        for output_name, field, names in fields:
            field_additions = Counter(int(building[field]) for _, building in additions)
            for key, value in field_additions.items():
                label = names[key] if names else str(key)
                manifest[output_name][label] = int(manifest[output_name].get(label, 0)) + value
        page_additions: dict[str, Counter] = defaultdict(Counter)
        for page, building in additions:
            key = f"{int(page['column']):02d}_{int(page['row']):02d}"
            page_additions[key][MATERIAL_NAMES[int(building["material_id"])] ] += 1
        for key, additions in page_additions.items():
            values = manifest["page_material_counts"].setdefault(key, {})
            for material, value in additions.items():
                values[material] = int(values.get(material, 0)) + value
            manifest["page_material_counts"][key] = dict(sorted(values.items()))
        manifest["page_material_counts"] = dict(sorted(manifest["page_material_counts"].items()))
    digest = geometry_hash(database)
    manifest["building_count"] = sum(len(page.get("buildings", [])) for page in database["pages"])
    manifest["geometry_hash_before"] = digest
    manifest["geometry_hash_after"] = digest
    manifest["airport_gap_fill_building_count"] = expected_count
    manifest["sources"]["airport_gap_fill_context_path"] = context_logical_path
    manifest["sources"]["airport_gap_fill_context_sha256"] = sha256(context_path)


def apply(config: dict, database: dict, context: dict, replacements: dict) -> tuple[dict, dict]:
    selected, exclusions, aerodrome_ids, right_departure_ids = select_features(
        config, context, replacements)
    expected_ids = [source_identity(feature) for feature, _ in selected]
    expected_set = set(expected_ids)
    existing_gap = {
        item["source_id"] for page in database["pages"] for item in page.get("buildings", [])
        if item["source_id"].startswith("OSM:")
    }
    if existing_gap not in (set(), aerodrome_ids, expected_set):
        raise ValueError(
            "Airport gap-fill is in an unknown partial state; refusing an ambiguous repair")
    missing = expected_set - existing_gap
    if missing:
        terrain = TerrainSurfaceSampler(pathlib.Path(config["terrain_surface_path"]))
        next_id = max(item["id"] for page in database["pages"] for item in page.get("buildings", [])) + 1
        pages = {(int(page["column"]), int(page["row"])): page for page in database["pages"]}
        offset = 0
        for feature, polygon in selected:
            if source_identity(feature) not in missing:
                continue
            column, row, record = make_record(config, database, terrain, feature, polygon, next_id + offset)
            pages[(column, row)]["buildings"].append(record)
            offset += 1
        for page in database["pages"]:
            page["buildings"].sort(key=lambda item: (item["source_id"], int(item["component"])))
    total = sum(len(page.get("buildings", [])) for page in database["pages"])
    report = {
        "status": "PASS",
        "source_contract": config["airport_gap_fill"]["source_contract"],
        "restored_building_count": len(selected),
        "restored_source_ids": expected_ids,
        "added_source_ids": sorted(missing),
        "aerodrome_building_count": len(aerodrome_ids),
        "right_runway_08_departure_building_count": len(right_departure_ids),
        "selection_exclusions": exclusions,
        "total_building_count": total,
        "hero_replacement_source_ids": sorted(int(item["source_osm_id"]) for item in replacements["replacements"]),
    }
    return database, report


def main() -> int:
    root = pathlib.Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=pathlib.Path, default=pathlib.Path(__file__).with_name("config.json"))
    parser.add_argument("--database", type=pathlib.Path, required=True)
    parser.add_argument("--provenance", type=pathlib.Path, required=True)
    parser.add_argument("--facade-manifest", type=pathlib.Path, required=True)
    parser.add_argument("--report", type=pathlib.Path, required=True)
    args = parser.parse_args()
    config = load(args.config)
    context_path = root / config["airport_gap_fill"]["source_context_path"]
    replacements_path = root / config["airport_gap_fill"]["hero_replacements_path"]
    database, report = apply(config, load(args.database), load(context_path), load(replacements_path))
    facade = load(args.facade_manifest)
    update_facade_manifest(
        database, facade, context_path, config["airport_gap_fill"]["source_context_path"],
        int(report["restored_building_count"]), set(report["added_source_ids"]))
    compact(args.database, database)
    pretty(args.facade_manifest, facade)
    provenance = load(args.provenance)
    count = int(report["restored_building_count"])
    provenance["selection"]["compiled_components"] = int(report["total_building_count"])
    provenance["selection"]["airport_gap_fill_components"] = count
    provenance["selection"]["airport_gap_fill_contract"] = config["airport_gap_fill"]["source_contract"]
    provenance["sources"] = [item for item in provenance["sources"] if item.get("name") != "Pinned OSM KBDU airport gap fill"]
    provenance["sources"].append({
        "name": "Pinned OSM KBDU airport gap fill",
        "path": config["airport_gap_fill"]["source_context_path"],
        "sha256": sha256(context_path),
        "retained_components": count,
        "rights_record": "OpenStreetMap ODbL-1.0; attribution retained in the committed source context.",
    })
    provenance["derivative"]["sha256"] = sha256(args.database)
    pretty(args.provenance, provenance)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
