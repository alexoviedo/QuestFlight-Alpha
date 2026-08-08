using System;
using System.Linq;
using QuestFlight.Product;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QuestFlight.Editor
{
    /// <summary>
    /// One-time, Editor-only productization pass. It serializes product-owned references into the
    /// extracted prefabs and scene; no corresponding runtime repair path exists.
    /// </summary>
    public static class ProductSceneReauthoring
    {
        private const string WorldPrefabPath =
            "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
        private const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        private const string FlightConfigPath =
            "Assets/Resources/C172StyleAircraftConfig.asset";
        private const string FlightLessonRouteProfilePath =
            "Assets/Production/Profiles/KBDU_ExtendedReservoirLessonV1.asset";
        private const string TerrainSurfacePath =
            "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset";

        [MenuItem("QuestFlight/Product/Reauthor Extracted Production Scene")]
        public static void Reauthor()
        {
            ReauthorFlightConfig();
            ReauthorFlightLessonRouteProfile();
            ReauthorAircraftPrefab();
            ReauthorEnvironmentPrefab();
            ReauthorWorldPrefab();
            ValidateAndSaveScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[QuestFlight][Reauthor] Product input, reservoir lesson, terrain-aware recovery, attribution, evidence, environment probe, and sole Unity authority are serialized.");
        }

        private static void ReauthorFlightConfig()
        {
            C172StyleAircraftConfig config =
                AssetDatabase.LoadAssetAtPath<C172StyleAircraftConfig>(FlightConfigPath);
            Require(config != null, "The production C172 flight configuration is missing.");

            // Touch every active nested contract so Unity serializes the effective values and
            // strips the retired duplicate scalar fields on save. This does not retune physics.
            Require(config.Speeds != null && config.Aero != null && config.Engine != null &&
                    config.Control != null && config.Gear != null && config.Training != null,
                "The production C172 flight configuration contains an invalid nested group.");
            EditorUtility.SetDirty(config);
        }

        private static void ReauthorFlightLessonRouteProfile()
        {
            FlightLessonRouteProfile profile =
                AssetDatabase.LoadAssetAtPath<FlightLessonRouteProfile>(
                    FlightLessonRouteProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<FlightLessonRouteProfile>();
                profile.name = "KBDU_ExtendedReservoirLessonV1";
                AssetDatabase.CreateAsset(profile, FlightLessonRouteProfilePath);
            }

            FlightLessonRouteDefinition definition =
                KbduExtendedReservoirLesson.CreateDefinition();
            Require(definition.TryValidate(out string failure),
                "The authored KBDU reservoir lesson is invalid: " + failure);
            profile.SetDefinitionForAuthoring(definition);
            EditorUtility.SetDirty(profile);
        }

        public static void ReauthorBatch()
        {
            try
            {
                Reauthor();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void ReauthorAircraftPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                Require(root.name == "ProductionC172AircraftRig",
                    "Aircraft prefab asset root must remain ProductionC172AircraftRig; the authored world instance owns the AircraftSimulationRoot name.");
                Require(CountMissingScripts(root) == 0, "Aircraft prefab contains missing scripts.");

                SimpleAircraftPhysics prototype = RequireOne<SimpleAircraftPhysics>(root);
                AircraftState state = RequireOne<AircraftState>(root);
                Rigidbody body = root.GetComponent<Rigidbody>();
                FlightDynamicsCoordinator coordinator = RequireOne<FlightDynamicsCoordinator>(root);
                FlightDynamicsInitialConditionProvider initial = RequireOne<FlightDynamicsInitialConditionProvider>(root);
                AircraftReferenceFrameRig rig = RequireOne<AircraftReferenceFrameRig>(root);

                Require(body != null, "AircraftSimulationRoot Rigidbody is missing.");
                Require(rig.ValidateHierarchy(), "Serialized aircraft/XR hierarchy is invalid.");
                coordinator.requestedBackend = FlightDynamicsBackendKind.UnityPrototype;
                coordinator.simulationRoot = root.transform;
                coordinator.presentationRoot = root.transform;
                coordinator.aircraftState = state;
                coordinator.unityPrototype = prototype;
                coordinator.authoritativeRigidbody = body;
                coordinator.controls = null;
                coordinator.initialConditionProvider = initial;
                coordinator.simulationRateHz = 120f;
                prototype.controls = null;
                prototype.state = state;

                EditorUtility.SetDirty(prototype);
                EditorUtility.SetDirty(coordinator);
                PrefabUtility.SaveAsPrefabAsset(root, AircraftPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ReauthorEnvironmentPrefab()
        {
            string path = ProductionEnvironmentRoot.PrefabAssetPath;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ProductionEnvironmentRoot contract = RequireOne<ProductionEnvironmentRoot>(root);
                MountainTemporalStabilityProbe probe = contract.GetComponent<MountainTemporalStabilityProbe>();
                if (probe == null) probe = contract.gameObject.AddComponent<MountainTemporalStabilityProbe>();
                probe.autoCaptureInPlayer = false;
                probe.automaticWarmupSeconds = MountainTemporalStabilityProbe.DefaultWarmupSeconds;
                probe.automaticCaptureSeconds = MountainTemporalStabilityProbe.DefaultCaptureSeconds;
                EditorUtility.SetDirty(probe);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ReauthorWorldPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(WorldPrefabPath);
            try
            {
                Require(root.name == "ProductionWorldRoot", "World prefab root name changed.");
                Require(CountMissingScripts(root) == 0,
                    "ProductionWorldRoot must be stripped of legacy missing-script blocks before reauthoring.");

                Transform inputRoot = root.transform.Find("ProductionInputRoot");
                Require(inputRoot != null, "ProductionInputRoot is missing.");
                GamepadInputReader gamepad = inputRoot.GetComponent<GamepadInputReader>();
                DeterministicGamepadInputSource deterministic = inputRoot.GetComponent<DeterministicGamepadInputSource>();
                Require(gamepad != null && deterministic != null, "Authored gamepad sources are missing.");

                ProductControlInput productInput = inputRoot.GetComponent<ProductControlInput>();
                if (productInput == null) productInput = inputRoot.gameObject.AddComponent<ProductControlInput>();
                productInput.ConfigureAuthoredSources(gamepad, deterministic);
                productInput.SetDeterministicMode(false);

                ProductDemoPilot demo = inputRoot.GetComponent<ProductDemoPilot>();
                if (demo == null) demo = inputRoot.gameObject.AddComponent<ProductDemoPilot>();

                FlightLessonRouteProfile routeProfile =
                    AssetDatabase.LoadAssetAtPath<FlightLessonRouteProfile>(
                        FlightLessonRouteProfilePath);
                ProductionTerrainSurfaceAsset terrainSurface =
                    AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(
                        TerrainSurfacePath);
                Require(routeProfile != null,
                    "The authored KBDU reservoir lesson profile is missing.");
                Require(terrainSurface != null,
                    "The authored production terrain surface is missing.");

                ProductAttribution attribution = root.GetComponent<ProductAttribution>();
                if (attribution == null) attribution = root.AddComponent<ProductAttribution>();
                QuestTemporalVisualGateRecorder recorder = root.GetComponent<QuestTemporalVisualGateRecorder>();
                if (recorder == null) recorder = root.AddComponent<QuestTemporalVisualGateRecorder>();

                ProductionVerticalSliceRoot productionMarker = RequireOne<ProductionVerticalSliceRoot>(root);
                MountainTemporalStabilityProbe mountainProbe = RequireOne<MountainTemporalStabilityProbe>(root);
                recorder.ConfigureAuthoredReferences(productionMarker, mountainProbe);

                FlightDynamicsCoordinator coordinator = RequireOne<FlightDynamicsCoordinator>(root);
                demo.ConfigureAuthoredSources(productInput, deterministic, coordinator);
                demo.ConfigureAuthoredAutoStart(true);
                demo.ConfigureRouteProfileForAuthoring(routeProfile);

                FlightLessonRecoveryRuntimeAdapter recovery =
                    inputRoot.GetComponent<FlightLessonRecoveryRuntimeAdapter>();
                if (recovery == null)
                    recovery = inputRoot.gameObject.AddComponent<FlightLessonRecoveryRuntimeAdapter>();
                recovery.ConfigureAuthoredReferences(
                    demo,
                    productInput,
                    coordinator,
                    terrainSurface);
                SimpleAircraftPhysics prototype = RequireOne<SimpleAircraftPhysics>(root);
                AircraftState state = RequireOne<AircraftState>(root);
                AircraftReferenceFrameRig rig = RequireOne<AircraftReferenceFrameRig>(root);
                Rigidbody body = rig.GetComponent<Rigidbody>();
                Require(body != null, "Authored aircraft Rigidbody is missing.");

                prototype.controls = productInput;
                prototype.state = state;
                coordinator.requestedBackend = FlightDynamicsBackendKind.UnityPrototype;
                coordinator.simulationRoot = rig.transform;
                coordinator.presentationRoot = rig.transform;
                coordinator.aircraftState = state;
                coordinator.unityPrototype = prototype;
                coordinator.authoritativeRigidbody = body;
                coordinator.controls = productInput;

                ProductionAircraftControlAnimator animator = RequireOne<ProductionAircraftControlAnimator>(root);
                animator.ConfigureControlSources(coordinator, productInput);

                MarkNestedOverride(prototype);
                MarkNestedOverride(coordinator);
                MarkNestedOverride(animator);
                EditorUtility.SetDirty(productInput);
                EditorUtility.SetDirty(demo);
                EditorUtility.SetDirty(recovery);
                EditorUtility.SetDirty(attribution);
                EditorUtility.SetDirty(recorder);

                PrefabUtility.SaveAsPrefabAsset(root, WorldPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(WorldPrefabPath);
            Require(saved != null, "Saved world prefab could not be loaded.");
            Require(CountMissingScripts(saved) == 0, "Saved world prefab contains missing scripts.");
            Require(saved.GetComponents<ProductAttribution>().Length == 1,
                "Exactly one runtime ProductAttribution must be serialized on ProductionWorldRoot.");
            Require(saved.GetComponentsInChildren<ProductDemoPilot>(true).Length == 1,
                "Exactly one ProductDemoPilot must be serialized.");
            Require(saved.GetComponentsInChildren<ProductControlInput>(true).Length == 1,
                "Exactly one ProductControlInput must be serialized.");
            FlightLessonRecoveryRuntimeAdapter[] recoveryAdapters =
                saved.GetComponentsInChildren<FlightLessonRecoveryRuntimeAdapter>(true);
            Require(recoveryAdapters.Length == 1,
                "Exactly one FlightLessonRecoveryRuntimeAdapter must be serialized.");
            Require(recoveryAdapters[0].HasAuthoredReferences(out string recoveryFailure),
                "The serialized recovery adapter is invalid: " + recoveryFailure);
            ProductDemoPilot savedDemo =
                saved.GetComponentInChildren<ProductDemoPilot>(true);
            Require(savedDemo != null && savedDemo.AuthoredRouteProfile != null,
                "The serialized demo pilot must reference the authored lesson profile.");
            Require(saved.GetComponents<QuestTemporalVisualGateRecorder>().Length == 1,
                "Exactly one temporal gate recorder must be serialized on ProductionWorldRoot.");
        }

        private static void ValidateAndSaveScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ProductProjectConfigurator.ProductionScenePath, OpenSceneMode.Single);
            GameObject[] roots = scene.GetRootGameObjects();
            Require(roots.Length == 1 && roots[0].name == "ProductionWorldRoot",
                "ProductionVerticalSlice must serialize exactly one ProductionWorldRoot.");
            Require(CountMissingScripts(roots[0]) == 0, "Production scene contains missing scripts.");
            Require(roots[0].GetComponentsInChildren<ProductAttribution>(true).Length == 1,
                "Runtime Credits/About attribution is not serialized into the product scene closure.");
            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ProductProjectConfigurator.ProductionScenePath, true)
            };
        }

        private static T RequireOne<T>(GameObject root) where T : Component
        {
            T[] components = root.GetComponentsInChildren<T>(true);
            Require(components.Length == 1,
                $"Expected exactly one {typeof(T).Name} below {root.name}; found {components.Length}.");
            return components[0];
        }

        private static int CountMissingScripts(GameObject root)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .Sum(candidate => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(candidate.gameObject));
        }

        private static void MarkNestedOverride(UnityEngine.Object value)
        {
            EditorUtility.SetDirty(value);
            PrefabUtility.RecordPrefabInstancePropertyModifications(value);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
