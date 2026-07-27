#!/usr/bin/env python3
"""Build the tiny shared, project-authored traffic surface-detail texture."""

from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[2]
OUTPUT = (
    ROOT
    / "QuestFlight/Assets/Production/Environment/TrafficV1/Textures"
    / "KBDU_TrafficSurfaceDetail.png"
)
MANIFEST = Path(__file__).with_name("traffic_surface_texture_manifest.json")
SIZE = 128


def hash_noise(x: int, y: int) -> float:
    value = (x * 0x1F123BB5) ^ (y * 0x05491333) ^ 0xA47C2D19
    value ^= value >> 15
    value = (value * 0x2C1B3C6D) & 0xFFFFFFFF
    value ^= value >> 12
    return (value & 0xFFFF) / 65535.0


def build_pixel(x: int, y: int) -> tuple[int, int, int, int]:
    broad = (
        math.sin(x * math.tau / 37.0)
        + math.sin((x + y) * math.tau / 53.0)
        + math.cos(y * math.tau / 29.0)
    ) / 6.0 + 0.5
    noise = hash_noise(x, y)
    paint = max(0.0, min(1.0, broad * 0.72 + noise * 0.28))
    roughness = max(
        0.0,
        min(1.0, 0.45 + broad * 0.30 + (noise - 0.5) * 0.12),
    )
    glass = max(0.0, min(1.0, 0.48 + broad * 0.18))
    return (
        round(paint * 255),
        round(roughness * 255),
        round(glass * 255),
        255,
    )


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    image = Image.new("RGBA", (SIZE, SIZE))
    image.putdata(
        [build_pixel(x, y) for y in range(SIZE) for x in range(SIZE)]
    )
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    image.save(OUTPUT, optimize=True)
    result = {
        "schema": "questflight.traffic-surface-detail-v1",
        "path": OUTPUT.relative_to(ROOT).as_posix(),
        "size_pixels": [SIZE, SIZE],
        "channels": {
            "r": "project-authored paint micro-variation",
            "g": "project-authored roughness variation",
            "b": "reserved glass variation",
            "a": "opaque",
        },
        "license": "Project-owned",
        "sha256": sha256(OUTPUT),
    }
    MANIFEST.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
