#!/usr/bin/env python3
"""Build the Aerial Scenery V1 vegetation source and Unity asset package."""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any


TOOL_DIR = Path(__file__).resolve().parent
REPO_ROOT = TOOL_DIR.parents[2]
UNITY_ROOT = REPO_ROOT / "QuestFlight"
ART_SOURCE = UNITY_ROOT / "Assets/ArtSource/Blender/AerialSceneryV1"
VEGETATION_ROOT = UNITY_ROOT / "Assets/Production/Environment/AerialSceneryV1/Vegetation"
LIDAR_MANIFEST = UNITY_ROOT / "Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_LidarManifestV1.json"
FROZEN_LIDAR_MANIFEST_SHA256 = "1a08c8c4be14737e86d1a8819ae4cdff3edb58f7f9a2147a23278b366d0f34e5"
GUID_NAMESPACE = "questflight:aerial-scenery-v1:vegetation:"


def logical(path: Path) -> str:
    return str(path.relative_to(UNITY_ROOT)).replace("\\", "/")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def stable_guid(logical_path: str) -> str:
    return hashlib.sha256((GUID_NAMESPACE + logical_path.replace("\\", "/")).encode()).hexdigest()[:32]


def write_text(path: Path, value: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(value, encoding="utf-8", newline="\n")


def write_json(path: Path, value: Any) -> None:
    write_text(path, json.dumps(value, indent=2, sort_keys=True) + "\n")


def default_meta(path: Path, note: str) -> None:
    write_text(Path(str(path) + ".meta"), (
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(logical(path))}\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        f"  userData: {note}\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n"
    ))


def folder_meta(path: Path, note: str) -> None:
    path.mkdir(parents=True, exist_ok=True)
    write_text(Path(str(path) + ".meta"), (
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(logical(path))}\n"
        "folderAsset: yes\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        f"  userData: {note}\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n"
    ))


def shader_meta(path: Path) -> None:
    write_text(Path(str(path) + ".meta"), (
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(logical(path))}\n"
        "ShaderImporter:\n"
        "  externalObjects: {}\n"
        "  defaultTextures: []\n"
        "  nonModifiableTextures: []\n"
        "  userData: Aerial Scenery V1 project-authored fixed-card alpha-test vegetation shader\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n"
    ))


def script_meta(path: Path, note: str) -> None:
    write_text(Path(str(path) + ".meta"), (
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(logical(path))}\n"
        "MonoImporter:\n"
        "  externalObjects: {}\n"
        "  serializedVersion: 2\n"
        "  defaultReferences: []\n"
        "  executionOrder: 0\n"
        "  icon: {instanceID: 0}\n"
        f"  userData: {note}\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n"
    ))


def material_meta(path: Path) -> None:
    write_text(Path(str(path) + ".meta"), (
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(logical(path))}\n"
        "NativeFormatImporter:\n"
        "  externalObjects: {}\n"
        "  mainObjectFileID: 2100000\n"
        "  userData: Aerial Scenery V1 one-material fixed-card alpha-test vegetation contract\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n"
    ))


def texture_meta(
    path: Path,
    *,
    srgb: bool,
    alpha_cutout: bool,
    single_channel_r8: bool = False,
    mipmaps: bool = True,
    note: str,
) -> None:
    write_text(Path(str(path) + ".meta"), f"""fileFormatVersion: 2
guid: {stable_guid(logical(path))}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: {1 if mipmaps else 0}
    sRGBTexture: {1 if srgb else 0}
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: {1 if alpha_cutout and mipmaps else 0}
    alphaTestReferenceValue: 0.42
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: {63 if single_channel_r8 else 1}
  maxTextureSize: 1024
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 2
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 100
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: {0 if single_channel_r8 else 1}
  alphaIsTransparency: 0
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: {1 if single_channel_r8 else 0}
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: {0 if srgb else 1}
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 1024
    resizeAlgorithm: 0
    textureFormat: {63 if single_channel_r8 else -1}
    textureCompression: 0
    compressionQuality: 100
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 4
    buildTarget: Android
    maxTextureSize: 1024
    resizeAlgorithm: 0
    textureFormat: {63 if single_channel_r8 else -1}
    textureCompression: 0
    compressionQuality: 100
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 1
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData: {note}
  assetBundleName:
  assetBundleVariant:
""")


def write_material(path: Path, shader_path: Path, atlas_path: Path, surface_path: Path) -> None:
    write_text(path, f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: AerialVegetation_AtlasCutout
  m_Shader: {{fileID: 4800000, guid: {stable_guid(logical(shader_path))}, type: 3}}
  m_Parent: {{fileID: 0}}
  m_ModifiedSerializedProperties: 0
  m_ValidKeywords: []
  m_InvalidKeywords: []
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 1
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  stringTagMap: {{}}
  disabledShaderPasses: []
  m_LockedProperties:
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
    - _Atlas:
        m_Texture: {{fileID: 2800000, guid: {stable_guid(logical(atlas_path))}, type: 3}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    - _SurfaceAtlas:
        m_Texture: {{fileID: 2800000, guid: {stable_guid(logical(surface_path))}, type: 3}}
        m_Scale: {{x: 1, y: 1}}
        m_Offset: {{x: 0, y: 0}}
    m_Ints: []
    m_Floats:
    - _AmbientStrength: 0.56
    - _Cutoff: 0.42
    - _NormalStrength: 0.44
    - _SunWrap: 0.28
    - _SurfaceOcclusionStrength: 0.72
    - _VertexOcclusionStrength: 0.58
    m_Colors:
    - _InstanceTint: {{r: 1, g: 1, b: 1, a: 1}}
    - _Tint: {{r: 1, g: 1, b: 1, a: 1}}
  m_BuildTextureStacks: []
  m_AllowLocking: 1
""")


def legacy_roots() -> list[str]:
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


def write_contract(
    path: Path,
    config: dict[str, Any],
    geometry_report: dict[str, Any],
    handoff_report: dict[str, Any],
) -> None:
    objects = {record["name"]: record for record in geometry_report["objects"]}
    contract = {
        "schema": "questflight.aerial-scenery-v1.vegetation-contract.v1",
        "status": "UNITY_READY_PREFAB_REQUIRES_AUTHORED_PRODUCT_INTEGRATION_AND_STEREO_QA",
        "source_blend": "Assets/ArtSource/Blender/AerialSceneryV1/AerialVegetationMultiscale.blend",
        "runtime_model": "Assets/Production/Environment/AerialSceneryV1/Vegetation/Models/AerialVegetationMultiscale.fbx",
        "shared_material": "Assets/Production/Environment/AerialSceneryV1/Vegetation/Materials/AerialVegetation_AtlasCutout.mat",
        "shader": "Assets/Production/Environment/AerialSceneryV1/Vegetation/Shaders/AerialVegetationOpaque.shader",
        "color_alpha_atlas": "Assets/Production/Environment/AerialSceneryV1/Vegetation/Textures/AerialVegetation_Atlas.png",
        "packed_surface_atlas": "Assets/Production/Environment/AerialSceneryV1/Vegetation/Textures/AerialVegetation_Surface.png",
        "geometry_to_map_handoff_mask": "Assets/Production/Environment/AerialSceneryV1/Vegetation/Textures/AerialVegetation_HandoffMask_R8.png",
        "geometry_to_map_handoff_report": "Assets/Production/Environment/AerialSceneryV1/Vegetation/AerialVegetation_HandoffMaskReport.json",
        "runtime_batch_renderer": "Assets/Production/Environment/AerialSceneryV1/Vegetation/Runtime/AerialVegetationBatchRenderer.cs",
        "prefab_authoring_entry_point": "QuestFlight/Aerial Scenery V1/Build Vegetation Prefab",
        "generated_prefab": "Assets/Production/Environment/AerialSceneryV1/Vegetation/Generated/AerialVegetationRoot.prefab",
        "distribution_manifest": "Assets/Production/Environment/AerialSceneryV1/Vegetation/AerialVegetation_Distribution.json",
        "representations": [
            {
                "tier": 0,
                "range_m": [0, 500],
                "mode": "fixed_orientation_alpha_clip_tree_sources_editor_baked_to_one_instanced_tier_mesh",
                "source_objects": config["tiers"][0]["source_objects"],
                "maximum_instances": config["tiers"][0]["maximum_instances"],
            },
            {
                "tier": 1,
                "range_m": [500, 1500],
                "mode": "fixed_orientation_alpha_clip_tree_sources_editor_baked_to_one_instanced_tier_mesh",
                "source_objects": config["tiers"][1]["source_objects"],
                "maximum_instances": config["tiers"][1]["maximum_instances"],
            },
            {
                "tier": 2,
                "range_m": [1500, 4000],
                "mode": "fixed_bounded_upper_canopy_cell_aggregates",
                "source_objects": config["tiers"][2]["source_objects"],
                "maximum_instances": config["tiers"][2]["maximum_instances"],
                "procedural_triangles_per_instance": config["runtime_budget"]["tier2_procedural_triangles_per_instance"],
                "maximum_footprint_radius_m": config["sampling"]["grove_cluster_max_radius_m"],
                "camera_facing": False,
            },
            {
                "tier": 3,
                "spatial_domain": "geometry complement inside local X/Z bounds [-2048,+2048] meters",
                "mode": "opaque_terrain_canopy_shader_maps_gated_by_r8_ownership",
                "geometry_instances": 0,
                "surface_features_semantic": config["lidar_input"]["surface_features_semantic"],
                "terrain_validity_semantic": config["lidar_input"]["terrain_validity_semantic"],
                "canopy_normal_occlusion_semantic": config["lidar_input"]["canopy_normal_semantic"],
                "shader_include": "Assets/Production/Environment/AerialSceneryV1/Vegetation/Shaders/AerialCanopyMap.cginc",
                "duplicate_runtime_textures": 0,
                "explicit_inside_bounds_mask": True,
                "geometry_to_map_handoff_texture": "_AerialCanopyHandoffMask",
                "geometry_to_map_handoff_semantics": handoff_report["ownership_semantics"],
                "scalar_handoff_uniform": None,
            },
        ],
        "geometry_triangles": {name: record["triangles"] for name, record in objects.items()},
        "runtime_budget": config["runtime_budget"],
        "rendering_policy": {
            "shared_materials": 1,
            "maximum_draws": 3,
            "transparent_material_slots": 0,
            "alpha_blending": False,
            "alpha_testing": True,
            "alpha_to_coverage": True,
            "alpha_cards": "fixed orientation only",
            "ordinary_vertical_billboards": False,
            "camera_facing_geometry": False,
            "lod_crossfade": False,
            "shadows": False,
            "one_gameobject_per_tree": False,
            "runtime_hierarchy_construction": False,
            "steady_state_allocations": 0,
            "geometry_ownership_handoff": "R8 spatial mask on the exact lidar grid",
        },
        "legacy_renderer_roots_to_disable_during_authored_integration": legacy_roots(),
        "legacy_disable_policy": (
            "Lead integration must disable or remove these serialized legacy roots while authoring the product prefab. "
            "Runtime repair/bootstrap code is prohibited."
        ),
        "quality_gates": {
            "source_geometry": "PASS",
            "distribution": "PASS only when status is VALIDATED_LIDAR_DERIVATIVE",
            "spatial_handoff_mask": "PASS when report input hashes match distribution, geometry, and frozen lidar manifest",
            "unity_prefab": "GENERATED by deterministic Editor entry point",
            "integrated_visual_qa": "PENDING_LEAD_INTEGRATION",
            "stereo_simulator_capture": "PENDING_LEAD_INTEGRATION",
            "quest_hardware_performance": "NOT_ESTABLISHED",
        },
    }
    write_json(path, contract)


def write_source_manifest(path: Path, files: list[tuple[Path, str]]) -> None:
    records = []
    for source_path, source_description in files:
        if not source_path.is_file():
            continue
        try:
            path_value = logical(source_path)
        except ValueError:
            path_value = str(source_path.relative_to(REPO_ROOT)).replace("\\", "/")
        lidar_derivative = source_path.name in {
            "AerialVegetation_Distribution.json",
            "AerialVegetation_HandoffMask_R8.png",
            "AerialVegetation_HandoffMaskReport.json",
        }
        records.append({
            "path": path_value,
            "bytes": source_path.stat().st_size,
            "sha256": sha256(source_path),
            "source": source_description,
            "license": (
                "Project-authored derivative of USGS 3DEP United States Government Work / Public Domain input"
                if lidar_derivative else "Project-authored"
            ),
        })
    write_json(path, {
        "schema": "questflight.aerial-scenery-v1.vegetation-source-manifest.v1",
        "external_assets": [],
        "upstream_shared_inputs": [
            {
                "path": logical(LIDAR_MANIFEST),
                "sha256": sha256(LIDAR_MANIFEST),
                "attribution": "U.S. Geological Survey 3D Elevation Program (3DEP)",
                "license": "United States Government Work / Public Domain",
                "raw_data_committed": False,
            }
        ],
        "rights_summary": (
            "Vegetation geometry, atlases, shaders, code, and configuration are project-authored. "
            "The distribution and final R8 canopy ownership weight are deterministic derivatives of the "
            "shared validated USGS 3DEP public-domain lidar products."
        ),
        "records": sorted(records, key=lambda record: record["path"]),
    })


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--blender",
        type=Path,
        default=Path(r"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"),
    )
    parser.add_argument("--preview-dir", type=Path)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    blender = args.blender.resolve()
    if not blender.is_file():
        raise FileNotFoundError(blender)
    config_path = TOOL_DIR / "vegetation_config.json"
    config = json.loads(config_path.read_text(encoding="utf-8"))
    distribution_path = VEGETATION_ROOT / "AerialVegetation_Distribution.json"
    if not distribution_path.is_file():
        raise FileNotFoundError(
            "Generate the frozen validated lidar vegetation distribution before building Unity assets: "
            + str(distribution_path)
        )
    if not LIDAR_MANIFEST.is_file() or sha256(LIDAR_MANIFEST) != FROZEN_LIDAR_MANIFEST_SHA256:
        raise ValueError("The Aerial Scenery V1 vegetation build requires the frozen final lidar manifest SHA-256.")
    distribution = json.loads(distribution_path.read_text(encoding="utf-8"))
    if (
        distribution.get("status") != "VALIDATED_LIDAR_DERIVATIVE"
        or distribution.get("source_kind") != "validated_lidar_manifest"
        or distribution.get("input", {}).get("lidar_manifest_sha256") != FROZEN_LIDAR_MANIFEST_SHA256
    ):
        raise ValueError("The product vegetation distribution is synthetic, unvalidated, or pinned to a stale lidar manifest.")

    folders = [
        UNITY_ROOT / "Assets/ArtSource",
        UNITY_ROOT / "Assets/ArtSource/Blender",
        ART_SOURCE,
        VEGETATION_ROOT,
        VEGETATION_ROOT / "Models",
        VEGETATION_ROOT / "Materials",
        VEGETATION_ROOT / "Textures",
        VEGETATION_ROOT / "Shaders",
        VEGETATION_ROOT / "Runtime",
        VEGETATION_ROOT / "Editor",
        VEGETATION_ROOT / "Generated",
    ]
    # Existing parent folder metas are preserved; new owned folders receive
    # deterministic GUIDs.
    for folder in folders:
        if folder.exists() and Path(str(folder) + ".meta").exists():
            continue
        folder_meta(folder, "Aerial Scenery V1 project-authored vegetation")

    blend_path = ART_SOURCE / "AerialVegetationMultiscale.blend"
    model_path = VEGETATION_ROOT / "Models/AerialVegetationMultiscale.fbx"
    geometry_path = VEGETATION_ROOT / "AerialVegetation_GeometryReport.json"
    atlas_path = VEGETATION_ROOT / "Textures/AerialVegetation_Atlas.png"
    surface_path = VEGETATION_ROOT / "Textures/AerialVegetation_Surface.png"
    atlas_report_path = VEGETATION_ROOT / "AerialVegetation_AtlasReport.json"
    subprocess.run([
        sys.executable,
        str(TOOL_DIR / "generate_vegetation_atlas.py"),
        "--color", str(atlas_path),
        "--surface", str(surface_path),
        "--report", str(atlas_report_path),
    ], cwd=REPO_ROOT, check=True)
    texture_meta(
        atlas_path,
        srgb=True,
        alpha_cutout=True,
        note="Project-authored 1K bark and five-foliage alpha-test atlas; no external image sources",
    )
    texture_meta(
        surface_path,
        srgb=False,
        alpha_cutout=False,
        note="Project-authored packed tangent normal XY, roughness, and ambient occlusion atlas",
    )
    default_meta(atlas_report_path, "Deterministic project-authored vegetation atlas report")
    command = [
        str(blender), "--background", "--factory-startup",
        "--python", str(TOOL_DIR / "author_multiscale_vegetation.py"), "--",
        "--blend", str(blend_path),
        "--fbx", str(model_path),
        "--report", str(geometry_path),
        "--atlas", str(atlas_path),
        "--atlas-report", str(atlas_report_path),
        "--write-unity-meta",
    ]
    if args.preview_dir:
        command.extend(("--preview-dir", str(args.preview_dir.resolve())))
    subprocess.run(command, cwd=REPO_ROOT, check=True)

    handoff_mask_path = VEGETATION_ROOT / "Textures/AerialVegetation_HandoffMask_R8.png"
    handoff_report_path = VEGETATION_ROOT / "AerialVegetation_HandoffMaskReport.json"
    handoff_config = config["tier3_handoff"]
    handoff_command = [
        sys.executable,
        str(TOOL_DIR / "generate_vegetation_handoff_mask.py"),
        "--repo-root", str(REPO_ROOT),
        "--config", str(config_path),
        "--distribution", str(distribution_path),
        "--geometry-report", str(geometry_path),
        "--lidar-manifest", str(LIDAR_MANIFEST),
        "--output", str(handoff_mask_path),
        "--report", str(handoff_report_path),
        "--zero-padding-m", str(handoff_config["zero_padding_m"]),
        "--feather-m", str(handoff_config["smoothstep_feather_m"]),
    ]
    if args.preview_dir:
        handoff_command.extend(("--preview", str(args.preview_dir.resolve() / "tier3_handoff_mask_qa.png")))
    subprocess.run(handoff_command, cwd=REPO_ROOT, check=True)
    texture_meta(
        handoff_mask_path,
        srgb=False,
        alpha_cutout=False,
        single_channel_r8=True,
        mipmaps=False,
        note="Final Tier3 eligible-canopy and Tier0-2 ownership weight; R8, no mips, exact frozen lidar grid",
    )
    default_meta(handoff_report_path, "Tier3 eligible-canopy and geometry-ownership handoff report")

    shader_path = VEGETATION_ROOT / "Shaders/AerialVegetationOpaque.shader"
    include_path = VEGETATION_ROOT / "Shaders/AerialCanopyMap.cginc"
    shutil.copyfile(TOOL_DIR / "templates/AerialVegetationOpaque.shader", shader_path)
    shutil.copyfile(TOOL_DIR / "templates/AerialCanopyMap.cginc", include_path)
    shader_meta(shader_path)
    default_meta(include_path, "Aerial Scenery V1 Tier 3 canopy-map shader contract")

    material_path = VEGETATION_ROOT / "Materials/AerialVegetation_AtlasCutout.mat"
    write_material(material_path, shader_path, atlas_path, surface_path)
    material_meta(material_path)

    runtime_path = VEGETATION_ROOT / "Runtime/AerialVegetationBatchRenderer.cs"
    editor_path = VEGETATION_ROOT / "Editor/AerialVegetationPrefabAuthoring.cs"
    script_meta(runtime_path, "Aerial Scenery V1 preallocated GPU-instanced batch renderer")
    script_meta(editor_path, "Aerial Scenery V1 deterministic prefab authoring entry point")

    geometry = json.loads(geometry_path.read_text(encoding="utf-8"))
    handoff_report = json.loads(handoff_report_path.read_text(encoding="utf-8"))
    contract_path = VEGETATION_ROOT / "AerialVegetation_Contract.json"
    write_contract(contract_path, config, geometry, handoff_report)
    default_meta(contract_path, "Aerial Scenery V1 vegetation representation and integration contract")

    default_meta(distribution_path, "Validated lidar-derived Aerial Scenery V1 vegetation distribution")

    source_manifest_path = VEGETATION_ROOT / "SOURCE_MANIFEST.json"
    source_files = [
        (blend_path, "Deterministic Blender 5.1 project source"),
        (model_path, "FBX export from AerialVegetationMultiscale.blend"),
        (geometry_path, "Blender-authored structural and budget report"),
        (atlas_path, "Deterministic project-authored 1K bark and five-foliage alpha-test atlas"),
        (surface_path, "Deterministic packed normal XY, roughness, and AO atlas"),
        (atlas_report_path, "Deterministic atlas layout, source, license, and hash report"),
        (handoff_mask_path, "Final R8 eligible-canopy and Tier0-2/Tier3 spatial ownership weight"),
        (handoff_report_path, "Deterministic handoff inputs, semantics, exclusions, coverage, and hash report"),
        (shader_path, "Fixed-card alpha-test Built-in Render Pipeline vegetation shader"),
        (include_path, "Tier 3 canopy-map shader include with bounds and R8 ownership gating"),
        (material_path, "One-material Unity instancing configuration"),
        (contract_path, "Four-tier vegetation and integration contract"),
        (runtime_path, "Preallocated runtime instance batch renderer"),
        (editor_path, "Deterministic Unity prefab authoring entry point"),
        (TOOL_DIR / "author_multiscale_vegetation.py", "Deterministic Blender authoring generator"),
        (TOOL_DIR / "generate_vegetation_atlas.py", "Deterministic 1K atlas generator"),
        (TOOL_DIR / "generate_vegetation_distribution.py", "Validated lidar distribution generator"),
        (TOOL_DIR / "generate_vegetation_handoff_mask.py", "Deterministic eligible-canopy and spatial ownership mask generator"),
        (config_path, "Pinned vegetation tiers, sampling, ownership handoff, and budgets"),
    ]
    source_files.extend(
        (
            VEGETATION_ROOT / f"Generated/AerialVegetation_Tier{tier}_Combined.asset",
            f"Deterministic Unity Editor-baked heterogeneous Tier{tier} mesh",
        )
        for tier in range(3)
    )
    source_files.append((
        VEGETATION_ROOT / "Generated/AerialVegetationRoot.prefab",
        "Deterministic three-draw vegetation prefab; product-scene integration remains lead-owned",
    ))
    source_files.append((distribution_path, "Validated lidar-derived distribution; frozen source hashes embedded"))
    write_source_manifest(source_manifest_path, source_files)
    default_meta(source_manifest_path, "Aerial Scenery V1 vegetation source and license manifest")

    print(json.dumps({
        "status": "BUILT",
        "blend": logical(blend_path),
        "model": logical(model_path),
        "geometry_report": logical(geometry_path),
        "handoff_mask": logical(handoff_mask_path),
        "handoff_mask_sha256": handoff_report["output"]["sha256"],
        "handoff_mask_runtime_bytes": handoff_report["output"]["runtime_uncompressed_bytes"],
        "contract": logical(contract_path),
        "distribution_lidar_manifest_sha256": distribution["input"]["lidar_manifest_sha256"],
        "source_manifest": logical(source_manifest_path),
    }, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
