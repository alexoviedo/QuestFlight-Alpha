#!/usr/bin/env python3
"""Enrich the compact building database with parcel-grounded facade semantics.

Raw Assessor tables and parcel geometry remain external.  The committed output
contains only compact material/style identifiers and confidence codes.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import pathlib
import time
from collections import Counter, defaultdict

import requests
from pyproj import CRS, Transformer
from shapely.geometry import Point, Polygon, shape
from shapely.strtree import STRtree


PARCEL_SERVICE = (
    "https://maps.bouldercounty.org/arcgis/rest/services/"
    "PARCELS/PARCELS_OWNER/FeatureServer/0/query"
)

MATERIAL_NAMES = [
    "painted_wood_siding",
    "gray_wood_siding",
    "cement_board",
    "red_brick",
    "tan_brick",
    "beige_stucco",
    "gray_stucco",
    "concrete_block",
    "precast_concrete",
    "white_corrugated_metal",
    "blue_corrugated_metal",
    "moss_rock_stone",
    "painted_block",
    "dark_utility_metal",
    "cedar_or_log",
    "light_vinyl_siding",
]

EXACT_MATERIAL = {
    "frame wood/shake": (0, 1),
    "brick veneer": (3, 4),
    "frame stucco": (5, 6),
    "cement board": (2,),
    "brick on block": (3, 4),
    "moss rock/flagstone": (11,),
    "log": (14,),
    "block stucco": (5, 6),
    "vinyl": (15,),
    "faux stone": (11,),
    "metal": (9, 10, 13),
    "painted block": (12,),
    "cedar": (14,),
    "frame asbestos": (2, 15),
    "strawbale": (5,),
}

CONSTRUCTION_MATERIAL = {
    "masonry": (3, 4, 7),
    "brick": (3, 4),
    "block": (7, 12),
    "steel": (9, 10, 13),
    "precast": (8,),
    "concrete": (7, 8, 12),
    "tees/tilt": (8,),
    "moss rock": (11,),
    "veneer": (3, 4, 11),
    "wood": (0, 1, 14),
}

FALLBACK_BY_CLASS = {
    0: (0, 0, 1, 2, 2, 3, 5, 15),
    1: (3, 4, 5, 7, 8),
    2: (7, 8, 9, 9, 10, 12, 13),
    3: (3, 4, 5, 8, 11),
    4: (7, 9, 12, 13),
    5: (0, 2, 3, 5, 7, 8, 9, 12),
}


def stable_index(seed: int, values: tuple[int, ...], salt: int = 0) -> int:
    mixed = (int(seed) ^ (salt * 0x9E3779B1)) & 0xFFFFFFFF
    return values[mixed % len(values)]


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def source_bbox(config: dict) -> tuple[float, float, float, float]:
    lat, lon = config["center_wgs84"]
    local = CRS.from_proj4(
        f"+proj=aeqd +lat_0={lat} +lon_0={lon} +datum=WGS84 +units=m +no_defs"
    )
    to_wgs = Transformer.from_crs(local, CRS.from_epsg(4326), always_xy=True)
    extent = float(config["map_half_extent_meters"]) + 500.0
    points = [to_wgs.transform(x, z) for x in (-extent, extent) for z in (-extent, extent)]
    return (
        min(point[0] for point in points),
        min(point[1] for point in points),
        max(point[0] for point in points),
        max(point[1] for point in points),
    )


def fetch_parcels(config: dict, output: pathlib.Path) -> pathlib.Path:
    if output.exists():
        return output
    bbox = source_bbox(config)
    features: list[dict] = []
    offset = 0
    session = requests.Session()
    while True:
        params = {
            "f": "geojson",
            "where": "1=1",
            "outFields": "OBJECTID,AccountNo,ParcelNo,Nbhd,StrCity,SubCode,SubDscr",
            "returnGeometry": "true",
            "geometry": ",".join(f"{value:.9f}" for value in bbox),
            "geometryType": "esriGeometryEnvelope",
            "inSR": "4326",
            "outSR": "4326",
            "spatialRel": "esriSpatialRelIntersects",
            "resultOffset": str(offset),
            "resultRecordCount": "2000",
            "orderByFields": "OBJECTID ASC",
        }
        response = session.get(PARCEL_SERVICE, params=params, timeout=120)
        response.raise_for_status()
        page = response.json()
        if "error" in page:
            raise RuntimeError(json.dumps(page["error"], indent=2))
        batch = page.get("features", [])
        features.extend(batch)
        if len(batch) < 2000:
            break
        offset += len(batch)
        print(f"parcel features: {offset}", flush=True)
        time.sleep(0.03)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(
        json.dumps({"type": "FeatureCollection", "features": features}, separators=(",", ":")),
        encoding="utf-8",
    )
    return output


def clean(value: str | None) -> str:
    return (value or "").strip()


def mode(rows: list[dict], field: str) -> str:
    values = Counter(clean(row.get(field)) for row in rows if clean(row.get(field)))
    return values.most_common(1)[0][0] if values else ""


def number_mode(rows: list[dict], field: str) -> float:
    values: list[float] = []
    for row in rows:
        try:
            value = float(clean(row.get(field)))
        except ValueError:
            continue
        if value > 0:
            values.append(value)
    return float(Counter(values).most_common(1)[0][0]) if values else 0.0


def read_assessor(path: pathlib.Path) -> dict[str, dict]:
    grouped: dict[str, list[dict]] = defaultdict(list)
    with path.open("r", encoding="utf-8-sig", newline="") as stream:
        for row in csv.DictReader(stream):
            if clean(row.get("status_cd")) != "A":
                continue
            grouped[clean(row.get("strap"))].append(row)
    result = {}
    for account, rows in grouped.items():
        result[account] = {
            "primary": mode(rows, "ExtWallDscrPrim"),
            "secondary": mode(rows, "ExtWallDscrSec"),
            "construction": mode(rows, "ConstCodeDscr"),
            "design": mode(rows, "designCodeDscr"),
            "building_class": mode(rows, "bldgClassDscr"),
            "stories": number_mode(rows, "Stories"),
            "built_year": int(number_mode(rows, "builtYear")),
        }
    return result


def choose(values: tuple[int, ...], seed: int, salt: int) -> int:
    return stable_index(seed, values, salt)


def material_for(building: dict, assessor: dict | None) -> tuple[int, int, int]:
    seed = int(building["seed"])
    if assessor:
        primary_key = assessor["primary"].lower()
        secondary_key = assessor["secondary"].lower()
        if primary_key in EXACT_MATERIAL:
            primary = choose(EXACT_MATERIAL[primary_key], seed, 11)
            secondary = choose(EXACT_MATERIAL[secondary_key], seed, 17) if secondary_key in EXACT_MATERIAL else -1
            if secondary == primary:
                secondary = -1
            return primary, secondary, 2
        construction_key = assessor["construction"].lower()
        if construction_key in CONSTRUCTION_MATERIAL:
            return choose(CONSTRUCTION_MATERIAL[construction_key], seed, 23), -1, 1
    cls = int(building["class_id"])
    # Large flat generic shells are commercial/industrial more often than houses.
    if cls == 5 and (int(building["roof_id"]) == 0 or float(building["area_m2"]) >= 450.0):
        values = (7, 8, 9, 12)
    else:
        values = FALLBACK_BY_CLASS.get(cls, FALLBACK_BY_CLASS[5])
    return choose(values, seed, 29), -1, 0


def style_for(building: dict, assessor: dict | None) -> int:
    cls = int(building["class_id"])
    text = ""
    if assessor:
        text = f"{assessor['design']} {assessor['building_class']}".lower()
    if any(token in text for token in ("warehouse", "manufactur", "ind flex", "utility building")):
        return 2
    if any(token in text for token in ("office", "retail", "restaurant", "lodging", "medical", "bank")):
        return 1
    if any(token in text for token in ("school", "worship", "church", "government", "political sub")):
        return 3
    if any(token in text for token in ("barn", "farm", "stable", "cattle", "equipment shed")):
        return 5
    return min(5, max(0, cls))


def database_polygons(database: dict) -> list[tuple[dict, Polygon]]:
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


def enrich(args: argparse.Namespace) -> dict:
    config = json.loads(args.config.read_text(encoding="utf-8"))
    database = json.loads(args.database.read_text(encoding="utf-8"))
    parcel_path = fetch_parcels(config, args.parcels)
    parcel_source = json.loads(parcel_path.read_text(encoding="utf-8"))
    lat, lon = config["center_wgs84"]
    local = CRS.from_proj4(
        f"+proj=aeqd +lat_0={lat} +lon_0={lon} +datum=WGS84 +units=m +no_defs"
    )
    project = Transformer.from_crs(CRS.from_epsg(4326), local, always_xy=True).transform
    from shapely.ops import transform

    parcel_geometries: list[Polygon] = []
    parcel_properties: list[dict] = []
    for feature in parcel_source.get("features", []):
        geometry = transform(project, shape(feature["geometry"]))
        if geometry.is_empty:
            continue
        parcel_geometries.append(geometry)
        parcel_properties.append(feature.get("properties") or {})
    tree = STRtree(parcel_geometries)
    assessor_by_account = read_assessor(args.buildings_csv)

    confidence_counts = Counter()
    material_counts = Counter()
    style_counts = Counter()
    matched_parcels = 0
    matched_assessor = 0
    unmatched = 0
    for building, polygon in database_polygons(database):
        point = polygon.representative_point()
        candidates = tree.query(point)
        parcel = None
        for raw_index in candidates:
            index = int(raw_index)
            if parcel_geometries[index].covers(point):
                parcel = parcel_properties[index]
                break
        assessor = None
        if parcel:
            matched_parcels += 1
            assessor = assessor_by_account.get(clean(parcel.get("AccountNo")))
            if assessor:
                matched_assessor += 1
        else:
            unmatched += 1
        primary, secondary, confidence = material_for(building, assessor)
        style = style_for(building, assessor)
        stories = assessor["stories"] if assessor and assessor["stories"] > 0 else max(
            1.0, min(20.0, int(building["height_dm"]) * 0.1 / 3.15)
        )
        building["material_id"] = int(primary)
        building["secondary_material_id"] = int(secondary)
        building["material_confidence"] = int(confidence)
        building["facade_style_id"] = int(style)
        building["stories_x2"] = int(max(2, min(40, round(stories * 2.0))))
        building["built_year"] = int(assessor["built_year"] if assessor else 0)
        confidence_counts[confidence] += 1
        material_counts[MATERIAL_NAMES[primary]] += 1
        style_counts[style] += 1

    database["facade_contract_version"] = "parcel-grounded-facades-v1"
    args.output.write_text(json.dumps(database, separators=(",", ":")), encoding="utf-8")
    report = {
        "facade_contract_version": database["facade_contract_version"],
        "building_count": sum(len(page.get("buildings", [])) for page in database["pages"]),
        "parcel_feature_count": len(parcel_geometries),
        "parcel_match_count": matched_parcels,
        "assessor_match_count": matched_assessor,
        "unmatched_parcel_count": unmatched,
        "confidence_counts": {
            "inferred": confidence_counts[0],
            "parcel_construction": confidence_counts[1],
            "authoritative_exterior": confidence_counts[2],
        },
        "material_counts": dict(sorted(material_counts.items())),
        "style_counts": dict(sorted(style_counts.items())),
        "sources": {
            "assessor_buildings_url": "https://assessor.boco.solutions/ASR_PublicDataFiles/Buildings.csv",
            "assessor_buildings_sha256": sha256(args.buildings_csv),
            "parcels_url": PARCEL_SERVICE,
            "parcels_sha256": sha256(parcel_path),
        },
        "material_names": MATERIAL_NAMES,
        "confidence_contract": {
            "0": "semantic inference",
            "1": "parcel construction evidence",
            "2": "assessor exterior-wall evidence",
        },
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--database", type=pathlib.Path, required=True)
    parser.add_argument("--buildings-csv", type=pathlib.Path, required=True)
    parser.add_argument("--parcels", type=pathlib.Path, required=True)
    parser.add_argument("--config", type=pathlib.Path, default=pathlib.Path("tools/full_map_buildings/config.json"))
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--report", type=pathlib.Path, required=True)
    args = parser.parse_args()
    report = enrich(args)
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
