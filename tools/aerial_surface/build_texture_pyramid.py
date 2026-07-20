#!/usr/bin/env python3
"""Build a hash-pinned, ENU-aligned KBDU aerial texture pyramid.

Raw ArcGIS metadata and cache tiles are always kept beneath an external raw
cache.  Compact JPEG derivatives, paired Unity importer metadata, a source
snapshot, and a runtime handoff manifest are deterministic for a fixed config
and source snapshot.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import math
import os
import platform
import socket
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable, Mapping, Sequence

import numpy as np
from PIL import Image, ImageStat, __version__ as pillow_version
import pyproj
import rasterio
import scipy
from scipy import ndimage
from rasterio.crs import CRS
from rasterio.transform import from_bounds, from_origin
from rasterio.warp import Resampling, reproject


SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parents[1]
DEFAULT_CONFIG = SCRIPT_DIR / "config.json"
MANIFEST_NAME = "KBDU_AerialSurfaceV1_SourceManifest.json"
SNAPSHOT_NAME = "KBDU_AerialSurfaceV1_SourceSnapshot.json"
USER_AGENT = "T2-QuestFlight-AerialSurface/1.0 (bounded CC0 authoring fetch)"


def canonical_json_bytes(value: Any) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n").encode("utf-8")


def sha256_bytes(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def atomic_write_bytes(path: Path, payload: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(payload)
        os.replace(temporary, path)
    except Exception:
        try:
            os.unlink(temporary)
        except FileNotFoundError:
            pass
        raise


def atomic_write_json(path: Path, value: Any) -> None:
    atomic_write_bytes(path, canonical_json_bytes(value))


def load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"Expected a JSON object: {path}")
    return value


def require_external(path: Path, *, create: bool = True) -> Path:
    candidate = path.expanduser().resolve()
    try:
        candidate.relative_to(REPO_ROOT.resolve())
    except ValueError:
        pass
    else:
        raise ValueError(f"Raw source/cache data must remain outside the repository: {candidate}")
    if create:
        candidate.mkdir(parents=True, exist_ok=True)
    return candidate


def load_config(path: Path = DEFAULT_CONFIG) -> dict[str, Any]:
    config = load_json(path)
    if config.get("schema_version") != 1:
        raise ValueError(f"Unsupported aerial-surface config schema: {config.get('schema_version')!r}")
    validate_config(config)
    return config


def validate_config(config: Mapping[str, Any]) -> None:
    source = config["source"]
    urls = [
        source["arcgis_item_url"],
        source["arcgis_item_metadata_url"],
        source["service_url"],
        source["service_metadata_url"],
        source["tile_url_template"],
        source["license_url"],
    ]
    if any("google" in str(url).lower() for url in urls):
        raise ValueError("Google imagery and Google-hosted imagery endpoints are prohibited")
    for url in urls:
        if not str(url).startswith("https://"):
            raise ValueError(f"Every source and license URL must use HTTPS: {url}")
    if source["license_spdx"] != "CC0-1.0":
        raise ValueError("Selected City of Boulder source must remain explicitly CC0-1.0")
    if config["water_policy"]["mode"] != "orthophoto_only":
        raise ValueError("Water must remain orthophoto-only")
    guards = config["scope_guards"]
    forbidden_true = (
        "world_extent_expansion",
        "runway_changes",
        "sky_changes",
        "mountain_changes",
        "google_imagery_allowed",
    )
    if any(bool(guards[key]) for key in forbidden_true):
        raise ValueError("Aerial surface scope guard was relaxed")
    if guards["far_texture_replacement"] is not True:
        raise ValueError("The fixed 24 km far-density replacement is required")
    if guards["runtime_controller_required"] is not True:
        raise ValueError("GeospatialMipController ownership is required")
    if not config["runtime"]["replacement_only"]:
        raise ValueError("Runtime assets must be budgeted as one-for-one replacements")
    if config["runtime"]["streaming_mipmaps"] is not True:
        raise ValueError("Every aerial texture must enable mip streaming for GeospatialMipController ownership")

    assets = config["assets"]
    ids = [str(asset["id"]) for asset in assets]
    if len(ids) != len(set(ids)):
        raise ValueError("Duplicate aerial asset identifiers")
    expected = {"near_4km", "mid_nw", "mid_ne", "mid_sw", "mid_se", "mid_base_12km", "far_24km"}
    if set(ids) != expected:
        raise ValueError(f"Unexpected fixed tile set: {set(ids)!r}")
    runtime = config["runtime"]
    if runtime.get("selection_mode") != (
        "explicit GeospatialMipController-managed mip residency for seven fixed ENU shader tiles; "
        "no camera transform ownership or hierarchy construction"
    ):
        raise ValueError("GeospatialMipController selection contract changed")
    if runtime.get("controller_type") != "QuestFlightLab.Environment.GeospatialMipController":
        raise ValueError("Unexpected geospatial mip controller type")
    if list(runtime.get("controller_owned_texture_ids", [])) != ids:
        raise ValueError("GeospatialMipController must own all seven configured texture ids in authored order")
    for asset in assets:
        bounds = [float(value) for value in asset["local_bounds_meters"]]
        width, height = [int(value) for value in asset["dimensions_pixels"]]
        if len(bounds) != 4 or bounds[0] >= bounds[2] or bounds[1] >= bounds[3]:
            raise ValueError(f"Invalid local bounds for {asset['id']}")
        if width <= 0 or height <= 0 or width & (width - 1) or height & (height - 1):
            raise ValueError(f"Runtime dimensions must be positive powers of two: {asset['id']}")
        if int(asset["source_lod"]) not in (15, 16, 17, 18):
            raise ValueError(f"Unreviewed source cache LOD: {asset['source_lod']}")
        if not str(asset["unity_path"]).startswith(
            "QuestFlight/Assets/Production/Environment/AerialSurfaceV1/Ground/"
        ):
            raise ValueError(f"Asset escapes the dedicated aerial-surface handoff path: {asset['id']}")


def astc_mip_chain_bytes(width: int, height: int, block_width: int, block_height: int) -> int:
    total = 0
    while True:
        total += math.ceil(width / block_width) * math.ceil(height / block_height) * 16
        if width == 1 and height == 1:
            return total
        width = max(1, width // 2)
        height = max(1, height // 2)


def unity_guid(unity_path: str) -> str:
    normalized = unity_path.replace("\\", "/")
    return hashlib.sha256(("QuestFlight:AerialSurfaceV1:" + normalized).encode("utf-8")).hexdigest()[:32]


def unity_texture_meta(asset: Mapping[str, Any], config: Mapping[str, Any]) -> bytes:
    guid = unity_guid(str(asset["unity_path"]))
    width, height = [int(value) for value in asset["dimensions_pixels"]]
    maximum = max(width, height)
    priority = 0
    mip_bias = float(asset["mip_bias"])
    texture_format = int(asset["android_texture_format"])
    user_data = (
        "QuestFlight AerialSurfaceV1; source=CityOfBoulder2020CC0; "
        f"manifest={MANIFEST_NAME}; tile={asset['id']}; owner=GeospatialMipController; ENU-fixed-UV"
    )
    return f"""fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 1
  streamingMipmapsPriority: {priority}
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: {maximum}
  textureSettings:
    serializedVersion: 2
    filterMode: 2
    aniso: 8
    mipBias: {mip_bias}
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 80
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 0
  alphaIsTransparency: 0
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: {maximum}
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 4
    buildTarget: Android
    maxTextureSize: {maximum}
    resizeAlgorithm: 0
    textureFormat: {texture_format}
    textureCompression: 1
    compressionQuality: 100
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 1
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData: {user_data}
  assetBundleName:
  assetBundleVariant:
""".encode("utf-8")


def json_importer_meta(unity_path: str, user_data: str) -> bytes:
    return f"""fileFormatVersion: 2
guid: {unity_guid(unity_path)}
TextScriptImporter:
  externalObjects: {{}}
  userData: {user_data}
  assetBundleName:
  assetBundleVariant:
""".encode("utf-8")


def assert_allowed_url(url: str, allowed_hosts: Sequence[str]) -> None:
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme != "https" or parsed.hostname not in set(allowed_hosts):
        raise ValueError(f"Network URL is outside the reviewed HTTPS allowlist: {url}")
    if "google" in url.lower():
        raise ValueError("Google imagery is prohibited")


def request_bytes(
    url: str,
    allowed_hosts: Sequence[str],
    *,
    attempts: int,
    timeout_seconds: int,
) -> bytes:
    assert_allowed_url(url, allowed_hosts)
    error: Exception | None = None
    for attempt in range(attempts):
        request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": "*/*"})
        try:
            with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
                final_url = response.geturl()
                assert_allowed_url(final_url, allowed_hosts)
                payload = response.read()
                if not payload:
                    raise RuntimeError(f"Empty source response: {url}")
                return payload
        except (urllib.error.URLError, TimeoutError, socket.timeout, RuntimeError) as current:
            error = current
            if attempt + 1 < attempts:
                time.sleep(min(8, 2**attempt))
    assert error is not None
    raise RuntimeError(f"Unable to retrieve reviewed source URL after {attempts} attempts: {url}") from error


ITEM_CONTRACT_FIELDS = (
    "id",
    "owner",
    "created",
    "modified",
    "title",
    "type",
    "description",
    "snippet",
    "tags",
    "extent",
    "spatialReference",
    "accessInformation",
    "licenseInfo",
    "url",
    "access",
    "size",
)


def normalize_arcgis_item(item: Mapping[str, Any]) -> dict[str, Any]:
    """Remove counters and last-viewed fields that do not alter the source contract."""
    return {field: item.get(field) for field in ITEM_CONTRACT_FIELDS}


def normalize_service_metadata(service: Mapping[str, Any]) -> dict[str, Any]:
    tile_info = service.get("tileInfo") or {}
    lods = [
        {
            "level": int(lod["level"]),
            "resolution": float(lod["resolution"]),
            "scale": float(lod["scale"]),
        }
        for lod in tile_info.get("lods", [])
    ]
    return {
        "currentVersion": service.get("currentVersion"),
        "serviceDescription": service.get("serviceDescription"),
        "description": service.get("description"),
        "copyrightText": service.get("copyrightText"),
        "capabilities": service.get("capabilities"),
        "spatialReference": service.get("spatialReference"),
        "fullExtent": service.get("fullExtent"),
        "maxImageWidth": service.get("maxImageWidth"),
        "maxImageHeight": service.get("maxImageHeight"),
        "tileInfo": {
            "rows": tile_info.get("rows"),
            "cols": tile_info.get("cols"),
            "dpi": tile_info.get("dpi"),
            "format": tile_info.get("format"),
            "compressionQuality": tile_info.get("compressionQuality"),
            "origin": tile_info.get("origin"),
            "spatialReference": tile_info.get("spatialReference"),
            "lods": lods,
        },
    }


def contract_sha256(value: Mapping[str, Any]) -> str:
    return sha256_bytes(canonical_json_bytes(value))


def parse_json_response(payload: bytes, url: str) -> dict[str, Any]:
    try:
        value = json.loads(payload.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError(f"Source metadata is not valid UTF-8 JSON: {url}") from error
    if not isinstance(value, dict) or value.get("error"):
        raise ValueError(f"Source metadata response is invalid: {url}: {value.get('error')!r}")
    return value


def load_or_fetch_response(
    cache_path: Path,
    url: str,
    config: Mapping[str, Any],
    *,
    offline: bool,
    refresh: bool,
) -> tuple[bytes, dict[str, Any]]:
    if cache_path.is_file() and (offline or not refresh):
        payload = cache_path.read_bytes()
    elif offline:
        raise FileNotFoundError(f"Offline source response is absent: {cache_path}")
    else:
        processing = config["processing"]
        payload = request_bytes(
            url,
            config["source"]["allowed_network_hosts"],
            attempts=int(processing["network_attempts"]),
            timeout_seconds=int(processing["network_timeout_seconds"]),
        )
        atomic_write_bytes(cache_path, payload)
    return payload, parse_json_response(payload, url)


def validate_source_metadata(
    item: Mapping[str, Any],
    service: Mapping[str, Any],
    config: Mapping[str, Any],
) -> None:
    source = config["source"]
    if item.get("id") != source["arcgis_item_id"]:
        raise ValueError("ArcGIS item identifier changed")
    if item.get("url") != source["service_url"]:
        raise ValueError("ArcGIS item no longer points to the reviewed City service")
    if item.get("access") != source["required_access"]:
        raise ValueError("Reviewed City source is no longer public")
    if int(item.get("modified") or -1) != int(source["service_modified_epoch_ms"]):
        raise ValueError("ArcGIS item modification revision changed; re-audit source rights and content")
    license_text = str(item.get("licenseInfo") or "").lower()
    if source["license_evidence_required_substring"].lower() not in license_text:
        raise ValueError("ArcGIS item no longer carries its explicit CC0 license evidence")
    description = str(item.get("description") or "")
    for token in source["source_description_required_substrings"]:
        if str(token).lower() not in description.lower():
            raise ValueError(f"ArcGIS source description lost required provenance token: {token!r}")

    normalized = normalize_service_metadata(service)
    tile_info = normalized["tileInfo"]
    spatial_reference = tile_info.get("spatialReference") or normalized.get("spatialReference") or {}
    observed_wkid = spatial_reference.get("latestWkid") or spatial_reference.get("wkid")
    if int(observed_wkid or -1) != int(source["required_service_wkid"]):
        raise ValueError(f"Unexpected City cache CRS: {observed_wkid!r}")
    expected_pixels = int(source["required_cache_tile_pixels"])
    if int(tile_info.get("rows") or -1) != expected_pixels or int(tile_info.get("cols") or -1) != expected_pixels:
        raise ValueError("City cache tile dimensions changed")
    if str(tile_info.get("format") or "").upper() != str(source["required_cache_format"]).upper():
        raise ValueError("City cache format changed")
    lod_levels = {int(lod["level"]) for lod in tile_info.get("lods", [])}
    required_levels = {int(asset["source_lod"]) for asset in config["assets"]}
    if not required_levels.issubset(lod_levels):
        raise ValueError(f"City cache lost required levels: {sorted(required_levels - lod_levels)}")


def local_crs(config: Mapping[str, Any]) -> CRS:
    origin = config["origin"]
    projection = (
        f"+proj=aeqd +lat_0={float(origin['latitude_degrees']):.12f} "
        f"+lon_0={float(origin['longitude_degrees']):.12f} +datum=WGS84 +units=m +no_defs"
    )
    return CRS.from_string(projection)


def densified_bounds_points(bounds: Sequence[float], segments: int) -> tuple[list[float], list[float]]:
    min_x, min_y, max_x, max_y = map(float, bounds)
    if segments < 1:
        raise ValueError("Edge densification must use at least one segment")
    xs: list[float] = []
    ys: list[float] = []
    for index in range(segments + 1):
        t = index / segments
        x = min_x + (max_x - min_x) * t
        y = min_y + (max_y - min_y) * t
        xs.extend((x, x, min_x, max_x))
        ys.extend((min_y, max_y, y, y))
    return xs, ys


def web_mercator_bounds(
    local_bounds: Sequence[float],
    config: Mapping[str, Any],
) -> list[float]:
    segments = int(config["processing"]["edge_densification_segments"])
    east, north = densified_bounds_points(local_bounds, segments)
    transformer = pyproj.Transformer.from_crs(local_crs(config), "EPSG:3857", always_xy=True)
    web_x, web_y = transformer.transform(east, north)
    return [float(min(web_x)), float(min(web_y)), float(max(web_x)), float(max(web_y))]


def lod_record(service: Mapping[str, Any], level: int) -> dict[str, Any]:
    tile_info = service.get("tileInfo", service)
    for record in tile_info["lods"]:
        if int(record["level"]) == int(level):
            return dict(record)
    raise ValueError(f"Cache level is absent: {level}")


def tile_range_for_bounds(
    bounds: Sequence[float],
    tile_info: Mapping[str, Any],
    level: int,
    *,
    padding: int,
) -> dict[str, Any]:
    min_x, min_y, max_x, max_y = map(float, bounds)
    lod = lod_record(tile_info, level)
    resolution = float(lod["resolution"])
    rows = int(tile_info["rows"])
    columns = int(tile_info["cols"])
    origin_x = float(tile_info["origin"]["x"])
    origin_y = float(tile_info["origin"]["y"])
    span_x = columns * resolution
    span_y = rows * resolution
    column_min = math.floor((min_x - origin_x) / span_x) - padding
    column_max = math.ceil((max_x - origin_x) / span_x) - 1 + padding
    row_min = math.floor((origin_y - max_y) / span_y) - padding
    row_max = math.ceil((origin_y - min_y) / span_y) - 1 + padding
    return {
        "level": int(level),
        "row_min": int(row_min),
        "row_max": int(row_max),
        "column_min": int(column_min),
        "column_max": int(column_max),
        "resolution_web_mercator_meters": resolution,
    }


def union_tile_ranges(ranges: Iterable[Mapping[str, Any]]) -> dict[int, dict[str, Any]]:
    by_level: dict[int, list[Mapping[str, Any]]] = defaultdict(list)
    for record in ranges:
        by_level[int(record["level"])].append(record)
    return {
        level: {
            "level": level,
            "row_min": min(int(record["row_min"]) for record in records),
            "row_max": max(int(record["row_max"]) for record in records),
            "column_min": min(int(record["column_min"]) for record in records),
            "column_max": max(int(record["column_max"]) for record in records),
            "resolution_web_mercator_meters": float(records[0]["resolution_web_mercator_meters"]),
        }
        for level, records in sorted(by_level.items())
    }


def iter_tile_coordinates(tile_range: Mapping[str, Any]) -> Iterable[tuple[int, int, int]]:
    level = int(tile_range["level"])
    for row in range(int(tile_range["row_min"]), int(tile_range["row_max"]) + 1):
        for column in range(int(tile_range["column_min"]), int(tile_range["column_max"]) + 1):
            yield level, row, column


def tile_relative_path(level: int, row: int, column: int) -> str:
    return f"tiles/l{level:02d}/r{row:08d}_c{column:08d}.bin"


def tile_url(config: Mapping[str, Any], level: int, row: int, column: int) -> str:
    return str(config["source"]["tile_url_template"]).format(level=level, row=row, column=column)


def validate_tile_payload(payload: bytes, expected_pixels: int, label: str) -> None:
    try:
        with Image.open(io.BytesIO(payload)) as image:
            image.load()
            if image.size != (expected_pixels, expected_pixels):
                raise ValueError(f"Unexpected cache tile dimensions for {label}: {image.size}")
            if image.mode not in ("RGB", "RGBA", "P", "L"):
                raise ValueError(f"Unexpected cache tile mode for {label}: {image.mode}")
    except Exception as error:
        if isinstance(error, ValueError):
            raise
        raise ValueError(f"ArcGIS cache response is not a valid image tile: {label}") from error


def tile_set_sha256(records: Sequence[Mapping[str, Any]]) -> str:
    contract = [
        {
            "level": int(record["level"]),
            "row": int(record["row"]),
            "column": int(record["column"]),
            "url": str(record["url"]),
            "bytes": int(record["bytes"]),
            "sha256": str(record["sha256"]),
        }
        for record in sorted(records, key=lambda item: (int(item["level"]), int(item["row"]), int(item["column"])))
    ]
    return sha256_bytes(canonical_json_bytes(contract))


def acquire_tiles(
    raw_cache: Path,
    tile_ranges: Mapping[int, Mapping[str, Any]],
    config: Mapping[str, Any],
    *,
    offline: bool,
    refresh: bool,
    expected_snapshot: Mapping[str, Any] | None,
) -> list[dict[str, Any]]:
    source = config["source"]
    processing = config["processing"]
    expected_pixels = int(source["required_cache_tile_pixels"])
    expected_records: dict[tuple[int, int, int], Mapping[str, Any]] = {}
    if expected_snapshot is not None:
        expected_records = {
            (int(record["level"]), int(record["row"]), int(record["column"])): record
            for record in expected_snapshot["tiles"]
        }
    planned = {
        coordinate
        for tile_range in tile_ranges.values()
        for coordinate in iter_tile_coordinates(tile_range)
    }
    if expected_snapshot is not None and planned != set(expected_records):
        missing = sorted(planned - set(expected_records))[:10]
        extra = sorted(set(expected_records) - planned)[:10]
        raise ValueError(f"Pinned snapshot tile plan differs from config; missing={missing}, extra={extra}")

    records: list[dict[str, Any]] = []
    for level, row, column in sorted(planned):
        relative = tile_relative_path(level, row, column)
        path = raw_cache / relative
        url = tile_url(config, level, row, column)
        expected = expected_records.get((level, row, column))
        if expected is not None and str(expected["url"]) != url:
            raise ValueError(f"Pinned tile URL differs from reviewed template: {level}/{row}/{column}")

        if path.is_file() and (offline or expected is not None or not refresh):
            payload = path.read_bytes()
        elif offline or expected is not None:
            raise FileNotFoundError(f"Pinned/offline cache tile is absent: {path}")
        else:
            payload = request_bytes(
                url,
                source["allowed_network_hosts"],
                attempts=int(processing["network_attempts"]),
                timeout_seconds=int(processing["network_timeout_seconds"]),
            )
            validate_tile_payload(payload, expected_pixels, f"{level}/{row}/{column}")
            atomic_write_bytes(path, payload)

        validate_tile_payload(payload, expected_pixels, f"{level}/{row}/{column}")
        record = {
            "level": level,
            "row": row,
            "column": column,
            "url": url,
            "cache_relative_path": relative,
            "bytes": len(payload),
            "sha256": sha256_bytes(payload),
        }
        if expected is not None:
            for field in ("bytes", "sha256", "cache_relative_path"):
                if record[field] != expected[field]:
                    raise ValueError(f"Pinned cache tile {field} mismatch: {level}/{row}/{column}")
        records.append(record)
    return records


def build_source_snapshot(
    config: Mapping[str, Any],
    item_payload: bytes,
    item: Mapping[str, Any],
    service_payload: bytes,
    service: Mapping[str, Any],
    tile_ranges: Mapping[int, Mapping[str, Any]],
    tiles: Sequence[Mapping[str, Any]],
) -> dict[str, Any]:
    item_contract = normalize_arcgis_item(item)
    service_contract = normalize_service_metadata(service)
    return {
        "schema_version": 1,
        "label": config["label"],
        "retrieved_utc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "source_id": config["source"]["id"],
        "arcgis_item_metadata_url": config["source"]["arcgis_item_metadata_url"],
        "arcgis_item_response_sha256": sha256_bytes(item_payload),
        "arcgis_item_contract": item_contract,
        "arcgis_item_contract_sha256": contract_sha256(item_contract),
        "service_metadata_url": config["source"]["service_metadata_url"],
        "service_metadata_response_sha256": sha256_bytes(service_payload),
        "service_contract": service_contract,
        "service_contract_sha256": contract_sha256(service_contract),
        "tile_ranges": [dict(tile_ranges[level]) for level in sorted(tile_ranges)],
        "tile_count": len(tiles),
        "tile_set_sha256": tile_set_sha256(tiles),
        "tiles": [dict(record) for record in tiles],
        "raw_policy": "Metadata responses and cache tiles remain external and are never committed.",
    }


def validate_snapshot_metadata(
    snapshot: Mapping[str, Any],
    config: Mapping[str, Any],
    item_payload: bytes,
    item: Mapping[str, Any],
    service_payload: bytes,
    service: Mapping[str, Any],
) -> None:
    if snapshot.get("schema_version") != 1 or snapshot.get("source_id") != config["source"]["id"]:
        raise ValueError("Unsupported or wrong-source snapshot")
    checks = {
        "arcgis_item_response_sha256": sha256_bytes(item_payload),
        "arcgis_item_contract_sha256": contract_sha256(normalize_arcgis_item(item)),
        "service_metadata_response_sha256": sha256_bytes(service_payload),
        "service_contract_sha256": contract_sha256(normalize_service_metadata(service)),
    }
    for field, observed in checks.items():
        if snapshot.get(field) != observed:
            raise ValueError(f"Pinned source snapshot metadata mismatch: {field}")


def compose_mosaic(
    raw_cache: Path,
    tile_range: Mapping[str, Any],
    tile_info: Mapping[str, Any],
    records: Sequence[Mapping[str, Any]],
) -> tuple[np.ndarray, Any]:
    expected_pixels_y = int(tile_info["rows"])
    expected_pixels_x = int(tile_info["cols"])
    row_min, row_max = int(tile_range["row_min"]), int(tile_range["row_max"])
    column_min, column_max = int(tile_range["column_min"]), int(tile_range["column_max"])
    mosaic = np.empty(
        (
            (row_max - row_min + 1) * expected_pixels_y,
            (column_max - column_min + 1) * expected_pixels_x,
            3,
        ),
        dtype=np.uint8,
    )
    by_coordinate = {
        (int(record["level"]), int(record["row"]), int(record["column"])): record
        for record in records
    }
    level = int(tile_range["level"])
    for _, row, column in iter_tile_coordinates(tile_range):
        record = by_coordinate[(level, row, column)]
        path = raw_cache / str(record["cache_relative_path"])
        if sha256_file(path) != record["sha256"]:
            raise ValueError(f"Cache tile changed after snapshot validation: {path}")
        with Image.open(path) as image_source:
            tile = np.asarray(image_source.convert("RGB"), dtype=np.uint8)
        y0 = (row - row_min) * expected_pixels_y
        x0 = (column - column_min) * expected_pixels_x
        mosaic[y0 : y0 + expected_pixels_y, x0 : x0 + expected_pixels_x] = tile

    lod = lod_record(tile_info, level)
    resolution = float(lod["resolution"])
    origin_x = float(tile_info["origin"]["x"])
    origin_y = float(tile_info["origin"]["y"])
    west = origin_x + column_min * expected_pixels_x * resolution
    north = origin_y - row_min * expected_pixels_y * resolution
    transform = from_origin(west, north, resolution, resolution)
    return mosaic, transform


def warp_mosaic_to_local_asset(
    mosaic: np.ndarray,
    source_transform: Any,
    asset: Mapping[str, Any],
    config: Mapping[str, Any],
    *,
    source_crs: CRS | str = "EPSG:3857",
    destination_crs: CRS | str | None = None,
) -> np.ndarray:
    width, height = [int(value) for value in asset["dimensions_pixels"]]
    min_x, min_y, max_x, max_y = map(float, asset["local_bounds_meters"])
    destination = np.zeros((3, height, width), dtype=np.uint8)
    target_crs = destination_crs or local_crs(config)
    destination_transform = from_bounds(min_x, min_y, max_x, max_y, width, height)
    with rasterio.Env(PROJ_NETWORK="OFF", GDAL_NUM_THREADS="1"):
        reproject(
            source=np.moveaxis(mosaic, -1, 0),
            destination=destination,
            src_transform=source_transform,
            src_crs=source_crs,
            dst_transform=destination_transform,
            dst_crs=target_crs,
            resampling=Resampling.lanczos,
            num_threads=1,
            init_dest_nodata=True,
            dst_nodata=0,
        )
    return np.moveaxis(destination, 0, -1)


def encode_jpeg(image: np.ndarray, processing: Mapping[str, Any]) -> bytes:
    buffer = io.BytesIO()
    subsampling = 0 if str(processing["jpeg_subsampling"]) == "4:4:4" else 2
    Image.fromarray(image).save(
        buffer,
        format="JPEG",
        quality=int(processing["jpeg_quality"]),
        subsampling=subsampling,
        optimize=bool(processing["jpeg_optimize"]),
        progressive=bool(processing["jpeg_progressive"]),
    )
    return buffer.getvalue()


def channel_statistics(image: np.ndarray) -> list[dict[str, float]]:
    sample = Image.fromarray(image)
    sample.thumbnail((512, 512), Image.Resampling.BOX)
    stats = ImageStat.Stat(sample)
    return [
        {
            "mean": round(float(stats.mean[index]), 6),
            "standard_deviation": round(float(stats.stddev[index]), 6),
        }
        for index in range(3)
    ]


def asset_tile_records(
    records: Sequence[Mapping[str, Any]], tile_range: Mapping[str, Any]
) -> list[Mapping[str, Any]]:
    return [
        record
        for record in records
        if int(record["level"]) == int(tile_range["level"])
        and int(tile_range["row_min"]) <= int(record["row"]) <= int(tile_range["row_max"])
        and int(tile_range["column_min"]) <= int(record["column"]) <= int(tile_range["column_max"])
    ]


def verify_replacement_asset(asset: Mapping[str, Any]) -> None:
    path = REPO_ROOT / str(asset["replacement_path"])
    if not path.is_file():
        raise FileNotFoundError(f"Baseline replacement target is absent: {path}")
    observed = sha256_file(path)
    if observed != asset["replacement_sha256"]:
        raise ValueError(
            f"Baseline replacement target changed for {asset['id']}: expected {asset['replacement_sha256']}, got {observed}"
        )


def build_assets(
    raw_cache: Path,
    output_dir: Path,
    config: Mapping[str, Any],
    service_contract: Mapping[str, Any],
    asset_plans: Mapping[str, Mapping[str, Any]],
    union_ranges: Mapping[int, Mapping[str, Any]],
    tile_records: Sequence[Mapping[str, Any]],
) -> tuple[list[dict[str, Any]], dict[str, Image.Image]]:
    output_dir.mkdir(parents=True, exist_ok=True)
    processing = config["processing"]
    result_records: list[dict[str, Any]] = []
    previews: dict[str, Image.Image] = {}
    assets_by_lod: dict[int, list[Mapping[str, Any]]] = defaultdict(list)
    for asset in config["assets"]:
        assets_by_lod[int(asset["source_lod"])].append(asset)

    for level in sorted(assets_by_lod):
        mosaic, transform = compose_mosaic(raw_cache, union_ranges[level], service_contract["tileInfo"], tile_records)
        for asset in assets_by_lod[level]:
            verify_replacement_asset(asset)
            pixels = warp_mosaic_to_local_asset(mosaic, transform, asset, config)
            statistics = channel_statistics(pixels)
            minimum_stddev = float(processing["minimum_channel_standard_deviation"])
            if min(record["standard_deviation"] for record in statistics) < minimum_stddev:
                raise ValueError(f"Derived imagery appears blank or corrupt: {asset['id']}: {statistics}")

            payload = encode_jpeg(pixels, processing)
            output_path = output_dir / str(asset["output_name"])
            atomic_write_bytes(output_path, payload)
            atomic_write_bytes(Path(str(output_path) + ".meta"), unity_texture_meta(asset, config))
            width, height = [int(value) for value in asset["dimensions_pixels"]]
            bounds = [float(value) for value in asset["local_bounds_meters"]]
            block_width, block_height = [int(value) for value in asset["android_astc_block"]]
            source_records = asset_tile_records(tile_records, asset_plans[str(asset["id"])]["tile_range"])
            record = {
                "id": asset["id"],
                "shader_property": asset["shader_property"],
                "path": asset["unity_path"],
                "output_name": asset["output_name"],
                "sha256": sha256_bytes(payload),
                "bytes": len(payload),
                "dimensions_pixels": [width, height],
                "local_bounds_meters": bounds,
                "nominal_cell_bounds_meters": asset.get("nominal_cell_bounds_meters"),
                "world_min_inverse_size": [
                    bounds[0],
                    bounds[1],
                    1.0 / (bounds[2] - bounds[0]),
                    1.0 / (bounds[3] - bounds[1]),
                ],
                "meters_per_texel": [
                    (bounds[2] - bounds[0]) / width,
                    (bounds[3] - bounds[1]) / height,
                ],
                "channel_statistics": statistics,
                "source_cache": {
                    "level": level,
                    "web_mercator_resolution_meters": float(
                        asset_plans[str(asset["id"])]["tile_range"]["resolution_web_mercator_meters"]
                    ),
                    "approximate_ground_resolution_at_origin_meters": float(
                        asset_plans[str(asset["id"])]["approximate_ground_resolution_at_origin_meters"]
                    ),
                    "tile_range": dict(asset_plans[str(asset["id"])]["tile_range"]),
                    "tile_count": len(source_records),
                    "tile_set_sha256": tile_set_sha256(source_records),
                },
                "android_import": {
                    "format": f"ASTC_{block_width}x{block_height}",
                    "estimated_full_mip_chain_bytes": astc_mip_chain_bytes(
                        width, height, block_width, block_height
                    ),
                    "mipmaps": True,
                    "streaming_mipmaps": bool(config["runtime"]["streaming_mipmaps"]),
                    "filter": "trilinear",
                    "anisotropy": 8,
                    "mip_bias": float(asset["mip_bias"]),
                    "wrap": "clamp",
                },
                "replacement": {
                    "path": asset["replacement_path"],
                    "sha256": asset["replacement_sha256"],
                    "mode": "one_for_one",
                    "dimensions_pixels": asset.get("replacement_dimensions_pixels", asset["dimensions_pixels"]),
                    "android_astc_block": asset.get("replacement_android_astc_block", asset["android_astc_block"]),
                    "estimated_full_mip_chain_bytes": astc_mip_chain_bytes(
                        int(asset.get("replacement_dimensions_pixels", asset["dimensions_pixels"])[0]),
                        int(asset.get("replacement_dimensions_pixels", asset["dimensions_pixels"])[1]),
                        int(asset.get("replacement_android_astc_block", asset["android_astc_block"])[0]),
                        int(asset.get("replacement_android_astc_block", asset["android_astc_block"])[1]),
                    ),
                },
                "unity_meta": {
                    "guid": unity_guid(str(asset["unity_path"])),
                    "sha256": sha256_file(Path(str(output_path) + ".meta")),
                },
            }
            result_records.append(record)
            preview = Image.fromarray(pixels)
            preview.thumbnail((512, 512), Image.Resampling.LANCZOS)
            previews[str(asset["id"])] = preview.copy()
        del mosaic
    return sorted(result_records, key=lambda record: str(record["id"])), previews


def sample_local_bounds(
    image: Image.Image,
    source_bounds: Sequence[float],
    target_bounds: Sequence[float],
    target_width: int,
    target_height: int,
) -> np.ndarray:
    source = np.moveaxis(np.asarray(image.convert("RGB"), dtype=np.uint8), -1, 0)
    destination = np.zeros((3, target_height, target_width), dtype=np.uint8)
    with rasterio.Env(PROJ_NETWORK="OFF", GDAL_NUM_THREADS="1"):
        reproject(
            source=source,
            destination=destination,
            src_transform=from_bounds(*map(float, source_bounds), image.width, image.height),
            src_crs="EPSG:3857",
            dst_transform=from_bounds(*map(float, target_bounds), target_width, target_height),
            dst_crs="EPSG:3857",
            resampling=Resampling.bilinear,
            num_threads=1,
            init_dest_nodata=True,
            dst_nodata=0,
        )
    return np.moveaxis(destination, 0, -1)


def overlap_difference(
    first: Image.Image,
    first_bounds: Sequence[float],
    second: Image.Image,
    second_bounds: Sequence[float],
) -> dict[str, Any]:
    overlap = [
        max(float(first_bounds[0]), float(second_bounds[0])),
        max(float(first_bounds[1]), float(second_bounds[1])),
        min(float(first_bounds[2]), float(second_bounds[2])),
        min(float(first_bounds[3]), float(second_bounds[3])),
    ]
    if overlap[0] >= overlap[2] or overlap[1] >= overlap[3]:
        raise ValueError("Seam pair has no authored local overlap")
    first_meters_per_texel = max(
        (float(first_bounds[2]) - float(first_bounds[0])) / first.width,
        (float(first_bounds[3]) - float(first_bounds[1])) / first.height,
    )
    second_meters_per_texel = max(
        (float(second_bounds[2]) - float(second_bounds[0])) / second.width,
        (float(second_bounds[3]) - float(second_bounds[1])) / second.height,
    )
    common_resolution = max(first_meters_per_texel, second_meters_per_texel)
    target_width = max(1, round((overlap[2] - overlap[0]) / common_resolution))
    target_height = max(1, round((overlap[3] - overlap[1]) / common_resolution))
    first_samples = sample_local_bounds(first, first_bounds, overlap, target_width, target_height)
    second_samples = sample_local_bounds(second, second_bounds, overlap, target_width, target_height)
    difference = np.abs(first_samples.astype(np.int16) - second_samples.astype(np.int16))
    mean = [round(float(value), 6) for value in difference.mean(axis=(0, 1))]
    return {
        "local_overlap_bounds_meters": overlap,
        "comparison_dimensions_pixels": [target_width, target_height],
        "mean_absolute_channel_delta": mean,
        "maximum_mean_absolute_channel_delta": max(mean),
    }


def seam_report(output_dir: Path, config: Mapping[str, Any]) -> dict[str, Any]:
    assets = {str(asset["id"]): asset for asset in config["assets"]}
    images: dict[str, Image.Image] = {}
    for identifier in ("mid_nw", "mid_ne", "mid_sw", "mid_se"):
        with Image.open(output_dir / str(assets[identifier]["output_name"])) as source:
            images[identifier] = source.convert("RGB")

    def compare(first: str, second: str) -> dict[str, Any]:
        return overlap_difference(
            images[first],
            assets[first]["local_bounds_meters"],
            images[second],
            assets[second]["local_bounds_meters"],
        )

    comparisons = {
        "north_west_to_east": compare("mid_nw", "mid_ne"),
        "south_west_to_east": compare("mid_sw", "mid_se"),
        "west_north_to_south": compare("mid_nw", "mid_sw"),
        "east_north_to_south": compare("mid_ne", "mid_se"),
    }
    limit = float(config["processing"]["maximum_seam_mean_absolute_channel_delta"])
    for value in comparisons.values():
        value["limit"] = limit
        value["passed"] = value["maximum_mean_absolute_channel_delta"] <= limit
    if not all(value["passed"] for value in comparisons.values()):
        raise ValueError(f"One or more quadrant overlaps exceed the seam limit: {comparisons}")
    return comparisons


def phase_correlation_shift(first: np.ndarray, second: np.ndarray) -> dict[str, Any]:
    if first.shape != second.shape or first.ndim != 2 or first.shape[0] != first.shape[1]:
        raise ValueError("Phase-correlation inputs must be equal square grayscale arrays")
    size = first.shape[0]
    first_float = first.astype(np.float32)
    second_float = second.astype(np.float32)
    first_edges = ndimage.gaussian_filter(first_float, 0.8) - ndimage.gaussian_filter(first_float, 4.0)
    second_edges = ndimage.gaussian_filter(second_float, 0.8) - ndimage.gaussian_filter(second_float, 4.0)
    window = np.outer(np.hanning(size), np.hanning(size)).astype(np.float32)
    first_edges = (first_edges - first_edges.mean()) * window
    second_edges = (second_edges - second_edges.mean()) * window
    cross_power = np.fft.fft2(first_edges) * np.conj(np.fft.fft2(second_edges))
    cross_power /= np.maximum(np.abs(cross_power), 1e-12)
    correlation = np.fft.ifft2(cross_power).real
    peak = np.unravel_index(np.argmax(correlation), correlation.shape)
    shift = np.asarray(peak, dtype=np.float64)
    shift[shift > size / 2] -= size
    return {
        "shift_rows_columns_pixels": [float(shift[0]), float(shift[1])],
        "peak_correlation": round(float(correlation[peak]), 9),
    }


def alignment_report(output_dir: Path, config: Mapping[str, Any]) -> dict[str, Any]:
    near = next(asset for asset in config["assets"] if asset["id"] == "near_4km")
    resolution = int(config["processing"]["alignment_check_resolution_pixels"])
    if resolution <= 0 or resolution & (resolution - 1):
        raise ValueError("Alignment check resolution must be a positive power of two")
    new_path = output_dir / str(near["output_name"])
    baseline_path = REPO_ROOT / str(near["replacement_path"])
    with Image.open(new_path) as source:
        new_image = np.asarray(
            source.convert("L").resize((resolution, resolution), Image.Resampling.BOX), dtype=np.uint8
        )
    with Image.open(baseline_path) as source:
        baseline_image = np.asarray(
            source.convert("L").resize((resolution, resolution), Image.Resampling.BOX), dtype=np.uint8
        )
    report = phase_correlation_shift(new_image, baseline_image)
    bounds = [float(value) for value in near["local_bounds_meters"]]
    meters_per_pixel = (bounds[2] - bounds[0]) / resolution
    row_shift, column_shift = report["shift_rows_columns_pixels"]
    east_shift = column_shift * meters_per_pixel
    north_shift = -row_shift * meters_per_pixel
    magnitude = math.hypot(east_shift, north_shift)
    limit = float(config["processing"]["maximum_baseline_phase_shift_meters"])
    report.update(
        {
            "comparison_resolution_pixels": [resolution, resolution],
            "meters_per_comparison_pixel": meters_per_pixel,
            "east_north_shift_meters": [east_shift, north_shift],
            "shift_magnitude_meters": magnitude,
            "maximum_shift_meters": limit,
            "baseline_path": near["replacement_path"],
            "baseline_sha256": near["replacement_sha256"],
            "method": "windowed phase correlation of multi-scale grayscale edges",
            "passed": magnitude <= limit,
        }
    )
    if not report["passed"]:
        raise ValueError(f"Derived near imagery is misaligned with the frozen ENU baseline: {report}")
    return report


def build_runtime_manifest(
    config: Mapping[str, Any],
    config_path: Path,
    snapshot: Mapping[str, Any],
    snapshot_sha: str,
    asset_records: Sequence[Mapping[str, Any]],
    seams: Mapping[str, Any],
    alignment: Mapping[str, Any],
) -> dict[str, Any]:
    new_bytes = sum(int(record["android_import"]["estimated_full_mip_chain_bytes"]) for record in asset_records)
    replaced_bytes = sum(int(record["replacement"]["estimated_full_mip_chain_bytes"]) for record in asset_records)
    baseline_scene = int(config["runtime"]["baseline_projected_scene_texture_bytes"])
    projected_scene = baseline_scene + new_bytes - replaced_bytes
    ceiling = int(config["runtime"]["scene_texture_ceiling_bytes"])
    if projected_scene > ceiling:
        raise ValueError("Static texture budget exceeds the reviewed scene ceiling")
    source = config["source"]
    tool_path = Path(__file__).resolve()
    return {
        "schema_version": 1,
        "label": config["label"],
        "coordinate_frame": config["coordinate_frame"],
        "origin": config["origin"],
        "source": {
            "id": source["id"],
            "provider": source["provider"],
            "producer": source["producer"],
            "arcgis_item_id": source["arcgis_item_id"],
            "arcgis_item_url": source["arcgis_item_url"],
            "arcgis_item_metadata_url": source["arcgis_item_metadata_url"],
            "service_url": source["service_url"],
            "service_metadata_url": source["service_metadata_url"],
            "tile_url_template": source["tile_url_template"],
            "source_resolution_meters": source["source_resolution_meters"],
            "acquisition_period": source["acquisition_period"],
            "license_name": source["license_name"],
            "license_spdx": source["license_spdx"],
            "license_url": source["license_url"],
            "credit": source["credit"],
            "arcgis_item_response_sha256": snapshot["arcgis_item_response_sha256"],
            "arcgis_item_contract_sha256": snapshot["arcgis_item_contract_sha256"],
            "service_metadata_response_sha256": snapshot["service_metadata_response_sha256"],
            "service_contract_sha256": snapshot["service_contract_sha256"],
            "source_snapshot_sha256": snapshot_sha,
            "source_tile_count": snapshot["tile_count"],
            "source_tile_set_sha256": snapshot["tile_set_sha256"],
        },
        "processing": {
            "projection": config["processing"]["local_projection"],
            "resampling": config["processing"]["resampling"],
            "orientation": "image row 0=north; image column 0=west; Unity UV v increases toward local +Z/north",
            "synthetic_detail": False,
            "ai_upscaling": False,
            "jpeg": {
                "quality": config["processing"]["jpeg_quality"],
                "subsampling": config["processing"]["jpeg_subsampling"],
                "optimize": config["processing"]["jpeg_optimize"],
                "progressive": config["processing"]["jpeg_progressive"],
            },
            "toolchain": {
                "python": platform.python_version(),
                "pillow": pillow_version,
                "numpy": np.__version__,
                "rasterio": rasterio.__version__,
                "gdal": rasterio.__gdal_version__,
                "pyproj": pyproj.__version__,
                "scipy": scipy.__version__,
            },
            "files": [
                {
                    "path": config_path.resolve().relative_to(REPO_ROOT).as_posix(),
                    "bytes": config_path.stat().st_size,
                    "sha256": sha256_file(config_path),
                },
                {
                    "path": tool_path.relative_to(REPO_ROOT).as_posix(),
                    "bytes": tool_path.stat().st_size,
                    "sha256": sha256_file(tool_path),
                },
            ],
        },
        "derived_assets": list(asset_records),
        "seam_checks": seams,
        "enu_alignment_check": alignment,
        "runtime": {
            **config["runtime"],
            "near_meters_per_texel": next(
                float(record["meters_per_texel"][0])
                for record in asset_records
                if record["id"] == "near_4km"
            ),
            "pattern_meters_per_texel": next(
                float(record["meters_per_texel"][0])
                for record in asset_records
                if record["id"] == "mid_base_12km"
            ),
            "far_meters_per_texel": next(
                float(record["meters_per_texel"][0])
                for record in asset_records
                if record["id"] == "far_24km"
            ),
            "runtime_set_android_bytes": new_bytes,
            "baseline_replaced_android_bytes": replaced_bytes,
            "projected_scene_android_texture_bytes": projected_scene,
            "hard_ceiling_bytes": ceiling,
            "shader_bindings": {record["shader_property"]: record["path"] for record in asset_records},
            "fixed_local_uv_vectors": {
                record["shader_property"]: record["world_min_inverse_size"] for record in asset_records
            },
            "budget": {
                "new_full_astc_mip_bytes": new_bytes,
                "replaced_full_astc_mip_bytes": replaced_bytes,
                "net_full_mip_delta_bytes": new_bytes - replaced_bytes,
                "baseline_projected_scene_texture_bytes": baseline_scene,
                "projected_scene_texture_bytes": projected_scene,
                "scene_texture_ceiling_bytes": ceiling,
                "headroom_bytes": ceiling - projected_scene,
            },
        },
        "water_policy": config["water_policy"],
        "scope_guards": config["scope_guards"],
        "raw_policy": (
            "The full source snapshot, ArcGIS response bodies, and every raw cache tile remain beneath the external "
            "artifact/cache root. Only compact derivatives, paired Unity metadata, and this manifest are handoff candidates."
        ),
        "prohibited_sources_confirmation": (
            "No Google, commercial basemap, unlicensed 2024 City imagery, unclear-rights County basemap, "
            "AI-generated, or AI-upscaled pixels are used."
        ),
        "limitations": [
            "Visual context only; not current airport, survey, or navigation data.",
            "City source flights occurred in Spring and Summer 2020; the item does not publish exact frame dates.",
            "The service cache uses lossy source tiles; the runtime JPEG quality does not create source detail.",
            "Full-chain ASTC mip accounting is not a measured controller residency, bandwidth, frame-time, or thermal result.",
            "Integration still requires inspected stereo simulator captures and standard Visual QA.",
        ],
    }


def write_contact_sheet(path: Path, previews: Mapping[str, Image.Image]) -> None:
    order = ("near_4km", "mid_nw", "mid_ne", "far_24km", "mid_sw", "mid_se", "mid_base_12km")
    cell = 512
    header = 44
    sheet = Image.new("RGB", (cell * 4, cell * 2 + header), (23, 26, 30))
    positions = [
        (0, header),
        (cell, header),
        (cell * 2, header),
        (cell * 3, header),
        (0, header + cell),
        (cell, header + cell),
        (cell * 2, header + cell),
    ]
    from PIL import ImageDraw

    draw = ImageDraw.Draw(sheet)
    draw.text((16, 14), "KBDU City of Boulder 2020 CC0 aerial surface V1", fill=(255, 255, 255))
    for identifier, position in zip(order, positions):
        preview = previews[identifier].resize((cell, cell), Image.Resampling.LANCZOS)
        sheet.paste(preview, position)
        draw.rectangle((position[0] + 8, position[1] + 8, position[0] + 210, position[1] + 34), fill=(18, 20, 23))
        draw.text((position[0] + 14, position[1] + 12), identifier, fill=(255, 255, 255))
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path, format="JPEG", quality=92, subsampling=0, optimize=False, progressive=False)


def plan_assets(
    config: Mapping[str, Any], service_contract: Mapping[str, Any]
) -> tuple[dict[str, dict[str, Any]], dict[int, dict[str, Any]]]:
    padding = int(config["processing"]["source_tile_padding"])
    latitude = math.radians(float(config["origin"]["latitude_degrees"]))
    plans: dict[str, dict[str, Any]] = {}
    for asset in config["assets"]:
        bounds = web_mercator_bounds(asset["local_bounds_meters"], config)
        tile_range = tile_range_for_bounds(
            bounds,
            service_contract["tileInfo"],
            int(asset["source_lod"]),
            padding=padding,
        )
        ground_resolution = float(tile_range["resolution_web_mercator_meters"]) * math.cos(latitude)
        width, height = [int(value) for value in asset["dimensions_pixels"]]
        local_bounds = [float(value) for value in asset["local_bounds_meters"]]
        output_resolution = max(
            (local_bounds[2] - local_bounds[0]) / width,
            (local_bounds[3] - local_bounds[1]) / height,
        )
        if output_resolution + 1e-9 < ground_resolution:
            raise ValueError(
                f"{asset['id']} would upscale cache samples ({output_resolution:.6f} < {ground_resolution:.6f} m)"
            )
        plans[str(asset["id"])] = {
            "web_mercator_bounds": bounds,
            "tile_range": tile_range,
            "approximate_ground_resolution_at_origin_meters": ground_resolution,
        }
    union = union_tile_ranges(plan["tile_range"] for plan in plans.values())
    return plans, union


def validate_coverage(
    plans: Mapping[str, Mapping[str, Any]], service_contract: Mapping[str, Any]
) -> None:
    extent = service_contract.get("fullExtent") or {}
    minimum_x, minimum_y = float(extent.get("xmin", math.inf)), float(extent.get("ymin", math.inf))
    maximum_x, maximum_y = float(extent.get("xmax", -math.inf)), float(extent.get("ymax", -math.inf))
    for identifier, plan in plans.items():
        min_x, min_y, max_x, max_y = map(float, plan["web_mercator_bounds"])
        if min_x < minimum_x or min_y < minimum_y or max_x > maximum_x or max_y > maximum_y:
            raise ValueError(f"Reviewed City service does not cover fixed local tile: {identifier}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--raw-cache", type=Path, required=True, help="External raw metadata/tile cache")
    parser.add_argument("--output-dir", type=Path, required=True, help="Compact derivative output/handoff directory")
    parser.add_argument("--snapshot", type=Path, default=None, help="Pinned external source snapshot for offline rebuild")
    parser.add_argument("--snapshot-out", type=Path, default=None, help="External first-acquisition snapshot path")
    parser.add_argument("--offline", action="store_true", help="Forbid network access and require cached source bytes")
    parser.add_argument("--refresh", action="store_true", help="Refetch reviewed metadata and source tiles")
    parser.add_argument("--contact-sheet", type=Path, default=None, help="Optional external visual-inspection sheet")
    args = parser.parse_args()
    if args.offline and args.refresh:
        parser.error("--offline and --refresh are mutually exclusive")

    config_path = args.config.resolve()
    config = load_config(config_path)
    raw_cache = require_external(args.raw_cache)
    output_dir = args.output_dir.expanduser().resolve()
    snapshot_path = args.snapshot.expanduser().resolve() if args.snapshot else None
    if snapshot_path is not None:
        require_external(snapshot_path.parent, create=False)
    snapshot_out = (
        args.snapshot_out.expanduser().resolve()
        if args.snapshot_out
        else raw_cache / SNAPSHOT_NAME
    )
    require_external(snapshot_out.parent, create=True)
    expected_snapshot = load_json(snapshot_path) if snapshot_path else None

    metadata_dir = raw_cache / "metadata"
    item_payload, item = load_or_fetch_response(
        metadata_dir / "arcgis_item_response.json",
        config["source"]["arcgis_item_metadata_url"],
        config,
        offline=args.offline or expected_snapshot is not None,
        refresh=args.refresh,
    )
    service_payload, service = load_or_fetch_response(
        metadata_dir / "service_metadata_response.json",
        config["source"]["service_metadata_url"],
        config,
        offline=args.offline or expected_snapshot is not None,
        refresh=args.refresh,
    )
    validate_source_metadata(item, service, config)
    if expected_snapshot is not None:
        validate_snapshot_metadata(expected_snapshot, config, item_payload, item, service_payload, service)

    service_contract = normalize_service_metadata(service)
    asset_plans, union_ranges = plan_assets(config, service_contract)
    validate_coverage(asset_plans, service_contract)
    tile_records = acquire_tiles(
        raw_cache,
        union_ranges,
        config,
        offline=args.offline or expected_snapshot is not None,
        refresh=args.refresh,
        expected_snapshot=expected_snapshot,
    )
    if expected_snapshot is None:
        snapshot = build_source_snapshot(
            config,
            item_payload,
            item,
            service_payload,
            service,
            union_ranges,
            tile_records,
        )
        atomic_write_json(snapshot_out, snapshot)
    else:
        snapshot = expected_snapshot
        if snapshot["tile_set_sha256"] != tile_set_sha256(tile_records):
            raise ValueError("Pinned source tile-set digest mismatch")
    snapshot_sha = sha256_bytes(canonical_json_bytes(snapshot))

    asset_records, previews = build_assets(
        raw_cache,
        output_dir,
        config,
        service_contract,
        asset_plans,
        union_ranges,
        tile_records,
    )
    seams = seam_report(output_dir, config)
    alignment = alignment_report(output_dir, config)
    manifest = build_runtime_manifest(
        config, config_path, snapshot, snapshot_sha, asset_records, seams, alignment
    )
    manifest_path = output_dir / MANIFEST_NAME
    atomic_write_json(manifest_path, manifest)
    manifest_unity_path = "QuestFlight/Assets/Production/Environment/AerialSurfaceV1/Ground/" + MANIFEST_NAME
    atomic_write_bytes(
        Path(str(manifest_path) + ".meta"),
        json_importer_meta(manifest_unity_path, "QuestFlight AerialSurfaceV1 source/runtime manifest"),
    )
    if args.contact_sheet:
        contact_sheet = args.contact_sheet.expanduser().resolve()
        require_external(contact_sheet.parent, create=True)
        write_contact_sheet(contact_sheet, previews)

    print(manifest_path)
    print(snapshot_path or snapshot_out)
    print(f"source tiles: {len(tile_records)} ({snapshot['tile_set_sha256']})")
    print(f"runtime derivatives: {len(asset_records)}")
    print(json.dumps(manifest["runtime"]["budget"], indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
