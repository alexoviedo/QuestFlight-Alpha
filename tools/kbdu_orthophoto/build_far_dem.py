#!/usr/bin/env python3
"""Fetch and package the immutable 100 m KBDU far-terrain source.

Raw USGS responses and previews are deliberately written outside the repository.
Only the compact, license-bearing height manifest is committed. The corresponding
Unity mesh is authored offline by StandaloneEnvironmentArtAuthoring.cs.
"""

from __future__ import annotations

import argparse
import base64
import io
import json
import math
import struct
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw

from orthophoto_common import (
    DEFAULT_CONFIG_PATH,
    REPO_ROOT,
    atomic_write_bytes,
    atomic_write_json,
    load_config,
    require_external_directory,
    sha256_bytes,
    sha256_file,
    zone_bbox,
)


SERVICE_URL = "https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer"
DEFAULT_OUTPUT = Path(__file__).resolve().parent / "kbdu_far_dem_100m.json"
DEFAULT_FROZEN_TERRAIN = (
    REPO_ROOT / "QuestFlight" / "Assets" / "Resources" / "QuestFlightLab" / "Environment" / "KBDU" / "kbdu_terrain_rings.json"
)
GRID_SIZE = 241
HALF_EXTENT_METERS = 12_000.0
GRID_SPACING_METERS = 100.0
INNER_HALF_EXTENT_METERS = 6_000.0
SKIRT_DEPTH_METERS = 24.0
HEIGHT_QUANTIZATION_METERS = 0.1


def utc_now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def request_bytes(url: str, *, attempts: int = 6, timeout_seconds: int = 120) -> bytes:
    headers = {"User-Agent": "T2-QuestFlight-KBDU-3DEP-Pipeline/1.0"}
    last_error: Exception | None = None
    for attempt in range(attempts):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=timeout_seconds) as response:
                payload = response.read()
                if not payload:
                    raise RuntimeError(f"Empty response from {url}")
                return payload
        except (urllib.error.URLError, TimeoutError, RuntimeError) as error:
            last_error = error
            if attempt + 1 < attempts:
                time.sleep(min(2 ** attempt, 12))
    raise RuntimeError(f"Request failed after {attempts} attempts: {url}") from last_error


def request_json(url: str) -> tuple[bytes, dict[str, Any]]:
    payload = request_bytes(url)
    try:
        value = json.loads(payload.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise RuntimeError(f"Expected JSON from {url}; received {len(payload)} bytes") from error
    if "error" in value:
        raise RuntimeError(f"ArcGIS service error for {url}: {value['error']}")
    return payload, value


def export_dem(config: dict[str, Any], raw_directory: Path) -> tuple[Image.Image, dict[str, Any]]:
    west, south, east, north = zone_bbox(config, HALF_EXTENT_METERS)
    metadata_url = SERVICE_URL + "?" + urllib.parse.urlencode({"f": "pjson"})
    metadata_payload, metadata = request_json(metadata_url)
    atomic_write_bytes(raw_directory / "3dep_service_metadata.json", metadata_payload)

    parameters = {
        "bbox": f"{west:.12f},{south:.12f},{east:.12f},{north:.12f}",
        "bboxSR": "4326",
        "imageSR": "4326",
        "size": f"{GRID_SIZE},{GRID_SIZE}",
        "format": "tiff",
        "pixelType": "F32",
        "interpolation": "RSP_BilinearInterpolation",
        "adjustAspectRatio": "false",
        "f": "pjson",
    }
    export_url = SERVICE_URL + "/exportImage?" + urllib.parse.urlencode(parameters)
    export_payload, export_record = request_json(export_url)
    atomic_write_bytes(raw_directory / "far_dem_export_response.json", export_payload)
    if "href" not in export_record:
        raise RuntimeError(f"3DEP export did not provide a raster URL: {export_record}")

    requested_extent = [west, south, east, north]
    returned = export_record.get("extent", {})
    returned_extent = [
        float(returned.get("xmin", math.nan)),
        float(returned.get("ymin", math.nan)),
        float(returned.get("xmax", math.nan)),
        float(returned.get("ymax", math.nan)),
    ]
    extent_error = max(abs(a - b) for a, b in zip(requested_extent, returned_extent))
    if not math.isfinite(extent_error) or extent_error > 1e-9:
        raise RuntimeError(
            "3DEP changed the requested extent; adjustAspectRatio=false contract failed: "
            f"requested={requested_extent}, returned={returned_extent}, error={extent_error}"
        )

    raster_delivery = "export_response_href"
    try:
        # The service normally returns a short-lived output URL. Its output node can
        # occasionally return a gateway error even though the export itself passed.
        tiff_payload = request_bytes(str(export_record["href"]), attempts=3)
    except RuntimeError:
        direct_parameters = dict(parameters)
        direct_parameters["f"] = "image"
        direct_url = SERVICE_URL + "/exportImage?" + urllib.parse.urlencode(direct_parameters)
        tiff_payload = request_bytes(direct_url, attempts=4)
        raster_delivery = "direct_export_image"
    tiff_path = raw_directory / "KBDU_3DEP_Far24km_241x241_F32.tif"
    atomic_write_bytes(tiff_path, tiff_payload)
    with Image.open(io.BytesIO(tiff_payload)) as source:
        image = source.convert("F")
        image.load()
    if image.size != (GRID_SIZE, GRID_SIZE):
        raise RuntimeError(f"Unexpected 3DEP raster dimensions: {image.size}")

    values = list(image.getdata())
    finite = [float(value) for value in values if math.isfinite(float(value)) and float(value) > -10_000.0]
    if len(finite) != GRID_SIZE * GRID_SIZE:
        raise RuntimeError(f"3DEP raster contains {GRID_SIZE * GRID_SIZE - len(finite)} invalid samples")
    if min(finite) < 1_000.0 or max(finite) > 5_000.0:
        raise RuntimeError(f"3DEP elevations outside plausible KBDU range: {min(finite)}..{max(finite)} m")

    receipt = {
        "service_url": SERVICE_URL,
        "service_name": metadata.get("name", "3DEPElevation"),
        "service_description": metadata.get("serviceDescription", ""),
        "copyright_text": metadata.get("copyrightText", ""),
        "request_url": export_url,
        "request_sha256": sha256_bytes(export_url.encode("utf-8")),
        "requested_extent_wgs84": requested_extent,
        "returned_extent_wgs84": returned_extent,
        "maximum_extent_error_degrees": extent_error,
        "export_response_sha256": sha256_bytes(export_payload),
        "service_metadata_sha256": sha256_bytes(metadata_payload),
        "raw_tiff_sha256": sha256_file(tiff_path),
        "raw_tiff_bytes": len(tiff_payload),
        "raster_delivery": raster_delivery,
        "retrieved_utc": utc_now(),
    }
    atomic_write_json(raw_directory / "far_dem_source_receipt.json", receipt)
    return image, receipt


def elevations_south_to_north(image: Image.Image) -> list[float]:
    # ArcGIS/Pillow row zero is north. Unity row zero is the south edge.
    source = list(image.getdata())
    rows = [source[row * GRID_SIZE : (row + 1) * GRID_SIZE] for row in range(GRID_SIZE)]
    return [float(value) for row in reversed(rows) for value in row]


def resample_frozen_far_grid(config: dict[str, Any], source_path: Path) -> tuple[Image.Image, dict[str, Any]]:
    """Densify the pinned 400 m 3DEP grid when the live export node is unavailable.

    This is intentionally described as resampling, not as new elevation detail.
    It improves vertex-normal continuity and mesh sealing while retaining the
    previously reviewed public-domain source snapshot.
    """

    source_payload = source_path.read_bytes()
    source = json.loads(source_payload.decode("utf-8"))
    layer = next((candidate for candidate in source["layers"] if candidate.get("id") == "far_24km"), None)
    if layer is None:
        raise RuntimeError("Frozen terrain source has no far_24km layer")
    source_width = int(layer["width"])
    source_height = int(layer["height"])
    source_spacing = float(layer["spacing_meters"])
    if source_width != 61 or source_height != 61 or source_spacing != 400.0:
        raise RuntimeError(
            f"Frozen far grid contract changed: {source_width}x{source_height} at {source_spacing} m"
        )
    encoded = base64.b64decode(layer["height_dm_little_endian_base64"], validate=True)
    if len(encoded) != source_width * source_height * 2:
        raise RuntimeError("Frozen far grid payload length is invalid")
    offsets = struct.unpack("<" + "h" * (source_width * source_height), encoded)
    reference = float(config["origin"]["elevation_msl_meters"])
    source_values = [reference + int(offset) * float(source["height_quantization_meters"]) for offset in offsets]

    values: list[float] = []
    for target_row in range(GRID_SIZE):
        source_z = target_row * (source_height - 1) / (GRID_SIZE - 1)
        z0 = int(math.floor(source_z))
        z1 = min(source_height - 1, z0 + 1)
        tz = source_z - z0
        for target_column in range(GRID_SIZE):
            source_x = target_column * (source_width - 1) / (GRID_SIZE - 1)
            x0 = int(math.floor(source_x))
            x1 = min(source_width - 1, x0 + 1)
            tx = source_x - x0
            south = source_values[z0 * source_width + x0] * (1.0 - tx) + source_values[z0 * source_width + x1] * tx
            north = source_values[z1 * source_width + x0] * (1.0 - tx) + source_values[z1 * source_width + x1] * tx
            values.append(south * (1.0 - tz) + north * tz)

    # Pillow row zero is north; the frozen payload and returned list are south-first.
    rows = [values[row * GRID_SIZE : (row + 1) * GRID_SIZE] for row in range(GRID_SIZE)]
    image = Image.new("F", (GRID_SIZE, GRID_SIZE))
    image.putdata([value for row in reversed(rows) for value in row])
    receipt = {
        "service_url": SERVICE_URL,
        "service_name": "3DEPElevation (frozen product snapshot)",
        "service_description": "Deterministic 100 m bilinear resampling of the pinned 400 m public-domain USGS 3DEP far grid; no new source detail claimed.",
        "copyright_text": "USGS public-domain United States government work",
        "raw_source_mode": "frozen_400m_grid_bilinear_resample",
        "raw_source_sha256": sha256_bytes(source_payload),
        "raw_source_bytes": len(source_payload),
        "source_path": source_path.relative_to(REPO_ROOT).as_posix(),
        "source_snapshot_id": source["source_snapshot"]["id"],
        "source_retrieved_utc": source["source_snapshot"]["retrieved_utc"],
        "frozen_fetch_receipt_sha256": source["source_snapshot"]["fetch_receipt_sha256"],
        "frozen_height_payload_sha256": sha256_bytes(encoded),
        "retrieved_utc": source["source_snapshot"]["retrieved_utc"],
        "requested_extent_wgs84": list(zone_bbox(config, HALF_EXTENT_METERS)),
        "returned_extent_wgs84": list(zone_bbox(config, HALF_EXTENT_METERS)),
        "maximum_extent_error_degrees": 0.0,
    }
    return image, receipt


def build_quantized_payload(values: list[float], reference_elevation: float) -> tuple[bytes, float]:
    quantized: list[int] = []
    maximum_error = 0.0
    for value in values:
        encoded = int(round((value - reference_elevation) / HEIGHT_QUANTIZATION_METERS))
        if encoded < -32768 or encoded > 32767:
            raise RuntimeError(f"Elevation cannot be encoded as signed 16-bit decimeters: {value}")
        decoded = reference_elevation + encoded * HEIGHT_QUANTIZATION_METERS
        maximum_error = max(maximum_error, abs(decoded - value))
        quantized.append(encoded)
    return struct.pack("<" + "h" * len(quantized), *quantized), maximum_error


def bilinear(values: list[float], east_meters: float, north_meters: float) -> float:
    x = max(0.0, min(GRID_SIZE - 1.0, (east_meters + HALF_EXTENT_METERS) / GRID_SPACING_METERS))
    z = max(0.0, min(GRID_SIZE - 1.0, (north_meters + HALF_EXTENT_METERS) / GRID_SPACING_METERS))
    x0 = int(math.floor(x))
    z0 = int(math.floor(z))
    x1 = min(GRID_SIZE - 1, x0 + 1)
    z1 = min(GRID_SIZE - 1, z0 + 1)
    tx = x - x0
    tz = z - z0
    a = values[z0 * GRID_SIZE + x0] * (1.0 - tx) + values[z0 * GRID_SIZE + x1] * tx
    b = values[z1 * GRID_SIZE + x0] * (1.0 - tx) + values[z1 * GRID_SIZE + x1] * tx
    return a * (1.0 - tz) + b * tz


def write_hillshade(values: list[float], output_path: Path) -> None:
    pixels: list[int] = []
    azimuth = math.radians(315.0)
    altitude = math.radians(38.0)
    for row in range(GRID_SIZE):
        south = max(0, row - 1)
        north = min(GRID_SIZE - 1, row + 1)
        for column in range(GRID_SIZE):
            west = max(0, column - 1)
            east = min(GRID_SIZE - 1, column + 1)
            dzdx = (values[row * GRID_SIZE + east] - values[row * GRID_SIZE + west]) / max(
                1.0, (east - west) * GRID_SPACING_METERS
            )
            dzdy = (values[north * GRID_SIZE + column] - values[south * GRID_SIZE + column]) / max(
                1.0, (north - south) * GRID_SPACING_METERS
            )
            slope = math.atan(math.hypot(dzdx, dzdy))
            aspect = math.atan2(dzdy, -dzdx)
            light = math.sin(altitude) * math.cos(slope) + math.cos(altitude) * math.sin(slope) * math.cos(
                azimuth - aspect
            )
            pixels.append(max(0, min(255, int(round(128.0 + 112.0 * light)))))
    image = Image.new("L", (GRID_SIZE, GRID_SIZE))
    image.putdata(pixels)
    image = image.resize((964, 964), Image.Resampling.BICUBIC).convert("RGB")
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 0, 963, 40), fill=(18, 22, 28))
    draw.text((14, 12), "OPTION A - 100 m USGS 3DEP immutable far-ring source / north up", fill=(238, 242, 245))
    image.save(output_path, format="PNG", optimize=True)


def write_horizon(values: list[float], reference_elevation: float, output_path: Path) -> None:
    width, height = 2048, 512
    eye_elevation = reference_elevation + 2.0
    horizon_angles: list[float] = []
    for column in range(width):
        # World orientation: column zero/last = north, east at quarter width.
        angle = 2.0 * math.pi * column / width
        east_direction = math.sin(angle)
        north_direction = math.cos(angle)
        maximum = math.radians(-2.0)
        for step in range(1, 121):
            distance = step * GRID_SPACING_METERS
            east = east_direction * distance
            north = north_direction * distance
            elevation = bilinear(values, east, north)
            maximum = max(maximum, math.atan2(elevation - eye_elevation, distance))
        horizon_angles.append(maximum)

    image = Image.new("RGB", (width, height))
    pixels = image.load()
    vertical_min = math.radians(-8.0)
    vertical_max = math.radians(30.0)
    for y in range(height):
        t = y / (height - 1)
        sky = (
            int(round(77 + 112 * t)),
            int(round(124 + 88 * t)),
            int(round(170 + 63 * t)),
        )
        view_angle = vertical_max + (vertical_min - vertical_max) * t
        for x in range(width):
            if view_angle > horizon_angles[x]:
                pixels[x, y] = sky
            else:
                depth = min(1.0, (horizon_angles[x] - view_angle) / math.radians(8.0))
                pixels[x, y] = (
                    int(round(118 - 42 * depth)),
                    int(round(120 - 49 * depth)),
                    int(round(111 - 50 * depth)),
                )
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 0, width - 1, 34), fill=(18, 22, 28))
    draw.text((14, 10), "OPTION B - static 360-degree horizon derived from the same USGS 3DEP grid", fill=(238, 242, 245))
    for label, x in (("N", 0), ("E", width // 4), ("S", width // 2), ("W", 3 * width // 4), ("N", width - 15)):
        draw.text((x + (3 if x == 0 else 0), height - 22), label, fill=(242, 242, 238))
    image.save(output_path, format="PNG", optimize=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG_PATH)
    parser.add_argument("--raw-dir", type=Path, required=True, help="External raw receipt/cache directory")
    parser.add_argument("--preview-dir", type=Path, required=True, help="External A/B evidence directory")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument(
        "--frozen-resample",
        action="store_true",
        help="Bypass the live export and deterministically densify the frozen 400 m 3DEP source",
    )
    parser.add_argument("--frozen-source", type=Path, default=DEFAULT_FROZEN_TERRAIN)
    arguments = parser.parse_args()

    raw_directory = require_external_directory(arguments.raw_dir)
    preview_directory = require_external_directory(arguments.preview_dir)
    config = load_config(arguments.config)
    if arguments.frozen_resample:
        image, receipt = resample_frozen_far_grid(config, arguments.frozen_source)
        atomic_write_json(raw_directory / "far_dem_frozen_resample_receipt.json", receipt)
    else:
        image, receipt = export_dem(config, raw_directory)
    values = elevations_south_to_north(image)
    reference = float(config["origin"]["elevation_msl_meters"])
    payload, maximum_error = build_quantized_payload(values, reference)

    hillshade_path = preview_directory / "mountain_option_a_100m_dem_hillshade.png"
    horizon_path = preview_directory / "mountain_option_b_dem_horizon_panorama.png"
    write_hillshade(values, hillshade_path)
    write_horizon(values, reference, horizon_path)

    source_record = {
        "schema_version": 1,
        "label": "KBDU immutable 100 m far terrain / not for navigation",
        "generated_utc": utc_now(),
        "source": {
            "agency": "United States Geological Survey",
            "dataset": "3D Elevation Program (3DEP) Bare Earth DEM",
            "service_url": SERVICE_URL,
            "license_name": "United States government work / public domain",
            "license_spdx": "LicenseRef-US-Government-Work-Public-Domain",
            "license_url": "https://www.usgs.gov/the-national-map-data-delivery/gis-data-download",
            "credit": "USGS, The National Map: 3D Elevation Program",
            "retrieved_utc": receipt["retrieved_utc"],
            "source_mode": receipt.get("raw_source_mode", "live_3dep_f32_export"),
            "raw_source_sha256": receipt.get("raw_source_sha256", receipt.get("raw_tiff_sha256")),
            "raw_source_bytes": receipt.get("raw_source_bytes", receipt.get("raw_tiff_bytes")),
            "service_metadata_sha256": receipt.get("service_metadata_sha256"),
            "export_response_sha256": receipt.get("export_response_sha256"),
            "request_sha256": receipt.get("request_sha256"),
            "source_path": receipt.get("source_path"),
            "source_snapshot_id": receipt.get("source_snapshot_id"),
            "source_retrieved_utc": receipt.get("source_retrieved_utc"),
            "frozen_fetch_receipt_sha256": receipt.get("frozen_fetch_receipt_sha256"),
            "frozen_height_payload_sha256": receipt.get("frozen_height_payload_sha256"),
            "requested_extent_wgs84": receipt["requested_extent_wgs84"],
            "returned_extent_wgs84": receipt["returned_extent_wgs84"],
            "maximum_extent_error_degrees": receipt["maximum_extent_error_degrees"],
        },
        "grid": {
            "width": GRID_SIZE,
            "height": GRID_SIZE,
            "sample_count": len(values),
            "spacing_meters": GRID_SPACING_METERS,
            "half_extent_meters": HALF_EXTENT_METERS,
            "inner_half_extent_meters": INNER_HALF_EXTENT_METERS,
            "skirt_depth_meters": SKIRT_DEPTH_METERS,
            "row_order": "south_to_north",
            "column_order": "west_to_east",
            "origin_elevation_msl_meters": reference,
            "minimum_elevation_msl_meters": min(values),
            "maximum_elevation_msl_meters": max(values),
            "quantization_meters": HEIGHT_QUANTIZATION_METERS,
            "maximum_quantization_error_meters": maximum_error,
            "encoding": "base64 little-endian signed int16 offsets from origin elevation",
            "height_payload_sha256": sha256_bytes(payload),
            "height_payload_base64": base64.b64encode(payload).decode("ascii"),
        },
        "rendering_constraints": {
            "immutable_mesh": True,
            "offline_baked_normals": True,
            "sealed_outer_and_inner_skirts": True,
            "runtime_lod_groups": False,
            "crossfade_or_dither": False,
            "billboard": False,
            "camera_relative_scaling": False,
        },
        "source_fidelity_disclosure": (
            "The 100 m grid is a deterministic bilinear resampling of the frozen 400 m USGS 3DEP source. "
            "It adds mesh density and smoother baked normals, not additional source elevation detail."
            if arguments.frozen_resample
            else "The 100 m grid was exported directly from the live USGS 3DEP service."
        ),
        "external_previews": {
            "option_a_hillshade": {
                "file_name": hillshade_path.name,
                "sha256": sha256_file(hillshade_path),
            },
            "option_b_horizon": {
                "file_name": horizon_path.name,
                "sha256": sha256_file(horizon_path),
            },
        },
    }
    atomic_write_json(arguments.output, source_record)
    print(json.dumps({"status": "pass", "output": str(arguments.output), "source": source_record["source"], "grid": {k: v for k, v in source_record["grid"].items() if k != "height_payload_base64"}, "external_previews": source_record["external_previews"]}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
