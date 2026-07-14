using System;
using System.IO;
using System.Linq;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace QuestFlightLab.Editor
{
    /// <summary>
    /// Builds a Windows OpenXR development player only from an external QA copy.
    /// The source product's Android settings and authored scene are never changed.
    /// </summary>
    public static class MetaXrSimulatorQaBuild
    {
        private const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        private const string GeneralSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        private const string OpenXrLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";

        [MenuItem("Quest Flight Lab/Development/Build Meta XR Simulator QA Player")]
        public static void BuildPlayer()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string expectedRoot = System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_EXPECTED_PROJECT_ROOT");
            bool devCopyAuthorized = string.Equals(
                System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_ALLOW_DEV_COPY"),
                "1",
                StringComparison.Ordinal);
            if (!devCopyAuthorized || string.IsNullOrWhiteSpace(expectedRoot) ||
                !PathsEqual(projectRoot, expectedRoot))
            {
                throw new InvalidOperationException(
                    "Meta XR Simulator QA builds may run only from the external development copy " +
                    "created by scripts/run_meta_xr_simulator_qa.ps1. The product project is protected.");
            }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                throw new InvalidOperationException(
                    "Launch Unity with -buildTarget StandaloneWindows64 before the execute method. " +
                    "This prevents conditional compilation from using the Android product target.");
            }

            string outputPath = System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_PLAYER_PATH");
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new InvalidOperationException("QFL_META_XR_SIMULATOR_PLAYER_PATH is required.");
            outputPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectRoot);

            ConfigureStandaloneOpenXrInDevelopmentCopy();
            ConfigureDevelopmentPlayer();
            AssetDatabase.SaveAssets();

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.Development
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            MetaXrSimulatorDevBuildReport evidence = new MetaXrSimulatorDevBuildReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                projectRoot = projectRoot,
                playerPath = outputPath,
                scenePath = ScenePath,
                buildTarget = report.summary.platform.ToString(),
                result = report.summary.result.ToString(),
                totalBytes = report.summary.totalSize,
                errorCount = report.summary.totalErrors,
                warningCount = report.summary.totalWarnings,
                openXrLoaderAssigned = IsOpenXrLoaderAssigned(),
                oculusTouchProfileEnabled = IsFeatureEnabled<OculusTouchControllerProfile>(),
                questTouchPlusProfileEnabled = IsFeatureEnabled<MetaQuestTouchPlusControllerProfile>(),
                shippingConfigurationChanged = false,
                limitation = "Windows player is a Meta XR Simulator development target only; it is not a product build."
            };
            string evidencePath = System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_BUILD_REPORT");
            if (!string.IsNullOrWhiteSpace(evidencePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(evidencePath) ?? projectRoot);
                File.WriteAllText(evidencePath, JsonUtility.ToJson(evidence, true));
            }
            Debug.Log($"[QuestFlight][MetaXRSimulatorQA] Development build {evidence.result}: {outputPath}");
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Meta XR Simulator development player build failed.");
        }

        private static void ConfigureStandaloneOpenXrInDevelopmentCopy()
        {
            XRGeneralSettingsPerBuildTarget general =
                AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(GeneralSettingsPath);
            if (general == null)
                throw new InvalidOperationException("XR general settings asset is missing: " + GeneralSettingsPath);
            if (!general.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone))
                general.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);

            XRGeneralSettings standalone = general.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            XRManagerSettings manager = general.ManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (standalone == null || manager == null)
                throw new InvalidOperationException("Could not create Standalone XR manager settings in the development copy.");
            standalone.InitManagerOnStart = true;
            if (!manager.activeLoaders.Any(loader => loader != null && loader.GetType().FullName == OpenXrLoaderType) &&
                !XRPackageMetadataStore.AssignLoader(manager, OpenXrLoaderType, BuildTargetGroup.Standalone))
            {
                throw new InvalidOperationException("Could not assign the OpenXR loader for the simulator development player.");
            }

            OpenXRSettings openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (openXr == null)
                throw new InvalidOperationException("Standalone OpenXR package settings are unavailable in the development copy.");
            EnableRequiredFeature<OculusTouchControllerProfile>(openXr);
            EnableRequiredFeature<MetaQuestTouchPlusControllerProfile>(openXr);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(standalone);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(openXr);
        }

        private static void EnableRequiredFeature<TFeature>(OpenXRSettings settings)
            where TFeature : UnityEngine.XR.OpenXR.Features.OpenXRFeature
        {
            TFeature feature = settings.GetFeature<TFeature>();
            if (feature == null)
                throw new InvalidOperationException("Required OpenXR feature is absent: " + typeof(TFeature).FullName);
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        private static void ConfigureDevelopmentPlayer()
        {
            PlayerSettings.productName = "QuestFlight Meta XR Simulator QA";
            PlayerSettings.companyName = "QuestFlight Development";
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.defaultScreenWidth = 1440;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
            EditorUserBuildSettings.development = true;
        }

        private static bool IsOpenXrLoaderAssigned()
        {
            XRGeneralSettingsPerBuildTarget general =
                AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(GeneralSettingsPath);
            XRManagerSettings manager = general?.ManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            return manager != null && manager.activeLoaders.Any(loader =>
                loader != null && loader.GetType().FullName == OpenXrLoaderType);
        }

        private static bool IsFeatureEnabled<TFeature>()
            where TFeature : UnityEngine.XR.OpenXR.Features.OpenXRFeature
        {
            OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            TFeature feature = settings != null ? settings.GetFeature<TFeature>() : null;
            return feature != null && feature.enabled;
        }

        private static bool PathsEqual(string left, string right)
        {
            string normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        [Serializable]
        private sealed class MetaXrSimulatorDevBuildReport
        {
            public string generatedUtc, unityVersion, projectRoot, playerPath, scenePath, buildTarget, result;
            public ulong totalBytes;
            public int errorCount, warningCount;
            public bool openXrLoaderAssigned, oculusTouchProfileEnabled, questTouchPlusProfileEnabled;
            public bool shippingConfigurationChanged;
            public string limitation;
        }
    }
}
