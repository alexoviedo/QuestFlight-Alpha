#if UNITY_EDITOR
// Keep this fixture under Assets/Editor so Unity's implicit Editor test assembly discovers it.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace QuestFlightLab.Tests.EditMode
{
    public class ProductionEnvironmentEditModeTests
    {
        [Test]
        public void AuthoredPrefabExistsAndPassesItsFullContract()
        {
            WithInstance(root =>
            {
                ProductionEnvironmentRoot contract = root.GetComponent<ProductionEnvironmentRoot>();
                Assert.That(contract, Is.Not.Null);
                Assert.That(contract.TryValidateContract(out string report), Is.True, report);
                Assert.That(contract.bakeToolVersion, Is.EqualTo("standalone_environment_art_v2"));
                Assert.That(contract.bakeUtc, Is.Not.Empty);
            });
        }

        [Test]
        public void HierarchyIsExplicitAndContainsNoRuntimeWorldConstructionProviders()
        {
            WithInstance(root =>
            {
                Assert.That(root.transform.Find("NearProductionZone_4km/USGS_Terrain"), Is.Not.Null);
                Assert.That(root.transform.Find("NearProductionZone_4km/AirportContext"), Is.Not.Null);
                Assert.That(root.transform.Find("MidContextZone_12km/USGS_Terrain"), Is.Not.Null);
                Assert.That(root.transform.Find("MidContextZone_12km/OSM_Context"), Is.Not.Null);
                Assert.That(root.transform.Find("ImmutableFarTerrain_24km"), Is.Not.Null);
                Assert.That(root.transform.Find("AuthoritativeRunwaySystem"), Is.Not.Null);
                Assert.That(root.transform.Find("EssentialWater"), Is.Not.Null);
                Assert.That(HasComponentNamed(root, "SceneryModeController"), Is.False);
                Assert.That(HasComponentNamed(root, "MeshSceneryProvider"), Is.False);
                Assert.That(HasComponentNamed(root, "SplatSceneryProvider"), Is.False);
                Assert.That(HasComponentNamed(root, "RealKbduEnvironmentStatus"), Is.False,
                    "The prefab must contain baked geometry, not a runtime builder status.");
            });
        }

        [Test]
        public void RuntimeMarkerHasNoConstructionOrPerFrameMutationEntryPoint()
        {
            MethodInfo[] methods = typeof(ProductionEnvironmentRoot).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
            string[] forbidden = { "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable" };
            foreach (string method in forbidden)
                Assert.That(methods.Any(item => item.Name == method), Is.False, method + " must not construct or mutate the production world");
            Assert.That(methods.Any(item => item.Name == "Awake"), Is.True, "Awake is validation/reporting only");
        }

        [Test]
        public void FarTerrainIsOneImmutableUsgsRendererWithoutLodOrCameraCulling()
        {
            WithInstance(root =>
            {
                ProductionEnvironmentRoot contract = root.GetComponent<ProductionEnvironmentRoot>();
                Transform far = contract.immutableFarTerrain;
                Assert.That(far.GetComponentsInChildren<MeshRenderer>(true).Length, Is.EqualTo(1));
                Assert.That(far.GetComponentsInChildren<MeshFilter>(true).Length, Is.EqualTo(1));
                Assert.That(far.GetComponentsInChildren<LODGroup>(true), Is.Empty);
                Assert.That(HasComponentNamed(far.gameObject, "RealKbduBatchDistanceCuller"), Is.False);
                Mesh mesh = contract.farTerrainMesh.sharedMesh;
                Assert.That(contract.farTerrainSourceVertexCount, Is.EqualTo(mesh.vertexCount));
                Assert.That(contract.farTerrainSourceIndexCount, Is.EqualTo((int)mesh.GetIndexCount(0)));
                Assert.That(contract.farTerrainBakedTopologyHash, Is.EqualTo(contract.farTerrainSourceTopologyHash));
                Assert.That(contract.farTerrainSmoothingPassCount, Is.EqualTo(2));
                Matrix4x4 before = contract.farTerrainMesh.transform.localToWorldMatrix;
                GameObject cameraObject = new GameObject("IndependentCameraSweep");
                try
                {
                    cameraObject.AddComponent<Camera>();
                    for (int yaw = -180; yaw <= 180; yaw += 15) cameraObject.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    Assert.That(contract.farTerrainMesh.sharedMesh, Is.SameAs(mesh));
                    Assert.That(contract.farTerrainMesh.transform.localToWorldMatrix, Is.EqualTo(before));
                }
                finally
                {
                    Object.DestroyImmediate(cameraObject);
                }
            });
        }

        [Test]
        public void RunwayUsesOneFaaSurfaceOneColliderAndControlledDepthMarkings()
        {
            WithInstance(root =>
            {
                ProductionEnvironmentRoot contract = root.GetComponent<ProductionEnvironmentRoot>();
                Assert.That(contract.runwayLengthMeters, Is.EqualTo(1249.68f).Within(3f));
                Assert.That(contract.runwayWidthMeters, Is.EqualTo(22.86f).Within(0.05f));
                Assert.That(contract.runwayGradeDeltaMeters, Is.EqualTo(-2.919f).Within(0.05f));
                Assert.That(contract.measuredRunwayToTerrainMaximumGapMeters, Is.LessThanOrEqualTo(0.025f));
                Assert.That(contract.measuredMarkingToRunwayMaximumGapMeters, Is.LessThanOrEqualTo(0.003f));
                Assert.That(contract.measuredCollisionSurfaceDisagreementMeters, Is.LessThanOrEqualTo(0.0001f));
                Assert.That(contract.runwayCollisionSurface.sharedMesh, Is.SameAs(contract.runwayPavement.sharedMesh));
                Renderer[] runwayRenderers = contract.authoritativeRunwaySystem.GetComponentsInChildren<Renderer>(true);
                Assert.That(runwayRenderers.Length, Is.EqualTo(2), "Only pavement and one combined marking renderer are allowed.");
                Assert.That(contract.bakedRunwayThresholdStripeQuadCount, Is.EqualTo(12));
                Assert.That(contract.bakedRunwayMarkingQuadCount, Is.EqualTo(26));
                Assert.That(contract.runwayMarkings.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3, Is.EqualTo(52));
                Assert.That(contract.runwayMarkings.sharedMaterial.shader.name, Is.EqualTo("QuestFlightLab/Production Runway Marking"));
                Assert.That(contract.runwayMarkings.sharedMaterial.renderQueue, Is.LessThan((int)RenderQueue.Transparent));
            });
        }

        [Test]
        public void EssentialWaterRetainsOnlyBoulderReservoirAndOpaqueStableShoreInterface()
        {
            WithInstance(root =>
            {
                ProductionEnvironmentRoot contract = root.GetComponent<ProductionEnvironmentRoot>();
                Assert.That(contract.retainedWaterBodyCount, Is.EqualTo(1));
                Assert.That(contract.discardedMinorWaterFeatureCount, Is.GreaterThan(100));
                Assert.That(contract.essentialWater.GetComponentsInChildren<MeshRenderer>(true).Length, Is.EqualTo(2),
                    "One surface and one natural shoreline bank are expected.");
                Assert.That(contract.boulderReservoirSurface.name, Does.Contain("BoulderReservoir"));
                Assert.That(contract.boulderReservoirShoreBank.name, Does.Contain("ShoreBank"));
                Material water = contract.boulderReservoirSurface.GetComponent<Renderer>().sharedMaterial;
                Assert.That(water.shader.name, Is.EqualTo(ProductionEnvironmentRoot.ProductionWaterShaderName));
                Assert.That(water.renderQueue, Is.LessThan((int)RenderQueue.Transparent));
                Assert.That(water.GetFloat("_ZWrite"), Is.GreaterThan(0.5f));
                Assert.That(contract.boulderReservoirShorelineClosed, Is.True);
                Assert.That(contract.boulderReservoirSmoothedShorelineVertexCount,
                    Is.EqualTo(contract.boulderReservoirSourceShorelineVertexCount * 2));
                Assert.That(contract.boulderReservoirShorelineSelfIntersectionCount, Is.Zero);
                AssertBoundaryIsOneClosedNonSelfIntersectingLoop(contract.boulderReservoirSurface.sharedMesh);
                Assert.That(contract.minimumWaterTerrainSeparationMeters,
                    Is.GreaterThanOrEqualTo(ProductionEnvironmentRoot.MinimumAcceptedWaterTerrainSeparationMeters));
                Assert.That(contract.essentialWater.GetComponentsInChildren<LODGroup>(true), Is.Empty);
            });
        }

        [Test]
        public void MacroMapIsUniqueMipmappedAnisotropicAndAstcForAndroid()
        {
            GameObject prefab = LoadPrefab();
            ProductionEnvironmentRoot contract = prefab.GetComponent<ProductionEnvironmentRoot>();
            Assert.That(contract.uniqueMacroAlbedo, Is.Not.Null);
            Assert.That(contract.uniqueMacroAlbedo.width, Is.EqualTo(2048));
            Assert.That(contract.uniqueMacroAlbedo.height, Is.EqualTo(2048));
            Assert.That(contract.macroRepeatedTileSimilarity, Is.LessThan(0.985f));
            string path = AssetDatabase.GetAssetPath(contract.uniqueMacroAlbedo);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear));
            Assert.That(importer.anisoLevel, Is.GreaterThanOrEqualTo(4));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True);
            Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_6x6));
            string absolute = Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
            Assert.That(File.Exists(absolute), Is.True);
        }

        [Test]
        public void PrefabStaysInsideQuestPlanningBudgets()
        {
            WithInstance(root =>
            {
                ProductionEnvironmentBudget budget = root.GetComponent<ProductionEnvironmentRoot>().CalculateBudget();
                Assert.That(budget.rendererCount, Is.LessThanOrEqualTo(ProductionEnvironmentRoot.MaximumRenderers));
                Assert.That(budget.triangleCount, Is.LessThanOrEqualTo(ProductionEnvironmentRoot.MaximumVisibleTriangles));
                Assert.That(budget.materialCount, Is.LessThanOrEqualTo(ProductionEnvironmentRoot.MaximumMaterials));
            });
        }

        [Test]
        public void ProductionContextRejectsMalformedRoadAndDoesNotDuplicateMacroLandcover()
        {
            WithInstance(root =>
            {
                ProductionEnvironmentRoot contract = root.GetComponent<ProductionEnvironmentRoot>();
                Assert.That(contract.rejectedMalformedGroundBatchCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(contract.omittedDuplicatedLandcoverBatchCount, Is.GreaterThan(0));
                string[] rendererNames = root.GetComponentsInChildren<Renderer>(true).Select(renderer => renderer.name).ToArray();
                Assert.That(rendererNames.Any(name => name.Contains("RealContext_0_road_asphalt_021")), Is.False);
                Assert.That(rendererNames.Any(name => name.IndexOf("_landcover_", StringComparison.OrdinalIgnoreCase) >= 0), Is.False);
            });
        }

        [Test]
        public void ProductionPrefabContainsNoRuntimeWorldBuilderComponent()
        {
            GameObject prefab = LoadPrefab();
            Assert.That(HasComponentNamed(prefab, "KbduInspiredWorldBuilder"), Is.False);
            Assert.That(HasComponentNamed(prefab, "RealKbduEnvironmentBuilder"), Is.False);
            Assert.That(HasComponentNamed(prefab, "QuestFirstViewRuntimeRepair"), Is.False);
        }

        [Test]
        public void ProductionShadersHaveNoCompilerErrorsOrFallbackAssignment()
        {
            Shader ground = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Resources/QuestFlightLab/Environment/Shaders/ProductionMacroGround.shader");
            Shader marking = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Resources/QuestFlightLab/Environment/Shaders/ProductionRunwayMarking.shader");
            Assert.That(ground, Is.Not.Null);
            Assert.That(marking, Is.Not.Null);
            Assert.That(ground.isSupported, Is.True);
            Assert.That(marking.isSupported, Is.True);
            Assert.That(ShaderUtil.GetShaderMessages(ground)
                .Where(item => string.Equals(item.severity.ToString(), "Error", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.message), Is.Empty);
            Assert.That(ShaderUtil.GetShaderMessages(marking)
                .Where(item => string.Equals(item.severity.ToString(), "Error", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.message), Is.Empty);
            GameObject prefab = LoadPrefab();
            ProductionEnvironmentRoot contract = prefab.GetComponent<ProductionEnvironmentRoot>();
            Assert.That(contract.nearTerrainMeshes[0].GetComponent<Renderer>().sharedMaterial.shader, Is.SameAs(ground));
            Assert.That(contract.runwayMarkings.sharedMaterial.shader, Is.SameAs(marking));
        }

        private static void AssertBoundaryIsOneClosedNonSelfIntersectingLoop(Mesh mesh)
        {
            int[] triangles = mesh.triangles;
            System.Collections.Generic.Dictionary<(int, int), int> edgeCounts =
                new System.Collections.Generic.Dictionary<(int, int), int>();
            for (int index = 0; index < triangles.Length; index += 3)
            {
                CountEdge(edgeCounts, triangles[index], triangles[index + 1]);
                CountEdge(edgeCounts, triangles[index + 1], triangles[index + 2]);
                CountEdge(edgeCounts, triangles[index + 2], triangles[index]);
            }
            var boundary = edgeCounts.Where(pair => pair.Value == 1).Select(pair => pair.Key).ToArray();
            Assert.That(boundary.Length, Is.GreaterThanOrEqualTo(3));
            var adjacency = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<int>>();
            foreach ((int first, int second) in boundary)
            {
                AddNeighbor(adjacency, first, second);
                AddNeighbor(adjacency, second, first);
            }
            Assert.That(adjacency.Values.All(neighbors => neighbors.Count == 2), Is.True, "Water boundary must be a closed manifold loop.");
            System.Collections.Generic.List<int> ordered = new System.Collections.Generic.List<int> { adjacency.Keys.First() };
            int previous = -1;
            int current = ordered[0];
            do
            {
                int next = adjacency[current][0] == previous ? adjacency[current][1] : adjacency[current][0];
                if (next == ordered[0]) break;
                ordered.Add(next);
                previous = current;
                current = next;
            } while (ordered.Count <= adjacency.Count);
            Assert.That(ordered.Count, Is.EqualTo(adjacency.Count), "Water boundary must contain exactly one closed loop.");
            Vector3[] vertices = mesh.vertices;
            for (int first = 0; first < ordered.Count; first++)
            {
                int firstNext = (first + 1) % ordered.Count;
                Vector2 a = new Vector2(vertices[ordered[first]].x, vertices[ordered[first]].z);
                Vector2 b = new Vector2(vertices[ordered[firstNext]].x, vertices[ordered[firstNext]].z);
                for (int second = first + 1; second < ordered.Count; second++)
                {
                    int secondNext = (second + 1) % ordered.Count;
                    if (firstNext == second || secondNext == first || (first == 0 && secondNext == 0)) continue;
                    Vector2 c = new Vector2(vertices[ordered[second]].x, vertices[ordered[second]].z);
                    Vector2 d = new Vector2(vertices[ordered[secondNext]].x, vertices[ordered[secondNext]].z);
                    Assert.That(SegmentsProperlyIntersect(a, b, c, d), Is.False, "Smoothed water boundary self-intersects.");
                }
            }
        }

        private static void CountEdge(System.Collections.Generic.IDictionary<(int, int), int> counts, int first, int second)
        {
            var edge = first < second ? (first, second) : (second, first);
            counts.TryGetValue(edge, out int count);
            counts[edge] = count + 1;
        }

        private static void AddNeighbor(
            System.Collections.Generic.IDictionary<int, System.Collections.Generic.List<int>> adjacency,
            int vertex,
            int neighbor)
        {
            if (!adjacency.TryGetValue(vertex, out System.Collections.Generic.List<int> neighbors))
            {
                neighbors = new System.Collections.Generic.List<int>();
                adjacency.Add(vertex, neighbors);
            }
            neighbors.Add(neighbor);
        }

        private static bool SegmentsProperlyIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float abC = Cross2D(b - a, c - a);
            float abD = Cross2D(b - a, d - a);
            float cdA = Cross2D(d - c, a - c);
            float cdB = Cross2D(d - c, b - c);
            const float epsilon = 0.0001f;
            return ((abC > epsilon && abD < -epsilon) || (abC < -epsilon && abD > epsilon)) &&
                   ((cdA > epsilon && cdB < -epsilon) || (cdA < -epsilon && cdB > epsilon));
        }

        private static float Cross2D(Vector2 first, Vector2 second) => first.x * second.y - first.y * second.x;

        private static bool HasComponentNamed(GameObject root, string typeName) =>
            root.GetComponentsInChildren<MonoBehaviour>(true).Any(component =>
                component != null && string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal));

        private static GameObject LoadPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProductionEnvironmentRoot.PrefabAssetPath);
            Assert.That(prefab, Is.Not.Null, "Run Quest Flight Lab/Production/Bake Production Environment Prefab first.");
            return prefab;
        }

        private static void WithInstance(Action<GameObject> assertion)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadPrefab());
            try
            {
                assertion(instance);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
#endif
