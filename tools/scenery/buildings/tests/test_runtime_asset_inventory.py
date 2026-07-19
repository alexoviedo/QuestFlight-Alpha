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
        self.assertEqual(expected["asset_count"], 24)
        self.assertTrue(all(record["sha256"] for record in expected["records"]))


if __name__ == "__main__":
    unittest.main()
