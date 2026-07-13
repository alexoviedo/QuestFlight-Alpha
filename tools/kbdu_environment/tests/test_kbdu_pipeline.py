from __future__ import annotations

import math
import sys
import unittest
from pathlib import Path


PIPELINE_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PIPELINE_DIR))

from kbdu_common import (  # noqa: E402
    bbox_for_half_extent,
    decode_int16_base64,
    encode_int16_base64,
    enu_to_geodetic,
    expected_triangle_count,
    geodetic_to_enu,
    layer_dimensions,
    load_config,
    simplify_polyline,
)
from process_kbdu_environment import macro_material_for, runway_blended_height  # noqa: E402


class KbduCommonTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.config = load_config()
        cls.origin = cls.config["airport"]["origin"]

    def test_pinned_faa_origin(self) -> None:
        self.assertAlmostEqual(self.origin["latitude_degrees"], 40.03936527, places=8)
        self.assertAlmostEqual(self.origin["longitude_degrees"], -105.22608958, places=8)
        self.assertAlmostEqual(self.origin["elevation_msl_meters"], 5288 * 0.3048, places=4)

    def test_enu_geodetic_round_trip_across_far_ring(self) -> None:
        for east, north, up in ((0.0, 0.0, 0.0), (12000.0, -12000.0, 400.0), (-9000.0, 7300.0, -25.0)):
            latitude, longitude, height = enu_to_geodetic(
                east,
                north,
                up,
                self.origin["latitude_degrees"],
                self.origin["longitude_degrees"],
                self.origin["elevation_msl_meters"],
            )
            round_east, round_north, round_up = geodetic_to_enu(
                latitude,
                longitude,
                height,
                self.origin["latitude_degrees"],
                self.origin["longitude_degrees"],
                self.origin["elevation_msl_meters"],
            )
            self.assertAlmostEqual(round_east, east, places=4)
            self.assertAlmostEqual(round_north, north, places=4)
            self.assertAlmostEqual(round_up, up, places=4)

    def test_far_bbox_is_bounded_and_contains_requested_enu_corners(self) -> None:
        south, west, north, east = bbox_for_half_extent(self.config, 12000.0)
        self.assertLess(south, self.origin["latitude_degrees"])
        self.assertGreater(north, self.origin["latitude_degrees"])
        self.assertLess(west, self.origin["longitude_degrees"])
        self.assertGreater(east, self.origin["longitude_degrees"])
        self.assertLess(north - south, 0.23)
        self.assertLess(east - west, 0.29)

    def test_terrain_layer_budgets_are_stable(self) -> None:
        layers = self.config["terrain"]["layers"]
        by_id = {layer["id"]: layer for layer in layers}
        samples = sum(layer_dimensions(layer)[0] * layer_dimensions(layer)[1] for layer in layers)
        triangles = sum(expected_triangle_count(layer, by_id) for layer in layers)
        self.assertEqual(samples, 30304)
        self.assertEqual(triangles, 54768)
        self.assertLessEqual(samples, self.config["terrain"]["budgets"]["maximum_height_samples"])
        self.assertLessEqual(triangles, self.config["terrain"]["budgets"]["maximum_terrain_triangles"])

    def test_int16_height_encoding_round_trip(self) -> None:
        values = [-32768, -200, 0, 123, 32767]
        self.assertEqual(decode_int16_base64(encode_int16_base64(values), len(values)), values)

    def test_closed_simplification_preserves_polygon(self) -> None:
        polygon = [(0.0, 0.0), (5.0, 0.1), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0), (0.0, 0.0)]
        simplified = simplify_polyline(polygon, 0.5, closed=True)
        self.assertGreaterEqual(len(simplified), 5)
        self.assertEqual(simplified[0], simplified[-1])


class KbduProcessingTests(unittest.TestCase):
    def test_runway_blend_uses_endpoint_plane_and_leaves_far_terrain_unchanged(self) -> None:
        runways = [
            {
                "runway_id": "08/26",
                "width_feet": 75,
                "endpoints": [
                    {"x_east_meters": -625.0, "z_north_meters": -30.0, "usgs_elevation_msl_meters": 1612.0},
                    {"x_east_meters": 625.0, "z_north_meters": -30.0, "usgs_elevation_msl_meters": 1609.0},
                ],
            }
        ]
        blend = {
            "paved_core_margin_meters": 4.0,
            "paved_outer_margin_meters": 40.0,
            "paved_end_margin_meters": 55.0,
            "other_core_margin_meters": 2.0,
            "other_outer_margin_meters": 18.0,
            "other_end_margin_meters": 30.0,
        }
        center, runway_id, weight = runway_blended_height(0.0, -30.0, 1625.0, runways, blend)
        self.assertEqual(runway_id, "08/26")
        self.assertAlmostEqual(weight, 1.0, places=6)
        self.assertAlmostEqual(center, 1610.5, places=3)
        far, runway_id, weight = runway_blended_height(0.0, 300.0, 1625.0, runways, blend)
        self.assertIsNone(runway_id)
        self.assertEqual(weight, 0.0)
        self.assertEqual(far, 1625.0)

    def test_macro_material_assignment_is_deterministic(self) -> None:
        self.assertEqual(macro_material_for("water", {"natural": "water"}, 1), "water")
        self.assertEqual(macro_material_for("road", {"surface": "gravel"}, 2), "gravel")
        self.assertEqual(macro_material_for("aeroway", {"surface": "concrete"}, 3), "concrete")
        self.assertEqual(macro_material_for("landcover", {"landuse": "farmland"}, 3), "irrigated_field")
        self.assertEqual(macro_material_for("landcover", {"landuse": "farmland"}, 4), "harvested_field")


if __name__ == "__main__":
    unittest.main()
