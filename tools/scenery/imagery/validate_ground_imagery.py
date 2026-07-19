#!/usr/bin/env python3
"""Validate KBDU recovery imagery, seams, provenance, importer settings, and budget."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

from PIL import Image, ImageChops, ImageDraw, ImageStat

from ground_imagery_common import DEFAULT_CONFIG, DEFAULT_OUTPUT, REPO_ROOT, atomic_write_json, load_config, sha256_file


def crop_overlap(image: Image.Image, side: str, pixels: int = 32) -> Image.Image:
    if side == "left":
        return image.crop((0, 0, pixels, image.height))
    if side == "right":
        return image.crop((image.width - pixels, 0, image.width, image.height))
    if side == "top":
        return image.crop((0, 0, image.width, pixels))
    if side == "bottom":
        return image.crop((0, image.height - pixels, image.width, image.height))
    raise ValueError(side)


def seam_metric(first: Image.Image, first_side: str, second: Image.Image, second_side: str) -> dict[str, Any]:
    left = crop_overlap(first, first_side)
    right = crop_overlap(second, second_side).resize(left.size, Image.Resampling.BILINEAR)
    difference = ImageChops.difference(left, right)
    stats = ImageStat.Stat(difference)
    mean = [round(value, 6) for value in stats.mean]
    return {
        "mean_absolute_channel_delta": mean,
        "maximum_mean_absolute_channel_delta": max(mean),
        "passed": max(mean) <= 12.0,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--asset-dir", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--contact-sheet", type=Path, default=None)
    parser.add_argument("--comparison-sheet", type=Path, default=None)
    args = parser.parse_args()

    config = load_config(args.config)
    asset_dir = args.asset_dir.resolve()
    manifest_path = asset_dir / "KBDU_GroundImageryRecoveryV2_SourceManifest.json"
    contract_path = asset_dir / "KBDU_GroundImageryRecoveryV2_Contract.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    contract = json.loads(contract_path.read_text(encoding="utf-8"))
    errors: list[str] = []
    source = manifest["source"]
    if not source["service_url"].startswith("https://imagery.nationalmap.gov/"):
        errors.append("source is not the official USGS imagery service")
    if source["license_spdx"] != "LicenseRef-US-Government-Work-Public-Domain":
        errors.append("public-domain source marker is absent")
    if source["catalog_year"] != 2019 or float(source["resolution_meters"]) != 0.6:
        errors.append("pinned current KBDU catalog lineage changed")
    allowed_source_hosts = ("imagery.nationalmap.gov", "earthexplorer.usgs.gov")
    source_urls = [source["service_url"]] + [
        str(feature.get("download_url") or "") for feature in source["catalog_features"]
    ]
    if any(not any(host in source_url for host in allowed_source_hosts) for source_url in source_urls if source_url):
        errors.append("catalog lineage includes a non-USGS source URL")

    assets: dict[str, Image.Image] = {}
    asset_checks: list[dict[str, Any]] = []
    for record in manifest["derived_assets"]:
        path = REPO_ROOT / record["path"]
        meta_path = Path(str(path) + ".meta")
        if not path.is_file() or not meta_path.is_file():
            errors.append(f"missing asset or paired meta: {record['path']}")
            continue
        with Image.open(path) as source_image:
            image = source_image.convert("RGB")
        assets[record["id"]] = image
        meta = meta_path.read_text(encoding="utf-8")
        is_fallback = record["id"] == "mid_base_12km"
        expected_dimensions = [4096, 4096] if is_fallback else [2048, 2048]
        expected_format = "textureFormat: 52" if is_fallback else "textureFormat: 50"
        checks = {
            "id": record["id"],
            "path": record["path"],
            "sha256_matches": sha256_file(path) == record["sha256"],
            "dimensions_match": list(image.size) == record["dimensions_pixels"] == expected_dimensions,
            "mipmaps": "enableMipMap: 1" in meta,
            "streaming_disabled": "streamingMipmaps: 0" in meta,
            "android_astc": "buildTarget: Android" in meta and expected_format in meta,
            "trilinear": "filterMode: 2" in meta,
            "anisotropy_8": "aniso: 8" in meta,
            "clamp": "wrapU: 1" in meta and "wrapV: 1" in meta,
        }
        if not all(value for key, value in checks.items() if key not in {"id", "path"}):
            errors.append(f"asset/import contract failed: {record['id']}")
        asset_checks.append(checks)

    seams = {
        "north_west_to_east": seam_metric(assets["mid_nw"], "right", assets["mid_ne"], "left"),
        "south_west_to_east": seam_metric(assets["mid_sw"], "right", assets["mid_se"], "left"),
        "west_north_to_south": seam_metric(assets["mid_nw"], "bottom", assets["mid_sw"], "top"),
        "east_north_to_south": seam_metric(assets["mid_ne"], "bottom", assets["mid_se"], "top"),
    }
    if not all(item["passed"] for item in seams.values()):
        errors.append("one or more authored quadrant overlaps exceed the seam threshold")

    budget = manifest["budget"]
    if int(budget["net_full_mip_increase_bytes"]) > int(config["runtime"]["maximum_net_full_mip_increase_bytes"]):
        errors.append("net full-mip increase exceeds the configured limit")
    if int(budget["projected_scene_estimated_texture_bytes"]) > int(budget["scene_texture_ceiling_bytes"]):
        errors.append("projected full-scene texture memory exceeds the static ceiling")
    water = contract["water"]
    water_text = json.dumps(water, sort_keys=True).lower()
    if water.get("mode") != "orthophoto_only" or not all(
        token in water_text for token in ("no water renderer", "no extra draw", "no animated normal", "no reflection")
    ):
        errors.append("orthophoto-only water contract is malformed")
    if contract["renderer_contract"].get("no_draw_triangle_or_material_delta_from_imagery_itself") is not True:
        errors.append("zero geometry/material/draw delta contract is absent")

    if args.contact_sheet:
        preview_size = 512
        order = ["mid_nw", "mid_ne", "mid_sw", "mid_se", "mid_base_12km"]
        sheet = Image.new("RGB", (preview_size * 3, preview_size * 2 + 80), (23, 26, 30))
        draw = ImageDraw.Draw(sheet)
        positions = [(0, 40), (512, 40), (0, 552), (512, 552), (1024, 40)]
        for identifier, position in zip(order, positions):
            preview = assets[identifier].resize((preview_size, preview_size), Image.Resampling.LANCZOS)
            sheet.paste(preview, position)
            draw.text((position[0] + 10, position[1] + 10), identifier, fill=(255, 255, 255))
        draw.text((16, 12), "KBDU public-domain ground imagery recovery derivatives", fill=(255, 255, 255))
        args.contact_sheet.parent.mkdir(parents=True, exist_ok=True)
        sheet.save(args.contact_sheet, format="JPEG", quality=92, subsampling=0, optimize=True)

    if args.comparison_sheet:
        baseline_path = REPO_ROOT / config["baseline"]["active_mid_texture"]
        with Image.open(baseline_path) as baseline_source:
            baseline = baseline_source.convert("RGB")
        records = {
            record["id"]: record for record in manifest["derived_assets"] if record["id"] != "mid_base_12km"
        }
        row_height = 540
        comparison = Image.new("RGB", (2048, row_height * 4 + 44), (23, 26, 30))
        draw = ImageDraw.Draw(comparison)
        draw.text((16, 12), "V1 12km/4096 crop (left) | Recovery 8km hot tile (right)", fill=(255, 255, 255))
        for row, identifier in enumerate(("mid_nw", "mid_ne", "mid_sw", "mid_se")):
            nominal = records[identifier]["nominal_cell_bounds_meters"]
            min_x, min_z, max_x, max_z = map(float, nominal)
            left = round((min_x + 6000.0) / 12000.0 * baseline.width)
            right = round((max_x + 6000.0) / 12000.0 * baseline.width)
            top = round((6000.0 - max_z) / 12000.0 * baseline.height)
            bottom = round((6000.0 - min_z) / 12000.0 * baseline.height)
            before = baseline.crop((left, top, right, bottom)).resize((1024, 512), Image.Resampling.LANCZOS)
            recovery_image = assets[identifier]
            inset = round(32.0 / 4064.0 * recovery_image.width)
            after = recovery_image.crop(
                (inset, inset, recovery_image.width - inset, recovery_image.height - inset)
            ).resize(
                (1024, 512), Image.Resampling.LANCZOS
            )
            y = 44 + row * row_height
            comparison.paste(before, (0, y))
            comparison.paste(after, (1024, y))
            draw.rectangle((8, y + 8, 132, y + 34), fill=(20, 22, 24))
            draw.text((14, y + 12), identifier, fill=(255, 255, 255))
        args.comparison_sheet.parent.mkdir(parents=True, exist_ok=True)
        comparison.save(args.comparison_sheet, format="JPEG", quality=92, subsampling=0, optimize=True)

    report = {
        "schema_version": 1,
        "passed": not errors,
        "errors": errors,
        "asset_checks": asset_checks,
        "seam_checks": seams,
        "budget": budget,
        "water_mode": water["mode"],
        "source_service": source["service_url"],
        "source_year": source["catalog_year"],
        "source_resolution_meters": source["resolution_meters"],
        "runtime_note": "Static full mip-chain validation only; physical Quest residency and timing remain unmeasured.",
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    atomic_write_json(args.report, report)
    print(json.dumps(report, indent=2, sort_keys=True))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
