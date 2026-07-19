"""Shared deterministic helpers for the bounded KBDU 3DEP pipeline."""

from __future__ import annotations

import hashlib
import json
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable, Sequence


GUID_NAMESPACE = "questflight:aerial-scenery-v1:lidar:"


def repository_root() -> Path:
    return Path(__file__).resolve().parents[3]


def default_config_path() -> Path:
    return Path(__file__).with_name("config.json")


def load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"Expected an object in {path}")
    return value


def load_config(path: Path) -> dict[str, Any]:
    config = load_json(path)
    if config.get("schema_version") != 1:
        raise ValueError(f"Unsupported lidar config schema: {config.get('schema_version')!r}")
    grid = config["product_grid"]
    expected_width = float(grid["pixel_size_meters"]) * int(grid["width_pixels"])
    if not math.isclose(expected_width, 2.0 * float(grid["half_extent_meters"]), abs_tol=1e-9):
        raise ValueError("Product width, pixel size, and half extent disagree")
    if int(grid["width_pixels"]) != int(grid["height_pixels"]):
        raise ValueError("The V1 lidar product must remain square")
    return config


def canonical_json_bytes(value: Any) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(canonical_json_bytes(value))


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def file_record(path: Path, logical_path: str | None = None) -> dict[str, Any]:
    return {
        "bytes": path.stat().st_size,
        "path": logical_path or path.as_posix(),
        "sha256": sha256_file(path),
    }


def stable_guid(logical_path: str) -> str:
    normalized = logical_path.replace("\\", "/")
    return hashlib.sha256((GUID_NAMESPACE + normalized).encode("utf-8")).hexdigest()[:32]


@dataclass(frozen=True)
class GridContract:
    width: int
    height: int
    pixel_size: float
    half_extent: float
    origin_easting: float
    origin_northing: float
    min_easting: float
    min_northing: float
    max_easting: float
    max_northing: float
    source_query_bounds: tuple[float, float, float, float, float, float]

    def flat_indices(self, easting: Any, northing: Any) -> tuple[Any, Any, Any]:
        """Return inside mask, rows, and columns for NumPy-like arrays."""
        import numpy as np

        inside = (
            (easting >= self.min_easting)
            & (easting < self.max_easting)
            & (northing >= self.min_northing)
            & (northing < self.max_northing)
        )
        columns = np.floor((easting[inside] - self.min_easting) / self.pixel_size).astype(np.int32)
        rows = np.floor((self.max_northing - northing[inside]) / self.pixel_size).astype(np.int32)
        return inside, rows, columns

    def manifest_value(self) -> dict[str, Any]:
        return {
            "height_pixels": self.height,
            "pixel_size_meters": self.pixel_size,
            "product_bounds_utm13n_meters": [
                self.min_easting,
                self.min_northing,
                self.max_easting,
                self.max_northing,
            ],
            "width_pixels": self.width,
        }


def compute_grid_contract(config: dict[str, Any]) -> GridContract:
    from pyproj import Transformer

    grid = config["product_grid"]
    origin = config["origin"]
    product_epsg = int(grid["product_crs_epsg"])
    source_epsg = int(grid["source_crs_epsg"])
    to_product = Transformer.from_crs(4326, product_epsg, always_xy=True)
    to_source = Transformer.from_crs(product_epsg, source_epsg, always_xy=True)
    origin_easting, origin_northing = to_product.transform(
        float(origin["longitude_degrees"]), float(origin["latitude_degrees"])
    )
    half = float(grid["half_extent_meters"])
    min_easting = origin_easting - half
    max_easting = origin_easting + half
    min_northing = origin_northing - half
    max_northing = origin_northing + half
    corners = [
        to_source.transform(easting, northing)
        for easting in (min_easting, max_easting)
        for northing in (min_northing, max_northing)
    ]
    select = config["ept_selection"]
    source_bounds = (
        min(item[0] for item in corners),
        min(item[1] for item in corners),
        float(select["vertical_query_min_meters"]),
        max(item[0] for item in corners),
        max(item[1] for item in corners),
        float(select["vertical_query_max_meters"]),
    )
    return GridContract(
        width=int(grid["width_pixels"]),
        height=int(grid["height_pixels"]),
        pixel_size=float(grid["pixel_size_meters"]),
        half_extent=half,
        origin_easting=origin_easting,
        origin_northing=origin_northing,
        min_easting=min_easting,
        min_northing=min_northing,
        max_easting=max_easting,
        max_northing=max_northing,
        source_query_bounds=source_bounds,
    )


def parse_node_key(key: str) -> tuple[int, int, int, int]:
    parts = key.split("-")
    if len(parts) != 4:
        raise ValueError(f"Invalid EPT node key: {key!r}")
    depth, x_index, y_index, z_index = (int(item) for item in parts)
    if depth < 0 or min(x_index, y_index, z_index) < 0:
        raise ValueError(f"Invalid EPT node key: {key!r}")
    return depth, x_index, y_index, z_index


def node_sort_key(key: str) -> tuple[int, int, int, int]:
    return parse_node_key(key)


def node_bounds(root_bounds: Sequence[float], key: str) -> tuple[float, float, float, float, float, float]:
    if len(root_bounds) != 6:
        raise ValueError("EPT root bounds must contain six numbers")
    depth, x_index, y_index, z_index = parse_node_key(key)
    x_side = float(root_bounds[3]) - float(root_bounds[0])
    y_side = float(root_bounds[4]) - float(root_bounds[1])
    z_side = float(root_bounds[5]) - float(root_bounds[2])
    divisor = 1 << depth
    dx, dy, dz = x_side / divisor, y_side / divisor, z_side / divisor
    return (
        float(root_bounds[0]) + x_index * dx,
        float(root_bounds[1]) + y_index * dy,
        float(root_bounds[2]) + z_index * dz,
        float(root_bounds[0]) + (x_index + 1) * dx,
        float(root_bounds[1]) + (y_index + 1) * dy,
        float(root_bounds[2]) + (z_index + 1) * dz,
    )


def bounds_intersect(first: Sequence[float], second: Sequence[float]) -> bool:
    if len(first) != 6 or len(second) != 6:
        raise ValueError("3D bounds must contain six numbers")
    return (
        float(first[0]) < float(second[3])
        and float(first[3]) > float(second[0])
        and float(first[1]) < float(second[4])
        and float(first[4]) > float(second[1])
        and float(first[2]) < float(second[5])
        and float(first[5]) > float(second[2])
    )


def selected_hierarchy_entries(
    pages: Iterable[dict[str, int]],
    root_bounds: Sequence[float],
    query_bounds: Sequence[float],
    target_depth: int,
) -> list[tuple[str, int]]:
    entries: dict[str, int] = {}
    for page in pages:
        entries.update(page)
    selected = [
        (key, count)
        for key, count in entries.items()
        if int(count) >= 0
        and parse_node_key(key)[0] <= target_depth
        and bounds_intersect(node_bounds(root_bounds, key), query_bounds)
    ]
    return sorted(selected, key=lambda item: node_sort_key(item[0]))


def smoothstep(edge0: float, edge1: float, value: Any) -> Any:
    import numpy as np

    if not edge1 > edge0:
        raise ValueError("smoothstep requires edge1 > edge0")
    t = np.clip((value - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def decode_height_image(path: Path, offset: float, scale: float) -> Any:
    import numpy as np
    from PIL import Image

    codes = np.asarray(Image.open(path), dtype=np.float32)
    if codes.ndim != 2:
        raise ValueError(f"Expected one-channel height image: {path}")
    return codes * scale + offset


def sample_north_up_grid(
    raster: Any,
    contract: GridContract,
    local_x: Any,
    local_z: Any,
) -> Any:
    """Bilinearly sample a north-up raster at local ENU x/z coordinates."""
    import numpy as np

    easting = contract.origin_easting + np.asarray(local_x, dtype=np.float64)
    northing = contract.origin_northing + np.asarray(local_z, dtype=np.float64)
    column = (easting - contract.min_easting) / contract.pixel_size - 0.5
    row = (contract.max_northing - northing) / contract.pixel_size - 0.5
    column = np.clip(column, 0.0, contract.width - 1.0)
    row = np.clip(row, 0.0, contract.height - 1.0)
    c0 = np.floor(column).astype(np.int64)
    r0 = np.floor(row).astype(np.int64)
    c1 = np.minimum(c0 + 1, contract.width - 1)
    r1 = np.minimum(r0 + 1, contract.height - 1)
    u = column - c0
    v = row - r0
    return (
        raster[r0, c0] * (1.0 - u) * (1.0 - v)
        + raster[r0, c1] * u * (1.0 - v)
        + raster[r1, c0] * (1.0 - u) * v
        + raster[r1, c1] * u * v
    )


def write_folder_meta(path: Path, logical_path: str) -> None:
    meta = Path(str(path) + ".meta")
    meta.write_text(
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(logical_path)}\n"
        "folderAsset: yes\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData:\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n",
        encoding="utf-8",
        newline="\n",
    )


def write_default_meta(path: Path, logical_path: str, note: str) -> None:
    meta = Path(str(path) + ".meta")
    meta.write_text(
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(logical_path)}\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        f"  userData: {note}\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n",
        encoding="utf-8",
        newline="\n",
    )


def write_text_meta(path: Path, logical_path: str, note: str) -> None:
    meta = Path(str(path) + ".meta")
    meta.write_text(
        "fileFormatVersion: 2\n"
        f"guid: {stable_guid(logical_path)}\n"
        "TextScriptImporter:\n"
        "  externalObjects: {}\n"
        f"  userData: {note}\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n",
        encoding="utf-8",
        newline="\n",
    )


def write_model_meta(path: Path, logical_path: str, note: str) -> None:
    """Write a deterministic OBJ importer contract with no materials or animation."""
    meta = Path(str(path) + ".meta")
    meta.write_text(
        f"""fileFormatVersion: 2
guid: {stable_guid(logical_path)}
ModelImporter:
  serializedVersion: 24200
  internalIDToNameTable: []
  externalObjects: {{}}
  materials:
    materialImportMode: 0
    materialName: 0
    materialSearch: 1
    materialLocation: 1
  animations:
    legacyGenerateAnimations: 4
    bakeSimulation: 0
    resampleCurves: 1
    optimizeGameObjects: 0
    removeConstantScaleCurves: 0
    motionNodeName:
    animationImportErrors:
    animationImportWarnings:
    animationRetargetingWarnings:
    animationDoRetargetingWarnings: 0
    importAnimatedCustomProperties: 0
    importConstraints: 0
    animationCompression: 1
    animationRotationError: 0.5
    animationPositionError: 0.5
    animationScaleError: 0.5
    animationWrapMode: 0
    extraExposedTransformPaths: []
    extraUserProperties: []
    clipAnimations: []
    isReadable: 0
  meshes:
    lODScreenPercentages: []
    globalScale: 1
    meshCompression: 0
    addColliders: 0
    useSRGBMaterialColor: 1
    sortHierarchyByName: 1
    importPhysicalCameras: 0
    importVisibility: 0
    importBlendShapes: 0
    importCameras: 0
    importLights: 0
    nodeNameCollisionStrategy: 1
    fileIdsGeneration: 2
    swapUVChannels: 0
    generateSecondaryUV: 0
    useFileUnits: 1
    keepQuads: 0
    weldVertices: 1
    bakeAxisConversion: 0
    preserveHierarchy: 0
    skinWeightsMode: 0
    maxBonesPerVertex: 4
    minBoneWeight: 0.001
    optimizeBones: 1
    generateMeshLods: 0
    meshLodGenerationFlags: 0
    maximumMeshLod: -1
    meshOptimizationFlags: -1
    indexFormat: 0
    secondaryUVAngleDistortion: 8
    secondaryUVAreaDistortion: 15.000001
    secondaryUVHardAngle: 88
    secondaryUVMarginMethod: 1
    secondaryUVMinLightmapResolution: 40
    secondaryUVMinObjectScale: 1
    secondaryUVPackMargin: 4
    useFileScale: 1
    strictVertexDataChecks: 1
  tangentSpace:
    normalSmoothAngle: 60
    normalImportMode: 0
    tangentImportMode: 3
    normalCalculationMode: 4
    legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes: 0
    blendShapeNormalImportMode: 1
    normalSmoothingSource: 0
  referencedClips: []
  importAnimation: 0
  animationType: 0
  humanoidOversampling: 1
  avatarSetup: 0
  importBlendShapeDeformPercent: 1
  remapMaterialsIfMaterialImportModeIsNone: 0
  additionalBone: 0
  userData: {note}
  assetBundleName:
  assetBundleVariant:
""",
        encoding="utf-8",
        newline="\n",
    )


def write_texture_meta(path: Path, logical_path: str, note: str, max_size: int = 1024) -> None:
    """Write a data-texture importer contract with no lossy compression or mips."""
    meta = Path(str(path) + ".meta")
    meta.write_text(
        f"""fileFormatVersion: 2
guid: {stable_guid(logical_path)}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 0
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
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
  textureFormat: 1
  maxTextureSize: {max_size}
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
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
  alphaUsage: 1
  alphaIsTransparency: 0
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 1
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: {max_size}
    resizeAlgorithm: 0
    textureFormat: -1
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
    maxTextureSize: {max_size}
    resizeAlgorithm: 0
    textureFormat: -1
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
""",
        encoding="utf-8",
        newline="\n",
    )
