#!/usr/bin/env python3
"""Apply one bounded grade and match every outer level to its inner footprint."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image, ImageEnhance

from orthophoto_common import (
    atomic_write_json,
    file_record,
    image_channel_stats,
    load_config,
    require_external_directory,
)


def apply_base_grade(image: Image.Image, settings: dict) -> Image.Image:
    rgb = image.convert("RGB")
    rgb = ImageEnhance.Brightness(rgb).enhance(float(settings["brightness"]))
    rgb = ImageEnhance.Contrast(rgb).enhance(float(settings["contrast"]))
    rgb = ImageEnhance.Color(rgb).enhance(float(settings["saturation"]))
    gains = (float(settings["red_gain"]), float(settings["green_gain"]), float(settings["blue_gain"]))
    channels = rgb.split()
    corrected = [channel.point([min(255, round(value * gain)) for value in range(256)]) for channel, gain in zip(channels, gains)]
    return Image.merge("RGB", corrected)


def overlap_adjustment(
    inner: Image.Image,
    outer: Image.Image,
    footprint_ratio: float,
    settings: dict,
) -> tuple[list[float], list[float], list[dict], list[dict]]:
    overlap_size = max(1, round(outer.width * footprint_ratio))
    left = (outer.width - overlap_size) // 2
    upper = (outer.height - overlap_size) // 2
    outer_overlap = outer.crop((left, upper, left + overlap_size, upper + overlap_size))
    inner_stats = image_channel_stats(inner)
    outer_stats = image_channel_stats(outer_overlap)
    gains: list[float] = []
    offsets: list[float] = []
    minimum = float(settings["mid_overlap_gain_min"])
    maximum = float(settings["mid_overlap_gain_max"])
    offset_limit = float(settings["mid_overlap_offset_limit"])
    for inner_channel, outer_channel in zip(inner_stats, outer_stats):
        raw_gain = inner_channel["standard_deviation"] / max(outer_channel["standard_deviation"], 1e-6)
        gain = max(minimum, min(maximum, raw_gain))
        raw_offset = inner_channel["mean"] - outer_channel["mean"] * gain
        offset = max(-offset_limit, min(offset_limit, raw_offset))
        gains.append(gain)
        offsets.append(offset)
    return gains, offsets, inner_stats, outer_stats


def apply_linear_channels(image: Image.Image, gains: list[float], offsets: list[float]) -> Image.Image:
    channels = image.convert("RGB").split()
    corrected = []
    for channel, gain, offset in zip(channels, gains, offsets):
        corrected.append(channel.point([max(0, min(255, round(value * gain + offset))) for value in range(256)]))
    return Image.merge("RGB", corrected)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work-dir", required=True, type=Path)
    parser.add_argument("--config", type=Path, default=None)
    args = parser.parse_args()
    config = load_config(args.config) if args.config else load_config()
    work_dir = require_external_directory(args.work_dir)
    settings = config["color_normalization"]

    normalized: dict[str, Image.Image] = {}
    zone_reports: dict[str, dict] = {}
    overlap_matches: list[dict] = []
    for index, zone in enumerate(config["zones"]):
        zone_id = zone["id"]
        input_path = work_dir / f"{zone_id}_reprojected.png"
        with Image.open(input_path) as source:
            source.load()
            before = image_channel_stats(source)
            image = apply_base_grade(source, settings)
        if index:
            inner_zone = config["zones"][index - 1]
            inner_id = inner_zone["id"]
            footprint_ratio = float(inner_zone["half_extent_meters"]) / float(zone["half_extent_meters"])
            gains, offsets, inner_stats, outer_stats = overlap_adjustment(
                normalized[inner_id], image, footprint_ratio, settings
            )
            image = apply_linear_channels(image, gains, offsets)
            overlap_matches.append(
                {
                    "inner_zone_id": inner_id,
                    "outer_zone_id": zone_id,
                    "inner_footprint_ratio": footprint_ratio,
                    "inner_overlap_target": inner_stats,
                    "outer_overlap_before_match": outer_stats,
                    "outer_linear_gain": [round(value, 8) for value in gains],
                    "outer_linear_offset": [round(value, 8) for value in offsets],
                }
            )
        normalized[zone_id] = image
        output_path = work_dir / f"{zone_id}_normalized.png"
        image.save(output_path, format="PNG", optimize=False, compress_level=6)
        zone_reports[zone_id] = {
            "before": before,
            "after": image_channel_stats(image),
            "output": file_record(output_path, relative_to=work_dir),
        }

    report = {
        "schema_version": 1,
        "settings": settings,
        "zones": zone_reports,
        "overlap_matches": overlap_matches,
        "outputs": [zone_reports[zone["id"]]["output"] for zone in config["zones"]],
    }
    atomic_write_json(work_dir / "color_normalization_report.json", report)
    for zone in config["zones"]:
        print(work_dir / f"{zone['id']}_normalized.png")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
