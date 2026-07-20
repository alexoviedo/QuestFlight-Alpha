#!/usr/bin/env python3
"""Plan, fetch, or verify the bounded USGS 3DEP EPT snapshot outside Git."""

from __future__ import annotations

import argparse
import importlib.util
import json
import sys
from pathlib import Path
from typing import Any

from build_dtm_dsm import REPOSITORY_ROOT, SOURCE_COMMIT, SOURCE_MANIFEST, load_json


LEGACY_CONFIG = REPOSITORY_ROOT / "tools/scenery/lidar/config.json"
LEGACY_FETCH = REPOSITORY_ROOT / "tools/scenery/lidar/ept_snapshot.py"


def ensure_external_artifact_root(path: Path) -> Path:
    resolved = path.expanduser().resolve()
    repository = REPOSITORY_ROOT.resolve()
    try:
        resolved.relative_to(repository)
    except ValueError:
        return resolved
    raise ValueError(
        "Raw EPT/LAZ artifacts must remain outside the repository; choose an external --artifact-root"
    )


def fetch_plan(source_manifest_path: Path = SOURCE_MANIFEST) -> dict[str, Any]:
    source = load_json(source_manifest_path)
    source_record = source["source"]
    mapping = source["coordinate_mapping"]
    return {
        "dataset_id": source_record["dataset_id"],
        "depth": int(source_record["target_depth"]),
        "ept_url": source_record["ept_url"],
        "expected_data_file_count": int(source_record["data_file_count"]),
        "expected_data_inventory_sha256": source_record["data_inventory_sha256"],
        "expected_raw_snapshot_manifest_sha256": source_record[
            "raw_snapshot_manifest_sha256"
        ],
        "nominal_ept_resolution_meters": float(source_record["nominal_ept_resolution_meters"]),
        "product_bounds_utm13n_meters": mapping["grid"]["product_bounds_utm13n_meters"],
        "product_grid": {
            "height_pixels": int(mapping["grid"]["height_pixels"]),
            "pixel_size_meters": float(mapping["grid"]["pixel_size_meters"]),
            "width_pixels": int(mapping["grid"]["width_pixels"]),
        },
        "raw_data_policy": "External only; raw EPT metadata, hierarchy, and LAZ are prohibited from Git.",
        "source_commit": SOURCE_COMMIT,
        "stac_item_url": source_record["stac_item_url"],
    }


def _load_legacy_fetch_module() -> Any:
    sys.path.insert(0, str(LEGACY_FETCH.parent))
    spec = importlib.util.spec_from_file_location("questflight_ept_snapshot", LEGACY_FETCH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load {LEGACY_FETCH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def verify_snapshot(artifact_root: Path) -> dict[str, Any]:
    module = _load_legacy_fetch_module()
    return module.verify_offline_snapshot(ensure_external_artifact_root(artifact_root))


def download_snapshot(
    artifact_root: Path,
    workers: int,
    refresh: bool,
    retrieved_utc: str | None,
) -> dict[str, Any]:
    if not 1 <= workers <= 32:
        raise ValueError("--workers must be between 1 and 32")
    module = _load_legacy_fetch_module()
    return module.build_snapshot(
        LEGACY_CONFIG,
        ensure_external_artifact_root(artifact_root),
        workers=workers,
        refresh=refresh,
        retrieved_utc=retrieved_utc,
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--artifact-root", type=Path)
    parser.add_argument("--download", action="store_true")
    parser.add_argument("--verify", action="store_true")
    parser.add_argument("--workers", type=int, default=12)
    parser.add_argument("--refresh", action="store_true")
    parser.add_argument("--retrieved-utc")
    parser.add_argument("--source-manifest", type=Path, default=SOURCE_MANIFEST)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.download and args.verify:
        raise ValueError("Choose either --download or --verify")
    if args.download or args.verify:
        if args.artifact_root is None:
            raise ValueError("--artifact-root is required for --download or --verify")
        result = (
            download_snapshot(args.artifact_root, args.workers, args.refresh, args.retrieved_utc)
            if args.download
            else verify_snapshot(args.artifact_root)
        )
        print(json.dumps(result, indent=2, sort_keys=True))
        return 0
    print(json.dumps(fetch_plan(args.source_manifest), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
