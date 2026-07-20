from __future__ import annotations

import copy
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np
from PIL import Image
from rasterio.transform import from_origin


TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))

from build_texture_pyramid import (
    DEFAULT_CONFIG,
    astc_mip_chain_bytes,
    build_runtime_manifest,
    canonical_json_bytes,
    compose_mosaic,
    contract_sha256,
    load_config,
    normalize_arcgis_item,
    phase_correlation_shift,
    plan_assets,
    sha256_bytes,
    tile_range_for_bounds,
    tile_relative_path,
    tile_set_sha256,
    union_tile_ranges,
    unity_guid,
    unity_texture_meta,
    validate_config,
    validate_snapshot_metadata,
    validate_source_metadata,
    warp_mosaic_to_local_asset,
)


def representative_item(config: dict) -> dict:
    source = config["source"]
    return {
        "id": source["arcgis_item_id"],
        "owner": "BoulderCO",
        "created": 1627932234000,
        "modified": source["service_modified_epoch_ms"],
        "title": "2020 Imagery",
        "type": "Map Service",
        "description": (
            "The dataset consists of tiled orthogonal imagery produced from nadir images captured by "
            "Pictometry International. The resolution is 9 inches and the flights were in Spring and Summer of 2020."
        ),
        "snippet": "2020 9 inch aerial photos.",
        "tags": ["aerial", "photo", "boulder", "county"],
        "extent": [[-105.5394, 39.9132], [-105.0442, 40.2628]],
        "spatialReference": "102100",
        "accessInformation": None,
        "licenseInfo": (
            "<a href='https://creativecommons.org/publicdomain/zero/1.0/'>"
            "https://creativecommons.org/publicdomain/zero/1.0/</a>"
        ),
        "url": source["service_url"],
        "access": "public",
        "size": 88,
        "numViews": 100,
        "lastViewed": 123,
    }


def representative_service() -> dict:
    lods = [
        {"level": level, "resolution": 156543.033928 / (2**level), "scale": 591657527.591555 / (2**level)}
        for level in range(22)
    ]
    spatial_reference = {"wkid": 102100, "latestWkid": 3857}
    return {
        "currentVersion": 11.5,
        "serviceDescription": "",
        "description": "",
        "copyrightText": "",
        "capabilities": "Data,Map,Query",
        "spatialReference": spatial_reference,
        "fullExtent": {
            "xmin": -11761303.609145807,
            "ymin": 4851218.428597882,
            "xmax": -11689372.173132956,
            "ymax": 4906237.870150264,
            "spatialReference": spatial_reference,
        },
        "maxImageWidth": 4096,
        "maxImageHeight": 4096,
        "tileInfo": {
            "rows": 256,
            "cols": 256,
            "dpi": 96,
            "format": "MIXED",
            "compressionQuality": 75,
            "origin": {"x": -20037508.342787, "y": 20037508.342787},
            "spatialReference": spatial_reference,
            "lods": lods,
        },
    }


class AerialSurfaceConfigTests(unittest.TestCase):
    def setUp(self) -> None:
        self.config = load_config(DEFAULT_CONFIG)

    def test_fixed_tile_contract_matches_existing_shader_bindings(self) -> None:
        assets = {asset["id"]: asset for asset in self.config["assets"]}
        self.assertEqual(
            {asset["shader_property"] for asset in assets.values()},
            {
                "_NearAlbedo",
                "_RecoveryMidNWAlbedo",
                "_RecoveryMidNEAlbedo",
                "_RecoveryMidSWAlbedo",
                "_RecoveryMidSEAlbedo",
                "_MidAlbedo",
                "_FarAlbedo",
            },
        )
        self.assertEqual(assets["near_4km"]["local_bounds_meters"], [-2000.0, -2000.0, 2000.0, 2000.0])
        self.assertEqual(assets["near_4km"]["dimensions_pixels"], [8192, 8192])
        self.assertLessEqual(4000.0 / assets["near_4km"]["dimensions_pixels"][0], 0.75)
        self.assertEqual(assets["mid_base_12km"]["local_bounds_meters"], [-6000.0, -6000.0, 6000.0, 6000.0])
        self.assertEqual(assets["mid_base_12km"]["dimensions_pixels"], [8192, 8192])
        self.assertLessEqual(12000.0 / assets["mid_base_12km"]["dimensions_pixels"][0], 2.0)
        self.assertEqual(assets["far_24km"]["dimensions_pixels"], [4096, 4096])
        self.assertEqual(24000.0 / assets["far_24km"]["dimensions_pixels"][0], 5.859375)
        self.assertEqual(assets["far_24km"]["mip_bias"], 0.0)

    def test_unity_importer_matches_serialized_integration_contract(self) -> None:
        near = next(asset for asset in self.config["assets"] if asset["id"] == "near_4km")
        metadata = unity_texture_meta(near, self.config).decode("utf-8")
        self.assertIn("  nPOTScale: 0\n", metadata)
        self.assertIn("  streamingMipmaps: 1\n", metadata)
        self.assertIn("owner=GeospatialMipController", metadata)
        android = metadata.split("    buildTarget: Android\n", 1)[1]
        self.assertIn("    textureFormat: 51\n", android)
        self.assertIn("    compressionQuality: 100\n", android)

    def test_scope_and_water_guards_are_closed(self) -> None:
        self.assertEqual(self.config["water_policy"]["mode"], "orthophoto_only")
        self.assertTrue(self.config["runtime"]["streaming_mipmaps"])
        self.assertTrue(self.config["runtime"]["replacement_only"])
        self.assertTrue(self.config["scope_guards"]["runtime_controller_required"])
        self.assertFalse(any(
            self.config["scope_guards"][key]
            for key in (
                "world_extent_expansion",
                "runway_changes",
                "sky_changes",
                "mountain_changes",
                "google_imagery_allowed",
            )
        ))

    def test_selected_source_is_explicit_city_cc0_and_not_google(self) -> None:
        source = self.config["source"]
        self.assertEqual(source["arcgis_item_id"], "22b7c93a74394696824258f807f345ba")
        self.assertEqual(source["license_spdx"], "CC0-1.0")
        serialized = json.dumps(source).lower()
        self.assertNotIn("google", serialized)
        self.assertIn("bouldercolorado.gov", serialized)

    def test_validation_rejects_scope_relaxation_and_google(self) -> None:
        bad = copy.deepcopy(self.config)
        bad["scope_guards"]["world_extent_expansion"] = True
        with self.assertRaises(ValueError):
            validate_config(bad)
        bad = copy.deepcopy(self.config)
        bad["source"]["service_url"] = "https://maps.google.example/imagery"
        with self.assertRaises(ValueError):
            validate_config(bad)
        bad = copy.deepcopy(self.config)
        bad["runtime"]["streaming_mipmaps"] = False
        with self.assertRaises(ValueError):
            validate_config(bad)
        bad = copy.deepcopy(self.config)
        bad["scope_guards"]["runtime_controller_required"] = False
        with self.assertRaises(ValueError):
            validate_config(bad)
        bad = copy.deepcopy(self.config)
        bad["runtime"]["controller_type"] = "UnexpectedController"
        with self.assertRaises(ValueError):
            validate_config(bad)

    def test_astc_budget_matches_baseline_asset_shapes(self) -> None:
        self.assertEqual(astc_mip_chain_bytes(4096, 4096, 6, 6), 9_961_536)
        self.assertEqual(astc_mip_chain_bytes(8192, 8192, 8, 8), 22_369_664)
        self.assertEqual(astc_mip_chain_bytes(2048, 2048, 6, 6), 2_497_712)
        self.assertEqual(astc_mip_chain_bytes(4096, 4096, 10, 10), 3_589_696)
        self.assertEqual(astc_mip_chain_bytes(8192, 8192, 10, 10), 14_348_096)
        total = sum(
            astc_mip_chain_bytes(*asset["dimensions_pixels"], *asset["android_astc_block"])
            for asset in self.config["assets"]
        )
        self.assertEqual(total, 50_298_304)

    def test_unity_guids_are_stable_and_path_specific(self) -> None:
        first = self.config["assets"][0]["unity_path"]
        second = self.config["assets"][1]["unity_path"]
        self.assertEqual(unity_guid(first), unity_guid(first))
        self.assertNotEqual(unity_guid(first), unity_guid(second))
        self.assertEqual(len(unity_guid(first)), 32)

    def test_runtime_manifest_exposes_integration_density_and_budget_keys(self) -> None:
        records = []
        for asset in self.config["assets"]:
            width, height = asset["dimensions_pixels"]
            bounds = asset["local_bounds_meters"]
            block_width, block_height = asset["android_astc_block"]
            replacement_dimensions = asset.get("replacement_dimensions_pixels", asset["dimensions_pixels"])
            replacement_block = asset.get("replacement_android_astc_block", asset["android_astc_block"])
            records.append(
                {
                    "id": asset["id"],
                    "shader_property": asset["shader_property"],
                    "path": asset["unity_path"],
                    "meters_per_texel": [
                        (bounds[2] - bounds[0]) / width,
                        (bounds[3] - bounds[1]) / height,
                    ],
                    "world_min_inverse_size": [
                        bounds[0],
                        bounds[1],
                        1.0 / (bounds[2] - bounds[0]),
                        1.0 / (bounds[3] - bounds[1]),
                    ],
                    "android_import": {
                        "estimated_full_mip_chain_bytes": astc_mip_chain_bytes(
                            width, height, block_width, block_height
                        )
                    },
                    "replacement": {
                        "estimated_full_mip_chain_bytes": astc_mip_chain_bytes(
                            replacement_dimensions[0],
                            replacement_dimensions[1],
                            replacement_block[0],
                            replacement_block[1],
                        )
                    },
                }
            )
        snapshot = {
            "arcgis_item_response_sha256": "a" * 64,
            "arcgis_item_contract_sha256": "b" * 64,
            "service_metadata_response_sha256": "c" * 64,
            "service_contract_sha256": "d" * 64,
            "tile_count": 1,
            "tile_set_sha256": "e" * 64,
        }
        manifest = build_runtime_manifest(
            self.config,
            DEFAULT_CONFIG,
            snapshot,
            "f" * 64,
            records,
            {},
            {},
        )
        runtime = manifest["runtime"]
        self.assertEqual(runtime["near_meters_per_texel"], 0.48828125)
        self.assertEqual(runtime["pattern_meters_per_texel"], 1.46484375)
        self.assertEqual(runtime["far_meters_per_texel"], 5.859375)
        self.assertEqual(runtime["runtime_set_android_bytes"], 50_298_304)
        self.assertEqual(runtime["baseline_replaced_android_bytes"], 26_039_792)
        self.assertEqual(runtime["projected_scene_android_texture_bytes"], 210_808_066)
        self.assertEqual(runtime["hard_ceiling_bytes"], 220_200_960)
        self.assertTrue(runtime["streaming_mipmaps"])
        self.assertEqual(
            runtime["selection_mode"],
            "explicit GeospatialMipController-managed mip residency for seven fixed ENU shader tiles; "
            "no camera transform ownership or hierarchy construction",
        )
        self.assertEqual(runtime["controller_type"], "QuestFlightLab.Environment.GeospatialMipController")
        self.assertEqual(
            runtime["controller_owned_texture_ids"],
            [asset["id"] for asset in self.config["assets"]],
        )


class SourceContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.config = load_config(DEFAULT_CONFIG)
        self.item = representative_item(self.config)
        self.service = representative_service()

    def test_reviewed_source_metadata_is_accepted(self) -> None:
        validate_source_metadata(self.item, self.service, self.config)

    def test_missing_license_or_changed_revision_is_rejected(self) -> None:
        item = copy.deepcopy(self.item)
        item["licenseInfo"] = None
        with self.assertRaises(ValueError):
            validate_source_metadata(item, self.service, self.config)
        item = copy.deepcopy(self.item)
        item["modified"] += 1
        with self.assertRaises(ValueError):
            validate_source_metadata(item, self.service, self.config)

    def test_dynamic_arcgis_counters_do_not_change_item_contract_hash(self) -> None:
        first = normalize_arcgis_item(self.item)
        changed = copy.deepcopy(self.item)
        changed["numViews"] = 999999
        changed["lastViewed"] = 999999
        second = normalize_arcgis_item(changed)
        self.assertEqual(contract_sha256(first), contract_sha256(second))
        changed["licenseInfo"] = ""
        self.assertNotEqual(contract_sha256(first), contract_sha256(normalize_arcgis_item(changed)))

    def test_pinned_snapshot_detects_metadata_byte_or_contract_change(self) -> None:
        item_payload = canonical_json_bytes(self.item)
        service_payload = canonical_json_bytes(self.service)
        snapshot = {
            "schema_version": 1,
            "source_id": self.config["source"]["id"],
            "arcgis_item_response_sha256": sha256_bytes(item_payload),
            "arcgis_item_contract_sha256": contract_sha256(normalize_arcgis_item(self.item)),
            "service_metadata_response_sha256": sha256_bytes(service_payload),
            "service_contract_sha256": contract_sha256(
                {
                    "currentVersion": self.service["currentVersion"],
                    "serviceDescription": self.service["serviceDescription"],
                    "description": self.service["description"],
                    "copyrightText": self.service["copyrightText"],
                    "capabilities": self.service["capabilities"],
                    "spatialReference": self.service["spatialReference"],
                    "fullExtent": self.service["fullExtent"],
                    "maxImageWidth": self.service["maxImageWidth"],
                    "maxImageHeight": self.service["maxImageHeight"],
                    "tileInfo": self.service["tileInfo"],
                }
            ),
        }
        # Use the production normalizer to avoid duplicating subtle LOD coercions.
        from build_texture_pyramid import normalize_service_metadata

        snapshot["service_contract_sha256"] = contract_sha256(normalize_service_metadata(self.service))
        validate_snapshot_metadata(
            snapshot,
            self.config,
            item_payload,
            self.item,
            service_payload,
            self.service,
        )
        with self.assertRaises(ValueError):
            validate_snapshot_metadata(
                snapshot,
                self.config,
                item_payload + b" ",
                self.item,
                service_payload,
                self.service,
            )


class TilePlanningTests(unittest.TestCase):
    def setUp(self) -> None:
        self.config = load_config(DEFAULT_CONFIG)
        from build_texture_pyramid import normalize_service_metadata

        self.service = normalize_service_metadata(representative_service())

    def test_tile_range_encloses_web_mercator_bounds_with_padding(self) -> None:
        bounds = [-1000.0, -500.0, 1000.0, 500.0]
        tile_info = {
            "rows": 4,
            "cols": 4,
            "origin": {"x": -2048.0, "y": 2048.0},
            "lods": [{"level": 3, "resolution": 2.0, "scale": 1.0}],
        }
        result = tile_range_for_bounds(bounds, tile_info, 3, padding=1)
        tile_span = 8.0
        west = tile_info["origin"]["x"] + result["column_min"] * tile_span
        east = tile_info["origin"]["x"] + (result["column_max"] + 1) * tile_span
        north = tile_info["origin"]["y"] - result["row_min"] * tile_span
        south = tile_info["origin"]["y"] - (result["row_max"] + 1) * tile_span
        self.assertLessEqual(west, bounds[0] - tile_span)
        self.assertGreaterEqual(east, bounds[2] + tile_span)
        self.assertGreaterEqual(north, bounds[3] + tile_span)
        self.assertLessEqual(south, bounds[1] - tile_span)

    def test_union_is_partitioned_by_lod(self) -> None:
        union = union_tile_ranges(
            [
                {"level": 16, "row_min": 2, "row_max": 4, "column_min": 5, "column_max": 8, "resolution_web_mercator_meters": 2.0},
                {"level": 16, "row_min": 1, "row_max": 3, "column_min": 6, "column_max": 9, "resolution_web_mercator_meters": 2.0},
                {"level": 17, "row_min": 10, "row_max": 11, "column_min": 12, "column_max": 13, "resolution_web_mercator_meters": 1.0},
            ]
        )
        self.assertEqual(union[16]["row_min"], 1)
        self.assertEqual(union[16]["column_max"], 9)
        self.assertEqual(union[17]["row_min"], 10)

    def test_asset_plan_never_upscales_source_cache(self) -> None:
        plans, union = plan_assets(self.config, self.service)
        self.assertEqual(set(union), {15, 16, 17, 18})
        self.assertEqual(plans["near_4km"]["tile_range"]["level"], 18)
        self.assertEqual(plans["mid_base_12km"]["tile_range"]["level"], 17)
        self.assertEqual(plans["far_24km"]["tile_range"]["level"], 15)
        assets = {asset["id"]: asset for asset in self.config["assets"]}
        for identifier, plan in plans.items():
            asset = assets[identifier]
            width, height = asset["dimensions_pixels"]
            bounds = asset["local_bounds_meters"]
            output = max((bounds[2] - bounds[0]) / width, (bounds[3] - bounds[1]) / height)
            self.assertGreaterEqual(output, plan["approximate_ground_resolution_at_origin_meters"])

    def test_tile_set_digest_is_order_independent_and_content_sensitive(self) -> None:
        records = [
            {"level": 16, "row": 2, "column": 3, "url": "https://example.test/a", "bytes": 10, "sha256": "a" * 64},
            {"level": 16, "row": 2, "column": 4, "url": "https://example.test/b", "bytes": 11, "sha256": "b" * 64},
        ]
        self.assertEqual(tile_set_sha256(records), tile_set_sha256(list(reversed(records))))
        changed = copy.deepcopy(records)
        changed[1]["sha256"] = "c" * 64
        self.assertNotEqual(tile_set_sha256(records), tile_set_sha256(changed))


class RasterOrientationTests(unittest.TestCase):
    def test_compose_mosaic_preserves_arcgis_north_and_east_order(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            records = []
            colors = {
                (0, 0): (10, 20, 30),
                (0, 1): (40, 50, 60),
                (1, 0): (70, 80, 90),
                (1, 1): (100, 110, 120),
            }
            for (row, column), color in colors.items():
                relative = tile_relative_path(3, row, column)
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                buffer = io.BytesIO()
                Image.new("RGB", (4, 4), color).save(buffer, format="PNG")
                path.write_bytes(buffer.getvalue())
                records.append(
                    {
                        "level": 3,
                        "row": row,
                        "column": column,
                        "cache_relative_path": relative,
                        "sha256": sha256_bytes(buffer.getvalue()),
                    }
                )
            tile_range = {
                "level": 3,
                "row_min": 0,
                "row_max": 1,
                "column_min": 0,
                "column_max": 1,
            }
            tile_info = {
                "rows": 4,
                "cols": 4,
                "origin": {"x": 0.0, "y": 16.0},
                "lods": [{"level": 3, "resolution": 1.0, "scale": 1.0}],
            }
            mosaic, transform = compose_mosaic(root, tile_range, tile_info, records)
            np.testing.assert_array_equal(mosaic[0, 0], colors[(0, 0)])
            np.testing.assert_array_equal(mosaic[0, -1], colors[(0, 1)])
            np.testing.assert_array_equal(mosaic[-1, 0], colors[(1, 0)])
            np.testing.assert_array_equal(mosaic[-1, -1], colors[(1, 1)])
            self.assertEqual(transform.c, 0.0)
            self.assertEqual(transform.f, 16.0)

    def test_warp_output_row_zero_is_north_and_column_zero_is_west(self) -> None:
        config = load_config(DEFAULT_CONFIG)
        size = 64
        east = np.linspace(0, 255, size, dtype=np.uint8)
        north = np.linspace(255, 0, size, dtype=np.uint8)
        red = np.tile(east, (size, 1))
        green = np.tile(north[:, None], (1, size))
        blue = np.full((size, size), 127, dtype=np.uint8)
        mosaic = np.stack((red, green, blue), axis=-1)
        asset = {
            "dimensions_pixels": [32, 32],
            "local_bounds_meters": [0.0, 0.0, 64.0, 64.0],
        }
        result = warp_mosaic_to_local_asset(
            mosaic,
            from_origin(0.0, 64.0, 1.0, 1.0),
            asset,
            config,
            source_crs="EPSG:3857",
            destination_crs="EPSG:3857",
        )
        self.assertLess(float(result[:, 0, 0].mean()), float(result[:, -1, 0].mean()))
        self.assertGreater(float(result[0, :, 1].mean()), float(result[-1, :, 1].mean()))

    def test_phase_correlation_detects_integer_pixel_shift(self) -> None:
        rng = np.random.default_rng(7)
        baseline = rng.integers(0, 256, size=(128, 128), dtype=np.uint8)
        shifted = np.roll(np.roll(baseline, 3, axis=0), -5, axis=1)
        report = phase_correlation_shift(shifted, baseline)
        self.assertEqual(report["shift_rows_columns_pixels"], [3.0, -5.0])


if __name__ == "__main__":
    unittest.main()
