#!/usr/bin/env python3
"""Fetch and compile City building footprints into compact aircraft-centered pages."""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import math
import pathlib
import struct
import sys
import time
from collections import Counter, defaultdict
from datetime import datetime, timezone

import requests
from pyproj import CRS, Transformer
from shapely.geometry import MultiPolygon, Polygon, shape
from shapely.geometry.polygon import orient
from shapely.strtree import STRtree


def load_json(path: pathlib.Path):
    return json.loads(path.read_text(encoding="utf-8"))


def sha256(path: pathlib.Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def stable_u32(text: str) -> int:
    return int.from_bytes(hashlib.sha256(text.encode("utf-8")).digest()[:4], "little")


def compact_json(path: pathlib.Path, value) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, separators=(",", ":"), ensure_ascii=False), encoding="utf-8")


def source_bbox(config: dict) -> tuple[float, float, float, float]:
    lat, lon = config["center_wgs84"]
    local = CRS.from_proj4(f"+proj=aeqd +lat_0={lat} +lon_0={lon} +datum=WGS84 +units=m +no_defs")
    to_wgs = Transformer.from_crs(local, CRS.from_epsg(4326), always_xy=True)
    extent = float(config["map_half_extent_meters"]) + 1200.0
    points = [to_wgs.transform(x, z) for x in (-extent, extent) for z in (-extent, extent)]
    return min(p[0] for p in points), min(p[1] for p in points), max(p[0] for p in points), max(p[1] for p in points)


def fetch_arcgis(config: dict, raw_dir: pathlib.Path, key: str, filename: str, page_size: int) -> pathlib.Path:
    raw_dir.mkdir(parents=True, exist_ok=True)
    raw_path = raw_dir / filename
    if raw_path.exists():
        return raw_path
    bbox = source_bbox(config)
    features = []
    offset = 0
    session = requests.Session()
    while True:
        params = {
            "f": "geojson",
            "where": config[f"{key}_where"],
            "outFields": ",".join(config[f"{key}_out_fields"]),
            "returnGeometry": "true",
            "geometry": ",".join(f"{v:.9f}" for v in bbox),
            "geometryType": "esriGeometryEnvelope",
            "inSR": "4326",
            "outSR": "4326",
            "spatialRel": "esriSpatialRelIntersects",
            "resultOffset": str(offset),
            "resultRecordCount": str(page_size),
            "orderByFields": "OBJECTID ASC",
        }
        response = session.get(config[f"{key}_service_url"], params=params, timeout=120)
        response.raise_for_status()
        page = response.json()
        if "error" in page:
            raise RuntimeError(json.dumps(page["error"], indent=2))
        batch = page.get("features", [])
        features.extend(batch)
        if len(batch) < page_size:
            break
        offset += len(batch)
        time.sleep(0.05)
    payload = {
        "type": "FeatureCollection",
        "name": f"{key.title()} building footprints bounded for QuestFlight",
        "features": features,
    }
    compact_json(raw_path, payload)
    return raw_path


def semantic_class(props: dict) -> str:
    text = " ".join(str(props.get(k) or "") for k in ("BLDGTYPE", "SUBTYPE", "SHORTNAME", "LONGNAME")).lower()
    if any(t in text for t in ("house", "residen", "dwelling", "apartment", "townhome", "duplex", "condo")):
        return "residential"
    if any(t in text for t in ("industrial", "warehouse", "hangar", "storage", "manufactur", "garage")):
        return "industrial"
    if any(t in text for t in ("school", "university", "hospital", "church", "government", "recreation", "public")):
        return "institutional"
    if any(t in text for t in ("utility", "shed", "outbuilding", "tank", "pump")):
        return "utility"
    if any(t in text for t in ("commercial", "office", "retail", "restaurant", "hotel")):
        return "commercial"
    return "generic"


def roof_profile(building_class: str, polygon: Polygon, seed: int) -> str:
    rectangle_likeness = polygon.area / max(polygon.minimum_rotated_rectangle.area, 0.001)
    coords = list(polygon.exterior.coords)[:-1]
    if building_class == "residential" and rectangle_likeness > 0.70:
        return "hip" if seed % 5 == 0 else "gable"
    if building_class == "industrial" and rectangle_likeness > 0.78 and seed % 4 == 0:
        return "gable"
    if building_class == "institutional" and rectangle_likeness > 0.82 and seed % 7 == 0:
        return "hip"
    if len(coords) <= 8 and building_class == "generic" and seed % 9 == 0:
        return "gable"
    return "flat"


def default_height(config: dict, cls: str, area: float, props: dict) -> tuple[float, str]:
    raw = props.get("BLDGHEIGHT")
    if isinstance(raw, (int, float)) and raw > 0:
        value = float(raw) * 0.3048
        low = float(config["height_contract"]["minimum_meters"])
        high = float(config["height_contract"]["maximum_context_meters"])
        if low <= value <= high:
            return value, "city_height_feet"
    floors = props.get("FLOORCOUNT")
    if isinstance(floors, (int, float)) and floors > 0:
        return min(90.0, max(2.7, float(floors) * 3.15)), "city_floor_count"
    base = float(config["height_contract"]["fallback_by_class_meters"].get(cls, 6.5))
    return base + min(2.5, max(0.0, math.log10(max(area, 20.0) / 80.0))), "semantic_fallback"


def normalized_polygons(geometry) -> list[Polygon]:
    candidate = shape(geometry)
    if not candidate.is_valid:
        candidate = candidate.buffer(0)
    if isinstance(candidate, Polygon):
        values = [candidate]
    elif isinstance(candidate, MultiPolygon):
        values = list(candidate.geoms)
    else:
        values = []
    return [orient(p, sign=1.0) for p in values if not p.is_empty]


def simplify_ring(polygon: Polygon, tolerance: float) -> Polygon:
    simplified = polygon.simplify(tolerance, preserve_topology=True)
    if not isinstance(simplified, Polygon) or simplified.is_empty or len(simplified.exterior.coords) < 4:
        return polygon
    area_error = abs(simplified.area - polygon.area) / max(polygon.area, 0.001)
    return simplified if area_error <= 0.12 else polygon


def quantized_ring(polygon: Polygon, page_min_x: float, page_min_z: float, quantum: float) -> list[int]:
    result = []
    for x, z in list(polygon.exterior.coords)[:-1]:
        result.extend((round((x - page_min_x) / quantum), round((z - page_min_z) / quantum)))
    return result


class TerrainSurfaceSampler:
    """Bilinear sampler for the retained USGS terrain surfaces already used by the product."""

    def __init__(self, source_path: pathlib.Path):
        source = load_json(source_path)
        self.layers = []
        for layer in source["layers"]:
            width = int(layer["width"])
            height = int(layer["height"])
            packed = base64.b64decode(layer["height_cm_little_endian_base64"])
            values = struct.unpack(f"<{width * height}h", packed)
            self.layers.append({
                "id": layer["id"],
                "width": width,
                "height": height,
                "min": tuple(float(value) for value in layer["minimum_enu_meters"]),
                "max": tuple(float(value) for value in layer["maximum_enu_meters"]),
                "spacing": float(layer["spacing_meters"]),
                "quantum": float(layer["height_quantization_meters"]),
                "values": values,
            })
        self.layers.sort(key=lambda value: value["spacing"])

    def sample(self, x: float, z: float) -> tuple[float, str]:
        for layer in self.layers:
            if layer["min"][0] <= x <= layer["max"][0] and layer["min"][1] <= z <= layer["max"][1]:
                fx = (x - layer["min"][0]) / layer["spacing"]
                fz = (z - layer["min"][1]) / layer["spacing"]
                x0 = min(layer["width"] - 1, max(0, int(math.floor(fx))))
                z0 = min(layer["height"] - 1, max(0, int(math.floor(fz))))
                x1 = min(layer["width"] - 1, x0 + 1)
                z1 = min(layer["height"] - 1, z0 + 1)
                tx = min(1.0, max(0.0, fx - x0))
                tz = min(1.0, max(0.0, fz - z0))
                def at(ix: int, iz: int) -> float:
                    return layer["values"][iz * layer["width"] + ix] * layer["quantum"]
                lower = at(x0, z0) * (1.0 - tx) + at(x1, z0) * tx
                upper = at(x0, z1) * (1.0 - tx) + at(x1, z1) * tx
                return lower * (1.0 - tz) + upper * tz, layer["id"]
        raise ValueError(f"No retained terrain layer covers building position ({x:.2f}, {z:.2f}).")


def compile_database(config: dict, raw_paths: list[tuple[str, pathlib.Path]]) -> tuple[dict, dict]:
    lat, lon = config["center_wgs84"]
    local = CRS.from_proj4(f"+proj=aeqd +lat_0={lat} +lon_0={lon} +datum=WGS84 +units=m +no_defs")
    project = Transformer.from_crs(CRS.from_epsg(4326), local, always_xy=True).transform
    from shapely.ops import transform

    extent = float(config["map_half_extent_meters"])
    size = float(config["page_size_meters"])
    cols = rows = int(round(extent * 2 / size))
    quantum = float(config["quantization_meters"])
    area_min = float(config["roof_contract"]["minimum_component_area_square_meters"])
    sources = [(name, load_json(path), path) for name, path in raw_paths]
    pages: dict[tuple[int, int], list[dict]] = defaultdict(list)
    excluded_post_2020 = 0
    excluded_outside = 0
    excluded_small = 0
    excluded_authored_hero = 0
    class_counts = Counter()
    roof_counts = Counter()
    height_counts = Counter()
    landmark_tokens = [value.upper() for value in config["landmark_name_tokens"]]
    landmark_records = []
    next_id = 0
    accepted_city_geometries = []
    city_tree = None
    source_counts = Counter()
    base_height_counts = Counter()
    excluded_overlap = 0
    terrain_source_path = pathlib.Path(config["terrain_surface_path"])
    terrain = TerrainSurfaceSampler(terrain_source_path)

    for source_name, source, _ in sources:
      if source_name == "county" and accepted_city_geometries:
        city_tree = STRtree(accepted_city_geometries)
      for feature in source.get("features", []):
        props = dict(feature.get("properties") or {})
        if source_name == "county":
            # StructureI is a parcel-scale identifier and is not unique for every
            # separately surveyed structure. Preserve both it and the ArcGIS row.
            props["FACILITYID"] = "COUNTY:" + str(props.get("StructureI") or "UNASSIGNED") + ":" + str(props.get("OBJECTID"))
            props["BLDGTYPE"] = props.get("StructureT") or "generic"
            props["BLDGHEIGHT"] = props.get("Height")
            props["GROUNDELEV"] = props.get("BaseElevat")
        year = props.get("BUILDYEAR")
        if isinstance(year, (int, float)) and year > int(config["imagery_authority_year"]):
            excluded_post_2020 += 1
            continue
        for component_index, source_poly in enumerate(normalized_polygons(feature.get("geometry"))):
            poly = transform(project, source_poly)
            if poly.area < area_min:
                excluded_small += 1
                continue
            center = poly.representative_point()
            if not (-extent <= center.x < extent and -extent <= center.y < extent):
                excluded_outside += 1
                continue
            hero = config["authored_hero_exclusion_bounds_xz"]
            if hero[0] <= center.x <= hero[2] and hero[1] <= center.y <= hero[3]:
                excluded_authored_hero += 1
                continue
            if source_name == "county" and city_tree is not None and any(
                accepted_city_geometries[int(index)].intersection(poly).area / max(poly.area, 0.001) > 0.30
                for index in city_tree.query(poly)
            ):
                excluded_overlap += 1
                continue
            if source_name == "city":
                accepted_city_geometries.append(poly)
            col = min(cols - 1, max(0, int((center.x + extent) // size)))
            row = min(rows - 1, max(0, int((center.y + extent) // size)))
            page_min_x = -extent + col * size
            page_min_z = -extent + row * size
            cls = semantic_class(props)
            identity = str(props.get("FACILITYID") or props.get("DRCOGID") or props.get("OBJECTID") or next_id)
            seed = stable_u32(f"{identity}:{component_index}")
            profile = roof_profile(cls, poly, seed)
            height, height_source = default_height(config, cls, poly.area, props)
            rectangle = poly.minimum_rotated_rectangle
            rect_coords = list(rectangle.exterior.coords)[:-1]
            spans = [math.dist(rect_coords[i], rect_coords[(i + 1) % 4]) for i in range(4)]
            short_span = max(1.0, min(spans))
            ratio = config["roof_contract"]["residential_pitch_rise_ratio"] if cls == "residential" else config["roof_contract"]["industrial_pitch_rise_ratio"]
            roof_rise = 0.0 if profile == "flat" else min(float(config["roof_contract"]["maximum_rise_meters"]), max(float(config["roof_contract"]["minimum_rise_meters"]), short_span * float(ratio)))
            ground = props.get("GROUNDELEV")
            if isinstance(ground, (int, float)) and ground > 4000:
                base_y = float(ground) * 0.3048 - float(config["origin_elevation_meters"])
                base_source = f"{source_name}_source_ground_feet"
            else:
                base_y, terrain_layer = terrain.sample(center.x, center.y)
                base_source = f"retained_usgs_terrain:{terrain_layer}"
            name = " ".join(str(props.get(k) or "") for k in ("SHORTNAME", "LONGNAME")).strip()
            landmark = any(token in name.upper() for token in landmark_tokens)
            lod0 = simplify_ring(poly, 0.08)
            lod1 = simplify_ring(poly, float(config["roof_contract"]["lod1_simplification_meters"]))
            lod2 = simplify_ring(poly, float(config["roof_contract"]["lod2_simplification_meters"]))
            record = {
                "id": next_id,
                "source_id": identity,
                "component": component_index,
                "class_id": {"residential": 0, "commercial": 1, "industrial": 2, "institutional": 3, "utility": 4, "generic": 5}[cls],
                "roof_id": {"flat": 0, "gable": 1, "hip": 2}[profile],
                "height_dm": round(height * 10),
                "roof_rise_dm": round(roof_rise * 10),
                "base_y_dm": round(base_y * 10),
                "area_m2": round(poly.area, 1),
                "seed": seed,
                "landmark": landmark,
                "lod0_q": quantized_ring(lod0, page_min_x, page_min_z, quantum),
                "lod1_q": quantized_ring(lod1, page_min_x, page_min_z, quantum),
                "lod2_q": quantized_ring(lod2, page_min_x, page_min_z, quantum),
            }
            pages[(col, row)].append(record)
            source_counts[source_name] += 1
            if landmark:
                landmark_records.append({"id": next_id, "name": name, "source_id": identity, "page": [col, row]})
            class_counts[cls] += 1
            roof_counts[profile] += 1
            height_counts[height_source] += 1
            base_height_counts[base_source] += 1
            next_id += 1

    page_records = []
    for row in range(rows):
        for col in range(cols):
            records = sorted(pages.get((col, row), []), key=lambda item: (item["source_id"], item["component"]))
            page_records.append({"column": col, "row": row, "buildings": records})
    output = {
        "schema_version": 1,
        "contract_version": "full-map-building-clipmap-v1",
        "center_latitude": lat,
        "center_longitude": lon,
        "origin_elevation_meters": config["origin_elevation_meters"],
        "map_half_extent_meters": extent,
        "page_size_meters": size,
        "columns": cols,
        "rows": rows,
        "quantization_meters": quantum,
        "runtime_window_cells": config["runtime_window_cells"],
        "prediction_seconds": config["runtime_contract"]["prediction_seconds"],
        "lod0_max_distance_meters": config["runtime_contract"]["lod0_max_distance_meters"],
        "lod1_max_distance_meters": config["runtime_contract"]["lod1_max_distance_meters"],
        "hysteresis_meters": config["runtime_contract"]["hysteresis_meters"],
        "pages": page_records,
    }
    report = {
        "generated_utc": datetime.now(timezone.utc).isoformat(),
        "source_urls": {name: config[f"{name}_service_url"] for name, _, _ in sources},
        "source_sha256": {name: sha256(path) for name, _, path in sources},
        "raw_feature_count": {name: len(source.get("features", [])) for name, source, _ in sources},
        "compiled_component_count": next_id,
        "nonempty_page_count": sum(1 for page in page_records if page["buildings"]),
        "page_count": len(page_records),
        "excluded_post_2020": excluded_post_2020,
        "excluded_outside": excluded_outside,
        "excluded_small": excluded_small,
        "excluded_authored_hero": excluded_authored_hero,
        "excluded_overlap": excluded_overlap,
        "compiled_source_counts": dict(sorted(source_counts.items())),
        "class_counts": dict(sorted(class_counts.items())),
        "roof_counts": dict(sorted(roof_counts.items())),
        "height_source_counts": dict(sorted(height_counts.items())),
        "base_height_source_counts": dict(sorted(base_height_counts.items())),
        "terrain_surface_source": str(terrain_source_path).replace("\\", "/"),
        "terrain_surface_sha256": sha256(terrain_source_path),
        "landmarks": landmark_records,
    }
    return output, report


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=pathlib.Path, default=pathlib.Path(__file__).with_name("config.json"))
    parser.add_argument("--raw-dir", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    parser.add_argument("--report", type=pathlib.Path, required=True)
    args = parser.parse_args()
    config = load_json(args.config)
    city = fetch_arcgis(config, args.raw_dir, "city", "city_building_footprints.geojson", 2000)
    county = fetch_arcgis(config, args.raw_dir, "county", "county_building_footprints.geojson", 1000)
    output, report = compile_database(config, [("city", city), ("county", county)])
    compact_json(args.output, output)
    compact_json(args.report, report)
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
