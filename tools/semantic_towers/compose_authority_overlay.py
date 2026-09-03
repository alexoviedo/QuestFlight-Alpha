#!/usr/bin/env python3
"""Compose the two accepted authority overlays into one 1920x1080 evidence frame."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


def fit_panel(source: Image.Image, width: int, height: int) -> Image.Image:
    source = source.convert("RGB")
    scale = min(width / source.width, height / source.height)
    resized = source.resize(
        (round(source.width * scale), round(source.height * scale)), Image.Resampling.LANCZOS
    )
    panel = Image.new("RGB", (width, height), (18, 22, 27))
    panel.paste(resized, ((width - resized.width) // 2, (height - resized.height) // 2))
    return panel


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--communications", type=Path, required=True)
    parser.add_argument("--industrial", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    canvas = Image.new("RGB", (1920, 1080), (12, 16, 21))
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.load_default(size=24)
    small = ImageFont.load_default(size=17)
    draw.text((30, 22), "Semantic Tower Replacement V1 - authority overhead", fill="white", font=font)
    draw.text(
        (30, 58),
        "City of Boulder 2020 orthophoto; FCC/FAA/OSM points and exact product-shell decisions",
        fill=(190, 204, 216),
        font=small,
    )
    canvas.paste(fit_panel(Image.open(args.communications), 920, 920), (30, 115))
    canvas.paste(fit_panel(Image.open(args.industrial), 920, 920), (970, 115))
    draw.text((45, 85), "FCC ASR 1024061 - preserve equipment shelter; add guyed mast", fill="white", font=small)
    draw.text((985, 85), "FAA 08-000121 - exclude building 52188; add industrial stack", fill="white", font=small)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(args.output, optimize=True)
    print(f"wrote {args.output} size={canvas.width}x{canvas.height}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
