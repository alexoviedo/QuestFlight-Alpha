#!/usr/bin/env python3
"""Author deterministic, project-owned multiscale vegetation source meshes.

Run with Blender 5.x in background mode. The five regional archetypes and a
reference ten-tree grove use project-authored atlas-cutout foliage on fixed 3D
card clusters with visible tapered wood. Unity uses the tree sources for Tiers
0/1 and compiles bounded fixed upper-canopy cells for Tier 2; no representation
creates one GameObject or draw call per tree.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from array import array
from dataclasses import dataclass
from pathlib import Path

import bpy
from mathutils import Vector


LOGICAL_BLEND = "Assets/ArtSource/Blender/AerialSceneryV1/AerialVegetationMultiscale.blend"
LOGICAL_MODEL = (
    "Assets/Production/Environment/AerialSceneryV1/Vegetation/Models/"
    "AerialVegetationMultiscale.fbx"
)
LOGICAL_REPORT = (
    "Assets/Production/Environment/AerialSceneryV1/Vegetation/"
    "AerialVegetation_GeometryReport.json"
)
GUID_NAMESPACE = "questflight:aerial-scenery-v1:vegetation:"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def stable_guid(logical_path: str) -> str:
    return hashlib.sha256((GUID_NAMESPACE + logical_path.replace("\\", "/")).encode()).hexdigest()[:32]


def write_default_meta(path: Path, logical_path: str, note: str) -> None:
    Path(str(path) + ".meta").write_text(
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


def write_model_meta(path: Path) -> None:
    Path(str(path) + ".meta").write_text(
        f"""fileFormatVersion: 2
guid: {stable_guid(LOGICAL_MODEL)}
ModelImporter:
  serializedVersion: 22200
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
    optimizeGameObjects: 1
    removeConstantScaleCurves: 1
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
    isReadable: 1
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
    fileIdsGeneration: 2
    swapUVChannels: 0
    generateSecondaryUV: 0
    useFileUnits: 1
    keepQuads: 0
    weldVertices: 1
    bakeAxisConversion: 0
    preserveHierarchy: 1
    skinWeightsMode: 0
    maxBonesPerVertex: 4
    minBoneWeight: 0.001
    optimizeBones: 1
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
  humanDescription:
    serializedVersion: 3
    human: []
    skeleton: []
    armTwist: 0.5
    foreArmTwist: 0.5
    upperLegTwist: 0.5
    legTwist: 0.5
    armStretch: 0.05
    legStretch: 0.05
    feetSpacing: 0
    globalScale: 1
    rootMotionBoneName:
    hasTranslationDoF: 0
    hasExtraRoot: 0
    skeletonHasParents: 1
  lastHumanDescriptionAvatarSource: {{instanceID: 0}}
  autoGenerateAvatarMappingIfUnspecified: 1
  animationType: 0
  humanoidOversampling: 1
  avatarSetup: 0
  addHumanoidExtraRootOnlyWhenUsingAvatar: 1
  remapMaterialsIfMaterialImportModeIsNone: 0
  additionalBone: 0
  userData: Aerial Scenery V1 project-authored fixed atlas-card sources for deterministic Editor tier baking; no camera-facing behavior
  assetBundleName:
  assetBundleVariant:
""",
        encoding="utf-8",
        newline="\n",
    )


Color = tuple[float, float, float, float]

ATLAS_GRID: dict[str, tuple[int, int]] = {
    "bark": (0, 0),
    "cottonwood": (1, 0),
    "willow_riparian": (2, 0),
    "general_deciduous": (3, 0),
    "sparse_conifer": (0, 1),
    "regional_shrub": (1, 1),
    "coarse_canopy": (2, 1),
}


def atlas_rect(name: str) -> tuple[float, float, float, float]:
    column, row = ATLAS_GRID[name]
    inset = 3.0 / 1024.0
    return (
        column * 0.25 + inset,
        row * 0.5 + inset,
        (column + 1) * 0.25 - inset,
        (row + 1) * 0.5 - inset,
    )


def atlas_center(name: str) -> tuple[float, float]:
    u0, v0, u1, v1 = atlas_rect(name)
    return ((u0 + u1) * 0.5, (v0 + v1) * 0.5)


def clamp01(value: float) -> float:
    return max(0.0, min(1.0, value))


def shade_color(base: Color, vertical: float, azimuth: float, ao: float = 1.0) -> Color:
    light = 0.78 + vertical * 0.18 + math.cos(azimuth - 0.7) * 0.055
    return (
        clamp01(base[0] * light),
        clamp01(base[1] * light),
        clamp01(base[2] * light),
        clamp01(ao),
    )


@dataclass
class MeshBuilder:
    vertices: list[tuple[float, float, float]]
    colors: list[Color]
    uvs: list[tuple[float, float]]
    faces: list[tuple[int, int, int]]
    smooth: list[bool]
    closed_parts: int = 0
    atlas_card_faces: int = 0
    fixed_card_clusters: int = 0

    @classmethod
    def create(cls) -> "MeshBuilder":
        return cls([], [], [], [], [])

    def vertex(self, point: Vector, color: Color, uv: tuple[float, float] | None = None) -> int:
        self.vertices.append((point.x, point.y, point.z))
        self.colors.append(color)
        self.uvs.append(uv or atlas_center("coarse_canopy"))
        return len(self.vertices) - 1

    def triangle(self, a: int, b: int, c: int, *, smooth: bool) -> None:
        self.faces.append((a, b, c))
        self.smooth.append(smooth)

    def cylinder(
        self,
        start: Vector,
        end: Vector,
        radius_start: float,
        radius_end: float,
        segments: int,
        base_color: Color,
    ) -> None:
        direction = end - start
        if direction.length <= 1e-5:
            raise ValueError("Cylinder length must be positive")
        direction.normalize()
        reference = Vector((0.0, 0.0, 1.0)) if abs(direction.z) < 0.9 else Vector((1.0, 0.0, 0.0))
        axis_x = direction.cross(reference).normalized()
        axis_y = direction.cross(axis_x).normalized()
        lower: list[int] = []
        upper: list[int] = []
        u0, v0, u1, v1 = atlas_rect("bark")
        for index in range(segments + 1):
            angle = math.tau * index / segments
            radial = axis_x * math.cos(angle) + axis_y * math.sin(angle)
            u = u0 + (u1 - u0) * index / segments
            lower.append(self.vertex(start + radial * radius_start, shade_color(base_color, 0.0, angle, 0.72), (u, v0)))
            upper.append(self.vertex(end + radial * radius_end, shade_color(base_color, 1.0, angle, 0.92), (u, v1)))
        for index in range(segments):
            nxt = index + 1
            self.triangle(lower[index], lower[nxt], upper[nxt], smooth=True)
            self.triangle(lower[index], upper[nxt], upper[index], smooth=True)
        center_uv = ((u0 + u1) * 0.5, (v0 + v1) * 0.5)
        bottom = self.vertex(start, shade_color(base_color, 0.0, 0.0, 0.65), center_uv)
        top = self.vertex(end, shade_color(base_color, 1.0, 0.0, 0.95), center_uv)
        for index in range(segments):
            nxt = index + 1
            self.triangle(bottom, lower[nxt], lower[index], smooth=False)
            self.triangle(top, upper[index], upper[nxt], smooth=False)
        self.closed_parts += 1

    def atlas_card(
        self,
        center: Vector,
        width: float,
        height: float,
        yaw: float,
        pitch: float,
        atlas_cell: str,
        color: Color,
    ) -> None:
        right = Vector((math.cos(yaw), math.sin(yaw), 0.0))
        upward = Vector((
            -math.sin(yaw) * math.sin(pitch),
            math.cos(yaw) * math.sin(pitch),
            math.cos(pitch),
        )).normalized()
        half_right = right * (width * 0.5)
        half_up = upward * (height * 0.5)
        u0, v0, u1, v1 = atlas_rect(atlas_cell)
        vertices = (
            self.vertex(center - half_right - half_up, color, (u0, v0)),
            self.vertex(center + half_right - half_up, color, (u1, v0)),
            self.vertex(center + half_right + half_up, color, (u1, v1)),
            self.vertex(center - half_right + half_up, color, (u0, v1)),
        )
        self.triangle(vertices[0], vertices[1], vertices[2], smooth=False)
        self.triangle(vertices[0], vertices[2], vertices[3], smooth=False)
        self.atlas_card_faces += 2








FOLIAGE_COTTONWOOD: Color = (1.00, 1.00, 0.94, 1.0)
FOLIAGE_WILLOW: Color = (0.90, 1.00, 0.95, 1.0)
FOLIAGE_DECIDUOUS: Color = (0.94, 1.00, 0.88, 1.0)
FOLIAGE_CONIFER: Color = (0.88, 0.97, 0.92, 1.0)
FOLIAGE_SHRUB: Color = (1.00, 0.98, 0.84, 1.0)
TRUNK: Color = (0.95, 0.90, 0.82, 1.0)
WILLOW_TRUNK: Color = (0.98, 0.94, 0.84, 1.0)
CONIFER_TRUNK: Color = (0.90, 0.85, 0.78, 1.0)


def fraction(index: int, salt: float) -> float:
    value = math.sin((index + 1) * 12.9898 + salt * 78.233) * 43758.5453
    return value - math.floor(value)


def varied_color(base: Color, index: int, salt: float) -> Color:
    warm = fraction(index, salt) - 0.5
    light = 0.90 + fraction(index, salt + 4.7) * 0.20
    return (
        clamp01(base[0] * light * (1.0 + warm * 0.10)),
        clamp01(base[1] * light),
        clamp01(base[2] * light * (1.0 - warm * 0.08)),
        1.0,
    )


def ellipsoid_points(
    count: int,
    center: Vector,
    spread: Vector,
    salt: float,
    *,
    umbrella: bool = False,
) -> list[Vector]:
    points: list[Vector] = []
    golden_angle = math.pi * (3.0 - math.sqrt(5.0))
    for index in range(count):
        vertical = -0.82 + 1.64 * ((index + 0.5) / count)
        radial = math.sqrt(max(0.0, 1.0 - vertical * vertical))
        angle = index * golden_angle + salt
        radial_jitter = 0.72 + fraction(index, salt) * 0.34
        z = vertical * spread.z
        if umbrella:
            z -= abs(radial) ** 1.7 * spread.z * 0.20
        points.append(center + Vector((
            math.cos(angle) * spread.x * radial * radial_jitter,
            math.sin(angle) * spread.y * radial * radial_jitter,
            z + (fraction(index, salt + 2.1) - 0.5) * spread.z * 0.18,
        )))
    return points


def cone_points(count: int, center_z: float, height: float, radius: float, salt: float) -> list[Vector]:
    points: list[Vector] = []
    layers = 5
    for index in range(count):
        layer = index % layers
        height_fraction = (layer + 0.45 + fraction(index, salt) * 0.34) / layers
        angle = index * 2.399963 + salt
        layer_radius = radius * (1.0 - height_fraction * 0.76) * (0.72 + fraction(index, salt + 1.0) * 0.34)
        points.append(Vector((
            math.cos(angle) * layer_radius,
            math.sin(angle) * layer_radius,
            center_z + height_fraction * height,
        )))
    return points


def add_trunk_and_branches(
    builder: MeshBuilder,
    trunk_height: float,
    trunk_radius: float,
    branch_targets: list[Vector],
    tier: int,
    trunk_color: Color,
    *,
    trunk_offsets: tuple[Vector, ...] = (Vector((0.0, 0.0, 0.0)),),
) -> None:
    trunk_segments = 8 if tier == 0 else 6
    branch_segments = 5 if tier == 0 else 4
    for offset_index, offset in enumerate(trunk_offsets):
        section_count = 4 if offset_index == 0 else 3
        previous = offset
        base_radius = trunk_radius * (0.88 if offset_index else 1.0)
        for section in range(section_count):
            t = (section + 1) / section_count
            bend = (0.06 + offset_index * 0.045) * trunk_height
            endpoint = Vector((
                offset.x * (1.0 - t * 0.55) + math.sin(section * 0.91 + offset_index) * bend * t * 0.16,
                offset.y * (1.0 - t * 0.55) + math.cos(section * 0.77 + offset_index) * bend * t * 0.13,
                trunk_height * t,
            ))
            radius_start = base_radius * (1.0 - (section / section_count) * 0.61)
            radius_end = base_radius * (1.0 - t * 0.66)
            builder.cylinder(previous, endpoint, radius_start, radius_end,
                             trunk_segments if offset_index == 0 else branch_segments, trunk_color)
            previous = endpoint
    for index, target in enumerate(branch_targets):
        angle = math.atan2(target.y, target.x)
        base_z = trunk_height * (0.42 + 0.34 * ((index % 5) / 4.0))
        base = Vector((math.cos(angle) * trunk_radius * 0.18, math.sin(angle) * trunk_radius * 0.18, base_z))
        joint = base.lerp(target, 0.52) + Vector((
            math.sin(angle) * (0.10 + fraction(index, trunk_height) * 0.18),
            -math.cos(angle) * (0.10 + fraction(index, trunk_height + 1.0) * 0.18),
            0.18 + fraction(index, trunk_height + 2.0) * 0.30,
        ))
        radius = trunk_radius * (0.27 + fraction(index, trunk_height + 3.0) * 0.10)
        builder.cylinder(base, joint, radius, radius * 0.58, branch_segments, trunk_color)
        builder.cylinder(joint, target, radius * 0.58, radius * 0.18, branch_segments, trunk_color)


def add_card_crown(
    builder: MeshBuilder,
    centers: list[Vector],
    card_size: tuple[float, float],
    atlas_cell: str,
    color: Color,
    tier: int,
    salt: float,
    *,
    conifer_spray: bool = False,
) -> None:
    for index, center in enumerate(centers):
        size = 0.78 + fraction(index, salt + 8.0) * 0.40
        width = card_size[0] * size
        height = card_size[1] * size
        base_yaw = salt + index * 2.399963
        pitches = (0.06, 0.58, 1.03) if not conifer_spray else (0.18, 0.72, 1.16)
        tint = varied_color(color, index, salt)
        for card_index, pitch in enumerate(pitches):
            builder.atlas_card(
                center,
                width * (0.92 + card_index * 0.06),
                height * (1.0 - card_index * 0.08),
                base_yaw + card_index * math.pi / 3.0,
                pitch,
                atlas_cell,
                tint,
            )
        builder.fixed_card_clusters += 1


def cottonwood_builder(tier: int) -> MeshBuilder:
    builder = MeshBuilder.create()
    count = 120 if tier == 0 else 38
    centers = ellipsoid_points(count, Vector((0.0, 0.0, 10.6)), Vector((3.6, 3.2, 4.7)), 0.23)
    branch_targets = [Vector((center.x * 0.74, center.y * 0.74, center.z * 0.82))
                      for center in centers[::10 if tier == 0 else 10]]
    add_trunk_and_branches(builder, 10.0, 0.48 if tier == 0 else 0.42, branch_targets, tier, TRUNK)
    add_card_crown(builder, centers, (1.55, 3.10), "cottonwood", FOLIAGE_COTTONWOOD, tier, 0.23)
    return builder


def willow_builder(tier: int) -> MeshBuilder:
    builder = MeshBuilder.create()
    count = 124 if tier == 0 else 40
    centers = ellipsoid_points(
        count, Vector((0.0, 0.0, 7.0)), Vector((5.0, 4.4, 2.45)), 1.17, umbrella=True
    )
    branch_targets = [Vector((center.x * 0.82, center.y * 0.82, center.z * 0.78))
                      for center in centers[::10 if tier == 0 else 10]]
    offsets = (
        Vector((-0.28, 0.02, 0.0)),
        Vector((0.24, -0.16, 0.0)),
        Vector((0.08, 0.24, 0.0)),
    )
    add_trunk_and_branches(builder, 6.2, 0.31 if tier == 0 else 0.27, branch_targets, tier,
                           WILLOW_TRUNK, trunk_offsets=offsets)
    add_card_crown(builder, centers, (1.65, 3.25), "willow_riparian", FOLIAGE_WILLOW, tier, 1.17)
    return builder


def deciduous_builder(tier: int) -> MeshBuilder:
    builder = MeshBuilder.create()
    count = 116 if tier == 0 else 37
    centers = ellipsoid_points(count, Vector((0.0, 0.0, 8.7)), Vector((4.0, 3.7, 3.25)), 2.41)
    branch_targets = [Vector((center.x * 0.78, center.y * 0.78, center.z * 0.80))
                      for center in centers[::10 if tier == 0 else 10]]
    add_trunk_and_branches(builder, 7.7, 0.43 if tier == 0 else 0.37, branch_targets, tier, TRUNK)
    add_card_crown(builder, centers, (1.62, 3.15), "general_deciduous", FOLIAGE_DECIDUOUS, tier, 2.41)
    return builder


def conifer_builder(tier: int) -> MeshBuilder:
    builder = MeshBuilder.create()
    count = 130 if tier == 0 else 42
    centers = cone_points(count, 1.8, 9.7, 3.7, 3.22)
    branch_targets = [Vector((center.x * 0.80, center.y * 0.80, center.z - 0.35))
                      for center in centers[::10 if tier == 0 else 10]]
    add_trunk_and_branches(builder, 11.2, 0.34 if tier == 0 else 0.29, branch_targets, tier, CONIFER_TRUNK)
    add_card_crown(builder, centers, (1.45, 2.90), "sparse_conifer", FOLIAGE_CONIFER, tier, 3.22,
                   conifer_spray=True)
    return builder


def shrub_builder(tier: int) -> MeshBuilder:
    builder = MeshBuilder.create()
    count = 105 if tier == 0 else 34
    centers = ellipsoid_points(count, Vector((0.0, 0.0, 2.15)), Vector((3.2, 2.8, 1.35)), 4.31,
                               umbrella=True)
    branch_targets = [Vector((center.x * 0.86, center.y * 0.86, center.z * 0.76))
                      for center in centers[::10 if tier == 0 else 9]]
    stem_count = 5 if tier == 0 else 3
    offsets = tuple(
        Vector(((index - (stem_count - 1) * 0.5) * 0.16, ((index * 2) % stem_count - (stem_count - 1) * 0.5) * 0.12, 0.0))
        for index in range(stem_count)
    )
    add_trunk_and_branches(builder, 2.2, 0.13 if tier == 0 else 0.11, branch_targets, tier, TRUNK,
                           trunk_offsets=offsets)
    add_card_crown(builder, centers, (1.35, 2.55), "regional_shrub", FOLIAGE_SHRUB, tier, 4.31)
    return builder


def grove_builder() -> MeshBuilder:
    builder = MeshBuilder.create()
    trees = (
        (-10.8, -3.8, 8.8, 3.7, 0.18, "cottonwood"),
        (-8.0, 5.0, 7.4, 4.1, 0.62, "willow_riparian"),
        (-4.0, -1.0, 9.6, 4.4, 1.04, "general_deciduous"),
        (0.8, 4.2, 10.2, 4.5, 1.46, "cottonwood"),
        (5.8, -3.1, 8.0, 4.0, 1.91, "willow_riparian"),
        (10.0, 3.8, 7.2, 3.6, 2.34, "general_deciduous"),
        (0.0, 10.0, 7.7, 3.8, 2.79, "cottonwood"),
        (-5.7, 10.2, 6.0, 3.1, 3.18, "regional_shrub"),
        (-3.0, -9.4, 6.8, 3.4, 3.62, "general_deciduous"),
        (6.2, -9.0, 7.8, 3.5, 4.05, "sparse_conifer"),
    )
    colors = {
        "cottonwood": FOLIAGE_COTTONWOOD,
        "willow_riparian": FOLIAGE_WILLOW,
        "general_deciduous": FOLIAGE_DECIDUOUS,
        "sparse_conifer": FOLIAGE_CONIFER,
        "regional_shrub": FOLIAGE_SHRUB,
    }
    for index, (x, y, height, crown_radius, phase, atlas_cell) in enumerate(trees):
        color = varied_color(colors[atlas_cell], index, 5.17)
        base = Vector((x, y, 0.0))
        trunk_top = base + Vector((math.sin(phase) * 0.18, math.cos(phase) * 0.16, height * 0.68))
        builder.cylinder(base, trunk_top, 0.24, 0.105, 3, TRUNK if atlas_cell != "sparse_conifer" else CONIFER_TRUNK)
        centers = [
            base + Vector((math.cos(phase) * crown_radius * 0.32,
                           math.sin(phase) * crown_radius * 0.32, height * 0.80)),
            base + Vector((math.cos(phase + 2.1) * crown_radius * 0.34,
                           math.sin(phase + 2.1) * crown_radius * 0.34, height * 0.76)),
            base + Vector((math.cos(phase + 4.2) * crown_radius * 0.22,
                           math.sin(phase + 4.2) * crown_radius * 0.22, height * 0.94)),
        ]
        for branch_index, target in enumerate(centers[:2]):
            branch_start = base + Vector((0.0, 0.0, height * (0.46 + branch_index * 0.08)))
            builder.cylinder(branch_start, target, 0.095, 0.035, 3,
                             TRUNK if atlas_cell != "sparse_conifer" else CONIFER_TRUNK)
        add_card_crown(
            builder,
            centers,
            (crown_radius * 0.88, crown_radius * 1.46),
            atlas_cell,
            color,
            2,
            phase + index * 0.27,
            conifer_spray=atlas_cell == "sparse_conifer",
        )
    return builder


def make_material(atlas_path: Path) -> bpy.types.Material:
    material = bpy.data.materials.new("AerialVegetation_AtlasCutout")
    material.use_nodes = True
    material.diffuse_color = (*FOLIAGE_DECIDUOUS[:3], 1.0)
    if hasattr(material, "surface_render_method"):
        material.surface_render_method = "DITHERED"
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    principled = nodes.new("ShaderNodeBsdfPrincipled")
    vertex_color = nodes.new("ShaderNodeVertexColor")
    atlas = nodes.new("ShaderNodeTexImage")
    atlas.image = bpy.data.images.load(str(atlas_path), check_existing=True)
    atlas.interpolation = "Linear"
    multiply = nodes.new("ShaderNodeMixRGB")
    multiply.blend_type = "MULTIPLY"
    multiply.inputs[0].default_value = 1.0
    alpha_clip = nodes.new("ShaderNodeMath")
    alpha_clip.operation = "GREATER_THAN"
    alpha_clip.inputs[1].default_value = 0.42
    vertex_color.layer_name = "COLOR_0"
    links.new(atlas.outputs["Color"], multiply.inputs[1])
    links.new(vertex_color.outputs["Color"], multiply.inputs[2])
    links.new(multiply.outputs["Color"], principled.inputs["Base Color"])
    links.new(atlas.outputs["Alpha"], alpha_clip.inputs[0])
    links.new(alpha_clip.outputs[0], principled.inputs["Alpha"])
    links.new(principled.outputs["BSDF"], output.inputs["Surface"])
    principled.inputs["Roughness"].default_value = 0.82
    return material


def create_object(
    name: str,
    builder: MeshBuilder,
    material: bpy.types.Material,
    tier: int,
    role: str,
) -> tuple[bpy.types.Object, dict]:
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(builder.vertices, [], builder.faces)
    mesh.update(calc_edges=True)
    colors = mesh.color_attributes.new(name="COLOR_0", type="BYTE_COLOR", domain="CORNER")
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for polygon_index, polygon in enumerate(mesh.polygons):
        polygon.use_smooth = builder.smooth[polygon_index]
        for loop_index in polygon.loop_indices:
            vertex_index = mesh.loops[loop_index].vertex_index
            colors.data[loop_index].color = builder.colors[vertex_index]
            uv_layer.data[loop_index].uv = builder.uvs[vertex_index]
    validation_changed = mesh.validate(verbose=False, clean_customdata=False)
    mesh.update(calc_edges=True)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(material)
    obj["questflight_role"] = role
    obj["representation_tier"] = tier
    obj["geometry_mode"] = "alpha_test_fixed_cards_and_closed_tapered_wood"
    obj["camera_facing"] = False
    obj["ordinary_vertical_billboard"] = False
    obj["alpha_blended"] = False
    obj["alpha_tested"] = True
    obj["runtime_usage"] = "editor-baked per-tier source; never one GameObject per placement"

    edge_face_counts = [0] * len(mesh.edges)
    edge_lookup = {tuple(sorted(edge.vertices)): edge.index for edge in mesh.edges}
    for polygon in mesh.polygons:
        for edge_key in polygon.edge_keys:
            edge_face_counts[edge_lookup[tuple(sorted(edge_key))]] += 1
    boundary_edges = sum(1 for count in edge_face_counts if count == 1)
    overconnected_edges = sum(1 for count in edge_face_counts if count > 2)
    xs = [vertex.co.x for vertex in mesh.vertices]
    ys = [vertex.co.y for vertex in mesh.vertices]
    zs = [vertex.co.z for vertex in mesh.vertices]
    report = {
        "name": name,
        "tier": tier,
        "role": role,
        "vertices": len(mesh.vertices),
        "triangles": len(mesh.polygons),
        "material_slots": len(obj.material_slots),
        "color_attributes": len(mesh.color_attributes),
        "uv_layers": len(mesh.uv_layers),
        "closed_parts": builder.closed_parts,
        "boundary_edges": boundary_edges,
        "overconnected_edges": overconnected_edges,
        "mesh_validation_changed": bool(validation_changed),
        "bounds_min": [min(xs), min(ys), min(zs)],
        "bounds_max": [max(xs), max(ys), max(zs)],
        "ground_error_m": abs(min(zs)),
        "alpha_card_faces": builder.atlas_card_faces,
        "fixed_orientation_card_clusters": builder.fixed_card_clusters,
        "alpha_clip": True,
        "alpha_blend": False,
        "camera_facing_faces": 0,
        "negative_scale": any(component < 0 for component in obj.scale),
    }
    return obj, report


def compose_contact_sheet(panel_paths: list[Path], output: Path, columns: int) -> None:
    images = [bpy.data.images.load(str(path), check_existing=False) for path in panel_paths]
    if not images:
        raise ValueError("Contact sheet needs at least one panel")
    width, height = int(images[0].size[0]), int(images[0].size[1])
    if any(tuple(image.size) != (width, height) for image in images):
        raise ValueError("Contact sheet panels must have equal dimensions")
    rows = math.ceil(len(images) / columns)
    sheet = bpy.data.images.new("AerialVegetation_QA_ContactSheet", width=width * columns, height=height * rows)
    pixels = array("f", [0.055, 0.07, 0.08, 1.0]) * (width * columns * height * rows)
    for panel_index, source in enumerate(images):
        source_pixels = array("f", [0.0]) * (width * height * 4)
        source.pixels.foreach_get(source_pixels)
        panel_column = panel_index % columns
        # First manifest row appears at the top of the PNG.
        panel_row = rows - 1 - panel_index // columns
        for row in range(height):
            source_start = row * width * 4
            destination_start = ((panel_row * height + row) * width * columns + panel_column * width) * 4
            pixels[destination_start:destination_start + width * 4] = source_pixels[source_start:source_start + width * 4]
    sheet.pixels.foreach_set(pixels)
    sheet.filepath_raw = str(output)
    sheet.file_format = "PNG"
    sheet.save()
    for source in images:
        bpy.data.images.remove(source)
    bpy.data.images.remove(sheet)


def render_previews(objects: list[bpy.types.Object], preview_dir: Path) -> list[dict]:
    preview_dir.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 700
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.world.color = (0.07, 0.105, 0.14)
    scene.view_settings.look = "AgX - Medium High Contrast"

    bpy.ops.mesh.primitive_plane_add(size=120, location=(0.0, 0.0, -0.03))
    ground = bpy.context.object
    ground.name = "PREVIEW_Ground"
    ground_material = bpy.data.materials.new("PREVIEW_GroundMaterial")
    ground_material.diffuse_color = (0.15, 0.19, 0.095, 1.0)
    ground.data.materials.append(ground_material)

    bpy.ops.object.light_add(type="SUN", location=(0, 0, 30))
    sun = bpy.context.object
    sun.name = "PREVIEW_Sun"
    sun.rotation_euler = (math.radians(28), math.radians(-12), math.radians(132))
    sun.data.energy = 3.0
    sun.data.angle = math.radians(1.5)

    bpy.ops.object.light_add(type="AREA", location=(-10, -12, 18))
    area = bpy.context.object
    area.name = "PREVIEW_Fill"
    area.data.energy = 900
    area.data.shape = "DISK"
    area.data.size = 10

    bpy.ops.object.camera_add()
    camera = bpy.context.object
    camera.name = "PREVIEW_Camera"
    camera.data.lens = 54
    camera.data.clip_end = 500.0
    scene.camera = camera

    def aim(location: Vector, target: Vector) -> None:
        camera.location = location
        camera.rotation_euler = (target - location).to_track_quat("-Z", "Y").to_euler()

    by_name = {obj.name: obj for obj in objects}
    tier0_names = [
        "AerialTree_Cottonwood_T0",
        "AerialTree_WillowRiparian_T0",
        "AerialTree_Deciduous_T0",
        "AerialTree_SparseConifer_T0",
        "AerialShrub_Regional_T0",
    ]
    tier1_names = [name.replace("_T0", "_T1") for name in tier0_names]
    lineup_x = (-14.5, -7.2, 0.0, 7.2, 14.5)
    views = (
        ("regional_t0_close", tier0_names, Vector((0, -50, 8.0)), Vector((0, 0, 6.6))),
        ("regional_t0_oblique", tier0_names, Vector((31, -45, 27)), Vector((0, 0, 6.2))),
        ("regional_t0_top", tier0_names, Vector((0, -0.5, 66)), Vector((0, 0, 1.0))),
        ("regional_t1_oblique", tier1_names, Vector((29, -44, 25)), Vector((0, 0, 6.0))),
        ("grove_t2_oblique", ["AerialGroveCluster_T2"], Vector((37, -47, 33)), Vector((0, 1, 3.0))),
        ("grove_t2_top", ["AerialGroveCluster_T2"], Vector((2, -2, 64)), Vector((0, 1, 1.0))),
    )
    records: list[dict] = []
    panel_paths: list[Path] = []
    for stem, visible_names, location, target in views:
        for obj in objects:
            obj.hide_render = obj.name not in visible_names
            obj.location = Vector((0.0, 0.0, 0.0))
        if len(visible_names) > 1:
            for name, x in zip(visible_names, lineup_x):
                by_name[name].location.x = x
        aim(location, target)
        output = preview_dir / f"{stem}.png"
        scene.render.filepath = str(output)
        bpy.ops.render.render(write_still=True)
        records.append({"name": stem, "path": str(output), "sha256": sha256(output)})
        panel_paths.append(output)
    contact_sheet = preview_dir / "aerial_vegetation_close_oblique_top_contact_sheet.png"
    compose_contact_sheet(panel_paths, contact_sheet, columns=3)
    records.append({
        "name": "close_oblique_top_contact_sheet",
        "path": str(contact_sheet),
        "sha256": sha256(contact_sheet),
        "panel_order": [record["name"] for record in records],
        "columns": 3,
        "rows": 2,
    })
    return records


def parse_args() -> argparse.Namespace:
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--blend", required=True)
    parser.add_argument("--fbx", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument("--atlas", required=True)
    parser.add_argument("--atlas-report", required=True)
    parser.add_argument("--preview-dir")
    parser.add_argument("--write-unity-meta", action="store_true")
    return parser.parse_args(argv)


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def main() -> int:
    args = parse_args()
    blend_path = Path(args.blend).resolve()
    fbx_path = Path(args.fbx).resolve()
    report_path = Path(args.report).resolve()
    atlas_path = Path(args.atlas).resolve()
    atlas_report_path = Path(args.atlas_report).resolve()
    if not atlas_path.is_file() or not atlas_report_path.is_file():
        raise FileNotFoundError("Vegetation atlas and atlas report must exist before Blender authoring")
    for path in (blend_path, fbx_path, report_path):
        path.parent.mkdir(parents=True, exist_ok=True)

    clear_scene()
    scene = bpy.context.scene
    bpy.context.preferences.filepaths.save_version = 0
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    material = make_material(atlas_path)
    definitions = (
        ("AerialTree_Cottonwood_T0", cottonwood_builder(0), 0, "regional_cottonwood"),
        ("AerialTree_Cottonwood_T1", cottonwood_builder(1), 1, "regional_cottonwood"),
        ("AerialTree_WillowRiparian_T0", willow_builder(0), 0, "riparian_willow"),
        ("AerialTree_WillowRiparian_T1", willow_builder(1), 1, "riparian_willow"),
        ("AerialTree_Deciduous_T0", deciduous_builder(0), 0, "general_deciduous"),
        ("AerialTree_Deciduous_T1", deciduous_builder(1), 1, "general_deciduous"),
        ("AerialTree_SparseConifer_T0", conifer_builder(0), 0, "sparse_conifer"),
        ("AerialTree_SparseConifer_T1", conifer_builder(1), 1, "sparse_conifer"),
        ("AerialShrub_Regional_T0", shrub_builder(0), 0, "regional_shrub"),
        ("AerialShrub_Regional_T1", shrub_builder(1), 1, "regional_shrub"),
        ("AerialGroveCluster_T2", grove_builder(), 2, "fixed_aerial_grove_cluster"),
    )
    objects: list[bpy.types.Object] = []
    reports: list[dict] = []
    for name, builder, tier, role in definitions:
        obj, report = create_object(name, builder, material, tier, role)
        objects.append(obj)
        reports.append(report)

    for candidate in bpy.context.selected_objects:
        candidate.select_set(False)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]

    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path), compress=True)
    bpy.ops.file.make_paths_relative()
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path), compress=True)
    bpy.ops.export_scene.fbx(
        filepath=str(fbx_path),
        use_selection=True,
        object_types={"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_tspace=False,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="RELATIVE",
        embed_textures=False,
    )

    triangle_by_name = {record["name"]: record["triangles"] for record in reports}
    tier0_triangles = [value for name, value in triangle_by_name.items() if name.endswith("_T0")]
    tier1_triangles = [value for name, value in triangle_by_name.items() if name.endswith("_T1")]
    per_view_budget = {
        "tier0_instance_cap": 3,
        "tier1_instance_cap": 19,
        "tier2_instance_cap": 248,
        "tier2_runtime_triangles_per_instance": 18,
        "tier2_runtime_representation": "Unity-authored bounded fixed upper-canopy cell aggregate",
        "worst_case_triangles": (
            3 * max(tier0_triangles)
            + 19 * max(tier1_triangles)
            + 248 * 18
        ),
        "replacement_ceiling_triangles": 19648,
        "source_triangle_bands": {"tier0_minimum": 1000, "tier0_maximum": 3000,
                                  "tier1_minimum": 250, "tier1_maximum": 800},
    }
    report = {
        "schema": "questflight.aerial-scenery-v1.vegetation-geometry.v1",
        "status": "SOURCE_GEOMETRY_VALIDATED_INTEGRATION_AND_STEREO_QA_PENDING",
        "blender_version": bpy.app.version_string,
        "source_blend_sha256": sha256(blend_path),
        "runtime_fbx_sha256": sha256(fbx_path),
        "atlas_sha256": sha256(atlas_path),
        "atlas_report_sha256": sha256(atlas_report_path),
        "objects": reports,
        "all_one_material": all(item["material_slots"] == 1 for item in reports),
        "all_vertex_colored": all(item["color_attributes"] == 1 for item in reports),
        "all_grounded_within_0p5m_terrain_intersection": all(
            item["ground_error_m"] <= 0.5 for item in reports
        ),
        "all_wood_and_fixed_card_geometry_non_overconnected": all(item["overconnected_edges"] == 0 for item in reports),
        "all_fixed_orientation_alpha_test_cards": all(
            item["fixed_orientation_card_clusters"] > 0 and item["alpha_clip"] and not item["alpha_blend"]
            for item in reports
        ),
        "all_tier0_sources_in_quality_band": all(1000 <= value <= 3000 for value in tier0_triangles),
        "all_tier1_sources_in_quality_band": all(250 <= value <= 800 for value in tier1_triangles),
        "regional_archetypes": ["cottonwood", "willow_riparian", "general_deciduous", "sparse_conifer", "shrub"],
        "ordinary_vertical_billboards": 0,
        "camera_facing_geometry": 0,
        "per_view_budget_contract": per_view_budget,
        "runtime_contract": {
            "one_gameobject_per_tree": False,
            "draw_api": "three editor-baked per-tier meshes serialized in one prefab",
            "material_count": 1,
            "alpha_mode": "clip",
            "broad_alpha_blend": False,
            "maximum_simultaneous_source_draws": 3,
            "preferred_simultaneous_source_draws": 3,
        },
    }
    report_path.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")

    if args.write_unity_meta:
        write_default_meta(blend_path, LOGICAL_BLEND, "Aerial Scenery V1 project-authored Blender vegetation source")
        write_model_meta(fbx_path)
        write_default_meta(report_path, LOGICAL_REPORT, "Aerial Scenery V1 deterministic vegetation geometry report")

    previews: list[dict] = []
    if args.preview_dir:
        previews = render_previews(objects, Path(args.preview_dir).resolve())
        preview_manifest = Path(args.preview_dir).resolve() / "preview_manifest.json"
        preview_manifest.write_text(
            json.dumps({"schema": "questflight.aerial-scenery-v1.vegetation-preview.v1", "renders": previews}, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
            newline="\n",
        )

    print(json.dumps({
        "status": "AUTHORED",
        "blend": str(blend_path),
        "fbx": str(fbx_path),
        "triangles": triangle_by_name,
        "per_view_worst_case_triangles": per_view_budget["worst_case_triangles"],
        "previews": len(previews),
    }, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
