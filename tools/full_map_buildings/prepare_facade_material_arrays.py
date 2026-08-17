#!/usr/bin/env python3
"""Build compact, physically scaled facade texture-array flipbooks.

High-resolution CC0 sources are cached outside Git.  Only the 4x4, 512-pixel
runtime slices and their source/hash manifest enter the Unity project.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import pathlib

import numpy as np
import requests
from PIL import Image, ImageEnhance


SLICE_SIZE = 512
GRID = 4

SLICES = [
    # name, Poly Haven asset, RGB multiplier/target, full palette recolor
    ("painted_wood_siding", "weathered_plank_siding", (0.68, 0.61, 0.48), True),
    ("gray_wood_siding", "weathered_plank_siding", (0.46, 0.49, 0.50), True),
    ("cement_board", "weathered_plank_siding", (0.72, 0.70, 0.63), True),
    ("red_brick", "brick_wall_001", (1.00, 0.88, 0.82), False),
    ("tan_brick", "yellow_brick", (0.96, 0.93, 0.83), False),
    ("beige_stucco", "beige_wall_001", (1.00, 0.98, 0.92), False),
    ("gray_stucco", "grey_plaster", (0.96, 0.98, 1.00), False),
    ("concrete_block", "concrete_block_wall", (0.94, 0.96, 0.98), False),
    ("precast_concrete", "precast_concrete_wall", (0.98, 0.98, 0.96), False),
    ("white_corrugated_metal", "corrugated_iron", (1.12, 1.12, 1.08), False),
    ("blue_corrugated_metal", "corrugated_iron", (0.48, 0.70, 0.82), False),
    ("moss_rock_stone", "stone_wall_04", (0.90, 0.90, 0.82), False),
    ("painted_block", "concrete_block_wall", (0.82, 0.84, 0.80), False),
    ("dark_utility_metal", "corrugated_iron", (0.42, 0.44, 0.42), False),
    ("cedar_or_log", "weathered_plank_siding", (0.82, 0.62, 0.43), False),
    ("light_vinyl_siding", "weathered_plank_siding", (0.76, 0.75, 0.69), True),
]


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def download(session: requests.Session, files: dict, asset: str, channel: str,
             raw_dir: pathlib.Path) -> tuple[pathlib.Path, str]:
    candidates = files[channel]["1k"]
    entry = candidates.get("png") or candidates.get("jpg")
    if not entry:
        raise RuntimeError(f"{asset} has no 1K PNG/JPG {channel} map")
    suffix = pathlib.Path(entry["url"]).suffix
    path = raw_dir / f"{asset}_{channel}{suffix}"
    if not path.exists():
        response = session.get(entry["url"], timeout=120)
        response.raise_for_status()
        path.write_bytes(response.content)
    payload = path.read_bytes()
    return path, sha256_bytes(payload)


def resize_rgb(path: pathlib.Path) -> Image.Image:
    with Image.open(path) as source:
        return source.convert("RGB").resize((SLICE_SIZE, SLICE_SIZE), Image.Resampling.LANCZOS)


def tint(image: Image.Image, multiplier: tuple[float, float, float], recolor: bool) -> Image.Image:
    pixels = np.asarray(image, dtype=np.float32) / 255.0
    luminance = pixels[..., 0] * 0.2126 + pixels[..., 1] * 0.7152 + pixels[..., 2] * 0.0722
    if recolor:
        # Painted siding should retain the photographed plank relief and grime,
        # not inherit the source timber's brown pigment.  Normalize luminance
        # around its robust median and apply the documented paint palette.
        median = float(np.median(luminance))
        relief = np.clip(luminance / max(median, 0.08), 0.60, 1.38)[..., None]
        target = np.asarray(multiplier, dtype=np.float32)
        chroma_detail = (pixels - luminance[..., None]) * 0.06
        colored = np.clip(target * relief + chroma_detail, 0.0, 1.0)
        result = Image.fromarray(np.uint8(colored * 255.0))
        return ImageEnhance.Contrast(result).enhance(0.94)
    colored = np.clip(pixels * np.asarray(multiplier, dtype=np.float32), 0.0, 1.0)
    # Preserve photographed high-frequency contrast while moving the broad palette.
    colored_luminance = colored[..., 0] * 0.2126 + colored[..., 1] * 0.7152 + colored[..., 2] * 0.0722
    scale = np.divide(luminance, np.maximum(colored_luminance, 0.05))[..., None]
    colored = np.clip(colored * (0.72 + scale * 0.28), 0.0, 1.0)
    result = Image.fromarray(np.uint8(colored * 255.0))
    return ImageEnhance.Contrast(result).enhance(0.96)


def mrao_from_arm(path: pathlib.Path) -> Image.Image:
    arm = np.asarray(resize_rgb(path), dtype=np.uint8)
    # Poly Haven ARM is AO, roughness, metallic. QuestFlight uses M, R, AO.
    return Image.fromarray(np.dstack((arm[..., 2], arm[..., 1], arm[..., 0])))


def flipbook(images: list[Image.Image]) -> Image.Image:
    output = Image.new("RGB", (SLICE_SIZE * GRID, SLICE_SIZE * GRID))
    for index, image in enumerate(images):
        output.paste(image, ((index % GRID) * SLICE_SIZE, (index // GRID) * SLICE_SIZE))
    return output


def build(args: argparse.Namespace) -> dict:
    args.raw_dir.mkdir(parents=True, exist_ok=True)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    session = requests.Session()
    asset_catalog = session.get("https://api.polyhaven.com/assets?t=textures", timeout=60).json()
    file_catalog: dict[str, dict] = {}
    source_records: dict[str, dict] = {}
    albedo_slices: list[Image.Image] = []
    normal_slices: list[Image.Image] = []
    mrao_slices: list[Image.Image] = []
    slice_records = []
    for index, (name, asset, multiplier, recolor) in enumerate(SLICES):
        if asset not in file_catalog:
            file_catalog[asset] = session.get(f"https://api.polyhaven.com/files/{asset}", timeout=60).json()
        files = file_catalog[asset]
        paths = {}
        hashes = {}
        for channel in ("Diffuse", "nor_gl", "arm"):
            paths[channel], hashes[channel] = download(session, files, asset, channel, args.raw_dir)
        albedo_slices.append(tint(resize_rgb(paths["Diffuse"]), multiplier, recolor))
        normal_slices.append(resize_rgb(paths["nor_gl"]))
        mrao_slices.append(mrao_from_arm(paths["arm"]))
        dimensions_mm = asset_catalog[asset].get("dimensions") or [2000.0, 2000.0]
        meters = [round(float(value) / 1000.0, 4) for value in dimensions_mm[:2]]
        source_records[asset] = {
            "name": asset_catalog[asset].get("name", asset),
            "license": "CC0",
            "source": f"https://polyhaven.com/a/{asset}",
            "dimensions_meters": meters,
            "authors": asset_catalog[asset].get("authors", {}),
            "download_sha256": hashes,
        }
        slice_records.append({
            "index": index,
            "name": name,
            "source_asset": asset,
            "meters_per_tile": meters,
            "albedo_multiplier": multiplier,
            "full_palette_recolor": recolor,
        })

    outputs = {
        "FullMapBuildingFacadeArray_Albedo.png": flipbook(albedo_slices),
        "FullMapBuildingFacadeArray_Normal.png": flipbook(normal_slices),
        "FullMapBuildingFacadeArray_MRAO.png": flipbook(mrao_slices),
    }
    output_hashes = {}
    for filename, image in outputs.items():
        path = args.output_dir / filename
        image.save(path, optimize=True)
        output_hashes[filename] = sha256_bytes(path.read_bytes())

    manifest = {
        "contract": "physically-scaled-facade-array-v1",
        "runtime_slice_size": SLICE_SIZE,
        "flipbook_grid": [GRID, GRID],
        "slice_count": len(SLICES),
        "channels": {
            "albedo": "sRGB photographic diffuse",
            "normal": "Poly Haven OpenGL tangent-space normal",
            "mrao": "R=metallic, G=roughness, B=ambient occlusion",
        },
        "slices": slice_records,
        "sources": source_records,
        "runtime_derivative_sha256": output_hashes,
        "raw_sources_committed": False,
    }
    manifest_path = args.output_dir / "FullMapBuildingFacadeArray_SOURCE_MANIFEST.json"
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw-dir", type=pathlib.Path, required=True)
    parser.add_argument("--output-dir", type=pathlib.Path, required=True)
    args = parser.parse_args()
    print(json.dumps(build(args), indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
