using System;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Offline-only V4.3 correction. It changes serialized material bindings and removes the
    /// obsolete minor-water presentation meshes; it never constructs or repairs runtime state.
    /// </summary>
    public static class AirportCoreV43ArtifactAuthoring
    {
        public const string RunwayMaterialPath =
            "Assets/Production/Environment/AirportCoreV4/Runway/AirportCoreContinuousRunway.mat";
        public const string ReservoirMaterialPath =
            "Assets/Production/Environment/AirportCoreV4/Water/Materials/AirportCoreReservoir_V4.mat";
        public const string ReservoirPackedPath =
            "Assets/Production/Environment/AirportCoreV4/Water/Textures/AirportCoreReservoir_DepthShoreRoughness.png";
        public const string MinorWaterMaskPath =
            "Assets/Production/Environment/AirportCoreV4/Water/Textures/AirportCoreMinorWater_TerrainMask.png";
        public const string SourceMinorWaterMeshPath =
            "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_OtherWaterFeatures_Surface_V2.asset";
        public const int MaskResolution = 512;

        public static void RunBatch()
        {
            try
            {
                Apply();
                Debug.Log("[QuestFlight][AirportCoreV43] SERIALIZED_ARTIFACT_CORRECTION_SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Apply()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Mesh minorWaterSource = LoadRequired<Mesh>(SourceMinorWaterMeshPath);
            GenerateMinorWaterMask(minorWaterSource);
            ConfigureTexture(MinorWaterMaskPath, false, TextureWrapMode.Clamp, MaskResolution, 8);
            ConfigureTexture(ReservoirPackedPath, false, TextureWrapMode.Clamp, 512, 6);

            Material runway = ConfigureRunway();
            Material reservoir = ConfigureReservoir();
            Material terrain = LoadRequired<Material>(
                "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat");
            ConfigureTerrain(terrain);

            GameObject environment = PrefabUtility.LoadPrefabContents(ProductionEnvironmentRoot.PrefabAssetPath);
            try
            {
                ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
                AirportCoreBenchmarkRoot root = environment.GetComponentInChildren<AirportCoreBenchmarkRoot>(true);
                if (contract == null || root == null || contract.runwayPavement == null ||
                    contract.boulderReservoirSurface == null)
                    throw new InvalidOperationException("V4.2 serialized environment contract is incomplete.");

                Mesh runwayMesh = contract.runwayPavement.sharedMesh;
                Mesh colliderMesh = contract.runwayCollisionSurface == null
                    ? null
                    : contract.runwayCollisionSurface.sharedMesh;
                if (runwayMesh == null || !ReferenceEquals(runwayMesh, colliderMesh))
                    throw new InvalidOperationException("Authoritative runway mesh/collider identity changed.");

                contract.runwayPavement.GetComponent<MeshRenderer>().sharedMaterial = runway;
                contract.boulderReservoirSurface.GetComponent<MeshRenderer>().sharedMaterial = reservoir;
                MeshRenderer airportTerrain = contract.nearTerrainMeshes.FirstOrDefault()
                    ?.GetComponent<MeshRenderer>();
                if (airportTerrain == null || airportTerrain.sharedMaterial != terrain)
                    throw new InvalidOperationException("Authoritative terrain material binding changed.");
                root.minorWaterTerrainEffect = airportTerrain;

                DestroyMinorWaterPresentation(contract.otherWaterFeaturesSurfaceV2);
                DestroyMinorWaterPresentation(contract.otherWaterFeaturesShoreBankV2);
                contract.otherWaterFeaturesSurfaceV2 = null;
                contract.otherWaterFeaturesShoreBankV2 = null;

                root.authoritativeRunway = contract.runwayPavement.GetComponent<MeshRenderer>();
                root.boulderReservoir = contract.boulderReservoirSurface.GetComponent<MeshRenderer>();
                root.manifest.runwayMaterial = runway;
                root.manifest.reservoirMaterial = reservoir;
                root.manifest.runwayMacroPatternRemoved = true;
                root.manifest.minorWaterUsesTerrainMask = true;

                ProductionEnvironmentBudget budget = contract.CalculateBudget();
                contract.bakedRendererCount = budget.rendererCount;
                contract.bakedTriangleCount = budget.triangleCount;
                contract.bakedMaterialCount = budget.materialCount;
                contract.bakeToolVersion = "airport_core_artifact_v4_3";
                contract.bakeUtc = "2026-07-18T18:15:00Z";
                EditorUtility.SetDirty(root.manifest);
                EditorUtility.SetDirty(root);
                EditorUtility.SetDirty(contract);
                PrefabUtility.SaveAsPrefabAsset(environment, ProductionEnvironmentRoot.PrefabAssetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(environment);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static Material ConfigureRunway()
        {
            Material material = LoadRequired<Material>(RunwayMaterialPath);
            Shader shader = Shader.Find("QuestFlight/AirportCoreV4/ContinuousRunway");
            if (shader == null) throw new InvalidOperationException("V4.3 runway shader is unavailable.");
            material.shader = shader;
            material.SetTexture("_MainTex", LoadRequired<Texture2D>(
                "Assets/Production/Environment/AirportCoreV4/Runway/AirportCoreRunway_Albedo.png"));
            material.SetTexture("_NormalTex", LoadRequired<Texture2D>(
                "Assets/Production/Environment/AirportCoreV4/Runway/AirportCoreRunway_Normal.png"));
            material.SetTexture("_MRAOTex", LoadRequired<Texture2D>(
                "Assets/Production/Environment/AirportCoreV4/Runway/AirportCoreRunway_MRAO.png"));
            Vector2 origin = new Vector2(-629.757f, -30.671f);
            Vector2 end = new Vector2(620.628f, -30.054f);
            Vector2 axis = (end - origin).normalized;
            material.SetVector("_RunwayOrigin", new Vector4(origin.x, origin.y, 0f, 0f));
            material.SetVector("_RunwayAxis", new Vector4(axis.x, axis.y, 0f, 0f));
            material.SetVector("_RunwayLengthWidth", new Vector4((end - origin).magnitude, 22.86f, 0f, 0f));
            material.SetFloat("_WorldTiling", 0.09f);
            material.SetFloat("_SecondaryTiling", 0.031f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material ConfigureReservoir()
        {
            Material material = LoadRequired<Material>(ReservoirMaterialPath);
            Shader shader = Shader.Find(ProductionEnvironmentRoot.ProductionReservoirShaderName);
            if (shader == null) throw new InvalidOperationException("V4.3 reservoir shader is unavailable.");
            material.shader = shader;
            material.SetTexture("_WaveNormalA", LoadRequired<Texture2D>(
                "Assets/Production/Environment/Textures/Reservoir/BoulderReservoir_BroadWave_NM.png"));
            material.SetTexture("_WaveNormalB", LoadRequired<Texture2D>(
                "Assets/Production/Environment/Textures/Reservoir/BoulderReservoir_Ripple_NM.png"));
            material.SetTexture("_DepthShoreRoughness", LoadRequired<Texture2D>(ReservoirPackedPath));
            material.SetTexture("_TerrainMacroAlbedo", LoadRequired<Texture2D>(
                "Assets/Production/Environment/Textures/KBDU_NAIP_2019_Mid12km_4096.jpg"));
            material.SetTexture("_EnvironmentCube", LoadRequired<Texture>(
                "Assets/Production/Environment/Sky/Kloppenheim03_PureSky_2K.hdr"));
            material.SetVector("_TerrainWorldMinInvSize",
                new Vector4(-6000f, -6000f, 1f / 12000f, 1f / 12000f));
            material.SetFloat("_OrthophotoInfluence", 0.72f);
            material.enableInstancing = true;
            material.renderQueue = (int)RenderQueue.Geometry + 5;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureTerrain(Material material)
        {
            material.SetTexture("_AirportCoreMinorWaterMask", LoadRequired<Texture2D>(MinorWaterMaskPath));
            material.SetVector("_AirportCoreMinorWaterNearWorldMinInvSize",
                new Vector4(-2000f, -2000f, 1f / 4000f, 1f / 4000f));
            material.SetVector("_AirportCoreMinorWaterMidWorldMinInvSize",
                new Vector4(-6000f, -6000f, 1f / 12000f, 1f / 12000f));
            material.SetFloat("_AirportCoreMinorWaterStrength", 1f);
            material.SetColor("_AirportCoreMinorWaterReflectionColor", new Color(0.44f, 0.56f, 0.63f, 1f));
            EditorUtility.SetDirty(material);
        }

        private static void DestroyMinorWaterPresentation(MeshFilter filter)
        {
            if (filter != null) UnityEngine.Object.DestroyImmediate(filter.gameObject);
        }

        private static void GenerateMinorWaterMask(Mesh mesh)
        {
            bool[] near = Rasterize(mesh, -2000f, -2000f, 4000f, MaskResolution);
            bool[] mid = Rasterize(mesh, -6000f, -6000f, 12000f, MaskResolution);
            ApplyRunwayExclusion(near, -2000f, -2000f, 4000f, MaskResolution);
            ApplyRunwayExclusion(mid, -6000f, -6000f, 12000f, MaskResolution);
            byte[] nearWater = new byte[near.Length];
            byte[] nearBank = new byte[near.Length];
            byte[] midWater = new byte[mid.Length];
            byte[] midBank = new byte[mid.Length];
            BuildFeatheredChannels(near, MaskResolution, nearWater, nearBank);
            BuildFeatheredChannels(mid, MaskResolution, midWater, midBank);

            Texture2D texture = new Texture2D(MaskResolution, MaskResolution, TextureFormat.RGBA32, false, true)
            {
                name = "AirportCoreMinorWater_TerrainMask"
            };
            Color32[] pixels = new Color32[near.Length];
            for (int index = 0; index < pixels.Length; index++)
                pixels[index] = new Color32(nearWater[index], nearBank[index], midWater[index], midBank[index]);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", MinorWaterMaskPath));
            File.WriteAllBytes(absolute, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(MinorWaterMaskPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static bool[] Rasterize(Mesh mesh, float minimumX, float minimumZ, float size, int resolution)
        {
            bool[] mask = new bool[resolution * resolution];
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int triangle = 0; triangle < triangles.Length; triangle += 3)
            {
                Vector2 a = Pixel(vertices[triangles[triangle]], minimumX, minimumZ, size, resolution);
                Vector2 b = Pixel(vertices[triangles[triangle + 1]], minimumX, minimumZ, size, resolution);
                Vector2 c = Pixel(vertices[triangles[triangle + 2]], minimumX, minimumZ, size, resolution);
                int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), 0, resolution - 1);
                int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), 0, resolution - 1);
                int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), 0, resolution - 1);
                int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), 0, resolution - 1);
                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    if (InsideTriangle(new Vector2(x + 0.5f, y + 0.5f), a, b, c))
                        mask[y * resolution + x] = true;
            }
            return mask;
        }

        private static Vector2 Pixel(Vector3 vertex, float minimumX, float minimumZ, float size, int resolution) =>
            new Vector2((vertex.x - minimumX) / size * resolution,
                (vertex.z - minimumZ) / size * resolution);

        private static bool InsideTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(point, a, b);
            float d2 = Sign(point, b, c);
            float d3 = Sign(point, c, a);
            return !(d1 < 0f || d2 < 0f || d3 < 0f) || !(d1 > 0f || d2 > 0f || d3 > 0f);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3) =>
            (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

        private static void ApplyRunwayExclusion(
            bool[] mask, float minimumX, float minimumZ, float size, int resolution)
        {
            Vector2 start = new Vector2(-629.757f, -30.671f);
            Vector2 end = new Vector2(620.628f, -30.054f);
            Vector2 segment = end - start;
            float denominator = segment.sqrMagnitude;
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                int index = y * resolution + x;
                if (!mask[index]) continue;
                Vector2 world = new Vector2(
                    minimumX + (x + 0.5f) / resolution * size,
                    minimumZ + (y + 0.5f) / resolution * size);
                float t = Mathf.Clamp01(Vector2.Dot(world - start, segment) / denominator);
                if (Vector2.Distance(world, start + segment * t) < 20f) mask[index] = false;
            }
        }

        private static void BuildFeatheredChannels(bool[] inside, int resolution, byte[] water, byte[] bank)
        {
            const int maximum = 32767;
            int[] distance = Enumerable.Repeat(maximum, inside.Length).ToArray();
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                int index = y * resolution + x;
                bool boundary = x == 0 || y == 0 || x == resolution - 1 || y == resolution - 1 ||
                    inside[index - (x > 0 ? 1 : 0)] != inside[index] ||
                    inside[index + (x < resolution - 1 ? 1 : 0)] != inside[index] ||
                    inside[index - (y > 0 ? resolution : 0)] != inside[index] ||
                    inside[index + (y < resolution - 1 ? resolution : 0)] != inside[index];
                if (boundary) distance[index] = 0;
            }
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                int index = y * resolution + x;
                if (x > 0) distance[index] = Math.Min(distance[index], distance[index - 1] + 1);
                if (y > 0) distance[index] = Math.Min(distance[index], distance[index - resolution] + 1);
            }
            for (int y = resolution - 1; y >= 0; y--)
            for (int x = resolution - 1; x >= 0; x--)
            {
                int index = y * resolution + x;
                if (x < resolution - 1) distance[index] = Math.Min(distance[index], distance[index + 1] + 1);
                if (y < resolution - 1) distance[index] = Math.Min(distance[index], distance[index + resolution] + 1);
                float edge = Mathf.Clamp01(distance[index] / 2.5f);
                float coverage = inside[index] ? 0.5f + edge * 0.5f : 0.5f - edge * 0.5f;
                water[index] = (byte)Mathf.RoundToInt(Mathf.Clamp01(coverage) * 255f);
                bank[index] = (byte)Mathf.RoundToInt(Mathf.Clamp01(1f - distance[index] / 4f) * 255f);
            }
        }

        private static void ConfigureTexture(
            string path, bool normal, TextureWrapMode wrap, int maximumSize, int astcBlock)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing texture importer: " + path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = true;
            importer.wrapMode = wrap;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = maximumSize;
            android.format = astcBlock == 8 ? TextureImporterFormat.ASTC_8x8 : TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException($"Missing {typeof(T).Name}: {path}");
            return asset;
        }
    }
}
