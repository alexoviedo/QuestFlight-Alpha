import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from build_water_manifest import build_manifest, classify, polygon_area


CONTEXT = ROOT / "QuestFlight/Assets/Resources/QuestFlightLab/Environment/KBDU/kbdu_reference_context.json"


class WaterManifestTests(unittest.TestCase):
    def test_area_is_orientation_independent(self):
        square = [[0, 0], [10, 0], [10, 10], [0, 10]]
        self.assertEqual(100.0, polygon_area(square))
        self.assertEqual(100.0, polygon_area(list(reversed(square))))

    def test_thresholds_fail_closed(self):
        polygon = {"geometry_type": "polygon", "tags": {}, "render_width_meters": 0}
        self.assertFalse(classify(polygon, [[0, 0], [10, 0], [10, 10], [0, 10]])[0])
        waterway = {"geometry_type": "polyline", "tags": {"waterway": "stream"}, "render_width_meters": 2.9}
        self.assertFalse(classify(waterway, [[0, 0], [100, 0]])[0])

    def test_current_manifest_is_complete_and_deterministic(self):
        first = build_manifest(CONTEXT)
        second = build_manifest(CONTEXT)
        self.assertEqual(first, second)
        self.assertEqual(800, first["source_feature_count"])
        self.assertEqual(first["source_feature_count"], first["represented_feature_count"] + first["excluded_feature_count"])
        self.assertGreater(first["represented_feature_count"], 0)
        self.assertTrue(all(item["source_license_class"] for item in first["represented_features"]))


if __name__ == "__main__":
    unittest.main()
