from __future__ import annotations

import copy
import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[1]
if str(TOOL_DIR) not in sys.path:
    sys.path.insert(0, str(TOOL_DIR))

import generate_vegetation_distribution as pipeline
import generate_vegetation_handoff_mask as handoff


class VegetationPipelineTests(unittest.TestCase):
    def setUp(self) -> None:
        self.config = json.loads((TOOL_DIR / "vegetation_config.json").read_text(encoding="utf-8"))
        # Compact radii exercise all tiers without pretending the fixture is a
        # product distribution.  Output remains TEST_ONLY_SYNTHETIC.
        self.config["tiers"][0]["maximum_radius_m"] = 80.0
        self.config["tiers"][1]["minimum_radius_m"] = 80.0
        self.config["tiers"][1]["maximum_radius_m"] = 180.0
        self.config["tiers"][2]["minimum_radius_m"] = 180.0
        self.config["tiers"][2]["maximum_radius_m"] = 400.0

    @staticmethod
    def _hash(path: Path) -> str:
        return hashlib.sha256(path.read_bytes()).hexdigest()

    def _fixture(self, root: Path) -> tuple[Path, dict]:
        width = height = 128
        surface = bytearray(width * height * 4)
        dtm_values = [12236] * (width * height)
        for row in range(height):
            for column in range(width):
                x = -254.0 + column * 4.0
                z = 254.0 - row * 4.0
                radius = (x * x + z * z) ** 0.5
                if radius > 238.0 or ((column // 10 + row // 9) % 5 == 0):
                    continue
                offset = (row * width + column) * 4
                surface[offset] = 24 + ((column + row) % 24)  # 6..11.75 m canopy HAG
                surface[offset + 1] = 120 + ((column * 3 + row) % 110)
                surface[offset + 2] = 0
                surface[offset + 3] = 90 + ((column + row * 2) % 150)
        # A strong candidate with building HAG must never enter a batch.
        building_offset = (64 * width + 64) * 4
        surface[building_offset : building_offset + 4] = bytes((60, 255, 40, 255))

        surface_path = root / "surface.png"
        dtm_path = root / "dtm.png"
        terrain_normal_path = root / "terrain_normal.png"
        canopy_normal_path = root / "canopy_normal.png"
        pipeline.write_rgba8_png(surface_path, width, height, bytes(surface))
        pipeline.write_gray16_png(dtm_path, width, height, dtm_values)
        terrain_normal = bytes((128, 255, 128, 1)) * (width * height)
        canopy_normal = bytes((128, 255, 128, 180)) * (width * height)
        pipeline.write_rgba8_png(terrain_normal_path, width, height, terrain_normal)
        pipeline.write_rgba8_png(canopy_normal_path, width, height, canopy_normal)
        manifest = {
            "schema": pipeline.LIDAR_SCHEMA,
            "assets": [
                {
                    "semantic": "surface_features_canopy_building",
                    "path": surface_path.name,
                    "bytes": surface_path.stat().st_size,
                    "sha256": self._hash(surface_path),
                    "dimensions": {"width_pixels": width, "height_pixels": height},
                    "encoding": {"container": "PNG", "channels": "RGBA", "bits_per_channel": 8},
                },
                {
                    "semantic": "digital_terrain_model",
                    "path": dtm_path.name,
                    "bytes": dtm_path.stat().st_size,
                    "sha256": self._hash(dtm_path),
                    "dimensions": {"width_pixels": width, "height_pixels": height},
                    "encoding": {
                        "container": "PNG",
                        "channel": "R",
                        "bits_per_channel": 16,
                        "code_zero_semantic": "nodata_reserved",
                        "decoded_source_z_meters": {"scale": 0.05, "offset": 1000.0},
                    },
                },
                {
                    "semantic": "terrain_surface_normal_and_validity",
                    "path": terrain_normal_path.name,
                    "bytes": terrain_normal_path.stat().st_size,
                    "sha256": self._hash(terrain_normal_path),
                    "dimensions": {"width_pixels": width, "height_pixels": height},
                    "encoding": {"container": "PNG", "channels": "RGBA", "bits_per_channel": 8},
                },
                {
                    "semantic": "canopy_height_normal_and_occlusion",
                    "path": canopy_normal_path.name,
                    "bytes": canopy_normal_path.stat().st_size,
                    "sha256": self._hash(canopy_normal_path),
                    "dimensions": {"width_pixels": width, "height_pixels": height},
                    "encoding": {"container": "PNG", "channels": "RGBA", "bits_per_channel": 8},
                },
            ],
            "coordinate_mapping": {
                "grid": {
                    "product_crs_epsg": 32613,
                    "source_crs_epsg": 3857,
                    "width_pixels": width,
                    "height_pixels": height,
                    "pixel_size_meters": 4.0,
                    "product_bounds_utm13n_meters": [0.0, 0.0, 512.0, 512.0],
                },
                "origin": {
                    "wgs84_latitude_degrees": 40.03936527,
                    "wgs84_longitude_degrees": -105.22608958,
                    "source_elevation_msl_meters": 1611.7824,
                    "utm13n_easting_meters": 0.0,
                    "utm13n_northing_meters": 0.0,
                },
                "pixel_center_affine_local_meters": {
                    "x_at_column0_row0": -254.0,
                    "z_at_column0_row0": 254.0,
                    "x_per_column": 4.0,
                    "x_per_row": 0.0,
                    "z_per_column": 0.0,
                    "z_per_row": -4.0,
                },
                "unity_axes": {"x": "east", "y": "up", "z": "north"},
            },
        }
        manifest_path = root / "manifest.json"
        manifest_path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        return manifest_path, manifest

    def test_png_round_trip_supports_rgba8_r8_and_r16(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            rgba_path = root / "rgba.png"
            r8_path = root / "r8.png"
            r16_path = root / "r16.png"
            rgba = bytes((10, 20, 30, 40, 50, 60, 70, 80))
            pipeline.write_rgba8_png(rgba_path, 2, 1, rgba)
            handoff.write_r8_png(r8_path, 2, 1, bytes((7, 249)))
            pipeline.write_gray16_png(r16_path, 2, 1, [1, 65535])
            decoded_rgba = pipeline.read_png(rgba_path)
            decoded_r8 = pipeline.read_png(r8_path)
            decoded_r16 = pipeline.read_png(r16_path)
            self.assertEqual(decoded_rgba.rgba8(1, 0), (50, 60, 70, 80))
            self.assertEqual(decoded_r8.gray8(0, 0), 7)
            self.assertEqual(decoded_r8.gray8(1, 0), 249)
            self.assertEqual(decoded_r16.gray16(0, 0), 1)
            self.assertEqual(decoded_r16.gray16(1, 0), 65535)

    def test_tier2_is_fixed_world_oriented_and_map_owned(self) -> None:
        tier2 = next(record for record in self.config["tiers"] if int(record["tier"]) == 2)
        self.assertEqual(tier2["minimum_radius_m"], 180.0)
        self.assertEqual(tier2["maximum_instances"], 42)
        self.assertEqual(tier2["source_objects"], ["AerialGroveCluster_T2"])
        self.assertEqual(tier2["geometry_draws"], 1)
        self.assertFalse(tier2["carves_handoff_mask"])
        self.assertNotIn("upper_canopy_lobes_per_placement", tier2)

    def test_canopy_shader_template_preserves_semantic_frame_transform(self) -> None:
        include = (TOOL_DIR / "templates" / "AerialCanopyMap.cginc").read_text(encoding="utf-8")
        self.assertIn("_AerialCanopyProductToSourceRow0", include)
        self.assertIn("dot(_AerialCanopyProductToSourceRow0.xy, localXZ)", include)
        self.assertIn("_AerialCanopySourceToProductRow0", include)
        self.assertIn("dot(_AerialCanopySourceToProductRow0.xy, sourceNormal.xz)", include)

    def test_handoff_weight_is_spatially_feathered_and_eligible_canopy_gated(self) -> None:
        self.assertEqual(handoff.quantized_handoff(-1.0, 16.0, 64.0), 0)
        self.assertEqual(handoff.quantized_handoff(16.0, 16.0, 64.0), 0)
        midpoint = handoff.quantized_handoff(48.0, 16.0, 64.0)
        self.assertIn(midpoint, (127, 128))
        self.assertEqual(handoff.quantized_handoff(80.0, 16.0, 64.0), 255)

        mask = bytearray((255, 192, 64, 0, 255))
        handoff.apply_eligible_canopy_gate(mask, bytearray((1, 0, 1, 1, 0)))
        self.assertEqual(mask, bytearray((255, 0, 64, 0, 0)))
        with self.assertRaisesRegex(ValueError, "dimensions"):
            handoff.apply_eligible_canopy_gate(bytearray((255,)), bytearray((1, 1)))

    def test_distribution_is_deterministic_bounded_and_test_labelled(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest_path, _ = self._fixture(root)
            inputs = pipeline.load_inputs(root, manifest_path, self.config)
            first = pipeline.build_distribution(inputs, self.config, allow_synthetic_test_input=True)
            second = pipeline.build_distribution(inputs, copy.deepcopy(self.config), allow_synthetic_test_input=True)
            self.assertEqual(
                json.dumps(first, sort_keys=True, separators=(",", ":")),
                json.dumps(second, sort_keys=True, separators=(",", ":")),
            )
            self.assertEqual(first["status"], "TEST_ONLY_SYNTHETIC")
            self.assertEqual(first["source_kind"], "synthetic_test_fixture")
            self.assertEqual(first["batch_count"], 3)
            counts = {batch["batch_id"]: len(batch["instances"]) for batch in first["batches"]}
            self.assertLessEqual(counts["tier0_editor_baked"], 3)
            self.assertLessEqual(counts["tier1_editor_baked"], 19)
            self.assertGreater(counts["tier2_instanced_fixed_groves"], 0)
            self.assertLessEqual(counts["tier2_instanced_fixed_groves"], 42)
            stable_ids = [instance["stable_id"] for batch in first["batches"] for instance in batch["instances"]]
            self.assertEqual(len(stable_ids), len(set(stable_ids)))
            self.assertFalse(first["runtime_policy"]["one_gameobject_per_tree"])
            self.assertEqual(first["runtime_policy"]["ordinary_vertical_billboards"], 0)
            self.assertEqual(first["runtime_policy"]["source_draws_maximum"], 3)
            self.assertFalse(first["runtime_policy"]["camera_facing"])
            self.assertEqual(first["runtime_policy"]["alpha_mode"], "clip")
            self.assertEqual(
                first["runtime_policy"]["tier2_geometry_instances"],
                counts["tier2_instanced_fixed_groves"],
            )
            self.assertEqual(first["runtime_policy"]["tier2_geometry_draws"], 1)
            self.assertFalse(first["runtime_policy"]["tier2_geometry_carves_handoff"])
            self.assertEqual(
                first["runtime_policy"]["tier2_representation"],
                "fixed_world_oriented_grove_clusters_over_orthophoto_lidar_canopy_shading",
            )
            self.assertEqual(first["coverage_agreement"]["tier2"]["canopy_mask_coverage_ratio"], 1.0)
            self.assertTrue(first["coverage_agreement"]["tier2"]["terrain_map_retained_beneath_geometry"])
            self.assertFalse(first["coverage_agreement"]["tier2"]["geometry_carves_handoff"])
            self.assertEqual(first["coverage_agreement"]["tier3"]["eligible_canopy_coverage_ratio"], 1.0)

    def test_tier2_clusters_never_carve_handoff_mask(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest_path, _ = self._fixture(root)
            inputs = pipeline.load_inputs(root, manifest_path, self.config)
            distribution = pipeline.build_distribution(
                inputs, self.config, allow_synthetic_test_input=True
            )
            tier2_batch = next(batch for batch in distribution["batches"] if int(batch["tier"]) == 2)
            # The compact fixture may select fewer than the production cap. Pad
            # with deterministic copies solely to exercise the exact production
            # handoff batch-count contract.
            originals = tier2_batch["instances"]
            self.assertTrue(originals)
            tier2_batch["instances"] = [
                dict(originals[index % len(originals)], stable_id=f"fixture-tier2-{index:02d}")
                for index in range(42)
            ]
            geometry_names = {
                instance["source_object"]
                for batch in distribution["batches"]
                if int(batch["tier"]) in (0, 1)
                for instance in batch["instances"]
            }
            geometry = {
                "objects": [
                    {"name": name, "bounds_min": [-2.0, -2.0, 0.0], "bounds_max": [2.0, 2.0, 8.0]}
                    for name in sorted(geometry_names)
                ]
            }
            affine = {
                "x_at_column0_row0": -254.0,
                "z_at_column0_row0": 254.0,
                "x_per_column": 4.0,
                "z_per_row": -4.0,
            }
            mask, footprints = handoff.build_mask(
                distribution, geometry, 128, 128, affine, 16.0, 64.0
            )
            self.assertEqual(len(footprints), 22)
            self.assertTrue(all(record["tier"] in (0, 1) for record in footprints))
            self.assertTrue(any(value == 255 for value in mask))

    def test_cluster_spatial_index_matches_brute_force_ellipse_queries(self) -> None:
        clusters = [
            {
                "x": -31.0, "z": 47.0,
                "footprint_radius_major_m": 24.0,
                "footprint_radius_minor_m": 11.0,
                "footprint_yaw_degrees": 37.0,
            },
            {
                "x": 152.0, "z": -96.0,
                "footprint_radius_major_m": 18.0,
                "footprint_radius_minor_m": 15.0,
                "footprint_yaw_degrees": 121.0,
            },
        ]
        spatial = pipeline.build_cluster_spatial_index(clusters)
        for x in range(-256, 257, 7):
            for z in range(-256, 257, 11):
                self.assertEqual(
                    pipeline.point_in_any_cluster(float(x), float(z), clusters),
                    pipeline.point_in_any_cluster(float(x), float(z), clusters, spatial),
                )

    def test_hash_and_schema_mismatch_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest_path, manifest = self._fixture(root)
            bad_hash = copy.deepcopy(manifest)
            bad_hash["assets"][0]["sha256"] = "0" * 64
            manifest_path.write_text(json.dumps(bad_hash), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
                pipeline.load_inputs(root, manifest_path, self.config)
            bad_schema = copy.deepcopy(manifest)
            bad_schema["schema"] = "questflight.aerial-scenery-v1.lidar.future"
            manifest_path.write_text(json.dumps(bad_schema), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Unsupported lidar schema"):
                pipeline.load_inputs(root, manifest_path, self.config)

    def test_ambiguous_semantic_and_axis_mapping_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest_path, manifest = self._fixture(root)
            ambiguous = copy.deepcopy(manifest)
            ambiguous["assets"].append(copy.deepcopy(ambiguous["assets"][0]))
            manifest_path.write_text(json.dumps(ambiguous), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "exactly one lidar asset"):
                pipeline.load_inputs(root, manifest_path, self.config)
            bad_axes = copy.deepcopy(manifest)
            bad_axes["coordinate_mapping"]["unity_axes"] = {"x": "east", "y": "north", "z": "up"}
            manifest_path.write_text(json.dumps(bad_axes), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "axis mapping"):
                pipeline.load_inputs(root, manifest_path, self.config)


if __name__ == "__main__":
    unittest.main()
