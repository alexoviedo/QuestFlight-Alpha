#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using QuestFlight.Editor;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.Presentation;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class CockpitMechanicalV2ContractEditModeTests
    {
        [Test]
        public void MechanicalCandidatesAreExplicitlyCompletedOrFailClosed()
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
                    "rudder_toe_brake_pedals_completed",
                    out CockpitMechanicalV2SourceDescriptor pedals),
                Is.True);
            Assert.That(pedals.HasRuntimeMotion, Is.True);
            Assert.That(pedals.auditedRepresentedAssemblyCount, Is.EqualTo(4));
            Assert.That(
                pedals.blocker,
                Is.EqualTo(CockpitMechanicalV2Blocker.None));
            Assert.That(
                pedals.layout,
                Is.EqualTo(
                    CockpitMechanicalV2LayoutKind.AuthoredFourPedalRigidBatch));
            Assert.That(
                pedals.axisEvidence,
                Is.EqualTo(
                    CockpitMechanicalV2AxisEvidence
                        .AuthoredRudderAndToeBrakeHingeAxes));

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
        public void CompletedPedalBatchHasFourAnimationReadyAssembliesAtOneDraw()
        {
            Assert.That(
                CockpitPedalCompletionAuthoring.ValidateAuthoredPrefab(
                    out string error),
                Is.True,
                error);

            GameObject root = PrefabUtility.LoadPrefabContents(
                CockpitPedalCompletionAuthoring.AircraftPrefabPath);
            try
            {
                Transform authored = Array.Find(
                    root.GetComponentsInChildren<Transform>(true),
                    value => value.name ==
                        CockpitPedalCompletionAuthoring.RootName);
                Assert.That(authored, Is.Not.Null);
                SkinnedMeshRenderer renderer =
                    authored.GetComponent<SkinnedMeshRenderer>();
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.enabled, Is.True);
                Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(1));
                Assert.That(renderer.bones.Length, Is.EqualTo(9));
                Assert.That(renderer.sharedMesh.vertexCount, Is.EqualTo(18903));
                Assert.That(
                    renderer.sharedMesh.triangles.Length / 3,
                    Is.EqualTo(16913));
                BoneWeight[] rigidWeights = renderer.sharedMesh.boneWeights;
                int[] rigidTriangles = renderer.sharedMesh.triangles;
                Assert.That(rigidWeights, Has.Length.EqualTo(renderer.sharedMesh.vertexCount));
                for (int vertex = 0; vertex < rigidWeights.Length; vertex++)
                {
                    Assert.That(rigidWeights[vertex].weight0, Is.EqualTo(1f),
                        $"Pedal vertex {vertex} is not rigidly weighted.");
                    Assert.That(rigidWeights[vertex].weight1, Is.Zero,
                        $"Pedal vertex {vertex} has blended ownership.");
                }
                for (int triangle = 0; triangle < rigidTriangles.Length; triangle += 3)
                {
                    int owner = rigidWeights[rigidTriangles[triangle]].boneIndex0;
                    Assert.That(rigidWeights[rigidTriangles[triangle + 1]].boneIndex0,
                        Is.EqualTo(owner), $"Triangle {triangle / 3} spans pedal bones.");
                    Assert.That(rigidWeights[rigidTriangles[triangle + 2]].boneIndex0,
                        Is.EqualTo(owner), $"Triangle {triangle / 3} spans pedal bones.");
                }
                StringAssert.Contains(
                    "\"source_connected_components\": 81",
                    File.ReadAllText(
                        CockpitPedalCompletionAuthoring.ManifestPath));
                StringAssert.Contains(
                    "\"mirrored_connected_components\": 16",
                    File.ReadAllText(
                        CockpitPedalCompletionAuthoring.ManifestPath));
                StringAssert.Contains(
                    "\"rejected_duplicate_components\": 2",
                    File.ReadAllText(
                        CockpitPedalCompletionAuthoring.ManifestPath));
                StringAssert.Contains(
                    "\"rejected_duplicate_triangles\": 16",
                    File.ReadAllText(
                        CockpitPedalCompletionAuthoring.ManifestPath));

                string[] pivots =
                {
                    CockpitPedalCompletionAuthoring.PilotLeftRudderName,
                    CockpitPedalCompletionAuthoring.PilotLeftToeName,
                    CockpitPedalCompletionAuthoring.PilotRightRudderName,
                    CockpitPedalCompletionAuthoring.PilotRightToeName,
                    CockpitPedalCompletionAuthoring.CopilotLeftRudderName,
                    CockpitPedalCompletionAuthoring.CopilotLeftToeName,
                    CockpitPedalCompletionAuthoring.CopilotRightRudderName,
                    CockpitPedalCompletionAuthoring.CopilotRightToeName
                };
                foreach (string pivot in pivots)
                    Assert.That(
                        Array.Exists(
                            authored.GetComponentsInChildren<Transform>(true),
                            value => value.name == pivot),
                        Is.True,
                        pivot);
                var fixedPivotPositions = new Dictionary<string, Vector3>();
                foreach (string pivot in pivots)
                {
                    Transform target = Array.Find(
                        authored.GetComponentsInChildren<Transform>(true),
                        value => value.name == pivot);
                    fixedPivotPositions.Add(pivot, target.localPosition);
                }

                ProductionCockpitDynamicsController presentation =
                    root.GetComponentInChildren<ProductionCockpitDynamicsController>(true);
                Assert.That(presentation, Is.Not.Null);
                Assert.That(
                    presentation.ValidateAuthoredBindings(out error),
                    Is.True,
                    error);
                ProductionCockpitDynamicsController.TransformBinding[] bindings =
                    presentation.CopyAuthoredBindingsForEditor();
                int rudderBindings = 0;
                int leftBrakeBindings = 0;
                int rightBrakeBindings = 0;
                foreach (ProductionCockpitDynamicsController.TransformBinding binding in bindings)
                {
                    if (binding?.target == null || !binding.target.IsChildOf(authored))
                        continue;
                    Assert.That(binding.motions, Has.Length.EqualTo(1), binding.label);
                    ProductionCockpitDynamicsController.MotionTerm motion = binding.motions[0];
                    Assert.That(motion.localRotationAxis, Is.EqualTo(Vector3.right));
                    Assert.That(
                        motion.responseRatePerSecond,
                        Is.EqualTo(CockpitPedalCompletionAuthoring
                            .PedalResponseRatePerSecond));
                    switch (motion.channel)
                    {
                        case CockpitPresentationChannel.RudderPedalsNormalized:
                            rudderBindings++;
                            Assert.That(motion.inputMinimum, Is.EqualTo(-1f));
                            Assert.That(motion.inputMaximum, Is.EqualTo(1f));
                            Assert.That(
                                Mathf.Abs(motion.minimumAngleDegrees),
                                Is.EqualTo(CockpitPedalCompletionAuthoring
                                    .RudderPedalMaximumTravelDegrees));
                            Assert.That(
                                motion.maximumAngleDegrees,
                                Is.EqualTo(-motion.minimumAngleDegrees));
                            break;
                        case CockpitPresentationChannel.LeftToeBrakeNormalized:
                            leftBrakeBindings++;
                            Assert.That(motion.minimumAngleDegrees, Is.Zero);
                            Assert.That(
                                motion.maximumAngleDegrees,
                                Is.EqualTo(CockpitPedalCompletionAuthoring
                                    .ToeBrakeMaximumTravelDegrees));
                            break;
                        case CockpitPresentationChannel.RightToeBrakeNormalized:
                            rightBrakeBindings++;
                            Assert.That(motion.minimumAngleDegrees, Is.Zero);
                            Assert.That(
                                motion.maximumAngleDegrees,
                                Is.EqualTo(CockpitPedalCompletionAuthoring
                                    .ToeBrakeMaximumTravelDegrees));
                            break;
                        default:
                            Assert.Fail($"Unexpected pedal channel: {motion.channel}");
                            break;
                    }
                }
                Assert.That(rudderBindings, Is.EqualTo(4));
                Assert.That(leftBrakeBindings, Is.EqualTo(2));
                Assert.That(rightBrakeBindings, Is.EqualTo(2));

                ProductionCockpitDynamicsController.TransformBinding[] pedalBindings =
                    Array.FindAll(
                        bindings,
                        binding => binding?.target != null &&
                                   binding.target.IsChildOf(authored));
                presentation.ConfigureAuthoredReferences(null, null, pedalBindings);
                presentation.ApplySnapshotsForTest(
                    default,
                    new FlightDynamicsControlSnapshot
                    {
                        rudder = 1f,
                        leftToeBrake = 1f,
                        rightToeBrake = 0f
                    },
                    0f);
                Transform pilotLeft = authored.Find(
                    CockpitPedalCompletionAuthoring.PilotLeftRudderName);
                Transform pilotRight = authored.Find(
                    CockpitPedalCompletionAuthoring.PilotRightRudderName);
                Transform copilotLeft = authored.Find(
                    CockpitPedalCompletionAuthoring.CopilotLeftRudderName);
                Transform copilotRight = authored.Find(
                    CockpitPedalCompletionAuthoring.CopilotRightRudderName);
                AssertRotation(
                    pilotLeft,
                    -CockpitPedalCompletionAuthoring.RudderPedalMaximumTravelDegrees);
                AssertRotation(
                    pilotRight,
                    CockpitPedalCompletionAuthoring.RudderPedalMaximumTravelDegrees);
                AssertRotation(
                    copilotLeft,
                    -CockpitPedalCompletionAuthoring.RudderPedalMaximumTravelDegrees);
                AssertRotation(
                    copilotRight,
                    CockpitPedalCompletionAuthoring.RudderPedalMaximumTravelDegrees);
                AssertRotation(
                    pilotLeft.Find(CockpitPedalCompletionAuthoring.PilotLeftToeName),
                    CockpitPedalCompletionAuthoring.ToeBrakeMaximumTravelDegrees);
                AssertRotation(
                    copilotLeft.Find(CockpitPedalCompletionAuthoring.CopilotLeftToeName),
                    CockpitPedalCompletionAuthoring.ToeBrakeMaximumTravelDegrees);
                AssertRotation(
                    pilotRight.Find(CockpitPedalCompletionAuthoring.PilotRightToeName),
                    0f);
                AssertRotation(
                    copilotRight.Find(CockpitPedalCompletionAuthoring.CopilotRightToeName),
                    0f);
                foreach (KeyValuePair<string, Vector3> fixedPivot in fixedPivotPositions)
                {
                    Transform target = Array.Find(
                        authored.GetComponentsInChildren<Transform>(true),
                        value => value.name == fixedPivot.Key);
                    Assert.That(target.localPosition, Is.EqualTo(fixedPivot.Value),
                        $"Pedal hinge translated during animation: {fixedPivot.Key}.");
                }
                string manifest = File.ReadAllText(
                    CockpitPedalCompletionAuthoring.ManifestPath);
                StringAssert.Contains("\"runtime_motion_enabled\": true", manifest);
                StringAssert.Contains("\"runtime_motion_bindings\": 8", manifest);
                StringAssert.Contains(
                    "\"skinning_ownership\": \"welded_component_rigid\"",
                    manifest);
                StringAssert.Contains("\"mixed_bone_triangles\": 0", manifest);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AssertRotation(Transform target, float expectedDegrees)
        {
            Assert.That(target, Is.Not.Null);
            Assert.That(
                Quaternion.Angle(
                    target.localRotation,
                    Quaternion.AngleAxis(expectedDegrees, Vector3.right)),
                Is.LessThan(0.01f),
                target.name);
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
