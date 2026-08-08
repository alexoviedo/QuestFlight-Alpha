using System.IO;
using System.Reflection;
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlightLab.Flight;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests.Product
{
    public sealed class FlightLessonProductIntegrationEditModeTests
    {
        [Test]
        public void DemoRuntimeUsesStateBasedRouteWithoutTransformAuthority()
        {
            string source = File.ReadAllText(
                "Assets/Scripts/Product/ProductDemoPilot.cs");
            Assert.That(source, Does.Contain("FlightLessonNavigator.TryEvaluate"));
            Assert.That(source, Does.Contain("FlightLessonGuidanceController"));
            Assert.That(source, Does.Not.Contain("Mathf.Repeat(Time.unscaledTime"));
            Assert.That(source, Does.Not.Contain("SetPositionAndRotation"));
            Assert.That(source, Does.Not.Contain("Main Camera"));
            Assert.That(source, Does.Not.Contain("XR Origin"));
        }

        [Test]
        public void DemoRequiresAuthoredRouteAndCanRestoreAnyValidatedSegment()
        {
            GameObject target = new GameObject("FlightLessonDemoRestoreTest");
            FlightLessonRouteProfile profile =
                ScriptableObject.CreateInstance<FlightLessonRouteProfile>();
            try
            {
                ProductDemoPilot demo = target.AddComponent<ProductDemoPilot>();
                Assert.That(demo.RestoreLessonSegment(4), Is.False,
                    "Product runtime must not synthesize an unauthored route.");
                profile.SetDefinitionForAuthoring(
                    KbduExtendedReservoirLesson.CreateDefinition());
                demo.ConfigureRouteProfileForAuthoring(profile);
                Assert.That(demo.RestoreLessonSegment(4), Is.True);
                Assert.That(demo.RouteDefinition, Is.Not.Null);
                Assert.That(demo.RouteDefinition.TryValidate(out string failure),
                    Is.True, failure);
                Assert.That(demo.ProgressState.currentSegmentIndex, Is.EqualTo(4));
                Assert.That(demo.ProgressState.lessonComplete, Is.False);
                Assert.That(demo.RestoreLessonSegment(-1), Is.False);
                Assert.That(
                    demo.RestoreLessonSegment(demo.RouteDefinition.segments.Length),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void TouchFlapSelectionRemainsPlayerOwnedDuringGuidance()
        {
            GameObject target = new GameObject("FlightLessonFlapOwnershipTest");
            target.SetActive(false);
            try
            {
                GamepadInputReader gamepad = target.AddComponent<GamepadInputReader>();
                DeterministicGamepadInputSource deterministic =
                    target.AddComponent<DeterministicGamepadInputSource>();
                ProductControlInput input = target.AddComponent<ProductControlInput>();
                input.ConfigureAuthoredSources(gamepad, deterministic);
                target.SetActive(true);

                Assert.That(input.TryApplyTouchDiscreteValue(
                    ProductTouchControlChannel.FlapSelector,
                    71,
                    0.5f), Is.True);
                FieldInfo owned = typeof(ProductControlInput).GetField(
                    "_flapsManuallyOwned",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(owned, Is.Not.Null);
                Assert.That((bool)owned.GetValue(input), Is.True);
                Assert.That(input.Current.flaps, Is.EqualTo(0.5f));
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BackendStateConversionUsesConventionalAviationRateSigns()
        {
            FlightDynamicsState state = new FlightDynamicsState
            {
                altitudeMslMeters = 1600.0,
                calibratedAirspeedKnots = 82.0,
                headingDegrees = 91.0,
                pitchDegrees = 4.0,
                bankDegrees = 12.0,
                sideslipDegrees = -2.0,
                verticalSpeedFeetPerMinute = 480.0,
                positionUnityMeters = new Vector3(10f, 20f, 30f),
                angularVelocityBodyDegreesPerSecond = new Vector3(-3f, 2f, -5f)
            };

            FlightLessonAircraftNavigationState navigation =
                ProductDemoPilot.BuildNavigationState(state);
            FlightLessonGuidanceAircraftState guidance =
                ProductDemoPilot.BuildGuidanceAircraftState(state);

            Assert.That(navigation.positionUnityMeters, Is.EqualTo(state.positionUnityMeters));
            Assert.That(navigation.altitudeMslFeet,
                Is.EqualTo(1600f * (float)FlightFrameConversions.MetersToFeet)
                    .Within(0.01f));
            Assert.That(guidance.rollRateDegreesPerSecond, Is.EqualTo(5f));
            Assert.That(guidance.pitchRateDegreesPerSecond, Is.EqualTo(3f));
            Assert.That(guidance.yawRateDegreesPerSecond, Is.EqualTo(2f));
        }

        [Test]
        public void PrototypeAileronResponseClosesGuidanceLoopWithAviationSigns()
        {
            C172StyleAircraftConfig config =
                Resources.Load<C172StyleAircraftConfig>("C172StyleAircraftConfig");
            Assert.That(config, Is.Not.Null);

            GameObject target = new GameObject("PrototypeGuidanceSignContract");
            target.SetActive(false);
            UnityPrototypeFlightBackend backend =
                new UnityPrototypeFlightBackend();
            try
            {
                AircraftState state = target.AddComponent<AircraftState>();
                SimpleAircraftPhysics physics =
                    target.AddComponent<SimpleAircraftPhysics>();
                state.config = config;
                physics.state = state;
                physics.config = config;

                Assert.That(backend.Initialize(
                    new FlightDynamicsBackendContext
                    {
                        simulationRoot = target.transform,
                        aircraftState = state,
                        unityPrototype = physics,
                        localOrigin = GeodeticReference.Kbdu,
                        fixedDeltaTimeSeconds = 1.0 / 120.0
                    }), Is.True, backend.LastError);

                GeodeticReference origin = GeodeticReference.Kbdu;
                Assert.That(backend.Reset(
                    new FlightDynamicsInitialConditions
                    {
                        latitudeDegrees = origin.latitudeDegrees,
                        longitudeDegrees = origin.longitudeDegrees,
                        altitudeMslMeters = origin.altitudeMslMeters + 100.0,
                        terrainElevationMslMeters = origin.altitudeMslMeters,
                        calibratedAirspeedKnots = 80.0,
                        headingDegrees = 0.0,
                        pitchDegrees = 0.0,
                        bankDegrees = 0.0,
                        flightPathAngleDegrees = 0.0,
                        engineRunning = true
                    }), Is.True, backend.LastError);

                AircraftControlState controls =
                    AircraftControlState.Neutral(0.55f);
                controls.aileron = 0.35f;
                backend.SetControls(controls);
                for (int step = 0; step < 240; step++)
                    Assert.That(backend.Advance(1.0 / 120.0), Is.True,
                        backend.LastError);

                FlightDynamicsState response = backend.CurrentState;
                Assert.That(response.bankDegrees, Is.GreaterThan(1.0),
                    "Positive aileron must report positive right-wing-down bank.");

                FlightLessonGuidanceAircraftState guidanceState =
                    ProductDemoPilot.BuildGuidanceAircraftState(response);
                FlightLessonGuidanceIntent wingsLevelIntent =
                    new FlightLessonGuidanceIntent(
                        "sign_contract",
                        FlightLessonSegmentKind.Cruise,
                        response.positionUnityMeters,
                        (float)response.headingDegrees,
                        0f,
                        (float)(response.altitudeMslMeters *
                                FlightFrameConversions.MetersToFeet),
                        (float)response.calibratedAirspeedKnots,
                        25f,
                        800f,
                        0.5f);
                FlightLessonGuidanceController controller =
                    new FlightLessonGuidanceController();
                Assert.That(controller.TryStep(
                    wingsLevelIntent,
                    guidanceState,
                    1f / 120f,
                    FlightLessonGuidanceControllerSettings.Default,
                    out FlightLessonControlCommand correction), Is.True);
                Assert.That(correction.aileron, Is.LessThan(0f),
                    "Wings-level guidance must oppose a measured right bank.");

                target.transform.rotation = Quaternion.identity;
                Assert.That(
                    UnityPrototypeFlightBackend.ComputeAviationSideslipDegrees(
                        target.transform,
                        new Vector3(10f, 0f, 10f)),
                    Is.EqualTo(45.0).Within(0.01),
                    "Prototype beta must be an angle in degrees, not a normalized slip-ball ratio.");
            }
            finally
            {
                backend.Dispose();
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void DepartureGroundRollCommandsRotationBeforeRunwayOverrun()
        {
            FlightLessonGuidanceIntent intent = new FlightLessonGuidanceIntent(
                "departure",
                FlightLessonSegmentKind.Departure,
                Vector3.forward,
                90f,
                0f,
                6000f,
                70f,
                8f,
                800f,
                0.25f);
            FlightDynamicsState state = new FlightDynamicsState
            {
                headingDegrees = 90.0,
                pitchDegrees = 0.0,
                bankDegrees = 0.0,
                angularVelocityBodyDegreesPerSecond = Vector3.zero
            };

            FlightLessonControlCommand groundRoll =
                ProductDemoPilot.BuildTakeoffCommand(intent, state, false);
            Assert.That(groundRoll.throttle, Is.EqualTo(1f));
            Assert.That(groundRoll.elevator, Is.EqualTo(0.02f));
            Assert.That(groundRoll.leftToeBrake, Is.Zero);
            Assert.That(groundRoll.rightToeBrake, Is.Zero);

            FlightLessonControlCommand rotation =
                ProductDemoPilot.BuildTakeoffCommand(intent, state, true);
            Assert.That(rotation.elevator, Is.GreaterThan(0.25f),
                "Rotation must command a positive pitch response without injecting vertical velocity.");
            Assert.That(rotation.trim, Is.EqualTo(0.08f));

            state.pitchDegrees = ProductDemoPilot.TakeoffRotationPitchDegrees;
            FlightLessonControlCommand pitchCaptured =
                ProductDemoPilot.BuildTakeoffCommand(intent, state, true);
            Assert.That(pitchCaptured.elevator, Is.EqualTo(0f).Within(0.001f),
                "The rotation command must relax after capturing the bounded pitch target.");
        }

        [Test]
        public void RateLimitedProductionDepartureLiftsOffBeforeTheRunwayEnd()
        {
            C172StyleAircraftConfig config =
                Resources.Load<C172StyleAircraftConfig>("C172StyleAircraftConfig");
            Assert.That(config, Is.Not.Null);

            GameObject target = new GameObject("ProductionDepartureContract");
            target.SetActive(false);
            UnityPrototypeFlightBackend backend = new UnityPrototypeFlightBackend();
            try
            {
                AircraftState aircraftState = target.AddComponent<AircraftState>();
                SimpleAircraftPhysics physics =
                    target.AddComponent<SimpleAircraftPhysics>();
                aircraftState.config = config;
                physics.state = aircraftState;
                physics.config = config;

                const double fixedStep = 1.0 / 120.0;
                Assert.That(backend.Initialize(
                    new FlightDynamicsBackendContext
                    {
                        simulationRoot = target.transform,
                        aircraftState = aircraftState,
                        unityPrototype = physics,
                        localOrigin = GeodeticReference.Kbdu,
                        fixedDeltaTimeSeconds = fixedStep
                    }), Is.True, backend.LastError);
                Assert.That(backend.Reset(FlightDynamicsInitialConditions.KbduRunway()),
                    Is.True, backend.LastError);

                FlightLessonGuidanceIntent intent = new FlightLessonGuidanceIntent(
                    "departure",
                    FlightLessonSegmentKind.Departure,
                    new Vector3(1600f, 0f, 0f),
                    90f,
                    0f,
                    6000f,
                    74f,
                    8f,
                    800f,
                    0f);
                AircraftControlState scheduled = AircraftControlState.Neutral(0.10f);
                AircraftControlState applied = AircraftControlState.Neutral(0.10f);
                float airborneSeconds = 0f;
                float liftoffGroundRollMeters = float.NaN;
                float liftoffAltitudeMeters = 0f;
                FlightDynamicsState finalState = backend.CurrentState;
                AircraftControlState finalControls = applied;

                for (int step = 0; step < 3600; step++)
                {
                    FlightDynamicsState state = backend.CurrentState;
                    bool rotating = state.calibratedAirspeedKnots >=
                        config.Speeds.rotationKts -
                        ProductDemoPilot.TakeoffRotationLeadKnots;
                    FlightLessonControlCommand command =
                        ProductDemoPilot.BuildTakeoffCommand(
                            intent,
                            state,
                            rotating);
                    ProductControlInput.CopyFlightLessonCommandInto(
                        command,
                        scheduled);
                    ProductDemoPilot.RateLimitControlsInto(
                        scheduled,
                        applied,
                        (float)fixedStep);
                    backend.SetControls(applied);
                    Assert.That(backend.Advance(fixedStep), Is.True,
                        backend.LastError);

                    state = backend.CurrentState;
                    finalState = state;
                    finalControls = applied;
                    if (!state.weightOnWheels)
                    {
                        if (float.IsNaN(liftoffGroundRollMeters))
                        {
                            liftoffGroundRollMeters =
                                Mathf.Abs(state.positionUnityMeters.x);
                            liftoffAltitudeMeters = (float)state.altitudeAglMeters;
                        }
                        airborneSeconds += (float)fixedStep;
                        if (airborneSeconds >= 0.75f &&
                            state.altitudeAglMeters >= liftoffAltitudeMeters + 2f)
                            break;
                    }
                    else
                    {
                        airborneSeconds = 0f;
                    }
                }

                Assert.That(float.IsNaN(liftoffGroundRollMeters), Is.False,
                    $"The production departure command never produced aerodynamic liftoff. " +
                    $"GS={finalState.groundSpeedKnots:0.0}kt " +
                    $"CAS={finalState.calibratedAirspeedKnots:0.0}kt " +
                    $"Pitch={finalState.pitchDegrees:0.0}deg " +
                    $"AGL={finalState.altitudeAglMeters:0.00}m " +
                    $"Ele={finalControls.elevator:0.00} Thr={finalControls.throttle:0.00}.");
                Assert.That(liftoffGroundRollMeters, Is.LessThan(1250f),
                    "The aircraft must lift off before crossing the measured runway endpoint.");
                Assert.That(airborneSeconds, Is.GreaterThanOrEqualTo(0.75f),
                    "Liftoff must be sustained rather than a ground-contact oscillation.");
                Assert.That(backend.CurrentState.altitudeAglMeters,
                    Is.GreaterThanOrEqualTo(liftoffAltitudeMeters + 2f),
                    "The departure must gain altitude naturally after releasing wheel contact.");
            }
            finally
            {
                backend.Dispose();
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void GuidanceCommandConversionIsBoundedAndAllocationFreeByShape()
        {
            AircraftControlState controls = AircraftControlState.Neutral(0.1f);
            FlightLessonControlCommand command = new FlightLessonControlCommand
            {
                aileron = 2f,
                elevator = -2f,
                rudder = 0.25f,
                throttle = 2f,
                mixture = -1f,
                trim = 0.5f,
                flaps = 2f,
                leftToeBrake = 0.2f,
                rightToeBrake = 0.3f
            };

            ProductControlInput.CopyFlightLessonCommandInto(command, controls);
            Assert.That(controls.aileron, Is.EqualTo(1f));
            Assert.That(controls.elevator, Is.EqualTo(-1f));
            Assert.That(controls.throttle, Is.EqualTo(1f));
            Assert.That(controls.mixture, Is.EqualTo(0f));
            Assert.That(controls.flaps, Is.EqualTo(1f));

            FlightLessonControlCommand roundTrip =
                ProductControlInput.ToFlightLessonCommand(controls);
            Assert.That(roundTrip.rudder, Is.EqualTo(0.25f));
            Assert.That(roundTrip.leftToeBrake, Is.EqualTo(0.2f));
            Assert.That(roundTrip.rightToeBrake, Is.EqualTo(0.3f));
        }

        [Test]
        public void RecoverySuppressionReleasesLeasesAndRejectsNewOwnership()
        {
            GameObject target = new GameObject("FlightLessonInputSuppressionTest");
            target.SetActive(false);
            try
            {
                GamepadInputReader gamepad = target.AddComponent<GamepadInputReader>();
                DeterministicGamepadInputSource deterministic =
                    target.AddComponent<DeterministicGamepadInputSource>();
                ProductControlInput input = target.AddComponent<ProductControlInput>();
                input.ConfigureAuthoredSources(gamepad, deterministic);
                target.SetActive(true);
                input.SetFlightLessonGuidance(
                    true,
                    new FlightLessonControlCommand
                    {
                        throttle = 0.8f,
                        mixture = 1f
                    });

                Assert.That(input.TryBeginTouchOverride(
                    ProductTouchControlChannel.PilotYoke,
                    42,
                    0.4f,
                    -0.2f), Is.True);
                Assert.That(input.ActiveTouchOverrideCount, Is.EqualTo(1));

                input.SetFlightLessonInputSuppressed(true);
                Assert.That(input.ActiveTouchOverrideCount, Is.Zero);
                Assert.That(input.FlightLessonInputSuppressed, Is.True);
                Assert.That(input.TryBeginTouchOverride(
                    ProductTouchControlChannel.PilotYoke,
                    43,
                    0f,
                    0f), Is.False);
                Assert.That(input.Current.aileron, Is.Zero);
                Assert.That(input.Current.elevator, Is.Zero);

                input.SetFlightLessonInputSuppressed(false);
                Assert.That(input.TryBeginTouchOverride(
                    ProductTouchControlChannel.PilotYoke,
                    43,
                    0f,
                    0f), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
