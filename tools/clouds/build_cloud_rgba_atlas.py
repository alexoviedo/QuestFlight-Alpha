#!/usr/bin/env python3
"""Build a feathered, color-dilated RGBA cloud atlas from the V1 RGB source.

The original cloud artwork was composited over black.  Using its luminance as
runtime opacity preserves black fringe pixels in RGB and produces a halo after
mip filtering/ASTC compression.  This offline step derives a real alpha matte
and extends valid cloud color through the transparent texels so the runtime
shader never has to chroma-key RGB.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage


EXPECTED_SOURCE_SHA256 = (
    "a4bf9047b4427f71e902cb6aa1b5c3ff1296e29f4f5d0dd685be5ecc4548dd82"
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def smoothstep(low: float, high: float, value: np.ndarray) -> np.ndarray:
    t = np.clip((value - low) / (high - low), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def cell_gutter(height: int, atlas_width: int, width: int = 12) -> np.ndarray:
    if height % 2 or atlas_width % 2:
        raise ValueError("The cloud atlas must divide exactly into a 2x2 grid.")

    # The generated RGB source contains a few wisps that cross a quadrant
    # boundary.  Runtime UVs intentionally isolate the four quadrants, so those
    # pixels were already being cut at exactly U/V=0.5.  Taper every cell to a
    # protected transparent gutter, turning that hard slice into a stable vapor
    # edge and preventing adjacent-cell mip bleed.
    cell_height = height // 2
    cell_width = atlas_width // 2
    y = np.arange(cell_height, dtype=np.float32)
    x = np.arange(cell_width, dtype=np.float32)
    distance_y = np.minimum(y, cell_height - 1 - y)
    distance_x = np.minimum(x, cell_width - 1 - x)
    fade_y = smoothstep(0.0, float(width), distance_y)
    fade_x = smoothstep(0.0, float(width), distance_x)
    cell_fade = np.minimum(fade_y[:, None], fade_x[None, :])
    result = np.ones((height, atlas_width), dtype=np.float32)
    for row in range(2):
        for column in range(2):
            y0 = row * cell_height
            x0 = column * cell_width
            result[y0:y0 + cell_height, x0:x0 + cell_width] = cell_fade
    return result


def extract_straight_alpha_clouds(
    rgb: np.ndarray, luminance: np.ndarray
) -> tuple[np.ndarray, np.ndarray]:
    """Remove the black source plate without retaining its charcoal contour.

    The source is already composited against black, so a luminance key alone
    makes the dark antialiasing ring fully covered.  Eroding the subject by two
    source pixels removes that contaminated contour.  A Gaussian feather then
    rebuilds a soft matte, while RGB for the rebuilt edge is copied from a
    five-pixel-safe interior.  Processing each atlas cell independently avoids
    color bleeding between cloud variants.
    """

    height, atlas_width = luminance.shape
    if height % 2 or atlas_width % 2:
        raise ValueError("The cloud atlas must divide exactly into a 2x2 grid.")

    gutter = cell_gutter(height, atlas_width)
    # Match the original shader's black-plate rejection threshold.  The
    # six-pixel contour trim below removes both its charcoal antialiasing ring
    # and the smooth generator streaks present behind two atlas variants.
    source_subject = (luminance > 0.075) & (gutter > 0.02)
    alpha = np.zeros_like(luminance, dtype=np.float32)
    clean_rgb = np.zeros_like(rgb, dtype=np.float32)
    cell_height = height // 2
    cell_width = atlas_width // 2

    for row in range(2):
        for column in range(2):
            y0 = row * cell_height
            x0 = column * cell_width
            region = np.s_[y0:y0 + cell_height, x0:x0 + cell_width]
            subject = source_subject[region]
            if not np.any(subject):
                raise ValueError(f"Cloud atlas cell {row},{column} is empty.")

            trimmed = ndimage.binary_erosion(subject, iterations=6)
            if not np.any(trimmed):
                raise ValueError(f"Cloud atlas cell {row},{column} vanished while trimming.")

            feather = ndimage.gaussian_filter(
                trimmed.astype(np.float32), sigma=2.25, mode="constant", cval=0.0
            )
            cell_alpha = smoothstep(0.025, 0.985, feather)
            cell_alpha *= gutter[region]
            cell_alpha[cell_alpha < (1.0 / 255.0)] = 0.0
            alpha[region] = cell_alpha

            color_core = ndimage.binary_erosion(subject, iterations=12)
            if not np.any(color_core):
                color_core = trimmed
            _, nearest = ndimage.distance_transform_edt(
                ~color_core, return_indices=True
            )
            cell_rgb = rgb[region]
            dilated_rgb = cell_rgb[nearest[0], nearest[1]]
            distance_inside = ndimage.distance_transform_edt(subject)
            edge_weight = np.clip((14.0 - distance_inside) / 14.0, 0.0, 1.0)
            edge_weight[~subject] = 1.0
            edge_weight = edge_weight[..., None]
            clean_rgb[region] = (
                cell_rgb * (1.0 - edge_weight) + dilated_rgb * edge_weight
            )

    # Alpha-to-coverage samples contain unblended RGB.  Keep the partially
    # covered fringe neutral and bright enough to disappear into daylight;
    # opaque cloud shadows retain their authored tone.
    clean_luminance = np.tensordot(
        clean_rgb, np.array([0.299, 0.587, 0.114]), axes=([2], [0]))
    fringe = 1.0 - smoothstep(0.72, 0.96, alpha)
    neutral_lift = np.maximum(0.68 - clean_luminance, 0.0) * fringe
    clean_rgb = np.clip(clean_rgb + neutral_lift[..., None], 0.0, 1.0)
    return clean_rgb, alpha


def build(source_path: Path, output_path: Path, report_path: Path) -> None:
    source_hash = sha256(source_path)
    if source_hash != EXPECTED_SOURCE_SHA256:
        raise ValueError(
            "Refusing to derive the runtime atlas from an unrecognized source: "
            f"expected {EXPECTED_SOURCE_SHA256}, got {source_hash}."
        )

    source_image = Image.open(source_path)
    if source_image.mode != "RGB":
        raise ValueError(f"Expected the original RGB atlas, got {source_image.mode}.")

    rgb_u8 = np.asarray(source_image, dtype=np.uint8)
    rgb = rgb_u8.astype(np.float32) / 255.0
    luminance = np.tensordot(rgb, np.array([0.299, 0.587, 0.114]), axes=([2], [0]))

    clean_rgb, alpha = extract_straight_alpha_clouds(rgb, luminance)

    rgba = np.concatenate((clean_rgb, alpha[..., None]), axis=2)
    rgba_u8 = np.clip(np.rint(rgba * 255.0), 0, 255).astype(np.uint8)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    temporary_path = output_path.with_suffix(output_path.suffix + ".tmp")
    Image.fromarray(rgba_u8).save(temporary_path, format="PNG", optimize=True)
    temporary_path.replace(output_path)

    output_hash = sha256(output_path)
    report = {
        "contract": "questflight-cloud-rgba-atlas-v1",
        "source": str(source_path),
        "sourceSha256": source_hash,
        "output": str(output_path),
        "outputSha256": output_hash,
        "size": [int(source_image.width), int(source_image.height)],
        "sourceMode": source_image.mode,
        "outputMode": "RGBA",
        "sourcePlateThreshold": 0.075,
        "contourTrimPixels": 6,
        "colorCoreInsetPixels": 12,
        "featherSigmaPixels": 2.25,
        "nonzeroAlphaFraction": float(np.mean(alpha > 0.0)),
        "fullyOpaqueFraction": float(np.mean(alpha >= 0.98)),
        "transparentCornerAlphaMaximum": int(
            max(
                rgba_u8[0, 0, 3], rgba_u8[0, -1, 3],
                rgba_u8[-1, 0, 3], rgba_u8[-1, -1, 3]
            )
        ),
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    arguments = parser.parse_args()
    build(arguments.source.resolve(), arguments.output.resolve(), arguments.report.resolve())


if __name__ == "__main__":
    main()
