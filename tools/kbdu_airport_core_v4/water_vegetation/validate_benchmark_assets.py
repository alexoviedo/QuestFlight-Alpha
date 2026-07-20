#!/usr/bin/env python3
"""Offline contract validator for bounded V4 water and vegetation candidates."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOL_DIR))
import generate_benchmark_assets as generate  # noqa: E402


def sha256(path: Path) -> str:
    return generate.sha256(path)


def check(condition: bool, code: str, detail: str, failures: list[dict]) -> None:
    if not condition:
        failures.append({"code": code, "detail": detail})


def find_repo() -> Path:
    current = TOOL_DIR
    while current.parent != current:
        if (current / ".git").exists() and (current / "QuestFlight").is_dir():
            return current
        current = current.parent
    raise RuntimeError("repository root not found")


def resolve_record(record_path: str, root: Path, repo: Path, blend: Path | None) -> Path | None:
    prefix = "Assets/Production/Environment/AirportCoreV4/"
    if record_path.startswith(prefix):
        return root / record_path[len(prefix) :]
    if record_path == "Assets/ArtSource/Blender/KBDU/VegetationHeroCluster.blend":
        return blend
    if record_path.startswith("tools/"):
        return repo / record_path
    return None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, help="AirportCoreV4 root")
    parser.add_argument("--blend")
    parser.add_argument("--artifact-dir")
    parser.add_argument("--strict", action="store_true")
    args = parser.parse_args()

    root = Path(args.root).resolve()
    blend = Path(args.blend).resolve() if args.blend else None
    repo = find_repo()
    failures: list[dict] = []
    warnings: list[dict] = [
        {"code": "UNITY_INTEGRATION_PENDING", "detail": "No product prefab/scene binding is asserted by this package."},
        {"code": "MINOR_WATER_UV_BINDING_PENDING", "detail": "The normalized minor-water mask is not georeferenced to final benchmark UVs."},
        {"code": "VEGETATION_VISUAL_ACCEPTANCE_PENDING", "detail": "Offline preview shows authored alpha-card layering; full-resolution Unity and stereo quality remain unproven."},
        {"code": "STEREO_AND_QUEST_PENDING", "detail": "No simulator stereo or physical Quest performance evidence is produced here."},
    ]

    required = [
        root / "Water/Textures/AirportCoreReservoir_WaveA_NM.png",
        root / "Water/Textures/AirportCoreReservoir_WaveB_NM.png",
        root / "Water/Textures/AirportCoreReservoir_DepthShoreRoughnessSuppression.png",
        root / "Water/Textures/AirportCoreMinorWater_TerrainMask.png",
        root / "Water/Shaders/KBDUReservoirWaterV4.shader",
        root / "Water/Shaders/KBDUMinorWaterTerrainEffect.hlsl",
        root / "Water/Shaders/KBDUReservoirGroundSuppression.hlsl",
        root / "Water/Materials/AirportCoreReservoir_V4.mat",
        root / "Water/AirportCoreWater_Contract.json",
        root / "Vegetation/Textures/AirportCoreVegetation_Atlas.png",
        root / "Vegetation/Shaders/KBDUVegetationHeroV4.shader",
        root / "Vegetation/Materials/AirportCoreVegetation_V4.mat",
        root / "Vegetation/Models/AirportCoreVegetationCluster.fbx",
        root / "Vegetation/AirportCoreVegetation_Contract.json",
        root / "Vegetation/AirportCoreVegetation_GeometryReport.json",
        root / "Water/Reports/AirportCoreWaterVegetation_CostReport.json",
        root / "Water/Reports/AirportCoreWaterVegetation_SourceManifest.json",
        root / "Water/Reports/AirportCoreWaterVegetation_GenerationReport.json",
    ]
    for path in required:
        check(path.is_file(), "MISSING_REQUIRED_ASSET", str(path), failures)
    for owned_root in (root / "Water", root / "Vegetation"):
        check(Path(str(owned_root) + ".meta").is_file(), "UNITY_FOLDER_META_MISSING", str(owned_root), failures)
        for directory in (path for path in owned_root.rglob("*") if path.is_dir()):
            check(Path(str(directory) + ".meta").is_file(), "UNITY_FOLDER_META_MISSING", str(directory), failures)
        for asset in (path for path in owned_root.rglob("*") if path.is_file() and path.suffix != ".meta"):
            check(Path(str(asset) + ".meta").is_file(), "UNITY_ASSET_META_MISSING", str(asset), failures)
    if failures:
        report = {"status": "FAIL", "failures": failures, "warnings": warnings}
        print(json.dumps(report, indent=2))
        return 1

    for path, expected_size in (
        (required[0], 512), (required[1], 512), (required[2], 1024), (required[3], 512),
        (root / "Vegetation/Textures/AirportCoreVegetation_Atlas.png", 1024),
    ):
        width, height, pixels = generate.read_png_rgba(path)
        check(width == expected_size and height == expected_size, "TEXTURE_DIMENSION_MISMATCH",
              f"{path}: {width}x{height}, expected {expected_size}", failures)
        check(len(pixels) == expected_size * expected_size * 4, "TEXTURE_CHANNEL_MISMATCH", str(path), failures)

    water_shader = (root / "Water/Shaders/KBDUReservoirWaterV4.shader").read_text(encoding="utf-8")
    for token in ("ZWrite On", '"RenderType"="Opaque"', "samplerCUBE _EnvironmentCube", "FresnelSchlick",
                  "_SunGlitter", "_WaveNormalA", "_WaveNormalB", "UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX"):
        check(token in water_shader, "WATER_SHADER_REQUIRED_TOKEN_MISSING", token, failures)
    for token in ("GrabPass", "_CameraDepthTexture", "#pragma hull", "#pragma domain", "Blend SrcAlpha"):
        check(token not in water_shader, "WATER_SHADER_FORBIDDEN_FEATURE", token, failures)

    minor_include = (root / "Water/Shaders/KBDUMinorWaterTerrainEffect.hlsl").read_text(encoding="utf-8")
    check("ApplyKBDUMinorWaterTerrainEffect" in minor_include, "MINOR_WATER_FUNCTION_MISSING", "terrain effect", failures)
    check(not any(path.suffix.lower() in (".fbx", ".obj", ".asset") for path in (root / "Water").rglob("*")),
          "MINOR_WATER_OR_RESERVOIR_MESH_DUPLICATED", "Water candidate must not carry a second mesh", failures)

    vegetation_shader = (root / "Vegetation/Shaders/KBDUVegetationHeroV4.shader").read_text(encoding="utf-8")
    for token in ("TransparentCutout", "ZWrite On", "Blend Off", "clip(atlas.a - _Cutoff)",
                  "#pragma multi_compile_instancing", "LIGHTMAP_ON", "UNITY_ACCESS_INSTANCED_PROP"):
        check(token in vegetation_shader, "VEGETATION_SHADER_REQUIRED_TOKEN_MISSING", token, failures)
    for token in ("_WorldSpaceCameraPos -", "UNITY_MATRIX_V", "_Time.y"):
        check(token not in vegetation_shader, "VEGETATION_CAMERA_OR_TIME_DEPENDENCE", token, failures)
    water_material = (root / "Water/Materials/AirportCoreReservoir_V4.mat").read_text(encoding="utf-8")
    vegetation_material = (root / "Vegetation/Materials/AirportCoreVegetation_V4.mat").read_text(encoding="utf-8")
    for token in ("m_EnableInstancingVariants: 1", "m_CustomRenderQueue: 2005", "RenderType: Opaque"):
        check(token in water_material, "WATER_MATERIAL_CONTRACT_MISMATCH", token, failures)
    for token in ("m_EnableInstancingVariants: 1", "m_CustomRenderQueue: 2450", "RenderType: TransparentCutout"):
        check(token in vegetation_material, "VEGETATION_MATERIAL_CONTRACT_MISMATCH", token, failures)

    water_contract = json.loads((root / "Water/AirportCoreWater_Contract.json").read_text(encoding="utf-8"))
    check(water_contract["authoritative_reservoir"]["renderer_count"] == 1,
          "RESERVOIR_RENDERER_COUNT", "expected exactly one", failures)
    check(water_contract["authoritative_reservoir"]["zwrite"] is True,
          "RESERVOIR_ZWRITE", "must be stable opaque depth writer", failures)
    check(water_contract["minor_water"]["additional_renderer_count"] == 0,
          "MINOR_WATER_RENDERER_COUNT", "minor water must remain in terrain shader", failures)

    authoritative_mesh = repo / "QuestFlight/Assets/Production/Environment/Generated/BoulderReservoir_OSM_AuthoritativeWater_Mesh.asset"
    check(authoritative_mesh.is_file(), "AUTHORITATIVE_RESERVOIR_MESH_MISSING", str(authoritative_mesh), failures)
    if authoritative_mesh.is_file():
        check(sha256(authoritative_mesh) == water_contract["authoritative_reservoir"]["mesh_sha256"],
              "AUTHORITATIVE_RESERVOIR_MESH_HASH_MISMATCH", str(authoritative_mesh), failures)

    geometry = json.loads((root / "Vegetation/AirportCoreVegetation_GeometryReport.json").read_text(encoding="utf-8"))
    objects = sorted(geometry["objects"], key=lambda record: record["lod"])
    check(len(objects) == 3, "VEGETATION_LOD_COUNT", f"got {len(objects)}", failures)
    check([record["lod"] for record in objects] == [0, 1, 2], "VEGETATION_LOD_IDENTITIES", str(objects), failures)
    if len(objects) == 3:
        check(objects[0]["triangles"] <= 6000, "VEGETATION_LOD0_TRIANGLE_CEILING", str(objects[0]["triangles"]), failures)
        check(0.30 <= objects[1]["triangles"] / objects[0]["triangles"] <= 0.50,
              "VEGETATION_LOD1_RATIO", str(geometry["lod_triangle_ratios"]), failures)
        check(0.04 <= objects[2]["triangles"] / objects[0]["triangles"] <= 0.15,
              "VEGETATION_LOD2_RATIO", str(geometry["lod_triangle_ratios"]), failures)
    for record in objects:
        check(record["material_slots"] == 1, "VEGETATION_MATERIAL_SLOT_COUNT", record["name"], failures)
        check(record["ground_error_m"] <= 0.002, "VEGETATION_GROUND_ERROR", record["name"], failures)
        check(record["uv_layers"] >= 1, "VEGETATION_UV_MISSING", record["name"], failures)
        check(record["color_attributes"] >= 1, "VEGETATION_VERTEX_COLOR_MISSING", record["name"], failures)
        check(record["overconnected_nonmanifold_edges"] == 0,
              "VEGETATION_OVERCONNECTED_NONMANIFOLD", record["name"], failures)
        check(not record["negative_scale"], "VEGETATION_NEGATIVE_SCALE", record["name"], failures)
    fbx = root / "Vegetation/Models/AirportCoreVegetationCluster.fbx"
    check(sha256(fbx) == geometry["runtime_fbx_sha256"], "VEGETATION_FBX_HASH_MISMATCH", str(fbx), failures)
    if blend:
        check(blend.is_file(), "VEGETATION_BLEND_MISSING", str(blend), failures)
        if blend.is_file():
            check(sha256(blend) == geometry["source_blend_sha256"], "VEGETATION_BLEND_HASH_MISMATCH", str(blend), failures)

    cost = json.loads((root / "Water/Reports/AirportCoreWaterVegetation_CostReport.json").read_text(encoding="utf-8"))
    check(cost["estimated_texture_bytes_total"] <= 3 * 1024 * 1024,
          "TEXTURE_COST_CEILING", str(cost["estimated_texture_bytes_total"]), failures)
    check(cost["water"]["transparent_material_slots"] == 0 and cost["vegetation"]["transparent_material_slots"] == 0,
          "TRANSPARENT_SLOT_DELTA", "must remain zero", failures)
    check(cost["water"]["minor_water_renderer_delta"] == 0,
          "MINOR_WATER_RENDERER_DELTA", str(cost["water"]), failures)

    source = json.loads((root / "Water/Reports/AirportCoreWaterVegetation_SourceManifest.json").read_text(encoding="utf-8"))
    check(source["raw_external_assets"] == [] and source["external_license_dependencies"] == [],
          "UNEXPECTED_EXTERNAL_SOURCE", str(source), failures)
    check(source["generator_sha256"] == sha256(TOOL_DIR / "generate_benchmark_assets.py"),
          "GENERATOR_HASH_MISMATCH", "source manifest does not identify current generator", failures)
    for record in source["records"]:
        resolved = resolve_record(record["path"], root, repo, blend)
        if resolved is None:
            failures.append({"code": "UNRESOLVED_SOURCE_RECORD", "detail": record["path"]})
        elif not resolved.is_file():
            failures.append({"code": "SOURCE_RECORD_FILE_MISSING", "detail": str(resolved)})
        else:
            check(sha256(resolved) == record["sha256"], "SOURCE_RECORD_HASH_MISMATCH", record["path"], failures)
        check(bool(record.get("license")), "SOURCE_RECORD_LICENSE_MISSING", record["path"], failures)

    status = "OFFLINE_PASS_PRODUCT_ACCEPTANCE_BLOCKED" if not failures else "FAIL"
    report = {
        "schema": "questflight.airport-core-v4.water-vegetation-validation.v1",
        "status": status,
        "offline_contract_passed": not failures,
        "product_visual_acceptance": "NOT_RUN",
        "stereo_acceptance": "NOT_RUN",
        "quest_performance_acceptance": "NOT_RUN",
        "failures": failures,
        "warnings": warnings,
        "cost": cost,
        "geometry_summary": {
            "triangles_by_lod": {f"lod{record['lod']}": record["triangles"] for record in objects},
            "material_slots_per_lod": [record["material_slots"] for record in objects],
        },
    }
    report_path = root / "Water/Reports/AirportCoreWaterVegetation_ErrorReport.json"
    report_path.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    Path(str(report_path) + ".meta").write_text(generate.generic_meta(
        generate.LOGICAL_ROOT + "/Water/Reports/AirportCoreWaterVegetation_ErrorReport.json",
        "Airport Core V4 offline validation; not visual or hardware acceptance"), encoding="utf-8", newline="\n")

    summary_lines = [
        "# Airport-core V4 water/vegetation offline validation",
        "",
        f"- Status: `{status}`",
        f"- Offline failures: {len(failures)}",
        "- Product visual acceptance: NOT RUN",
        "- Stereo acceptance: NOT RUN",
        "- Quest performance acceptance: NOT RUN",
        f"- Estimated ASTC texture bytes: {cost['estimated_texture_bytes_total']:,}",
        f"- Vegetation triangles: {report['geometry_summary']['triangles_by_lod']}",
        "",
        "## Explicit blockers",
        "",
    ] + [f"- `{warning['code']}`: {warning['detail']}" for warning in warnings]
    summary = "\n".join(summary_lines) + "\n"
    if args.artifact_dir:
        artifact = Path(args.artifact_dir).resolve()
        artifact.mkdir(parents=True, exist_ok=True)
        (artifact / "water_vegetation_validation_report.json").write_text(
            json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
        (artifact / "water_vegetation_validation_summary.md").write_text(summary, encoding="utf-8", newline="\n")
    print(summary)
    if failures and args.strict:
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
