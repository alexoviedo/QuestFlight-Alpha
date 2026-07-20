import importlib.util
import json
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[3]
MODULE_PATH = REPO_ROOT / "tools" / "kbdu_hlod_v2" / "build_runtime_hlod_recipe.py"
SPEC = importlib.util.spec_from_file_location("build_runtime_hlod_recipe", MODULE_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class RuntimeHlodRecipeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.config = json.loads((REPO_ROOT / "tools/kbdu_hlod_v2/config.json").read_text(encoding="utf-8"))
        logical_path = REPO_ROOT / cls.config["runtime_hlod"]["logical_recipe_path"]
        context_path = REPO_ROOT / cls.config["runtime_hlod"]["context_path"]
        cls.logical = json.loads(logical_path.read_text(encoding="utf-8"))
        cls.runtime = MODULE.build_runtime_recipe(
            cls.logical,
            json.loads(context_path.read_text(encoding="utf-8")),
            cls.config,
            MODULE._sha256(logical_path),
            MODULE._sha256(context_path),
        )

    def test_every_logical_building_cell_is_preserved_once(self):
        expected = sorted(batch["id"] for batch in self.logical["building_batches"])
        actual = [
            cell_id
            for batch in self.runtime["building_runtime_batches"]
            for cell_id in batch["logical_batch_ids"]
        ]
        self.assertEqual(sorted(actual), expected)
        self.assertEqual(len(actual), len(set(actual)))

    def test_runtime_grouping_meets_integrated_static_limits(self):
        budget = self.runtime["static_budget"]
        self.assertEqual(budget["status"], "PASS")
        estimates = budget["estimates"]
        limits = budget["limits"]
        self.assertEqual(len(self.runtime["building_runtime_batches"]), 20)
        self.assertEqual(estimates["context_batch_count"], 25)
        self.assertLessEqual(estimates["integrated_visible_draw_slots"], limits["visible_draw_slots"])
        self.assertLessEqual(estimates["integrated_visible_triangles"], limits["visible_triangles"])
        self.assertLessEqual(
            estimates["integrated_aggregate_serialized_renderers"],
            limits["aggregate_serialized_renderers"],
        )
        self.assertLessEqual(
            estimates["integrated_aggregate_serialized_triangles"],
            limits["aggregate_serialized_triangles"],
        )
        self.assertLessEqual(estimates["maximum_single_mesh_vertices"], 200000)

    def test_all_batches_define_deterministic_three_level_geometry(self):
        batches = (
            self.runtime["building_runtime_batches"]
            + self.runtime["vegetation_runtime_batches"]
            + self.runtime["parked_aircraft_runtime_batches"]
        )
        for batch in batches:
            self.assertEqual([row["level"] for row in batch["lod_policy"]], ["LOD0", "LOD1", "LOD2"])
            self.assertTrue(all(row["estimated_triangles"] > 0 for row in batch["lod_policy"]))
            self.assertTrue(all(row["estimated_vertices"] > 0 for row in batch["lod_policy"]))

    def test_every_vegetation_cell_placement_and_species_is_preserved(self):
        expected_cells = sorted(batch["id"] for batch in self.logical["vegetation_batches"])
        actual_cells = [
            cell_id
            for batch in self.runtime["vegetation_runtime_batches"]
            for cell_id in batch["logical_batch_ids"]
        ]
        expected_placements = [
            placement
            for batch in self.logical["vegetation_batches"]
            for placement in batch["placements"]
        ]
        self.assertEqual(sorted(actual_cells), expected_cells)
        self.assertEqual(len(actual_cells), len(set(actual_cells)))
        self.assertEqual(
            sum(batch["placement_count"] for batch in self.runtime["vegetation_runtime_batches"]),
            len(expected_placements),
        )
        self.assertEqual(
            self.runtime["vegetation_species_ids"],
            sorted({placement["species_id"] for placement in expected_placements}),
        )

    def test_generation_is_byte_deterministic(self):
        first = json.dumps(self.runtime, separators=(",", ":"), sort_keys=True)
        second = json.dumps(self.runtime, separators=(",", ":"), sort_keys=True)
        self.assertEqual(first.encode("utf-8"), second.encode("utf-8"))
        self.assertFalse(self.runtime["hardware_performance_established"])


if __name__ == "__main__":
    unittest.main()
