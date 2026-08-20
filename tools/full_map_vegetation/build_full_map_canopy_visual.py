#!/usr/bin/env python3
"""Build the no-new-draw full-map canopy normal/coverage product.

Every 2020 high-resolution ground page is segmented at 0.667 m analysis
resolution, then conservatively integrated into a single 4096-square runtime
texture.  R is canopy coverage, G is low/shrub vegetation, and BA encode the
horizontal components of a smoothed aggregate canopy normal.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

from build_full_map_vegetation import BuildingIndex, canopy_mask, rasterize_buildings


OUTPUT_SIZE = 4096
WORLD_MIN = -12000.0
WORLD_SIZE = 24000.0
ANALYSIS_DOWNSAMPLE = 2


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--imagery-root", type=Path, required=True)
    parser.add_argument("--clipmap-manifest", type=Path, required=True)
    parser.add_argument("--building-database", type=Path, required=True)
    parser.add_argument("--placement-database", type=Path, required=True)
    parser.add_argument("--semantic-exclusion-mask", type=Path, required=True)
    parser.add_argument("--semantic-exclusion-manifest", type=Path, required=True)
    parser.add_argument("--output-texture", type=Path, required=True)
    parser.add_argument("--output-manifest", type=Path, required=True)
    parser.add_argument("--debug-preview", type=Path)
    return parser.parse_args()


def placement_support(path: Path) -> tuple[np.ndarray, np.ndarray]:
    header = struct.Struct("<8sIffffIII")
    page = struct.Struct("<II")
    instance = struct.Struct("<HHiHHHBBBBB")
    data = path.read_bytes()
    magic, version, minimum_x, minimum_z, page_size, quantization, columns, rows, count = (
        header.unpack_from(data, 0)
    )
    if (magic, version) not in ((b"QFFMVG2\0", 2), (b"QFFMVG3\0", 3)):
        raise ValueError("Canopy support requires a full-map V2/V3 vegetation database")
    support = np.zeros((OUTPUT_SIZE, OUTPUT_SIZE), dtype=np.uint8)
    shrub = np.zeros_like(support)
    table_offset = header.size
    records_offset = header.size + columns * rows * page.size
    meters_per_pixel = WORLD_SIZE / OUTPUT_SIZE
    for page_index in range(columns * rows):
        offset, page_count = page.unpack_from(data, table_offset + page_index * page.size)
        column = page_index % columns
        row = page_index // columns
        page_x = minimum_x + column * page_size
        page_z = minimum_z + row * page_size
        for local in range(page_count):
            raw = instance.unpack_from(data, records_offset + (offset + local) * instance.size)
            if version == 3 and raw[10] == 3:
                continue
            x = page_x + raw[0] * quantization
            z = page_z + raw[1] * quantization
            height = raw[3] * 0.01
            crown = raw[4] * 0.01
            pixel_x = int(round((x - WORLD_MIN) / WORLD_SIZE * OUTPUT_SIZE))
            pixel_y = int(round((WORLD_MIN + WORLD_SIZE - z) / WORLD_SIZE * OUTPUT_SIZE))
            radius = max(1, int(round(crown * 0.58 / meters_per_pixel)))
            if 0 <= pixel_x < OUTPUT_SIZE and 0 <= pixel_y < OUTPUT_SIZE:
                cv2.circle(support, (pixel_x, pixel_y), radius, 255, -1, cv2.LINE_AA)
                if raw[10] == 2 or (height <= 7.5 and crown <= 7.0):
                    cv2.circle(shrub, (pixel_x, pixel_y), radius, 255, -1, cv2.LINE_AA)
    support = cv2.morphologyEx(
        support,
        cv2.MORPH_CLOSE,
        cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3)),
    )
    return support.astype(np.float32) / 255.0, shrub.astype(np.float32) / 255.0


def destination_rect(minimum_x, minimum_z, maximum_x, maximum_z):
    x0 = int(round((minimum_x - WORLD_MIN) / WORLD_SIZE * OUTPUT_SIZE))
    x1 = int(round((maximum_x - WORLD_MIN) / WORLD_SIZE * OUTPUT_SIZE))
    y0 = int(round((WORLD_MIN + WORLD_SIZE - maximum_z) / WORLD_SIZE * OUTPUT_SIZE))
    y1 = int(round((WORLD_MIN + WORLD_SIZE - minimum_z) / WORLD_SIZE * OUTPUT_SIZE))
    return x0, y0, x1, y1


def main() -> None:
    args = parse_args()
    clipmap = json.loads(args.clipmap_manifest.read_text(encoding="utf-8"))
    high = clipmap["tiers"]["high"]
    buildings = BuildingIndex(args.building_database)
    coverage = np.zeros((OUTPUT_SIZE, OUTPUT_SIZE), dtype=np.float32)
    shrubs = np.zeros_like(coverage)
    source_pages = [item for item in clipmap["pages"] if item["tier"] == "high"]
    source_pages.sort(key=lambda item: (int(item["row"]), int(item["column"])))

    for ordinal, page in enumerate(source_pages, 1):
        source = args.imagery_root / f"{page['asset_name']}.jpg"
        image = cv2.imread(str(source), cv2.IMREAD_COLOR)
        if image is None:
            raise FileNotFoundError(source)
        gutter = int(page["gutter_pixels"])
        inner = image[gutter:-gutter, gutter:-gutter]
        analysis = cv2.resize(
            inner,
            (inner.shape[1] // ANALYSIS_DOWNSAMPLE, inner.shape[0] // ANALYSIS_DOWNSAMPLE),
            interpolation=cv2.INTER_AREA,
        )
        minimum_x = float(high["grid_min_x"]) + int(page["column"]) * float(high["tile_inner_size_meters"])
        minimum_z = float(high["grid_min_z"]) + int(page["row"]) * float(high["tile_inner_size_meters"])
        maximum_x = minimum_x + float(high["tile_inner_size_meters"])
        maximum_z = minimum_z + float(high["tile_inner_size_meters"])
        building_mask = rasterize_buildings(
            buildings, minimum_x, minimum_z, maximum_x, maximum_z, analysis.shape[:2]
        )
        mask, confidence = canopy_mask(analysis, building_mask)
        binary = (mask != 0).astype(np.uint8)
        # Low vegetation is foliage removed by a roughly 4 m morphological core.
        # It intentionally includes hedges and shrubs while excluding broad lawns.
        woody_core = cv2.morphologyEx(
            binary,
            cv2.MORPH_OPEN,
            cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (7, 7)),
        )
        shrub_mask = binary * (1 - woody_core)
        x0, y0, x1, y1 = destination_rect(minimum_x, minimum_z, maximum_x, maximum_z)
        clipped_x0, clipped_y0 = max(0, x0), max(0, y0)
        clipped_x1, clipped_y1 = min(OUTPUT_SIZE, x1), min(OUTPUT_SIZE, y1)
        if clipped_x1 > clipped_x0 and clipped_y1 > clipped_y0:
            width, height = x1 - x0, y1 - y0
            page_coverage = cv2.resize(
                binary.astype(np.float32), (width, height), interpolation=cv2.INTER_AREA
            )
            page_shrubs = cv2.resize(
                shrub_mask.astype(np.float32), (width, height), interpolation=cv2.INTER_AREA
            )
            source_x0, source_y0 = clipped_x0 - x0, clipped_y0 - y0
            source_x1 = source_x0 + clipped_x1 - clipped_x0
            source_y1 = source_y0 + clipped_y1 - clipped_y0
            coverage[clipped_y0:clipped_y1, clipped_x0:clipped_x1] = np.maximum(
                coverage[clipped_y0:clipped_y1, clipped_x0:clipped_x1],
                page_coverage[source_y0:source_y1, source_x0:source_x1],
            )
            shrubs[clipped_y0:clipped_y1, clipped_x0:clipped_x1] = np.maximum(
                shrubs[clipped_y0:clipped_y1, clipped_x0:clipped_x1],
                page_shrubs[source_y0:source_y1, source_x0:source_x1],
            )
        if ordinal % 24 == 0 or ordinal == len(source_pages):
            print(f"processed {ordinal}/{len(source_pages)} high-resolution pages", flush=True)

    accepted_support, accepted_shrubs = placement_support(args.placement_database)
    semantic = np.asarray(Image.open(args.semantic_exclusion_mask).convert("RGB"))
    if semantic.shape[:2] != (OUTPUT_SIZE, OUTPUT_SIZE):
        raise ValueError("Semantic exclusion mask does not match the canopy output grid")
    hard_exclusion = np.max(semantic[:, :, :2], axis=2) >= 128
    coverage = cv2.GaussianBlur(coverage, (0, 0), 0.72)
    coverage = np.clip((coverage - 0.055) / 0.78, 0.0, 1.0)
    coverage = np.maximum(coverage * accepted_support, accepted_support * 0.58)
    shrubs = np.maximum(
        np.clip(cv2.GaussianBlur(shrubs, (0, 0), 0.55), 0.0, 1.0) * coverage,
        accepted_shrubs * 0.62,
    )
    coverage[hard_exclusion] = 0.0
    shrubs[hard_exclusion] = 0.0
    relief = cv2.GaussianBlur(coverage, (0, 0), 2.2)
    gradient_x = cv2.Sobel(relief, cv2.CV_32F, 1, 0, ksize=3) * 1.65
    gradient_z = -cv2.Sobel(relief, cv2.CV_32F, 0, 1, ksize=3) * 1.65
    normal_x = -gradient_x
    normal_z = -gradient_z
    normal_y = np.ones_like(normal_x)
    inverse = 1.0 / np.sqrt(normal_x * normal_x + normal_y * normal_y + normal_z * normal_z)
    normal_x *= inverse
    normal_z *= inverse
    normal_x[hard_exclusion] = 0.0
    normal_z[hard_exclusion] = 0.0

    rgba = np.empty((OUTPUT_SIZE, OUTPUT_SIZE, 4), dtype=np.uint8)
    rgba[:, :, 0] = np.rint(coverage * 255.0).astype(np.uint8)
    rgba[:, :, 1] = np.rint(shrubs * 255.0).astype(np.uint8)
    rgba[:, :, 2] = np.rint((normal_x * 0.5 + 0.5) * 255.0).astype(np.uint8)
    rgba[:, :, 3] = np.rint((normal_z * 0.5 + 0.5) * 255.0).astype(np.uint8)
    args.output_texture.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(rgba).save(args.output_texture, optimize=True)

    if args.debug_preview is not None:
        preview = np.zeros((1024, 1024, 3), dtype=np.uint8)
        small_coverage = cv2.resize(coverage, (1024, 1024), interpolation=cv2.INTER_AREA)
        small_shrubs = cv2.resize(shrubs, (1024, 1024), interpolation=cv2.INTER_AREA)
        preview[:, :, 1] = np.rint(small_coverage * 255.0).astype(np.uint8)
        preview[:, :, 2] = np.rint(small_shrubs * 220.0).astype(np.uint8)
        args.debug_preview.parent.mkdir(parents=True, exist_ok=True)
        cv2.imwrite(str(args.debug_preview), preview)

    manifest = {
        "schema": "questflight.full-map-canopy-visual.v1.1",
        "contract": {
            "bounds_meters": [WORLD_MIN, WORLD_MIN, WORLD_MIN + WORLD_SIZE, WORLD_MIN + WORLD_SIZE],
            "texture_size": [OUTPUT_SIZE, OUTPUT_SIZE],
            "meters_per_texel": WORLD_SIZE / OUTPUT_SIZE,
            "channels": {
                "r": "feathered woody vegetation coverage",
                "g": "bounded hedge/shrub/low-vegetation fraction",
                "b": "aggregate canopy normal X encoded 0..1",
                "a": "aggregate canopy normal Z encoded 0..1",
            },
            "runtime_draws": 0,
        },
        "processing": {
            "generator": "tools/full_map_vegetation/build_full_map_canopy_visual.py",
            "analysis_meters_per_pixel": float(high["meters_per_texel"]) * ANALYSIS_DOWNSAMPLE,
            "segmentation": "source RGB texture/vegetation classifier with building-footprint exclusion",
            "normal": "smoothed coverage gradient; aggregate far-canopy relief only",
            "semantic_exclusion": (
                "water interiors and verified athletic playing surfaces are cleared after all feathering"
            ),
        },
        "sources": {
            "clipmap_manifest": {"path": str(args.clipmap_manifest), "sha256": sha256(args.clipmap_manifest)},
            "building_database": {"path": str(args.building_database), "sha256": sha256(args.building_database)},
            "placement_database": {"path": str(args.placement_database), "sha256": sha256(args.placement_database)},
            "semantic_exclusion_mask": {
                "path": str(args.semantic_exclusion_mask),
                "sha256": sha256(args.semantic_exclusion_mask),
            },
            "semantic_exclusion_manifest": {
                "path": str(args.semantic_exclusion_manifest),
                "sha256": sha256(args.semantic_exclusion_manifest),
            },
            "imagery_acquisition": "2020",
            "raw_imagery_committed": False,
        },
        "statistics": {
            "source_pages": len(source_pages),
            "canopy_coverage_fraction": round(float(np.mean(coverage)), 6),
            "canopy_supported_texels": int(np.count_nonzero(coverage >= 0.12)),
            "shrub_supported_texels": int(np.count_nonzero(shrubs >= 0.12)),
            "runtime_bytes": args.output_texture.stat().st_size,
            "runtime_sha256": sha256(args.output_texture),
            "semantic_excluded_texels": int(np.count_nonzero(hard_exclusion)),
        },
    }
    args.output_manifest.parent.mkdir(parents=True, exist_ok=True)
    args.output_manifest.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest["statistics"], indent=2))


if __name__ == "__main__":
    main()
