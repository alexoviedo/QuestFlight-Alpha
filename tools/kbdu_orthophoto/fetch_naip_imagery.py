#!/usr/bin/env python3
"""Fetch only the pinned, bounded USGS/USDA NAIP exports into an external raw directory."""

from __future__ import annotations

import argparse
import json
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from orthophoto_common import (
    atomic_write_bytes,
    atomic_write_json,
    canonical_json_bytes,
    file_record,
    iter_zone_tiles,
    load_config,
    require_external_directory,
    sha256_bytes,
    zone_bbox,
)


USER_AGENT = "T2-QuestFlight-KBDU-Orthophoto/1.0 (bounded public-domain data fetch)"


def request_bytes(url: str, *, attempts: int = 4, timeout_seconds: int = 180) -> bytes:
    last_error: Exception | None = None
    for attempt in range(attempts):
        try:
            request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": "*/*"})
            with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
                return response.read()
        except (urllib.error.URLError, TimeoutError) as error:
            last_error = error
            if attempt + 1 < attempts:
                time.sleep(2**attempt)
    assert last_error is not None
    raise last_error


def build_url(endpoint: str, parameters: list[tuple[str, str]]) -> str:
    return endpoint + "?" + urllib.parse.urlencode(parameters, quote_via=urllib.parse.quote)


def fetch_json(url: str, target: Path) -> tuple[dict[str, Any], dict[str, Any]]:
    payload = request_bytes(url)
    value = json.loads(payload.decode("utf-8"))
    if "error" in value:
        raise RuntimeError(f"ArcGIS service error from {url}: {value['error']}")
    atomic_write_bytes(target, canonical_json_bytes(value))
    return value, {
        **file_record(target, relative_to=target.parent),
        "url": url,
        "request_sha256": sha256_bytes(url.encode("utf-8")),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--raw-dir", required=True, type=Path, help="External raw snapshot directory")
    parser.add_argument("--config", type=Path, default=None)
    parser.add_argument("--resume", action="store_true", help="Reuse already fetched files after hashing them")
    args = parser.parse_args()

    config = load_config(args.config) if args.config else load_config()
    raw_dir = require_external_directory(args.raw_dir)
    receipt_path = raw_dir / "fetch_receipt.json"
    if receipt_path.exists() and not args.resume:
        raise FileExistsError(f"Completed snapshot already exists; pass --resume to verify/reuse it: {receipt_path}")

    source = config["source"]
    service_url = source["service_url"].rstrip("/")
    retrieval_utc = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    requests: list[dict[str, Any]] = []

    metadata_url = build_url(service_url, [("f", "pjson")])
    metadata_path = raw_dir / "service_metadata.json"
    if args.resume and metadata_path.exists():
        metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        metadata_record = {
            **file_record(metadata_path, relative_to=raw_dir),
            "url": metadata_url,
            "request_sha256": sha256_bytes(metadata_url.encode("utf-8")),
        }
    else:
        metadata, metadata_record = fetch_json(metadata_url, metadata_path)
    requests.append({"id": "service_metadata", **metadata_record})
    if int(metadata.get("maxImageWidth", 0)) < 2048 or int(metadata.get("maxImageHeight", 0)) < 2048:
        raise RuntimeError("USGSNAIPPlus no longer supports the required 2048 px bounded exports")
    credit = str(metadata.get("copyrightText", ""))
    if "USGS" not in credit or "USDA" not in credit:
        raise RuntimeError(f"Unexpected source credit from USGSNAIPPlus: {credit!r}")

    widest_zone = max(config["zones"], key=lambda zone: float(zone["half_extent_meters"]))
    west, south, east, north = zone_bbox(config, float(widest_zone["half_extent_meters"]))
    catalog_parameters = [
        ("where", f"Category=1 AND Year={int(source['catalog_year'])} AND agency='USDA'"),
        ("geometry", f"{west:.12f},{south:.12f},{east:.12f},{north:.12f}"),
        ("geometryType", "esriGeometryEnvelope"),
        ("inSR", "4326"),
        ("spatialRel", "esriSpatialRelIntersects"),
        (
            "outFields",
            "OBJECTID,Name,Year,raster_name,download_url,acquisition_date,agency,vendor,resolution_value,resolution_units",
        ),
        ("returnGeometry", "false"),
        ("orderByFields", "OBJECTID ASC"),
        ("resultRecordCount", "50"),
        ("f", "pjson"),
    ]
    catalog_url = build_url(service_url + "/query", catalog_parameters)
    catalog_path = raw_dir / "catalog.json"
    if args.resume and catalog_path.exists():
        catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
        catalog_record = {
            **file_record(catalog_path, relative_to=raw_dir),
            "url": catalog_url,
            "request_sha256": sha256_bytes(catalog_url.encode("utf-8")),
        }
    else:
        catalog, catalog_record = fetch_json(catalog_url, catalog_path)
    requests.append({"id": "catalog", **catalog_record})
    features = catalog.get("features", [])
    observed_ids = sorted(int(feature["attributes"]["OBJECTID"]) for feature in features)
    expected_ids = sorted(int(value) for value in source["raster_ids"])
    if observed_ids != expected_ids:
        raise RuntimeError(f"Pinned NAIP catalog changed: expected {expected_ids}, observed {observed_ids}")
    for feature in features:
        attributes = feature["attributes"]
        if attributes.get("Year") != source["catalog_year"] or attributes.get("agency") != "USDA":
            raise RuntimeError(f"Unexpected catalog lineage: {attributes}")
        if float(attributes.get("resolution_value") or 999) > float(source["resolution_meters"]):
            raise RuntimeError(f"Catalog resolution is coarser than pinned source: {attributes}")
    expected_acquisition_dates = sorted(source.get("acquisition_dates", []))
    observed_acquisition_dates = sorted(
        {
            datetime.fromtimestamp(int(feature["attributes"]["acquisition_date"]) / 1000, timezone.utc)
            .date()
            .isoformat()
            for feature in features
        }
    )
    if observed_acquisition_dates != expected_acquisition_dates:
        raise RuntimeError(
            "Pinned NAIP acquisition dates changed: "
            f"expected {expected_acquisition_dates}, observed {observed_acquisition_dates}"
        )

    mosaic_rule = json.dumps(
        {
            "mosaicMethod": "esriMosaicLockRaster",
            "lockRasterIds": expected_ids,
            "mosaicOperation": "MT_FIRST",
        },
        separators=(",", ":"),
        sort_keys=True,
    )
    rendering_rule = json.dumps(
        {"rasterFunction": source["raster_function"]}, separators=(",", ":"), sort_keys=True
    )
    tile_records: list[dict[str, Any]] = []
    for zone in config["zones"]:
        for tile in iter_zone_tiles(config, zone):
            target = raw_dir / tile["file_name"]
            bbox = tile["bbox_wgs84"]
            export_parameters = [
                ("bbox", ",".join(f"{value:.12f}" for value in bbox)),
                ("bboxSR", "4326"),
                ("size", f"{tile['size_pixels']},{tile['size_pixels']}"),
                ("imageSR", "4326"),
                ("format", "png"),
                ("pixelType", "U8"),
                ("interpolation", "RSP_BilinearInterpolation"),
                ("mosaicRule", mosaic_rule),
                ("renderingRule", rendering_rule),
                ("adjustAspectRatio", "false"),
                ("f", "pjson"),
            ]
            export_url = build_url(service_url + "/exportImage", export_parameters)
            export_metadata_path = raw_dir / (Path(tile["file_name"]).stem + "_export.json")
            if args.resume and target.exists() and export_metadata_path.exists():
                export_metadata = json.loads(export_metadata_path.read_text(encoding="utf-8"))
            else:
                export_payload = request_bytes(export_url)
                export_metadata = json.loads(export_payload.decode("utf-8"))
                if "error" in export_metadata:
                    raise RuntimeError(f"ArcGIS export error for {tile['file_name']}: {export_metadata['error']}")
                atomic_write_bytes(export_metadata_path, canonical_json_bytes(export_metadata))
                payload = request_bytes(export_metadata["href"])
                if not payload.startswith(b"\x89PNG\r\n\x1a\n"):
                    try:
                        detail = json.loads(payload.decode("utf-8"))
                    except Exception:
                        detail = payload[:200].decode("utf-8", errors="replace")
                    raise RuntimeError(f"Expected PNG for {tile['file_name']}, received: {detail}")
                atomic_write_bytes(target, payload)
            actual_extent = export_metadata.get("extent", {})
            actual_bbox = [
                float(actual_extent.get("xmin", float("nan"))),
                float(actual_extent.get("ymin", float("nan"))),
                float(actual_extent.get("xmax", float("nan"))),
                float(actual_extent.get("ymax", float("nan"))),
            ]
            maximum_extent_error = max(abs(actual - requested) for actual, requested in zip(actual_bbox, bbox))
            if maximum_extent_error > 1e-9:
                raise RuntimeError(
                    f"USGS export adjusted {tile['file_name']} extent by {maximum_extent_error:.12g} degrees: "
                    f"requested={bbox}, actual={actual_bbox}"
                )
            if int(export_metadata.get("width", -1)) != tile["size_pixels"] or int(
                export_metadata.get("height", -1)
            ) != tile["size_pixels"]:
                raise RuntimeError(f"USGS export returned unexpected dimensions for {tile['file_name']}: {export_metadata}")
            record = {
                **tile,
                **file_record(target, relative_to=raw_dir),
                "url": export_url,
                "request_sha256": sha256_bytes(export_url.encode("utf-8")),
                "actual_bbox_wgs84": actual_bbox,
                "maximum_extent_error_degrees": maximum_extent_error,
                "export_metadata": file_record(export_metadata_path, relative_to=raw_dir),
            }
            tile_records.append(record)
            requests.append({"id": tile["file_name"], **record})

    receipt = {
        "schema_version": 1,
        "retrieved_utc": retrieval_utc,
        "source": source,
        "service_metadata": {
            "current_version": metadata.get("currentVersion"),
            "max_image_width": metadata.get("maxImageWidth"),
            "max_image_height": metadata.get("maxImageHeight"),
            "credit": credit,
        },
        "catalog_features": [feature["attributes"] for feature in features],
        "requests": requests,
        "tiles": tile_records,
        "raw_data_policy": "External snapshot only; never commit this directory or these raw exports.",
    }
    atomic_write_json(receipt_path, receipt)
    print(receipt_path)
    print(f"Fetched/verified {len(tile_records)} bounded NAIP tiles ({sum(r['bytes'] for r in tile_records):,} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
