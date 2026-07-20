from __future__ import annotations

import pathlib
import sys
import unittest
from unittest.mock import patch

import numpy as np
from PIL import Image, ImageDraw


TOOL_ROOT = pathlib.Path(__file__).resolve().parents[1]
REPO_ROOT = TOOL_ROOT.parents[2]
sys.path.insert(0, str(TOOL_ROOT))

from build_workstream_b import build_atlas, infer_sun  # noqa: E402
from validate_workstream_b import validate  # noqa: E402


REPLACEMENTS = REPO_ROOT / "tools/kbdu_airport_core_v4/buildings/airport_core_replacement_footprints.json"


class WorkstreamBTests(unittest.TestCase):
    def test_atlas_tile_zero_is_in_runtime_bottom_left(self) -> None:
        slots = [f"procedural_test_{index}" for index in range(16)]

        def solid_tile(name: str, size: int, _rng: object) -> tuple[Image.Image, Image.Image, Image.Image]:
            value = int(name.rsplit("_", 1)[1]) + 1
            image = Image.new("RGB", (size, size), (value, 0, 0))
            normal = Image.new("RGB", (size, size), (128, 128, 255))
            mrao = Image.new("RGB", (size, size), (0, 180, 255))
            return image, normal, mrao

        with patch("build_workstream_b.procedural_tile", side_effect=solid_tile):
            albedo, _normal, _mrao, layout = build_atlas(slots, 16, {}, 1)

        self.assertEqual((1, 0, 0), albedo.getpixel((1, 14)))
        self.assertEqual((13, 0, 0), albedo.getpixel((1, 1)))
        self.assertLess(layout[0]["uvRect"][1], layout[12]["uvRect"][1])

    def test_current_static_authoring_assets_validate(self) -> None:
        report = validate(REPO_ROOT, REPLACEMENTS)
        self.assertTrue(report["passed"], report["issues"])

    def test_shadow_fit_uses_both_explicit_replacements(self) -> None:
        config = {
            "cleanPlate": {"nearHalfExtentMeters": 100.0},
            "lighting": {
                "azimuthStepDegrees": 5,
                "elevationRangeDegrees": [30, 65],
                "elevationStepDegrees": 5,
                "knownBuildingHeightsMeters": {"executive_terminal": 8.0, "type_2_t_hangar_row": 6.0},
                "acquisitionDates": ["2019-08-03", "2019-09-19"],
            },
        }
        replacements = [
            {"object_id": "terminal", "asset_id": "ExecutiveTerminal", "source_footprint_xz_meters": [[-45, -10], [-25, -10], [-25, 10], [-45, 10]]},
            {"object_id": "hangar", "asset_id": "Type2THangarRow", "source_footprint_xz_meters": [[15, -25], [27, -25], [27, 25], [15, 25]]},
        ]
        image = Image.new("RGB", (512, 512), (165, 165, 165))
        draw = ImageDraw.Draw(image)
        for item in replacements:
            points = [((x + 100) / 200 * 512, (100 - z) / 200 * 512) for x, z in item["source_footprint_xz_meters"]]
            draw.polygon(points, fill=(205, 205, 205))
            shadow = [(x - 18, y - 18) for x, y in points]
            draw.polygon(shadow, fill=(55, 55, 55))
        result = infer_sun(image, replacements, config)
        self.assertEqual(2, result["sampleCount"])
        self.assertLessEqual(abs((result["shadowAzimuthDegrees"] - 315 + 180) % 360 - 180), 30, result)
        self.assertLessEqual(result["confidence"], 0.65)


if __name__ == "__main__":
    unittest.main()
