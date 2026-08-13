import importlib.util
import json
import pathlib
import sys
import unittest


MODULE_ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(MODULE_ROOT))

from build_full_map_clipmap import build_manifest, sample_cells, tier_specs
from validate_android_bundle_inventory import manifest_assets
from validate_full_map_clipmap import expected_page_count


def config():
    return json.loads((MODULE_ROOT / "config.json").read_text(encoding="utf-8"))


class FullMapClipmapTests(unittest.TestCase):
    def test_required_offline_dependencies_are_installed(self):
        for module in ("numpy", "PIL", "pyproj", "rasterio"):
            self.assertIsNotNone(
                importlib.util.find_spec(module),
                f"required offline compiler dependency is missing: {module}",
            )

    def test_high_tier_covers_full_product_extent_without_origin_bias(self):
        value = config()
        specs = tier_specs(value, "high")
        bounds = value["local_bounds_meters"]
        inner_min_x = min(
            spec.local_bounds[0] + spec.gutter_pixels * spec.meters_per_texel
            for spec in specs
        )
        inner_min_z = min(
            spec.local_bounds[1] + spec.gutter_pixels * spec.meters_per_texel
            for spec in specs
        )
        inner_max_x = max(
            spec.local_bounds[2] - spec.gutter_pixels * spec.meters_per_texel
            for spec in specs
        )
        inner_max_z = max(
            spec.local_bounds[3] - spec.gutter_pixels * spec.meters_per_texel
            for spec in specs
        )
        self.assertLessEqual(inner_min_x, bounds[0])
        self.assertLessEqual(inner_min_z, bounds[1])
        self.assertGreaterEqual(inner_max_x, bounds[2])
        self.assertGreaterEqual(inner_max_z, bounds[3])
        self.assertGreater(len({spec.bundle_name for spec in specs}), 1)

    def test_sample_is_a_three_by_three_airport_center_neighborhood(self):
        value = config()
        selected = sample_cells(value, "high")
        self.assertEqual(len(selected), 9)
        self.assertEqual(
            max(column for column, _ in selected) - min(column for column, _ in selected),
            2,
        )
        self.assertEqual(
            max(row for _, row in selected) - min(row for _, row in selected),
            2,
        )

    def test_manifest_records_aircraft_centered_grid_contract(self):
        value = config()
        audit = {"passed": True}
        manifest = build_manifest(value, audit, [], "sample")
        self.assertEqual(
            manifest["local_bounds_meters"],
            [-12000.0, -12000.0, 12000.0, 12000.0],
        )
        self.assertEqual(manifest["tiers"]["high"]["runtime_grid_size"], 3)
        self.assertEqual(manifest["tiers"]["high"]["columns"], 36)
        self.assertAlmostEqual(
            manifest["tiers"]["high"]["meters_per_texel"], 1.0 / 3.0
        )
        self.assertGreater(manifest["tiers"]["medium"]["columns"], 10)
        self.assertEqual(manifest["source"], audit)

    def test_runtime_budget_includes_both_atomic_buffers(self):
        value = config()

        def astc10_mips(size):
            total = 0
            width = height = size
            while True:
                total += ((width + 9) // 10) * ((height + 9) // 10) * 16
                if width == 1 and height == 1:
                    return total
                width = max(1, width // 2)
                height = max(1, height // 2)

        total = (
            astc10_mips(value["tiers"]["high"]["output_pixels"]) * 9 * 2
            + astc10_mips(value["tiers"]["medium"]["output_pixels"]) * 9 * 2
            + astc10_mips(value["tiers"]["global"]["output_pixels"])
        )
        self.assertLessEqual(total, value["runtime"]["maximum_active_aerial_texture_bytes"])
        self.assertGreater(total, 22 * 1024 * 1024)

    def test_full_manifest_page_count_is_complete_and_bounded(self):
        value = config()
        manifest = build_manifest(value, {"passed": True}, [], "full")
        self.assertEqual(expected_page_count(manifest), 1873)

    def test_android_bundle_text_manifest_parser_is_strictly_asset_scoped(self):
        scratch = MODULE_ROOT / "tests" / "_bundle_manifest_test.tmp"
        scratch.write_text(
            "ManifestFileVersion: 0\nAssets:\n"
            "- Assets/Generated/FullMapAerialClipmap/high/KBDU_2020_high_000_000.jpg\n"
            "Dependencies: []\n- must-not-be-parsed\n",
            encoding="utf-8",
        )
        try:
            self.assertEqual(
                manifest_assets(scratch),
                ["Assets/Generated/FullMapAerialClipmap/high/"
                 "KBDU_2020_high_000_000.jpg"],
            )
        finally:
            scratch.unlink(missing_ok=True)


if __name__ == "__main__":
    unittest.main()
