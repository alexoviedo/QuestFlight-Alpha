import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[3]
MODULE_PATH = REPO_ROOT / "tools" / "kbdu_hlod_v2" / "generate_hlod_report.py"
SPEC = importlib.util.spec_from_file_location("generate_hlod_report", MODULE_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class CurrentRepositoryAuditTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.config = json.loads(
            (REPO_ROOT / "tools" / "kbdu_hlod_v2" / "config.json").read_text(encoding="utf-8")
        )
        cls.report = MODULE.build_report(REPO_ROOT, cls.config)

    def test_current_inventory_is_read_from_serialized_sources(self):
        placement = self.report["placement_inventory"]
        recipe = self.report["recipe_inventory"]
        tiers = self.report["mesh_tier_totals"]
        placement_source = json.loads(
            (REPO_ROOT / self.config["paths"]["placement_manifest"]).read_text(encoding="utf-8")
        )
        recipe_source = json.loads(
            (REPO_ROOT / self.config["paths"]["recipe"]).read_text(encoding="utf-8")
        )
        recipe_keys = (
            ("building_runtime_batches", "vegetation_runtime_batches", "parked_aircraft_runtime_batches")
            if "building_runtime_batches" in recipe_source
            else ("building_batches", "vegetation_batches", "parked_aircraft_batches")
        )
        expected_batches = sum(len(recipe_source.get(name, [])) for name in recipe_keys)
        expected_cells = {
            item.get("lod_hlod", {}).get("cell_id")
            for item in placement_source["placements"]
            if item.get("lod_hlod", {}).get("cell_id")
        }
        self.assertEqual(placement["placement_count"], len(placement_source["placements"]))
        self.assertEqual(placement["unique_cell_count"], len(expected_cells))
        self.assertEqual(placement["cell_size_meters"], placement_source["cell_size_meters"])
        self.assertEqual(recipe["batch_count"], expected_batches)
        expected_cell_size = recipe_source.get("logical_cell_size_meters")
        if expected_cell_size is None:
            expected_cell_size = recipe_source["integration"]["building_spatial_batch_cell_meters"]
        self.assertEqual(recipe["logical_cell_size_meters"], expected_cell_size)
        expected_ids = {
            batch["id"] for key in recipe_keys for batch in recipe_source.get(key, [])
        }
        for level, totals in tiers.items():
            self.assertEqual(
                totals["mesh_count"],
                sum(
                    (REPO_ROOT / self.config["paths"]["mesh_root"] / f"{batch_id}_{level}.asset").is_file()
                    for batch_id in expected_ids
                ),
            )

    def test_classification_matches_all_fail_closed_checks(self):
        failed = [item for item in self.report["checks"] if item["status"] == "FAIL"]
        if failed:
            self.assertTrue(self.report["classification"].startswith("FAIL_"))
            self.assertEqual(len(self.report["blocking_reasons"]), len(failed))
        else:
            self.assertTrue(self.report["classification"].startswith("PASS_"))
            self.assertEqual(self.report["blocking_reasons"], [])

    def test_compatible_sources_do_not_become_runtime_performance_claims(self):
        self.assertTrue(all(item["compatible"] for item in self.report["shaders"].values()))
        self.assertTrue(all(item["compatible"] for item in self.report["materials"].values()))
        self.assertEqual(self.report["evidence_kind"], "STATIC_OFFLINE_PLANNING_ONLY")
        self.assertFalse(self.report["hardware_performance_claim_allowed"])
        self.assertFalse(self.report["headset_performance_established"])
        self.assertEqual(
            self.report["context_subset_estimate"]["scope"],
            "serialized_context_subset_only_not_whole_product",
        )

    def test_report_serialization_is_deterministic(self):
        with tempfile.TemporaryDirectory() as first, tempfile.TemporaryDirectory() as second:
            paths_a = MODULE.write_report(self.report, Path(first))
            paths_b = MODULE.write_report(self.report, Path(second))
            self.assertEqual(paths_a[0].read_bytes(), paths_b[0].read_bytes())
            self.assertEqual(paths_a[1].read_bytes(), paths_b[1].read_bytes())


class ParserContractTests(unittest.TestCase):
    def test_missing_hlod_meshes_fail_the_report_gate(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            documents = {
                "recipe.json": {
                    "building_batches": [{"id": "cell_0"}],
                    "vegetation_batches": [],
                    "parked_aircraft_batches": [],
                    "integration": {"building_spatial_batch_cell_meters": 250.0},
                },
                "placement.json": {
                    "cell_size_meters": 250.0,
                    "placements": [{"category": "building", "lod_hlod": {"cell_id": "cell_0", "class": "near_3d"}}],
                },
                "coverage.json": {"v2_acceptance_status": "PASS"},
                "vegetation.json": {
                    "distribution_source": {"status": "READY"},
                    "species_library": {"status": "READY"},
                },
            }
            for name, content in documents.items():
                (root / name).write_text(json.dumps(content), encoding="utf-8")
            config = {
                "paths": {
                    "recipe": "recipe.json",
                    "placement_manifest": "placement.json",
                    "building_coverage": "coverage.json",
                    "vegetation_manifest": "vegetation.json",
                    "mesh_root": "meshes",
                    "prefab": "context.prefab",
                    "context_atlas": "atlas.png",
                },
                "required_lod_levels": ["LOD0", "LOD1", "LOD2"],
                "required_shader_tokens": [],
                "shader_paths": [],
                "material_paths": [],
                "cell_size_meters_range": [200.0, 300.0],
                "android_astc_texture_format": 50,
                "astc_block_size": 6,
                "limits": {
                    "visible_draw_slots": 130,
                    "visible_triangles": 650000,
                    "visible_materials": 30,
                    "texture_memory_bytes": 230686720,
                    "transparent_material_slots": 3,
                },
                "integrated_product_budget": {
                    "visible_draw_slots": 1,
                    "visible_triangles": 1,
                    "visible_materials": 1,
                    "texture_memory_bytes": 1,
                    "transparent_material_slots": 0,
                },
            }
            report = MODULE.build_report(root, config)
            checks = {item["id"]: item["status"] for item in report["checks"]}
            self.assertEqual(checks["three_level_mesh_coverage"], "FAIL")
            self.assertTrue(report["classification"].startswith("FAIL_"))

    def test_mesh_parser_rejects_non_triangle_index_count(self):
        with tempfile.TemporaryDirectory() as directory:
            mesh = Path(directory) / "bad.asset"
            mesh.write_text(
                "  m_SubMeshes:\n  - indexCount: 5\n    topology: 0\n  m_VertexCount: 4\n",
                encoding="utf-8",
            )
            evidence = MODULE._mesh_evidence(mesh)
            self.assertFalse(evidence["valid"])

    def test_astc_estimate_includes_complete_mip_chain(self):
        self.assertEqual(MODULE._astc_mip_bytes(1024, 1024, 6), 626288)


if __name__ == "__main__":
    unittest.main()
