#!/usr/bin/env python3
"""Build small Quest-oriented KBDU terrain rings and vector context from a raw snapshot."""

from __future__ import annotations

import argparse
import json
import math
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

from kbdu_common import (
    atomic_write_json,
    canonical_json_bytes,
    decode_int16_base64,
    encode_int16_base64,
    expected_triangle_count,
    file_record,
    geodetic_to_enu,
    iter_layer_grid,
    layer_dimensions,
    load_config,
    normalized_sha_key,
    repo_root,
    sha256_bytes,
    sha256_file,
    simplify_polyline,
    smoothstep,
)


ASSET_RELATIVE_DIR = Path("QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU")
MANIFEST_RELATIVE_PATH = Path("tools/kbdu_environment/kbdu_source_manifest.json")

ALLOWED_TAGS = (
    "name",
    "ref",
    "aeroway",
    "building",
    "building:levels",
    "height",
    "highway",
    "surface",
    "lanes",
    "waterway",
    "natural",
    "water",
    "landuse",
    "barrier",
    "access",
)

MACRO_MATERIAL_PALETTE = {
    "dry_prairie": {"albedo_hex": "#6F7442", "roughness": 0.92, "micro_detail": "dry_grass"},
    "irrigated_field": {"albedo_hex": "#4F7434", "roughness": 0.9, "micro_detail": "short_grass"},
    "harvested_field": {"albedo_hex": "#8B7B39", "roughness": 0.94, "micro_detail": "dry_grass"},
    "orchard": {"albedo_hex": "#3F672F", "roughness": 0.91, "micro_detail": "vegetation"},
    "meadow": {"albedo_hex": "#6B8248", "roughness": 0.92, "micro_detail": "short_grass"},
    "forest": {"albedo_hex": "#294529", "roughness": 0.93, "micro_detail": "vegetation"},
    "quarry": {"albedo_hex": "#82796C", "roughness": 0.88, "micro_detail": "rock"},
    "industrial_ground": {"albedo_hex": "#77736C", "roughness": 0.84, "micro_detail": "gravel"},
    "water": {"albedo_hex": "#28536B", "roughness": 0.28, "micro_detail": "water"},
    "asphalt": {"albedo_hex": "#343536", "roughness": 0.82, "micro_detail": "asphalt"},
    "concrete": {"albedo_hex": "#777777", "roughness": 0.76, "micro_detail": "concrete"},
    "gravel": {"albedo_hex": "#756A57", "roughness": 0.95, "micro_detail": "gravel"},
    "airfield_turf": {"albedo_hex": "#647542", "roughness": 0.94, "micro_detail": "short_grass"},
    "building_footprint": {"albedo_hex": "#696866", "roughness": 0.8, "micro_detail": "concrete"},
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--raw-dir", type=Path, required=True, help="Completed external raw snapshot directory.")
    parser.add_argument("--config", type=Path, default=Path(__file__).with_name("config.json"))
    parser.add_argument("--repo-root", type=Path, default=repo_root())
    return parser.parse_args()


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def verify_raw_snapshot(raw_dir: Path) -> dict[str, Any]:
    receipt_path = raw_dir / "fetch_receipt.json"
    if not receipt_path.is_file():
        raise RuntimeError(f"Missing raw fetch receipt: {receipt_path}")
    receipt = load_json(receipt_path)
    if receipt.get("schema_version") != 1 or not receipt.get("complete"):
        raise RuntimeError("Raw snapshot is incomplete or uses an unsupported receipt schema")
    for source in receipt.get("sources", []):
        source_path = raw_dir / source["path"]
        if not source_path.is_file():
            raise RuntimeError(f"Raw source missing: {source_path}")
        actual = sha256_file(source_path)
        if actual != source["sha256"]:
            raise RuntimeError(f"Raw source hash mismatch: {source_path} ({actual} != {source['sha256']})")
    return receipt


def load_height_samples(raw_dir: Path, receipt: dict[str, Any]) -> tuple[dict[str, float], dict[str, Any], dict[str, Any]]:
    index = load_json(raw_dir / "usgs_sample_index.json")
    points = index["points"]
    sample_records = sorted(
        (source for source in receipt["sources"] if source["id"].startswith("usgs_3dep_samples_")),
        key=lambda source: int(source["sample_start"]),
    )
    heights: dict[str, float] = {}
    raster_ids: set[int] = set()
    reported_resolutions: set[float] = set()
    next_start = 0
    for source in sample_records:
        start = int(source["sample_start"])
        count = int(source["sample_count"])
        if start != next_start:
            raise RuntimeError(f"USGS sample batch discontinuity at {start}; expected {next_start}")
        response = load_json(raw_dir / source["path"])
        samples = response.get("samples")
        if not isinstance(samples, list) or len(samples) != count:
            raise RuntimeError(f"USGS response {source['path']} has an unexpected sample count")
        seen_location_ids: set[int] = set()
        for sample in samples:
            location_id = int(sample.get("locationId", -1))
            if location_id < 0 or location_id >= count or location_id in seen_location_ids:
                raise RuntimeError(f"USGS response {source['path']} has an invalid/duplicate locationId {location_id}")
            seen_location_ids.add(location_id)
            point = points[start + location_id]
            returned_location = sample.get("location") or {}
            returned_longitude = float(returned_location.get("x", math.inf))
            returned_latitude = float(returned_location.get("y", math.inf))
            if (
                abs(returned_longitude - float(point["longitude_degrees"])) > 1e-8
                or abs(returned_latitude - float(point["latitude_degrees"])) > 1e-8
            ):
                raise RuntimeError(
                    f"USGS response {source['path']} locationId {location_id} coordinate mismatch: "
                    f"returned ({returned_longitude},{returned_latitude}) vs requested "
                    f"({point['longitude_degrees']},{point['latitude_degrees']})"
                )
            value = sample.get("value")
            try:
                height = float(value)
            except (TypeError, ValueError) as error:
                raise RuntimeError(f"USGS returned a non-numeric height {value!r} for {point['key']}") from error
            if not math.isfinite(height) or height < -500.0 or height > 9000.0:
                raise RuntimeError(f"USGS returned an implausible height {height} for {point['key']}")
            heights[point["key"]] = height
            if sample.get("rasterId") is not None:
                raster_ids.add(int(sample["rasterId"]))
            if sample.get("resolution") is not None:
                reported_resolutions.add(float(sample["resolution"]))
        if seen_location_ids != set(range(count)):
            raise RuntimeError(f"USGS response {source['path']} does not map every requested locationId")
        next_start += count
    if next_start != len(points) or len(heights) != len(points):
        raise RuntimeError(f"Loaded {len(heights)} of {len(points)} requested USGS heights")
    evidence = {
        "sample_count": len(points),
        "raster_ids": sorted(raster_ids),
        "service_reported_resolution_values": sorted(reported_resolutions),
        "location_mapping_verified": True,
        "arcgis_axis_order": "sample.location.x=longitude, sample.location.y=latitude; locationId maps to request index",
    }
    return heights, index, evidence


def validate_faa_origin(config: dict[str, Any], facility_geojson: dict[str, Any]) -> dict[str, Any]:
    features = facility_geojson.get("features") or []
    if len(features) != 1:
        raise RuntimeError(f"Expected one FAA BDU facility, found {len(features)}")
    feature = features[0]
    properties = feature.get("properties") or {}
    origin = config["airport"]["origin"]
    latitude = float(properties["LAT_DECIMAL"])
    longitude = float(properties["LONG_DECIMAL"])
    elevation_feet = float(properties["ELEV"])
    distance = math.hypot(
        *geodetic_to_enu(
            latitude,
            longitude,
            origin["elevation_msl_meters"],
            origin["latitude_degrees"],
            origin["longitude_degrees"],
            origin["elevation_msl_meters"],
        )[:2]
    )
    if distance > 2.0:
        raise RuntimeError(f"Pinned KBDU tangent origin differs from the FAA ARP by {distance:.2f} m")
    if abs(elevation_feet * 0.3048 - origin["elevation_msl_meters"]) > 0.02:
        raise RuntimeError("Pinned KBDU elevation no longer agrees with FAA facility metadata")
    keep = (
        "EFF_DATE",
        "SITE_NO",
        "ARPT_ID",
        "ICAO_ID",
        "ARPT_NAME",
        "CITY",
        "STATE_CODE",
        "LAT_DECIMAL",
        "LONG_DECIMAL",
        "ELEV",
        "TPA",
        "ARPT_STATUS",
        "ARPT_PSN_SOURCE",
        "POSITION_SRC_DATE",
        "ARPT_ELEV_SOURCE",
        "ELEVATION_SRC_DATE",
    )
    return {key: properties.get(key) for key in keep}


def build_runway_records(
    config: dict[str, Any],
    runways_geojson: dict[str, Any],
    height_samples: dict[str, float],
    sample_index: dict[str, Any],
) -> list[dict[str, Any]]:
    origin = config["airport"]["origin"]
    control_keys = sample_index["control_keys"]
    records: list[dict[str, Any]] = []
    for feature in runways_geojson.get("features") or []:
        properties = feature.get("properties") or {}
        runway_id = str(properties.get("RWY_ID") or "unknown")
        endpoints: list[dict[str, float]] = []
        for end_index in (1, 2):
            latitude = float(properties[f"LAT{end_index}_DECIMAL"])
            longitude = float(properties[f"LONG{end_index}_DECIMAL"])
            east, north, _ = geodetic_to_enu(
                latitude,
                longitude,
                origin["elevation_msl_meters"],
                origin["latitude_degrees"],
                origin["longitude_degrees"],
                origin["elevation_msl_meters"],
            )
            key = control_keys[f"runway:{runway_id}:end{end_index}"]
            endpoints.append(
                {
                    "latitude_degrees": latitude,
                    "longitude_degrees": longitude,
                    "x_east_meters": round(east, 3),
                    "z_north_meters": round(north, 3),
                    "usgs_elevation_msl_meters": round(height_samples[key], 3),
                }
            )
        polygon_local: list[list[float]] = []
        geometry = feature.get("geometry") or {}
        rings = geometry.get("coordinates") or []
        if rings:
            for longitude, latitude, *_ in rings[0]:
                east, north, _ = geodetic_to_enu(
                    float(latitude),
                    float(longitude),
                    origin["elevation_msl_meters"],
                    origin["latitude_degrees"],
                    origin["longitude_degrees"],
                    origin["elevation_msl_meters"],
                )
                polygon_local.append([round(east, 2), round(north, 2)])
        records.append(
            {
                "runway_id": runway_id,
                "effective_date": properties.get("EFF_DATE"),
                "length_feet": properties.get("RWY_LEN"),
                "width_feet": properties.get("RWY_WIDTH"),
                "surface": properties.get("SURFACE_TYPE_CODE"),
                "condition": properties.get("COND"),
                "lighting": properties.get("RWY_LGT_CODE"),
                "endpoints": endpoints,
                "polygon_xz_meters": polygon_local,
            }
        )
    if not any(record["runway_id"] == "08/26" for record in records):
        raise RuntimeError("FAA source did not contain KBDU paved runway 08/26")
    return sorted(records, key=lambda record: record["runway_id"])


def runway_blended_height(
    x_meters: float,
    z_meters: float,
    raw_height: float,
    runways: list[dict[str, Any]],
    blend_config: dict[str, Any],
) -> tuple[float, str | None, float]:
    best_weight = 0.0
    best_height = raw_height
    best_runway: str | None = None
    for runway in runways:
        first, second = runway["endpoints"]
        ax, az = first["x_east_meters"], first["z_north_meters"]
        bx, bz = second["x_east_meters"], second["z_north_meters"]
        dx, dz = bx - ax, bz - az
        length_squared = dx * dx + dz * dz
        length = math.sqrt(length_squared)
        if length < 1.0:
            continue
        t_unclamped = ((x_meters - ax) * dx + (z_meters - az) * dz) / length_squared
        t = max(0.0, min(1.0, t_unclamped))
        closest_x, closest_z = ax + t * dx, az + t * dz
        lateral = math.hypot(x_meters - closest_x, z_meters - closest_z)
        end_distance = 0.0 if 0.0 <= t_unclamped <= 1.0 else abs(t_unclamped - t) * length
        paved = runway["runway_id"] == "08/26"
        prefix = "paved" if paved else "other"
        half_width = float(runway["width_feet"] or 0.0) * 0.3048 * 0.5
        core = half_width + float(blend_config[f"{prefix}_core_margin_meters"])
        outer = half_width + float(blend_config[f"{prefix}_outer_margin_meters"])
        end_margin = float(blend_config[f"{prefix}_end_margin_meters"])
        lateral_weight = 1.0 - smoothstep(core, outer, lateral)
        end_weight = 1.0 - smoothstep(0.0, end_margin, end_distance)
        weight = lateral_weight * end_weight
        if weight <= best_weight:
            continue
        plane_height = first["usgs_elevation_msl_meters"] + t * (
            second["usgs_elevation_msl_meters"] - first["usgs_elevation_msl_meters"]
        )
        best_weight = weight
        best_height = raw_height + weight * (plane_height - raw_height)
        best_runway = runway["runway_id"]
    return best_height, best_runway, best_weight


def build_terrain_asset(
    config: dict[str, Any],
    receipt: dict[str, Any],
    height_samples: dict[str, float],
    usgs_evidence: dict[str, Any],
    runways: list[dict[str, Any]],
) -> dict[str, Any]:
    terrain = config["terrain"]
    origin = config["airport"]["origin"]
    quantization = float(terrain["height_quantization_meters"])
    layers_by_id = {layer["id"]: layer for layer in terrain["layers"]}
    output_layers: list[dict[str, Any]] = []
    total_samples = 0
    total_triangles = 0
    modified_samples = 0
    max_adjustment = 0.0
    runway_modified_counts: dict[str, int] = {}

    for layer in terrain["layers"]:
        width, height, min_x, max_x, min_z, max_z = layer_dimensions(layer)
        quantized: list[int] = []
        raw_values: list[float] = []
        render_values: list[float] = []
        for _, _, x_meters, z_meters in iter_layer_grid(layer):
            key = normalized_sha_key(x_meters, z_meters)
            if key not in height_samples:
                raise RuntimeError(f"Missing USGS sample {key} for terrain layer {layer['id']}")
            raw_height = height_samples[key]
            render_height, runway_id, weight = runway_blended_height(
                x_meters,
                z_meters,
                raw_height,
                runways,
                terrain["runway_blend"],
            )
            adjustment = abs(render_height - raw_height)
            if adjustment > 0.02 and weight > 0.0:
                modified_samples += 1
                max_adjustment = max(max_adjustment, adjustment)
                runway_modified_counts[runway_id or "unknown"] = runway_modified_counts.get(runway_id or "unknown", 0) + 1
            relative = int(round((render_height - origin["elevation_msl_meters"]) / quantization))
            quantized.append(relative)
            raw_values.append(raw_height)
            render_values.append(render_height)
        triangle_count = expected_triangle_count(layer, layers_by_id)
        total_samples += len(quantized)
        total_triangles += triangle_count
        output_layer = {
            "id": layer["id"],
            "kind": layer["kind"],
            "priority": layer["priority"],
            "min_x_meters": min_x,
            "max_x_meters": max_x,
            "min_z_meters": min_z,
            "max_z_meters": max_z,
            "spacing_meters": layer["spacing_meters"],
            "width": width,
            "height": height,
            "sample_count": len(quantized),
            "expected_triangle_count": triangle_count,
            "minimum_raw_elevation_msl_meters": round(min(raw_values), 3),
            "maximum_raw_elevation_msl_meters": round(max(raw_values), 3),
            "minimum_render_elevation_msl_meters": round(min(render_values), 3),
            "maximum_render_elevation_msl_meters": round(max(render_values), 3),
            "height_dm_little_endian_base64": encode_int16_base64(quantized),
        }
        if layer["kind"] == "ring":
            output_layer["inner_radius_meters"] = layer["inner_radius_meters"]
            output_layer["outer_radius_meters"] = layer["outer_radius_meters"]
        if layer.get("cutout_layer"):
            output_layer["cutout_layer"] = layer["cutout_layer"]
        output_layers.append(output_layer)

    return {
        "schema_version": 1,
        "label": config["airport"]["label"],
        "airport": {"faa_id": config["airport"]["faa_id"], "icao_id": config["airport"]["icao_id"]},
        "origin": origin,
        "coordinate_frame": {
            "name": "KBDU local tangent ENU",
            "unity_mapping": "Unity +X=east, +Y=up, +Z=north",
            "height_reference": "meters above mean sea level, quantized relative to origin.elevation_msl_meters",
        },
        "height_quantization_meters": quantization,
        "height_encoding": "signed int16 little-endian, row-major south-to-north then west-to-east, base64",
        "skirt_depth_meters": terrain["skirt_depth_meters"],
        "layers": output_layers,
        "runway_terrain_blend": {
            "algorithm": "smooth lateral/end blend to a linearly sloped plane between USGS-sampled FAA runway endpoints",
            "parameters": terrain["runway_blend"],
            "modified_sample_count": modified_samples,
            "modified_samples_by_runway": dict(sorted(runway_modified_counts.items())),
            "maximum_absolute_adjustment_meters": round(max_adjustment, 3),
            "note": "This render-only blend prevents runway/terrain gaps; it is not surveyed grading data.",
        },
        "source_snapshot": {
            "id": receipt["snapshot_id"],
            "retrieved_utc": receipt["retrieved_utc"],
            "fetch_receipt_sha256": sha256_file(Path(receipt["_receipt_path"])),
            "usgs_evidence": usgs_evidence,
        },
        "budgets": {
            "height_samples": total_samples,
            "expected_terrain_triangles": total_triangles,
            "maximum_height_samples": terrain["budgets"]["maximum_height_samples"],
            "maximum_terrain_triangles": terrain["budgets"]["maximum_terrain_triangles"],
        },
        "limitations": [
            "Not for navigation; FAA/OSM/USGS source dates and accuracy differ.",
            "No aerial imagery or Google-derived content is included.",
            "Runtime terrain loader, material blending, culling, and LOD transition behavior require integrated Unity validation.",
        ],
    }


def classify_osm(tags: dict[str, Any]) -> str | None:
    if tags.get("aeroway"):
        return "aeroway"
    if tags.get("building"):
        return "building"
    if tags.get("highway"):
        return "road"
    if tags.get("waterway") or tags.get("natural") == "water" or tags.get("landuse") == "reservoir":
        return "water"
    if tags.get("landuse") in {"farmland", "orchard", "meadow", "forest", "quarry", "industrial"}:
        return "landcover"
    if tags.get("barrier"):
        return "barrier"
    return None


def macro_material_for(category: str, tags: dict[str, Any], source_id: int) -> str | None:
    surface = str(tags.get("surface") or "").lower()
    landuse = str(tags.get("landuse") or "").lower()
    if category == "water":
        return "water"
    if category == "building":
        return "building_footprint"
    if category == "landcover":
        if landuse == "farmland":
            # Stable variation prevents one repeated checkerboard color while remaining reproducible.
            return "irrigated_field" if source_id % 3 == 0 else "harvested_field"
        return {
            "orchard": "orchard",
            "meadow": "meadow",
            "forest": "forest",
            "quarry": "quarry",
            "industrial": "industrial_ground",
            "reservoir": "water",
        }.get(landuse, "dry_prairie")
    if category in {"road", "aeroway"}:
        if any(token in surface for token in ("gravel", "dirt", "ground", "unpaved", "fine_gravel")):
            return "gravel"
        if any(token in surface for token in ("grass", "turf")):
            return "airfield_turf"
        if "concrete" in surface:
            return "concrete"
        return "asphalt"
    return None


def parse_tag_meters(value: Any) -> float | None:
    if value is None:
        return None
    text = str(value).strip().lower().replace("meters", "").replace("meter", "").replace("m", "").strip()
    try:
        parsed = float(text)
    except ValueError:
        return None
    return parsed if math.isfinite(parsed) and parsed > 0.0 else None


def runtime_hints_for(category: str, tags: dict[str, Any], source_id: int) -> tuple[float, float]:
    if category == "building":
        height = parse_tag_meters(tags.get("height"))
        if height is None:
            try:
                levels = float(str(tags.get("building:levels") or ""))
                height = levels * 3.1 if levels > 0.0 else None
            except ValueError:
                height = None
        return 0.0, max(3.5, min(30.0, height if height is not None else 4.5 + (source_id % 4) * 1.4))
    if category == "road":
        width = {
            "motorway": 16.0,
            "trunk": 14.0,
            "primary": 12.0,
            "secondary": 9.0,
            "tertiary": 7.0,
            "residential": 6.0,
            "service": 4.0,
        }.get(str(tags.get("highway") or "").lower(), 5.0)
        return width, 0.0
    if category == "water" and tags.get("waterway"):
        width = {"river": 20.0, "canal": 9.0, "stream": 5.0, "drain": 3.0, "ditch": 2.0}.get(
            str(tags.get("waterway") or "").lower(), 4.0
        )
        return width, 0.0
    if category == "aeroway":
        width = {"runway": 23.0, "taxiway": 11.0, "taxilane": 8.0}.get(
            str(tags.get("aeroway") or "").lower(), 5.0
        )
        return width, 0.0
    if category == "barrier":
        return 0.35, 1.5
    return 0.0, 0.0


def sutherland_hodgman_clip(
    polygon: list[tuple[float, float]], extent: float
) -> list[tuple[float, float]]:
    if polygon and polygon[0] == polygon[-1]:
        polygon = polygon[:-1]
    boundaries = (
        (lambda point: point[0] >= -extent, lambda a, b: (-extent, a[1] + (b[1] - a[1]) * (-extent - a[0]) / (b[0] - a[0]))),
        (lambda point: point[0] <= extent, lambda a, b: (extent, a[1] + (b[1] - a[1]) * (extent - a[0]) / (b[0] - a[0]))),
        (lambda point: point[1] >= -extent, lambda a, b: (a[0] + (b[0] - a[0]) * (-extent - a[1]) / (b[1] - a[1]), -extent)),
        (lambda point: point[1] <= extent, lambda a, b: (a[0] + (b[0] - a[0]) * (extent - a[1]) / (b[1] - a[1]), extent)),
    )
    output = list(polygon)
    for inside, intersect in boundaries:
        if not output:
            break
        source = output
        output = []
        previous = source[-1]
        previous_inside = inside(previous)
        for current in source:
            current_inside = inside(current)
            if current_inside:
                if not previous_inside:
                    output.append(intersect(previous, current))
                output.append(current)
            elif previous_inside:
                output.append(intersect(previous, current))
            previous, previous_inside = current, current_inside
    if len(output) >= 3:
        output.append(output[0])
    return output


def clip_segment(
    start: tuple[float, float], end: tuple[float, float], extent: float
) -> tuple[tuple[float, float], tuple[float, float]] | None:
    x0, y0 = start
    x1, y1 = end
    dx, dy = x1 - x0, y1 - y0
    p = (-dx, dx, -dy, dy)
    q = (x0 + extent, extent - x0, y0 + extent, extent - y0)
    low, high = 0.0, 1.0
    for pi, qi in zip(p, q):
        if pi == 0.0:
            if qi < 0.0:
                return None
            continue
        ratio = qi / pi
        if pi < 0.0:
            low = max(low, ratio)
        else:
            high = min(high, ratio)
        if low > high:
            return None
    return (x0 + low * dx, y0 + low * dy), (x0 + high * dx, y0 + high * dy)


def clip_polyline(points: list[tuple[float, float]], extent: float) -> list[list[tuple[float, float]]]:
    parts: list[list[tuple[float, float]]] = []
    current: list[tuple[float, float]] = []
    for start, end in zip(points, points[1:]):
        clipped = clip_segment(start, end, extent)
        if clipped is None:
            if len(current) >= 2:
                parts.append(current)
            current = []
            continue
        first, second = clipped
        if not current or math.dist(current[-1], first) > 0.01:
            if len(current) >= 2:
                parts.append(current)
            current = [first]
        current.append(second)
    if len(current) >= 2:
        parts.append(current)
    return parts


def relation_geometries(element: dict[str, Any]) -> Iterable[tuple[str, list[dict[str, Any]], str]]:
    if element.get("type") == "node" and element.get("lat") is not None and element.get("lon") is not None:
        yield "node", [{"lat": element["lat"], "lon": element["lon"]}], "node"
        return
    geometry = element.get("geometry")
    if isinstance(geometry, list):
        yield element.get("type", "way"), geometry, "geometry"
    for member_index, member in enumerate(element.get("members") or []):
        member_geometry = member.get("geometry")
        if isinstance(member_geometry, list):
            role = str(member.get("role") or "part")
            yield str(member.get("type") or "member"), member_geometry, f"member:{member_index}:{role}"


def projected_parts(
    element: dict[str, Any],
    config: dict[str, Any],
    category: str,
) -> Iterable[dict[str, Any]]:
    origin = config["airport"]["origin"]
    extent = float(config["vector_context"]["far_half_extent_meters"])
    tolerance = float(config["vector_context"]["simplification_meters"][category])
    tags = element.get("tags") or {}
    source_type = str(element.get("type") or "unknown")
    source_id = int(element.get("id") or 0)
    for geometry_type, geometry, part_id in relation_geometries(element):
        points: list[tuple[float, float]] = []
        for point in geometry:
            if point.get("lat") is None or point.get("lon") is None:
                continue
            east, north, _ = geodetic_to_enu(
                float(point["lat"]),
                float(point["lon"]),
                origin["elevation_msl_meters"],
                origin["latitude_degrees"],
                origin["longitude_degrees"],
                origin["elevation_msl_meters"],
            )
            points.append((east, north))
        if not points:
            continue
        closed = len(points) >= 4 and math.dist(points[0], points[-1]) < 1.0
        if len(points) == 1:
            if max(abs(points[0][0]), abs(points[0][1])) <= extent:
                yield {
                    "source_type": source_type,
                    "source_id": source_id,
                    "source_part": part_id,
                    "geometry_type": "point",
                    "points": points,
                    "tags": tags,
                }
            continue
        clipped_parts = [sutherland_hodgman_clip(points, extent)] if closed else clip_polyline(points, extent)
        for clipped_index, clipped in enumerate(clipped_parts):
            minimum = 4 if closed else 2
            if len(clipped) < minimum:
                continue
            simplified = simplify_polyline(clipped, tolerance, closed=closed)
            if len(simplified) < minimum:
                continue
            yield {
                "source_type": source_type,
                "source_id": source_id,
                "source_part": f"{part_id}:{clipped_index}",
                "geometry_type": "polygon" if closed else "polyline",
                "points": simplified,
                "tags": tags,
            }


def polygon_area(points: list[tuple[float, float]]) -> float:
    if len(points) < 4:
        return 0.0
    return abs(sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(points, points[1:]))) * 0.5


def build_osm_features(config: dict[str, Any], raw_dir: Path) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    candidates: list[dict[str, Any]] = []
    seen: set[tuple[str, int, str]] = set()
    raw_counts: dict[str, int] = {}
    for filename in ("osm_airport.json", "osm_context.json"):
        data = load_json(raw_dir / filename)
        raw_counts[filename] = len(data.get("elements") or [])
        for element in data.get("elements") or []:
            tags = element.get("tags") or {}
            category = classify_osm(tags)
            if category is None:
                continue
            for part in projected_parts(element, config, category):
                identity = (part["source_type"], part["source_id"], part["source_part"])
                if identity in seen:
                    continue
                seen.add(identity)
                part["category"] = category
                points = part["points"]
                part["distance"] = min(math.hypot(x, z) for x, z in points)
                part["area"] = polygon_area(points) if part["geometry_type"] == "polygon" else 0.0
                candidates.append(part)

    priority = {"aeroway": 0, "water": 1, "road": 2, "barrier": 3, "building": 4, "landcover": 5}
    candidates.sort(
        key=lambda item: (
            priority[item["category"]],
            item["distance"] if item["category"] != "landcover" else -item["area"],
            item["source_type"],
            item["source_id"],
            item["source_part"],
        )
    )
    budgets = config["vector_context"]["budgets"]
    quantization = float(config["vector_context"]["quantization_meters"])
    output: list[dict[str, Any]] = []
    point_count = 0
    dropped = 0
    category_counts: dict[str, int] = {}
    category_caps = budgets["maximum_features_by_category"]
    for candidate in candidates:
        if category_counts.get(candidate["category"], 0) >= int(category_caps[candidate["category"]]):
            dropped += 1
            continue
        points_q: list[list[int]] = []
        for x_meters, z_meters in candidate["points"]:
            point = [int(round(x_meters / quantization)), int(round(z_meters / quantization))]
            if not points_q or point != points_q[-1]:
                points_q.append(point)
        minimum = 1 if candidate["geometry_type"] == "point" else (4 if candidate["geometry_type"] == "polygon" else 2)
        if len(points_q) < minimum:
            dropped += 1
            continue
        if candidate["geometry_type"] == "polygon" and points_q[0] != points_q[-1]:
            points_q.append(points_q[0])
        if len(output) + 1 > int(budgets["maximum_features"]) or point_count + len(points_q) > int(budgets["maximum_points"]):
            dropped += 1
            continue
        filtered_tags = {key: str(candidate["tags"][key]) for key in ALLOWED_TAGS if key in candidate["tags"]}
        render_width, render_height = runtime_hints_for(
            candidate["category"], candidate["tags"], candidate["source_id"]
        )
        flat_points_q = [coordinate for point in points_q for coordinate in point]
        output.append(
            {
                "category": candidate["category"],
                "macro_material_id": macro_material_for(
                    candidate["category"], candidate["tags"], candidate["source_id"]
                ),
                "geometry_type": candidate["geometry_type"],
                "source": {
                    "osm_type": candidate["source_type"],
                    "osm_id": candidate["source_id"],
                    "part": candidate["source_part"],
                },
                "tags": filtered_tags,
                "render_width_meters": render_width,
                "render_height_meters": render_height,
                "points_q": flat_points_q,
            }
        )
        point_count += len(points_q)
        category_counts[candidate["category"]] = category_counts.get(candidate["category"], 0) + 1
    evidence = {
        "raw_element_counts": raw_counts,
        "candidate_part_count": len(candidates),
        "output_feature_count": len(output),
        "output_point_count": point_count,
        "dropped_or_budget_trimmed_parts": dropped,
        "category_counts": dict(sorted(category_counts.items())),
    }
    return output, evidence


def build_context_asset(
    config: dict[str, Any],
    receipt: dict[str, Any],
    facility: dict[str, Any],
    runways: list[dict[str, Any]],
    osm_features: list[dict[str, Any]],
    osm_evidence: dict[str, Any],
    imagery_decision: dict[str, Any],
) -> dict[str, Any]:
    vector = config["vector_context"]
    return {
        "schema_version": 1,
        "label": config["airport"]["label"],
        "origin": config["airport"]["origin"],
        "coordinate_frame": "KBDU local tangent ENU; Unity +X=east, +Z=north",
        "coordinate_quantization_meters": vector["quantization_meters"],
        "coordinate_encoding": "points_q values multiply by coordinate_quantization_meters",
        "faa": {
            "license": "United States government work; unrestricted public use",
            "attribution": "Federal Aviation Administration (FAA); USDOT Bureau of Transportation Statistics (BTS)",
            "facility": facility,
            "runways": runways,
        },
        "openstreetmap": {
            "license": "ODbL-1.0",
            "attribution": "© OpenStreetMap contributors",
            "copyright_url": "https://www.openstreetmap.org/copyright",
            "features": osm_features,
            "evidence": osm_evidence,
        },
        "macro_material_fallback": {
            "default_material_id": config["imagery"]["fallback"]["default_material_id"],
            "palette": MACRO_MATERIAL_PALETTE,
            "assignment": config["imagery"]["fallback"]["source"],
            "license": "Project-authored deterministic colors/parameters; no downloaded textures.",
            "imagery_gate": {
                "status": imagery_decision["status"],
                "availability_feature_count": imagery_decision["availability_feature_count"],
                "availability_bbox_wgs84": imagery_decision["availability_bbox_wgs84"],
                "service_max_export_pixels": imagery_decision["service_max_export_pixels"],
                "decision": imagery_decision["decision"],
                "exact_blocker": imagery_decision["exact_blocker"],
                "raw_imagery_downloaded": imagery_decision["raw_imagery_downloaded"],
            },
        },
        "coverage": {
            "airport_context_width_meters": 2.0 * float(vector["airport_half_extent_meters"]),
            "mid_context_width_meters": 2.0 * float(vector["mid_half_extent_meters"]),
            "far_context_width_meters": 2.0 * float(vector["far_half_extent_meters"]),
        },
        "source_snapshot": {"id": receipt["snapshot_id"], "retrieved_utc": receipt["retrieved_utc"]},
        "limitations": [
            "Reference geometry is simplified, clipped, and quantized for a standalone Quest prototype.",
            "OSM completeness and tag accuracy vary; this asset is not an aeronautical database.",
            "Building heights are used only when explicitly tagged; untagged buildings need conservative runtime defaults.",
            "NAIP availability was proven, but imagery was not committed because a seam-safe tiled/ASTC path is not yet validated.",
        ],
    }


def validate_prewrite_budgets(config: dict[str, Any], terrain_asset: dict[str, Any], context_asset: dict[str, Any]) -> None:
    terrain_budget = config["terrain"]["budgets"]
    if terrain_asset["budgets"]["height_samples"] > int(terrain_budget["maximum_height_samples"]):
        raise RuntimeError("Terrain sample budget exceeded")
    if terrain_asset["budgets"]["expected_terrain_triangles"] > int(terrain_budget["maximum_terrain_triangles"]):
        raise RuntimeError("Terrain triangle budget exceeded")
    vector_budget = config["vector_context"]["budgets"]
    evidence = context_asset["openstreetmap"]["evidence"]
    if evidence["output_feature_count"] > int(vector_budget["maximum_features"]):
        raise RuntimeError("Vector feature budget exceeded")
    if evidence["output_point_count"] > int(vector_budget["maximum_points"]):
        raise RuntimeError("Vector point budget exceeded")


def build_manifest(
    config: dict[str, Any],
    receipt: dict[str, Any],
    repo: Path,
    derived_paths: list[Path],
    terrain_asset: dict[str, Any],
    context_asset: dict[str, Any],
) -> dict[str, Any]:
    tool_paths = [
        Path(__file__).with_name("config.json"),
        Path(__file__).with_name("kbdu_common.py"),
        Path(__file__).with_name("fetch_kbdu_sources.py"),
        Path(__file__),
        Path(__file__).with_name("validate_kbdu_assets.py"),
        Path(__file__).with_name("render_kbdu_preview.py"),
        Path(__file__).with_name("README.md"),
        Path(__file__).with_name("tests") / "test_kbdu_pipeline.py",
        repo / "QuestFlight/Assets/Scripts/Environment/ProductionEnvironmentRoot.cs",
        repo / "QuestFlight/Assets/Editor/ProductionEnvironmentEditorTests.cs",
        repo / "QuestFlight/Assets/Tests/EditMode/ProductionEnvironmentEditModeTests.cs",
    ]
    tools = []
    for path in tool_paths:
        if path.is_file():
            tools.append(file_record(path, relative_to=repo))
    raw_sources = []
    for source in receipt["sources"]:
        raw_sources.append(
            {
                key: source[key]
                for key in (
                    "id",
                    "agency",
                    "url",
                    "method",
                    "request_sha256",
                    "retrieved_utc",
                    "path",
                    "bytes",
                    "sha256",
                    "license",
                    "notes",
                )
                if key in source
            }
        )
    return {
        "schema_version": 1,
        "label": config["airport"]["label"],
        "generated_from_snapshot": receipt["snapshot_id"],
        "generated_from_snapshot_utc": receipt["retrieved_utc"],
        "pipeline": "QuestFlightLab KBDU environment pipeline v1",
        "raw_data_policy": "Raw FAA/USGS/OSM responses are stored outside Git. Paths below are snapshot-relative and verified by SHA256.",
        "origin": config["airport"]["origin"],
        "coverage": context_asset["coverage"],
        "source_data": sorted(raw_sources, key=lambda source: source["id"]),
        "derived_assets": [file_record(path, relative_to=repo) for path in derived_paths],
        "toolchain": tools,
        "budgets": {
            "terrain": terrain_asset["budgets"],
            "vectors": context_asset["openstreetmap"]["evidence"],
        },
        "license_summary": [
            {
                "source": "FAA / USDOT BTS NTAD airport and runway data",
                "status": "United States government work; unrestricted public use",
                "attribution": "Federal Aviation Administration (FAA); USDOT Bureau of Transportation Statistics (BTS)",
            },
            {
                "source": "USGS 3DEP Bare Earth DEM dynamic service",
                "status": "USGS-authored data; public domain / no use restrictions",
                "attribution": "U.S. Geological Survey 3D Elevation Program (3DEP)",
            },
            {
                "source": "USGS/USDA NAIP Plus imagery availability",
                "status": "Public domain; availability queried but no imagery pixels downloaded or committed",
                "attribution": "USGS, USDA, The National Map: Orthoimagery",
            },
            {
                "source": "OpenStreetMap",
                "status": "ODbL-1.0",
                "attribution": "© OpenStreetMap contributors",
                "url": "https://www.openstreetmap.org/copyright",
            },
        ],
        "prohibited_sources_confirmation": "No Google Maps, Google Earth, or Street View-derived content is used.",
        "limitations": terrain_asset["limitations"] + context_asset["limitations"],
    }


def main() -> int:
    args = parse_args()
    raw_dir = args.raw_dir.resolve()
    repo = args.repo_root.resolve()
    config = load_config(args.config)
    receipt = verify_raw_snapshot(raw_dir)
    receipt["_receipt_path"] = str(raw_dir / "fetch_receipt.json")

    facility_geojson = load_json(raw_dir / "faa_airport.geojson")
    runways_geojson = load_json(raw_dir / "faa_runways.geojson")
    height_samples, sample_index, usgs_evidence = load_height_samples(raw_dir, receipt)
    usgs_evidence["control_elevations_msl_meters"] = {
        name: round(height_samples[key], 3) for name, key in sorted(sample_index["control_keys"].items())
    }
    facility = validate_faa_origin(config, facility_geojson)
    runways = build_runway_records(config, runways_geojson, height_samples, sample_index)
    terrain_asset = build_terrain_asset(config, receipt, height_samples, usgs_evidence, runways)
    osm_features, osm_evidence = build_osm_features(config, raw_dir)
    imagery_decision = load_json(raw_dir / "naip_imagery_decision.json")
    context_asset = build_context_asset(
        config, receipt, facility, runways, osm_features, osm_evidence, imagery_decision
    )
    validate_prewrite_budgets(config, terrain_asset, context_asset)

    asset_dir = repo / ASSET_RELATIVE_DIR
    terrain_path = asset_dir / "kbdu_terrain_rings.json"
    context_path = asset_dir / "kbdu_reference_context.json"
    atomic_write_json(terrain_path, terrain_asset, pretty=False)
    atomic_write_json(context_path, context_asset, pretty=False)

    if terrain_path.stat().st_size > int(config["terrain"]["budgets"]["maximum_asset_bytes"]):
        raise RuntimeError(f"Terrain asset exceeds byte budget: {terrain_path.stat().st_size}")
    if context_path.stat().st_size > int(config["vector_context"]["budgets"]["maximum_asset_bytes"]):
        raise RuntimeError(f"Context asset exceeds byte budget: {context_path.stat().st_size}")

    manifest = build_manifest(config, receipt, repo, [terrain_path, context_path], terrain_asset, context_asset)
    manifest_path = repo / MANIFEST_RELATIVE_PATH
    atomic_write_json(manifest_path, manifest)
    print(
        json.dumps(
            {
                "terrain_asset": str(terrain_path),
                "terrain_bytes": terrain_path.stat().st_size,
                "terrain_samples": terrain_asset["budgets"]["height_samples"],
                "terrain_triangles": terrain_asset["budgets"]["expected_terrain_triangles"],
                "context_asset": str(context_path),
                "context_bytes": context_path.stat().st_size,
                "context_features": osm_evidence["output_feature_count"],
                "context_points": osm_evidence["output_point_count"],
                "manifest": str(manifest_path),
            },
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
