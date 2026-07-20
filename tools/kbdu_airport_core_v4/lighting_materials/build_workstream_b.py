#!/usr/bin/env python3
"""Build bounded V4 materials, clean plate, lighting plans, and runway assets.

The tool is deliberately scene-agnostic. It consumes Workstream A's exact
replacement polygons and external, verified Poly Haven inputs, then writes only
optimized Unity authoring assets under AirportCoreV4. It does not run or claim
a Unity light bake.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import pathlib
import random
from typing import Any, Iterable

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageOps


TOOL_ROOT = pathlib.Path(__file__).resolve().parent
REPO_ROOT = TOOL_ROOT.parents[2]
CONFIG_PATH = TOOL_ROOT / "config.json"
GUID_PREFIX = "questflight-airport-core-v4-workstream-b:"
SEED = 20260717


def load_json(path: pathlib.Path) -> dict[str, Any]:
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(data, dict):
        raise ValueError(f"Expected object: {path}")
    return data


def write_json(path: pathlib.Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def record(path: pathlib.Path, repo: pathlib.Path) -> dict[str, Any]:
    try:
        relative = path.relative_to(repo).as_posix()
    except ValueError:
        relative = path.name
    return {"path": relative, "bytes": path.stat().st_size, "sha256": sha256(path)}


def stable_guid(path: pathlib.Path, repo: pathlib.Path) -> str:
    relative = path.relative_to(repo).as_posix()
    return hashlib.sha1((GUID_PREFIX + relative).encode("utf-8")).hexdigest()[:32]


def write_folder_meta(folder: pathlib.Path, repo: pathlib.Path) -> None:
    meta = folder.with_name(folder.name + ".meta")
    if meta.exists():
        return
    meta.write_text(
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(folder, repo)}\n"
        "folderAsset: yes\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: AirportCoreV4 Workstream B\n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n",
        encoding="utf-8",
        newline="\n",
    )


def ensure_unity_folders(asset_root: pathlib.Path, repo: pathlib.Path) -> dict[str, pathlib.Path]:
    folders = {
        "root": asset_root,
        "materials": asset_root / "Materials",
        "clean": asset_root / "CleanPlate",
        "lighting": asset_root / "Lighting",
        "runway": asset_root / "Runway",
    }
    lineage: list[pathlib.Path] = []
    cursor = asset_root
    while cursor != repo and cursor not in lineage:
        lineage.append(cursor)
        cursor = cursor.parent
    unity_assets_root = repo / "QuestFlight/Assets"
    for folder in reversed(lineage):
        folder.mkdir(parents=True, exist_ok=True)
        # Unity does not use an Assets.meta beside the project Assets root.
        # Only nested folders receive folder metadata.
        if folder != unity_assets_root and folder.is_relative_to(unity_assets_root):
            write_folder_meta(folder, repo)
    for folder in folders.values():
        folder.mkdir(parents=True, exist_ok=True)
        write_folder_meta(folder, repo)
    return folders


def write_text_meta(path: pathlib.Path, repo: pathlib.Path, importer: str = "TextScriptImporter") -> None:
    body = (
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(path, repo)}\n"
        f"{importer}:\n"
        "  externalObjects: {}\n"
    )
    if importer == "ShaderImporter":
        body += "  defaultTextures: []\n  nonModifiableTextures: []\n"
    body += "  userData: AirportCoreV4 Workstream B\n  assetBundleName: \n  assetBundleVariant: \n"
    path.with_name(path.name + ".meta").write_text(body, encoding="utf-8", newline="\n")


def write_texture_meta(
    path: pathlib.Path,
    repo: pathlib.Path,
    *,
    srgb: bool,
    normal: bool,
    max_size: int,
    android_max_size: int | None = None,
    streaming: bool = True,
    aniso: int = 4,
) -> None:
    android_size = android_max_size or max_size
    texture_type = 1 if normal else 0
    path.with_name(path.name + ".meta").write_text(
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(path, repo)}\n"
        "TextureImporter:\n"
        "  internalIDToNameTable: []\n"
        "  externalObjects: {}\n"
        "  serializedVersion: 13\n"
        "  mipmaps:\n"
        "    mipMapMode: 0\n"
        "    enableMipMap: 1\n"
        f"    sRGBTexture: {1 if srgb else 0}\n"
        "    linearTexture: 0\n"
        "    fadeOut: 0\n"
        "    borderMipMap: 0\n"
        "    mipMapsPreserveCoverage: 0\n"
        "    alphaTestReferenceValue: 0.5\n"
        "  bumpmap:\n"
        "    convertToNormalMap: 0\n"
        "    externalNormalMap: 0\n"
        "    heightScale: 0.25\n"
        "    normalMapFilter: 0\n"
        "    flipGreenChannel: 0\n"
        "  isReadable: 0\n"
        f"  streamingMipmaps: {1 if streaming else 0}\n"
        "  streamingMipmapsPriority: 1\n"
        "  vTOnly: 0\n"
        "  ignoreMipmapLimit: 0\n"
        "  grayScaleToAlpha: 0\n"
        "  generateCubemap: 6\n"
        "  cubemapConvolution: 0\n"
        "  seamlessCubemap: 0\n"
        "  textureFormat: 1\n"
        f"  maxTextureSize: {max_size}\n"
        "  textureSettings:\n"
        "    serializedVersion: 2\n"
        "    filterMode: 2\n"
        f"    aniso: {aniso}\n"
        "    mipBias: 0\n"
        "    wrapU: 0\n"
        "    wrapV: 0\n"
        "    wrapW: 0\n"
        "  nPOTScale: 1\n"
        "  lightmap: 0\n"
        "  compressionQuality: 75\n"
        "  spriteMode: 0\n"
        "  alphaUsage: 1\n"
        "  alphaIsTransparency: 0\n"
        f"  textureType: {texture_type}\n"
        "  textureShape: 1\n"
        "  platformSettings:\n"
        "  - serializedVersion: 4\n"
        "    buildTarget: DefaultTexturePlatform\n"
        f"    maxTextureSize: {max_size}\n"
        "    resizeAlgorithm: 0\n"
        "    textureFormat: -1\n"
        "    textureCompression: 2\n"
        "    compressionQuality: 75\n"
        "    crunchedCompression: 0\n"
        "    allowsAlphaSplitting: 0\n"
        "    overridden: 0\n"
        "  - serializedVersion: 4\n"
        "    buildTarget: Android\n"
        f"    maxTextureSize: {android_size}\n"
        "    resizeAlgorithm: 0\n"
        "    textureFormat: 50\n"
        "    textureCompression: 1\n"
        "    compressionQuality: 75\n"
        "    crunchedCompression: 0\n"
        "    allowsAlphaSplitting: 0\n"
        "    overridden: 1\n"
        "    androidETC2FallbackOverride: 0\n"
        "  spriteSheet:\n"
        "    serializedVersion: 2\n"
        "    sprites: []\n"
        "    outline: []\n"
        "    physicsShape: []\n"
        "    bones: []\n"
        "    spriteID: \n"
        "    internalID: 0\n"
        "    vertices: []\n"
        "    indices: \n"
        "    edges: []\n"
        "    weights: []\n"
        "  userData: AirportCoreV4 optimized derivative; see adjacent SOURCE_MANIFEST.json\n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n",
        encoding="utf-8",
        newline="\n",
    )


def write_material(
    path: pathlib.Path,
    repo: pathlib.Path,
    shader: pathlib.Path,
    textures: list[tuple[str, pathlib.Path]],
    floats: list[tuple[str, float]],
    colors: list[tuple[str, tuple[float, float, float, float]]],
) -> None:
    texture_yaml = "".join(
        f"    - {name}:\n"
        f"        m_Texture: {{fileID: 2800000, guid: {stable_guid(texture, repo)}, type: 3}}\n"
        "        m_Scale: {x: 1, y: 1}\n"
        "        m_Offset: {x: 0, y: 0}\n"
        for name, texture in textures
    )
    float_yaml = "".join(f"    - {name}: {value:g}\n" for name, value in floats)
    color_yaml = "".join(
        f"    - {name}: {{r: {value[0]:g}, g: {value[1]:g}, b: {value[2]:g}, a: {value[3]:g}}}\n"
        for name, value in colors
    )
    path.write_text(
        "%YAML 1.1\n"
        "%TAG !u! tag:unity3d.com,2011:\n"
        "--- !u!21 &2100000\n"
        "Material:\n"
        "  serializedVersion: 8\n"
        "  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        f"  m_Name: {path.stem}\n"
        f"  m_Shader: {{fileID: 4800000, guid: {stable_guid(shader, repo)}, type: 3}}\n"
        "  m_Parent: {fileID: 0}\n"
        "  m_ModifiedSerializedProperties: 0\n"
        "  m_ValidKeywords: []\n"
        "  m_InvalidKeywords: []\n"
        "  m_LightmapFlags: 4\n"
        "  m_EnableInstancingVariants: 1\n"
        "  m_DoubleSidedGI: 0\n"
        "  m_CustomRenderQueue: -1\n"
        "  stringTagMap: {}\n"
        "  disabledShaderPasses: []\n"
        "  m_LockedProperties: \n"
        "  m_SavedProperties:\n"
        "    serializedVersion: 3\n"
        "    m_TexEnvs:\n"
        + texture_yaml +
        "    m_Ints: []\n"
        "    m_Floats:\n"
        + float_yaml +
        "    m_Colors:\n"
        + color_yaml +
        "  m_BuildTextureStacks: []\n"
        "  m_AllowLocking: 1\n",
        encoding="utf-8",
        newline="\n",
    )
    path.with_name(path.name + ".meta").write_text(
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(path, repo)}\n"
        "NativeFormatImporter:\n"
        "  externalObjects: {}\n"
        "  mainObjectFileID: 2100000\n"
        "  userData: AirportCoreV4 Workstream B\n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n",
        encoding="utf-8",
        newline="\n",
    )


def asset_map(raw_root: pathlib.Path, receipt: dict[str, Any]) -> dict[tuple[str, str], pathlib.Path]:
    values: dict[tuple[str, str], pathlib.Path] = {}
    for entry in receipt.get("files", []):
        path = raw_root / str(entry.get("externalFile", ""))
        if not path.is_file():
            raise FileNotFoundError(path)
        if sha256(path) != entry.get("sha256"):
            raise ValueError(f"Raw source SHA-256 mismatch: {path}")
        values[(str(entry["assetId"]), str(entry["semantic"]))] = path
    return values


def fit_square(image: Image.Image, size: int) -> Image.Image:
    return ImageOps.fit(image.convert("RGB"), (size, size), method=Image.Resampling.LANCZOS, centering=(0.5, 0.5))


def external_tile(source: str, size: int, maps: dict[tuple[str, str], pathlib.Path]) -> tuple[Image.Image, Image.Image, Image.Image]:
    albedo = fit_square(Image.open(maps[(source, "diffuse")]), size)
    normal = fit_square(Image.open(maps[(source, "normalOpenGl")]), size)
    arm = np.asarray(fit_square(Image.open(maps[(source, "arm")]), size), dtype=np.uint8)
    # Poly Haven ARM = AO, roughness, metallic. Product MRAO = metallic, roughness, AO.
    mrao = Image.fromarray(np.stack((arm[..., 2], arm[..., 1], arm[..., 0]), axis=-1))
    return albedo, normal, mrao


def procedural_tile(name: str, size: int, rng: random.Random) -> tuple[Image.Image, Image.Image, Image.Image]:
    palette = {
        "procedural_opaque_glazing": ((42, 62, 70), (72, 42, 255)),
        "procedural_dark_roof": ((43, 46, 48), (48, 142, 245)),
        "procedural_dark_trim": ((34, 38, 40), (84, 118, 252)),
        "procedural_sign_face": ((220, 218, 207), (4, 94, 255)),
        "procedural_roof_equipment": ((105, 110, 109), (145, 135, 240)),
        "procedural_bifold_door": ((181, 182, 174), (118, 148, 244)),
        "procedural_safety_yellow": ((214, 163, 30), (5, 126, 245)),
        "procedural_landscape_rock": ((124, 111, 93), (2, 190, 230)),
        "procedural_shrub": ((67, 85, 45), (2, 216, 225)),
        "procedural_service_prop": ((88, 92, 91), (132, 132, 244)),
        "procedural_painted_metal": ((183, 184, 177), (150, 132, 250)),
    }
    base, mrao_value = palette.get(name, ((128, 128, 128), (0, 180, 255)))
    noise = np.asarray(Image.effect_noise((size, size), 11.0), dtype=np.int16) - 128
    albedo = np.empty((size, size, 3), dtype=np.uint8)
    for channel, value in enumerate(base):
        albedo[..., channel] = np.clip(value + noise * (0.035 if name == "procedural_opaque_glazing" else 0.075), 0, 255)
    if name in {"procedural_dark_roof", "procedural_bifold_door"}:
        draw = ImageDraw.Draw(image := Image.fromarray(albedo))
        spacing = max(12, size // 24)
        for x in range(0, size, spacing):
            draw.line((x, 0, x, size), fill=tuple(max(0, v - 18) for v in base), width=max(1, size // 256))
        albedo = np.asarray(image)
    normal = np.zeros((size, size, 3), dtype=np.uint8)
    normal[..., 0] = 128
    normal[..., 1] = 128
    normal[..., 2] = 255
    mrao = np.empty((size, size, 3), dtype=np.uint8)
    mrao[:] = mrao_value
    return Image.fromarray(albedo), Image.fromarray(normal), Image.fromarray(mrao)


def build_atlas(
    slots: list[str], size: int, maps: dict[tuple[str, str], pathlib.Path], seed: int
) -> tuple[Image.Image, Image.Image, Image.Image, list[dict[str, Any]]]:
    if len(slots) != 16:
        raise ValueError(f"Expected sixteen 4x4 material slots, got {len(slots)}")
    tile_size = size // 4
    albedo = Image.new("RGB", (size, size))
    normal = Image.new("RGB", (size, size), (128, 128, 255))
    mrao = Image.new("RGB", (size, size), (0, 180, 255))
    layout = []
    for index, source in enumerate(slots):
        if source.startswith("procedural_"):
            tile = procedural_tile(source, tile_size, random.Random(seed + index * 101))
            license_name = "Project-owned"
        else:
            tile = external_tile(source, tile_size, maps)
            license_name = "CC0-1.0"
        # Pillow addresses image rows from the top, while Blender and Unity UVs
        # address V from the bottom. Keep tile indices and manifest UV rectangles
        # in the runtime bottom-left convention, and flip only the image row used
        # for the paste operation.
        x = (index % 4) * tile_size
        uv_y = (index // 4) * tile_size
        image_y = size - uv_y - tile_size
        albedo.paste(tile[0], (x, image_y))
        normal.paste(tile[1], (x, image_y))
        mrao.paste(tile[2], (x, image_y))
        inset = 2.0 / size
        layout.append({
            "tileIndex": index,
            "source": source,
            "license": license_name,
            "uvRect": [x / size + inset, uv_y / size + inset, (x + tile_size) / size - inset, (uv_y + tile_size) / size - inset],
            "gutterPixelsRecommendedInMeshUvs": 2,
        })
    return albedo, normal, mrao, layout


def world_to_pixel(point: Iterable[float], size: int, half_extent: float) -> tuple[float, float]:
    x, z = [float(value) for value in point]
    return ((x + half_extent) / (2 * half_extent) * size, (half_extent - z) / (2 * half_extent) * size)


def draw_world_polygon(target: Image.Image, points: list[list[float]], half_extent: float, fill: int = 255) -> None:
    ImageDraw.Draw(target).polygon([world_to_pixel(point, target.width, half_extent) for point in points], fill=fill)


def shift_mask(source: np.ndarray, dx: int, dy: int) -> np.ndarray:
    target = np.zeros_like(source)
    y0, y1 = max(0, -dy), min(source.shape[0], source.shape[0] - dy)
    x0, x1 = max(0, -dx), min(source.shape[1], source.shape[1] - dx)
    if y1 > y0 and x1 > x0:
        target[y0 + dy:y1 + dy, x0 + dx:x1 + dx] = source[y0:y1, x0:x1]
    return target


def role_for(asset_id: str) -> str:
    lower = asset_id.lower()
    if "terminal" in lower:
        return "executive_terminal"
    if "hangar" in lower:
        return "type_2_t_hangar_row"
    return lower


def lighting_reference_footprints(
    repo: pathlib.Path, config: dict[str, Any], fallback: list[dict[str, Any]]
) -> list[dict[str, Any]]:
    references = config.get("lighting", {}).get("referenceFootprints", [])
    if not references:
        return fallback
    context_path = repo / config["cleanPlate"]["sourceContext"]
    context = load_json(context_path)
    quantization = float(context["coordinate_quantization_meters"])
    features: dict[int, dict[str, Any]] = {}
    for feature in context.get("openstreetmap", {}).get("features", []):
        if feature.get("category") != "building" or feature.get("geometry_type") != "polygon":
            continue
        source = feature.get("source", {})
        if isinstance(source.get("osm_id"), int):
            features[int(source["osm_id"])] = feature
    result = []
    for reference in references:
        osm_id = int(reference["osmId"])
        feature = features.get(osm_id)
        if not feature:
            raise ValueError(f"Lighting reference OSM footprint missing: {osm_id}")
        values = feature.get("points_q", [])
        if len(values) < 8 or len(values) % 2:
            raise ValueError(f"Invalid lighting reference points: {osm_id}")
        result.append({
            "object_id": reference["objectId"],
            "asset_id": reference["assetId"],
            "source_footprint_xz_meters": [
                [float(values[index]) * quantization, float(values[index + 1]) * quantization]
                for index in range(0, len(values), 2)
            ],
        })
    return result


def score_shadow_candidate(
    gray: np.ndarray,
    source_mask: np.ndarray,
    azimuth: float,
    elevation: float,
    height: float,
    meters_per_pixel: float,
) -> float:
    shadow_meters = height / max(math.tan(math.radians(elevation)), 0.05)
    distance = max(1, int(round(shadow_meters / meters_per_pixel)))
    radians = math.radians(azimuth)
    dx = int(round(math.sin(radians) * distance))
    dy = int(round(-math.cos(radians) * distance))
    forward = shift_mask(source_mask, dx, dy) & ~source_mask
    reverse = shift_mask(source_mask, -dx, -dy) & ~source_mask
    if int(forward.sum()) < 8 or int(reverse.sum()) < 8:
        return -1e6
    return float(np.median(gray[reverse]) - np.median(gray[forward]))


def infer_sun(
    near: Image.Image, replacements: list[dict[str, Any]], config: dict[str, Any]
) -> dict[str, Any]:
    image = np.asarray(near.convert("RGB"), dtype=np.float32)
    gray_full = image[..., 0] * 0.2126 + image[..., 1] * 0.7152 + image[..., 2] * 0.0722
    half = float(config["cleanPlate"]["nearHalfExtentMeters"])
    meters_per_pixel = 2 * half / near.width
    lighting = config["lighting"]
    azimuths = np.arange(0.0, 360.0, float(lighting["azimuthStepDegrees"]))
    elevations = np.arange(
        float(lighting["elevationRangeDegrees"][0]),
        float(lighting["elevationRangeDegrees"][1]) + 0.01,
        float(lighting["elevationStepDegrees"]),
    )
    samples = []
    score_fields = []
    for replacement in replacements:
        role = role_for(str(replacement["asset_id"]))
        height = float(lighting["knownBuildingHeightsMeters"].get(role, 6.0))
        points = replacement["source_footprint_xz_meters"]
        pixels = [world_to_pixel(point, near.width, half) for point in points]
        xs, ys = [p[0] for p in pixels], [p[1] for p in pixels]
        margin = int(math.ceil(height / math.tan(math.radians(float(elevations.min()))) / meters_per_pixel)) + 12
        left, top = max(0, int(min(xs)) - margin), max(0, int(min(ys)) - margin)
        right, bottom = min(near.width, int(max(xs)) + margin + 1), min(near.height, int(max(ys)) + margin + 1)
        local_mask_image = Image.new("L", (right - left, bottom - top), 0)
        ImageDraw.Draw(local_mask_image).polygon([(x - left, y - top) for x, y in pixels], fill=255)
        local_mask = np.asarray(local_mask_image) > 0
        local_gray = gray_full[top:bottom, left:right]
        scores = np.empty((len(elevations), len(azimuths)), dtype=np.float64)
        for e_index, elevation in enumerate(elevations):
            for a_index, azimuth in enumerate(azimuths):
                scores[e_index, a_index] = score_shadow_candidate(
                    local_gray, local_mask, float(azimuth), float(elevation), height, meters_per_pixel
                )
        valid = scores > -1e5
        center = float(np.median(scores[valid]))
        spread = max(float(np.std(scores[valid])), 1e-6)
        normalized = (scores - center) / spread
        score_fields.append(normalized)
        best_index = np.unravel_index(int(np.argmax(scores)), scores.shape)
        samples.append({
            "objectId": replacement["object_id"],
            "assetId": replacement["asset_id"],
            "assumedHeightMeters": height,
            "bestShadowAzimuthDegrees": float(azimuths[best_index[1]]),
            "bestSunElevationDegrees": float(elevations[best_index[0]]),
            "bestScoreLuma": float(scores[best_index]),
        })
    aggregate = np.mean(np.stack(score_fields, axis=0), axis=0)
    best_index = np.unravel_index(int(np.argmax(aggregate)), aggregate.shape)
    shadow_azimuth = float(azimuths[best_index[1]])
    elevation = float(elevations[best_index[0]])
    best_score = float(aggregate[best_index])
    prominence = (best_score - float(np.median(aggregate))) / max(float(np.std(aggregate)), 1e-6)
    sample_differences = [abs((sample["bestShadowAzimuthDegrees"] - shadow_azimuth + 180) % 360 - 180) for sample in samples]
    consistency = max(0.0, 1.0 - float(np.mean(sample_differences)) / 90.0)
    confidence = min(0.65, max(0.1, 0.18 + 0.10 * prominence + 0.22 * consistency))
    return {
        "method": "robust joint grid search over explicit replacement footprints using exterior median-luminance contrast",
        "sampleCount": len(samples),
        "samples": samples,
        "shadowAzimuthDegrees": shadow_azimuth,
        "sunAzimuthDegrees": (shadow_azimuth + 180.0) % 360.0,
        "sunElevationDegrees": elevation,
        "aggregateScoreProminenceStandardDeviations": prominence,
        "sampleAzimuthConsistency": consistency,
        "confidence": confidence,
        "confidenceClass": "MEDIUM" if confidence >= 0.5 else "LOW",
        "confidenceCapReason": f"Only {len(samples)} reference-grade OSM footprints and mixed-date NAIP imagery without capture time are available.",
        "metadataTimeOfDayAvailable": False,
        "metadataAcquisitionDates": config["lighting"]["acquisitionDates"],
        "coordinateConvention": "Azimuth clockwise from geographic north in local ENU; shadow points away from sun.",
    }


def bounded_mask(mask: np.ndarray, bounds: list[float], size: int, half_extent: float) -> np.ndarray:
    west, south, east, north = [float(value) for value in bounds]
    left, top = world_to_pixel((west, north), size, half_extent)
    right, bottom = world_to_pixel((east, south), size, half_extent)
    clip = np.zeros_like(mask)
    clip[max(0, int(top)):min(size, int(math.ceil(bottom))), max(0, int(left)):min(size, int(math.ceil(right)))] = True
    return mask & clip


def patch_fill_component(
    source: np.ndarray, component_mask: np.ndarray, forbidden_mask: np.ndarray
) -> tuple[np.ndarray, dict[str, Any]]:
    """Fill one bounded component from the least-structured adjacent source patch.

    This keeps real pavement/ground texture and avoids the rectangular blur used
    by V3. The selected patch is channel-matched only within a conservative
    range; photographed structures are penalized by gradient energy.
    """
    ys, xs = np.where(component_mask)
    margin = 3
    left, right = max(0, int(xs.min()) - margin), min(source.shape[1], int(xs.max()) + margin + 1)
    top, bottom = max(0, int(ys.min()) - margin), min(source.shape[0], int(ys.max()) + margin + 1)
    width, height = right - left, bottom - top
    target_mask = component_mask[top:bottom, left:right]
    ring_left, ring_right = max(0, left - 8), min(source.shape[1], right + 8)
    ring_top, ring_bottom = max(0, top - 8), min(source.shape[0], bottom + 8)
    ring_source = source[ring_top:ring_bottom, ring_left:ring_right]
    ring_forbidden = forbidden_mask[ring_top:ring_bottom, ring_left:ring_right]
    ring_pixels = ring_source[~ring_forbidden]
    target_median = np.median(ring_pixels, axis=0) if len(ring_pixels) else np.array((105.0, 103.0, 95.0))
    offsets = [
        (width + 10, 0), (-(width + 10), 0), (0, height + 10), (0, -(height + 10)),
        (width + 10, height + 10), (-(width + 10), height + 10),
        (width + 10, -(height + 10)), (-(width + 10), -(height + 10)),
        (2 * width + 14, 0), (0, 2 * height + 14),
    ]
    candidates: list[tuple[float, int, int, np.ndarray, list[float]]] = []
    for dx, dy in offsets:
        source_left, source_top = left + dx, top + dy
        source_right, source_bottom = source_left + width, source_top + height
        if source_left < 0 or source_top < 0 or source_right > source.shape[1] or source_bottom > source.shape[0]:
            continue
        if forbidden_mask[source_top:source_bottom, source_left:source_right].mean() > 0.01:
            continue
        patch = source[source_top:source_bottom, source_left:source_right]
        patch_luma = patch[..., 0] * 0.2126 + patch[..., 1] * 0.7152 + patch[..., 2] * 0.0722
        gradient = float(np.mean(np.abs(np.diff(patch_luma, axis=0)))) + float(np.mean(np.abs(np.diff(patch_luma, axis=1))))
        median = np.median(patch.reshape(-1, 3), axis=0)
        color_delta = float(np.mean(np.abs(median - target_median)))
        luma_median = float(np.median(patch_luma))
        exposure_penalty = max(0.0, 48.0 - luma_median) + max(0.0, luma_median - 185.0)
        score = gradient + 0.22 * color_delta + 2.0 * exposure_penalty
        candidates.append((score, dx, dy, patch, median.tolist()))
    if not candidates:
        raise ValueError("No clean adjacent source patch is available for bounded clean-plate synthesis")
    score, dx, dy, selected, selected_median = min(candidates, key=lambda item: item[0])
    correction = np.clip(target_median - np.asarray(selected_median), -14.0, 14.0)
    # Synthesize non-directional pavement/ground texture from the selected real
    # patch's robust color and variance. This removes copied aircraft, markings,
    # and roof seams without introducing a Gaussian blur rectangle.
    selected_float = selected.astype(np.float32)
    selected_luma = selected_float[..., 0] * 0.2126 + selected_float[..., 1] * 0.7152 + selected_float[..., 2] * 0.0722
    luma_median = float(np.median(selected_luma))
    robust_pixels = selected_float[np.abs(selected_luma - luma_median) <= 28.0]
    robust_std = np.clip(np.std(robust_pixels, axis=0) if len(robust_pixels) else np.array((8.0, 8.0, 8.0)), 3.0, 14.0)
    final_median = np.asarray(selected_median) + correction
    seed = (left * 73856093) ^ (top * 19349663) ^ (width * 83492791) ^ height
    generator = np.random.default_rng(seed & 0xFFFFFFFF)
    coarse_small = generator.normal(0.0, 1.0, size=(max(3, height // 12), max(3, width // 12))).astype(np.float32)
    coarse = np.asarray(
        Image.fromarray(coarse_small).resize((width, height), Image.Resampling.BICUBIC),
        dtype=np.float32,
    )
    coarse = (coarse - float(coarse.mean())) / max(float(coarse.std()), 1e-6)
    fine = generator.normal(0.0, 1.0, size=(height, width)).astype(np.float32)
    texture = final_median[None, None, :] + coarse[..., None] * robust_std[None, None, :] * 0.62 + fine[..., None] * robust_std[None, None, :] * 0.22
    corrected = np.clip(texture, 0, 255).astype(np.uint8)
    result = source.copy()
    destination = result[top:bottom, left:right]
    destination[target_mask] = corrected[target_mask]
    result[top:bottom, left:right] = destination
    return result, {
        "destinationPixelBounds": [left, top, right, bottom],
        "selectedSourceOffsetPixels": [dx, dy],
        "selectionScore": score,
        "selectedMedianRgb": selected_median,
        "targetRingMedianRgb": target_median.tolist(),
        "channelCorrectionRgb": correction.tolist(),
    }


def build_clean_plate(
    near: Image.Image,
    replacements: list[dict[str, Any]],
    replacement_manifest: dict[str, Any],
    inference: dict[str, Any],
    config: dict[str, Any],
    output_dir: pathlib.Path,
) -> tuple[list[pathlib.Path], dict[str, Any]]:
    clean_config = config["cleanPlate"]
    size = near.width
    mask_size = int(clean_config["maskSizePixels"])
    half = float(clean_config["nearHalfExtentMeters"])
    object_image = Image.new("L", (mask_size, mask_size), 0)
    footprint_image = Image.new("L", (mask_size, mask_size), 0)
    for replacement in replacements:
        # The padded polygon is a maximum edit envelope. Actual roof removal is
        # the exact footprint so the replacement model hides the entire edit
        # without consuming the adjacent conditional aircraft-cleanup region.
        draw_world_polygon(object_image, replacement["source_footprint_xz_meters"], half)
        draw_world_polygon(footprint_image, replacement["source_footprint_xz_meters"], half)
    object_mask = np.asarray(object_image) > 0
    footprint_mask = np.asarray(footprint_image) > 0
    shadow_mask = np.zeros_like(footprint_mask)
    shadow_azimuth = float(inference["shadowAzimuthDegrees"])
    elevation = float(inference["sunElevationDegrees"])
    meters_per_mask_pixel = 2 * half / mask_size
    component_masks: list[tuple[dict[str, Any], np.ndarray]] = []
    for replacement in replacements:
        local = Image.new("L", (mask_size, mask_size), 0)
        draw_world_polygon(local, replacement["source_footprint_xz_meters"], half)
        source_mask = np.asarray(local) > 0
        object_local = Image.new("L", (mask_size, mask_size), 0)
        draw_world_polygon(object_local, replacement["source_footprint_xz_meters"], half)
        component = np.asarray(object_local) > 0
        role = role_for(str(replacement["asset_id"]))
        height = float(config["lighting"]["knownBuildingHeightsMeters"].get(role, 6.0))
        distance = max(1, int(round(height / max(math.tan(math.radians(elevation)), 0.05) / meters_per_mask_pixel)))
        radians = math.radians(shadow_azimuth)
        for step in range(1, distance + 1):
            dx = int(round(math.sin(radians) * step))
            dy = int(round(-math.cos(radians) * step))
            shifted = shift_mask(source_mask, dx, dy)
            shadow_mask |= shifted
            component |= shifted
        component_masks.append((replacement, component))
    bounds = replacement_manifest["benchmark_bounds_xz_meters"]
    object_mask = bounded_mask(object_mask, bounds, mask_size, half)
    shadow_mask = bounded_mask(shadow_mask & ~footprint_mask, bounds, mask_size, half)
    union = object_mask | shadow_mask
    if not union.any():
        raise ValueError("Clean-plate union is empty")
    full_mask = np.asarray(Image.fromarray(union.astype(np.uint8) * 255).resize((size, size), Image.Resampling.NEAREST)) > 0
    source = np.asarray(near.convert("RGB"), dtype=np.uint8)
    synthesized = source.copy()
    synthesis_records = []
    for replacement, component in component_masks:
        component = bounded_mask(component, bounds, mask_size, half)
        component_full = np.asarray(Image.fromarray(component.astype(np.uint8) * 255).resize((size, size), Image.Resampling.NEAREST)) > 0
        synthesized, synthesis = patch_fill_component(synthesized, component_full, full_mask)
        synthesis["objectId"] = replacement["object_id"]
        synthesis_records.append(synthesis)
    feather_radius_pixels = float(clean_config["featherMeters"]) / (2 * half / size)
    alpha = Image.fromarray(full_mask.astype(np.uint8) * 255).filter(ImageFilter.GaussianBlur(feather_radius_pixels))
    clean = Image.composite(Image.fromarray(synthesized), near, alpha)

    clean_path = output_dir / "AirportCoreCleanPlate_Albedo.jpg"
    object_path = output_dir / "AirportCoreReplacementMask.png"
    shadow_path = output_dir / "AirportCoreOriginalShadowMask.png"
    clean.save(clean_path, quality=94, subsampling=0, optimize=True)
    Image.fromarray(object_mask.astype(np.uint8) * 255).save(object_path, optimize=True)
    Image.fromarray(shadow_mask.astype(np.uint8) * 255).save(shadow_path, optimize=True)
    optional_regions = [
        item for item in replacement_manifest.get("parked_aircraft_and_prop_regions", [])
        if str(item.get("status", "")).startswith("CANDIDATE")
    ]
    manifest = {
        "schemaVersion": 1,
        "status": "BOUNDED_DERIVATIVE_READY_NOT_VISUALLY_ACCEPTED",
        "replacementCount": len(replacements),
        "replacementObjectIds": [item["object_id"] for item in replacements],
        "maximumEditEnvelopesXzMeters": {item["object_id"]: item["clean_plate_polygon_xz_meters"] for item in replacements},
        "benchmarkBoundsXzMeters": bounds,
        "replacementMaskPixelCount": int(object_mask.sum()),
        "shadowMaskPixelCount": int(shadow_mask.sum()),
        "maskSizePixels": [mask_size, mask_size],
        "cleanPlateSizePixels": [size, size],
        "method": "Exact authored replacement polygons plus calibrated per-height shadow extrusion; least-structured adjacent real-ground patches with bounded channel matching and feathered edges.",
        "synthesisPatches": synthesis_records,
        "conditionalRegionsNotRemoved": [item.get("region_id") for item in optional_regions],
        "conditionalPolicy": "Photographed aircraft/prop regions remain untouched until the lead serializes a matching authored 3D stand.",
        "limitations": [
            "OSM and authored replacement polygons are reference-grade rather than survey-grade.",
            "The clean plate requires full-resolution A/B inspection after model placement.",
            "JPEG encoding can alter values outside the semantic edit mask; masks remain the authoritative edit boundary.",
        ],
    }
    return [clean_path, object_path, shadow_path], manifest


def build_runway(
    maps: dict[tuple[str, str], pathlib.Path],
    near: Image.Image,
    config: dict[str, Any],
    output_dir: pathlib.Path,
) -> tuple[list[pathlib.Path], dict[str, Any]]:
    size = 2048
    albedo, normal, mrao = external_tile("asphalt_02", size, maps)
    source = np.asarray(albedo, dtype=np.float32)
    half = float(config["cleanPlate"]["nearHalfExtentMeters"])
    west, south, east, north = [float(value) for value in config["materials"]["runwayReferenceSampleEnuMeters"]]
    left, top = world_to_pixel((west, north), near.width, half)
    right, bottom = world_to_pixel((east, south), near.width, half)
    ortho_sample = np.asarray(near, dtype=np.float32)[int(top):int(bottom), int(left):int(right)]
    luma = ortho_sample[..., 0] * 0.2126 + ortho_sample[..., 1] * 0.7152 + ortho_sample[..., 2] * 0.0722
    valid = (luma >= 35) & (luma <= 190)
    target_luma = float(np.median(luma[valid])) if valid.any() else 92.0
    source_luma = source[..., 0] * 0.2126 + source[..., 1] * 0.7152 + source[..., 2] * 0.0722
    scale = min(1.35, max(0.65, target_luma / max(float(np.median(source_luma)), 1.0)))
    source = np.clip(source * scale, 0, 255).astype(np.uint8)

    rng = random.Random(SEED + 404)
    feature = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    rubber = Image.new("L", (size, size), 0)
    rubber_draw = ImageDraw.Draw(rubber)
    for center in (int(size * 0.44), int(size * 0.56)):
        for _ in range(34):
            y = rng.randrange(-100, size)
            length = rng.randrange(90, 360)
            rubber_draw.line((center + rng.randrange(-18, 19), y, center + rng.randrange(-18, 19), y + length), fill=rng.randrange(45, 115), width=rng.randrange(2, 9))
    rubber = rubber.filter(ImageFilter.GaussianBlur(4.0))
    patches = Image.new("L", (size, size), 0)
    patch_draw = ImageDraw.Draw(patches)
    for _ in range(11):
        x, y = rng.randrange(size), rng.randrange(size)
        width, height = rng.randrange(70, 250), rng.randrange(35, 130)
        patch_draw.rounded_rectangle((x, y, min(size, x + width), min(size, y + height)), radius=rng.randrange(3, 13), fill=rng.randrange(30, 100))
    cracks = Image.new("L", (size, size), 0)
    crack_draw = ImageDraw.Draw(cracks)
    for _ in range(48):
        x, y = rng.randrange(size), rng.randrange(size)
        points = [(x, y)]
        for _ in range(rng.randrange(2, 7)):
            x = max(0, min(size - 1, x + rng.randrange(-44, 45)))
            y = max(0, min(size - 1, y + rng.randrange(15, 85)))
            points.append((x, y))
        crack_draw.line(points, fill=rng.randrange(75, 170), width=rng.choice((1, 1, 2)))
    edge = np.zeros((size, size), dtype=np.uint8)
    x_values = np.linspace(-1.0, 1.0, size)
    edge[:] = np.clip((np.abs(x_values)[None, :] - 0.72) / 0.28 * 170, 0, 170).astype(np.uint8)
    feature = Image.merge("RGBA", (rubber, patches, cracks, Image.fromarray(edge)))
    features = np.asarray(feature, dtype=np.float32) / 255.0
    source_float = source.astype(np.float32)
    source_float *= (1.0 - features[..., 0:1] * 0.20)
    source_float *= (1.0 - features[..., 1:2] * 0.08)
    source_float *= (1.0 - features[..., 2:3] * 0.26)
    source_float *= (1.0 + features[..., 3:4] * 0.05)
    runway_albedo = Image.fromarray(np.clip(source_float, 0, 255).astype(np.uint8))
    mrao_array = np.asarray(mrao, dtype=np.uint8).copy()
    roughness = mrao_array[..., 1].astype(np.float32)
    roughness -= features[..., 0] * 26.0
    roughness += features[..., 2] * 18.0 + features[..., 3] * 12.0
    mrao_array[..., 1] = np.clip(roughness, 0, 255).astype(np.uint8)
    runway_mrao = Image.fromarray(mrao_array)

    paths = [
        output_dir / "AirportCoreRunway_Albedo.png",
        output_dir / "AirportCoreRunway_Normal.png",
        output_dir / "AirportCoreRunway_MRAO.png",
        output_dir / "AirportCoreRunway_FeatureMask.png",
    ]
    runway_albedo.save(paths[0], optimize=True)
    normal.save(paths[1], optimize=True)
    runway_mrao.save(paths[2], optimize=True)
    feature.save(paths[3], optimize=True)
    return paths, {
        "schemaVersion": 1,
        "status": "SERIALIZED_MATERIAL_CANDIDATE_NOT_VISUALLY_ACCEPTED",
        "source": "Poly Haven asphalt_02 2K CC0 plus project-authored deterministic feature masks",
        "orthophotoReferenceSampleEnuMeters": [west, south, east, north],
        "orthophotoMedianLuma": target_luma,
        "sourceLumaScale": scale,
        "featureChannels": {"r": "rubber deposition", "g": "repairs", "b": "cracks/joints", "a": "edge dirt"},
        "contract": {
            "continuousBase": True,
            "alternatingMacroBlocks": False,
            "geometryChanges": False,
            "colliderChanges": False,
            "markingAlignmentChanges": False,
            "opaque": True,
            "instancing": True,
        },
    }


def estimate_astc_6x6_mipped_bytes(size: int) -> int:
    total = 0
    dimension = size
    while dimension >= 1:
        blocks = math.ceil(dimension / 6) ** 2
        total += blocks * 16
        dimension //= 2
    return total


def write_profiles(
    folders: dict[str, pathlib.Path],
    inference: dict[str, Any],
    config: dict[str, Any],
    repo: pathlib.Path,
) -> list[pathlib.Path]:
    sky_manifest_path = repo / "QuestFlight/Assets/Production/Environment/Sky/SOURCE_MANIFEST.json"
    sky = load_json(sky_manifest_path)
    hdr_uv = sky["runtime_derivative"]["maximum_luminance_uv"]
    hdr_source_azimuth = float(hdr_uv[0]) * 360.0
    sky_rotation = (float(inference["sunAzimuthDegrees"]) - hdr_source_azimuth + 180) % 360 - 180
    azimuth = math.radians(float(inference["sunAzimuthDegrees"]))
    elevation = math.radians(float(inference["sunElevationDegrees"]))
    direction = [math.sin(azimuth) * math.cos(elevation), math.sin(elevation), math.cos(azimuth) * math.cos(elevation)]
    lighting = config["lighting"]
    profile = {
        "schemaVersion": 1,
        "assetType": "AirportCoreLightingProfile",
        "profileId": "KBDU_AIRPORT_CORE_FIXED_NAIP_MATCH_V4",
        "status": "AUTHORING_PROFILE_READY_BAKE_NOT_RUN",
        "fixedTimeOfDay": True,
        "confidence": inference["confidence"],
        "sun": {
            "azimuthDegrees": inference["sunAzimuthDegrees"],
            "elevationDegrees": inference["sunElevationDegrees"],
            "directionTowardSunEnu": direction,
            "colorTemperatureKelvin": 5600,
            "intensityStartingValue": 1.0,
            "staticCoreShadowMode": "future accepted-bake intent: baked direct and indirect only; this generated base profile is not accepted lighting",
            "dynamicAircraftShadowMode": "lead A/B: existing blob/contact shadow or one bounded aircraft-only realtime shadow",
        },
        "sky": {
            "cubemap": "../../Sky/Kloppenheim03_PureSky_2K.hdr",
            "sourceLicense": "CC0-1.0",
            "rotationDegrees": sky_rotation,
            "exposureStartingValue": 0.74,
            "runtimeUpdates": False,
        },
        "ambient": {"source": "custom sky cubemap", "intensityStartingValue": 1.0, "runtimeGiUpdates": False},
        "acceptanceBoundary": "Unity bake, full-resolution A/B, stereo simulator, and physical Quest validation have not run.",
    }
    lightmaps = {
        "schemaVersion": 1,
        "status": "AUTHORING_PLAN_READY_BAKE_NOT_RUN",
        "scope": "AirportCoreBenchmarkRoot only; no Stage-1 lightmap outside benchmark bounds.",
        "maximumAtlases": int(lighting["maximumLightmapAtlases"]),
        "atlasSizePixels": int(lighting["lightmapSizePixels"]),
        "directionalMode": True,
        "compressed": True,
        "runtimeGiUpdates": False,
        "budgetedAtlases": 1,
        "estimatedPreferredAndroidBytes": estimate_astc_6x6_mipped_bytes(int(lighting["lightmapSizePixels"])),
        "estimatedMaximumAndroidBytes": 2 * estimate_astc_6x6_mipped_bytes(int(lighting["lightmapSizePixels"])),
        "secondAtlasPolicy": "Conditional only after the full-scene <=180 MiB gate demonstrates headroom.",
        "validation": ["no doubled shadows", "no static realtime shadow map", "no out-of-bounds renderer in bake"],
    }
    probe_positions = [
        [-385.0 + x, 1.5 + y, -224.0 + z]
        for x, z in ((-28, -34), (0, -34), (28, -34), (-28, 0), (0, 0), (28, 0), (-28, 34), (0, 34), (28, 34))
        for y in (0.0, 3.5)
    ]
    probes = {
        "schemaVersion": 1,
        "status": "AUTHORING_POSITIONS_READY_NOT_INTEGRATED",
        "coordinateFrame": "KBDU local ENU meters; Unity +Y up",
        "probeCount": len(probe_positions),
        "positionsXyzMeters": probe_positions,
        "runtimeUpdates": False,
        "purpose": "Dynamic aircraft and authored airport props; a future accepted bake may light the static core.",
    }
    reflections = {
        "schemaVersion": 1,
        "status": "AUTHORING_PLAN_READY_NOT_BAKED",
        "screenSpaceReflections": False,
        "plan": [{
            "name": "AirportCoreV4_BakedReflection",
            "positionXyzMeters": [-385.0, 5.0, -260.0],
            "boxSizeXyzMeters": [260.0, 35.0, 240.0],
            "resolutionPerFace": int(lighting["reflectionProbeResolutionPerFace"]),
            "runtimeUpdates": False,
            "consumers": ["terminal opaque glazing", "parked aircraft", "service props"],
        }],
        "estimatedAndroidBytes": estimate_astc_6x6_mipped_bytes(int(lighting["reflectionProbeResolutionPerFace"])) * 6,
    }
    paths = {
        "profile": folders["lighting"] / "AirportCoreLightingProfile.json",
        "calibration": folders["lighting"] / "OrthophotoSunCalibration.json",
        "lightmaps": folders["lighting"] / "AirportCoreLightmapPlan.json",
        "probes": folders["lighting"] / "AirportCoreProbePlan.json",
        "reflections": folders["lighting"] / "AirportCoreReflectionPlan.json",
        "contract": folders["lighting"] / "INTEGRATION_CONTRACT.md",
    }
    write_json(paths["profile"], profile)
    write_json(paths["calibration"], {"schemaVersion": 1, "inference": inference})
    write_json(paths["lightmaps"], lightmaps)
    write_json(paths["probes"], probes)
    write_json(paths["reflections"], reflections)
    paths["contract"].write_text(
        "# Airport-core V4 lighting integration contract\n\n"
        "- Serialize this fixed profile under `AirportCoreBenchmarkRoot`; do not construct it at runtime.\n"
        "- This base Stage-1 authoring plan allows one or two compressed 2048 atlases; V4.1 overrides it with a one-1024-atlas remediation target and currently has no accepted lightmap.\n"
        "- Disable broad realtime shadows on those static renderers and reconcile every removed photograph shadow.\n"
        "- Use the listed light probes for dynamic aircraft/props and at most one baked 128-face reflection probe.\n"
        "- Opaque terminal glazing samples the selected sky/reflection probe; no realtime reflection updates.\n"
        "- Preserve the existing cockpit shadow contract; an aircraft-only realtime sun shadow is an A/B candidate, not a default.\n"
        "- A successful Unity bake requires exported lighting-data hashes, memory, overlap, pixel-level finiteness validation, and full-resolution A/B evidence.\n"
        "- Simulator stereo and physical Quest timing remain separate mandatory gates.\n",
        encoding="utf-8",
        newline="\n",
    )
    for path in paths.values():
        write_text_meta(path, repo)
    return list(paths.values())


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=pathlib.Path, default=REPO_ROOT)
    parser.add_argument("--config", type=pathlib.Path, default=CONFIG_PATH)
    parser.add_argument("--raw-root", type=pathlib.Path, required=True)
    parser.add_argument("--replacement-manifest", type=pathlib.Path, required=True)
    parser.add_argument("--artifact-dir", type=pathlib.Path, required=True)
    args = parser.parse_args()
    repo = args.repo_root.resolve()
    config = load_json(args.config.resolve())
    raw_root = args.raw_root.resolve()
    replacement_path = args.replacement_manifest.resolve()
    replacements_manifest = load_json(replacement_path)
    replacements = replacements_manifest.get("replacements", [])
    required_roles = set(config["cleanPlate"]["requiredReplacementRoles"])
    actual_roles = {role_for(str(item.get("asset_id", ""))) for item in replacements}
    missing_roles = required_roles - actual_roles
    if missing_roles:
        raise ValueError(f"Replacement manifest missing required roles: {sorted(missing_roles)}")
    if len(replacements) != 2:
        raise ValueError(f"Stage 1 clean plate expects exactly two replacements; got {len(replacements)}")
    receipt_path = raw_root / "polyhaven_raw_receipt.json"
    receipt = load_json(receipt_path)
    maps = asset_map(raw_root, receipt)
    asset_root = repo / config["assetRoot"]
    folders = ensure_unity_folders(asset_root, repo)
    near_path = repo / config["cleanPlate"]["orthophoto"]
    near = Image.open(near_path).convert("RGB")
    if near.size != (4096, 4096):
        raise ValueError(f"Unexpected near orthophoto dimensions: {near.size}")

    material_outputs: list[pathlib.Path] = []
    atlas_records = []
    for label, slots in (("Terminal", config["materials"]["terminalAtlas"]), ("HangarKit", config["materials"]["hangarKitAtlas"])):
        atlas = build_atlas(slots, int(config["materials"]["atlasSizePixels"]), maps, SEED + len(label))
        paths = [
            folders["materials"] / f"AirportCore{label}_Albedo.png",
            folders["materials"] / f"AirportCore{label}_Normal.png",
            folders["materials"] / f"AirportCore{label}_MRAO.png",
        ]
        atlas[0].save(paths[0], optimize=True)
        atlas[1].save(paths[1], optimize=True)
        atlas[2].save(paths[2], optimize=True)
        material_outputs.extend(paths)
        atlas_records.append({"name": label, "grid": [4, 4], "sizePixels": [atlas[0].width, atlas[0].height], "tiles": atlas[3], "outputs": [record(path, repo) for path in paths]})
    for index, path in enumerate(material_outputs):
        semantic = index % 3
        write_texture_meta(
            path,
            repo,
            srgb=semantic == 0,
            normal=semantic == 1,
            max_size=2048,
            android_max_size=2048 if semantic in (0, 1) else 1024,
        )
    building_shader = folders["materials"] / "AirportCoreBakedLit.shader"
    if not building_shader.is_file():
        raise FileNotFoundError(building_shader)
    write_text_meta(building_shader, repo, "ShaderImporter")
    building_materials: list[pathlib.Path] = []
    for index, label in enumerate(("Terminal", "HangarKit")):
        texture_paths = material_outputs[index * 3:index * 3 + 3]
        material_path = folders["materials"] / f"AirportCore{label}Atlas.mat"
        write_material(
            material_path,
            repo,
            building_shader,
            [("_MainTex", texture_paths[0]), ("_NormalTex", texture_paths[1]), ("_MRAOTex", texture_paths[2])],
            [("_NormalStrength", 0.72), ("_SpecularStrength", 0.42), ("_ReflectionStrength", 0.34)],
            [("_Tint", (1.0, 1.0, 1.0, 1.0))],
        )
        building_materials.append(material_path)
    material_outputs.extend([building_shader, *building_materials])
    material_manifest_path = folders["materials"] / "SOURCE_MANIFEST.json"
    write_json(material_manifest_path, {
        "schemaVersion": 1,
        "status": "OPTIMIZED_DERIVATIVES_READY_NOT_VISUALLY_ACCEPTED",
        "atlasContract": "Two shared 2048 4x4 atlases matching Workstream A tile indices; MRAO channels are metallic/roughness/AO.",
        "atlases": atlas_records,
        "externalSources": receipt,
        "externalRawPolicy": "Raw Poly Haven maps and API snapshots remain outside Git.",
        "estimatedAndroidResidentBytesIfBothAtlasesLoaded": 4 * estimate_astc_6x6_mipped_bytes(2048) + 2 * estimate_astc_6x6_mipped_bytes(1024),
        "runtimeMaterials": [record(path, repo) for path in building_materials],
        "shader": record(building_shader, repo),
        "acceptanceBoundary": "UV assignment, Unity import, baked response, stereo, and Quest budget require lead integration evidence.",
    })
    write_text_meta(material_manifest_path, repo)

    lighting_samples = lighting_reference_footprints(repo, config, replacements)
    inference = infer_sun(near, lighting_samples, config)
    clean_outputs, clean_manifest = build_clean_plate(near, replacements, replacements_manifest, inference, config, folders["clean"])
    write_texture_meta(clean_outputs[0], repo, srgb=True, normal=False, max_size=4096, streaming=True, aniso=8)
    for path in clean_outputs[1:]:
        write_texture_meta(path, repo, srgb=False, normal=False, max_size=2048, streaming=False, aniso=1)
    clean_manifest.update({
        "source": record(near_path, repo),
        "replacementManifest": record(replacement_path, repo),
        "license": "LicenseRef-US-Government-Work-Public-Domain derivative; OSM produced-work polygon contract",
        "outputs": [record(path, repo) for path in clean_outputs],
    })
    clean_manifest_path = folders["clean"] / "SOURCE_MANIFEST.json"
    write_json(clean_manifest_path, clean_manifest)
    write_text_meta(clean_manifest_path, repo)

    profile_outputs = write_profiles(folders, inference, config, repo)
    runway_outputs, runway_manifest = build_runway(maps, near, config, folders["runway"])
    for index, path in enumerate(runway_outputs):
        write_texture_meta(path, repo, srgb=index == 0, normal=index == 1, max_size=2048, android_max_size=1024, streaming=True, aniso=8)
    runway_shader = folders["runway"] / "AirportCoreContinuousRunway.shader"
    if not runway_shader.is_file():
        raise FileNotFoundError(runway_shader)
    write_text_meta(runway_shader, repo, "ShaderImporter")
    runway_material = folders["runway"] / "AirportCoreContinuousRunway.mat"
    write_material(
        runway_material,
        repo,
        runway_shader,
        [("_MainTex", runway_outputs[0]), ("_NormalTex", runway_outputs[1]), ("_MRAOTex", runway_outputs[2]), ("_FeatureMask", runway_outputs[3])],
        [("_WorldTiling", 0.08), ("_SecondaryTiling", 0.027), ("_NormalStrength", 0.62)],
        [("_Tint", (1.0, 1.0, 1.0, 1.0))],
    )
    runway_contract = folders["runway"] / "INTEGRATION_CONTRACT.md"
    if not runway_contract.is_file():
        raise FileNotFoundError(runway_contract)
    write_text_meta(runway_contract, repo)
    runway_outputs.extend([runway_shader, runway_material, runway_contract])
    runway_manifest.update({
        "outputs": [record(path, repo) for path in runway_outputs],
        "license": "CC0-1.0 source derivative plus project-authored procedural masks",
        "rawSource": [entry for entry in receipt["files"] if entry["assetId"] == "asphalt_02"],
        "estimatedAndroidResidentBytes": 4 * estimate_astc_6x6_mipped_bytes(1024),
    })
    runway_manifest_path = folders["runway"] / "SOURCE_MANIFEST.json"
    write_json(runway_manifest_path, runway_manifest)
    write_text_meta(runway_manifest_path, repo)

    artifact = args.artifact_dir.resolve()
    artifact.mkdir(parents=True, exist_ok=True)
    west, south, east, north = replacements_manifest["benchmark_bounds_xz_meters"]
    left, top = world_to_pixel((west, north), near.width, float(config["cleanPlate"]["nearHalfExtentMeters"]))
    right, bottom = world_to_pixel((east, south), near.width, float(config["cleanPlate"]["nearHalfExtentMeters"]))
    near.crop((int(left), int(top), int(right), int(bottom))).save(artifact / "airport_core_original_orthophoto.png", optimize=True)
    Image.open(clean_outputs[0]).crop((int(left), int(top), int(right), int(bottom))).save(artifact / "airport_core_clean_plate.png", optimize=True)
    summary = {
        "schemaVersion": 1,
        "classification": "PASS_WORKSTREAM_B_AUTHORING_ASSETS_UNITY_BAKE_NOT_RUN",
        "materialOutputs": [record(path, repo) for path in material_outputs],
        "cleanPlateOutputs": [record(path, repo) for path in clean_outputs],
        "lightingOutputs": [record(path, repo) for path in profile_outputs],
        "runwayOutputs": [record(path, repo) for path in runway_outputs],
        "sun": inference,
        "artifactPreviews": [record(artifact / "airport_core_original_orthophoto.png", artifact), record(artifact / "airport_core_clean_plate.png", artifact)],
        "evidenceBoundary": "Deterministic source/derivative and authoring-contract evidence only; Unity bake, visual acceptance, stereo, and Quest performance are unrun.",
    }
    write_json(artifact / "workstream_b_build_report.json", summary)
    print(json.dumps({"classification": summary["classification"], "sunAzimuthDegrees": inference["sunAzimuthDegrees"], "sunElevationDegrees": inference["sunElevationDegrees"]}, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
