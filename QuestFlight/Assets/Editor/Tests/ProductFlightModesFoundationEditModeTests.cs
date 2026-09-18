#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class ProductFlightModesFoundationEditModeTests
    {
        // September 14: native coordinator repinned for approved mandatory shared atmosphere; other frozen sources unchanged.
        private static readonly IReadOnlyDictionary<string, string> FrozenSha256 =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Assets/Production/Profiles/KBDU_ExtendedReservoirLessonV1.asset"] = "7569be8a2672a2a7a89ed3e522150c65e327c9f0e7cfe911d4d5930289e210aa",
                ["Assets/Scripts/Environment/GoldenEagleThermalSoaring.cs"] = "b4ee6522720493fbea23ef915344cfd3fa317f6e94356aff196abdd3426e1b23",
                ["Assets/Scripts/Flight/Backends/FlightDynamicsCoordinator.cs"] = "e67648f8b6eb025604ea6a8e925fe5d92a24bd34df490cc5130cade732e8e391",
                ["Assets/Scripts/Input/ProductControlInput.cs"] = "e88e565a00325675313c886a80c0e13d86e9ce25413a787546def3da059652cb",
                ["Assets/Scripts/Product/ProductDemoPilot.cs"] = "abe5d66c66350101d26e5e469b70b6fb699d3ce4aea8a2a0a25432d5f31fb3c5",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonAssistanceMixer.cs"] = "fb297d37923b9b629eed1ad8800b1bea7f424ecba1df397130d5591340f773c5",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonAssistanceMixer.cs.meta"] = "33737dd1003ac800fe690eae13f3dd34bc76d5539bc570f8cd544d20615e6c15",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonCrashClassifier.cs"] = "d589d8ec656ec9bff32b9ffe284a5a7bc9bad0feb4da1d1cfe632728bb9af5f7",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonCrashClassifier.cs.meta"] = "be067d7ecf1acc645a1691c1eac1b329f83eb0f4938fa9d9ef5588acd49ef666",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonGuidanceController.cs"] = "f72f80b2f4d2d1f0c56beea0209a9bf36b234f145561998667b95a2e4938b1bc",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonGuidanceController.cs.meta"] = "25656981b6976612c05fb96790453b25f9bb60f4bc9b25d43c78796e7ed7250a",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonNavigator.cs"] = "3846424a5dfc5a511c17f016aa4946b4b8b4b6c412a3a707bdb9a72882ee1357",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonNavigator.cs.meta"] = "a721a70c09c7f719dcc5c402b196e7c633419581ab74d55d1eda0860a6a6905b",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonPathMath.cs"] = "01319b367f4115d9974a9422e31fe139bcdeada43a50344d00011c3a2f86a432",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonPathMath.cs.meta"] = "56222e7af2d220f07a1150e61307a910a00f0f3a85f065a7a29c09b7ab9c1f42",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonRecovery.cs"] = "89f3cba65144266dbddc1741587ac99e794c9f8853d3a446c7318df52917694e",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonRecovery.cs.meta"] = "a0da365a0d8992027e5027177cb6ead9a3a2277aa0415a0e0005b6c6d5a6d535",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonRecoveryRuntimeAdapter.cs"] = "d4fcd253417f502e295e85e5f8c2892ce785abd76b6692cdbebee80f933b1d8d",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonRecoveryRuntimeAdapter.cs.meta"] = "8bb3b815a221cae66e44e6486223b8a925d0402066667408aa59cdc9daa48923",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonRouteData.cs"] = "039852310c0dd5f718893bdb960a7dd78e34fa0864413ba5632dd4ad45cb2650",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonRouteData.cs.meta"] = "1a530d68e40e9949c3e7ad1c45274842531cc2041e1b99f062b760cd08f642b8",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonRouteProfile.cs"] = "83ce4882dda6c3fe070c993bbcef71792a28847344ef2c3575f6fb19ceb467df",
                ["Assets/Scripts/Training/FlightLessonV1/FlightLessonRouteProfile.cs.meta"] = "c18443b476bf5056df24587edff6430bcf7ed8da9d4010714d68ab644a869d1e",
                ["Assets/Scripts/Training/FlightLessonV1/KbduExtendedReservoirLesson.cs"] = "3b7561326e062344470bd040b21e100358e0091e208ff13c6d732f632a732349",
                ["Assets/Scripts/Training/FlightLessonV1/KbduExtendedReservoirLesson.cs.meta"] = "d64a47ce77981d6a2f73d315f908c7f6eecd4fb6d2979c900032a5fddaed0245"
            };

        [Test]
        public void StateModelAndExecutionOrderAreExact()
        {
            Assert.That(Enum.GetNames(typeof(ProductFlightModeCoordinator.FlightModeState)),
                Is.EqualTo(new[] { "Selection", "Demo", "FreeFlight", "Blocked" }));
            DefaultExecutionOrder order = (DefaultExecutionOrder)Attribute.GetCustomAttribute(
                typeof(ProductFlightModeCoordinator), typeof(DefaultExecutionOrder));
            Assert.That(order, Is.Not.Null);
            Assert.That(order.order, Is.LessThan(-100));
        }

        [Test]
        public void SerializedProductHasOneCoordinatorPanelAndEngineRunningInitialCondition()
        {
            GameObject world = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductFlightModesFoundationAuthoring.WorldPrefabPath);
            Assert.That(world, Is.Not.Null);
            ProductFlightModeCoordinator[] modes = world
                .GetComponentsInChildren<ProductFlightModeCoordinator>(true);
            Assert.That(modes, Has.Length.EqualTo(1));
            Assert.That(modes[0].transform.name, Is.EqualTo("ProductionInputRoot"));
            Assert.That(modes[0].HasAuthoredReferences, Is.True);
            Assert.That(modes[0].AuthoredDemoPilot.AutoStartDemo, Is.False);
            Assert.That(modes[0].AuthoredFlightCoordinator.initialConditionProvider,
                Is.Not.Null);
            Assert.That(modes[0].AuthoredFlightCoordinator.initialConditionProvider.engineRunning,
                Is.True);

            Transform calibration = world.transform.Find(
                "AircraftSimulationRoot/AircraftVisualRoot/PilotSeatAnchor/UserViewCalibrationOffset");
            Assert.That(calibration, Is.Not.Null);
            Transform[] panels = calibration.GetComponentsInChildren<Transform>(true)
                .Where(candidate => candidate.name ==
                    ProductFlightModesFoundationAuthoring.PanelName).ToArray();
            Assert.That(panels, Has.Length.EqualTo(1));
            Assert.That(panels[0].parent, Is.EqualTo(calibration));
            Assert.That(panels[0].gameObject.activeSelf, Is.False,
                "The serialized selector stays inactive until the early startup coordinator owns it.");
            ProductFlightModeMenuView view =
                panels[0].GetComponent<ProductFlightModeMenuView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(view.HasAuthoredReferences, Is.True);
            Assert.That(view.LayoutVersion,
                Is.EqualTo(ProductFlightModeMenuView.CurrentLayoutVersion));
            Assert.That(panels[0]
                .GetComponentsInChildren<ProductFlightModeMenuButton>(true),
                Has.Length.EqualTo(4));
            TextMesh[] contracts = panels[0]
                .GetComponentsInChildren<TextMesh>(true)
                .Where(text => text.name ==
                    ProductStartupShowroomAuthoring.StateContractName)
                .ToArray();
            Assert.That(contracts, Has.Length.EqualTo(1));
            Assert.That(contracts[0].GetComponent<Renderer>().enabled, Is.False);
            Assert.That(modes[0].AuthoredModePanelRoot, Is.EqualTo(panels[0].gameObject));
            Assert.That(modes[0].AuthoredModePanelText, Is.EqualTo(contracts[0]));
            Assert.That(modes[0].AuthoredModeMenuView, Is.EqualTo(view));
        }

        [Test]
        public void AuthoringIsByteIdempotentForItsTwoPrefabTargets()
        {
            string[] paths =
            {
                ProductFlightModesFoundationAuthoring.WorldPrefabPath,
                ProductFlightModesFoundationAuthoring.AircraftPrefabPath,
                ProductStartupShowroomAuthoring.ShellMaterialPath,
                ProductStartupShowroomAuthoring.SurfaceMaterialPath,
                ProductStartupShowroomAuthoring.AccentMaterialPath,
                ProductStartupShowroomAuthoring.OverlayMaterialPath,
                ProductStartupShowroomAuthoring.BackdropMaterialPath,
                ProductStartupShowroomAuthoring.TextMaterialPath
            };
            string[] before = paths.Select(Sha256).ToArray();
            ProductFlightModesFoundationAuthoring.Apply();
            string[] after = paths.Select(Sha256).ToArray();
            Assert.That(after, Is.EqualTo(before));
        }

        [Test]
        public void RuntimeCoordinatorContainsNoHierarchyConstructionDiscoveryOrTransformWrites()
        {
            string source = File.ReadAllText(
                "Assets/Scripts/Product/ProductFlightModeCoordinator.cs");
            foreach (string forbidden in new[]
                     {
                         "Find(", "FindObjectOfType", "FindObjectsOfType", "Resources.Load",
                         "Instantiate(", "new GameObject", "AddComponent", "SetPositionAndRotation",
                         ".position =", ".rotation =", ".localPosition =", ".localRotation ="
                     })
                Assert.That(source, Does.Not.Contain(forbidden), forbidden);
            Assert.That(source, Does.Not.Contain("AircraftControlState"));
            Assert.That(source, Does.Not.Contain("Main Camera"));
            Assert.That(source, Does.Not.Contain("XR Origin"));

            foreach (string menuSourcePath in new[]
                     {
                         "Assets/Scripts/Product/ProductFlightModeMenuView.cs",
                         "Assets/Scripts/Product/ProductFlightModeMenuButton.cs"
                     })
            {
                string menuSource = File.ReadAllText(menuSourcePath);
                foreach (string forbidden in new[]
                         {
                             "Find(", "FindObjectOfType", "FindObjectsOfType",
                             "Resources.Load", "Instantiate(", "new GameObject",
                             "AddComponent", "SetPositionAndRotation",
                             ".position =", ".rotation =", ".localPosition =",
                             ".localRotation ="
                         })
                    Assert.That(menuSource, Does.Not.Contain(forbidden),
                        menuSourcePath + ": " + forbidden);
            }
        }

        [Test]
        public void StartupShowroomUsesOneRendererOnlyBackdropAndFourDistinctMetaActions()
        {
            GameObject world = AssetDatabase.LoadAssetAtPath<GameObject>(
                ProductFlightModesFoundationAuthoring.WorldPrefabPath);
            Assert.That(world, Is.Not.Null);
            ProductFlightModeMenuView view = world
                .GetComponentsInChildren<ProductFlightModeMenuView>(true)
                .Single();
            ProductFlightModeMenuButton[] buttons = view
                .GetComponentsInChildren<ProductFlightModeMenuButton>(true);
            Assert.That(buttons.Select(button => button.Action).Distinct().Count(),
                Is.EqualTo(4));
            Assert.That(buttons.All(button => button.HasAuthoredReferences), Is.True);
            Assert.That(buttons.All(button => button.AuthoredMenuView == view), Is.True);
            Assert.That(buttons.All(button => button.PointableCount == 2), Is.True);
            Assert.That(view.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(view.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(view.GetComponentsInChildren<Collider>(true),
                Has.Length.EqualTo(4));
            Assert.That(view.AircraftPresentationRendererCount,
                Is.GreaterThan(0));
            Assert.That(view.InteractionPresentationRendererCount, Is.EqualTo(2));
            Assert.That(view.WorldPresentationRendererCount, Is.GreaterThan(0));
            Assert.That(view.ValidateWorldPresentationReferences(out string worldError),
                Is.True, worldError);
            Assert.That(view.MenuButtonCount, Is.EqualTo(4));

            Renderer[] suppressedRenderers = (Renderer[])typeof(ProductFlightModeMenuView)
                .GetField("aircraftPresentationRenderers",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(view);
            Renderer[] interactionRenderers = (Renderer[])typeof(ProductFlightModeMenuView)
                .GetField("interactionPresentationRenderers",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(view);
            Assert.That(suppressedRenderers, Is.Not.Null.And.Not.Empty);
            Assert.That(interactionRenderers, Has.Length.EqualTo(2));
            Assert.That(interactionRenderers.Intersect(suppressedRenderers), Is.Empty,
                "Touch-controller beams must remain visible in the showroom.");
            Assert.That(interactionRenderers.All(renderer =>
                    renderer.name == "Cube" &&
                    HasAncestorNamed(renderer.transform, "ControllerRayInteractor")),
                Is.True, "Only Touch-controller beams may bypass suppression.");
            Renderer[] controllerCursorRenderers = view.transform.root
                .GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.name == "OculusCursor" &&
                    HasAncestorNamed(renderer.transform, "ControllerRayInteractor"))
                .ToArray();
            Assert.That(controllerCursorRenderers, Has.Length.EqualTo(2));
            Assert.That(controllerCursorRenderers.All(renderer =>
                    suppressedRenderers.Contains(renderer)),
                Is.True,
                "Meta Touch cursor quads must remain suppressed in the showroom.");
            Renderer[] handRayRenderers = view.transform.root
                .GetComponentsInChildren<Renderer>(true)
                .Where(renderer => HasAncestorNamed(
                    renderer.transform, "Hand and No Controller") &&
                    (renderer.name == "Cube" || renderer.name == "OculusCursor"))
                .ToArray();
            Assert.That(handRayRenderers, Has.Length.EqualTo(4));
            Assert.That(handRayRenderers.All(renderer =>
                    suppressedRenderers.Contains(renderer)),
                Is.True,
                "Bare-hand ray/cursor geometry must remain suppressed in the showroom.");

            Transform backdrop = view.GetComponentsInChildren<Transform>(true)
                .Single(transform => transform.name ==
                    ProductStartupShowroomAuthoring.BackdropName);
            Assert.That(backdrop.GetComponentsInChildren<Renderer>(true),
                Has.Length.EqualTo(1));
            Assert.That(backdrop.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);

            Transform shell = view.GetComponentsInChildren<Transform>(true)
                .Single(transform => transform.name ==
                    ProductStartupShowroomAuthoring.ShellName);
            Assert.That(shell.GetComponentsInChildren<Renderer>(true),
                Has.Length.EqualTo(1));
            Assert.That(shell.GetComponentsInChildren<Collider>(true), Is.Empty);

            Material backdropMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                ProductStartupShowroomAuthoring.BackdropMaterialPath);
            Material surfaceMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                ProductStartupShowroomAuthoring.SurfaceMaterialPath);
            Material overlayMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                ProductStartupShowroomAuthoring.OverlayMaterialPath);
            Assert.That(backdropMaterial.GetTag("RenderType", false),
                Is.EqualTo("Opaque"));
            Assert.That(surfaceMaterial.GetTag("RenderType", false),
                Is.EqualTo("Opaque"));
            Assert.That(overlayMaterial.GetTag("RenderType", false),
                Is.EqualTo("Transparent"));
        }

        private static bool HasAncestorNamed(Transform transform, string name)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (current.name == name) return true;
            }
            return false;
        }

        [Test]
        public void FrozenDemoInputAuthorityAndTrainingFilesRemainByteIdentical()
        {
            foreach (KeyValuePair<string, string> item in FrozenSha256)
            {
                Assert.That(File.Exists(item.Key), Is.True, item.Key);
                Assert.That(Sha256(item.Key), Is.EqualTo(item.Value), item.Key);
            }
        }

        private static string Sha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(stream)
                    .Select(value => value.ToString("x2")));
        }
    }
}
#endif
