from __future__ import annotations

import copy
import hashlib
import json
import math
import sys
import unittest
from pathlib import Path

import numpy as np
from PIL import Image


TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))

from build_building_surfaces import (
    FACADE_TILE_BRICK,
    FACADE_TILE_CONCRETE,
    FACADE_TILE_CORRUGATED,
    FACADE_TILE_LIGHT_METAL,
    FACADE_TILE_WEATHERED_METAL,
    closed_surface_indices,
    edge_multiplicity,
    facade_semantics,
    simplify_footprint,
    triangulate_polygon,
)
from build_canopy_surfaces import (
    GENERATED_DIR as CANOPY_GENERATED_DIR,
    GEOMETRY_MASK_NAME,
    MANIFEST_NAME as CANOPY_MANIFEST_NAME,
    OUTPUT_DIR as CANOPY_OUTPUT_DIR,
    REVIEWED_EXCLUSION_CONTRACT,
    TERRAIN_HANDOFF_MASK_NAME,
    component_run_signature,
    connected_runs,
    eligible_canopy_mask,
    filter_small_geometry_components,
    load_reviewed_exclusion_contract,
    reviewed_canopy_exclusion_mask,
    resample_operational_exclusion,
    screen_relevant_grove_source_mask,
    tapered_corner_heights,
    terrain_map_only_partition,
)
from build_dtm_dsm import (
    RUNTIME_ROOT,
    SOURCE_MANIFEST,
    build_ndsm_codes,
    canonical_json_bytes,
    load_json,
    resolve_logical,
    sha256_file,
)
from build_product_terrain import (
    MAXIMUM_TRIANGLES as PRODUCT_TERRAIN_MAXIMUM_TRIANGLES,
    derive as derive_product_terrain,
    obj_text as product_terrain_obj_text,
)
from classify_surface import (
    BUILDING,
    CANOPY,
    ELEVATED_SURFACE,
    HEURISTIC_CLASSIFICATION,
    VALID_SURFACE,
    classify,
)
from validate_aerial_surface import validate
from geospatial_frame import (
    horizontal_frame_contract,
    lidar_pixel_centers_product_aeqd,
    product_aeqd_to_utm13n_delta,
    utm13n_delta_to_product_aeqd,
)


def pinned_mapping() -> dict:
    return copy.deepcopy(load_json(SOURCE_MANIFEST)["coordinate_mapping"])


class DtmDsmContractTests(unittest.TestCase):
    def test_ndsm_is_clipped_integer_difference(self) -> None:
        dtm = np.asarray([[100, 200], [300, 400]], dtype=np.uint16)
        dsm = np.asarray([[110, 190], [300, 65535]], dtype=np.uint16)
        result, negative = build_ndsm_codes(dtm, dsm)
        np.testing.assert_array_equal(
            result, np.asarray([[10, 0], [0, 65135]], dtype=np.uint16)
        )
        self.assertEqual(negative, 1)

    def test_canonical_json_is_order_independent(self) -> None:
        first = {"z": 1, "a": {"q": 2, "b": 3}}
        second = {"a": {"b": 3, "q": 2}, "z": 1}
        self.assertEqual(canonical_json_bytes(first), canonical_json_bytes(second))
        self.assertTrue(canonical_json_bytes(first).endswith(b"\n"))

    def test_exact_utm_delta_product_aeqd_round_trip_and_contract(self) -> None:
        mapping = pinned_mapping()
        x_product, z_product = utm13n_delta_to_product_aeqd(1000.0, 0.0, mapping)
        self.assertAlmostEqual(float(x_product), 1000.3926, places=3)
        self.assertAlmostEqual(float(z_product), 2.5395, places=3)
        x_utm, z_utm = product_aeqd_to_utm13n_delta(
            x_product, z_product, mapping
        )
        self.assertAlmostEqual(float(x_utm), 1000.0, places=6)
        self.assertAlmostEqual(float(z_utm), 0.0, places=6)

        contract = horizontal_frame_contract(mapping)
        exact = contract["exact_conversion"]
        affine = contract["diagnostic_affine_product_from_utm_delta"]
        self.assertLessEqual(
            exact["maximum_round_trip_residual_meters"],
            exact["maximum_allowed_round_trip_residual_meters"],
        )
        self.assertLessEqual(
            affine["maximum_residual_meters"],
            affine["maximum_allowed_residual_meters"],
        )
        self.assertGreater(contract["maximum_product_offset_from_utm_delta_meters"], 7.0)

        runtime = contract["runtime_affine_coefficients"]
        self.assertEqual(
            set(runtime),
            {
                "position_maximum_allowed_residual_meters",
                "position_maximum_residual_meters",
                "product_to_utm_x_from_product_x",
                "product_to_utm_x_from_product_z",
                "product_to_utm_x_offset_meters",
                "product_to_utm_z_from_product_x",
                "product_to_utm_z_from_product_z",
                "product_to_utm_z_offset_meters",
                "utm_normal_to_product_rotation_degrees_counterclockwise",
                "utm_normal_to_product_x_from_utm_x",
                "utm_normal_to_product_x_from_utm_z",
                "utm_normal_to_product_z_from_utm_x",
                "utm_normal_to_product_z_from_utm_z",
            },
        )
        self.assertTrue(all(isinstance(value, (int, float)) for value in runtime.values()))
        self.assertLessEqual(
            runtime["position_maximum_residual_meters"],
            runtime["position_maximum_allowed_residual_meters"],
        )
        runtime_x = (
            runtime["product_to_utm_x_from_product_x"] * float(x_product)
            + runtime["product_to_utm_x_from_product_z"] * float(z_product)
            + runtime["product_to_utm_x_offset_meters"]
        )
        runtime_z = (
            runtime["product_to_utm_z_from_product_x"] * float(x_product)
            + runtime["product_to_utm_z_from_product_z"] * float(z_product)
            + runtime["product_to_utm_z_offset_meters"]
        )
        self.assertLess(
            math.hypot(runtime_x - 1000.0, runtime_z),
            runtime["position_maximum_allowed_residual_meters"],
        )

        normal_matrix = np.asarray(
            [
                [
                    runtime["utm_normal_to_product_x_from_utm_x"],
                    runtime["utm_normal_to_product_x_from_utm_z"],
                ],
                [
                    runtime["utm_normal_to_product_z_from_utm_x"],
                    runtime["utm_normal_to_product_z_from_utm_z"],
                ],
            ],
            dtype=np.float64,
        )
        np.testing.assert_allclose(normal_matrix.T @ normal_matrix, np.identity(2), atol=1e-12)
        self.assertAlmostEqual(float(np.linalg.det(normal_matrix)), 1.0, places=12)
        self.assertAlmostEqual(
            runtime["utm_normal_to_product_rotation_degrees_counterclockwise"],
            0.145446988159,
            places=10,
        )


class ClassificationContractTests(unittest.TestCase):
    def test_expected_bits_are_composed_without_source_class_claims(self) -> None:
        ndsm = np.asarray([[0, 30], [50, 50]], dtype=np.uint16)
        features = np.zeros((2, 2, 4), dtype=np.uint8)
        features[1, 0, 0] = 8
        features[1, 1, 2] = 8
        normal = np.zeros((2, 2, 4), dtype=np.uint8)
        normal[:, :, 3] = 1
        result = classify(ndsm, features, normal)
        self.assertEqual(int(result[0, 0]), VALID_SURFACE)
        self.assertEqual(int(result[0, 1]), VALID_SURFACE | ELEVATED_SURFACE)
        self.assertEqual(
            int(result[1, 0]),
            VALID_SURFACE | ELEVATED_SURFACE | CANOPY | HEURISTIC_CLASSIFICATION,
        )
        self.assertEqual(
            int(result[1, 1]),
            VALID_SURFACE | ELEVATED_SURFACE | BUILDING | HEURISTIC_CLASSIFICATION,
        )

    def test_building_canopy_overlap_is_rejected(self) -> None:
        ndsm = np.zeros((1, 1), dtype=np.uint16)
        features = np.zeros((1, 1, 4), dtype=np.uint8)
        features[0, 0, 0] = features[0, 0, 2] = 1
        with self.assertRaises(ValueError):
            classify(ndsm, features, np.zeros((1, 1, 4), dtype=np.uint8))


class SurfaceTopologyTests(unittest.TestCase):
    def test_facade_semantics_are_usage_derived_and_building_stable(self) -> None:
        residential = facade_semantics(
            {"tags": {"building": "house"}}, 101, 2.5
        )
        industrial = facade_semantics(
            {"tags": {"building": "hangar"}}, 202, -1.0
        )
        unknown = facade_semantics(
            {"tags": {"building": "yes"}}, 303, 0.0
        )
        self.assertIn(residential["facade_tile"], {FACADE_TILE_BRICK, FACADE_TILE_CONCRETE})
        self.assertIn(
            industrial["facade_tile"],
            {FACADE_TILE_LIGHT_METAL, FACADE_TILE_CORRUGATED,
             FACADE_TILE_WEATHERED_METAL},
        )
        self.assertEqual(unknown["facade_tile"], FACADE_TILE_CONCRETE)
        self.assertEqual(
            industrial,
            facade_semantics({"tags": {"building": "hangar"}}, 202, -1.0),
        )
        self.assertTrue(0 <= industrial["facade_base_height_code"] <= 255)

    def test_closed_building_surface_has_two_incident_faces_per_edge(self) -> None:
        points = [(0, 0), (0, 20), (20, 20), (20, 0)]
        if sum(
            points[index][0] * points[(index + 1) % len(points)][1]
            - points[(index + 1) % len(points)][0] * points[index][1]
            for index in range(len(points))
        ) < 0:
            points.reverse()
        roof = triangulate_polygon(points)
        indices = closed_surface_indices(points, roof)
        self.assertTrue(all(value == 2 for value in edge_multiplicity(indices).values()))
        self.assertEqual(len(indices) // 3, 12)

    def test_simplification_respects_area_and_hausdorff_bounds(self) -> None:
        points = [(0, 0), (10, 0), (20, 0), (20, 10), (20, 20), (10, 20), (0, 20), (0, 10)]
        simplified, metrics = simplify_footprint(points)
        self.assertGreaterEqual(metrics["area_ratio"], 0.90)
        self.assertLessEqual(metrics["area_ratio"], 1.05)
        self.assertLessEqual(metrics["hausdorff_meters"], 10.0)
        self.assertLessEqual(len(simplified), len(points))

    def test_connected_canopy_runs_are_deterministic_and_exact(self) -> None:
        mask = np.asarray(
            [
                [0, 1, 1, 0, 0],
                [0, 0, 1, 0, 1],
                [1, 0, 0, 0, 1],
            ],
            dtype=bool,
        )
        components, runs = connected_runs(mask)
        self.assertEqual(len(components), 3)
        reconstructed = np.zeros_like(mask)
        for row, start, end, _component in runs:
            reconstructed[row, start:end] = True
        np.testing.assert_array_equal(reconstructed, mask)
        self.assertEqual((components, runs), connected_runs(copy.deepcopy(mask)))

    def test_product_canopy_partition_is_map_only_exact_and_deterministic(self) -> None:
        eligible = np.asarray(
            [[1, 1, 0, 1], [0, 1, 1, 1], [1, 0, 0, 1]], dtype=bool
        )
        hero = np.asarray(
            [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 0, 0]], dtype=bool
        )
        first_geometry, first_handoff = terrain_map_only_partition(eligible, hero)
        second_geometry, second_handoff = terrain_map_only_partition(
            eligible.copy(), hero.copy()
        )
        self.assertFalse(np.any(first_geometry))
        np.testing.assert_array_equal(first_handoff, eligible & ~hero)
        np.testing.assert_array_equal(hero | first_handoff, eligible)
        np.testing.assert_array_equal(first_geometry, second_geometry)
        np.testing.assert_array_equal(first_handoff, second_handoff)
        first_hash = hashlib.sha256(first_handoff.tobytes(order="C")).hexdigest()
        second_hash = hashlib.sha256(second_handoff.tobytes(order="C")).hexdigest()
        self.assertEqual(first_hash, second_hash)

    def test_only_screen_relevant_connected_groves_receive_geometry(self) -> None:
        source = np.zeros((24, 24), dtype=bool)
        source[0:12, 0:12] = True
        source[18:23, 18:23] = True
        retained, metrics = screen_relevant_grove_source_mask(source, 4.0)
        np.testing.assert_array_equal(retained[0:12, 0:12], np.ones((12, 12), dtype=bool))
        self.assertFalse(np.any(retained[18:23, 18:23]))
        self.assertEqual(metrics["retained_component_count"], 1)
        self.assertEqual(metrics["rejected_component_count"], 1)
        self.assertEqual(metrics["retained_source_pixel_count"], 144)
        self.assertEqual(metrics["rejected_source_pixel_count"], 25)

    def test_fragmented_geometry_components_are_handed_to_terrain_map(self) -> None:
        coarse = np.zeros((8, 8), dtype=bool)
        coarse[0:2, 0:4] = True
        refinement = np.zeros((16, 16), dtype=bool)
        refinement[14, 14] = True
        ownership = np.repeat(np.repeat(coarse, 4, axis=0), 4, axis=1)
        ownership |= np.repeat(np.repeat(refinement, 2, axis=0), 2, axis=1)
        retained_coarse, retained_refinement, retained, metrics = (
            filter_small_geometry_components(coarse, refinement, ownership)
        )
        np.testing.assert_array_equal(retained_coarse, coarse)
        self.assertFalse(np.any(retained_refinement))
        self.assertEqual(int(np.count_nonzero(retained)), 128)
        self.assertEqual(metrics["retained_component_count"], 1)
        self.assertEqual(metrics["removed_component_count"], 1)

    def test_canopy_boundary_topology_tapers_to_terrain_without_vertical_skirts(self) -> None:
        cells = np.ones((3, 3), dtype=bool)
        tops = np.full((3, 3), 10.0, dtype=np.float64)
        dtm = np.zeros((4, 4), dtype=np.float64)
        affine = {
            "x_at_column0_row0": -2048.0,
            "x_per_column": 4.0,
            "z_at_column0_row0": 2048.0,
            "z_per_row": -4.0,
        }
        corners = tapered_corner_heights(cells, tops, dtm, affine, 4.0)
        self.assertAlmostEqual(corners[(0, 0)], 0.04)
        self.assertAlmostEqual(corners[(1, 1)], 10.0)
        self.assertAlmostEqual(corners[(3, 3)], 0.04)

    def test_operational_exclusions_remove_false_canopy_candidates(self) -> None:
        dtm = np.ones((2, 2), dtype=np.uint16)
        surface = np.zeros((2, 2, 4), dtype=np.uint8)
        surface[:, :, 0] = 8
        surface[:, :, 1] = 255
        exclusion = np.asarray([[0, 64], [192, 0]], dtype=np.uint8)
        eligible, metrics = eligible_canopy_mask(
            dtm, surface, np.zeros((2, 2), dtype=np.uint8), exclusion
        )
        np.testing.assert_array_equal(
            eligible, np.asarray([[True, False], [False, True]], dtype=bool)
        )
        self.assertEqual(metrics["operational_exclusion_rejected_pixel_count"], 2)
        self.assertEqual(metrics["operational_exclusion_rejected_by_code"], {"64": 1, "192": 1})

    def test_reviewed_canopy_exclusion_matches_exact_component_and_fails_closed(self) -> None:
        ownership = np.asarray(
            [
                [0, 1, 1, 0, 0],
                [0, 1, 1, 0, 1],
                [0, 0, 0, 0, 1],
                [1, 0, 0, 0, 0],
            ],
            dtype=bool,
        )
        eligible = ownership.copy()
        mapping = pinned_mapping()
        mapping["pixel_center_affine_local_meters"] = {
            "x_at_column0_row0": -8.0,
            "x_per_column": 4.0,
            "x_per_row": 0.0,
            "z_at_column0_row0": 6.0,
            "z_per_column": 0.0,
            "z_per_row": -4.0,
        }
        origin = mapping["origin"]
        mapping["pixel_center_affine_utm13n_meters"] = {
            "easting_at_column0_row0": float(origin["utm13n_easting_meters"]) - 8.0,
            "easting_per_column": 4.0,
            "easting_per_row": 0.0,
            "northing_at_column0_row0": float(origin["utm13n_northing_meters"]) + 6.0,
            "northing_per_column": 0.0,
            "northing_per_row": -4.0,
        }
        components, runs = connected_runs(ownership)
        component = components[0]
        selected = runs[: int(component["run_count"])]
        expected_mask = np.zeros_like(ownership)
        for row, start, end, _component_id in selected:
            expected_mask[row, start:end] = True
        product_x, product_z = lidar_pixel_centers_product_aeqd(mapping, ownership.shape)
        rows, columns = np.where(expected_mask)
        affine = mapping["pixel_center_affine_utm13n_meters"]
        contract = {
            "exclusions": [
                {
                    "id": "synthetic-reviewed-component",
                    "expected_pre_exclusion_component": {
                        "bounds_inclusive": {
                            field: int(component[field])
                            for field in (
                                "minimum_column",
                                "maximum_column",
                                "minimum_row",
                                "maximum_row",
                            )
                        },
                        "centroid_product_aeqd_meters": [
                            float(np.mean(product_x[expected_mask])),
                            float(np.mean(product_z[expected_mask])),
                        ],
                        "centroid_utm13n_meters": [
                            float(
                                np.mean(
                                    float(affine["easting_at_column0_row0"])
                                    + columns * float(affine["easting_per_column"])
                                    + rows * float(affine["easting_per_row"])
                                )
                            ),
                            float(
                                np.mean(
                                    float(affine["northing_at_column0_row0"])
                                    + columns * float(affine["northing_per_column"])
                                    + rows * float(affine["northing_per_row"])
                                )
                            ),
                        ],
                        "pixel_count": int(component["pixel_count"]),
                        "run_count": int(component["run_count"]),
                        "run_signature_sha256": component_run_signature(selected),
                        "source_eligible_pixel_count": int(np.count_nonzero(expected_mask)),
                    },
                }
            ]
        }
        result, metrics = reviewed_canopy_exclusion_mask(
            ownership, eligible, mapping, contract
        )
        np.testing.assert_array_equal(result, expected_mask)
        self.assertEqual(metrics["component_count"], 1)

        changed = copy.deepcopy(contract)
        changed["exclusions"][0]["expected_pre_exclusion_component"][
            "run_signature_sha256"
        ] = "0" * 64
        with self.assertRaisesRegex(ValueError, "component changed"):
            reviewed_canopy_exclusion_mask(ownership, eligible, mapping, changed)

    def test_reviewed_exclusion_contract_prohibits_global_likelihood_gate(self) -> None:
        contract = load_reviewed_exclusion_contract(
            REVIEWED_EXCLUSION_CONTRACT, pinned_mapping(), (1024, 1024)
        )
        self.assertIsNone(contract["policy"]["global_imagery_likelihood_threshold"])
        self.assertEqual(len(contract["exclusions"]), 2)

    def test_operational_exclusion_resampling_uses_centered_enu_coordinates(self) -> None:
        source = np.arange(16, dtype=np.uint8).reshape(4, 4)
        mapping = pinned_mapping()
        mapping["grid"]["height_pixels"] = 2
        mapping["grid"]["width_pixels"] = 2
        mapping["pixel_center_affine_local_meters"] = {
            "x_at_column0_row0": -0.5,
            "x_per_column": 2.0,
            "x_per_row": 0.0,
            "z_at_column0_row0": 0.5,
            "z_per_column": 0.0,
            "z_per_row": -2.0,
        }
        result = resample_operational_exclusion(
            source,
            (2, 2),
            mapping,
            {"dimensions": [4, 4], "meters_per_pixel": 1.0},
        )
        np.testing.assert_array_equal(result, np.asarray([[5, 7], [13, 15]], dtype=np.uint8))

    def test_operational_exclusion_sampling_projects_utm_targets_to_product_frame(self) -> None:
        mapping = pinned_mapping()
        mapping["grid"]["height_pixels"] = 1
        mapping["grid"]["width_pixels"] = 1
        mapping["pixel_center_affine_local_meters"] = {
            "x_at_column0_row0": 1000.0,
            "x_per_column": 4.0,
            "x_per_row": 0.0,
            "z_at_column0_row0": 0.0,
            "z_per_column": 0.0,
            "z_per_row": -4.0,
        }
        source = np.zeros((4096, 4096), dtype=np.uint8)
        x_product, z_product = utm13n_delta_to_product_aeqd(1000.0, 0.0, mapping)
        expected_column = int(np.floor(float(x_product) + 2048.0))
        expected_row = int(np.floor(2048.0 - float(z_product)))
        unprojected_column = int(np.floor(1000.0 + 2048.0))
        unprojected_row = 2048
        self.assertNotEqual((expected_row, expected_column), (unprojected_row, unprojected_column))
        source[unprojected_row, unprojected_column] = 17
        source[expected_row, expected_column] = 211
        result = resample_operational_exclusion(
            source,
            (1, 1),
            mapping,
            {"dimensions": [4096, 4096], "meters_per_pixel": 1.0},
        )
        self.assertEqual(int(result[0, 0]), 211)


class ProductTerrainContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.derived = derive_product_terrain()

    def test_fixed_product_terrain_is_border_locked_and_within_budget(self) -> None:
        report = self.derived["report"]
        self.assertEqual(self.derived["triangle_count"], PRODUCT_TERRAIN_MAXIMUM_TRIANGLES)
        self.assertEqual(self.derived["vertex_count"], 46_426)
        self.assertTrue(report["border_lock"]["exact_4096_meter_product_square"])
        self.assertEqual(
            report["geometric_integration_eligibility"]["status"], "ELIGIBLE"
        )
        self.assertLessEqual(
            report["comparison"]["runway_mesh_vs_input_plane"][
                "maximum_absolute_meters"
            ],
            1.0e-5,
        )

    def test_product_terrain_height_anchors_use_exact_projected_queries(self) -> None:
        mapping = pinned_mapping()
        anchors = self.derived["report"]["verification_anchors"]
        self.assertGreaterEqual(len(anchors), 7)
        maximum_frame_delta = 0.0
        for anchor in anchors:
            expected_x, expected_z = product_aeqd_to_utm13n_delta(
                anchor["product_x_meters"], anchor["product_z_meters"], mapping
            )
            self.assertAlmostEqual(anchor["utm_x_delta_meters"], float(expected_x), places=9)
            self.assertAlmostEqual(anchor["utm_z_delta_meters"], float(expected_z), places=9)
            maximum_frame_delta = max(
                maximum_frame_delta,
                math.hypot(
                    float(expected_x) - anchor["product_x_meters"],
                    float(expected_z) - anchor["product_z_meters"],
                ),
            )
        self.assertGreater(maximum_frame_delta, 7.0)
        payload = product_terrain_obj_text(self.derived)
        self.assertEqual(payload.count("\nv "), self.derived["vertex_count"])
        self.assertEqual(payload.count("\nf "), self.derived["triangle_count"])


class CommittedAssetContractTests(unittest.TestCase):
    def test_full_committed_contract_passes_without_raw_lidar(self) -> None:
        report = validate(RUNTIME_ROOT)
        self.assertEqual(report["status"], "PASS")
        self.assertEqual(report["building"]["triangle_count"], 128_092)
        self.assertEqual(report["canopy"]["triangle_count"], 0)
        self.assertEqual(report["canopy"]["vertex_count"], 0)
        self.assertEqual(report["canopy"]["geometry_component_count"], 0)
        self.assertEqual(report["canopy"]["combined_source_assignment_percent"], 100.0)
        self.assertEqual(report["canopy"]["median_height_agreement_percent"], 100.0)
        self.assertEqual(report["terrain"]["triangle_count"], 92_000)
        self.assertEqual(report["terrain"]["vertex_count"], 46_426)
        self.assertTrue(report["terrain"]["border_locked"])
        self.assertEqual(report["terrain"]["geometric_integration_status"], "ELIGIBLE")
        building_manifest = load_json(
            RUNTIME_ROOT / "Buildings" / "ProductionBuildingSurfaceLayer_Manifest.json"
        )
        semantic = building_manifest["facade_semantics"]
        self.assertEqual(
            semantic["material_policy"],
            "one coherent usage-derived facade family per OSM footprint",
        )
        self.assertEqual(
            set(semantic["atlas_tiles"].values()),
            {FACADE_TILE_BRICK, FACADE_TILE_LIGHT_METAL, FACADE_TILE_CONCRETE,
             FACADE_TILE_CORRUGATED, FACADE_TILE_WEATHERED_METAL},
        )

    def test_committed_canopy_assets_are_map_only_with_pinned_hashes(self) -> None:
        manifest = load_json(CANOPY_OUTPUT_DIR / CANOPY_MANIFEST_NAME)
        geometry = np.asarray(Image.open(CANOPY_OUTPUT_DIR / GEOMETRY_MASK_NAME))
        handoff = np.asarray(Image.open(CANOPY_OUTPUT_DIR / TERRAIN_HANDOFF_MASK_NAME))
        self.assertFalse(np.any(geometry))
        self.assertEqual(
            int(np.count_nonzero(handoff)),
            int(manifest["coverage"]["terrain_handoff_source_eligible_pixel_count"]),
        )
        self.assertEqual(
            int(np.count_nonzero(handoff)),
            int(manifest["coverage"]["final_eligible_pixel_count"])
            - int(manifest["coverage"]["hero_0_250m_source_eligible_pixel_count"]),
        )
        self.assertEqual(manifest["coverage"]["source_assignment_coverage_fraction"], 1.0)
        self.assertEqual(manifest["budgets"]["mesh_batch_count"], 0)
        self.assertEqual(manifest["budgets"]["mesh_batch_total_triangles"], 0)
        self.assertTrue(all(int(value) == 0 for value in manifest["topology"].values()))
        self.assertFalse(CANOPY_GENERATED_DIR.exists())
        assets = {item["semantic"]: item for item in manifest["assets"]}
        pinned = {
            "ProductionCanopySurfaceLayer":
                "64f101766866c79d74c05352ec20f8c78ef8737e0166fab5b246018f27b17805",
            "production_canopy_surface_geometry_ownership":
                "1eb50a3ee3fcac0adc60c0e90baa6d051c642f3a930330ac9aec9dd92996f3f7",
            "production_canopy_terrain_map_handoff":
                "f7f16eb9f971e83b35d181c7e45986d813cf52f5d7fe72c2006816852e2ef2a2",
        }
        for semantic, expected in pinned.items():
            record = assets[semantic]
            self.assertEqual(record["sha256"], expected)
            self.assertEqual(sha256_file(resolve_logical(record["path"])), expected)


if __name__ == "__main__":
    unittest.main()
