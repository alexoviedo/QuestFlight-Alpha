from __future__ import annotations

import copy
import json
import pathlib
import sys
import tempfile
import unittest


TOOL_ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOL_ROOT))

from validate_environment_reconstruction import EXPECTED_REPORT_TYPES, validate_paths  # noqa: E402


def common_instance(stable_id: str, category: str) -> dict:
    return {
        "stableId": stable_id,
        "sourceFeatureId": f"source-{stable_id}",
        "category": category,
        "xMeters": 10.0,
        "zMeters": 20.0,
        "authoredHeadingDegrees": 12.0,
        "modelForwardAxisCorrectionDegrees": 0.0,
        "scale": [1.0, 1.0, 1.0],
        "terrainHeightMeters": 1608.0,
        "terrainNormal": [0.0, 1.0, 0.0],
        "enuCoordinateResolved": True,
        "terrainSurfaceSampleValid": True,
        "baseContactOffsetMeters": 0.0,
        "footprint": {"kind": "polygon", "pointCount": 4},
        "foundationMode": "best_fit_plane",
        "lodHlodClass": "near_lod0",
        "sourceLicenseClass": "project-owned",
    }


def passing_reports() -> dict[str, dict]:
    environment_build_id = "environment-reconstruction-v2-test"
    surface_sha = "a" * 64
    building = common_instance("building-1", "building")
    building.update(
        baseToFoundationErrorMeters=0.02,
        visibleFloorPenetration=False,
        unsupportedFloatingBase=False,
        slopedFootprintRequiresFoundation=True,
        zone="near_0_2km",
    )
    building_2 = copy.deepcopy(building)
    building_2.update(stableId="building-2", sourceFeatureId="source-building-2", zone="extended_near_2_4km")
    building_3 = copy.deepcopy(building)
    building_3.update(stableId="building-3", sourceFeatureId="source-building-3", zone="mid_4_6km")
    tree = common_instance("tree-1", "tree")
    tree.update(
        rootRelativeToTerrainMeters=-0.04,
        trunkWorldUp=True,
        explicitSpeciesTiltRule=False,
        intersectsExcludedSurface=False,
    )
    aircraft = common_instance("parked-aircraft-1", "parked_aircraft")
    aircraft.update(
        gearContactErrorsMeters=[0.01, -0.01, 0.02],
        headingErrorDegrees=2.0,
        fuselageTerrainClearanceMeters=0.3,
        propellerTerrainClearanceMeters=0.2,
        apronContained=True,
        standId="stand-a",
    )
    aircraft_2 = copy.deepcopy(aircraft)
    aircraft_2.update(stableId="parked-aircraft-2", sourceFeatureId="source-parked-aircraft-2", standId="stand-b")
    aircraft_3 = copy.deepcopy(aircraft)
    aircraft_3.update(stableId="parked-aircraft-3", sourceFeatureId="source-parked-aircraft-3", standId="stand-c")
    prop = common_instance("prop-1", "airport_prop")
    prop.update(groundContactErrorMeters=0.02, duplicateOverlap=False)
    fence = common_instance("fence-1", "fence")
    fence.update(groundContactErrorMeters=0.02, duplicateOverlap=False)
    road = common_instance("road-1", "road")
    water_body = common_instance("water-near-1", "water_body")

    instances = [building, building_2, building_3, tree, aircraft, aircraft_2, aircraft_3, prop, fence, road, water_body]
    category_counts: dict[str, int] = {}
    for instance in instances:
        category_counts[instance["category"]] = category_counts.get(instance["category"], 0) + 1

    return {
        "surface": {
            "schemaVersion": 1,
            "reportType": "production_terrain_surface",
            "environmentBuildId": environment_build_id,
            "manifestSha256": surface_sha,
            "authoritative": True,
            "coordinateSystem": "local_ENU",
            "enuOriginAligned": True,
            "fixedRuntimeMesh": True,
            "runtimeCameraDependentMorphing": False,
            "stableNormalsBakedOffline": True,
            "seamValidationPassed": True,
            "brokenSeamCount": 0,
            "sourceResolutionMeters": 1.0,
            "nearCoverageWidthMeters": 4000,
            "nearCoverageHeightMeters": 4000,
            "runtimeVertexCount": 100000,
            "runtimeTriangleCount": 190000,
            "maximumGeometricErrorMeters": 0.25,
            "averageGeometricErrorMeters": 0.04,
            "airportZoneErrorMeters": 0.01,
            "normalMapResolutionPixels": 4096,
            "samplerCapabilities": {
                "height": True,
                "normal": True,
                "terrainZone": True,
                "macroImageryUv": True,
                "landCover": True,
                "runwayApron": True,
                "water": True,
                "bestFitPlane": True,
            },
        },
        "placement": {
            "schemaVersion": 1,
            "reportType": "production_placement",
            "environmentBuildId": environment_build_id,
            "surfaceManifestSha256": surface_sha,
            "instances": instances,
            "declaredInstanceCount": len(instances),
            "categoryCounts": category_counts,
            "duplicateOverlapCount": 0,
            "missingSourceLicenseEntryCount": 0,
            "runway": {
                "authoritativeLayerCount": 1,
                "zFightingDetected": False,
                "floatingMarkingsDetected": False,
                "runwayTerrainMaximumGapMeters": 0.02,
                "markingRunwayMaximumGapMeters": 0.002,
                "colliderSurfaceMaximumDisagreementMeters": 0.00005,
            },
        },
        "building": {
            "schemaVersion": 1,
            "reportType": "building_coverage",
            "environmentBuildId": environment_build_id,
            "surfaceManifestSha256": surface_sha,
            "zones": [
                {"id": "near_0_2km", "eligibleFootprints": 1, "representedFootprints": 1},
                {"id": "extended_near_2_4km", "eligibleFootprints": 1, "representedFootprints": 1},
                {"id": "mid_4_6km", "eligibleFootprints": 1, "representedFootprints": 1},
            ],
            "duplicateFootprintCount": 0,
            "unexplainedExcludedFootprintCount": 0,
            "missingSourceLicenseEntryCount": 0,
        },
        "vegetation": {
            "schemaVersion": 1,
            "reportType": "vegetation_coverage",
            "environmentBuildId": environment_build_id,
            "surfaceManifestSha256": surface_sha,
            "maskAvailable": True,
            "maskSource": "NAIP NIR NDVI + OSM exclusions",
            "deterministicDistribution": True,
            "deterministicSeed": 1729,
            "aerialCanopyRepresented": True,
            "speciesVariantCount": 4,
            "excludedSurfaceViolationCount": 0,
            "perTreeUpdateComponentCount": 0,
            "uniquePerTreeMaterialCount": 0,
            "missingSourceLicenseEntryCount": 0,
            "obviousRepeatedPatternDetected": False,
            "uniformDistributionDetected": False,
            "denseGrassCarpetDetected": False,
            "stereoBillboardInstabilityDetected": False,
            "severeLodPoppingDetected": False,
            "lodCounts": {
                "lod0Close3d": 50,
                "lod1Simplified3d": 200,
                "lod2ClusterOrImpostor": 400,
                "farOrthophotoAreaSquareMeters": 1000000,
            },
        },
        "water": {
            "schemaVersion": 1,
            "reportType": "water_coverage",
            "environmentBuildId": environment_build_id,
            "surfaceManifestSha256": surface_sha,
            "sourceFeatureCount": 2,
            "missingSignificantFeatureCount": 0,
            "missingSourceLicenseEntryCount": 0,
            "features": [
                {
                    "stableId": "water-near-1",
                    "sourceFeatureId": "osm-water-1",
                    "sourceLicenseClass": "ODbL-1.0",
                    "zone": "near_0_4km",
                    "geometryType": "polygon",
                    "areaSquareMeters": 1000,
                    "represented": True,
                    "validSmoothedGeometry": True,
                    "terrainSeparated": True,
                    "terrainCutDepressedOrMasked": True,
                    "singleAuthoritativeShoreline": True,
                    "featheredBankBlend": True,
                    "sharedGeospatialUv": True,
                    "orthophotoTintDerived": True,
                    "coplanarOverlapDetected": False,
                    "materialId": "shared-water",
                },
                {
                    "stableId": "water-far-1",
                    "sourceFeatureId": "osm-water-2",
                    "sourceLicenseClass": "ODbL-1.0",
                    "zone": "far_beyond_12km",
                    "geometryType": "polygon",
                    "areaSquareMeters": 100000,
                    "represented": False,
                    "exclusionReason": "far zone uses orthophoto only",
                },
            ],
        },
        "hlod": {
            "schemaVersion": 1,
            "reportType": "environment_hlod",
            "environmentBuildId": environment_build_id,
            "surfaceManifestSha256": surface_sha,
            "offlineGenerated": True,
            "deterministic": True,
            "sharedMaterialBatching": True,
            "gpuInstancingCompatible": True,
            "runtimeMeshCombination": False,
            "xrCrossfadeShimmerDetected": False,
            "missingAssetCount": 0,
            "perInstanceScriptCount": 0,
            "missingSourceLicenseEntryCount": 0,
            "cells": [
                {
                    "stableId": "cell-1",
                    "sizeMeters": 250,
                    "lod0Present": True,
                    "lod1Present": True,
                    "lod2Present": True,
                    "farOrthophotoStatePresent": True,
                    "missingAssetCount": 0,
                }
            ],
        },
        "budgets": {
            "schemaVersion": 1,
            "reportType": "quest_environment_budgets",
            "environmentBuildId": environment_build_id,
            "surfaceManifestSha256": surface_sha,
            "maximumVisibleDrawCalls": 130,
            "maximumVisibleTriangles": 650000,
            "maximumVisibleMaterials": 30,
            "estimatedRuntimeTextureMemoryBytes": 220 * 1024 * 1024,
            "transparentMaterialSlots": 3,
            "msaaSamples": 4,
            "steadyStateManagedAllocations": 0,
            "controlledShaderVariants": True,
            "uncontrolledTransparentOverdrawDetected": False,
        },
    }


class EnvironmentReconstructionQaTests(unittest.TestCase):
    def validate(self, reports: dict[str, dict], omit: str | None = None) -> dict:
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            paths: dict[str, pathlib.Path] = {}
            for category in EXPECTED_REPORT_TYPES:
                path = root / f"{category}.json"
                paths[category] = path
                if category != omit:
                    path.write_text(json.dumps(reports[category]), encoding="utf-8")
            return validate_paths(paths)

    @staticmethod
    def codes(report: dict) -> set[str]:
        return {issue["code"] for issue in report["issues"]}

    @staticmethod
    def instance(reports: dict[str, dict], stable_id: str) -> dict:
        return next(item for item in reports["placement"]["instances"] if item["stableId"] == stable_id)

    def test_complete_evidence_passes_at_exact_limits(self) -> None:
        report = self.validate(passing_reports())
        self.assertTrue(report["passed"], report["issues"])
        self.assertEqual("PASS_ENVIRONMENT_RECONSTRUCTION_QA", report["classification"])

    def test_missing_report_fails_closed(self) -> None:
        report = self.validate(passing_reports(), omit="surface")
        self.assertFalse(report["passed"])
        self.assertIn("MISSING_REPORT", self.codes(report))

    def test_reports_must_bind_to_the_same_surface_and_build(self) -> None:
        reports = passing_reports()
        reports["water"]["environmentBuildId"] = "different-build"
        reports["water"]["surfaceManifestSha256"] = "b" * 64
        report = self.validate(reports)
        self.assertIn("ENVIRONMENT_BUILD_ID_MISMATCH", self.codes(report))
        self.assertIn("SURFACE_MANIFEST_HASH_MISMATCH", self.codes(report))

    def test_surface_camera_morphing_fails(self) -> None:
        reports = passing_reports()
        reports["surface"]["runtimeCameraDependentMorphing"] = True
        report = self.validate(reports)
        self.assertIn("CAMERA_DEPENDENT_TERRAIN", self.codes(report))

    def test_building_grounding_failure_is_detected(self) -> None:
        reports = passing_reports()
        self.instance(reports, "building-1")["baseToFoundationErrorMeters"] = 0.16
        report = self.validate(reports)
        self.assertIn("BUILDING_GROUNDING_MAX", self.codes(report))

    def test_tree_exclusion_failure_is_detected(self) -> None:
        reports = passing_reports()
        self.instance(reports, "tree-1")["intersectsExcludedSurface"] = True
        report = self.validate(reports)
        self.assertIn("TREE_EXCLUSION", self.codes(report))

    def test_parked_aircraft_orientation_failure_is_detected(self) -> None:
        reports = passing_reports()
        self.instance(reports, "parked-aircraft-1")["headingErrorDegrees"] = 90
        report = self.validate(reports)
        self.assertIn("AIRCRAFT_ORIENTATION", self.codes(report))

    def test_parked_aircraft_count_outside_three_to_eight_fails(self) -> None:
        reports = passing_reports()
        reports["placement"]["instances"] = [
            item for item in reports["placement"]["instances"] if item["stableId"] != "parked-aircraft-3"
        ]
        reports["placement"]["declaredInstanceCount"] -= 1
        reports["placement"]["categoryCounts"]["parked_aircraft"] = 2
        report = self.validate(reports)
        self.assertIn("PARKED_AIRCRAFT_COUNT", self.codes(report))

    def test_airport_prop_grounding_failure_is_detected(self) -> None:
        reports = passing_reports()
        self.instance(reports, "prop-1")["groundContactErrorMeters"] = 0.051
        report = self.validate(reports)
        self.assertIn("PROP_GROUND_CONTACT", self.codes(report))

    def test_hashed_unity_aggregate_placement_evidence_is_accepted(self) -> None:
        reports = passing_reports()
        reports["placement"]["instances"] = []
        reports["placement"]["aggregateValidation"] = {
            "evidenceKind": "UNITY_SERIALIZED_PLACEMENT_VALIDATION",
            "fullInventoryCsvSha256": "b" * 64,
            "fullInventoryRowCount": 11,
            "fullInventoryCategoryCounts": {"Building": 3, "Tree": 1, "ParkedAircraft": 3, "AirportProp":1,"Fence":1,"Road":1,"Water":1},
            "summaryPassed": True,
            "buildings": 3,
            "trees": 1,
            "parkedAircraft": 3,
            "duplicateStableIds": 0,
            "missingFootprints": 0,
            "invalidLicenses": 0,
            "unvalidatedVegetationExclusions": 0,
            "buildingMedianFoundationErrorMeters": 0.03,
            "buildingP95FoundationErrorMeters": 0.08,
            "buildingMaximumFoundationErrorMeters": 0.15,
            "treeMinimumRootOffsetMeters": -0.12,
            "treeMaximumRootOffsetMeters": 0.03,
            "parkedAircraftMaximumGearErrorMeters": 0.03,
            "parkedAircraftMaximumHeadingErrorDegrees": 3.0,
        }
        report = self.validate(reports)
        self.assertNotIn("MISSING_INSTANCES", self.codes(report))
        self.assertNotIn("BUILDING_PLACEMENT_COUNT_MISMATCH", self.codes(report))

    def test_unvalidated_aggregate_vegetation_exclusions_fail(self) -> None:
        reports = passing_reports()
        reports["placement"]["instances"] = []
        reports["placement"]["aggregateValidation"] = {
            "evidenceKind": "UNITY_SERIALIZED_PLACEMENT_VALIDATION",
            "fullInventoryCsvSha256": "b" * 64,
            "fullInventoryRowCount": 11,
            "fullInventoryCategoryCounts": {"Building": 3, "Tree": 1, "ParkedAircraft": 3, "AirportProp":1,"Fence":1,"Road":1,"Water":1},
            "summaryPassed": True,
            "buildings": 3, "trees": 1, "parkedAircraft": 3,
            "duplicateStableIds": 0, "missingFootprints": 0, "invalidLicenses": 0,
            "unvalidatedVegetationExclusions": 1,
            "buildingMedianFoundationErrorMeters": 0.0,
            "buildingP95FoundationErrorMeters": 0.0,
            "buildingMaximumFoundationErrorMeters": 0.0,
            "treeMinimumRootOffsetMeters": -0.05,
            "treeMaximumRootOffsetMeters": -0.05,
            "parkedAircraftMaximumGearErrorMeters": 0.01,
            "parkedAircraftMaximumHeadingErrorDegrees": 0.0,
        }
        report = self.validate(reports)
        self.assertIn("VEGETATION_EXCLUSIONS_UNVALIDATED", self.codes(report))

    def test_aggregate_runway_checks_match_detailed_checks(self) -> None:
        mutations = {"authoritativeLayerCount": 2, "zFightingDetected": True, "floatingMarkingsDetected": True, "runwayTerrainMaximumGapMeters": 100, "markingRunwayMaximumGapMeters": 100, "colliderSurfaceMaximumDisagreementMeters": 100}
        expected = {"DUPLICATE_RUNWAY_LAYER", "RUNWAY_Z_FIGHTING", "RUNWAY_FLOATING_MARKINGS", "RUNWAY_TERRAIN_GAP", "RUNWAY_MARKING_GAP", "RUNWAY_COLLIDER_DISAGREEMENT"}
        for field, bad in mutations.items():
            with self.subTest(field=field):
                detailed=passing_reports()
                detailed["placement"]["runway"][field]=bad
                aggregate=copy.deepcopy(detailed)
                aggregate["placement"]["instances"]=[]
                aggregate["placement"]["aggregateValidation"]={"summaryPassed":True}
                self.assertEqual(self.codes(self.validate(detailed)) & expected, self.codes(self.validate(aggregate)) & expected)

    def test_aggregate_failed_summary_and_category_totals_rejected(self) -> None:
        reports=passing_reports()
        reports["placement"]["instances"]=[]
        reports["placement"]["aggregateValidation"]={"summaryPassed":False,"fullInventoryCategoryCounts":{"Building":1},"fullInventoryRowCount":11}
        codes=self.codes(self.validate(reports))
        self.assertIn("PLACEMENT_SUMMARY_FAILED", codes)
        self.assertIn("PLACEMENT_CATEGORY_COUNT", codes)

    def test_duplicate_stable_id_fails(self) -> None:
        reports = passing_reports()
        duplicate = copy.deepcopy(self.instance(reports, "prop-1"))
        reports["placement"]["instances"].append(duplicate)
        report = self.validate(reports)
        self.assertIn("DUPLICATE_STABLE_ID", self.codes(report))

    def test_runway_duplicate_layer_fails(self) -> None:
        reports = passing_reports()
        reports["placement"]["runway"]["authoritativeLayerCount"] = 2
        report = self.validate(reports)
        self.assertIn("DUPLICATE_RUNWAY_LAYER", self.codes(report))

    def test_building_zone_coverage_failure_is_detected(self) -> None:
        reports = passing_reports()
        reports["building"]["zones"][1]["eligibleFootprints"] = 100
        reports["building"]["zones"][1]["representedFootprints"] = 94
        report = self.validate(reports)
        self.assertIn("BUILDING_COVERAGE", self.codes(report))

    def test_vegetation_distribution_failure_is_detected(self) -> None:
        reports = passing_reports()
        reports["vegetation"]["excludedSurfaceViolationCount"] = 1
        report = self.validate(reports)
        self.assertIn("VEGETATION_EXCLUSION", self.codes(report))

    def test_missing_eligible_water_feature_is_detected(self) -> None:
        reports = passing_reports()
        reports["water"]["features"][0]["represented"] = False
        reports["water"]["features"][0]["exclusionReason"] = "omitted"
        report = self.validate(reports)
        self.assertIn("MISSING_SIGNIFICANT_WATER", self.codes(report))

    def test_missing_hlod_asset_is_detected(self) -> None:
        reports = passing_reports()
        reports["hlod"]["cells"][0]["missingAssetCount"] = 1
        report = self.validate(reports)
        self.assertIn("MISSING_HLOD_ASSET", self.codes(report))

    def test_each_v2_budget_limit_fails_when_exceeded(self) -> None:
        cases = {
            "maximumVisibleDrawCalls": (131, "DRAW_CALL_BUDGET"),
            "maximumVisibleTriangles": (650001, "TRIANGLE_BUDGET"),
            "maximumVisibleMaterials": (31, "MATERIAL_BUDGET"),
            "estimatedRuntimeTextureMemoryBytes": (220 * 1024 * 1024 + 1, "TEXTURE_MEMORY_BUDGET"),
            "transparentMaterialSlots": (4, "TRANSPARENT_MATERIAL_BUDGET"),
            "steadyStateManagedAllocations": (1, "STEADY_STATE_ALLOCATION"),
        }
        for field, (value, expected_code) in cases.items():
            with self.subTest(field=field):
                reports = passing_reports()
                reports["budgets"][field] = value
                report = self.validate(reports)
                self.assertIn(expected_code, self.codes(report))

    def test_explicit_unmeasured_allocation_blocker_does_not_require_invented_number(self) -> None:
        reports = passing_reports()
        reports["budgets"]["evidenceComplete"] = False
        reports["budgets"]["evidenceBlockers"] = [
            {
                "code": "STEADY_STATE_ALLOCATION_UNMEASURED",
                "message": "Runtime allocation evidence was not collected.",
            }
        ]
        reports["budgets"]["steadyStateManagedAllocations"] = None
        report = self.validate(reports)
        codes = self.codes(report)
        self.assertIn("STEADY_STATE_ALLOCATION_UNMEASURED", codes)
        self.assertNotIn("INVALID_NUMBER", codes)


if __name__ == "__main__":
    unittest.main()
