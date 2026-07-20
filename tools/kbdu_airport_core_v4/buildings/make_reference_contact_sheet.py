#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageOps


RIGHTS = {
    "city_hangar_addresses.jpg": "City of Boulder - reference only; all rights reserved",
    "airport_data_terminal_007472.jpg": "Bluedharma / Airport-Data - copyrighted reference only",
    "airport_data_terminal_007473.jpg": "Bluedharma / Airport-Data - copyrighted reference only",
    "commons_airport_1280.jpg": "Jeffrey Beall - CC BY 4.0; external reference only",
    "opengov_bdu_aerial.jfif": "City of Boulder OpenGov - reference only; no reusable license identified",
    "opengov_bdu_snow_ops.jfif": "City of Boulder OpenGov - reference only; no reusable license identified"
}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("reference_dir", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    images = []
    receipts = []
    for name, rights in RIGHTS.items():
        path = args.reference_dir / name
        if not path.exists():
            continue
        with Image.open(path) as source:
            image = ImageOps.exif_transpose(source).convert("RGB")
            images.append((name, rights, image.copy()))
            receipts.append({"name": name, "bytes": path.stat().st_size, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "rights": rights, "repository_status": "EXTERNAL_ONLY_NOT_COMMITTED"})
    cell_w, cell_h, caption_h = 720, 440, 78
    columns = 2
    rows = (len(images) + columns - 1) // columns
    sheet = Image.new("RGB", (columns * cell_w, rows * (cell_h + caption_h)), (28, 30, 34))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default(size=18)
    for index, (name, rights, image) in enumerate(images):
        x = (index % columns) * cell_w
        y = (index // columns) * (cell_h + caption_h)
        fitted = ImageOps.contain(image, (cell_w - 20, cell_h - 20))
        px = x + (cell_w - fitted.width) // 2
        py = y + (cell_h - fitted.height) // 2
        sheet.paste(fitted, (px, py))
        draw.text((x + 10, y + cell_h + 5), name, fill=(245, 245, 245), font=font)
        draw.text((x + 10, y + cell_h + 31), rights, fill=(190, 196, 203), font=font)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(args.output, quality=94)
    (args.reference_dir / "reference_receipts.json").write_text(json.dumps({"schema_version": 1, "files": receipts, "copyrighted_reference_pixels_committed": False}, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps({"status": "PASS", "images": len(images), "output": str(args.output), "receipts": len(receipts)}, indent=2))


if __name__ == "__main__":
    main()
