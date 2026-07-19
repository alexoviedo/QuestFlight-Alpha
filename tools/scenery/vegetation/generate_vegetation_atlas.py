#!/usr/bin/env python3
"""Generate the project-owned 1K alpha-test vegetation atlas.

The atlas contains one opaque bark cell, five foliage-cutout cells, and two
top/oblique upper-canopy cells with irregular alpha-clipped silhouettes. All
shapes are deterministic vector primitives rendered at 2x and downsampled;
there are no downloaded, scanned, or unclear-rights source images.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw


ATLAS_SIZE = 1024
SUPERSAMPLE = 2
CELL_WIDTH = 256
CELL_HEIGHT = 512
SCHEMA = "questflight.aerial-scenery-v1.vegetation-atlas.v1"
REPO_ROOT = Path(__file__).resolve().parents[3]


def portable_path(path: Path) -> str:
    resolved = path.resolve()
    try:
        return resolved.relative_to(REPO_ROOT).as_posix()
    except ValueError:
        return resolved.as_posix()

CELLS: dict[str, tuple[int, int]] = {
    "bark": (0, 0),
    "cottonwood": (1, 0),
    "willow_riparian": (2, 0),
    "general_deciduous": (3, 0),
    "sparse_conifer": (0, 1),
    "regional_shrub": (1, 1),
    "coarse_canopy": (2, 1),
    "coarse_canopy_b": (3, 1),
}

FOLIAGE: dict[str, dict[str, Any]] = {
    "cottonwood": {
        "seed": 713,
        "base": (104, 143, 56),
        "count": 68,
        "leaf_length": (25, 39),
        "leaf_width": (18, 31),
        "canopy": (0.45, 0.48),
        "lance": False,
    },
    "willow_riparian": {
        "seed": 1423,
        "base": (77, 137, 73),
        "count": 92,
        "leaf_length": (29, 47),
        "leaf_width": (7, 13),
        "canopy": (0.47, 0.47),
        "lance": True,
    },
    "general_deciduous": {
        "seed": 2153,
        "base": (76, 130, 45),
        "count": 78,
        "leaf_length": (23, 36),
        "leaf_width": (15, 27),
        "canopy": (0.47, 0.48),
        "lance": False,
    },
    "sparse_conifer": {
        "seed": 3259,
        "base": (62, 112, 72),
        "count": 118,
        "leaf_length": (31, 55),
        "leaf_width": (4, 8),
        "canopy": (0.45, 0.47),
        "lance": True,
    },
    "regional_shrub": {
        "seed": 4513,
        "base": (99, 119, 40),
        "count": 86,
        "leaf_length": (17, 28),
        "leaf_width": (12, 22),
        "canopy": (0.48, 0.42),
        "lance": False,
    },
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def clamp8(value: float) -> int:
    return max(0, min(255, round(value)))


def local_rect(cell: tuple[int, int]) -> tuple[int, int, int, int]:
    column, row_from_bottom = cell
    x0 = column * CELL_WIDTH * SUPERSAMPLE
    # Pillow rows are top-down while atlas UV rows are declared bottom-up.
    y0 = (1 - row_from_bottom) * CELL_HEIGHT * SUPERSAMPLE
    return x0, y0, x0 + CELL_WIDTH * SUPERSAMPLE, y0 + CELL_HEIGHT * SUPERSAMPLE


def uv_rect(cell: tuple[int, int], inset_pixels: float = 3.0) -> list[float]:
    column, row = cell
    return [
        round((column * CELL_WIDTH + inset_pixels) / ATLAS_SIZE, 8),
        round((row * CELL_HEIGHT + inset_pixels) / ATLAS_SIZE, 8),
        round(((column + 1) * CELL_WIDTH - inset_pixels) / ATLAS_SIZE, 8),
        round(((row + 1) * CELL_HEIGHT - inset_pixels) / ATLAS_SIZE, 8),
    ]


def transformed_polygon(
    center: tuple[float, float], length: float, width: float, angle: float, *, lance: bool
) -> list[tuple[float, float]]:
    samples = 14
    points: list[tuple[float, float]] = []
    for side in (1.0, -1.0):
        sequence = range(samples + 1) if side > 0 else range(samples, -1, -1)
        for index in sequence:
            t = index / samples
            along = (t - 0.5) * length
            profile = math.sin(math.pi * t)
            if lance:
                profile = profile ** 1.65
            else:
                profile *= 0.84 + 0.16 * math.sin(math.pi * t)
            across = side * width * 0.5 * profile
            # A small asymmetry keeps the procedural leaves from reading as icons.
            across += math.sin(t * math.tau) * width * 0.035
            cos_a, sin_a = math.cos(angle), math.sin(angle)
            points.append((
                center[0] + along * cos_a - across * sin_a,
                center[1] + along * sin_a + across * cos_a,
            ))
    return points


def draw_bark(color: Image.Image, surface: Image.Image) -> None:
    x0, y0, x1, y1 = local_rect(CELLS["bark"])
    color_draw = ImageDraw.Draw(color, "RGBA")
    surface_draw = ImageDraw.Draw(surface, "RGBA")
    color_draw.rectangle((x0, y0, x1 - 1, y1 - 1), fill=(91, 61, 35, 255))
    surface_draw.rectangle((x0, y0, x1 - 1, y1 - 1), fill=(128, 128, 202, 222))
    rng = random.Random(1019)
    width = x1 - x0
    height = y1 - y0
    for index in range(46):
        base_x = x0 + rng.uniform(0.02, 0.98) * width
        points = []
        for step in range(17):
            y = y0 + step / 16.0 * height
            x = base_x + math.sin(step * 0.88 + index * 1.7) * rng.uniform(2.0, 8.0) * SUPERSAMPLE
            points.append((x, y))
        shade = rng.randint(40, 73)
        line_width = rng.randint(1, 4) * SUPERSAMPLE
        color_draw.line(points, fill=(shade, max(28, shade - 13), max(17, shade - 24), 220), width=line_width)
        normal_x = clamp8(128 + (rng.random() - 0.5) * 34)
        surface_draw.line(points, fill=(normal_x, 128, rng.randint(185, 218), rng.randint(170, 215)), width=line_width)
    for index in range(13):
        cx = x0 + rng.uniform(0.08, 0.92) * width
        cy = y0 + rng.uniform(0.05, 0.95) * height
        rx = rng.uniform(5, 12) * SUPERSAMPLE
        ry = rng.uniform(2, 6) * SUPERSAMPLE
        color_draw.ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=(54, 34, 20, 235))
        surface_draw.ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=(128, 119, 190, 168))


def irregular_crown(
    center: tuple[float, float], radius_x: float, radius_y: float, phase: float, jitter: float
) -> list[tuple[float, float]]:
    """Return a deterministic crown outline without a billboard-hard ellipse edge."""
    points: list[tuple[float, float]] = []
    for index in range(24):
        angle = math.tau * index / 24.0
        radial = 1.0 + math.sin(angle * 3.0 + phase) * jitter + math.sin(angle * 7.0 - phase) * jitter * 0.42
        points.append((
            center[0] + math.cos(angle) * radius_x * radial,
            center[1] + math.sin(angle) * radius_y * radial,
        ))
    return points


def draw_coarse_canopy_variant(
    name: str,
    color: Image.Image,
    surface: Image.Image,
    *,
    seed: int,
    base: tuple[int, int, int],
) -> None:
    """Bake an alpha-clipped top/oblique grove cell from layered crown masses.

    Transparent perimeter gaps and internal openings are intentional.  They let
    the fixed upper-crown geometry keep an irregular tree-line silhouette from
    above without alpha blending, camera rotation, or extra geometry.
    """
    x0, y0, x1, y1 = local_rect(CELLS[name])
    color_draw = ImageDraw.Draw(color, "RGBA")
    surface_draw = ImageDraw.Draw(surface, "RGBA")
    color_draw.rectangle((x0, y0, x1 - 1, y1 - 1), fill=(0, 0, 0, 0))
    surface_draw.rectangle((x0, y0, x1 - 1, y1 - 1), fill=(128, 128, 224, 235))
    rng = random.Random(seed)
    width = x1 - x0
    height = y1 - y0
    center_x = x0 + width * 0.50
    center_y = y0 + height * 0.51

    # Large overlapping crowns establish volume; finer crowns break up scale
    # and prevent the flat, single-color "green dome" read of Vegetation V1.
    crown_records: list[tuple[float, float, float, float, float]] = []
    for index in range(92):
        angle = rng.random() * math.tau
        radial = math.sqrt(rng.random())
        cx = center_x + math.cos(angle) * width * 0.38 * radial
        cy = center_y + math.sin(angle) * height * 0.39 * radial
        scale = rng.uniform(0.055, 0.145) * (1.12 - radial * 0.24)
        crown_records.append((cy, cx, width * scale, height * scale * rng.uniform(0.42, 0.68), rng.random() * math.tau))

    # Back-to-front ordering gives a stable, restrained baked occlusion cue.
    for cy, cx, radius_x, radius_y, phase in sorted(crown_records):
        sun = 1.08 - ((cx - x0) / width) * 0.13 - ((cy - y0) / height) * 0.08
        variation = rng.uniform(0.82, 1.17)
        shade = sun * variation
        fill = (
            clamp8(base[0] * shade),
            clamp8(base[1] * shade),
            clamp8(base[2] * (0.96 + (shade - 1.0) * 0.55)),
            255,
        )
        outline = irregular_crown((cx, cy), radius_x, radius_y, phase, 0.11)
        color_draw.polygon(outline, fill=fill)
        nx = clamp8(128 + (center_x - cx) / width * 76)
        ny = clamp8(128 + (center_y - cy) / height * 72)
        roughness = clamp8(rng.uniform(188, 226))
        ao = clamp8(236 - radial * 20 if (radial := math.hypot((cx - center_x) / width, (cy - center_y) / height)) else 236)
        surface_draw.polygon(outline, fill=(nx, ny, roughness, ao))

    # Small deterministic sky gaps survive mipmapping through preserve-coverage
    # import settings and stop each lobe reading as one opaque plastic slab.
    for _ in range(17):
        angle = rng.random() * math.tau
        radial = math.sqrt(rng.random()) * 0.76
        cx = center_x + math.cos(angle) * width * 0.34 * radial
        cy = center_y + math.sin(angle) * height * 0.35 * radial
        radius_x = rng.uniform(0.010, 0.027) * width
        radius_y = rng.uniform(0.010, 0.026) * height
        color_draw.ellipse((cx - radius_x, cy - radius_y, cx + radius_x, cy + radius_y), fill=(0, 0, 0, 0))
        surface_draw.ellipse((cx - radius_x, cy - radius_y, cx + radius_x, cy + radius_y), fill=(128, 128, 224, 235))


def draw_coarse_canopy(color: Image.Image, surface: Image.Image) -> None:
    draw_coarse_canopy_variant(
        "coarse_canopy", color, surface, seed=6029, base=(94, 137, 58)
    )
    draw_coarse_canopy_variant(
        "coarse_canopy_b", color, surface, seed=7919, base=(79, 126, 64)
    )


def draw_foliage_cell(name: str, color: Image.Image, surface: Image.Image) -> None:
    spec = FOLIAGE[name]
    x0, y0, x1, y1 = local_rect(CELLS[name])
    color_draw = ImageDraw.Draw(color, "RGBA")
    surface_draw = ImageDraw.Draw(surface, "RGBA")
    rng = random.Random(int(spec["seed"]))
    width, height = x1 - x0, y1 - y0
    center = (x0 + width * 0.50, y0 + height * 0.51)
    base = tuple(spec["base"])

    # A restrained branchlet network remains visible through the leaf gaps.
    tips: list[tuple[float, float]] = []
    for branch_index in range(13):
        angle = -math.pi * 0.86 + branch_index / 12.0 * math.pi * 1.72
        radius_x = width * (0.27 + rng.random() * 0.18)
        radius_y = height * (0.27 + rng.random() * 0.18)
        tip = (center[0] + math.cos(angle) * radius_x, center[1] + math.sin(angle) * radius_y)
        tips.append(tip)
        color_draw.line((center, tip), fill=(65, 48, 27, 255), width=max(2, round(2.2 * SUPERSAMPLE)))
        surface_draw.line((center, tip), fill=(128, 128, 188, 150), width=max(2, round(2.2 * SUPERSAMPLE)))

    leaf_records = []
    for leaf_index in range(int(spec["count"])):
        angle = rng.random() * math.tau
        radial = math.sqrt(rng.random())
        rx = width * float(spec["canopy"][0])
        ry = height * float(spec["canopy"][1])
        cx = center[0] + math.cos(angle) * rx * radial
        cy = center[1] + math.sin(angle) * ry * radial
        length = rng.uniform(*spec["leaf_length"]) * SUPERSAMPLE
        leaf_width = rng.uniform(*spec["leaf_width"]) * SUPERSAMPLE
        orientation = angle + math.pi * 0.5 + rng.uniform(-0.72, 0.72)
        if name == "willow_riparian":
            orientation = rng.uniform(math.pi * 0.36, math.pi * 0.64)
        if name == "sparse_conifer":
            orientation = angle + rng.uniform(-0.28, 0.28)
        leaf_records.append((cy, cx, length, leaf_width, orientation, leaf_index))

    # Back-to-front ordering gives consistent, subtle self-occlusion.
    for cy, cx, length, leaf_width, orientation, leaf_index in sorted(leaf_records):
        polygon = transformed_polygon((cx, cy), length, leaf_width, orientation, lance=bool(spec["lance"]))
        variation = rng.uniform(0.78, 1.18)
        sun = 1.04 - ((cx - x0) / width) * 0.11 - ((cy - y0) / height) * 0.06
        fill = (
            clamp8(base[0] * variation * sun),
            clamp8(base[1] * variation * sun),
            clamp8(base[2] * variation * (1.03 - sun * 0.03)),
            255,
        )
        color_draw.polygon(polygon, fill=fill)
        nx = clamp8(128 + math.cos(orientation) * 23)
        ny = clamp8(128 - math.sin(orientation) * 18)
        roughness = 176 if name != "sparse_conifer" else 194
        ao = clamp8(229 - (1.0 - math.hypot(cx - center[0], cy - center[1]) / (max(rx, ry))) * 45)
        surface_draw.polygon(polygon, fill=(nx, ny, roughness, ao))
        # One-pixel center vein supplies readable close evidence without photo noise.
        vein_half = length * 0.34
        dx, dy = math.cos(orientation) * vein_half, math.sin(orientation) * vein_half
        color_draw.line((cx - dx, cy - dy, cx + dx, cy + dy), fill=(45, 73, 31, 150), width=SUPERSAMPLE)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--color", type=Path, required=True)
    parser.add_argument("--surface", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    render_size = ATLAS_SIZE * SUPERSAMPLE
    color = Image.new("RGBA", (render_size, render_size), (0, 0, 0, 0))
    surface = Image.new("RGBA", (render_size, render_size), (128, 128, 210, 255))
    draw_bark(color, surface)
    draw_coarse_canopy(color, surface)
    for name in FOLIAGE:
        draw_foliage_cell(name, color, surface)
    color = color.resize((ATLAS_SIZE, ATLAS_SIZE), Image.Resampling.LANCZOS)
    surface = surface.resize((ATLAS_SIZE, ATLAS_SIZE), Image.Resampling.LANCZOS)
    for path in (args.color, args.surface, args.report):
        path.parent.mkdir(parents=True, exist_ok=True)
    color.save(args.color, format="PNG", optimize=False, compress_level=9)
    surface.save(args.surface, format="PNG", optimize=False, compress_level=9)
    report = {
        "schema": SCHEMA,
        "status": "PROJECT_AUTHORED_DETERMINISTIC_ALPHA_TEST_ATLAS",
        "dimensions": [ATLAS_SIZE, ATLAS_SIZE],
        "supersample": SUPERSAMPLE,
        "color_alpha": {"path": portable_path(args.color), "sha256": sha256(args.color)},
        "surface_packed": {
            "path": portable_path(args.surface),
            "sha256": sha256(args.surface),
            "channels": {"r": "tangent_normal_x", "g": "tangent_normal_y", "b": "roughness", "a": "ambient_occlusion"},
        },
        "cells": {
            name: {"grid": list(cell), "uv_rect": uv_rect(cell)}
            for name, cell in CELLS.items()
        },
        "rendering": {"alpha_mode": "clip", "broad_alpha_blend": False, "camera_facing": False},
        "source": "Deterministic Pillow vector primitives in generate_vegetation_atlas.py",
        "license": "Project-authored",
        "external_assets": [],
    }
    args.report.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({
        "status": report["status"],
        "color": str(args.color),
        "surface": str(args.surface),
        "color_sha256": report["color_alpha"]["sha256"],
        "surface_sha256": report["surface_packed"]["sha256"],
    }, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
