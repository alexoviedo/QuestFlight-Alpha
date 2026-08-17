#!/usr/bin/env python3
"""Build the warm Boulder facade arrays and one compact semantic detail atlas."""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib

import numpy as np
import requests
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter


SLICE_SIZE = 512
GRID = 4
DETAIL_SIZE = 1024
DETAIL_CELL = DETAIL_SIZE // GRID

# Every slice has a distinct photographic source. The palette is deliberately
# warm/neutral; no blue facade family exists without actual color evidence.
SLICES = [
    ("warm_white_wood_siding", "white_planks_clean", (0.78, 0.72, 0.62), True),
    ("weathered_wood_siding", "weathered_plank_siding", (0.96, 0.89, 0.78), False),
    ("warm_fiber_cement", "synthetic_wood", (0.68, 0.64, 0.55), True),
    ("red_brick", "brick_wall_001", (1.00, 0.90, 0.84), False),
    ("brown_brick", "brown_brick_02", (1.00, 0.93, 0.84), False),
    ("tan_brick", "yellow_brick", (0.92, 0.86, 0.76), False),
    ("warm_beige_stucco", "beige_wall_001", (1.00, 0.96, 0.87), False),
    ("cream_stucco", "white_stucco", (0.91, 0.85, 0.74), False),
    ("warm_concrete_block", "concrete_block_wall", (0.52, 0.47, 0.40), True),
    ("warm_precast_concrete", "precast_concrete_wall", (0.66, 0.60, 0.52), True),
    ("galvanized_corrugated_metal", "corrugated_iron", (0.66, 0.64, 0.59), True),
    ("tan_metal_panel", "corrugated_iron_02", (0.54, 0.47, 0.37), True),
    ("boulder_sandstone", "old_sandstone_02", (1.00, 0.96, 0.88), False),
    ("warm_painted_block", "painted_brick", (0.64, 0.57, 0.46), True),
    ("charcoal_metal_panel", "box_profile_metal_sheet", (0.24, 0.23, 0.21), True),
    ("cedar_siding", "weathered_brown_planks", (0.94, 0.79, 0.62), False),
]


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def download(session: requests.Session, files: dict, asset: str, channel: str,
             raw_dir: pathlib.Path) -> tuple[pathlib.Path, str]:
    entry = files[channel]["1k"].get("png") or files[channel]["1k"].get("jpg")
    if not entry:
        raise RuntimeError(f"{asset} has no 1K {channel} map")
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
    target = np.asarray(multiplier, dtype=np.float32)
    if recolor:
        median = float(np.median(luminance))
        relief = np.clip(luminance / max(median, 0.08), 0.62, 1.34)[..., None]
        chroma = (pixels - luminance[..., None]) * 0.04
        colored = np.clip(target * relief + chroma, 0.0, 1.0)
    else:
        colored = np.clip(pixels * target, 0.0, 1.0)
        colored_luminance = np.maximum(
            colored[..., 0] * 0.2126 + colored[..., 1] * 0.7152 + colored[..., 2] * 0.0722,
            0.05,
        )
        colored *= (0.76 + (luminance / colored_luminance)[..., None] * 0.24)
        colored = np.clip(colored, 0.0, 1.0)
    result = Image.fromarray(np.uint8(colored * 255.0))
    return ImageEnhance.Contrast(result).enhance(0.94)


def mrao_from_arm(path: pathlib.Path) -> Image.Image:
    arm = np.asarray(resize_rgb(path), dtype=np.uint8)
    return Image.fromarray(np.dstack((arm[..., 2], arm[..., 1], arm[..., 0])))


def flipbook(images: list[Image.Image]) -> Image.Image:
    output = Image.new("RGB", (SLICE_SIZE * GRID, SLICE_SIZE * GRID))
    for index, image in enumerate(images):
        output.paste(image, ((index % GRID) * SLICE_SIZE, (index // GRID) * SLICE_SIZE))
    return output


def detail_cell(index: int, door: bool) -> Image.Image:
    """Project-authored opaque aperture response; alpha is restrained edge coverage."""
    size = DETAIL_CELL
    image = Image.new("RGBA", (size, size), (24, 25, 23, 255))
    draw = ImageDraw.Draw(image, "RGBA")
    # Subtle warm lower reflection and cooler sky/header response without a blue pane.
    for y in range(size):
        t = y / max(1, size - 1)
        color = (
            int(30 + 20 * (1.0 - t)),
            int(31 + 18 * (1.0 - t)),
            int(29 + 16 * (1.0 - t)),
            255,
        )
        draw.line((0, y, size, y), fill=color)
    if door:
        if index == 2:
            draw.rectangle((18, 18, size - 18, size - 18), fill=(78, 74, 65, 255), outline=(42, 40, 36, 255), width=9)
            for y in (size // 3, 2 * size // 3):
                draw.line((22, y, size - 22, y), fill=(48, 47, 43, 255), width=5)
        else:
            draw.rectangle((24, 12, size - 24, size - 12), fill=(68, 61, 51, 255), outline=(39, 36, 31, 255), width=8)
            draw.ellipse((size - 54, size // 2 - 6, size - 42, size // 2 + 6), fill=(190, 169, 118, 255))
    else:
        frame = (122, 115, 101, 255) if index in (0, 3) else (78, 77, 72, 255)
        width = 7 if index != 1 else 5
        draw.rectangle((8, 8, size - 8, size - 8), outline=frame, width=width)
        if index == 0:  # residential paired sash
            draw.line((size // 2, 10, size // 2, size - 10), fill=frame, width=5)
            draw.line((10, size // 2, size - 10, size // 2), fill=frame, width=4)
        elif index == 1:  # commercial mullions
            for x in (size // 3, 2 * size // 3):
                draw.line((x, 8, x, size - 8), fill=frame, width=4)
        elif index == 2:  # industrial clerestory
            draw.line((10, size // 2, size - 10, size // 2), fill=frame, width=4)
        elif index == 3:  # institutional grid
            draw.line((size // 2, 8, size // 2, size - 8), fill=frame, width=4)
            for y in (size // 3, 2 * size // 3):
                draw.line((8, y, size - 8, y), fill=frame, width=4)
        # Broad reflected headliner/cloud band, intentionally blurred and bounded.
        highlight = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        hdraw = ImageDraw.Draw(highlight, "RGBA")
        hdraw.polygon(((18, 42), (size - 18, 18), (size - 18, 64), (18, 92)), fill=(220, 213, 194, 44))
        image = Image.alpha_composite(image, highlight.filter(ImageFilter.GaussianBlur(8)))
    return image


def build_detail_atlas() -> Image.Image:
    atlas = Image.new("RGBA", (DETAIL_SIZE, DETAIL_SIZE), (0, 0, 0, 0))
    for style in range(6):
        index = style
        atlas.paste(detail_cell(style, False), ((index % GRID) * DETAIL_CELL, (index // GRID) * DETAIL_CELL))
        index = 8 + style
        atlas.paste(detail_cell(style, True), ((index % GRID) * DETAIL_CELL, (index // GRID) * DETAIL_CELL))
    return atlas


def build(args: argparse.Namespace) -> dict:
    args.raw_dir.mkdir(parents=True, exist_ok=True)
    args.output_dir.mkdir(parents=True, exist_ok=True)
    session = requests.Session()
    assets = session.get("https://api.polyhaven.com/assets?t=textures", timeout=60).json()
    file_catalog: dict[str, dict] = {}
    source_records, slice_records = {}, []
    albedo_slices, normal_slices, mrao_slices = [], [], []
    for index, (name, asset, multiplier, recolor) in enumerate(SLICES):
        files = file_catalog.setdefault(
            asset, session.get(f"https://api.polyhaven.com/files/{asset}", timeout=60).json())
        paths, hashes = {}, {}
        for channel in ("Diffuse", "nor_gl", "arm"):
            paths[channel], hashes[channel] = download(session, files, asset, channel, args.raw_dir)
        albedo_slices.append(tint(resize_rgb(paths["Diffuse"]), multiplier, recolor))
        normal_slices.append(resize_rgb(paths["nor_gl"]))
        mrao_slices.append(mrao_from_arm(paths["arm"]))
        dimensions_mm = assets[asset].get("dimensions") or [2000.0, 2000.0]
        meters = [round(float(value) / 1000.0, 4) for value in dimensions_mm[:2]]
        source_records[asset] = {
            "name": assets[asset].get("name", asset),
            "license": "CC0",
            "source": f"https://polyhaven.com/a/{asset}",
            "dimensions_meters": meters,
            "authors": assets[asset].get("authors", {}),
            "download_sha256": hashes,
        }
        slice_records.append({
            "index": index, "name": name, "source_asset": asset,
            "meters_per_tile": meters, "albedo_multiplier": multiplier,
            "full_palette_recolor": recolor,
        })

    outputs = {
        "FullMapBuildingFacadeArray_Albedo.png": flipbook(albedo_slices),
        "FullMapBuildingFacadeArray_Normal.png": flipbook(normal_slices),
        "FullMapBuildingFacadeArray_MRAO.png": flipbook(mrao_slices),
        "FullMapBuildingFacadeDetailAtlas.png": build_detail_atlas(),
    }
    output_hashes = {}
    for filename, image in outputs.items():
        path = args.output_dir / filename
        image.save(path, optimize=True)
        output_hashes[filename] = sha256_bytes(path.read_bytes())

    manifest = {
        "contract": "boulder-warm-facade-array-v2",
        "runtime_slice_size": SLICE_SIZE,
        "flipbook_grid": [GRID, GRID],
        "slice_count": len(SLICES),
        "detail_atlas": {
            "size": DETAIL_SIZE, "cell_size": DETAIL_CELL,
            "window_cells": list(range(6)), "door_cells": list(range(8, 14)),
            "source": "project-authored deterministic aperture response",
            "license": "project-owned",
        },
        "channels": {
            "albedo": "sRGB photographic diffuse",
            "normal": "Poly Haven OpenGL tangent-space normal",
            "mrao": "R=metallic, G=roughness, B=ambient occlusion",
            "detail": "sRGB opaque window/door response with alpha edge coverage",
        },
        "slices": slice_records,
        "sources": source_records,
        "runtime_derivative_sha256": output_hashes,
        "raw_sources_committed": False,
        "explicit_blue_facade_family": False,
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
