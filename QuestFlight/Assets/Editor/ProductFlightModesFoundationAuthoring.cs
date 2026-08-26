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
                Transform[] panels = calibration.GetComponentsInChildren<Transform>(true)
                    .Where(candidate => candidate.name == PanelName)
                    .ToArray();
                Require(panels.Length <= 1,
                    $"Refusing duplicate {PanelName} objects; found {panels.Length}.");

                if (panels.Length == 0)
                {
                    ProductionSeatCalibrationController seat = root
                        .GetComponentsInChildren<ProductionSeatCalibrationController>(true)
                        .SingleOrDefault();
                    Require(seat != null && seat.AuthoredPanelRoot != null &&
                            seat.AuthoredPanelText != null,
                        "The expected serialized seat-calibration TextMesh panel is missing.");

                    GameObject panel = Object.Instantiate(seat.AuthoredPanelRoot, calibration, false);
                    panel.name = PanelName;
                    panel.transform.SetSiblingIndex(0);
                    panel.transform.localPosition = new Vector3(0.48f, -0.08f, 1.05f);
                    panel.transform.localRotation = Quaternion.identity;
                    panel.transform.localScale = Vector3.one;
                    panel.SetActive(false);

                    TextMesh[] texts = panel.GetComponentsInChildren<TextMesh>(true);
                    Require(texts.Length == 1,
                        $"The cloned mode panel must contain exactly one TextMesh, found {texts.Length}.");
                    texts[0].text = ProductFlightModeCoordinator.SelectionText;
                }

                Transform authoredPanel = calibration.GetComponentsInChildren<Transform>(true)
                    .Single(candidate => candidate.name == PanelName);
                Require(authoredPanel.parent == calibration,
                    $"{PanelName} must be a direct child of UserViewCalibrationOffset.");
                Require(authoredPanel.GetComponentsInChildren<TextMesh>(true).Length == 1,
                    $"{PanelName} must own exactly one TextMesh.");
                // Keep editor-only product audits from treating the selector's TextMesh
                // as an always-rendered product surface. The early-running coordinator
                // activates it during startup selection before the first frame.
                authoredPanel.gameObject.SetActive(false);
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
                TextMesh[] texts = panels[0].GetComponentsInChildren<TextMesh>(true);
                Require(texts.Length == 1,
                    $"Expected exactly one mode-panel TextMesh, found {texts.Length}.");

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
                    texts[0]);
                demo.ConfigureAuthoredAutoStart(false);
                EditorUtility.SetDirty(coordinator);
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
