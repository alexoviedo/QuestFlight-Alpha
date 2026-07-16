#!/usr/bin/env python3
"""Encode the normalized KBDU atlases and write their committed provenance manifest."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw, __version__ as pillow_version

from orthophoto_common import (
    DEFAULT_MANIFEST_PATH,
    DEFAULT_TEXTURE_DIR,
    REPO_ROOT,
    astc_mip_chain_bytes,
    atomic_write_json,
    file_record,
    image_channel_stats,
    load_config,
    require_external_directory,
    sha256_file,
    zone_bbox,
)


def pixel_sha256(image: Image.Image) -> str:
    digest = hashlib.sha256()
    digest.update(f"RGB:{image.width}x{image.height}\n".encode("ascii"))
    digest.update(image.convert("RGB").tobytes())
    return digest.hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--raw-dir", required=True, type=Path)
    parser.add_argument("--work-dir", required=True, type=Path)
    parser.add_argument("--output-dir", type=Path, default=DEFAULT_TEXTURE_DIR)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST_PATH)
    parser.add_argument("--config", type=Path, default=None)
    args = parser.parse_args()

    config_path = args.config.resolve() if args.config else (Path(__file__).resolve().parent / "config.json")
    config = load_config(config_path)
    raw_dir = require_external_directory(args.raw_dir)
    work_dir = require_external_directory(args.work_dir)
    output_dir = args.output_dir.resolve()
    manifest_path = args.manifest.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    manifest_path.parent.mkdir(parents=True, exist_ok=True)

    receipt_path = raw_dir / "fetch_receipt.json"
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    normalization_path = work_dir / "color_normalization_report.json"
    normalization = json.loads(normalization_path.read_text(encoding="utf-8"))
    assets: list[dict] = []
    preview_images: list[tuple[str, Image.Image]] = []
    runtime_texture_bytes = 0
    for zone in config["zones"]:
        input_path = work_dir / f"{zone['id']}_normalized.png"
        output_path = output_dir / zone["output_name"]
        with Image.open(input_path) as source:
            source.load()
            image = source.convert("RGB")
            expected_size = int(zone["atlas_size_pixels"])
            if image.size != (expected_size, expected_size):
                raise ValueError(f"{input_path} has dimensions {image.size}; expected {expected_size} square")
            content_hash = pixel_sha256(image)
            image.save(
                output_path,
                format="JPEG",
                quality=int(zone["jpeg_quality"]),
                subsampling=1,
                optimize=True,
                progressive=False,
                exif=b"",
            )
            preview = image.copy()
            preview.thumbnail((1024, 1024), Image.Resampling.LANCZOS)
            preview_images.append((zone["id"], preview))
            channel_stats = image_channel_stats(image)
        runtime_bytes = astc_mip_chain_bytes(expected_size, expected_size)
        runtime_texture_bytes += runtime_bytes
        west, south, east, north = zone_bbox(config, float(zone["half_extent_meters"]))
        asset_path = output_path.relative_to(REPO_ROOT).as_posix()
        assets.append(
            {
                "zone_id": zone["id"],
                "path": asset_path,
                "bytes": output_path.stat().st_size,
                "sha256": sha256_file(output_path),
                "decoded_pixel_sha256": content_hash,
                "dimensions_pixels": [expected_size, expected_size],
                "coverage_meters": float(zone["half_extent_meters"]) * 2.0,
                "nominal_meters_per_pixel": float(zone["half_extent_meters"]) * 2.0 / expected_size,
                "bbox_wgs84": [west, south, east, north],
                "jpeg_quality": int(zone["jpeg_quality"]),
                "jpeg_subsampling": "4:2:2",
                "channel_statistics": channel_stats,
                "android_import": {
                    "format": "ASTC_6x6",
                    "mipmaps": True,
                    "streaming_mipmaps": True,
                    "filter": "trilinear",
                    "anisotropy": 4,
                    "wrap": "clamp",
                    "estimated_mip_chain_bytes": runtime_bytes,
                },
            }
        )

    if runtime_texture_bytes > int(config["unity"]["maximum_runtime_texture_bytes"]):
        raise RuntimeError(
            f"Estimated ASTC mip memory {runtime_texture_bytes:,} exceeds "
            f"{config['unity']['maximum_runtime_texture_bytes']:,} bytes"
        )

    canvas = Image.new("RGB", (1024 * len(preview_images), 1080), (28, 31, 34))
    draw = ImageDraw.Draw(canvas)
    x = 0
    for zone_id, preview in preview_images:
        canvas.paste(preview, (x, 28))
        draw.text((x + 16, 1040), zone_id, fill=(240, 240, 240))
        x += 1024
    preview_path = work_dir / "orthophoto_atlas_contact_sheet.jpg"
    canvas.save(preview_path, format="JPEG", quality=90, subsampling=1, optimize=True)

    tile_lineage = [
        {
            "file_name": tile["file_name"],
            "zone_id": tile["zone_id"],
            "bbox_wgs84": tile["bbox_wgs84"],
            "actual_bbox_wgs84": tile["actual_bbox_wgs84"],
            "maximum_extent_error_degrees": tile["maximum_extent_error_degrees"],
            "dimensions_pixels": [tile["size_pixels"], tile["size_pixels"]],
            "bytes": tile["bytes"],
            "sha256": tile["sha256"],
            "request_sha256": tile["request_sha256"],
        }
        for tile in receipt["tiles"]
    ]
    script_records = []
    for name in (
        "config.json",
        "requirements.txt",
        "orthophoto_common.py",
        "fetch_naip_imagery.py",
        "reproject_crop_imagery.py",
        "color_normalize_imagery.py",
        "build_orthophoto_atlas.py",
        "build_terrain_pyramid_contract.py",
        "build_context_assets.py",
        "validate_orthophoto_assets.py",
    ):
        path = Path(__file__).resolve().parent / name
        if path.exists():
            script_records.append(file_record(path, relative_to=REPO_ROOT))

    manifest = {
        "schema_version": 1,
        "label": config["label"],
        "origin": config["origin"],
        "source": {
            **config["source"],
            "retrieved_utc": receipt["retrieved_utc"],
            "service_metadata_sha256": next(
                request["sha256"] for request in receipt["requests"] if request["id"] == "service_metadata"
            ),
            "catalog_sha256": next(request["sha256"] for request in receipt["requests"] if request["id"] == "catalog"),
            "catalog_features": receipt["catalog_features"],
            "raw_tiles": tile_lineage,
        },
        "processing": {
            "projection": (
                "Server-side USGS ImageServer NaturalColor reprojection/resampling into WGS84 envelopes "
                "computed from exact local-ENU square corners; deterministic top-origin assembly"
            ),
            "color_normalization": normalization,
            "python": sys.version.split()[0],
            "pillow": pillow_version,
            "tool_files": script_records,
        },
        "derived_assets": assets,
        "budgets": {
            "estimated_android_astc_mip_chain_bytes": runtime_texture_bytes,
            "maximum_runtime_texture_bytes": int(config["unity"]["maximum_runtime_texture_bytes"]),
            "draw_call_delta_if_integrated": 0,
            "triangle_delta_if_integrated": 0,
            "material_delta_if_integrated": 0,
        },
        "raw_data_policy": (
            "Raw ImageServer exports, lossless intermediates, previews, package caches, and fetch receipts remain external. "
            "Only the three optimized JPEG runtime derivatives and this provenance manifest enter the repository."
        ),
        "prohibited_sources_confirmation": "No Google Maps, Google Earth, Street View, paid, or unclear-license imagery is used.",
        "limitations": [
            "The imagery is a 2019 macro visual layer and is not current airport or navigation data.",
            "JPEG repository assets are transcoded to ASTC by Unity for Android; simulator appearance is not Quest performance evidence.",
            "Unity integration and stereo visual QA are required before the material switch is accepted.",
        ],
    }
    atomic_write_json(manifest_path, manifest)
    print(manifest_path)
    print(preview_path)
    for asset in assets:
        print(f"{asset['path']}: {asset['bytes']:,} bytes, {asset['sha256']}")
    print(f"Estimated Android ASTC mip memory: {runtime_texture_bytes:,} bytes")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
