using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR;

namespace QuestFlight.Editor
{
    /// <summary>Product-only Android build entry point. It cannot build a legacy or alternate scene.</summary>
    public static class ProductionBuild
    {
        public const string ApkRelativePath = "Builds/Android/QuestFlight-production.apk";

        [MenuItem("QuestFlight/Product/Build Quest APK")]
        public static void PerformAndroidBuild()
        {
            ProductProjectConfigurator.ConfigureProject();
            ProductIntegrityReport integrity = ProductIntegrityValidator.Validate();
            if (!integrity.passed) throw new InvalidOperationException(integrity.ToString());

            string apkPath = Path.GetFullPath(ApkRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(apkPath) ?? "Builds/Android");
            bool development = string.Equals(
                Environment.GetEnvironmentVariable("QUESTFLIGHT_DEVELOPMENT_BUILD"),
                "1",
                StringComparison.Ordinal);
            bool forceUnityPrototype = string.Equals(
                Environment.GetEnvironmentVariable("QUESTFLIGHT_FORCE_UNITY_PROTOTYPE"),
                "1",
                StringComparison.Ordinal);
            bool enableNativeJsbsim = string.Equals(
                Environment.GetEnvironmentVariable("QUESTFLIGHT_ENABLE_NATIVE_JSBSIM"),
                "1",
                StringComparison.Ordinal);
            if (forceUnityPrototype && enableNativeJsbsim)
                throw new InvalidOperationException(
                    "Unity-prototype forcing and native-JSBSim opt-in are mutually exclusive.");
            BuildOptions buildOptions = development
                ? BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.ConnectWithProfiler
                : BuildOptions.None;
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ProductProjectConfigurator.ProductionScenePath },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = buildOptions,
                extraScriptingDefines = forceUnityPrototype
                    ? new[] { "QUESTFLIGHT_FORCE_UNITY_PROTOTYPE" }
                    : enableNativeJsbsim
                        ? new[] { "QUESTFLIGHT_ENABLE_NATIVE_JSBSIM" }
                        : Array.Empty<string>()
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            WriteBuildEvidence(report, apkPath, development, integrity);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException(
                    $"Quest product build failed: {report.summary.result}; errors={report.summary.totalErrors}");
            ValidateCompiledProductShaders();
            Debug.Log(
                $"[QuestFlight][Build] SUCCESS apk={apkPath}; bytes={report.summary.totalSize}; " +
                $"duration={report.summary.totalTime}; development={development}");
        }

        private static void ValidateCompiledProductShaders()
        {
            string[] dependencies = AssetDatabase.GetDependencies(
                ProductProjectConfigurator.ProductionScenePath, true);
            string[] failures = dependencies
                .Where(path => path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                .Select(path => new
                {
                    path,
                    shader = AssetDatabase.LoadAssetAtPath<Shader>(path)
                })
                .Where(item => item.shader == null || !item.shader.isSupported ||
                    ShaderUtil.GetShaderMessages(item.shader).Any(message =>
                        string.Equals(message.severity.ToString(), "Error",
                            StringComparison.OrdinalIgnoreCase)))
                .Select(item => item.shader == null
                    ? item.path + ": missing shader asset"
                    : item.path + ": " + string.Join(" | ", ShaderUtil.GetShaderMessages(item.shader)
                        .Where(message => string.Equals(message.severity.ToString(), "Error",
                            StringComparison.OrdinalIgnoreCase))
                        .Select(message => message.message)))
                .ToArray();
            if (failures.Length != 0)
                throw new InvalidOperationException(
                    "Android product dependency contains unsupported or failed shaders:\n" +
                    string.Join("\n", failures));
        }

        public static void PerformAndroidBuildBatch()
        {
            try
            {
                PerformAndroidBuild();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void WriteBuildEvidence(
            BuildReport buildReport,
            string apkPath,
            bool development,
            ProductIntegrityReport integrity)
        {
            GraphicsDeviceType[] graphicsApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            OpenXRSettings openXrSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            BuildEvidence evidence = new BuildEvidence
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                applicationIdentifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android),
                scene = ProductProjectConfigurator.ProductionScenePath,
                apkPath = apkPath,
                developmentBuild = development,
                buildResult = buildReport.summary.result.ToString(),
                totalErrors = buildReport.summary.totalErrors,
                totalWarnings = buildReport.summary.totalWarnings,
                totalBytes = buildReport.summary.totalSize,
                duration = buildReport.summary.totalTime.ToString(),
                graphicsApis = graphicsApis.Select(api => api.ToString()).ToArray(),
                architecture = PlayerSettings.Android.targetArchitectures.ToString(),
                scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android).ToString(),
                textureCompression = EditorUserBuildSettings.androidBuildSubtarget.ToString(),
                colorSpace = PlayerSettings.colorSpace.ToString(),
                msaa = QualitySettings.antiAliasing,
                openXrRenderMode = openXrSettings != null ? openXrSettings.renderMode.ToString() : "missing",
                integrity = integrity.ToString()
            };
            string directory = Path.GetDirectoryName(apkPath) ?? "Builds/Android";
            File.WriteAllText(Path.Combine(directory, "questflight_build_evidence.json"), JsonUtility.ToJson(evidence, true));
        }

        [Serializable]
        private sealed class BuildEvidence
        {
            public string generatedUtc;
            public string applicationIdentifier;
            public string scene;
            public string apkPath;
            public bool developmentBuild;
            public string buildResult;
            public int totalErrors;
            public int totalWarnings;
            public ulong totalBytes;
            public string duration;
            public string[] graphicsApis;
            public string architecture;
            public string scriptingBackend;
            public string textureCompression;
            public string colorSpace;
            public int msaa;
            public string openXrRenderMode;
            public string integrity;
        }
    }
}
