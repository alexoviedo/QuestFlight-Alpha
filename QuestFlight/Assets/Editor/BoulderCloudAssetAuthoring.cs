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
    /// Deterministically compiles the Boulder cloud field into project-owned
    /// optimized assets and an explicitly serialized product-prefab hierarchy.
    /// </summary>
    public static class BoulderCloudAssetAuthoring
    {
        private const string Root = "Assets/Production/Environment/CloudsV1";
        private const string Generated = Root + "/Generated";
        private const string ProfilePath = Root + "/KBDU_BoulderCloudProfile.asset";
        private const string TexturePath = Root + "/KBDU_CloudImpostorAtlasV3.png";
        private const string MaterialPath = Root + "/KBDU_BoulderCloudShell.mat";
        private const string ShaderPath = Root + "/BoulderCloudShell.shader";
        private const string ManifestPath = Root + "/SOURCE_MANIFEST.json";
        private const string CloudRootName = "BoulderCloudFieldRoot";
        private const float AirportMslMeters = 1612.6f;
        private const int RuntimeSectorCount = 1;

        private readonly struct CloudDefinition
        {
            public readonly string id;
            public readonly Vector3 center;
            public readonly Vector3 radii;
            public readonly int sector;
            public readonly int seed;

            public CloudDefinition(
                string cloudId,
                Vector3 localCenter,
                Vector3 localRadii,
                int sectorIndex,
                int deterministicSeed)
            {
                id = cloudId;
                center = localCenter;
                radii = localRadii;
                sector = sectorIndex;
                seed = deterministicSeed;
            }
        }

        [MenuItem("QuestFlight/Product/Author Boulder Cloud Field V1")]
        public static void BuildInteractive() => Build(exitWhenComplete: false);

        public static void RunBatch()
        {
            try
            {
                Build(exitWhenComplete: true);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Build(bool exitWhenComplete)
        {
            EnsureFolder("Assets/Production/Environment", "CloudsV1");
            EnsureFolder(Root, "Generated");

            Texture2D detail = BuildDetailTexture();
            Material material = BuildMaterial(detail);
            CloudDefinition[] definitions = BuildDefinitions();
            BoulderCloudProfile profile = BuildProfile(definitions);
            Mesh[] sectorMeshes = new Mesh[RuntimeSectorCount];
            for (int sector = 0; sector < sectorMeshes.Length; sector++)
                sectorMeshes[sector] = BuildSectorMesh(definitions, sector);
            for (int staleSector = RuntimeSectorCount; staleSector < 3; staleSector++)
                AssetDatabase.DeleteAsset(
                    $"{Generated}/KBDU_BoulderCloudSector_{staleSector}.asset");
            AuthorEnvironmentPrefab(profile, material, sectorMeshes);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                $"[QuestFlight][BoulderClouds] Authored {definitions.Length} cloud volumes, " +
                $"{sectorMeshes.Sum(TriangleCount):N0} triangles, " +
                $"{sectorMeshes.Length} opaque alpha-to-coverage draw.");
            if (exitWhenComplete)
                EditorApplication.Exit(0);
        }

        private static CloudDefinition[] BuildDefinitions()
        {
            // ENU-like local metres around KBDU. The layer is deliberately
            // scattered rather than uniform and leaves clear flight corridors.
            return new[]
            {
                new CloudDefinition("SCATTERED_WNW_01", new Vector3(-5100f, 2180f, -4300f), new Vector3(920f, 520f, 650f), 0, 101),
                new CloudDefinition("SCATTERED_WNW_02", new Vector3(-2500f, 2030f, -5050f), new Vector3(760f, 460f, 570f), 0, 103),
                new CloudDefinition("SCATTERED_N_01", new Vector3(350f, 1930f, -4700f), new Vector3(820f, 430f, 610f), 0, 107),
                new CloudDefinition("SCATTERED_NE_01", new Vector3(3400f, 2260f, -5050f), new Vector3(1050f, 560f, 710f), 0, 109),
                new CloudDefinition("SCATTERED_FAR_NE", new Vector3(6250f, 2460f, -3150f), new Vector3(1180f, 630f, 820f), 0, 113),

                new CloudDefinition("SCATTERED_W_01", new Vector3(-5600f, 2470f, -350f), new Vector3(1080f, 610f, 720f), 1, 127),
                new CloudDefinition("SCATTERED_AIRPORT_W", new Vector3(-2750f, 1880f, -850f), new Vector3(700f, 390f, 540f), 1, 131),
                new CloudDefinition("SCATTERED_AIRPORT_E", new Vector3(2050f, 2020f, -550f), new Vector3(780f, 440f, 610f), 1, 137),
                new CloudDefinition("SCATTERED_E_01", new Vector3(5050f, 2340f, 420f), new Vector3(980f, 560f, 690f), 1, 139),
                new CloudDefinition("SCATTERED_FAR_W", new Vector3(-6900f, 2780f, 1800f), new Vector3(1350f, 650f, 760f), 1, 149),

                new CloudDefinition("SCATTERED_RESERVOIR_W", new Vector3(-3200f, 2150f, 3350f), new Vector3(930f, 540f, 690f), 2, 151),
                new CloudDefinition("SCATTERED_RESERVOIR", new Vector3(-500f, 1810f, 4050f), new Vector3(720f, 390f, 520f), 2, 157),
                new CloudDefinition("SCATTERED_SE_01", new Vector3(2500f, 2210f, 3550f), new Vector3(940f, 520f, 700f), 2, 163),
                new CloudDefinition("SCATTERED_SE_02", new Vector3(5200f, 2520f, 4650f), new Vector3(1150f, 650f, 820f), 2, 167),
                new CloudDefinition("SCATTERED_FAR_S", new Vector3(850f, 2710f, 6900f), new Vector3(1280f, 640f, 880f), 2, 173)
            };
        }

        private static BoulderCloudProfile BuildProfile(CloudDefinition[] definitions)
        {
            BoulderCloudProfile profile =
                AssetDatabase.LoadAssetAtPath<BoulderCloudProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<BoulderCloudProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            BoulderCloudProfile.Volume[] volumes = definitions.Select(definition =>
            {
                float baseMsl = AirportMslMeters + definition.center.y - definition.radii.y;
                float topMsl = AirportMslMeters + definition.center.y + definition.radii.y;
                return new BoulderCloudProfile.Volume(
                    definition.id,
                    definition.center,
                    definition.radii,
                    baseMsl,
                    topMsl,
                    0.78f);
            }).ToArray();
            profile.Configure(
                "KBDU_SUMMER_SCATTERED_V1",
                "Front Range fair-weather cumulus",
                AirportMslMeters,
                1450f,
                2550f,
                0.32f,
                24000f,
                new Color(0.76f, 0.80f, 0.83f, 1f),
                0.026f,
                0.38f,
                0.72f,
                volumes);
            EditorUtility.SetDirty(profile);
            if (!profile.TryValidate(out string report))
                throw new InvalidOperationException("Cloud profile invalid: " + report);
            return profile;
        }

        private static Texture2D BuildDetailTexture()
        {
            const int size = 512;
            if (!File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "../" + TexturePath))))
                throw new FileNotFoundException("Project-owned cloud radiance atlas is missing.", TexturePath);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.mipMapsPreserveCoverage = true;
            importer.alphaTestReferenceValue = 0.02f;
            importer.streamingMipmaps = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 2;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = size;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.compressionQuality = 70;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (texture == null)
                throw new InvalidOperationException("Cloud radiance atlas did not import as Texture2D.");
            return texture;
        }

        private static Material BuildMaterial(Texture2D detail)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
                throw new InvalidOperationException("Cloud shader is missing: " + ShaderPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "KBDU_BoulderCloudShell" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.name = "KBDU_BoulderCloudShell";
            material.shader = shader;
            material.SetTexture("_CloudAtlas", detail);
            material.SetColor("_CloudTint", new Color(0.84f, 0.87f, 0.91f, 1f));
            material.SetFloat("_CoverageCutoff", 0.02f);
            material.SetFloat("_EdgeSoftness", 0.55f);
            material.SetFloat("_AmbientGain", 0.72f);
            material.SetFloat("_SunGain", 0.16f);
            material.enableInstancing = false;
            material.renderQueue = (int)RenderQueue.AlphaTest + 20;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh BuildSectorMesh(CloudDefinition[] definitions, int sector)
        {
            var vertices = new List<Vector3>(4096);
            var normals = new List<Vector3>(4096);
            var colors = new List<Color32>(4096);
            var uv = new List<Vector2>(4096);
            var billboardCorners = new List<Vector2>(4096);
            var billboardHalfSizes = new List<Vector2>(4096);
            var indices = new List<int>(8192);
            foreach (CloudDefinition definition in definitions.Where(item =>
                         RuntimeSectorCount == 1 || item.sector == sector))
            {
                AppendImpostorCloud(
                    vertices,
                    normals,
                    colors,
                    uv,
                    billboardCorners,
                    billboardHalfSizes,
                    indices,
                    definition);
            }
            Mesh mesh = new Mesh
            {
                name = $"KBDU_BoulderCloudSector_{sector}",
                indexFormat = vertices.Count > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, billboardCorners);
            mesh.SetUVs(2, billboardHalfSizes);
            mesh.SetTriangles(indices, 0, true);
            Bounds expandedBounds = CalculateExpandedBounds(definitions, sector);
            SubMeshDescriptor subMesh = mesh.GetSubMesh(0);
            subMesh.bounds = expandedBounds;
            mesh.SetSubMesh(
                0,
                subMesh,
                MeshUpdateFlags.DontRecalculateBounds |
                MeshUpdateFlags.DontValidateIndices);
            mesh.bounds = expandedBounds;
            mesh.UploadMeshData(true);
            string path = $"{Generated}/KBDU_BoulderCloudSector_{sector}.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                existing = mesh;
            }
            else
            {
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
            }
            return existing;
        }

        private static Bounds CalculateExpandedBounds(
            IEnumerable<CloudDefinition> definitions,
            int sector)
        {
            bool initialized = false;
            Vector3 minimum = Vector3.zero;
            Vector3 maximum = Vector3.zero;
            foreach (CloudDefinition definition in definitions.Where(item =>
                         RuntimeSectorCount == 1 || item.sector == sector))
            {
                float halfWidth = Mathf.Max(definition.radii.x, definition.radii.z);
                float halfHeight = definition.radii.y;
                // The vertex shader may rotate both billboard axes onto any
                // world axis.  A sphere around all four expanded corners is a
                // conservative CPU-side AABB for every camera orientation.
                float cornerRadius = Mathf.Sqrt(
                    halfWidth * halfWidth + halfHeight * halfHeight);
                Vector3 expansion = Vector3.one * cornerRadius;
                Vector3 itemMinimum = definition.center - expansion;
                Vector3 itemMaximum = definition.center + expansion;
                if (!initialized)
                {
                    minimum = itemMinimum;
                    maximum = itemMaximum;
                    initialized = true;
                }
                else
                {
                    minimum = Vector3.Min(minimum, itemMinimum);
                    maximum = Vector3.Max(maximum, itemMaximum);
                }
            }
            if (!initialized)
                throw new InvalidOperationException("Cannot build empty cloud-sector bounds.");
            return new Bounds((minimum + maximum) * 0.5f, maximum - minimum);
        }

        private static void AppendImpostorCloud(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Color32> colors,
            List<Vector2> uv,
            List<Vector2> billboardCorners,
            List<Vector2> billboardHalfSizes,
            List<int> indices,
            CloudDefinition definition)
        {
            float brightness = Mathf.Lerp(
                0.91f,
                1.02f,
                (definition.seed % 17) / 16f);
            Color32 color = new Color(brightness, brightness, brightness, 1f);
            float halfWidth = Mathf.Max(definition.radii.x, definition.radii.z);
            float halfHeight = definition.radii.y;
            AppendBillboard(
                vertices,
                normals,
                colors,
                uv,
                billboardCorners,
                billboardHalfSizes,
                indices,
                definition.center,
                new Vector2(halfWidth, halfHeight),
                color,
                definition.seed & 3);
        }

        private static void AppendBillboard(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Color32> colors,
            List<Vector2> uv,
            List<Vector2> billboardCorners,
            List<Vector2> billboardHalfSizes,
            List<int> indices,
            Vector3 center,
            Vector2 halfSize,
            Color32 color,
            int atlasIndex)
        {
            int start = vertices.Count;
            for (int index = 0; index < 4; index++)
                vertices.Add(center);
            billboardCorners.Add(new Vector2(-1f, -1f));
            billboardCorners.Add(new Vector2(-1f, 1f));
            billboardCorners.Add(new Vector2(1f, 1f));
            billboardCorners.Add(new Vector2(1f, -1f));
            for (int index = 0; index < 4; index++)
            {
                normals.Add(Vector3.back);
                colors.Add(color);
                billboardHalfSizes.Add(halfSize);
            }
            int column = atlasIndex & 1;
            int row = (atlasIndex >> 1) & 1;
            float u0 = column * 0.5f;
            float v0 = row * 0.5f;
            float u1 = u0 + 0.5f;
            float v1 = v0 + 0.5f;
            uv.Add(new Vector2(u0, v0));
            uv.Add(new Vector2(u0, v1));
            uv.Add(new Vector2(u1, v1));
            uv.Add(new Vector2(u1, v0));
            indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
            indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
        }

        private static void AuthorEnvironmentPrefab(
            BoulderCloudProfile profile,
            Material material,
            Mesh[] sectorMeshes)
        {
            GameObject environment = PrefabUtility.LoadPrefabContents(
                ProductionEnvironmentRoot.PrefabAssetPath);
            try
            {
                ProductionEnvironmentRoot contract =
                    environment.GetComponent<ProductionEnvironmentRoot>();
                if (contract == null)
                    throw new InvalidOperationException("Production environment contract is missing.");
                Transform existing = environment.transform.Find(CloudRootName);
                GameObject rootObject = existing == null
                    ? new GameObject(CloudRootName)
                    : existing.gameObject;
                if (existing == null)
                    rootObject.transform.SetParent(environment.transform, false);
                rootObject.transform.SetLocalPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                rootObject.transform.localScale = Vector3.one;
                BoulderCloudField field = rootObject.GetComponent<BoulderCloudField>() ??
                                          rootObject.AddComponent<BoulderCloudField>();
                MeshRenderer[] renderers = new MeshRenderer[sectorMeshes.Length];
                for (int sector = 0; sector < sectorMeshes.Length; sector++)
                {
                    string childName = $"CloudSector_{sector}";
                    Transform existingChild = rootObject.transform.Find(childName);
                    GameObject child = existingChild == null
                        ? new GameObject(childName)
                        : existingChild.gameObject;
                    if (existingChild == null)
                        child.transform.SetParent(rootObject.transform, false);
                    child.transform.SetLocalPositionAndRotation(
                        Vector3.zero,
                        Quaternion.identity);
                    child.transform.localScale = Vector3.one;
                    MeshFilter filter = child.GetComponent<MeshFilter>() ??
                                        child.AddComponent<MeshFilter>();
                    filter.sharedMesh = sectorMeshes[sector];
                    MeshRenderer renderer = child.GetComponent<MeshRenderer>() ??
                                            child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderer.allowOcclusionWhenDynamic = false;
                    renderers[sector] = renderer;
                }
                for (int index = rootObject.transform.childCount - 1; index >= 0; index--)
                {
                    Transform child = rootObject.transform.GetChild(index);
                    if (!child.name.StartsWith("CloudSector_", StringComparison.Ordinal))
                        continue;
                    if (renderers.Any(renderer => renderer != null && renderer.transform == child))
                        continue;
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
                TextAsset manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
                if (manifest == null)
                    throw new InvalidOperationException("Cloud source manifest did not import as TextAsset.");
                field.Configure(profile, renderers, material, manifest, null, null);
                contract.boulderCloudField = rootObject.transform;
                ProductionEnvironmentBudget budget = contract.CalculateBudget();
                contract.bakedRendererCount = budget.rendererCount;
                contract.bakedTriangleCount = budget.triangleCount;
                contract.bakedMaterialCount = budget.materialCount;
                contract.bakedTextureCount = Mathf.Max(contract.bakedTextureCount, 1);
                EditorUtility.SetDirty(field);
                EditorUtility.SetDirty(contract);
                PrefabUtility.SaveAsPrefabAsset(
                    environment,
                    ProductionEnvironmentRoot.PrefabAssetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(environment);
            }
        }

        private static int TriangleCount(Mesh mesh) =>
            mesh == null || mesh.subMeshCount == 0
                ? 0
                : checked((int)(mesh.GetIndexCount(0) / 3u));

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }
    }
}
