from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path


SCRIPT_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = SCRIPT_DIR.parents[2]
sys.path.insert(0, str(SCRIPT_DIR))

from ground_imagery_common import DEFAULT_CONFIG, DEFAULT_OUTPUT, astc_mip_chain_bytes, load_config, sha256_file


class GroundImageryContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.config = load_config(DEFAULT_CONFIG)
        cls.manifest = json.loads((DEFAULT_OUTPUT / "KBDU_GroundImageryRecoveryV2_SourceManifest.json").read_text())
        cls.contract = json.loads((DEFAULT_OUTPUT / "KBDU_GroundImageryRecoveryV2_Contract.json").read_text())

    def test_current_source_is_official_public_domain_naip(self) -> None:
        source = self.manifest["source"]
        self.assertTrue(source["service_url"].startswith("https://imagery.nationalmap.gov/"))
        self.assertEqual(source["license_spdx"], "LicenseRef-US-Government-Work-Public-Domain")
        self.assertEqual(source["catalog_year"], 2019)
        self.assertEqual(source["resolution_meters"], 0.6)
        self.assertEqual(len(source["raster_ids"]), 25)

    def test_runtime_derivatives_are_real_downsamples_not_upscales(self) -> None:
        for asset in self.manifest["derived_assets"]:
            self.assertGreaterEqual(min(asset["meters_per_texel"]), self.manifest["source"]["resolution_meters"])
            expected = [4096, 4096] if asset["id"] == "mid_base_12km" else [2048, 2048]
            self.assertEqual(asset["dimensions_pixels"], expected)
            self.assertTrue((REPO_ROOT / asset["path"]).is_file())
            self.assertEqual(sha256_file(REPO_ROOT / asset["path"]), asset["sha256"])

    def test_four_hot_quadrants_are_complete_and_overlap(self) -> None:
        hot = {asset["id"]: asset for asset in self.manifest["derived_assets"] if asset["id"] != "mid_base_12km"}
        self.assertEqual(set(hot), {"mid_nw", "mid_ne", "mid_sw", "mid_se"})
        self.assertEqual(hot["mid_nw"]["local_bounds_meters"][2], 32.0)
        self.assertEqual(hot["mid_ne"]["local_bounds_meters"][0], -32.0)
        self.assertEqual(hot["mid_nw"]["local_bounds_meters"][1], -32.0)
        self.assertEqual(hot["mid_sw"]["local_bounds_meters"][3], 32.0)

    def test_full_residency_fits_static_texture_ceiling(self) -> None:
        budget = self.manifest["budget"]
        hot_bytes = astc_mip_chain_bytes(2048, 2048, 6, 6)
        fallback_bytes = astc_mip_chain_bytes(4096, 4096, 10, 10)
        self.assertEqual(hot_bytes, 2497712)
        self.assertEqual(fallback_bytes, 3589696)
        self.assertEqual(budget["new_full_astc_mip_bytes"], hot_bytes * 4 + fallback_bytes)
        self.assertLessEqual(budget["net_full_mip_increase_bytes"], 4000000)
        self.assertLessEqual(budget["projected_scene_estimated_texture_bytes"], budget["scene_texture_ceiling_bytes"])

    def test_automatic_streaming_is_not_relied_upon(self) -> None:
        for asset in self.manifest["derived_assets"]:
            self.assertFalse(asset["android_import"]["streaming_mipmaps"])
        fallback = next(asset for asset in self.manifest["derived_assets"] if asset["id"] == "mid_base_12km")
        self.assertEqual(fallback["android_import"]["format"], "ASTC_10x10")
        self.assertEqual(fallback["meters_per_texel"], [2.9296875, 2.9296875])
        self.assertTrue(self.contract["renderer_contract"]["automatic_mip_streaming_is_not_relied_upon"])

    def test_water_is_orthophoto_only_and_zero_draw(self) -> None:
        self.assertEqual(self.contract["water"]["mode"], "orthophoto_only")
        self.assertTrue(self.contract["renderer_contract"]["no_draw_triangle_or_material_delta_from_imagery_itself"])
        serialized = json.dumps(self.contract["water"]).lower()
        self.assertIn("no water renderer", serialized)
        self.assertIn("no extra draw", serialized)

    def test_shader_integration_is_exact_and_replaces_only_mid(self) -> None:
        spec = self.contract["shader_integration_spec"]
        self.assertEqual(spec["rebound_property"]["_MidAlbedo"], next(
            asset["path"] for asset in self.manifest["derived_assets"] if asset["id"] == "mid_base_12km"
        ))
        self.assertEqual(spec["rebound_property"]["replaces"], self.config["baseline"]["active_mid_texture"])
        self.assertEqual(set(spec["new_texture_properties"]), {
            "_RecoveryMidNWAlbedo", "_RecoveryMidNEAlbedo", "_RecoveryMidSWAlbedo", "_RecoveryMidSEAlbedo"
        })
        self.assertEqual(len(spec["fragment_branch_order"]), 7)


if __name__ == "__main__":
    unittest.main()
