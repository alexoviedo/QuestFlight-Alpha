import json
import sys
import unittest
from pathlib import Path

from PIL import Image


TOOL_DIR = Path(__file__).resolve().parents[1]
REPOSITORY_ROOT = TOOL_DIR.parents[1]
sys.path.insert(0, str(TOOL_DIR))

from build_full_map_water_mask import (  # noqa: E402
    MASK_SIZE,
    MAJOR_WATER_AREA_SQUARE_METERS,
    MINIMUM_EFFECTIVE_AREA_SQUARE_METERS,
    WORLD_SIZE_METERS,
    pixel_point,
    polygon_area,
    polyline_length,
)


MASK = (
    REPOSITORY_ROOT
    / "QuestFlight/Assets/Production/Environment/PhotorealTerrainV1/Water/"
    "KBDU_FullMapWaterMask.png"
)
MANIFEST = MASK.with_name("KBDU_FullMapWaterReflectionManifest.json")


class FullMapWaterMaskTests(unittest.TestCase):
    def test_geometry_metrics_are_deterministic(self):
        square = [(0.0, 0.0), (20.0, 0.0), (20.0, 20.0), (0.0, 20.0)]
        self.assertEqual(400.0, polygon_area(square))
        self.assertEqual(400.0, polygon_area(list(reversed(square))))
        self.assertEqual(10.0, polyline_length([(0.0, 0.0), (6.0, 8.0)]))

    def test_world_to_image_orientation_matches_north_up_orthophoto(self):
        scale = (MASK_SIZE - 1) / WORLD_SIZE_METERS
        center = pixel_point((0.0, 0.0), scale)
        north = pixel_point((0.0, 100.0), scale)
        east = pixel_point((100.0, 0.0), scale)
        self.assertLess(north[1], center[1])
        self.assertGreater(east[0], center[0])

    def test_committed_mask_and_manifest_cover_full_product_extent(self):
        self.assertTrue(MASK.is_file())
        self.assertTrue(MANIFEST.is_file())
        with Image.open(MASK) as image:
            self.assertEqual((MASK_SIZE, MASK_SIZE), image.size)
            self.assertEqual("RGB", image.mode)
            extrema = image.getextrema()
            self.assertTrue(all(channel[0] == 0 for channel in extrema))
            self.assertGreaterEqual(extrema[0][1], 240)
            self.assertGreaterEqual(extrema[1][1], 200)
            self.assertGreater(extrema[2][1], 0)
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
        self.assertEqual([WORLD_SIZE_METERS, WORLD_SIZE_METERS], manifest["mask"]["world_size_meters"])
        self.assertEqual(
            MINIMUM_EFFECTIVE_AREA_SQUARE_METERS,
            manifest["coverage"]["minimum_effective_area_square_meters"],
        )
        self.assertEqual(
            MAJOR_WATER_AREA_SQUARE_METERS,
            manifest["coverage"]["major_water_area_square_meters"],
        )
        self.assertGreater(manifest["mask"]["major_polygon_count"], 0)
        self.assertGreater(manifest["mask"]["secondary_polygon_count"], 0)
        self.assertGreaterEqual(manifest["coverage"]["selected_feature_count"], 1_000)
        self.assertEqual(
            manifest["coverage"]["selected_feature_count"],
            len(manifest["selected_features"]),
        )
        self.assertEqual(0, manifest["runtime_contract"]["additional_draw_calls"])
        self.assertEqual(0, manifest["runtime_contract"]["additional_renderers"])
        self.assertTrue(manifest["runtime_contract"]["orthophoto_remains_base_color"])
        self.assertEqual(
            "none",
            manifest["runtime_contract"]["diffuse_color_modification"],
        )


if __name__ == "__main__":
    unittest.main()
