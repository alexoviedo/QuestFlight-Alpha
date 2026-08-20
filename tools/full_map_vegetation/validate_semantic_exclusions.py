#!/usr/bin/env python3
"""Fail-closed validation for the V3.1 vegetation semantic correction."""

from __future__ import annotations

import argparse
import json
import struct
from collections import Counter
from pathlib import Path

import cv2
import numpy as np
from PIL import Image


HEADER = struct.Struct("<8sIffffIII")
PAGE = struct.Struct("<II")
INSTANCE = struct.Struct("<HHiHHHBBBBB")
WORLD_MIN = -12_000.0
WORLD_SIZE = 24_000.0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--database", type=Path, required=True)
    parser.add_argument("--database-manifest", type=Path, required=True)
    parser.add_argument("--semantic-mask", type=Path, required=True)
    parser.add_argument("--semantic-manifest", type=Path, required=True)
    parser.add_argument("--canopy-texture", type=Path, required=True)
    parser.add_argument("--output-report", type=Path)
    return parser.parse_args()


def pixel(x: float, z: float, size: int) -> tuple[int, int]:
    column = int(np.clip(round((x - WORLD_MIN) / WORLD_SIZE * (size - 1)), 0, size - 1))
    row = int(np.clip(round((WORLD_MIN + WORLD_SIZE - z) / WORLD_SIZE * (size - 1)), 0, size - 1))
    return row, column


def main() -> None:
    args = parse_args()
    semantic = np.asarray(Image.open(args.semantic_mask).convert("RGB"))
    hard = np.max(semantic[:, :, :2], axis=2) >= 128
    clear = (~hard).astype(np.uint8)
    meters_per_texel = WORLD_SIZE / semantic.shape[0]
    clearance = cv2.distanceTransform(clear, cv2.DIST_L2, 5) * meters_per_texel

    data = args.database.read_bytes()
    magic, version, minimum_x, minimum_z, page_size, quantization, columns, rows, count = (
        HEADER.unpack_from(data, 0)
    )
    if (magic, version) != (b"QFFMVG3\0", 3):
        raise ValueError("Semantic validation requires the V3 runtime database")
    table_offset = HEADER.size
    records_offset = HEADER.size + columns * rows * PAGE.size
    violations = Counter()
    kinds = Counter()
    for page_index in range(columns * rows):
        offset, page_count = PAGE.unpack_from(data, table_offset + page_index * PAGE.size)
        page_x = minimum_x + (page_index % columns) * page_size
        page_z = minimum_z + (page_index // columns) * page_size
        for local in range(page_count):
            raw = INSTANCE.unpack_from(data, records_offset + (offset + local) * INSTANCE.size)
            x = page_x + raw[0] * quantization
            z = page_z + raw[1] * quantization
            kind = raw[10]
            kinds[kind] += 1
            row, column = pixel(x, z, semantic.shape[0])
            if semantic[row, column, 0] >= 128:
                violations["water_center"] += 1
            if semantic[row, column, 1] >= 128:
                violations["athletic_surface_center"] += 1
            if kind == 3:
                radius = raw[4] * 0.005
                if radius > float(clearance[row, column]) + meters_per_texel:
                    violations["grove_crown_crosses_hard_exclusion"] += 1

    canopy = np.asarray(Image.open(args.canopy_texture).convert("RGBA"))
    if canopy.shape[:2] != hard.shape:
        raise ValueError("Canopy texture and semantic mask grids differ")
    excluded_canopy = canopy[hard]
    canopy_violations = {
        "coverage_nonzero": int(np.count_nonzero(excluded_canopy[:, 0])),
        "shrub_nonzero": int(np.count_nonzero(excluded_canopy[:, 1])),
        "normal_x_not_neutral": int(np.count_nonzero(excluded_canopy[:, 2] != 128)),
        "normal_z_not_neutral": int(np.count_nonzero(excluded_canopy[:, 3] != 128)),
    }

    database_manifest = json.loads(args.database_manifest.read_text(encoding="utf-8"))
    semantic_manifest = json.loads(args.semantic_manifest.read_text(encoding="utf-8"))
    statistics = database_manifest["statistics"]
    rejected = statistics["semantic_rejections"]
    source_count = int(statistics["source_individuals"])
    retained = int(statistics["retained_individuals"])
    rejected_total = int(rejected["water"]) + int(rejected["athletic_surface"])
    accounting_valid = source_count == retained + rejected_total

    report = {
        "database_records": count,
        "record_kinds": dict(sorted((str(key), value) for key, value in kinds.items())),
        "semantic_hard_texels": int(np.count_nonzero(hard)),
        "sports_polygon_count": int(semantic_manifest["statistics"]["sports_polygon_count"]),
        "record_violations": dict(sorted(violations.items())),
        "canopy_violations": canopy_violations,
        "source_accounting_valid": accounting_valid,
        "result": "PASS",
    }
    if violations or any(canopy_violations.values()) or not accounting_valid:
        report["result"] = "FAIL"
    if report["sports_polygon_count"] < 100:
        report["result"] = "FAIL"
        report["sports_coverage_error"] = "Athletic-surface source is unexpectedly sparse"

    if args.output_report is not None:
        args.output_report.parent.mkdir(parents=True, exist_ok=True)
        args.output_report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    if report["result"] != "PASS":
        raise SystemExit(1)


if __name__ == "__main__":
    main()
