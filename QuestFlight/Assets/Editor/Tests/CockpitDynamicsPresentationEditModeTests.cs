#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using QuestFlightLab.Aircraft.CockpitDynamics.Presentation;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class CockpitDynamicsPresentationEditModeTests
    {
        [Test]
        public void OneBindingComposesRotationAndTranslationWithoutWritingCamera()
        {
            GameObject root = new GameObject("CockpitDynamicsTestRoot");
            GameObject targetObject = new GameObject("AuthoredYokeTarget");
            GameObject cameraObject = new GameObject("OpenXRCameraTestSentinel");
            try
            {
                targetObject.transform.SetParent(root.transform, false);
                targetObject.transform.localPosition = new Vector3(0.2f, 0.3f, 0.4f);
                cameraObject.transform.position = new Vector3(10f, 20f, 30f);
                cameraObject.transform.rotation = Quaternion.Euler(11f, 22f, 33f);
                Vector3 cameraPosition = cameraObject.transform.position;
                Quaternion cameraRotation = cameraObject.transform.rotation;

                ProductionCockpitDynamicsController controller =
                    root.AddComponent<ProductionCockpitDynamicsController>();
                ProductionCockpitDynamicsController.TransformBinding binding =
                    new ProductionCockpitDynamicsController.TransformBinding
                    {
                        label = "Positively identified pilot yoke",
                        target = targetObject.transform,
                        motions = new[]
                        {
                            new ProductionCockpitDynamicsController.MotionTerm
                            {
                                label = "Yoke roll",
                                channel = CockpitPresentationChannel.YokeRollNormalized,
                                inputMinimum = -1f,
                                inputMaximum = 1f,
                                localRotationAxis = Vector3.forward,
                                minimumAngleDegrees = -45f,
                                maximumAngleDegrees = 45f
                            },
                            new ProductionCockpitDynamicsController.MotionTerm
                            {
                                label = "Yoke pitch",
                                channel = CockpitPresentationChannel.YokePitchNormalized,
                                inputMinimum = -1f,
                                inputMaximum = 1f,
                                localTranslationAxis = Vector3.back,
                                minimumTranslationMeters = -0.04f,
                                maximumTranslationMeters = 0.04f
                            }
                        }
                    };
                controller.ConfigureAuthoredReferences(null, null, new[] { binding });

                FlightDynamicsControlSnapshot controls = FlightDynamicsControlSnapshot.NeutralIdle;
                controls.aileron = 1f;
                controls.elevator = 1f;
                controller.ApplySnapshotsForTest(default, controls, 1f / 72f);

                Assert.That(
                    Quaternion.Angle(
                        targetObject.transform.localRotation,
                        Quaternion.AngleAxis(45f, Vector3.forward)),
                    Is.LessThan(0.01f));
                Assert.That(
                    Vector3.Distance(
                        targetObject.transform.localPosition,
                        new Vector3(0.2f, 0.3f, 0.36f)),
                    Is.LessThan(0.0001f));
                Assert.That(cameraObject.transform.position, Is.EqualTo(cameraPosition));
                Assert.That(cameraObject.transform.rotation, Is.EqualTo(cameraRotation));
                Assert.That(controller.GetComponentsInChildren<Camera>(true), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void AltimeterCardUsesMslAltitudeAndContinuousWrappedDomain()
        {
            GameObject root = new GameObject("AltimeterTestRoot");
            GameObject card = new GameObject("AuthoredAltimeterCard");
            try
            {
                card.transform.SetParent(root.transform, false);
                ProductionCockpitDynamicsController controller =
                    root.AddComponent<ProductionCockpitDynamicsController>();
                controller.ConfigureAuthoredReferences(
                    null,
                    null,
                    new[]
                    {
                        new ProductionCockpitDynamicsController.TransformBinding
                        {
                            label = "Positively identified 1000-foot altimeter card",
                            target = card.transform,
                            motions = new[]
                            {
                                new ProductionCockpitDynamicsController.MotionTerm
                                {
                                    label = "MSL altitude, one revolution per 1000 feet",
                                    channel = CockpitPresentationChannel.AltitudeMslFeet,
                                    inputRemap =
                                        ProductionCockpitDynamicsController.InputRemapMode.Wrap,
                                    inputMinimum = 0f,
                                    inputMaximum = 1000f,
                                    localRotationAxis = Vector3.forward,
                                    minimumAngleDegrees = 0f,
                                    maximumAngleDegrees = -360f
                                }
                            }
                        }
                    });

                FlightDynamicsState state = new FlightDynamicsState
                {
                    altitudeMslMeters = 152.4,
                    altitudeAglMeters = 15.24
                };
                controller.ApplySnapshotsForTest(
                    state, FlightDynamicsControlSnapshot.NeutralIdle, 1f / 72f);

                Assert.That(
                    Quaternion.Angle(
                        card.transform.localRotation,
                        Quaternion.AngleAxis(-180f, Vector3.forward)),
                    Is.LessThan(0.01f),
                    "The card must use 500 feet MSL, not the 50-foot AGL convenience altitude.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void AuthoredProviderOverridesOnlyChannelsItOwns()
        {
            GameObject root = new GameObject("ProviderOverrideTestRoot");
            GameObject target = new GameObject("AuthoredFlapIndicator");
            try
            {
                target.transform.SetParent(root.transform, false);
                CockpitPresentationTestProvider provider =
                    root.AddComponent<CockpitPresentationTestProvider>();
                provider.channel = CockpitPresentationChannel.ActualFlapDegrees;
                provider.value = 20f;

                ProductionCockpitDynamicsController controller =
                    root.AddComponent<ProductionCockpitDynamicsController>();
                controller.ConfigureAuthoredReferences(
                    null,
                    provider,
                    new[]
                    {
                        new ProductionCockpitDynamicsController.TransformBinding
                        {
                            label = "Positively identified flap indicator",
                            target = target.transform,
                            motions = new[]
                            {
                                new ProductionCockpitDynamicsController.MotionTerm
                                {
                                    label = "Actual flap angle",
                                    channel = CockpitPresentationChannel.ActualFlapDegrees,
                                    inputMinimum = 0f,
                                    inputMaximum = 30f,
                                    localRotationAxis = Vector3.forward,
                                    minimumAngleDegrees = 0f,
                                    maximumAngleDegrees = 90f
                                }
                            }
                        }
                    });

                controller.ApplySnapshotsForTest(
                    default, FlightDynamicsControlSnapshot.NeutralIdle, 1f / 72f);

                Assert.That(
                    Quaternion.Angle(
                        target.transform.localRotation,
                        Quaternion.AngleAxis(60f, Vector3.forward)),
                    Is.LessThan(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ValidationRejectsDuplicateTargetsAndUnidentifiedMotion()
        {
            GameObject root = new GameObject("ValidationTestRoot");
            GameObject target = new GameObject("DuplicatedTarget");
            try
            {
                target.transform.SetParent(root.transform, false);
                ProductionCockpitDynamicsController controller =
                    root.AddComponent<ProductionCockpitDynamicsController>();
                ProductionCockpitDynamicsController.TransformBinding first =
                    ValidAileronBinding(target.transform, "First");
                ProductionCockpitDynamicsController.TransformBinding duplicate =
                    ValidAileronBinding(target.transform, "Duplicate");
                controller.ConfigureAuthoredReferences(null, null, new[] { first, duplicate });

                Assert.That(controller.ValidateAuthoredBindings(out string error), Is.False);
                StringAssert.Contains("bound more than once", error);

                first.motions[0].channel = CockpitPresentationChannel.None;
                controller.ConfigureAuthoredReferences(null, null, new[] { first });
                Assert.That(controller.ValidateAuthoredBindings(out error), Is.False);
                StringAssert.Contains("unidentified motion", error);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MissingSystemChannelLeavesAuthoredRestPoseUntouched()
        {
            GameObject root = new GameObject("MissingChannelTestRoot");
            GameObject target = new GameObject("AuthoredOilPressureNeedle");
            try
            {
                target.transform.SetParent(root.transform, false);
                target.transform.localRotation = Quaternion.Euler(0f, 0f, 17f);
                Quaternion rest = target.transform.localRotation;
                ProductionCockpitDynamicsController controller =
                    root.AddComponent<ProductionCockpitDynamicsController>();
                controller.ConfigureAuthoredReferences(
                    null,
                    null,
                    new[]
                    {
                        new ProductionCockpitDynamicsController.TransformBinding
                        {
                            label = "Positively identified oil-pressure needle",
                            target = target.transform,
                            motions = new[]
                            {
                                new ProductionCockpitDynamicsController.MotionTerm
                                {
                                    label = "Oil pressure",
                                    channel = CockpitPresentationChannel.OilPressurePsi,
                                    inputMinimum = 0f,
                                    inputMaximum = 100f,
                                    localRotationAxis = Vector3.forward,
                                    minimumAngleDegrees = -70f,
                                    maximumAngleDegrees = 70f
                                }
                            }
                        }
                    });

                controller.ApplySnapshotsForTest(
                    default, FlightDynamicsControlSnapshot.NeutralIdle, 1f / 72f);

                Assert.That(Quaternion.Angle(target.transform.localRotation, rest),
                    Is.LessThan(0.001f),
                    "Unavailable systems values must not fabricate an indication.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void UninitializedCoordinatorLeavesAuthoredPoseUnchanged()
        {
            GameObject root = new GameObject("UninitializedCoordinatorTestRoot");
            GameObject target = new GameObject("FailClosedTarget");
            try
            {
                root.SetActive(false);
                target.transform.SetParent(root.transform, false);
                Quaternion rest = target.transform.localRotation;
                FlightDynamicsCoordinator coordinator =
                    root.AddComponent<FlightDynamicsCoordinator>();
                Assert.That(coordinator.IsInitialized, Is.False);

                ProductionCockpitDynamicsController controller =
                    root.AddComponent<ProductionCockpitDynamicsController>();
                controller.ConfigureAuthoredReferences(
                    coordinator,
                    null,
                    new[]
                    {
                        new ProductionCockpitDynamicsController.TransformBinding
                        {
                            label = "Fail-closed aileron sentinel",
                            target = target.transform,
                            motions = new[]
                            {
                                new ProductionCockpitDynamicsController.MotionTerm
                                {
                                    label = "Non-neutral-at-zero sentinel",
                                    channel =
                                        CockpitPresentationChannel.AileronNormalized,
                                    inputMinimum = -1f,
                                    inputMaximum = 1f,
                                    localRotationAxis = Vector3.forward,
                                    minimumAngleDegrees = 10f,
                                    maximumAngleDegrees = 30f
                                }
                            }
                        }
                    });

                MethodInfo lateUpdate =
                    typeof(ProductionCockpitDynamicsController).GetMethod(
                        "LateUpdate",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(lateUpdate, Is.Not.Null);
                lateUpdate.Invoke(controller, null);

                Assert.That(
                    Quaternion.Angle(target.transform.localRotation, rest),
                    Is.LessThan(0.001f),
                    "An authored but uninitialized coordinator must not animate controls.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void UnavailableAuthoredProviderNeverFallsThroughToRawControls()
        {
            GameObject root = new GameObject("UnavailableProviderTestRoot");
            GameObject target = new GameObject("ProviderFailClosedTarget");
            try
            {
                target.transform.SetParent(root.transform, false);
                Quaternion rest = target.transform.localRotation;
                CockpitPresentationTestProvider provider =
                    root.AddComponent<CockpitPresentationTestProvider>();
                provider.channel = CockpitPresentationChannel.ActualFlapDegrees;
                provider.value = 20f;

                ProductionCockpitDynamicsController controller =
                    root.AddComponent<ProductionCockpitDynamicsController>();
                controller.ConfigureAuthoredReferences(
                    null,
                    provider,
                    new[] { ValidAileronBinding(target.transform, "Provider unavailable") });

                FlightDynamicsControlSnapshot rawControls =
                    FlightDynamicsControlSnapshot.NeutralIdle;
                rawControls.aileron = 1f;
                controller.ApplySnapshotsForTest(default, rawControls, 1f / 72f);

                Assert.That(
                    Quaternion.Angle(target.transform.localRotation, rest),
                    Is.LessThan(0.001f),
                    "A serialized provider that lacks a channel must fail closed instead " +
                    "of leaking raw coordinator/default state into presentation.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static ProductionCockpitDynamicsController.TransformBinding ValidAileronBinding(
            Transform target,
            string label)
        {
            return new ProductionCockpitDynamicsController.TransformBinding
            {
                label = label,
                target = target,
                motions = new[]
                {
                    new ProductionCockpitDynamicsController.MotionTerm
                    {
                        label = "Aileron",
                        channel = CockpitPresentationChannel.AileronNormalized,
                        inputMinimum = -1f,
                        inputMaximum = 1f,
                        localRotationAxis = Vector3.forward,
                        minimumAngleDegrees = -20f,
                        maximumAngleDegrees = 20f
                    }
                }
            };
        }
    }

    public sealed class CockpitPresentationTestProvider :
        MonoBehaviour,
        ICockpitPresentationStateProvider
    {
        public CockpitPresentationChannel channel;
        public float value;

        public bool TryGetCockpitPresentationValue(
            CockpitPresentationChannel requestedChannel,
            out float presentationValue)
        {
            presentationValue = value;
            return requestedChannel == channel;
        }
    }
}
#endif
