#!/usr/bin/env python3
"""Render a dependency-free PNG data-QA preview of the generated KBDU terrain/context."""

from __future__ import annotations

import argparse
import json
import math
import struct
import zlib
from pathlib import Path
from typing import Any

from kbdu_common import atomic_write_bytes, atomic_write_json, decode_int16_base64, repo_root, sha256_file


ASSET_DIR = Path("QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=repo_root())
    parser.add_argument("--output", type=Path, required=True, help="PNG evidence path outside Git.")
    parser.add_argument("--size", type=int, default=1200)
    return parser.parse_args()


def load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def png_chunk(kind: bytes, payload: bytes) -> bytes:
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)


def write_png(path: Path, width: int, height: int, pixels: bytearray) -> None:
    rows = bytearray()
    stride = width * 3
    for row in range(height):
        rows.append(0)
        rows.extend(pixels[row * stride : (row + 1) * stride])
    payload = b"\x89PNG\r\n\x1a\n"
    payload += png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
    payload += png_chunk(b"IDAT", zlib.compress(bytes(rows), 9))
    payload += png_chunk(b"IEND", b"")
    atomic_write_bytes(path, payload)


def blend_color(low: tuple[int, int, int], high: tuple[int, int, int], amount: float) -> tuple[int, int, int]:
    amount = max(0.0, min(1.0, amount))
    return tuple(int(round(a + (b - a) * amount)) for a, b in zip(low, high))


def terrain_color(elevation: float, minimum: float, maximum: float, light: float) -> tuple[int, int, int]:
    normalized = (elevation - minimum) / max(1.0, maximum - minimum)
    if normalized < 0.28:
        base = blend_color((101, 105, 58), (91, 110, 65), normalized / 0.28)
    elif normalized < 0.63:
        base = blend_color((91, 110, 65), (105, 91, 69), (normalized - 0.28) / 0.35)
    elif normalized < 0.86:
        base = blend_color((105, 91, 69), (116, 111, 103), (normalized - 0.63) / 0.23)
    else:
        base = blend_color((116, 111, 103), (194, 198, 194), (normalized - 0.86) / 0.14)
    shade = max(0.48, min(1.35, light))
    return tuple(max(0, min(255, int(channel * shade))) for channel in base)


def set_pixel(pixels: bytearray, size: int, x: int, y: int, color: tuple[int, int, int]) -> None:
    if x < 0 or y < 0 or x >= size or y >= size:
        return
    offset = (y * size + x) * 3
    pixels[offset : offset + 3] = bytes(color)


def draw_line(
    pixels: bytearray,
    size: int,
    start: tuple[int, int],
    end: tuple[int, int],
    color: tuple[int, int, int],
    thickness: int,
) -> None:
    x0, y0 = start
    x1, y1 = end
    dx, dy = abs(x1 - x0), -abs(y1 - y0)
    step_x = 1 if x0 < x1 else -1
    step_y = 1 if y0 < y1 else -1
    error = dx + dy
    while True:
        radius = max(0, thickness // 2)
        for oy in range(-radius, radius + 1):
            for ox in range(-radius, radius + 1):
                set_pixel(pixels, size, x0 + ox, y0 + oy, color)
        if x0 == x1 and y0 == y1:
            break
        doubled = 2 * error
        if doubled >= dy:
            error += dy
            x0 += step_x
        if doubled <= dx:
            error += dx
            y0 += step_y


def main() -> int:
    args = parse_args()
    if args.size < 256 or args.size > 2400:
        raise RuntimeError("Preview size must be between 256 and 2400 pixels")
    asset_dir = args.repo_root.resolve() / ASSET_DIR
    terrain = load_json(asset_dir / "kbdu_terrain_rings.json")
    context = load_json(asset_dir / "kbdu_reference_context.json")
    far = next(layer for layer in terrain["layers"] if layer["id"] == "far_24km")
    values = decode_int16_base64(far["height_dm_little_endian_base64"], far["sample_count"])
    width, height = far["width"], far["height"]
    datum = terrain["origin"]["elevation_msl_meters"]
    quantization = terrain["height_quantization_meters"]
    heights = [datum + value * quantization for value in values]
    minimum, maximum = min(heights), max(heights)
    size = args.size
    pixels = bytearray(size * size * 3)

    sun_x, sun_z, sun_y = -0.55, -0.45, 0.70
    for py in range(size):
        gz = (size - 1 - py) / (size - 1) * (height - 1)
        iz = max(0, min(height - 1, int(round(gz))))
        for px in range(size):
            gx = px / (size - 1) * (width - 1)
            ix = max(0, min(width - 1, int(round(gx))))
            elevation = heights[iz * width + ix]
            left = heights[iz * width + max(0, ix - 1)]
            right = heights[iz * width + min(width - 1, ix + 1)]
            south = heights[max(0, iz - 1) * width + ix]
            north = heights[min(height - 1, iz + 1) * width + ix]
            spacing = float(far["spacing_meters"])
            slope_x = (right - left) / max(spacing, 1.0)
            slope_z = (north - south) / max(spacing, 1.0)
            nx, ny, nz = -slope_x, 2.0, -slope_z
            norm = math.sqrt(nx * nx + ny * ny + nz * nz)
            light = 0.72 + 0.58 * max(-0.3, (nx * sun_x + ny * sun_y + nz * sun_z) / norm)
            set_pixel(pixels, size, px, py, terrain_color(elevation, minimum, maximum, light))

    extent = float(far["max_x_meters"])
    coordinate_scale = float(context["coordinate_quantization_meters"])

    def screen(point_q: tuple[int, int]) -> tuple[int, int]:
        x_meters = point_q[0] * coordinate_scale
        z_meters = point_q[1] * coordinate_scale
        return (
            int(round((x_meters + extent) / (2.0 * extent) * (size - 1))),
            int(round((extent - z_meters) / (2.0 * extent) * (size - 1))),
        )

    category_style = {
        "landcover": ((139, 132, 72), 1),
        "building": ((55, 53, 51), 1),
        "barrier": ((90, 88, 82), 1),
        "road": ((205, 199, 181), 2),
        "water": ((42, 113, 151), 3),
        "aeroway": ((245, 188, 74), 3),
    }
    drawn_features = 0
    for feature in context["openstreetmap"]["features"]:
        coordinates_q = feature["points_q"]
        points_q = list(zip(coordinates_q[0::2], coordinates_q[1::2]))
        if len(points_q) < 2:
            continue
        color, thickness = category_style[feature["category"]]
        for first, second in zip(points_q, points_q[1:]):
            draw_line(pixels, size, screen(first), screen(second), color, thickness)
        drawn_features += 1

    for runway in context["faa"]["runways"]:
        first, second = runway["endpoints"]
        first_q = [
            int(round(first["x_east_meters"] / coordinate_scale)),
            int(round(first["z_north_meters"] / coordinate_scale)),
        ]
        second_q = [
            int(round(second["x_east_meters"] / coordinate_scale)),
            int(round(second["z_north_meters"] / coordinate_scale)),
        ]
        draw_line(pixels, size, screen(first_q), screen(second_q), (255, 72, 54), 5)

    output = args.output.resolve()
    write_png(output, size, size, pixels)
    report = {
        "schema_version": 1,
        "preview": str(output),
        "preview_sha256": sha256_file(output),
        "preview_bytes": output.stat().st_size,
        "size_pixels": [size, size],
        "terrain_layer": far["id"],
        "terrain_minimum_msl_meters": minimum,
        "terrain_maximum_msl_meters": maximum,
        "terrain_relief_meters": maximum - minimum,
        "vector_features_drawn": drawn_features,
        "note": "Data-QA preview only; not a Unity material/rendering or Quest performance result.",
    }
    atomic_write_json(output.with_suffix(".json"), report)
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
