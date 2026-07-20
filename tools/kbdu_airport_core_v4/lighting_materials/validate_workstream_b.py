#!/usr/bin/env python3
"""Fail-closed static validation for airport-core V4 Workstream B assets."""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import sys
from typing import Any

import numpy as np
from PIL import Image


TOOL_ROOT = pathlib.Path(__file__).resolve().parent
REPO_ROOT = TOOL_ROOT.parents[2]
sys.path.insert(0, str(TOOL_ROOT))
from build_workstream_b import estimate_astc_6x6_mipped_bytes, role_for, world_to_pixel  # noqa: E402


def load(path: pathlib.Path, issues: list[str]) -> dict[str, Any]:
    if not path.is_file():
        issues.append(f"MISSING_FILE: {path}")
        return {}
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except Exception as exc:
        issues.append(f"INVALID_JSON: {path}: {exc}")
        return {}
    if not isinstance(value, dict):
        issues.append(f"INVALID_ROOT: {path}")
        return {}
    return value


def sha256(path: pathlib.Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def require(condition: bool, code: str, detail: Any, issues: list[str]) -> None:
    if not condition:
        issues.append(f"{code}: {detail}")


def validate(repo: pathlib.Path, replacement_path: pathlib.Path) -> dict[str, Any]:
    issues: list[str] = []
    config = load(TOOL_ROOT / "config.json", issues)
    replacement = load(replacement_path, issues)
    root = repo / "QuestFlight/Assets/Production/Environment/AirportCoreV4"
    materials, clean, lighting, runway = (root / name for name in ("Materials", "CleanPlate", "Lighting", "Runway"))
    expected = [
        materials / "AirportCoreTerminal_Albedo.png", materials / "AirportCoreTerminal_Normal.png", materials / "AirportCoreTerminal_MRAO.png",
        materials / "AirportCoreHangarKit_Albedo.png", materials / "AirportCoreHangarKit_Normal.png", materials / "AirportCoreHangarKit_MRAO.png",
        materials / "AirportCoreBakedLit.shader", materials / "AirportCoreTerminalAtlas.mat", materials / "AirportCoreHangarKitAtlas.mat", materials / "SOURCE_MANIFEST.json",
        clean / "AirportCoreCleanPlate_Albedo.jpg", clean / "AirportCoreReplacementMask.png", clean / "AirportCoreOriginalShadowMask.png", clean / "SOURCE_MANIFEST.json",
        lighting / "AirportCoreLightingProfile.json", lighting / "OrthophotoSunCalibration.json", lighting / "AirportCoreLightmapPlan.json",
        lighting / "AirportCoreProbePlan.json", lighting / "AirportCoreReflectionPlan.json", lighting / "INTEGRATION_CONTRACT.md",
        runway / "AirportCoreRunway_Albedo.png", runway / "AirportCoreRunway_Normal.png", runway / "AirportCoreRunway_MRAO.png",
        runway / "AirportCoreRunway_FeatureMask.png", runway / "AirportCoreContinuousRunway.shader", runway / "AirportCoreContinuousRunway.mat",
        runway / "INTEGRATION_CONTRACT.md", runway / "SOURCE_MANIFEST.json",
    ]
    for path in expected:
        require(path.is_file(), "MISSING_ASSET", path, issues)
        require(path.with_name(path.name + ".meta").is_file(), "MISSING_META", path, issues)
    prohibited = [path for path in root.rglob("*") if path.is_file() and (path.suffix.lower() in {".zip", ".exr", ".blend1"} or "raw" in path.name.lower())]
    require(not prohibited, "RAW_OR_CACHE_IN_PRODUCT", [str(path) for path in prohibited], issues)

    material_manifest = load(materials / "SOURCE_MANIFEST.json", issues)
    atlases = material_manifest.get("atlases", [])
    require(len(atlases) == 2, "ATLAS_COUNT", len(atlases), issues)
    for atlas in atlases:
        require(atlas.get("sizePixels") == [2048, 2048], "ATLAS_SIZE", atlas, issues)
        require(len(atlas.get("tiles", [])) == 16, "ATLAS_TILE_COUNT", atlas.get("name"), issues)
        require(atlas.get("grid") == [4, 4], "ATLAS_GRID", atlas.get("grid"), issues)
    for name in ("Terminal", "HangarKit"):
        for suffix in ("Albedo", "Normal", "MRAO"):
            path = materials / f"AirportCore{name}_{suffix}.png"
            if path.is_file():
                with Image.open(path) as image:
                    require(image.size == (2048, 2048), "ATLAS_IMAGE_SIZE", f"{path}: {image.size}", issues)
                    require(image.mode == "RGB", "ATLAS_MODE", f"{path}: {image.mode}", issues)
    material_bytes = material_manifest.get("estimatedAndroidResidentBytesIfBothAtlasesLoaded", 10**12)
    expected_material_bytes = 4 * estimate_astc_6x6_mipped_bytes(2048) + 2 * estimate_astc_6x6_mipped_bytes(1024)
    require(material_bytes == expected_material_bytes, "MATERIAL_MEMORY", material_bytes, issues)

    clean_manifest = load(clean / "SOURCE_MANIFEST.json", issues)
    replacements = replacement.get("replacements", [])
    roles = {role_for(str(item.get("asset_id", ""))) for item in replacements}
    require(roles == {"executive_terminal", "type_2_t_hangar_row"}, "REPLACEMENT_ROLES", sorted(roles), issues)
    require(clean_manifest.get("replacementObjectIds") == [item.get("object_id") for item in replacements], "CLEAN_COVERAGE", clean_manifest.get("replacementObjectIds"), issues)
    require(clean_manifest.get("conditionalRegionsNotRemoved") == ["TerminalApronAuthoredStand_A"], "CONDITIONAL_AIRCRAFT_POLICY", clean_manifest.get("conditionalRegionsNotRemoved"), issues)
    for output in clean_manifest.get("outputs", []):
        path = repo / str(output.get("path"))
        require(path.is_file(), "CLEAN_OUTPUT_MISSING", path, issues)
        if path.is_file():
            require(path.stat().st_size == output.get("bytes"), "CLEAN_OUTPUT_SIZE", path, issues)
            require(sha256(path) == output.get("sha256"), "CLEAN_OUTPUT_HASH", path, issues)
    replacement_mask_path = clean / "AirportCoreReplacementMask.png"
    shadow_mask_path = clean / "AirportCoreOriginalShadowMask.png"
    if replacement_mask_path.is_file() and shadow_mask_path.is_file():
        replacement_mask = np.asarray(Image.open(replacement_mask_path)) > 0
        shadow_mask = np.asarray(Image.open(shadow_mask_path)) > 0
        require(replacement_mask.shape == (2048, 2048), "MASK_SIZE", replacement_mask.shape, issues)
        require(0 < int(replacement_mask.sum()) < 0.002 * replacement_mask.size, "REPLACEMENT_MASK_SCOPE", int(replacement_mask.sum()), issues)
        require(0 < int(shadow_mask.sum()) < 0.002 * shadow_mask.size, "SHADOW_MASK_SCOPE", int(shadow_mask.sum()), issues)
        candidate = replacement.get("parked_aircraft_and_prop_regions", [])[0]
        west, south, east, north = candidate["bounds_xz_meters"]
        half = float(config["cleanPlate"]["nearHalfExtentMeters"])
        left, top = world_to_pixel((west, north), 2048, half)
        right, bottom = world_to_pixel((east, south), 2048, half)
        require(not replacement_mask[int(top):int(bottom) + 1, int(left):int(right) + 1].any(), "CONDITIONAL_AIRCRAFT_WAS_REMOVED", candidate["region_id"], issues)
    with Image.open(clean / "AirportCoreCleanPlate_Albedo.jpg") as image:
        require(image.size == (4096, 4096), "CLEAN_PLATE_SIZE", image.size, issues)

    profile = load(lighting / "AirportCoreLightingProfile.json", issues)
    calibration = load(lighting / "OrthophotoSunCalibration.json", issues).get("inference", {})
    lightmaps = load(lighting / "AirportCoreLightmapPlan.json", issues)
    probes = load(lighting / "AirportCoreProbePlan.json", issues)
    reflections = load(lighting / "AirportCoreReflectionPlan.json", issues)
    nonaccepted_profile_states = {
        "AUTHORING_PROFILE_READY_BAKE_NOT_RUN",
        "SERIALIZED_LOW_CONFIDENCE_PROFILE_NOT_VISUALLY_ACCEPTED",
    }
    require(
        profile.get("status") in nonaccepted_profile_states,
        "BAKE_BOUNDARY",
        profile.get("status"),
        issues,
    )
    require(calibration.get("sampleCount") == len(config["lighting"]["referenceFootprints"]) >= 5, "SHADOW_SAMPLE_COUNT", calibration.get("sampleCount"), issues)
    require(calibration.get("metadataTimeOfDayAvailable") is False, "NAIP_TIME_BOUNDARY", calibration.get("metadataTimeOfDayAvailable"), issues)
    require(calibration.get("confidenceClass") in {"LOW", "MEDIUM"} and float(calibration.get("confidence", 2)) <= 0.65, "SUN_CONFIDENCE", calibration.get("confidence"), issues)
    require(0 <= float(calibration.get("sunAzimuthDegrees", -1)) < 360 and 15 <= float(calibration.get("sunElevationDegrees", -1)) <= 80, "SUN_SOLUTION", calibration, issues)
    require(lightmaps.get("maximumAtlases") <= 2 and lightmaps.get("budgetedAtlases") == 1 and lightmaps.get("atlasSizePixels") == 2048, "LIGHTMAP_BOUNDS", lightmaps, issues)
    require(probes.get("probeCount") == len(probes.get("positionsXyzMeters", [])) <= 40 and probes.get("runtimeUpdates") is False, "PROBE_BOUNDS", probes.get("probeCount"), issues)
    require(len(reflections.get("plan", [])) <= 1 and reflections.get("screenSpaceReflections") is False, "REFLECTION_BOUNDS", reflections, issues)

    runway_manifest = load(runway / "SOURCE_MANIFEST.json", issues)
    contract = runway_manifest.get("contract", {})
    require(contract.get("alternatingMacroBlocks") is False and contract.get("geometryChanges") is False and contract.get("colliderChanges") is False, "RUNWAY_CONTRACT", contract, issues)
    require(runway_manifest.get("estimatedAndroidResidentBytes") == 4 * estimate_astc_6x6_mipped_bytes(1024), "RUNWAY_MEMORY", runway_manifest.get("estimatedAndroidResidentBytes"), issues)
    for name in ("AirportCoreRunway_Albedo.png", "AirportCoreRunway_Normal.png", "AirportCoreRunway_MRAO.png", "AirportCoreRunway_FeatureMask.png"):
        path = runway / name
        if path.is_file():
            with Image.open(path) as image:
                require(image.size == (2048, 2048), "RUNWAY_IMAGE_SIZE", f"{name}: {image.size}", issues)
    building_shader = (materials / "AirportCoreBakedLit.shader").read_text(encoding="utf-8") if (materials / "AirportCoreBakedLit.shader").is_file() else ""
    runway_shader = (runway / "AirportCoreContinuousRunway.shader").read_text(encoding="utf-8") if (runway / "AirportCoreContinuousRunway.shader").is_file() else ""
    require("LIGHTMAP_ON" in building_shader and "ZWrite On" in building_shader and "Blend" not in building_shader, "BUILDING_SHADER_STATIC_CONTRACT", "missing baked/opaque contract", issues)
    require("worldPosition.xz" in runway_shader and "ZWrite On" in runway_shader and "Blend" not in runway_shader, "RUNWAY_SHADER_CONTINUITY", "missing world-space/opaque contract", issues)
    preferred_new_bytes = int(material_bytes) + int(runway_manifest.get("estimatedAndroidResidentBytes", 0)) + int(lightmaps.get("estimatedPreferredAndroidBytes", 0)) + int(reflections.get("estimatedAndroidBytes", 0))
    require(preferred_new_bytes <= 17 * 1024 * 1024, "PREFERRED_INCREMENTAL_TEXTURE_PLAN", preferred_new_bytes, issues)

    return {
        "schemaVersion": 1,
        "classification": "PASS_WORKSTREAM_B_STATIC_AUTHORING_ASSETS" if not issues else "FAIL_WORKSTREAM_B_STATIC_AUTHORING_ASSETS",
        "passed": not issues,
        "issueCount": len(issues),
        "issues": issues,
        "sunAzimuthDegrees": calibration.get("sunAzimuthDegrees"),
        "sunElevationDegrees": calibration.get("sunElevationDegrees"),
        "preferredIncrementalAndroidBytes": preferred_new_bytes,
        "evidenceBoundary": "Static authoring assets only. Unity shader compilation/import, bake, visual acceptance, stereo, and Quest performance are unproven.",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=pathlib.Path, default=REPO_ROOT)
    parser.add_argument("--replacement-manifest", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path)
    args = parser.parse_args()
    report = validate(args.repo_root.resolve(), args.replacement_manifest.resolve())
    if args.output:
        args.output.resolve().parent.mkdir(parents=True, exist_ok=True)
        args.output.resolve().write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2, sort_keys=True))
    return 0 if report["passed"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
