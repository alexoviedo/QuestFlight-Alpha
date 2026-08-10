#if UNITY_EDITOR
using NUnit.Framework;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.SystemsV2;
using QuestFlightLab.Aircraft.CockpitDynamics.TouchInputV2;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class CockpitSerializedAuthorityIntegrationEditModeTests
    {
        private const string WorldPrefabPath =
            "Assets/Resources/QuestFlightLab/Production/" +
            "ProductionWorldRoot.prefab";

        [Test]
        public void ProductionWorldRootSerializesOneCompleteCockpitAuthorityChain()
        {
            GameObject root =
                PrefabUtility.LoadPrefabContents(WorldPrefabPath);
            try
            {
                Assert.That(root.name, Is.EqualTo("ProductionWorldRoot"));

                FlightDynamicsCoordinator[] coordinators =
                    root.GetComponentsInChildren<
                        FlightDynamicsCoordinator>(true);
                ProductControlInput[] inputs =
                    root.GetComponentsInChildren<
                        ProductControlInput>(true);
                ProductionCockpitSystemsController[] systemsControllers =
                    root.GetComponentsInChildren<
                        ProductionCockpitSystemsController>(true);
                CockpitTouchInputV2Bridge[] bridges =
                    root.GetComponentsInChildren<
                        CockpitTouchInputV2Bridge>(true);
                CockpitTouchInteractionV2Controller[] touchControllers =
                    root.GetComponentsInChildren<
                        CockpitTouchInteractionV2Controller>(true);
                MonoBehaviour[] behaviours =
                    root.GetComponentsInChildren<MonoBehaviour>(true);
                int standaloneAuthorityCount = 0;
                for (int i = 0; i < behaviours.Length; i++)
                {
                    MonoBehaviour behaviour = behaviours[i];
                    if (behaviour != null &&
                        behaviour.GetType().FullName ==
                        "QuestFlightLab.Aircraft.CockpitDynamics.SystemsV2." +
                        "ProductionCockpitSystemsAuthorityV2")
                    {
                        standaloneAuthorityCount++;
                    }
                }

                Assert.That(coordinators, Has.Length.EqualTo(1));
                Assert.That(inputs, Has.Length.EqualTo(1));
                Assert.That(systemsControllers, Has.Length.EqualTo(1));
                Assert.That(bridges, Has.Length.EqualTo(1));
                Assert.That(touchControllers, Has.Length.EqualTo(1));
                Assert.That(
                    standaloneAuthorityCount,
                    Is.Zero,
                    "The consolidated systems controller is the sole " +
                    "cockpit-systems facade.");

                FlightDynamicsCoordinator coordinator = coordinators[0];
                ProductControlInput input = inputs[0];
                ProductionCockpitSystemsController systems =
                    systemsControllers[0];
                CockpitTouchInputV2Bridge bridge = bridges[0];
                CockpitTouchInteractionV2Controller touch =
                    touchControllers[0];

                Assert.That(coordinator.controls, Is.SameAs(input));
                Assert.That(coordinator.terrainSurface, Is.Not.Null);
                Assert.That(bridge.Coordinator, Is.SameAs(coordinator));
                Assert.That(bridge.InputAuthority, Is.SameAs(input));
                Assert.That(touch.CommandConsumer, Is.SameAs(bridge));
                Assert.That(input.HasAuthoredSources, Is.True);
                Assert.That(
                    bridge.ValidateAuthoredReferences(out string bridgeError),
                    Is.True,
                    bridgeError);
                Assert.That(
                    touch.ValidateAuthoredReferences(out string touchError),
                    Is.True,
                    touchError);

                SerializedObject serializedSystems =
                    new SerializedObject(systems);
                Assert.That(
                    serializedSystems.FindProperty("coordinator")
                        .objectReferenceValue,
                    Is.SameAs(coordinator));
                SerializedProperty scenario =
                    serializedSystems.FindProperty("systemsScenarioV2");
                Assert.That(scenario, Is.Not.Null);
                Assert.That(
                    scenario.FindPropertyRelative(
                        "hasAuthoritativeConfiguration").boolValue,
                    Is.True);
                Assert.That(
                    scenario.FindPropertyRelative(
                        "scenarioUtcUnixSeconds").longValue,
                    Is.EqualTo(1785434400L));
                Assert.That(
                    scenario.FindPropertyRelative(
                        "batteryMasterOn").boolValue,
                    Is.True);
                Assert.That(
                    scenario.FindPropertyRelative(
                        "alternatorMasterOn").boolValue,
                    Is.True);
                Assert.That(
                    scenario.FindPropertyRelative(
                        "avionicsMasterOn").boolValue,
                    Is.True);
                Assert.That(
                    scenario.FindPropertyRelative(
                        "ignitionSwitchPosition").enumValueIndex,
                    Is.EqualTo(
                        (int)CockpitIgnitionSwitchPositionV2.Both));
                Assert.That(
                    scenario.FindPropertyRelative(
                        "primerPositionNormalized").floatValue,
                    Is.EqualTo(0f));
                Assert.That(
                    scenario.FindPropertyRelative(
                        "fuelSelectorPosition").enumValueIndex,
                    Is.EqualTo(
                        (int)CockpitFuelSelectorPositionV2.Both));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
#endif
