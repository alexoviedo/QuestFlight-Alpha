"""Create deterministic contact sheets for evaluating runtime aerial resolution/compression."""

from __future__ import annotations

import argparse
import json
import pathlib

from PIL import Image, ImageDraw, ImageFont


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    return parser.parse_args()


def resize_to_mpt(source: Image.Image, source_mpt: float, target_mpt: float) -> Image.Image:
    pixels = max(1, round(source.width * source_mpt / target_mpt))
    reduced = source.resize((pixels, pixels), Image.Resampling.LANCZOS)
    return reduced.resize(source.size, Image.Resampling.LANCZOS)


def main() -> int:
    args = parse_args()
    source = Image.open(args.input).convert("RGB")
    variants = [(0.25, source)] + [
        (mpt, resize_to_mpt(source, 0.25, mpt))
        for mpt in (0.30, 1.0 / 3.0, 0.375, 0.40, 0.48828125)
    ]
    crops = {
        "center": (source.width // 4, source.height // 4, source.width * 3 // 4, source.height * 3 // 4),
        "northwest": (0, 0, source.width // 2, source.height // 2),
        "southeast": (source.width // 2, source.height // 2, source.width, source.height),
    }
    panel = 512
    label_height = 34
    sheet = Image.new("RGB", (len(variants) * panel, len(crops) * (panel + label_height)), "black")
    draw = ImageDraw.Draw(sheet)
    manifest: list[dict[str, object]] = []
    for column, (mpt, variant) in enumerate(variants):
        for row, (crop_name, box) in enumerate(crops.items()):
            crop = variant.crop(box).resize((panel, panel), Image.Resampling.LANCZOS)
            y = row * (panel + label_height)
            sheet.paste(crop, (column * panel, y + label_height))
            draw.text((column * panel + 8, y + 8), f"{mpt:.3f} m/texel · {crop_name}", fill="white")
        manifest.append({
            "meters_per_texel": mpt,
            "equivalent_full_map_pixels_per_side": round(24000.0 / mpt),
        })
    args.output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(args.output, quality=95)
    args.output.with_suffix(".json").write_text(
        json.dumps({"source": str(args.input), "variants": manifest}, indent=2) + "\n",
        encoding="utf-8",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
