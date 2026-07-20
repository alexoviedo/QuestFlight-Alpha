#!/usr/bin/env python3
"""Snapshot a bounded set of official USGS 3DEP EPT nodes outside Git."""

from __future__ import annotations

import argparse
import concurrent.futures
import datetime as dt
import hashlib
import json
import os
import threading
import time
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any

from common import (
    bounds_intersect,
    compute_grid_contract,
    default_config_path,
    load_config,
    load_json,
    node_bounds,
    node_sort_key,
    parse_node_key,
    selected_hierarchy_entries,
    sha256_bytes,
    sha256_file,
    write_json,
)


USER_AGENT = "QuestFlight-KBDU-Lidar-Pipeline/1.0 (bounded USGS 3DEP public-data derivative)"


def utc_now() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z")


def fetch_bytes(url: str, attempts: int = 5, timeout_seconds: int = 180) -> bytes:
    last_error: Exception | None = None
    for attempt in range(attempts):
        try:
            request = urllib.request.Request(
                url,
                headers={
                    "Accept": "application/json,application/octet-stream;q=0.9,*/*;q=0.1",
                    "User-Agent": USER_AGENT,
                },
            )
            with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
                return response.read()
        except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError, OSError) as error:
            last_error = error
            if attempt + 1 < attempts:
                time.sleep(min(2**attempt, 16))
    raise RuntimeError(f"Failed to retrieve {url} after {attempts} attempts: {last_error}")


def capture_json(url: str, path: Path, relative_to: Path) -> tuple[dict[str, Any], dict[str, Any]]:
    payload = fetch_bytes(url)
    value = json.loads(payload)
    if not isinstance(value, dict):
        raise ValueError(f"Expected a JSON object from {url}")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)
    return value, {
        "bytes": len(payload),
        "path": path.relative_to(relative_to).as_posix(),
        "sha256": sha256_bytes(payload),
        "url": url,
    }


def ept_base_url(config: dict[str, Any]) -> str:
    url = str(config["source"]["ept_url"])
    if not url.endswith("/ept.json"):
        raise ValueError("The configured EPT URL must end with /ept.json")
    return url[: -len("/ept.json")]


def validate_ept_metadata(config: dict[str, Any], ept: dict[str, Any]) -> None:
    if ept.get("dataType") != "laszip" or ept.get("hierarchyType") != "json":
        raise ValueError("Only JSON-hierarchy LASzip EPT sources are supported")
    if ept.get("version") != "1.0.0":
        raise ValueError(f"Unsupported EPT version: {ept.get('version')!r}")
    srs = ept.get("srs", {})
    expected = str(config["product_grid"]["source_crs_epsg"])
    if str(srs.get("horizontal")) != expected:
        raise ValueError(f"EPT horizontal CRS {srs.get('horizontal')!r} != EPSG:{expected}")
    bounds = ept.get("bounds")
    if not isinstance(bounds, list) or len(bounds) != 6:
        raise ValueError("EPT metadata has invalid root bounds")
    if not isinstance(ept.get("span"), int) or int(ept["span"]) <= 0:
        raise ValueError("EPT metadata has invalid span")


def fetch_hierarchy_pages(
    base_url: str,
    output_root: Path,
    ept: dict[str, Any],
    query_bounds: tuple[float, float, float, float, float, float],
    target_depth: int,
) -> tuple[list[dict[str, int]], list[dict[str, Any]]]:
    pages: list[dict[str, int]] = []
    records: list[dict[str, Any]] = []
    pending = ["0-0-0-0"]
    seen: set[str] = set()
    while pending:
        page_key = pending.pop(0)
        if page_key in seen:
            continue
        seen.add(page_key)
        url = f"{base_url}/ept-hierarchy/{page_key}.json"
        page, record = capture_json(
            url,
            output_root / "hierarchy" / f"{page_key}.json",
            output_root,
        )
        normalized = {str(key): int(value) for key, value in page.items()}
        pages.append(normalized)
        records.append(record)
        for key, count in sorted(normalized.items(), key=lambda item: node_sort_key(item[0])):
            depth = parse_node_key(key)[0]
            if (
                count == -1
                and depth <= target_depth
                and bounds_intersect(node_bounds(ept["bounds"], key), query_bounds)
            ):
                pending.append(key)
    return pages, sorted(records, key=lambda record: record["path"])


def download_node(
    base_url: str,
    output_root: Path,
    key: str,
    declared_points: int,
    refresh: bool,
) -> dict[str, Any]:
    relative = Path("ept-data") / f"{key}.laz"
    path = output_root / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    url = f"{base_url}/ept-data/{key}.laz"
    if refresh or not path.exists() or path.stat().st_size == 0:
        payload = fetch_bytes(url)
        temporary = path.with_name(
            f"{path.name}.part.{os.getpid()}.{threading.get_ident()}"
        )
        try:
            temporary.write_bytes(payload)
            os.replace(temporary, path)
        finally:
            temporary.unlink(missing_ok=True)
    return {
        "bytes": path.stat().st_size,
        "declared_points": declared_points,
        "key": key,
        "path": relative.as_posix(),
        "sha256": sha256_file(path),
        "url": url,
    }


def verify_offline_snapshot(output_root: Path) -> dict[str, Any]:
    manifest_path = output_root / "raw_snapshot_manifest.json"
    manifest = load_json(manifest_path)
    problems: list[str] = []
    for group in ("metadata_files", "hierarchy_files", "data_files"):
        for record in manifest.get(group, []):
            path = output_root / record["path"]
            if not path.is_file():
                problems.append(f"missing {record['path']}")
            elif path.stat().st_size != int(record["bytes"]):
                problems.append(f"size mismatch {record['path']}")
            elif sha256_file(path) != record["sha256"]:
                problems.append(f"hash mismatch {record['path']}")
    if problems:
        raise RuntimeError("Offline snapshot verification failed: " + "; ".join(problems[:20]))
    return manifest


def build_snapshot(
    config_path: Path,
    output_root: Path,
    workers: int = 12,
    refresh: bool = False,
    retrieved_utc: str | None = None,
) -> dict[str, Any]:
    config = load_config(config_path)
    contract = compute_grid_contract(config)
    base_url = ept_base_url(config)
    output_root.mkdir(parents=True, exist_ok=True)
    ept, ept_record = capture_json(
        config["source"]["ept_url"], output_root / "metadata" / "ept.json", output_root
    )
    stac, stac_record = capture_json(
        config["source"]["stac_item_url"],
        output_root / "metadata" / "stac-item.json",
        output_root,
    )
    validate_ept_metadata(config, ept)
    if stac.get("id") != config["source"]["dataset_id"]:
        raise ValueError(f"STAC item id {stac.get('id')!r} does not match the configured source")
    target_depth = int(config["ept_selection"]["target_depth"])
    pages, hierarchy_records = fetch_hierarchy_pages(
        base_url, output_root, ept, contract.source_query_bounds, target_depth
    )
    selected = selected_hierarchy_entries(
        pages, ept["bounds"], contract.source_query_bounds, target_depth
    )
    if not selected:
        raise RuntimeError("The bounded EPT selection contains no data nodes")
    data_records: list[dict[str, Any]] = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as executor:
        futures = [
            executor.submit(download_node, base_url, output_root, key, count, refresh)
            for key, count in selected
        ]
        for index, future in enumerate(concurrent.futures.as_completed(futures), 1):
            data_records.append(future.result())
            if index % 100 == 0 or index == len(futures):
                print(f"snapshotted {index}/{len(futures)} EPT data nodes", flush=True)
    data_records.sort(key=lambda record: node_sort_key(record["key"]))
    inventory = "".join(
        f"{record['sha256']}  {record['path']}\n" for record in data_records
    ).encode("utf-8")
    side = float(ept["bounds"][3]) - float(ept["bounds"][0])
    nominal_resolution = side / (int(ept["span"]) * (1 << target_depth))
    manifest = {
        "data_bytes": sum(int(record["bytes"]) for record in data_records),
        "data_file_count": len(data_records),
        "data_files": data_records,
        "data_inventory_sha256": sha256_bytes(inventory),
        "declared_point_count": sum(int(record["declared_points"]) for record in data_records),
        "ept_base_url": base_url,
        "hierarchy_files": hierarchy_records,
        "metadata_files": [ept_record, stac_record],
        "raw_data_policy": "External only; this manifest and all EPT source files are prohibited from Git.",
        "retrieved_utc": retrieved_utc or utc_now(),
        "schema_version": 1,
        "selection": {
            "ept_3857_query_bounds_xyz": list(contract.source_query_bounds),
            "nominal_ept_resolution_meters": nominal_resolution,
            "origin_utm13n": [contract.origin_easting, contract.origin_northing],
            "origin_wgs84": [
                config["origin"]["longitude_degrees"],
                config["origin"]["latitude_degrees"],
            ],
            "target_depth": target_depth,
            "utm13n_bounds_meters": [
                contract.min_easting,
                contract.min_northing,
                contract.max_easting,
                contract.max_northing,
            ],
            "vertical_query_bounds_note": (
                "Broad source-Z safety window only; EPT metadata does not identify a vertical CRS."
            ),
        },
        "source_dataset": config["source"]["dataset_id"],
        "stac_item_url": config["source"]["stac_item_url"],
    }
    budget = int(config["derivative_budgets"]["maximum_raw_snapshot_bytes"])
    if manifest["data_bytes"] > budget:
        raise RuntimeError(f"Raw snapshot {manifest['data_bytes']} bytes exceeds budget {budget}")
    write_json(output_root / "raw_snapshot_manifest.json", manifest)
    return manifest


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, default=default_config_path())
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--workers", type=int, default=12)
    parser.add_argument("--refresh", action="store_true")
    parser.add_argument("--offline", action="store_true")
    parser.add_argument("--retrieved-utc")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if not 1 <= args.workers <= 32:
        raise ValueError("--workers must be between 1 and 32")
    if args.offline:
        manifest = verify_offline_snapshot(args.output.resolve())
    else:
        manifest = build_snapshot(
            args.config.resolve(),
            args.output.resolve(),
            workers=args.workers,
            refresh=args.refresh,
            retrieved_utc=args.retrieved_utc,
        )
    print(
        json.dumps(
            {
                "data_bytes": manifest["data_bytes"],
                "data_file_count": manifest["data_file_count"],
                "data_inventory_sha256": manifest["data_inventory_sha256"],
                "declared_point_count": manifest["declared_point_count"],
                "status": "VERIFIED" if args.offline else "SNAPSHOTTED",
            },
            indent=2,
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

