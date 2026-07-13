using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace QuestFlightLab.EditorTests
{
    /// <summary>
    /// Editor-assembly discovery bridge for this project, which still uses predefined Unity
    /// assemblies rather than test asmdefs. Detailed assertions also live under Assets/Tests.
    /// </summary>
    public sealed class ProductionEnvironmentEditorTests
    {
        [Test]
        public void AuthoredPrefabPassesHierarchyAlignmentWaterAndBudgetContract()
        {
            GameObject prefab = LoadPrefab();
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                ProductionEnvironmentRoot contract = instance.GetComponent<ProductionEnvironmentRoot>();
                Assert.That(contract, Is.Not.Null);
                Assert.That(contract.TryValidateContract(out string report), Is.True, report);
                Assert.That(contract.measuredRunwayToTerrainMaximumGapMeters, Is.LessThanOrEqualTo(0.025f));
                Assert.That(contract.measuredMarkingToRunwayMaximumGapMeters, Is.LessThanOrEqualTo(0.003f));
                Assert.That(contract.runwayCollisionSurface.sharedMesh, Is.SameAs(contract.runwayPavement.sharedMesh));
                Assert.That(contract.bakedRunwayThresholdStripeQuadCount, Is.EqualTo(12));
                Assert.That(contract.bakedRunwayMarkingQuadCount, Is.EqualTo(26));
                Assert.That(contract.runwayMarkings.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3, Is.EqualTo(52));
                Assert.That(contract.retainedWaterBodyCount, Is.EqualTo(1));
                Assert.That(contract.boulderReservoirSurface, Is.Not.Null);
                Assert.That(contract.immutableFarTerrain.GetComponentsInChildren<LODGroup>(true), Is.Empty);
                Assert.That(HasComponentNamed(contract.immutableFarTerrain.gameObject, "RealKbduBatchDistanceCuller"), Is.False);
                Assert.That(HasComponentNamed(instance, "SceneryModeController"), Is.False);
                ProductionEnvironmentBudget budget = contract.CalculateBudget();
                Assert.That(budget.rendererCount, Is.LessThanOrEqualTo(ProductionEnvironmentRoot.MaximumRenderers));
                Assert.That(budget.triangleCount, Is.LessThanOrEqualTo(ProductionEnvironmentRoot.MaximumVisibleTriangles));
                Assert.That(budget.materialCount, Is.LessThanOrEqualTo(ProductionEnvironmentRoot.MaximumMaterials));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void VisualCorrectionBakeRejectsMalformedContextAndPreservesFarTopology()
        {
            GameObject contractObject = LoadPrefab();
            ProductionEnvironmentRoot contract = contractObject.GetComponent<ProductionEnvironmentRoot>();
            string[] rendererNames = contractObject.GetComponentsInChildren<Renderer>(true).Select(renderer => renderer.name).ToArray();
            Assert.That(contract.rejectedMalformedGroundBatchCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(contract.omittedDuplicatedLandcoverBatchCount, Is.GreaterThan(0));
            Assert.That(rendererNames.Any(name => name.Contains("RealContext_0_road_asphalt_021")), Is.False);
            Assert.That(rendererNames.Any(name => name.IndexOf("_landcover_", StringComparison.OrdinalIgnoreCase) >= 0), Is.False);
            Mesh far = contract.farTerrainMesh.sharedMesh;
            Assert.That(contract.farTerrainSourceVertexCount, Is.EqualTo(far.vertexCount));
            Assert.That(contract.farTerrainSourceIndexCount, Is.EqualTo((int)far.GetIndexCount(0)));
            Assert.That(contract.farTerrainSourceTopologyHash, Is.EqualTo(contract.farTerrainBakedTopologyHash));
            Assert.That(contract.farTerrainSmoothingPassCount, Is.EqualTo(2));
            Assert.That(far.name, Is.EqualTo("Terrain_Far24km_Immutable_USGS_Mesh"));
        }

        [Test]
        public void SmoothedReservoirHasOneClosedNonSelfIntersectingBoundary()
        {
            ProductionEnvironmentRoot contract = LoadPrefab().GetComponent<ProductionEnvironmentRoot>();
            Assert.That(contract.boulderReservoirShorelineClosed, Is.True);
            Assert.That(contract.boulderReservoirSmoothedShorelineVertexCount,
                Is.EqualTo(contract.boulderReservoirSourceShorelineVertexCount * 2));
            Assert.That(contract.boulderReservoirShorelineSelfIntersectionCount, Is.Zero);
            AssertBoundaryIsOneClosedNonSelfIntersectingLoop(contract.boulderReservoirSurface.sharedMesh);
        }

        [Test]
        public void MacroTextureAndMicrodetailImportsAreQuestReady()
        {
            ProductionEnvironmentRoot contract = LoadPrefab().GetComponent<ProductionEnvironmentRoot>();
            Assert.That(contract.uniqueMacroAlbedo, Is.Not.Null);
            Assert.That(contract.uniqueMacroAlbedo.width, Is.EqualTo(1024));
            Assert.That(contract.macroRepeatedTileSimilarity, Is.LessThan(0.985f));
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(contract.uniqueMacroAlbedo));
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear));
            Assert.That(importer.anisoLevel, Is.GreaterThanOrEqualTo(4));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True);
            Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_6x6));
        }

        [Test]
        public void ProductionShadersCompileAndAreBoundWithoutFallback()
        {
            Shader ground = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Resources/QuestFlightLab/Environment/Shaders/ProductionMacroGround.shader");
            Shader marking = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Resources/QuestFlightLab/Environment/Shaders/ProductionRunwayMarking.shader");
            Shader water = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Resources/QuestFlightLab/Environment/Shaders/ProductionStableWater.shader");
            Assert.That(ground, Is.Not.Null);
            Assert.That(marking, Is.Not.Null);
            Assert.That(water, Is.Not.Null);
            Assert.That(ground.isSupported, Is.True);
            Assert.That(marking.isSupported, Is.True);
            Assert.That(water.isSupported, Is.True);
            Assert.That(ShaderUtil.GetShaderMessages(ground).Where(IsError).Select(message => message.message), Is.Empty);
            Assert.That(ShaderUtil.GetShaderMessages(marking).Where(IsError).Select(message => message.message), Is.Empty);
            Assert.That(ShaderUtil.GetShaderMessages(water).Where(IsError).Select(message => message.message), Is.Empty);
            ProductionEnvironmentRoot contract = LoadPrefab().GetComponent<ProductionEnvironmentRoot>();
            Assert.That(contract.nearTerrainMeshes[0].GetComponent<Renderer>().sharedMaterial.shader, Is.SameAs(ground));
            Assert.That(contract.runwayMarkings.sharedMaterial.shader, Is.SameAs(marking));
            Assert.That(contract.boulderReservoirSurface.GetComponent<Renderer>().sharedMaterial.shader, Is.SameAs(water));
            Assert.That(contract.boulderReservoirSurface.GetComponent<Renderer>().sharedMaterial.renderQueue,
                Is.LessThan((int)RenderQueue.Transparent));
        }

        private static bool IsError(ShaderMessage message) =>
            string.Equals(message.severity.ToString(), "Error", StringComparison.OrdinalIgnoreCase);

        private static void AssertBoundaryIsOneClosedNonSelfIntersectingLoop(Mesh mesh)
        {
            int[] triangles = mesh.triangles;
            Dictionary<(int, int), int> edgeCounts = new Dictionary<(int, int), int>();
            for (int index = 0; index < triangles.Length; index += 3)
            {
                CountEdge(edgeCounts, triangles[index], triangles[index + 1]);
                CountEdge(edgeCounts, triangles[index + 1], triangles[index + 2]);
                CountEdge(edgeCounts, triangles[index + 2], triangles[index]);
            }
            (int, int)[] boundary = edgeCounts.Where(pair => pair.Value == 1).Select(pair => pair.Key).ToArray();
            Assert.That(boundary.Length, Is.GreaterThanOrEqualTo(3));
            Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();
            foreach ((int first, int second) in boundary)
            {
                AddNeighbor(adjacency, first, second);
                AddNeighbor(adjacency, second, first);
            }
            Assert.That(adjacency.Values.All(neighbors => neighbors.Count == 2), Is.True, "Water boundary must be a closed manifold loop.");
            List<int> ordered = new List<int> { adjacency.Keys.First() };
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

        private static void CountEdge(IDictionary<(int, int), int> counts, int first, int second)
        {
            (int, int) edge = first < second ? (first, second) : (second, first);
            counts.TryGetValue(edge, out int count);
            counts[edge] = count + 1;
        }

        private static void AddNeighbor(IDictionary<int, List<int>> adjacency, int vertex, int neighbor)
        {
            if (!adjacency.TryGetValue(vertex, out List<int> neighbors))
            {
                neighbors = new List<int>();
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
            Assert.That(prefab, Is.Not.Null);
            return prefab;
        }
    }
}
