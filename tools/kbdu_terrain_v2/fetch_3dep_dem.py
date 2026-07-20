#!/usr/bin/env python3
"""Fetch one bounded public-domain USGS 3DEP near-zone elevation export outside Git."""

from __future__ import annotations

import argparse
import json
import sys
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ORTHO_TOOLS = ROOT / "tools" / "kbdu_orthophoto"
sys.path.insert(0, str(ORTHO_TOOLS))

from orthophoto_common import (  # noqa: E402
    atomic_write_bytes,
    atomic_write_json,
    file_record,
    require_external_directory,
    sha256_bytes,
    zone_bbox,
)


def request_bytes(url: str, timeout_seconds: int = 240) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": "T2-QuestFlight/terrain-v2"})
    with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
        return response.read()


def build_url(endpoint: str, parameters: list[tuple[str, str]]) -> str:
    return endpoint + "?" + urllib.parse.urlencode(parameters, quote_via=urllib.parse.quote)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw-dir", type=Path, required=True)
    parser.add_argument("--config", type=Path, default=Path(__file__).with_name("config.json"))
    parser.add_argument("--resume", action="store_true")
    args = parser.parse_args()

    raw_dir = require_external_directory(args.raw_dir)
    config = json.loads(args.config.read_text(encoding="utf-8"))
    source = config["source"]
    service_url = source["service_url"].rstrip("/")
    metadata_url = build_url(service_url, [("f", "pjson")])
    metadata_path = raw_dir / "service_metadata.json"
    metadata_payload = request_bytes(metadata_url)
    metadata = json.loads(metadata_payload.decode("utf-8"))
    if "USGS" not in str(metadata.get("copyrightText", "")):
        raise RuntimeError("Unexpected 3DEP service attribution")
    if float(metadata.get("pixelSizeX", 999.0)) > 1.01:
        raise RuntimeError(f"3DEP service no longer advertises a 1 m source pixel: {metadata.get('pixelSizeX')}")
    requested_size = int(config["near_zone"]["source_size_pixels"])
    if int(metadata.get("maxImageWidth", 0)) < requested_size or int(metadata.get("maxImageHeight", 0)) < requested_size:
        raise RuntimeError("3DEP service cannot produce the required bounded 4 km export")
    atomic_write_bytes(metadata_path, metadata_payload)

    west, south, east, north = zone_bbox(config, float(config["near_zone"]["half_extent_meters"]))
    export_parameters = [
        ("bbox", ",".join(f"{value:.12f}" for value in (west, south, east, north))),
        ("bboxSR", "4326"),
        ("size", f"{requested_size},{requested_size}"),
        ("imageSR", "4326"),
        ("format", "tiff"),
        ("pixelType", "F32"),
        ("interpolation", "RSP_BilinearInterpolation"),
        ("renderingRule", json.dumps({"rasterFunction": "None"}, separators=(",", ":"))),
        ("adjustAspectRatio", "false"),
        ("f", "pjson"),
    ]
    export_url = build_url(service_url + "/exportImage", export_parameters)
    export_metadata_path = raw_dir / "near_4km_dem_export.json"
    target = raw_dir / "near_4km_dem_f32.tif"
    if args.resume and target.exists() and export_metadata_path.exists():
        export_metadata = json.loads(export_metadata_path.read_text(encoding="utf-8"))
    else:
        export_metadata_payload = request_bytes(export_url)
        export_metadata = json.loads(export_metadata_payload.decode("utf-8"))
        if "error" in export_metadata:
            raise RuntimeError(f"ArcGIS export error: {export_metadata['error']}")
        atomic_write_bytes(export_metadata_path, export_metadata_payload)
        image_payload = request_bytes(export_metadata["href"])
        if image_payload[:4] not in (b"II*\x00", b"MM\x00*"):
            raise RuntimeError("3DEP export did not return a TIFF")
        atomic_write_bytes(target, image_payload)

    if int(export_metadata.get("width", -1)) != requested_size or int(export_metadata.get("height", -1)) != requested_size:
        raise RuntimeError(f"Unexpected 3DEP export dimensions: {export_metadata}")
    actual_extent = export_metadata.get("extent", {})
    actual_bbox = [actual_extent.get(key) for key in ("xmin", "ymin", "xmax", "ymax")]
    requested_bbox = [west, south, east, north]
    maximum_extent_error = max(abs(float(actual) - requested) for actual, requested in zip(actual_bbox, requested_bbox))
    if maximum_extent_error > 1e-9:
        raise RuntimeError(f"3DEP adjusted the requested extent: {maximum_extent_error}")

    receipt = {
        "schema_version": 1,
        "retrieved_utc": datetime.now(timezone.utc).isoformat(),
        "source": source,
        "service": {
            "description": metadata.get("serviceDescription"),
            "copyright": metadata.get("copyrightText"),
            "pixel_size": [metadata.get("pixelSizeX"), metadata.get("pixelSizeY")],
            "max_image_size": [metadata.get("maxImageWidth"), metadata.get("maxImageHeight")],
            "metadata": file_record(metadata_path, relative_to=raw_dir),
        },
        "request": {
            "url": export_url,
            "request_sha256": sha256_bytes(export_url.encode("utf-8")),
            "bbox_wgs84": requested_bbox,
            "actual_bbox_wgs84": actual_bbox,
            "maximum_extent_error_degrees": maximum_extent_error,
            "size_pixels": [requested_size, requested_size],
        },
        "raw_dem": file_record(target, relative_to=raw_dir),
        "raw_data_policy": "External cache only; never commit this TIFF, metadata response, or receipt to Git.",
    }
    receipt_path = raw_dir / "fetch_receipt.json"
    atomic_write_json(receipt_path, receipt)
    print(receipt_path)
    print(f"Fetched/verified {target.name}: {target.stat().st_size:,} bytes")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
