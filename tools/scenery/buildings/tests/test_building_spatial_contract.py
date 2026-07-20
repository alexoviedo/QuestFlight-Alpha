import importlib.util
import json
import math
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[4]
MODULE_PATH = REPO_ROOT / "tools/scenery/buildings/generate_building_spatial_contract.py"
SPEC = importlib.util.spec_from_file_location("generate_building_spatial_contract", MODULE_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class BuildingSpatialContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.config_path = REPO_ROOT / "tools/scenery/buildings/config.json"
        cls.config = MODULE.load_json(cls.config_path)
        cls.context_path = REPO_ROOT / cls.config["source_context_path"]
        cls.recipe_path = REPO_ROOT / cls.config["source_recipe_path"]
        cls.replacements_path = REPO_ROOT / cls.config["hero_replacements_path"]
        cls.output_path = REPO_ROOT / cls.config["output_manifest_path"]
        cls.batch_output_path = REPO_ROOT / cls.config["output_batch_replacement_manifest_path"]
        cls.lidar_path = REPO_ROOT / cls.config["optional_lidar_manifest_path"]
        cls.material_manifest_path = REPO_ROOT / cls.config["material_contract"][
            "atlas_source_manifest_path"
        ]
        cls.material_albedo_path = REPO_ROOT / cls.config["material_contract"]["albedo_path"]
        cls.material_normal_path = REPO_ROOT / cls.config["material_contract"]["normal_path"]
        cls.material_mrao_path = REPO_ROOT / cls.config["material_contract"]["mrao_path"]
        cls.context = MODULE.load_json(cls.context_path)
        cls.recipe = MODULE.load_json(cls.recipe_path)
        cls.contract = MODULE.load_json(cls.output_path)

    def test_committed_contract_is_byte_deterministic_and_source_pinned(self):
        regenerated = MODULE.build_contract(
            self.context,
            self.recipe,
            MODULE.load_json(self.replacements_path),
            self.config,
            {
                "context": MODULE.sha256(self.context_path),
                "recipe": MODULE.sha256(self.recipe_path),
                "hero_replacements": MODULE.sha256(self.replacements_path),
                "config": MODULE.sha256(self.config_path),
                "generator": MODULE.sha256(MODULE_PATH),
                "material_manifest": MODULE.sha256(self.material_manifest_path),
                "material_albedo": MODULE.sha256(self.material_albedo_path),
                "material_normal": MODULE.sha256(self.material_normal_path),
                "material_mrao": MODULE.sha256(self.material_mrao_path),
            },
            MODULE.load_json(self.lidar_path),
            self.lidar_path.relative_to(REPO_ROOT).as_posix(),
            MODULE.sha256(self.lidar_path),
        )
        self.assertEqual(MODULE.canonical_bytes(regenerated), self.output_path.read_bytes())
        self.assertEqual(
            regenerated["height_input"]["input_path"],
            self.lidar_path.relative_to(REPO_ROOT).as_posix(),
        )
        self.assertEqual(regenerated["height_input"]["input_sha256"], MODULE.sha256(self.lidar_path))
        self.assertFalse(regenerated["height_input"]["eligible_for_authoritative_override"])
        self.assertFalse(regenerated["height_input"]["meets_10_percent_vertical_error_gate"])
        self.assertEqual(regenerated["statistics"]["lidar_height_override_count"], 0)
        self.assertEqual(regenerated["material_contract"]["incremental_texture_bytes"], 0)
        batch_regenerated = MODULE.build_batch_replacement_manifest(
            regenerated,
            self.recipe,
            MODULE.load_json(REPO_ROOT / self.config["legacy_runtime_recipe_path"]),
            self.config,
            MODULE.sha256(REPO_ROOT / self.config["legacy_runtime_recipe_path"]),
        )
        self.assertEqual(MODULE.canonical_bytes(batch_regenerated), self.batch_output_path.read_bytes())

    def test_batch_manifest_maps_every_legacy_renderer_and_far_batches_to_no_geometry(self):
        batch_manifest = MODULE.load_json(self.batch_output_path)
        runtime = MODULE.load_json(REPO_ROOT / self.config["legacy_runtime_recipe_path"])
        self.assertEqual(
            batch_manifest["legacy_group_ids_to_disable"],
            [batch["id"] for batch in runtime["building_runtime_batches"]],
        )
        self.assertEqual(len(batch_manifest["legacy_renderer_ids_to_disable"]), 60)
        self.assertEqual(batch_manifest["statistics"]["hero_replacement_source_count_omitted"], 2)
        self.assertEqual(batch_manifest["statistics"]["ordinary_far_geometry_count"], 0)
        fixed_shell_groups = 0
        for batch in batch_manifest["batches"]:
            if not batch["represented_feature_indices"]:
                self.assertIsNone(batch["new_group_id"])
                self.assertEqual(batch["new_renderer_ids"], [])
                self.assertIn("ORTHOPHOTO_ONLY", batch["integration_action"])
            else:
                self.assertTrue(batch["new_group_id"].startswith("AerialBuildingsV1__"))
                self.assertEqual(len(batch["new_renderer_ids"]), 1)
                self.assertEqual(len(batch["lods"]), 1)
                fixed_shell_groups += 1
                self.assertEqual(batch["lods"][0]["level"], "FIXED")
                self.assertEqual(
                    batch["lods"][0]["mesh_mode"],
                    "CLOSED_TRUE_FOOTPRINT_SHELL_FIXED_RENDERER",
                )
            represented_far_ordinary = next(
                row["count"] for row in batch["represented_tier_counts"]
                if row["tier"] == "far_orthophoto"
            )
            self.assertEqual(represented_far_ordinary, 0)
            if batch["zone"] == "4-6km" and batch["represented_feature_indices"]:
                self.assertTrue(batch["new_group_id"].startswith("AerialBuildingsV1__mid_boundary_"))
                self.assertEqual(
                    sum(
                        row["count"]
                        for row in batch["represented_tier_counts"]
                        if row["tier"] != "mid"
                    ),
                    0,
                )
        self.assertEqual(batch_manifest["statistics"]["fixed_mid_group_count"], fixed_shell_groups)
        self.assertEqual(batch_manifest["statistics"]["near_reduction_lod_group_count"], 0)

    def test_near_is_exact_mid_is_bounded_and_far_is_orthophoto_only(self):
        features = self.context["openstreetmap"]["features"]
        maximum_ratio = 1.0 + self.config["mid_simplification"]["maximum_area_error_ratio"]
        tolerance = 1e-9
        self.assertLessEqual(
            self.contract["statistics"]["maximum_footprint_area_ratio"],
            maximum_ratio + tolerance,
        )
        allowlist = {
            (row["osm_type"], row["osm_id"], row.get("part", "geometry:0"))
            for row in self.contract["far_landmark_allowlist"]
        }
        represented_indices = {row["feature_index"] for row in self.contract["records"]}
        far_indices = set(self.contract["far_orthophoto_feature_indices"])
        source_building_indices = {
            index
            for index, feature in enumerate(features)
            if feature.get("category") == "building"
        }
        self.assertFalse(represented_indices & far_indices)
        self.assertEqual(represented_indices | far_indices, source_building_indices)
        self.assertEqual(
            len(self.contract["records"]),
            self.contract["statistics"]["serialized_record_count"],
        )
        for row in self.contract["records"]:
            source = MODULE.decode_points_q(features[row["feature_index"]])
            geometry = list(zip(row["geometry_points_q"][0::2], row["geometry_points_q"][1::2]))
            if row["tier"] in ("hero_core", "near"):
                self.assertEqual(geometry, source)
                self.assertEqual(row["footprint_area_ratio"], 1.0)
            elif row["tier"] == "mid":
                self.assertTrue(geometry)
                self.assertEqual(MODULE.bounds_q(geometry), MODULE.bounds_q(source))
                self.assertTrue(MODULE.polygon_is_simple(geometry))
                self.assertGreaterEqual(row["footprint_area_ratio"], 0.9)
                self.assertLessEqual(row["footprint_area_ratio"], maximum_ratio + tolerance)
            elif row["tier"] == "far_landmark":
                source_id = features[row["feature_index"]]["source"]
                self.assertIn(
                    (source_id["osm_type"], source_id["osm_id"], source_id.get("part", "geometry:0")),
                    allowlist,
                )
            else:
                self.fail(row["tier"])
        quantization = self.contract["coordinate_quantization_meters"]
        cutoff_radius = self.config["mid_geometry_selection"]["unconditional_radius_meters"]
        cutoff_area = self.config["mid_geometry_selection"][
            "minimum_footprint_area_square_meters_beyond_radius"
        ]
        for feature_index in far_indices:
            source = MODULE.decode_points_q(features[feature_index])
            centroid = MODULE.polygon_centroid_q(source)
            radius = math.hypot(centroid[0] * quantization, centroid[1] * quantization)
            source_area = abs(MODULE.signed_area2(source)) * quantization * quantization * 0.5
            self.assertTrue(
                radius >= self.config["tier_radii_meters"]["mid_max_exclusive"]
                or (radius >= cutoff_radius and source_area < cutoff_area)
            )

    def test_every_runtime_representation_is_a_fixed_closed_shell(self):
        batch_manifest = MODULE.load_json(self.batch_output_path)
        records = {row["feature_index"]: row for row in self.contract["records"]}
        total = 0
        for batch in batch_manifest["batches"]:
            if not batch["represented_feature_indices"]:
                continue
            self.assertEqual(len(batch["lods"]), 1)
            self.assertEqual(batch["lods"][0]["level"], "FIXED")
            expected_triangles = 0
            expected_vertices = 0
            for feature_index in batch["represented_feature_indices"]:
                record = records[feature_index]
                point_count = len(record["geometry_points_q"]) // 2
                if record["roof_profile"] == "gable":
                    self.assertEqual(point_count, 4)
                    self.assertGreater(record["roof_rise_meters"], 0.0)
                    expected_triangles += 14
                    expected_vertices += 30
                elif record["roof_profile"] == "hip":
                    self.assertGreater(record["roof_rise_meters"], 0.0)
                    expected_triangles += 3 * point_count
                    expected_vertices += 7 * point_count
                else:
                    self.assertEqual(record["roof_profile"], "flat")
                    self.assertEqual(record["roof_rise_meters"], 0.0)
                    expected_triangles += 3 * point_count - 2
                    expected_vertices += 5 * point_count
            self.assertEqual(batch["lods"][0]["triangles"], expected_triangles)
            self.assertEqual(batch["lods"][0]["vertices"], expected_vertices)
            total += expected_triangles
        self.assertEqual(
            batch_manifest["statistics"]["replacement_serialized_triangles"], total
        )
        self.assertEqual(
            batch_manifest["statistics"]["replacement_worst_case_triangles"], total
        )

    def test_material_tiles_use_only_existing_airport_core_pbr_cells(self):
        material = self.contract["material_contract"]
        self.assertEqual(material["atlas_grid"], [4, 4])
        self.assertEqual(material["mrao_channels"], {
            "r": "metallic", "g": "roughness", "b": "ambient_occlusion"
        })
        self.assertEqual(material["incremental_texture_bytes"], 0)
        for key, path in (
            ("atlas_source_manifest_sha256", self.material_manifest_path),
            ("albedo_sha256", self.material_albedo_path),
            ("normal_sha256", self.material_normal_path),
            ("mrao_sha256", self.material_mrao_path),
        ):
            self.assertEqual(material[key], MODULE.sha256(path))
        allowed_facades = set(material["allowed_facade_tiles"])
        allowed_roofs = set(material["allowed_roof_tiles"])
        self.assertEqual(allowed_facades, {0, 1, 5, 8, 10})
        self.assertEqual(allowed_roofs, {3, 10})
        self.assertTrue(all(record["facade_tile"] in allowed_facades
                            for record in self.contract["records"]))
        self.assertTrue(all(record["roof_tile"] in allowed_roofs
                            for record in self.contract["records"]))
        first = MODULE.build_contract(
            self.context,
            self.recipe,
            MODULE.load_json(self.replacements_path),
            self.config,
            {
                "context": MODULE.sha256(self.context_path),
                "recipe": MODULE.sha256(self.recipe_path),
                "hero_replacements": MODULE.sha256(self.replacements_path),
                "config": MODULE.sha256(self.config_path),
                "generator": MODULE.sha256(MODULE_PATH),
                "material_manifest": MODULE.sha256(self.material_manifest_path),
                "material_albedo": MODULE.sha256(self.material_albedo_path),
                "material_normal": MODULE.sha256(self.material_normal_path),
                "material_mrao": MODULE.sha256(self.material_mrao_path),
            },
            MODULE.load_json(self.lidar_path),
            self.lidar_path.relative_to(REPO_ROOT).as_posix(),
            MODULE.sha256(self.lidar_path),
        )
        self.assertEqual(
            [(row["feature_index"], row["facade_tile"], row["roof_tile"])
             for row in first["records"]],
            [(row["feature_index"], row["facade_tile"], row["roof_tile"])
             for row in self.contract["records"]],
        )

    def test_components_have_only_touching_or_intersecting_spanning_edges(self):
        records = {row["feature_index"]: row for row in self.contract["records"]}
        covered_multi_parts = set()
        removed_singleton_equivalents = 0
        for component_index, component in enumerate(self.contract["multi_part_components"]):
            members = component["feature_indices"]
            edges = list(zip(component["connectivity_edges"][0::2], component["connectivity_edges"][1::2]))
            self.assertGreater(len(members), 1)
            self.assertTrue(covered_multi_parts.isdisjoint(members))
            covered_multi_parts.update(members)
            removed_singleton_equivalents += len(members) - 1
            self.assertEqual(len(edges), max(0, len(members) - 1))
            for first, second in edges:
                self.assertIn(first, members)
                self.assertIn(second, members)
                first_points = records[first]["geometry_points_q"]
                second_points = records[second]["geometry_points_q"]
                self.assertTrue(
                    MODULE.polygons_intersect(
                        list(zip(first_points[0::2], first_points[1::2])),
                        list(zip(second_points[0::2], second_points[1::2])),
                    ),
                    f"component {component_index}: {first}/{second}",
                )
                self.assertEqual(records[first]["tier"], records[second]["tier"])
        self.assertEqual(
            len(records) - removed_singleton_equivalents,
            self.contract["statistics"]["component_count"],
        )

    def test_compact_records_preserve_recomputable_pivot_bounds_foundation_and_scale(self):
        foundations = {
            item["feature_index"]: item["base_y_meters"]
            for batch in self.recipe["building_batches"]
            for item in batch["buildings"]
        }
        self.assertEqual(self.contract["authored_scale_xyz"], [1.0, 1.0, 1.0])
        self.assertLess(self.output_path.stat().st_size, self.context_path.stat().st_size // 2)
        redundant_keys = {
            "component_id",
            "height_source",
            "pivot_xz_meters",
            "representation",
            "scale_xyz",
            "source_area_square_meters",
            "source_bounds_xz_q",
            "stable_id",
        }
        features = self.context["openstreetmap"]["features"]
        for row in self.contract["records"]:
            self.assertEqual(row["base_y_meters"], foundations[row["feature_index"]])
            self.assertGreater(row["height_meters"], 0.0)
            self.assertTrue(redundant_keys.isdisjoint(row))
            source = MODULE.decode_points_q(features[row["feature_index"]])
            geometry = list(zip(row["geometry_points_q"][0::2], row["geometry_points_q"][1::2]))
            self.assertEqual(MODULE.bounds_q(geometry), MODULE.bounds_q(source))
            self.assertEqual(len(MODULE.polygon_centroid_q(source)), 2)

    def test_unqualified_lidar_never_overrides_and_qualified_identity_data_can(self):
        rejected, rejected_summary = MODULE.qualified_lidar_heights(
            {
                "schema": "questflight.aerial-scenery-v1.lidar.v1",
                "eligible_for_authoritative_override": False,
                "meets_10_percent_vertical_error_gate": False,
                "vertical_error_fraction_bound": None,
                "building_heights": [
                    {"stable_id": "a", "height_meters": 10.0, "confidence": 1.0}
                ],
            },
            0.8,
            0.1,
        )
        self.assertEqual(rejected, {})
        self.assertTrue(rejected_summary["status"].startswith("REJECTED_"))
        wrong_schema, _ = MODULE.qualified_lidar_heights(
            {
                "schema": "unrelated.lidar.v1",
                "eligible_for_authoritative_override": True,
                "meets_10_percent_vertical_error_gate": True,
                "vertical_error_fraction_bound": 0.08,
                "building_heights": [
                    {
                        "stable_id": "a",
                        "height_meters": 10.0,
                        "confidence": 0.9,
                        "vertical_error_fraction": 0.08,
                    }
                ],
            },
            0.8,
            0.1,
        )
        self.assertEqual(wrong_schema, {})
        accepted, accepted_summary = MODULE.qualified_lidar_heights(
            {
                "schema": "questflight.aerial-scenery-v1.lidar.v1",
                "eligible_for_authoritative_override": True,
                "meets_10_percent_vertical_error_gate": True,
                "vertical_error_fraction_bound": 0.08,
                "building_heights": [
                    {
                        "stable_id": "a",
                        "height_meters": 10.0,
                        "confidence": 0.9,
                        "vertical_error_fraction": 0.08,
                    }
                ],
            },
            0.8,
            0.1,
        )
        self.assertEqual(accepted["a"]["height_meters"], 10.0)
        self.assertEqual(accepted_summary["qualified_identity_record_count"], 1)


if __name__ == "__main__":
    unittest.main()
