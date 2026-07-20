from __future__ import annotations

import json
import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path


TOOL_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOL_DIR))

from generate_reservoir_assets import (  # noqa: E402
    DEFAULT_MANIFEST,
    DEFAULT_MESH,
    DEFAULT_REFERENCE,
    TEXTURE_SIZE,
    generate,
    parse_unity_mesh,
    sha256,
    validate_geometry,
)
from validate_reservoir_assets import validate  # noqa: E402


def decode_rgb_png(path: Path) -> tuple[int, int, bytes]:
    payload = path.read_bytes()
    if payload[:8] != b"\x89PNG\r\n\x1a\n":
        raise AssertionError("Not a PNG")
    position = 8
    idat = bytearray()
    width = height = 0
    while position < len(payload):
        length = struct.unpack_from(">I", payload, position)[0]
        kind = payload[position + 4 : position + 8]
        data = payload[position + 8 : position + 8 + length]
        position += 12 + length
        if kind == b"IHDR":
            width, height, bit_depth, color_type, _, _, _ = struct.unpack(">IIBBBBB", data)
            if (bit_depth, color_type) != (8, 2):
                raise AssertionError("Expected 8-bit RGB PNG")
        elif kind == b"IDAT":
            idat.extend(data)
        elif kind == b"IEND":
            break
    raw = zlib.decompress(bytes(idat))
    row_bytes = width * 3
    pixels = bytearray()
    for row in range(height):
        offset = row * (row_bytes + 1)
        if raw[offset] != 0:
            raise AssertionError("Generator must use deterministic filter 0")
        pixels.extend(raw[offset + 1 : offset + 1 + row_bytes])
    return width, height, bytes(pixels)


class ReservoirAssetTests(unittest.TestCase):
    def test_authoritative_mesh_matches_smoothed_osm_shoreline(self) -> None:
        mesh = parse_unity_mesh(DEFAULT_MESH)
        geometry = validate_geometry(mesh, DEFAULT_REFERENCE)
        self.assertEqual(geometry["source_vertex_count"], 127)
        self.assertEqual(geometry["smoothed_boundary_vertex_count"], 254)
        self.assertEqual(geometry["triangle_count"], 252)
        self.assertEqual(geometry["self_intersection_count"], 0)
        self.assertEqual(geometry["maximum_osm_to_mesh_delta_meters"], 0.0)

    def test_generation_is_byte_deterministic_and_channels_are_populated(self) -> None:
        with tempfile.TemporaryDirectory() as first_temp, tempfile.TemporaryDirectory() as second_temp:
            first = Path(first_temp)
            second = Path(second_temp)
            first_manifest = generate(DEFAULT_MESH, DEFAULT_REFERENCE, first, first / "manifest.json")
            second_manifest = generate(DEFAULT_MESH, DEFAULT_REFERENCE, second, second / "manifest.json")
            self.assertEqual(
                [asset["sha256"] for asset in first_manifest["derived_assets"]],
                [asset["sha256"] for asset in second_manifest["derived_assets"]],
            )

            for asset in first_manifest["derived_assets"]:
                path = first / Path(asset["path"]).name
                width, height, pixels = decode_rgb_png(path)
                self.assertEqual((width, height), (TEXTURE_SIZE, TEXTURE_SIZE))
                self.assertEqual(sha256(path), asset["sha256"])
                self.assertGreater(len(set(pixels[0::3])), 16)
                if path.name.endswith("_NM.png"):
                    self.assertGreater(sum(pixels[2::3]) / (width * height), 238.0)

            packed = first / "BoulderReservoir_DepthShoreRoughness.png"
            _, _, pixels = decode_rgb_png(packed)
            self.assertEqual(min(pixels[0::3]), 0)
            self.assertEqual(max(pixels[0::3]), 255)
            self.assertGreater(max(pixels[1::3]), 240)
            self.assertGreater(len(set(pixels[2::3])), 64)

    def test_committed_assets_shader_and_cost_contract(self) -> None:
        report = validate(DEFAULT_MANIFEST)
        self.assertEqual(report["status"], "PASS")
        self.assertEqual(report["derived_asset_count"], 3)
        self.assertEqual(report["shader_pass_count"], 1)
        self.assertEqual(report["water_surface_draw_slots"], 1)
        self.assertEqual(report["transparent_slots"], 0)
        self.assertLess(report["estimated_android_astc_6x6_bytes_with_full_mips"], 512 * 1024)


if __name__ == "__main__":
    unittest.main()
