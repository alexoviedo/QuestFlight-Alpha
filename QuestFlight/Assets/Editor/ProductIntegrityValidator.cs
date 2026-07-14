using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using QuestFlight.Product;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;

namespace QuestFlight.Editor
{
    public sealed class ProductIntegrityReport
    {
        public readonly List<string> failures = new List<string>();
        public readonly List<string> warnings = new List<string>();
        public int assetCount;
        public int sceneObjectCount;
        public int rendererCount;
        public int triangleCount;
        public int materialCount;
        public string aircraftPresentation = "unclassified";

        public bool passed => failures.Count == 0;

        public override string ToString()
        {
            string summary =
                $"passed={passed}; assets={assetCount}; sceneObjects={sceneObjectCount}; renderers={rendererCount}; " +
                $"triangles={triangleCount}; materials={materialCount}; aircraft={aircraftPresentation}";
            if (failures.Count > 0) summary += "; failures=" + string.Join(" | ", failures);
            if (warnings.Count > 0) summary += "; warnings=" + string.Join(" | ", warnings);
            return summary;
        }
    }

    /// <summary>Read-only integrity gate for the clean product repository and its one authored scene.</summary>
    public static class ProductIntegrityValidator
    {
        private static readonly IReadOnlyDictionary<string, string> RequiredPackages =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["com.unity.inputsystem"] = "1.18.0",
                ["com.unity.cloud.gltfast"] = "6.13.0",
                ["com.unity.test-framework"] = "1.5.2",
                ["com.unity.xr.management"] = "4.5.4",
                ["com.unity.xr.core-utils"] = "2.5.3",
                ["com.unity.xr.openxr"] = "1.16.1"
            };

        private static readonly HashSet<string> AllowedManifestPackages = new HashSet<string>(StringComparer.Ordinal)
        {
            "com.unity.inputsystem",
            "com.unity.cloud.gltfast",
            "com.unity.test-framework",
            "com.unity.xr.management",
            "com.unity.xr.core-utils",
            "com.unity.xr.openxr",
            "com.unity.modules.animation",
            "com.unity.modules.androidjni",
            "com.unity.modules.audio",
            "com.unity.modules.imageconversion",
            "com.unity.modules.imgui",
            "com.unity.modules.jsonserialize",
            "com.unity.modules.physics",
            "com.unity.modules.ui",
            "com.unity.modules.uielements",
            "com.unity.modules.vr",
            "com.unity.modules.xr"
        };

        private static readonly string[] ForbiddenAssetPathTokens =
        {
            "InputLab",
            "QuestFirstViewRuntimeRepair",
            "KbduInspiredWorldBuilder",
            "RealKbduEnvironmentBuilder",
            "ProductionEnvironmentPrefabBaker",
            "Gaussian",
            "Splat",
            "Training",
            "DebugHud",
            "DebugHUD",
            "Usb2Ble",
            "USB2BLE",
            "HOTAS"
        };

        private static readonly HashSet<string> ApprovedJsbsimProductAssets =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Assets/Scripts/Flight/Backends/JSBSimNativeDevelopmentBackend.cs",
                "Assets/Editor/Tests/JSBSimPromotionEditModeTests.cs"
            };

        private static readonly string[] ForbiddenSceneComponentNames =
        {
            "QuestFirstViewRuntimeRepair",
            "KbduInspiredWorldBuilder",
            "RealKbduEnvironmentBuilder",
            "SceneryModeController",
            "MeshSceneryProvider",
            "SplatSceneryProvider",
            "QuestSplatRuntimeGateController",
            "JSBSimNativeFlightBackend",
            "JSBSimEditorSidecarBackend",
            "TrainingModeController",
            "PlaytestHud"
        };

        public const string AircraftAssetPath =
            "Assets/Resources/QuestFlightLab/ImportedAssets/Cessna172KogThorns/cessna172.glb";
        public const long AircraftAssetBytes = 26962216;
        public const string AircraftAssetSha256 =
            "a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872";
        public const string AircraftMetaSha256 =
            "caf371d9b2ab80ae89d3d69a7eb5f257ebf3d17cf4eaad171084ed63384327ab";

        [MenuItem("QuestFlight/Product/Validate Product Integrity")]
        public static void ValidateMenu()
        {
            ProductIntegrityReport report = Validate();
            if (!report.passed) throw new InvalidOperationException(report.ToString());
            Debug.Log("[QuestFlight][Integrity] " + report);
        }

        public static void ValidateBatch()
        {
            try
            {
                ValidateMenu();
                Debug.Log("[QuestFlight][Integrity] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static ProductIntegrityReport Validate()
        {
            ProductIntegrityReport report = new ProductIntegrityReport();
            ValidateManifest(report);
            ValidateProjectSettings(report);
            ValidateBuildSceneList(report);
            ValidateAircraftBinary(report);
            ValidateAssetPolicyAndGuidClosure(report);
            if (File.Exists(ProductProjectConfigurator.ProductionScenePath)) ValidateAuthoredScene(report);
            else report.failures.Add("authored production scene is missing: " + ProductProjectConfigurator.ProductionScenePath);
            return report;
        }

        private static void ValidateManifest(ProductIntegrityReport report)
        {
            const string manifestPath = "Packages/manifest.json";
            if (!File.Exists(manifestPath))
            {
                report.failures.Add("Packages/manifest.json is missing");
                return;
            }
            string json = File.ReadAllText(manifestPath);
            MatchCollection matches = Regex.Matches(json, "\\\"(?<id>[^\\\"]+)\\\"\\s*:\\s*\\\"(?<version>[^\\\"]+)\\\"");
            Dictionary<string, string> packages = matches.Cast<Match>().ToDictionary(
                match => match.Groups["id"].Value,
                match => match.Groups["version"].Value,
                StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> required in RequiredPackages)
            {
                if (!packages.TryGetValue(required.Key, out string version))
                    report.failures.Add("required package is missing: " + required.Key);
                else if (!string.Equals(version, required.Value, StringComparison.Ordinal))
                    report.failures.Add($"package {required.Key} must be pinned to {required.Value}, found {version}");
            }
            foreach (string package in packages.Keys.Where(package => !AllowedManifestPackages.Contains(package)))
                report.failures.Add("non-product package is present in manifest: " + package);
        }

        private static void ValidateProjectSettings(ProductIntegrityReport report)
        {
            if (!string.Equals(
                    PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android),
                    ProductProjectConfigurator.AndroidApplicationIdentifier,
                    StringComparison.Ordinal))
                report.failures.Add("Android application identifier is not " + ProductProjectConfigurator.AndroidApplicationIdentifier);
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
                report.failures.Add("Android target architecture must be ARM64 only");
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
                report.failures.Add("Android scripting backend must be IL2CPP");
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                report.failures.Add("player color space must be Linear");
            GraphicsDeviceType[] graphicsApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            if (graphicsApis.Length != 1 || graphicsApis[0] != GraphicsDeviceType.Vulkan)
                report.failures.Add("Android graphics API must be Vulkan only");
            if (!PlayerSettings.GetMobileMTRendering(NamedBuildTarget.Android))
                report.failures.Add("Android multithreaded rendering must be enabled");
            if (EditorUserBuildSettings.androidBuildSubtarget != MobileTextureSubtarget.ASTC)
                report.failures.Add("Android texture-compression build target must be ASTC");

            string settingsText = File.Exists("ProjectSettings/ProjectSettings.asset")
                ? File.ReadAllText("ProjectSettings/ProjectSettings.asset")
                : string.Empty;
            if (!Regex.IsMatch(settingsText, @"(?m)^\s*activeInputHandler:\s*1\s*$"))
                report.failures.Add("active input handling must be Input System Package only");

            int originalQuality = QualitySettings.GetQualityLevel();
            try
            {
                for (int index = 0; index < QualitySettings.names.Length; index++)
                {
                    QualitySettings.SetQualityLevel(index, false);
                    if (QualitySettings.antiAliasing != 4)
                        report.failures.Add($"quality level '{QualitySettings.names[index]}' is not configured for 4x MSAA");
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(Mathf.Clamp(originalQuality, 0, QualitySettings.names.Length - 1), false);
            }

            if (!EditorBuildSettings.TryGetConfigObject(
                    XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget xrSettings) || xrSettings == null)
            {
                report.failures.Add("XR Management settings are not registered");
            }
            else
            {
                XRManagerSettings manager = xrSettings.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
                bool hasOpenXr = manager != null && manager.activeLoaders.Any(loader =>
                    loader != null && loader.GetType().FullName == "UnityEngine.XR.OpenXR.OpenXRLoader");
                if (!hasOpenXr) report.failures.Add("Android XR Management does not have the OpenXR loader");
            }
        }

        private static void ValidateBuildSceneList(ProductIntegrityReport report)
        {
            EditorBuildSettingsScene[] enabled = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            if (enabled.Length != 1 ||
                !string.Equals(enabled[0].path, ProductProjectConfigurator.ProductionScenePath, StringComparison.Ordinal))
                report.failures.Add("build settings must contain only ProductionVerticalSlice.unity");
        }

        private static void ValidateAircraftBinary(ProductIntegrityReport report)
        {
            if (!File.Exists(AircraftAssetPath))
            {
                report.failures.Add(
                    "verified CC-BY aircraft is missing; run scripts/sync_local_aircraft_asset.ps1 (expected " +
                    AircraftAssetSha256 + ")");
                return;
            }
            FileInfo aircraft = new FileInfo(AircraftAssetPath);
            if (aircraft.Length != AircraftAssetBytes)
                report.failures.Add($"aircraft byte length mismatch: {aircraft.Length}/{AircraftAssetBytes}");
            string aircraftHash = Sha256(AircraftAssetPath);
            if (!string.Equals(aircraftHash, AircraftAssetSha256, StringComparison.Ordinal))
                report.failures.Add($"aircraft SHA-256 mismatch: {aircraftHash}/{AircraftAssetSha256}");

            string metaPath = AircraftAssetPath + ".meta";
            if (!File.Exists(metaPath)) report.failures.Add("aircraft importer meta is missing: " + metaPath);
            else
            {
                string metaHash = Sha256(metaPath);
                if (!string.Equals(metaHash, AircraftMetaSha256, StringComparison.Ordinal))
                    report.failures.Add($"aircraft meta SHA-256 mismatch: {metaHash}/{AircraftMetaSha256}");
            }
        }

        private static string Sha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
        }

        private static void ValidateAssetPolicyAndGuidClosure(ProductIntegrityReport report)
        {
            string[] assets = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal))
                .ToArray();
            report.assetCount = assets.Length;
            foreach (string path in assets)
            foreach (string token in ForbiddenAssetPathTokens)
                if (path.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    report.failures.Add($"excluded R&D asset is present: {path} ({token})");
            foreach (string path in assets.Where(path =>
                         path.IndexOf("JSBSim", StringComparison.OrdinalIgnoreCase) >= 0 &&
                         !ApprovedJsbsimProductAssets.Contains(path)))
                report.failures.Add($"unapproved JSBSim asset is present: {path}");

            Regex guidPattern = new Regex(@"guid:\s*([0-9a-f]{32})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            foreach (string path in assets.Where(IsTextSerializedUnityAsset))
            {
                string text;
                try { text = File.ReadAllText(path); }
                catch (Exception exception)
                {
                    report.failures.Add($"could not inspect serialized asset {path}: {exception.Message}");
                    continue;
                }
                foreach (Match match in guidPattern.Matches(text))
                {
                    string guid = match.Groups[1].Value;
                    if (guid.All(character => character == '0')) continue;
                    if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                        report.failures.Add($"unresolved GUID {guid} referenced by {path}");
                }
            }
        }

        private static bool IsTextSerializedUnityAsset(string path)
        {
            string extension = Path.GetExtension(path);
            return extension == ".unity" || extension == ".prefab" || extension == ".asset" ||
                   extension == ".mat" || extension == ".controller" || extension == ".anim";
        }

        private static void ValidateAuthoredScene(ProductIntegrityReport report)
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                Scene scene = EditorSceneManager.OpenScene(
                    ProductProjectConfigurator.ProductionScenePath,
                    OpenSceneMode.Single);
                GameObject[] roots = scene.GetRootGameObjects();
                Transform[] transforms = roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
                report.sceneObjectCount = transforms.Length;

                GameObject productRoot = roots.SingleOrDefault(root => root.name == "ProductionWorldRoot");
                Require(productRoot != null, "scene must have exactly one root named ProductionWorldRoot", report);
                if (productRoot != null)
                {
                    ProductAttribution[] attributions = productRoot.GetComponents<ProductAttribution>();
                    Require(attributions.Length == 1,
                        $"ProductionWorldRoot must expose exactly one ProductAttribution component, found {attributions.Length}", report);
                    if (attributions.Length == 1)
                    {
                        string credits = attributions[0].CreditsText;
                        Require(credits.Contains("KOG_THORNS") && credits.Contains(ProductAttribution.AircraftLicenseUrl),
                            "runtime Credits/About metadata is missing KOG_THORNS CC BY 4.0 attribution", report);
                        Require(credits.Contains(ProductAttribution.OsmCredit) && credits.Contains(ProductAttribution.OsmLicenseUrl),
                            "runtime Credits/About metadata is missing OpenStreetMap/ODbL attribution", report);
                    }
                    Require(productRoot.transform.Find("AircraftSimulationRoot") != null,
                        "ProductionWorldRoot/AircraftSimulationRoot is missing", report);
                    Require(productRoot.transform.Find("AircraftSimulationRoot/CenterOfGravityReference") != null,
                        "CenterOfGravityReference is missing", report);
                    Require(productRoot.transform.Find("AircraftSimulationRoot/AircraftVisualRoot") != null,
                        "AircraftVisualRoot is missing", report);
                    Require(productRoot.transform.Find(
                                "AircraftSimulationRoot/AircraftVisualRoot/PilotSeatAnchor/UserViewCalibrationOffset/XR Origin") != null,
                        "explicit PilotSeatAnchor/UserViewCalibrationOffset/XR Origin hierarchy is missing", report);
                }

                Camera[] cameras = roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true)).ToArray();
                Camera[] mainCameras = cameras.Where(camera => camera.name == "Main Camera").ToArray();
                Camera[] enabledCameras = cameras.Where(camera =>
                    camera.enabled && camera.gameObject.activeInHierarchy).ToArray();
                Require(mainCameras.Length == 1,
                    $"scene must contain exactly one Main Camera, found {mainCameras.Length}", report);
                Require(enabledCameras.Length == 1 && enabledCameras[0] == mainCameras.SingleOrDefault(),
                    $"only the tracked Main Camera may be enabled at startup; enabled cameras={enabledCameras.Length}", report);
                if (mainCameras.Length == 1)
                {
                    Require(mainCameras[0].GetComponents<Component>().Any(component =>
                            component != null && component.GetType().Name == "TrackedPoseDriver"),
                        "Main Camera is not owned by TrackedPoseDriver", report);
                }
                Require(transforms.Count(transform => transform.name == "XR Origin") == 1,
                    "scene must contain exactly one XR Origin", report);

                MonoBehaviour[] behaviours = roots.SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
                ProductControlInput[] productInputs = behaviours.OfType<ProductControlInput>().ToArray();
                Require(productInputs.Length == 1,
                    $"scene must contain one ProductControlInput, found {productInputs.Length}", report);
                if (productInputs.Length == 1)
                    Require(productInputs[0].HasAuthoredSources,
                        "ProductControlInput gamepad/deterministic references are not serialized", report);

                ProductDemoPilot[] demos = behaviours.OfType<ProductDemoPilot>().ToArray();
                Require(demos.Length == 1,
                    $"scene must contain one minimal ProductDemoPilot acceptance route, found {demos.Length}", report);
                if (demos.Length == 1)
                {
                    Require(demos[0].HasAuthoredSources,
                        "ProductDemoPilot input references are not serialized", report);
                    Require(demos[0].AutoStartDemo,
                        "ProductDemoPilot must auto-start the installed acceptance route", report);
                }

                ProductionSeatCalibrationController[] seatControllers = behaviours
                    .OfType<ProductionSeatCalibrationController>().ToArray();
                Require(seatControllers.Length == 1,
                    $"scene must contain one seat calibration controller, found {seatControllers.Length}", report);
                if (seatControllers.Length == 1)
                {
                    Require(seatControllers[0].HasAuthoredReferences,
                        "seat calibration rig/profile/panel references are not serialized", report);
                    Require(seatControllers[0].AuthoredReferenceFrameRig != null &&
                            seatControllers[0].AuthoredReferenceFrameRig.ValidateHierarchy(),
                        "seat reset/recenter target is not the valid authored XR reference-frame rig", report);
                    Require(seatControllers[0].AuthoredSeatProfile != null &&
                            !string.IsNullOrWhiteSpace(seatControllers[0].AuthoredSeatProfile.aircraftId),
                        "seat persistence aircraft identity is missing", report);
                }

                QuestTemporalVisualGateRecorder[] temporalRecorders = behaviours
                    .OfType<QuestTemporalVisualGateRecorder>().ToArray();
                Require(temporalRecorders.Length == 1,
                    $"scene must contain one temporal recorder, found {temporalRecorders.Length}", report);
                if (temporalRecorders.Length == 1)
                    Require(temporalRecorders[0].HasAuthoredReferences,
                        "temporal recorder production/mountain references are not serialized", report);
                foreach (string forbidden in ForbiddenSceneComponentNames)
                    if (behaviours.Any(behaviour => behaviour != null && behaviour.GetType().Name == forbidden))
                        report.failures.Add("forbidden production-scene component is present: " + forbidden);

                FlightDynamicsCoordinator[] coordinators = behaviours
                    .OfType<FlightDynamicsCoordinator>()
                    .Where(coordinator => coordinator.enabled)
                    .ToArray();
                Require(coordinators.Length == 1,
                    $"exactly one enabled FlightDynamicsCoordinator authority is required, found {coordinators.Length}", report);
                if (coordinators.Length == 1)
                {
                    Require(coordinators[0].requestedBackend == FlightDynamicsBackendKind.UnityPrototype,
                        "the sole coordinator must request UnityPrototypeFlightBackend", report);
                    Require(coordinators[0].preferNativeInDevelopment,
                        "the sole coordinator must serialize the development-only native JSBSim preference", report);
                    Require(coordinators[0].HasAuthoredReferences(out string authorityBindingError),
                        "flight-authority references are not fully serialized: " + authorityBindingError, report);
                }

                int missingScripts = transforms.Sum(transform =>
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject));
                Require(missingScripts == 0, $"scene contains {missingScripts} missing MonoBehaviour script reference(s)", report);

                Renderer[] renderers = roots.SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
                report.rendererCount = renderers.Length;
                HashSet<Material> materials = new HashSet<Material>();
                int triangles = 0;
                foreach (Renderer renderer in renderers)
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null)
                        {
                            report.failures.Add("renderer has a missing material: " + HierarchyPath(renderer.transform));
                            continue;
                        }
                        materials.Add(material);
                        if (material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                            report.failures.Add("renderer has a missing/fallback shader: " + HierarchyPath(renderer.transform));
                    }
                }
                foreach (MeshFilter filter in roots.SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true)))
                {
                    if (filter.sharedMesh == null) continue;
                    for (int subMesh = 0; subMesh < filter.sharedMesh.subMeshCount; subMesh++)
                        triangles += (int)(filter.sharedMesh.GetIndexCount(subMesh) / 3);
                }
                report.triangleCount = triangles;
                report.materialCount = materials.Count;
                Require(renderers.Length <= 180, $"scene renderer budget exceeded: {renderers.Length}/180", report);
                Require(triangles <= 700000, $"scene triangle budget exceeded: {triangles}/700000", report);
                Require(materials.Count <= 40, $"scene material budget exceeded: {materials.Count}/40", report);

                Transform aircraftVisual = productRoot != null
                    ? productRoot.transform.Find("AircraftSimulationRoot/AircraftVisualRoot")
                    : null;
                if (aircraftVisual == null) report.aircraftPresentation = "missing";
                else if (aircraftVisual.GetComponentsInChildren<Renderer>(true).Length == 0)
                    report.aircraftPresentation = "diagnostic-no-renderers";
                else if (aircraftVisual.GetComponentsInChildren<Transform>(true).Any(transform =>
                             transform.name.IndexOf("placeholder", StringComparison.OrdinalIgnoreCase) >= 0))
                    report.aircraftPresentation = "rights-safe-placeholder";
                else report.aircraftPresentation = "extracted-aircraft";
            }
            finally
            {
                if (previousSetup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            }
        }

        private static string HierarchyPath(Transform transform)
        {
            List<string> names = new List<string>();
            for (Transform current = transform; current != null; current = current.parent) names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static void Require(bool condition, string message, ProductIntegrityReport report)
        {
            if (!condition) report.failures.Add(message);
        }
    }
}
