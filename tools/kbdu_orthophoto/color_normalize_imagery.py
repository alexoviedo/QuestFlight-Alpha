#!/usr/bin/env python3
"""Apply a bounded natural-color grade and match the mid mosaic to the near overlap."""

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


def overlap_adjustment(near: Image.Image, mid: Image.Image, settings: dict) -> tuple[list[float], list[float], list[dict], list[dict]]:
    # The near 4 km footprint occupies the central third of the 12 km mid image.
    left = mid.width // 3
    upper = mid.height // 3
    right = mid.width - left
    lower = mid.height - upper
    mid_overlap = mid.crop((left, upper, right, lower))
    near_stats = image_channel_stats(near)
    mid_stats = image_channel_stats(mid_overlap)
    gains: list[float] = []
    offsets: list[float] = []
    minimum = float(settings["mid_overlap_gain_min"])
    maximum = float(settings["mid_overlap_gain_max"])
    offset_limit = float(settings["mid_overlap_offset_limit"])
    for near_channel, mid_channel in zip(near_stats, mid_stats):
        raw_gain = near_channel["standard_deviation"] / max(mid_channel["standard_deviation"], 1e-6)
        gain = max(minimum, min(maximum, raw_gain))
        raw_offset = near_channel["mean"] - mid_channel["mean"] * gain
        offset = max(-offset_limit, min(offset_limit, raw_offset))
        gains.append(gain)
        offsets.append(offset)
    return gains, offsets, near_stats, mid_stats


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

    near_input = work_dir / "near_4km_reprojected.png"
    mid_input = work_dir / "mid_12km_reprojected.png"
    with Image.open(near_input) as source:
        source.load()
        near_before = image_channel_stats(source)
        near = apply_base_grade(source, settings)
    with Image.open(mid_input) as source:
        source.load()
        mid_before = image_channel_stats(source)
        mid_base = apply_base_grade(source, settings)

    gains, offsets, near_overlap_stats, mid_overlap_stats = overlap_adjustment(near, mid_base, settings)
    mid = apply_linear_channels(mid_base, gains, offsets)
    near_output = work_dir / "near_4km_normalized.png"
    mid_output = work_dir / "mid_12km_normalized.png"
    near.save(near_output, format="PNG", optimize=False, compress_level=6)
    mid.save(mid_output, format="PNG", optimize=False, compress_level=6)

    report = {
        "schema_version": 1,
        "settings": settings,
        "near_before": near_before,
        "near_after": image_channel_stats(near),
        "mid_before": mid_before,
        "mid_after": image_channel_stats(mid),
        "near_overlap_target": near_overlap_stats,
        "mid_overlap_before_match": mid_overlap_stats,
        "mid_overlap_linear_gain": [round(value, 8) for value in gains],
        "mid_overlap_linear_offset": [round(value, 8) for value in offsets],
        "outputs": [
            file_record(near_output, relative_to=work_dir),
            file_record(mid_output, relative_to=work_dir),
        ],
    }
    atomic_write_json(work_dir / "color_normalization_report.json", report)
    print(near_output)
    print(mid_output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
