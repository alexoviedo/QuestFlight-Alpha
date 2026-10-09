from __future__ import annotations

import json
import math
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = SCRIPT_DIR.parents[1]
sys.path.insert(0, str(SCRIPT_DIR))

from generate_content_contracts import generate  # noqa: E402


OUTPUTS = (
    "KBDU_ContextBatchRecipeV2.json",
    "KBDU_ReferenceContextV2.json",
    "airport_parking_stand_manifest.json",
    "building_coverage_report.json",
    "content_pipeline_report.json",
    "production_placement_manifest.json",
    "vegetation_distribution_manifest.json",
)


class ContentContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.config = SCRIPT_DIR / "config.json"
        if not (SCRIPT_DIR / "osm_buildings_6km_source.json").is_file():
            raise unittest.SkipTest("Full historical source regeneration requires the pinned external normalized 6 km OSM input; focused current aircraft composition tests run independently.")
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary)
            generate(REPO_ROOT, cls.config, output)
            cls.generated = {name: (output / name).read_bytes() for name in OUTPUTS}
        cls.documents = {
            name: json.loads(payload.decode("utf-8"))
            for name, payload in cls.generated.items()
        }

    def test_checked_in_outputs_are_byte_deterministic(self) -> None:
        for name in OUTPUTS:
            self.assertEqual((SCRIPT_DIR / name).read_bytes(), self.generated[name], name)

    def test_schema_contracts_are_present_and_versioned(self) -> None:
        names = {
            "airport_parking_stand_manifest.schema.json",
            "building_coverage_report.schema.json",
            "kbdu_context_batch_recipe_v2.schema.json",
            "production_placement_manifest.schema.json",
            "vegetation_distribution_manifest.schema.json",
            "vegetation_source_v2.schema.json",
            "vegetation_species_library_v2.schema.json",
            "vegetation_coverage_report.schema.json",
        }
        observed = {path.name for path in (SCRIPT_DIR / "schemas").glob("*.schema.json")}
        self.assertEqual(observed, names)
        for name in names:
            schema = json.loads((SCRIPT_DIR / "schemas" / name).read_text(encoding="utf-8"))
            self.assertEqual(schema["$schema"], "https://json-schema.org/draft/2020-12/schema")
            self.assertEqual(schema["type"], "object")

    def test_building_coverage_is_honest_about_checkpoint_extent(self) -> None:
        report = self.documents["building_coverage_report.json"]
        self.assertEqual(report["cell_size_meters"], 250.0)
        self.assertEqual(report["v2_acceptance_status"], "PASS")
        self.assertEqual(report["zones"]["0-2km"]["source_footprints"], 1831)
        self.assertEqual(report["zones"]["2-4km"]["source_footprints"], 7863)
        self.assertEqual(report["zones"]["4-6km"]["source_footprints"], 17985)
        self.assertTrue(all(value["status"] == "PASS" for value in report["zones"].values()))
        self.assertEqual(report["zones"]["0-2km"]["coverage_percent_of_committed_source"], 100.0)
        self.assertGreaterEqual(report["zones"]["2-4km"]["coverage_percent_of_committed_source"], 95.0)
        self.assertGreaterEqual(report["zones"]["4-6km"]["significant_coverage_percent"], 90.0)
        self.assertGreater(
            report["checkpoint_source_limitations"]["maximum_committed_building_centroid_distance_meters"],
            5900.0,
        )

    def test_every_placement_has_unique_stable_id_and_250m_cell(self) -> None:
        manifest = self.documents["production_placement_manifest.json"]
        placements = manifest["placements"]
        identifiers = [placement["stable_id"] for placement in placements]
        self.assertEqual(len(identifiers), len(set(identifiers)))
        self.assertEqual(len(placements), 27679 + 377 + 35)
        self.assertTrue(all(placement["lod_hlod"]["cell_id"].startswith("cell_250m_") for placement in placements))
        self.assertEqual(
            {placement["category"] for placement in placements},
            {"building", "parked_aircraft", "vegetation"},
        )

    def test_parking_stands_and_corrected_aircraft_are_bounded(self) -> None:
        manifest = self.documents["airport_parking_stand_manifest.json"]
        self.assertEqual(manifest["stand_count"], 5)
        self.assertEqual(manifest["placement_count"], 3)
        self.assertTrue(all(stand["apron_contained"] for stand in manifest["stands"]))
        active = [stand for stand in manifest["stands"] if stand["active_parked_aircraft"]]
        self.assertEqual(len(active), 3)
        for first_index, first in enumerate(active):
            for second in active[first_index + 1 :]:
                dx = first["local_enu_meters"]["x"] - second["local_enu_meters"]["x"]
                dz = first["local_enu_meters"]["z"] - second["local_enu_meters"]["z"]
                self.assertGreater(math.hypot(dx, dz), 25.0)
        calibration = manifest["aircraft_model_calibration"]
        self.assertEqual(calibration["source_model_axes"], {"forward": "-Y", "right": "+X", "up": "+Z"})
        self.assertEqual(calibration["model_forward_axis_correction_degrees"], 90.0)
        contacts = calibration["gear_contact_points_unity_meters"]
        self.assertEqual(set(contacts), {"main_left", "main_right", "nose"})
        self.assertTrue(all(contact[1] == 0.0 for contact in contacts.values()))
        for placement in manifest["parked_aircraft_placements"]:
            self.assertEqual(
                placement["root_yaw_degrees"],
                (placement["authored_heading_degrees"] + placement["model_forward_axis_correction_degrees"]) % 360.0,
            )

    def test_v2_recipe_replaces_vegetation_and_corrects_parked_aircraft(self) -> None:
        source_path = REPO_ROOT / "QuestFlight/Assets/Production/Environment/PhotorealTerrainV1/KBDU_ContextBatchRecipe.json"
        source = json.loads(source_path.read_text(encoding="utf-8"))
        generated = self.documents["KBDU_ContextBatchRecipeV2.json"]
        self.assertNotEqual(generated["vegetation_batches"], source["vegetation_batches"])
        self.assertEqual(sum(len(batch["placements"]) for batch in generated["vegetation_batches"]), 377)
        self.assertTrue(all(batch["id"].startswith("vegetation_v2_cell_250m_") for batch in generated["vegetation_batches"]))
        self.assertEqual(generated["atlas_grid"], source["atlas_grid"])
        self.assertEqual(generated["coordinate_frame"], source["coordinate_frame"])
        self.assertEqual(generated["coordinate_quantization_meters"], source["coordinate_quantization_meters"])
        self.assertEqual(generated["integration"]["building_spatial_batch_cell_meters"], 250.0)
        self.assertTrue(all(batch["id"].startswith("buildings_") for batch in generated["building_batches"]))
        self.assertEqual(sum(len(batch["buildings"]) for batch in generated["building_batches"]), 27679)
        self.assertEqual(
            [batch["id"] for batch in generated["parked_aircraft_batches"]],
            [batch["id"] for batch in source["parked_aircraft_batches"]],
        )
        recipe_placements = generated["parked_aircraft_batches"][0]["placements"]
        self.assertEqual(len(recipe_placements), 35)
        self.assertEqual(sum(row["model_family"] == "powered_c172" for row in recipe_placements),17)
        self.assertEqual(sum(row["model_family"] == "sailplane" for row in recipe_placements),18)
        for row in recipe_placements:
            self.assertEqual(row["placement_v2"]["orthophoto_alignment"],"active_2020_city_aircraft_silhouette")
            self.assertEqual(row["base_y_meters"],row["placement_v2"]["terrain_height_meters"])

    def test_vegetation_policy_is_multiscale_and_replaces_legacy_sources(self) -> None:
        manifest = self.documents["vegetation_distribution_manifest.json"]
        self.assertEqual(manifest["distribution_source"]["accepted_placement_count"], 377)
        self.assertEqual(manifest["distribution_source"]["legacy_candidate_count_replaced"], 246)
        self.assertEqual(manifest["distribution_source"]["status"], "PASS_NATURAL_COLOR_FALLBACK")
        self.assertEqual(
            [(value["minimum_distance_meters"], value["maximum_distance_meters"]) for value in manifest["lod_policy"]],
            [(0.0, 250.0), (250.0, 600.0), (600.0, 1500.0), (1500.0, None)],
        )
        self.assertFalse(manifest["stereo_policy"]["single_vertical_billboard_allowed"])
        self.assertTrue(manifest["stereo_policy"]["top_down_and_oblique_canopy_required"])
        self.assertEqual(manifest["species_library"]["status"], "PROJECT_OWNED_PROCEDURAL_LOD0_LOD1_LOD2_AUTHORED")
        self.assertEqual(len(manifest["species_library"]["species"]), 4)
        self.assertEqual(manifest["validation"]["status"], "PASS")
        placements = self.documents["production_placement_manifest.json"]["placements"]
        vegetation = [placement for placement in placements if placement["category"] == "vegetation"]
        self.assertEqual(len(vegetation), 377)
        self.assertTrue(all(placement["distribution"]["status"] == "V2_MASK_AND_EXCLUSIONS_VALIDATED" for placement in vegetation))
        self.assertNotIn("vegetation_candidate", {placement["category"] for placement in placements})

    def test_pipeline_report_pins_inputs_and_disclaims_unity_changes(self) -> None:
        report = self.documents["content_pipeline_report.json"]
        self.assertFalse(report["unity_assets_modified"])
        self.assertEqual(
            set(report["input_hashes"]),
            {
                "context", "context_recipe", "osm_building_source_6km",
                "parked_aircraft_source_manifest", "terrain_rings",
                "vegetation_coverage_v2", "vegetation_source_v2", "vegetation_species_v2",
            },
        )
        self.assertEqual(len(report["generated_outputs"]), 6)
        self.assertTrue(all(len(value) == 64 for value in report["input_hashes"].values()))

    def test_extended_context_contains_complete_current_building_source(self) -> None:
        context = self.documents["KBDU_ReferenceContextV2.json"]
        buildings = [value for value in context["openstreetmap"]["features"] if value["category"] == "building"]
        self.assertEqual(len(buildings), 27679)
        self.assertEqual(context["coverage"]["airport_context_width_meters"], 12000.0)
        self.assertEqual(context["openstreetmap"]["evidence"]["dropped_or_budget_trimmed_parts"], 0)
        self.assertIn("building_source_raw_response_sha256", context["openstreetmap"]["evidence"])


if __name__ == "__main__":
    unittest.main()
