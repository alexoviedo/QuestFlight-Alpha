#!/usr/bin/env python3
"""Fetch pinned USGS NAIP exports externally and build bounded Quest runtime derivatives."""

from __future__ import annotations

import argparse
import io
import json
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from PIL import Image, ImageEnhance, ImageStat, __version__ as pillow_version

from ground_imagery_common import (
    DEFAULT_CONFIG,
    DEFAULT_OUTPUT,
    REPO_ROOT,
    astc_mip_chain_bytes,
    atomic_write_bytes,
    atomic_write_json,
    load_config,
    local_bounds_to_wgs84,
    require_external,
    sha256_bytes,
    sha256_file,
    unity_guid,
)


USER_AGENT = "T2-QuestFlight-GroundImageryRecovery/2.0 (bounded public-domain USGS fetch)"


def request_bytes(url: str, attempts: int = 4) -> bytes:
    error: Exception | None = None
    for attempt in range(attempts):
        try:
            request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": "*/*"})
            with urllib.request.urlopen(request, timeout=180) as response:
                return response.read()
        except (urllib.error.URLError, TimeoutError) as current:
            error = current
            if attempt + 1 < attempts:
                time.sleep(2**attempt)
    assert error is not None
    raise error


def url(endpoint: str, parameters: list[tuple[str, str]]) -> str:
    return endpoint + "?" + urllib.parse.urlencode(parameters, quote_via=urllib.parse.quote)


def fetch_json(request_url: str, target: Path) -> dict[str, Any]:
    payload = request_bytes(request_url)
    value = json.loads(payload.decode("utf-8"))
    if value.get("error"):
        raise RuntimeError(f"USGS service error: {value['error']}")
    atomic_write_json(target, value)
    return value


def base_grade(image: Image.Image, settings: dict[str, Any]) -> Image.Image:
    result = image.convert("RGB")
    result = ImageEnhance.Brightness(result).enhance(float(settings["brightness"]))
    result = ImageEnhance.Contrast(result).enhance(float(settings["contrast"]))
    result = ImageEnhance.Color(result).enhance(float(settings["saturation"]))
    gains = (float(settings["red_gain"]), float(settings["green_gain"]), float(settings["blue_gain"]))
    return Image.merge(
        "RGB",
        [
            channel.point([min(255, round(value * gain)) for value in range(256)])
            for channel, gain in zip(result.split(), gains)
        ],
    )


def channel_stats(image: Image.Image) -> list[dict[str, float]]:
    sample = image.convert("RGB")
    sample.thumbnail((512, 512), Image.Resampling.BOX)
    statistics = ImageStat.Stat(sample)
    return [
        {"mean": round(statistics.mean[index], 6), "standard_deviation": round(statistics.stddev[index], 6)}
        for index in range(3)
    ]


def match_adjustment(reference: Image.Image, candidate: Image.Image, settings: dict[str, Any]) -> tuple[list[float], list[float]]:
    reference_stats = channel_stats(reference)
    candidate_stats = channel_stats(candidate)
    gains: list[float] = []
    offsets: list[float] = []
    for target, source in zip(reference_stats, candidate_stats):
        gain = target["standard_deviation"] / max(source["standard_deviation"], 1e-6)
        gain = max(float(settings["near_match_gain_min"]), min(float(settings["near_match_gain_max"]), gain))
        offset = target["mean"] - source["mean"] * gain
        offset = max(-float(settings["near_match_offset_limit"]), min(float(settings["near_match_offset_limit"]), offset))
        gains.append(gain)
        offsets.append(offset)
    return gains, offsets


def apply_linear(image: Image.Image, gains: list[float], offsets: list[float]) -> Image.Image:
    return Image.merge(
        "RGB",
        [
            channel.point([max(0, min(255, round(value * gain + offset))) for value in range(256)])
            for channel, gain, offset in zip(image.convert("RGB").split(), gains, offsets)
        ],
    )


def importer_meta(
    guid: str,
    priority: int,
    mip_bias: float,
    user_data: str,
    max_texture_size: int,
    android_texture_format: int,
) -> bytes:
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
  streamingMipmaps: 0
  streamingMipmapsPriority: {priority}
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: {max_texture_size}
  textureSettings:
    serializedVersion: 2
    filterMode: 2
    aniso: 8
    mipBias: {mip_bias}
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 1
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
    maxTextureSize: {max_texture_size}
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 2
    compressionQuality: 80
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 4
    buildTarget: Standalone
    maxTextureSize: {max_texture_size}
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 2
    compressionQuality: 75
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 1
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 4
    buildTarget: Android
    maxTextureSize: {max_texture_size}
    resizeAlgorithm: 0
    textureFormat: {android_texture_format}
    textureCompression: 1
    compressionQuality: 85
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
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData: {user_data}
  assetBundleName:
  assetBundleVariant:
""".encode("utf-8")


def default_importer_meta(guid: str) -> bytes:
    return f"""fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
""".encode("utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--raw-dir", type=Path, required=True)
    parser.add_argument("--work-dir", type=Path, required=True)
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--resume", action="store_true")
    args = parser.parse_args()

    config = load_config(args.config)
    raw_dir = require_external(args.raw_dir)
    work_dir = require_external(args.work_dir)
    output_dir = args.output_dir.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    for obsolete_name in ("KBDU_NAIP_2019_MidBase12km_2048.jpg", "KBDU_NAIP_2019_MidBase12km_2048.jpg.meta"):
        obsolete_path = output_dir / obsolete_name
        if obsolete_path.exists():
            obsolete_path.unlink()
    source = config["source"]
    service = source["service_url"].rstrip("/")
    hot_size = int(config["runtime"]["hot_resolution_pixels"])
    fallback_size = int(config["runtime"]["fallback_resolution_pixels"])
    hot_export_size = int(config["runtime"]["hot_export_resolution_pixels"])

    metadata_url = url(service, [("f", "pjson")])
    metadata_path = raw_dir / "service_metadata.json"
    metadata = json.loads(metadata_path.read_text(encoding="utf-8")) if args.resume and metadata_path.exists() else fetch_json(metadata_url, metadata_path)
    if int(metadata.get("maxImageWidth", 0)) < hot_export_size or "USGS" not in str(metadata.get("copyrightText", "")):
        raise RuntimeError("USGSNAIPPlus metadata no longer satisfies the pinned bounded export contract")

    catalog_half_extent = float(source["catalog_validation_half_extent_meters"])
    fallback_bbox = local_bounds_to_wgs84(
        [-catalog_half_extent, -catalog_half_extent, catalog_half_extent, catalog_half_extent],
        config["origin"],
    )
    catalog_url = url(
        service + "/query",
        [
            ("where", f"Category=1 AND Year={source['catalog_year']} AND agency='USDA'"),
            ("geometry", ",".join(f"{value:.12f}" for value in fallback_bbox)),
            ("geometryType", "esriGeometryEnvelope"),
            ("inSR", "4326"),
            ("spatialRel", "esriSpatialRelIntersects"),
            ("outFields", "OBJECTID,Name,Year,raster_name,download_url,acquisition_date,agency,vendor,resolution_value,resolution_units,band_count"),
            ("returnGeometry", "false"),
            ("orderByFields", "OBJECTID ASC"),
            ("resultRecordCount", "100"),
            ("f", "pjson"),
        ],
    )
    catalog_path = raw_dir / "catalog.json"
    catalog = json.loads(catalog_path.read_text(encoding="utf-8")) if args.resume and catalog_path.exists() else fetch_json(catalog_url, catalog_path)
    features = catalog.get("features", [])
    observed_ids = sorted(int(feature["attributes"]["OBJECTID"]) for feature in features)
    if observed_ids != sorted(source["raster_ids"]):
        raise RuntimeError(f"Pinned KBDU NAIP catalog changed: {observed_ids}")
    observed_dates = sorted(
        {
            datetime.fromtimestamp(int(feature["attributes"]["acquisition_date"]) / 1000, timezone.utc).date().isoformat()
            for feature in features
        }
    )
    if observed_dates != sorted(source["acquisition_dates"]):
        raise RuntimeError(f"Pinned acquisition dates changed: {observed_dates}")

    mosaic_rule = json.dumps(
        {"mosaicMethod": "esriMosaicLockRaster", "lockRasterIds": sorted(source["raster_ids"]), "mosaicOperation": "MT_FIRST"},
        separators=(",", ":"),
        sort_keys=True,
    )
    rendering_rule = json.dumps({"rasterFunction": source["raster_function"]}, separators=(",", ":"), sort_keys=True)
    requests: list[dict[str, Any]] = []
    entries = [*config["recovery_tiles"], config["fallback"]]
    raw_images: dict[str, Image.Image] = {}

    def fetch_export(export_id: str, local_bounds: list[float], width: int, height: int) -> Image.Image:
        bbox = local_bounds_to_wgs84(local_bounds, config["origin"])
        export_url = url(
            service + "/exportImage",
            [
                ("bbox", ",".join(f"{value:.12f}" for value in bbox)),
                ("bboxSR", "4326"),
                ("size", f"{width},{height}"),
                ("imageSR", "4326"),
                ("format", "png"),
                ("pixelType", "U8"),
                ("interpolation", "RSP_BilinearInterpolation"),
                ("mosaicRule", mosaic_rule),
                ("renderingRule", rendering_rule),
                ("adjustAspectRatio", "false"),
                ("f", "pjson"),
            ],
        )
        export_path = raw_dir / f"{export_id}_export.json"
        image_path = raw_dir / f"{export_id}_raw.png"
        if args.resume and export_path.exists() and image_path.exists():
            export = json.loads(export_path.read_text(encoding="utf-8"))
            payload = image_path.read_bytes()
        else:
            export = fetch_json(export_url, export_path)
            payload = request_bytes(export["href"])
            if not payload.startswith(b"\x89PNG\r\n\x1a\n"):
                raise RuntimeError(f"USGS export for {export_id} was not a PNG")
            atomic_write_bytes(image_path, payload)
        if int(export.get("width", -1)) != width or int(export.get("height", -1)) != height:
            raise RuntimeError(f"Unexpected export dimensions for {export_id}: {export}")
        requests.append(
            {
                "id": export_id,
                "request_sha256": sha256_bytes(export_url.encode("utf-8")),
                "raw_sha256": sha256_file(image_path),
                "raw_bytes": image_path.stat().st_size,
                "bbox_wgs84": bbox,
                "export_extent": [export["extent"][key] for key in ("xmin", "ymin", "xmax", "ymax")],
            }
        )
        return Image.open(io.BytesIO(payload)).convert("RGB")

    # One official 4000-square export is the common source for all hot quadrants.
    # Cropping one mosaic removes independent-ImageServer resampling seams.
    hot_mosaic_bounds = [-4032.0, -4032.0, 4032.0, 4032.0]
    hot_mosaic = fetch_export("hot_mosaic_8km", hot_mosaic_bounds, hot_export_size, hot_export_size)
    hot_min_x, hot_min_z, hot_max_x, hot_max_z = hot_mosaic_bounds
    for entry in config["recovery_tiles"]:
        min_x, min_z, max_x, max_z = map(float, entry["local_bounds_meters"])
        left = round((min_x - hot_min_x) / (hot_max_x - hot_min_x) * hot_export_size)
        right = round((max_x - hot_min_x) / (hot_max_x - hot_min_x) * hot_export_size)
        top = round((hot_max_z - max_z) / (hot_max_z - hot_min_z) * hot_export_size)
        bottom = round((hot_max_z - min_z) / (hot_max_z - hot_min_z) * hot_export_size)
        raw_images[entry["id"]] = hot_mosaic.crop((left, top, right, bottom)).resize(
            (hot_size, hot_size), Image.Resampling.LANCZOS
        )

    # The service export ceiling is 4000 pixels, so assemble the 4096 fallback
    # from four exactly bounded 2048 exports, matching the established pipeline.
    fallback_bounds = list(map(float, config["fallback"]["local_bounds_meters"]))
    min_x, min_z, max_x, max_z = fallback_bounds
    center_x = (min_x + max_x) * 0.5
    center_z = (min_z + max_z) * 0.5
    fallback_mosaic = Image.new("RGB", (fallback_size, fallback_size))
    fallback_exports = [
        ("mid_base_r0c0", [min_x, center_z, center_x, max_z], (0, 0)),
        ("mid_base_r0c1", [center_x, center_z, max_x, max_z], (fallback_size // 2, 0)),
        ("mid_base_r1c0", [min_x, min_z, center_x, center_z], (0, fallback_size // 2)),
        ("mid_base_r1c1", [center_x, min_z, max_x, center_z], (fallback_size // 2, fallback_size // 2)),
    ]
    for export_id, bounds, position in fallback_exports:
        fallback_mosaic.paste(fetch_export(export_id, bounds, fallback_size // 2, fallback_size // 2), position)
    raw_images[config["fallback"]["id"]] = fallback_mosaic

    settings = config["color_normalization"]
    graded = {identifier: base_grade(image, settings) for identifier, image in raw_images.items()}
    baseline_near = REPO_ROOT / config["baseline"]["existing_near_texture"]
    if sha256_file(baseline_near) != config["baseline"]["existing_near_texture_sha256"]:
        raise RuntimeError("Existing near orthophoto changed; refusing to derive an unreviewed color match")
    with Image.open(baseline_near) as reference_source:
        reference_near = reference_source.convert("RGB")
    fallback = graded[config["fallback"]["id"]]
    footprint = reference_near.width / 3.0
    left = round((fallback.width - footprint) / 2.0)
    upper = round((fallback.height - footprint) / 2.0)
    central = fallback.crop((left, upper, round(left + footprint), round(upper + footprint))).resize(reference_near.size, Image.Resampling.LANCZOS)
    match_gains, match_offsets = match_adjustment(reference_near, central, settings)

    derived: list[dict[str, Any]] = []
    for entry in entries:
        is_fallback = entry["id"] == config["fallback"]["id"]
        resolution = fallback_size if is_fallback else hot_size
        astc_block = 10 if is_fallback else 6
        android_texture_format = 52 if is_fallback else 50
        runtime_bytes = astc_mip_chain_bytes(resolution, resolution, astc_block, astc_block)
        final_image = apply_linear(graded[entry["id"]], match_gains, match_offsets)
        output_path = output_dir / entry["output_name"]
        final_image.save(
            output_path,
            format="JPEG",
            quality=int(config["runtime"]["jpeg_quality"]),
            subsampling=0,
            optimize=True,
            progressive=False,
            exif=b"",
        )
        relative = output_path.relative_to(REPO_ROOT).as_posix()
        priority = int(config["runtime"]["fallback_streaming_priority"] if is_fallback else config["runtime"]["hot_tile_streaming_priority"])
        mip_bias = float(config["runtime"]["fallback_mip_bias"] if is_fallback else config["runtime"]["hot_tile_mip_bias"])
        atomic_write_bytes(
            Path(str(output_path) + ".meta"),
            importer_meta(
                unity_guid(relative),
                priority,
                mip_bias,
                "USGS/USDA NAIP 2019 public-domain optimized derivative; see KBDU_GroundImageryRecoveryV2_SourceManifest.json",
                resolution,
                android_texture_format,
            ),
        )
        bounds = list(map(float, entry["local_bounds_meters"]))
        width_meters = bounds[2] - bounds[0]
        height_meters = bounds[3] - bounds[1]
        derived.append(
            {
                "id": entry["id"],
                "path": relative,
                "bytes": output_path.stat().st_size,
                "sha256": sha256_file(output_path),
                "dimensions_pixels": [resolution, resolution],
                "local_bounds_meters": bounds,
                "nominal_cell_bounds_meters": entry.get("nominal_cell_bounds_meters"),
                "world_min_inverse_size": [bounds[0], bounds[1], 1.0 / width_meters, 1.0 / height_meters],
                "meters_per_texel": [width_meters / resolution, height_meters / resolution],
                "channel_statistics": channel_stats(final_image),
                "android_import": {
                    "format": f"ASTC_{astc_block}x{astc_block}",
                    "mipmaps": True,
                    "streaming_mipmaps": False,
                    "streaming_priority": priority,
                    "filter": "trilinear",
                    "anisotropy": 8,
                    "mip_bias": mip_bias,
                    "wrap": "clamp",
                    "estimated_full_mip_chain_bytes": runtime_bytes,
                },
            }
        )

    total_new_bytes = sum(
        int(asset["android_import"]["estimated_full_mip_chain_bytes"])
        for asset in derived
    )
    net_increase = total_new_bytes - int(config["baseline"]["active_mid_astc6x6_mip_bytes"])
    projected_scene_bytes = int(config["baseline"]["full_scene_estimated_texture_bytes"]) + net_increase
    if net_increase > int(config["runtime"]["maximum_net_full_mip_increase_bytes"]):
        raise RuntimeError(f"Net full-mip increase {net_increase:,} exceeds the configured limit")
    if projected_scene_bytes > int(config["baseline"]["full_scene_texture_ceiling_bytes"]):
        raise RuntimeError(f"Projected scene texture bytes {projected_scene_bytes:,} exceed the static ceiling")

    retrieved = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    manifest = {
        "schema_version": 1,
        "label": config["label"],
        "source": {
            **source,
            "retrieved_utc": retrieved,
            "service_metadata_sha256": sha256_file(metadata_path),
            "catalog_sha256": sha256_file(catalog_path),
            "catalog_features": [feature["attributes"] for feature in features],
            "requests": requests,
        },
        "processing": {
            "python": sys.version.split()[0],
            "pillow": pillow_version,
            "resampling": "USGS ImageServer NaturalColor / bilinear / adjustAspectRatio=false; no synthetic or AI-generated pixels",
            "base_grade": settings,
            "near_match_gains": [round(value, 8) for value in match_gains],
            "near_match_offsets": [round(value, 8) for value in match_offsets],
            "config_sha256": sha256_file(args.config),
            "tool_files": [
                {
                    "path": path.relative_to(REPO_ROOT).as_posix(),
                    "bytes": path.stat().st_size,
                    "sha256": sha256_file(path),
                }
                for path in (
                    Path(__file__).resolve().parent / "config.json",
                    Path(__file__).resolve().parent / "ground_imagery_common.py",
                    Path(__file__).resolve().parent / "fetch_and_build_ground_imagery.py",
                    Path(__file__).resolve().parent / "validate_ground_imagery.py",
                )
            ],
        },
        "derived_assets": derived,
        "budget": {
            "new_texture_count": len(entries),
            "new_full_astc_mip_bytes": total_new_bytes,
            "replaced_mid_full_astc_mip_bytes": config["baseline"]["active_mid_astc6x6_mip_bytes"],
            "net_full_mip_increase_bytes": net_increase,
            "baseline_scene_estimated_texture_bytes": config["baseline"]["full_scene_estimated_texture_bytes"],
            "projected_scene_estimated_texture_bytes": projected_scene_bytes,
            "scene_texture_ceiling_bytes": config["baseline"]["full_scene_texture_ceiling_bytes"],
            "residency_note": "All macro-map mip chains are explicitly resident because the current world-space custom shader does not expose a proven automatic mip-streaming UV contract.",
        },
        "raw_policy": "Raw PNG exports, service responses, and work products remain external and are not committed.",
        "prohibited_sources_confirmation": "No Google, commercial, unclear-license, AI-generated, or AI-upscaled imagery is used.",
        "limitations": [
            "The current official KBDU catalog contains 2019 0.6 m NAIP; the service query found no newer source at this location.",
            "The recovery tiles improve sampling allocation but remain downsampled derivatives of real source imagery.",
            "This imagery is visual context only and is not current airport or navigation data.",
            "Final mip residency, bandwidth, and visual quality require an integrated physical Quest measurement.",
        ],
    }
    manifest_path = output_dir / "KBDU_GroundImageryRecoveryV2_SourceManifest.json"
    atomic_write_json(manifest_path, manifest)
    manifest_relative = manifest_path.relative_to(REPO_ROOT).as_posix()
    atomic_write_bytes(Path(str(manifest_path) + ".meta"), default_importer_meta(unity_guid(manifest_relative)))

    contract = {
        "schema_version": 1,
        "label": "KBDU culling-compatible ground imagery selection and orthophoto-only water contract",
        "coordinate_frame": "KBDU local tangent ENU; Unity +X=east, +Z=north",
        "selection": {
            "near": {
                "path": config["baseline"]["existing_near_texture"],
                "half_extent_meters": config["baseline"]["existing_near_half_extent_meters"],
                "blend_to_recovery_meters": config["runtime"]["near_to_recovery_blend_meters"],
            },
            "recovery_quadrants": [asset for asset in derived if asset["id"] != config["fallback"]["id"]],
            "quadrant_selection": "Select by signs of world X/Z; cross-blend only within 32 m of X=0 or Z=0 using the 64 m authored overlap.",
            "recovery_to_fallback_blend_meters": config["runtime"]["recovery_to_fallback_blend_meters"],
            "fallback": next(asset for asset in derived if asset["id"] == config["fallback"]["id"]),
            "fallback_to_far_blend_meters": config["runtime"]["fallback_to_far_blend_meters"],
            "far": {"path": config["baseline"]["existing_far_texture"]},
        },
        "shader_integration_spec": {
            "shader": "QuestFlight/KBDU Terrain Pyramid",
            "preserved_properties": {
                "_NearAlbedo": config["baseline"]["existing_near_texture"],
                "_FarAlbedo": config["baseline"]["existing_far_texture"],
            },
            "rebound_property": {
                "_MidAlbedo": next(asset["path"] for asset in derived if asset["id"] == config["fallback"]["id"]),
                "replaces": config["baseline"]["active_mid_texture"],
            },
            "new_texture_properties": {
                "_RecoveryMidNWAlbedo": next(asset["path"] for asset in derived if asset["id"] == "mid_nw"),
                "_RecoveryMidNEAlbedo": next(asset["path"] for asset in derived if asset["id"] == "mid_ne"),
                "_RecoveryMidSWAlbedo": next(asset["path"] for asset in derived if asset["id"] == "mid_sw"),
                "_RecoveryMidSEAlbedo": next(asset["path"] for asset in derived if asset["id"] == "mid_se"),
            },
            "new_vector_properties": {
                "_RecoveryMidNWWorldMinInvSize": [-4032.0, -32.0, 0.00024606299212598425, 0.00024606299212598425],
                "_RecoveryMidNEWorldMinInvSize": [-32.0, -32.0, 0.00024606299212598425, 0.00024606299212598425],
                "_RecoveryMidSWWorldMinInvSize": [-4032.0, -4032.0, 0.00024606299212598425, 0.00024606299212598425],
                "_RecoveryMidSEWorldMinInvSize": [-32.0, -4032.0, 0.00024606299212598425, 0.00024606299212598425],
                "_RecoveryNearBlendStartEnd": config["runtime"]["near_to_recovery_blend_meters"],
                "_RecoveryHotBlendStartEnd": config["runtime"]["recovery_to_fallback_blend_meters"],
                "_RecoveryQuadrantSeamHalfWidthMeters": [32.0, 0.0, 0.0, 0.0],
            },
            "fragment_branch_order": [
                "radius <= 1800: sample _NearAlbedo",
                "1800 < radius < 1980: blend selected recovery quadrant to _NearAlbedo",
                "1980 <= radius <= 3800: sample selected recovery quadrant",
                "3800 < radius < 4000: blend _MidAlbedo fallback to selected recovery quadrant",
                "4000 <= radius <= 5000: sample _MidAlbedo fallback",
                "5000 < radius < 6000: preserve existing _MidAlbedo-to-_FarAlbedo blend",
                "radius >= 6000: sample _FarAlbedo",
            ],
            "quadrant_uv_formula": "uv = (worldXZ - property.xy) * property.zw; use tex2Dgrad with ddx/ddy of that uv",
            "quadrant_selection_formula": "north = worldZ >= 0; east = worldX >= 0; choose NW/NE/SW/SE. Within abs(worldX)<32 or abs(worldZ)<32, cross-blend the two adjacent tiles using smoothstep(-32,32,axis); at radius>1980 both axes cannot simultaneously be in the seam.",
            "deterministic_batch_method": "Read KBDU_GroundImageryRecoveryV2_Contract.json, bind the exact GUID-backed paths above, set vectors verbatim, set every macro TextureImporter.streamingMipmaps=false, save the shared material and explicitly serialized product prefab, then run the imagery validator and product Visual QA.",
        },
        "renderer_contract": {
            "shared_material_binds_all_five_replacement_textures": True,
            "zero_renderer_delta": True,
            "automatic_mip_streaming_is_not_relied_upon": True,
            "manual_requested_mipmap_level_is_optional_future_work_only_after_hardware_proof": True,
            "texture_array_not_required": True,
            "no_runtime_material_clones": True,
            "no_network_or_pc_runtime_dependency": True,
            "no_draw_triangle_or_material_delta_from_imagery_itself": True,
            "future_culling_compatibility": "The four quadrant assets preserve bounded ENU footprints and may later be moved to renderer-bound cells; that path is not required or credited in this milestone.",
        },
        "water": config["water_policy"],
        "budget": manifest["budget"],
        "integration_gate": [
            "Remove the replaced 4096 mid atlas from the product dependency closure before counting the net delta.",
            "Keep automatic mip streaming disabled for every world-space macro map unless a later manual requested-mip controller is separately proven.",
            "Capture physical Quest memory/timing before acceptance.",
            "Use the full mip-chain estimate as resident texture memory; do not claim unmeasured streaming savings.",
        ],
    }
    contract_path = output_dir / "KBDU_GroundImageryRecoveryV2_Contract.json"
    atomic_write_json(contract_path, contract)
    contract_relative = contract_path.relative_to(REPO_ROOT).as_posix()
    atomic_write_bytes(Path(str(contract_path) + ".meta"), default_importer_meta(unity_guid(contract_relative)))
    receipt = {
        "schema_version": 1,
        "retrieved_utc": retrieved,
        "service_metadata": str(metadata_path),
        "catalog": str(catalog_path),
        "requests": requests,
        "raw_policy": "artifact-only",
    }
    atomic_write_json(work_dir / "ground_imagery_fetch_receipt.json", receipt)
    print(manifest_path)
    print(contract_path)
    print(f"New full ASTC mip bytes: {total_new_bytes:,}")
    print(f"Net full-mip increase after replacing old mid atlas: {net_increase:,}")
    print(f"Projected full-scene texture bytes: {projected_scene_bytes:,}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
