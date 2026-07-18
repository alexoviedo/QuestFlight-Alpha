#!/usr/bin/env python3
from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageOps


ORDER = ["terminal_front.png", "terminal_aerial.png", "hangar_front.png", "hangar_aerial.png", "hangar_door.png", "fuel_oblique.png"]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("preview_dir", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    cell_w, cell_h, caption_h = 640, 360, 34
    sheet = Image.new("RGB", (cell_w * 2, (cell_h + caption_h) * 3), (24, 27, 31))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default(size=18)
    for index, name in enumerate(ORDER):
        path = args.preview_dir / name
        with Image.open(path) as source:
            image = ImageOps.fit(source.convert("RGB"), (cell_w, cell_h), method=Image.Resampling.LANCZOS)
        x = (index % 2) * cell_w
        y = (index // 2) * (cell_h + caption_h)
        sheet.paste(image, (x, y))
        draw.text((x + 9, y + cell_h + 7), name.removesuffix(".png").replace("_", " "), fill=(235, 238, 242), font=font)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(args.output)
    print(f"Wrote {args.output}")


if __name__ == "__main__":
    main()
