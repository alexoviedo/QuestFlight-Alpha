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

            AndroidSigningState signingState = AndroidSigningState.Capture();
            bool customSigning = ConfigureAndroidSigningFromEnvironment();

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
            ValidateBackendBuildFlags(development, forceUnityPrototype, enableNativeJsbsim);
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

            UnityEngine.Object[] authoredPreloadedAssets = PlayerSettings.GetPreloadedAssets();
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                // OpenXR build processors temporarily remove settings subassets.
                // Restore the exact serialized objects (including their local file
                // IDs) so batch builds cannot mutate tracked ProjectSettings.
                PlayerSettings.SetPreloadedAssets(authoredPreloadedAssets);
                signingState.Restore();
                AssetDatabase.SaveAssets();
            }
            WriteBuildEvidence(report, apkPath, development, customSigning, integrity);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException(
                    $"Quest product build failed: {report.summary.result}; errors={report.summary.totalErrors}");
            ValidateCompiledProductShaders();
            Debug.Log(
                $"[QuestFlight][Build] SUCCESS apk={apkPath}; bytes={report.summary.totalSize}; " +
                $"duration={report.summary.totalTime}; development={development}");
        }

        public static void ValidateBackendBuildFlags(
            bool development,
            bool forceUnityPrototype,
            bool enableNativeJsbsim)
        {
            if (forceUnityPrototype && enableNativeJsbsim)
                throw new InvalidOperationException(
                    "Unity-prototype forcing and native-JSBSim opt-in are mutually exclusive.");
            if (enableNativeJsbsim && !development)
                throw new InvalidOperationException(
                    "Native JSBSim requires a development build because the runtime authority " +
                    "is deliberately compile-gated out of release-style products.");
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
            bool customSigning,
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
                customSigning = customSigning,
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
            public bool customSigning;
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

        private static bool ConfigureAndroidSigningFromEnvironment()
        {
            string keystorePath = Environment.GetEnvironmentVariable("QUESTFLIGHT_ANDROID_KEYSTORE_PATH");
            string keystorePassword = Environment.GetEnvironmentVariable("QUESTFLIGHT_ANDROID_KEYSTORE_PASSWORD");
            string keyAlias = Environment.GetEnvironmentVariable("QUESTFLIGHT_ANDROID_KEYALIAS");
            string keyAliasPassword = Environment.GetEnvironmentVariable("QUESTFLIGHT_ANDROID_KEYALIAS_PASSWORD");
            bool requireReleaseSigning = string.Equals(
                Environment.GetEnvironmentVariable("QUESTFLIGHT_REQUIRE_RELEASE_SIGNING"),
                "1",
                StringComparison.Ordinal);

            bool anySigningValue = !string.IsNullOrWhiteSpace(keystorePath) ||
                !string.IsNullOrWhiteSpace(keystorePassword) ||
                !string.IsNullOrWhiteSpace(keyAlias) ||
                !string.IsNullOrWhiteSpace(keyAliasPassword);
            if (!anySigningValue)
            {
                if (requireReleaseSigning)
                    throw new InvalidOperationException(
                        "Release signing is required, but no Android signing environment was supplied.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(keystorePath) ||
                string.IsNullOrWhiteSpace(keystorePassword) ||
                string.IsNullOrWhiteSpace(keyAlias) ||
                string.IsNullOrWhiteSpace(keyAliasPassword))
                throw new InvalidOperationException(
                    "Android signing configuration is incomplete. Supply the keystore path, " +
                    "keystore password, key alias, and key-alias password together.");

            string fullKeystorePath = Path.GetFullPath(keystorePath);
            if (!File.Exists(fullKeystorePath))
                throw new FileNotFoundException("Android release keystore was not found.", fullKeystorePath);

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = fullKeystorePath;
            PlayerSettings.Android.keystorePass = keystorePassword;
            PlayerSettings.Android.keyaliasName = keyAlias;
            PlayerSettings.Android.keyaliasPass = keyAliasPassword;
            return true;
        }

        private sealed class AndroidSigningState
        {
            private bool useCustomKeystore;
            private string keystoreName;
            private string keystorePass;
            private string keyaliasName;
            private string keyaliasPass;

            public static AndroidSigningState Capture()
            {
                return new AndroidSigningState
                {
                    useCustomKeystore = PlayerSettings.Android.useCustomKeystore,
                    keystoreName = PlayerSettings.Android.keystoreName,
                    keystorePass = PlayerSettings.Android.keystorePass,
                    keyaliasName = PlayerSettings.Android.keyaliasName,
                    keyaliasPass = PlayerSettings.Android.keyaliasPass
                };
            }

            public void Restore()
            {
                PlayerSettings.Android.useCustomKeystore = useCustomKeystore;
                PlayerSettings.Android.keystoreName = keystoreName;
                PlayerSettings.Android.keystorePass = keystorePass;
                PlayerSettings.Android.keyaliasName = keyaliasName;
                PlayerSettings.Android.keyaliasPass = keyaliasPass;
            }
        }
    }
}
