#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using QuestFlight.Editor;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.Presentation;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class CockpitDynamicsIntegrationEditModeTests
    {
        [Test]
        public void AuthoringBindsOnlyFourPositivelyRepresentedCockpitTargets()
        {
            WithAuthoredPrefab(root =>
            {
                Assert.That(
                    CockpitDynamicsIntegrationAuthoring.ValidateAuthoredPrefabContents(
                        root, out string error),
                    Is.True,
                    error);

                ProductionCockpitDynamicsController controller =
                    root.GetComponentInChildren<ProductionCockpitDynamicsController>(true);
                SerializedProperty bindings =
                    new SerializedObject(controller).FindProperty("bindings");
                Assert.That(
                    bindings.arraySize,
                    Is.EqualTo(
                        CockpitDynamicsIntegrationAuthoring.ExpectedPresentationBindingCount));

                Dictionary<string, CockpitPresentationChannel> expected =
                    new Dictionary<string, CockpitPresentationChannel>
                    {
                        [CockpitDynamicsIntegrationAuthoring.CopilotYokePitchPivotPath] =
                            CockpitPresentationChannel.YokePitchNormalized,
                        [CockpitDynamicsIntegrationAuthoring.CopilotYokeRollPivotPath] =
                            CockpitPresentationChannel.YokeRollNormalized,
                        [CockpitDynamicsIntegrationAuthoring.ThrottleTravelPivotPath] =
                            CockpitPresentationChannel.ThrottleNormalized,
                        [CockpitDynamicsIntegrationAuthoring.MixtureTravelPivotPath] =
                            CockpitPresentationChannel.MixtureNormalized
                    };
                for (int index = 0; index < bindings.arraySize; index++)
                {
                    SerializedProperty binding = bindings.GetArrayElementAtIndex(index);
                    Transform target = binding.FindPropertyRelative("target")
                        .objectReferenceValue as Transform;
                    Assert.That(target, Is.Not.Null);
                    string path = RelativePath(root.transform, target);
                    Assert.That(expected.ContainsKey(path), Is.True, path);
                    SerializedProperty motions = binding.FindPropertyRelative("motions");
                    Assert.That(motions.arraySize, Is.EqualTo(1), path);
                    Assert.That(
                        motions.GetArrayElementAtIndex(0)
                            .FindPropertyRelative("channel").intValue,
                        Is.EqualTo((int)expected[path]),
                        path);
                }
            });
        }

        [Test]
        public void WingFlapSelectorRemainsStaticAndUnboundWithoutProvenHinge()
        {
            WithAuthoredPrefab(root =>
            {
                ProductionCockpitDynamicsController presentation =
                    root.GetComponentInChildren<ProductionCockpitDynamicsController>(true);
                Transform selector = root.transform.Find(
                    CockpitDynamicsIntegrationAuthoring.WingFlapSelectorSourcePath);
                Assert.That(selector, Is.Not.Null);
                Assert.That(
                    root.transform.Find(
                        CockpitDynamicsIntegrationAuthoring.ImportedCockpitPath +
                        "/WingFlapSelectorTravelPivot"),
                    Is.Null);

                SerializedProperty bindings =
                    new SerializedObject(presentation).FindProperty("bindings");
                for (int index = 0; index < bindings.arraySize; index++)
                {
                    SerializedProperty binding = bindings.GetArrayElementAtIndex(index);
                    Assert.That(
                        binding.FindPropertyRelative("target").objectReferenceValue,
                        Is.Not.SameAs(selector));
                    Assert.That(
                        binding.FindPropertyRelative("motions")
                            .GetArrayElementAtIndex(0)
                            .FindPropertyRelative("channel").intValue,
                        Is.Not.EqualTo(
                            (int)CockpitPresentationChannel.FlapsCommandNormalized));
                }
            });
        }

        [Test]
        public void EngineControlsMovePredominantlyForwardAlongTheirOwnShaftGeometry()
        {
            WithAuthoredPrefab(root =>
            {
                ProductionCockpitSystemsController systems =
                    root.GetComponentInChildren<ProductionCockpitSystemsController>(true);
                ProductionCockpitDynamicsController presentation =
                    root.GetComponentInChildren<ProductionCockpitDynamicsController>(true);
                Transform visualRoot = root.transform.Find("AircraftVisualRoot");
                Transform throttle = root.transform.Find(
                    CockpitDynamicsIntegrationAuthoring.ThrottleTravelPivotPath);
                Transform mixture = root.transform.Find(
                    CockpitDynamicsIntegrationAuthoring.MixtureTravelPivotPath);
                Assert.That(visualRoot, Is.Not.Null);
                Assert.That(throttle, Is.Not.Null);
                Assert.That(mixture, Is.Not.Null);

                FlightDynamicsState state = RepresentativeState();
                FlightDynamicsControlSnapshot controls = RepresentativeControls();
                controls.throttle = 0f;
                controls.mixture = 0f;
                Assert.That(
                    systems.StepForTest(
                        state,
                        controls,
                        CockpitElectricalPowerInput.Unavailable,
                        1f / 72f),
                    Is.True);
                presentation.ApplySnapshotsForTest(state, controls, 1f / 72f);
                Vector3 throttleAftWorld = throttle.position;
                Vector3 mixtureCutoffWorld = mixture.position;

                controls.throttle = 1f;
                controls.mixture = 1f;
                Assert.That(
                    systems.StepForTest(
                        state,
                        controls,
                        CockpitElectricalPowerInput.Unavailable,
                        1f / 72f),
                    Is.True);
                presentation.ApplySnapshotsForTest(state, controls, 1f / 72f);

                AssertPredominantlyAircraftForward(
                    visualRoot,
                    throttle.position - throttleAftWorld,
                    "Throttle full-forward");
                AssertPredominantlyAircraftForward(
                    visualRoot,
                    mixture.position - mixtureCutoffWorld,
                    "Mixture full-rich");
            });
        }

        [Test]
        public void RudderIsAppendedOnceToExistingSurfaceAnimator()
        {
            WithAuthoredPrefab(root =>
            {
                ProductionAircraftControlAnimator animator =
                    root.GetComponentInChildren<ProductionAircraftControlAnimator>(true);
                SerializedProperty bindings =
                    new SerializedObject(animator).FindProperty("bindings");
                Assert.That(
                    bindings.arraySize,
                    Is.EqualTo(
                        CockpitDynamicsIntegrationAuthoring
                            .ExpectedAnimatorBindingCountAfterRudder));

                int rudderCount = 0;
                for (int index = 0; index < bindings.arraySize; index++)
                {
                    SerializedProperty binding = bindings.GetArrayElementAtIndex(index);
                    int channel = binding.FindPropertyRelative("channel").enumValueIndex;
                    if (channel !=
                        (int)ProductionAircraftControlAnimator.ControlChannel.Rudder)
                        continue;
                    rudderCount++;
                    Transform target = binding.FindPropertyRelative("target")
                        .objectReferenceValue as Transform;
                    Assert.That(
                        RelativePath(root.transform, target),
                        Is.EqualTo(CockpitDynamicsIntegrationAuthoring.RudderPivotPath));
                    Assert.That(
                        binding.FindPropertyRelative("minimumAngleDegrees").floatValue,
                        Is.EqualTo(
                            CockpitDynamicsIntegrationAuthoring
                                .RudderMaximumTravelDegrees).Within(0.0001f));
                    Assert.That(
                        binding.FindPropertyRelative("maximumAngleDegrees").floatValue,
                        Is.EqualTo(
                            -CockpitDynamicsIntegrationAuthoring
                                .RudderMaximumTravelDegrees).Within(0.0001f));
                    Assert.That(
                        Vector3.Distance(
                            binding.FindPropertyRelative("localRotationAxis").vector3Value,
                            Vector3.up),
                        Is.LessThan(0.0001f),
                        "Imported rudder must rotate about its vertical parent-local +Y axis.");

                    MeshFilter filter =
                        target.GetComponentInChildren<MeshFilter>(true);
                    Assert.That(filter, Is.Not.Null);
                    Assert.That(filter.sharedMesh, Is.Not.Null);
                    MeshBoundsInParent(
                        target.parent,
                        filter,
                        out Vector3 minimum,
                        out Vector3 maximum);
                    Vector3 size = maximum - minimum;
                    Assert.That(size.y, Is.GreaterThan(size.z),
                        "Imported rudder vertical span must be parent-local Y.");
                    Assert.That(size.z, Is.GreaterThan(size.x),
                        "Imported rudder chord must be parent-local Z and exceed X thickness.");
                    Vector3 expectedLeadingEdgeHinge = new Vector3(
                        (minimum.x + maximum.x) * 0.5f,
                        (minimum.y + maximum.y) * 0.5f,
                        maximum.z);
                    Assert.That(
                        Vector3.Distance(
                            target.localPosition,
                            expectedLeadingEdgeHinge),
                        Is.LessThan(0.0001f),
                        "Rudder pivot must sit at the X/Y centre of the maximum-Z " +
                        "leading edge, defining a vertical hinge line.");
                    Assert.That(
                        Quaternion.Angle(target.localRotation, Quaternion.identity),
                        Is.LessThan(0.001f),
                        "Rudder pivot axes must remain aligned with its aircraft parent.");
                }
                Assert.That(rudderCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void ExistingPilotAndSurfaceTargetsAreNeverDoubleWritten()
        {
            WithAuthoredPrefab(root =>
            {
                ProductionCockpitDynamicsController presentation =
                    root.GetComponentInChildren<ProductionCockpitDynamicsController>(true);
                ProductionAircraftControlAnimator animator =
                    root.GetComponentInChildren<ProductionAircraftControlAnimator>(true);
                HashSet<Transform> targets = new HashSet<Transform>();

                SerializedProperty presentationBindings =
                    new SerializedObject(presentation).FindProperty("bindings");
                for (int index = 0; index < presentationBindings.arraySize; index++)
                {
                    Transform target = presentationBindings.GetArrayElementAtIndex(index)
                        .FindPropertyRelative("target").objectReferenceValue as Transform;
                    Assert.That(targets.Add(target), Is.True, target.name);
                    Assert.That(target.GetComponent<Camera>(), Is.Null);
                }

                SerializedProperty animatorBindings =
                    new SerializedObject(animator).FindProperty("bindings");
                for (int index = 0; index < animatorBindings.arraySize; index++)
                {
                    Transform target = animatorBindings.GetArrayElementAtIndex(index)
                        .FindPropertyRelative("target").objectReferenceValue as Transform;
                    Assert.That(targets.Add(target), Is.True,
                        $"{target.name} is controlled by both update loops.");
                }

                string[] existingExclusiveTargets =
                {
                    "LeftAileronPivot", "RightAileronPivot",
                    "LeftFlapPivot", "RightFlapPivot",
                    "LeftElevatorPivot", "RightElevatorPivot",
                    "PilotYokePitchPivot", "PilotYokeRollPivot"
                };
                foreach (string name in existingExclusiveTargets)
                    Assert.That(
                        Array.Exists(
                            root.GetComponentsInChildren<Transform>(true),
                            candidate => candidate.name == name),
                        Is.True,
                        name);
            });
        }

        [Test]
        public void AuthoringPreservesSourceMeshesMaterialsAndWorldPose()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(
                CockpitDynamicsIntegrationAuthoring.AircraftPrefabPath);
            try
            {
                Transform copilot = FindOriginalOrAuthoredSource(
                    root,
                    CockpitDynamicsIntegrationAuthoring.CopilotYokeSourcePath,
                    CockpitDynamicsIntegrationAuthoring.CopilotYokeRollPivotPath +
                    "/Cessna_Interior_Steering_MAT_0.001");
                Transform throttle = FindOriginalOrAuthoredSource(
                    root,
                    CockpitDynamicsIntegrationAuthoring.ThrottlePath,
                    CockpitDynamicsIntegrationAuthoring.ThrottleTravelPivotPath +
                    "/Dynamic_Switch_005");
                Transform mixture = FindOriginalOrAuthoredSource(
                    root,
                    CockpitDynamicsIntegrationAuthoring.MixturePath,
                    CockpitDynamicsIntegrationAuthoring.MixtureTravelPivotPath +
                    "/Dynamic_Switch_006");
                Transform flap = root.transform.Find(
                    CockpitDynamicsIntegrationAuthoring.WingFlapSelectorSourcePath) ??
                    root.transform.Find(
                        CockpitDynamicsIntegrationAuthoring.ImportedCockpitPath +
                        "/WingFlapSelectorTravelPivot/Dynamic_Switch_007");
                Transform rudder = FindOriginalOrAuthoredSource(
                    root,
                    CockpitDynamicsIntegrationAuthoring.RudderSourcePath,
                    CockpitDynamicsIntegrationAuthoring.RudderPivotPath +
                    "/Cessna_Exterior_Body_MAT_0.008");
                Assert.That(copilot, Is.Not.Null);
                Assert.That(throttle, Is.Not.Null);
                Assert.That(mixture, Is.Not.Null);
                Assert.That(flap, Is.Not.Null);
                Assert.That(rudder, Is.Not.Null);
                SourceSnapshot copilotBefore = SourceSnapshot.Capture(copilot);
                SourceSnapshot throttleBefore = SourceSnapshot.Capture(throttle);
                SourceSnapshot mixtureBefore = SourceSnapshot.Capture(mixture);
                SourceSnapshot flapBefore = SourceSnapshot.Capture(flap);
                SourceSnapshot rudderBefore = SourceSnapshot.Capture(rudder);

                CockpitDynamicsIntegrationAuthoring.AuthorPrefabContents(root);

                copilotBefore.AssertPreserved(copilot);
                throttleBefore.AssertPreserved(throttle);
                mixtureBefore.AssertPreserved(mixture);
                flapBefore.AssertPreserved(flap);
                Assert.That(
                    root.transform.Find(
                        CockpitDynamicsIntegrationAuthoring.WingFlapSelectorSourcePath),
                    Is.SameAs(flap),
                    "The represented flap selector must be restored to its static source path.");
                rudderBefore.AssertPreserved(rudder);

                CockpitDynamicsIntegrationAuthoring.AuthorPrefabContents(root);
                Assert.That(
                    root.GetComponentsInChildren<ProductionCockpitSystemsController>(true).Length,
                    Is.EqualTo(1));
                Assert.That(
                    root.GetComponentsInChildren<ProductionCockpitDynamicsController>(true).Length,
                    Is.EqualTo(1));
                Assert.That(
                    CockpitDynamicsIntegrationAuthoring.ValidateAuthoredPrefabContents(
                        root, out string error),
                    Is.True,
                    error);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void UnsupportedControlsAndRuntimeDiscoveryStayAbsent()
        {
            string sourcePath = Path.Combine(
                Application.dataPath,
                "Scripts/Aircraft/CockpitDynamics/Presentation/" +
                "ProductionCockpitDynamicsController.cs");
            string source = File.ReadAllText(sourcePath);
            string[] forbiddenRuntimePatterns =
            {
                "Camera.main",
                "FindObjectOfType",
                "FindFirstObjectByType",
                "FindAnyObjectByType",
                "GameObject.Find",
                "GetComponent<",
                "AddComponent<",
                "new GameObject"
            };
            foreach (string pattern in forbiddenRuntimePatterns)
                StringAssert.DoesNotContain(pattern, source, pattern);

            WithAuthoredPrefab(root =>
            {
                ProductionCockpitDynamicsController controller =
                    root.GetComponentInChildren<ProductionCockpitDynamicsController>(true);
                SerializedProperty bindings =
                    new SerializedObject(controller).FindProperty("bindings");
                string[] prohibited =
                {
                    "master", "primer", "ignition", "key", "breaker",
                    "fuel selector", "instrument", "propeller", "wheel"
                };
                for (int index = 0; index < bindings.arraySize; index++)
                {
                    string label = bindings.GetArrayElementAtIndex(index)
                        .FindPropertyRelative("label").stringValue;
                    foreach (string value in prohibited)
                        Assert.That(
                            label.IndexOf(value, StringComparison.OrdinalIgnoreCase),
                            Is.LessThan(0),
                            label);
                }
            });
        }

        [Test]
        public void AuthoredSteadyStatePresentationDoesNotAllocateManagedMemory()
        {
            WithAuthoredPrefab(root =>
            {
                ProductionCockpitSystemsController systems =
                    root.GetComponentInChildren<ProductionCockpitSystemsController>(true);
                ProductionCockpitDynamicsController presentation =
                    root.GetComponentInChildren<ProductionCockpitDynamicsController>(true);
                FlightDynamicsState state = RepresentativeState();
                FlightDynamicsControlSnapshot controls = RepresentativeControls();
                Assert.That(
                    systems.StepForTest(
                        state,
                        controls,
                        CockpitElectricalPowerInput.Unavailable,
                        1f / 72f),
                    Is.True);

                for (int index = 0; index < 32; index++)
                    presentation.ApplySnapshotsForTest(state, controls, 1f / 72f);

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int index = 0; index < 4096; index++)
                {
                    state.simulationTimeSeconds += 1.0 / 72.0;
                    presentation.ApplySnapshotsForTest(state, controls, 1f / 72f);
                }
                long after = GC.GetAllocatedBytesForCurrentThread();
                Assert.That(after - before, Is.Zero);
            });
        }

        private static void WithAuthoredPrefab(Action<GameObject> action)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(
                CockpitDynamicsIntegrationAuthoring.AircraftPrefabPath);
            try
            {
                CockpitDynamicsIntegrationAuthoring.AuthorPrefabContents(root);
                action(root);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static FlightDynamicsState RepresentativeState()
        {
            return new FlightDynamicsState
            {
                simulationTimeSeconds = 12.0,
                latitudeDegrees = 40.03936527,
                longitudeDegrees = -105.22608958,
                altitudeMslMeters = 1700.0,
                altitudeAglMeters = 88.0,
                calibratedAirspeedKnots = 92.0,
                groundSpeedKnots = 90.0,
                verticalSpeedFeetPerMinute = 450.0,
                headingDegrees = 84.0,
                pitchDegrees = 4.0,
                bankDegrees = -12.0,
                angleOfAttackDegrees = 5.0,
                sideslipDegrees = 1.25,
                engineRpm = 2350.0,
                loadFactorG = 1.1,
                weightOnWheels = false,
                rotationUnity = Quaternion.identity,
                velocityUnityMetersPerSecond = new Vector3(0f, 2f, 45f),
                angularVelocityBodyDegreesPerSecond = new Vector3(0f, 2f, 0f)
            };
        }

        private static FlightDynamicsControlSnapshot RepresentativeControls()
        {
            return new FlightDynamicsControlSnapshot
            {
                aileron = 0.3f,
                elevator = -0.2f,
                rudder = 0.15f,
                throttle = 0.8f,
                mixture = 1f,
                trim = 0.05f,
                flaps = 1f / 3f,
                leftToeBrake = 0f,
                rightToeBrake = 0f
            };
        }

        private static string RelativePath(Transform root, Transform value)
        {
            List<string> parts = new List<string>();
            Transform current = value;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            Assert.That(current, Is.SameAs(root));
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static Transform FindOriginalOrAuthoredSource(
            GameObject root,
            string originalPath,
            string authoredPath)
        {
            return root.transform.Find(originalPath) ??
                   root.transform.Find(authoredPath);
        }

        private static void AssertPredominantlyAircraftForward(
            Transform aircraftVisualRoot,
            Vector3 worldDisplacement,
            string label)
        {
            Vector3 aircraftLocalDisplacement =
                aircraftVisualRoot.InverseTransformVector(worldDisplacement);
            Assert.That(
                aircraftLocalDisplacement.magnitude,
                Is.EqualTo(
                    CockpitDynamicsIntegrationAuthoring.EngineControlHalfTravelMeters * 2f)
                    .Within(0.0001f),
                $"{label} travel must preserve the authored 45 mm total stroke.");
            Assert.That(
                Vector3.Dot(
                    aircraftLocalDisplacement.normalized,
                    Vector3.forward),
                Is.GreaterThanOrEqualTo(0.95f),
                $"{label} must move toward panel/nose AircraftVisualRoot +Z, " +
                "not along a mesh-local or vertical axis.");
            Assert.That(
                aircraftLocalDisplacement.z,
                Is.GreaterThan(
                    Mathf.Abs(aircraftLocalDisplacement.x) +
                    Mathf.Abs(aircraftLocalDisplacement.y)),
                $"{label} displacement must be predominantly fore-aft.");
        }

        private static void MeshBoundsInParent(
            Transform parent,
            MeshFilter filter,
            out Vector3 minimum,
            out Vector3 maximum)
        {
            minimum = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            maximum = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);
            Bounds meshBounds = filter.sharedMesh.bounds;
            Vector3 center = meshBounds.center;
            Vector3 extents = meshBounds.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 meshLocalCorner = center + Vector3.Scale(
                    extents, new Vector3(x, y, z));
                Vector3 local = parent.InverseTransformPoint(
                    filter.transform.TransformPoint(meshLocalCorner));
                minimum = Vector3.Min(minimum, local);
                maximum = Vector3.Max(maximum, local);
            }
        }

        private readonly struct SourceSnapshot
        {
            private readonly Vector3 _position;
            private readonly Quaternion _rotation;
            private readonly Vector3 _scale;
            private readonly Mesh _mesh;
            private readonly Material[] _materials;

            private SourceSnapshot(
                Vector3 position,
                Quaternion rotation,
                Vector3 scale,
                Mesh mesh,
                Material[] materials)
            {
                _position = position;
                _rotation = rotation;
                _scale = scale;
                _mesh = mesh;
                _materials = materials;
            }

            public static SourceSnapshot Capture(Transform source)
            {
                MeshFilter filter = source.GetComponentInChildren<MeshFilter>(true);
                MeshRenderer renderer = source.GetComponentInChildren<MeshRenderer>(true);
                return new SourceSnapshot(
                    source.position,
                    source.rotation,
                    source.lossyScale,
                    filter.sharedMesh,
                    renderer.sharedMaterials);
            }

            public void AssertPreserved(Transform source)
            {
                MeshFilter filter = source.GetComponentInChildren<MeshFilter>(true);
                MeshRenderer renderer = source.GetComponentInChildren<MeshRenderer>(true);
                Assert.That(Vector3.Distance(source.position, _position), Is.LessThan(0.0001f));
                Assert.That(Quaternion.Angle(source.rotation, _rotation), Is.LessThan(0.001f));
                Assert.That(Vector3.Distance(source.lossyScale, _scale), Is.LessThan(0.0001f));
                Assert.That(filter.sharedMesh, Is.SameAs(_mesh));
                Assert.That(renderer.sharedMaterials, Is.EqualTo(_materials));
            }
        }
    }
}
#endif
