using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Offline compiler for the explicitly serialized airport-close tree tier.
    /// It never enters a player build and never constructs product hierarchy at runtime.
    /// </summary>
    public static class AirportCloseTreeLodAuthoring
    {
        public const string CanopyRoot =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Canopy";
        public const string BinarySourcePath =
            CanopyRoot + "/EditorSource/AirportCloseTreeLods.bytes";
        public const string PlacementManifestPath =
            CanopyRoot + "/ProductionAirportCloseTreeInstances.json";
        public const string SourceManifestPath =
            CanopyRoot + "/AirportCloseTreeLods_SourceManifest.json";
        public const string Lod0AssetPath =
            CanopyRoot + "/Native/AirportCloseTree_LOD0.asset";
        public const string Lod1AssetPath =
            CanopyRoot + "/Native/AirportCloseTree_LOD1.asset";
        public const string Lod2AssetPath =
            CanopyRoot + "/Native/AirportCloseTree_LOD2.asset";
        public const string HeroLeafAtlasPath =
            CanopyRoot + "/Textures/AirportCloseTree_HeroLeaf_RGBA.png";
        public const string ImpostorAtlasPath =
            CanopyRoot + "/Textures/AirportCloseTree_Impostor8_RGBA.png";
        public const string CanopyMaterialPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Materials/" +
            "ProductionCanopySurfaceLayer.mat";
        public const string RuntimeObjectName = "AirportCloseTreeLOD";

        private static readonly byte[] Magic =
            { (byte)'Q', (byte)'F', (byte)'C', (byte)'T', (byte)'I', (byte)'2', 0, 0 };

        [Serializable]
        private sealed class PlacementManifest
        {
            public string schema;
            public int instance_count;
            public Placement[] instances;
        }

        [Serializable]
        private sealed class Placement
        {
            public float x;
            public float y;
            public float z;
            public float height;
            public float crown_diameter;
            public float rotation_degrees;
            public float tint_r;
            public float tint_g;
            public float tint_b;
            public float seed;
        }

        [MenuItem("QuestFlight/Photoreal Aerial Surface V1/Author Airport Close Tree LOD")]
        public static void RunInteractive() => Apply();

        public static void RunBatch()
        {
            try
            {
                Apply();
                Debug.Log("[QuestFlight][AirportCloseTrees] SERIALIZED_INTEGRATION_SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void Apply()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            PhotorealAerialSurfaceNativeMeshAuthoring.CompileAll();
            Dictionary<string, Mesh> meshes = CompileNativeLods();
            Mesh lod0 = meshes["AirportCloseTree_LOD0"];
            Mesh lod1 = meshes["AirportCloseTree_LOD1"];
            Mesh lod2 = meshes["AirportCloseTree_LOD2"];
            TextAsset placementAsset = LoadRequired<TextAsset>(PlacementManifestPath);
            TextAsset sourceAsset = LoadRequired<TextAsset>(SourceManifestPath);
            Material canopyMaterial = LoadRequired<Material>(CanopyMaterialPath);
            Texture2D heroLeafAtlas = LoadRequired<Texture2D>(HeroLeafAtlasPath);
            Texture2D impostorAtlas = LoadRequired<Texture2D>(ImpostorAtlasPath);
            canopyMaterial.enableInstancing = true;
            canopyMaterial.SetTexture("_HeroLeafAtlas", heroLeafAtlas);
            canopyMaterial.SetTexture("_TreeImpostorAtlas", impostorAtlas);

            PlacementManifest manifest =
                JsonUtility.FromJson<PlacementManifest>(placementAsset.text);
            if (manifest == null ||
                manifest.schema != "questflight.airport-close-tree-instances.v1" ||
                manifest.instances == null ||
                manifest.instance_count != manifest.instances.Length ||
                manifest.instances.Length < 100)
                throw new InvalidOperationException("Airport-close placement manifest is invalid.");

            AirportCloseTreeLodRenderer.Instance[] instances =
                BuildInstances(manifest.instances, lod0);
            GameObject environment =
                PrefabUtility.LoadPrefabContents(ProductionEnvironmentRoot.PrefabAssetPath);
            try
            {
                ProductionEnvironmentRoot environmentRoot =
                    environment.GetComponent<ProductionEnvironmentRoot>();
                PhotorealAerialSurfaceRoot surface =
                    environment.GetComponentInChildren<PhotorealAerialSurfaceRoot>(true);
                AerialSceneryRoot aerial =
                    environment.GetComponentInChildren<AerialSceneryRoot>(true);
                if (environmentRoot == null || surface == null || aerial == null ||
                    surface.productionCanopySurfaceLayer == null)
                    throw new InvalidOperationException(
                        "Authored production aerial hierarchy is incomplete.");

                Transform canopy = surface.productionCanopySurfaceLayer;
                Transform previous = canopy.Find(RuntimeObjectName);
                if (previous != null)
                    UnityEngine.Object.DestroyImmediate(previous.gameObject);
                GameObject runtimeObject = new GameObject(RuntimeObjectName);
                runtimeObject.layer = canopy.gameObject.layer;
                runtimeObject.transform.SetParent(canopy, false);
                AirportCloseTreeLodRenderer renderer =
                    runtimeObject.AddComponent<AirportCloseTreeLodRenderer>();
                renderer.Configure(canopyMaterial, lod0, lod1, lod2,
                    placementAsset, sourceAsset, instances);

                Renderer[] staticCanopy = surface.canopySurfaceRenderers
                    .Where(item => item != null).ToArray();
                surface.bakedCanopyDrawCount = staticCanopy.Length;
                surface.bakedCanopyTriangleCount = CountTriangles(staticCanopy);
                ProductionEnvironmentBudget budget = environmentRoot.CalculateBudget();
                environmentRoot.bakedRendererCount = budget.rendererCount;
                environmentRoot.bakedTriangleCount = budget.triangleCount;
                environmentRoot.bakedMaterialCount = budget.materialCount;
                aerial.bakedRendererCount = budget.rendererCount;
                aerial.bakedSelectedTriangleCount = budget.triangleCount;
                aerial.bakedMaterialCount = budget.materialCount;
                aerial.bakedVegetationRendererCount =
                    staticCanopy.Length + renderer.AuthoredMaximumDrawCount;
                aerial.bakedVegetationTriangleCount =
                    surface.bakedCanopyTriangleCount +
                    checked((int)renderer.AuthoredMaximumSelectedTriangleCount);
                aerial.bakedVegetationMaterialCount = 1;

                if (renderer.AuthoredMaximumDrawCount != 3 ||
                    renderer.AuthoredMaximumSelectedTriangleCount <= 0 ||
                    renderer.AuthoredMaximumSelectedTriangleCount > 24000)
                    throw new InvalidOperationException(
                        "Airport-close runtime LOD budget exceeds its authored contract.");
                if (budget.rendererCount > 75 || budget.triangleCount > 650000 ||
                    budget.materialCount > 29)
                    throw new InvalidOperationException(
                        $"Airport-close product budget failed: {budget.rendererCount}/75 draws, " +
                        $"{budget.triangleCount}/650000 triangles, " +
                        $"{budget.materialCount}/29 materials.");

                EditorUtility.SetDirty(canopyMaterial);
                EditorUtility.SetDirty(surface);
                EditorUtility.SetDirty(aerial);
                EditorUtility.SetDirty(environmentRoot);
                EditorUtility.SetDirty(renderer);
                PrefabUtility.SaveAsPrefabAsset(
                    environment, ProductionEnvironmentRoot.PrefabAssetPath);
                Debug.Log(
                    $"[QuestFlight][AirportCloseTrees] instances={instances.Length}, " +
                    $"LOD0={TriangleCount(lod0):N0} tris, LOD1={TriangleCount(lod1):N0} tris, " +
                    $"LOD2={TriangleCount(lod2):N0} tris, " +
                    $"maxSelected={renderer.AuthoredMaximumSelectedTriangleCount:N0}, " +
                    $"product={budget.rendererCount} draws/{budget.triangleCount:N0} tris/" +
                    $"{budget.materialCount} materials.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(environment);
            }

            AssetDatabase.SaveAssets();
        }

        private static Dictionary<string, Mesh> CompileNativeLods()
        {
            TextAsset source = LoadRequired<TextAsset>(BinarySourcePath);
            Dictionary<string, Mesh> result = new Dictionary<string, Mesh>();
            using MemoryStream memory = new MemoryStream(source.bytes, false);
            using BinaryReader reader = new BinaryReader(memory);
            byte[] magic = reader.ReadBytes(8);
            if (!magic.SequenceEqual(Magic))
                throw new InvalidDataException("Airport-close tree source magic mismatch.");
            int version = reader.ReadInt32();
            int meshCount = reader.ReadInt32();
            if (version != 2 || meshCount != 3)
                throw new InvalidDataException("Airport-close tree source version mismatch.");

            for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
            {
                string name = System.Text.Encoding.ASCII
                    .GetString(reader.ReadBytes(32)).TrimEnd('\0');
                int vertexCount = reader.ReadInt32();
                int indexCount = reader.ReadInt32();
                int triangleCount = reader.ReadInt32();
                if (vertexCount <= 0 || vertexCount > ushort.MaxValue ||
                    indexCount <= 0 || indexCount % 3 != 0 ||
                    triangleCount != indexCount / 3)
                    throw new InvalidDataException(name + " topology header is invalid.");

                Vector3[] vertices = new Vector3[vertexCount];
                Vector3[] normals = new Vector3[vertexCount];
                Color32[] colors = new Color32[vertexCount];
                Vector2[] uv = new Vector2[vertexCount];
                for (int vertex = 0; vertex < vertexCount; vertex++)
                {
                    vertices[vertex] = new Vector3(
                        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    normals[vertex] = new Vector3(
                        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    colors[vertex] = new Color32(
                        reader.ReadByte(), reader.ReadByte(),
                        reader.ReadByte(), reader.ReadByte());
                    uv[vertex] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                }
                int[] indices = new int[indexCount];
                for (int index = 0; index < indexCount; index++)
                    indices[index] = reader.ReadUInt16();
                Mesh generated = new Mesh
                {
                    name = name,
                    indexFormat = IndexFormat.UInt16,
                    vertices = vertices,
                    normals = normals,
                    colors32 = colors,
                    uv = uv,
                    triangles = indices
                };
                generated.RecalculateBounds();
                generated.UploadMeshData(true);
                string path = name.EndsWith("LOD0", StringComparison.Ordinal)
                    ? Lod0AssetPath
                    : name.EndsWith("LOD1", StringComparison.Ordinal)
                        ? Lod1AssetPath
                        : Lod2AssetPath;
                Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null)
                {
                    AssetDatabase.CreateAsset(generated, path);
                    existing = generated;
                }
                else
                {
                    EditorUtility.CopySerialized(generated, existing);
                    UnityEngine.Object.DestroyImmediate(generated);
                    EditorUtility.SetDirty(existing);
                }
                result.Add(name, existing);
            }
            if (memory.Position != memory.Length)
                throw new InvalidDataException("Airport-close tree source has trailing bytes.");
            return result;
        }

        private static AirportCloseTreeLodRenderer.Instance[] BuildInstances(
            Placement[] placements, Mesh sourceMesh)
        {
            Bounds bounds = sourceMesh.bounds;
            if (bounds.size.x <= 0.01f || bounds.size.y <= 0.01f ||
                bounds.size.z <= 0.01f || bounds.min.y < -0.01f)
                throw new InvalidOperationException("Normalized tree mesh bounds are invalid.");
            AirportCloseTreeLodRenderer.Instance[] result =
                new AirportCloseTreeLodRenderer.Instance[placements.Length];
            for (int index = 0; index < placements.Length; index++)
            {
                Placement item = placements[index];
                if (item.height < 3f || item.height > 30f ||
                    item.crown_diameter < 2f || item.crown_diameter > 20f)
                    throw new InvalidDataException(
                        $"Tree placement {index} has implausible dimensions.");
                Vector3 position = new Vector3(item.x, item.y, item.z);
                Vector3 scale = new Vector3(
                    item.crown_diameter / bounds.size.x,
                    item.height / bounds.size.y,
                    item.crown_diameter / bounds.size.z);
                Quaternion rotation = Quaternion.Euler(0f, item.rotation_degrees, 0f);
                Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, scale);
                Vector3 center = position + Vector3.up * item.height * 0.5f;
                float radius = Mathf.Sqrt(
                    item.height * item.height * 0.25f +
                    item.crown_diameter * item.crown_diameter * 0.5f);
                result[index] = new AirportCloseTreeLodRenderer.Instance(
                    matrix,
                    new Vector4(item.tint_r, item.tint_g, item.tint_b, 1f),
                    center,
                    position,
                    scale,
                    radius,
                    item.rotation_degrees);
            }
            return result;
        }

        private static int CountTriangles(IEnumerable<Renderer> renderers)
        {
            int count = 0;
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    count += checked((int)(mesh.GetIndexCount(submesh) / 3u));
            }
            return count;
        }

        private static int TriangleCount(Mesh mesh) =>
            mesh == null || mesh.subMeshCount == 0
                ? 0
                : checked((int)(mesh.GetIndexCount(0) / 3u));

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new FileNotFoundException($"Required asset not found: {path}");
            return asset;
        }
    }
}
