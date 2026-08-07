#!/usr/bin/env python3
"""Compile a Quest-bounded KBDU runway material from a rights-cleared source."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


ROOT = Path(__file__).resolve().parents[2]
OUTPUT_DIR = (
    ROOT
    / "QuestFlight"
    / "Assets"
    / "Production"
    / "Environment"
    / "AirportCoreV4"
    / "Runway"
)
SOURCE_PATH = (
    ROOT
    / "tools"
    / "kbdu_runway_photoreal"
    / "sources"
    / "KBDU_Runway_Asphalt_GeneratedSource.png"
)
SEED = 20260807


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def record(path: Path) -> dict[str, object]:
    return {
        "path": path.relative_to(ROOT).as_posix(),
        "bytes": path.stat().st_size,
        "sha256": sha256(path),
    }


def smoothstep(value: np.ndarray) -> np.ndarray:
    value = np.clip(value, 0.0, 1.0)
    return value * value * (3.0 - 2.0 * value)


def make_seamless(source: Image.Image, size: int) -> np.ndarray:
    width, height = source.size
    side = min(width, height)
    left = (width - side) // 2
    top = (height - side) // 2
    source = source.crop((left, top, left + side, top + side))
    source = source.resize((size, size), Image.Resampling.LANCZOS)
    original = np.asarray(source.convert("RGB"), dtype=np.float32) / 255.0

    # The half-tile roll makes opposite borders adjacent samples from the
    # original image. Blend the unshifted source back into the interior so the
    # relocated source border cannot appear as a cross through the tile.
    shifted = np.roll(original, shift=(size // 2, size // 2), axis=(0, 1))
    coordinate = np.linspace(0.0, 1.0, size, endpoint=False)
    edge_distance = np.minimum(coordinate, 1.0 - coordinate) * 2.0
    interior = smoothstep(np.minimum.outer(edge_distance, edge_distance) / 0.62)
    seamless = shifted * (1.0 - interior[..., None]) + original * interior[..., None]

    # KBDU's maintained runway is neutral medium gray. Preserve real aggregate
    # chroma but remove the generated source's slight warm cast.
    luminance = np.sum(seamless * np.array([0.2126, 0.7152, 0.0722]), axis=2)
    neutral = np.repeat(luminance[..., None], 3, axis=2)
    seamless = neutral * 0.82 + seamless * 0.18
    target_mean = 0.405
    seamless *= target_mean / max(float(np.mean(luminance)), 0.001)
    return np.clip(seamless, 0.06, 0.78)


def compile_normal(albedo: np.ndarray) -> np.ndarray:
    luminance = np.sum(albedo * np.array([0.2126, 0.7152, 0.0722]), axis=2)
    height_image = Image.fromarray(np.uint8(np.clip(luminance * 255.0, 0, 255)))
    height = np.asarray(
        height_image.filter(ImageFilter.GaussianBlur(radius=0.75)),
        dtype=np.float32,
    ) / 255.0
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    normal = np.stack((-dx * 5.2, -dy * 5.2, np.ones_like(height)), axis=2)
    normal /= np.maximum(np.linalg.norm(normal, axis=2, keepdims=True), 1e-6)
    return np.clip(normal * 0.5 + 0.5, 0.0, 1.0)


def value_noise(width: int, height: int, rng: np.random.Generator) -> np.ndarray:
    coarse = rng.uniform(0.0, 1.0, size=(16, 64)).astype(np.float32)
    image = Image.fromarray(np.uint8(coarse * 255.0))
    image = image.resize((width, height), Image.Resampling.BICUBIC)
    image = image.filter(ImageFilter.GaussianBlur(radius=7.0))
    return np.asarray(image, dtype=np.float32) / 255.0


def compile_macro(width: int = 2048, height: int = 256) -> np.ndarray:
    rng = np.random.default_rng(SEED)
    u = np.linspace(0.0, 1.0, width, endpoint=True)[None, :]
    v = np.linspace(-0.5, 0.5, height, endpoint=True)[:, None]
    noise = value_noise(width, height, rng)

    # Two restrained tire-polish lanes, strongest near the touchdown thirds.
    tire_lanes = (
        np.exp(-((v - 0.095) / 0.055) ** 2)
        + np.exp(-((v + 0.095) / 0.055) ** 2)
    )
    touchdown = (
        np.exp(-((u - 0.18) / 0.14) ** 2)
        + np.exp(-((u - 0.82) / 0.14) ** 2)
    )
    center_polish = np.clip(tire_lanes * (0.10 + 0.46 * touchdown), 0.0, 1.0)

    # A few irregular, low-contrast maintenance areas. They are unique in the
    # runway coordinate frame and cannot form the rejected slab pattern.
    repairs = np.zeros((height, width), dtype=np.float32)
    for center_u, center_v, length, breadth, strength in (
        (0.29, -0.20, 0.075, 0.12, 0.34),
        (0.54, 0.24, 0.045, 0.10, 0.25),
        (0.71, -0.27, 0.060, 0.08, 0.29),
    ):
        repairs += strength * np.exp(
            -((u - center_u) / length) ** 4
            -((v - center_v) / breadth) ** 4
        )
    repairs = np.clip(repairs * (0.82 + noise * 0.36), 0.0, 1.0)

    edge_weathering = smoothstep((np.abs(v) - 0.36) / 0.14)
    roughness = 0.78 + (noise - 0.5) * 0.055
    roughness -= center_polish * 0.095
    roughness += repairs * 0.055 + edge_weathering * 0.045
    roughness = np.clip(roughness, 0.60, 0.91)

    # R: tire polish/wear, G: perceptual roughness, B: repairs/weathering.
    packed = np.stack(
        (
            center_polish,
            roughness,
            np.clip(repairs * 0.72 + edge_weathering * 0.34, 0.0, 1.0),
        ),
        axis=2,
    )
    return packed


def save_rgb(path: Path, values: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image = Image.fromarray(np.uint8(np.clip(values * 255.0 + 0.5, 0, 255)))
    image.save(path, format="PNG", optimize=True, compress_level=9)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=SOURCE_PATH)
    parser.add_argument("--output-dir", type=Path, default=OUTPUT_DIR)
    args = parser.parse_args()

    source = args.source.resolve()
    output = args.output_dir.resolve()
    if not source.is_file():
        raise FileNotFoundError(source)

    albedo = make_seamless(Image.open(source), 1024)
    normal = compile_normal(albedo)
    macro = compile_macro()

    albedo_path = output / "AirportCoreRunway_Albedo.png"
    normal_path = output / "AirportCoreRunway_Normal.png"
    macro_path = output / "AirportCoreRunway_MRAO.png"
    save_rgb(albedo_path, albedo)
    save_rgb(normal_path, normal)
    save_rgb(macro_path, macro)

    result = {
        "schemaVersion": 2,
        "generator": record(Path(__file__).resolve()),
        "seed": SEED,
        "source": record(source),
        "outputs": [record(albedo_path), record(normal_path), record(macro_path)],
        "runtime": {
            "albedoAndroid": "1024 ASTC 6x6, mipmapped, trilinear, anisotropy 8",
            "normalAndroid": "1024 ASTC 6x6, mipmapped, trilinear, anisotropy 8",
            "macroAndroid": "1024x128 ASTC 6x6, mipmapped, clamped",
            "additionalRenderers": 0,
            "additionalMaterials": 0,
            "additionalTextureSamplers": 0,
            "transparentPasses": 0,
        },
    }
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
