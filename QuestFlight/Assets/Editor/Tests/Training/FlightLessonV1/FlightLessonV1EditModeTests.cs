#if UNITY_EDITOR
using NUnit.Framework;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEngine;

namespace QuestFlightLab.Tests.EditMode.Training
{
    public sealed class FlightLessonV1EditModeTests
    {
        [Test]
        public void KbduDefinitionIsValidAndExplicitlyNotAStandardPattern()
        {
            FlightLessonRouteDefinition route = KbduExtendedReservoirLesson.CreateDefinition();

            Assert.That(route.TryValidate(out string failure), Is.True, failure);
            StringAssert.Contains("Extended Reservoir", route.displayName);
            StringAssert.Contains("not a standard traffic pattern", route.operationalDisclaimer.ToLowerInvariant());
            StringAssert.Contains("not for navigation", route.operationalDisclaimer.ToLowerInvariant());
            Assert.That(route.provenanceUrls, Does.Contain(KbduExtendedReservoirLesson.OfficialNoiseAbatementUrl));
            Assert.That(route.officialOperatingFacts, Does.Contain(KbduExtendedReservoirLesson.PublishedTrafficDirectionFact));
            Assert.That(route.officialOperatingFacts, Does.Contain(KbduExtendedReservoirLesson.PublishedDownwindLocationFact));
            Assert.That(KbduExtendedReservoirLesson.PublishedTrafficPatternAltitudeMslFeet, Is.EqualTo(6300f));
            Assert.That(route.waypoints[1].altitudeMslFeet,
                Is.GreaterThanOrEqualTo(KbduExtendedReservoirLesson.PublishedMinimumTurnAltitudeMslFeet));
        }

        [Test]
        public void InitialRecoveryIsAnExplicitRunwayRestartNotAnAirborneFallback()
        {
            FlightLessonRouteDefinition route =
                KbduExtendedReservoirLesson.CreateDefinition();
            FlightLessonProgressState progress =
                FlightLessonProgressState.InitialFor(route);

            Assert.That(progress.latestRecoveryCheckpointIndex,
                Is.EqualTo(route.initialRecoveryCheckpointIndex));
            Assert.That(route.recoveryCheckpoints[progress.latestRecoveryCheckpointIndex].id,
                Is.EqualTo("runway_08_restart"));
            Assert.That(route.recoveryCheckpoints[progress.latestRecoveryCheckpointIndex].resumeSegmentIndex,
                Is.Zero);
            Assert.That(route.recoveryCheckpoints[progress.latestRecoveryCheckpointIndex].calibratedAirspeedKnots,
                Is.Zero);

            FlightLessonRouteDefinition legacyRoute = new FlightLessonRouteDefinition
            {
                recoveryCheckpoints = new[]
                {
                    new FlightLessonRecoveryCheckpoint(
                        "unearned_airborne",
                        1,
                        new Vector3(100f, 0f, 100f),
                        6200f,
                        80f,
                        90f)
                }
            };
            Assert.That(FlightLessonRecoverySelector.SelectCheckpointIndex(
                    legacyRoute,
                    FlightLessonProgressState.Initial),
                Is.EqualTo(-1),
                "An unearned airborne checkpoint must never be selected for a segment-zero failure.");
        }

        [Test]
        public void KbduRouteGeometryIsContinuousAndMatchesItsPublishedConstraints()
        {
            FlightLessonRouteDefinition route = KbduExtendedReservoirLesson.CreateDefinition();
            for (int i = 1; i < route.segments.Length; i++)
            {
                Assert.That(route.segments[i].startWaypointIndex,
                    Is.EqualTo(route.segments[i - 1].endWaypointIndex),
                    $"Discontinuous segment at index {i}.");
            }

            FlightLessonWaypoint reservoir = route.waypoints[4];
            Assert.That(reservoir.positionUnityMeters.x,
                Is.InRange(KbduExtendedReservoirLesson.ReservoirBoundsEastMeters.x,
                    KbduExtendedReservoirLesson.ReservoirBoundsEastMeters.y));
            Assert.That(reservoir.positionUnityMeters.z,
                Is.InRange(KbduExtendedReservoirLesson.ReservoirBoundsNorthMeters.x,
                    KbduExtendedReservoirLesson.ReservoirBoundsNorthMeters.y));

            FlightLessonWaypoint downwindStart = route.waypoints[7];
            FlightLessonWaypoint downwindEnd = route.waypoints[8];
            float downwindTrack = FlightLessonPathMath.SampleLine(
                downwindStart.positionUnityMeters,
                downwindEnd.positionUnityMeters,
                downwindStart.positionUnityMeters,
                100f).desiredTrackDegreesTrue;
            Assert.That(downwindTrack, Is.EqualTo(270f).Within(0.1f));
            Assert.That(downwindStart.altitudeMslFeet,
                Is.EqualTo(KbduExtendedReservoirLesson.PublishedTrafficPatternAltitudeMslFeet));
        }

        [Test]
        public void LinePathReturnsContinuousProgressCrossTrackAndLookAhead()
        {
            FlightLessonPathSample sample = FlightLessonPathMath.SampleLine(
                Vector3.zero,
                new Vector3(1000f, 0f, 0f),
                new Vector3(250f, 0f, 40f),
                100f);

            Assert.That(sample.normalizedProgress, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(sample.closestPointUnityMeters.x, Is.EqualTo(250f).Within(0.001f));
            Assert.That(sample.lookAheadPointUnityMeters.x, Is.EqualTo(350f).Within(0.001f));
            Assert.That(Mathf.Abs(sample.crossTrackErrorMeters), Is.EqualTo(40f).Within(0.001f));
            Assert.That(sample.desiredTrackDegreesTrue, Is.EqualTo(90f).Within(0.001f));
        }

        [Test]
        public void ArcPathUsesStableWorldOrientationAndProgress()
        {
            FlightLessonPathSample sample = FlightLessonPathMath.SampleArc(
                new Vector3(100f, 0f, 0f),
                new Vector3(0f, 0f, 100f),
                Vector3.zero,
                false,
                new Vector3(70.7107f, 0f, 70.7107f),
                10f);

            Assert.That(sample.normalizedProgress, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(sample.crossTrackErrorMeters, Is.EqualTo(0f).Within(0.01f));
            Assert.That(sample.desiredTrackDegreesTrue, Is.EqualTo(315f).Within(0.1f));
        }

        [Test]
        public void NavigatorAdvancesFromStateCaptureNotElapsedTime()
        {
            FlightLessonRouteDefinition route = KbduExtendedReservoirLesson.CreateDefinition();
            FlightLessonProgressState progress = FlightLessonProgressState.InitialFor(route);
            FlightLessonAircraftNavigationState farAway = new FlightLessonAircraftNavigationState
            {
                positionUnityMeters = new Vector3(300f, 0f, 500f),
                altitudeMslFeet = 6000f
            };

            FlightLessonProgressState unchanged = FlightLessonNavigator.AdvanceIfCaptured(route, progress, farAway);
            Assert.That(unchanged.currentSegmentIndex, Is.EqualTo(0));

            FlightLessonWaypoint endpoint = route.waypoints[route.segments[0].endWaypointIndex];
            FlightLessonAircraftNavigationState captured = new FlightLessonAircraftNavigationState
            {
                positionUnityMeters = endpoint.positionUnityMeters,
                altitudeMslFeet = endpoint.altitudeMslFeet
            };
            FlightLessonProgressState advanced = FlightLessonNavigator.AdvanceIfCaptured(route, progress, captured);
            Assert.That(advanced.currentSegmentIndex, Is.EqualTo(1));
            Assert.That(advanced.latestRecoveryCheckpointIndex, Is.EqualTo(1));
        }

        [Test]
        public void NavigatorSequencesAClimbLegAfterCrossingItsEndGate()
        {
            FlightLessonRouteDefinition route = KbduExtendedReservoirLesson.CreateDefinition();
            FlightLessonProgressState progress = new FlightLessonProgressState
            {
                currentSegmentIndex = 2,
                latestRecoveryCheckpointIndex = 1,
                lessonComplete = false
            };
            FlightLessonSegment segment = route.segments[progress.currentSegmentIndex];
            FlightLessonWaypoint start = route.waypoints[segment.startWaypointIndex];
            FlightLessonWaypoint end = route.waypoints[segment.endWaypointIndex];
            Vector3 direction = (end.positionUnityMeters - start.positionUnityMeters).normalized;
            FlightLessonAircraftNavigationState crossedEndGate = new FlightLessonAircraftNavigationState
            {
                positionUnityMeters = end.positionUnityMeters + direction * 50f,
                altitudeMslFeet = end.altitudeMslFeet -
                                  segment.completionAltitudeToleranceFeet - 100f
            };

            FlightLessonProgressState advanced = FlightLessonNavigator.AdvanceIfCaptured(
                route,
                progress,
                crossedEndGate);

            Assert.That(advanced.currentSegmentIndex, Is.EqualTo(3),
                "A missed altitude gate must not leave guidance attached to the infinite extension of a completed lateral leg.");
            Assert.That(advanced.latestRecoveryCheckpointIndex, Is.EqualTo(2));
        }

        [Test]
        public void NavigatorDoesNotSequenceAnOffCorridorEndPlaneCrossing()
        {
            FlightLessonRouteDefinition route = KbduExtendedReservoirLesson.CreateDefinition();
            FlightLessonProgressState progress = new FlightLessonProgressState
            {
                currentSegmentIndex = 2,
                latestRecoveryCheckpointIndex = 1,
                lessonComplete = false
            };
            FlightLessonSegment segment = route.segments[progress.currentSegmentIndex];
            FlightLessonWaypoint start = route.waypoints[segment.startWaypointIndex];
            FlightLessonWaypoint end = route.waypoints[segment.endWaypointIndex];
            Vector3 direction = (end.positionUnityMeters - start.positionUnityMeters).normalized;
            Vector3 lateral = new Vector3(-direction.z, 0f, direction.x);
            FlightLessonAircraftNavigationState offCorridor = new FlightLessonAircraftNavigationState
            {
                positionUnityMeters = end.positionUnityMeters +
                                      direction * 50f +
                                      lateral * (end.captureRadiusMeters + 25f),
                altitudeMslFeet = end.altitudeMslFeet
            };

            FlightLessonProgressState unchanged = FlightLessonNavigator.AdvanceIfCaptured(
                route,
                progress,
                offCorridor);

            Assert.That(unchanged.currentSegmentIndex, Is.EqualTo(2));
            Assert.That(unchanged.latestRecoveryCheckpointIndex, Is.EqualTo(1));
        }

        [Test]
        public void GuidanceIntentInterpolatesTargetsAndHonorsSegmentLimits()
        {
            FlightLessonRouteDefinition route = KbduExtendedReservoirLesson.CreateDefinition();
            FlightLessonProgressState progress = FlightLessonProgressState.Initial;
            FlightLessonSegment segment = route.segments[0];
            FlightLessonWaypoint start = route.waypoints[segment.startWaypointIndex];
            FlightLessonWaypoint end = route.waypoints[segment.endWaypointIndex];
            FlightLessonAircraftNavigationState aircraft = new FlightLessonAircraftNavigationState
            {
                positionUnityMeters = Vector3.Lerp(start.positionUnityMeters, end.positionUnityMeters, 0.5f),
                altitudeMslFeet = 5600f,
                calibratedAirspeedKnots = 65f,
                headingDegreesTrue = 90f
            };

            Assert.That(FlightLessonNavigator.TryEvaluate(route, progress, aircraft, out FlightLessonGuidanceIntent intent), Is.True);
            Assert.That(intent.normalizedSegmentProgress, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(intent.targetAltitudeMslFeet,
                Is.EqualTo(Mathf.Lerp(start.altitudeMslFeet, end.altitudeMslFeet, 0.5f)).Within(0.1f));
            Assert.That(intent.maximumBankDegrees, Is.EqualTo(segment.maximumBankDegrees));
            Assert.That(intent.maximumClimbRateFeetPerMinute, Is.EqualTo(segment.maximumClimbRateFeetPerMinute));
        }

        [Test]
        public void AssistanceMixerYieldsSmoothPlayerAuthorityAndExplicitLeverOwnership()
        {
            FlightLessonControlCommand guidance = new FlightLessonControlCommand
            {
                aileron = 0.25f,
                elevator = 0.2f,
                throttle = 0.75f,
                mixture = 1f
            };
            FlightLessonPlayerInput player = new FlightLessonPlayerInput
            {
                primaryAxesAvailable = true,
                throttleOwned = false,
                command = new FlightLessonControlCommand
                {
                    aileron = 0f,
                    elevator = -1f,
                    throttle = 0.2f
                }
            };
            FlightLessonAssistanceMixSettings settings = FlightLessonAssistanceMixSettings.Default;

            FlightLessonControlCommand mixed = FlightLessonAssistanceMixer.Mix(guidance, player, settings);
            Assert.That(mixed.aileron, Is.EqualTo(guidance.aileron).Within(0.0001f));
            Assert.That(mixed.elevator, Is.EqualTo(-1f).Within(0.0001f));
            Assert.That(mixed.throttle, Is.EqualTo(0.75f).Within(0.0001f));

            player.throttleOwned = true;
            mixed = FlightLessonAssistanceMixer.Mix(guidance, player, settings);
            Assert.That(mixed.throttle, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void GuidanceControllerIsNeutralOnAStabilizedStraightPath()
        {
            FlightLessonGuidanceController controller = new FlightLessonGuidanceController();
            FlightLessonGuidanceControllerSettings settings = FlightLessonGuidanceControllerSettings.Default;
            FlightLessonGuidanceIntent intent = Intent(track: 90f, altitude: 6500f, speed: 90f);
            FlightLessonGuidanceAircraftState state = GuidanceState(heading: 90f, altitude: 6500f, speed: 90f);

            Assert.That(controller.TryStep(intent, state, 0.02f, settings, out FlightLessonControlCommand command), Is.True);
            Assert.That(command.aileron, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(command.elevator, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(command.rudder, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(command.throttle, Is.EqualTo(settings.cruiseThrottle).Within(0.0001f));
        }

        [Test]
        public void GuidanceControllerUsesConventionalHeadingBankAndAileronSigns()
        {
            FlightLessonGuidanceController controller = new FlightLessonGuidanceController();
            FlightLessonGuidanceControllerSettings settings = FlightLessonGuidanceControllerSettings.Default;
            FlightLessonGuidanceIntent intent = Intent(track: 100f, altitude: 6500f, speed: 90f);
            FlightLessonGuidanceAircraftState state = GuidanceState(heading: 90f, altitude: 6500f, speed: 90f);

            Assert.That(controller.TryStep(intent, state, 0.02f, settings, out FlightLessonControlCommand rightTurn), Is.True);
            Assert.That(controller.Diagnostics.targetBankDegrees, Is.GreaterThan(0f));
            Assert.That(rightTurn.aileron, Is.GreaterThan(0f));

            controller.Reset();
            intent = Intent(track: 80f, altitude: 6500f, speed: 90f);
            Assert.That(controller.TryStep(intent, state, 0.02f, settings, out FlightLessonControlCommand leftTurn), Is.True);
            Assert.That(controller.Diagnostics.targetBankDegrees, Is.LessThan(0f));
            Assert.That(leftTurn.aileron, Is.LessThan(0f));
        }

        [Test]
        public void GuidanceControllerCommandsNoseUpForAltitudeAndAddsThrottleForLowAirspeed()
        {
            FlightLessonGuidanceController controller = new FlightLessonGuidanceController();
            FlightLessonGuidanceControllerSettings settings = FlightLessonGuidanceControllerSettings.Default;
            FlightLessonGuidanceIntent intent = Intent(track: 90f, altitude: 7000f, speed: 95f);
            FlightLessonGuidanceAircraftState state = GuidanceState(heading: 90f, altitude: 6500f, speed: 80f);

            Assert.That(controller.TryStep(intent, state, 0.02f, settings, out FlightLessonControlCommand command), Is.True);
            Assert.That(controller.Diagnostics.targetVerticalSpeedFeetPerMinute, Is.GreaterThan(0f));
            Assert.That(controller.Diagnostics.targetPitchDegrees, Is.GreaterThan(0f));
            Assert.That(command.elevator, Is.GreaterThan(0f));
            Assert.That(command.throttle, Is.GreaterThan(settings.cruiseThrottle));
        }

        [Test]
        public void GuidanceControllerCountersRatesAndUsesSideslipForRudder()
        {
            FlightLessonGuidanceController controller = new FlightLessonGuidanceController();
            FlightLessonGuidanceControllerSettings settings = FlightLessonGuidanceControllerSettings.Default;
            FlightLessonGuidanceIntent intent = Intent(track: 90f, altitude: 6500f, speed: 90f);
            FlightLessonGuidanceAircraftState state = GuidanceState(heading: 90f, altitude: 6500f, speed: 90f);
            state.rollRateDegreesPerSecond = 8f;
            state.pitchRateDegreesPerSecond = 5f;
            state.sideslipDegrees = 4f;

            Assert.That(controller.TryStep(intent, state, 0.02f, settings, out FlightLessonControlCommand command), Is.True);
            Assert.That(command.aileron, Is.LessThan(0f));
            Assert.That(command.elevator, Is.LessThan(0f));
            Assert.That(command.rudder, Is.GreaterThan(0f));
        }

        [Test]
        public void GuidanceControllerBoundsOutputsPreventsWindupAndResetsState()
        {
            FlightLessonGuidanceController controller = new FlightLessonGuidanceController();
            FlightLessonGuidanceControllerSettings settings = FlightLessonGuidanceControllerSettings.Default;
            FlightLessonGuidanceIntent intent = Intent(track: 270f, altitude: 12000f, speed: 160f);
            FlightLessonGuidanceAircraftState state = GuidanceState(heading: 90f, altitude: 5300f, speed: 20f);
            state.bankDegrees = -60f;
            state.pitchDegrees = -30f;
            state.sideslipDegrees = 50f;

            FlightLessonControlCommand command = default;
            for (int i = 0; i < 500; i++)
            {
                Assert.That(controller.TryStep(intent, state, 0.02f, settings, out command), Is.True);
            }

            Assert.That(command.aileron, Is.InRange(-1f, 1f));
            Assert.That(command.elevator, Is.InRange(-1f, 1f));
            Assert.That(command.rudder, Is.InRange(-1f, 1f));
            Assert.That(command.throttle, Is.InRange(0f, 1f));
            Assert.That(Mathf.Abs(controller.Diagnostics.rollIntegral), Is.LessThanOrEqualTo(settings.rollIntegralLimit));
            Assert.That(Mathf.Abs(controller.Diagnostics.pitchIntegral), Is.LessThanOrEqualTo(settings.pitchIntegralLimit));
            Assert.That(Mathf.Abs(controller.Diagnostics.throttleIntegral), Is.LessThanOrEqualTo(settings.throttleIntegralLimit));

            controller.Reset();
            Assert.That(controller.Diagnostics.rollIntegral, Is.EqualTo(0f));
            Assert.That(controller.Diagnostics.pitchIntegral, Is.EqualTo(0f));
            Assert.That(controller.Diagnostics.throttleIntegral, Is.EqualTo(0f));
        }

        [Test]
        public void CrashClassifierAllowsNormalRunwayTouchdownAndRejectsHardLanding()
        {
            FlightLessonCrashSettings settings = FlightLessonCrashSettings.ConservativeTrainingDefaults;
            FlightLessonTerrainSample runway = Terrain(runway: true);
            FlightLessonCrashClassifier classifier = new FlightLessonCrashClassifier();
            FlightLessonAircraftSafetyState state = NominalAirborneState();
            Assert.That(classifier.Evaluate(state, runway, 0.02f, settings), Is.EqualTo(FlightLessonCrashReason.None));

            state.weightOnWheels = true;
            state.altitudeAglMeters = 0f;
            state.verticalSpeedFeetPerMinute = -250f;
            Assert.That(classifier.Evaluate(state, runway, 0.02f, settings), Is.EqualTo(FlightLessonCrashReason.None));

            classifier.Reset();
            state = NominalAirborneState();
            classifier.Evaluate(state, runway, 0.02f, settings);
            state.weightOnWheels = true;
            state.altitudeAglMeters = 0f;
            state.verticalSpeedFeetPerMinute = -1100f;
            Assert.That(classifier.Evaluate(state, runway, 0.02f, settings), Is.EqualTo(FlightLessonCrashReason.HardLanding));

            classifier.Reset();
            state = NominalAirborneState();
            state.verticalSpeedFeetPerMinute = -1100f;
            classifier.Evaluate(state, runway, 0.02f, settings);
            state.weightOnWheels = true;
            state.altitudeAglMeters = 0f;
            state.verticalSpeedFeetPerMinute = 0f;
            Assert.That(classifier.Evaluate(state, runway, 0.02f, settings),
                Is.EqualTo(FlightLessonCrashReason.HardLanding),
                "Prototype contact clamping must not erase the pre-impact sink rate.");
        }

        [Test]
        public void CrashClassifierUsesTerrainAndDebounceWithoutTreatingRouteDeviationAsCrash()
        {
            FlightLessonCrashSettings settings = FlightLessonCrashSettings.ConservativeTrainingDefaults;
            FlightLessonCrashClassifier classifier = new FlightLessonCrashClassifier();
            FlightLessonAircraftSafetyState state = NominalAirborneState();
            state.positionUnityMeters = new Vector3(7000f, 100f, 0f);
            Assert.That(classifier.Evaluate(state, Terrain(false), 0.1f, settings), Is.EqualTo(FlightLessonCrashReason.None));

            state.positionUnityMeters = new Vector3(0f, 10f, 0f);
            state.altitudeAglMeters = 10f;
            state.calibratedAirspeedKnots = 30f;
            state.verticalSpeedFeetPerMinute = -900f;
            Assert.That(classifier.Evaluate(state, Terrain(false), 0.4f, settings), Is.EqualTo(FlightLessonCrashReason.None));
            Assert.That(classifier.Evaluate(state, Terrain(false), 0.4f, settings),
                Is.EqualTo(FlightLessonCrashReason.UnrecoverableLowAltitudeState));
        }

        [Test]
        public void CrashClassifierDebouncesATransientRunwayBoundarySample()
        {
            FlightLessonCrashSettings settings =
                FlightLessonCrashSettings.ConservativeTrainingDefaults;
            FlightLessonCrashClassifier classifier =
                new FlightLessonCrashClassifier();
            FlightLessonAircraftSafetyState state = NominalAirborneState();
            state.weightOnWheels = true;
            state.altitudeAglMeters = 0f;
            state.groundSpeedKnots = 66f;

            Assert.That(classifier.Evaluate(state, Terrain(false), 0.10f, settings),
                Is.EqualTo(FlightLessonCrashReason.None));
            Assert.That(classifier.Evaluate(state, Terrain(false), 0.10f, settings),
                Is.EqualTo(FlightLessonCrashReason.None));
            Assert.That(classifier.Evaluate(state, Terrain(true), 0.02f, settings),
                Is.EqualTo(FlightLessonCrashReason.None),
                "Returning to the runway must clear transient unsafe-surface time.");

            Assert.That(classifier.Evaluate(state, Terrain(false), 0.20f, settings),
                Is.EqualTo(FlightLessonCrashReason.None));
            Assert.That(classifier.Evaluate(state, Terrain(false), 0.16f, settings),
                Is.EqualTo(FlightLessonCrashReason.UnsafeSurfaceContact));
        }

        [Test]
        public void RecoveryStateMachineRequestsOneAuthoritativeResetThenFadesBackIn()
        {
            FlightLessonRouteDefinition route = KbduExtendedReservoirLesson.CreateDefinition();
            FlightLessonProgressState progress = new FlightLessonProgressState
            {
                currentSegmentIndex = 6,
                latestRecoveryCheckpointIndex = 3
            };
            FlightLessonRecoveryStateMachine machine = new FlightLessonRecoveryStateMachine();
            FlightLessonRecoveryTimings timings = new FlightLessonRecoveryTimings
            {
                fadeOutSeconds = 0.1f,
                stabilizationSeconds = 0.1f,
                fadeInSeconds = 0.1f
            };

            Assert.That(machine.TryBegin(FlightLessonCrashReason.HardLanding, route, progress, timings), Is.True);
            FlightLessonRecoveryOutput faded = machine.Step(0.1f);
            Assert.That(faded.cancelControlOwnership, Is.True);
            Assert.That(faded.comfortFadeOpacity, Is.EqualTo(1f));

            FlightLessonRecoveryOutput reset = machine.Step(0f);
            Assert.That(reset.requestAuthoritativeReset, Is.True);
            Assert.That(reset.request.checkpointIndex, Is.EqualTo(3));
            Assert.That(machine.Step(0f).requestAuthoritativeReset, Is.False);

            machine.AcknowledgeAuthoritativeReset(true);
            Assert.That(machine.Step(0.1f).phase, Is.EqualTo(FlightLessonRecoveryPhase.FadingIn));
            FlightLessonRecoveryOutput complete = machine.Step(0.1f);
            Assert.That(complete.phase, Is.EqualTo(FlightLessonRecoveryPhase.Idle));
            Assert.That(complete.comfortFadeOpacity, Is.EqualTo(0f));
            Assert.That(complete.suppressPlayerInput, Is.False);
        }

        [Test]
        public void RecoverySelectorFallsBackToMostRecentPassedCheckpoint()
        {
            FlightLessonRouteDefinition route = KbduExtendedReservoirLesson.CreateDefinition();
            FlightLessonProgressState progress = new FlightLessonProgressState
            {
                currentSegmentIndex = 7,
                latestRecoveryCheckpointIndex = -1
            };

            Assert.That(FlightLessonRecoverySelector.SelectCheckpointIndex(route, progress), Is.EqualTo(4));
        }

        private static FlightLessonAircraftSafetyState NominalAirborneState()
        {
            return new FlightLessonAircraftSafetyState
            {
                positionUnityMeters = new Vector3(0f, 100f, 0f),
                rotationUnity = Quaternion.identity,
                velocityUnityMetersPerSecond = new Vector3(30f, 0f, 0f),
                altitudeAglMeters = 100f,
                calibratedAirspeedKnots = 80f,
                groundSpeedKnots = 80f,
                verticalSpeedFeetPerMinute = 0f,
                pitchDegrees = 2f,
                bankDegrees = 0f,
                weightOnWheels = false
            };
        }

        private static FlightLessonGuidanceIntent Intent(float track, float altitude, float speed)
        {
            return new FlightLessonGuidanceIntent(
                "test",
                FlightLessonSegmentKind.Cruise,
                Vector3.zero,
                track,
                0f,
                altitude,
                speed,
                25f,
                700f,
                0.5f);
        }

        private static FlightLessonGuidanceAircraftState GuidanceState(float heading, float altitude, float speed)
        {
            return new FlightLessonGuidanceAircraftState
            {
                headingDegreesTrue = heading,
                bankDegrees = 0f,
                pitchDegrees = 0f,
                rollRateDegreesPerSecond = 0f,
                pitchRateDegreesPerSecond = 0f,
                yawRateDegreesPerSecond = 0f,
                altitudeMslFeet = altitude,
                verticalSpeedFeetPerMinute = 0f,
                calibratedAirspeedKnots = speed,
                sideslipDegrees = 0f
            };
        }

        private static FlightLessonTerrainSample Terrain(bool runway)
        {
            return new FlightLessonTerrainSample
            {
                isValid = true,
                heightUnityMeters = 0f,
                normalUnity = Vector3.up,
                isRunwayOrApron = runway,
                isWater = false
            };
        }
    }
}
#endif
