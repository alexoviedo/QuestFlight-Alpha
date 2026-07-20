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
                Assert.That(contract.retainedWaterBodyCount, Is.GreaterThan(1));
                Assert.That(contract.waterFeatureManifestV2, Is.Not.Null);
                Assert.That(contract.otherWaterFeaturesSurfaceV2, Is.Null);
                Assert.That(contract.otherWaterFeaturesShoreBankV2, Is.Null);
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
        public void BuiltPlayerContractAllowsTheStaticBatchedRunwayMeshIdentityRewrite()
        {
            WithInstance(root =>
            {
                ProductionEnvironmentRoot contract = root.GetComponent<ProductionEnvironmentRoot>();
                Mesh authoredVisualMesh = contract.runwayPavement.sharedMesh;
                Mesh staticBatchedVisualMesh = Object.Instantiate(authoredVisualMesh);
                staticBatchedVisualMesh.name = authoredVisualMesh.name + "_PlayerStaticBatch";
                try
                {
                    contract.runwayPavement.sharedMesh = staticBatchedVisualMesh;

                    Assert.That(contract.TryValidateContract(out string editorReport), Is.False,
                        "Editor validation must retain the exact authored visual/collider mesh identity check.");
                    Assert.That(editorReport, Does.Contain("visual and physical runway must share exactly one mesh asset"));

                    Assert.That(contract.TryValidateBuiltPlayerContract(out string playerReport), Is.True, playerReport);
                    Assert.That(playerReport, Does.Contain("budgetSource=serializedBake"));
                }
                finally
                {
                    contract.runwayPavement.sharedMesh = authoredVisualMesh;
                    Object.DestroyImmediate(staticBatchedVisualMesh);
                }
            });
        }

        [Test]
        public void BuiltPlayerContractUsesSerializedFarTopologyAfterStaticBatchMeshRewrite()
        {
            WithInstance(root =>
            {
                ProductionEnvironmentRoot contract = root.GetComponent<ProductionEnvironmentRoot>();
                Mesh authoredFarMesh = contract.farTerrainMesh.sharedMesh;
                Mesh staticBatchedFarMesh = Object.Instantiate(authoredFarMesh);
                staticBatchedFarMesh.name = "Combined Mesh (root: scene)";
                Vector3[] vertices = staticBatchedFarMesh.vertices;
                Array.Resize(ref vertices, vertices.Length + 1);
                staticBatchedFarMesh.vertices = vertices;
                try
                {
                    contract.farTerrainMesh.sharedMesh = staticBatchedFarMesh;

                    Assert.That(contract.TryValidateContract(out string editorReport), Is.False,
                        "Editor validation must retain the exact authored far-terrain vertex topology check.");
                    Assert.That(editorReport, Does.Contain("far-terrain smoothing changed vertex topology"));

                    Assert.That(contract.TryValidateBuiltPlayerContract(out string playerReport), Is.True, playerReport);
                    Assert.That(playerReport, Does.Contain("budgetSource=serializedBake"));
                }
                finally
                {
                    contract.farTerrainMesh.sharedMesh = authoredFarMesh;
                    Object.DestroyImmediate(staticBatchedFarMesh);
                }
            });
        }

        [Test]
        public void BuiltPlayerContractStillRejectsMissingRunwayGeometryToleranceAndBakeEvidence()
        {
            WithInstance(root =>
            {
                ProductionEnvironmentRoot contract = root.GetComponent<ProductionEnvironmentRoot>();
                Assert.That(contract.TryValidateBuiltPlayerContract(out string baselineReport), Is.True, baselineReport);

                Mesh colliderMesh = contract.runwayCollisionSurface.sharedMesh;
                contract.runwayCollisionSurface.sharedMesh = null;
                Assert.That(contract.TryValidateBuiltPlayerContract(out string missingMeshReport), Is.False);
                Assert.That(missingMeshReport, Does.Contain("physical runway collision mesh is missing"));
                contract.runwayCollisionSurface.sharedMesh = colliderMesh;

                float collisionDisagreement = contract.measuredCollisionSurfaceDisagreementMeters;
                contract.measuredCollisionSurfaceDisagreementMeters = 0.01f;
                Assert.That(contract.TryValidateBuiltPlayerContract(out string toleranceReport), Is.False);
                Assert.That(toleranceReport, Does.Contain("runway collider disagrees with visual mesh"));
                contract.measuredCollisionSurfaceDisagreementMeters = collisionDisagreement;

                int bakedTriangles = contract.bakedTriangleCount;
                contract.bakedTriangleCount = ProductionEnvironmentRoot.MaximumVisibleTriangles + 1;
                Assert.That(contract.TryValidateBuiltPlayerContract(out string budgetReport), Is.False);
                Assert.That(budgetReport, Does.Contain("triangle budget exceeded"));
                contract.bakedTriangleCount = bakedTriangles;

                string bakeToolVersion = contract.bakeToolVersion;
                contract.bakeToolVersion = string.Empty;
                Assert.That(contract.TryValidateBuiltPlayerContract(out string provenanceReport), Is.False);
                Assert.That(provenanceReport, Does.Contain("serialized bake tool version is missing"));
                contract.bakeToolVersion = bakeToolVersion;

                string bakedTopologyHash = contract.farTerrainBakedTopologyHash;
                contract.farTerrainBakedTopologyHash = "player-topology-corruption";
                Assert.That(contract.TryValidateBuiltPlayerContract(out string topologyReport), Is.False);
                Assert.That(topologyReport, Does.Contain("far-terrain smoothing changed triangle identity/order"));
                contract.farTerrainBakedTopologyHash = bakedTopologyHash;
            });
        }

        [Test]
        public void MountainStaticFlagPolicyRemainsStrictInEditorOnly()
        {
            Assert.That(MountainTemporalStabilityProbe.RequiresAuthoredStaticFlag(true), Is.True);
            Assert.That(MountainTemporalStabilityProbe.RequiresAuthoredStaticFlag(false), Is.False);
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
            Assert.That(contract.uniqueMacroAlbedo.width, Is.EqualTo(4096),
                "The bounded mid-zone macro texture is authored at the standalone recovery target resolution");
            Assert.That(contract.macroRepeatedTileSimilarity, Is.LessThan(0.985f));
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(contract.uniqueMacroAlbedo));
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.streamingMipmaps, Is.False,
                "Custom world-space macro UVs require bounded full residency.");
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear));
            Assert.That(importer.anisoLevel, Is.GreaterThanOrEqualTo(4));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True);
            Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_10x10),
                "The 12 km fallback uses ASTC 10x10 to fund four sharper ASTC 6x6 hot quadrants.");
        }

        [Test]
        public void ProductionShadersCompileAndAreBoundWithoutFallback()
        {
            Shader ground = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.shader");
            Shader marking = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Resources/QuestFlightLab/Environment/Shaders/ProductionRunwayMarking.shader");
            Shader reservoir = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Production/Environment/AirportCoreV4/Water/Shaders/KBDUReservoirWaterV4.shader");
            Shader secondaryWater = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Resources/QuestFlightLab/Environment/Shaders/ProductionStableWater.shader");
            Assert.That(ground, Is.Not.Null);
            Assert.That(marking, Is.Not.Null);
            Assert.That(reservoir, Is.Not.Null);
            Assert.That(secondaryWater, Is.Not.Null);
            Assert.That(ground.isSupported, Is.True);
            Assert.That(marking.isSupported, Is.True);
            Assert.That(reservoir.isSupported, Is.True);
            Assert.That(secondaryWater.isSupported, Is.True);
            Assert.That(ShaderUtil.GetShaderMessages(ground).Where(IsError).Select(message => message.message), Is.Empty);
            Assert.That(ShaderUtil.GetShaderMessages(marking).Where(IsError).Select(message => message.message), Is.Empty);
            Assert.That(ShaderUtil.GetShaderMessages(reservoir).Where(IsError).Select(message => message.message), Is.Empty);
            Assert.That(ShaderUtil.GetShaderMessages(secondaryWater).Where(IsError).Select(message => message.message), Is.Empty);
            ProductionEnvironmentRoot contract = LoadPrefab().GetComponent<ProductionEnvironmentRoot>();
            Assert.That(contract.nearTerrainMeshes[0].GetComponent<Renderer>().sharedMaterial.shader, Is.SameAs(ground));
            Assert.That(contract.runwayMarkings.sharedMaterial.shader, Is.SameAs(marking));
            Renderer reservoirRenderer = contract.boulderReservoirSurface.GetComponent<Renderer>();
            Assert.That(reservoirRenderer.enabled, Is.False);
            Assert.That(reservoirRenderer.sharedMaterials, Is.Empty,
                "Orthophoto-only product water must not retain a serialized material dependency.");
            Material reservoirSource = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Production/Environment/AirportCoreV4/Water/Materials/AirportCoreReservoir_V4.mat");
            Assert.That(reservoirSource, Is.Not.Null,
                "The unbound historical water source remains available for provenance and later polish.");
            Assert.That(reservoirSource.shader, Is.SameAs(reservoir));
            Assert.That(reservoirSource.renderQueue, Is.EqualTo((int)RenderQueue.Geometry + 5));
            Assert.That(reservoirSource.passCount, Is.EqualTo(1));
            Assert.That(contract.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null)
                .Any(material => material.shader == secondaryWater), Is.False,
                "The obsolete minor-water mesh shader must not remain bound in the product hierarchy.");
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
