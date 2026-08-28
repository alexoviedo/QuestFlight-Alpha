#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using QuestFlightLab.Input;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Idempotently opts the existing serialized product input reader into the
    /// six-axis flight mapping. It never adds objects or components.
    /// </summary>
    public static class ProductExternalControllerCompatibilityAuthoring
    {
        public const string WorldPrefabPath =
            "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";

        [MenuItem("T2 Quest Flight/Authoring/Apply External Controller Compatibility V1")]
        public static void ApplyFromMenu() => Apply();

        public static void ApplyBatch()
        {
            try
            {
                Apply();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static bool Apply()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(WorldPrefabPath);
            if (root == null)
                throw new InvalidOperationException(
                    "ProductionWorldRoot prefab could not be loaded.");

            try
            {
                GamepadInputReader[] readers = root
                    .GetComponentsInChildren<GamepadInputReader>(true);
                ProductControlInput[] inputs = root
                    .GetComponentsInChildren<ProductControlInput>(true);
                Require(readers.Length == 1,
                    $"Expected one GamepadInputReader, found {readers.Length}.");
                Require(inputs.Length == 1,
                    $"Expected one ProductControlInput, found {inputs.Length}.");
                GamepadInputReader reader = readers.Single();
                Require(reader.transform.name == "ProductionInputRoot",
                    "GamepadInputReader must remain on ProductionInputRoot.");
                Require(inputs.Single().AuthoredGamepad == reader,
                    "ProductControlInput must reference the accepted GamepadInputReader.");

                bool serialized = File.ReadAllText(WorldPrefabPath).Contains(
                    "externalFlightControllersEnabled:");
                if (IsConfigured(reader) && serialized)
                    return false;

                reader.ConfigureExternalFlightControllers(
                    true,
                    ProductExternalControllerMapping.SixAxisFlightDefault());
                EditorUtility.SetDirty(reader);
                PrefabUtility.SaveAsPrefabAsset(root, WorldPrefabPath);
                AssetDatabase.SaveAssets();
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static bool IsConfigured(GamepadInputReader reader)
        {
            if (reader == null ||
                !reader.ExternalFlightControllersEnabled ||
                !reader.HasValidExternalMapping)
                return false;

            ProductExternalControllerMapping map = reader.ExternalMapping;
            return map.aileronAxis == ProductExternalControllerAxis.X &&
                   !map.invertAileron &&
                   map.elevatorAxis == ProductExternalControllerAxis.Y &&
                   map.invertElevator &&
                   map.throttleAxis == ProductExternalControllerAxis.Z &&
                   !map.invertThrottle &&
                   map.rudderAxis == ProductExternalControllerAxis.Rx &&
                   !map.invertRudder &&
                   map.leftToeBrakeAxis == ProductExternalControllerAxis.Ry &&
                   !map.invertLeftToeBrake &&
                   map.rightToeBrakeAxis == ProductExternalControllerAxis.Rz &&
                   !map.invertRightToeBrake &&
                   map.mixtureAxis == ProductExternalControllerAxis.None &&
                   map.flapsAxis == ProductExternalControllerAxis.None &&
                   Mathf.Approximately(map.primaryAxisDeadzone, 0.04f) &&
                   Mathf.Approximately(map.positionChangeEpsilon, 0.01f);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
#endif
