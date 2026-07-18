from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


TOOL_DIR = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("generator", TOOL_DIR / "generate_benchmark_assets.py")
GENERATOR = importlib.util.module_from_spec(SPEC)
assert SPEC.loader
SPEC.loader.exec_module(GENERATOR)


class WaterVegetationAssetTests(unittest.TestCase):
    def test_periodic_wave_normal_is_deterministic(self) -> None:
        with tempfile.TemporaryDirectory() as first, tempfile.TemporaryDirectory() as second:
            path_a = Path(first) / "wave.png"
            path_b = Path(second) / "wave.png"
            report_a = GENERATOR.generate_wave_normal(path_a, 64, 0.37, True)
            report_b = GENERATOR.generate_wave_normal(path_b, 64, 0.37, True)
            self.assertEqual(report_a["sha256"], report_b["sha256"])
            self.assertTrue(report_a["seamless"])

    def test_minor_water_mask_is_bounded_and_normalized(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "minor.png"
            report = GENERATOR.generate_minor_water_mask(path, 128)
            self.assertFalse(report["georeferenced"])
            self.assertGreater(report["water_coverage_fraction"], 0.005)
            self.assertLess(report["water_coverage_fraction"], 0.10)
            self.assertGreater(report["wet_bank_coverage_fraction"], 0.005)

    def test_vegetation_atlas_has_alpha_test_coverage(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "atlas.png"
            report = GENERATOR.generate_vegetation_atlas(path, 256)
            self.assertGreater(report["covered_pixel_fraction"], 0.20)
            self.assertLess(report["covered_pixel_fraction"], 0.90)
            width, height, pixels = GENERATOR.read_png_rgba(path)
            self.assertEqual((width, height, len(pixels)), (256, 256, 256 * 256 * 4))

    def test_shader_templates_preserve_quest_contract(self) -> None:
        water = (TOOL_DIR / "templates/KBDUReservoirWaterV4.shader").read_text(encoding="utf-8")
        vegetation = (TOOL_DIR / "templates/KBDUVegetationHeroV4.shader").read_text(encoding="utf-8")
        self.assertIn("ZWrite On", water)
        self.assertIn('"RenderType"="Opaque"', water)
        self.assertNotIn("GrabPass", water)
        self.assertNotIn("Blend SrcAlpha", water)
        self.assertIn("TransparentCutout", vegetation)
        self.assertIn("Blend Off", vegetation)
        self.assertIn("#pragma multi_compile_instancing", vegetation)

    def test_planned_astc_texture_cost_is_bounded(self) -> None:
        total = (
            GENERATOR.astc_bytes(512, 512, 6) * 3
            + GENERATOR.astc_bytes(1024, 1024, 6)
            + GENERATOR.astc_bytes(1024, 1024, 4)
        )
        self.assertLessEqual(total, 3 * 1024 * 1024)


if __name__ == "__main__":
    unittest.main()
