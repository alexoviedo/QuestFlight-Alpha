#!/usr/bin/env python3
"""Validate and assemble the already server-reprojected/cropped bounded NAIP exports."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image, ImageChops, ImageStat

from orthophoto_common import (
    atomic_write_json,
    file_record,
    iter_zone_tiles,
    load_config,
    require_external_directory,
    zone_bbox,
)


def suspicious_overlap(first: Image.Image, second: Image.Image, *, vertical: bool) -> dict:
    """Find a low-error repeated border strip; true neighbors should meet but not overlap."""
    first_small = first.convert("L").resize((256, 256), Image.Resampling.BOX)
    second_small = second.convert("L").resize((256, 256), Image.Resampling.BOX)
    best = {"overlap_pixels_at_256": 0, "mean_absolute_error": 255.0}
    for overlap in range(12, 97, 2):
        if vertical:
            first_strip = first_small.crop((0, 256 - overlap, 256, 256))
            second_strip = second_small.crop((0, 0, 256, overlap))
        else:
            first_strip = first_small.crop((256 - overlap, 0, 256, 256))
            second_strip = second_small.crop((0, 0, overlap, 256))
        error = float(ImageStat.Stat(ImageChops.difference(first_strip, second_strip)).mean[0])
        if error < best["mean_absolute_error"]:
            best = {"overlap_pixels_at_256": overlap, "mean_absolute_error": round(error, 6)}
    return best


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--raw-dir", required=True, type=Path)
    parser.add_argument("--work-dir", required=True, type=Path)
    parser.add_argument("--config", type=Path, default=None)
    args = parser.parse_args()

    config = load_config(args.config) if args.config else load_config()
    raw_dir = require_external_directory(args.raw_dir)
    work_dir = require_external_directory(args.work_dir)
    receipt_path = raw_dir / "fetch_receipt.json"
    if not receipt_path.exists():
        raise FileNotFoundError(f"Fetch receipt is missing: {receipt_path}")
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    receipt_tiles = {tile["file_name"]: tile for tile in receipt["tiles"]}

    outputs: list[dict] = []
    for zone in config["zones"]:
        size = int(zone["atlas_size_pixels"])
        grid = int(zone["tile_grid"])
        tile_size = size // grid
        atlas = Image.new("RGB", (size, size))
        loaded_tiles: dict[tuple[int, int], Image.Image] = {}
        source_tiles: list[dict] = []
        for tile in iter_zone_tiles(config, zone):
            path = raw_dir / tile["file_name"]
            if tile["file_name"] not in receipt_tiles:
                raise ValueError(f"Raw tile is absent from fetch receipt: {tile['file_name']}")
            with Image.open(path) as source_image:
                source_image.load()
                if source_image.size != (tile_size, tile_size):
                    raise ValueError(
                        f"Tile {path.name} is {source_image.size}, expected {(tile_size, tile_size)}"
                    )
                rgb = source_image.convert("RGB")
                atlas.paste(rgb, (int(tile["column"]) * tile_size, int(tile["row"]) * tile_size))
                loaded_tiles[(int(tile["row"]), int(tile["column"]))] = rgb.copy()
            source_tiles.append(file_record(path, relative_to=raw_dir))

        overlap_checks: list[dict] = []
        for row in range(grid):
            for column in range(grid):
                current = loaded_tiles[(row, column)]
                if row + 1 < grid:
                    result = suspicious_overlap(current, loaded_tiles[(row + 1, column)], vertical=True)
                    result.update({"first": [row, column], "second": [row + 1, column], "orientation": "vertical"})
                    overlap_checks.append(result)
                if column + 1 < grid:
                    result = suspicious_overlap(current, loaded_tiles[(row, column + 1)], vertical=False)
                    result.update({"first": [row, column], "second": [row, column + 1], "orientation": "horizontal"})
                    overlap_checks.append(result)
        suspicious = [result for result in overlap_checks if result["mean_absolute_error"] < 6.0]
        if suspicious:
            raise RuntimeError(f"Zone {zone['id']} contains a likely duplicated/overlapping tile strip: {suspicious}")

        output_path = work_dir / f"{zone['id']}_reprojected.png"
        # PNG is an external, lossless intermediate. Runtime output is JPEG then Android ASTC.
        atlas.save(output_path, format="PNG", optimize=False, compress_level=6)
        west, south, east, north = zone_bbox(config, float(zone["half_extent_meters"]))
        outputs.append(
            {
                "zone_id": zone["id"],
                "bbox_wgs84": [west, south, east, north],
                "coverage_meters": float(zone["half_extent_meters"]) * 2.0,
                "dimensions_pixels": [size, size],
                "nominal_meters_per_pixel": float(zone["half_extent_meters"]) * 2.0 / size,
                "projection_method": (
                    "USGS ImageServer resampling into an exact WGS84 envelope computed from the four "
                    "local-ENU coverage corners; north-up rows are preserved during deterministic assembly"
                ),
                "source_tiles": source_tiles,
                "duplicate_overlap_checks": overlap_checks,
                "output": file_record(output_path, relative_to=work_dir),
            }
        )
        print(output_path)

    report = {
        "schema_version": 1,
        "input_receipt": file_record(receipt_path, relative_to=raw_dir),
        "outputs": outputs,
    }
    atomic_write_json(work_dir / "reproject_crop_report.json", report)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
