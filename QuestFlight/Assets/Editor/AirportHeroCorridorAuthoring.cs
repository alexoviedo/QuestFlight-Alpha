using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Editor
{
    /// <summary>
    /// Editor-only deterministic compiler for the serialized airport hero corridor.
    /// No runtime bootstrap or repair path exists.
    /// </summary>
    public static class AirportHeroCorridorAuthoring
    {
        private const string EnvironmentPath =
            "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        private const string SurfacePath =
            "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset";
        private const string ReportPath =
            "Assets/Production/Environment/AirportHeroCorridorV1/Data/AirportHeroCorridorGenerationReportV1.json";
        private const string ModelFolder =
            "Assets/Production/Environment/AirportHeroCorridorV1/Models";
        private const string GeneratedFolder =
            "Assets/Production/Environment/AirportHeroCorridorV1/Generated";
        private const string MaterialPath =
            "Assets/Production/Environment/AirportCoreV4/Materials/AirportCoreHangarKitAtlas.mat";

        [Serializable]
        private sealed class GenerationReport
        {
            public AssetRecord[] assets = Array.Empty<AssetRecord>();
        }

        [Serializable]
        private sealed class AssetRecord
        {
            public string asset_id;
            public string address;
            public long source_osm_id;
            public string family;
            public float[] position_xz_m;
            public int footprint_vertex_count;
            public bool wall_winding_normalized;
            public string roof_style;
            public float maximum_footprint_error_m;
            public LodRecord[] lods = Array.Empty<LodRecord>();
        }

        [Serializable]
        private sealed class LodRecord
        {
            public int lod;
            public int triangles;
        }

        [MenuItem("QuestFlight/Production/Author Airport Hero Corridor V1")]
        public static void Author()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Directory.CreateDirectory(Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", GeneratedFolder)));

            TextAsset reportAsset = LoadRequired<TextAsset>(ReportPath);
            GenerationReport report = JsonUtility.FromJson<GenerationReport>(reportAsset.text);
            if (report?.assets == null ||
                report.assets.Length != AirportHeroCorridorRoot.ExpectedBuildingCount)
                throw new InvalidOperationException(
                    $"Airport hero corridor generation report must contain " +
                    $"{AirportHeroCorridorRoot.ExpectedBuildingCount} assets.");
            if (report.assets.Any(item => item == null || string.IsNullOrWhiteSpace(item.asset_id) ||
                    item.position_xz_m == null || item.position_xz_m.Length != 2 ||
                    item.lods == null || item.lods.Length != 3))
                throw new InvalidOperationException("Airport hero corridor generation record is incomplete.");
            if (report.assets.Any(item =>
                    item.footprint_vertex_count < 3 ||
                    !item.wall_winding_normalized ||
                    item.maximum_footprint_error_m > 0.01f))
                throw new InvalidOperationException(
                    "Airport hero corridor requires outward-facing walls and <= 1 cm footprint error.");
            if (report.assets.Any(item =>
                    string.Equals(item.family, "residential", StringComparison.Ordinal) &&
                    !string.Equals(item.roof_style, "exact_footprint_gable",
                        StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    "Every residential takeoff-corridor building requires an exact-footprint pitched roof.");

            ProductionTerrainSurfaceAsset surface = LoadRequired<ProductionTerrainSurfaceAsset>(SurfacePath);
            Mesh corridorMesh = BuildCombinedCorridor(report.assets, surface, 0);

            GameObject environment = PrefabUtility.LoadPrefabContents(EnvironmentPath);
            try
            {
                AirportCoreBenchmarkRoot benchmark = environment
                    .GetComponentsInChildren<AirportCoreBenchmarkRoot>(true).Single();
                Transform content = benchmark.benchmarkContent;
                if (content == null)
                    throw new InvalidOperationException("Airport benchmark content binding is missing.");
                Transform prior = FindDescendant(content, AirportHeroCorridorRoot.GameObjectName);
                if (prior != null)
                    UnityEngine.Object.DestroyImmediate(prior.gameObject);

                Material material = LoadRequired<Material>(MaterialPath);
                GameObject rootObject = new GameObject(AirportHeroCorridorRoot.GameObjectName);
                rootObject.transform.SetParent(content, false);
                AirportHeroCorridorRoot root = rootObject.AddComponent<AirportHeroCorridorRoot>();
                LODGroup group = rootObject.AddComponent<LODGroup>();
                Renderer renderer = CreateStaticRenderer(
                    rootObject.transform, "AirportHeroCorridor_Hero",
                    corridorMesh, material, true);
                // A single stable representation avoids airport-building shape/scale
                // popping. The row is culled once it is below useful projected size.
                group.SetLODs(new[] { new LOD(0.008f, new[] { renderer }) });
                group.fadeMode = LODFadeMode.None;
                group.animateCrossFading = false;
                group.RecalculateBounds();

                root.corridorLodGroup = group;
                root.sourceManifest = reportAsset;
                root.representedAddresses = report.assets.Select(item => item.address).ToArray();
                root.representedOsmIds = report.assets.Select(item => item.source_osm_id).ToArray();
                root.lod0Triangles = report.assets.Sum(item => item.lods.Single(row => row.lod == 0).triangles);
                root.lod1Triangles = report.assets.Sum(item => item.lods.Single(row => row.lod == 1).triangles);
                root.lod2Triangles = report.assets.Sum(item => item.lods.Single(row => row.lod == 2).triangles);
                root.usesExistingSharedAtlas = true;
                root.genericRepresentationsExcluded = true;
                benchmark.airportHeroCorridor = group;
                if (!root.TryValidate(out string rootReport))
                    throw new InvalidOperationException(rootReport);

                ProductionEnvironmentRoot environmentContract =
                    environment.GetComponent<ProductionEnvironmentRoot>();
                if (environmentContract == null)
                    throw new InvalidOperationException("Production environment contract is missing.");
                ProductionEnvironmentBudget budget = environmentContract.CalculateBudget();
                environmentContract.bakedRendererCount = budget.rendererCount;
                environmentContract.bakedTriangleCount = budget.triangleCount;
                environmentContract.bakedMaterialCount = budget.materialCount;
                environmentContract.bakeToolVersion = "photoreal_aerial_surface_v1";

                PhotorealAerialSurfaceRoot surfaceContract =
                    environment.GetComponentInChildren<PhotorealAerialSurfaceRoot>(true);
                if (surfaceContract == null)
                    throw new InvalidOperationException("Photoreal aerial-surface contract is missing.");
                Renderer[] surfaceBuildings = surfaceContract.buildingSurfaceRenderers
                    .Where(item => item != null && item.enabled && item.gameObject.activeInHierarchy)
                    .ToArray();
                surfaceContract.bakedBuildingDrawCount = surfaceBuildings.Length;
                surfaceContract.bakedBuildingTriangleCount = surfaceBuildings.Sum(item =>
                {
                    Mesh mesh = item.GetComponent<MeshFilter>()?.sharedMesh;
                    return mesh == null ? 0 : Enumerable.Range(0, mesh.subMeshCount)
                        .Sum(submesh => (int)(mesh.GetIndexCount(submesh) / 3));
                });

                Renderer[] benchmarkRenderers =
                    benchmark.benchmarkContent.GetComponentsInChildren<Renderer>(true);
                benchmark.manifest.serializedRendererCount = benchmarkRenderers.Length;
                benchmark.manifest.serializedTriangleCount = benchmarkRenderers.Sum(item =>
                {
                    Mesh mesh = item.GetComponent<MeshFilter>()?.sharedMesh;
                    return mesh == null ? 0 : Enumerable.Range(0, mesh.subMeshCount)
                        .Sum(submesh => (int)(mesh.GetIndexCount(submesh) / 3));
                });
                benchmark.manifest.serializedMaterialCount = benchmarkRenderers
                    .SelectMany(item => item.sharedMaterials ?? Array.Empty<Material>())
                    .Where(item => item != null).Distinct().Count();
                EditorUtility.SetDirty(root);
                EditorUtility.SetDirty(benchmark);
                EditorUtility.SetDirty(benchmark.manifest);
                EditorUtility.SetDirty(surfaceContract);
                EditorUtility.SetDirty(environmentContract);
                PrefabUtility.SaveAsPrefabAsset(environment, EnvironmentPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(environment);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[QuestFlight][AirportHeroCorridorV1] Authored {report.assets.Length} " +
                      "footprint-faithful buildings " +
                      "as one shared-material renderer per active LOD.");
        }

        private static Mesh BuildCombinedCorridor(
            IReadOnlyList<AssetRecord> assets,
            ProductionTerrainSurfaceAsset surface,
            int lod)
        {
            List<CombineInstance> instances = new List<CombineInstance>();
            foreach (AssetRecord record in assets.OrderBy(item => item.source_osm_id))
            {
                string modelPath = $"{ModelFolder}/{record.asset_id}_LOD{lod}.glb";
                GameObject source = LoadRequired<GameObject>(modelPath);
                Mesh mesh = CombineImportedModel(source, $"{record.asset_id}_LOD{lod}_Imported");
                float x = record.position_xz_m[0];
                float z = record.position_xz_m[1];
                Matrix4x4 placement = Matrix4x4.Translate(new Vector3(x, Ground(surface, x, z), z));
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    instances.Add(new CombineInstance
                    {
                        mesh = mesh,
                        subMeshIndex = submesh,
                        transform = placement
                    });
            }
            Mesh combined = new Mesh
            {
                name = "AirportHeroCorridor_LOD" + lod,
                indexFormat = IndexFormat.UInt32
            };
            combined.CombineMeshes(instances.ToArray(), true, true, false);
            combined.RecalculateBounds();
            if (combined.normals == null || combined.normals.Length != combined.vertexCount)
                combined.RecalculateNormals();
            if (combined.tangents == null || combined.tangents.Length != combined.vertexCount)
                combined.RecalculateTangents();
            return SaveMesh(combined, $"{GeneratedFolder}/AirportHeroCorridor_LOD{lod}.asset");
        }

        private static Mesh CombineImportedModel(GameObject source, string name)
        {
            MeshFilter[] filters = source.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.sharedMesh != null).ToArray();
            if (filters.Length == 0)
                throw new InvalidOperationException("No imported model meshes found for " + name);
            List<CombineInstance> instances = new List<CombineInstance>();
            foreach (MeshFilter filter in filters)
            {
                Matrix4x4 matrix = source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int submesh = 0; submesh < filter.sharedMesh.subMeshCount; submesh++)
                    instances.Add(new CombineInstance
                    {
                        mesh = filter.sharedMesh,
                        subMeshIndex = submesh,
                        transform = matrix
                    });
            }
            Mesh mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(instances.ToArray(), true, true, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Renderer CreateStaticRenderer(
            Transform parent, string name, Mesh mesh, Material material, bool shadows)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            MeshFilter filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = shadows;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            renderer.scaleInLightmap = 0f;
            GameObjectUtility.SetStaticEditorFlags(child,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic);
            return renderer;
        }

        private static float Ground(ProductionTerrainSurfaceAsset surface, float x, float z)
        {
            if (!surface.TrySample(x, z, out ProductionTerrainSample sample))
                throw new InvalidOperationException($"Terrain sampling failed at ({x:0.0}, {z:0.0}).");
            return sample.heightMeters;
        }

        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(child.name, name, StringComparison.Ordinal))
                    return child;
            return null;
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException($"Required asset is missing: {path}");
            return asset;
        }
    }
}
