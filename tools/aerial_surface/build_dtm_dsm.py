#!/usr/bin/env python3
"""Build the compact nDSM contract from the pinned USGS 3DEP DTM and DSM.

The authoritative point-cloud-to-raster implementation remains
``tools/scenery/lidar/derive_surfaces.py``.  This milestone intentionally reuses
its committed, public-domain 4 m derivatives instead of committing another raw
EPT/LAZ snapshot or duplicating the DTM/DSM textures.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image

from geospatial_frame import horizontal_frame_contract


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
SOURCE_MANIFEST = REPOSITORY_ROOT / (
    "QuestFlight/Assets/Production/Environment/AerialSceneryV1/Lidar/"
    "KBDU_LidarManifestV1.json"
)
RUNTIME_ROOT = REPOSITORY_ROOT / (
    "QuestFlight/Assets/Production/Environment/PhotorealAerialSurfaceV1"
)
DEFAULT_OUTPUT = RUNTIME_ROOT / "Lidar"
NDSM_NAME = "KBDU_AerialSurface_nDSM_R16.png"
MANIFEST_NAME = "KBDU_AerialSurface_DtmDsmManifestV1.json"
SOURCE_COMMIT = "197283fc4f17ae1f78db57c6fcc4e27dc995cd66"
GUID_NAMESPACE = "questflight:photoreal-aerial-surface-v1:"


def canonical_json_bytes(value: Any) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(canonical_json_bytes(value))


def load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"Expected a JSON object in {path}")
    return value


def sha256_bytes(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def logical_path(path: Path) -> str:
    return path.resolve().relative_to(REPOSITORY_ROOT.resolve()).as_posix()


def resolve_logical(path: str) -> Path:
    result = (REPOSITORY_ROOT / path).resolve()
    try:
        result.relative_to(REPOSITORY_ROOT.resolve())
    except ValueError as error:
        raise ValueError(f"Manifest path escapes the repository: {path}") from error
    return result


def file_record(path: Path, semantic: str | None = None) -> dict[str, Any]:
    record: dict[str, Any] = {
        "bytes": path.stat().st_size,
        "path": logical_path(path),
        "sha256": sha256_file(path),
    }
    if semantic is not None:
        record["semantic"] = semantic
    return record


def stable_guid(path: Path) -> str:
    return hashlib.sha256((GUID_NAMESPACE + logical_path(path)).encode("utf-8")).hexdigest()[:32]


def write_folder_meta(path: Path, note: str) -> None:
    path.mkdir(parents=True, exist_ok=True)
    meta = Path(str(path) + ".meta")
    meta.write_text(
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(path)}\n"
        "folderAsset: yes\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        f"  userData: {note}\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n",
        encoding="utf-8",
        newline="\n",
    )


def write_text_meta(path: Path, note: str) -> None:
    Path(str(path) + ".meta").write_text(
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(path)}\n"
        "TextScriptImporter:\n"
        "  externalObjects: {}\n"
        f"  userData: {note}\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n",
        encoding="utf-8",
        newline="\n",
    )


def write_texture_meta(path: Path, note: str, single_channel: bool = True) -> None:
    """Write a deterministic, linear, no-mip data-texture import contract."""

    single = 1 if single_channel else 0
    Path(str(path) + ".meta").write_text(
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(path)}\n"
        "TextureImporter:\n"
        "  internalIDToNameTable: []\n"
        "  externalObjects: {}\n"
        "  serializedVersion: 13\n"
        "  mipmaps:\n"
        "    mipMapMode: 0\n"
        "    enableMipMap: 0\n"
        "    sRGBTexture: 0\n"
        "    linearTexture: 1\n"
        "    fadeOut: 0\n"
        "    borderMipMap: 0\n"
        "    mipMapsPreserveCoverage: 0\n"
        "    alphaTestReferenceValue: 0.5\n"
        "    mipMapFadeDistanceStart: 1\n"
        "    mipMapFadeDistanceEnd: 3\n"
        "  bumpmap:\n"
        "    convertToNormalMap: 0\n"
        "    externalNormalMap: 0\n"
        "    heightScale: 0.25\n"
        "    normalMapFilter: 0\n"
        "    flipGreenChannel: 0\n"
        "  isReadable: 0\n"
        "  streamingMipmaps: 0\n"
        "  streamingMipmapsPriority: 0\n"
        "  vTOnly: 0\n"
        "  ignoreMipmapLimit: 0\n"
        "  grayScaleToAlpha: 0\n"
        "  generateCubemap: 6\n"
        "  cubemapConvolution: 0\n"
        "  seamlessCubemap: 0\n"
        "  textureFormat: 1\n"
        "  maxTextureSize: 1024\n"
        "  textureSettings:\n"
        "    serializedVersion: 2\n"
        "    filterMode: 0\n"
        "    aniso: 0\n"
        "    mipBias: 0\n"
        "    wrapU: 1\n"
        "    wrapV: 1\n"
        "    wrapW: 1\n"
        "  nPOTScale: 0\n"
        "  lightmap: 0\n"
        "  compressionQuality: 100\n"
        "  spriteMode: 0\n"
        "  spriteExtrude: 1\n"
        "  spriteMeshType: 1\n"
        "  alignment: 0\n"
        "  spritePivot: {x: 0.5, y: 0.5}\n"
        "  spritePixelsToUnits: 100\n"
        "  spriteBorder: {x: 0, y: 0, z: 0, w: 0}\n"
        "  spriteGenerateFallbackPhysicsShape: 1\n"
        "  alphaUsage: 1\n"
        "  alphaIsTransparency: 0\n"
        "  spriteTessellationDetail: -1\n"
        "  textureType: 0\n"
        "  textureShape: 1\n"
        f"  singleChannelComponent: {single}\n"
        "  flipbookRows: 1\n"
        "  flipbookColumns: 1\n"
        "  maxTextureSizeSet: 0\n"
        "  compressionQualitySet: 0\n"
        "  textureFormatSet: 0\n"
        "  ignorePngGamma: 1\n"
        "  applyGammaDecoding: 0\n"
        "  swizzle: 50462976\n"
        "  cookieLightType: 0\n"
        "  platformSettings:\n"
        "  - serializedVersion: 4\n"
        "    buildTarget: DefaultTexturePlatform\n"
        "    maxTextureSize: 1024\n"
        "    resizeAlgorithm: 0\n"
        "    textureFormat: -1\n"
        "    textureCompression: 0\n"
        "    compressionQuality: 100\n"
        "    crunchedCompression: 0\n"
        "    allowsAlphaSplitting: 0\n"
        "    overridden: 0\n"
        "    ignorePlatformSupport: 0\n"
        "    androidETC2FallbackOverride: 0\n"
        "    forceMaximumCompressionQuality_BC6H_BC7: 0\n"
        "  - serializedVersion: 4\n"
        "    buildTarget: Android\n"
        "    maxTextureSize: 1024\n"
        "    resizeAlgorithm: 0\n"
        "    textureFormat: -1\n"
        "    textureCompression: 0\n"
        "    compressionQuality: 100\n"
        "    crunchedCompression: 0\n"
        "    allowsAlphaSplitting: 0\n"
        "    overridden: 1\n"
        "    ignorePlatformSupport: 0\n"
        "    androidETC2FallbackOverride: 0\n"
        "    forceMaximumCompressionQuality_BC6H_BC7: 0\n"
        "  spriteSheet:\n"
        "    serializedVersion: 2\n"
        "    sprites: []\n"
        "    outline: []\n"
        "    customData:\n"
        "    physicsShape: []\n"
        "    bones: []\n"
        "    spriteID:\n"
        "    internalID: 0\n"
        "    vertices: []\n"
        "    indices:\n"
        "    edges: []\n"
        "    weights: []\n"
        "    secondaryTextures: []\n"
        "    spriteCustomMetadata:\n"
        "      entries: []\n"
        "    nameFileIdTable: {}\n"
        "  mipmapLimitGroupName:\n"
        "  pSDRemoveMatte: 0\n"
        f"  userData: {note}\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n",
        encoding="utf-8",
        newline="\n",
    )


def asset_by_semantic(manifest: dict[str, Any], semantic: str) -> dict[str, Any]:
    matches = [item for item in manifest.get("assets", []) if item.get("semantic") == semantic]
    if len(matches) != 1:
        raise ValueError(f"Expected exactly one {semantic!r} asset, found {len(matches)}")
    return matches[0]


def verify_source_asset(record: dict[str, Any]) -> Path:
    path = resolve_logical(str(record["path"]))
    if not path.is_file():
        raise FileNotFoundError(path)
    if path.stat().st_size != int(record["bytes"]):
        raise ValueError(f"Source size mismatch: {path}")
    if sha256_file(path) != record["sha256"]:
        raise ValueError(f"Source hash mismatch: {path}")
    return path


def load_r16(path: Path, expected_shape: tuple[int, int]) -> np.ndarray:
    image = np.asarray(Image.open(path))
    if image.dtype != np.uint16 or image.shape != expected_shape:
        raise ValueError(
            f"Expected uint16 image {expected_shape} at {path}, got {image.dtype} {image.shape}"
        )
    return image


def build_ndsm_codes(dtm_codes: np.ndarray, dsm_codes: np.ndarray) -> tuple[np.ndarray, int]:
    if dtm_codes.shape != dsm_codes.shape:
        raise ValueError("DTM and DSM dimensions differ")
    delta = dsm_codes.astype(np.int32) - dtm_codes.astype(np.int32)
    negative_count = int(np.count_nonzero(delta < 0))
    return np.clip(delta, 0, np.iinfo(np.uint16).max).astype(np.uint16), negative_count


def save_png(path: Path, values: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(values).save(path, format="PNG", compress_level=9, optimize=False)


def percentile(values: np.ndarray, q: float) -> float:
    return float(np.percentile(values.astype(np.float64), q))


def build(
    source_manifest_path: Path = SOURCE_MANIFEST,
    output_dir: Path = DEFAULT_OUTPUT,
    write_meta: bool = True,
) -> dict[str, Any]:
    source_manifest_path = source_manifest_path.resolve()
    output_dir = output_dir.resolve()
    source = load_json(source_manifest_path)
    if source.get("schema") != "questflight.aerial-scenery-v1.lidar.v1":
        raise ValueError("Unsupported source lidar manifest schema")

    mapping = source["coordinate_mapping"]
    frame_contract = horizontal_frame_contract(mapping)
    grid = mapping["grid"]
    width = int(grid["width_pixels"])
    height = int(grid["height_pixels"])
    if width != 1024 or height != 1024 or float(grid["pixel_size_meters"]) != 4.0:
        raise ValueError("Photoreal Aerial Surface V1 requires the pinned 1024x1024, 4 m grid")

    dtm_record = asset_by_semantic(source, "digital_terrain_model")
    dsm_record = asset_by_semantic(source, "digital_surface_model")
    dtm_path = verify_source_asset(dtm_record)
    dsm_path = verify_source_asset(dsm_record)
    shape = (height, width)
    dtm_codes = load_r16(dtm_path, shape)
    dsm_codes = load_r16(dsm_path, shape)
    ndsm_codes, negative_count = build_ndsm_codes(dtm_codes, dsm_codes)

    scale = float(dtm_record["encoding"]["decoded_source_z_meters"]["scale"])
    dsm_scale = float(dsm_record["encoding"]["decoded_source_z_meters"]["scale"])
    if not math.isclose(scale, dsm_scale, rel_tol=0.0, abs_tol=1e-12):
        raise ValueError("DTM and DSM height scales differ")

    output_dir.mkdir(parents=True, exist_ok=True)
    ndsm_path = output_dir / NDSM_NAME
    save_png(ndsm_path, ndsm_codes)
    elevated = ndsm_codes > 0
    ndsm_meters = ndsm_codes.astype(np.float64) * scale
    metrics = {
        "all_pixel_count": int(ndsm_codes.size),
        "elevated_pixel_count": int(np.count_nonzero(elevated)),
        "elevated_pixel_fraction": float(np.mean(elevated)),
        "maximum_height_meters": float(np.max(ndsm_meters)),
        "negative_dsm_minus_dtm_pixel_count_clamped": negative_count,
        "p50_height_meters_all_pixels": percentile(ndsm_meters, 50.0),
        "p95_height_meters_all_pixels": percentile(ndsm_meters, 95.0),
        "p99_height_meters_all_pixels": percentile(ndsm_meters, 99.0),
        "quantization_maximum_absolute_error_meters": scale,
    }
    manifest = {
        "assets": [
            {
                **file_record(dtm_path, "digital_terrain_model"),
                "ownership": "referenced_existing_public_domain_derivative",
            },
            {
                **file_record(dsm_path, "digital_surface_model"),
                "ownership": "referenced_existing_public_domain_derivative",
            },
            {
                **file_record(ndsm_path, "normalized_digital_surface_model"),
                "dimensions": {"height_pixels": height, "width_pixels": width},
                "encoding": {
                    "bits_per_channel": 16,
                    "channel": "R",
                    "container": "PNG",
                    "decoded_height_above_ground_meters": {"offset": 0.0, "scale": scale},
                },
                "ownership": "new_compact_public_domain_derivative",
            },
        ],
        "coordinate_mapping": mapping,
        "horizontal_frame_contract": frame_contract,
        "derivation": {
            "formula": "max(DSM_R16_code - DTM_R16_code, 0)",
            "raw_ept_laz_committed": False,
            "source_height_quantization_meters": scale,
        },
        "license_and_provenance": source["license_and_provenance"],
        "limitations": [
            "Not for navigation.",
            "Raster pixel centres are origin-relative UTM13N deltas and require the exact recorded transform before comparison with product AEQD geometry or imagery.",
            "The source is a depth-8 EPT subset and a 4 m product grid, not full-density lidar.",
            "DTM/DSM quantization bounds nDSM error but no surveyed vertical truth study is available.",
        ],
        "metrics": metrics,
        "schema": "questflight.photoreal-aerial-surface-v1.dtm-dsm.v1",
        "source_commit": SOURCE_COMMIT,
        "source_lidar_manifest": file_record(source_manifest_path),
    }
    manifest_path = output_dir / MANIFEST_NAME
    write_json(manifest_path, manifest)
    if write_meta:
        write_folder_meta(output_dir, "USGS 3DEP compact DTM/DSM/nDSM data products")
        write_texture_meta(
            ndsm_path,
            "USGS 3DEP public-domain nDSM; linear R16 data; not for navigation",
        )
        write_text_meta(manifest_path, "USGS 3DEP DTM/DSM/nDSM provenance and metrics")
    return manifest


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-manifest", type=Path, default=SOURCE_MANIFEST)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--no-meta", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    manifest = build(args.source_manifest, args.output, not args.no_meta)
    print(
        json.dumps(
            {
                "manifest": (args.output / MANIFEST_NAME).as_posix(),
                "metrics": manifest["metrics"],
            },
            indent=2,
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
