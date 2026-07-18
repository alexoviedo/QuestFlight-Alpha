#!/usr/bin/env python3
"""Author the project-owned airport-core vegetation hero cluster in Blender 5.x."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
import sys
from pathlib import Path

import bpy
from mathutils import Vector


TOOL_DIR = Path(__file__).resolve().parent
LOGICAL_MODEL = "Assets/Production/Environment/AirportCoreV4/Vegetation/Models/AirportCoreVegetationCluster.fbx"
LOGICAL_BLEND = "Assets/ArtSource/Blender/KBDU/VegetationHeroCluster.blend"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def stable_guid(path: str) -> str:
    return hashlib.sha256(("questflight-v4:" + path).encode()).hexdigest()[:32]


def generic_meta(path: Path, logical_path: str, note: str) -> None:
    Path(str(path) + ".meta").write_text(
        f"fileFormatVersion: 2\nguid: {stable_guid(logical_path)}\nDefaultImporter:\n"
        f"  externalObjects: {{}}\n  userData: {note}\n  assetBundleName:\n  assetBundleVariant:\n",
        encoding="utf-8", newline="\n")


def model_meta(path: Path) -> None:
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
  userData: Airport Core V4 project-authored static vegetation cluster; one shared material slot
  assetBundleName:
  assetBundleVariant:
""", encoding="utf-8", newline="\n")


class MeshBuilder:
    def __init__(self) -> None:
        self.vertices: list[tuple[float, float, float]] = []
        self.colors: list[tuple[float, float, float, float]] = []
        self.faces: list[tuple[int, ...]] = []
        self.face_uvs: list[tuple[tuple[float, float], ...]] = []
        self.face_smooth: list[bool] = []
        self.card_faces = 0
        self.solid_parts = 0

    def add_vertex(self, value: Vector, color: tuple[float, float, float, float]) -> int:
        self.vertices.append((value.x, value.y, value.z))
        self.colors.append(color)
        return len(self.vertices) - 1

    def add_cylinder(
        self,
        start: Vector,
        end: Vector,
        start_radius: float,
        end_radius: float,
        segments: int,
        tint: tuple[float, float, float, float],
    ) -> None:
        direction = end - start
        length = direction.length
        if length < 1e-4:
            return
        direction.normalize()
        reference = Vector((0.0, 0.0, 1.0)) if abs(direction.z) < 0.88 else Vector((1.0, 0.0, 0.0))
        axis_x = direction.cross(reference).normalized()
        axis_y = direction.cross(axis_x).normalized()
        ring0: list[int] = []
        ring1: list[int] = []
        for index in range(segments):
            angle = math.tau * index / segments
            radial = axis_x * math.cos(angle) + axis_y * math.sin(angle)
            ring0.append(self.add_vertex(start + radial * start_radius, tint))
            ring1.append(self.add_vertex(end + radial * end_radius, tint))
        for index in range(segments):
            nxt = (index + 1) % segments
            self.faces.append((ring0[index], ring0[nxt], ring1[nxt], ring1[index]))
            u0, u1 = index / segments * 0.125, (index + 1) / segments * 0.125
            self.face_uvs.append(((u0, 0.02), (u1, 0.02), (u1, 0.98), (u0, 0.98)))
            self.face_smooth.append(True)
        center0 = self.add_vertex(start, tint)
        center1 = self.add_vertex(end, tint)
        for index in range(segments):
            nxt = (index + 1) % segments
            self.faces.append((center0, ring0[nxt], ring0[index]))
            self.face_uvs.append(((0.0625, 0.5), (0.12, 0.94), (0.005, 0.94)))
            self.face_smooth.append(False)
            self.faces.append((center1, ring1[index], ring1[nxt]))
            self.face_uvs.append(((0.0625, 0.5), (0.005, 0.06), (0.12, 0.06)))
            self.face_smooth.append(False)
        self.solid_parts += 1

    def add_card(
        self,
        center: Vector,
        right: Vector,
        up: Vector,
        uv_rect: tuple[float, float, float, float],
        tint: tuple[float, float, float, float],
    ) -> None:
        right_half, up_half = right * 0.5, up * 0.5
        indices = (
            self.add_vertex(center - right_half - up_half, tint),
            self.add_vertex(center + right_half - up_half, tint),
            self.add_vertex(center + right_half + up_half, tint),
            self.add_vertex(center - right_half + up_half, tint),
        )
        u0, v0, u1, v1 = uv_rect
        self.faces.append(indices)
        self.face_uvs.append(((u0, v0), (u1, v0), (u1, v1), (u0, v1)))
        self.face_smooth.append(False)
        self.card_faces += 1


UV_RECTS = {
    # PNG top-half tiles are V 0.5..1.0 in Blender UV space.
    "cottonwood_a": (0.128, 0.505, 0.558, 0.995),
    "cottonwood_b": (0.567, 0.505, 0.995, 0.995),
    "deciduous": (0.128, 0.005, 0.558, 0.495),
    "shrub": (0.567, 0.005, 0.995, 0.495),
}


def oriented_card(builder: MeshBuilder, center: Vector, width: float, height: float, yaw: float,
                  species: str, tint: tuple[float, float, float, float], horizontal: bool = False) -> None:
    if horizontal:
        # A shallow compound tilt preserves top-down canopy coverage without
        # producing the conspicuous edge-on horizontal lines of a flat card.
        right = Vector((math.cos(yaw) * width, math.sin(yaw) * width, -width * 0.08))
        up = Vector((-math.sin(yaw) * height, math.cos(yaw) * height, height * 0.20))
    else:
        right = Vector((math.cos(yaw) * width, math.sin(yaw) * width, 0.0))
        up = Vector((0.0, 0.0, height))
    builder.add_card(center, right, up, UV_RECTS[species], tint)


def add_tree(builder: MeshBuilder, rng: random.Random, origin: Vector, species: str, lod: int,
             height: float, crown_radius: float, variant: float) -> None:
    if species == "shrub":
        add_shrub_group(builder, rng, origin, lod, crown_radius)
        return
    trunk_color = (0.90 + variant * 0.04, 0.86 + variant * 0.03, 0.80, 1.0)
    leaf_color = (0.90 + variant * 0.03, 0.94, 0.88 + variant * 0.04, 1.0)
    if lod == 2:
        builder.add_cylinder(origin, origin + Vector((0, 0, height * 0.64)), crown_radius * 0.055,
                             crown_radius * 0.025, 4, trunk_color)
        center = origin + Vector((0, 0, height * 0.69))
        for yaw in (0.0, math.pi / 3.0, math.pi * 2.0 / 3.0):
            oriented_card(builder, center, crown_radius * 1.92, height * 0.62, yaw, species, leaf_color)
        oriented_card(builder, origin + Vector((0, 0, height * 0.88)), crown_radius * 1.72,
                      crown_radius * 1.72, 0.35, species, leaf_color, horizontal=True)
        return

    trunk_segments = 10 if lod == 0 else 7
    branch_count = 11 if lod == 0 else 5
    card_count = 58 if lod == 0 else 21
    trunk_top = origin + Vector((variant * 0.18, -variant * 0.12, height * 0.73))
    builder.add_cylinder(origin, trunk_top, crown_radius * 0.075, crown_radius * 0.028,
                         trunk_segments, trunk_color)
    branch_tips: list[Vector] = []
    for branch in range(branch_count):
        theta = math.tau * branch / branch_count + rng.uniform(-0.32, 0.32) + variant
        elevation = height * rng.uniform(0.45, 0.78)
        radial = crown_radius * rng.uniform(0.43, 0.92)
        start = origin + Vector((variant * 0.10, 0, elevation * rng.uniform(0.72, 0.90)))
        tip = origin + Vector((math.cos(theta) * radial, math.sin(theta) * radial,
                               elevation + rng.uniform(0.7, height * 0.20)))
        builder.add_cylinder(start, tip, crown_radius * 0.030, crown_radius * 0.010,
                             6 if lod == 0 else 4, trunk_color)
        branch_tips.append(tip)
    for index in range(card_count):
        theta = rng.random() * math.tau
        radius = math.sqrt(rng.random()) * crown_radius
        vertical = height * (0.56 + rng.random() * 0.38)
        if branch_tips and index < len(branch_tips):
            center = branch_tips[index] + Vector((rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.5), rng.uniform(-0.3, 0.8)))
        else:
            center = origin + Vector((math.cos(theta) * radius, math.sin(theta) * radius, vertical))
        width = crown_radius * rng.uniform(0.48, 0.78)
        card_height = height * rng.uniform(0.10, 0.18)
        oriented_card(builder, center, width, card_height, theta + rng.uniform(-0.6, 0.6), species, leaf_color)
        if lod == 0 and index % 7 == 0:
            oriented_card(builder, center + Vector((0,0,card_height * 0.18)), width * 0.82, width * 0.82,
                          theta, species, leaf_color, horizontal=True)
    # Explicit top card keeps the canopy stable from the airport-core aerial QA view.
    oriented_card(builder, origin + Vector((0,0,height * 0.91)), crown_radius * 1.55, crown_radius * 1.55,
                  variant + 0.2, species, leaf_color, horizontal=True)


def add_shrub_group(builder: MeshBuilder, rng: random.Random, origin: Vector, lod: int, radius: float) -> None:
    count = 3 if lod < 2 else 2
    for member in range(count):
        center = origin + Vector(((member - 1) * radius * 0.68, (member % 2) * radius * 0.40, 0.0))
        height = radius * (0.82 + member * 0.10)
        if lod < 2:
            builder.add_cylinder(center, center + Vector((0,0,height * 0.52)), radius * 0.045,
                                 radius * 0.018, 5 if lod == 0 else 4, (0.82,0.78,0.70,1.0))
        vertical_cards = 7 if lod == 0 else 4 if lod == 1 else 2
        for card in range(vertical_cards):
            yaw = math.tau * card / vertical_cards + member * 0.31
            oriented_card(builder, center + Vector((0,0,height * 0.56)), radius * 1.05, height,
                          yaw, "shrub", (0.90,0.93,0.87,1.0))
        oriented_card(builder, center + Vector((0,0,height * 0.83)), radius * 0.95, radius * 0.95,
                      member * 0.4, "shrub", (0.90,0.93,0.87,1.0), horizontal=True)


def create_mesh_object(name: str, lod: int, material: bpy.types.Material) -> tuple[bpy.types.Object, dict]:
    builder = MeshBuilder()
    rng = random.Random(91073 + lod * 101)
    add_tree(builder, rng, Vector((-4.1, -0.8, 0.0)), "cottonwood_a", lod, 14.8, 3.9, -0.22)
    add_tree(builder, rng, Vector((5.0, 1.1, 0.0)), "cottonwood_b", lod, 13.4, 3.6, 0.28)
    add_tree(builder, rng, Vector((0.4, 6.0, 0.0)), "deciduous", lod, 10.2, 3.5, 0.10)
    add_tree(builder, rng, Vector((1.0, -5.2, 0.0)), "shrub", lod, 2.0, 2.0, 0.0)

    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(builder.vertices, [], builder.faces)
    mesh.update(calc_edges=True)
    uv_layer = mesh.uv_layers.new(name="UVMap")
    color_layer = mesh.color_attributes.new(name="COLOR_0", type="BYTE_COLOR", domain="CORNER")
    for polygon_index, polygon in enumerate(mesh.polygons):
        polygon.use_smooth = builder.face_smooth[polygon_index]
        uvs = builder.face_uvs[polygon_index]
        for local_index, loop_index in enumerate(polygon.loop_indices):
            uv_layer.data[loop_index].uv = uvs[local_index]
            color_layer.data[loop_index].color = builder.colors[mesh.loops[loop_index].vertex_index]
    validation_changed = mesh.validate(verbose=False, clean_customdata=False)
    mesh.update(calc_edges=True)

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(material)
    obj["questflight_role"] = "airport_core_vegetation_hero_cluster"
    obj["lod_index"] = lod
    obj["members"] = "cottonwood_a,cottonwood_b,deciduous,shrub_group"
    obj["camera_facing_billboard"] = False
    obj["expected_material_slots"] = 1

    triangles = sum(max(0, len(polygon.vertices) - 2) for polygon in mesh.polygons)
    xs = [vertex.co.x for vertex in mesh.vertices]
    ys = [vertex.co.y for vertex in mesh.vertices]
    zs = [vertex.co.z for vertex in mesh.vertices]
    edge_face_counts = {edge.index: 0 for edge in mesh.edges}
    edge_index_by_key = {tuple(sorted(edge.vertices)): edge.index for edge in mesh.edges}
    for polygon in mesh.polygons:
        for edge_key in polygon.edge_keys:
            edge_index = edge_index_by_key.get(tuple(sorted(edge_key)))
            if edge_index is not None:
                edge_face_counts[edge_index] += 1
    boundary_edges = sum(1 for count in edge_face_counts.values() if count == 1)
    overconnected_edges = sum(1 for count in edge_face_counts.values() if count > 2)
    report = {
        "name": name,
        "lod": lod,
        "vertices": len(mesh.vertices),
        "triangles": triangles,
        "faces": len(mesh.polygons),
        "material_slots": len(obj.material_slots),
        "uv_layers": len(mesh.uv_layers),
        "color_attributes": len(mesh.color_attributes),
        "bounds_min": [min(xs), min(ys), min(zs)],
        "bounds_max": [max(xs), max(ys), max(zs)],
        "ground_error_m": abs(min(zs)),
        "negative_scale": any(value < 0 for value in obj.scale),
        "mesh_validation_changed": bool(validation_changed),
        "expected_open_card_boundary_edges": boundary_edges,
        "overconnected_nonmanifold_edges": overconnected_edges,
        "alpha_card_faces": builder.card_faces,
        "closed_solid_parts": builder.solid_parts,
    }
    return obj, report


def create_material(atlas_path: Path) -> bpy.types.Material:
    image = bpy.data.images.load(str(atlas_path), check_existing=True)
    material = bpy.data.materials.new("AirportCoreVegetation_Atlas")
    material.use_nodes = True
    material.diffuse_color = (0.44, 0.62, 0.36, 1.0)
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    for node in list(nodes):
        nodes.remove(node)
    output = nodes.new("ShaderNodeOutputMaterial")
    principled = nodes.new("ShaderNodeBsdfPrincipled")
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = image
    links.new(texture.outputs["Color"], principled.inputs["Base Color"])
    links.new(texture.outputs["Alpha"], principled.inputs["Alpha"])
    links.new(principled.outputs["BSDF"], output.inputs["Surface"])
    principled.inputs["Roughness"].default_value = 0.78
    if hasattr(material, "surface_render_method"):
        material.surface_render_method = "DITHERED"
    return material


def render_previews(objects: list[bpy.types.Object], preview_dir: Path) -> list[dict]:
    preview_dir.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 960
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.world.color = (0.16, 0.23, 0.30)

    bpy.ops.mesh.primitive_plane_add(size=80, location=(0,0,-0.03))
    ground = bpy.context.object
    ground.name = "PREVIEW_Ground"
    ground_material = bpy.data.materials.new("PREVIEW_GroundMaterial")
    ground_material.diffuse_color = (0.18,0.23,0.12,1)
    ground.data.materials.append(ground_material)

    bpy.ops.object.light_add(type="SUN", location=(0,0,25))
    sun = bpy.context.object
    sun.name = "PREVIEW_Sun"
    sun.rotation_euler = (math.radians(33.066), 0, math.radians(135))
    sun.data.energy = 3.2
    sun.data.angle = math.radians(1.2)

    bpy.ops.object.light_add(type="AREA", location=(-5,-8,12))
    fill = bpy.context.object
    fill.name = "PREVIEW_Fill"
    fill.data.energy = 700
    fill.data.shape = "DISK"
    fill.data.size = 9

    bpy.ops.object.camera_add(location=(24,-30,16))
    camera = bpy.context.object
    camera.name = "PREVIEW_Camera"
    scene.camera = camera

    def aim(target: Vector) -> None:
        camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()

    records = []
    views = [
        (0, "lod0_front", Vector((24,-30,16)), Vector((0,1,6))),
        (0, "lod0_aerial", Vector((17,-19,31)), Vector((0,1,4))),
        (1, "lod1_front", Vector((24,-30,16)), Vector((0,1,6))),
        (2, "lod2_front", Vector((24,-30,16)), Vector((0,1,6))),
    ]
    for active_lod, name, location, target in views:
        for index, obj in enumerate(objects):
            obj.hide_render = index != active_lod
        camera.location = location
        aim(target)
        path = preview_dir / f"vegetation_{name}.png"
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        records.append({"name": name, "path": str(path), "sha256": sha256(path)})
    return records


def update_manifests(root: Path, blend_path: Path, fbx_path: Path, geometry_report: dict) -> None:
    cost_path = root / "Water/Reports/AirportCoreWaterVegetation_CostReport.json"
    if cost_path.exists():
        cost = json.loads(cost_path.read_text(encoding="utf-8"))
        cost["vegetation"]["geometry_report_pending"] = False
        cost["vegetation"]["triangles_by_lod"] = {
            f"lod{record['lod']}": record["triangles"] for record in geometry_report["objects"]
        }
        cost["vegetation"]["vertices_by_lod"] = {
            f"lod{record['lod']}": record["vertices"] for record in geometry_report["objects"]
        }
        cost_path.write_text(json.dumps(cost, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")

    source_path = root / "Water/Reports/AirportCoreWaterVegetation_SourceManifest.json"
    if source_path.exists():
        source = json.loads(source_path.read_text(encoding="utf-8"))
        source["records"] = [record for record in source["records"]
                             if record["path"] not in (LOGICAL_BLEND, LOGICAL_MODEL)]
        source["records"].extend([
            {"path": LOGICAL_BLEND, "sha256": sha256(blend_path),
             "source": "Reproducible Blender 5.x project source", "license": "Project-authored"},
            {"path": LOGICAL_MODEL, "sha256": sha256(fbx_path),
             "source": "Exported from VegetationHeroCluster.blend", "license": "Project-authored"},
            {"path": "tools/kbdu_airport_core_v4/water_vegetation/author_vegetation_blender.py",
             "sha256": sha256(Path(__file__)), "source": "Deterministic Blender authoring script",
             "license": "Project-authored"},
        ])
        source["records"] = sorted(source["records"], key=lambda record: record["path"])
        source_path.write_text(json.dumps(source, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")

    generation_path = root / "Water/Reports/AirportCoreWaterVegetation_GenerationReport.json"
    if generation_path.exists():
        generation = json.loads(generation_path.read_text(encoding="utf-8"))
        generation["vegetation"]["geometry"] = geometry_report
        generation_path.write_text(json.dumps(generation, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")


def parse_args() -> argparse.Namespace:
    args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--atlas", required=True)
    parser.add_argument("--blend", required=True)
    parser.add_argument("--fbx", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument("--preview-dir")
    parser.add_argument("--asset-root", required=True, help="AirportCoreV4 output root")
    return parser.parse_args(args)


def main() -> int:
    args = parse_args()
    atlas_path = Path(args.atlas).resolve()
    blend_path = Path(args.blend).resolve()
    fbx_path = Path(args.fbx).resolve()
    report_path = Path(args.report).resolve()
    asset_root = Path(args.asset_root).resolve()
    for path in (atlas_path,):
        if not path.exists():
            raise FileNotFoundError(path)
    blend_path.parent.mkdir(parents=True, exist_ok=True)
    fbx_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.parent.mkdir(parents=True, exist_ok=True)

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0

    material = create_material(atlas_path)
    objects: list[bpy.types.Object] = []
    reports: list[dict] = []
    for lod in range(3):
        obj, report = create_mesh_object(f"AirportCoreVegetationCluster_LOD{lod}", lod, material)
        objects.append(obj)
        reports.append(report)

    for obj in bpy.context.selected_objects:
        obj.select_set(False)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]

    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path), compress=True)
    bpy.ops.file.make_paths_relative()
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path), compress=True)
    generic_meta(blend_path, LOGICAL_BLEND, "Airport Core V4 project-authored Blender source")

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
        use_tspace=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="RELATIVE",
        embed_textures=False,
    )
    model_meta(fbx_path)

    ratios = {
        "lod1_to_lod0": reports[1]["triangles"] / reports[0]["triangles"],
        "lod2_to_lod0": reports[2]["triangles"] / reports[0]["triangles"],
    }
    geometry_report = {
        "schema": "questflight.airport-core-v4.vegetation-geometry.v1",
        "status": "OFFLINE_GEOMETRY_VALIDATED_VISUAL_AND_STEREO_QA_PENDING",
        "blender_version": bpy.app.version_string,
        "source_blend_sha256": sha256(blend_path),
        "runtime_fbx_sha256": sha256(fbx_path),
        "atlas_sha256": sha256(atlas_path),
        "objects": reports,
        "lod_triangle_ratios": ratios,
        "cluster_members": ["cottonwood_a", "cottonwood_b", "deciduous", "shrub_group"],
        "one_material_per_lod": all(record["material_slots"] == 1 for record in reports),
        "all_grounded": all(record["ground_error_m"] <= 0.002 for record in reports),
        "no_negative_scale": all(not record["negative_scale"] for record in reports),
        "no_overconnected_nonmanifold_edges": all(record["overconnected_nonmanifold_edges"] == 0 for record in reports),
        "open_edges_policy": "Expected only on authored alpha cards; all trunk/branch parts are capped solids.",
    }
    report_path.write_text(json.dumps(geometry_report, indent=2, sort_keys=True) + "\n", encoding="utf-8", newline="\n")
    generic_meta(report_path,
                 "Assets/Production/Environment/AirportCoreV4/Vegetation/AirportCoreVegetation_GeometryReport.json",
                 "Airport Core V4 deterministic geometry report")
    update_manifests(asset_root, blend_path, fbx_path, geometry_report)

    preview_records = []
    if args.preview_dir:
        preview_records = render_previews(objects, Path(args.preview_dir).resolve())
        preview_manifest = Path(args.preview_dir).resolve() / "preview_manifest.json"
        preview_manifest.write_text(json.dumps({"renders": preview_records}, indent=2, sort_keys=True) + "\n",
                                    encoding="utf-8", newline="\n")

    print(json.dumps({"status": "AUTHORED", "blend": str(blend_path), "fbx": str(fbx_path),
                      "triangles": {f"lod{r['lod']}": r["triangles"] for r in reports},
                      "ratios": ratios, "previews": len(preview_records)}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
