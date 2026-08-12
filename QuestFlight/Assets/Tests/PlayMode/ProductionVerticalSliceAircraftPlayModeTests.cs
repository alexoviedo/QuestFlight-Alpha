#if UNITY_INCLUDE_TESTS
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Flight;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using QuestFlightLab.Environment;
using QuestFlightLab.Training.FlightLessonV1;
using QuestFlight.Product;
using UnityEngine;

namespace QuestFlightLab.Tests.PlayMode
{
    public sealed class ProductionVerticalSliceAircraftPlayModeTests
    {
        private const string WorldResourcePath = "QuestFlightLab/Production/ProductionWorldRoot";
        private GameObject _worldInstance;

        [TearDown]
        public void TearDown()
        {
            if (_worldInstance != null) Object.DestroyImmediate(_worldInstance);
        }

        [Test]
        public void ProductionSceneOwnsCompleteAircraftXrAndDemoHierarchy()
        {
            Transform world = LoadProductionWorld();
            string[] requiredPaths =
            {
                "ProductionVerticalSliceRoot",
                "EnvironmentRoot/ProductionEnvironmentRoot",
                "RunwayRoot",
                "ProductionInputRoot",
                "AircraftSimulationRoot/CenterOfGravityReference",
                "AircraftSimulationRoot/AircraftVisualRoot/ImportedAircraftExterior",
                "AircraftSimulationRoot/AircraftVisualRoot/ImportedCockpitInterior",
                "AircraftSimulationRoot/AircraftVisualRoot/AnimatedControlSurfaces",
                "AircraftSimulationRoot/AircraftVisualRoot/PilotSeatAnchor/UserViewCalibrationOffset/ProductOVRCameraRig/TrackingSpace/CenterEyeAnchor/Main Camera",
                "AircraftSimulationRoot/AircraftVisualRoot/PilotSeatAnchor/UserViewCalibrationOffset/ProductOVRCameraRig/TrackingSpace/LeftHandAnchor/LeftControllerAnchor/Left Touch Controller",
                "AircraftSimulationRoot/AircraftVisualRoot/PilotSeatAnchor/UserViewCalibrationOffset/ProductOVRCameraRig/TrackingSpace/RightHandAnchor/RightControllerAnchor/Right Touch Controller"
            };
            foreach (string path in requiredPaths) Assert.That(world.Find(path), Is.Not.Null, path);

            OVRCameraRig[] origins = ComponentsInWorld<OVRCameraRig>(world);
            Assert.That(origins, Has.Length.EqualTo(1));
            Assert.That(origins[0].isActiveAndEnabled, Is.True);
            Camera main = ComponentsInWorld<Camera>(world).Single(camera =>
                camera.CompareTag("MainCamera") && camera.enabled &&
                camera.gameObject.activeInHierarchy);
            Assert.That(TrackedXrCameraPoseDriver.HasRequiredBindings(main), Is.True);
            Assert.That(main.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(main.transform.localRotation, Is.EqualTo(Quaternion.identity));

            AircraftReferenceFrameRig rig = ComponentsInWorld<AircraftReferenceFrameRig>(world).Single();
            Assert.That(rig.ValidateHierarchy(), Is.True);
            Assert.That(rig.AircraftSimulationRoot.name, Is.EqualTo("AircraftSimulationRoot"));
            Assert.That(rig.PilotSeatAnchor.IsChildOf(rig.AircraftVisualRoot), Is.True);
            Assert.That(rig.XrOrigin.parent, Is.EqualTo(rig.UserViewCalibrationOffset));
            Assert.That(HasComponentNamed(world, "QuestFirstViewRuntimeRepair"), Is.False);

            Assert.That(ComponentsInWorld<DeterministicGamepadInputSource>(world), Has.Length.EqualTo(1));
            ProductDemoPilot demo = ComponentsInWorld<ProductDemoPilot>(world).Single();
            ProductControlInput mapper = ComponentsInWorld<ProductControlInput>(world).Single();
            Assert.That(mapper.DeterministicMode, Is.False);
            demo.StartDemo();
            Assert.That(mapper.DeterministicMode, Is.True);
            demo.StopDemo();

            FlightDynamicsCoordinator coordinator = ComponentsInWorld<FlightDynamicsCoordinator>(world).Single();
            Assert.That(coordinator.requestedBackend, Is.EqualTo(FlightDynamicsBackendKind.UnityPrototype));
            Assert.That(coordinator.preferNativeInDevelopment, Is.True);
            Assert.That(coordinator.simulationRoot, Is.EqualTo(rig.transform));
            Assert.That(coordinator.presentationRoot, Is.EqualTo(rig.transform));
            Assert.That(coordinator.initialConditionProvider, Is.Not.Null);
            Assert.That(coordinator.initialConditionProvider.spawnTransform, Is.EqualTo(rig.transform));
            Assert.That(coordinator.initialConditionProvider.derivePositionFromSpawnTransform, Is.True);
            Assert.That(coordinator.initialConditionProvider.deriveHeadingFromSpawnTransform, Is.True);
            ProductionEnvironmentRoot authoredEnvironment =
                ComponentsInWorld<ProductionEnvironmentRoot>(world).Single();
            Assert.That(coordinator.terrainSurface,
                Is.SameAs(authoredEnvironment.authoritativeTerrainSurface));
            Assert.That(ComponentsInWorld<SimpleAircraftPhysics>(world), Has.Length.EqualTo(1));

            Assert.That(coordinator.InitializeSelectedBackend(), Is.True, coordinator.LastError);
            ProductionEnvironmentRoot environment = authoredEnvironment;
            Vector3 expectedSpawn = environment.transform.TransformPoint(Vector3.Lerp(
                environment.runway08EndLocal,
                environment.runway26EndLocal,
                0.06f) + Vector3.up * 1.25f);
            Assert.That(Vector3.Distance(rig.transform.position, expectedSpawn), Is.LessThan(0.02f));
            Assert.That(coordinator.ResetToConfiguredInitialConditions(), Is.True, coordinator.LastError);
            Assert.That(Vector3.Distance(rig.transform.position, expectedSpawn), Is.LessThan(0.02f));

            Vector3 runwayDirection = Vector3.ProjectOnPlane(
                environment.transform.TransformDirection(environment.runway26EndLocal - environment.runway08EndLocal),
                Vector3.up).normalized;
            Assert.That(Vector3.Angle(rig.transform.forward, runwayDirection), Is.LessThan(0.1f));
            Assert.That(Mathf.Abs(rig.transform.position.y - expectedSpawn.y), Is.LessThan(0.005f));
        }

        [Test]
        public void ProductionSeatProfileUsesMeasuredAdditionalAftCorrection()
        {
            Transform world = LoadProductionWorld();
            ProductionVerticalSliceRoot marker = ComponentsInWorld<ProductionVerticalSliceRoot>(world).Single();
            PilotSeatProfile profile = marker.PilotSeatProfile;
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.aircraftId, Is.EqualTo(CockpitViewpointPersistence.DefaultAircraftId));
            Assert.That(profile.seatAnchorLocalPosition, Is.EqualTo(new Vector3(-0.28f, 0.72f, 0f)));
            Assert.That(profile.nominalEyeLocalPosition, Is.EqualTo(new Vector3(-0.28f, 0.94f, -0.23f)));
            Assert.That(profile.nominalEyeLocalPosition.z, Is.EqualTo(-0.23f).Within(0.0001f));
            Assert.That(profile.EyeToPanelForwardDistanceMeters(), Is.EqualTo(0.547f).Within(0.001f));
            Assert.That(profile.targetEyeToPanelDistanceMeters, Is.EqualTo(0.547f).Within(0.001f));
            Assert.That(marker.AircraftRig.PilotSeatAnchor.localPosition,
                Is.EqualTo(profile.nominalEyeLocalPosition));
        }

        [Test]
        public void AuthoredFlightLessonRecoverySuppressesResetsRestoresAndReenables()
        {
            Transform world = LoadProductionWorld();
            ProductDemoPilot demo = ComponentsInWorld<ProductDemoPilot>(world).Single();
            ProductControlInput input = ComponentsInWorld<ProductControlInput>(world).Single();
            FlightDynamicsCoordinator coordinator =
                ComponentsInWorld<FlightDynamicsCoordinator>(world).Single();
            FlightLessonRecoveryRuntimeAdapter recovery =
                ComponentsInWorld<FlightLessonRecoveryRuntimeAdapter>(world).Single();

            Assert.That(recovery.HasAuthoredReferences(out string referenceFailure),
                Is.True, referenceFailure);
            Assert.That(recovery.AuthoredDemoPilot, Is.SameAs(demo));
            Assert.That(recovery.AuthoredProductInput, Is.SameAs(input));
            Assert.That(recovery.AuthoredFlightCoordinator, Is.SameAs(coordinator));

            Assert.That(coordinator.InitializeSelectedBackend(), Is.True,
                coordinator.LastError);
            demo.StartDemo();
            InvokeUnityMessage(recovery, "Start");
            Assert.That(coordinator.IsInitialized, Is.True, coordinator.LastError);
            Assert.That(demo.IsRunning, Is.True);
            Assert.That(recovery.RecoveryPhase, Is.EqualTo(FlightLessonRecoveryPhase.Idle));
            Assert.That(input.TryBeginTouchOverride(
                ProductTouchControlChannel.PilotYoke,
                41001,
                0.35f,
                -0.2f), Is.True);
            Assert.That(input.ActiveTouchOverrideCount, Is.EqualTo(1));

            ProductionTerrainSurfaceAsset terrain = recovery.AuthoredTerrainSurface;
            const float outsideLessonEastMeters = 9500f;
            const float outsideLessonNorthMeters = 0f;
            Assert.That(terrain.TrySample(
                outsideLessonEastMeters,
                outsideLessonNorthMeters,
                out ProductionTerrainSample outsideTerrain), Is.True);

            GeodeticReference origin = GeodeticReference.Kbdu;
            Vector3 outsidePosition = new Vector3(
                outsideLessonEastMeters,
                outsideTerrain.heightMeters + 300f,
                outsideLessonNorthMeters);
            GeodeticReference outsideGeodetic =
                FlightFrameConversions.UnityToGeodetic(outsidePosition, origin);
            GeodeticReference outsideTerrainGeodetic =
                FlightFrameConversions.UnityToGeodetic(
                    new Vector3(
                        outsideLessonEastMeters,
                        outsideTerrain.heightMeters,
                        outsideLessonNorthMeters),
                    origin);
            FlightDynamicsInitialConditions lessonAreaExit =
                new FlightDynamicsInitialConditions
                {
                    latitudeDegrees = outsideGeodetic.latitudeDegrees,
                    longitudeDegrees = outsideGeodetic.longitudeDegrees,
                    altitudeMslMeters = outsideGeodetic.altitudeMslMeters,
                    terrainElevationMslMeters = outsideTerrainGeodetic.altitudeMslMeters,
                    calibratedAirspeedKnots = 80d,
                    headingDegrees = 90d,
                    pitchDegrees = 0d,
                    bankDegrees = 0d,
                    flightPathAngleDegrees = 0d,
                    engineRunning = true
                };
            Assert.That(coordinator.TryResetToInitialConditions(lessonAreaExit),
                Is.True, coordinator.LastError);

            bool sawFadingOut = false;
            bool sawAwaitingReset = false;
            bool sawStabilizing = false;
            bool sawFadingIn = false;
            bool sawSuppression = false;
            bool sawLeaseCancellation = false;
            bool verifiedCheckpointReset = false;
            bool recoveryStarted = false;
            FlightLessonRouteDefinition route = demo.RouteDefinition;
            FlightLessonRecoveryCheckpoint expectedCheckpoint =
                route.recoveryCheckpoints[0];

            const int recoveryFixedFrameLimit = 360;
            for (int frame = 0; frame < recoveryFixedFrameLimit; frame++)
            {
                InvokeUnityMessage(recovery, "FixedUpdate");
                Assert.That(coordinator.StepForTest(input.Current, Time.fixedDeltaTime),
                    Is.True, coordinator.LastError);

                FlightLessonRecoveryPhase phase = recovery.RecoveryPhase;
                recoveryStarted |= phase != FlightLessonRecoveryPhase.Idle;
                sawFadingOut |= phase == FlightLessonRecoveryPhase.FadingOut;
                sawAwaitingReset |= phase == FlightLessonRecoveryPhase.AwaitingAuthoritativeReset;
                sawStabilizing |= phase == FlightLessonRecoveryPhase.Stabilizing;
                sawFadingIn |= phase == FlightLessonRecoveryPhase.FadingIn;
                sawSuppression |= input.FlightLessonInputSuppressed &&
                                  demo.GuidanceSuppressed;
                sawLeaseCancellation |= input.ActiveTouchOverrideCount == 0;

                if (!verifiedCheckpointReset &&
                    phase == FlightLessonRecoveryPhase.Stabilizing)
                {
                    FlightDynamicsInitialConditions reset =
                        coordinator.LastResetInitialConditions;
                    Vector3 resetPosition = FlightFrameConversions.GeodeticToUnity(
                        reset.latitudeDegrees,
                        reset.longitudeDegrees,
                        reset.altitudeMslMeters,
                        origin);
                    Assert.That(resetPosition.x,
                        Is.EqualTo(expectedCheckpoint.positionUnityMeters.x).Within(0.1f));
                    Assert.That(resetPosition.z,
                        Is.EqualTo(expectedCheckpoint.positionUnityMeters.z).Within(0.1f));
                    Assert.That(reset.calibratedAirspeedKnots,
                        Is.EqualTo(expectedCheckpoint.calibratedAirspeedKnots).Within(0.001d));
                    Assert.That(reset.headingDegrees,
                        Is.EqualTo(expectedCheckpoint.headingDegreesTrue).Within(0.001d));
                    Assert.That(demo.ProgressState.currentSegmentIndex,
                        Is.EqualTo(expectedCheckpoint.resumeSegmentIndex));
                    verifiedCheckpointReset = true;
                }

                if (recoveryStarted &&
                    phase == FlightLessonRecoveryPhase.Idle)
                    break;
            }

            Assert.That(sawFadingOut, Is.True);
            Assert.That(sawAwaitingReset, Is.True);
            Assert.That(sawStabilizing, Is.True);
            Assert.That(sawFadingIn, Is.True);
            Assert.That(sawSuppression, Is.True);
            Assert.That(sawLeaseCancellation, Is.True);
            Assert.That(verifiedCheckpointReset, Is.True);
            Assert.That(recovery.RecoveryPhase, Is.EqualTo(FlightLessonRecoveryPhase.Idle));
            Assert.That(input.FlightLessonInputSuppressed, Is.False);
            Assert.That(demo.GuidanceSuppressed, Is.False);
            Assert.That(demo.IsRunning, Is.True);

            // Reproduce the headset failure condition: a later airborne recovery after
            // the player reduced throttle, with pause and the coordinator's fail-closed
            // component gate both latched. Recovery must clear both gates and prove the
            // same backend advances before releasing the fade.
            Assert.That(demo.RestoreLessonSegment(1), Is.True);
            Assert.That(coordinator.TryResetToInitialConditions(lessonAreaExit),
                Is.True, coordinator.LastError);
            SetAutoProperty(input, nameof(ProductControlInput.Paused), true);
            input.Current.throttle = 0f;
            coordinator.enabled = false;

            bool secondRecoveryStarted = false;
            double secondResetSimulationTime = 0d;
            Vector3 secondResetPosition = default;
            for (int frame = 0; frame < recoveryFixedFrameLimit; frame++)
            {
                InvokeUnityMessage(recovery, "FixedUpdate");
                FlightLessonRecoveryPhase phase = recovery.RecoveryPhase;
                secondRecoveryStarted |= phase != FlightLessonRecoveryPhase.Idle;
                if (coordinator.enabled)
                {
                    Assert.That(coordinator.StepForTest(input.Current, Time.fixedDeltaTime),
                        Is.True, coordinator.LastError);
                }

                if (phase == FlightLessonRecoveryPhase.AwaitingAuthoritativeReset &&
                    coordinator.enabled)
                {
                    secondResetSimulationTime = coordinator.CurrentState.simulationTimeSeconds;
                    secondResetPosition = coordinator.CurrentState.positionUnityMeters;
                }

                if (secondRecoveryStarted && phase == FlightLessonRecoveryPhase.Idle)
                    break;
            }

            Assert.That(secondRecoveryStarted, Is.True);
            Assert.That(recovery.RecoveryPhase, Is.EqualTo(FlightLessonRecoveryPhase.Idle),
                recovery.LastFailure);
            Assert.That(coordinator.enabled, Is.True);
            Assert.That(input.Paused, Is.False);
            Assert.That(input.FlightLessonInputSuppressed, Is.False);
            Assert.That(coordinator.CurrentState.simulationTimeSeconds,
                Is.GreaterThan(secondResetSimulationTime));
            Assert.That(Vector3.Distance(
                    secondResetPosition,
                    coordinator.CurrentState.positionUnityMeters),
                Is.GreaterThan(0.05f));
            Assert.That(coordinator.LastResetInitialConditions.calibratedAirspeedKnots,
                Is.EqualTo(route.recoveryCheckpoints[1].calibratedAirspeedKnots).Within(0.001d));
            Assert.That(input.Current.throttle, Is.GreaterThan(0f),
                "Low throttle at impact must not leave the recovered aircraft frozen.");
            Assert.That(input.TryBeginTouchOverride(
                ProductTouchControlChannel.PilotYoke,
                41002,
                0f,
                0f), Is.True,
                "Cockpit control ownership must be available after the comfort fade completes.");
        }

        [Test]
        public void VisibilityProfilesAndBackendDrivenAnimationUseAuthoredParts()
        {
            Transform world = LoadProductionWorld();
            ProductionVerticalSliceRoot marker = ComponentsInWorld<ProductionVerticalSliceRoot>(world).Single();
            AircraftVisibilityProfileController visibility = marker.VisibilityProfiles;
            Transform exterior = marker.AircraftRig.AircraftVisualRoot.Find("ImportedAircraftExterior");
            Transform interior = marker.AircraftRig.AircraftVisualRoot.Find("ImportedCockpitInterior");
            Transform leftAileron = marker.AircraftRig.AircraftVisualRoot.Find("AnimatedControlSurfaces/LeftAileronPivot");
            Transform yoke = marker.AircraftRig.AircraftVisualRoot.Find(
                "AnimatedControlSurfaces/PilotYokePitchPivot/PilotYokeRollPivot");
            Renderer staticPropeller = exterior.GetComponentsInChildren<Renderer>(true).Single(renderer =>
                renderer.name == "Cessna_Exterior_Body_MAT_0.001_Body_MAT_0");
            Renderer rightWing = exterior.GetComponentsInChildren<Renderer>(true).Single(renderer =>
                renderer.name == "Cessna_Exterior_Body_MAT_0.002_Body_MAT_0");
            Assert.That(exterior.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(15));
            Assert.That(interior.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(15));
            Assert.That(leftAileron, Is.Not.Null);
            Assert.That(yoke, Is.Not.Null);

            Bounds propellerBounds = MeshBoundsInFrame(staticPropeller, marker.AircraftRig.AircraftVisualRoot);
            Assert.That(Mathf.Abs(propellerBounds.center.x), Is.LessThan(0.1f),
                "The cockpit occluder must remain the centerline propeller, not broad exterior geometry.");
            Assert.That(propellerBounds.size.x, Is.LessThan(0.2f));
            Assert.That(Mathf.Max(propellerBounds.size.y, propellerBounds.size.z), Is.GreaterThan(1.8f));
            Assert.That(Mathf.Min(propellerBounds.size.y, propellerBounds.size.z), Is.LessThan(0.35f));

            visibility.SetExternalView();
            Assert.That(exterior.GetComponentsInChildren<Renderer>(true).All(renderer => renderer.enabled), Is.True);
            Assert.That(staticPropeller.enabled, Is.True,
                "External view must retain the imported static propeller geometry.");
            Assert.That(interior.GetComponentsInChildren<Renderer>(true).All(renderer => !renderer.enabled), Is.True);
            visibility.SetCockpitView();
            Renderer[] interiorRenderers = interior.GetComponentsInChildren<Renderer>(true);
            Renderer[] unexpectedDisabledInterior = interiorRenderers
                .Where(renderer =>
                    renderer.name !=
                        "Cessna_Interior_Paddle_MAT_0_Paddle_MAT_0" &&
                    !renderer.enabled)
                .ToArray();
            Assert.That(
                unexpectedDisabledInterior,
                Is.Empty,
                string.Join(", ", unexpectedDisabledInterior.Select(renderer =>
                    renderer.name)));
            Assert.That(
                interiorRenderers.Single(renderer => renderer.name ==
                    "Cessna_Interior_Paddle_MAT_0_Paddle_MAT_0").enabled,
                Is.False,
                "The incomplete frozen three-pedal source must remain replaced " +
                "by the authored four-pedal renderer.");
            Assert.That(leftAileron.GetComponentsInChildren<Renderer>(true).All(renderer => renderer.enabled), Is.True);
            Assert.That(staticPropeller.enabled, Is.True,
                "The stopped propeller must remain visible from the cockpit; the RPM-driven presentation controller replaces it with the authored blur disc while running.");
            Assert.That(rightWing.enabled, Is.True,
                "Cockpit view must not hide non-occluding exterior geometry such as the wing.");

            ProductionAircraftControlAnimator animator = marker.ControlAnimator;
            animator.CaptureAuthoredRestPosesForValidation();
            Quaternion aileronRest = leftAileron.localRotation;
            Quaternion yokeRest = yoke.localRotation;
            animator.SetControlStateForTest(new AircraftControlState
            {
                aileron = 1f,
                elevator = 0.5f,
                flaps = 1f,
                throttle = 1f,
                mixture = 1f
            });
            Assert.That(Quaternion.Angle(aileronRest, leftAileron.localRotation), Is.GreaterThan(15f));
            Assert.That(Quaternion.Angle(yokeRest, yoke.localRotation), Is.GreaterThan(40f));
            Assert.That(animator.LastAnimatedControls.aileron, Is.EqualTo(1f).Within(0.0001f));
        }

        private Transform LoadProductionWorld()
        {
            GameObject prefab = Resources.Load<GameObject>(WorldResourcePath);
            Assert.That(prefab, Is.Not.Null, "Authored production world resource is missing.");
            _worldInstance = Object.Instantiate(prefab);
            _worldInstance.name = "ProductionWorldRoot";
            return _worldInstance.transform;
        }

        private static T[] ComponentsInWorld<T>(Transform world) where T : Component
        {
            return world.GetComponentsInChildren<T>(true);
        }

        private static bool HasComponentNamed(Transform world, string typeName) =>
            world.GetComponentsInChildren<MonoBehaviour>(true).Any(component =>
                component != null && component.GetType().Name == typeName);

        private static void InvokeUnityMessage(MonoBehaviour target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null,
                target.GetType().Name + " is missing the expected " + methodName + " lifecycle method.");
            method.Invoke(target, null);
        }

        private static void SetAutoProperty<T>(
            object target,
            string propertyName,
            T value)
        {
            FieldInfo field = target.GetType().GetField(
                "<" + propertyName + ">k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null,
                target.GetType().Name + "." + propertyName + " has no backing field.");
            field.SetValue(target, value);
        }

        private static Bounds MeshBoundsInFrame(Renderer renderer, Transform frame)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Assert.That(filter, Is.Not.Null, renderer.name + " has no MeshFilter.");
            Assert.That(filter.sharedMesh, Is.Not.Null, renderer.name + " has no shared mesh.");

            Bounds source = filter.sharedMesh.bounds;
            Bounds result = default;
            bool initialized = false;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 meshPoint = new Vector3(
                    x == 0 ? source.min.x : source.max.x,
                    y == 0 ? source.min.y : source.max.y,
                    z == 0 ? source.min.z : source.max.z);
                Vector3 framePoint = frame.InverseTransformPoint(renderer.transform.TransformPoint(meshPoint));
                if (!initialized)
                {
                    result = new Bounds(framePoint, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    result.Encapsulate(framePoint);
                }
            }
            return result;
        }
    }
}
#endif
