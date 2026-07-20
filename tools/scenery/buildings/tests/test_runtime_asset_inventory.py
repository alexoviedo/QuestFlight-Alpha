from __future__ import annotations

import importlib.util
from pathlib import Path
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "generate_runtime_asset_inventory.py"
SPEC = importlib.util.spec_from_file_location("building_runtime_inventory", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
SPEC.loader.exec_module(MODULE)


class BuildingRuntimeAssetInventoryTests(unittest.TestCase):
    def test_committed_inventory_matches_all_generated_building_assets(self) -> None:
        expected = MODULE.build_inventory()
        actual = MODULE.DEFAULT_OUTPUT.read_bytes()
        self.assertEqual(MODULE.canonical_bytes(expected), actual)
        batch = MODULE.json.loads(
            (MODULE.BUILDING_ROOT / "KBDU_BuildingBatchReplacementV1.json").read_text(encoding="utf-8")
        )
        self.assertEqual(
            expected["asset_count"],
            batch["statistics"]["replacement_renderer_count"] + 3,
        )
        self.assertTrue(all(record["sha256"] for record in expected["records"]))
        material = expected["material_dependency_contract"]
        self.assertEqual(material["atlas_grid"], [4, 4])
        self.assertEqual(material["incremental_texture_bytes"], 0)
        self.assertEqual(material["mrao_channels"], {
            "r": "metallic", "g": "roughness", "b": "ambient_occlusion"
        })
        dependencies = material["reused_runtime_dependencies"]
        self.assertEqual(len(dependencies), 3)
        self.assertEqual(
            {Path(record["path"]).name for record in dependencies},
            {
                "AirportCoreHangarKit_Albedo.png",
                "AirportCoreHangarKit_Normal.png",
                "AirportCoreHangarKit_MRAO.png",
            },
        )
        self.assertTrue(all(record["sha256"] for record in dependencies))
        self.assertNotIn(
            "KBDU_ContextAtlas.png",
            {Path(record["path"]).name for record in dependencies},
        )


if __name__ == "__main__":
    unittest.main()
