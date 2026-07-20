"""Build the bounded KBDU V2 water coverage manifest from the pinned OSM context.

This tool performs source classification only.  Runtime meshes are authored by
Unity against IProductionTerrainSurface so source geometry never invents height.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
from typing import Any


NEAR_RADIUS_METERS = 2_000.0
MID_RADIUS_METERS = 6_000.0
NEAR_POLYGON_AREA_METERS2 = 500.0
MID_POLYGON_AREA_METERS2 = 5_000.0
NEAR_WATERWAY_WIDTH_METERS = 3.0
MID_WATERWAY_WIDTH_METERS = 5.0
MAJOR_MID_WATERWAYS = {"canal", "river"}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def decode_points(feature: dict[str, Any], quantization: float) -> list[list[float]]:
    encoded = feature.get("points_q") or []
    if len(encoded) < 4 or len(encoded) % 2:
        return []
    points = [[encoded[index] * quantization, encoded[index + 1] * quantization]
              for index in range(0, len(encoded), 2)]
    if len(points) > 1 and points[0] == points[-1]:
        points.pop()
    return points


def polygon_area(points: list[list[float]]) -> float:
    if len(points) < 3:
        return 0.0
    return abs(sum(
        points[index][0] * points[(index + 1) % len(points)][1]
        - points[(index + 1) % len(points)][0] * points[index][1]
        for index in range(len(points))
    )) * 0.5


def feature_id(feature: dict[str, Any], index: int) -> str:
    source = feature.get("source") or {}
    return f"osm:{source.get('osm_type', 'unknown')}/{source.get('osm_id', index)}/{source.get('part', 'geometry:0')}"


def classify(feature: dict[str, Any], points: list[list[float]]) -> tuple[bool, str, str, float]:
    center_x = sum(point[0] for point in points) / len(points)
    center_z = sum(point[1] for point in points) / len(points)
    radius = math.hypot(center_x, center_z)
    zone = "near_0_2km" if radius <= NEAR_RADIUS_METERS else "mid_2_6km" if radius <= MID_RADIUS_METERS else "far_beyond_6km"
    geometry = feature.get("geometry_type")
    if geometry == "polygon":
        area = polygon_area(points)
        threshold = NEAR_POLYGON_AREA_METERS2 if zone == "near_0_2km" else MID_POLYGON_AREA_METERS2
        if zone == "far_beyond_6km":
            return False, zone, "far_orthophoto_only", area
        return area >= threshold, zone, "eligible_polygon" if area >= threshold else "polygon_below_area_threshold", area
    if geometry == "polyline":
        width = float(feature.get("render_width_meters") or 0.0)
        waterway = str((feature.get("tags") or {}).get("waterway") or "")
        if zone == "far_beyond_6km":
            return False, zone, "far_orthophoto_only", 0.0
        if zone == "near_0_2km":
            eligible = width >= NEAR_WATERWAY_WIDTH_METERS
            return eligible, zone, "eligible_waterway" if eligible else "waterway_below_width_threshold", 0.0
        eligible = width >= MID_WATERWAY_WIDTH_METERS and waterway in MAJOR_MID_WATERWAYS
        return eligible, zone, "eligible_major_waterway" if eligible else "minor_mid_waterway", 0.0
    return False, zone, "unsupported_geometry", 0.0


def build_manifest(context_path: Path) -> dict[str, Any]:
    context = json.loads(context_path.read_text(encoding="utf-8"))
    quantization = float(context["coordinate_quantization_meters"])
    source_features = [feature for feature in context["openstreetmap"]["features"] if feature.get("category") == "water"]
    represented: list[dict[str, Any]] = []
    excluded: list[dict[str, Any]] = []
    for index, feature in enumerate(source_features):
        points = decode_points(feature, quantization)
        stable_id = feature_id(feature, index)
        if len(points) < (3 if feature.get("geometry_type") == "polygon" else 2):
            excluded.append({"source_feature_id": stable_id, "reason": "insufficient_geometry"})
            continue
        eligible, zone, reason, area = classify(feature, points)
        record = {
            "stable_id": "water-" + stable_id.replace(":", "-").replace("/", "-"),
            "source_feature_id": stable_id,
            "geometry_type": feature.get("geometry_type"),
            "zone": zone,
            "points_enu_meters": points,
            "points_enu_flat_meters": [coordinate for point in points for coordinate in point],
            "interpreted_width_meters": float(feature.get("render_width_meters") or 0.0),
            "area_square_meters": round(area, 3),
            "name": str((feature.get("tags") or {}).get("name") or ""),
            "waterway": str((feature.get("tags") or {}).get("waterway") or ""),
            "source_license_class": "OpenStreetMap ODbL-1.0; © OpenStreetMap contributors",
            "terrain_integration": "opaque_depth_surface_with_terrain_sampled_bank",
        }
        if eligible:
            represented.append(record)
        else:
            excluded.append({
                "source_feature_id": stable_id,
                "zone": zone,
                "geometry_type": feature.get("geometry_type"),
                "reason": reason,
                "area_square_meters": round(area, 3),
                "interpreted_width_meters": record["interpreted_width_meters"],
            })
    represented.sort(key=lambda item: item["stable_id"])
    excluded.sort(key=lambda item: item["source_feature_id"])
    return {
        "schema_version": 2,
        "coordinate_frame": context["coordinate_frame"],
        "source": {
            "path": context_path.as_posix(),
            "sha256": sha256(context_path),
            "license": "OpenStreetMap ODbL-1.0",
            "attribution": "© OpenStreetMap contributors",
            "snapshot": context["source_snapshot"],
        },
        "thresholds": {
            "near_radius_meters": NEAR_RADIUS_METERS,
            "mid_radius_meters": MID_RADIUS_METERS,
            "near_polygon_minimum_area_square_meters": NEAR_POLYGON_AREA_METERS2,
            "mid_polygon_minimum_area_square_meters": MID_POLYGON_AREA_METERS2,
            "near_waterway_minimum_width_meters": NEAR_WATERWAY_WIDTH_METERS,
            "mid_major_waterway_minimum_width_meters": MID_WATERWAY_WIDTH_METERS,
        },
        "source_feature_count": len(source_features),
        "represented_feature_count": len(represented),
        "excluded_feature_count": len(excluded),
        "represented_features": represented,
        "excluded_features": excluded,
    }


def report(manifest: dict[str, Any]) -> str:
    by_zone: dict[str, int] = {}
    by_geometry: dict[str, int] = {}
    for feature in manifest["represented_features"]:
        by_zone[feature["zone"]] = by_zone.get(feature["zone"], 0) + 1
        by_geometry[feature["geometry_type"]] = by_geometry.get(feature["geometry_type"], 0) + 1
    return (
        "# KBDU V2 water coverage source report\n\n"
        f"- Source features: {manifest['source_feature_count']}\n"
        f"- Represented by threshold: {manifest['represented_feature_count']}\n"
        f"- Excluded with recorded reason: {manifest['excluded_feature_count']}\n"
        f"- Represented zones: {json.dumps(by_zone, sort_keys=True)}\n"
        f"- Represented geometry: {json.dumps(by_geometry, sort_keys=True)}\n"
        "- License: OpenStreetMap ODbL-1.0; © OpenStreetMap contributors\n\n"
        "This report proves deterministic source coverage only. Unity terrain separation, visual quality, and Quest performance are separate gates.\n"
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--context", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    manifest = build_manifest(args.context)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(manifest, separators=(",", ":"), sort_keys=True) + "\n", encoding="utf-8")
    args.report.write_text(report(manifest), encoding="utf-8")
    print(args.output)
    print(args.report)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
