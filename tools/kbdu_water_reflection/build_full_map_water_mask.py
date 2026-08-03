#!/usr/bin/env python3
"""Build the full-map, orthophoto-preserving KBDU water reflection mask.

Raw OpenStreetMap downloads remain outside Git.  This tool consumes the pinned
2026-07-10 raw extract, audits every water candidate inside the authored 24 km
terrain square, and commits only the optimized mask plus a compact provenance
manifest.  Runtime water remains terrain-owned: no water mesh is generated.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import sys
from pathlib import Path
from typing import Any

from PIL import Image, ImageChops, ImageDraw, ImageFilter


TOOL_DIR = Path(__file__).resolve().parent
REPOSITORY_ROOT = TOOL_DIR.parents[1]
ENVIRONMENT_TOOL_DIR = REPOSITORY_ROOT / "tools" / "kbdu_environment"
sys.path.insert(0, str(ENVIRONMENT_TOOL_DIR))

from process_kbdu_environment import build_osm_features  # noqa: E402


WORLD_HALF_EXTENT_METERS = 12_000.0
WORLD_SIZE_METERS = WORLD_HALF_EXTENT_METERS * 2.0
MINIMUM_EFFECTIVE_AREA_SQUARE_METERS = 250.0
MAJOR_WATER_AREA_SQUARE_METERS = 20_000.0
# 11.72 m/texel across the 24 km product extent. Two-times supersampling,
# feathering, mipmaps, and the orthophoto base retain stable shorelines while
# ASTC 12x12 keeps the comprehensive mask below 0.6 MiB on Quest.
MASK_SIZE = 2048
SUPERSAMPLE = 2
SOURCE_SNAPSHOT = "20260710T214309Z"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def decode_points(feature: dict[str, Any], quantization: float) -> list[tuple[float, float]]:
    encoded = feature["points_q"]
    return [
        (encoded[index] * quantization, encoded[index + 1] * quantization)
        for index in range(0, len(encoded), 2)
    ]


def polygon_area(points: list[tuple[float, float]]) -> float:
    if len(points) < 3:
        return 0.0
    return abs(
        sum(
            points[index][0] * points[(index + 1) % len(points)][1]
            - points[(index + 1) % len(points)][0] * points[index][1]
            for index in range(len(points))
        )
    ) * 0.5


def polyline_length(points: list[tuple[float, float]]) -> float:
    return sum(math.dist(points[index], points[index + 1]) for index in range(len(points) - 1))


def saturate(value: float) -> float:
    return max(0.0, min(1.0, value))


def stable_id(feature: dict[str, Any]) -> str:
    source = feature["source"]
    return f"osm:{source['osm_type']}/{source['osm_id']}/{source['part']}"


def pixel_point(point: tuple[float, float], scale: float) -> tuple[float, float]:
    east, north = point
    # Match the committed far orthophoto's north-up source-image orientation.
    # Both textures are sampled by the same world-space UV in Unity.
    return (
        (east + WORLD_HALF_EXTENT_METERS) * scale,
        (WORLD_HALF_EXTENT_METERS - north) * scale,
    )


def load_all_water_features(config_path: Path, raw_dir: Path) -> tuple[list[dict[str, Any]], dict[str, Any], float]:
    config = json.loads(config_path.read_text(encoding="utf-8"))
    expanded = copy.deepcopy(config)
    category_caps = expanded["vector_context"]["budgets"]["maximum_features_by_category"]
    for category in category_caps:
        category_caps[category] = 0
    category_caps["water"] = 100_000
    expanded["vector_context"]["budgets"]["maximum_features"] = 100_000
    expanded["vector_context"]["budgets"]["maximum_points"] = 10_000_000
    features, evidence = build_osm_features(expanded, raw_dir)
    water = [feature for feature in features if feature["category"] == "water"]
    if evidence["category_counts"].get("water") != len(water):
        raise RuntimeError("Expanded water-source accounting is inconsistent.")
    return water, evidence, float(expanded["vector_context"]["quantization_meters"])


def select_features(
    candidates: list[dict[str, Any]], quantization: float
) -> tuple[list[tuple[dict[str, Any], list[tuple[float, float]], float, float, float]], list[dict[str, Any]]]:
    selected: list[tuple[dict[str, Any], list[tuple[float, float]], float, float, float]] = []
    records: list[dict[str, Any]] = []
    for feature in candidates:
        points = decode_points(feature, quantization)
        geometry_type = feature["geometry_type"]
        area = polygon_area(points) if geometry_type == "polygon" else 0.0
        length = polyline_length(points) if geometry_type == "polyline" else 0.0
        width = float(feature.get("render_width_meters") or 0.0)
        effective_area = area if geometry_type == "polygon" else length * width
        if effective_area < MINIMUM_EFFECTIVE_AREA_SQUARE_METERS:
            continue
        selected.append((feature, points, area, length, effective_area))
        records.append(
            {
                "stable_id": stable_id(feature),
                "geometry_type": geometry_type,
                "source": feature["source"],
                "tags": feature.get("tags") or {},
                "area_square_meters": round(area, 3),
                "length_meters": round(length, 3),
                "interpreted_width_meters": round(width, 3),
                "effective_area_square_meters": round(effective_area, 3),
                "reflection_tier": reflection_tier(feature, area),
                "bounds_enu_meters": [
                    round(min(point[0] for point in points), 3),
                    round(min(point[1] for point in points), 3),
                    round(max(point[0] for point in points), 3),
                    round(max(point[1] for point in points), 3),
                ],
            }
        )
    records.sort(key=lambda item: item["stable_id"])
    return selected, records


def reflection_tier(feature: dict[str, Any], area: float) -> str:
    if feature["geometry_type"] == "polyline":
        return "linear"
    return "major" if area >= MAJOR_WATER_AREA_SQUARE_METERS else "secondary"


def render_mask(
    selected: list[tuple[dict[str, Any], list[tuple[float, float]], float, float, float]],
    output_path: Path,
) -> dict[str, Any]:
    size = MASK_SIZE * SUPERSAMPLE
    scale = (size - 1) / WORLD_SIZE_METERS
    meters_per_pixel = WORLD_SIZE_METERS / MASK_SIZE
    major_core = Image.new("L", (size, size), 0)
    secondary_core = Image.new("L", (size, size), 0)
    linear_core = Image.new("L", (size, size), 0)
    major_draw = ImageDraw.Draw(major_core)
    secondary_draw = ImageDraw.Draw(secondary_core)
    linear_draw = ImageDraw.Draw(linear_core)
    major_count = 0
    secondary_count = 0
    polygon_count = 0
    polyline_count = 0
    for feature, points, area, _, effective_area in selected:
        pixels = [pixel_point(point, scale) for point in points]
        if feature["geometry_type"] == "polygon":
            if reflection_tier(feature, area) == "major":
                major_draw.polygon(pixels, fill=255)
                major_count += 1
            else:
                secondary_draw.polygon(pixels, fill=255)
                secondary_count += 1
            polygon_count += 1
        else:
            physical_width = float(feature.get("render_width_meters") or 0.0)
            width_pixels = max(1, round(physical_width / meters_per_pixel * SUPERSAMPLE))
            # Linear water remains represented everywhere, but its mask authority
            # is deliberately much lower than a lake/reservoir. This prevents
            # narrow tributaries from becoming bright ribbons from altitude.
            significance = saturate(
                (math.log2(max(effective_area, MINIMUM_EFFECTIVE_AREA_SQUARE_METERS)) -
                 math.log2(MINIMUM_EFFECTIVE_AREA_SQUARE_METERS)) / 8.0
            )
            linear_level = round(40.0 + significance * 88.0)
            linear_draw.line(
                pixels,
                fill=linear_level,
                width=width_pixels,
                joint="curve",
            )
            polyline_count += 1

    # Blur the actual coverage rather than taking max(core, blur). The former
    # implementation retained a hard interior edge and only bled outward,
    # which made the effect read as a colored cutout. These radii provide a
    # roughly 20-30 m transition at the final 11.72 m/texel density.
    major = major_core.resize((MASK_SIZE, MASK_SIZE), Image.Resampling.LANCZOS)
    major = major.filter(ImageFilter.GaussianBlur(radius=2.4))
    secondary = secondary_core.resize((MASK_SIZE, MASK_SIZE), Image.Resampling.LANCZOS)
    secondary = secondary.filter(ImageFilter.GaussianBlur(radius=2.0))
    linear = linear_core.resize((MASK_SIZE, MASK_SIZE), Image.Resampling.LANCZOS)
    linear = linear.filter(ImageFilter.GaussianBlur(radius=1.4))
    mask = Image.merge("RGB", (major, secondary, linear))
    output_path.parent.mkdir(parents=True, exist_ok=True)
    mask.save(output_path, optimize=True, compress_level=9)
    composite = ImageChops.lighter(ImageChops.lighter(major, secondary), linear)
    histogram = composite.histogram()
    covered = sum(histogram[8:])
    strong = sum(histogram[128:])
    return {
        "width": MASK_SIZE,
        "height": MASK_SIZE,
        "world_min_enu_meters": [-WORLD_HALF_EXTENT_METERS, -WORLD_HALF_EXTENT_METERS],
        "world_size_meters": [WORLD_SIZE_METERS, WORLD_SIZE_METERS],
        "meters_per_pixel": meters_per_pixel,
        "channel_contract": {
            "r": "major polygon water >= 20000 m2",
            "g": "secondary polygon water >= 250 m2",
            "b": "linear waterways >= 250 m2 effective area, significance weighted",
        },
        "shore_feather_pixels": {
            "major": 2.4,
            "secondary": 2.0,
            "linear": 1.4,
        },
        "major_polygon_count": major_count,
        "secondary_polygon_count": secondary_count,
        "polygon_count": polygon_count,
        "polyline_count": polyline_count,
        "nonzero_coverage_fraction": covered / (MASK_SIZE * MASK_SIZE),
        "strong_coverage_fraction": strong / (MASK_SIZE * MASK_SIZE),
        "sha256": sha256(output_path),
    }


def render_preview(mask_path: Path, far_albedo_path: Path, preview_dir: Path) -> None:
    preview_dir.mkdir(parents=True, exist_ok=True)
    packed = Image.open(mask_path).convert("RGB")
    major, secondary, linear = packed.split()
    mask = ImageChops.lighter(ImageChops.lighter(major, secondary), linear)
    far = Image.open(far_albedo_path).convert("RGB").resize(mask.size, Image.Resampling.LANCZOS)
    highlight = Image.new("RGB", mask.size, (238, 245, 238))
    overlay_alpha = mask.point(lambda value: round(value * 0.22))
    overlay = Image.composite(highlight, far, overlay_alpha)
    debug = Image.merge("RGB", (major, secondary, linear))
    overlay.thumbnail((2048, 2048), Image.Resampling.LANCZOS)
    debug.thumbnail((2048, 2048), Image.Resampling.LANCZOS)
    overlay.save(preview_dir / "full_map_water_mask_overlay.jpg", quality=92, optimize=True)
    debug.save(preview_dir / "full_map_water_mask_debug.png", optimize=True)


def build(
    raw_dir: Path,
    config_path: Path,
    output_mask: Path,
    output_manifest: Path,
    far_albedo: Path | None = None,
    preview_dir: Path | None = None,
) -> dict[str, Any]:
    for name in ("osm_airport.json", "osm_context.json", "osm_queries.json", "fetch_receipt.json"):
        if not (raw_dir / name).is_file():
            raise FileNotFoundError(f"Pinned raw source is incomplete: {raw_dir / name}")
    candidates, source_evidence, quantization = load_all_water_features(config_path, raw_dir)
    selected, feature_records = select_features(candidates, quantization)
    if len(selected) < 1_000:
        raise RuntimeError(f"Full-map water selection is unexpectedly sparse ({len(selected)}).")
    mask = render_mask(selected, output_mask)
    source_hashes = {
        name: sha256(raw_dir / name)
        for name in ("osm_airport.json", "osm_context.json", "osm_queries.json", "fetch_receipt.json")
    }
    manifest = {
        "schema_version": 1,
        "label": "KBDU full-map orthophoto-preserving water reflection mask",
        "source_snapshot": SOURCE_SNAPSHOT,
        "coordinate_frame": "KBDU local tangent ENU; Unity +X=east, +Z=north",
        "image_row_contract": "North-up source image matching the far orthophoto sampled by the same Unity UV.",
        "coverage": {
            "terrain_width_meters": WORLD_SIZE_METERS,
            "candidate_feature_count": len(candidates),
            "selected_feature_count": len(selected),
            "excluded_below_effective_area_count": len(candidates) - len(selected),
            "minimum_effective_area_square_meters": MINIMUM_EFFECTIVE_AREA_SQUARE_METERS,
            "major_water_area_square_meters": MAJOR_WATER_AREA_SQUARE_METERS,
            "selection_rule": (
                "Every pinned OSM natural-water/reservoir polygon or waterway centerline "
                "whose polygon area or interpreted width x length is at least 250 m2."
            ),
            "swimming_pool_policy": (
                "The pinned source query excludes leisure=swimming_pool; the 250 m2 threshold "
                "is a size floor, not a request to render pools."
            ),
        },
        "runtime_contract": {
            "representation": "single opaque terrain-pass mask",
            "orthophoto_remains_base_color": True,
            "additional_renderers": 0,
            "additional_draw_calls": 0,
            "additional_materials": 0,
            "additional_transparent_slots": 0,
            "runtime_scripts": 0,
            "reflection_source": "existing baked/global sky reflection, luminance-gated and nearly neutral",
            "normal_distortion": "stable world-space procedural low-frequency perturbation",
            "diffuse_color_modification": "none",
            "priority": "major polygon > secondary polygon > linear waterway",
        },
        "mask": mask,
        "source": {
            "logical_raw_location": "external-cache/raw_20260710",
            "hashes": source_hashes,
            "license": "ODbL-1.0",
            "attribution": "© OpenStreetMap contributors",
            "copyright_url": "https://www.openstreetmap.org/copyright",
            "expanded_source_evidence": source_evidence,
        },
        "generator": {
            "path": "tools/kbdu_water_reflection/build_full_map_water_mask.py",
            "sha256": sha256(Path(__file__)),
            "pillow_version": Image.__version__,
        },
        "selected_features": feature_records,
    }
    output_manifest.parent.mkdir(parents=True, exist_ok=True)
    output_manifest.write_text(
        json.dumps(manifest, separators=(",", ":"), sort_keys=True) + "\n",
        encoding="utf-8",
    )
    if far_albedo is not None and preview_dir is not None:
        render_preview(output_mask, far_albedo, preview_dir)
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw-dir", type=Path, required=True)
    parser.add_argument(
        "--config",
        type=Path,
        default=REPOSITORY_ROOT / "tools" / "kbdu_environment" / "config.json",
    )
    parser.add_argument("--output-mask", type=Path, required=True)
    parser.add_argument("--output-manifest", type=Path, required=True)
    parser.add_argument("--far-albedo", type=Path)
    parser.add_argument("--preview-dir", type=Path)
    args = parser.parse_args()
    manifest = build(
        args.raw_dir,
        args.config,
        args.output_mask,
        args.output_manifest,
        args.far_albedo,
        args.preview_dir,
    )
    print(
        json.dumps(
            {
                "candidate_feature_count": manifest["coverage"]["candidate_feature_count"],
                "selected_feature_count": manifest["coverage"]["selected_feature_count"],
                "mask": manifest["mask"],
            },
            indent=2,
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
