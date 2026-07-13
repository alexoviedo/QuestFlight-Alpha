using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace QuestFlight.Editor
{
    /// <summary>Applies the minimal, deterministic Quest product settings without generating scene content.</summary>
    public static class ProductProjectConfigurator
    {
        public const string ProductionScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        public const string AndroidApplicationIdentifier = "com.alexoviedo.t2.questflight";

        [MenuItem("QuestFlight/Product/Configure Quest Project")]
        public static void ConfigureProject()
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Unity could not switch the clean product project to Android.");

            PlayerSettings.companyName = "Alex Oviedo / T2";
            PlayerSettings.productName = "QuestFlight";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidApplicationIdentifier);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.resizeableActivity = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.runInBackground = true;
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.SetMobileMTRendering(NamedBuildTarget.Android, true);
            SetActiveInputHandlingToInputSystemOnly();

            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            QualitySettings.vSyncCount = 0;
            ConfigureEveryQualityLevelForQuest();
            EnableOpenXrForAndroid();

            if (!File.Exists(ProductionScenePath))
                throw new FileNotFoundException("The authored production scene has not been extracted yet.", ProductionScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ProductionScenePath, true) };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                "[QuestFlight][Configure] Android package=com.alexoviedo.t2.questflight, ARM64/IL2CPP, " +
                "Vulkan-only, ASTC, OpenXR/Meta Quest, Input System, Linear, 4x MSAA, one production scene.");
        }

        public static void ConfigureProjectBatch()
        {
            try
            {
                ConfigureProject();
                Debug.Log("[QuestFlight][Configure] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void ConfigureEveryQualityLevelForQuest()
        {
            int original = QualitySettings.GetQualityLevel();
            try
            {
                for (int index = 0; index < QualitySettings.names.Length; index++)
                {
                    QualitySettings.SetQualityLevel(index, false);
                    QualitySettings.antiAliasing = 4;
                    QualitySettings.vSyncCount = 0;
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                    QualitySettings.shadowDistance = Mathf.Min(QualitySettings.shadowDistance, 180f);
                    QualitySettings.realtimeReflectionProbes = false;
                    QualitySettings.streamingMipmapsActive = true;
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(Mathf.Clamp(original, 0, QualitySettings.names.Length - 1), true);
            }
        }

        private static void SetActiveInputHandlingToInputSystemOnly()
        {
            Type inputHandlingType = typeof(PlayerSettings).GetNestedType(
                "ActiveInputHandling",
                BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo method = typeof(PlayerSettings).GetMethod(
                "SetActiveInputHandling",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (inputHandlingType != null && method != null)
            {
                object inputSystemOnly = Enum.Parse(inputHandlingType, "InputSystemPackage");
                method.Invoke(null, new[] { inputSystemOnly });
                return;
            }

            // Unity 6000.3 no longer exposes SetActiveInputHandling. Persist the
            // same documented ProjectSettings value through Unity's serialized
            // settings object rather than rewriting YAML outside the Editor.
            SerializedObject serializedSettings = null;
            MethodInfo serializedGetter = typeof(PlayerSettings).GetMethod(
                "GetSerializedObject",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (serializedGetter != null)
                serializedSettings = serializedGetter.Invoke(null, null) as SerializedObject;
            if (serializedSettings == null)
            {
                UnityEngine.Object settingsAsset = AssetDatabase
                    .LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")
                    .FirstOrDefault(asset => asset != null && asset.GetType().Name == "PlayerSettings");
                if (settingsAsset != null) serializedSettings = new SerializedObject(settingsAsset);
            }

            SerializedProperty property = serializedSettings?.FindProperty("activeInputHandler");
            if (property == null)
                throw new MissingMemberException("Unity PlayerSettings activeInputHandler property is unavailable.");
            property.intValue = 1; // Input System Package (New) only.
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnableOpenXrForAndroid()
        {
            XRGeneralSettingsPerBuildTarget buildTargetSettings = GetOrCreateXrBuildTargetSettings();
            if (!buildTargetSettings.HasSettingsForBuildTarget(BuildTargetGroup.Android))
                buildTargetSettings.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!buildTargetSettings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                buildTargetSettings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

            XRGeneralSettings general = buildTargetSettings.SettingsForBuildTarget(BuildTargetGroup.Android);
            XRManagerSettings manager = buildTargetSettings.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            if (general == null || manager == null)
                throw new InvalidOperationException("Android XR Management settings could not be created.");
            general.InitManagerOnStart = true;
            manager.automaticLoading = true;
            manager.automaticRunning = true;
            if (!XRPackageMetadataStore.AssignLoader(manager, typeof(OpenXRLoader).FullName, BuildTargetGroup.Android))
                throw new InvalidOperationException("OpenXRLoader could not be assigned to Android XR Management.");

            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, buildTargetSettings, true);
            EditorUtility.SetDirty(buildTargetSettings);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);
            AssetDatabase.SaveAssets();

            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            RequireFeature(MetaQuestFeature.featureId).enabled = true;
            EnableOptionalFeature(OculusTouchControllerProfile.featureId);
            EnableOptionalFeature(MetaQuestTouchPlusControllerProfile.featureId);
            EnableOptionalFeature(FoveatedRenderingFeature.featureId);

            OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null) throw new InvalidOperationException("Android OpenXR settings are unavailable.");
            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            settings.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;
            EditorUtility.SetDirty(settings);
            foreach (OpenXRFeature feature in settings.GetFeatures().Where(feature => feature != null && feature.enabled))
                EditorUtility.SetDirty(feature);
        }

        private static XRGeneralSettingsPerBuildTarget GetOrCreateXrBuildTargetSettings()
        {
            if (EditorBuildSettings.TryGetConfigObject(
                    XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget registered) && registered != null)
                return registered;

            string[] existing = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
            if (existing.Length > 0)
            {
                XRGeneralSettingsPerBuildTarget asset = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(
                    AssetDatabase.GUIDToAssetPath(existing[0]));
                if (asset != null)
                {
                    EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, asset, true);
                    return asset;
                }
            }

            const string xrFolder = "Assets/XR";
            if (!AssetDatabase.IsValidFolder(xrFolder)) AssetDatabase.CreateFolder("Assets", "XR");
            XRGeneralSettingsPerBuildTarget created = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(created, xrFolder + "/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, created, true);
            return created;
        }

        private static OpenXRFeature RequireFeature(string featureId)
        {
            OpenXRFeature feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, featureId);
            if (feature == null) throw new InvalidOperationException("Required Android OpenXR feature is missing: " + featureId);
            EditorUtility.SetDirty(feature);
            return feature;
        }

        private static void EnableOptionalFeature(string featureId)
        {
            OpenXRFeature feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, featureId);
            if (feature == null)
            {
                Debug.LogWarning("[QuestFlight][Configure] Optional OpenXR feature unavailable: " + featureId);
                return;
            }
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }
    }
}
