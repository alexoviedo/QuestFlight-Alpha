#!/usr/bin/env python3
"""Build a deterministic mobile sky derivative from a Poly Haven Radiance HDR.

The source download intentionally lives outside Git.  This tool decodes RGBE into
linear radiance, applies an exact 2x2 box filter, and writes scanline-RLE RGBE.
It never tone maps or clips the photographed dynamic range.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct
from typing import BinaryIO
import zlib

import numpy as np


def _read_line(stream: BinaryIO) -> str:
    line = stream.readline()
    if not line:
        raise ValueError("Unexpected end of Radiance HDR header")
    return line.decode("ascii").rstrip("\r\n")


def read_rgbe(path: Path) -> tuple[np.ndarray, dict[str, object]]:
    with path.open("rb") as stream:
        signature = _read_line(stream)
        if signature not in {"#?RADIANCE", "#?RGBE"}:
            raise ValueError(f"Unsupported HDR signature: {signature!r}")

        header_lines: list[str] = []
        while True:
            line = _read_line(stream)
            if not line:
                break
            header_lines.append(line)
        resolution = _read_line(stream).split()
        if len(resolution) != 4 or resolution[0] != "-Y" or resolution[2] != "+X":
            raise ValueError("Only canonical -Y height +X width Radiance HDRs are supported")
        height = int(resolution[1])
        width = int(resolution[3])
        if width < 8 or width > 32767:
            raise ValueError(f"Unsupported scanline width: {width}")

        rgbe = np.empty((height, width, 4), dtype=np.uint8)
        for y in range(height):
            marker = stream.read(4)
            expected = bytes((2, 2, (width >> 8) & 0xFF, width & 0xFF))
            if marker != expected:
                raise ValueError(f"Scanline {y} is not modern Radiance RLE")
            for channel in range(4):
                x = 0
                while x < width:
                    packet = stream.read(1)
                    if not packet:
                        raise ValueError(f"Truncated RLE packet at scanline {y}")
                    count = packet[0]
                    if count > 128:
                        count -= 128
                        value = stream.read(1)
                        if not value or count == 0 or x + count > width:
                            raise ValueError(f"Invalid RLE run at scanline {y}")
                        rgbe[y, x : x + count, channel] = value[0]
                    else:
                        if count == 0 or x + count > width:
                            raise ValueError(f"Invalid RLE literal at scanline {y}")
                        values = stream.read(count)
                        if len(values) != count:
                            raise ValueError(f"Truncated RLE literal at scanline {y}")
                        rgbe[y, x : x + count, channel] = np.frombuffer(values, dtype=np.uint8)
                    x += count

    exponent = rgbe[:, :, 3].astype(np.int16)
    scale = np.zeros((height, width), dtype=np.float32)
    nonzero = exponent > 0
    scale[nonzero] = np.exp2(exponent[nonzero].astype(np.float32) - 136.0)
    linear = rgbe[:, :, :3].astype(np.float32) * scale[:, :, None]
    metadata = {
        "signature": signature,
        "header": header_lines,
        "width": width,
        "height": height,
    }
    return linear, metadata


def encode_rgbe(linear: np.ndarray) -> np.ndarray:
    if linear.ndim != 3 or linear.shape[2] != 3:
        raise ValueError("Expected an HxWx3 linear RGB array")
    maximum = np.max(linear, axis=2)
    rgbe = np.zeros((*linear.shape[:2], 4), dtype=np.uint8)
    active = maximum > 1.0e-32
    if not np.any(active):
        return rgbe

    _, exponent = np.frexp(maximum[active])
    scale = np.exp2(8.0 - exponent.astype(np.float32))
    encoded = np.floor(linear[active] * scale[:, None]).clip(0, 255).astype(np.uint8)
    rgbe[active, :3] = encoded
    rgbe[active, 3] = (exponent + 128).clip(0, 255).astype(np.uint8)
    return rgbe


def _write_channel_rle(stream: BinaryIO, channel: np.ndarray) -> None:
    data = channel.tobytes()
    width = len(data)
    cursor = 0
    while cursor < width:
        run = 1
        while cursor + run < width and run < 127 and data[cursor + run] == data[cursor]:
            run += 1
        if run >= 4:
            stream.write(bytes((128 + run, data[cursor])))
            cursor += run
            continue

        literal_start = cursor
        cursor += run
        while cursor < width and cursor - literal_start < 128:
            next_run = 1
            while cursor + next_run < width and next_run < 127 and data[cursor + next_run] == data[cursor]:
                next_run += 1
            if next_run >= 4:
                break
            if cursor - literal_start + next_run > 128:
                break
            cursor += next_run
        literal_count = cursor - literal_start
        stream.write(bytes((literal_count,)))
        stream.write(data[literal_start:cursor])


def write_rgbe(path: Path, linear: np.ndarray) -> None:
    height, width, _ = linear.shape
    if width < 8 or width > 32767:
        raise ValueError(f"Unsupported output scanline width: {width}")
    rgbe = encode_rgbe(linear)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("wb") as stream:
        stream.write(b"#?RADIANCE\n")
        stream.write(b"FORMAT=32-bit_rle_rgbe\n")
        stream.write(b"SOFTWARE=QuestFlight build_hdri_derivative.py\n")
        stream.write(b"\n")
        stream.write(f"-Y {height} +X {width}\n".encode("ascii"))
        marker = bytes((2, 2, (width >> 8) & 0xFF, width & 0xFF))
        for y in range(height):
            stream.write(marker)
            for channel in range(4):
                _write_channel_rle(stream, rgbe[y, :, channel])


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def downsample_box(linear: np.ndarray, factor: int) -> np.ndarray:
    if factor < 1:
        raise ValueError("Downsample factor must be positive")
    height, width, channels = linear.shape
    if height % factor or width % factor:
        raise ValueError(f"{width}x{height} is not divisible by factor {factor}")
    return linear.reshape(height // factor, factor, width // factor, factor, channels).mean(axis=(1, 3))


def analyze(linear: np.ndarray) -> dict[str, object]:
    luminance = linear[:, :, 0] * 0.2126 + linear[:, :, 1] * 0.7152 + linear[:, :, 2] * 0.0722
    y, x = np.unravel_index(int(np.argmax(luminance)), luminance.shape)
    height, width = luminance.shape
    wrap_difference = np.abs(linear[:, 0, :] - linear[:, -1, :])
    return {
        "width": width,
        "height": height,
        "maximum_luminance_relative": float(luminance[y, x]),
        "maximum_luminance_pixel": [int(x), int(y)],
        "maximum_luminance_uv": [float((x + 0.5) / width), float((y + 0.5) / height)],
        "maximum_luminance_elevation_degrees": float(90.0 - ((y + 0.5) / height) * 180.0),
        "luminance_percentiles_relative": {
            "50": float(np.percentile(luminance, 50.0)),
            "90": float(np.percentile(luminance, 90.0)),
            "99": float(np.percentile(luminance, 99.0)),
            "99.9": float(np.percentile(luminance, 99.9)),
        },
        "nonfinite_sample_count": int(np.size(linear) - np.isfinite(linear).sum()),
        "negative_sample_count": int(np.count_nonzero(linear < 0.0)),
        "equirectangular_wrap_mean_abs_rgb_relative": float(np.mean(wrap_difference)),
        "equirectangular_wrap_p99_abs_rgb_relative": float(np.percentile(wrap_difference, 99.0)),
    }


def write_tonemapped_preview(path: Path, linear: np.ndarray) -> None:
    mapped = linear / (1.0 + linear)
    rgb = np.floor(np.power(np.clip(mapped, 0.0, 1.0), 1.0 / 2.2) * 255.0 + 0.5).astype(np.uint8)
    height, width, _ = rgb.shape
    scanlines = b"".join(b"\x00" + rgb[y].tobytes() for y in range(height))

    def chunk(chunk_type: bytes, payload: bytes) -> bytes:
        return (
            struct.pack(">I", len(payload))
            + chunk_type
            + payload
            + struct.pack(">I", zlib.crc32(chunk_type + payload) & 0xFFFFFFFF)
        )

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(scanlines, level=9))
        + chunk(b"IEND", b"")
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--downsample-factor", type=int, default=2)
    parser.add_argument("--expected-source-sha256")
    parser.add_argument("--report", type=Path)
    parser.add_argument("--preview", type=Path, help="Optional external Reinhard/gamma preview PNG")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    source_sha = sha256(args.input)
    if args.expected_source_sha256 and source_sha.lower() != args.expected_source_sha256.lower():
        raise ValueError(f"Source SHA-256 mismatch: {source_sha}")

    source, source_header = read_rgbe(args.input)
    derivative = downsample_box(source, args.downsample_factor)
    write_rgbe(args.output, derivative)
    roundtrip, output_header = read_rgbe(args.output)
    if roundtrip.shape != derivative.shape:
        raise ValueError("Derivative round-trip dimensions changed")
    if not np.all(np.isfinite(roundtrip)) or np.any(roundtrip < 0.0):
        raise ValueError("Derivative contains invalid radiance")
    if args.preview:
        write_tonemapped_preview(args.preview, roundtrip)

    quantization_error = np.abs(roundtrip - derivative)
    report = {
        "schema": "questflight.sky_derivative.v1",
        "processing": "linear-radiance 2x2 box filter; Radiance RGBE scanline RLE; no tone mapping",
        "source": {
            "path": str(args.input.resolve()),
            "sha256": source_sha,
            "bytes": args.input.stat().st_size,
            **source_header,
            "analysis": analyze(source),
        },
        "derivative": {
            "path": str(args.output.resolve()),
            "sha256": sha256(args.output),
            "bytes": args.output.stat().st_size,
            **output_header,
            "analysis": analyze(roundtrip),
            "maximum_rgbe_quantization_error_relative": float(np.max(quantization_error)),
            "mean_rgbe_quantization_error_relative": float(np.mean(quantization_error)),
        },
    }
    rendered = json.dumps(report, indent=2, sort_keys=True) + "\n"
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(rendered, encoding="utf-8")
    print(rendered, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
