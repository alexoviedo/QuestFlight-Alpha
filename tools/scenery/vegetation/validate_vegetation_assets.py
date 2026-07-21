#!/usr/bin/env python3
"""Fail-closed validator for the Aerial Scenery V1 vegetation deliverable."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path
from typing import Any

import generate_vegetation_distribution as distribution_pipeline
import generate_vegetation_handoff_mask as handoff_pipeline


FROZEN_LIDAR_SHA256 = "1a08c8c4be14737e86d1a8819ae4cdff3edb58f7f9a2147a23278b366d0f34e5"
EXPECTED_TRIANGLES = {
    "AerialTree_Cottonwood_T0": 1328,
    "AerialTree_Cottonwood_T1": 452,
    "AerialTree_WillowRiparian_T0": 1512,
    "AerialTree_WillowRiparian_T1": 560,
    "AerialTree_Deciduous_T0": 1304,
    "AerialTree_Deciduous_T1": 446,
    "AerialTree_SparseConifer_T0": 1428,
    "AerialTree_SparseConifer_T1": 508,
    "AerialShrub_Regional_T0": 1438,
    "AerialShrub_Regional_T1": 524,
    "AerialGroveCluster_T2": 540,
}
EXPECTED_BATCHES = {
    0: ("tier0_editor_baked", "heterogeneous_fixed_orientation_editor_bake", 3),
    1: ("tier1_editor_baked", "heterogeneous_fixed_orientation_editor_bake", 19),
    2: ("tier2_instanced_fixed_groves", "fixed_world_oriented_source_mesh_instancing", 42),
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    require(isinstance(value, dict), f"Expected JSON object: {path}")
    return value


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def resolve_asset_path(repo_root: Path, unity_root: Path, recorded: str) -> Path:
    normalized = recorded.replace("\\", "/")
    if normalized.startswith("QuestFlight/"):
        return repo_root / normalized
    if normalized.startswith("Assets/"):
        return unity_root / normalized
    return repo_root / normalized


def manifest_asset(lidar: dict[str, Any], semantic: str) -> dict[str, Any]:
    matches = [record for record in lidar["assets"] if record.get("semantic") == semantic]
    require(len(matches) == 1, f"Lidar manifest must contain exactly one {semantic} asset")
    return matches[0]


def expected_legacy_roots() -> list[str]:
    roots = [
        "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/KBDU_BatchedContext/vegetation_v2_hlod_1500m_xn001_zn001",
        "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/KBDU_BatchedContext/vegetation_v2_hlod_1500m_xn001_zp000",
        "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/KBDU_BatchedContext/vegetation_v2_hlod_1500m_xp000_zn001",
        "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/KBDU_BatchedContext/vegetation_v2_hlod_1500m_xp000_zp000",
    ]
    roots.extend(
        "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/"
        f"SparseVegetationProduction/CottonwoodCluster_{index:02d}"
        for index in range(8)
    )
    return roots


def validate(repo_root: Path) -> dict[str, Any]:
    tool_root = repo_root / "tools/scenery/vegetation"
    unity_root = repo_root / "QuestFlight"
    vegetation_root = unity_root / "Assets/Production/Environment/AerialSceneryV1/Vegetation"
    art_root = unity_root / "Assets/ArtSource/Blender/AerialSceneryV1"
    lidar_path = unity_root / "Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_LidarManifestV1.json"
    config_path = tool_root / "vegetation_config.json"
    distribution_path = vegetation_root / "AerialVegetation_Distribution.json"
    geometry_path = vegetation_root / "AerialVegetation_GeometryReport.json"
    atlas_report_path = vegetation_root / "AerialVegetation_AtlasReport.json"
    handoff_path = vegetation_root / "Textures/AerialVegetation_HandoffMask_R8.png"
    handoff_report_path = vegetation_root / "AerialVegetation_HandoffMaskReport.json"
    contract_path = vegetation_root / "AerialVegetation_Contract.json"
    source_manifest_path = vegetation_root / "SOURCE_MANIFEST.json"
    blend_path = art_root / "AerialVegetationMultiscale.blend"
    fbx_path = vegetation_root / "Models/AerialVegetationMultiscale.fbx"
    atlas_path = vegetation_root / "Textures/AerialVegetation_Atlas.png"
    surface_path = vegetation_root / "Textures/AerialVegetation_Surface.png"
    shader_path = vegetation_root / "Shaders/AerialVegetationOpaque.shader"
    include_path = vegetation_root / "Shaders/AerialCanopyMap.cginc"
    material_path = vegetation_root / "Materials/AerialVegetation_AtlasCutout.mat"
    runtime_path = vegetation_root / "Runtime/AerialVegetationBatchRenderer.cs"
    editor_path = vegetation_root / "Editor/AerialVegetationPrefabAuthoring.cs"
    prefab_path = vegetation_root / "Generated/AerialVegetationRoot.prefab"

    required_files = [
        lidar_path, config_path, distribution_path, geometry_path, atlas_report_path,
        handoff_path, handoff_report_path, contract_path, source_manifest_path,
        blend_path, fbx_path, atlas_path, surface_path, shader_path, include_path,
        material_path, runtime_path, editor_path, prefab_path,
        *(vegetation_root / f"Generated/AerialVegetation_Tier{tier}_Combined.asset" for tier in range(2)),
    ]
    missing = [str(path) for path in required_files if not path.is_file()]
    require(not missing, "Missing final vegetation assets: " + ", ".join(missing))

    config = load_json(config_path)
    lidar = load_json(lidar_path)
    distribution = load_json(distribution_path)
    geometry = load_json(geometry_path)
    atlas_report = load_json(atlas_report_path)
    handoff_report = load_json(handoff_report_path)
    contract = load_json(contract_path)
    source_manifest = load_json(source_manifest_path)

    require(sha256(lidar_path) == FROZEN_LIDAR_SHA256, "Frozen lidar manifest SHA-256 mismatch")
    require(lidar.get("schema") == distribution_pipeline.LIDAR_SCHEMA, "Lidar schema mismatch")
    require(distribution.get("schema") == distribution_pipeline.DISTRIBUTION_SCHEMA, "Distribution schema mismatch")
    require(distribution.get("status") == "VALIDATED_LIDAR_DERIVATIVE", "Distribution is not validated")
    require(distribution.get("source_kind") == "validated_lidar_manifest", "Distribution source kind mismatch")
    require(distribution.get("input", {}).get("lidar_manifest_sha256") == FROZEN_LIDAR_SHA256,
            "Distribution is not pinned to the frozen lidar manifest")

    input_semantics = {
        "surface_features": "surface_features_canopy_building",
        "dtm": "digital_terrain_model",
        "terrain_normal_validity": "terrain_surface_normal_and_validity",
        "canopy_normal_occlusion": "canopy_height_normal_and_occlusion",
    }
    for key, semantic in input_semantics.items():
        record = manifest_asset(lidar, semantic)
        asset_path = resolve_asset_path(repo_root, unity_root, str(record["path"]))
        require(asset_path.is_file(), f"Missing lidar asset {semantic}")
        require(sha256(asset_path) == record["sha256"], f"Lidar asset hash mismatch for {semantic}")
        embedded_key = {
            "surface_features": "surface_features_sha256",
            "dtm": "dtm_sha256",
            "terrain_normal_validity": "terrain_normal_validity_sha256",
            "canopy_normal_occlusion": "canopy_normal_occlusion_sha256",
        }[key]
        require(distribution["input"][embedded_key] == record["sha256"],
                f"Distribution input hash mismatch for {semantic}")

    batches = sorted(distribution["batches"], key=lambda record: int(record["tier"]))
    require(distribution.get("batch_count") == 3 and len(batches) == 3,
            "Distribution must contain two near batches and one Tier2 grove batch")
    require(distribution.get("instance_count") == 64,
            "Distribution must contain 22 Tier0/Tier1 placements and 42 Tier2 groves")
    require(float(config["tiers"][2]["minimum_radius_m"]) == 500.0,
            "Tier2 canopy must begin at the pilot-visible 500 m boundary")
    require(int(config["tiers"][2]["maximum_instances"]) == 42 and
            config["tiers"][2]["source_objects"] == ["AerialGroveCluster_T2"] and
            int(config["tiers"][2]["geometry_draws"]) == 1 and
            config["tiers"][2]["carves_handoff_mask"] is False,
            "Tier2 must be exactly 42 fixed grove placements in one additive draw")
    require(int(distribution["statistics"]["tier2_selected"]) == 42 and
            int(distribution["statistics"]["tier2_geometry_draws"]) == 1,
            "Distribution Tier2 grove count/draw contract mismatch")
    tier2_instances = next(batch for batch in batches if int(batch["tier"]) == 2)["instances"]
    near_priority_count = sum(
        (float(instance["x"]) ** 2 + float(instance["z"]) ** 2) ** 0.5 < 1500.0
        for instance in tier2_instances
    )
    require(near_priority_count >= 26,
            "Tier2 distribution does not reserve 26 grove clusters inside the pilot-visible 1.5 km band")
    require(distribution["runtime_policy"]["tier2_geometry_instances"] == 42 and
            distribution["runtime_policy"]["tier2_geometry_draws"] == 1 and
            distribution["runtime_policy"]["tier2_geometry_carves_handoff"] is False,
            "Tier2 runtime policy must retain map ownership beneath one grove draw")
    stable_ids: set[str] = set()
    source_triangle_count = 0
    tier_actual_triangles = {0: 0, 1: 0, 2: 0}
    configured_sources = {
        int(record["tier"]): set(record["source_objects"])
        for record in config["tiers"] if int(record["tier"]) < 3
    }
    for batch in batches:
        tier = int(batch["tier"])
        expected_id, expected_mode, expected_count = EXPECTED_BATCHES[tier]
        require(batch["batch_id"] == expected_id, f"Tier{tier} batch ID mismatch")
        require(batch["source_mode"] == expected_mode, f"Tier{tier} source mode mismatch")
        require(len(batch["instances"]) == expected_count, f"Tier{tier} placement count mismatch")
        for instance in batch["instances"]:
            source = str(instance["source_object"])
            require(source in configured_sources[tier], f"Tier{tier} uses unconfigured source {source}")
            require(source in EXPECTED_TRIANGLES, f"Triangle evidence is missing for {source}")
            stable_id = str(instance["stable_id"])
            require(stable_id not in stable_ids, f"Duplicate placement stable ID {stable_id}")
            stable_ids.add(stable_id)
            require(all(float(instance[key]) > 0.0 for key in ("scale_x", "scale_y", "scale_z")),
                    f"Non-positive placement scale in {stable_id}")
            runtime_triangles = EXPECTED_TRIANGLES[source]
            if tier == 2:
                require(0.0 < float(instance["footprint_radius_major_m"]) <= 24.0,
                        f"Tier2 major radius is not bounded in {stable_id}")
                require(0.0 < float(instance["footprint_radius_minor_m"]) <= 24.0,
                        f"Tier2 minor radius is not bounded in {stable_id}")
                require(float(instance["mean_canopy_hag_m"]) > 0.0,
                        f"Tier2 canopy height is invalid in {stable_id}")
            source_triangle_count += runtime_triangles
            tier_actual_triangles[tier] += runtime_triangles

    require(distribution["runtime_policy"]["runtime_batches_after_editor_bake"] == 3 and
            distribution["runtime_policy"]["source_draws_maximum"] == 3,
            "Runtime batch count policy mismatch")
    require(distribution["runtime_policy"]["tier3_final_weight"].startswith("R8 eligible-canopy"),
            "Distribution does not name the final R8 Tier3 weight")
    require(distribution["runtime_policy"]["tier3_scalar_handoff"] is False,
            "Scalar Tier3 handoff must be disabled")

    require(geometry.get("schema") == "questflight.aerial-scenery-v1.vegetation-geometry.v1",
            "Geometry report schema mismatch")
    geometry_objects = {record["name"]: record for record in geometry["objects"]}
    require(set(geometry_objects) == set(EXPECTED_TRIANGLES), "Geometry source object inventory mismatch")
    for name, expected_triangles in EXPECTED_TRIANGLES.items():
        record = geometry_objects[name]
        require(int(record["triangles"]) == expected_triangles, f"Triangle count mismatch for {name}")
        require(record["material_slots"] == 1 and record["alpha_clip"] is True and record["alpha_blend"] is False,
                f"Alpha-test one-material contract mismatch for {name}")
        require(record["fixed_orientation_card_clusters"] > 0 and record["camera_facing_faces"] == 0,
                f"Fixed-orientation card evidence mismatch for {name}")
        require(record["overconnected_edges"] == 0 and record["negative_scale"] is False,
                f"Mesh structural evidence mismatch for {name}")
        require("upper_hemisphere_lobe_faces" not in record, f"Rejected opaque lobe evidence remains in {name}")
        if name.startswith(("AerialTree_Cottonwood_", "AerialTree_WillowRiparian_", "AerialTree_Deciduous_")):
            require(int(record["apparent_tree_units"]) >= 3,
                    f"{name} did not compile as a visible multi-tree micro-grove")
    require(geometry["all_fixed_orientation_alpha_test_cards"] is True, "Fixed-card report gate failed")
    require(geometry["all_grounded_within_0p5m_terrain_intersection"] is True, "Ground intersection gate failed")
    require(geometry["ordinary_vertical_billboards"] == 0 and geometry["camera_facing_geometry"] == 0,
            "Billboard or camera-facing geometry is prohibited")
    require(geometry["runtime_fbx_sha256"] == sha256(fbx_path), "FBX hash mismatch")
    require(geometry["source_blend_sha256"] == sha256(blend_path), "Blend hash mismatch")
    require(geometry["atlas_sha256"] == sha256(atlas_path), "Geometry atlas hash mismatch")
    require(geometry["atlas_report_sha256"] == sha256(atlas_report_path), "Geometry atlas-report hash mismatch")
    deterministic_authored_total = 35540
    require(source_triangle_count == deterministic_authored_total and source_triangle_count <= 36860,
            "Deterministic authored vegetation triangle budget mismatch")

    require(atlas_report.get("schema") == "questflight.aerial-scenery-v1.vegetation-atlas.v1",
            "Atlas report schema mismatch")
    require(atlas_report["dimensions"] == [1024, 1024], "Atlas dimensions mismatch")
    require(atlas_report["external_assets"] == [] and atlas_report["license"] == "Project-authored",
            "Atlas source/license evidence mismatch")
    require(atlas_report["rendering"] == {
        "alpha_mode": "clip", "broad_alpha_blend": False, "camera_facing": False,
    }, "Atlas rendering policy mismatch")
    require(atlas_report["color_alpha"]["sha256"] == sha256(atlas_path), "Color atlas hash mismatch")
    require(atlas_report["surface_packed"]["sha256"] == sha256(surface_path), "Surface atlas hash mismatch")

    width, height, affine = handoff_pipeline.validate_inputs(distribution, geometry, lidar, lidar_path)
    handoff = distribution_pipeline.read_png(handoff_path)
    require((handoff.width, handoff.height, handoff.bit_depth, handoff.color_type) == (1024, 1024, 8, 0),
            "Tier3 handoff texture must be 1024x1024 PNG R8")
    expected_mask, expected_footprints = handoff_pipeline.build_mask(
        distribution, geometry, width, height, affine,
        float(config["tier3_handoff"]["zero_padding_m"]),
        float(config["tier3_handoff"]["smoothstep_feather_m"]),
    )
    lidar_inputs = distribution_pipeline.load_inputs(repo_root, lidar_path, config)
    _, exclusion_counters, eligible_mask = distribution_pipeline.eligible_candidates(lidar_inputs, config)
    handoff_pipeline.apply_eligible_canopy_gate(expected_mask, eligible_mask)
    require(handoff.pixels == bytes(expected_mask), "Tier3 handoff texture is not a deterministic final-weight rebuild")
    require(handoff_report.get("schema") == handoff_pipeline.MASK_SCHEMA, "Handoff report schema mismatch")
    require(handoff_report["output"]["sha256"] == sha256(handoff_path), "Handoff report hash mismatch")
    require(handoff_report["output"]["mipmaps"] is False, "Handoff mask mipmaps must be disabled")
    require(handoff_report["output"]["runtime_uncompressed_bytes"] == 1048576,
            "Handoff R8 runtime byte count mismatch")
    require(handoff_report["inputs"]["lidar_manifest_sha256"] == FROZEN_LIDAR_SHA256,
            "Handoff report is not pinned to frozen lidar")
    require(handoff_report["inputs"]["distribution_sha256"] == sha256(distribution_path),
            "Handoff report distribution hash mismatch")
    require(handoff_report["inputs"]["geometry_report_sha256"] == sha256(geometry_path),
            "Handoff report geometry hash mismatch")
    eligible_pixels = sum(bool(value) for value in eligible_mask)
    zero_eligible = sum(bool(eligible_mask[index]) and value == 0 for index, value in enumerate(handoff.pixels))
    transition_pixels = sum(0 < value < 255 for value in handoff.pixels)
    full_pixels = sum(value == 255 for value in handoff.pixels)
    require(all(eligible_mask[index] or value == 0 for index, value in enumerate(handoff.pixels)),
            "Handoff texture is nonzero outside the validated eligible-canopy mask")
    require(zero_eligible + transition_pixels + full_pixels == eligible_pixels,
            "Final Tier3 eligible-canopy coverage accounting mismatch")
    require(handoff_report["statistics"]["eligible_canopy_pixels"] == eligible_pixels,
            "Handoff eligible-canopy statistic mismatch")
    require(handoff_report["statistics"]["eligible_geometry_owned_zero_pixels"] == zero_eligible,
            "Handoff geometry-owned statistic mismatch")
    require(handoff_report["statistics"]["transition_pixels"] == transition_pixels,
            "Handoff transition statistic mismatch")
    require(handoff_report["statistics"]["tier3_owned_full_pixels"] == full_pixels,
            "Handoff Tier3-owned statistic mismatch")
    require(handoff_report["statistics"]["eligible_canopy_exclusion_counters"] == exclusion_counters,
            "Handoff exclusion counters mismatch")
    require(len(expected_footprints) == 22 and
            all(int(record["tier"]) in (0, 1) for record in expected_footprints),
            "Handoff must carve only the 22 Tier0/Tier1 geometry footprints")
    require(handoff_report["statistics"]["tier_counts"] == {"0": 3, "1": 19, "2": 0},
            "Handoff report must rasterize only Tier0/Tier1; Tier2 is additive map-owned geometry")

    shader = shader_path.read_text(encoding="utf-8")
    for required in (
        '"Queue"="AlphaTest"', '"RenderType"="TransparentCutout"', "clip(atlas.a - _Cutoff)",
        "Cull Off", "ZWrite On", "Blend Off", "AlphaToMask On", "#pragma multi_compile_instancing",
    ):
        require(required in shader, f"Vegetation shader is missing {required}")
    require("Blend SrcAlpha" not in shader and "billboard" not in shader.lower(),
            "Vegetation shader contains prohibited broad blend or billboard behavior")
    include = include_path.read_text(encoding="utf-8")
    include_template = (tool_root / "templates" / "AerialCanopyMap.cginc").read_text(encoding="utf-8")
    require(include == include_template,
            "Generated canopy include differs from its deterministic source template")
    for required in (
        "sampler2D _AerialCanopyHandoffMask", "sample.coverage = geometryToMapHandoff",
        "half insideBounds = step(0.0, uv.x)", "float2 sampledUv = saturate(uv)",
        "sampler2D _AerialCanopyNormalOcclusion", "_AerialCanopyProductToSourceRow0",
        "_AerialCanopyProductToSourceRow1", "_AerialCanopySourceToProductRow0",
        "_AerialCanopySourceToProductRow1",
        "dot(_AerialCanopyProductToSourceRow0.xy, localXZ)",
        "dot(_AerialCanopySourceToProductRow0.xy, sourceNormal.xz)",
        "sample.canopyNormalLocal = normalize(half3(productNormalXZ.x, sourceNormal.y, productNormalXZ.y))",
    ):
        require(required in include, f"Tier3 include is missing {required}")
    require("_AerialCanopyMapHandoffWeight" not in include,
            "Invalid scalar Tier3 handoff remains in shader include")

    runtime = runtime_path.read_text(encoding="utf-8")
    for required in (
        "Graphics.DrawMeshInstanced(", "public int NonEmptyDrawCount", "public long FullyInstancedTriangleCount",
        "authoredFullyInstancedTriangleCount", "cameraFacingBehavior", "lodCrossfadeBehavior",
    ):
        require(required in runtime, f"Runtime renderer is missing {required}")
    require("Instantiate(" not in runtime and "new GameObject" not in runtime,
            "Runtime vegetation renderer constructs product hierarchy")
    editor = editor_path.read_text(encoding="utf-8")
    for required in (
        "AerialVegetation_AtlasCutout.mat", "BuildCombinedTierMesh",
        "ExpectedDrawCount = 3", "ExpectedSourcePlacementCount = 64", FROZEN_LIDAR_SHA256,
        "AerialGroveCluster_T2", "Tier2_FixedWorldOrientedGroves_500m_4000m",
        "Tier3_OrthophotoLidarCanopyTerrainShading_Far",
    ):
        require(required in editor, f"Editor authoring entry point is missing {required}")
    for prohibited in ("BuildUpperCanopyTierMesh", "AddUpperCanopyCrownLobe", "Tier2CrownLobes"):
        require(prohibited not in editor, f"Rejected procedural Tier2 geometry authoring remains: {prohibited}")
    require("AerialVegetation_Tier2_Combined.asset" not in editor,
            "Tier2 must remain one instanced source-mesh draw, not an editor-combined mesh")
    require("ProductionEnvironmentRoot.prefab" not in editor and "LoadScene" not in editor,
            "Vegetation authoring entry point performs unauthorized product-scene integration")

    author_script = (tool_root / "author_multiscale_vegetation.py").read_text(encoding="utf-8")
    for prohibited in ("grove_canopy_volume", "atlas_crown_lobe", "canopy_core_octahedron"):
        require(prohibited not in author_script, f"Rejected opaque canopy authoring method remains: {prohibited}")

    material = material_path.read_text(encoding="utf-8")
    require("m_Name: AerialVegetation_AtlasCutout" in material and "m_EnableInstancingVariants: 1" in material,
            "Shared material name or instancing flag mismatch")
    require(material.count("- _Atlas:") == 1 and material.count("- _SurfaceAtlas:") == 1,
            "Shared material texture binding mismatch")
    handoff_meta = Path(str(handoff_path) + ".meta").read_text(encoding="utf-8")
    for required in ("enableMipMap: 0", "sRGBTexture: 0", "singleChannelComponent: 1", "textureFormat: 63"):
        require(required in handoff_meta, f"R8 handoff TextureImporter is missing {required}")
    model_meta = Path(str(fbx_path) + ".meta").read_text(encoding="utf-8")
    require("isReadable: 1" in model_meta and "tangentImportMode: 3" in model_meta,
            "FBX import settings do not support deterministic Editor baking")

    require(contract.get("schema") == "questflight.aerial-scenery-v1.vegetation-contract.v1",
            "Vegetation contract schema mismatch")
    require(contract["rendering_policy"]["shared_materials"] == 1 and
            contract["rendering_policy"]["maximum_draws"] == 3 and
            contract["rendering_policy"]["alpha_blending"] is False and
            contract["rendering_policy"]["camera_facing_geometry"] is False,
            "Vegetation contract rendering policy mismatch")
    require(contract["representations"][3]["geometry_to_map_handoff_texture"] == "_AerialCanopyHandoffMask" and
            contract["representations"][3]["scalar_handoff_uniform"] is None,
            "Vegetation contract Tier3 handoff mismatch")
    require(contract["representations"][2]["range_m"] == [500.0, 4000.0] and
            contract["representations"][2]["source_objects"] == ["AerialGroveCluster_T2"] and
            contract["representations"][2]["geometry_instances"] == 42 and
            contract["representations"][2]["geometry_draws"] == 1 and
            contract["representations"][2]["geometry_triangles"] == 22680 and
            contract["representations"][2]["carves_canopy_map_handoff"] is False and
            contract["representations"][2]["terrain_map_retained_beneath_geometry"] is True,
            "Vegetation contract Tier2 additive grove representation mismatch")
    require(contract["legacy_renderer_roots_to_disable_during_authored_integration"] == expected_legacy_roots(),
            "Legacy vegetation renderer inventory mismatch")

    prefab = prefab_path.read_text(encoding="utf-8")
    for required in (
        "authoredSourcePlacementCount: 64", "authoredDrawCount: 3",
        f"authoredFullyInstancedTriangleCount: {source_triangle_count}", "authoredMaterialCount: 1",
        "fixedOrientationOnly: 1", "cameraFacingBehavior: 0", "lodCrossfadeBehavior: 0",
        "Tier0_EditorBakedTrees_0m_500m", "Tier1_EditorBakedTrees_500m_1500m",
        "Tier2_FixedWorldOrientedGroves_500m_4000m",
        "Tier3_OrthophotoLidarCanopyTerrainShading_Far",
    ):
        require(required in prefab, f"Generated vegetation prefab is missing {required}")
    require("AerialVegetation_Tier2_Combined" not in prefab,
            "Generated vegetation prefab must instance the Tier2 source mesh rather than retain a combined mesh")

    require(source_manifest.get("schema") == "questflight.aerial-scenery-v1.vegetation-source-manifest.v1",
            "Source manifest schema mismatch")
    require(source_manifest.get("external_assets") == [], "Vegetation source manifest has external assets")
    require(source_manifest["upstream_shared_inputs"][0]["sha256"] == FROZEN_LIDAR_SHA256,
            "Source manifest upstream lidar hash mismatch")
    recorded_paths: set[str] = set()
    for record in source_manifest["records"]:
        source_path = resolve_asset_path(repo_root, unity_root, str(record["path"]))
        require(source_path.is_file(), f"Source manifest path is missing: {record['path']}")
        require(sha256(source_path) == record["sha256"], f"Source manifest hash mismatch: {record['path']}")
        require(bool(record.get("license")), f"Source manifest license is missing: {record['path']}")
        recorded_paths.add(str(record["path"]).replace("\\", "/"))
    for generated in [prefab_path, *(vegetation_root / f"Generated/AerialVegetation_Tier{tier}_Combined.asset" for tier in range(2))]:
        require(str(generated.relative_to(unity_root)).replace("\\", "/") in recorded_paths,
                f"Generated Unity asset is absent from source manifest: {generated.name}")
    require(not (vegetation_root / "Generated/AerialVegetation_Tier2_Combined.asset").exists(),
            "Rejected Tier2 combined mesh remains in the runtime asset tree")

    owned_roots = [vegetation_root, art_root]
    asset_files = [
        path for root in owned_roots for path in root.rglob("*")
        if path.is_file() and path.suffix != ".meta"
    ]
    missing_meta = [str(path) for path in asset_files if not Path(str(path) + ".meta").is_file()]
    require(not missing_meta, "Owned Unity assets lack paired .meta files: " + ", ".join(missing_meta))
    owned_folders = [path for root in owned_roots for path in [root, *root.rglob("*")] if path.is_dir()]
    missing_folder_meta = [str(path) for path in owned_folders if not Path(str(path) + ".meta").is_file()]
    require(not missing_folder_meta, "Owned Unity folders lack paired .meta files: " + ", ".join(missing_folder_meta))
    guid_records: dict[str, Path] = {}
    for root in owned_roots:
        for meta_path in root.parent.glob(root.name + ".meta"):
            match = re.search(r"^guid: ([0-9a-f]{32})$", meta_path.read_text(encoding="utf-8"), re.MULTILINE)
            require(match is not None, f"Unity meta GUID is missing: {meta_path}")
            require(match.group(1) not in guid_records, f"Duplicate owned Unity GUID: {match.group(1)}")
            guid_records[match.group(1)] = meta_path
        for meta_path in root.rglob("*.meta"):
            match = re.search(r"^guid: ([0-9a-f]{32})$", meta_path.read_text(encoding="utf-8"), re.MULTILINE)
            require(match is not None, f"Unity meta GUID is missing: {meta_path}")
            require(match.group(1) not in guid_records, f"Duplicate owned Unity GUID: {match.group(1)}")
            guid_records[match.group(1)] = meta_path

    caches = [
        path for path in tool_root.rglob("*")
        if path.name == "__pycache__" or path.suffix in {".pyc", ".pyo"}
    ]
    require(not caches, "Python caches remain in vegetation-owned repo paths")
    prohibited_repo_artifacts = [
        path for path in vegetation_root.rglob("*")
        if path.is_file() and path.suffix.lower() in {".apk", ".zip", ".blend1"}
    ]
    require(not prohibited_repo_artifacts, "Prohibited generated artifacts remain in vegetation asset paths")

    return {
        "schema": "questflight.aerial-scenery-v1.vegetation-validation.v1",
        "status": "PASS",
        "frozen_lidar_manifest_sha256": FROZEN_LIDAR_SHA256,
        "distribution_sha256": sha256(distribution_path),
        "geometry_report_sha256": sha256(geometry_path),
        "fbx_sha256": sha256(fbx_path),
        "blend_sha256": sha256(blend_path),
        "color_atlas_sha256": sha256(atlas_path),
        "surface_atlas_sha256": sha256(surface_path),
        "handoff_mask_sha256": sha256(handoff_path),
        "handoff_mask_runtime_bytes": 1048576,
        "placement_counts": {str(tier): EXPECTED_BATCHES[tier][2] for tier in range(3)},
        "source_placement_count": 64,
        "non_empty_draw_count": 3,
        "fully_instanced_triangle_count": source_triangle_count,
        "triangle_ceiling": 36860,
        "deterministic_authored_triangle_contract": deterministic_authored_total,
        "material_count": 1,
        "tier2_geometry_instances": 42,
        "tier2_geometry_draws": 1,
        "tier2_geometry_triangles": 22680,
        "tier2_terrain_shading_owned_pixels": int(
            distribution["statistics"]["tier2_terrain_shading_owned_pixels"]
        ),
        "eligible_canopy_pixels": eligible_pixels,
        "eligible_geometry_owned_zero_pixels": zero_eligible,
        "handoff_transition_pixels": transition_pixels,
        "tier3_owned_full_pixels": full_pixels,
        "tier3_nonzero_eligible_coverage_fraction": round(
            (transition_pixels + full_pixels) / eligible_pixels if eligible_pixels else 0.0,
            8,
        ),
        "legacy_renderer_id_count": len(expected_legacy_roots()),
        "source_manifest_record_count": len(source_manifest["records"]),
        "paired_asset_meta_count": len(asset_files),
        "owned_unique_guid_count": len(guid_records),
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    parser.add_argument("--report", type=Path)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    report = validate(args.repo_root.resolve())
    payload = json.dumps(report, indent=2, sort_keys=True) + "\n"
    if args.report:
        report_path = args.report.resolve()
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(payload, encoding="utf-8", newline="\n")
    print(payload, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
