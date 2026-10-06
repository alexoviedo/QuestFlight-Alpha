using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using QuestFlightLab.Environment;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Deterministic offline authoring pass for the standalone KBDU environment.
    /// It mutates serialized assets only; no product hierarchy is created at runtime.
    /// </summary>
    public static class StandaloneEnvironmentArtAuthoring
    {
        private const string EnvironmentPrefabPath = "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        private const string FarMeshPath = "Assets/Production/Environment/Generated/Terrain_Far24km_Immutable_USGS_Mesh.asset";
        private const string AirportTransitionMeshPath = GeneratedArtFolder + "/Terrain_AirportPatch_Near_Transition_Mesh.asset";
        private const string AirportTransitionObjectName = "Terrain_AirportPatch_Near_Transition";
        private const string AirportModelPath = "Assets/Production/Environment/AirportKit/KBDU_AirportKit.glb";
        private const string AirportAtlasPath = "Assets/Production/Environment/AirportKit/KBDU_AirportSharedAtlas.png";
        private const string RunwayAlbedoPath = "Assets/Production/Environment/AirportKit/KBDU_RunwaySurface_Albedo.jpg";
        private const string RunwayPackedPath = "Assets/Production/Environment/AirportKit/KBDU_RunwaySurface_MetallicSmoothness.png";
        private const string NearOrthophotoPath = "Assets/Production/Environment/Textures/KBDU_NAIP_2019_Near4km_4096.jpg";
        private const string MidOrthophotoPath = "Assets/Production/Environment/Textures/KBDU_NAIP_2019_Mid12km_2048.jpg";
        private const string AirportMaterialPath = "Assets/Production/Environment/Materials/Production_AirportShared.mat";
        private const string VegetationMaterialPath = "Assets/Production/Environment/Materials/Production_SparseVegetation.mat";
        private const string FarTerrainMaterialPath = "Assets/Production/Environment/Materials/Production_FarTerrain_Immutable.mat";
        private const string GeneratedArtFolder = "Assets/Production/Environment/Generated/StandaloneRecovery";
        private const string FarDemRepositoryRelativePath = "tools/kbdu_orthophoto/kbdu_far_dem_100m.json";

        private static readonly string[] GroundMaterialPaths =
        {
            "Assets/Production/Environment/Materials/Production_MacroGround.mat",
            "Assets/Production/Environment/Materials/Production_MacroGround_Green.mat",
            "Assets/Production/Environment/Materials/Production_MacroGround_Harvest.mat"
        };

        private static readonly AirportPlacement[] AirportPlacements =
        {
            new AirportPlacement("Hangar_A_Gable", new Vector3(-310f, 0f, -195f), 0f),
            new AirportPlacement("Hangar_B_Wide", new Vector3(-250f, 0f, -195f), 0f),
            new AirportPlacement("Hangar_C_Quonset", new Vector3(-190f, 0f, -192f), 0f),
            new AirportPlacement("Fuel_Service", new Vector3(-112f, 0f, -205f), 0f),
            new AirportPlacement("Fence_Signage", new Vector3(-35f, 0f, -225f), 0f),
            new AirportPlacement("Ramp_Props", new Vector3(-140f, 0f, -132f), 0f)
        };

        private static readonly Vector3[] VegetationPlacements =
        {
            new Vector3(-890f, 0f, 3550f),
            new Vector3(-1030f, 0f, 4210f),
            new Vector3(-820f, 0f, 4960f),
            new Vector3(1180f, 0f, 3580f),
            new Vector3(1370f, 0f, 4250f),
            new Vector3(1120f, 0f, 5010f),
            new Vector3(-720f, 0f, 275f),
            new Vector3(730f, 0f, 255f)
        };

        [MenuItem("QuestFlight/Product/Author Standalone Environment Art")]
        public static void RunInteractive() => AuthorEnvironment();

        public static void RunBatch()
        {
            try
            {
                AuthorEnvironment();
                Debug.Log("[QuestFlight][StandaloneEnvironment] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Regenerates only the already-referenced airport and sparse-vegetation mesh
        /// assets. This deliberately does not load or save the product environment,
        /// world prefab, or scene.
        /// </summary>
        public static void RepairVisualAssetsBatch()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Dictionary<string, Mesh[]> airportMeshes = AuthorAirportCombinedMeshes();
                Mesh vegetationLod0 = AuthorVegetationMesh("Production_SparseCottonwood_LOD0", highDetail: true);
                Mesh vegetationLod1 = AuthorVegetationMesh("Production_SparseCottonwood_LOD1", highDetail: false);
                Mesh[] quonset = airportMeshes["Hangar_C_Quonset"];
                if (quonset.Any(mesh => mesh == null || mesh.subMeshCount != 1 || mesh.triangles.Length == 0))
                    throw new InvalidOperationException("Repaired Quonset LOD assets are missing or split");
                if (vegetationLod0.triangles.Length / 3 > 240 || vegetationLod1.triangles.Length / 3 > 60)
                    throw new InvalidOperationException("Repaired sparse vegetation exceeded its bounded topology budget");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log(
                    "[QuestFlight][StandaloneEnvironmentVisualRepair] Rebuilt closed Quonset LODs and " +
                    "deciduous cottonwood/shrub LOD meshes without modifying any prefab or scene.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }


        private static void AuthorEnvironment()
        {
            ConfigureTexture(NearOrthophotoPath, 4096, true, true, 4, true);
            ConfigureTexture(MidOrthophotoPath, 2048, true, true, 4, true);
            ConfigureTexture(AirportAtlasPath, 512, true, false, 2, true);
            ConfigureTexture(RunwayAlbedoPath, 1024, true, false, 2, true);
            ConfigureTexture(RunwayPackedPath, 1024, false, false, 2, true);

            Material airportMaterial = CreateOrUpdateAirportMaterial();
            Material vegetationMaterial = CreateOrUpdateVegetationMaterial();
            ConfigureGroundMaterials();
            ConfigureFarTerrainMaterial();
            ConfigureRunwayMaterial();
            ConfigureWaterMaterial();
            Mesh farMesh = AuthorFarTerrainMesh();
            Dictionary<string, Mesh[]> airportMeshes = AuthorAirportCombinedMeshes();
            Mesh vegetationLod0 = AuthorVegetationMesh("Production_SparseCottonwood_LOD0", highDetail: true);
            Mesh vegetationLod1 = AuthorVegetationMesh("Production_SparseCottonwood_LOD1", highDetail: false);

            GameObject root = PrefabUtility.LoadPrefabContents(EnvironmentPrefabPath);
            try
            {
                bool wasPreviouslyAuthored = FindDescendant(root.transform, "AirportArtProduction") != null;
                ReplaceFarTerrainMesh(root, farMesh);
                Mesh airportTransitionMesh = AuthorAirportPatchTransitionMesh(root);
                AuthorAirportPatchTransitionHierarchy(root, airportTransitionMesh);
                RemoveRedundantContextGeometry(root, wasPreviouslyAuthored);
                Transform airportContext = FindDescendant(root.transform, "AirportContext");
                if (airportContext == null || airportContext.parent == null ||
                    !string.Equals(airportContext.parent.name, "NearProductionZone_4km", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Required authored airport parent NearProductionZone_4km/AirportContext is missing");
                // Remove either the earlier root-level authored hierarchy or a prior nested pass.
                ReplaceChild(root.transform, "AirportArtProduction");
                ReplaceChild(airportContext, "AirportArtProduction");
                ReplaceChild(root.transform, "SparseCottonwoodContext");
                ReplaceChild(root.transform, "SparseVegetationProduction");
                AuthorAirportHierarchy(root, airportContext, airportMeshes, airportMaterial);
                AuthorVegetationHierarchy(root, vegetationLod0, vegetationLod1, vegetationMaterial);
                UpdateEnvironmentContract(root, farMesh);
                ValidateAuthoredHierarchy(root, airportMaterial, vegetationMaterial);
                PrefabUtility.SaveAsPrefabAsset(root, EnvironmentPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                "[QuestFlight][StandaloneEnvironment] Authored immutable 100 m far ring, near/mid NAIP material bindings, " +
                "bounded airport-patch terrain transition, " +
                "six single-material airport LOD groups, sparse instanced vegetation LODs, runway surface, and bounded water tuning.");
        }

        private static void ConfigureTexture(
            string path,
            int maximumSize,
            bool srgb,
            bool streaming,
            int anisotropy,
            bool clamp)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Texture importer is missing: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = streaming;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = anisotropy;
            importer.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android",
                overridden = true,
                maxTextureSize = maximumSize,
                format = TextureImporterFormat.ASTC_6x6,
                compressionQuality = 75
            });
            importer.SaveAndReimport();
        }

        private static Material CreateOrUpdateAirportMaterial()
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Standard shader is unavailable");
            Material material = LoadOrCreateMaterial(AirportMaterialPath, shader);
            material.name = "Production_AirportShared";
            material.SetTexture("_MainTex", LoadRequired<Texture2D>(AirportAtlasPath));
            // Preserve one shared, instanced material while keeping shaded hangar faces
            // legible against the dark orthophoto and mountain backdrop.
            material.SetColor("_Color", new Color(1.18f, 1.16f, 1.12f, 1f));
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", 0.12f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateOrUpdateVegetationMaterial()
        {
            Shader shader = Shader.Find("QuestFlight/Production Sparse Vegetation");
            if (shader == null) throw new InvalidOperationException("Sparse vegetation shader is unavailable");
            Material material = LoadOrCreateMaterial(VegetationMaterialPath, shader);
            material.name = "Production_SparseVegetation";
            material.SetColor("_Tint", Color.white);
            material.SetFloat("_Roughness", 0.9f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreateMaterial(string path, Shader shader)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                material.shader = shader;
                return material;
            }
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void ConfigureGroundMaterials()
        {
            Texture2D near = LoadRequired<Texture2D>(NearOrthophotoPath);
            Texture2D mid = LoadRequired<Texture2D>(MidOrthophotoPath);
            foreach (string path in GroundMaterialPaths)
            {
                Material material = LoadRequired<Material>(path);
                material.SetTexture("_MacroAlbedo", mid);
                material.SetTexture("_NearOrthophoto", near);
                material.SetVector("_MacroWorldMinInvSize", new Vector4(-6000f, -6000f, 1f / 12000f, 1f / 12000f));
                material.SetVector("_NearWorldMinInvSize", new Vector4(-2000f, -2000f, 1f / 4000f, 1f / 4000f));
                material.SetVector("_NearBlendStartEnd", new Vector4(1750f, 2000f, 0f, 0f));
                material.SetFloat("_OrthophotoEnabled", 1f);
                material.SetFloat("_MacroBrightness", 0.94f);
                material.SetFloat("_MacroSaturation", 0.94f);
                material.SetFloat("_RegionalContrast", 1.04f);
                material.SetFloat("_LocalContrast", 1.02f);
                material.SetFloat("_DetailFadeStart", 180f);
                material.SetFloat("_DetailFadeEnd", 1250f);
                EditorUtility.SetDirty(material);
            }
        }

        private static void ConfigureRunwayMaterial()
        {
            Material material = LoadRequired<Material>(
                "Assets/Production/Environment/Materials/Production_Runway_Asphalt.mat");
            material.SetTexture("_MainTex", LoadRequired<Texture2D>(RunwayAlbedoPath));
            material.SetTexture("_MetallicGlossMap", LoadRequired<Texture2D>(RunwayPackedPath));
            material.SetTextureScale("_MainTex", Vector2.one);
            material.SetTextureOffset("_MainTex", Vector2.zero);
            material.SetTextureScale("_MetallicGlossMap", Vector2.one);
            // The generated atlas already carries its asphalt value range. The historical
            // dark tint multiplied it nearly to black and hid the authored wear/cracks.
            material.SetColor("_Color", new Color(0.92f, 0.94f, 0.98f, 1f));
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_GlossMapScale", 0.62f);
            material.SetFloat("_Glossiness", 0.16f);
            material.EnableKeyword("_METALLICGLOSSMAP");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }

        private static void ConfigureFarTerrainMaterial()
        {
            Shader shader = Shader.Find("QuestFlightLab/Production Far Terrain");
            if (shader == null) throw new InvalidOperationException("Far terrain shader is unavailable");
            Material material = LoadRequired<Material>(FarTerrainMaterialPath);
            material.shader = shader;
            material.SetColor("_LowColor", new Color(0.29f, 0.31f, 0.20f, 1f));
            material.SetColor("_MidColor", new Color(0.39f, 0.35f, 0.23f, 1f));
            material.SetColor("_HighColor", new Color(0.43f, 0.42f, 0.37f, 1f));
            material.SetColor("_RockColor", new Color(0.34f, 0.35f, 0.34f, 1f));
            material.SetVector("_ElevationRange", new Vector4(-110f, 980f, 0f, 0f));
            material.SetFloat("_VariationScale", 0.00072f);
            material.SetFloat("_Roughness", 0.91f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }

        private static void ConfigureWaterMaterial()
        {
            Material material = LoadRequired<Material>(
                "Assets/Production/Environment/Materials/Production_BoulderReservoir_Opaque.mat");
            material.SetColor("_Color", new Color(0.095f, 0.275f, 0.345f, 1f));
            material.SetColor("_ShoreColor", new Color(0.33f, 0.49f, 0.42f, 1f));
            material.SetColor("_HorizonColor", new Color(0.55f, 0.68f, 0.71f, 1f));
            material.SetFloat("_DepthContrast", 0.92f);
            material.SetFloat("_SurfaceVariation", 0.16f);
            material.SetFloat("_GlintStrength", 0.34f);
            material.SetFloat("_Roughness", 0.54f);
            material.SetFloat("_RippleStrength", 0.48f);
            EditorUtility.SetDirty(material);
        }

        internal static Mesh AuthorFarTerrainMesh()
        {
            string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string path = Path.Combine(repositoryRoot, FarDemRepositoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) throw new FileNotFoundException("Far DEM source is missing", path);
            FarDemDocument document = JsonUtility.FromJson<FarDemDocument>(File.ReadAllText(path));
            if (document?.grid == null || document.grid.width != 241 || document.grid.height != 241 ||
                !Mathf.Approximately(document.grid.spacing_meters, 100f))
                throw new InvalidOperationException("Far DEM grid contract must be 241x241 at 100 m");
            byte[] encoded = Convert.FromBase64String(document.grid.height_payload_base64);
            int count = document.grid.width * document.grid.height;
            if (encoded.Length != count * 2) throw new InvalidOperationException("Far DEM payload length is invalid");

            List<Vector3> vertices = new List<Vector3>(count + 6000);
            for (int row = 0; row < document.grid.height; row++)
            for (int column = 0; column < document.grid.width; column++)
            {
                int index = row * document.grid.width + column;
                short offset = (short)(encoded[index * 2] | encoded[index * 2 + 1] << 8);
                vertices.Add(new Vector3(
                    -document.grid.half_extent_meters + column * document.grid.spacing_meters,
                    offset * document.grid.quantization_meters,
                    -document.grid.half_extent_meters + row * document.grid.spacing_meters));
            }

            List<int> triangles = new List<int>(90000 * 3);
            int innerStart = Mathf.RoundToInt(
                (document.grid.half_extent_meters - document.grid.inner_half_extent_meters) /
                document.grid.spacing_meters);
            int innerEnd = document.grid.width - 1 - innerStart;
            for (int row = 0; row < document.grid.height - 1; row++)
            for (int column = 0; column < document.grid.width - 1; column++)
            {
                if (row >= innerStart && row < innerEnd && column >= innerStart && column < innerEnd) continue;
                int southWest = row * document.grid.width + column;
                int southEast = southWest + 1;
                int northWest = southWest + document.grid.width;
                int northEast = northWest + 1;
                triangles.Add(southWest); triangles.Add(northWest); triangles.Add(northEast);
                triangles.Add(southWest); triangles.Add(northEast); triangles.Add(southEast);
            }

            AddSkirtLoop(vertices, triangles, BuildOuterLoop(document.grid.width), document.grid.skirt_depth_meters);
            AddSkirtLoop(vertices, triangles, BuildInnerLoop(document.grid.width, innerStart, innerEnd), document.grid.skirt_depth_meters);

            Mesh generated = new Mesh
            {
                name = "Terrain_Far24km_Immutable_USGS_Mesh",
                indexFormat = IndexFormat.UInt32
            };
            generated.SetVertices(vertices);
            generated.SetTriangles(triangles, 0, true);
            ApplyTwoPassFarSmoothing(generated);
            generated.RecalculateNormals();
            generated.RecalculateBounds();
            Mesh transitioned = TerrainDefectRepairV1Authoring.BuildTransitionMesh(
                LoadRequired<Mesh>(TerrainDefectRepairV1Authoring.MiddleMeshPath),
                TerrainDefectRepairV1Authoring.MiddleHalfExtent,
                generated,
                TerrainDefectRepairV1Authoring.FarTransitionOuterHalfExtent,
                "Terrain_Far24km_Immutable_USGS_Mesh");
            UnityEngine.Object.DestroyImmediate(generated);
            generated = transitioned;
            Mesh stored = WriteMeshAsset(FarMeshPath, generated);
            if (stored.triangles.Length / 3 != 87184)
                throw new InvalidOperationException(
                    "Far terrain bounded transition triangle contract changed: " +
                    stored.triangles.Length / 3);
            return stored;
        }

        private static void ApplyTwoPassFarSmoothing(Mesh mesh)
        {
            int[] topology = mesh.triangles;
            Vector3[] vertices = mesh.vertices;
            HashSet<int>[] neighbors = Enumerable.Range(0, vertices.Length).Select(_ => new HashSet<int>()).ToArray();
            for (int index = 0; index + 2 < topology.Length; index += 3)
            {
                int a = topology[index];
                int b = topology[index + 1];
                int c = topology[index + 2];
                neighbors[a].Add(b); neighbors[a].Add(c);
                neighbors[b].Add(a); neighbors[b].Add(c);
                neighbors[c].Add(a); neighbors[c].Add(b);
            }
            const int passes = 2;
            const float blend = 0.18f;
            for (int pass = 0; pass < passes; pass++)
            {
                Vector3[] next = (Vector3[])vertices.Clone();
                for (int index = 0; index < vertices.Length; index++)
                {
                    float squareRadius = Mathf.Max(Mathf.Abs(vertices[index].x), Mathf.Abs(vertices[index].z));
                    if (squareRadius <= 6200f || squareRadius >= 11800f || neighbors[index].Count < 3) continue;
                    float averageY = 0f;
                    foreach (int neighbor in neighbors[index]) averageY += vertices[neighbor].y;
                    averageY /= neighbors[index].Count;
                    next[index].y = Mathf.Lerp(vertices[index].y, averageY, blend);
                }
                vertices = next;
            }
            mesh.vertices = vertices;
        }

        private static List<int> BuildOuterLoop(int width)
        {
            List<int> loop = new List<int>();
            for (int column = 0; column < width; column++) loop.Add(column);
            for (int row = 1; row < width; row++) loop.Add(row * width + width - 1);
            for (int column = width - 2; column >= 0; column--) loop.Add((width - 1) * width + column);
            for (int row = width - 2; row > 0; row--) loop.Add(row * width);
            return loop;
        }

        private static List<int> BuildInnerLoop(int width, int start, int end)
        {
            List<int> loop = new List<int>();
            for (int column = start; column <= end; column++) loop.Add(start * width + column);
            for (int row = start + 1; row <= end; row++) loop.Add(row * width + end);
            for (int column = end - 1; column >= start; column--) loop.Add(end * width + column);
            for (int row = end - 1; row > start; row--) loop.Add(row * width + start);
            return loop;
        }

        private static void AddSkirtLoop(List<Vector3> vertices, List<int> triangles, List<int> loop, float depth)
        {
            for (int index = 0; index < loop.Count; index++)
            {
                Vector3 first = vertices[loop[index]];
                Vector3 second = vertices[loop[(index + 1) % loop.Count]];
                int baseIndex = vertices.Count;
                vertices.Add(first);
                vertices.Add(second);
                vertices.Add(first + Vector3.down * depth);
                vertices.Add(second + Vector3.down * depth);
                triangles.Add(baseIndex); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 1);
            }
        }

        private static Dictionary<string, Mesh[]> AuthorAirportCombinedMeshes()
        {
            EnsureFolder(GeneratedArtFolder);
            GameObject model = LoadRequired<GameObject>(AirportModelPath);
            Dictionary<string, Mesh[]> result = new Dictionary<string, Mesh[]>(StringComparer.Ordinal);
            foreach (AirportPlacement placement in AirportPlacements)
            {
                Mesh[] levels = new Mesh[3];
                for (int lod = 0; lod < levels.Length; lod++)
                {
                    string sourceName = placement.assetId + "_LOD" + lod;
                    Transform source = FindDescendant(model.transform, sourceName);
                    if (source == null) throw new InvalidOperationException("Airport kit root is missing: " + sourceName);
                    List<CombineInstance> combines = new List<CombineInstance>();
                    foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh == null) continue;
                        Matrix4x4 matrix = source.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                        for (int subMesh = 0; subMesh < filter.sharedMesh.subMeshCount; subMesh++)
                            combines.Add(new CombineInstance { mesh = filter.sharedMesh, subMeshIndex = subMesh, transform = matrix });
                    }
                    if (combines.Count == 0) throw new InvalidOperationException("Airport kit LOD has no meshes: " + sourceName);
                    Mesh combined = new Mesh { name = sourceName + "_Combined", indexFormat = IndexFormat.UInt32 };
                    combined.CombineMeshes(combines.ToArray(), true, true, false);
                    combined.RecalculateBounds();
                    string assetPath = GeneratedArtFolder + "/" + combined.name + ".asset";
                    levels[lod] = WriteMeshAsset(assetPath, combined);
                }
                result.Add(placement.assetId, levels);
            }
            return result;
        }

        private static Mesh AuthorVegetationMesh(string name, bool highDetail)
        {
            EnsureFolder(GeneratedArtFolder);
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Color32> colors = new List<Color32>();
            Vector3[] offsets = { new Vector3(-3.2f, 0f, -1.7f), new Vector3(2.7f, 0f, 0.8f), new Vector3(0f, 0f, 3.6f) };
            float[] heights = { 6.4f, 7.2f, 5.8f };
            for (int index = 0; index < offsets.Length; index++)
            {
                float height = heights[index];
                if (highDetail)
                {
                    AddBox(vertices, triangles, colors, offsets[index] + new Vector3(0f, height * 0.23f, 0f),
                        new Vector3(0.38f, height * 0.46f, 0.38f), new Color32(94, 78, 55, 255));
                    Vector3 crownCenter = offsets[index] + new Vector3(0f, height * 0.68f, 0f);
                    AddDeciduousCrown(vertices, triangles, colors, crownCenter,
                        new Vector3(2.25f, height * 0.23f, 2.05f), 8, new Color32(74, 111, 58, 255));
                    AddDeciduousCrown(vertices, triangles, colors, crownCenter + new Vector3(-1.1f, -0.1f, 0.3f),
                        new Vector3(1.45f, height * 0.17f, 1.35f), 6, new Color32(88, 124, 65, 255));
                    AddDeciduousCrown(vertices, triangles, colors, crownCenter + new Vector3(1.0f, 0.15f, -0.35f),
                        new Vector3(1.35f, height * 0.16f, 1.45f), 6, new Color32(97, 132, 70, 255));
                }
                else
                {
                    AddDeciduousCrown(vertices, triangles, colors, offsets[index] + new Vector3(0f, height * 0.65f, 0f),
                        new Vector3(2.15f, height * 0.29f, 2.0f), 6, new Color32(80, 111, 60, 255));
                }
            }
            if (highDetail)
            {
                Vector3[] shrubCenters =
                {
                    new Vector3(-0.8f, 0.58f, -3.8f),
                    new Vector3(4.6f, 0.62f, 2.7f),
                    new Vector3(-4.7f, 0.54f, 2.4f)
                };
                foreach (Vector3 center in shrubCenters)
                    AddDeciduousCrown(vertices, triangles, colors, center,
                        new Vector3(1.05f, 0.58f, 0.86f), 5, new Color32(91, 111, 63, 255));
            }
            Mesh mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return WriteMeshAsset(GeneratedArtFolder + "/" + name + ".asset", mesh);
        }

        private static void AddDeciduousCrown(
            List<Vector3> vertices,
            List<int> triangles,
            List<Color32> colors,
            Vector3 center,
            Vector3 radius,
            int segments,
            Color32 color)
        {
            int start = vertices.Count;
            vertices.Add(center + Vector3.up * radius.y);
            vertices.Add(center - Vector3.up * radius.y);
            colors.Add(new Color32((byte)Mathf.Min(255, color.r + 12), (byte)Mathf.Min(255, color.g + 12),
                (byte)Mathf.Min(255, color.b + 12), color.a));
            colors.Add(new Color32((byte)Mathf.Max(0, color.r - 10), (byte)Mathf.Max(0, color.g - 10),
                (byte)Mathf.Max(0, color.b - 10), color.a));
            for (int index = 0; index < segments; index++)
            {
                float angle = index * Mathf.PI * 2f / segments;
                vertices.Add(center + new Vector3(
                    Mathf.Cos(angle) * radius.x,
                    Mathf.Sin(angle * 2f) * radius.y * 0.08f,
                    Mathf.Sin(angle) * radius.z));
                colors.Add(index % 2 == 0
                    ? color
                    : new Color32((byte)Mathf.Max(0, color.r - 5), (byte)Mathf.Max(0, color.g - 5),
                        (byte)Mathf.Max(0, color.b - 5), color.a));
            }
            for (int index = 0; index < segments; index++)
            {
                int current = start + 2 + index;
                int next = start + 2 + (index + 1) % segments;
                triangles.Add(start); triangles.Add(next); triangles.Add(current);
                triangles.Add(start + 1); triangles.Add(current); triangles.Add(next);
            }
        }

        private static void AddBox(
            List<Vector3> vertices,
            List<int> triangles,
            List<Color32> colors,
            Vector3 center,
            Vector3 size,
            Color32 color)
        {
            Vector3 half = size * 0.5f;
            int start = vertices.Count;
            vertices.AddRange(new[]
            {
                center + new Vector3(-half.x, -half.y, -half.z), center + new Vector3(half.x, -half.y, -half.z),
                center + new Vector3(half.x, -half.y, half.z), center + new Vector3(-half.x, -half.y, half.z),
                center + new Vector3(-half.x, half.y, -half.z), center + new Vector3(half.x, half.y, -half.z),
                center + new Vector3(half.x, half.y, half.z), center + new Vector3(-half.x, half.y, half.z)
            });
            for (int index = 0; index < 8; index++) colors.Add(color);
            int[] local =
            {
                0,4,5, 0,5,1, 1,5,6, 1,6,2, 2,6,7, 2,7,3,
                3,7,4, 3,4,0, 4,7,6, 4,6,5, 0,1,2, 0,2,3
            };
            triangles.AddRange(local.Select(index => start + index));
        }

        private static void AddOctahedron(
            List<Vector3> vertices,
            List<int> triangles,
            List<Color32> colors,
            Vector3 center,
            Vector3 radius,
            Color32 color)
        {
            int start = vertices.Count;
            vertices.Add(center + Vector3.up * radius.y);
            vertices.Add(center + Vector3.down * radius.y);
            vertices.Add(center + Vector3.right * radius.x);
            vertices.Add(center + Vector3.forward * radius.z);
            vertices.Add(center + Vector3.left * radius.x);
            vertices.Add(center + Vector3.back * radius.z);
            for (int index = 0; index < 6; index++) colors.Add(color);
            int[] local = { 0,3,2, 0,4,3, 0,5,4, 0,2,5, 1,2,3, 1,3,4, 1,4,5, 1,5,2 };
            triangles.AddRange(local.Select(index => start + index));
        }

        private static Mesh WriteMeshAsset(string path, Mesh generated)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return AssetDatabase.LoadAssetAtPath<Mesh>(path);
            }
            EditorUtility.CopySerialized(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static void ReplaceFarTerrainMesh(GameObject root, Mesh mesh)
        {
            Transform far = FindDescendant(root.transform, "Terrain_Far24km_Immutable_USGS");
            if (far == null) throw new InvalidOperationException("Immutable far terrain object is missing");
            far.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (far.GetComponent<LODGroup>() != null)
                throw new InvalidOperationException("Immutable far terrain must not have an LODGroup");
        }

        private static Mesh AuthorAirportPatchTransitionMesh(GameObject root)
        {
            MeshFilter airportPatch = RequiredMeshFilter(root, "Terrain_AirportPatch_USGS");
            MeshFilter near = RequiredMeshFilter(root, "Terrain_Near4km_USGS");
            Transform parent = airportPatch.transform.parent;
            if (parent == null || near.transform.parent != parent)
                throw new InvalidOperationException("Airport patch and near terrain must share one authored USGS_Terrain parent");

            Bounds patchBounds = airportPatch.GetComponent<Renderer>().bounds;
            if (!Approximately(patchBounds.min.x, -800f) || !Approximately(patchBounds.max.x, 800f) ||
                !Approximately(patchBounds.min.z, -300f) || !Approximately(patchBounds.max.z, 300f))
                throw new InvalidOperationException("Airport patch source bounds changed; measured transition contract is no longer valid");

            SurfaceHeightSampler patchSampler = new SurfaceHeightSampler(airportPatch);
            SurfaceHeightSampler nearSampler = new SurfaceHeightSampler(near);
            List<Vector3> vertices = new List<Vector3>(644);
            List<Vector2> uvs = new List<Vector2>(644);
            List<int> triangles = new List<int>(1920);
            float maximumHeightDelta = 0f;
            AuthorTransitionSide(+1f, parent, patchSampler, nearSampler, vertices, uvs, triangles, ref maximumHeightDelta);
            AuthorTransitionSide(-1f, parent, patchSampler, nearSampler, vertices, uvs, triangles, ref maximumHeightDelta);

            Mesh generated = new Mesh
            {
                name = "Terrain_AirportPatch_Near_Transition_Mesh",
                indexFormat = IndexFormat.UInt32
            };
            generated.SetVertices(vertices);
            generated.SetUVs(0, uvs);
            generated.SetTriangles(triangles, 0, true);
            generated.RecalculateNormals();
            generated.RecalculateBounds();
            if (generated.vertexCount != 644 || generated.triangles.Length / 3 != 640)
                throw new InvalidOperationException("Airport transition topology contract changed");
            if (maximumHeightDelta > 8f)
                throw new InvalidOperationException("Airport transition height delta is unsafe: " + maximumHeightDelta.ToString("F3") + " m");
            Debug.Log("[QuestFlight][StandaloneEnvironment] Airport patch transition authored across exact Z [300,320] m " +
                      "and [-320,-300] m gaps; max adjacent height delta=" + maximumHeightDelta.ToString("F3") + " m.");
            return WriteMeshAsset(AirportTransitionMeshPath, generated);
        }

        private static void AuthorTransitionSide(
            float sign,
            Transform parent,
            SurfaceHeightSampler patchSampler,
            SurfaceHeightSampler nearSampler,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            ref float maximumHeightDelta)
        {
            const int segments = 160;
            const float minimumX = -800f;
            const float spacing = 10f;
            float patchZ = sign * 300f;
            float nearZ = sign * 320f;
            int start = vertices.Count;
            for (int index = 0; index <= segments; index++)
            {
                float x = minimumX + index * spacing;
                float patchY = patchSampler.Sample(x, patchZ);
                float nearY = nearSampler.Sample(x, nearZ);
                maximumHeightDelta = Mathf.Max(maximumHeightDelta, Mathf.Abs(nearY - patchY));
                AddWorldVertex(parent, new Vector3(x, patchY, patchZ), vertices, uvs);
                AddWorldVertex(parent, new Vector3(x, nearY, nearZ), vertices, uvs);
            }
            for (int index = 0; index < segments; index++)
            {
                int patchA = start + index * 2;
                int nearA = patchA + 1;
                int patchB = patchA + 2;
                int nearB = patchA + 3;
                AddUpwardTriangle(vertices, triangles, patchA, nearA, nearB);
                AddUpwardTriangle(vertices, triangles, patchA, nearB, patchB);
            }
        }

        private static void AddWorldVertex(
            Transform parent,
            Vector3 world,
            ICollection<Vector3> vertices,
            ICollection<Vector2> uvs)
        {
            vertices.Add(parent.InverseTransformPoint(world));
            uvs.Add(new Vector2((world.x + 6000f) / 12000f, (world.z + 6000f) / 12000f));
        }

        private static void AddUpwardTriangle(
            IReadOnlyList<Vector3> vertices,
            ICollection<int> triangles,
            int a,
            int b,
            int c)
        {
            if (Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).y < 0f)
            {
                int swap = b;
                b = c;
                c = swap;
            }
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        private static void AuthorAirportPatchTransitionHierarchy(GameObject root, Mesh mesh)
        {
            MeshFilter airportPatch = RequiredMeshFilter(root, "Terrain_AirportPatch_USGS");
            Transform parent = airportPatch.transform.parent;
            ReplaceChild(parent, AirportTransitionObjectName);
            Transform transition = CreateChild(parent, AirportTransitionObjectName);
            MeshFilter filter = transition.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = transition.gameObject.AddComponent<MeshRenderer>();
            ConfigureRenderer(renderer, airportPatch.GetComponent<MeshRenderer>().sharedMaterial,
                receiveShadows: true, batchingStatic: true);
        }

        private static MeshFilter RequiredMeshFilter(GameObject root, string name)
        {
            MeshFilter filter = root.GetComponentsInChildren<MeshFilter>(true)
                .SingleOrDefault(candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));
            if (filter == null || filter.sharedMesh == null)
                throw new InvalidOperationException("Required terrain mesh filter is missing: " + name);
            return filter;
        }

        private static bool Approximately(float value, float expected) => Mathf.Abs(value - expected) <= 0.01f;

        private static void RemoveRedundantContextGeometry(GameObject root, bool wasPreviouslyAuthored)
        {
            Transform[] contextTransforms = root.GetComponentsInChildren<Transform>(true)
                .Where(transform => transform != root.transform &&
                    transform.name.StartsWith("Context_", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Transform[] roots = contextTransforms
                .Where(transform => transform.parent == null ||
                    !transform.parent.name.StartsWith("Context_", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            string materials = string.Join(", ", contextTransforms
                .SelectMany(transform => transform.GetComponents<Renderer>())
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null)
                .GroupBy(material => material.name, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group.Key + "=" + group.Count()));
            foreach (Transform contextRoot in roots) UnityEngine.Object.DestroyImmediate(contextRoot.gameObject);
            if (contextTransforms.Length < 10 && !wasPreviouslyAuthored)
                throw new InvalidOperationException(
                    "Expected the serialized OSM context layer before orthophoto replacement; removed " + contextTransforms.Length);
            Debug.Log(
                "[QuestFlight][StandaloneEnvironment] Removed redundant serialized orthophoto overlays: " +
                roots.Length + " root objects / " + contextTransforms.Length + " context transforms; materials=" +
                (string.IsNullOrEmpty(materials) ? "none (already authored)" : materials));
        }

        private static void AuthorAirportHierarchy(
            GameObject environmentRoot,
            Transform airportContext,
            IReadOnlyDictionary<string, Mesh[]> meshes,
            Material material)
        {
            Transform parent = CreateChild(airportContext, "AirportArtProduction");
            foreach (AirportPlacement placement in AirportPlacements)
            {
                Transform groupRoot = CreateChild(parent, placement.assetId);
                Vector3 position = placement.position;
                position.y = SampleTerrainHeight(environmentRoot, position.x, position.z) + 0.015f;
                // AirportContext is the explicit authored owner. Assign world transforms so
                // reparenting cannot shift the reviewed airport placements if its parent moves.
                groupRoot.position = position;
                groupRoot.rotation = Quaternion.Euler(0f, placement.yawDegrees, 0f);
                Renderer[] renderers = new Renderer[3];
                for (int lod = 0; lod < renderers.Length; lod++)
                {
                    Transform child = CreateChild(groupRoot, "LOD" + lod);
                    MeshFilter filter = child.gameObject.AddComponent<MeshFilter>();
                    filter.sharedMesh = meshes[placement.assetId][lod];
                    MeshRenderer renderer = child.gameObject.AddComponent<MeshRenderer>();
                    ConfigureRenderer(renderer, material, receiveShadows: true, batchingStatic: true);
                    renderers[lod] = renderer;
                }
                LODGroup lodGroup = groupRoot.gameObject.AddComponent<LODGroup>();
                lodGroup.fadeMode = LODFadeMode.None;
                lodGroup.animateCrossFading = false;
                lodGroup.SetLODs(new[]
                {
                    new LOD(0.58f, new[] { renderers[0] }),
                    new LOD(0.24f, new[] { renderers[1] }),
                    new LOD(0.06f, new[] { renderers[2] })
                });
                lodGroup.RecalculateBounds();
            }
        }

        private static void AuthorVegetationHierarchy(
            GameObject root,
            Mesh lod0,
            Mesh lod1,
            Material material)
        {
            Transform parent = CreateChild(root.transform, "SparseVegetationProduction");
            for (int index = 0; index < VegetationPlacements.Length; index++)
            {
                Vector3 position = VegetationPlacements[index];
                position.y = SampleTerrainHeight(root, position.x, position.z);
                Transform groupRoot = CreateChild(parent, "CottonwoodCluster_" + index.ToString("00"));
                groupRoot.localPosition = position;
                groupRoot.localRotation = Quaternion.Euler(0f, (index * 47f) % 360f, 0f);
                groupRoot.localScale = Vector3.one * (0.86f + (index % 4) * 0.07f);
                Renderer[] renderers = new Renderer[2];
                for (int lod = 0; lod < renderers.Length; lod++)
                {
                    Transform child = CreateChild(groupRoot, "LOD" + lod);
                    MeshFilter filter = child.gameObject.AddComponent<MeshFilter>();
                    filter.sharedMesh = lod == 0 ? lod0 : lod1;
                    MeshRenderer renderer = child.gameObject.AddComponent<MeshRenderer>();
                    ConfigureRenderer(renderer, material, receiveShadows: false, batchingStatic: false);
                    renderers[lod] = renderer;
                }
                LODGroup lodGroup = groupRoot.gameObject.AddComponent<LODGroup>();
                lodGroup.fadeMode = LODFadeMode.None;
                lodGroup.animateCrossFading = false;
                lodGroup.SetLODs(new[]
                {
                    new LOD(0.12f, new[] { renderers[0] }),
                    new LOD(0.025f, new[] { renderers[1] })
                });
                lodGroup.RecalculateBounds();
            }
        }

        private static void ConfigureRenderer(
            MeshRenderer renderer,
            Material material,
            bool receiveShadows,
            bool batchingStatic)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = receiveShadows;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            GameObjectUtility.SetStaticEditorFlags(
                renderer.gameObject,
                batchingStatic ? StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic : StaticEditorFlags.OccludeeStatic);
        }

        private static void ValidateAuthoredHierarchy(GameObject root, Material airportMaterial, Material vegetationMaterial)
        {
            Transform airport = FindDescendant(root.transform, "AirportArtProduction");
            Transform vegetation = FindDescendant(root.transform, "SparseVegetationProduction");
            Transform airportTransition = FindDescendant(root.transform, AirportTransitionObjectName);
            if (airport == null || vegetation == null) throw new InvalidOperationException("Authored art roots are missing");
            if (airportTransition == null) throw new InvalidOperationException("Airport terrain transition is missing");
            MeshFilter transitionFilter = airportTransition.GetComponent<MeshFilter>();
            MeshRenderer transitionRenderer = airportTransition.GetComponent<MeshRenderer>();
            MeshRenderer patchRenderer = RequiredMeshFilter(root, "Terrain_AirportPatch_USGS").GetComponent<MeshRenderer>();
            if (transitionFilter == null || transitionFilter.sharedMesh == null ||
                transitionFilter.sharedMesh.vertexCount != 644 || transitionFilter.sharedMesh.triangles.Length / 3 != 640)
                throw new InvalidOperationException("Airport terrain transition topology changed");
            if (transitionRenderer == null || transitionRenderer.sharedMaterial != patchRenderer.sharedMaterial)
                throw new InvalidOperationException("Airport terrain transition must share the macro-ground material");
            if (airportTransition.GetComponent<Collider>() != null || airportTransition.GetComponent<LODGroup>() != null)
                throw new InvalidOperationException("Airport terrain transition must remain immutable visual geometry");
            if (FindDescendant(root.transform, "SparseCottonwoodContext") != null)
                throw new InvalidOperationException("Legacy wide-area cottonwood mesh must not overlap the authored vegetation LODs");
            LODGroup[] airportGroups = airport.GetComponentsInChildren<LODGroup>(true);
            LODGroup[] vegetationGroups = vegetation.GetComponentsInChildren<LODGroup>(true);
            if (airportGroups.Length != 6) throw new InvalidOperationException("Airport must have exactly six LOD groups");
            if (vegetationGroups.Length != VegetationPlacements.Length)
                throw new InvalidOperationException("Sparse vegetation LOD group count changed");
            ValidateGroups(airportGroups, 3, airportMaterial, "airport");
            ValidateGroups(vegetationGroups, 2, vegetationMaterial, "vegetation");

            Transform far = FindDescendant(root.transform, "Terrain_Far24km_Immutable_USGS");
            if (far.GetComponent<LODGroup>() != null || far.GetComponentsInChildren<LODGroup>(true).Length != 0)
                throw new InvalidOperationException("Far terrain may not use LOD, crossfade, dither, or billboard behavior");
            MeshRenderer[] allRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
            if (allRenderers.Length > 120)
                throw new InvalidOperationException("Environment renderer component budget exceeded: " + allRenderers.Length + "/120");
        }

        private static void UpdateEnvironmentContract(GameObject root, Mesh farMesh)
        {
            ProductionEnvironmentRoot contract = root.GetComponent<ProductionEnvironmentRoot>();
            if (contract == null) throw new InvalidOperationException("ProductionEnvironmentRoot contract marker is missing");
            string topologyHash = TopologyHash(farMesh.vertexCount, farMesh.triangles);
            contract.farTerrainSourceVertexCount = farMesh.vertexCount;
            contract.farTerrainSourceIndexCount = (int)farMesh.GetIndexCount(0);
            contract.farTerrainSourceTopologyHash = topologyHash;
            contract.farTerrainBakedTopologyHash = topologyHash;
            contract.farTerrainSmoothingPassCount = 2;
            contract.uniqueMacroAlbedo = LoadRequired<Texture2D>(MidOrthophotoPath);
            contract.macroWorldMinimumMeters = new Vector2(-6000f, -6000f);
            contract.macroWorldSizeMeters = new Vector2(12000f, 12000f);
            contract.macroSource = "Bounded 2019 USGS/USDA NAIP public-domain near/mid derivatives; see kbdu_orthophoto_manifest.json";
            ProductionEnvironmentBudget budget = contract.CalculateBudget();
            contract.bakedRendererCount = budget.rendererCount;
            contract.bakedTriangleCount = budget.triangleCount;
            contract.bakedMaterialCount = budget.materialCount;
            contract.bakedTextureCount = 11;
            contract.bakeUtc = "2026-07-13T00:00:00Z";
            contract.bakeToolVersion = "standalone_environment_art_v2";
            EditorUtility.SetDirty(contract);
        }

        private static string TopologyHash(int vertexCount, IReadOnlyList<int> indices)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)vertexCount) * 16777619u;
                for (int index = 0; index < indices.Count; index++)
                    hash = (hash ^ (uint)indices[index]) * 16777619u;
                return hash.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private static void ValidateGroups(LODGroup[] groups, int levels, Material requiredMaterial, string label)
        {
            foreach (LODGroup group in groups)
            {
                if (group.fadeMode != LODFadeMode.None || group.animateCrossFading)
                    throw new InvalidOperationException(label + " LOD crossfade/dither is forbidden: " + group.name);
                LOD[] lods = group.GetLODs();
                if (lods.Length != levels) throw new InvalidOperationException(label + " LOD count changed: " + group.name);
                foreach (LOD lod in lods)
                {
                    if (lod.renderers.Length != 1 || lod.renderers[0] == null)
                        throw new InvalidOperationException(label + " must use exactly one combined renderer per active LOD: " + group.name);
                    Material[] materials = lod.renderers[0].sharedMaterials;
                    if (materials.Length != 1 || materials[0] != requiredMaterial)
                        throw new InvalidOperationException(label + " must use the one shared authored material: " + group.name);
                }
            }
        }

        private static float SampleTerrainHeight(GameObject root, float x, float z)
        {
            float bestDistance = float.PositiveInfinity;
            float bestHeight = 0f;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.name.IndexOf("Terrain_", StringComparison.Ordinal) < 0) continue;
                foreach (Vector3 local in filter.sharedMesh.vertices)
                {
                    Vector3 world = filter.transform.TransformPoint(local);
                    float dx = world.x - x;
                    float dz = world.z - z;
                    float distance = dx * dx + dz * dz;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    bestHeight = world.y;
                }
            }
            if (!float.IsFinite(bestDistance)) throw new InvalidOperationException("No authored terrain mesh could be sampled");
            return bestHeight;
        }

        private sealed class SurfaceHeightSampler
        {
            private readonly Vector3[] vertices;
            private readonly int[] triangles;

            public SurfaceHeightSampler(MeshFilter filter)
            {
                vertices = filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
                triangles = filter.sharedMesh.triangles;
            }

            public float Sample(float x, float z)
            {
                bool found = false;
                float sampledHeight = 0f;
                for (int offset = 0; offset < triangles.Length; offset += 3)
                {
                    Vector3 a = vertices[triangles[offset]];
                    Vector3 b = vertices[triangles[offset + 1]];
                    Vector3 c = vertices[triangles[offset + 2]];
                    if (Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y) <= 0.15f) continue;
                    float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(denominator) <= 0.000001f) continue;
                    float weightA = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / denominator;
                    float weightB = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / denominator;
                    float weightC = 1f - weightA - weightB;
                    const float epsilon = 0.0001f;
                    if (weightA < -epsilon || weightB < -epsilon || weightC < -epsilon) continue;
                    float height = weightA * a.y + weightB * b.y + weightC * c.y;
                    if (found && Mathf.Abs(height - sampledHeight) > 0.02f)
                        throw new InvalidOperationException(
                            $"Terrain surface is discontinuous at ({x:F3}, {z:F3}): {sampledHeight:F3} versus {height:F3} m");
                    sampledHeight = found ? (sampledHeight + height) * 0.5f : height;
                    found = true;
                }
                if (!found)
                    throw new InvalidOperationException($"Terrain surface has no horizontal coverage at ({x:F3}, {z:F3})");
                return sampledHeight;
            }
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException(typeof(T).Name + " asset is missing: " + path);
            return asset;
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void ReplaceChild(Transform root, string name)
        {
            Transform existing = FindDescendant(root, name);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDescendant(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static void EnsureFolder(string path)
        {
            string[] components = path.Split('/');
            string current = components[0];
            for (int index = 1; index < components.Length; index++)
            {
                string next = current + "/" + components[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, components[index]);
                current = next;
            }
        }

        [Serializable]
        private sealed class FarDemDocument
        {
            public FarDemGrid grid;
        }

        [Serializable]
        private sealed class FarDemGrid
        {
            public int width;
            public int height;
            public float spacing_meters;
            public float half_extent_meters;
            public float inner_half_extent_meters;
            public float skirt_depth_meters;
            public float quantization_meters;
            public string height_payload_base64;
        }

        private readonly struct AirportPlacement
        {
            public readonly string assetId;
            public readonly Vector3 position;
            public readonly float yawDegrees;

            public AirportPlacement(string assetId, Vector3 position, float yawDegrees)
            {
                this.assetId = assetId;
                this.position = position;
                this.yawDegrees = yawDegrees;
            }
        }
    }
}
