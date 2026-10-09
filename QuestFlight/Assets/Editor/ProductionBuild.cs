using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR;
using AlexOviedo.QuestFlight.JSBSim.Editor;

namespace QuestFlight.Editor
{
    /// <summary>Product-only Android build entry point. It cannot build a legacy or alternate scene.</summary>
    public static class ProductionBuild
    {
        public const string ApkRelativePath = "Builds/Android/QuestFlight-production.apk";
        private const string ProductionAircraftRigPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";

        [MenuItem("QuestFlight/Product/Build Quest APK")]
        public static void PerformAndroidBuild()
        {
            // A failed player build can skip Unity's post-build callback and leave
            // the package-owned, ignored JSBSim staging copy behind. Remove only a
            // correctly marked prior injection before the repository integrity gate.
            JSBSimBuildDataInjector.RemoveOwnedInjection();
            ProductProjectConfigurator.ConfigureProject();
            // The scene owns a fixed renderer pool; only deterministic offline page bundles
            // are generated here. No runtime hierarchy or geometry authoring is permitted.
            FullMapAerialClipmapAssetAuthoring.EnsureVerifiedBuildArtifacts();
            FullMapBuildingClipmapAssetAuthoring.EnsureBuildArtifacts();
            RefreshSerializedProductClosure();
            ConfigureMetaOpenXrNativePluginSelection();
            ProductIntegrityReport integrity = ValidateBuildPreflight();
            if (!integrity.passed) throw new InvalidOperationException(integrity.ToString());

            AndroidSigningState signingState = AndroidSigningState.Capture();
            bool customSigning = ConfigureAndroidSigningFromEnvironment();

            string apkPath = Path.GetFullPath(ApkRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(apkPath) ?? "Builds/Android");
            bool development = string.Equals(
                Environment.GetEnvironmentVariable("QUESTFLIGHT_DEVELOPMENT_BUILD"),
                "1",
                StringComparison.Ordinal);
            // Development enables the bounded attachment diagnostics. It must not
            // implicitly start a managed debugger or an unattended profiler: the
            // latter buffers samples even when no Editor can receive them.
            BuildOptions buildOptions = development ? BuildOptions.Development : BuildOptions.None;
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ProductProjectConfigurator.ProductionScenePath },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = buildOptions,
                extraScriptingDefines = new[] { "QUESTFLIGHT_REQUIRE_NATIVE_JSBSIM" }
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
                if (customSigning) signingState.Restore();
                JSBSimBuildDataInjector.RemoveOwnedInjection();
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

        public static ProductIntegrityReport ValidateBuildPreflight()
        {
            // A batch executeMethod can run before the first Editor update.
            // SDK205 may refill the disabled template during its initial load;
            // sanitize synchronously before validation, not just via update/build callbacks.
            ProductReleasePolicy.SanitizeAuthoredAgentTemplateBatch();
            return ProductIntegrityValidator.Validate();
        }

        /// <summary>
        /// Reimport the small serialized product closure synchronously before validation.
        /// This prevents a stale AssetDatabase artifact from presenting valid MonoBehaviour
        /// GUIDs as missing after a project directory has been copied for diagnostics. It
        /// does not author, repair, or reserialize any product object.
        /// </summary>
        private static void RefreshSerializedProductClosure()
        {
            ImportAssetOptions options = ImportAssetOptions.ForceUpdate |
                ImportAssetOptions.ForceSynchronousImport;
            string[] paths =
            {
                ProductProjectConfigurator.ProductionScenePath,
                ProductFlightModesFoundationAuthoring.WorldPrefabPath,
                ProductionAircraftRigPath
            };
            foreach (string path in paths)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException(
                        "Serialized product build dependency is missing.", path);
                AssetDatabase.ImportAsset(path, options);
            }
        }

        /// <summary>
        /// Meta XR supplies the OpenXR runtime loader inside OVRPlugin. A clean
        /// package import can otherwise stage Unity OpenXR's loader AAR as well,
        /// leaving Gradle with two different libopenxr_loader.so files. Meta's
        /// own OVRGradleGeneration build hook applies this same importer policy;
        /// enforce it here so a production build does not depend on Library
        /// cache history or editor callback ordering.
        /// </summary>
        private static void ConfigureMetaOpenXrNativePluginSelection()
        {
            bool disabledUnityLoader = false;
            bool includedMetaLoader = false;
            foreach (PluginImporter importer in PluginImporter.GetAllImporters())
            {
                string path = importer.assetPath.Replace('\\', '/');
                if (path.StartsWith("Packages/com.unity.xr.openxr/", StringComparison.Ordinal) &&
                    path.EndsWith("/openxr_loader.aar", StringComparison.OrdinalIgnoreCase))
                {
                    importer.SetIncludeInBuildDelegate(_ => false);
                    disabledUnityLoader = true;
                }
                else if (path.StartsWith("Packages/com.meta.xr.sdk.core/", StringComparison.Ordinal) &&
                         path.EndsWith("/OVRPlugin.aar", StringComparison.OrdinalIgnoreCase))
                {
                    importer.SetIncludeInBuildDelegate(_ => true);
                    includedMetaLoader = true;
                }
            }

            if (!disabledUnityLoader || !includedMetaLoader)
                throw new InvalidOperationException(
                    "Could not establish one deterministic Meta OpenXR native-loader source. " +
                    $"unityLoaderDisabled={disabledUnityLoader}; metaLoaderIncluded={includedMetaLoader}");

            Debug.Log("[QuestFlight][Build] Meta OVRPlugin is the sole Android OpenXR native-loader source.");
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
                integrity = integrity.ToString(),
                nativeJsbsimRequired = true,
                unityPrototypeAllowed = false
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
            public bool nativeJsbsimRequired;
            public bool unityPrototypeAllowed;
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

    /// <summary>
    /// Unity 6 can stage the Unity OpenXR loader AAR even after its plugin importer
    /// include delegate rejects it. Meta's OVRPlugin AAR already contains the Quest
    /// OpenXR loader, so keeping both makes Gradle fail on duplicate native libraries.
    /// Remove exactly the redundant Unity dependency from the generated project and
    /// fail closed if the generated dependency shape is not the one we audited.
    /// </summary>
    /// <summary>Final product boundaries after SDK callbacks; no developer secrets in players.</summary>
    public sealed class ProductReleasePolicy : IPreprocessBuildWithReport, IPostprocessBuildWithReport,
        IPostGenerateGradleAndroidProject
    {
        public const string DevAgentAssetPath = "Assets/Resources/DevAgentSettings.asset";
        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
        public int callbackOrder => int.MaxValue - 1;
        private static double nextAgentCheck;

        [InitializeOnLoadMethod]
        private static void RegisterAuthoredAgentBoundary()
        {
            // SDK205 also repopulates an empty token on Editor load and in its UI,
            // even while the agent is disabled. Keep the authored product template
            // safe between builds without changing the pinned SDK or user host.
            EditorApplication.update += EnforceAuthoredAgentBoundary;
            EditorApplication.quitting += SanitizeAgentAsset;
            AssemblyReloadEvents.beforeAssemblyReload += EnforceAuthoredAgentBoundary;
        }

        private static void EnforceAuthoredAgentBoundary()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.timeSinceStartup < nextAgentCheck) return;
            nextAgentCheck = EditorApplication.timeSinceStartup + 1;
            UnityEngine.Object template = AssetDatabase.LoadMainAssetAtPath(DevAgentAssetPath);
            if (template != null && !IsSanitizedAgent(new SerializedObject(template)))
                SanitizeAgentAsset();
        }

        public static bool IsSanitizedAgent(SerializedObject asset)
        {
            SerializedProperty enabled = asset.FindProperty("enabled");
            SerializedProperty token = asset.FindProperty("accessToken");
            SerializedProperty witToken = asset.FindProperty("witClientAccessToken");
            return enabled != null && token != null && witToken != null && !enabled.boolValue &&
                string.IsNullOrEmpty(token.stringValue) && string.IsNullOrEmpty(witToken.stringValue);
        }

        public static void SanitizeAgent(SerializedObject asset)
        {
            SerializedProperty enabled = asset.FindProperty("enabled");
            SerializedProperty token = asset.FindProperty("accessToken");
            SerializedProperty witToken = asset.FindProperty("witClientAccessToken");
            if (enabled == null || token == null || witToken == null)
                throw new BuildFailedException("Development-agent credential boundary changed; inspect SDK (value redacted).");
            enabled.boolValue = false;
            token.stringValue = string.Empty;
            witToken.stringValue = string.Empty;
            asset.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SanitizeAuthoredAgentTemplateBatch()
        {
            SanitizeAgentAsset();
            Debug.Log("[QuestFlight][Security] Authored development agent disabled; credential fields empty (values redacted).");
        }

        [Serializable]
        public sealed class AgentResourceInspection
        {
            public string schema = "questflight.serialized-agent-inspection.v1";
            public string serialized_file_sha256;
            public int loaded_objects;
            public int decoded_agent_templates;
            public bool verified;
            public string limitation;
        }

        public static AgentResourceInspection InspectSerializedAgentResources(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Owned serialized resource input is missing.");
            RequireEditorSerializedResource(path);
            var result = new AgentResourceInspection();
            using (var hash = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(path))
                result.serialized_file_sha256 = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            UnityEngine.Object[] objects = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(path);
            result.loaded_objects = objects.Length;
            bool sanitized = true;
            foreach (UnityEngine.Object item in objects)
            {
                if (item == null || item.GetType().FullName != "Meta.XR.ImmersiveDebugger.DevAgent.RuntimeSettings") continue;
                result.decoded_agent_templates++;
                sanitized &= IsSanitizedAgent(new SerializedObject(item));
            }
            result.verified = result.decoded_agent_templates > 0 && sanitized;
            result.limitation = result.decoded_agent_templates == 0
                ? "Expected agent template was not decoded; absence or compiled-player compatibility is not proven."
                : "Decoded credential fields and enabled flag inspected without emitting values. Service revocation and runtime network behavior are separate.";
            // Batch shutdown owns unloading this isolated file. Do not destroy
            // referenced source assets or any authored product object here.
            return result;
        }

        private static void RequireEditorSerializedResource(string path)
        {
            // The Editor native loader assumes Editor MonoBehaviour fields. Player
            // files strip some of those fields and can crash in native code before
            // a managed catch runs. Only our current-format Editor fixtures are
            // supported here; inspect packaged players with a bounded decoder.
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 48)
                    throw new NotSupportedException("Serialized resource header is unsupported (values redacted).");
                byte[] header = reader.ReadBytes(48);
                uint version = (uint)(header[8] << 24 | header[9] << 16 | header[10] << 8 | header[11]);
                uint metadataBytes = (uint)(header[20] << 24 | header[21] << 16 | header[22] << 8 | header[23]);
                if (version != 22 || header[16] != 0 || metadataBytes < 8 || metadataBytes > stream.Length - 48)
                    throw new NotSupportedException("Serialized resource format is unsupported (values redacted).");
                int limit = (int)Math.Min(metadataBytes - 4, 128);
                bool terminated = false;
                for (int i = 0; i < limit; i++)
                    if (reader.ReadByte() == 0) { terminated = true; break; }
                if (!terminated || stream.Position + 4 > 48 + metadataBytes ||
                    reader.ReadInt32() != (int)BuildTarget.NoTarget)
                    throw new NotSupportedException("Native inspection supports Editor fixtures only; use the bounded packaged-player decoder.");
            }
        }

        public static void InspectPlayerAgentResourcesBatch()
        {
            string input = Environment.GetEnvironmentVariable("QFL_AGENT_PLAYER_RESOURCES_PATH");
            string output = Environment.GetEnvironmentVariable("QFL_AGENT_PLAYER_INSPECTION_REPORT");
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
                throw new InvalidOperationException("Explicit owned input and inspection report paths are required.");
            try
            {
                AgentResourceInspection result = InspectSerializedAgentResources(input);
                File.WriteAllText(output, JsonUtility.ToJson(result, true) + "\n");
                Debug.Log("[QuestFlight][Security] Decoded agent templates=" + result.decoded_agent_templates + "; verified=" + result.verified + " (values redacted).");
                EditorApplication.Exit(result.verified ? 0 : 1);
            }
            catch (Exception error)
            {
                File.WriteAllText(output, JsonUtility.ToJson(new AgentResourceInspection {
                    limitation = "Serialized resource inspection failed: " + error.GetType().Name + "; credential values redacted."
                }, true) + "\n");
                EditorApplication.Exit(1);
            }
        }

        private static void SanitizeAgentAsset()
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(DevAgentAssetPath);
            if (asset == null) throw new BuildFailedException("Disabled development-agent template is missing.");
            SanitizeAgent(new SerializedObject(asset));
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            // SDK205 injects a token even for a disabled agent at callback order 1.
            // Run after it and before resource collection, preserving the host field.
            SanitizeAgentAsset();
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.Android) SanitizeAgentAsset();
        }

        public static string Quest3Manifest(string source)
        {
            var xml = new XmlDocument { XmlResolver = null };
            xml.LoadXml(source);
            XmlElement application = xml.SelectSingleNode("/manifest/application") as XmlElement;
            if (application == null) throw new BuildFailedException("Generated Android manifest has no application.");
            XmlElement supported = null;
            foreach (XmlNode node in application.SelectNodes("meta-data"))
            {
                var element = (XmlElement)node;
                if (element.GetAttribute("name", AndroidNamespace) != "com.oculus.supportedDevices") continue;
                if (supported != null) throw new BuildFailedException("Duplicate supportedDevices metadata.");
                supported = element;
            }
            if (supported == null)
            {
                supported = xml.CreateElement("meta-data");
                supported.SetAttribute("name", AndroidNamespace, "com.oculus.supportedDevices");
                application.AppendChild(supported);
            }
            supported.SetAttribute("value", AndroidNamespace, "quest3");
            return xml.OuterXml;
        }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest)) throw new BuildFailedException("Generated Android manifest is missing.");
            File.WriteAllText(manifest, Quest3Manifest(File.ReadAllText(manifest)));
        }
    }

    internal sealed class MetaOpenXrLoaderGradleDeduplicator : IPostGenerateGradleAndroidProject
    {
        private static readonly Regex UnityLoaderDependency = new Regex(
            "(?m)^[ \\t]*implementation\\(name:\\s*['\\\"]openxr_loader['\\\"],\\s*ext:\\s*['\\\"]aar['\\\"]\\)\\s*\\r?\\n?",
            RegexOptions.CultureInvariant);

        private static readonly Regex MetaLoaderDependency = new Regex(
            "(?m)^[ \\t]*implementation\\(name:\\s*['\\\"]OVRPlugin['\\\"],\\s*ext:\\s*['\\\"]aar['\\\"]\\)\\s*$",
            RegexOptions.CultureInvariant);

        public int callbackOrder => int.MaxValue;

        public void OnPostGenerateGradleAndroidProject(string basePath)
        {
            string gradlePath = Path.Combine(basePath, "build.gradle");
            if (!File.Exists(gradlePath))
                throw new BuildFailedException(
                    "Generated unityLibrary/build.gradle is missing; cannot establish one OpenXR loader source.");

            string gradle = File.ReadAllText(gradlePath);
            MatchCollection unityMatches = UnityLoaderDependency.Matches(gradle);
            MatchCollection metaMatches = MetaLoaderDependency.Matches(gradle);
            if (unityMatches.Count > 1 || metaMatches.Count != 1)
                throw new BuildFailedException(
                    "Unexpected generated OpenXR loader dependencies. " +
                    $"unityOpenXrAar={unityMatches.Count}; metaOvrPluginAar={metaMatches.Count}");

            if (unityMatches.Count == 1)
            {
                string corrected = UnityLoaderDependency.Replace(gradle, string.Empty, 1);
                File.WriteAllText(gradlePath, corrected);
                Debug.Log(
                    "[QuestFlight][Build] Removed the redundant Unity openxr_loader AAR dependency; " +
                    "Meta OVRPlugin remains the sole Android OpenXR native-loader source.");
            }
            else
            {
                Debug.Log(
                    "[QuestFlight][Build] Unity openxr_loader AAR was already excluded; " +
                    "Meta OVRPlugin remains the sole Android OpenXR native-loader source.");
            }
        }
    }
}
