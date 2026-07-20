#!/usr/bin/env python3
"""Generate deterministic V4 water/vegetation benchmark textures and contracts.

The script intentionally has no third-party dependencies. It does not alter a
Unity scene or prefab. When pointed at the product Assets directory it writes
only the Workstream C-owned AirportCoreV4/Water and /Vegetation trees.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
import shutil
import struct
import zlib
from pathlib import Path
from typing import Iterable, Sequence


TOOL_DIR = Path(__file__).resolve().parent
TEMPLATE_DIR = TOOL_DIR / "templates"
LOGICAL_ROOT = "Assets/Production/Environment/AirportCoreV4"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def stable_guid(logical_path: str) -> str:
    return hashlib.sha256(("questflight-v4:" + logical_path.replace("\\", "/")).encode()).hexdigest()[:32]


def png_chunk(kind: bytes, data: bytes) -> bytes:
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)


def write_png_rgba(path: Path, width: int, height: int, pixels: bytes | bytearray) -> None:
    if len(pixels) != width * height * 4:
        raise ValueError(f"RGBA payload mismatch for {path}: {len(pixels)}")
    rows = bytearray()
    stride = width * 4
    for y in range(height):
        rows.append(0)
        rows.extend(pixels[y * stride : (y + 1) * stride])
    payload = b"\x89PNG\r\n\x1a\n"
    payload += png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    payload += png_chunk(b"IDAT", zlib.compress(bytes(rows), 9))
    payload += png_chunk(b"IEND", b"")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)


def paeth(a: int, b: int, c: int) -> int:
    p = a + b - c
    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
    return a if pa <= pb and pa <= pc else b if pb <= pc else c


def read_png_rgba(path: Path) -> tuple[int, int, bytearray]:
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"Not a PNG: {path}")
    cursor = 8
    idat = bytearray()
    width = height = bit_depth = color_type = interlace = None
    while cursor < len(data):
        length = struct.unpack(">I", data[cursor : cursor + 4])[0]
        kind = data[cursor + 4 : cursor + 8]
        body = data[cursor + 8 : cursor + 8 + length]
        cursor += 12 + length
        if kind == b"IHDR":
            width, height, bit_depth, color_type, _, _, interlace = struct.unpack(">IIBBBBB", body)
        elif kind == b"IDAT":
            idat.extend(body)
        elif kind == b"IEND":
            break
    if bit_depth != 8 or interlace != 0 or color_type not in (0, 2, 4, 6):
        raise ValueError(f"Unsupported PNG encoding for {path}: depth={bit_depth}, color={color_type}, interlace={interlace}")
    channels = {0: 1, 2: 3, 4: 2, 6: 4}[color_type]
    raw = zlib.decompress(bytes(idat))
    stride = width * channels
    rows: list[bytearray] = []
    offset = 0
    for _ in range(height):
        filter_type = raw[offset]
        offset += 1
        scan = bytearray(raw[offset : offset + stride])
        offset += stride
        previous = rows[-1] if rows else bytearray(stride)
        for i in range(stride):
            left = scan[i - channels] if i >= channels else 0
            up = previous[i]
            upper_left = previous[i - channels] if i >= channels else 0
            if filter_type == 1:
                scan[i] = (scan[i] + left) & 255
            elif filter_type == 2:
                scan[i] = (scan[i] + up) & 255
            elif filter_type == 3:
                scan[i] = (scan[i] + ((left + up) >> 1)) & 255
            elif filter_type == 4:
                scan[i] = (scan[i] + paeth(left, up, upper_left)) & 255
            elif filter_type != 0:
                raise ValueError(f"Unsupported PNG filter {filter_type}")
        rows.append(scan)
    rgba = bytearray(width * height * 4)
    for y, row in enumerate(rows):
        for x in range(width):
            src = x * channels
            dst = (y * width + x) * 4
            if color_type == 0:
                value = row[src]
                rgba[dst : dst + 4] = bytes((value, value, value, 255))
            elif color_type == 2:
                rgba[dst : dst + 4] = bytes((row[src], row[src + 1], row[src + 2], 255))
            elif color_type == 4:
                value, alpha = row[src], row[src + 1]
                rgba[dst : dst + 4] = bytes((value, value, value, alpha))
            else:
                rgba[dst : dst + 4] = row[src : src + 4]
    return width, height, rgba


def sample_bilinear(pixels: Sequence[int], width: int, height: int, u: float, v: float) -> tuple[int, int, int, int]:
    x = max(0.0, min(width - 1.0, u * (width - 1)))
    y = max(0.0, min(height - 1.0, v * (height - 1)))
    x0, y0 = int(x), int(y)
    x1, y1 = min(width - 1, x0 + 1), min(height - 1, y0 + 1)
    tx, ty = x - x0, y - y0
    result = []
    for channel in range(4):
        a = pixels[(y0 * width + x0) * 4 + channel]
        b = pixels[(y0 * width + x1) * 4 + channel]
        c = pixels[(y1 * width + x0) * 4 + channel]
        d = pixels[(y1 * width + x1) * 4 + channel]
        value = (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty
        result.append(round(value))
    return tuple(result)  # type: ignore[return-value]


def smoothstep(edge0: float, edge1: float, value: float) -> float:
    t = max(0.0, min(1.0, (value - edge0) / max(edge1 - edge0, 1e-8)))
    return t * t * (3.0 - 2.0 * t)


def generate_wave_normal(path: Path, size: int, phase: float, broad: bool) -> dict:
    pixels = bytearray(size * size * 4)
    waves = (
        ((2, 1), 0.46, phase),
        ((-1, 3), 0.28, phase + 1.7),
        ((4, -3), 0.16, phase + 3.1),
        ((5, 7), 0.10, phase + 0.8),
    ) if broad else (
        ((3, 5), 0.40, phase),
        ((-5, 4), 0.30, phase + 2.0),
        ((8, -3), 0.18, phase + 4.2),
        ((11, 9), 0.12, phase + 1.2),
    )
    strength = 0.045 if broad else 0.030
    for y in range(size):
        v = y / size
        for x in range(size):
            u = x / size
            dx = dy = 0.0
            for (kx, ky), amplitude, wave_phase in waves:
                angle = math.tau * (kx * u + ky * v) + wave_phase
                derivative = math.cos(angle) * math.tau * amplitude
                dx += derivative * kx
                dy += derivative * ky
            nx, ny, nz = -dx * strength, -dy * strength, 1.0
            inv = 1.0 / math.sqrt(nx * nx + ny * ny + nz * nz)
            offset = (y * size + x) * 4
            pixels[offset : offset + 4] = bytes((
                round((nx * inv * 0.5 + 0.5) * 255),
                round((ny * inv * 0.5 + 0.5) * 255),
                round((nz * inv * 0.5 + 0.5) * 255),
                255,
            ))
    write_png_rgba(path, size, size, pixels)
    return {"width": size, "height": size, "sha256": sha256(path), "seamless": True}


def generate_reservoir_mask(source: Path, output: Path, size: int = 1024) -> dict:
    source_width, source_height, source_pixels = read_png_rgba(source)
    target = bytearray(size * size * 4)
    coverage = 0
    for y in range(size):
        v = y / max(1, size - 1)
        for x in range(size):
            u = x / max(1, size - 1)
            red, green, blue, _ = sample_bilinear(source_pixels, source_width, source_height, u, v)
            occupancy = max(red / 255.0, green / 255.0)
            suppression = round(smoothstep(0.008, 0.14, occupancy) * 255)
            coverage += 1 if suppression >= 128 else 0
            offset = (y * size + x) * 4
            target[offset : offset + 4] = bytes((red, green, blue, suppression))
    write_png_rgba(output, size, size, target)
    return {
        "width": size,
        "height": size,
        "source_width": source_width,
        "source_height": source_height,
        "source_sha256": sha256(source),
        "sha256": sha256(output),
        "suppression_coverage_fraction": coverage / (size * size),
        "channels": {"r": "depth", "g": "shore feather", "b": "roughness", "a": "orthophoto suppression"},
    }


def generate_minor_water_mask(path: Path, size: int = 512) -> dict:
    pixels = bytearray(size * size * 4)
    water_count = bank_count = 0
    for y in range(size):
        v = y / max(1, size - 1)
        for x in range(size):
            u = x / max(1, size - 1)
            center = 0.50 + 0.095 * math.sin(math.tau * (v * 1.35 + 0.08)) + 0.025 * math.sin(math.tau * v * 4.0)
            half_width = 0.012 + 0.005 * (0.5 + 0.5 * math.sin(math.tau * v * 2.2 + 0.7))
            distance = abs(u - center)
            water = 1.0 - smoothstep(half_width * 0.68, half_width * 1.22, distance)
            wet_bank = (1.0 - smoothstep(half_width * 1.1, half_width * 3.2, distance)) * (1.0 - water)
            ripple = 0.5 + 0.5 * math.sin(math.tau * (u * 17.0 + v * 9.0))
            water_count += 1 if water >= 0.5 else 0
            bank_count += 1 if wet_bank >= 0.2 else 0
            offset = (y * size + x) * 4
            pixels[offset : offset + 4] = bytes((
                round(water * 255),
                round(wet_bank * 255),
                round(ripple * water * 255),
                255,
            ))
    write_png_rgba(path, size, size, pixels)
    return {
        "width": size,
        "height": size,
        "sha256": sha256(path),
        "water_coverage_fraction": water_count / (size * size),
        "wet_bank_coverage_fraction": bank_count / (size * size),
        "channels": {"r": "water", "g": "wet bank", "b": "ripple phase", "a": "reserved"},
        "georeferenced": False,
        "integration_status": "NORMALIZED_PROTOTYPE_REQUIRES_BENCHMARK_UV_BINDING",
    }


def set_pixel(pixels: bytearray, width: int, x: int, y: int, rgba: tuple[int, int, int, int]) -> None:
    if x < 0 or y < 0 or x >= width or y >= width:
        return
    offset = (y * width + x) * 4
    source_alpha = rgba[3] / 255.0
    destination_alpha = pixels[offset + 3] / 255.0
    out_alpha = source_alpha + destination_alpha * (1.0 - source_alpha)
    if out_alpha <= 1e-6:
        return
    for channel in range(3):
        source = rgba[channel] / 255.0
        destination = pixels[offset + channel] / 255.0
        result = (source * source_alpha + destination * destination_alpha * (1.0 - source_alpha)) / out_alpha
        pixels[offset + channel] = max(0, min(255, round(result * 255)))
    pixels[offset + 3] = max(0, min(255, round(out_alpha * 255)))


def draw_ellipse(
    pixels: bytearray,
    size: int,
    cx: float,
    cy: float,
    rx: float,
    ry: float,
    angle: float,
    color: tuple[int, int, int],
) -> None:
    cosine, sine = math.cos(angle), math.sin(angle)
    radius = math.ceil(max(rx, ry)) + 2
    for y in range(max(0, int(cy - radius)), min(size, int(cy + radius + 1))):
        for x in range(max(0, int(cx - radius)), min(size, int(cx + radius + 1))):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            local_x = dx * cosine + dy * sine
            local_y = -dx * sine + dy * cosine
            distance = (local_x / max(rx, 0.1)) ** 2 + (local_y / max(ry, 0.1)) ** 2
            if distance > 1.08:
                continue
            alpha = round((1.0 - smoothstep(0.82, 1.08, distance)) * 255)
            sun = max(0.0, min(1.0, 0.78 + (-local_x / max(rx, 0.1) - local_y / max(ry, 0.1)) * 0.10))
            shaded = tuple(max(0, min(255, round(component * sun))) for component in color)
            set_pixel(pixels, size, x, y, (*shaded, alpha))


def generate_vegetation_atlas(path: Path, size: int = 1024) -> dict:
    pixels = bytearray(size * size * 4)
    # Color-bleed fields avoid black ASTC/mipmap fringes while alpha remains zero.
    for y in range(size):
        for x in range(size):
            offset = (y * size + x) * 4
            if x < 128:
                wave = math.sin(x * 0.31 + y * 0.071) * 9 + math.sin(y * 0.23) * 5
                pixels[offset : offset + 4] = bytes((
                    max(0, min(255, round(92 + wave))),
                    max(0, min(255, round(69 + wave * 0.55))),
                    max(0, min(255, round(44 + wave * 0.30))),
                    255,
                ))
            else:
                pixels[offset : offset + 4] = bytes((62, 104, 49, 0))

    rng = random.Random(431901)
    tiles = [
        ((128, 0, 576, 512), (86, 139, 65), 320, "cottonwood_a"),
        ((576, 0, 1024, 512), (94, 147, 69), 310, "cottonwood_b"),
        ((128, 512, 576, 1024), (94, 130, 57), 340, "deciduous"),
        ((576, 512, 1024, 1024), (88, 124, 58), 380, "shrub"),
    ]
    for (x0, y0, x1, y1), base, count, _ in tiles:
        center_x, center_y = (x0 + x1) * 0.5, (y0 + y1) * 0.5
        spread_x, spread_y = (x1 - x0) * 0.39, (y1 - y0) * 0.39
        for index in range(count):
            theta = rng.random() * math.tau
            radius = math.sqrt(rng.random())
            cx = center_x + math.cos(theta) * radius * spread_x
            cy = center_y + math.sin(theta) * radius * spread_y
            rx = rng.uniform(10.0, 25.0) * (1.0 - radius * 0.12)
            ry = rx * rng.uniform(0.48, 0.80)
            variation = rng.randint(-15, 18)
            color = tuple(max(12, min(235, component + variation + (9 if index % 7 == 0 else 0))) for component in base)
            draw_ellipse(pixels, size, cx, cy, rx, ry, rng.uniform(-1.2, 1.2), color)

    alpha_pixels = sum(1 for index in range(3, len(pixels), 4) if pixels[index] >= 118)
    write_png_rgba(path, size, size, pixels)
    return {
        "width": size,
        "height": size,
        "sha256": sha256(path),
        "alpha_test_reference": 0.46,
        "covered_pixel_fraction": alpha_pixels / (size * size),
        "regions": {
            "bark": [0.0, 0.0, 0.125, 1.0],
            "cottonwood_a": [0.125, 0.0, 0.5625, 0.5],
            "cottonwood_b": [0.5625, 0.0, 1.0, 0.5],
            "deciduous": [0.125, 0.5, 0.5625, 1.0],
            "shrub": [0.5625, 0.5, 1.0, 1.0],
        },
        "sun_bake": {"azimuth_degrees": 135.0, "elevation_degrees": 56.934, "confidence": "LOW_V3_CALIBRATION"},
    }


def astc_bytes(width: int, height: int, block: int, mipmaps: bool = True) -> int:
    total = 0
    while True:
        total += math.ceil(width / block) * math.ceil(height / block) * 16
        if not mipmaps or (width == 1 and height == 1):
            break
        width, height = max(1, width // 2), max(1, height // 2)
    return total


def generic_meta(logical_path: str, user_data: str) -> str:
    return f"""fileFormatVersion: 2
guid: {stable_guid(logical_path)}
DefaultImporter:
  externalObjects: {{}}
  userData: {user_data}
  assetBundleName:
  assetBundleVariant:
"""


def shader_meta(logical_path: str) -> str:
    return f"""fileFormatVersion: 2
guid: {stable_guid(logical_path)}
ShaderImporter:
  externalObjects: {{}}
  defaultTextures: []
  nonModifiableTextures: []
  userData: Airport Core V4 project-authored bounded candidate
  assetBundleName:
  assetBundleVariant:
"""


def material_meta(logical_path: str) -> str:
    return f"""fileFormatVersion: 2
guid: {stable_guid(logical_path)}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 2100000
  userData: Airport Core V4 project-authored bounded candidate
  assetBundleName:
  assetBundleVariant:
"""


def texture_meta(logical_path: str, kind: str, max_size: int, astc_format: int) -> str:
    is_normal = kind == "normal"
    is_atlas = kind == "foliage"
    srgb = 1 if is_atlas else 0
    wrap = 0 if is_normal else 1
    return f"""fileFormatVersion: 2
guid: {stable_guid(logical_path)}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: {srgb}
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: {1 if is_atlas else 0}
    alphaTestReferenceValue: {0.46 if is_atlas else 0.5}
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: {max_size}
  textureSettings:
    serializedVersion: 2
    filterMode: 2
    aniso: {4 if is_atlas else 2}
    mipBias: 0
    wrapU: {wrap}
    wrapV: {wrap}
    wrapW: {wrap}
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
  alphaUsage: {1 if is_atlas else 0}
  alphaIsTransparency: {1 if is_atlas else 0}
  textureType: {1 if is_normal else 0}
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 1
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: {max_size}
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 80
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
  - serializedVersion: 4
    buildTarget: Android
    maxTextureSize: {max_size}
    resizeAlgorithm: 0
    textureFormat: {astc_format}
    textureCompression: 1
    compressionQuality: 80
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 1
    androidETC2FallbackOverride: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  userData: Airport Core V4 project-authored {kind}; see adjacent source manifest
  assetBundleName:
  assetBundleVariant:
"""


def write_meta(path: Path, logical_path: str, kind: str = "generic", max_size: int = 1024, astc_format: int = 50) -> None:
    if kind in ("normal", "mask", "foliage"):
        content = texture_meta(logical_path, kind, max_size, astc_format)
    elif kind in ("shader", "hlsl"):
        content = shader_meta(logical_path)
    elif kind == "material":
        content = material_meta(logical_path)
    else:
        content = generic_meta(logical_path, "Airport Core V4 project-authored bounded candidate")
    Path(str(path) + ".meta").write_text(content, encoding="utf-8", newline="\n")


def write_folder_meta(directory: Path, logical_path: str) -> None:
    Path(str(directory) + ".meta").write_text(
        f"fileFormatVersion: 2\nguid: {stable_guid(logical_path)}\nfolderAsset: yes\nDefaultImporter:\n"
        "  externalObjects: {}\n  userData: Airport Core V4 project-authored bounded candidate\n"
        "  assetBundleName:\n  assetBundleVariant:\n",
        encoding="utf-8", newline="\n")


def copy_template(name: str, output: Path, logical_path: str) -> dict:
    output.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(TEMPLATE_DIR / name, output)
    write_meta(output, logical_path, "shader" if output.suffix == ".shader" else "hlsl")
    return {"path": logical_path, "sha256": sha256(output), "license": "Project-authored"}


def write_json(path: Path, document: dict, logical_path: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(document, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    write_meta(path, logical_path)


def locate_repo_root(explicit: str | None) -> Path:
    if explicit:
        return Path(explicit).resolve()
    current = TOOL_DIR
    while current.parent != current:
        if (current / "QuestFlight").is_dir() and (current / ".git").exists():
            return current
        current = current.parent
    raise RuntimeError("Could not locate repository root")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, help="Root that will contain Water/ and Vegetation/")
    parser.add_argument("--repo-root")
    args = parser.parse_args()

    repo = locate_repo_root(args.repo_root)
    output = Path(args.output).resolve()
    water = output / "Water"
    vegetation = output / "Vegetation"
    water_textures = water / "Textures"
    water_shaders = water / "Shaders"
    vegetation_textures = vegetation / "Textures"
    vegetation_shaders = vegetation / "Shaders"
    vegetation_models = vegetation / "Models"
    water_materials = water / "Materials"
    vegetation_materials = vegetation / "Materials"
    reports = water / "Reports"
    for directory in (water_textures, water_shaders, water_materials, reports,
                      vegetation_textures, vegetation_shaders, vegetation_materials, vegetation_models):
        directory.mkdir(parents=True, exist_ok=True)
    for directory, logical in (
        (water, f"{LOGICAL_ROOT}/Water"),
        (water_textures, f"{LOGICAL_ROOT}/Water/Textures"),
        (water_shaders, f"{LOGICAL_ROOT}/Water/Shaders"),
        (water_materials, f"{LOGICAL_ROOT}/Water/Materials"),
        (reports, f"{LOGICAL_ROOT}/Water/Reports"),
        (vegetation, f"{LOGICAL_ROOT}/Vegetation"),
        (vegetation_textures, f"{LOGICAL_ROOT}/Vegetation/Textures"),
        (vegetation_shaders, f"{LOGICAL_ROOT}/Vegetation/Shaders"),
        (vegetation_materials, f"{LOGICAL_ROOT}/Vegetation/Materials"),
        (vegetation_models, f"{LOGICAL_ROOT}/Vegetation/Models"),
    ):
        write_folder_meta(directory, logical)

    reservoir_source = repo / "QuestFlight/Assets/Production/Environment/Textures/Reservoir/BoulderReservoir_DepthShoreRoughness.png"
    authoritative_mesh = repo / "QuestFlight/Assets/Production/Environment/Generated/BoulderReservoir_OSM_AuthoritativeWater_Mesh.asset"
    sky_cube = repo / "QuestFlight/Assets/Production/Environment/Sky/Kloppenheim03_PureSky_2K.hdr"
    for required in (reservoir_source, authoritative_mesh, sky_cube):
        if not required.exists():
            raise FileNotFoundError(required)

    wave_a_path = water_textures / "AirportCoreReservoir_WaveA_NM.png"
    wave_b_path = water_textures / "AirportCoreReservoir_WaveB_NM.png"
    reservoir_mask_path = water_textures / "AirportCoreReservoir_DepthShoreRoughnessSuppression.png"
    minor_mask_path = water_textures / "AirportCoreMinorWater_TerrainMask.png"
    atlas_path = vegetation_textures / "AirportCoreVegetation_Atlas.png"

    wave_a = generate_wave_normal(wave_a_path, 512, 0.37, True)
    wave_b = generate_wave_normal(wave_b_path, 512, 2.11, False)
    reservoir_mask = generate_reservoir_mask(reservoir_source, reservoir_mask_path)
    minor_mask = generate_minor_water_mask(minor_mask_path)
    atlas = generate_vegetation_atlas(atlas_path)

    for path, kind, max_size, astc_format in (
        (wave_a_path, "normal", 512, 50),
        (wave_b_path, "normal", 512, 50),
        (reservoir_mask_path, "mask", 1024, 50),
        (minor_mask_path, "mask", 512, 50),
        (atlas_path, "foliage", 1024, 48),
    ):
        relative = path.relative_to(output).as_posix()
        write_meta(path, f"{LOGICAL_ROOT}/{relative}", kind, max_size, astc_format)

    shader_records = [
        copy_template("KBDUReservoirWaterV4.shader", water_shaders / "KBDUReservoirWaterV4.shader",
                      f"{LOGICAL_ROOT}/Water/Shaders/KBDUReservoirWaterV4.shader"),
        copy_template("KBDUMinorWaterTerrainEffect.hlsl", water_shaders / "KBDUMinorWaterTerrainEffect.hlsl",
                      f"{LOGICAL_ROOT}/Water/Shaders/KBDUMinorWaterTerrainEffect.hlsl"),
        copy_template("KBDUReservoirGroundSuppression.hlsl", water_shaders / "KBDUReservoirGroundSuppression.hlsl",
                      f"{LOGICAL_ROOT}/Water/Shaders/KBDUReservoirGroundSuppression.hlsl"),
        copy_template("KBDUVegetationHeroV4.shader", vegetation_shaders / "KBDUVegetationHeroV4.shader",
                      f"{LOGICAL_ROOT}/Vegetation/Shaders/KBDUVegetationHeroV4.shader"),
    ]

    water_material_path = water_materials / "AirportCoreReservoir_V4.mat"
    water_shader_guid = stable_guid(f"{LOGICAL_ROOT}/Water/Shaders/KBDUReservoirWaterV4.shader")
    wave_a_guid = stable_guid(f"{LOGICAL_ROOT}/Water/Textures/{wave_a_path.name}")
    wave_b_guid = stable_guid(f"{LOGICAL_ROOT}/Water/Textures/{wave_b_path.name}")
    reservoir_mask_guid = stable_guid(f"{LOGICAL_ROOT}/Water/Textures/{reservoir_mask_path.name}")
    water_material_path.write_text(f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: AirportCoreReservoir_V4
  m_Shader: {{fileID: 4800000, guid: {water_shader_guid}, type: 3}}
  m_Parent: {{fileID: 0}}
  m_ModifiedSerializedProperties: 0
  m_ValidKeywords: []
  m_InvalidKeywords: []
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 1
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: 2005
  stringTagMap:
    RenderType: Opaque
  disabledShaderPasses: []
  m_LockedProperties:
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
    - _DepthShoreRoughnessSuppression:
        m_Texture: {{fileID: 2800000, guid: {reservoir_mask_guid}, type: 3}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _EnvironmentCube:
        m_Texture: {{fileID: 8900000, guid: 38418911228a4bf4c81b3e7ed32cf656, type: 3}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _WaveNormalA:
        m_Texture: {{fileID: 2800000, guid: {wave_a_guid}, type: 3}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _WaveNormalB:
        m_Texture: {{fileID: 2800000, guid: {wave_b_guid}, type: 3}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    m_Ints: []
    m_Floats:
    - _EnvironmentMaxMip: 7
    - _FresnelF0: 0.02
    - _ReflectionFloor: 0.07
    - _ReflectionIntensity: 0.82
    - _RoughnessMax: 0.68
    - _RoughnessMin: 0.32
    - _ShoreBandStrength: 0.18
    - _SunGlitter: 0.16
    - _WaveMetersA: 210
    - _WaveMetersB: 72
    - _WaveStrengthA: 0.2
    - _WaveStrengthB: 0.12
    m_Colors:
    - _BasinCenterExtent: {{r: 249.5625, g: 4307.6875, b: 1062.5625, a: 857.1875}}
    - _DeepColor: {{r: 0.035, g: 0.125, b: 0.185, a: 1}}
    - _EnvironmentRotationSinCos: {{r: -0.98799, g: 0.15451, b: 0, a: 0}}
    - _ReflectionTint: {{r: 0.86, g: 0.91, b: 0.94, a: 1}}
    - _ShallowColor: {{r: 0.17, g: 0.34, b: 0.35, a: 1}}
    - _ShoreBandColor: {{r: 0.55, g: 0.6, b: 0.43, a: 1}}
    - _WaveScrollA: {{r: 0.00022, g: 0.00009, b: 0, a: 0}}
    - _WaveScrollB: {{r: -0.00038, g: 0.00031, b: 0, a: 0}}
  m_BuildTextureStacks: []
  m_AllowLocking: 1
""", encoding="utf-8", newline="\n")
    write_meta(water_material_path, f"{LOGICAL_ROOT}/Water/Materials/{water_material_path.name}", "material")

    vegetation_material_path = vegetation_materials / "AirportCoreVegetation_V4.mat"
    vegetation_shader_guid = stable_guid(f"{LOGICAL_ROOT}/Vegetation/Shaders/KBDUVegetationHeroV4.shader")
    atlas_guid = stable_guid(f"{LOGICAL_ROOT}/Vegetation/Textures/{atlas_path.name}")
    vegetation_material_path.write_text(f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: AirportCoreVegetation_V4
  m_Shader: {{fileID: 4800000, guid: {vegetation_shader_guid}, type: 3}}
  m_Parent: {{fileID: 0}}
  m_ModifiedSerializedProperties: 0
  m_ValidKeywords: []
  m_InvalidKeywords: []
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 1
  m_DoubleSidedGI: 1
  m_CustomRenderQueue: 2450
  stringTagMap:
    RenderType: TransparentCutout
  disabledShaderPasses: []
  m_LockedProperties:
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
    - _MainTex:
        m_Texture: {{fileID: 2800000, guid: {atlas_guid}, type: 3}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    m_Ints: []
    m_Floats:
    - _BackfaceLight: 0.62
    - _BakedAtlasStrength: 0.55
    - _Cutoff: 0.46
    m_Colors:
    - _InstanceTint: {{r: 1, g: 1, b: 1, a: 1}}
    - _Tint: {{r: 1, g: 1, b: 1, a: 1}}
  m_BuildTextureStacks: []
  m_AllowLocking: 1
""", encoding="utf-8", newline="\n")
    write_meta(vegetation_material_path,
               f"{LOGICAL_ROOT}/Vegetation/Materials/{vegetation_material_path.name}", "material")

    water_contract = {
        "schema": "questflight.airport-core-v4.water-contract.v1",
        "status": "CANDIDATE_REQUIRES_AUTHORED_INTEGRATION_AND_VISUAL_QA",
        "authoritative_reservoir": {
            "mesh_path": "Assets/Production/Environment/Generated/BoulderReservoir_OSM_AuthoritativeWater_Mesh.asset",
            "mesh_sha256": sha256(authoritative_mesh),
            "renderer_count": 1,
            "shoreline_policy": "IMMUTABLE_AUTHORITATIVE_V2",
            "surface_queue": "OPAQUE_GEOMETRY_PLUS_5",
            "zwrite": True,
        },
        "ground_suppression": {
            "packed_mask": f"{LOGICAL_ROOT}/Water/Textures/{reservoir_mask_path.name}",
            "terrain_include": f"{LOGICAL_ROOT}/Water/Shaders/KBDUReservoirGroundSuppression.hlsl",
            "integration_required": True,
            "changes_outside_authoritative_mask": False,
        },
        "surface": {
            "shader": f"{LOGICAL_ROOT}/Water/Shaders/KBDUReservoirWaterV4.shader",
            "wave_normals": [wave_a_path.name, wave_b_path.name],
            "selected_reflection": "Assets/Production/Environment/Sky/Kloppenheim03_PureSky_2K.hdr",
            "selected_reflection_sha256": sha256(sky_cube),
            "features": ["shallow_deep_color", "dual_low_frequency_normals", "baked_sky_reflection",
                         "fresnel", "bounded_sun_glitter", "roughness_variation", "shore_light_band"],
            "forbidden_features": ["transparency", "SSR", "planar_reflection", "refraction_camera", "tessellation"],
        },
        "minor_water": {
            "representation": "TERRAIN_SHADER_MASK_ONLY",
            "additional_renderer_count": 0,
            "mask": f"{LOGICAL_ROOT}/Water/Textures/{minor_mask_path.name}",
            "include": f"{LOGICAL_ROOT}/Water/Shaders/KBDUMinorWaterTerrainEffect.hlsl",
            "georeferenced": False,
            "blocker": "Lead must bind normalized prototype to the final benchmark terrain UV/bounds before product use.",
        },
    }
    write_json(water / "AirportCoreWater_Contract.json", water_contract,
               f"{LOGICAL_ROOT}/Water/AirportCoreWater_Contract.json")

    vegetation_contract = {
        "schema": "questflight.airport-core-v4.vegetation-contract.v1",
        "status": "CANDIDATE_REQUIRES_AUTHORED_INTEGRATION_AND_VISUAL_QA",
        "cluster_members": ["cottonwood_a", "cottonwood_b", "deciduous", "shrub_group"],
        "lod_objects": ["AirportCoreVegetationCluster_LOD0", "AirportCoreVegetationCluster_LOD1",
                        "AirportCoreVegetationCluster_LOD2"],
        "lod_policy": {"lod0": [0.0, 45.0], "lod1": [45.0, 105.0], "lod2": [105.0, 240.0], "culled_after": 240.0},
        "atlas": f"{LOGICAL_ROOT}/Vegetation/Textures/{atlas_path.name}",
        "shader": f"{LOGICAL_ROOT}/Vegetation/Shaders/KBDUVegetationHeroV4.shader",
        "material_slots_per_lod": 1,
        "alpha_tested": True,
        "transparent": False,
        "gpu_instancing": True,
        "camera_facing_billboard": False,
        "top_oblique_stability": "AUTHORED_HORIZONTAL_AND_OBLIQUE_CARDS; REQUIRES_STEREO_QA",
        "source_blend": "Assets/ArtSource/Blender/KBDU/VegetationHeroCluster.blend",
        "runtime_model": f"{LOGICAL_ROOT}/Vegetation/Models/AirportCoreVegetationCluster.fbx",
    }
    write_json(vegetation / "AirportCoreVegetation_Contract.json", vegetation_contract,
               f"{LOGICAL_ROOT}/Vegetation/AirportCoreVegetation_Contract.json")

    cost_report = {
        "schema": "questflight.airport-core-v4.water-vegetation-cost.v1",
        "status": "TEXTURE_AND_RENDERER_PLANNING_ONLY",
        "water": {
            "reservoir_renderer_delta": 0,
            "reservoir_expected_active_renderers": 1,
            "minor_water_renderer_delta": 0,
            "transparent_material_slots": 0,
            "wave_a_astc_6x6_mip_bytes": astc_bytes(512, 512, 6),
            "wave_b_astc_6x6_mip_bytes": astc_bytes(512, 512, 6),
            "reservoir_mask_astc_6x6_mip_bytes": astc_bytes(1024, 1024, 6),
            "minor_mask_astc_6x6_mip_bytes": astc_bytes(512, 512, 6),
        },
        "vegetation": {
            "active_renderer_count": 1,
            "active_material_slots": 1,
            "transparent_material_slots": 0,
            "atlas_astc_4x4_mip_bytes": astc_bytes(1024, 1024, 4),
            "geometry_report_pending": True,
        },
    }
    cost_report["estimated_texture_bytes_total"] = sum(
        value for section in (cost_report["water"], cost_report["vegetation"])
        for key, value in section.items() if key.endswith("_bytes")
    )
    write_json(reports / "AirportCoreWaterVegetation_CostReport.json", cost_report,
               f"{LOGICAL_ROOT}/Water/Reports/AirportCoreWaterVegetation_CostReport.json")

    source_records = [
        {"path": f"{LOGICAL_ROOT}/Water/Textures/{wave_a_path.name}", "sha256": wave_a["sha256"],
         "source": "Deterministic analytic periodic wave generator", "license": "Project-authored"},
        {"path": f"{LOGICAL_ROOT}/Water/Textures/{wave_b_path.name}", "sha256": wave_b["sha256"],
         "source": "Deterministic analytic periodic wave generator", "license": "Project-authored"},
        {"path": f"{LOGICAL_ROOT}/Water/Textures/{reservoir_mask_path.name}", "sha256": reservoir_mask["sha256"],
         "source": "Derived from stable V2 project-authored reservoir mask", "source_sha256": reservoir_mask["source_sha256"],
         "license": "Project-authored derivative"},
        {"path": f"{LOGICAL_ROOT}/Water/Textures/{minor_mask_path.name}", "sha256": minor_mask["sha256"],
         "source": "Deterministic normalized terrain-mask prototype", "license": "Project-authored"},
        {"path": f"{LOGICAL_ROOT}/Vegetation/Textures/{atlas_path.name}", "sha256": atlas["sha256"],
         "source": "Deterministic procedural foliage and bark atlas", "license": "Project-authored"},
        *shader_records,
        {"path": f"{LOGICAL_ROOT}/Water/Materials/{water_material_path.name}",
         "sha256": sha256(water_material_path), "source": "Project-authored Unity material contract",
         "license": "Project-authored"},
        {"path": f"{LOGICAL_ROOT}/Vegetation/Materials/{vegetation_material_path.name}",
         "sha256": sha256(vegetation_material_path), "source": "Project-authored Unity material contract",
         "license": "Project-authored"},
    ]
    source_manifest = {
        "schema": "questflight.airport-core-v4.water-vegetation-sources.v1",
        "raw_external_assets": [],
        "external_license_dependencies": [],
        "generator": TOOL_DIR.relative_to(repo).as_posix() + "/generate_benchmark_assets.py",
        "generator_sha256": sha256(Path(__file__)),
        "records": sorted(source_records, key=lambda record: record["path"]),
        "notes": [
            "No V3 rejected water or vegetation runtime asset was reused.",
            "The stable V2 packed reservoir mask is the only derived pixel source.",
            "Offline contract validation is not visual, stereo, or Quest performance acceptance.",
        ],
    }
    write_json(reports / "AirportCoreWaterVegetation_SourceManifest.json", source_manifest,
               f"{LOGICAL_ROOT}/Water/Reports/AirportCoreWaterVegetation_SourceManifest.json")

    generation_report = {
        "schema": "questflight.airport-core-v4.water-vegetation-generation.v1",
        "water": {"wave_a": wave_a, "wave_b": wave_b, "reservoir_mask": reservoir_mask, "minor_mask": minor_mask},
        "vegetation": {"atlas": atlas, "geometry": "PENDING_BLENDER_AUTHORING"},
        "output": LOGICAL_ROOT,
    }
    write_json(reports / "AirportCoreWaterVegetation_GenerationReport.json", generation_report,
               f"{LOGICAL_ROOT}/Water/Reports/AirportCoreWaterVegetation_GenerationReport.json")
    print(json.dumps({"status": "GENERATED", "output": str(output),
                      "estimated_texture_bytes": cost_report["estimated_texture_bytes_total"]}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
