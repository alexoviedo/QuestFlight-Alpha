#if UNITY_INCLUDE_TESTS
using System.Linq;
using System.Reflection;
using AlexOviedo.QuestFlight.JSBSim;
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
            Assert.That(coordinator.requestedBackend,
                Is.EqualTo(FlightDynamicsBackendKind.JSBSimNativeDevelopment));
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
            Assert.That(ComponentsInWorld<MonoBehaviour>(world).Any(component =>
                component != null && component.GetType().Name == "SimpleAircraftPhysics"), Is.False);

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
        [Description("Synchronous authored menu/native integration; not Android frame or worn-headset acceptance.")]
        public void ThreeAuthoredMenuNewFlightCyclesMatchFreshNativeLoadingAndFirstSteps()
        {
            float previousTimeScale = Time.timeScale;
            Transform world = LoadProductionWorld();
            try
            {
                var coordinator = ComponentsInWorld<FlightDynamicsCoordinator>(world).Single();
                var modes = ComponentsInWorld<ProductFlightModeCoordinator>(world).Single();
                var input = ComponentsInWorld<ProductControlInput>(world).Single();
                Camera trackedEye = ComponentsInWorld<Camera>(world).Single(camera =>
                    camera.CompareTag("MainCamera") && camera.enabled &&
                    camera.gameObject.activeInHierarchy);
                int simulationRootId = coordinator.simulationRoot.GetInstanceID();
                Assert.That(coordinator.InitializeSelectedBackend(), Is.True, coordinator.LastError);
                JSBSimNativeSession native = MenuCycleNativeSession(coordinator);
                Assert.That(native.IsInitialized, Is.True);
                modes.SelectFreeFlight();
                Assert.That(modes.State, Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.FreeFlight), modes.LastError);

                // This baseline is the first, freshly constructed native session in
                // the actual authored world, before any dirty actuator/fuel history.
                var states = new JSBSimNativeState[121];
                var diagnostics = new JSBSimNativeDiagnostics[121];
                AircraftControlState idle = AircraftControlState.Neutral(.1f);
                states[0] = native.State;
                diagnostics[0] = native.Diagnostics;
                for (int step = 1; step <= 120; step++)
                {
                    Assert.That(coordinator.StepForTest(idle, 1d / 120), Is.True, coordinator.LastError);
                    states[step] = native.State;
                    diagnostics[step] = native.Diagnostics;
                }

                for (int cycle = 0; cycle < 3; cycle++)
                {
                    AircraftControlState dirty = AircraftControlState.Neutral(.8f);
                    dirty.flaps = 1;
                    dirty.trim = .2f;
                    for (int step = 0; step < 1200; step++)
                        Assert.That(coordinator.StepForTest(dirty, 1d / 120), Is.True, coordinator.LastError);
                    Assert.That(native.Diagnostics.flapPosition, Is.GreaterThan(.5));
                    Assert.That(native.Diagnostics.leftFuelPounds + native.Diagnostics.rightFuelPounds,
                        Is.LessThan(diagnostics[0].leftFuelPounds + diagnostics[0].rightFuelPounds));

                    // Call the real authored menu and free-flight entry points;
                    // do not replace them with a direct package Reset invocation.
                    modes.ReturnToMainMenu();
                    Assert.That(modes.State, Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Selection), modes.LastError);
                    Assert.That(input.FlightLessonInputSuppressed, Is.True);
                    Assert.That(Time.timeScale, Is.Zero);
                    Assert.That(coordinator.IsInitialized, Is.True, coordinator.LastError);
                    Assert.That(MenuCycleNativeSession(coordinator), Is.SameAs(native));
                    Assert.That(coordinator.simulationRoot.GetInstanceID(), Is.EqualTo(simulationRootId));
                    Assert.That(ComponentsInWorld<Camera>(world).Single(camera =>
                        camera.CompareTag("MainCamera") && camera.enabled &&
                        camera.gameObject.activeInHierarchy), Is.SameAs(trackedEye));
                    modes.SelectFreeFlight();
                    Assert.That(modes.State, Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.FreeFlight), modes.LastError);
                    Assert.That(input.FlightLessonInputSuppressed, Is.False);
                    Assert.That(input.FlightLessonGuidanceActive, Is.False);
                    Assert.That(Time.timeScale, Is.GreaterThan(0));
                    AssertMenuCycleNativeValues(native.State, states[0], cycle, 0);
                    AssertMenuCycleNativeValues(native.Diagnostics, diagnostics[0], cycle, 0);
                    for (int step = 1; step <= 120; step++)
                    {
                        Assert.That(coordinator.StepForTest(idle, 1d / 120), Is.True, coordinator.LastError);
                        AssertMenuCycleNativeValues(native.State, states[step], cycle, step);
                        AssertMenuCycleNativeValues(native.Diagnostics, diagnostics[step], cycle, step);
                    }
                }
            }
            finally { Time.timeScale = previousTimeScale; }
        }

        private static JSBSimNativeSession MenuCycleNativeSession(FlightDynamicsCoordinator coordinator)
        {
            object backend = typeof(FlightDynamicsCoordinator).GetField("_backend",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(coordinator);
            Assert.That(backend, Is.TypeOf<JSBSimNativeDevelopmentBackend>());
            return (JSBSimNativeSession)typeof(JSBSimNativeDevelopmentBackend).GetField("_session",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(backend);
        }

        private static void AssertMenuCycleNativeValues<T>(T actual, T fresh, int cycle, int step)
        {
            foreach (FieldInfo field in typeof(T).GetFields())
            {
                string context = $"cycle {cycle}, step {step}, {field.Name}";
                if (field.FieldType == typeof(double))
                    Assert.That((double)field.GetValue(actual), Is.EqualTo((double)field.GetValue(fresh)).Within(1e-7), context);
                else Assert.That(field.GetValue(actual), Is.EqualTo(field.GetValue(fresh)), context);
            }
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
            ProductFlightModeCoordinator modes = ComponentsInWorld<ProductFlightModeCoordinator>(world).Single();
            modes.SelectDemo();
            Assert.That(modes.State, Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Demo));
            Assert.That(Time.timeScale, Is.GreaterThan(0f));
            InvokeUnityMessage(recovery, "Start");
            Assert.That(coordinator.IsInitialized, Is.True, coordinator.LastError);
            Assert.That(demo.IsRunning, Is.True);
            Assert.That(recovery.RecoveryPhase, Is.EqualTo(FlightLessonRecoveryPhase.Idle));
            Assert.That(input.TryBeginTouchOverride(
                ProductTouchControlChannel.PilotYoke,
                41001,
                0.35f,
                -0.2f), Is.False, "The automatic demo owns controls; Touch must not compete with it.");
            Assert.That(input.DemoOwnsControls, Is.True);
            Assert.That(input.ActiveTouchOverrideCount, Is.Zero);

            ProductionTerrainSurfaceAsset terrain = recovery.AuthoredTerrainSurface;
            const float offRunwayEastMeters = 9500f;
            const float offRunwayNorthMeters = 0f;
            Assert.That(terrain.TrySample(
                offRunwayEastMeters,
                offRunwayNorthMeters,
                out ProductionTerrainSample outsideTerrain), Is.True);

            GeodeticReference origin = GeodeticReference.Kbdu;
            Vector3 outsidePosition = new Vector3(
                offRunwayEastMeters,
                outsideTerrain.heightMeters + 1.25f,
                offRunwayNorthMeters);
            GeodeticReference outsideGeodetic =
                FlightFrameConversions.UnityToGeodetic(outsidePosition, origin);
            GeodeticReference outsideTerrainGeodetic =
                FlightFrameConversions.UnityToGeodetic(
                    new Vector3(
                        offRunwayEastMeters,
                        outsideTerrain.heightMeters,
                        offRunwayNorthMeters),
                    origin);
            FlightDynamicsInitialConditions offRunwayContact =
                new FlightDynamicsInitialConditions
                {
                    latitudeDegrees = outsideGeodetic.latitudeDegrees,
                    longitudeDegrees = outsideGeodetic.longitudeDegrees,
                    altitudeMslMeters = outsideGeodetic.altitudeMslMeters,
                    terrainElevationMslMeters = outsideTerrainGeodetic.altitudeMslMeters,
                    calibratedAirspeedKnots = 35d,
                    headingDegrees = 90d,
                    pitchDegrees = 0d,
                    bankDegrees = 0d,
                    flightPathAngleDegrees = 0d,
                    engineRunning = true
                };
            Assert.That(FlightLessonRecoveryRuntimeAdapter.TryMapTerrainSample(terrain, outsidePosition,
                out FlightLessonTerrainSample contactSurface), Is.True);
            Assert.That(contactSurface.isRunwayOrApron, Is.False);
            Assert.That(contactSurface.isWater, Is.False);
            // A healthy aircraft9500m from the origin is inside the map. The old
            // lesson circle is not the current recovery owner and must not fire.
            FlightDynamicsInitialConditions healthyFarFlight = offRunwayContact;
            healthyFarFlight.altitudeMslMeters += 300d;
            healthyFarFlight.calibratedAirspeedKnots = 80d;
            Assert.That(coordinator.TryResetToInitialConditions(healthyFarFlight), Is.True, coordinator.LastError);
            InvokeUnityMessage(recovery, "FixedUpdate");
            Assert.That(recovery.RecoveryPhase, Is.EqualTo(FlightLessonRecoveryPhase.Idle));
            Assert.That(recovery.LastCrashReason, Is.EqualTo(FlightLessonCrashReason.None));
            // Drive the existing crash classifier with real native WOW/ground speed
            // on non-runway ground, rather than reviving the removed circle trigger.
            Assert.That(coordinator.TryResetToInitialConditions(offRunwayContact),
                Is.True, coordinator.LastError);
            Assert.That(coordinator.CurrentState.weightOnWheels, Is.True);
            Assert.That(coordinator.CurrentState.groundSpeedKnots,
                Is.GreaterThan(FlightLessonCrashSettings.ConservativeTrainingDefaults.unsafeSurfaceGroundSpeedKnots));

            bool sawFadingOut = false;
            bool sawAwaitingReset = false;
            bool sawStabilizing = false;
            bool sawFadingIn = false;
            bool sawSuppression = false;
            bool verifiedCheckpointReset = false;
            bool recoveryStarted = false;
            FlightLessonRouteDefinition route = demo.RouteDefinition;
            Assert.That(route.recoveryCheckpoints, Has.Length.EqualTo(1),
                "The authored scenic demo has one departure restart, not an airborne checkpoint per segment.");
            FlightLessonRecoveryCheckpoint expectedCheckpoint =
                route.recoveryCheckpoints[0];
            Assert.That(expectedCheckpoint.calibratedAirspeedKnots, Is.Zero);
            Assert.That(expectedCheckpoint.headingDegreesTrue, Is.EqualTo(90f));
            Assert.That(FlightLessonRecoveryRuntimeAdapter.TryBuildInitialConditions(
                expectedCheckpoint, terrain, out FlightDynamicsInitialConditions expectedReset), Is.True);
            Vector3 expectedResetPosition = FlightFrameConversions.GeodeticToUnity(
                expectedReset.latitudeDegrees, expectedReset.longitudeDegrees,
                expectedReset.altitudeMslMeters, origin);
            // The authored departure point is at the pavement end. Ground recovery
            // moves exactly 10m east so the whole existing aircraft footprint is safe.
            Assert.That(expectedResetPosition.x,
                Is.EqualTo(expectedCheckpoint.positionUnityMeters.x + 10f).Within(0.1f));
            Assert.That(expectedResetPosition.z,
                Is.EqualTo(expectedCheckpoint.positionUnityMeters.z).Within(0.1f));
            for (int footprintX = -1; footprintX <= 1; footprintX++)
            for (int footprintZ = -1; footprintZ <= 1; footprintZ++)
            {
                Assert.That(terrain.TrySample(expectedResetPosition.x + footprintZ * 5f,
                    expectedResetPosition.z + footprintX * 6.5f,
                    out ProductionTerrainSample footprint), Is.True);
                Assert.That(footprint.IsRunwayOrApron, Is.True);
                Assert.That(Vector3.Angle(footprint.normal, Vector3.up), Is.LessThanOrEqualTo(3f));
            }

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
                Assert.That(input.ActiveTouchOverrideCount, Is.Zero);
                Assert.That(input.TryBeginTouchOverride(ProductTouchControlChannel.PilotYoke,
                    41003, 0f, 0f), Is.False, "Automatic guidance and recovery keep control ownership exclusive.");

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
                        Is.EqualTo(expectedResetPosition.x).Within(0.1f));
                    Assert.That(resetPosition.z,
                        Is.EqualTo(expectedResetPosition.z).Within(0.1f));
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
            Assert.That(recovery.LastCrashReason, Is.EqualTo(FlightLessonCrashReason.UnsafeSurfaceContact));
            Assert.That(verifiedCheckpointReset, Is.True);
            Assert.That(recovery.RecoveryPhase, Is.EqualTo(FlightLessonRecoveryPhase.Idle));
            Assert.That(input.FlightLessonInputSuppressed, Is.False);
            Assert.That(demo.GuidanceSuppressed, Is.False);
            Assert.That(demo.IsRunning, Is.True);

            // A later demo crash must clear the input pause/low-throttle latches
            // and prove that the existing healthy native authority advances before
            // releasing the fade. A disabled authority has a different fail-closed owner.
            Assert.That(demo.RestoreLessonSegment(1), Is.True);
            Assert.That(demo.ProgressState.currentSegmentIndex, Is.EqualTo(1));
            int secondCheckpointIndex = FlightLessonRecoverySelector.SelectCheckpointIndex(
                route, demo.ProgressState);
            Assert.That(secondCheckpointIndex, Is.Zero);
            FlightLessonRecoveryCheckpoint secondCheckpoint = route.recoveryCheckpoints[secondCheckpointIndex];
            Assert.That(secondCheckpoint.id, Is.EqualTo("departure"));
            Assert.That(secondCheckpoint.resumeSegmentIndex, Is.Zero);
            Assert.That(secondCheckpoint.calibratedAirspeedKnots, Is.Zero);
            Assert.That(coordinator.TryResetToInitialConditions(offRunwayContact),
                Is.True, coordinator.LastError);
            SetAutoProperty(input, nameof(ProductControlInput.Paused), true);
            input.Current.throttle = 0f;
            Assert.That(coordinator.enabled, Is.True);

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
                Is.EqualTo(secondCheckpoint.calibratedAirspeedKnots).Within(0.001d));
            Assert.That(demo.ProgressState.currentSegmentIndex,
                Is.EqualTo(secondCheckpoint.resumeSegmentIndex));
            Assert.That(input.Current.throttle, Is.GreaterThan(0f),
                "Low throttle at impact must not leave the recovered aircraft frozen.");
            Assert.That(input.TryBeginTouchOverride(
                ProductTouchControlChannel.PilotYoke,
                41002,
                0f,
                0f), Is.False, "Completed recovery preserves automatic demo ownership.");
            FlightDynamicsInitialConditions lastReset = coordinator.LastResetInitialConditions;
            coordinator.enabled = false;
            InvokeUnityMessage(recovery, "FixedUpdate");
            Assert.That(coordinator.enabled, Is.False,
                "The lesson adapter must not resurrect a disabled flight authority.");
            Assert.That(coordinator.LastResetInitialConditions, Is.EqualTo(lastReset));
            Assert.That(recovery.RecoveryPhase, Is.EqualTo(FlightLessonRecoveryPhase.Idle));
            coordinator.enabled = true;
            demo.TakeUserControl();
            Assert.That(demo.UserHasControls, Is.True);
            Assert.That(input.DemoOwnsControls, Is.False);
            Assert.That(input.TryBeginTouchOverride(ProductTouchControlChannel.PilotYoke,
                41004, 0f, 0f), Is.False, "An explicit handoff still requires a neutral Touch release.");
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
                    renderer.name != "Cessna_Interior_Paddle_MAT_0_Paddle_MAT_0" &&
                    renderer.name != "Static_Display_New_Mat_01_0_New_Mat_01_0" &&
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
            Assert.That(interiorRenderers.Single(renderer => renderer.name ==
                "Static_Display_New_Mat_01_0_New_Mat_01_0").enabled, Is.False,
                "The overlapping imported display must remain replaced by the authored readout mesh.");
            Assert.That(marker.AircraftRig.AircraftVisualRoot.GetComponentsInChildren<Renderer>(true)
                .Single(renderer => renderer.name == "ProductionInstrumentDisplayReadouts").enabled, Is.True);
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
