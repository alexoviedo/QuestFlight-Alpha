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
        public const string FullMapRoot =
            "Assets/Production/Environment/FullMapVegetationV3";
        public const string FullMapDatabasePath =
            FullMapRoot + "/KBDU_FullMapVegetationHlodV3.bytes";
        public const string FullMapManifestPath =
            FullMapRoot + "/KBDU_FullMapVegetationHlodV3.json";
        public const string FullMapCanopyTexturePath =
            FullMapRoot + "/KBDU_FullMapCanopyVisual_RGBA.png";
        public const string FullMapCanopyManifestPath =
            FullMapRoot + "/KBDU_FullMapCanopyVisual.json";
        public const string Lod0AssetPath =
            CanopyRoot + "/Native/AirportCloseTree_LOD0.asset";
        public const string Lod1AssetPath =
            CanopyRoot + "/Native/AirportCloseTree_LOD1.asset";
        public const string Lod2AssetPath =
            CanopyRoot + "/Native/AirportCloseTree_LOD2.asset";
        public const string ShrubAssetPath =
            CanopyRoot + "/Native/FullMapShrub_LOD.asset";
        public const string GroveAssetPath =
            CanopyRoot + "/Native/FullMapGrove_HLOD.asset";
        public const string HeroLeafAtlasPath =
            CanopyRoot + "/Textures/AirportCloseTree_HeroLeaf_RGBA.png";
        public const string ImpostorAtlasPath =
            CanopyRoot + "/Textures/AirportCloseTree_Impostor8_RGBA.png";
        public const string CanopyMaterialPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/Materials/" +
            "ProductionCanopySurfaceLayer.mat";
        public const string TerrainMaterialPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat";
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
            ConfigureFullMapCanopyTexture();
            PhotorealAerialSurfaceNativeMeshAuthoring.CompileAll();
            Dictionary<string, Mesh> meshes = CompileNativeLods();
            Mesh lod0 = meshes["AirportCloseTree_LOD0"];
            Mesh lod1 = meshes["AirportCloseTree_LOD1"];
            Mesh lod2 = meshes["AirportCloseTree_LOD2"];
            Mesh shrub = CreateOrUpdateVolumeMesh(
                ShrubAssetPath, "FullMapShrub_LOD", false);
            Mesh grove = CreateOrUpdateVolumeMesh(
                GroveAssetPath, "FullMapGrove_HLOD", true);
            TextAsset placementAsset = LoadRequired<TextAsset>(PlacementManifestPath);
            TextAsset sourceAsset = LoadRequired<TextAsset>(SourceManifestPath);
            TextAsset fullMapDatabase = LoadRequired<TextAsset>(FullMapDatabasePath);
            TextAsset fullMapManifest = LoadRequired<TextAsset>(FullMapManifestPath);
            Material canopyMaterial = LoadRequired<Material>(CanopyMaterialPath);
            Material terrainMaterial = LoadRequired<Material>(TerrainMaterialPath);
            Texture2D fullMapCanopy = LoadRequired<Texture2D>(FullMapCanopyTexturePath);
            Texture2D heroLeafAtlas = LoadRequired<Texture2D>(HeroLeafAtlasPath);
            Texture2D impostorAtlas = LoadRequired<Texture2D>(ImpostorAtlasPath);
            canopyMaterial.enableInstancing = true;
            canopyMaterial.SetTexture("_HeroLeafAtlas", heroLeafAtlas);
            canopyMaterial.SetTexture("_TreeImpostorAtlas", impostorAtlas);
            if (!terrainMaterial.HasProperty("_FullMapCanopyVisual"))
                throw new InvalidOperationException(
                    "Terrain material shader lacks the full-map canopy contract.");
            terrainMaterial.SetTexture("_FullMapCanopyVisual", fullMapCanopy);
            terrainMaterial.SetVector("_FullMapCanopyWorldMinInvSize",
                new Vector4(-12000f, -12000f, 1f / 24000f, 1f / 24000f));
            terrainMaterial.SetVector("_FullMapCanopyGeometryFadeStartEnd",
                new Vector4(700f, 1450f, 0f, 0f));
            terrainMaterial.SetFloat("_FullMapCanopyColorStrength", 0.42f);
            terrainMaterial.SetFloat("_FullMapCanopyNormalStrength", 0.48f);

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
                AircraftCenteredBuildingClipmapController buildingClipmap =
                    environment.GetComponentInChildren<AircraftCenteredBuildingClipmapController>(true);
                AerialSceneryRoot aerial =
                    environment.GetComponentInChildren<AerialSceneryRoot>(true);
                AirportCoreBenchmarkRoot benchmark =
                    environment.GetComponentInChildren<AirportCoreBenchmarkRoot>(true);
                if (environmentRoot == null || surface == null || aerial == null ||
                    benchmark == null || benchmark.vegetationHeroCluster == null ||
                    surface.productionCanopySurfaceLayer == null)
                    throw new InvalidOperationException(
                        "Authored production aerial hierarchy is incomplete.");

                // V4's card-heavy hero cluster occupies the same runway-left corridor as
                // the source-derived close-tree tier. Keep it serialized for historical
                // A/B provenance, but never draw both representations in the product.
                benchmark.vegetationHeroCluster.gameObject.SetActive(false);

                Transform canopy = surface.productionCanopySurfaceLayer;
                Transform previous = canopy.Find(RuntimeObjectName);
                GameObject runtimeObject;
                AirportCloseTreeLodRenderer renderer;
                if (previous == null)
                {
                    runtimeObject = new GameObject(RuntimeObjectName);
                    runtimeObject.layer = canopy.gameObject.layer;
                    runtimeObject.transform.SetParent(canopy, false);
                    renderer = runtimeObject.AddComponent<AirportCloseTreeLodRenderer>();
                }
                else
                {
                    runtimeObject = previous.gameObject;
                    renderer = runtimeObject.GetComponent<AirportCloseTreeLodRenderer>() ??
                        runtimeObject.AddComponent<AirportCloseTreeLodRenderer>();
                }
                if (buildingClipmap == null)
                    throw new InvalidOperationException(
                        "Full-map vegetation requires the authored building clipmap residency authority.");
                renderer.Configure(canopyMaterial, lod0, lod1, lod2, shrub, grove,
                    placementAsset, sourceAsset, instances, fullMapDatabase, buildingClipmap);

                Renderer[] staticCanopy = surface.canopySurfaceRenderers
                    .Where(item => item != null).ToArray();
                foreach (Renderer obsoleteCanopyHull in staticCanopy)
                    UnityEngine.Object.DestroyImmediate(obsoleteCanopyHull.gameObject);
                surface.canopySurfaceRenderers = Array.Empty<Renderer>();
                surface.canopySurfaceManifest = fullMapManifest;
                surface.representedCanopyComponentCount = renderer.RuntimeInstanceCount;
                surface.bakedCanopyDrawCount = 0;
                surface.bakedCanopyTriangleCount = 0;
                ProductionEnvironmentBudget budget = environmentRoot.CalculateBudget();
                environmentRoot.bakedRendererCount = budget.rendererCount;
                environmentRoot.bakedTriangleCount = budget.triangleCount;
                environmentRoot.bakedMaterialCount = budget.materialCount;
                aerial.bakedRendererCount = budget.rendererCount;
                aerial.bakedSelectedTriangleCount = budget.triangleCount;
                aerial.bakedMaterialCount = budget.materialCount;
                aerial.bakedVegetationRendererCount =
                    renderer.AuthoredMaximumDrawCount;
                aerial.bakedVegetationTriangleCount =
                    checked((int)renderer.AuthoredMaximumSelectedTriangleCount);
                aerial.bakedVegetationMaterialCount = 1;

                if (renderer.AuthoredMaximumDrawCount != 6 ||
                    renderer.AuthoredMaximumSelectedTriangleCount <= 0 ||
                    renderer.AuthoredMaximumSelectedTriangleCount > 85000)
                    throw new InvalidOperationException(
                        "Airport-close runtime LOD budget exceeds its authored contract.");
                if (budget.rendererCount > 75 || budget.triangleCount > 650000 ||
                    budget.materialCount > 29)
                    throw new InvalidOperationException(
                        $"Airport-close product budget failed: {budget.rendererCount}/75 draws, " +
                        $"{budget.triangleCount}/650000 triangles, " +
                        $"{budget.materialCount}/29 materials.");

                EditorUtility.SetDirty(canopyMaterial);
                EditorUtility.SetDirty(terrainMaterial);
                EditorUtility.SetDirty(surface);
                EditorUtility.SetDirty(aerial);
                EditorUtility.SetDirty(benchmark);
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

        private static void ConfigureFullMapCanopyTexture()
        {
            AssetDatabase.ImportAsset(
                FullMapCanopyTexturePath,
                ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(
                FullMapCanopyTexturePath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException(
                    "Full-map canopy texture importer is missing.");
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 2;
            importer.maxTextureSize = 4096;
            TextureImporterPlatformSettings android =
                importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 4096;
            android.format = TextureImporterFormat.ASTC_8x8;
            android.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
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

        private static Mesh CreateOrUpdateVolumeMesh(
            string path,
            string name,
            bool grove)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Color32> colors = new List<Color32>();
            List<Vector2> uv = new List<Vector2>();
            List<int> triangles = new List<int>();
            int lobeCount = grove ? 8 : 6;
            for (int lobe = 0; lobe < lobeCount; lobe++)
            {
                float angle = lobe * 2.39996323f + (grove ? 0.37f : 0.11f);
                float radial = grove
                    ? 0.10f + 0.28f * ((lobe * 5) % 7) / 6f
                    : 0.04f + 0.19f * ((lobe * 3) % 5) / 4f;
                float centerY = grove
                    ? 0.52f + 0.20f * ((lobe * 7) % 5) / 4f
                    : 0.25f + 0.32f * ((lobe * 5) % 7) / 6f;
                Vector3 center = new Vector3(
                    Mathf.Cos(angle) * radial,
                    centerY,
                    Mathf.Sin(angle) * radial);
                float halfWidth = grove
                    ? 0.19f + 0.035f * (lobe % 3)
                    : 0.25f + 0.025f * (lobe % 2);
                float halfHeight = grove
                    ? 0.24f + 0.025f * ((lobe + 1) % 3)
                    : 0.24f + 0.035f * (lobe % 3);
                AppendFoliagePair(
                    vertices, normals, colors, uv, triangles,
                    center, halfWidth, halfHeight, angle + lobe * 0.19f, lobe);
            }
            if (grove)
            {
                for (int trunk = 0; trunk < 6; trunk++)
                {
                    float angle = trunk * 2.39996323f;
                    float radial = 0.08f + 0.25f * ((trunk * 3) % 5) / 4f;
                    AppendTrunkCard(
                        vertices, normals, colors, uv, triangles,
                        new Vector3(Mathf.Cos(angle) * radial, 0f,
                            Mathf.Sin(angle) * radial),
                        0.018f + 0.006f * (trunk % 2),
                        0.56f + 0.08f * (trunk % 3),
                        angle + 0.41f);
                }
            }

            Mesh generated = new Mesh
            {
                name = name,
                indexFormat = IndexFormat.UInt16,
                vertices = vertices.ToArray(),
                normals = normals.ToArray(),
                colors32 = colors.ToArray(),
                uv = uv.ToArray(),
                triangles = triangles.ToArray(),
            };
            generated.RecalculateBounds();
            generated.UploadMeshData(true);
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            EditorUtility.CopySerialized(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static void AppendFoliagePair(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Color32> colors,
            List<Vector2> uv,
            List<int> triangles,
            Vector3 center,
            float halfWidth,
            float halfHeight,
            float angle,
            int atlasSeed)
        {
            for (int plane = 0; plane < 2; plane++)
            {
                float orientation = angle + plane * Mathf.PI * 0.5f;
                Vector3 tangent = new Vector3(
                    Mathf.Cos(orientation), 0f, Mathf.Sin(orientation));
                Vector3 normal = new Vector3(-tangent.z, 0f, tangent.x);
                Vector3 up = Vector3.up;
                int first = vertices.Count;
                vertices.Add(center - tangent * halfWidth - up * halfHeight);
                vertices.Add(center - tangent * halfWidth + up * halfHeight);
                vertices.Add(center + tangent * halfWidth + up * halfHeight);
                vertices.Add(center + tangent * halfWidth - up * halfHeight);
                for (int index = 0; index < 4; index++)
                {
                    normals.Add(normal);
                    colors.Add(new Color32(0, 72, 116, 46));
                }
                float inset = 0.018f;
                float cellX = (atlasSeed * 3 + plane) % 4;
                float cellY = ((atlasSeed + plane * 2) / 4) % 4;
                float u0 = 4f + cellX * 0.25f + inset;
                float v0 = cellY * 0.25f + inset;
                float u1 = 4f + (cellX + 1f) * 0.25f - inset;
                float v1 = (cellY + 1f) * 0.25f - inset;
                uv.Add(new Vector2(u0, v0));
                uv.Add(new Vector2(u0, v1));
                uv.Add(new Vector2(u1, v1));
                uv.Add(new Vector2(u1, v0));
                triangles.Add(first);
                triangles.Add(first + 2);
                triangles.Add(first + 1);
                triangles.Add(first);
                triangles.Add(first + 3);
                triangles.Add(first + 2);
            }
        }

        private static void AppendTrunkCard(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Color32> colors,
            List<Vector2> uv,
            List<int> triangles,
            Vector3 basePosition,
            float halfWidth,
            float height,
            float angle)
        {
            Vector3 tangent = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 normal = new Vector3(-tangent.z, 0f, tangent.x);
            int first = vertices.Count;
            vertices.Add(basePosition - tangent * halfWidth);
            vertices.Add(basePosition - tangent * halfWidth + Vector3.up * height);
            vertices.Add(basePosition + tangent * halfWidth + Vector3.up * height);
            vertices.Add(basePosition + tangent * halfWidth);
            for (int index = 0; index < 4; index++)
            {
                normals.Add(normal);
                colors.Add(new Color32(255, 126, 92, 52));
                uv.Add(new Vector2(-1f, -1f));
            }
            triangles.Add(first);
            triangles.Add(first + 2);
            triangles.Add(first + 1);
            triangles.Add(first);
            triangles.Add(first + 3);
            triangles.Add(first + 2);
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
