#if UNITY_EDITOR
using System.IO;
using NUnit.Framework;
using QuestFlight.Editor;
using QuestFlightLab.Aircraft.CockpitDynamics;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class CockpitMechanicalV2ContractEditModeTests
    {
        [Test]
        public void UnprovenMechanicalCandidatesFailClosedAtZeroRenderCost()
        {
            Assert.That(
                CockpitMechanicalV2Contract.RecordCount,
                Is.EqualTo(5));
            for (int index = 0;
                 index < CockpitMechanicalV2Contract.RecordCount;
                 index++)
            {
                CockpitMechanicalV2SourceDescriptor record =
                    CockpitMechanicalV2Contract.GetRecord(index);
                Assert.That(record.PreservesFrozenSources, Is.True, record.id);
            }

            Assert.That(
                CockpitMechanicalV2Contract.TryGetRecord(
                    "wing_flap_selector_static",
                    out CockpitMechanicalV2SourceDescriptor flap),
                Is.True);
            Assert.That(flap.HasRuntimeMotion, Is.False);
            Assert.That(
                flap.blocker,
                Is.EqualTo(
                    CockpitMechanicalV2Blocker
                        .FlapSelectorHingeArcAndDetentsUnproven));

            Assert.That(
                CockpitMechanicalV2Contract.TryGetRecord(
                    "rudder_toe_brake_pedals_excluded",
                    out CockpitMechanicalV2SourceDescriptor pedals),
                Is.True);
            Assert.That(pedals.HasRuntimeMotion, Is.False);
            Assert.That(pedals.auditedRepresentedAssemblyCount, Is.EqualTo(3));
            Assert.That(
                pedals.blocker,
                Is.EqualTo(
                    CockpitMechanicalV2Blocker
                        .PedalSetIncompleteAndHingesUnproven));

            Assert.That(
                CockpitMechanicalV2Contract.TryGetRecord(
                    "fixed_pitch_propeller_and_spinner_static",
                    out CockpitMechanicalV2SourceDescriptor propeller),
                Is.True);
            Assert.That(propeller.HasRuntimeMotion, Is.False);
            Assert.That(
                propeller.axisEvidence,
                Is.EqualTo(
                    CockpitMechanicalV2AxisEvidence
                        .MeshLocalPositiveZPropellerShaft));

            Assert.That(
                CockpitMechanicalV2Contract.TryGetRecord(
                    "fixed_gear_wheel_roll_excluded",
                    out CockpitMechanicalV2SourceDescriptor wheels),
                Is.True);
            Assert.That(wheels.HasRuntimeMotion, Is.False);
            Assert.That(
                wheels.blocker,
                Is.EqualTo(
                    CockpitMechanicalV2Blocker
                        .TireAndFixedWheelPantShareOneMesh));
        }

        [Test]
        public void FrozenPrefabSourceAndPropellerLayoutAreReadOnlyAndProven()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(
                CockpitMechanicalV2SourceAuthoring.AircraftPrefabPath);
            try
            {
                int rendererCount =
                    root.GetComponentsInChildren<Renderer>(true).Length;
                int transformCount =
                    root.GetComponentsInChildren<Transform>(true).Length;
                Assert.That(
                    CockpitMechanicalV2SourceAuthoring.ValidateFrozenSource(
                        root,
                        out string error),
                    Is.True,
                    error);
                Assert.That(
                    CockpitMechanicalV2SourceAuthoring.TryBuildPropellerLayout(
                        root,
                        out CockpitMechanicalV2SourceAuthoring
                            .PropellerLayoutDescriptor layout,
                        out error),
                    Is.True,
                    error);
                Assert.That(layout.propeller, Is.Not.Null);
                Assert.That(layout.spinner, Is.Not.Null);
                Assert.That(layout.rotationAxisWorld.sqrMagnitude,
                    Is.EqualTo(1f).Within(0.0001f));
                Assert.That(layout.preservesSourceWorldPoses, Is.True);
                Assert.That(layout.motionEnabled, Is.False);
                Assert.That(
                    root.GetComponentsInChildren<Renderer>(true).Length,
                    Is.EqualTo(rendererCount));
                Assert.That(
                    root.GetComponentsInChildren<Transform>(true).Length,
                    Is.EqualTo(transformCount));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void MechanicalV2SourceAuditContainsNoAuthoringMutation()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Editor/CockpitMechanicalV2SourceAuthoring.cs");
            string source = File.ReadAllText(path);
            string[] forbidden =
            {
                "AddComponent<",
                "new GameObject",
                ".SetParent(",
                "SaveAsPrefabAsset",
                "DestroyImmediate"
            };
            foreach (string value in forbidden)
                StringAssert.DoesNotContain(value, source, value);
        }
    }
}
#endif
