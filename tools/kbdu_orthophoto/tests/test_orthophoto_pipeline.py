from __future__ import annotations

import base64
import json
import math
import random
import struct
import sys
import unittest
from pathlib import Path

from PIL import Image


SCRIPT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPT_DIR))

from orthophoto_common import REPO_ROOT, astc_mip_chain_bytes, iter_zone_tiles, load_config, sha256_file, zone_bbox
from build_far_dem import DEFAULT_FROZEN_TERRAIN, DEFAULT_OUTPUT, elevations_south_to_north, resample_frozen_far_grid
from reproject_crop_imagery import suspicious_overlap


class OrthophotoGeometryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.config = load_config()

    def test_zone_extents_are_bounded_and_metric_square(self) -> None:
        for zone in self.config["zones"]:
            west, south, east, north = zone_bbox(self.config, zone["half_extent_meters"])
            self.assertLess(west, self.config["origin"]["longitude_degrees"])
            self.assertGreater(east, self.config["origin"]["longitude_degrees"])
            self.assertLess(south, self.config["origin"]["latitude_degrees"])
            self.assertGreater(north, self.config["origin"]["latitude_degrees"])
        meters_per_texel = {
            zone["id"]: zone["half_extent_meters"] * 2.0 / zone["atlas_size_pixels"]
            for zone in self.config["zones"]
        }
        self.assertAlmostEqual(meters_per_texel["near_4km"], 0.9765625)
        self.assertAlmostEqual(meters_per_texel["mid_12km"], 2.9296875)
        self.assertAlmostEqual(meters_per_texel["far_24km"], 11.71875)
        self.assertLessEqual(meters_per_texel["mid_12km"] / meters_per_texel["near_4km"], 4.0)
        self.assertLessEqual(meters_per_texel["far_24km"] / meters_per_texel["mid_12km"], 4.0)

    def test_near_tiles_share_edges_without_requested_overlap(self) -> None:
        zone = next(item for item in self.config["zones"] if item["id"] == "near_4km")
        tiles = {(tile["row"], tile["column"]): tile for tile in iter_zone_tiles(self.config, zone)}
        self.assertEqual(tiles[(0, 0)]["bbox_wgs84"][1], tiles[(1, 0)]["bbox_wgs84"][3])
        self.assertEqual(tiles[(0, 0)]["bbox_wgs84"][2], tiles[(0, 1)]["bbox_wgs84"][0])
        self.assertEqual(len(tiles), 4)

    def test_astc_budget_is_bounded(self) -> None:
        total = sum(
            astc_mip_chain_bytes(zone["atlas_size_pixels"], zone["atlas_size_pixels"])
            for zone in self.config["zones"]
        )
        self.assertEqual(total, 22_420_784)
        self.assertLessEqual(total, self.config["unity"]["maximum_runtime_texture_bytes"])


class DuplicateStripRegressionTests(unittest.TestCase):
    @staticmethod
    def deterministic_noise(width: int, height: int) -> Image.Image:
        random_source = random.Random(20260713)
        payload = bytes(random_source.randrange(0, 256) for _ in range(width * height))
        return Image.frombytes("L", (width, height), payload).convert("RGB")

    def test_detects_arcgis_adjusted_aspect_overlap(self) -> None:
        # Models the rejected export: adjacent 256 px tiles share a 64 px repeated latitude strip.
        source = self.deterministic_noise(256, 448)
        north = source.crop((0, 0, 256, 256))
        south = source.crop((0, 192, 256, 448))
        result = suspicious_overlap(north, south, vertical=True)
        self.assertEqual(result["overlap_pixels_at_256"], 64)
        self.assertEqual(result["mean_absolute_error"], 0.0)
        self.assertLess(result["mean_absolute_error"], 6.0)

    def test_accepts_nonoverlapping_neighbors(self) -> None:
        source = self.deterministic_noise(256, 512)
        north = source.crop((0, 0, 256, 256))
        south = source.crop((0, 256, 256, 512))
        result = suspicious_overlap(north, south, vertical=True)
        self.assertGreater(result["mean_absolute_error"], 6.0)


class TerrainPyramidAndContextContractTests(unittest.TestCase):
    def test_ground_texel_density_audit_passes(self) -> None:
        audit = json.loads((SCRIPT_DIR / "kbdu_ground_texel_density_audit.json").read_text(encoding="utf-8"))
        self.assertTrue(audit["passed"])
        self.assertEqual([item["texel_density_ratio"] for item in audit["adjacency_checks"]], [3.0, 4.0])
        self.assertTrue(all(item["passed_color_mean_continuity"] for item in audit["overlap_pixel_diagnostics"]))
        self.assertLess(max(item["maximum_absolute_mean_channel_delta"] for item in audit["overlap_pixel_diagnostics"]), 1.0)

    def test_context_recipe_is_batched_and_source_seated(self) -> None:
        root = REPO_ROOT / "QuestFlight/Assets/Production/Environment/PhotorealTerrainV1"
        recipe = json.loads((root / "KBDU_ContextBatchRecipe.json").read_text(encoding="utf-8"))
        manifest = json.loads((root / "KBDU_ContextAssetsManifest.json").read_text(encoding="utf-8"))
        building_records = [record for batch in recipe["building_batches"] for record in batch["buildings"]]
        vegetation = [record for batch in recipe["vegetation_batches"] for record in batch["placements"]]
        self.assertEqual(len(recipe["building_batches"]), 23)
        self.assertEqual(len(building_records), 2500)
        self.assertEqual(len({record["feature_index"] for record in building_records}), 2500)
        self.assertEqual(len(recipe["vegetation_batches"]), 8)
        self.assertEqual(len(vegetation), 240)
        self.assertFalse(recipe["integration"]["one_renderer_per_building"])
        self.assertEqual(manifest["budgets"]["active_draw_call_delta"], 31)
        self.assertEqual(manifest["budgets"]["material_delta"], 2)
        self.assertTrue(manifest["generated_unity_assets"]["complete"])
        self.assertEqual(manifest["generated_unity_assets"]["observed_mesh_count"], 62)
        for record in manifest["authoring_sources"] + manifest["generated_unity_assets"]["files"]:
            path = REPO_ROOT / record["path"]
            self.assertTrue(path.is_file(), record["path"])
            self.assertEqual(sha256_file(path), record["sha256"], record["path"])
        self.assertTrue(all(math.isfinite(record["base_y_meters"]) for record in building_records + vegetation))
        source = REPO_ROOT / manifest["source"]["source_path"]
        self.assertEqual(sha256_file(source), manifest["source"]["source_sha256"])

    def test_context_atlas_and_unity_meta_pairing(self) -> None:
        root = REPO_ROOT / "QuestFlight/Assets/Production/Environment/PhotorealTerrainV1"
        with Image.open(root / "KBDU_ContextAtlas.png") as image:
            self.assertEqual(image.size, (1024, 1024))
            self.assertEqual(image.mode, "RGB")
        for path in root.rglob("*"):
            if path.is_file() and path.suffix != ".meta":
                self.assertTrue(Path(str(path) + ".meta").is_file(), f"missing Unity meta for {path}")


class FarTerrainResamplingTests(unittest.TestCase):
    def test_frozen_grid_control_samples_are_preserved(self) -> None:
        config = load_config()
        image, receipt = resample_frozen_far_grid(config, DEFAULT_FROZEN_TERRAIN)
        values = elevations_south_to_north(image)
        frozen = json.loads(DEFAULT_FROZEN_TERRAIN.read_text(encoding="utf-8"))
        layer = next(item for item in frozen["layers"] if item["id"] == "far_24km")
        payload = base64.b64decode(layer["height_dm_little_endian_base64"], validate=True)
        offsets = struct.unpack("<" + "h" * (61 * 61), payload)
        reference = config["origin"]["elevation_msl_meters"]
        for row in range(61):
            for column in range(61):
                expected = reference + offsets[row * 61 + column] * frozen["height_quantization_meters"]
                actual = values[(row * 4) * 241 + column * 4]
                self.assertAlmostEqual(actual, expected, places=3)
        self.assertEqual(receipt["raw_source_mode"], "frozen_400m_grid_bilinear_resample")
        self.assertEqual(receipt["source_snapshot_id"], "20260710T214309Z")

    def test_committed_far_manifest_discloses_resampling(self) -> None:
        manifest = json.loads(DEFAULT_OUTPUT.read_text(encoding="utf-8"))
        grid = manifest["grid"]
        payload = base64.b64decode(grid["height_payload_base64"], validate=True)
        self.assertEqual((grid["width"], grid["height"]), (241, 241))
        self.assertEqual(len(payload), 241 * 241 * 2)
        self.assertIn("not additional source elevation detail", manifest["source_fidelity_disclosure"])
        self.assertFalse(manifest["rendering_constraints"]["runtime_lod_groups"])
        self.assertTrue(manifest["rendering_constraints"]["sealed_outer_and_inner_skirts"])


if __name__ == "__main__":
    unittest.main()
