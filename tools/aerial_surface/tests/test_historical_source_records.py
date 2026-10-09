from __future__ import annotations

import hashlib
import copy
import json
import sys
import tempfile
import subprocess
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from validate_aerial_surface import (
    REVIEWED_CRLF_SOURCE_RECORDS,
    ValidationFailure,
    resolve_logical,
    verify_record,
    verify_historical_topology_common,
    HISTORICAL_TOPOLOGY_COMMON_IDENTITY,
    HISTORICAL_TOPOLOGY_SOURCE_COMMIT,
    EXPECTED_SCHEMAS,
    SOURCE_MANIFEST,
    RUNTIME_ROOT,
    DTM_DSM_MANIFEST_NAME,
    load_json,
    horizontal_frame_contract,
    validate_horizontal_frame_contract,
    verify_manifest_records,
)
from build_dtm_dsm import REPOSITORY_ROOT
from refresh_ownership_metadata import (
    OWNERSHIP_PATH,
    BUILDING_MANIFEST,
    prepare_refresh,
)
import validate_aerial_surface as validator


class HistoricalSourceTextRecordTests(unittest.TestCase):
    def setUp(self) -> None:
        self.logical_path = next(iter(REVIEWED_CRLF_SOURCE_RECORDS))
        size, digest = REVIEWED_CRLF_SOURCE_RECORDS[self.logical_path]
        self.record = {"path": self.logical_path, "bytes": size, "sha256": digest}
        self.original_lf = resolve_logical(self.logical_path).read_bytes()
        self.assertNotIn(b"\r", self.original_lf)
        self.original_crlf = self.original_lf.replace(b"\n", b"\r\n")
        self.assertEqual(len(self.original_crlf), size)
        self.assertEqual(hashlib.sha256(self.original_crlf).hexdigest(), digest)
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.file = Path(self.folder.name) / "source.json"

    def verify(self, payload: bytes, record: dict | None = None) -> list[dict]:
        self.file.write_bytes(payload)
        evidence = []
        with patch("validate_aerial_surface.resolve_logical", return_value=self.file):
            self.assertEqual(verify_record(record or self.record, evidence), self.file)
        return evidence

    def test_current_git_lf_matches_exact_historical_crlf_identity(self) -> None:
        evidence = self.verify(self.original_lf)
        self.assertEqual(evidence[0]["actual_bytes"], 61_027)
        self.assertEqual(evidence[0]["recorded_bytes"], 64_536)
        self.assertEqual(evidence[0]["actual_sha256"], hashlib.sha256(self.original_lf).hexdigest())
        self.assertEqual(evidence[0]["recorded_sha256"], self.record["sha256"])
        self.assertEqual(evidence[0]["verification_method"], "exact_recorded_crlf_reconstruction_from_git_lf")

    def test_exact_original_crlf_is_verified_without_conversion(self) -> None:
        self.assertEqual(self.verify(self.original_crlf)[0]["verification_method"], "exact_bytes")

    def test_changed_source_content_is_rejected_even_with_identical_size(self) -> None:
        with self.assertRaises(ValidationFailure):
            self.verify(self.original_lf.replace(b"Airport", b"AirporX", 1))

    def test_truncated_source_is_rejected(self) -> None:
        with self.assertRaises(ValidationFailure):
            self.verify(self.original_lf[:-1])

    def test_changed_record_hash_or_size_cannot_select_conversion(self) -> None:
        for field, value in (("bytes", 64_537), ("sha256", "0" * 64)):
            with self.subTest(field=field), self.assertRaises(ValidationFailure):
                self.verify(self.original_lf, {**self.record, field: value})

    def test_unreviewed_json_and_binary_records_remain_byte_exact(self) -> None:
        for path in ("tools/unreviewed.json", "assets/generated.json", "assets/input.bytes"):
            with self.subTest(path=path), self.assertRaises(ValidationFailure):
                self.verify(self.original_lf, {**self.record, "path": path})

    def test_mixed_line_endings_are_not_normalized(self) -> None:
        with self.assertRaises(ValidationFailure):
            self.verify(self.original_lf.replace(b"\n", b"\r\n", 1))

    def test_missing_source_fails_closed(self) -> None:
        with patch("validate_aerial_surface.resolve_logical", return_value=self.file):
            with self.assertRaises(ValidationFailure):
                verify_record(self.record)


class HistoricalTopologySourceRecordTests(unittest.TestCase):
    def setUp(self) -> None:
        path, size, digest = HISTORICAL_TOPOLOGY_COMMON_IDENTITY
        self.record = {"path": path, "bytes": size, "sha256": digest}

    def test_exact_archived_producer_is_distinguished_from_current_source(self) -> None:
        path, evidence = verify_historical_topology_common(self.record)
        self.assertEqual(path, resolve_logical(self.record["path"]))
        self.assertEqual(evidence["historical_source_commit"], HISTORICAL_TOPOLOGY_SOURCE_COMMIT)
        self.assertEqual(evidence["recorded_sha256"], self.record["sha256"])
        self.assertFalse(evidence["current_source_matches_historical_record"])
        self.assertFalse(evidence["current_source_claimed_to_have_generated_existing_terrain"])
        self.assertEqual(evidence["current_source_sha256"], hashlib.sha256(path.read_bytes()).hexdigest())

    def test_other_path_hash_or_byte_count_cannot_use_git_fallback(self) -> None:
        for field, value in (("path", "tools/other.py"), ("bytes", 16_444), ("sha256", "0" * 64)):
            with self.subTest(field=field), patch("validate_aerial_surface.subprocess.run") as run:
                with self.assertRaises(ValidationFailure):
                    verify_historical_topology_common({**self.record, field: value})
                run.assert_not_called()

    def test_unavailable_git_or_object_is_not_accepted(self) -> None:
        for failure in (OSError("missing Git"), subprocess.TimeoutExpired("git", 10)):
            with self.subTest(failure=type(failure).__name__):
                with patch("validate_aerial_surface.subprocess.run", side_effect=failure):
                    with self.assertRaises(ValidationFailure):
                        verify_historical_topology_common(self.record)
        with patch("validate_aerial_surface.subprocess.run", return_value=subprocess.CompletedProcess([], 128, b"", b"missing")):
            with self.assertRaises(ValidationFailure):
                verify_historical_topology_common(self.record)

    def test_wrong_archived_bytes_fail_even_when_git_succeeds(self) -> None:
        for payload in (b"wrong", b"x" * self.record["bytes"]):
            with patch("validate_aerial_surface.subprocess.run", return_value=subprocess.CompletedProcess([], 0, payload, b"")):
                with self.assertRaises(ValidationFailure):
                    verify_historical_topology_common(self.record)

    def test_regular_source_or_artifact_verification_has_no_git_fallback(self) -> None:
        with patch("validate_aerial_surface.subprocess.run") as run:
            with self.assertRaises(ValidationFailure):
                verify_record(self.record)
            run.assert_not_called()

    def test_missing_or_null_historical_role_fails_closed(self) -> None:
        for sources in ({}, {"historical_topology_common": None}):
            with self.assertRaises(ValidationFailure):
                verify_manifest_records([{"schema": EXPECTED_SCHEMAS["terrain"], "sources": sources}])

    def test_historical_canopy_and_current_ownership_verify_independent_identities(self) -> None:
        path, size, digest = validator.HISTORICAL_CANOPY_BUILDING_IDENTITY
        historical = {"path": path, "bytes": size, "sha256": digest}
        current = {"path": path, "bytes": BUILDING_MANIFEST.stat().st_size,
                   "sha256": hashlib.sha256(BUILDING_MANIFEST.read_bytes()).hexdigest()}
        evidence = []
        count, paths = verify_manifest_records([
            {"schema": EXPECTED_SCHEMAS["canopy"], "sources": {"building_manifest": historical}},
            {"asset_manifests": {"building": current}},
        ], historical_source_verifications=evidence)
        self.assertEqual(count, 2)
        self.assertEqual(len(paths), 1)
        self.assertEqual(evidence[0]["historical_input_role"], "canopy.sources.building_manifest")
        self.assertFalse(evidence[0]["current_input_claimed_to_have_generated_existing_derivatives"])

    def test_conflicting_regular_duplicate_identity_fails_before_file_validation(self) -> None:
        with patch("validate_aerial_surface.verify_record") as verify:
            with self.assertRaises(ValidationFailure):
                verify_manifest_records([{"records": [self.record, {**self.record, "sha256": "0" * 64}]}])
            verify.assert_not_called()

    def test_duplicate_metadata_roles_with_same_byte_identity_are_verified(self) -> None:
        with patch("validate_aerial_surface.verify_record", return_value=REPOSITORY_ROOT / "fake.py") as verify:
            count, _ = verify_manifest_records([{"records": [self.record, {**self.record, "semantic": "another_role"}]}])
            self.assertEqual(count, 1)
            verify.assert_called_once()

    def test_canopy_historical_role_missing_null_or_forged_is_rejected(self) -> None:
        for record in (None, {"path": self.record["path"], "bytes": 1, "sha256": "0" * 64}):
            with self.assertRaises(ValidationFailure):
                verify_manifest_records([{"schema": EXPECTED_SCHEMAS["canopy"], "sources": {"building_manifest": record}}])

    def test_canopy_git_object_failure_cannot_be_replaced_by_current_record(self) -> None:
        path, size, digest = validator.HISTORICAL_CANOPY_BUILDING_IDENTITY
        with patch("validate_aerial_surface.subprocess.run", return_value=subprocess.CompletedProcess([], 128, b"", b"missing")):
            with self.assertRaises(ValidationFailure):
                validator.verify_historical_canopy_building({"path": path, "bytes": size, "sha256": digest})


class OwnershipMetadataRefreshTests(unittest.TestCase):
    def setUp(self) -> None:
        logical = OWNERSHIP_PATH.relative_to(REPOSITORY_ROOT).as_posix()
        self.original = subprocess.check_output(
            ["git", "show", f"{HISTORICAL_TOPOLOGY_SOURCE_COMMIT}:{logical}"], cwd=REPOSITORY_ROOT,
        )
        self.building = BUILDING_MANIFEST.read_bytes()

    def test_only_two_original_scalar_byte_sequences_change(self) -> None:
        output, evidence = prepare_refresh(self.original, self.building)
        self.assertEqual(evidence["changed_scalar_paths"], ["asset_manifests.building.bytes", "asset_manifests.building.sha256"])
        self.assertTrue(evidence["all_other_serialized_bytes_preserved"])
        self.assertFalse(evidence["geometry_or_coordinate_regeneration"])
        self.assertFalse(evidence["product_asset_activation_performed"])
        self.assertEqual(len(output), len(self.original))

    def test_unreviewed_ownership_input_is_rejected(self) -> None:
        with self.assertRaises(ValueError):
            prepare_refresh(self.original.replace(b'"active": true', b'"active":false', 1), self.building)

    def test_unreviewed_building_input_is_rejected(self) -> None:
        with self.assertRaises(ValueError):
            prepare_refresh(self.original, self.building + b" ")

    def test_already_refreshed_receipt_cannot_be_reapplied_as_original(self) -> None:
        output, _ = prepare_refresh(self.original, self.building)
        with self.assertRaises(ValueError):
            prepare_refresh(output, self.building)


class HorizontalFrameReproducibilityTests(unittest.TestCase):
    def setUp(self) -> None:
        self.recorded = load_json(RUNTIME_ROOT / "Lidar" / DTM_DSM_MANIFEST_NAME)["horizontal_frame_contract"]
        self.current = horizontal_frame_contract(load_json(SOURCE_MANIFEST)["coordinate_mapping"])

    def test_existing_serialized_frame_passes_with_explicit_roundoff_evidence(self) -> None:
        report = validate_horizontal_frame_contract(self.recorded, self.current)
        self.assertFalse(report["physical_residual_limits_changed"])
        for item in report["fitted_roundoff_differences"]:
            self.assertLessEqual(item["absolute_delta"], item["roundoff_limit"])
        self.assertEqual(self.recorded["exact_conversion"], self.current["exact_conversion"])
        self.assertEqual(self.recorded["diagnostic_affine_product_from_utm_delta"]["maximum_allowed_residual_meters"], 0.005)

    def test_identical_frame_passes_without_roundoff_differences(self) -> None:
        report = validate_horizontal_frame_contract(self.recorded, self.recorded)
        self.assertEqual(report["fitted_roundoff_differences"], [])

    def test_changed_crs_origin_version_threshold_or_structure_is_rejected(self) -> None:
        cases = [
            ("lidar_raster_frame", "crs_epsg", 4326),
            ("lidar_raster_frame", "origin_easting_meters", self.recorded["lidar_raster_frame"]["origin_easting_meters"] + 1e-10),
            ("exact_conversion", "maximum_allowed_round_trip_residual_meters", 1.001e-6),
            ("runtime_affine_coefficients", "position_maximum_allowed_residual_meters", 0.005000000000001),
        ]
        for section, key, value in cases:
            bad = copy.deepcopy(self.recorded)
            bad[section][key] = value
            with self.subTest(key=key), self.assertRaises(ValidationFailure):
                validate_horizontal_frame_contract(bad, self.current)


    def test_unknown_frame_field_or_changed_version_is_rejected(self) -> None:
        for key, value in (("pyproj_version", "changed"), ("unknown_field", True)):
            bad = copy.deepcopy(self.recorded)
            bad[key] = value
            with self.assertRaises(ValidationFailure):
                validate_horizontal_frame_contract(bad, self.current)


    def test_nonfinite_or_material_coefficient_change_is_rejected(self) -> None:
        for value in (float("nan"), float("inf"), self.recorded["runtime_affine_coefficients"]["product_to_utm_x_from_product_x"] + 1e-13):
            bad = copy.deepcopy(self.recorded)
            bad["runtime_affine_coefficients"]["product_to_utm_x_from_product_x"] = value
            with self.assertRaises(ValidationFailure):
                validate_horizontal_frame_contract(bad, self.current)

    def test_fitted_scalar_cannot_change_type_to_bool_or_int(self) -> None:
        for value in (False, 0):
            bad = copy.deepcopy(self.recorded)
            bad["runtime_affine_coefficients"]["product_to_utm_x_offset_meters"] = value
            with self.assertRaises(ValidationFailure):
                validate_horizontal_frame_contract(bad, self.current)


class BuildingSourceConservationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.manifest = load_json(RUNTIME_ROOT / "Buildings" / validator.BUILDING_MANIFEST_NAME)
        cls.layer = validator.parse_building_layer(RUNTIME_ROOT / "Buildings" / validator.BUILDING_LAYER_NAME)
        def source(key):
            return load_json(resolve_logical(cls.manifest["sources"][key]["path"]))
        cls.arguments = [source(key) for key in ("osm_context", "placement_recipe", "coverage_report", "hero_replacements", "airport_hero_corridor_replacements")]

    def verify(self, layer=None, manifest=None):
        return validator.validate_building_coverage(layer or self.layer, manifest or self.manifest, *self.arguments)

    def test_current_identities_conserve_all_source_owned_buildings(self) -> None:
        evidence = self.verify()
        self.assertTrue(evidence["exact_source_footprint_conservation"])
        self.assertEqual(evidence["per_band"]["0-2km"]["hero_exclusion_count"], 83)
        self.assertEqual(evidence["per_band"]["0-2km"]["represented_footprint_count"], 1748)

    def test_missing_or_duplicate_compiled_building_is_rejected(self) -> None:
        for records in (self.layer["records"][:-1], self.layer["records"] + [self.layer["records"][0]]):
            with self.assertRaises(ValidationFailure):
                self.verify(layer={**self.layer, "records": records})

    def test_hero_identity_or_stale_denominator_is_rejected(self) -> None:
        for field in ("hero_exclusions", "per_band"):
            bad = copy.deepcopy(self.manifest)
            if field == "hero_exclusions":
                bad["coverage"][field][0]["osm_id"] += 1
            else:
                bad["coverage"][field]["0-2km"].update(hero_exclusion_count=2, represented_footprint_count=1829)
            with self.assertRaises(ValidationFailure):
                self.verify(manifest=bad)

    def test_wrong_compiled_band_cannot_pass_aggregate_counts(self) -> None:
        records = list(self.layer["records"])
        records[0] = {**records[0], "flags": records[0]["flags"] ^ validator.FLAG_ZONE_2_TO_4_KM}
        with self.assertRaises(ValidationFailure):
            self.verify(layer={**self.layer, "records": records})


class CanopySourceTierConservationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.manifest = load_json(RUNTIME_ROOT / "Canopy" / validator.CANOPY_MANIFEST_NAME)
        self.instances = load_json(RUNTIME_ROOT / "Canopy" / validator.NEAR_INSTANCE_MANIFEST_NAME)
        self.expected_instances = copy.deepcopy(self.instances["instances"])
        self.inventory = {"triangle_count": 29824, "vertex_count": 47588}

    def verify(self):
        return validator.validate_canopy_tier_inventory(self.manifest, self.inventory, self.instances,
                                                        self.inventory, self.expected_instances)

    def test_current_post_close_tree_inventory_and_nanometre_replay_are_accepted(self) -> None:
        self.expected_instances[0]["x"] += 2e-10
        evidence = self.verify()
        self.assertEqual(evidence["source_derived_close_tree_instance_count"], 1047)
        self.assertFalse(evidence["obsolete_static_30000_floor_applied"])
        self.assertEqual(evidence["static_hard_triangle_ceiling"], 59000)

    def test_material_position_or_diameter_and_nonfinite_changes_fail(self) -> None:
        for field in ("x", "z", "crown_diameter"):
            for value in (self.expected_instances[0][field] + 1e-7, float("nan"), float("inf")):
                bad = copy.deepcopy(self.instances)
                bad["instances"][0][field] = value
                with self.subTest(field=field), self.assertRaises(ValidationFailure):
                    validator.validate_canopy_tier_inventory(self.manifest, self.inventory, bad, self.inventory, self.expected_instances)

    def test_other_instance_fields_and_order_remain_exact(self) -> None:
        for field in ("rotation_degrees", "seed", "tint_r", "height", "y"):
            bad = copy.deepcopy(self.instances)
            bad["instances"][0][field] += 1e-10
            with self.subTest(field=field), self.assertRaises(ValidationFailure):
                validator.validate_canopy_tier_inventory(self.manifest, self.inventory, bad, self.inventory, self.expected_instances)
        self.instances["instances"][0], self.instances["instances"][1] = self.instances["instances"][1], self.instances["instances"][0]
        with self.assertRaises(ValidationFailure):
            self.verify()

    def test_missing_instance_or_static_topology_cannot_pass_conservation(self) -> None:
        self.instances["instances"].pop()
        with self.assertRaises(ValidationFailure):
            self.verify()
        self.instances["instances"] = self.expected_instances
        self.manifest["budgets"]["mesh_batch_total_triangles"] -= 1
        with self.assertRaises(ValidationFailure):
            self.verify()


class TerrainHistoricalReplayTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.derived = validator.derive_product_terrain()
        cls.report = load_json(validator.TERRAIN_OUTPUT_DIR / validator.TERRAIN_REPORT_NAME)
        cls.frame = cls.report["horizontal_frame_contract"]
        cls.obj = (validator.TERRAIN_OUTPUT_DIR / validator.TERRAIN_MESH_NAME).read_bytes()

    def verify(self, report=None, obj=None, frame=None):
        return validator.validate_terrain_replay(self.derived, frame or self.frame,
                                                obj or self.obj, report or self.report)

    def test_geometry_and_historical_source_comment_are_byte_exact(self) -> None:
        evidence = self.verify()
        self.assertEqual(evidence["exact_obj_sha256"], hashlib.sha256(self.obj).hexdigest())
        self.assertEqual(evidence["historical_source_contract_sha256"], self.report["source_contract_sha256"])
        self.assertTrue(evidence["positions_faces_bounds_maxima_percentiles_thresholds_anchors_exact"])
        self.assertFalse(evidence["assets_or_producer_lineage_changed"])

    def test_changed_obj_geometry_or_comment_or_line_endings_fail(self) -> None:
        for payload in (self.obj.replace(b'\nv ', b'\nv 1', 1), self.obj.replace(b'source_contract_sha256 ', b'source_contract_sha256 0', 1),
                        self.obj.replace(b'\n', b'\r\n')):
            with self.assertRaises(ValidationFailure):
                self.verify(obj=payload)

    def test_changed_source_fingerprint_or_frame_fails(self) -> None:
        report = copy.deepcopy(self.report)
        report["source_contract_sha256"] = "0" * 64
        with self.assertRaises(ValidationFailure):
            self.verify(report=report)
        frame = copy.deepcopy(self.frame)
        frame["runtime_affine_coefficients"]["product_to_utm_z_from_product_x"] += 1e-12
        with self.assertRaises(ValidationFailure):
            self.verify(frame=frame)

    def test_changed_reduction_nonfinite_maximum_threshold_and_count_fail(self) -> None:
        region = "mesh_vs_authored_target"
        for field, delta in (("mean_absolute_meters", 1e-9), ("maximum_absolute_meters", 1e-12), ("sample_count", 1)):
            report = copy.deepcopy(self.report)
            target = report["comparison"][region]["global"]
            target[field] += delta
            with self.assertRaises(ValidationFailure):
                self.verify(report=report)
        report = copy.deepcopy(self.report)
        report["comparison"][region]["global"]["rmse_meters"] = float("nan")
        with self.assertRaises(ValidationFailure):
            self.verify(report=report)


if __name__ == "__main__":
    unittest.main()
