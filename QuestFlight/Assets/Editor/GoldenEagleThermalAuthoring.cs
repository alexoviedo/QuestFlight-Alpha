using System;
using System.Linq;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Editor
{
    /// <summary>
    /// Editor-only compilation of one bounded Golden Eagle and the one thermal
    /// profile shared by its behavior and the opt-in native JSBSim development path.
    /// </summary>
    public static class GoldenEagleThermalAuthoring
    {
        public const string Root = "Assets/Production/Environment/Wildlife";
        public const string ModelPath = Root + "/Models/GoldenEagle_Quest.fbx";
        public const string TexturePath = Root + "/Textures/GoldenEagle_Albedo.png";
        public const string MaterialPath = Root + "/Materials/GoldenEagle_Quest.mat";
        public const string ControllerPath = Root + "/GoldenEagle_Flight.controller";
        public const string ProfilePath = Root + "/Profiles/KBDU_SunlitFieldThermal01.asset";
        public const string PrefabPath = Root + "/Prefabs/GoldenEagleThermal.prefab";
        private const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        private const string WorldPrefabPath =
            "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
        private const string WildlifeRootName = "WildlifeRoot";
        private const string EagleRootName = "GoldenEagleThermalRoot";
        private const string BlenderBackupPath =
            "Assets/ArtSource/Blender/Wildlife/GoldenEagle_Quest.blend1";

        [MenuItem("QuestFlight/Product/Author Golden Eagle Thermal V1")]
        public static void BuildInteractive() => Build(false);

        public static void RunBatch()
        {
            try
            {
                Build(true);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Build(bool exitWhenComplete)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.DeleteAsset(BlenderBackupPath);
            ConfigureTexture();
            ConfigureModel();
            ThermalFieldProfile profile = BuildThermalProfile();
            Material material = BuildMaterial();
            RuntimeAnimatorController controller = BuildAnimatorController();
            GameObject eaglePrefab = BuildEaglePrefab(profile, material, controller);
            AuthorEnvironmentPrefab(profile, eaglePrefab);
            BindFlightAuthority(profile, AircraftPrefabPath);
            BindFlightAuthority(profile, WorldPrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            Debug.Log("[QuestFlight][GoldenEagle] Authored one animated Golden Eagle and one shared development-native JSBSim thermal.");
            if (exitWhenComplete) EditorApplication.Exit(0);
        }

        private static void ConfigureTexture()
        {
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            Require(importer != null, "Golden Eagle texture importer is missing.");
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 2;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android",
                overridden = true,
                maxTextureSize = 1024,
                format = TextureImporterFormat.ASTC_6x6,
                compressionQuality = 75
            });
            importer.SaveAndReimport();
        }

        private static void ConfigureModel()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Require(importer != null, "Golden Eagle model importer is missing.");
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.optimizeGameObjects = false;
            importer.SaveAndReimport();
        }

        private static ThermalFieldProfile BuildThermalProfile()
        {
            ThermalFieldProfile profile =
                AssetDatabase.LoadAssetAtPath<ThermalFieldProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<ThermalFieldProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            profile.thermalId = "kbdu_sunlit_field_thermal_01";
            // The route from climb_northeast to reservoir_entry crosses this
            // open, sunlit field region. The profile is a moderate, bounded
            // fair-weather convective cue, not generalized weather synthesis.
            profile.centerUnityMeters = new Vector3(1700f, 0f, 2600f);
            profile.coreRadiusMeters = 70f;
            profile.outerRadiusMeters = 240f;
            profile.baseHeightUnityMeters = 40f;
            profile.fullStrengthBottomUnityMeters = 180f;
            profile.fullStrengthTopUnityMeters = 760f;
            profile.capHeightUnityMeters = 1100f;
            profile.peakUpdraftMetersPerSecond = 2.5f;
            Require(profile.IsValid(out string failure), "Thermal profile invalid: " + failure);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static Material BuildMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(RequireShader("Standard"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.name = "GoldenEagle_Quest";
            material.shader = RequireShader("Standard");
            material.SetTexture("_MainTex", LoadRequired<Texture2D>(TexturePath));
            material.SetColor("_Color", new Color(0.72f, 0.58f, 0.42f, 1f));
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", 0.18f);
            material.enableInstancing = false;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static RuntimeAnimatorController BuildAnimatorController()
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(candidate => !candidate.name.StartsWith("__preview__", StringComparison.Ordinal));
            Require(clip != null, "Golden Eagle source has no imported flight animation.");

            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            AnimatorState state = controller.layers[0].stateMachine.AddState("Fly");
            state.motion = clip;
            state.speed = 1f;
            controller.layers[0].stateMachine.defaultState = state;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static GameObject BuildEaglePrefab(
            ThermalFieldProfile profile,
            Material material,
            RuntimeAnimatorController controller)
        {
            GameObject source = LoadRequired<GameObject>(ModelPath);
            GameObject root = new GameObject(EagleRootName);
            try
            {
                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
                visual.name = "GoldenEagleVisual";
                visual.transform.SetParent(root.transform, false);
                Animator animator = visual.GetComponentInChildren<Animator>(true);
                if (animator == null) animator = visual.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                animator.updateMode = AnimatorUpdateMode.Normal;

                SkinnedMeshRenderer[] renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Require(renderers.Length == 1,
                    $"Golden Eagle must use one skinned renderer; found {renderers.Length}.");
                renderers[0].sharedMaterial = material;
                renderers[0].shadowCastingMode = ShadowCastingMode.Off;
                renderers[0].receiveShadows = true;
                renderers[0].motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderers[0].lightProbeUsage = LightProbeUsage.BlendProbes;

                GoldenEagleThermalSoaring behavior = root.AddComponent<GoldenEagleThermalSoaring>();
                behavior.thermalField = profile;
                behavior.visualRoot = animator.transform;
                behavior.animator = animator;
                behavior.orbitRadiusMeters = 105f;
                behavior.soaringAltitudeUnityMeters = 515f;
                behavior.airspeedMetersPerSecond = 16f;
                behavior.bankDegrees = 18f;
                behavior.glidePoseNormalizedTime = 0.25f;
                behavior.flapIntervalSeconds = 12f;
                behavior.flapDurationSeconds = 1.4f;
                behavior.phaseOffsetSeconds = 2.3f;
                Require(behavior.HasAuthoredReferences(out string failure),
                    "Golden Eagle behavior invalid: " + failure);

                root.transform.position = new Vector3(
                    profile.centerUnityMeters.x + behavior.orbitRadiusMeters,
                    behavior.soaringAltitudeUnityMeters,
                    profile.centerUnityMeters.z);
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void AuthorEnvironmentPrefab(ThermalFieldProfile profile, GameObject eaglePrefab)
        {
            GameObject environment = PrefabUtility.LoadPrefabContents(ProductionEnvironmentRoot.PrefabAssetPath);
            try
            {
                ProductionEnvironmentRoot contract = environment.GetComponent<ProductionEnvironmentRoot>();
                Require(contract != null, "Production environment contract is missing.");
                Transform existing = environment.transform.Find(WildlifeRootName);
                GameObject wildlife = existing != null ? existing.gameObject : new GameObject(WildlifeRootName);
                wildlife.transform.SetParent(environment.transform, false);
                Transform oldEagle = wildlife.transform.Find(EagleRootName);
                if (oldEagle != null) UnityEngine.Object.DestroyImmediate(oldEagle.gameObject);

                GameObject eagle = (GameObject)PrefabUtility.InstantiatePrefab(eaglePrefab, environment.scene);
                eagle.name = EagleRootName;
                eagle.transform.SetParent(wildlife.transform, true);
                GoldenEagleThermalSoaring behavior = eagle.GetComponent<GoldenEagleThermalSoaring>();
                contract.wildlifeRoot = wildlife.transform;
                contract.goldenEagleThermalSoaring = behavior;
                contract.sharedThermalField = profile;

                ProductionEnvironmentBudget budget = contract.CalculateBudget();
                contract.bakedRendererCount = budget.rendererCount;
                contract.bakedTriangleCount = budget.triangleCount;
                contract.bakedMaterialCount = budget.materialCount;
                contract.bakeUtc = DateTime.UtcNow.ToString("O");
                // Preserve the authoritative aerial-surface bake identity; this
                // bounded authoring pass adds wildlife without rebuilding it.
                contract.bakeToolVersion = "photoreal_aerial_surface_v1";
                EditorUtility.SetDirty(contract);
                PrefabUtility.SaveAsPrefabAsset(environment, ProductionEnvironmentRoot.PrefabAssetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(environment);
            }
        }

        private static void BindFlightAuthority(ThermalFieldProfile profile, string prefabPath)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                FlightDynamicsCoordinator[] coordinators =
                    root.GetComponentsInChildren<FlightDynamicsCoordinator>(true);
                Require(coordinators.Length == 1,
                    $"Expected one FlightDynamicsCoordinator in {prefabPath}; found {coordinators.Length}.");
                coordinators[0].thermalField = profile;
                EditorUtility.SetDirty(coordinators[0]);
                PrefabUtility.RecordPrefabInstancePropertyModifications(coordinators[0]);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Shader RequireShader(string name)
        {
            Shader shader = Shader.Find(name);
            if (shader == null) throw new InvalidOperationException("Required shader is missing: " + name);
            return shader;
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value == null) throw new InvalidOperationException("Required asset is missing: " + path);
            return value;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
