#!/usr/bin/env python3
"""Create deterministic Quest-ready albedo/normal/MRAO atlas derivatives."""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib

import numpy as np
from PIL import Image, ImageFilter


def digest(path: pathlib.Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def save_png(image: Image.Image, path: pathlib.Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG", optimize=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=pathlib.Path, required=True)
    parser.add_argument("--output-dir", type=pathlib.Path, required=True)
    parser.add_argument("--manifest", type=pathlib.Path, required=True)
    args = parser.parse_args()

    source = Image.open(args.source).convert("RGB")
    albedo = source.resize((1024, 1024), Image.Resampling.LANCZOS)
    # Keep mathematical atlas boundaries exact after resizing.
    pixels = np.asarray(albedo, dtype=np.float32) / 255.0
    luminance = pixels[..., 0] * 0.2126 + pixels[..., 1] * 0.7152 + pixels[..., 2] * 0.0722
    height = np.asarray(Image.fromarray(np.uint8(luminance * 255)).filter(ImageFilter.GaussianBlur(1.0)), dtype=np.float32) / 255.0
    dy, dx = np.gradient(height)
    strength = 3.0
    normals = np.dstack((-dx * strength, -dy * strength, np.ones_like(dx)))
    normals /= np.maximum(np.linalg.norm(normals, axis=2, keepdims=True), 1e-5)
    normal_rgb = np.uint8(np.clip(normals * 0.5 + 0.5, 0.0, 1.0) * 255)

    # MRAO: context surfaces are nonmetallic; roughness derives conservatively from
    # local luminance variation; AO remains neutral because geometry supplies it.
    mean = np.asarray(Image.fromarray(np.uint8(luminance * 255)).filter(ImageFilter.GaussianBlur(4.0)), dtype=np.float32) / 255.0
    detail = np.clip(np.abs(luminance - mean) * 2.0, 0.0, 1.0)
    roughness = np.clip(0.60 + detail * 0.28, 0.0, 1.0)
    # Window cells (row 4, columns 1/2) remain smooth and receive shader reflections.
    roughness[768:1024, 0:512] = 0.24
    mrao = np.dstack((np.zeros_like(roughness), roughness, np.full_like(roughness, 0.92)))

    albedo_path = args.output_dir / "FullMapBuildingAtlas_Albedo.png"
    normal_path = args.output_dir / "FullMapBuildingAtlas_Normal.png"
    mrao_path = args.output_dir / "FullMapBuildingAtlas_MRAO.png"
    save_png(albedo, albedo_path)
    save_png(Image.fromarray(normal_rgb, "RGB"), normal_path)
    save_png(Image.fromarray(np.uint8(np.clip(mrao, 0.0, 1.0) * 255), "RGB"), mrao_path)

    manifest = {
        "schema_version": 1,
        "generator": "tools/full_map_buildings/prepare_material_atlas.py",
        "source": str(args.source).replace("\\", "/"),
        "source_sha256": digest(args.source),
        "source_kind": "project-directed OpenAI image generation; no external copyrighted photograph",
        "derivatives": {
            albedo_path.name: digest(albedo_path),
            normal_path.name: digest(normal_path),
            mrao_path.name: digest(mrao_path),
        },
        "atlas_grid": [4, 4],
        "runtime_license": "Project-authored derivative for personal QuestFlight use",
    }
    args.manifest.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
