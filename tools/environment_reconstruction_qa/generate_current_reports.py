#!/usr/bin/env python3
"""Adapt current Reconstruction V2 manifests into candid QA input reports.

This adapter never upgrades design intent, legacy checkpoint measurements, or
partial source coverage into acceptance evidence. Unknown current-build values
remain null and every missing evidence class is emitted as a fail-closed blocker.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import pathlib
import struct
from typing import Any


REPORT_NAMES = {
    "surface": "production_terrain_surface_report.json",
    "placement": "placement_validation.json",
    "building": "building_coverage_report.json",
    "vegetation": "vegetation_coverage_report.json",
    "water": "water_coverage_report.json",
    "hlod": "hlod_validation_report.json",
    "budgets": "performance_budget_report.json",
}
REPORT_TYPES = {
    "surface": "production_terrain_surface",
    "placement": "production_placement",
    "building": "building_coverage",
    "vegetation": "vegetation_coverage",
    "water": "water_coverage",
    "hlod": "environment_hlod",
    "budgets": "quest_environment_budgets",
}


def read_json(path: pathlib.Path) -> dict[str, Any]:
    data = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(data, dict):
        raise ValueError(f"JSON root must be an object: {path}")
    return data


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def png_size(path: pathlib.Path) -> tuple[int | None, int | None]:
    try:
        with path.open("rb") as stream:
            header = stream.read(24)
        if header[:8] == b"\x89PNG\r\n\x1a\n" and header[12:16] == b"IHDR":
            return struct.unpack(">II", header[16:24])
    except OSError:
        pass
    return None, None


def blocker(code: str, message: str) -> dict[str, str]:
    return {"code": code, "message": message}


def csv_inventory(path: pathlib.Path) -> dict[str, Any]:
    counts: dict[str, int] = {}
    row_count = 0
    with path.open("r", encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        fields = list(reader.fieldnames or [])
        for row in reader:
            row_count += 1
            category = str(row.get("category", "")).strip()
            counts[category] = counts.get(category, 0) + 1
    return {"rowCount": row_count, "categoryCounts": counts, "fields": fields, "sha256": sha256(path)}


def header(kind: str, build_id: str, surface_sha: str, blockers: list[dict[str, str]]) -> dict[str, Any]:
    result: dict[str, Any] = {
        "schemaVersion": 1,
        "reportType": REPORT_TYPES[kind],
        "environmentBuildId": build_id,
        "evidenceComplete": not blockers,
        "evidenceBlockers": blockers,
    }
    if kind == "surface":
        result["manifestSha256"] = surface_sha
    else:
        result["surfaceManifestSha256"] = surface_sha
    return result


def provenance_entry(root: pathlib.Path, path: pathlib.Path) -> dict[str, Any]:
    try:
        relative = str(path.relative_to(root)).replace("\\", "/")
    except ValueError:
        relative = str(path)
    return {"path": relative, "bytes": path.stat().st_size, "sha256": sha256(path)}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=pathlib.Path, default=pathlib.Path.cwd())
    parser.add_argument("--output-dir", type=pathlib.Path, required=True)
    parser.add_argument(
        "--artifact-root",
        type=pathlib.Path,
        help="Existing Reconstruction V2 artifact root containing serialized HLOD and placement evidence.",
    )
    parser.add_argument("--performance-evidence", type=pathlib.Path)
    args = parser.parse_args()

    root = args.repo_root.resolve()
    out = args.output_dir.resolve()
    out.mkdir(parents=True, exist_ok=True)
    paths = {
        "surface_manifest": root / "QuestFlight/Assets/Production/Environment/ReconstructionV2/KBDU_ProductionTerrainSurfaceV2_SourceManifest.json",
        "surface": root / "QuestFlight/Assets/Production/Environment/ReconstructionV2/KBDU_ProductionTerrainSurfaceV2.json",
        "normal": root / "QuestFlight/Assets/Production/Environment/ReconstructionV2/KBDU_NearTerrainV2_Normal.png",
        "placement": root / "tools/kbdu_environment_reconstruction/production_placement_manifest.json",
        "building": root / "tools/kbdu_environment_reconstruction/building_coverage_report.json",
        "vegetation": root / "tools/kbdu_environment_reconstruction/vegetation_distribution_manifest.json",
        "parking": root / "tools/kbdu_environment_reconstruction/airport_parking_stand_manifest.json",
        "pipeline": root / "tools/kbdu_environment_reconstruction/content_pipeline_report.json",
        "legacy_source": root / "tools/kbdu_environment/kbdu_source_manifest.json",
        "hlod_recipe": root / "QuestFlight/Assets/Production/Environment/ReconstructionV2/KBDU_RuntimeHlodRecipeV2.json",
        "hlod_prefab": root / "QuestFlight/Assets/Production/Environment/PhotorealTerrainV1/KBDU_BatchedContext.prefab",
    }
    artifact_root = args.artifact_root.resolve() if args.artifact_root else None
    evidence_paths: dict[str, pathlib.Path] = {}
    if artifact_root:
        evidence_paths = {
            "hlod_audit": artifact_root / "hlod_serialized_audit/hlod_render_budget_report.json",
            "placement_summary": artifact_root / "placement_validation_after_root_seating/placement_validation.json",
            "placement_csv": artifact_root / "placement_validation_after_root_seating/placement_validation.csv",
        }
    missing = [str(path) for path in paths.values() if not path.is_file()]
    missing.extend(str(path) for path in evidence_paths.values() if not path.is_file())
    if missing:
        raise FileNotFoundError("Required adapter input(s) missing:\n" + "\n".join(missing))

    source_manifest = read_json(paths["surface_manifest"])
    surface_source = read_json(paths["surface"])
    placement_source = read_json(paths["placement"])
    building_source = read_json(paths["building"])
    vegetation_source = read_json(paths["vegetation"])
    parking_source = read_json(paths["parking"])
    pipeline_source = read_json(paths["pipeline"])
    legacy_source = read_json(paths["legacy_source"])
    hlod_audit = read_json(evidence_paths["hlod_audit"]) if evidence_paths else None
    placement_summary = read_json(evidence_paths["placement_summary"]) if evidence_paths else None
    surface_sha = sha256(paths["surface_manifest"])
    input_entries = [provenance_entry(root, path) for path in paths.values()]
    input_entries.extend(provenance_entry(root, path) for path in evidence_paths.values())
    build_digest = hashlib.sha256("".join(item["sha256"] for item in input_entries).encode("ascii")).hexdigest()
    build_id = f"reconstruction-v2-source-set-{build_digest[:16]}"

    layers = surface_source.get("layers", [])
    current_layers = [item for item in layers if isinstance(item, dict) and item.get("id") in {"near_4km_v2", "airport_5m_v2"}]
    vertices = sum(int(item.get("width", 0)) * int(item.get("height", 0)) for item in current_layers)
    near = next((item for item in current_layers if item.get("id") == "near_4km_v2"), {})
    normal_width, normal_height = png_size(paths["normal"])
    surface_blockers = [
        blocker("SURFACE_UNITY_INTEGRATION_UNPROVEN", "The V2 source surface exists, but no machine-readable evidence binds it to the authored product scene or runtime sampler."),
        blocker("SURFACE_SAMPLER_UNPROVEN", "Height, normal, terrain-zone, imagery-UV, land-cover, runway/apron, water, and best-fit-plane sampler capabilities have not been validated."),
        blocker("SURFACE_GEOMETRIC_ERROR_UNMEASURED", "Maximum, average, and airport-zone geometric error against the authoritative source are not measured."),
        blocker("SURFACE_SEAMS_UNVALIDATED", "Current-build seam count and fixed-mesh/stable-normal runtime behavior are not validated."),
    ]
    surface = header("surface", build_id, surface_sha, surface_blockers)
    surface.update({
        "authoritative": False,
        "enuOriginAligned": True,
        "fixedRuntimeMesh": False,
        "stableNormalsBakedOffline": False,
        "seamValidationPassed": False,
        "runtimeCameraDependentMorphing": False,
        "brokenSeamCount": None,
        "coordinateSystem": "local_enu",
        "sourceResolutionMeters": source_manifest.get("source_resolution_meters"),
        "nearCoverageWidthMeters": (int(near.get("width", 0)) - 1) * float(near.get("spacing_meters", 0)),
        "nearCoverageHeightMeters": (int(near.get("height", 0)) - 1) * float(near.get("spacing_meters", 0)),
        "runtimeVertexCount": vertices,
        "runtimeTriangleCount": source_manifest.get("runtime_triangle_estimate"),
        "normalMapResolutionPixels": normal_width if normal_width == normal_height else None,
        "maximumGeometricErrorMeters": None,
        "averageGeometricErrorMeters": None,
        "airportZoneErrorMeters": None,
        "samplerCapabilities": {key: False for key in ("height", "normal", "terrainZone", "macroImageryUv", "landCover", "runwayApron", "water", "bestFitPlane")},
        "sourceEvidence": {"runtimeLayerIds": [item.get("id") for item in current_layers], "normalMapDimensions": [normal_width, normal_height]},
    })

    placements = placement_source.get("placements", [])
    category_counts: dict[str, int] = {}
    cell_ids: set[str] = set()
    pending_foundations = 0
    for item in placements:
        if not isinstance(item, dict):
            continue
        category = str(item.get("category", ""))
        category_counts[category] = category_counts.get(category, 0) + 1
        cell = item.get("lod_hlod", {}).get("cell_id") if isinstance(item.get("lod_hlod"), dict) else None
        if isinstance(cell, str):
            cell_ids.add(cell)
        foundation = item.get("foundation")
        if isinstance(foundation, dict) and str(foundation.get("mode", "")).startswith("pending_"):
            pending_foundations += 1
    placement_csv = csv_inventory(evidence_paths["placement_csv"]) if evidence_paths else None
    if placement_summary and placement_csv:
        binding_ok = (
            placement_summary.get("totalPlacements") == len(placements) == placement_csv["rowCount"]
            and placement_summary.get("buildings") == category_counts.get("building", 0)
            and placement_summary.get("trees") == (
                category_counts.get("vegetation", 0) + category_counts.get("vegetation_candidate", 0)
            )
            and placement_summary.get("parkedAircraft") == category_counts.get("parked_aircraft", 0)
        )
        placement_blockers = []
        if not binding_ok:
            placement_blockers.append(blocker("PLACEMENT_EVIDENCE_COUNT_MISMATCH", "Unity placement summary, full CSV inventory, and source manifest counts do not match."))
        placement_blockers.extend([
            blocker("PLACEMENT_CATEGORIES_INCOMPLETE", "The serialized placement evidence has buildings, trees, and parked aircraft but no airport props, fences, roads, or water-body placement records."),
            blocker("PLACEMENT_SCENE_IDENTITY_UNBOUND", "The Unity placement summary does not record a scene path/hash, so activation in ProductionVerticalSlice is not established by this report alone."),
            blocker("RUNWAY_CONTACT_UNMEASURED", "Runway/marking/collider gap and z-fighting evidence is not part of the current placement validation."),
        ])
    else:
        placement_blockers = [
            blocker("PLACEMENT_UNITY_INTEGRATION_UNPROVEN", "No serialized Unity placement validation was supplied."),
            blocker("PLACEMENT_SURFACE_BINDING_INCOMPLETE", f"{pending_foundations} building foundations remain pending authoritative-surface placement."),
            blocker("PLACEMENT_CATEGORIES_INCOMPLETE", "The manifest contains buildings, vegetation, and parked-aircraft intent only; props, fences, roads, and water-body records are absent."),
            blocker("PARKED_AIRCRAFT_CONTACT_UNMEASURED", "Parked-aircraft contact and orientation have not been measured."),
            blocker("RUNWAY_CONTACT_UNMEASURED", "Runway/marking/collider gap and z-fighting evidence is unavailable."),
        ]
    placement = header("placement", build_id, surface_sha, placement_blockers)
    placement.update({
        "instances": [], "declaredInstanceCount": len(placements), "categoryCounts": category_counts,
        "duplicateOverlapCount": None, "missingSourceLicenseEntryCount": None,
        "runway": {"authoritativeLayerCount": None, "zFightingDetected": None, "floatingMarkingsDetected": None,
                    "runwayTerrainMaximumGapMeters": None, "markingRunwayMaximumGapMeters": None,
                    "colliderSurfaceMaximumDisagreementMeters": None},
        "sourceEvidence": {"sourcePlacementCount": len(placements), "distinctHlodCellReferenceCount": len(cell_ids),
                           "parkingStandCount": parking_source.get("stand_count"), "activeParkedAircraftCount": parking_source.get("placement_count"),
                           "unityAssetsModified": pipeline_source.get("unity_assets_modified")},
        "aggregateValidation": ({
            "evidenceKind": "UNITY_SERIALIZED_PLACEMENT_VALIDATION",
            "summaryPassed": placement_summary.get("passed"),
            "summaryClassification": placement_summary.get("classification"),
            "fullInventoryCsvSha256": placement_csv["sha256"],
            "fullInventoryRowCount": placement_csv["rowCount"],
            "fullInventoryCategoryCounts": placement_csv["categoryCounts"],
            "buildings": placement_summary.get("buildings"),
            "trees": placement_summary.get("trees"),
            "parkedAircraft": placement_summary.get("parkedAircraft"),
            "duplicateStableIds": placement_summary.get("duplicateStableIds"),
            "missingFootprints": placement_summary.get("missingFootprints"),
            "invalidLicenses": placement_summary.get("invalidLicenses"),
            "unvalidatedVegetationExclusions": placement_summary.get("unvalidatedVegetationExclusions"),
            "buildingMedianFoundationErrorMeters": placement_summary.get("buildingMedianFoundationErrorMeters"),
            "buildingP95FoundationErrorMeters": placement_summary.get("buildingP95FoundationErrorMeters"),
            "buildingMaximumFoundationErrorMeters": placement_summary.get("buildingMaximumFoundationErrorMeters"),
            "treeMinimumRootOffsetMeters": placement_summary.get("treeMinimumRootOffsetMeters"),
            "treeMaximumRootOffsetMeters": placement_summary.get("treeMaximumRootOffsetMeters"),
            "parkedAircraftMaximumGearErrorMeters": placement_summary.get("parkedAircraftMaximumGearErrorMeters"),
            "parkedAircraftMaximumHeadingErrorDegrees": placement_summary.get("parkedAircraftMaximumHeadingErrorDegrees"),
        } if placement_summary and placement_csv else None),
    })

    zone_map = {"0-2km": "near_0_2km", "2-4km": "extended_near_2_4km", "4-6km": "mid_4_6km"}
    zones = []
    for source_id, qa_id in zone_map.items():
        item = building_source.get("zones", {}).get(source_id, {})
        zones.append({"id": qa_id, "eligibleFootprints": item.get("source_footprints"),
                      "representedFootprints": item.get("represented_footprints"),
                      "sourceStatus": item.get("status"),
                      "coverageEvidenceComplete": item.get("status") == "PASS"})
    building_complete = (
        building_source.get("v2_acceptance_status") == "PASS"
        and all(item.get("coverageEvidenceComplete") for item in zones)
        and placement_summary is not None
        and placement_summary.get("buildings") == sum(int(item.get("representedFootprints", 0)) for item in zones)
        and placement_summary.get("buildingMaximumFoundationErrorMeters", 1.0) <= 0.15
    )
    building_blockers = [] if building_complete else [
        blocker("BUILDING_EVIDENCE_INCOMPLETE", "Complete 0-6 km source coverage and matching Unity grounding evidence are not both available."),
    ]
    building = header("building", build_id, surface_sha, building_blockers)
    building.update({"zones": zones,
                     "duplicateFootprintCount": placement_summary.get("duplicateStableIds", 0) if placement_summary else None,
                     "unexplainedExcludedFootprintCount": 0 if building_complete else None,
                     "missingSourceLicenseEntryCount": placement_summary.get("invalidLicenses", 0) if placement_summary else None,
                     "sourceAcceptanceStatus": building_source.get("v2_acceptance_status"),
                     "sourceLimitations": building_source.get("checkpoint_source_limitations")})

    veg_distribution = vegetation_source.get("distribution_source", {})
    species = vegetation_source.get("species_library", {})
    vegetation_blockers = [
        blocker("VEGETATION_MASK_MISSING", f"Vegetation distribution source status is {veg_distribution.get('status')}; no authoritative canopy mask is generated."),
        blocker("VEGETATION_SPECIES_LIBRARY_MISSING", f"Species library status is {species.get('status')}; required runtime species assets are not authored."),
        blocker("VEGETATION_EXCLUSIONS_UNVALIDATED", "Runway/apron/building/road/open-water/parking exclusion violations have not been measured."),
        blocker("VEGETATION_RUNTIME_AND_VISUAL_UNVALIDATED", "No current V2 runtime LOD counts, stereo capture, pattern review, or popping review exists."),
    ]
    vegetation = header("vegetation", build_id, surface_sha, vegetation_blockers)
    vegetation.update({
        "maskAvailable": False, "deterministicDistribution": False, "aerialCanopyRepresented": False,
        "maskSource": str(veg_distribution.get("status", "unknown")), "deterministicSeed": None,
        "speciesVariantCount": 0, "excludedSurfaceViolationCount": None, "perTreeUpdateComponentCount": None,
        "uniquePerTreeMaterialCount": None, "missingSourceLicenseEntryCount": None,
        "obviousRepeatedPatternDetected": None, "uniformDistributionDetected": None,
        "denseGrassCarpetDetected": None, "stereoBillboardInstabilityDetected": None, "severeLodPoppingDetected": None,
        "lodCounts": {"lod0Close3d": 0, "lod1Simplified3d": 0, "lod2ClusterOrImpostor": 0,
                      "farOrthophotoAreaSquareMeters": None},
        "sourceEvidence": {"checkpointCandidateCount": veg_distribution.get("checkpoint_candidate_count"),
                           "requiredSpeciesSlots": species.get("required_slots"), "lodPolicy": vegetation_source.get("lod_policy")},
    })

    water_source_count = legacy_source.get("budgets", {}).get("vectors", {}).get("category_counts", {}).get("water")
    water_blockers = [
        blocker("WATER_SOURCE_COVERAGE_INCOMPLETE", "Only a capped legacy candidate count is available; no authoritative V2 water feature inventory covers near 0-4 km and mid 4-12 km."),
        blocker("WATER_ELIGIBILITY_UNCLASSIFIED", "Polygon area, waterway width, and major-visible eligibility have not been classified against V2 thresholds."),
        blocker("WATER_REPRESENTATION_UNVALIDATED", "No feature-by-feature product-scene representation, terrain separation, shoreline, UV, or material evidence exists."),
    ]
    water = header("water", build_id, surface_sha, water_blockers)
    water.update({"features": [], "sourceFeatureCount": water_source_count, "missingSignificantFeatureCount": None,
                  "missingSourceLicenseEntryCount": None,
                  "sourceEvidence": {"legacyCappedCandidateCount": water_source_count,
                                     "warning": "Candidate count is not a complete V2 eligibility denominator."}})

    if hlod_audit:
        checks = {item.get("id"): item.get("status") for item in hlod_audit.get("checks", []) if isinstance(item, dict)}
        audit_inputs = hlod_audit.get("inputs", {})
        recipe_hash_current = sha256(paths["hlod_recipe"])
        prefab_hash_current = sha256(paths["hlod_prefab"])
        recipe_hash_audited = audit_inputs.get("recipe", {}).get("sha256")
        prefab_hash_audited = hlod_audit.get("prefab", {}).get("sha256")
        hlod_blockers = []
        if recipe_hash_audited != recipe_hash_current:
            hlod_blockers.append(blocker("HLOD_RECIPE_HASH_MISMATCH", f"Serialized audit recipe SHA {recipe_hash_audited} does not match current recipe SHA {recipe_hash_current}."))
        if prefab_hash_audited != prefab_hash_current:
            hlod_blockers.append(blocker("HLOD_PREFAB_HASH_MISMATCH", "Serialized audit prefab hash does not match the current authored prefab."))
        mesh_batches = hlod_audit.get("mesh_batches", [])
        cells = []
        missing_assets = 0
        far_state = bool(hlod_audit.get("context_atlas", {}).get("valid"))
        for batch in mesh_batches:
            if not isinstance(batch, dict):
                continue
            levels = batch.get("levels", {})
            present = {}
            for level in ("LOD0", "LOD1", "LOD2"):
                evidence = levels.get(level, {}) if isinstance(levels, dict) else {}
                valid = bool(evidence.get("exists") and evidence.get("valid") and evidence.get("triangle_topology_only"))
                present[level] = valid
                if not valid:
                    missing_assets += 1
            cells.append({
                "stableId": batch.get("batch_id"),
                "sizeMeters": hlod_audit.get("recipe_inventory", {}).get("logical_cell_size_meters"),
                "lod0Present": present["LOD0"], "lod1Present": present["LOD1"], "lod2Present": present["LOD2"],
                "farOrthophotoStatePresent": far_state, "missingAssetCount": sum(1 for value in present.values() if not value),
            })
        materials = hlod_audit.get("materials", {})
        shaders = hlod_audit.get("shaders", {})
        all_materials_compatible = bool(materials) and all(item.get("compatible") for item in materials.values())
        all_shaders_compatible = bool(shaders) and all(item.get("compatible") for item in shaders.values())
        hlod = header("hlod", build_id, surface_sha, hlod_blockers)
        hlod.update({
            "offlineGenerated": checks.get("three_level_mesh_coverage") == "PASS",
            "deterministic": hlod_audit.get("deterministic_output") is True,
            "sharedMaterialBatching": all_materials_compatible,
            "gpuInstancingCompatible": all_materials_compatible and all_shaders_compatible,
            "runtimeMeshCombination": False,
            "xrCrossfadeShimmerDetected": False if hlod_audit.get("prefab", {}).get("crossfade_disabled") is True else None,
            "missingAssetCount": missing_assets,
            "perInstanceScriptCount": 0,
            "missingSourceLicenseEntryCount": placement_summary.get("invalidLicenses", 0) if placement_summary else None,
            "cells": cells,
            "sourceEvidence": {
                "auditClassification": hlod_audit.get("classification"),
                "serializedBatchCount": len(cells),
                "logicalPlacementCellCount": hlod_audit.get("placement_inventory", {}).get("unique_cell_count"),
                "runtimeBuildingCellSizesMeters": hlod_audit.get("recipe_inventory", {}).get("runtime_building_cell_sizes_meters"),
                "meshTierTotals": hlod_audit.get("mesh_tier_totals"),
                "prefab": hlod_audit.get("prefab"),
                "contextSubsetEstimate": hlod_audit.get("context_subset_estimate"),
            },
        })
    else:
        hlod_blockers = [
            blocker("HLOD_ASSETS_NOT_GENERATED", f"{len(cell_ids)} V2 placement cell references exist, but no serialized HLOD audit was supplied."),
            blocker("HLOD_UNITY_INTEGRATION_UNPROVEN", "No serialized prefab evidence is available."),
            blocker("HLOD_XR_VISUAL_UNVALIDATED", "XR crossfade shimmer and transition stability have not been inspected for V2."),
        ]
        hlod = header("hlod", build_id, surface_sha, hlod_blockers)
        hlod.update({"offlineGenerated": False, "deterministic": False, "sharedMaterialBatching": False,
                     "gpuInstancingCompatible": False, "runtimeMeshCombination": False, "xrCrossfadeShimmerDetected": None,
                     "missingAssetCount": len(cell_ids), "perInstanceScriptCount": None,
                     "missingSourceLicenseEntryCount": None, "cells": [],
                     "sourceEvidence": {"distinctCellReferenceCount": len(cell_ids), "cellSizeMeters": placement_source.get("cell_size_meters")}})

    baseline: dict[str, Any] | None = None
    performance_path = args.performance_evidence.resolve() if args.performance_evidence else None
    if performance_path and performance_path.is_file():
        performance = read_json(performance_path)
        baseline = {"path": str(performance_path), "sha256": sha256(performance_path),
                    "evidenceKind": performance.get("evidenceKind"), "generatedUtc": performance.get("generatedUtc"),
                    "maximumVisibleDrawCalls": performance.get("maxEstimatedDrawCalls"),
                    "maximumVisibleTriangles": performance.get("maxEstimatedVisibleTriangles"),
                    "maximumVisibleMaterials": performance.get("sceneMaterialCount"),
                    "estimatedRuntimeTextureMemoryBytes": performance.get("estimatedTextureBytes"),
                    "transparentMaterialSlots": performance.get("transparentMaterialSlots"), "msaaSamples": performance.get("msaa")}
    budget_blockers = [
        blocker("CURRENT_V2_BUDGET_EVIDENCE_MISSING", "No static Unity budget report is bound to this Reconstruction V2 source-set build ID and surface hash."),
        blocker("STEADY_STATE_ALLOCATION_UNMEASURED", "Zero steady-state managed allocation has not been measured for the integrated V2 runtime."),
    ]
    if baseline:
        budget_blockers.append(blocker("BASELINE_BUDGET_NOT_CURRENT", "The supplied performance report is retained as rejected-checkpoint baseline only and is not attributed to V2."))
    budgets = header("budgets", build_id, surface_sha, budget_blockers)
    budgets.update({"maximumVisibleDrawCalls": None, "maximumVisibleTriangles": None, "maximumVisibleMaterials": None,
                    "estimatedRuntimeTextureMemoryBytes": None, "transparentMaterialSlots": None,
                    "steadyStateManagedAllocations": None, "msaaSamples": None,
                    "controlledShaderVariants": None, "uncontrolledTransparentOverdrawDetected": None,
                    "baselineOnly": baseline,
                    "serializedHlodSubsetEvidence": (hlod_audit.get("context_subset_estimate") if hlod_audit else None),
                    "configuredIntegratedEstimateNotAcceptedAsCurrentEvidence": (hlod_audit.get("integrated_product_budget") if hlod_audit else None)})

    reports = {"surface": surface, "placement": placement, "building": building, "vegetation": vegetation,
               "water": water, "hlod": hlod, "budgets": budgets}
    outputs = []
    for kind, report in reports.items():
        path = out / REPORT_NAMES[kind]
        path.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        outputs.append(provenance_entry(out, path))
    provenance = {
        "schemaVersion": 1, "adapter": "generate_current_reports.py", "environmentBuildId": build_id,
        "surfaceManifestSha256": surface_sha,
        "evidenceBoundary": "Source manifests and machine-readable evidence only; no visual, Unity integration, simulator, headset, or Quest performance conclusion.",
        "inputs": input_entries + ([provenance_entry(root, performance_path)] if performance_path else []),
        "outputs": outputs,
    }
    (out / "environment_reconstruction_adapter_provenance.json").write_text(
        json.dumps(provenance, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    print(out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
