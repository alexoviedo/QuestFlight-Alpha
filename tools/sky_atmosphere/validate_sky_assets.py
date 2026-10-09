#!/usr/bin/env python3
"""Validate the committed photoreal-sky derivative, provenance, and Unity import contract."""

from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path
import re

from build_hdri_derivative import read_rgbe


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
ASSET_ROOT = REPOSITORY_ROOT / "QuestFlight/Assets/Production/Environment/Sky"
HDR_PATH = ASSET_ROOT / "Kloppenheim03_PureSky_2K.hdr"
HDR_META_PATH = HDR_PATH.with_suffix(HDR_PATH.suffix + ".meta")
MATERIAL_PATH = ASSET_ROOT / "Kloppenheim03_PureSky_Skybox.mat"
MANIFEST_PATH = ASSET_ROOT / "SOURCE_MANIFEST.json"
DECISION_PATH = REPOSITORY_ROOT / "docs/MOUNTAIN_HORIZON_V2_DECISION.md"
DEM_PATH = REPOSITORY_ROOT / "tools/kbdu_orthophoto/kbdu_far_dem_100m.json"
ENVIRONMENT_PREFAB_PATH = (
    REPOSITORY_ROOT / "QuestFlight/Assets/Production/Environment/ProductionEnvironmentRoot.prefab"
)
ENVIRONMENT_AUTHORING_PATH = (
    REPOSITORY_ROOT / "QuestFlight/Assets/Editor/StandaloneEnvironmentArtAuthoring.cs"
)


# The immutable USGS samples feed the approved transition-ring repair; this is
# topology identity, not a performance acceptance budget.
FAR_MESH_PATH = REPOSITORY_ROOT / "QuestFlight/Assets/Production/Environment/Generated/Terrain_Far24km_Immutable_USGS_Mesh.asset"
FAR_MESH_GUID = "f7e9defd4f2f8134ea4ed04d496c0495"
FAR_VERTEX_COUNT = 47112
FAR_INDEX_COUNT = 261552


def validate_far_topology(prefab: str, mesh: str, meta: str, authoring: str) -> list[str]:
    failures = []
    require(f"guid: {FAR_MESH_GUID}" in meta and
            f"m_Mesh: {{fileID: 4300000, guid: {FAR_MESH_GUID}, type: 2}}" in prefab,
            "Far terrain must reference the preserved transition mesh GUID", failures)
    require(f"farTerrainSourceVertexCount: {FAR_VERTEX_COUNT}" in prefab and
            f"farTerrainSourceIndexCount: {FAR_INDEX_COUNT}" in prefab,
            "Serialized far transition topology differs from the approved repair", failures)
    require(re.search(r"m_VertexCount: (\d+)", mesh) is not None and
            int(re.search(r"m_VertexCount: (\d+)", mesh)[1]) == FAR_VERTEX_COUNT,
            "Stored far mesh vertex count differs from the transition repair", failures)
    counts = re.findall(r"indexCount: (\d+)", mesh)
    require(len(counts) == 1 and int(counts[0]) == FAR_INDEX_COUNT,
            "Stored far mesh must retain one bounded transition submesh", failures)
    require("farTerrainSmoothingPassCount: 2" in prefab and
            "ApplyTwoPassFarSmoothing(generated)" in authoring and
            "TerrainDefectRepairV1Authoring.BuildTransitionMesh(" in authoring and
            "stored.triangles.Length / 3 != 87184" in authoring,
            "Offline two-pass far transition authoring contract changed", failures)
    return failures


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def require(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)


def astc_mipped_cubemap_bytes(face_size: int, block_size: int) -> int:
    total = 0
    size = face_size
    while True:
        blocks = math.ceil(size / block_size)
        total += blocks * blocks * 16 * 6
        if size == 1:
            return total
        size = max(1, size // 2)


def main() -> int:
    failures: list[str] = []
    required = [HDR_PATH, HDR_META_PATH, MATERIAL_PATH, MATERIAL_PATH.with_suffix(".mat.meta"),
                MANIFEST_PATH, MANIFEST_PATH.with_suffix(".json.meta"), DECISION_PATH]
    for path in required:
        require(path.is_file(), f"Missing required file: {path.relative_to(REPOSITORY_ROOT)}", failures)
    if failures:
        print(json.dumps({"classification": "FAIL", "failures": failures}, indent=2))
        return 1

    manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    selected = [candidate for candidate in manifest["approved_candidate_evaluation"]
                if candidate["decision"] == "selected"]
    require(len(manifest["approved_candidate_evaluation"]) == 3,
            "Manifest must record all three approved candidates", failures)
    require(len(selected) == 1 and selected[0]["asset_id"] == "kloppenheim_03_puresky",
            "Manifest must select exactly Kloppenheim 03 Pure Sky", failures)
    require(manifest["license"]["spdx"] == "CC0-1.0", "License must be CC0-1.0", failures)
    for candidate in manifest["approved_candidate_evaluation"]:
        require(candidate["asset_url"].startswith("https://polyhaven.com/a/"),
                f"Non-official candidate asset URL: {candidate['asset_id']}", failures)
        require(candidate["info_api_url"].startswith("https://api.polyhaven.com/info/"),
                f"Non-official candidate metadata URL: {candidate['asset_id']}", failures)
        require(candidate["files_api_url"].startswith("https://api.polyhaven.com/files/"),
                f"Non-official candidate files URL: {candidate['asset_id']}", failures)

    derivative = manifest["runtime_derivative"]
    require(HDR_PATH.stat().st_size == derivative["bytes"], "Derivative byte count mismatch", failures)
    require(sha256(HDR_PATH) == derivative["sha256"], "Derivative SHA-256 mismatch", failures)
    linear, header = read_rgbe(HDR_PATH)
    require(header["width"] == 2048 and header["height"] == 1024,
            f"Derivative dimensions are {header['width']}x{header['height']}, expected 2048x1024", failures)
    require(derivative["processing"].startswith("Linear-radiance 2x2 box filter") or
            derivative["processing"].startswith("Linear-radiance 2×2 box filter"),
            "Manifest processing must preserve linear radiance", failures)
    require(int((~(linear == linear)).sum()) == 0, "Derivative contains NaNs", failures)
    require(float(linear.min()) >= 0.0, "Derivative contains negative radiance", failures)

    meta = HDR_META_PATH.read_text(encoding="utf-8")
    require("enableMipMap: 1" in meta, "HDR cubemap mipmaps are disabled", failures)
    require("sRGBTexture: 0" in meta, "HDR cubemap must remain linear", failures)
    require("generateCubemap: 2" in meta, "HDR importer is not cylindrical cubemap generation", failures)
    require("textureShape: 2" in meta, "HDR importer is not TextureCube", failures)
    android_match = re.search(
        r"buildTarget: Android(?P<body>.*?)(?:\n  - serializedVersion:|\n  spriteSheet:)", meta, re.DOTALL)
    require(android_match is not None, "Android platform override is missing", failures)
    if android_match:
        android = android_match.group("body")
        require("maxTextureSize: 1024" in android, "Android cubemap face cap is not 1024", failures)
        require("textureFormat: 68" in android, "Android format is not Unity ASTC_HDR_6x6 (68)", failures)
        require("overridden: 1" in android, "Android override is disabled", failures)

    texture_guid = re.search(r"^guid: ([0-9a-f]{32})$", meta, re.MULTILINE)
    material = MATERIAL_PATH.read_text(encoding="utf-8")
    require(texture_guid is not None and texture_guid.group(1) in material,
            "Skybox material does not reference the HDR cubemap GUID", failures)
    require("m_Shader: {fileID: 103, guid: 0000000000000000f000000000000000, type: 0}" in material,
            "Skybox material does not use Unity's built-in Skybox/Cubemap shader", failures)
    expected_exposure = float(manifest["product_integration"]["skybox_exposure"])
    expected_rotation = float(manifest["product_integration"]["skybox_rotation_degrees"])
    require(math.isclose(float(re.search(r"- _Exposure: ([0-9.eE+-]+)", material)[1]), expected_exposure, abs_tol=1e-5) and
            math.isclose(float(re.search(r"- _Rotation: ([0-9.eE+-]+)", material)[1]), expected_rotation, abs_tol=1e-5),
            "Reviewed skybox exposure/rotation contract changed", failures)

    estimated = astc_mipped_cubemap_bytes(1024, 6)
    require(estimated == manifest["unity_import"]["estimated_android_mipped_cubemap_bytes"],
            "ASTC HDR 6x6 memory estimate mismatch", failures)

    raw_candidates = list(REPOSITORY_ROOT.rglob("*_4k.hdr"))
    require(not raw_candidates, "Raw 4K HDR source is present inside the repository", failures)
    decision = DECISION_PATH.read_text(encoding="utf-8")
    require("Option A" in decision and "fixed USGS-derived far DEM mesh" in decision,
            "Mountain V2 decision does not retain the immutable real-DEM mesh", failures)
    # Human-readable decision prose changes across inspected iterations; the
    # machine-readable provenance carries the acceptance boundary.
    boundary = manifest.get("acceptance_boundary", "")
    require("performance are not accepted" in boundary and "Quest/simulator captures" in boundary,
            "Sky provenance must retain the explicit visual/performance acceptance boundary", failures)
    integration_authoring = REPOSITORY_ROOT / manifest["product_integration"]["authoring_path"]
    require('skybox.SetFloat("_Rotation", -64.123046875f)' in integration_authoring.read_text(encoding="utf-8") and
            math.isclose(expected_rotation, -64.123046875, abs_tol=1e-8),
            "Product sky rotation must match the reviewed AirportCoreV4 sun alignment", failures)


    dem = json.loads(DEM_PATH.read_text(encoding="utf-8"))
    grid = dem["grid"]
    constraints = dem["rendering_constraints"]
    require(grid["width"] == 241 and grid["height"] == 241 and grid["spacing_meters"] == 100.0,
            "Option A must remain the immutable 241x241, 100 m USGS DEM", failures)
    require(grid["half_extent_meters"] == 12000.0 and grid["inner_half_extent_meters"] == 6000.0,
            "Option A far/mid extents changed", failures)
    require(constraints == {
        "billboard": False,
        "camera_relative_scaling": False,
        "crossfade_or_dither": False,
        "immutable_mesh": True,
        "offline_baked_normals": True,
        "runtime_lod_groups": False,
        "sealed_outer_and_inner_skirts": True,
    }, "Option A rendering constraints changed", failures)
    prefab = ENVIRONMENT_PREFAB_PATH.read_text(encoding="utf-8")
    require("m_Name: Terrain_Far24km_Immutable_USGS" in prefab,
            "Serialized authoritative far-terrain object is missing", failures)
    authoring = ENVIRONMENT_AUTHORING_PATH.read_text(encoding="utf-8")
    failures.extend(validate_far_topology(prefab, FAR_MESH_PATH.read_text(encoding="utf-8"),
        FAR_MESH_PATH.with_suffix(".asset.meta").read_text(encoding="utf-8"), authoring))

    report = {
        "classification": "PASS" if not failures else "FAIL",
        "derivative": {
            "path": str(HDR_PATH.relative_to(REPOSITORY_ROOT)).replace("\\", "/"),
            "bytes": HDR_PATH.stat().st_size,
            "sha256": sha256(HDR_PATH),
            "width": header["width"],
            "height": header["height"],
        },
        "unity": {
            "android_format": "ASTC_HDR_6x6",
            "android_max_face_size": 1024,
            "estimated_mipped_bytes": estimated,
            "material_shader": "Skybox/Cubemap",
            "material_exposure": expected_exposure,
            "material_rotation_degrees": expected_rotation,
        },
        "candidate_count": len(manifest["approved_candidate_evaluation"]),
        "selected_candidate": selected[0]["asset_id"] if len(selected) == 1 else None,
        "mountain_option_a": {
            "source": "USGS 3DEP frozen snapshot 20260710T214309Z",
            "grid": "241x241 at 100 m",
            "serialized_vertices": FAR_VERTEX_COUNT,
            "serialized_triangles": FAR_INDEX_COUNT // 3,
            "selected": True,
            "visual_acceptance": "PARTIAL_24_VIEW_EDITOR_SCORE_6_3_RECORDED_STEREO_PENDING",
        },
        "failures": failures,
    }
    print(json.dumps(report, indent=2))
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
