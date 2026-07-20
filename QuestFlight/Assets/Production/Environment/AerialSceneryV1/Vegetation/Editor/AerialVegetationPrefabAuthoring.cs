using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Environment.Editor
{
    /// <summary>
    /// Deterministically bakes lidar-derived heterogeneous vegetation placements
    /// into one serialized mesh per near visible tier. The resulting prefab has two
    /// non-empty instanced draws, one shared material, and no per-tree GameObjects.
    /// Tier 2 and Tier 3 are owned by the orthophoto/lidar canopy terrain shading path.
    /// Product-scene placement and legacy-renderer disabling remain lead-owned.
    /// </summary>
    public static class AerialVegetationPrefabAuthoring
    {
        public const string ModelPath =
            "Assets/Production/Environment/AerialSceneryV1/Vegetation/Models/AerialVegetationMultiscale.fbx";
        public const string MaterialPath =
            "Assets/Production/Environment/AerialSceneryV1/Vegetation/Materials/AerialVegetation_AtlasCutout.mat";
        public const string DistributionPath =
            "Assets/Production/Environment/AerialSceneryV1/Vegetation/AerialVegetation_Distribution.json";
        public const string GeneratedFolder =
            "Assets/Production/Environment/AerialSceneryV1/Vegetation/Generated";
        public const string PrefabPath = GeneratedFolder + "/AerialVegetationRoot.prefab";

        private const int ExpectedDrawCount = 2;
        private const int ExpectedSourcePlacementCount = 22;
        private const long VisibleTriangleCeiling = 15184;
        private const string FrozenLidarManifestSha256 =
            "1a08c8c4be14737e86d1a8819ae4cdff3edb58f7f9a2147a23278b366d0f34e5";

        private static readonly string[] ExpectedBatchIds =
        {
            "tier0_editor_baked",
            "tier1_editor_baked"
        };

        private static readonly string[] ExpectedSourceModes =
        {
            "heterogeneous_fixed_orientation_editor_bake",
            "heterogeneous_fixed_orientation_editor_bake"
        };

        private static readonly int[] ExpectedTierPlacementCounts = { 3, 19 };

        private static readonly HashSet<string>[] AllowedSourcesByTier =
        {
            new HashSet<string>(new[]
            {
                "AerialTree_Cottonwood_T0",
                "AerialTree_WillowRiparian_T0",
                "AerialTree_Deciduous_T0",
                "AerialTree_SparseConifer_T0",
                "AerialShrub_Regional_T0"
            }, StringComparer.Ordinal),
            new HashSet<string>(new[]
            {
                "AerialTree_Cottonwood_T1",
                "AerialTree_WillowRiparian_T1",
                "AerialTree_Deciduous_T1",
                "AerialTree_SparseConifer_T1",
                "AerialShrub_Regional_T1"
            }, StringComparer.Ordinal)
        };

        public static readonly string[] LegacyRendererRootsToDisable =
        {
            "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/KBDU_BatchedContext/vegetation_v2_hlod_1500m_xn001_zn001",
            "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/KBDU_BatchedContext/vegetation_v2_hlod_1500m_xn001_zp000",
            "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/KBDU_BatchedContext/vegetation_v2_hlod_1500m_xp000_zn001",
            "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/KBDU_BatchedContext/vegetation_v2_hlod_1500m_xp000_zp000",
            "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/SparseVegetationProduction/CottonwoodCluster_00",
            "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/SparseVegetationProduction/CottonwoodCluster_01",
            "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/SparseVegetationProduction/CottonwoodCluster_02",
            "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/SparseVegetationProduction/CottonwoodCluster_03",
            "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/SparseVegetationProduction/CottonwoodCluster_04",
            "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/SparseVegetationProduction/CottonwoodCluster_05",
            "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/SparseVegetationProduction/CottonwoodCluster_06",
            "ProductionWorldRoot/EnvironmentRoot/ProductionEnvironmentRoot/SparseVegetationProduction/CottonwoodCluster_07"
        };

        [MenuItem("QuestFlight/Aerial Scenery V1/Build Vegetation Prefab")]
        public static void BuildPrefab()
        {
            TextAsset distributionAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(DistributionPath);
            if (distributionAsset == null)
            {
                throw new InvalidOperationException("Validated vegetation distribution is missing: " + DistributionPath);
            }

            DistributionRecord distribution = JsonUtility.FromJson<DistributionRecord>(distributionAsset.text);
            DistributionBatchRecord[] orderedRecords = ValidateDistribution(distribution);

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null || material.shader == null)
            {
                throw new InvalidOperationException("Vegetation shared material is missing: " + MaterialPath);
            }
            if (!material.enableInstancing)
            {
                throw new InvalidOperationException("Vegetation shared material must enable GPU instancing.");
            }
            if (!string.Equals(
                    material.shader.name,
                    "QuestFlight/Aerial Scenery V1/Vegetation Atlas Cutout",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Vegetation material must use the fixed-card alpha-test shader.");
            }

            Dictionary<string, Mesh> sourceMeshes = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<Mesh>()
                .GroupBy(mesh => mesh.name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            if (sourceMeshes.Count == 0)
            {
                throw new InvalidOperationException("No vegetation source meshes were imported from: " + ModelPath);
            }

            EnsureAssetFolder(GeneratedFolder);
            AerialVegetationBatchRenderer.Batch[] runtimeBatches = new AerialVegetationBatchRenderer.Batch[ExpectedDrawCount];
            long[] tierTriangles = new long[ExpectedDrawCount];
            int sourcePlacementCount = 0;
            for (int tier = 0; tier < orderedRecords.Length; tier++)
            {
                DistributionBatchRecord record = orderedRecords[tier];
                Mesh combined = BuildCombinedTierMesh(record, sourceMeshes);
                string assetPath = GeneratedFolder + "/AerialVegetation_Tier" + tier + "_Combined.asset";
                Mesh saved = WriteMeshAsset(assetPath, combined);
                tierTriangles[tier] = TriangleCount(saved);
                sourcePlacementCount += record.instances.Length;
                runtimeBatches[tier] = new AerialVegetationBatchRenderer.Batch(
                    record.batch_id,
                    record.tier,
                    saved,
                    new[] { Matrix4x4.identity },
                    new[] { Vector4.one });
            }

            if (sourcePlacementCount != distribution.instance_count ||
                sourcePlacementCount != ExpectedSourcePlacementCount)
            {
                throw new InvalidOperationException("Serialized source placement count is inconsistent.");
            }

            GameObject root = new GameObject("AerialVegetationRoot_V1");
            int nonEmptyDrawCount;
            long fullyInstancedTriangleCount;
            try
            {
                AerialVegetationBatchRenderer renderer = root.AddComponent<AerialVegetationBatchRenderer>();
                renderer.Configure(material, distributionAsset, runtimeBatches, sourcePlacementCount);
                nonEmptyDrawCount = renderer.NonEmptyDrawCount;
                fullyInstancedTriangleCount = renderer.FullyInstancedTriangleCount;
                if (nonEmptyDrawCount != ExpectedDrawCount)
                {
                    throw new InvalidOperationException(
                        $"Vegetation authored {nonEmptyDrawCount} non-empty draws; expected {ExpectedDrawCount}.");
                }
                if (fullyInstancedTriangleCount > VisibleTriangleCeiling)
                {
                    throw new InvalidOperationException(
                        $"Vegetation authored {fullyInstancedTriangleCount} triangles; ceiling is {VisibleTriangleCeiling}.");
                }
                if (renderer.AuthoredMaterialCount != 1 ||
                    !renderer.FixedOrientationOnly ||
                    renderer.CameraFacingBehavior ||
                    renderer.LodCrossfadeBehavior)
                {
                    throw new InvalidOperationException("Vegetation runtime representation policy mismatch.");
                }

                CreateTierAnchor(root.transform, "Tier0_EditorBakedTrees_0m_500m");
                CreateTierAnchor(root.transform, "Tier1_EditorBakedTrees_500m_1500m");
                CreateTierAnchor(root.transform, "Tier2_OrthophotoLidarCanopyTerrainShading_500m_4000m");
                CreateTierAnchor(root.transform, "Tier3_OrthophotoLidarCanopyTerrainShading_Far");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                $"Authored {PrefabPath}: sourcePlacements={sourcePlacementCount}, " +
                $"nonEmptyDraws={nonEmptyDrawCount}, fullyInstancedTriangles={fullyInstancedTriangleCount}, " +
                $"tierTriangles=[{string.Join(",", tierTriangles)}], materials=1, fixedOrientationOnly=true, " +
                "cameraFacing=false, lodCrossfade=false. Product-scene integration and exact legacy-root disabling " +
                "remain an explicit lead-owned serialized authoring step.");
        }

        private static DistributionBatchRecord[] ValidateDistribution(DistributionRecord distribution)
        {
            if (distribution == null ||
                !string.Equals(
                    distribution.schema,
                    "questflight.aerial-scenery-v1.vegetation-distribution.v1",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Vegetation distribution schema mismatch.");
            }
            if (!string.Equals(distribution.status, "VALIDATED_LIDAR_DERIVATIVE", StringComparison.Ordinal) ||
                !string.Equals(distribution.source_kind, "validated_lidar_manifest", StringComparison.Ordinal) ||
                distribution.input == null ||
                !string.Equals(
                    distribution.input.lidar_manifest_sha256,
                    FrozenLidarManifestSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The product prefab refuses synthetic or unvalidated vegetation distribution input.");
            }
            if (distribution.batch_count != ExpectedDrawCount ||
                distribution.batches == null ||
                distribution.batches.Length != ExpectedDrawCount)
            {
                throw new InvalidOperationException("Vegetation distribution must contain exactly two near editor-baked batches.");
            }

            DistributionBatchRecord[] ordered = new DistributionBatchRecord[ExpectedDrawCount];
            HashSet<string> stableIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (DistributionBatchRecord record in distribution.batches)
            {
                if (record == null || record.tier < 0 || record.tier >= ExpectedDrawCount || ordered[record.tier] != null)
                {
                    throw new InvalidOperationException("Vegetation distribution contains a missing, duplicate, or invalid tier.");
                }
                int tier = record.tier;
                if (!string.Equals(record.batch_id, ExpectedBatchIds[tier], StringComparison.Ordinal) ||
                    !string.Equals(record.source_mode, ExpectedSourceModes[tier], StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Vegetation batch identity or source mode mismatch for tier " + tier + ".");
                }
                if (record.instances == null || record.instances.Length != ExpectedTierPlacementCounts[tier])
                {
                    throw new InvalidOperationException(
                        $"Vegetation tier {tier} must contain exactly {ExpectedTierPlacementCounts[tier]} placements.");
                }
                foreach (DistributionInstanceRecord instance in record.instances)
                {
                    ValidateInstance(instance, tier, stableIds);
                }
                ordered[tier] = record;
            }

            if (ordered.Any(record => record == null) ||
                distribution.instance_count != ExpectedSourcePlacementCount)
            {
                throw new InvalidOperationException("Vegetation distribution does not satisfy the Tier0/Tier1 3/19 placement contract.");
            }
            return ordered;
        }

        private static void ValidateInstance(
            DistributionInstanceRecord instance,
            int tier,
            ISet<string> stableIds)
        {
            if (instance == null ||
                string.IsNullOrWhiteSpace(instance.stable_id) ||
                string.IsNullOrWhiteSpace(instance.archetype) ||
                string.IsNullOrWhiteSpace(instance.source_object) ||
                !AllowedSourcesByTier[tier].Contains(instance.source_object) ||
                !stableIds.Add(instance.stable_id))
            {
                throw new InvalidOperationException("Vegetation placement identity or source object is invalid.");
            }
            if (!IsFinite(instance.x) || !IsFinite(instance.y) || !IsFinite(instance.z) ||
                !IsFinite(instance.yaw_degrees) ||
                !IsFinitePositive(instance.scale_x) ||
                !IsFinitePositive(instance.scale_y) ||
                !IsFinitePositive(instance.scale_z) ||
                !IsUnit(instance.tint_r) || !IsUnit(instance.tint_g) ||
                !IsUnit(instance.tint_b) || !IsUnit(instance.tint_a))
            {
                throw new InvalidOperationException(instance.stable_id + " contains an invalid transform or tint.");
            }
        }

        private static Mesh BuildCombinedTierMesh(
            DistributionBatchRecord record,
            IReadOnlyDictionary<string, Mesh> sourceMeshes)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector4> tangents = new List<Vector4>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color32> colors = new List<Color32>();
            List<int> indices = new List<int>();

            foreach (DistributionInstanceRecord instance in record.instances)
            {
                Mesh source = ResolveMesh(sourceMeshes, instance.source_object);
                if (!source.isReadable)
                {
                    throw new InvalidOperationException(
                        source.name + " is not readable. The deterministic editor bake requires ModelImporter.isReadable.");
                }
                if (source.subMeshCount != 1 || source.GetTopology(0) != MeshTopology.Triangles)
                {
                    throw new InvalidOperationException(source.name + " must contain exactly one triangle submesh.");
                }

                Vector3[] sourceVertices = source.vertices;
                Vector3[] sourceNormals = source.normals;
                Vector4[] sourceTangents = source.tangents;
                Vector2[] sourceUvs = source.uv;
                Color32[] sourceColors = source.colors32;
                if (sourceNormals.Length != sourceVertices.Length ||
                    sourceTangents.Length != sourceVertices.Length ||
                    sourceUvs.Length != sourceVertices.Length ||
                    sourceColors.Length != sourceVertices.Length)
                {
                    throw new InvalidOperationException(
                        source.name + " must import normals, tangents, UV0, and vertex colors for every vertex.");
                }

                Matrix4x4 transform = Matrix4x4.TRS(
                    new Vector3(instance.x, instance.y, instance.z),
                    Quaternion.Euler(0f, instance.yaw_degrees, 0f),
                    new Vector3(instance.scale_x, instance.scale_y, instance.scale_z));
                Matrix4x4 normalTransform = transform.inverse.transpose;
                int vertexOffset = vertices.Count;
                for (int vertexIndex = 0; vertexIndex < sourceVertices.Length; vertexIndex++)
                {
                    vertices.Add(transform.MultiplyPoint3x4(sourceVertices[vertexIndex]));
                    normals.Add(normalTransform.MultiplyVector(sourceNormals[vertexIndex]).normalized);
                    Vector3 tangentDirection = transform.MultiplyVector(
                        new Vector3(
                            sourceTangents[vertexIndex].x,
                            sourceTangents[vertexIndex].y,
                            sourceTangents[vertexIndex].z)).normalized;
                    tangents.Add(new Vector4(
                        tangentDirection.x,
                        tangentDirection.y,
                        tangentDirection.z,
                        sourceTangents[vertexIndex].w));
                    uvs.Add(sourceUvs[vertexIndex]);
                    Color sourceColor = sourceColors[vertexIndex];
                    colors.Add((Color32)new Color(
                        Mathf.Clamp01(sourceColor.r * instance.tint_r),
                        Mathf.Clamp01(sourceColor.g * instance.tint_g),
                        Mathf.Clamp01(sourceColor.b * instance.tint_b),
                        Mathf.Clamp01(sourceColor.a * instance.tint_a)));
                }

                int[] sourceIndices = source.GetIndices(0);
                for (int index = 0; index < sourceIndices.Length; index++)
                {
                    indices.Add(vertexOffset + sourceIndices[index]);
                }
            }

            Mesh combined = new Mesh
            {
                name = "AerialVegetation_Tier" + record.tier + "_Combined",
                indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            combined.SetVertices(vertices);
            combined.SetNormals(normals);
            combined.SetTangents(tangents);
            combined.SetUVs(0, uvs);
            combined.SetColors(colors);
            combined.SetTriangles(indices, 0, false);
            combined.RecalculateBounds();
            return combined;
        }

        private static Mesh WriteMeshAsset(string assetPath, Mesh generated)
        {
            UnityEngine.Object existingObject = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (existingObject == null)
            {
                AssetDatabase.CreateAsset(generated, assetPath);
                return generated;
            }
            Mesh existingMesh = existingObject as Mesh;
            if (existingMesh == null)
            {
                UnityEngine.Object.DestroyImmediate(generated);
                throw new InvalidOperationException("Generated vegetation asset path is not a Mesh: " + assetPath);
            }
            EditorUtility.CopySerialized(generated, existingMesh);
            existingMesh.name = generated.name;
            EditorUtility.SetDirty(existingMesh);
            UnityEngine.Object.DestroyImmediate(generated);
            return existingMesh;
        }

        private static Mesh ResolveMesh(IReadOnlyDictionary<string, Mesh> meshes, string sourceObject)
        {
            if (meshes.TryGetValue(sourceObject, out Mesh exact))
            {
                return exact;
            }
            if (meshes.TryGetValue(sourceObject + "_Mesh", out Mesh dataName))
            {
                return dataName;
            }
            throw new InvalidOperationException("Imported vegetation mesh not found: " + sourceObject);
        }

        private static long TriangleCount(Mesh mesh)
        {
            return mesh == null || mesh.subMeshCount == 0 ? 0L : (long)(mesh.GetIndexCount(0) / 3u);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinitePositive(float value)
        {
            return IsFinite(value) && value > 0f;
        }

        private static bool IsUnit(float value)
        {
            return IsFinite(value) && value >= 0f && value <= 1f;
        }

        private static void CreateTierAnchor(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }
            string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetPath);
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(leaf))
            {
                throw new InvalidOperationException("Cannot create asset folder: " + assetPath);
            }
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        [Serializable]
        private sealed class DistributionRecord
        {
            public string schema;
            public string status;
            public string source_kind;
            public int batch_count;
            public int instance_count;
            public DistributionInputRecord input;
            public DistributionBatchRecord[] batches;
        }

        [Serializable]
        private sealed class DistributionInputRecord
        {
            public string lidar_manifest_sha256;
        }

        [Serializable]
        private sealed class DistributionBatchRecord
        {
            public string batch_id;
            public int tier;
            public string source_mode;
            public DistributionInstanceRecord[] instances;
        }

        [Serializable]
        private sealed class DistributionInstanceRecord
        {
            public string stable_id;
            public string archetype;
            public string source_object;
            public float x;
            public float y;
            public float z;
            public float yaw_degrees;
            public float scale_x;
            public float scale_y;
            public float scale_z;
            public float tint_r;
            public float tint_g;
            public float tint_b;
            public float tint_a;
            public float footprint_radius_major_m;
            public float footprint_radius_minor_m;
            public float footprint_yaw_degrees;
            public float mean_canopy_hag_m;
        }
    }
}
