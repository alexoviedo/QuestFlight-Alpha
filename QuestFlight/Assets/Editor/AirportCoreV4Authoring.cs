using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Offline authoring for the bounded Airport Core V4 benchmark. All hierarchy and asset
    /// changes are serialized into project assets; no runtime construction or repair is added.
    /// </summary>
    public static class AirportCoreV4Authoring
    {
        private const string EnvironmentPath = ProductionEnvironmentRoot.PrefabAssetPath;
        private const string ContextPath =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_BatchedContext.prefab";
        private const string TargetV2Hlod = "buildings_0-2km_hlod_1500m_xn001_zn001";
        private const string GeneratedFolder =
            "Assets/Production/Environment/AirportCoreV4/Generated";
        private const string MaterialsFolder =
            "Assets/Production/Environment/AirportCoreV4/Materials";
        private const string ModelsFolder =
            "Assets/Production/Environment/AirportCoreV4/Buildings/Models";
        private const string SkyCubemapPath =
            "Assets/Production/Environment/Sky/Kloppenheim03_PureSky_2K.hdr";
        private const string SkyboxMaterialPath =
            "Assets/Production/Environment/Sky/Kloppenheim03_PureSky_Skybox.mat";
        private const string V2RunwayMaterialPath =
            "Assets/Production/Environment/Materials/Production_Runway_Asphalt.mat";
        private const string V2ReservoirMaterialPath =
            "Assets/Production/Environment/Materials/Production_BoulderReservoir_Opaque.mat";
        private const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        private const string WorldPrefabPath =
            "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
        private const int BenchmarkLayer = 29;
        private const string IntegrationUtc = "2026-07-17T19:30:00Z";

        private static readonly Rect TerminalExclusion = new Rect(-402f, -252f, 34f, 56f);
        private static readonly Rect HangarExclusion = new Rect(-406f, -385f, 27.5f, 81f);
        private static readonly Bounds BenchmarkBounds =
            new Bounds(new Vector3(-300f, 60f, -225f), new Vector3(700f, 180f, 700f));

        [MenuItem("QuestFlight/Product/Integrate Airport Core Photoreal Benchmark V4")]
        public static void RunInteractive() => Integrate(false);

        public static void RunBatch()
        {
            try
            {
                Integrate(false);
                Debug.Log("[QuestFlight][AirportCoreV4Authoring] SERIALIZED_INTEGRATION_SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void RunBakeBatch()
        {
            try
            {
                Integrate(true);
                Debug.Log("[QuestFlight][AirportCoreV4Authoring] SERIALIZED_INTEGRATION_AND_BAKE_SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void QuarantineRejectedBakeBatch()
        {
            try
            {
                PrepareNeutralLightingCalibration();
                Debug.Log("[QuestFlight][AirportCoreV4Authoring] REJECTED_BAKE_QUARANTINED");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Integrate(bool bake)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            PrepareNeutralLightingCalibration();
            EnsureFolder(GeneratedFolder);
            ConfigureTextures();
            Material terminal = ConfigureBuildingMaterial(
                MaterialsFolder + "/AirportCoreTerminalAtlas.mat", "AirportCoreTerminal");
            Material hangar = ConfigureBuildingMaterial(
                MaterialsFolder + "/AirportCoreHangarKitAtlas.mat", "AirportCoreHangarKit");
            Material runway = ConfigureRunwayMaterial();
            Material reservoir = ConfigureReservoirMaterial();
            Material vegetation = ConfigureVegetationMaterial();

            Dictionary<string, Mesh[]> models = BuildRuntimeMeshes();
            V2ContextCopies context = FilterV2Context();
            AirportCoreBenchmarkManifest manifest = BuildManifest(
                terminal, hangar, runway, reservoir, vegetation);
            V2SunState v2Sun = ConfigureWorldSun(manifest);
            IntegrateEnvironment(
                manifest, models, context, terminal, hangar, runway, reservoir, vegetation, v2Sun);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            if (bake) BakeBenchmarkAndFinalizeManifest(manifest);
        }

        private static void PrepareNeutralLightingCalibration()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Lightmapping.Clear();
            Lightmapping.lightingDataAsset = null;
            LightmapSettings.lightmaps = Array.Empty<LightmapData>();
            LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.48f, 0.58f, 0.72f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.30f, 0.34f, 0.38f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.17f, 0.15f, 0.12f, 1f);
            RenderSettings.ambientIntensity = 0.86f;
            Material skybox = LoadRequired<Material>(SkyboxMaterialPath);
            if (!skybox.HasProperty("_Rotation"))
                throw new InvalidOperationException("The selected skybox has no rotation contract.");
            skybox.SetFloat("_Rotation", -64.123046875f);
            EditorUtility.SetDirty(skybox);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            DeleteRejectedBakeAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void DeleteRejectedBakeAssets()
        {
            const string folder = "Assets/Scenes/ProductionVerticalSlice";
            string[] candidates = AssetDatabase.FindAssets(string.Empty, new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path =>
                {
                    string name = Path.GetFileName(path);
                    return string.Equals(name, "LightingData.asset", StringComparison.Ordinal) ||
                           string.Equals(name, "BAKE_MANIFEST.json", StringComparison.Ordinal) ||
                           name.StartsWith("Lightmap-", StringComparison.Ordinal);
                })
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            foreach (string path in candidates) AssetDatabase.DeleteAsset(path);
        }

        private static void ConfigureTextures()
        {
            ConfigureTexture(MaterialsFolder + "/AirportCoreTerminal_Albedo.png", true, false, 2048, 8);
            ConfigureTexture(MaterialsFolder + "/AirportCoreTerminal_Normal.png", false, true, 2048, 6);
            ConfigureTexture(MaterialsFolder + "/AirportCoreTerminal_MRAO.png", false, false, 512, 8);
            ConfigureTexture(MaterialsFolder + "/AirportCoreHangarKit_Albedo.png", true, false, 1024, 8);
            ConfigureTexture(MaterialsFolder + "/AirportCoreHangarKit_Normal.png", false, true, 1024, 6);
            ConfigureTexture(MaterialsFolder + "/AirportCoreHangarKit_MRAO.png", false, false, 512, 8);
            ConfigureTexture("Assets/Production/Environment/AirportCoreV4/CleanPlate/AirportCoreCleanPlate_Albedo.jpg",
                true, false, 1024, 8);
            ConfigureTexture("Assets/Production/Environment/AirportCoreV4/CleanPlate/AirportCoreReplacementMask.png",
                false, false, 512, 8);
            ConfigureTexture("Assets/Production/Environment/AirportCoreV4/CleanPlate/AirportCoreOriginalShadowMask.png",
                false, false, 512, 8);
            ConfigureTexture("Assets/Production/Environment/AirportCoreV4/Runway/AirportCoreRunway_Albedo.png",
                true, false, 512, 6, TextureWrapMode.Repeat);
            ConfigureTexture("Assets/Production/Environment/AirportCoreV4/Runway/AirportCoreRunway_Normal.png",
                false, true, 512, 6, TextureWrapMode.Repeat);
            ConfigureTexture("Assets/Production/Environment/AirportCoreV4/Runway/AirportCoreRunway_MRAO.png",
                false, false, 512, 6, TextureWrapMode.Repeat);
            ConfigureTexture("Assets/Production/Environment/AirportCoreV4/Runway/AirportCoreRunway_FeatureMask.png",
                false, false, 512, 8, TextureWrapMode.Clamp);
            ConfigureTexture("Assets/Production/Environment/AirportCoreV4/Water/Textures/AirportCoreReservoir_WaveA_NM.png",
                false, true, 256, 6, TextureWrapMode.Repeat);
            ConfigureTexture("Assets/Production/Environment/AirportCoreV4/Water/Textures/AirportCoreReservoir_WaveB_NM.png",
                false, true, 256, 6, TextureWrapMode.Repeat);
            ConfigureTexture("Assets/Production/Environment/AirportCoreV4/Water/Textures/AirportCoreReservoir_DepthShoreRoughnessSuppression.png",
                false, false, 512, 6, TextureWrapMode.Clamp);
            ConfigureTexture("Assets/Production/Environment/AirportCoreV4/Water/Textures/AirportCoreMinorWater_TerrainMask.png",
                false, false, 256, 6, TextureWrapMode.Clamp);
            ConfigureTexture("Assets/Production/Environment/AirportCoreV4/Vegetation/Textures/AirportCoreVegetation_Atlas.png",
                true, false, 512, 4, TextureWrapMode.Clamp);
        }

        public static void ApplyTextureBudgetOnly()
        {
            ConfigureTextures();
            AssetDatabase.SaveAssets();
            Debug.Log("[QuestFlight][AirportCoreV4Authoring] TEXTURE_BUDGET_APPLIED");
            EditorApplication.Exit(0);
        }

        public static void ApplyStage1BudgetIntegrationOnly()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ConfigureTextures();
                ConfigureGeneratedLightmapImports();
                GameObject environment = PrefabUtility.LoadPrefabContents(EnvironmentPath);
                try
                {
                    ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
                    AirportCoreBenchmarkRoot root = environment.GetComponentsInChildren<AirportCoreBenchmarkRoot>(true)
                        .SingleOrDefault();
                    if (contract == null || root == null || root.manifest == null || root.benchmarkContent == null)
                        throw new InvalidOperationException("Serialized V4 benchmark hierarchy is incomplete.");

                    Transform overlay = root.benchmarkContent.Find("AirportCoreCleanPlate");
                    if (overlay != null) UnityEngine.Object.DestroyImmediate(overlay.gameObject);
                    MeshRenderer reservoir = contract.boulderReservoirSurface == null
                        ? null
                        : contract.boulderReservoirSurface.GetComponent<MeshRenderer>();
                    MeshRenderer airportTerrain = contract.nearTerrainMeshes.FirstOrDefault()
                        ?.GetComponent<MeshRenderer>();
                    if (reservoir == null || airportTerrain == null)
                        throw new InvalidOperationException("Authoritative terrain or reservoir renderer is missing.");
                    ConfigureTerrainWater(airportTerrain.sharedMaterial, reservoir.bounds);
                    root.minorWaterTerrainEffect = airportTerrain;

                    Renderer[] benchmarkRenderers = root.benchmarkContent.GetComponentsInChildren<Renderer>(true);
                    root.manifest.serializedRendererCount = benchmarkRenderers.Length;
                    root.manifest.serializedTriangleCount = benchmarkRenderers.Sum(TriangleCount);
                    root.manifest.serializedMaterialCount = benchmarkRenderers
                        .SelectMany(item => item.sharedMaterials ?? Array.Empty<Material>())
                        .Where(item => item != null).Distinct().Count();
                    ProductionEnvironmentBudget budget = contract.CalculateBudget();
                    contract.bakedRendererCount = budget.rendererCount;
                    contract.bakedTriangleCount = budget.triangleCount;
                    contract.bakedMaterialCount = budget.materialCount;
                    EditorUtility.SetDirty(root.manifest);
                    EditorUtility.SetDirty(root);
                    EditorUtility.SetDirty(contract);
                    PrefabUtility.SaveAsPrefabAsset(environment, EnvironmentPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(environment);
                }

                AssetDatabase.DeleteAsset(GeneratedFolder + "/AirportCoreCleanPlateSurface.asset");
                AssetDatabase.DeleteAsset(MaterialsFolder + "/AirportCoreCleanPlate.mat");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[QuestFlight][AirportCoreV4Authoring] STAGE1_BUDGET_INTEGRATION_APPLIED");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void ConfigureGeneratedLightmapImports()
        {
            const string folder = "Assets/Scenes/ProductionVerticalSlice";
            string[] paths = AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => Path.GetFileName(path).StartsWith("Lightmap-", StringComparison.Ordinal) &&
                               path.IndexOf("_comp_", StringComparison.Ordinal) >= 0)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            string[] colorPaths = paths.Where(path =>
                    path.IndexOf("_comp_light", StringComparison.Ordinal) >= 0)
                .ToArray();
            string[] directionalPaths = paths.Where(path =>
                    path.IndexOf("_comp_dir", StringComparison.Ordinal) >= 0)
                .ToArray();
            if (colorPaths.Length != 1 || directionalPaths.Length != 0)
                throw new InvalidOperationException(
                    $"Expected exactly one non-directional lightmap, found colors={colorPaths.Length}, " +
                    $"directional={directionalPaths.Length}.");
            foreach (string path in colorPaths)
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Missing lightmap importer: " + path);
                importer.mipmapEnabled = true;
                importer.streamingMipmaps = true;
                TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.maxTextureSize = 1024;
                android.format = TextureImporterFormat.ASTC_HDR_6x6;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }
        }

        private static void ConfigureTexture(
            string path,
            bool srgb,
            bool normal,
            int maximumSize,
            int astcBlock,
            TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing texture importer: " + path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = true;
            importer.wrapMode = wrap;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = wrap == TextureWrapMode.Repeat ? 8 : 4;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = maximumSize;
            android.format = Astc(astcBlock);
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static TextureImporterFormat Astc(int block)
        {
            switch (block)
            {
                case 4: return TextureImporterFormat.ASTC_4x4;
                case 6: return TextureImporterFormat.ASTC_6x6;
                case 8: return TextureImporterFormat.ASTC_8x8;
                default: throw new ArgumentOutOfRangeException(nameof(block));
            }
        }

        private static Material ConfigureBuildingMaterial(string path, string prefix)
        {
            Shader shader = RequireShader("QuestFlight/AirportCoreV4/BakedLit");
            Material material = LoadOrCreateMaterial(path, shader);
            material.enableInstancing = true;
            material.SetTexture("_MainTex", LoadRequired<Texture2D>(MaterialsFolder + "/" + prefix + "_Albedo.png"));
            material.SetTexture("_NormalTex", LoadRequired<Texture2D>(MaterialsFolder + "/" + prefix + "_Normal.png"));
            material.SetTexture("_MRAOTex", LoadRequired<Texture2D>(MaterialsFolder + "/" + prefix + "_MRAO.png"));
            material.SetFloat("_NormalStrength", 0.45f);
            material.SetFloat("_SpecularStrength", 0.15f);
            material.SetFloat("_ReflectionStrength", prefix.Contains("Terminal") ? 0.10f : 0.08f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material ConfigureRunwayMaterial()
        {
            const string folder = "Assets/Production/Environment/AirportCoreV4/Runway";
            Material material = LoadOrCreateMaterial(folder + "/AirportCoreContinuousRunway.mat",
                RequireShader("QuestFlight/AirportCoreV4/ContinuousRunway"));
            material.enableInstancing = true;
            material.SetTexture("_MainTex", LoadRequired<Texture2D>(folder + "/AirportCoreRunway_Albedo.png"));
            material.SetTexture("_NormalTex", LoadRequired<Texture2D>(folder + "/AirportCoreRunway_Normal.png"));
            material.SetTexture("_MRAOTex", LoadRequired<Texture2D>(folder + "/AirportCoreRunway_MRAO.png"));
            material.SetTexture("_FeatureMask", LoadRequired<Texture2D>(folder + "/AirportCoreRunway_FeatureMask.png"));
            material.SetFloat("_WorldTiling", 0.025f);
            material.SetFloat("_SecondaryTiling", 0.008f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material ConfigureReservoirMaterial()
        {
            const string folder = "Assets/Production/Environment/AirportCoreV4/Water";
            Material material = LoadOrCreateMaterial(folder + "/Materials/AirportCoreReservoir_V4.mat",
                RequireShader("QuestFlight/Airport Core V4/Reservoir Opaque"));
            material.SetTexture("_WaveNormalA", LoadRequired<Texture2D>(
                "Assets/Production/Environment/Textures/Reservoir/BoulderReservoir_BroadWave_NM.png"));
            material.SetTexture("_WaveNormalB", LoadRequired<Texture2D>(
                "Assets/Production/Environment/Textures/Reservoir/BoulderReservoir_Ripple_NM.png"));
            material.SetTexture("_DepthShoreRoughnessSuppression",
                LoadRequired<Texture2D>(folder + "/Textures/AirportCoreReservoir_DepthShoreRoughnessSuppression.png"));
            material.SetTexture("_EnvironmentCube", LoadRequired<Texture>(SkyCubemapPath));
            float skyRadians = -64.123046875f * Mathf.Deg2Rad;
            material.SetVector("_EnvironmentRotationSinCos",
                new Vector4(Mathf.Sin(skyRadians), Mathf.Cos(skyRadians), 0f, 0f));
            material.SetFloat("_WaveMetersA", 320f);
            material.SetFloat("_WaveMetersB", 48f);
            material.SetFloat("_WaveStrengthA", 0.06f);
            material.SetFloat("_WaveStrengthB", 0.025f);
            material.SetFloat("_SunGlitter", 0.05f);
            material.SetFloat("_ShoreBandStrength", 0.04f);
            material.SetColor("_ShoreBandColor", new Color(0.34f, 0.39f, 0.37f, 1f));
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material ConfigureVegetationMaterial()
        {
            const string folder = "Assets/Production/Environment/AirportCoreV4/Vegetation";
            Material material = LoadOrCreateMaterial(folder + "/Materials/AirportCoreVegetation_V4.mat",
                RequireShader("QuestFlight/Airport Core V4/Vegetation Hero Cutout"));
            material.SetTexture("_MainTex", LoadRequired<Texture2D>(folder + "/Textures/AirportCoreVegetation_Atlas.png"));
            material.SetFloat("_Cutoff", 0.52f);
            material.SetFloat("_BackfaceLight", 0.45f);
            material.SetFloat("_BakedAtlasStrength", 0.15f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Dictionary<string, Mesh[]> BuildRuntimeMeshes()
        {
            Dictionary<string, Mesh[]> result = new Dictionary<string, Mesh[]>(StringComparer.Ordinal)
            {
                ["ExecutiveTerminal"] = BuildThreeGlbLods("ExecutiveTerminal"),
                ["Type2THangarRow"] = BuildThreeGlbLods("Type2THangarRow"),
                ["FuelServiceCore"] = BuildThreeGlbLods("FuelServiceCore"),
                ["TieDown"] = BuildThreeGlbLods("TieDown"),
                ["Cone"] = BuildThreeGlbLods("Cone"),
                ["LightPole"] = BuildThreeGlbLods("LightPole"),
                ["ServiceCart"] = BuildThreeGlbLods("ServiceCart"),
                ["Fence"] = BuildThreeGlbLods("Fence"),
                ["Signboard"] = BuildThreeGlbLods("Signboard")
            };

            GameObject vegetationSource = LoadRequired<GameObject>(
                "Assets/Production/Environment/AirportCoreV4/Vegetation/Models/AirportCoreVegetationCluster.fbx");
            result["Vegetation"] = Enumerable.Range(0, 3)
                .Select(index => SaveMesh(
                    CombineModelMeshes(vegetationSource, "LOD" + index,
                        "AirportCoreVegetationCluster_LOD" + index),
                    GeneratedFolder + "/AirportCoreVegetationCluster_LOD" + index + ".asset"))
                .ToArray();
            return result;
        }

        private static Mesh[] BuildThreeGlbLods(string asset)
        {
            Mesh[] result = new Mesh[3];
            for (int lod = 0; lod < 3; lod++)
            {
                string path = ModelsFolder + "/" + asset + "_LOD" + lod + ".glb";
                GameObject source = LoadRequired<GameObject>(path);
                Mesh mesh = CombineModelMeshes(source, null, asset + "_LOD" + lod + "_V4");
                result[lod] = SaveMesh(mesh, GeneratedFolder + "/" + asset + "_LOD" + lod + "_V4.asset");
            }
            return result;
        }

        private static Mesh[] BuildCombinedApronProps(
            ProductionTerrainSurfaceAsset surface,
            IReadOnlyDictionary<string, Mesh[]> models)
        {
            PlacedProp[] placements =
            {
                new PlacedProp("TieDown", Ground(surface, -354f, -239f), Quaternion.Euler(0f, 90f, 0f)),
                new PlacedProp("Cone", Ground(surface, -366f, -235f), Quaternion.identity),
                new PlacedProp("ServiceCart", Ground(surface, -345f, -230f), Quaternion.Euler(0f, 20f, 0f)),
                new PlacedProp("LightPole", Ground(surface, -414f, -213f), Quaternion.identity),
                new PlacedProp("Fence", Ground(surface, -430f, -270f), Quaternion.Euler(0f, 90f, 0f)),
                new PlacedProp("Signboard", Ground(surface, -405f, -211f), Quaternion.Euler(0f, 90f, 0f))
            };
            Mesh[] result = new Mesh[3];
            for (int lod = 0; lod < 3; lod++)
            {
                List<CombineInstance> instances = new List<CombineInstance>();
                foreach (PlacedProp placement in placements)
                {
                    Mesh mesh = models[placement.asset][lod];
                    Matrix4x4 matrix = Matrix4x4.TRS(placement.position, placement.rotation, Vector3.one);
                    for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                        instances.Add(new CombineInstance
                        {
                            mesh = mesh,
                            subMeshIndex = submesh,
                            transform = matrix
                        });
                }
                Mesh combined = new Mesh
                {
                    name = "AirportCorePropsCombined_LOD" + lod,
                    indexFormat = IndexFormat.UInt32
                };
                combined.CombineMeshes(instances.ToArray(), true, true, false);
                combined.RecalculateBounds();
                if (combined.normals == null || combined.normals.Length != combined.vertexCount)
                    combined.RecalculateNormals();
                if (combined.tangents == null || combined.tangents.Length != combined.vertexCount)
                    combined.RecalculateTangents();
                result[lod] = SaveMesh(combined,
                    GeneratedFolder + "/AirportCorePropsCombined_LOD" + lod + ".asset");
            }
            return result;
        }

        private static Mesh CombineModelMeshes(GameObject source, string requiredNameToken, string name)
        {
            MeshFilter[] filters = source.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.sharedMesh != null &&
                    (string.IsNullOrEmpty(requiredNameToken) ||
                     filter.name.IndexOf(requiredNameToken, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     filter.transform.parent != null &&
                     filter.transform.parent.name.IndexOf(requiredNameToken, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToArray();
            if (filters.Length == 0)
                throw new InvalidOperationException("No model meshes matched " + name);
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
            Mesh combined = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            combined.CombineMeshes(instances.ToArray(), true, true, false);
            combined.RecalculateBounds();
            if (combined.normals == null || combined.normals.Length != combined.vertexCount) combined.RecalculateNormals();
            if (combined.tangents == null || combined.tangents.Length != combined.vertexCount) combined.RecalculateTangents();
            if (combined.uv2 == null || combined.uv2.Length != combined.vertexCount)
                Unwrapping.GenerateSecondaryUVSet(combined);
            return combined;
        }

        private static V2ContextCopies FilterV2Context()
        {
            string[] priorOriginalPaths = LoadPriorOriginalContextMeshPaths();
            GameObject context = PrefabUtility.LoadPrefabContents(ContextPath);
            try
            {
                Transform target = FindRequired(context.transform, TargetV2Hlod);
                LODGroup group = target.GetComponent<LODGroup>();
                if (group == null || group.GetLODs().Length != 3)
                    throw new InvalidOperationException("Target V2 HLOD group is missing its three LODs.");
                Mesh[] original = new Mesh[3];
                Mesh[] filtered = new Mesh[3];
                Material[] materials = new Material[3];
                Matrix4x4[] childMatrices = new Matrix4x4[3];
                LOD[] lods = group.GetLODs();
                for (int index = 0; index < 3; index++)
                {
                    if (lods[index].renderers.Length != 1)
                        throw new InvalidOperationException("Target V2 HLOD must have exactly one renderer per LOD.");
                    MeshRenderer renderer = lods[index].renderers[0] as MeshRenderer;
                    MeshFilter filter = renderer == null ? null : renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                        throw new InvalidOperationException("Target V2 HLOD LOD mesh is missing.");
                    original[index] = priorOriginalPaths.Length == 3
                        ? LoadRequired<Mesh>(priorOriginalPaths[index])
                        : filter.sharedMesh;
                    materials[index] = renderer.sharedMaterial;
                    childMatrices[index] = target.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                    Mesh candidate = FilterTriangles(
                        original[index],
                        childMatrices[index],
                        new[] { TerminalExclusion, HangarExclusion },
                        TargetV2Hlod + "_Filtered_LOD" + index);
                    if (candidate.triangles.Length >= original[index].triangles.Length)
                        throw new InvalidOperationException("V2 duplicate filter removed no triangles at LOD" + index + ".");
                    filtered[index] = SaveMesh(candidate,
                        GeneratedFolder + "/" + TargetV2Hlod + "_Filtered_LOD" + index + ".asset");
                    filter.sharedMesh = filtered[index];
                    EditorUtility.SetDirty(filter);
                }
                target.gameObject.SetActive(true);
                PrefabUtility.SaveAsPrefabAsset(context, ContextPath);
                return new V2ContextCopies(original, filtered, materials, childMatrices,
                    group.size, group.localReferencePoint,
                    original.Select(AssetDatabase.GetAssetPath).ToArray(),
                    filtered.Select(AssetDatabase.GetAssetPath).ToArray());
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(context);
            }
        }

        private static string[] LoadPriorOriginalContextMeshPaths()
        {
            GameObject environment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            AirportCoreBenchmarkRoot prior = environment == null
                ? null
                : environment.GetComponentsInChildren<AirportCoreBenchmarkRoot>(true).SingleOrDefault();
            if (prior?.v2ContextOriginalMeshPaths == null ||
                prior.v2ContextOriginalMeshPaths.Length != 3 ||
                prior.v2ContextOriginalMeshPaths.Any(string.IsNullOrWhiteSpace))
                return Array.Empty<string>();
            return prior.v2ContextOriginalMeshPaths.ToArray();
        }

        private static Mesh FilterTriangles(Mesh source, Matrix4x4 meshToGroup, Rect[] exclusions, string name)
        {
            Vector3[] vertices = source.vertices;
            Mesh result = UnityEngine.Object.Instantiate(source);
            result.name = name;
            result.subMeshCount = source.subMeshCount;
            int removed = 0;
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                int[] input = source.GetTriangles(submesh);
                List<int> output = new List<int>(input.Length);
                for (int index = 0; index < input.Length; index += 3)
                {
                    Vector3 a = meshToGroup.MultiplyPoint3x4(vertices[input[index]]);
                    Vector3 b = meshToGroup.MultiplyPoint3x4(vertices[input[index + 1]]);
                    Vector3 c = meshToGroup.MultiplyPoint3x4(vertices[input[index + 2]]);
                    Vector2 center = new Vector2((a.x + b.x + c.x) / 3f, (a.z + b.z + c.z) / 3f);
                    bool exclude = exclusions.Any(rect => rect.Contains(center) ||
                        rect.Contains(new Vector2(a.x, a.z)) || rect.Contains(new Vector2(b.x, b.z)) ||
                        rect.Contains(new Vector2(c.x, c.z)));
                    if (exclude) removed++;
                    else
                    {
                        output.Add(input[index]);
                        output.Add(input[index + 1]);
                        output.Add(input[index + 2]);
                    }
                }
                result.SetTriangles(output, submesh, false);
            }
            result.RecalculateBounds();
            if (removed == 0) throw new InvalidOperationException(
                "No triangles intersected the V4 replacement masks for " + name + ".");
            return result;
        }

        private static AirportCoreBenchmarkManifest BuildManifest(
            Material terminal, Material hangar, Material runway, Material reservoir, Material vegetation)
        {
            AirportCoreBenchmarkManifest manifest = AssetDatabase.LoadAssetAtPath<AirportCoreBenchmarkManifest>(
                AirportCoreBenchmarkManifest.AssetPath);
            if (manifest == null)
            {
                manifest = ScriptableObject.CreateInstance<AirportCoreBenchmarkManifest>();
                AssetDatabase.CreateAsset(manifest, AirportCoreBenchmarkManifest.AssetPath);
            }
            manifest.stableId = AirportCoreBenchmarkManifest.StableId;
            manifest.sourceV2Sha = AirportCoreBenchmarkManifest.SourceV2Sha;
            manifest.referenceV3Sha = AirportCoreBenchmarkManifest.ReferenceV3Sha;
            manifest.benchmarkBounds = BenchmarkBounds;
            manifest.replacementSourceFeatureIds = new[] { "osm:77434544", "osm:78054463" };
            manifest.sourceAndLicenseManifests = new[]
            {
                LoadRequired<TextAsset>("Assets/Production/Environment/AirportCoreV4/Buildings/AirportCoreBuildingSourceManifestV4.json"),
                LoadRequired<TextAsset>(MaterialsFolder + "/SOURCE_MANIFEST.json"),
                LoadRequired<TextAsset>("Assets/Production/Environment/AirportCoreV4/CleanPlate/SOURCE_MANIFEST.json"),
                LoadRequired<TextAsset>("Assets/Production/Environment/AirportCoreV4/Runway/SOURCE_MANIFEST.json"),
                LoadRequired<TextAsset>("Assets/Production/Environment/AirportCoreV4/Water/Reports/AirportCoreWaterVegetation_SourceManifest.json")
            };
            manifest.buildingValidationReport = LoadRequired<TextAsset>(
                "Assets/Production/Environment/AirportCoreV4/Buildings/Data/AirportCoreBuildingValidationV4.json");
            manifest.materialsValidationReport = LoadRequired<TextAsset>(MaterialsFolder + "/SOURCE_MANIFEST.json");
            manifest.waterVegetationValidationReport = LoadRequired<TextAsset>(
                "Assets/Production/Environment/AirportCoreV4/Water/Reports/AirportCoreWaterVegetation_ErrorReport.json");
            manifest.terminalAlbedo = LoadRequired<Texture2D>(MaterialsFolder + "/AirportCoreTerminal_Albedo.png");
            manifest.terminalNormal = LoadRequired<Texture2D>(MaterialsFolder + "/AirportCoreTerminal_Normal.png");
            manifest.terminalMrao = LoadRequired<Texture2D>(MaterialsFolder + "/AirportCoreTerminal_MRAO.png");
            manifest.hangarKitAlbedo = LoadRequired<Texture2D>(MaterialsFolder + "/AirportCoreHangarKit_Albedo.png");
            manifest.hangarKitNormal = LoadRequired<Texture2D>(MaterialsFolder + "/AirportCoreHangarKit_Normal.png");
            manifest.hangarKitMrao = LoadRequired<Texture2D>(MaterialsFolder + "/AirportCoreHangarKit_MRAO.png");
            manifest.cleanPlateAlbedo = LoadRequired<Texture2D>(
                "Assets/Production/Environment/AirportCoreV4/CleanPlate/AirportCoreCleanPlate_Albedo.jpg");
            manifest.replacementMask = LoadRequired<Texture2D>(
                "Assets/Production/Environment/AirportCoreV4/CleanPlate/AirportCoreReplacementMask.png");
            manifest.originalShadowMask = LoadRequired<Texture2D>(
                "Assets/Production/Environment/AirportCoreV4/CleanPlate/AirportCoreOriginalShadowMask.png");
            manifest.reflectionCubemap = LoadRequired<Texture>(SkyCubemapPath);
            manifest.terminalMaterial = terminal;
            manifest.hangarKitMaterial = hangar;
            manifest.runwayMaterial = runway;
            manifest.reservoirMaterial = reservoir;
            manifest.vegetationMaterial = vegetation;
            manifest.sunAzimuthDegrees = 152f;
            manifest.sunElevationDegrees = 72f;
            manifest.sunCalibrationConfidence = 0.436f;
            manifest.sunColor = new Color(1f, 0.966f, 0.92f, 1f);
            manifest.sunIntensity = 1f;
            manifest.ambientColor = new Color(0.54f, 0.61f, 0.68f, 1f);
            manifest.ambientIntensity = 1f;
            manifest.skyRotationDegrees = -64.123f;
            manifest.bakedLightmapCount = 0;
            manifest.bakedLightmapResolution = 0;
            manifest.estimatedLightmapBytes = 0;
            manifest.serializedLightProbeCount = 18;
            manifest.bakedReflectionProbeCount = 0;
            manifest.estimatedReflectionBytes = 0;
            manifest.cleanPlateCoversEveryReplacement = true;
            manifest.photographedShadowsReconciled = true;
            manifest.runwayMacroPatternRemoved = true;
            manifest.minorWaterUsesTerrainMask = true;
            manifest.rawSourcesExcluded = true;
            manifest.serializedReplacementCount = 2;
            manifest.integrationUtc = IntegrationUtc;
            EditorUtility.SetDirty(manifest);
            return manifest;
        }

        private static void IntegrateEnvironment(
            AirportCoreBenchmarkManifest manifest,
            IReadOnlyDictionary<string, Mesh[]> models,
            V2ContextCopies context,
            Material terminalMaterial,
            Material hangarMaterial,
            Material runwayMaterial,
            Material reservoirMaterial,
            Material vegetationMaterial,
            V2SunState v2Sun)
        {
            ProductionTerrainSurfaceAsset surface = LoadRequired<ProductionTerrainSurfaceAsset>(
                "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset");
            GameObject environment = PrefabUtility.LoadPrefabContents(EnvironmentPath);
            try
            {
                ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
                if (contract == null || contract.airportContext == null)
                    throw new InvalidOperationException("Authored production environment/airport context is missing.");
                Transform genericAirportArt = FindDescendant(environment.transform, "AirportArtProduction");
                if (genericAirportArt != null) UnityEngine.Object.DestroyImmediate(genericAirportArt.gameObject);
                Transform prior = FindDescendant(environment.transform, AirportCoreBenchmarkRoot.GameObjectName);
                if (prior != null) UnityEngine.Object.DestroyImmediate(prior.gameObject);

                GameObject rootObject = new GameObject(AirportCoreBenchmarkRoot.GameObjectName);
                rootObject.transform.SetParent(contract.airportContext, false);
                AirportCoreBenchmarkRoot root = rootObject.AddComponent<AirportCoreBenchmarkRoot>();
                root.validateOnAwake = true;
                root.manifest = manifest;

                Transform content = CreateChild(rootObject.transform, "BenchmarkContent", true);
                Transform filteredContext = FindRequired(environment.transform, TargetV2Hlod);
                LODGroup filtered = filteredContext.GetComponent<LODGroup>();
                if (filtered == null || filtered.GetLODs().Length != 3)
                    throw new InvalidOperationException("The active filtered V2 context LODGroup is missing.");
                root.benchmarkContent = content;
                root.filteredV2Context = filtered;
                root.v2ContextOriginalMeshPaths = context.originalPaths;
                root.v4ContextFilteredMeshPaths = context.filteredPaths;

                AssetDatabase.DeleteAsset(GeneratedFolder + "/AirportCoreCleanPlateSurface.asset");
                AssetDatabase.DeleteAsset(MaterialsFolder + "/AirportCoreCleanPlate.mat");

                root.executiveTerminal = CreateLodAsset(content, "ExecutiveTerminal_3335", models["ExecutiveTerminal"],
                    terminalMaterial, Ground(surface, -385.25f, -224.125f), Quaternion.identity, true);
                root.type2HangarRow = CreateLodAsset(content, "Type2THangarRow_3354", models["Type2THangarRow"],
                    hangarMaterial, Ground(surface, -392.25f, -344.875f), Quaternion.identity, true);

                root.fuelServiceContext = CreateLodAsset(content, "FuelServiceCore",
                    models["FuelServiceCore"], hangarMaterial,
                    Ground(surface, -112f, -205f), Quaternion.identity, false).transform;
                Mesh[] combinedProps = BuildCombinedApronProps(surface, models);
                root.authoredApronProps = CreateLodAsset(content, "AuthoredApronProps", combinedProps,
                    hangarMaterial, Vector3.zero, Quaternion.identity, false).transform;

                root.vegetationHeroCluster = CreateLodAsset(content, "VegetationHeroCluster", models["Vegetation"],
                    vegetationMaterial, Ground(surface, -435f, -215f), Quaternion.Euler(0f, 18f, 0f), false).transform;

                MeshRenderer runway = contract.runwayPavement == null
                    ? null
                    : contract.runwayPavement.GetComponent<MeshRenderer>();
                MeshRenderer reservoir = contract.boulderReservoirSurface == null
                    ? null
                    : contract.boulderReservoirSurface.GetComponent<MeshRenderer>();
                if (runway == null || reservoir == null)
                    throw new InvalidOperationException("Authoritative runway or Boulder Reservoir renderer is missing.");
                root.v2RunwayMaterial = LoadRequired<Material>(V2RunwayMaterialPath);
                root.v2ReservoirMaterial = LoadRequired<Material>(V2ReservoirMaterialPath);
                runway.sharedMaterial = runwayMaterial;
                reservoir.sharedMaterial = reservoirMaterial;
                root.authoritativeRunway = runway;
                root.boulderReservoir = reservoir;

                MeshRenderer airportTerrain = contract.nearTerrainMeshes.FirstOrDefault()
                    ?.GetComponent<MeshRenderer>();
                if (airportTerrain == null)
                    throw new InvalidOperationException("Airport terrain renderer is missing for minor-water binding.");
                ConfigureTerrainWater(airportTerrain.sharedMaterial, reservoir.bounds);
                root.minorWaterTerrainEffect = airportTerrain;
                ConfigureStableShoreAndGi(contract, airportTerrain.sharedMaterial);

                Transform lighting = CreateChild(rootObject.transform, "BenchmarkLighting", true);
                Light sun = CreateBakedSun(lighting, v2Sun);
                sun.enabled = false;
                root.benchmarkSun = sun;
                root.v2SunRotation = v2Sun.rotation;
                root.v2SunColor = v2Sun.color;
                root.v2SunIntensity = v2Sun.intensity;
                root.airportLightProbes = CreateLightProbes(lighting);
                root.airportReflectionProbe = null;

                Renderer[] benchmarkRenderers = content.GetComponentsInChildren<Renderer>(true);
                manifest.serializedRendererCount = benchmarkRenderers.Length;
                manifest.serializedTriangleCount = benchmarkRenderers.Sum(TriangleCount);
                manifest.serializedMaterialCount = benchmarkRenderers
                    .SelectMany(item => item.sharedMaterials ?? Array.Empty<Material>())
                    .Where(item => item != null).Distinct().Count();
                manifest.estimatedTextureBytes = 29L * 1024L * 1024L;
                EditorUtility.SetDirty(manifest);

                ProductionEnvironmentBudget budget = contract.CalculateBudget();
                contract.bakedRendererCount = budget.rendererCount;
                contract.bakedTriangleCount = budget.triangleCount;
                contract.bakedMaterialCount = budget.materialCount;
                contract.bakeToolVersion = "airport_core_photoreal_v4_stage1";
                contract.bakeUtc = IntegrationUtc;
                EditorUtility.SetDirty(contract);
                PrefabUtility.SaveAsPrefabAsset(environment, EnvironmentPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(environment);
            }
        }

        private static void ConfigureTerrainWater(Material material, Bounds reservoirBounds)
        {
            if (material == null || !material.HasProperty("_AirportCoreMinorWaterMask"))
                throw new InvalidOperationException("Terrain shader has not imported the V4 water-mask contract.");
            if (!material.HasProperty("_AirportCoreCleanPlateAlbedo") ||
                !material.HasProperty("_AirportCoreCleanPlateMask"))
                throw new InvalidOperationException("Terrain shader has not imported the V4 clean-plate contract.");
            material.SetTexture("_AirportCoreCleanPlateAlbedo", LoadRequired<Texture2D>(
                "Assets/Production/Environment/AirportCoreV4/CleanPlate/AirportCoreCleanPlate_Albedo.jpg"));
            material.SetTexture("_AirportCoreCleanPlateMask", LoadRequired<Texture2D>(
                "Assets/Production/Environment/AirportCoreV4/CleanPlate/AirportCoreReplacementMask.png"));
            material.SetVector("_AirportCoreCleanPlateWorldMinInvSize",
                new Vector4(-650f, -575f, 1f / 700f, 1f / 700f));
            material.SetFloat("_AirportCoreCleanPlateStrength", 1f);
            Texture2D packed = LoadRequired<Texture2D>(
                "Assets/Production/Environment/AirportCoreV4/Water/Textures/AirportCoreReservoir_DepthShoreRoughnessSuppression.png");
            Texture2D minor = LoadRequired<Texture2D>(
                "Assets/Production/Environment/AirportCoreV4/Water/Textures/AirportCoreMinorWater_TerrainMask.png");
            material.SetTexture("_AirportCoreReservoirMask", packed);
            material.SetVector("_AirportCoreReservoirCenterExtent", new Vector4(
                reservoirBounds.center.x, reservoirBounds.center.z,
                Mathf.Max(1f, reservoirBounds.extents.x), Mathf.Max(1f, reservoirBounds.extents.z)));
            material.SetFloat("_AirportCoreReservoirSuppressionStrength", 1f);
            material.SetTexture("_AirportCoreMinorWaterMask", minor);
            // Bounded drainage/wet-channel prototype; this is an authored benchmark UV binding,
            // not a claim of survey-grade hydrology.
            material.SetVector("_AirportCoreMinorWaterWorldMinInvSize",
                new Vector4(-610f, -525f, 1f / 110f, 1f / 70f));
            material.SetFloat("_AirportCoreMinorWaterStrength", 0.72f);
            material.SetColor("_AirportCoreMinorWaterReflectionColor", new Color(0.44f, 0.56f, 0.63f, 1f));
            EditorUtility.SetDirty(material);
        }

        private static void ConfigureStableShoreAndGi(ProductionEnvironmentRoot contract, Material terrainMaterial)
        {
            foreach (MeshFilter bank in new[]
                     {
                         contract.boulderReservoirShoreBank,
                         contract.otherWaterFeaturesShoreBankV2
                     }.Where(item => item != null))
            {
                MeshRenderer renderer = bank.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = terrainMaterial;
                    ClearContributeGi(renderer);
                    EditorUtility.SetDirty(renderer);
                }
                bank.gameObject.SetActive(false);
            }
            if (contract.otherWaterFeaturesSurfaceV2 != null)
                ClearContributeGi(contract.otherWaterFeaturesSurfaceV2.GetComponent<MeshRenderer>());
        }

        private static void ClearContributeGi(MeshRenderer renderer)
        {
            if (renderer == null) return;
            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
            flags &= ~StaticEditorFlags.ContributeGI;
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags);
            renderer.scaleInLightmap = 0f;
            renderer.receiveGI = ReceiveGI.LightProbes;
            EditorUtility.SetDirty(renderer);
        }

        private static V2SunState ConfigureWorldSun(AirportCoreBenchmarkManifest manifest)
        {
            GameObject world = PrefabUtility.LoadPrefabContents(WorldPrefabPath);
            try
            {
                Light[] directional = world.GetComponentsInChildren<Light>(true)
                    .Where(item => item.type == LightType.Directional &&
                                   !string.Equals(item.name, "AirportCoreBakedSun", StringComparison.Ordinal))
                    .ToArray();
                if (directional.Length != 1)
                    throw new InvalidOperationException(
                        "Production world must serialize exactly one global directional sun.");
                Light sun = directional[0];
                GameObject priorEnvironment = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
                AirportCoreBenchmarkRoot priorRoot = priorEnvironment == null
                    ? null
                    : priorEnvironment.GetComponentsInChildren<AirportCoreBenchmarkRoot>(true).SingleOrDefault();
                V2SunState original = priorRoot != null && priorRoot.v2SunIntensity > 0f
                    ? new V2SunState(priorRoot.v2SunRotation, priorRoot.v2SunColor,
                        priorRoot.v2SunIntensity, sun.cullingMask | (1 << BenchmarkLayer))
                    : new V2SunState(
                        sun.transform.rotation, sun.color, Mathf.Max(sun.intensity, 0.01f), sun.cullingMask);
                sun.transform.rotation = original.rotation;
                sun.color = original.color;
                sun.intensity = original.intensity;
                sun.cullingMask = original.cullingMask | (1 << BenchmarkLayer);
                Vector3 towardSun = -(original.rotation * Vector3.forward);
                float azimuth = Mathf.Atan2(towardSun.x, towardSun.z) * Mathf.Rad2Deg;
                if (azimuth < 0f) azimuth += 360f;
                manifest.sunAzimuthDegrees = azimuth;
                manifest.sunElevationDegrees = Mathf.Asin(Mathf.Clamp(towardSun.y, -1f, 1f)) * Mathf.Rad2Deg;
                manifest.sunCalibrationConfidence = 0.01f;
                manifest.sunColor = original.color;
                manifest.sunIntensity = original.intensity;
                manifest.ambientColor = RenderSettings.ambientSkyColor;
                manifest.ambientIntensity = RenderSettings.ambientIntensity;
                EditorUtility.SetDirty(manifest);
                EditorUtility.SetDirty(sun);
                PrefabUtility.SaveAsPrefabAsset(world, WorldPrefabPath);
                return original;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(world);
            }
        }

        private static LODGroup CreateLodAsset(
            Transform parent, string name, Mesh[] meshes, Material material,
            Vector3 position, Quaternion rotation, bool contributeGi)
        {
            if (meshes == null || meshes.Length != 3)
                throw new InvalidOperationException(name + " requires three LOD meshes.");
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            root.transform.localRotation = rotation;
            root.layer = BenchmarkLayer;
            LODGroup group = root.AddComponent<LODGroup>();
            Renderer[] renderers = new Renderer[3];
            for (int lod = 0; lod < 3; lod++)
            {
                bool bakeThisLod = contributeGi && lod == 0;
                renderers[lod] = CreateStaticRenderer(root.transform, name + "_LOD" + lod,
                    meshes[lod], material, bakeThisLod, bakeThisLod);
                renderers[lod].gameObject.layer = bakeThisLod ? BenchmarkLayer : 0;
            }
            group.SetLODs(new[]
            {
                new LOD(0.30f, new[] { renderers[0] }),
                new LOD(0.10f, new[] { renderers[1] }),
                new LOD(0.018f, new[] { renderers[2] })
            });
            group.fadeMode = LODFadeMode.None;
            group.animateCrossFading = false;
            group.RecalculateBounds();
            return group;
        }

        private static void CreateProp(
            Transform parent, string name, Mesh[] meshes, Material material, Vector3 position, Quaternion rotation)
        {
            CreateLodAsset(parent, name, meshes, material, position, rotation, false);
        }

        private static Transform CreateContextLodGroup(
            Transform parent,
            string name,
            Mesh[] meshes,
            Material[] materials,
            Matrix4x4[] matrices,
            float size,
            Vector3 referencePoint,
            bool active)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            LODGroup group = root.AddComponent<LODGroup>();
            Renderer[] renderers = new Renderer[3];
            for (int index = 0; index < 3; index++)
            {
                GameObject child = new GameObject(name + "_LOD" + index);
                child.transform.SetParent(root.transform, false);
                ApplyMatrix(child.transform, matrices[index]);
                MeshFilter filter = child.AddComponent<MeshFilter>();
                filter.sharedMesh = meshes[index];
                MeshRenderer renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = materials[index];
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderers[index] = renderer;
            }
            group.SetLODs(new[]
            {
                new LOD(0.08f, new[] { renderers[0] }),
                new LOD(0.025f, new[] { renderers[1] }),
                new LOD(0.004f, new[] { renderers[2] })
            });
            group.size = size;
            group.localReferencePoint = referencePoint;
            group.fadeMode = LODFadeMode.None;
            root.SetActive(active);
            return root.transform;
        }

        private static Renderer CreateStaticRenderer(
            Transform parent, string name, Mesh mesh, Material material, bool contributeGi, bool shadows)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            MeshFilter filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = shadows;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.lightProbeUsage = contributeGi ? LightProbeUsage.Off : LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            renderer.scaleInLightmap = contributeGi ? 0.25f : 0f;
            StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic |
                                      StaticEditorFlags.ReflectionProbeStatic;
            if (contributeGi) flags |= StaticEditorFlags.ContributeGI;
            GameObjectUtility.SetStaticEditorFlags(child, flags);
            return renderer;
        }

        private static Light CreateBakedSun(Transform parent, V2SunState state)
        {
            GameObject sunObject = new GameObject("AirportCoreBakedSun");
            sunObject.transform.SetParent(parent, false);
            sunObject.transform.rotation = state.rotation;
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.lightmapBakeType = LightmapBakeType.Baked;
            sun.shadows = LightShadows.Soft;
            sun.color = state.color;
            sun.intensity = state.intensity;
            sun.cullingMask = 1 << BenchmarkLayer;
            sun.renderMode = LightRenderMode.ForcePixel;
            return sun;
        }

        private static LightProbeGroup CreateLightProbes(Transform parent)
        {
            GameObject probeObject = new GameObject("AirportCoreLightProbes");
            probeObject.transform.SetParent(parent, false);
            LightProbeGroup group = probeObject.AddComponent<LightProbeGroup>();
            List<Vector3> positions = new List<Vector3>();
            foreach (float z in new[] { -258f, -224f, -190f })
            foreach (float x in new[] { -413f, -385f, -357f })
            foreach (float y in new[] { 1.5f, 5f })
                positions.Add(new Vector3(x, y, z));
            group.probePositions = positions.ToArray();
            return group;
        }

        private static void BakeBenchmarkAndFinalizeManifest(AirportCoreBenchmarkManifest manifest)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            AirportCoreBenchmarkRoot root = scene.GetRootGameObjects()
                .SelectMany(item => item.GetComponentsInChildren<AirportCoreBenchmarkRoot>(true))
                .Single();
            Light globalSun = scene.GetRootGameObjects()
                .SelectMany(item => item.GetComponentsInChildren<Light>(true))
                .Single(item => item.type == LightType.Directional && item != root.benchmarkSun);
            List<string> nonFiniteMessages = new List<string>();
            Application.LogCallback callback = (condition, _, __) =>
            {
                if (!string.IsNullOrWhiteSpace(condition) &&
                    condition.IndexOf("non finite", StringComparison.OrdinalIgnoreCase) >= 0)
                    nonFiniteMessages.Add(condition);
            };

            try
            {
                globalSun.cullingMask &= ~(1 << BenchmarkLayer);
                root.benchmarkSun.enabled = true;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
                LightingSettings settings = Lightmapping.lightingSettings;
                if (settings == null)
                {
                    settings = new LightingSettings { name = "AirportCoreV41BoundedLightingSettings" };
                    Lightmapping.lightingSettings = settings;
                }
                settings.bakedGI = true;
                settings.realtimeGI = false;
                settings.directionalityMode = LightmapsMode.NonDirectional;
                settings.lightmapMaxSize = 1024;
                settings.lightmapResolution = 6f;
                settings.lightmapCompression = LightmapCompression.NormalQuality;
                settings.environmentImportanceSampling = false;
                settings.maxBounces = 2;
                settings.minBounces = 1;
                EditorUtility.SetDirty(settings);
                Lightmapping.Clear();
                Application.logMessageReceived += callback;
                bool baked;
                try
                {
                    baked = Lightmapping.Bake();
                }
                finally
                {
                    Application.logMessageReceived -= callback;
                }
                if (!baked)
                    throw new InvalidOperationException("Unity lightmap bake returned failure.");
                if (nonFiniteMessages.Count != 0)
                    throw new InvalidOperationException("Lightmapper emitted non-finite output: " +
                        string.Join(" | ", nonFiniteMessages.Distinct()));

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ConfigureGeneratedLightmapImports();

                LightmapData[] lightmaps = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
                if (lightmaps.Length != 1 || lightmaps[0]?.lightmapColor == null ||
                    lightmaps[0].lightmapDir != null)
                    throw new InvalidOperationException(
                        $"Expected one non-directional scene lightmap, found {lightmaps.Length}.");
                int[] lightmapIndices = root.benchmarkContent.GetComponentsInChildren<Renderer>(true)
                    .Select(item => item.lightmapIndex)
                    .Where(index => index >= 0 && index < 0xFFFE)
                    .Distinct().ToArray();
                if (lightmapIndices.Length != 1 || lightmapIndices[0] != 0)
                    throw new InvalidOperationException(
                        "Benchmark GI renderers must use exactly lightmap index zero.");

                Texture2D color = lightmaps[0].lightmapColor;
                string colorPath = AssetDatabase.GetAssetPath(color);
                ValidateFiniteLightmap(colorPath);
                long bytes = EstimateAstcBytes(color.width, color.height, 6);
                manifest.bakedLightmapCount = 1;
                manifest.bakedLightmapResolution = Mathf.Max(color.width, color.height);
                manifest.estimatedLightmapBytes = bytes;
                manifest.serializedLightProbeCount = root.airportLightProbes.probePositions.Length;
                EditorUtility.SetDirty(manifest);
                WriteBakeManifest(colorPath, color, bytes);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();

                AirportCoreBenchmarkValidator.Report report = AirportCoreBenchmarkValidator.Validate();
                if (!report.passed)
                    throw new InvalidOperationException("Post-bake benchmark validation failed: " +
                        string.Join("; ", report.violations));
            }
            catch
            {
                RejectBake(scene, root, globalSun, manifest);
                throw;
            }
        }

        private static void RejectBake(
            Scene scene,
            AirportCoreBenchmarkRoot root,
            Light globalSun,
            AirportCoreBenchmarkManifest manifest)
        {
            Lightmapping.Clear();
            Lightmapping.lightingDataAsset = null;
            LightmapSettings.lightmaps = Array.Empty<LightmapData>();
            LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
            if (globalSun != null) globalSun.cullingMask |= 1 << BenchmarkLayer;
            if (root?.benchmarkSun != null) root.benchmarkSun.enabled = false;
            manifest.bakedLightmapCount = 0;
            manifest.bakedLightmapResolution = 0;
            manifest.estimatedLightmapBytes = 0;
            EditorUtility.SetDirty(manifest);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            DeleteRejectedBakeAssets();
            AssetDatabase.SaveAssets();
        }

        private static void ValidateFiniteLightmap(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing lightmap importer: " + path);
            bool previousReadable = importer.isReadable;
            importer.isReadable = true;
            importer.SaveAndReimport();
            try
            {
                Texture2D texture = LoadRequired<Texture2D>(path);
                Color[] pixels = texture.GetPixels();
                for (int index = 0; index < pixels.Length; index++)
                {
                    Color pixel = pixels[index];
                    if (!IsFinite(pixel.r) || !IsFinite(pixel.g) || !IsFinite(pixel.b) || !IsFinite(pixel.a))
                        throw new InvalidOperationException(
                            $"Lightmap contains a non-finite texel at {index}: {pixel}.");
                }
            }
            finally
            {
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    importer.isReadable = previousReadable;
                    importer.SaveAndReimport();
                }
            }
        }

        private static long EstimateAstcBytes(int width, int height, int block)
        {
            long blocks = (long)((width + block - 1) / block) * ((height + block - 1) / block);
            return (blocks * 16L * 4L + 2L) / 3L;
        }

        private static void WriteBakeManifest(string colorPath, Texture2D color, long estimatedBytes)
        {
            string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", colorPath));
            string sha256;
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(absolute))
                sha256 = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            BakeManifestRecord record = new BakeManifestRecord
            {
                schemaVersion = 2,
                classification = "PASS_FINITE_BOUNDED_NON_DIRECTIONAL_BAKE",
                scene = ScenePath,
                lightmapAssetPath = colorPath,
                lightmapSha256 = sha256,
                width = color.width,
                height = color.height,
                directional = false,
                finiteTexels = true,
                nonFiniteLogMessageCount = 0,
                ambientMode = AmbientMode.Trilight.ToString(),
                estimatedAndroidBytes = estimatedBytes
            };
            const string manifestPath = "Assets/Scenes/ProductionVerticalSlice/BAKE_MANIFEST.json";
            string manifestAbsolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", manifestPath));
            File.WriteAllText(manifestAbsolute, JsonUtility.ToJson(record, true) + Environment.NewLine,
                new UTF8Encoding(false));
            AssetDatabase.ImportAsset(manifestPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static Vector3 Ground(ProductionTerrainSurfaceAsset surface, float x, float z)
        {
            if (!surface.TrySample(x, z, out ProductionTerrainSample sample))
                throw new InvalidOperationException($"Could not ground benchmark asset at ({x},{z}).");
            return new Vector3(x, sample.heightMeters + 0.02f, z);
        }

        private static int TriangleCount(Renderer renderer)
        {
            Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null && renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
            if (mesh == null) return 0;
            long indices = 0;
            for (int index = 0; index < mesh.subMeshCount; index++) indices += (long)mesh.GetIndexCount(index);
            return (int)Math.Min(int.MaxValue, indices / 3L);
        }

        private static Light FindOneDirectionalLight(GameObject root)
        {
            return root.GetComponentsInChildren<Light>(true).FirstOrDefault(item => item.type == LightType.Directional);
        }

        private static void ApplyMatrix(Transform transform, Matrix4x4 matrix)
        {
            Vector3 position = matrix.GetColumn(3);
            Vector3 scale = new Vector3(matrix.GetColumn(0).magnitude, matrix.GetColumn(1).magnitude,
                matrix.GetColumn(2).magnitude);
            Quaternion rotation = Quaternion.LookRotation(matrix.GetColumn(2) / Mathf.Max(scale.z, 0.0001f),
                matrix.GetColumn(1) / Mathf.Max(scale.y, 0.0001f));
            transform.localPosition = position;
            transform.localRotation = rotation;
            transform.localScale = scale;
        }

        private static Mesh SaveMesh(Mesh candidate, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(candidate, path);
                return candidate;
            }
            EditorUtility.CopySerialized(candidate, existing);
            UnityEngine.Object.DestroyImmediate(candidate);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Material LoadOrCreateMaterial(string path, Shader shader)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            return material;
        }

        private static Shader RequireShader(string name)
        {
            Shader shader = Shader.Find(name);
            if (shader == null) throw new InvalidOperationException("Required shader did not import: " + name);
            return shader;
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException("Required asset is missing: " + path);
            return asset;
        }

        private static Transform CreateChild(Transform parent, string name, bool active)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.SetActive(active);
            return child.transform;
        }

        private static Transform FindRequired(Transform root, string name)
        {
            Transform result = FindDescendant(root, name);
            if (result == null) throw new InvalidOperationException("Required authored object is missing: " + name);
            return result;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => string.Equals(item.name, name, StringComparison.Ordinal));
        }

        private static void EnsureFolder(string path)
        {
            string[] segments = path.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segments[index]);
                current = next;
            }
        }

        private sealed class V2ContextCopies
        {
            public readonly Mesh[] original;
            public readonly Mesh[] filtered;
            public readonly Material[] materials;
            public readonly Matrix4x4[] childMatrices;
            public readonly float size;
            public readonly Vector3 referencePoint;
            public readonly string[] originalPaths;
            public readonly string[] filteredPaths;

            public V2ContextCopies(Mesh[] original, Mesh[] filtered, Material[] materials,
                Matrix4x4[] childMatrices, float size, Vector3 referencePoint,
                string[] originalPaths, string[] filteredPaths)
            {
                this.original = original;
                this.filtered = filtered;
                this.materials = materials;
                this.childMatrices = childMatrices;
                this.size = size;
                this.referencePoint = referencePoint;
                this.originalPaths = originalPaths;
                this.filteredPaths = filteredPaths;
            }
        }

        private readonly struct PlacedProp
        {
            public readonly string asset;
            public readonly Vector3 position;
            public readonly Quaternion rotation;

            public PlacedProp(string asset, Vector3 position, Quaternion rotation)
            {
                this.asset = asset;
                this.position = position;
                this.rotation = rotation;
            }
        }

        [Serializable]
        private sealed class BakeManifestRecord
        {
            public int schemaVersion;
            public string classification;
            public string scene;
            public string lightmapAssetPath;
            public string lightmapSha256;
            public int width;
            public int height;
            public bool directional;
            public bool finiteTexels;
            public int nonFiniteLogMessageCount;
            public string ambientMode;
            public long estimatedAndroidBytes;
        }

        private readonly struct V2SunState
        {
            public readonly Quaternion rotation;
            public readonly Color color;
            public readonly float intensity;
            public readonly int cullingMask;

            public V2SunState(Quaternion rotation, Color color, float intensity, int cullingMask)
            {
                this.rotation = rotation;
                this.color = color;
                this.intensity = intensity;
                this.cullingMask = cullingMask;
            }
        }
    }
}
