#!/usr/bin/env python3
"""Fetch bounded FAA, USGS 3DEP, and OpenStreetMap source data outside Git."""

from __future__ import annotations

import argparse
import json
import math
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from kbdu_common import (
    atomic_write_bytes,
    atomic_write_json,
    bbox_for_half_extent,
    canonical_json_bytes,
    chunks,
    default_raw_parent,
    enu_to_geodetic,
    file_record,
    iter_layer_grid,
    load_config,
    normalized_sha_key,
    sha256_bytes,
)


USER_AGENT = "QuestFlightLab-KBDU-Environment-Pipeline/1.0 (public-data prototype; not for navigation)"
FAA_FACILITY_URL = "https://services.arcgis.com/xOi1kZaI0eWDREZv/ArcGIS/rest/services/NTAD_Aviation_Facilities/FeatureServer/0/query"
FAA_RUNWAYS_URL = "https://services.arcgis.com/xOi1kZaI0eWDREZv/ArcGIS/rest/services/Runways_View/FeatureServer/0/query"
USGS_SERVICE_URL = "https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer"
USGS_SAMPLES_URL = USGS_SERVICE_URL + "/getSamples"
NAIP_SERVICE_URL = "https://imagery.nationalmap.gov/arcgis/rest/services/USGSNAIPPlus/ImageServer"
NAIP_QUERY_URL = NAIP_SERVICE_URL + "/query"
OVERPASS_ENDPOINTS = (
    "https://overpass-api.de/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
)

FAA_LICENSE = {
    "name": "United States government work; unrestricted public use",
    "spdx": "LicenseRef-US-Government-Work-Public-Domain",
    "url": "https://services.arcgis.com/xOi1kZaI0eWDREZv/ArcGIS/rest/services/Runways_View/FeatureServer/0",
    "attribution": "Federal Aviation Administration (FAA); USDOT Bureau of Transportation Statistics (BTS)",
}
USGS_LICENSE = {
    "name": "USGS-authored data; public domain / no use restrictions",
    "spdx": "LicenseRef-USGS-Public-Domain",
    "url": "https://www.usgs.gov/3d-elevation-program/about-3dep-products-services",
    "attribution": "U.S. Geological Survey 3D Elevation Program (3DEP)",
}
OSM_LICENSE = {
    "name": "Open Database License 1.0",
    "spdx": "ODbL-1.0",
    "url": "https://www.openstreetmap.org/copyright",
    "attribution": "© OpenStreetMap contributors",
}
NAIP_LICENSE = {
    "name": "USGS/USDA The National Map orthoimagery; public domain",
    "spdx": "LicenseRef-US-Government-Work-Public-Domain",
    "url": "https://imagery.nationalmap.gov/arcgis/rest/services/USGSNAIPPlus/ImageServer",
    "attribution": "USGS, USDA, The National Map: Orthoimagery",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=Path(__file__).with_name("config.json"))
    parser.add_argument(
        "--raw-dir",
        type=Path,
        help="Snapshot output outside Git. Default: sibling setup-artifacts timestamp directory.",
    )
    parser.add_argument("--skip-osm", action="store_true", help="Fetch FAA/USGS only; build will remain blocked until OSM is supplied.")
    parser.add_argument("--resume", action="store_true", help="Resume an incomplete snapshot and reuse validated USGS batches.")
    return parser.parse_args()


def request_bytes(
    url: str,
    *,
    params: dict[str, Any] | None,
    method: str,
    timeout_seconds: int,
    retries: int,
) -> tuple[bytes, str, bytes]:
    encoded = urllib.parse.urlencode(params or {}, doseq=True).encode("utf-8")
    request_url = url
    data = None
    if method.upper() == "GET":
        if encoded:
            request_url += ("&" if "?" in request_url else "?") + encoded.decode("ascii")
    else:
        data = encoded
    last_error: Exception | None = None
    for attempt in range(retries):
        request = urllib.request.Request(
            request_url,
            data=data,
            method=method.upper(),
            headers={
                "User-Agent": USER_AGENT,
                "Accept": "application/json, application/geo+json;q=0.9, */*;q=0.1",
                "Content-Type": "application/x-www-form-urlencoded",
            },
        )
        try:
            with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
                payload = response.read()
            return payload, request_url, encoded
        except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError) as error:
            last_error = error
            if attempt + 1 < retries:
                time.sleep(min(8.0, 1.5 * (2**attempt)))
    raise RuntimeError(f"Unable to fetch {url} after {retries} attempts: {last_error}")


def checked_json(payload: bytes, source_name: str) -> Any:
    try:
        value = json.loads(payload)
    except json.JSONDecodeError as error:
        raise RuntimeError(f"{source_name} did not return JSON: {error}") from error
    if isinstance(value, dict) and value.get("error"):
        raise RuntimeError(f"{source_name} returned an error: {value['error']}")
    return value


def save_source(
    raw_dir: Path,
    filename: str,
    payload: bytes,
    *,
    source_id: str,
    agency: str,
    url: str,
    method: str,
    request_body: bytes,
    license_record: dict[str, Any],
    retrieved_utc: str,
    notes: str,
) -> dict[str, Any]:
    path = raw_dir / filename
    atomic_write_bytes(path, payload)
    record = file_record(path, relative_to=raw_dir)
    record.update(
        {
            "id": source_id,
            "agency": agency,
            "url": url,
            "method": method,
            "request_sha256": sha256_bytes(request_body),
            "retrieved_utc": retrieved_utc,
            "license": license_record,
            "notes": notes,
        }
    )
    return record


def faa_query(url: str, faa_id: str, timeout: int, retries: int) -> tuple[bytes, str, bytes, dict[str, Any]]:
    params = {
        "where": f"ARPT_ID='{faa_id}'",
        "outFields": "*",
        "returnGeometry": "true",
        "outSR": "4326",
        "f": "geojson",
    }
    payload, request_url, request_body = request_bytes(
        url, params=params, method="GET", timeout_seconds=timeout, retries=retries
    )
    value = checked_json(payload, "FAA ArcGIS query")
    if not isinstance(value, dict) or not value.get("features"):
        raise RuntimeError(f"FAA query returned no features for {faa_id}")
    return payload, request_url, request_body, value


def bbox_text(bbox: tuple[float, float, float, float]) -> str:
    return ",".join(f"{value:.8f}" for value in bbox)


def build_osm_queries(config: dict[str, Any], timeout: int) -> dict[str, str]:
    vector = config["vector_context"]
    airport_bbox = bbox_text(bbox_for_half_extent(config, float(vector["airport_half_extent_meters"])))
    mid_bbox = bbox_text(bbox_for_half_extent(config, float(vector["mid_half_extent_meters"])))
    far_bbox = bbox_text(bbox_for_half_extent(config, float(vector["far_half_extent_meters"])))
    airport_query = f"""[out:json][timeout:{timeout}];
(
  nwr[\"aeroway\"]({airport_bbox});
  way[\"building\"]({airport_bbox});
  relation[\"building\"]({airport_bbox});
  way[\"highway\"]({airport_bbox});
  way[\"barrier\"]({airport_bbox});
);
out tags geom qt;
"""
    context_query = f"""[out:json][timeout:{timeout}];
(
  way[\"highway\"~\"^(motorway|trunk|primary|secondary|tertiary)$\"]({far_bbox});
  way[\"waterway\"~\"^(river|stream|canal|drain|ditch)$\"]({far_bbox});
  way[\"natural\"=\"water\"]({far_bbox});
  relation[\"natural\"=\"water\"]({far_bbox});
  way[\"landuse\"=\"reservoir\"]({far_bbox});
  relation[\"landuse\"=\"reservoir\"]({far_bbox});
  way[\"landuse\"~\"^(farmland|orchard|meadow|forest|quarry|industrial)$\"]({mid_bbox});
  relation[\"landuse\"~\"^(farmland|orchard|meadow|forest|quarry|industrial)$\"]({mid_bbox});
);
out tags geom qt;
"""
    return {"osm_airport.json": airport_query, "osm_context.json": context_query}


def fetch_overpass(query: str, timeout: int, retries: int) -> tuple[bytes, str, bytes]:
    errors: list[str] = []
    for endpoint in OVERPASS_ENDPOINTS:
        try:
            payload, request_url, request_body = request_bytes(
                endpoint,
                params={"data": query},
                method="POST",
                timeout_seconds=timeout,
                retries=retries,
            )
            value = checked_json(payload, f"Overpass {endpoint}")
            if not isinstance(value, dict) or not isinstance(value.get("elements"), list):
                raise RuntimeError("Overpass response has no elements array")
            return payload, request_url, request_body
        except RuntimeError as error:
            errors.append(str(error))
    raise RuntimeError("All Overpass endpoints failed: " + " | ".join(errors))


def build_usgs_sample_index(
    config: dict[str, Any], faa_runways: dict[str, Any]
) -> tuple[list[dict[str, Any]], dict[str, str]]:
    origin = config["airport"]["origin"]
    by_key: dict[str, dict[str, Any]] = {}

    def add_point(x_meters: float, z_meters: float) -> str:
        key = normalized_sha_key(x_meters, z_meters)
        if key not in by_key:
            latitude, longitude, _ = enu_to_geodetic(
                x_meters,
                z_meters,
                0.0,
                origin["latitude_degrees"],
                origin["longitude_degrees"],
                origin["elevation_msl_meters"],
            )
            by_key[key] = {
                "key": key,
                "x_meters": round(x_meters, 6),
                "z_meters": round(z_meters, 6),
                "latitude_degrees": round(latitude, 10),
                "longitude_degrees": round(longitude, 10),
            }
        return key

    for layer in config["terrain"]["layers"]:
        for _, _, x_meters, z_meters in iter_layer_grid(layer):
            add_point(x_meters, z_meters)

    control_keys = {"origin": add_point(0.0, 0.0)}
    for feature in faa_runways["features"]:
        properties = feature.get("properties") or {}
        runway_id = str(properties.get("RWY_ID") or "unknown")
        for end_index in (1, 2):
            latitude = properties.get(f"LAT{end_index}_DECIMAL")
            longitude = properties.get(f"LONG{end_index}_DECIMAL")
            if latitude is None or longitude is None:
                continue
            from kbdu_common import geodetic_to_enu

            east, north, _ = geodetic_to_enu(
                float(latitude),
                float(longitude),
                origin["elevation_msl_meters"],
                origin["latitude_degrees"],
                origin["longitude_degrees"],
                origin["elevation_msl_meters"],
            )
            control_keys[f"runway:{runway_id}:end{end_index}"] = add_point(east, north)
    points = sorted(by_key.values(), key=lambda item: item["key"])
    return points, control_keys


def fetch_usgs_samples(
    raw_dir: Path,
    config: dict[str, Any],
    points: list[dict[str, Any]],
    retrieved_utc: str,
    timeout: int,
    retries: int,
) -> list[dict[str, Any]]:
    batch_size = int(config["network"]["usgs_sample_batch_size"])
    records: list[dict[str, Any]] = []
    for batch_index, batch in enumerate(chunks(points, batch_size)):
        geometry = {
            "points": [[point["longitude_degrees"], point["latitude_degrees"]] for point in batch],
            "spatialReference": {"wkid": 4326},
        }
        params = {
            "geometry": json.dumps(geometry, separators=(",", ":")),
            "geometryType": "esriGeometryMultipoint",
            "returnFirstValueOnly": "true",
            "interpolation": "RSP_BilinearInterpolation",
            "f": "json",
        }
        request_body = urllib.parse.urlencode(params).encode("utf-8")
        request_url = USGS_SAMPLES_URL
        filename = f"usgs_samples_{batch_index:03d}.json"
        existing_path = raw_dir / filename
        if existing_path.is_file():
            payload = existing_path.read_bytes()
            reused = True
        else:
            payload, request_url, request_body = request_bytes(
                USGS_SAMPLES_URL,
                params=params,
                method="POST",
                timeout_seconds=timeout,
                retries=retries,
            )
            reused = False
        value = checked_json(payload, f"USGS 3DEP samples batch {batch_index}")
        samples = value.get("samples") if isinstance(value, dict) else None
        if not isinstance(samples, list) or len(samples) != len(batch):
            raise RuntimeError(
                f"USGS batch {batch_index} returned {len(samples) if isinstance(samples, list) else 'invalid'} "
                f"samples for {len(batch)} requested points"
            )
        location_ids = sorted(int(sample.get("locationId", -1)) for sample in samples)
        if location_ids != list(range(len(batch))):
            raise RuntimeError(
                f"USGS batch {batch_index} locationId values are not a complete 0..{len(batch) - 1} mapping"
            )
        for sample in samples:
            location_id = int(sample["locationId"])
            requested = batch[location_id]
            returned = sample.get("location") or {}
            if (
                abs(float(returned.get("x", math.inf)) - float(requested["longitude_degrees"])) > 1e-8
                or abs(float(returned.get("y", math.inf)) - float(requested["latitude_degrees"])) > 1e-8
            ):
                raise RuntimeError(
                    f"USGS batch {batch_index} locationId {location_id} coordinate mismatch; "
                    "expected x=longitude/y=latitude"
                )
        record = save_source(
            raw_dir,
            filename,
            payload,
            source_id=f"usgs_3dep_samples_{batch_index:03d}",
            agency="USGS",
            url=request_url,
            method="POST",
            request_body=request_body,
            license_record=USGS_LICENSE,
            retrieved_utc=retrieved_utc,
            notes=f"Bounded 3DEP bilinear samples; point index {batch_index * batch_size}..{batch_index * batch_size + len(batch) - 1}.",
        )
        record["sample_start"] = batch_index * batch_size
        record["sample_count"] = len(batch)
        record["reused_from_incomplete_snapshot"] = reused
        records.append(record)
        action = "reused" if reused else "fetched"
        print(f"USGS samples ({action}): {min((batch_index + 1) * batch_size, len(points))}/{len(points)}", flush=True)
    return records


def fetch_naip_availability(
    raw_dir: Path,
    config: dict[str, Any],
    retrieved_utc: str,
    timeout: int,
    retries: int,
) -> list[dict[str, Any]]:
    records: list[dict[str, Any]] = []
    metadata_payload, metadata_url, metadata_request = request_bytes(
        NAIP_SERVICE_URL,
        params={"f": "pjson"},
        method="GET",
        timeout_seconds=timeout,
        retries=retries,
    )
    metadata = checked_json(metadata_payload, "USGS NAIP Plus service metadata")
    records.append(
        save_source(
            raw_dir,
            "naip_service.json",
            metadata_payload,
            source_id="usgs_naip_plus_service_metadata",
            agency="USGS / USDA",
            url=metadata_url,
            method="GET",
            request_body=metadata_request,
            license_record=NAIP_LICENSE,
            retrieved_utc=retrieved_utc,
            notes="Official NAIP Plus ImageServer metadata captured for the bounded availability decision.",
        )
    )
    half_extent = float(config["imagery"]["availability_half_extent_meters"])
    south, west, north, east = bbox_for_half_extent(config, half_extent)
    params = {
        "where": "1=1",
        "geometry": f"{west:.10f},{south:.10f},{east:.10f},{north:.10f}",
        "geometryType": "esriGeometryEnvelope",
        "inSR": "4326",
        "spatialRel": "esriSpatialRelIntersects",
        "outFields": "OBJECTID,Name,State,Year,raster_name,download_url,acquisition_date,agency,vendor,resolution_value,resolution_units",
        "returnGeometry": "false",
        "orderByFields": "Year DESC",
        "resultRecordCount": "10",
        "f": "json",
    }
    availability_payload, availability_url, availability_request = request_bytes(
        NAIP_QUERY_URL,
        params=params,
        method="GET",
        timeout_seconds=timeout,
        retries=retries,
    )
    availability = checked_json(availability_payload, "bounded USGS NAIP Plus availability query")
    records.append(
        save_source(
            raw_dir,
            "naip_availability.json",
            availability_payload,
            source_id="usgs_naip_plus_kbdu_availability",
            agency="USGS / USDA",
            url=availability_url,
            method="GET",
            request_body=availability_request,
            license_record=NAIP_LICENSE,
            retrieved_utc=retrieved_utc,
            notes=f"One bounded automated availability query over the {2 * half_extent:.0f} m KBDU inner ring; no imagery pixels downloaded.",
        )
    )
    features = availability.get("features") if isinstance(availability, dict) else []
    max_width = int(metadata.get("maxImageWidth") or 0)
    max_height = int(metadata.get("maxImageHeight") or 0)
    native_inner_pixels = int(round(2.0 * half_extent))
    far_width_meters = 2.0 * float(config["vector_context"]["far_half_extent_meters"])
    decision = {
        "schema_version": 1,
        "status": "available_but_not_committed" if features else "no_intersecting_catalog_items",
        "availability_feature_count": len(features or []),
        "availability_bbox_wgs84": [west, south, east, north],
        "service_max_export_pixels": [max_width, max_height],
        "inner_ring_native_1m_pixels": [native_inner_pixels, native_inner_pixels],
        "far_ring_native_1m_pixels": [int(round(far_width_meters)), int(round(far_width_meters))],
        "decision": "Do not commit NAIP imagery in this pass; use deterministic OSM macro land-cover/material regions.",
        "exact_blocker": (
            "A useful 24 km x 24 km 1 m macro source would be about 24,000 x 24,000 pixels before tiling, "
            f"while this service advertises {max_width} x {max_height} maximum exports. The project lacks a proven "
            "seam-safe tiling, color-normalization, mip, and Android ASTC import path; a single downsampled image would "
            "either exceed the 750 KB committed macro-texture budget or blur airport/field detail."
        ),
        "fallback": config["imagery"]["fallback"],
        "raw_imagery_downloaded": False,
    }
    decision_path = raw_dir / "naip_imagery_decision.json"
    atomic_write_json(decision_path, decision)
    records.append(
        {
            **file_record(decision_path, relative_to=raw_dir),
            "id": "naip_imagery_decision",
            "agency": "QuestFlightLab deterministic pipeline",
            "url": NAIP_SERVICE_URL,
            "method": "generated decision",
            "request_sha256": sha256_bytes(canonical_json_bytes(params, pretty=False)),
            "retrieved_utc": retrieved_utc,
            "license": NAIP_LICENSE,
            "notes": "Exact automated imagery gate decision and fallback rationale; no raw imagery is stored.",
        }
    )
    return records


def main() -> int:
    args = parse_args()
    config = load_config(args.config)
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    retrieved_utc = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    raw_dir = (args.raw_dir or (default_raw_parent() / timestamp)).resolve()
    if raw_dir.exists() and not args.resume:
        raise RuntimeError(f"Raw snapshot already exists; pass --resume only for an incomplete snapshot: {raw_dir}")
    if (raw_dir / "fetch_receipt.json").exists() and args.resume:
        raise RuntimeError("Refusing to resume a snapshot that already has a completed fetch_receipt.json")
    raw_dir.mkdir(parents=True, exist_ok=args.resume)

    timeout = int(config["network"]["timeout_seconds"])
    retries = int(config["network"]["retries"])
    faa_id = config["airport"]["faa_id"]
    source_records: list[dict[str, Any]] = []

    facility_payload, facility_url, facility_request, facility = faa_query(
        FAA_FACILITY_URL, faa_id, timeout, retries
    )
    source_records.append(
        save_source(
            raw_dir,
            "faa_airport.geojson",
            facility_payload,
            source_id="faa_ntad_airport",
            agency="FAA / USDOT BTS",
            url=facility_url,
            method="GET",
            request_body=facility_request,
            license_record=FAA_LICENSE,
            retrieved_utc=retrieved_utc,
            notes="Official FAA-derived airport reference point and field metadata, bounded by ARPT_ID=BDU.",
        )
    )

    runways_payload, runways_url, runways_request, runways = faa_query(
        FAA_RUNWAYS_URL, faa_id, timeout, retries
    )
    source_records.append(
        save_source(
            raw_dir,
            "faa_runways.geojson",
            runways_payload,
            source_id="faa_ntad_runways",
            agency="FAA / USDOT BTS",
            url=runways_url,
            method="GET",
            request_body=runways_request,
            license_record=FAA_LICENSE,
            retrieved_utc=retrieved_utc,
            notes="Official FAA-derived runway geometry and attributes, bounded by ARPT_ID=BDU.",
        )
    )

    metadata_payload, metadata_url, metadata_request = request_bytes(
        USGS_SERVICE_URL,
        params={"f": "pjson"},
        method="GET",
        timeout_seconds=timeout,
        retries=retries,
    )
    checked_json(metadata_payload, "USGS 3DEP service metadata")
    source_records.append(
        save_source(
            raw_dir,
            "usgs_3dep_service.json",
            metadata_payload,
            source_id="usgs_3dep_service_metadata",
            agency="USGS",
            url=metadata_url,
            method="GET",
            request_body=metadata_request,
            license_record=USGS_LICENSE,
            retrieved_utc=retrieved_utc,
            notes="3DEP Bare Earth DEM dynamic service metadata captured with the raw snapshot.",
        )
    )

    points, control_keys = build_usgs_sample_index(config, runways)
    sample_index = {
        "schema_version": 1,
        "origin": config["airport"]["origin"],
        "coordinate_frame": "local ENU: x=east, z=north, y=up",
        "point_count": len(points),
        "points": points,
        "control_keys": control_keys,
    }
    sample_index_path = raw_dir / "usgs_sample_index.json"
    atomic_write_json(sample_index_path, sample_index)
    source_records.append(
        {
            **file_record(sample_index_path, relative_to=raw_dir),
            "id": "usgs_3dep_sample_request_index",
            "agency": "QuestFlightLab deterministic pipeline",
            "url": USGS_SAMPLES_URL,
            "method": "generated request index",
            "request_sha256": sha256_bytes(canonical_json_bytes(config, pretty=False)),
            "retrieved_utc": retrieved_utc,
            "license": USGS_LICENSE,
            "notes": "Local ENU sample coordinates and exact WGS84 request positions; raw source heights are in the numbered response files.",
        }
    )
    source_records.extend(
        fetch_usgs_samples(raw_dir, config, points, retrieved_utc, timeout, retries)
    )
    source_records.extend(fetch_naip_availability(raw_dir, config, retrieved_utc, timeout, retries))

    osm_queries: dict[str, str] = {}
    if not args.skip_osm:
        osm_queries = build_osm_queries(config, timeout)
        for filename, query in osm_queries.items():
            payload, endpoint, request_body = fetch_overpass(query, timeout, retries)
            source_records.append(
                save_source(
                    raw_dir,
                    filename,
                    payload,
                    source_id=filename.removesuffix(".json"),
                    agency="OpenStreetMap contributors via Overpass API",
                    url=endpoint,
                    method="POST",
                    request_body=request_body,
                    license_record=OSM_LICENSE,
                    retrieved_utc=retrieved_utc,
                    notes="Bounded vector extract; exact Overpass QL is stored in osm_queries.json.",
                )
            )
        query_path = raw_dir / "osm_queries.json"
        atomic_write_json(query_path, osm_queries)
        source_records.append(
            {
                **file_record(query_path, relative_to=raw_dir),
                "id": "osm_query_definitions",
                "agency": "QuestFlightLab deterministic pipeline",
                "url": OVERPASS_ENDPOINTS[0],
                "method": "generated query definition",
                "request_sha256": sha256_bytes(canonical_json_bytes(osm_queries, pretty=False)),
                "retrieved_utc": retrieved_utc,
                "license": OSM_LICENSE,
                "notes": "Exact bounded Overpass QL used for this snapshot.",
            }
        )

    receipt = {
        "schema_version": 1,
        "snapshot_id": timestamp,
        "retrieved_utc": retrieved_utc,
        "pipeline": "QuestFlightLab KBDU environment fetch v1",
        "config_sha256": sha256_bytes(canonical_json_bytes(config, pretty=False)),
        "raw_data_policy": "Raw downloads remain outside Git; only optimized derivatives and manifests may be committed.",
        "sources": sorted(source_records, key=lambda item: item["id"]),
        "complete": not args.skip_osm,
    }
    atomic_write_json(raw_dir / "fetch_receipt.json", receipt)
    print(raw_dir)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        print("Fetch interrupted; incomplete raw snapshot was not marked complete.", file=sys.stderr)
        raise SystemExit(130)
