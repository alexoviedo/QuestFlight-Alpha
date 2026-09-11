using System;
using System.Linq;
using QuestFlight.Product;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuestFlight.Editor
{
    /// <summary>Idempotently authors the serialized Flight Modes Foundation V1 objects.</summary>
    public static class ProductFlightModesFoundationAuthoring
    {
        public const string WorldPrefabPath =
            "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
        public const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        public const string PanelName = "FlightModesSelectorPanel";
        public const string InputRootName = "ProductionInputRoot";
        public const string CalibrationPath =
            "AircraftVisualRoot/PilotSeatAnchor/UserViewCalibrationOffset";

        [MenuItem("QuestFlight/Product/Author Flight Modes Foundation V1")]
        public static void RunInteractive() => Apply();

        public static void RunBatch()
        {
            try
            {
                Apply();
                Debug.Log("[QuestFlight][FlightModesAuthoring] SUCCESS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void Apply()
        {
            RequireAsset(WorldPrefabPath);
            RequireAsset(AircraftPrefabPath);
            ProductStartupShowroomAuthoring.EnsureMaterialAssets();
            AuthorAircraftPanel();
            AuthorWorldCoordinator();
            AssetDatabase.Refresh();
        }

        private static void AuthorAircraftPanel()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AircraftPrefabPath);
            try
            {
                Require(root.name == "ProductionC172AircraftRig",
                    "The aircraft prefab asset root must remain ProductionC172AircraftRig.");
                Transform calibration = RequireChild(root.transform, CalibrationPath);
                ProductFlightModeMenuView view =
                    ProductStartupShowroomAuthoring.AuthorAircraftPresentation(
                        root,
                        calibration);
                Require(view.transform.parent == calibration,
                    $"{PanelName} must be a direct child of UserViewCalibrationOffset.");
                view.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, AircraftPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AuthorWorldCoordinator()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(WorldPrefabPath);
            try
            {
                Require(root.name == "ProductionWorldRoot",
                    "The world prefab root must remain ProductionWorldRoot.");
                Transform inputRoot = RequireChild(root.transform, InputRootName);
                ProductFlightModeCoordinator[] coordinators = root
                    .GetComponentsInChildren<ProductFlightModeCoordinator>(true);
                Require(coordinators.Length <= 1,
                    $"Refusing duplicate ProductFlightModeCoordinator components; found {coordinators.Length}.");
                ProductMapBoundaryRecovery[] boundaryRecoveries = root
                    .GetComponentsInChildren<ProductMapBoundaryRecovery>(true);
                Require(boundaryRecoveries.Length <= 1,
                    $"Refusing duplicate ProductMapBoundaryRecovery components; found {boundaryRecoveries.Length}.");

                ProductDemoPilot demo = RequireSingle<ProductDemoPilot>(root);
                ProductControlInput input = RequireSingle<ProductControlInput>(root);
                FlightDynamicsCoordinator flight = RequireSingle<FlightDynamicsCoordinator>(root);
                ProductionSeatCalibrationController seat =
                    RequireSingle<ProductionSeatCalibrationController>(root);
                Require(demo.transform == inputRoot && input.transform == inputRoot,
                    "ProductDemoPilot and ProductControlInput must remain on ProductionInputRoot.");

                Transform simulationRoot = RequireChild(root.transform, "AircraftSimulationRoot");
                Transform calibration = RequireChild(simulationRoot, CalibrationPath);
                Transform[] panels = calibration.GetComponentsInChildren<Transform>(true)
                    .Where(candidate => candidate.name == PanelName)
                    .ToArray();
                Require(panels.Length == 1,
                    $"Expected exactly one serialized {PanelName}, found {panels.Length}.");
                Require(panels[0].parent == calibration,
                    $"{PanelName} must be directly under UserViewCalibrationOffset.");
                ProductFlightModeMenuView view = panels[0]
                    .GetComponent<ProductFlightModeMenuView>();
                Require(view != null,
                    "The serialized startup showroom view is missing.");
                Require(view.ValidateAuthoredReferences(out string viewError),
                    "The serialized startup showroom is invalid: " + viewError);
                Transform environmentRoot = RequireChild(root.transform, "EnvironmentRoot");
                Transform presentationEffectsRoot =
                    RequireChild(root.transform, "PresentationEffects");
                Renderer[] worldPresentationRenderers = environmentRoot
                    .GetComponentsInChildren<Renderer>(true)
                    .Concat(presentationEffectsRoot.GetComponentsInChildren<Renderer>(true))
                    .Concat(root.GetComponentsInChildren<QuestFlightLab.Environment.AmbientAircraftActor>(true).SelectMany(actor => actor.renderers))
                    .Concat(root.GetComponentsInChildren<QuestFlightLab.Environment.BoulderWindSock>(true).SelectMany(sock => sock.GetComponentsInChildren<Renderer>(true)))
                    .Where(renderer => renderer != null)
                    .Distinct()
                    .ToArray();
                Require(worldPresentationRenderers.Length > 0,
                    "The accepted product world contains no presentation renderers.");
                view.ConfigureWorldPresentationRenderers(worldPresentationRenderers);
                Require(view.ValidateWorldPresentationReferences(
                        out string worldPresentationError),
                    worldPresentationError);
                EditorUtility.SetDirty(view);
                TextMesh[] stateContracts = panels[0]
                    .GetComponentsInChildren<TextMesh>(true)
                    .Where(text => text.name ==
                        ProductStartupShowroomAuthoring.StateContractName)
                    .ToArray();
                Require(stateContracts.Length == 1,
                    $"Expected exactly one hidden mode-state contract TextMesh, found {stateContracts.Length}.");

                ProductFlightModeCoordinator coordinator = coordinators.Length == 1
                    ? coordinators[0]
                    : inputRoot.gameObject.AddComponent<ProductFlightModeCoordinator>();
                Require(coordinator.transform == inputRoot,
                    "ProductFlightModeCoordinator must be serialized on ProductionInputRoot.");
                coordinator.ConfigureAuthoredReferences(
                    demo,
                    input,
                    flight,
                    seat,
                    panels[0].gameObject,
                    stateContracts[0],
                    view);
                ProductMapBoundaryRecovery boundaryRecovery =
                    boundaryRecoveries.Length == 1
                        ? boundaryRecoveries[0]
                        : inputRoot.gameObject.AddComponent<ProductMapBoundaryRecovery>();
                Require(boundaryRecovery.transform == inputRoot,
                    "ProductMapBoundaryRecovery must be serialized on ProductionInputRoot.");
                boundaryRecovery.ConfigureAuthoredReferences(
                    coordinator,
                    input,
                    flight,
                    flight.terrainSurface);
                ProductStartupShowroomAuthoring.BindWorldAuthority(
                    coordinator,
                    view);
                demo.ConfigureAuthoredAutoStart(false);
                EditorUtility.SetDirty(coordinator);
                EditorUtility.SetDirty(boundaryRecovery);
                EditorUtility.SetDirty(demo);
                PrefabUtility.SaveAsPrefabAsset(root, WorldPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static T RequireSingle<T>(GameObject root) where T : Component
        {
            T[] components = root.GetComponentsInChildren<T>(true);
            Require(components.Length == 1,
                $"Expected exactly one {typeof(T).Name}, found {components.Length}.");
            return components[0];
        }

        private static Transform RequireChild(Transform root, string path)
        {
            Transform child = root.Find(path);
            Require(child != null, "Expected authored hierarchy is missing: " + path);
            return child;
        }

        private static void RequireAsset(string path) =>
            Require(AssetDatabase.LoadAssetAtPath<GameObject>(path) != null,
                "Required production prefab is missing: " + path);

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
