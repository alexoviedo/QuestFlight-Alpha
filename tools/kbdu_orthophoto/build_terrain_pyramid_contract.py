#!/usr/bin/env python3
"""Build the runtime terrain-pyramid contract, texel audit, and external heatmap."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageStat

from orthophoto_common import (
    DEFAULT_MANIFEST_PATH,
    SCRIPT_DIR,
    atomic_write_json,
    image_channel_stats,
    load_config,
)


DEFAULT_CONTRACT = SCRIPT_DIR / "kbdu_terrain_pyramid_contract.json"
DEFAULT_AUDIT = SCRIPT_DIR / "kbdu_ground_texel_density_audit.json"


def estimated_mip(meters_per_texel: float, slant_distance_meters: float) -> float:
    # Explicit planning assumption: 1832 vertical eye pixels and a 90 degree vertical FOV.
    projected_meters_per_pixel = 2.0 * slant_distance_meters * math.tan(math.radians(45.0)) / 1832.0
    return round(max(0.0, math.log2(max(projected_meters_per_pixel / meters_per_texel, 1.0))), 3)


def render_heatmap(path: Path, zones: list[dict], transitions: list[dict]) -> None:
    size = 1536
    center = size // 2
    scale = (size - 96) / (2.0 * max(zone["half_extent_meters"] for zone in zones))
    image = Image.new("RGB", (size, size), (24, 27, 32))
    draw = ImageDraw.Draw(image)
    colors = {
        "far_24km": (53, 96, 166),
        "mid_12km": (202, 149, 55),
        "near_4km": (54, 157, 91),
    }
    for zone in reversed(zones):
        radius = round(zone["half_extent_meters"] * scale)
        draw.rectangle(
            (center - radius, center - radius, center + radius, center + radius),
            fill=colors.get(zone["id"], (90, 90, 90)),
        )
    for transition in transitions:
        for distance in transition["blend_start_end_meters"]:
            radius = round(distance * scale)
            draw.rectangle(
                (center - radius, center - radius, center + radius, center + radius),
                outline=(245, 245, 245),
                width=3,
            )
    draw.text((32, 24), "KBDU terrain pyramid diagnostic / square ENU boundary metric", fill=(255, 255, 255))
    y = 56
    for zone in zones:
        draw.text(
            (32, y),
            f"{zone['id']}: {zone['meters_per_texel']:.6g} m/texel",
            fill=colors.get(zone["id"], (220, 220, 220)),
        )
        y += 24
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG", optimize=True)


def overlap_diagnostic(inner_path: Path, outer_path: Path, ratio: float) -> tuple[Image.Image, dict]:
    with Image.open(inner_path) as source:
        source.load()
        inner = source.convert("RGB")
    with Image.open(outer_path) as source:
        source.load()
        outer = source.convert("RGB")
    overlap_size = max(1, round(outer.width * ratio))
    left = (outer.width - overlap_size) // 2
    upper = (outer.height - overlap_size) // 2
    outer_overlap = outer.crop((left, upper, left + overlap_size, upper + overlap_size))
    inner_preview = inner.resize((512, 512), Image.Resampling.LANCZOS)
    outer_preview = outer_overlap.resize((512, 512), Image.Resampling.LANCZOS)
    difference = ImageChops.difference(inner_preview, outer_preview)
    amplified = difference.point(lambda value: min(255, value * 5))
    inner_stats = image_channel_stats(inner_preview)
    outer_stats = image_channel_stats(outer_preview)
    mean_deltas = [
        round(abs(inner_channel["mean"] - outer_channel["mean"]), 6)
        for inner_channel, outer_channel in zip(inner_stats, outer_stats)
    ]
    result = Image.new("RGB", (1536, 512), (26, 28, 31))
    result.paste(inner_preview, (0, 0))
    result.paste(outer_preview, (512, 0))
    result.paste(amplified, (1024, 0))
    return result, {
        "inner_channel_statistics": inner_stats,
        "outer_overlap_channel_statistics": outer_stats,
        "absolute_mean_channel_delta": mean_deltas,
        "maximum_absolute_mean_channel_delta": max(mean_deltas),
        "mean_absolute_pixel_error": [round(value, 6) for value in ImageStat.Stat(difference).mean],
        "passed_color_mean_continuity": max(mean_deltas) <= 8.0,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=None)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST_PATH)
    parser.add_argument("--contract", type=Path, default=DEFAULT_CONTRACT)
    parser.add_argument("--audit", type=Path, default=DEFAULT_AUDIT)
    parser.add_argument("--heatmap", required=True, type=Path, help="External diagnostic PNG")
    parser.add_argument("--overlap-contact-sheet", type=Path, default=None)
    args = parser.parse_args()

    config = load_config(args.config) if args.config else load_config()
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    assets = {asset["zone_id"]: asset for asset in manifest["derived_assets"]}
    zones: list[dict] = []
    for zone in config["zones"]:
        zone_id = zone["id"]
        asset = assets[zone_id]
        coverage = float(zone["half_extent_meters"]) * 2.0
        resolution = int(zone["atlas_size_pixels"])
        meters_per_texel = coverage / resolution
        zones.append(
            {
                "id": zone_id,
                "half_extent_meters": float(zone["half_extent_meters"]),
                "coverage_meters": coverage,
                "runtime_resolution_pixels": [resolution, resolution],
                "meters_per_texel": meters_per_texel,
                "texture_path": asset["path"],
                "texture_sha256": asset["sha256"],
                "world_min_inverse_size": [
                    -float(zone["half_extent_meters"]),
                    -float(zone["half_extent_meters"]),
                    1.0 / coverage,
                    1.0 / coverage,
                ],
                "mipmaps": True,
                "streaming_mipmaps": True,
                "anisotropy": 4,
                "android_compression": "ASTC_6x6",
                "estimated_mip_at_cockpit_height": estimated_mip(meters_per_texel, 10.0),
                "estimated_mip_at_pattern_altitude": estimated_mip(meters_per_texel, 431.0),
            }
        )

    pyramid = config["terrain_pyramid"]
    transitions = [
        {
            "inner_zone_id": "near_4km",
            "outer_zone_id": "mid_12km",
            "blend_start_end_meters": pyramid["near_to_mid_blend_meters"],
            "blend_width_meters": pyramid["near_to_mid_blend_meters"][1]
            - pyramid["near_to_mid_blend_meters"][0],
        },
        {
            "inner_zone_id": "mid_12km",
            "outer_zone_id": "far_24km",
            "blend_start_end_meters": pyramid["mid_to_far_blend_meters"],
            "blend_width_meters": pyramid["mid_to_far_blend_meters"][1]
            - pyramid["mid_to_far_blend_meters"][0],
        },
    ]
    zone_by_id = {zone["id"]: zone for zone in zones}
    adjacency_checks = []
    overlap_diagnostics = []
    overlap_rows = []
    for transition in transitions:
        inner = zone_by_id[transition["inner_zone_id"]]
        outer = zone_by_id[transition["outer_zone_id"]]
        ratio = outer["meters_per_texel"] / inner["meters_per_texel"]
        passed = ratio <= 4.0 and transition["blend_width_meters"] > 0.0
        adjacency_checks.append(
            {
                "inner_zone_id": inner["id"],
                "outer_zone_id": outer["id"],
                "texel_density_ratio": ratio,
                "maximum_ratio": 4.0,
                "overlap_blending_present": transition["blend_width_meters"] > 0.0,
                "passed": passed,
            }
        )
        inner_path = args.manifest.resolve().parents[2] / inner["texture_path"]
        outer_path = args.manifest.resolve().parents[2] / outer["texture_path"]
        diagnostic_image, diagnostic = overlap_diagnostic(
            inner_path,
            outer_path,
            inner["coverage_meters"] / outer["coverage_meters"],
        )
        diagnostic.update(
            {
                "inner_zone_id": inner["id"],
                "outer_zone_id": outer["id"],
            }
        )
        overlap_diagnostics.append(diagnostic)
        overlap_rows.append((transition, diagnostic_image))

    if args.overlap_contact_sheet:
        sheet = Image.new("RGB", (1536, 544 * len(overlap_rows)), (26, 28, 31))
        draw = ImageDraw.Draw(sheet)
        for row, (transition, diagnostic_image) in enumerate(overlap_rows):
            y = row * 544
            sheet.paste(diagnostic_image, (0, y + 32))
            draw.text(
                (12, y + 8),
                f"{transition['inner_zone_id']} | {transition['outer_zone_id']} central overlap | 5x absolute difference",
                fill=(244, 244, 244),
            )
        args.overlap_contact_sheet.parent.mkdir(parents=True, exist_ok=True)
        sheet.save(args.overlap_contact_sheet, format="JPEG", quality=92, subsampling=0, optimize=True)

    contract = {
        "schema_version": 1,
        "label": "KBDU continuous terrain imagery pyramid / not for navigation",
        "origin": config["origin"],
        "coordinate_frame": pyramid["coordinate_frame"],
        "boundary_metric": pyramid["boundary_metric"],
        "zones": zones,
        "transitions": transitions,
        "shader_contract": {
            "shader_name": "QuestFlight/KBDU Terrain Pyramid",
            "same_shader_and_lighting_across_all_rings": True,
            "globally_continuous_world_space_uv": True,
            "diagnostic_modes": pyramid["diagnostic_modes"],
            "microdetail_maximum_distance_meters": 1500.0,
            "runtime_material_instances_per_chunk": False,
        },
    }
    audit = {
        "schema_version": 1,
        "passed": all(check["passed"] for check in adjacency_checks)
        and all(item["passed_color_mean_continuity"] for item in overlap_diagnostics),
        "label": "GroundTexelDensityAudit",
        "projection_assumption": (
            "Planning estimate only: 1832 vertical eye pixels, 90 degree vertical FOV, "
            "10 m cockpit oblique slant sample and 431 m pattern-altitude slant sample. "
            "Unity/Quest runtime streaming may select different mips."
        ),
        "zones": zones,
        "adjacency_checks": adjacency_checks,
        "overlap_pixel_diagnostics": overlap_diagnostics,
        "fail_conditions": [
            "adjacent texel-density ratio exceeds 4x",
            "adjacent levels lack a nonzero overlap blend",
            "mipmaps or continuous ENU world-space UVs are absent",
            "the far macro texture is missing",
        ],
    }
    atomic_write_json(args.contract, contract)
    atomic_write_json(args.audit, audit)
    render_heatmap(args.heatmap, zones, transitions)
    print(args.contract)
    print(args.audit)
    print(args.heatmap)
    return 0 if audit["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
