#!/usr/bin/env python3
"""Generate a deterministic Quest-bounded runway albedo and packed smoothness atlas."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUTPUT = ROOT / "QuestFlight" / "Assets" / "Production" / "Environment" / "AirportKit"
DEFAULT_MANIFEST = ROOT / "tools" / "kbdu_airport_art" / "kbdu_runway_art_manifest.json"
SEED = 20260713


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def record(path: Path) -> dict:
    return {"path": path.relative_to(ROOT).as_posix(), "bytes": path.stat().st_size, "sha256": sha256(path)}


def value_noise(size: int, coarse: int, rng: random.Random) -> Image.Image:
    grid = Image.new("L", (coarse, coarse))
    grid.putdata([rng.randrange(70, 186) for _ in range(coarse * coarse)])
    return grid.resize((size, size), Image.Resampling.BICUBIC)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--preview-dir", type=Path, default=None)
    args = parser.parse_args()
    output_dir = args.output_dir.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    rng = random.Random(SEED)
    size = 1024

    broad = value_noise(size, 18, rng)
    medium = value_noise(size, 73, rng)
    fine = Image.effect_noise((size, size), 18.0)
    albedo = Image.new("RGB", (size, size))
    broad_pixels, medium_pixels, fine_pixels = broad.load(), medium.load(), fine.load()
    output = albedo.load()
    for y in range(size):
        joint = 0.93 if y % 128 < 3 else 1.0
        for x in range(size):
            variation = (broad_pixels[x, y] - 128) * 0.065 + (medium_pixels[x, y] - 128) * 0.035 + (fine_pixels[x, y] - 128) * 0.022
            edge_wear = 1.0 + 0.055 * min(1.0, abs(x - size / 2) / (size * 0.42))
            base = (50.0 + variation) * joint * edge_wear
            output[x, y] = (max(28, min(78, round(base * 0.98))), max(29, min(80, round(base))), max(31, min(83, round(base * 1.035))))

    wear = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(wear)
    for center_fraction in (0.42, 0.58):
        center = int(size * center_fraction)
        for _ in range(28):
            x = center + rng.randint(-24, 24)
            y0 = rng.randint(-120, size - 80)
            length = rng.randint(120, 480)
            shade = rng.randint(38, 90)
            draw.line((x, y0, x + rng.randint(-8, 8), y0 + length), fill=shade, width=rng.randint(2, 8))
    wear = wear.filter(ImageFilter.GaussianBlur(3.5))
    dark = Image.new("RGB", (size, size), (24, 25, 27))
    albedo = Image.composite(dark, albedo, wear)

    crack_mask = Image.new("L", (size, size), 0)
    cracks = ImageDraw.Draw(crack_mask)
    for _ in range(42):
        x, y = rng.randrange(size), rng.randrange(size)
        points = [(x, y)]
        for _ in range(rng.randint(2, 6)):
            x = max(0, min(size - 1, x + rng.randint(-38, 38)))
            y = max(0, min(size - 1, y + rng.randint(16, 70)))
            points.append((x, y))
        cracks.line(points, fill=rng.randint(80, 145), width=rng.choice((1, 1, 2)))
    albedo = Image.composite(Image.new("RGB", (size, size), (20, 21, 22)), albedo, crack_mask)

    smoothness = Image.new("L", (size, size), 46)
    smoothness = Image.composite(Image.new("L", (size, size), 82), smoothness, wear)
    smoothness = Image.composite(Image.new("L", (size, size), 28), smoothness, crack_mask)
    packed = Image.merge("RGBA", (Image.new("L", (size, size), 0), Image.new("L", (size, size), 255), Image.new("L", (size, size), 255), smoothness))

    albedo_path = output_dir / "KBDU_RunwaySurface_Albedo.jpg"
    packed_path = output_dir / "KBDU_RunwaySurface_MetallicSmoothness.png"
    albedo.save(albedo_path, format="JPEG", quality=92, subsampling=0, optimize=True)
    packed.save(packed_path, format="PNG", optimize=True, compress_level=9)
    manifest = {
        "schema_version": 1,
        "creator": "T2 QuestFlight deterministic project-authored generator",
        "license": "Project-owned original procedural pixels; no external texture input",
        "seed": SEED,
        "generator": record(Path(__file__).resolve()),
        "outputs": [record(albedo_path), record(packed_path)],
        "content": ["multi-scale asphalt variation", "bounded centerline rubber/wear", "transverse joints", "sparse cracks", "packed metallic/smoothness"],
        "integration": {
            "shared_material": "Production_Runway_Asphalt",
            "preserve_existing_controlled_depth_markings": True,
            "mesh_or_collider_changes": False,
            "z_offset_changes": False,
            "android_format": "ASTC_6x6 with mipmaps",
        },
    }
    args.manifest.resolve().write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    if args.preview_dir:
        preview_dir = args.preview_dir.resolve()
        preview_dir.mkdir(parents=True, exist_ok=True)
        albedo.save(preview_dir / "kbdu_runway_surface_preview.jpg", quality=94, subsampling=0)
    print(json.dumps(manifest, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
