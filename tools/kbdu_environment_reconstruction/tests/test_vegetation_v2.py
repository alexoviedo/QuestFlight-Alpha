from __future__ import annotations

import hashlib
import json
import math
import struct
import sys
import tempfile
import unittest
import zlib
from collections import Counter
from pathlib import Path


SCRIPT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPT_DIR))

from generate_vegetation_v2 import EXCLUSION_CODES, write_mesh_library  # noqa: E402


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_grayscale_png(path: Path) -> tuple[int, int, bytes]:
    payload = path.read_bytes()
    assert payload.startswith(b"\x89PNG\r\n\x1a\n")
    offset = 8
    width = height = 0
    compressed = bytearray()
    while offset < len(payload):
        length = struct.unpack(">I", payload[offset : offset + 4])[0]
        name = payload[offset + 4 : offset + 8]
        data = payload[offset + 8 : offset + 8 + length]
        offset += 12 + length
        if name == b"IHDR":
            width, height, bit_depth, color_type, *_ = struct.unpack(">IIBBBBB", data)
            assert (bit_depth, color_type) == (8, 0)
        elif name == b"IDAT":
            compressed.extend(data)
        elif name == b"IEND":
            break
    decoded = zlib.decompress(compressed)
    rows = []
    stride = width + 1
    for y in range(height):
        row = decoded[y * stride : (y + 1) * stride]
        assert row[0] == 0
        rows.append(row[1:])
    return width, height, b"".join(rows)


class VegetationV2Tests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.source = json.loads((SCRIPT_DIR / "vegetation_source_v2.json").read_text(encoding="utf-8"))
        cls.coverage = json.loads((SCRIPT_DIR / "vegetation_coverage_report.json").read_text(encoding="utf-8"))
        cls.species = json.loads((SCRIPT_DIR / "vegetation_species_library_v2.json").read_text(encoding="utf-8"))

    def test_masks_are_optimized_and_source_pinned(self) -> None:
        mask_contract = self.source["mask_contract"]
        for key in ("likelihood", "exclusion", "suitability"):
            path = SCRIPT_DIR / mask_contract[f"{key}_path"]
            width, height, _ = read_grayscale_png(path)
            self.assertEqual((width, height), (1024, 1024))
            self.assertEqual(sha256_file(path), mask_contract[f"{key}_sha256"])
            self.assertLess(path.stat().st_size, 1024 * 1024)
        self.assertIn("natural-color", self.source["source"]["classification"])
        self.assertIn("public domain", self.source["source"]["image_license"].lower())
        self.assertIn("ODbL", self.source["source"]["osm_license"])

    def test_all_required_exclusions_are_present_and_applied(self) -> None:
        _, _, exclusion = read_grayscale_png(SCRIPT_DIR / self.source["mask_contract"]["exclusion_path"])
        observed = Counter(exclusion)
        for name in ("runway_safety", "taxiway", "apron", "building", "road", "open_water", "parking"):
            code = EXCLUSION_CODES[name]
            self.assertGreater(observed[code], 0, name)
            self.assertEqual(observed[code], self.coverage["exclusion_pixel_counts"][name])
        for placement in self.source["placements"]:
            px, py = placement["likelihood_mask_pixel"]
            self.assertEqual(exclusion[py * 1024 + px], 0)

    def test_placements_replace_legacy_candidates_and_obey_spacing(self) -> None:
        placements = self.source["placements"]
        self.assertEqual(self.source["legacy_candidate_count_replaced"], 246)
        self.assertEqual(len(placements), 377)
        self.assertEqual(len({value["stable_id"] for value in placements}), len(placements))
        self.assertEqual({value["species_id"] for value in placements}, {
            "plains_cottonwood", "narrowleaf_willow", "rocky_mountain_juniper", "rabbitbrush_shrub"
        })
        for index, first in enumerate(placements):
            first_point = first["local_enu_meters"]
            self.assertLessEqual(math.hypot(first_point["x"], first_point["z"]), 1500.0)
            for second in placements[index + 1 :]:
                second_point = second["local_enu_meters"]
                self.assertGreaterEqual(
                    math.hypot(first_point["x"] - second_point["x"], first_point["z"] - second_point["z"]),
                    23.99,
                )
        self.assertEqual(self.coverage["status"], "PASS")

    def test_project_owned_species_lods_are_closed_geometry_not_billboards(self) -> None:
        self.assertEqual(self.species["license"], "Project-authored; repository license")
        self.assertFalse(self.species["stereo_geometry_contract"]["camera_facing_billboards"])
        self.assertTrue(self.species["stereo_geometry_contract"]["closed_crown_volume"])
        self.assertTrue(self.species["stereo_geometry_contract"]["top_and_oblique_views_supported"])
        self.assertEqual(len(self.species["species"]), 4)
        for species in self.species["species"]:
            self.assertEqual([lod["lod"] for lod in species["lods"]], [0, 1, 2])
            triangles = [lod["triangles"] for lod in species["lods"]]
            self.assertGreater(triangles[0], triangles[1])
            self.assertGreater(triangles[1], triangles[2])
            for lod in species["lods"]:
                path = SCRIPT_DIR / lod["path"]
                face_count = sum(line.startswith("f ") for line in path.read_text(encoding="ascii").splitlines())
                self.assertEqual(face_count, lod["triangles"])
                self.assertEqual(sha256_file(path), lod["sha256"])

    def test_mesh_library_regenerates_byte_identically(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary)
            generated = write_mesh_library(output)
            self.assertEqual(generated, self.species)
            for path in (SCRIPT_DIR / "vegetation_meshes").glob("*"):
                self.assertEqual(path.read_bytes(), (output / "vegetation_meshes" / path.name).read_bytes())


if __name__ == "__main__":
    unittest.main()
