#!/usr/bin/env python3
"""Build optimized RGBA leaf and multiview impostor atlases from CC0 inputs."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image


def digest(path: Path) -> str:
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def build_leaf(diffuse: Path, alpha_path: Path, output: Path):
    rgb = Image.open(diffuse).convert("RGB")
    alpha = Image.open(alpha_path).convert("L").resize(rgb.size, Image.Resampling.LANCZOS)
    output.parent.mkdir(parents=True, exist_ok=True)
    Image.merge("RGBA", (*rgb.split(), alpha)).save(output, optimize=True)


def pack_impostors(frames: list[Path], output: Path):
    if len(frames) != 8:
        raise ValueError("Exactly eight azimuth frames are required")
    images = [Image.open(path).convert("RGBA") for path in frames]
    size = images[0].size
    if any(image.size != size for image in images):
        raise ValueError("Impostor frames have mismatched dimensions")
    atlas = Image.new("RGBA", (size[0] * 4, size[1] * 2))
    for index, image in enumerate(images):
        atlas.paste(image, ((index % 4) * size[0], (index // 4) * size[1]))
    output.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(output, optimize=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--leaf-diffuse", type=Path, required=True)
    parser.add_argument("--leaf-alpha", type=Path, required=True)
    parser.add_argument("--leaf-output", type=Path, required=True)
    parser.add_argument("--impostor-frame-dir", type=Path, required=True)
    parser.add_argument("--impostor-output", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()

    frames = [args.impostor_frame_dir / f"tree_impostor_{index:02d}.png" for index in range(8)]
    for path in (args.leaf_diffuse, args.leaf_alpha, *frames):
        if not path.is_file():
            raise FileNotFoundError(path)
    build_leaf(args.leaf_diffuse, args.leaf_alpha, args.leaf_output)
    pack_impostors(frames, args.impostor_output)
    report = {
        "schema": "questflight.airport-tree-texture-build.v1",
        "inputs": {
            str(path): digest(path)
            for path in (args.leaf_diffuse, args.leaf_alpha, *frames)
        },
        "outputs": {
            str(path): {"bytes": path.stat().st_size, "sha256": digest(path)}
            for path in (args.leaf_output, args.impostor_output)
        },
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
