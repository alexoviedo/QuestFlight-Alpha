from __future__ import annotations

import sys
import unittest
from pathlib import Path

import numpy as np

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))

from build_runtime_terrain import (
    bilinear_runtime_grid,
    bilinear_source,
    resample_runtime_grid,
    seal_inner_layer_to_outer,
    runway_blended_height,
)


class TerrainV2Tests(unittest.TestCase):
    def test_bilinear_source_preserves_enu_orientation(self) -> None:
        source = np.array([[30.0, 40.0], [10.0, 20.0]], dtype=np.float32)
        self.assertAlmostEqual(bilinear_source(source, -1.0, -1.0, 1.0), 10.0)
        self.assertAlmostEqual(bilinear_source(source, 1.0, 1.0, 1.0), 40.0)
        self.assertAlmostEqual(bilinear_source(source, 0.0, 0.0, 1.0), 25.0)

    def test_runway_blend_uses_grade_plane_and_leaves_far_samples_unchanged(self) -> None:
        runway = {
            "end_08_enu_meters": [-10.0, 0.0],
            "end_26_enu_meters": [10.0, 0.0],
            "end_08_height_relative_meters": 2.0,
            "end_26_height_relative_meters": 0.0,
            "half_width_meters": 2.0,
            "core_margin_meters": 1.0,
            "outer_margin_meters": 5.0,
        }
        self.assertAlmostEqual(runway_blended_height(0.0, 0.0, 9.0, runway), 1.0)
        self.assertAlmostEqual(runway_blended_height(0.0, 100.0, 9.0, runway), 9.0)

    def test_inner_grid_boundary_is_sealed_to_outer_grid(self) -> None:
        outer = np.arange(25, dtype=np.float32).reshape(5, 5)
        inner = np.full((3, 3), 100.0, dtype=np.float32)
        layer = {
            "minimum_enu_meters": [1.0, 1.0],
            "maximum_enu_meters": [3.0, 3.0],
            "spacing_meters": 1.0,
            "height_quantization_meters": 0.01,
        }
        seal_inner_layer_to_outer(layer, inner, outer, (0.0, 0.0), 1.0, 0.5)
        self.assertAlmostEqual(inner[0, 0], bilinear_runtime_grid(outer, (0.0, 0.0), 1.0, 1.0, 1.0))
        self.assertAlmostEqual(inner[2, 2], bilinear_runtime_grid(outer, (0.0, 0.0), 1.0, 3.0, 3.0))
        self.assertEqual(inner[1, 1], 100.0)

    def test_endpoint_aligned_error_resampling_preserves_a_plane(self) -> None:
        coarse = np.array([[0.0, 2.0], [2.0, 4.0]], dtype=np.float32)
        reconstructed = resample_runtime_grid(coarse, 2.0)
        np.testing.assert_allclose(
            reconstructed,
            np.array([[0.0, 1.0, 2.0], [1.0, 2.0, 3.0], [2.0, 3.0, 4.0]], dtype=np.float32),
            atol=1e-6,
        )


if __name__ == "__main__":
    unittest.main()
