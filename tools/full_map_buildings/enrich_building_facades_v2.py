#!/usr/bin/env python3
"""Compile deterministic, Boulder-grounded facade semantics for every building.

The raw City/County footprint, parcel, and assessor sources remain outside Git.
The product database receives only compact semantic identifiers. Geometry fields
are hashed before and after enrichment so this stage cannot move or reshape a
building.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import pathlib
from collections import Counter, defaultdict
from dataclasses import dataclass

from pyproj import CRS, Transformer
from shapely.geometry import Polygon, shape
from shapely.ops import transform
from shapely.strtree import STRtree

from enrich_building_facades import PARCEL_SERVICE, clean, read_assessor, sha256


MATERIAL_NAMES = [
    "warm_white_wood_siding",
    "weathered_wood_siding",
    "warm_fiber_cement",
    "red_brick",
    "brown_brick",
    "tan_brick",
    "warm_beige_stucco",
    "cream_stucco",
    "warm_concrete_block",
    "warm_precast_concrete",
    "galvanized_corrugated_metal",
    "tan_metal_panel",
    "boulder_sandstone",
    "warm_painted_block",
    "charcoal_metal_panel",
    "cedar_siding",
]

SEMANTIC_NAMES = [
    "residential", "commercial", "industrial", "institutional", "utility",
    "residential_accessory", "commercial_accessory", "industrial_accessory",
    "agricultural_outbuilding", "generic",
]

REGION_NAMES = [
    "rural_context", "boulder_residential", "downtown_historic", "cu_campus",
    "commercial_mixed", "east_industrial_flex", "airport_service",
]

RULE_NAMES = [
    "deterministic_geometry_fallback",
    "parcel_construction_evidence",
    "assessor_exterior_wall_evidence",
    "parcel_primary_material_inheritance",
    "official_footprint_use_fallback",
]

EXACT_MATERIAL = {
    "frame wood/shake": (0, 1, 2, 15),
    "brick veneer": (3, 4, 5),
    "frame stucco": (6, 7),
    "cement board": (2,),
    "brick on block": (3, 4, 5),
    "moss rock/flagstone": (12,),
    "log": (15,),
    "block stucco": (6, 7, 13),
    "vinyl": (0, 2, 7),
    "faux stone": (12,),
    "metal": (10, 11, 14),
    "painted block": (13,),
    "cedar": (15,),
    "frame asbestos": (0, 2),
    "strawbale": (6,),
}

CONSTRUCTION_MATERIAL = {
    "masonry": (3, 4, 5, 8),
    "brick": (3, 4, 5),
    "block": (8, 13),
    "steel": (10, 11, 14),
    "precast": (9,),
    "concrete": (8, 9, 13),
    "tees/tilt": (9,),
    "moss rock": (12,),
    "veneer": (3, 4, 5, 12),
    "wood": (0, 1, 2, 15),
}

FAMILY = {
    0: "siding", 1: "siding", 2: "siding", 15: "siding",
    3: "masonry", 4: "masonry", 5: "masonry", 12: "masonry",
    6: "stucco", 7: "stucco",
    8: "block", 9: "block", 13: "block",
    10: "metal", 11: "metal", 14: "metal",
}


@dataclass
class Record:
    building: dict
    polygon: Polygon
    parcel_index: int | None
    parcel: dict | None
    assessor: dict | None
    source: dict | None
    semantic: str
    semantic_confidence: int
    region: int
    parcel_role: int = 0  # 0 unmatched, 1 primary, 2 accessory


def stable_pick(seed: int, values: tuple[int, ...], salt: int) -> int:
    value = (int(seed) ^ (salt * 0x9E3779B1)) & 0xFFFFFFFF
    return values[value % len(values)]


def geometry_hash(database: dict) -> str:
    digest = hashlib.sha256()
    fields = (
        "id", "source_id", "component", "class_id", "roof_id", "height_dm",
        "roof_rise_dm", "base_y_dm", "area_m2", "landmark", "lod0_q",
        "lod1_q", "lod2_q",
    )
    for page in database["pages"]:
        digest.update(f"{page['column']}:{page['row']}|".encode("ascii"))
        for building in page.get("buildings", []):
            payload = [building.get(field) for field in fields]
            digest.update(json.dumps(payload, separators=(",", ":"), ensure_ascii=True).encode("utf-8"))
    return digest.hexdigest()


def database_records(database: dict) -> list[tuple[dict, Polygon]]:
    extent = float(database["map_half_extent_meters"])
    page_size = float(database["page_size_meters"])
    quantum = float(database["quantization_meters"])
    result = []
    for page in database["pages"]:
        page_min_x = -extent + int(page["column"]) * page_size
        page_min_z = -extent + int(page["row"]) * page_size
        for building in page.get("buildings", []):
            ring = building["lod2_q"]
            points = [
                (page_min_x + ring[index] * quantum, page_min_z + ring[index + 1] * quantum)
                for index in range(0, len(ring), 2)
            ]
            result.append((building, Polygon(points)))
    return result


def footprint_properties(city_path: pathlib.Path, county_path: pathlib.Path) -> dict[str, dict]:
    result: dict[str, dict] = {}
    city = json.loads(city_path.read_text(encoding="utf-8"))
    for feature in city.get("features", []):
        props = feature.get("properties") or {}
        identity = str(props.get("FACILITYID") or props.get("DRCOGID") or props.get("OBJECTID") or "")
        if identity:
            result[identity] = props
    county = json.loads(county_path.read_text(encoding="utf-8"))
    for feature in county.get("features", []):
        props = feature.get("properties") or {}
        identity = f"COUNTY:{props.get('StructureI') or ''}:{props.get('OBJECTID') or ''}"
        result[identity] = props
    return result


def source_semantic(props: dict | None) -> tuple[str | None, int]:
    if not props or "BLDGTYPE" not in props:
        return None, 0
    primary = clean(props.get("BLDGTYPE")).lower()
    subtype = clean(props.get("SUBTYPE")).lower()
    if primary == "residential" or any(token in subtype for token in
        ("mobile home", "apartment", "condo", "duplex", "single-family", "university housing")):
        return "residential", 2
    if primary == "garage/shed":
        return "accessory", 2
    if primary == "commercial" or any(token in subtype for token in
        ("office", "retail", "lodging", "restaurant")):
        return "commercial", 2
    if primary == "industrial":
        return "industrial", 2
    if primary in ("school", "public", "public safety", "religious", "medical") or any(
        token in subtype for token in ("university", "school", "hospital", "library", "fire station", "museum")):
        return "institutional", 2
    if primary in ("tank", "foundation/ruin"):
        return "utility", 2
    if primary == "agricultural" or any(token in subtype for token in ("barn", "silo", "stable")):
        return "agricultural_outbuilding", 2
    return None, 0


def assessor_semantic(assessor: dict | None) -> str | None:
    if not assessor:
        return None
    text = " ".join(str(assessor.get(key) or "") for key in
        ("design", "building_class", "construction")).lower()
    if any(token in text for token in ("warehouse", "manufactur", "industrial", "ind flex")):
        return "industrial"
    if any(token in text for token in ("office", "retail", "restaurant", "lodging", "medical", "bank")):
        return "commercial"
    if any(token in text for token in ("school", "university", "worship", "church", "government", "political sub")):
        return "institutional"
    if any(token in text for token in ("barn", "farm", "stable", "cattle", "equipment shed")):
        return "agricultural_outbuilding"
    if any(token in text for token in ("residential", "dwelling", "single family", "multi family", "townhome", "condo")):
        return "residential"
    return None


def fallback_semantic(building: dict) -> str:
    cls = int(building["class_id"])
    area = float(building["area_m2"])
    if cls == 0:
        return "residential"
    if cls == 1:
        return "commercial"
    if cls == 2:
        # The V1 source classifier placed every Garage/Shed in industrial.
        # Small records without independent industrial evidence are accessories.
        return "industrial" if area >= 220.0 else "accessory"
    if cls == 3:
        return "institutional"
    if cls == 4:
        return "utility"
    if area >= 900.0 and int(building["roof_id"]) == 0:
        return "commercial"
    return "generic"


def region_for(point, semantic: str) -> int:
    x, z = float(point.x), float(point.y)
    if math.hypot(x + 3399.2, z + 3526.3) <= 1350.0:
        return 3
    if math.hypot(x + 3791.5, z + 2704.5) <= 1450.0:
        return 2
    if math.hypot(x, z) <= 1750.0:
        return 6
    if -2100.0 <= x <= 1150.0 and -4300.0 <= z <= -750.0:
        return 5
    if semantic == "commercial":
        return 4
    if semantic in ("residential", "accessory"):
        return 1
    return 0


def material_from_assessor(assessor: dict | None, seed: int) -> tuple[int, int, int, int] | None:
    if not assessor:
        return None
    primary_key = clean(assessor.get("primary")).lower()
    if primary_key in EXACT_MATERIAL:
        primary = stable_pick(seed, EXACT_MATERIAL[primary_key], 11)
        secondary = -1
        secondary_key = clean(assessor.get("secondary")).lower()
        if secondary_key in EXACT_MATERIAL:
            candidate = stable_pick(seed, EXACT_MATERIAL[secondary_key], 17)
            # The shader places secondary material at the lower metre. Permit
            # only believable masonry/block bases, never arbitrary mixed walls.
            if candidate != primary and FAMILY[candidate] in ("masonry", "block"):
                secondary = candidate
        return primary, secondary, 2, 2
    construction = clean(assessor.get("construction")).lower()
    for key, values in CONSTRUCTION_MATERIAL.items():
        if key in construction:
            return stable_pick(seed, values, 23), -1, 1, 1
    return None


def fallback_palette(semantic: str, region: int) -> tuple[int, ...]:
    if semantic == "residential":
        if region in (2, 3):
            return (3, 3, 4, 5, 6, 7, 12, 12, 0, 2)
        if region == 0:
            return (0, 0, 1, 2, 5, 6, 7, 12, 15, 15)
        return (0, 0, 0, 2, 2, 3, 4, 5, 6, 7, 12, 15)
    if semantic == "commercial":
        return (3, 4, 5, 6, 7, 9, 9, 12, 13)
    if semantic == "industrial":
        return (8, 9, 9, 10, 10, 11, 13, 14)
    if semantic == "institutional":
        return (3, 4, 5, 7, 9, 12, 12)
    if semantic == "utility":
        return (8, 10, 13, 14)
    if semantic in ("agricultural_outbuilding", "industrial_accessory"):
        return (1, 8, 10, 11, 14, 15)
    if semantic == "commercial_accessory":
        return (6, 8, 9, 10, 13, 14)
    if semantic == "residential_accessory":
        return (0, 1, 2, 6, 7, 15)
    return (0, 2, 3, 5, 6, 7, 8, 9, 12, 13)


def style_for(semantic: str) -> int:
    if semantic in ("residential", "residential_accessory"):
        return 0 if semantic == "residential" else 5
    if semantic in ("commercial", "commercial_accessory"):
        return 1 if semantic == "commercial" else 4
    if semantic in ("industrial", "industrial_accessory"):
        return 2
    if semantic == "institutional":
        return 3
    if semantic == "utility":
        return 4
    return 5


def load_parcels(path: pathlib.Path, config: dict) -> tuple[list, list[dict], STRtree]:
    source = json.loads(path.read_text(encoding="utf-8"))
    lat, lon = config["center_wgs84"]
    local = CRS.from_proj4(
        f"+proj=aeqd +lat_0={lat} +lon_0={lon} +datum=WGS84 +units=m +no_defs")
    project = Transformer.from_crs(CRS.from_epsg(4326), local, always_xy=True).transform
    geometries, properties = [], []
    for feature in source.get("features", []):
        geometry = transform(project, shape(feature["geometry"]))
        if not geometry.is_empty:
            geometries.append(geometry)
            properties.append(feature.get("properties") or {})
    return geometries, properties, STRtree(geometries)


def enrich(args: argparse.Namespace) -> dict:
    config = json.loads(args.config.read_text(encoding="utf-8"))
    database = json.loads(args.database.read_text(encoding="utf-8"))
    before_hash = geometry_hash(database)
    sources = footprint_properties(args.city_footprints, args.county_footprints)
    parcel_geometries, parcel_properties, parcel_tree = load_parcels(args.parcels, config)
    assessor_by_account = read_assessor(args.buildings_csv)

    records: list[Record] = []
    parcel_groups: dict[int, list[Record]] = defaultdict(list)
    for building, polygon in database_records(database):
        point = polygon.representative_point()
        parcel_index = None
        parcel = None
        for raw_index in parcel_tree.query(point):
            index = int(raw_index)
            if parcel_geometries[index].covers(point):
                parcel_index = index
                parcel = parcel_properties[index]
                break
        assessor = assessor_by_account.get(clean(parcel.get("AccountNo"))) if parcel else None
        source = sources.get(str(building["source_id"]))
        semantic, confidence = source_semantic(source)
        if semantic is None:
            semantic = assessor_semantic(assessor)
            confidence = 1 if semantic else 0
        if semantic is None:
            semantic = fallback_semantic(building)
        record = Record(building, polygon, parcel_index, parcel, assessor, source,
                        semantic, confidence, region_for(point, semantic))
        records.append(record)
        if parcel_index is not None:
            parcel_groups[parcel_index].append(record)

    primary_by_parcel: dict[int, Record] = {}
    for parcel_index, group in parcel_groups.items():
        candidates = [record for record in group if record.semantic != "accessory"] or group
        primary = max(candidates, key=lambda record: float(record.building["area_m2"]))
        primary.parcel_role = 1
        primary_by_parcel[parcel_index] = primary
        for record in group:
            if record is not primary:
                record.parcel_role = 2

    # Resolve accessory semantics from their actual parcel primary rather than
    # the V1 Garage/Shed => industrial shortcut.
    for record in records:
        if record.semantic != "accessory":
            continue
        primary = primary_by_parcel.get(record.parcel_index) if record.parcel_index is not None else None
        parent = primary.semantic if primary and primary is not record else "generic"
        if parent == "residential":
            record.semantic = "residential_accessory"
        elif parent == "commercial":
            record.semantic = "commercial_accessory"
        elif parent == "industrial":
            record.semantic = "industrial_accessory"
        else:
            record.semantic = "agricultural_outbuilding"
        record.region = region_for(record.polygon.representative_point(), record.semantic)

    # Material assignment is two-pass so accessories can inherit a compatible
    # parcel-primary material deterministically.
    assigned: dict[int, tuple[int, int, int, int]] = {}
    for record in records:
        if record.semantic.endswith("_accessory") or record.semantic == "agricultural_outbuilding":
            continue
        seed = int(record.building["seed"])
        evidence = material_from_assessor(record.assessor, seed)
        if evidence is None:
            material = stable_pick(seed, fallback_palette(record.semantic, record.region), 29)
            rule = 4 if record.semantic_confidence > 0 else 0
            evidence = (material, -1, 0, rule)
        assigned[id(record)] = evidence

    for record in records:
        if id(record) in assigned:
            continue
        seed = int(record.building["seed"])
        primary = primary_by_parcel.get(record.parcel_index) if record.parcel_index is not None else None
        inherited = assigned.get(id(primary)) if primary and primary is not record else None
        if inherited is not None and record.semantic == "residential_accessory":
            candidate = inherited[0]
            if FAMILY[candidate] in ("siding", "stucco", "masonry"):
                assigned[id(record)] = (candidate, -1, 1, 3)
                continue
        assigned[id(record)] = (
            stable_pick(seed, fallback_palette(record.semantic, record.region), 37), -1, 0,
            4 if record.semantic_confidence > 0 else 0,
        )

    material_counts, style_counts = Counter(), Counter()
    semantic_counts, region_counts, rule_counts = Counter(), Counter(), Counter()
    confidence_counts, parcel_role_counts = Counter(), Counter()
    page_counts: dict[str, Counter] = defaultdict(Counter)
    small_false_industrial = 0
    for record in records:
        building = record.building
        primary, secondary, confidence, rule = assigned[id(record)]
        semantic_id = SEMANTIC_NAMES.index(record.semantic)
        style = style_for(record.semantic)
        stories = record.assessor["stories"] if record.assessor and record.assessor["stories"] > 0 else max(
            1.0, min(20.0, int(building["height_dm"]) * 0.1 / 3.15))
        building["material_id"] = int(primary)
        building["secondary_material_id"] = int(secondary)
        building["material_confidence"] = int(confidence)
        building["facade_style_id"] = int(style)
        building["stories_x2"] = int(max(2, min(40, round(stories * 2.0))))
        building["built_year"] = int(record.assessor["built_year"] if record.assessor else 0)
        building["semantic_class_id"] = semantic_id
        building["semantic_region_id"] = int(record.region)
        building["semantic_confidence"] = int(record.semantic_confidence)
        building["parcel_role_id"] = int(record.parcel_role)
        building["material_rule_id"] = int(rule)
        material_counts[MATERIAL_NAMES[primary]] += 1
        style_counts[str(style)] += 1
        semantic_counts[record.semantic] += 1
        region_counts[REGION_NAMES[record.region]] += 1
        rule_counts[RULE_NAMES[rule]] += 1
        confidence_counts[str(confidence)] += 1
        parcel_role_counts[str(record.parcel_role)] += 1
        center = record.polygon.representative_point()
        extent = float(database["map_half_extent_meters"])
        size = float(database["page_size_meters"])
        page_key = f"{int((center.x + extent) // size):02d}_{int((center.y + extent) // size):02d}"
        page_counts[page_key][MATERIAL_NAMES[primary]] += 1
        if record.semantic == "industrial" and float(building["area_m2"]) < 120.0 and \
                source_semantic(record.source)[0] == "accessory":
            small_false_industrial += 1

    database["facade_contract_version"] = "boulder-semantic-facades-v2"
    after_hash = geometry_hash(database)
    if before_hash != after_hash:
        raise RuntimeError("Facade enrichment changed geometry-bearing fields")
    if len(records) != 57407:
        raise RuntimeError(f"Expected 57,407 buildings, found {len(records):,}")
    if small_false_industrial:
        raise RuntimeError(f"{small_false_industrial} Garage/Shed records remain false industrial classifications")
    if any(not 0 <= int(record.building["material_id"]) < len(MATERIAL_NAMES) for record in records):
        raise RuntimeError("Facade material assignment is incomplete")

    args.output.write_text(json.dumps(database, separators=(",", ":")), encoding="utf-8")
    report = {
        "facade_contract_version": database["facade_contract_version"],
        "building_count": len(records),
        "page_count": len(database["pages"]),
        "geometry_hash_before": before_hash,
        "geometry_hash_after": after_hash,
        "parcel_feature_count": len(parcel_geometries),
        "parcel_match_count": sum(1 for record in records if record.parcel is not None),
        "assessor_match_count": sum(1 for record in records if record.assessor is not None),
        "small_false_industrial_count": small_false_industrial,
        "blue_facade_count": 0,
        "material_counts": dict(sorted(material_counts.items())),
        "style_counts": dict(sorted(style_counts.items())),
        "semantic_counts": dict(sorted(semantic_counts.items())),
        "region_counts": dict(sorted(region_counts.items())),
        "rule_counts": dict(sorted(rule_counts.items())),
        "material_confidence_counts": dict(sorted(confidence_counts.items())),
        "parcel_role_counts": dict(sorted(parcel_role_counts.items())),
        "page_material_counts": {key: dict(sorted(value.items())) for key, value in sorted(page_counts.items())},
        "material_names": MATERIAL_NAMES,
        "semantic_names": SEMANTIC_NAMES,
        "region_names": REGION_NAMES,
        "rule_names": RULE_NAMES,
        "sources": {
            "city_footprints_sha256": sha256(args.city_footprints),
            "county_footprints_sha256": sha256(args.county_footprints),
            "assessor_buildings_url": "https://assessor.boco.solutions/ASR_PublicDataFiles/Buildings.csv",
            "assessor_buildings_sha256": sha256(args.buildings_csv),
            "parcels_url": PARCEL_SERVICE,
            "parcels_sha256": sha256(args.parcels),
        },
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--database", type=pathlib.Path, required=True)
    parser.add_argument("--city-footprints", type=pathlib.Path, required=True)
    parser.add_argument("--county-footprints", type=pathlib.Path, required=True)
    parser.add_argument("--buildings-csv", type=pathlib.Path, required=True)
    parser.add_argument("--parcels", type=pathlib.Path, required=True)
    parser.add_argument("--config", type=pathlib.Path,
                        default=pathlib.Path(__file__).with_name("config.json"))
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--report", type=pathlib.Path, required=True)
    args = parser.parse_args()
    print(json.dumps(enrich(args), indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
