"""Create deterministic feather-scale maps from the project-owned eagle atlas.

This preserves every UV island and adds only bounded, high-frequency plumage
variation. It is intentionally an offline authoring step; the Quest runtime
receives one 1K albedo, one 1K normal, and one packed 1K metallic/smoothness map.
"""

from __future__ import annotations

import argparse
import hashlib
import math
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--albedo", required=True)
    parser.add_argument("--normal", required=True)
    parser.add_argument("--packed", required=True)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    albedo_path = Path(args.albedo)
    normal_path = Path(args.normal)
    packed_path = Path(args.packed)
    source = Image.open(albedo_path).convert("RGBA")
    rgba = np.asarray(source, dtype=np.float32) / 255.0
    height, width, _ = rgba.shape
    luminance = rgba[..., :3] @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)

    # Deterministic feather-barb modulation. Restrict it to brown/dark atlas
    # pixels so beak, eye, feet, and unused atlas space remain unchanged.
    maximum = rgba[..., :3].max(axis=2)
    minimum = rgba[..., :3].min(axis=2)
    plumage = (luminance < 0.63) & (maximum - minimum > 0.035) & (rgba[..., 3] > 0.5)
    yy, xx = np.mgrid[0:height, 0:width]
    primary = np.sin(xx * 0.115 + yy * 0.041)
    secondary = np.sin(xx * -0.037 + yy * 0.092 + 1.7)
    variation = (primary * 0.55 + secondary * 0.45) * 0.055
    enhanced = rgba.copy()
    enhanced[..., :3][plumage] *= (1.0 + variation[plumage, None])
    enhanced[..., :3] = np.clip(enhanced[..., :3], 0.0, 1.0)
    Image.fromarray((enhanced * 255.0 + 0.5).astype(np.uint8), "RGBA").save(albedo_path)

    # Combine atlas luminance with the authored feather pattern to produce a
    # restrained tangent-space normal. At the encounter distance this supplies
    # feather breakup without extra geometry or transparency.
    feather_height = luminance + variation * plumage.astype(np.float32)
    gy, gx = np.gradient(feather_height)
    strength = 2.1
    nx = -gx * strength
    ny = -gy * strength
    nz = np.ones_like(nx)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack((nx / length, ny / length, nz / length), axis=2)
    normal = normal * 0.5 + 0.5
    normal_image = np.zeros((height, width, 4), dtype=np.uint8)
    normal_image[..., :3] = (normal * 255.0 + 0.5).astype(np.uint8)
    normal_image[..., 3] = 255
    Image.fromarray(normal_image, "RGBA").save(normal_path)

    # Unity Standard uses RGB metallic and alpha smoothness. Eagles are
    # dielectric; a low, varied smoothness keeps highlights natural.
    packed = np.zeros((height, width, 4), dtype=np.uint8)
    smoothness = np.where(plumage, 0.16 + 0.035 * (secondary * 0.5 + 0.5), 0.12)
    packed[..., 3] = np.clip(smoothness * 255.0, 0, 255).astype(np.uint8)
    Image.fromarray(packed, "RGBA").save(packed_path)

    for path in (albedo_path, normal_path, packed_path):
        digest = hashlib.sha256(path.read_bytes()).hexdigest().upper()
        print(f"{path}: {path.stat().st_size} bytes sha256={digest}")


if __name__ == "__main__":
    main()
