#!/usr/bin/env python3
"""Validate deterministic full-map vegetation scale evidence."""

from __future__ import annotations

import argparse
import collections
import json
import struct
from pathlib import Path

import numpy as np


HEADER = struct.Struct("<8sIffffIII")
PAGE = struct.Struct("<II")
INSTANCE = struct.Struct("<HHiHHHBBBBB")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--database", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--baseline-database", type=Path)
    parser.add_argument("--output", type=Path)
    return parser.parse_args()


def quantiles(values: np.ndarray) -> dict[str, float]:
    return {
        key: round(float(np.quantile(values, percentile)), 3)
        for key, percentile in (
            ("minimum", 0.0),
            ("p05", 0.05),
            ("p25", 0.25),
            ("median", 0.50),
            ("p75", 0.75),
            ("p95", 0.95),
            ("maximum", 1.0),
        )
    }


def analyze(database: Path, manifest_path: Path, baseline_database: Path | None = None) -> dict:
    payload = database.read_bytes()
    magic, version, _min_x, _min_z, _page_size, _quantization, columns, rows, total = (
        HEADER.unpack_from(payload, 0)
    )
    if magic != b"QFFMVG2\0" or version != 2:
        raise ValueError("Unsupported full-map vegetation database")
    page_count = columns * rows
    record_offset = HEADER.size + page_count * PAGE.size
    expected_bytes = record_offset + total * INSTANCE.size
    if len(payload) != expected_bytes:
        raise ValueError(f"Database length mismatch: {len(payload)} != {expected_bytes}")

    expected_offset = 0
    for page in range(page_count):
        offset, count = PAGE.unpack_from(payload, HEADER.size + page * PAGE.size)
        if offset != expected_offset or count > 1600:
            raise ValueError(f"Invalid page table record {page}")
        expected_offset += count
    if expected_offset != total:
        raise ValueError("Page table does not cover every instance")

    heights = np.empty(total, dtype=np.float64)
    crowns = np.empty(total, dtype=np.float64)
    kinds = collections.Counter()
    for index in range(total):
        values = INSTANCE.unpack_from(payload, record_offset + index * INSTANCE.size)
        heights[index] = values[3] * 0.01
        crowns[index] = values[4] * 0.01
        kinds[int(values[10])] += 1

    height_codes = np.rint(heights * 100.0).astype(np.int64)
    crown_codes = np.rint(crowns * 100.0).astype(np.int64)
    height_modes = collections.Counter(height_codes.tolist())
    crown_modes = collections.Counter(crown_codes.tolist())
    height_mode_code, height_mode_count = height_modes.most_common(1)[0]
    crown_mode_code, crown_mode_count = crown_modes.most_common(1)[0]
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    method = manifest["processing"]["height_method"]

    checks = {
        "instance_count_matches_manifest": total
        == int(manifest["statistics"]["runtime_instances"]),
        "height_method_rejects_hash_randomness": "no hash-derived height" in method,
        "height_range_is_plausible": float(np.min(heights)) >= 0.75
        and float(np.max(heights)) <= 30.0,
        "crown_range_is_plausible": float(np.min(crowns)) >= 1.5
        and float(np.max(crowns)) <= 20.0,
        "height_has_meaningful_range": float(np.quantile(heights, 0.95) - np.quantile(heights, 0.05))
        >= 7.5,
        "height_has_no_dominant_mode": height_mode_count / total <= 0.05,
        "crown_has_no_dominant_mode": crown_mode_count / total <= 0.10,
        "legacy_height_cap_removed": int(np.count_nonzero(height_codes == 2350)) / total
        <= 0.01,
        "legacy_crown_cap_removed": int(np.count_nonzero(crown_codes == 1550)) / total
        <= 0.01,
        "centimeter_height_values_are_varied": len(height_modes) >= 500,
        "centimeter_crown_values_are_varied": len(crown_modes) >= 250,
    }
    preserved_fields = None
    if baseline_database is not None:
        baseline = baseline_database.read_bytes()
        if len(baseline) != len(payload) or baseline[:record_offset] != payload[:record_offset]:
            preserved_fields = False
        else:
            preserved_fields = True
            immutable_indices = (0, 1, 2, 5, 6, 7, 8, 9)
            for index in range(total):
                position = record_offset + index * INSTANCE.size
                before = INSTANCE.unpack_from(baseline, position)
                after = INSTANCE.unpack_from(payload, position)
                if any(before[field] != after[field] for field in immutable_indices):
                    preserved_fields = False
                    break
        checks["placement_grounding_tint_rotation_and_seed_preserved"] = preserved_fields
    report = {
        "schema": "questflight.full-map-vegetation-height-analysis.v1",
        "database": str(database),
        "manifest": str(manifest_path),
        "instance_count": total,
        "height_meters": {
            **quantiles(heights),
            "unique_centimeter_values": len(height_modes),
            "mode": height_mode_code * 0.01,
            "mode_count": height_mode_count,
            "mode_fraction": round(height_mode_count / total, 6),
            "legacy_23_5_fraction": round(
                int(np.count_nonzero(height_codes == 2350)) / total, 6
            ),
        },
        "crown_meters": {
            **quantiles(crowns),
            "unique_centimeter_values": len(crown_modes),
            "mode": crown_mode_code * 0.01,
            "mode_count": crown_mode_count,
            "mode_fraction": round(crown_mode_count / total, 6),
            "legacy_15_5_fraction": round(
                int(np.count_nonzero(crown_codes == 1550)) / total, 6
            ),
        },
        "height_crown_correlation": round(float(np.corrcoef(heights, crowns)[0, 1]), 6),
        "kinds": dict(sorted(kinds.items())),
        "height_sources": manifest["statistics"].get("height_sources", {}),
        "immutable_runtime_fields_preserved": preserved_fields,
        "checks": checks,
        "passed": all(checks.values()),
    }
    return report


def main() -> None:
    args = parse_args()
    report = analyze(args.database, args.manifest, args.baseline_database)
    output = json.dumps(report, indent=2) + "\n"
    if args.output is not None:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(output, encoding="utf-8")
    print(output, end="")
    if not report["passed"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
