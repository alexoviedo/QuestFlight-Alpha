using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class AerialSceneryContractEditModeTests
    {
        private const string EnvironmentPath =
            "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        private const string TerrainMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat";
        private const string HandoffPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Canopy/" +
            "ProductionCanopySurfaceLayer_TerrainMapHandoff_R8.png";
        private const string VegetationAtlasPath =
            "Assets/Production/Environment/AerialSceneryV1/Vegetation/Textures/AerialVegetation_Atlas.png";
        private const string VegetationSurfacePath =
            "Assets/Production/Environment/AerialSceneryV1/Vegetation/Textures/AerialVegetation_Surface.png";
        private const string SurfaceFeaturesPath =
            "Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_Lidar_SurfaceFeatures_RGBA8.png";
        private const string SurfaceNormalPath =
            "Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_Lidar_SurfaceNormal_RGBA8.png";
        private const string CanopyNormalPath =
            "Assets/Production/Environment/AerialSceneryV1/Lidar/KBDU_Lidar_CanopyNormalOcclusion_RGBA8.png";

        [Test]
        public void RootHasNoPerFrameRuntimeMutationHook()
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                 BindingFlags.DeclaredOnly;
            foreach (string method in new[] { "Update", "LateUpdate", "FixedUpdate", "OnRenderObject" })
                Assert.That(typeof(AerialSceneryRoot).GetMethod(method, flags), Is.Null,
                    "The serialized scenery root must remain validation-only: " + method);
        }

        [Test]
        public void FourTierContractAcceptsAnExplicitlyAuthoredBoundedHierarchy()
        {
            GameObject rootObject = new GameObject(AerialSceneryRoot.GameObjectName);
            AerialSceneryManifest manifest = ScriptableObject.CreateInstance<AerialSceneryManifest>();
            Texture2D texture = new Texture2D(1, 1);
            Mesh near = new Mesh();
            Mesh mid = new Mesh();
            Mesh baseline = new Mesh();
            Mesh patch = new Mesh();
            Mesh baselinePatch = new Mesh();
            try
            {
                AerialSceneryRoot root = rootObject.AddComponent<AerialSceneryRoot>();
                root.tier0AirportHero = Child(rootObject.transform, "Tier0_AirportHero");
                root.tier1NearScenery = Child(rootObject.transform, "Tier1_Near");
                root.tier2MidScenery = Child(rootObject.transform, "Tier2_Mid");
                root.tier3FarScenery = Child(rootObject.transform, "Tier3_Far");
                root.vegetationRoot = Child(root.tier1NearScenery, "Vegetation");
                root.buildingRoot = Child(root.tier2MidScenery, "Buildings");
                root.terrainRoot = Child(root.tier3FarScenery, "Terrain");
                root.authoritativeAirportPatch = Child(root.terrainRoot, "AirportPatch")
                    .gameObject.AddComponent<MeshFilter>();
                root.authoritativeAirportPatch.sharedMesh = patch;
                root.baselineAirportPatchMesh = baselinePatch;
                root.authoritativeNearTerrain = Child(root.terrainRoot, "NearTerrain")
                    .gameObject.AddComponent<MeshFilter>();
                root.authoritativeNearTerrain.sharedMesh = near;
                root.baselineNearTerrainMesh = baseline;
                root.manifest = manifest;
                root.bakedRendererCount = 1;
                root.bakedSelectedTriangleCount = 1;
                root.bakedMaterialCount = 1;
                root.bakeToolVersion = "editmode_fixture";
                root.bakeUtc = "2026-07-18T00:00:00Z";

                manifest.lidarSourceManifest = new TextAsset("{}");
                manifest.terrainCoordinateConversionReport = new TextAsset("{}");
                manifest.terrainConformanceReport = new TextAsset("{}");
                manifest.vegetationSourceManifest = new TextAsset("{}");
                manifest.buildingSourceManifest = new TextAsset("{}");
                manifest.lidarPipelineSha256 = new string('0', 64);
                manifest.vegetationPipelineSha256 = new string('1', 64);
                manifest.buildingPipelineSha256 = new string('2', 64);
                manifest.surfaceFeatures = texture;
                manifest.surfaceNormals = texture;
                manifest.canopyNormals = texture;
                manifest.canopyOcclusion = texture;
                manifest.canopyGeometryHandoff = texture;
                manifest.airportPatchMesh = patch;
                manifest.nearTerrainMesh = near;
                manifest.midTerrainMesh = mid;
                manifest.boundedSourcePointCount = 1;
                manifest.buildingFootprintInflationMaximum = 1f;

                Assert.That(root.TryValidateContract(out string report), Is.True, report);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rootObject);
                UnityEngine.Object.DestroyImmediate(manifest);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(near);
                UnityEngine.Object.DestroyImmediate(mid);
                UnityEngine.Object.DestroyImmediate(baseline);
                UnityEngine.Object.DestroyImmediate(patch);
                UnityEngine.Object.DestroyImmediate(baselinePatch);
            }
        }

        [Test]
        public void ClusterBoxNameIsRejectedFromReplacementBuildingHierarchy()
        {
            GameObject rootObject = new GameObject(AerialSceneryRoot.GameObjectName);
            try
            {
                AerialSceneryRoot root = rootObject.AddComponent<AerialSceneryRoot>();
                root.tier0AirportHero = Child(rootObject.transform, "Tier0");
                root.tier1NearScenery = Child(rootObject.transform, "Tier1");
                root.tier2MidScenery = Child(rootObject.transform, "Tier2");
                root.tier3FarScenery = Child(rootObject.transform, "Tier3");
                root.vegetationRoot = Child(root.tier1NearScenery, "Vegetation");
                root.buildingRoot = Child(root.tier2MidScenery, "Buildings");
                root.terrainRoot = Child(root.tier3FarScenery, "Terrain");
                Child(root.buildingRoot, "cluster_boxes_400m_rejected");

                Assert.That(root.TryValidateContract(out string report), Is.False);
                StringAssert.Contains("cluster-box", report);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void ProductPrefabUsesTheFrozenFourTierRepresentation()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            Assert.That(environment, Is.Not.Null);
            AerialSceneryRoot[] roots = environment.GetComponentsInChildren<AerialSceneryRoot>(true);
            Assert.That(roots, Has.Length.EqualTo(1));
            AerialSceneryRoot root = roots[0];
            Assert.That(root.gameObject.activeSelf, Is.True);
            Assert.That(root.TryValidateContract(out string report), Is.True, report);
            Assert.That(root.disabledLegacyVegetationRoots, Has.Length.EqualTo(5));
            Assert.That(root.disabledLegacyBuildingRoots, Has.Length.EqualTo(20));
            Assert.That(root.disabledLegacyVegetationRoots.Concat(root.disabledLegacyBuildingRoots)
                .All(item => item != null && !item.gameObject.activeSelf), Is.True);
            Assert.That(root.authoritativeNearTerrain.sharedMesh, Is.SameAs(root.manifest.nearTerrainMesh));
            Assert.That(root.baselineNearTerrainMesh, Is.Not.SameAs(root.manifest.nearTerrainMesh));
            Assert.That(root.authoritativeAirportPatch.sharedMesh, Is.SameAs(root.manifest.airportPatchMesh));
            Assert.That(root.baselineAirportPatchMesh, Is.Not.SameAs(root.manifest.airportPatchMesh));

            AerialVegetationBatchRenderer batch = root.vegetationRoot
                .GetComponentsInChildren<AerialVegetationBatchRenderer>(true).Single();
            Assert.That(batch.NonEmptyDrawCount, Is.EqualTo(2));
            Assert.That(batch.FullyInstancedTriangleCount, Is.EqualTo(12860));
            Assert.That(batch.SharedMaterial, Is.Not.Null);
            Assert.That(batch.FixedOrientationOnly, Is.True);
            Assert.That(batch.CameraFacingBehavior, Is.False);
            Assert.That(batch.LodCrossfadeBehavior, Is.False);
            Assert.That(root.vegetationRoot.GetComponentsInChildren<BillboardRenderer>(true), Is.Empty);
            Transform[] vegetationTransforms = root.vegetationRoot.GetComponentsInChildren<Transform>(true);
            Assert.That(vegetationTransforms.Any(item =>
                item.name.IndexOf("instance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                item.name.IndexOf("tree_", StringComparison.OrdinalIgnoreCase) >= 0), Is.False,
                "Vegetation must not create one GameObject per tree instance.");

            PhotorealAerialSurfaceRoot surface =
                environment.GetComponentInChildren<PhotorealAerialSurfaceRoot>(true);
            Assert.That(surface, Is.Not.Null);
            Assert.That(surface.productionCanopySurfaceLayer.GetComponentsInChildren<Renderer>(true),
                Is.Empty,
                "Ordinary canopy is represented by the terrain handoff map, not raised renderers.");
            Assert.That(surface.canopySurfaceRenderers, Is.Empty);
            Assert.That(surface.productionCanopySurfaceLayer.GetComponentsInChildren<LODGroup>(true), Is.Empty);
            Assert.That(surface.productionCanopySurfaceLayer.GetComponentsInChildren<BillboardRenderer>(true), Is.Empty);

            Assert.That(root.buildingRoot.GetComponentsInChildren<MeshRenderer>(true), Has.Length.EqualTo(16));
            Assert.That(root.buildingRoot.GetComponentsInChildren<LODGroup>(true), Is.Empty,
                "Context buildings use fixed, closed footprint shells so their scale and silhouette cannot pop.");

            Material terrain = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
            Assert.That(terrain, Is.Not.Null);
            Assert.That(terrain.GetTexture("_AerialCanopyHandoffMask"),
                Is.SameAs(root.manifest.canopyGeometryHandoff));
            Assert.That(terrain.GetFloat("_AerialCanopyColorStrength"), Is.GreaterThan(0f));
            Assert.That(terrain.GetFloat("_AerialCanopyNormalStrength"), Is.GreaterThan(0f));

            TextureImporter handoff = AssetImporter.GetAtPath(HandoffPath) as TextureImporter;
            Assert.That(handoff, Is.Not.Null);
            Assert.That(handoff.mipmapEnabled, Is.False,
                "The exact Tier0-2 exclusion boundary must not bleed through mips.");
            Assert.That(handoff.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(handoff.GetPlatformTextureSettings("Android").overridden, Is.True);
            foreach (string path in new[] { VegetationAtlasPath, VegetationSurfacePath })
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.mipmapEnabled, Is.True, path);
                Assert.That(importer.streamingMipmaps, Is.True, path);
                Assert.That(importer.GetPlatformTextureSettings("Android").format,
                    Is.EqualTo(TextureImporterFormat.ASTC_6x6), path);
            }
            foreach (string path in new[] { SurfaceFeaturesPath, SurfaceNormalPath, CanopyNormalPath })
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.mipmapEnabled, Is.True, path);
                Assert.That(importer.streamingMipmaps, Is.False,
                    path + " uses a custom world-space UV contract and must remain fully resident.");
                Assert.That(importer.GetPlatformTextureSettings("Android").format,
                    Is.EqualTo(TextureImporterFormat.ASTC_6x6), path);
            }

            ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
            ProductionEnvironmentBudget budget = contract.CalculateBudget();
            Assert.That(contract.bakedRendererCount, Is.EqualTo(budget.rendererCount));
            Assert.That(contract.bakedTriangleCount, Is.EqualTo(budget.triangleCount));
            Assert.That(contract.bakedMaterialCount, Is.EqualTo(budget.materialCount));
        }

        private static Transform Child(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }
    }
}
