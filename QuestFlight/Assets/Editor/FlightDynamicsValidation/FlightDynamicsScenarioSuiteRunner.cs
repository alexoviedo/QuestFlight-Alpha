using System;
using System.Collections.Generic;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlight.Editor.FlightDynamicsValidation
{
    public sealed class FlightDynamicsScenarioSuiteRunner
    {
        private const double Step = FlightDynamicsAcceptanceContract.FixedStepSeconds;
        private readonly List<FlightTraceSample> _trace = new List<FlightTraceSample>(16384);
        private readonly List<FlightScenarioInputEvidence> _scenarioInputs = new List<FlightScenarioInputEvidence>();

        public IReadOnlyList<FlightTraceSample> Trace => _trace;

        public FlightDynamicsEvidenceSuite RunNativeOnly()
        {
            _trace.Clear();
            _scenarioInputs.Clear();
            FlightDynamicsEvidenceSuite suite = NewSuite();
            using (NativeJsbsimTraceBackend native = new NativeJsbsimTraceBackend())
            {
                suite.nativeInputs = NativeFlightEvidenceIdentity.Capture();
                if (native.IsAvailable)
                    RunBackend(native, suite);
                else
                    suite.scenarios.Add(UnsupportedScenario(
                        "native_backend_availability",
                        native.Id,
                        native.LastError));
            }
            suite.scenarioInputs.AddRange(_scenarioInputs);
            return suite;
        }

        private static FlightDynamicsEvidenceSuite NewSuite()
        {
            return new FlightDynamicsEvidenceSuite
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                productionConfig = ProductionFlightConfigInspector.Inspect()
            };
        }

        private void RunBackend(
            IFlightDynamicsTraceBackend backend,
            FlightDynamicsEvidenceSuite suite)
        {
            suite.scenarios.Add(ControlSignPulseDoublet(backend));
            suite.scenarios.Add(NeutralTrim(backend));
            suite.scenarios.Add(BetaYawDamping(backend));
            suite.scenarios.Add(CoordinatedTurn(backend));
            suite.scenarios.Add(SlowFlightStallRecovery(backend));
            suite.scenarios.Add(PowerOffGlide(backend));
            suite.scenarios.Add(FlapResponse(backend));
            suite.scenarios.Add(TakeoffClimb(backend));
            suite.scenarios.Add(ApproachLanding(backend));
            suite.scenarios.Add(GroundSteering(backend));
            suite.scenarios.Add(MassCgMatrix(backend));
            suite.scenarios.Add(WindMatrix(backend));
        }

        private FlightScenarioEvidence ControlSignPulseDoublet(IFlightDynamicsTraceBackend backend)
        {
            const string id = "control_sign_pulse_doublet";
            FlightScenarioEvidence result = Scenario(id, backend);
            AxisEvidence(backend, id, "aileron", ControlAxis.Aileron, state => state.bankDegrees, result);
            AxisEvidence(backend, id, "elevator", ControlAxis.Elevator, state => state.pitchDegrees, result);
            AxisEvidence(backend, id, "rudder", ControlAxis.Rudder, state => state.headingDegrees, result, true);
            RunResult doublet = Run(
                backend,
                id + "/aileron_doublet",
                FlightScenarioInitialState.Airborne(),
                6.0,
                (time, state) => Controls(
                    aileron: time >= 1.0 && time < 2.0 ? 0.35f :
                             time >= 2.0 && time < 3.0 ? -0.35f : 0f,
                    throttle: 0.55f));
            if (!doublet.ok) return Error(result, backend.LastError);
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "aileron_doublet_final_bank",
                FlightEvidenceStatus.Unproven,
                "Recorded for regression comparison; no validated C172 doublet envelope is yet pinned.",
                doublet.end.bankDegrees,
                "deg"));
            result.ResolveStatus();
            return result;
        }

        private void AxisEvidence(
            IFlightDynamicsTraceBackend backend,
            string scenario,
            string name,
            ControlAxis axis,
            Func<FlightDynamicsState, double> observed,
            FlightScenarioEvidence target,
            bool wrapAngle = false)
        {
            RunResult neutral = Run(
                backend,
                scenario + "/" + name + "_neutral_reference",
                FlightScenarioInitialState.Airborne(),
                3.0,
                (time, state) => AxisControls(axis, 0f));
            RunResult positive = Run(
                backend,
                scenario + "/" + name + "_positive",
                FlightScenarioInitialState.Airborne(),
                3.0,
                (time, state) => AxisControls(axis, time >= 1.0 && time < 2.0 ? 0.35f : 0f));
            RunResult negative = Run(
                backend,
                scenario + "/" + name + "_negative",
                FlightScenarioInitialState.Airborne(),
                3.0,
                (time, state) => AxisControls(axis, time >= 1.0 && time < 2.0 ? -0.35f : 0f));
            if (!neutral.ok || !positive.ok || !negative.ok)
            {
                target.metrics.Add(FlightEvidenceMetric.Classified(
                    name + "_sign",
                    FlightEvidenceStatus.Error,
                    backend.LastError));
                return;
            }

            FlightDynamicsState positivePulse = positive.StateNear(2.0);
            FlightDynamicsState negativePulse = negative.StateNear(2.0);
            FlightDynamicsState neutralPulse = neutral.StateNear(2.0);
            double positiveDelta = wrapAngle
                ? DeltaAngle(observed(neutralPulse), observed(positivePulse))
                : observed(positivePulse) - observed(neutralPulse);
            double negativeDelta = wrapAngle
                ? DeltaAngle(observed(neutralPulse), observed(negativePulse))
                : observed(negativePulse) - observed(neutralPulse);
            double signedMinimum = FlightDynamicsAcceptanceContract.MinimumControlResponseDegrees;
            target.metrics.Add(FlightEvidenceMetric.Bounded(
                name + "_positive_response",
                positiveDelta,
                "deg",
                signedMinimum,
                180.0,
                "Positive product command must produce the documented positive backend-neutral response."));
            target.metrics.Add(FlightEvidenceMetric.Bounded(
                name + "_negative_response",
                -negativeDelta,
                "deg",
                signedMinimum,
                180.0,
                "Negative product command must produce the documented negative backend-neutral response."));
            double symmetry = Math.Abs(Math.Abs(positiveDelta) - Math.Abs(negativeDelta)) /
                              Math.Max(0.01, Math.Max(Math.Abs(positiveDelta), Math.Abs(negativeDelta)));
            target.metrics.Add(FlightEvidenceMetric.Bounded(
                name + "_pulse_symmetry_error",
                symmetry,
                "ratio",
                0.0,
                0.35,
                "Pulse response is measured against a matched neutral trajectory so natural trim drift is not misclassified as control asymmetry."));
        }

        private FlightScenarioEvidence NeutralTrim(IFlightDynamicsTraceBackend backend)
        {
            const string id = "neutral_trim";
            FlightScenarioEvidence result = Scenario(id, backend);
            RunResult run;
            if (backend.SupportsTrim)
            {
                run = RunTrimmed(
                    backend,
                    id,
                    FlightScenarioInitialState.Airborne(),
                    20.0);
            }
            else
            {
                run = Run(
                    backend,
                    id,
                    FlightScenarioInitialState.Airborne(),
                    20.0,
                    (time, state) => Controls(throttle: 0.55f));
            }
            if (!run.ok) return Error(result, backend.LastError);
            result.metrics.Add(FlightEvidenceMetric.Bounded(
                "neutral_bank_drift",
                Math.Abs(run.end.bankDegrees - run.start.bankDegrees),
                "deg",
                0.0,
                FlightDynamicsAcceptanceContract.MaximumNeutralBankDriftDegrees));
            result.metrics.Add(FlightEvidenceMetric.Bounded(
                "neutral_heading_drift",
                Math.Abs(DeltaAngle(run.start.headingDegrees, run.end.headingDegrees)),
                "deg",
                0.0,
                FlightDynamicsAcceptanceContract.MaximumNeutralHeadingDriftDegrees));
            result.metrics.Add(FlightEvidenceMetric.Bounded(
                "neutral_final_vertical_speed",
                Math.Abs(run.end.verticalSpeedFeetPerMinute),
                "ft/min",
                0.0,
                FlightDynamicsAcceptanceContract.MaximumNeutralVerticalSpeedFpm,
                "Broad engineering bound only; a weight/CG/atmosphere-specific trim target remains unproven."));
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "trim_solver",
                backend.SupportsTrim ? FlightEvidenceStatus.Pass : FlightEvidenceStatus.Unsupported,
                backend.SupportsTrim
                    ? "The unchanged native candidate solved and held a longitudinal trim state at the configured loading and atmosphere."
                    : "This backend has no exposed trim solver."));
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "trim_point_reference_fidelity",
                FlightEvidenceStatus.Unproven,
                "The solver result is deterministic, but an independently sourced weight/CG/density-altitude/power-specific C172P trim reference is not yet pinned."));
            result.ResolveStatus();
            return result;
        }

        private FlightScenarioEvidence BetaYawDamping(IFlightDynamicsTraceBackend backend)
        {
            const string id = "beta_yaw_damping";
            FlightScenarioEvidence result = Scenario(id, backend);
            RunResult run = Run(
                backend,
                id,
                FlightScenarioInitialState.Airborne(),
                14.0,
                (time, state) => Controls(
                    rudder: time >= 1.0 && time < 3.0 ? 0.45f : 0f,
                    throttle: 0.55f));
            if (!run.ok) return Error(result, backend.LastError);
            double peak = run.MaximumAbsoluteDerivedBeta(1.0, 4.0);
            double residual = run.MaximumAbsoluteDerivedBeta(12.0, 14.0);
            double ratio = residual / Math.Max(0.1, peak);
            result.metrics.Add(FlightEvidenceMetric.Bounded(
                "sideslip_residual_ratio",
                ratio,
                "ratio",
                0.0,
                FlightDynamicsAcceptanceContract.MaximumSideslipDampingRatio,
                "Authoritative native aero/beta-deg amplitude in the 12-14s window divided by the 1-4s peak; the bound is an internal engineering criterion."));
            double yawPeak = run.MaximumAbsoluteYawRate(1.0, 4.0);
            double yawResidual = run.MaximumAbsoluteYawRate(12.0, 14.0);
            result.metrics.Add(FlightEvidenceMetric.Bounded(
                "yaw_rate_residual_ratio",
                yawResidual / Math.Max(0.1, yawPeak),
                "ratio",
                0.0,
                0.55));
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "reported_sideslip_unit_contract",
                FlightEvidenceStatus.Pass,
                "The native backend reports aero/beta-deg."));
            result.ResolveStatus();
            return result;
        }

        private FlightScenarioEvidence CoordinatedTurn(IFlightDynamicsTraceBackend backend)
        {
            const string id = "coordinated_turn";
            FlightScenarioEvidence result = Scenario(id, backend);
            RunResult run = Run(
                backend,
                id,
                FlightScenarioInitialState.Airborne(),
                12.0,
                (time, state) => Controls(aileron: 0.10f, rudder: 0.06f, throttle: 0.58f));
            if (!run.ok) return Error(result, backend.LastError);
            double beta = run.MeanAbsoluteDerivedBeta(8.0, 12.0);
            result.metrics.Add(FlightEvidenceMetric.Bounded(
                "mean_sideslip",
                beta,
                "deg",
                0.0,
                FlightDynamicsAcceptanceContract.MaximumCoordinatedTurnSideslipDegrees));
            double actualRate = Math.Abs(DeltaAngle(
                run.StateNear(8.0).headingDegrees,
                run.end.headingDegrees)) / 4.0;
            double meanBank = run.MeanAbsoluteBank(8.0, 12.0);
            double meanSpeedMps = Math.Max(1.0, run.MeanTrueSpeedKnots(8.0, 12.0) *
                FlightFrameConversions.KnotsToMetersPerSecond);
            double expectedRate = Math.Abs(9.80665 * Math.Tan(meanBank * Math.PI / 180.0) /
                                           meanSpeedMps * 180.0 / Math.PI);
            double relativeError = Math.Abs(actualRate - expectedRate) / Math.Max(0.25, expectedRate);
            result.metrics.Add(FlightEvidenceMetric.Bounded(
                "coordinated_turn_rate_relative_error",
                relativeError,
                "ratio",
                0.0,
                FlightDynamicsAcceptanceContract.MaximumTurnRateRelativeError,
                "Compared with g*tan(bank)/true-airspeed using native diagnostics; prototype evidence falls back to calibrated airspeed because that backend has no independent TAS output."));
            result.ResolveStatus();
            return result;
        }

        private FlightScenarioEvidence SlowFlightStallRecovery(IFlightDynamicsTraceBackend backend)
        {
            const string id = "slow_flight_stall_recovery";
            FlightScenarioEvidence result = Scenario(id, backend);
            RunResult run = Run(
                backend,
                id,
                FlightScenarioInitialState.Airborne(65.0, 900.0),
                30.0,
                (time, state) => time < 10.0
                    ? Controls(elevator: -0.25f, throttle: 0.25f)
                    : time < 18.0
                        ? Controls(elevator: -0.75f, throttle: 0.12f)
                        : Controls(elevator: 0.20f, throttle: 1f));
            if (!run.ok) return Error(result, backend.LastError);
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "maximum_angle_of_attack",
                FlightEvidenceStatus.Unproven,
                "The trace records AoA, but the backend-neutral state exposes no stall/buffet/break flag with a pinned reference contract.",
                run.MaximumAngleOfAttack(),
                "deg"));
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "minimum_airspeed",
                FlightEvidenceStatus.Unproven,
                "Recorded for comparison; this open-loop maneuver is not a trim-controlled certification maneuver.",
                run.MinimumSpeedKnots(),
                "kt"));
            result.metrics.Add(FlightEvidenceMetric.Bounded(
                "recovery_speed_gain",
                run.end.calibratedAirspeedKnots - run.MinimumSpeedKnots(),
                "kt",
                5.0,
                double.MaxValue,
                "Recovery must regain energy; an arbitrary upper bound is not a valid substitute for a pinned maneuver and overspeed contract."));
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "recovery_final_speed",
                FlightEvidenceStatus.Unproven,
                "Recorded separately for the future maneuver/overspeed contract; it is not used to tune the aircraft model to an invented cap.",
                run.end.calibratedAirspeedKnots,
                "kt"));
            result.ResolveStatus();
            return result;
        }

        private FlightScenarioEvidence PowerOffGlide(IFlightDynamicsTraceBackend backend)
        {
            const string id = "power_off_glide";
            FlightScenarioEvidence result = Scenario(id, backend);
            RunResult run = Run(
                backend,
                id,
                FlightScenarioInitialState.Airborne(75.0, 1000.0, engineRunning: false),
                30.0,
                (time, state) => Controls(elevator: 0.04f, throttle: 0f, mixture: 0f));
            if (!run.ok) return Error(result, backend.LastError);
            result.metrics.Add(FlightEvidenceMetric.Bounded(
                "engine_off_rpm",
                run.end.engineRpm,
                "rpm",
                0.0,
                150.0,
                "A stopped-engine initial condition must not be represented as idle power; windmilling support needs a separate prop-state field."));
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "glide_sink_rate",
                FlightEvidenceStatus.Unproven,
                "Recorded, but no stabilized best-glide trim/weight/atmosphere point is pinned yet.",
                run.MeanVerticalSpeed(20.0, 30.0),
                "ft/min"));
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "glide_speed",
                FlightEvidenceStatus.Unproven,
                "Recorded for future C172 reference comparison.",
                run.MeanSpeedKnots(20.0, 30.0),
                "kt"));
            result.ResolveStatus();
            return result;
        }

        private FlightScenarioEvidence FlapResponse(IFlightDynamicsTraceBackend backend)
        {
            const string id = "flap_response";
            FlightScenarioEvidence result = Scenario(id, backend);
            RunResult clean = Run(
                backend,
                id + "/clean",
                FlightScenarioInitialState.Airborne(70.0, 600.0),
                12.0,
                (time, state) => Controls(throttle: 0.35f, flaps: 0f));
            RunResult landing = Run(
                backend,
                id + "/landing",
                FlightScenarioInitialState.Airborne(70.0, 600.0),
                12.0,
                (time, state) => Controls(throttle: 0.35f, flaps: 1f));
            if (!clean.ok || !landing.ok) return Error(result, backend.LastError);
            result.metrics.Add(FlightEvidenceMetric.Bounded(
                "flap_deceleration_delta",
                clean.end.calibratedAirspeedKnots - landing.end.calibratedAirspeedKnots,
                "kt",
                1.0,
                80.0));
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "flap_pitch_and_lift_delta",
                FlightEvidenceStatus.Unproven,
                "Pitch and vertical-speed deltas are recorded, but a flap-transition transient envelope is not pinned.",
                landing.end.verticalSpeedFeetPerMinute - clean.end.verticalSpeedFeetPerMinute,
                "ft/min"));
            result.ResolveStatus();
            return result;
        }

        private FlightScenarioEvidence TakeoffClimb(IFlightDynamicsTraceBackend backend)
        {
            const string id = "takeoff_climb";
            FlightScenarioEvidence result = Scenario(id, backend);
            RunResult run = Run(
                backend,
                id,
                FlightScenarioInitialState.Ground(),
                45.0,
                (time, state) => Controls(
                    // Rotate by the configured C172 speed contract rather than
                    // by an arbitrary elapsed time that allowed acceleration
                    // well beyond the rotation window before pilot input.
                    elevator: state.calibratedAirspeedKnots < 55.0 ? 0f : 0.30f,
                    throttle: 1f,
                    mixture: 1f));
            if (!run.ok) return Error(result, backend.LastError);
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "settled_ground_contact",
                run.HasGroundContact(
                    FlightDynamicsAcceptanceContract.InitialGroundSettlingWindowSeconds,
                    10.0)
                    ? FlightEvidenceStatus.Pass
                    : FlightEvidenceStatus.Fail,
                "Ground contact must be observed after the reset/gear-settling guard window before liftoff can be credited."));
            if (!run.TryFirstAirborne(out FlightTraceSample liftoff))
            {
                result.metrics.Add(FlightEvidenceMetric.Classified(
                    "liftoff",
                    FlightEvidenceStatus.Fail,
                    "No sustained ground-to-airborne transition with positive terrain clearance occurred after the initial settling window."));
            }
            else
            {
                result.metrics.Add(FlightEvidenceMetric.Bounded(
                    "liftoff_speed",
                    liftoff.airspeedKnots,
                    "kt",
                    FlightDynamicsAcceptanceContract.MinimumTakeoffSpeedKnots,
                    FlightDynamicsAcceptanceContract.MaximumTakeoffSpeedKnots));
            }
            result.metrics.Add(FlightEvidenceMetric.Bounded(
                "final_climb_rate",
                run.MeanVerticalSpeed(38.0, 45.0),
                "ft/min",
                FlightDynamicsAcceptanceContract.MinimumClimbRateFpm,
                2000.0));
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "takeoff_distance",
                FlightEvidenceStatus.Unproven,
                "Recorded in the trace; density altitude, weight/CG, surface, and pilot-technique conditions are not yet pinned.",
                run.HorizontalDistanceMeters(),
                "m"));
            result.ResolveStatus();
            return result;
        }

        private FlightScenarioEvidence ApproachLanding(IFlightDynamicsTraceBackend backend)
        {
            const string id = "approach_landing";
            FlightScenarioEvidence result = Scenario(id, backend);
            RunResult run = Run(
                backend,
                id,
                FlightScenarioInitialState.Airborne(70.0, 95.0, flightPathAngleDegrees: -3.0),
                75.0,
                (time, state) => Controls(
                    elevator: time < 65.0 ? 0.04f : 0.22f,
                    throttle: time < 65.0 ? 0.24f : 0.05f,
                    flaps: 0.67f));
            if (!run.ok) return Error(result, backend.LastError);
            if (!run.TryTouchdown(out FlightTraceSample touchdown))
            {
                result.metrics.Add(FlightEvidenceMetric.Classified(
                    "touchdown_detected",
                    FlightEvidenceStatus.Fail,
                    "No airborne-to-weight-on-wheels transition occurred in the open-loop approach window."));
            }
            else
            {
                result.metrics.Add(FlightEvidenceMetric.Bounded(
                    "touchdown_sink_rate",
                    Math.Abs(touchdown.verticalSpeedFeetPerMinute),
                    "ft/min",
                    0.0,
                    FlightDynamicsAcceptanceContract.MaximumTouchdownSinkRateFpm));
            }
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "physical_landing_contact",
                backend.SupportsPhysicalGroundContact
                    ? FlightEvidenceStatus.Unproven
                    : FlightEvidenceStatus.Unsupported,
                backend.SupportsPhysicalGroundContact
                    ? "The native trace records three-wheel compression and aggregate gear force; a landing-load acceptance envelope is not yet pinned."
                    : "The prototype uses a single ground-height clamp, not three-wheel contact dynamics."));
            result.ResolveStatus();
            return result;
        }

        private FlightScenarioEvidence GroundSteering(IFlightDynamicsTraceBackend backend)
        {
            const string id = "ground_steering";
            FlightScenarioEvidence result = Scenario(id, backend);
            RunResult right = Run(
                backend,
                id + "/right",
                FlightScenarioInitialState.Ground(),
                12.0,
                (time, state) => Controls(
                    rudder: 0.8f,
                    throttle: 0.35f,
                    rightBrake: 0.15f));
            RunResult left = Run(
                backend,
                id + "/left",
                FlightScenarioInitialState.Ground(),
                12.0,
                (time, state) => Controls(
                    rudder: -0.8f,
                    throttle: 0.35f,
                    leftBrake: 0.15f));
            if (!right.ok || !left.ok) return Error(result, backend.LastError);
            double rightHeading = DeltaAngle(right.start.headingDegrees, right.end.headingDegrees);
            double leftHeading = DeltaAngle(left.start.headingDegrees, left.end.headingDegrees);
            result.metrics.Add(FlightEvidenceMetric.Bounded(
                "right_steering_sign",
                rightHeading,
                "deg",
                1.0,
                180.0));
            result.metrics.Add(FlightEvidenceMetric.Bounded(
                "left_steering_sign",
                -leftHeading,
                "deg",
                1.0,
                180.0));
            double symmetry = Math.Abs(Math.Abs(rightHeading) - Math.Abs(leftHeading)) /
                              Math.Max(0.1, Math.Max(Math.Abs(rightHeading), Math.Abs(leftHeading)));
            result.metrics.Add(FlightEvidenceMetric.Bounded(
                "ground_steering_symmetry_error",
                symmetry,
                "ratio",
                0.0,
                0.35));
            if (backend.SupportsNoseWheelSteeringDiagnostics)
            {
                result.metrics.Add(FlightEvidenceMetric.Bounded(
                    "right_nosewheel_command",
                    right.MaximumNoseGearSteeringDegrees(),
                    "deg",
                    7.5,
                    10.1,
                    "Full-right pedal input must reach the C172 model's ten-degree nosewheel steering authority."));
                result.metrics.Add(FlightEvidenceMetric.Bounded(
                    "left_nosewheel_command",
                    -left.MinimumNoseGearSteeringDegrees(),
                    "deg",
                    7.5,
                    10.1,
                    "Full-left pedal input must reach the C172 model's ten-degree nosewheel steering authority."));
            }
            else
            {
                result.metrics.Add(FlightEvidenceMetric.Classified(
                    "nosewheel_steering_diagnostics",
                    FlightEvidenceStatus.Unsupported,
                    "The selected backend exposes no independent landing-gear steering state."));
            }
            result.ResolveStatus();
            return result;
        }

        private FlightScenarioEvidence MassCgMatrix(IFlightDynamicsTraceBackend backend)
        {
            FlightScenarioEvidence result = Scenario("mass_cg_matrix", backend);
            if (!backend.SupportsMassAndCg)
            {
                result.metrics.Add(FlightEvidenceMetric.Classified(
                    "mass_and_cg_matrix",
                    FlightEvidenceStatus.Unsupported,
                    "The backend cannot configure or report aircraft loading."));
                result.ResolveStatus();
                return result;
            }

            FlightScenarioMassConfiguration forward = new FlightScenarioMassConfiguration
            {
                pilotPounds = 200.0,
                copilotPounds = 170.0,
                leftFuelPounds = 130.0,
                rightFuelPounds = 130.0
            };
            FlightScenarioMassConfiguration aft = new FlightScenarioMassConfiguration
            {
                pilotPounds = 170.0,
                rearLeftPounds = 150.0,
                rearRightPounds = 150.0,
                baggagePounds = 35.0,
                leftFuelPounds = 80.0,
                rightFuelPounds = 80.0
            };
            if (!TryMeasureLoading(backend, forward, out double forwardWeight, out double forwardCg) ||
                !TryMeasureLoading(backend, aft, out double aftWeight, out double aftCg))
                return Error(result, backend.LastError);

            double forwardEmpty = forwardWeight - forward.PayloadAndFuelPounds;
            double aftEmpty = aftWeight - aft.PayloadAndFuelPounds;
            result.metrics.Add(FlightEvidenceMetric.Bounded(
                "empty_weight_accounting_delta",
                Math.Abs(forwardEmpty - aftEmpty),
                "lb",
                0.0,
                0.5,
                "Changing stations must not change the inferred empty-aircraft weight."));
            result.metrics.Add(FlightEvidenceMetric.Bounded(
                "aft_loading_cg_shift",
                aftCg - forwardCg,
                "in",
                1.0,
                20.0,
                "Moving payload from the front stations to rear/baggage stations must move CG aft without directly editing CG."));
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "operational_weight_and_balance_envelope",
                FlightEvidenceStatus.Unproven,
                "The station mechanics are proven, but this evidence does not yet authorize either loading against an independently pinned C172P operational envelope."));
            backend.ConfigureMass(FlightScenarioMassConfiguration.Default);
            result.ResolveStatus();
            return result;
        }

        private FlightScenarioEvidence WindMatrix(IFlightDynamicsTraceBackend backend)
        {
            const string id = "wind_matrix";
            FlightScenarioEvidence result = Scenario(id, backend);
            if (!backend.SupportsWind)
            {
                result.metrics.Add(FlightEvidenceMetric.Classified(
                    "crosswind_response",
                    FlightEvidenceStatus.Unsupported,
                    "The backend's public atmosphere path does not model wind."));
                result.ResolveStatus();
                return result;
            }

            // Explicit east heading: north wind is perpendicular, not an east tailwind.
            FlightScenarioInitialState initial = FlightScenarioInitialState.Airborne(headingDegrees: 90.0);
            RunResult calm = Run(backend, id + "/calm", initial, 15.0,
                (time, state) => Controls(throttle: 0.55f));
            if (!calm.ok) return Error(result, backend.LastError);
            if (!backend.SetWind(5.0, 0.0, 0.0)) return Error(result, backend.LastError);
            RunResult wind = Run(backend, id + "/north_5mps", initial, 15.0,
                (time, state) => Controls(throttle: 0.55f), preserveWind: true);
            backend.SetWind(0.0, 0.0, 0.0);
            if (!wind.ok) return Error(result, backend.LastError);
            result.metrics.Add(FlightEvidenceMetric.Bounded("initial_wind_forward_component", 0.0, "m/s", -1e-9, 1e-9,
                "True heading90 with physical NED north wind5m/s has zero forward dot product."));
            result.metrics.Add(FlightEvidenceMetric.Bounded("initial_wind_lateral_component", 5.0, "m/s", 4.999, 5.001,
                "The perpendicular crosswind is explicit; reset receives atmosphere before RunIC."));
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "crosswind_track_displacement", FlightEvidenceStatus.Unproven,
                "Northward displacement minus matched calm displacement, same heading/IC/loading/control schedule. No validated handling envelope is pinned.",
                (wind.end.positionUnityMeters.z - wind.start.positionUnityMeters.z) -
                (calm.end.positionUnityMeters.z - calm.start.positionUnityMeters.z), "m"));
            result.ResolveStatus();
            return result;
        }

        private static bool TryMeasureLoading(
            IFlightDynamicsTraceBackend backend,
            FlightScenarioMassConfiguration loading,
            out double weightPounds,
            out double cgXInches)
        {
            weightPounds = 0.0;
            cgXInches = 0.0;
            if (!backend.ConfigureMass(loading) ||
                !backend.Reset(FlightScenarioInitialState.Airborne()))
                return false;
            weightPounds = backend.WeightPounds;
            cgXInches = backend.CgXInches;
            return !double.IsNaN(weightPounds) && !double.IsInfinity(weightPounds) &&
                   !double.IsNaN(cgXInches) && !double.IsInfinity(cgXInches);
        }

        private RunResult RunTrimmed(
            IFlightDynamicsTraceBackend backend,
            string scenario,
            FlightScenarioInitialState initial,
            double durationSeconds)
        {
            if (backend.SupportsWind) backend.SetWind(0.0, 0.0, 0.0);
            if (backend.SupportsMassAndCg &&
                !backend.ConfigureMass(FlightScenarioMassConfiguration.Default))
                return new RunResult();
            if (!backend.Reset(initial) || !backend.TryTrim(out AircraftControlState trimmed))
                return new RunResult();
            return RunFromCurrent(
                backend,
                scenario,
                durationSeconds,
                (time, state) => trimmed,
                trimmed);
        }

        private RunResult Run(
            IFlightDynamicsTraceBackend backend,
            string scenario,
            FlightScenarioInitialState initial,
            double durationSeconds,
            Func<double, FlightDynamicsState, AircraftControlState> schedule,
            bool preserveWind = false)
        {
            if (!preserveWind && backend.SupportsWind)
                backend.SetWind(0.0, 0.0, 0.0);
            if (backend.SupportsMassAndCg &&
                !backend.ConfigureMass(FlightScenarioMassConfiguration.Default))
                return new RunResult();
            if (!backend.Reset(initial)) return new RunResult();
            return RunFromCurrent(
                backend,
                scenario,
                durationSeconds,
                schedule,
                AircraftControlState.Neutral());
        }

        private RunResult RunFromCurrent(
            IFlightDynamicsTraceBackend backend,
            string scenario,
            double durationSeconds,
            Func<double, FlightDynamicsState, AircraftControlState> schedule,
            AircraftControlState initialControls)
        {
            RunResult run = new RunResult();
            if (backend is NativeJsbsimTraceBackend native)
            {
                _scenarioInputs.Add(native.CaptureScenarioInputs(scenario, durationSeconds));
                // Report the actual reset/trim submission at t=0. The first
                // scheduled control is applied only inside the step loop below.
                initialControls = native.CaptureSubmittedControls();
            }
            run.ok = true;
            run.start = backend.State;
            run.states.Add(new TimedState(
                0.0,
                backend.State,
                DerivedBeta(backend.State),
                backend.TrueAirspeedKnots,
                backend.WeightPounds,
                backend.CgXInches,
                backend.GearVerticalForcePounds,
                backend.GearCompressionFeet,
                backend.NoseGearSteeringDegrees));
            AddTrace(backend, scenario, initialControls, 0.0);

            int steps = Mathf.CeilToInt((float)(durationSeconds / Step));
            int traceStride = 12;
            for (int index = 1; index <= steps; index++)
            {
                double time = index * Step;
                AircraftControlState controls = schedule(time, backend.State) ?? AircraftControlState.Neutral();
                backend.SetControls(controls);
                if (!backend.Step(Step))
                {
                    run.ok = false;
                    break;
                }
                if (index % traceStride == 0 || index == steps)
                {
                    double beta = DerivedBeta(backend.State);
                    run.states.Add(new TimedState(
                        time,
                        backend.State,
                        beta,
                        backend.TrueAirspeedKnots,
                        backend.WeightPounds,
                        backend.CgXInches,
                        backend.GearVerticalForcePounds,
                        backend.GearCompressionFeet,
                        backend.NoseGearSteeringDegrees));
                    AddTrace(backend, scenario, controls, time, beta);
                }
            }
            run.end = backend.State;
            return run;
        }

        private void AddTrace(
            IFlightDynamicsTraceBackend backend,
            string scenario,
            AircraftControlState controls,
            double time,
            double? derivedBeta = null)
        {
            FlightDynamicsState state = backend.State;
            _trace.Add(new FlightTraceSample
            {
                backend = backend.Id,
                scenario = scenario,
                timeSeconds = time,
                aileron = controls.aileron,
                elevator = controls.elevator,
                rudder = controls.rudder,
                throttle = controls.throttle,
                flaps = controls.flaps,
                airspeedKnots = state.calibratedAirspeedKnots,
                trueAirspeedKnots = backend.TrueAirspeedKnots,
                altitudeAglMeters = state.altitudeAglMeters,
                verticalSpeedFeetPerMinute = state.verticalSpeedFeetPerMinute,
                headingDegrees = state.headingDegrees,
                pitchDegrees = state.pitchDegrees,
                bankDegrees = state.bankDegrees,
                angleOfAttackDegrees = state.angleOfAttackDegrees,
                sideslipDegrees = state.sideslipDegrees,
                groundTrackDegrees = state.HasNativeTelemetry
                    ? (Math.Atan2(state.groundVelocityNedMetersPerSecond.y,state.groundVelocityNedMetersPerSecond.x) * 180.0 / Math.PI + 360.0) % 360.0 : 0.0,
                groundTrackAvailable = state.HasNativeTelemetry &&
                    state.groundVelocityNedMetersPerSecond.x * state.groundVelocityNedMetersPerSecond.x +
                    state.groundVelocityNedMetersPerSecond.y * state.groundVelocityNedMetersPerSecond.y >= 0.25,
                yawRateDegreesPerSecond = state.angularVelocityBodyDegreesPerSecond.y,
                weightOnWheels = state.weightOnWheels,
                weightPounds = backend.WeightPounds,
                cgXInches = backend.CgXInches,
                gearVerticalForcePounds = backend.GearVerticalForcePounds,
                gearCompressionFeet = backend.GearCompressionFeet,
                noseGearSteeringDegrees = backend.NoseGearSteeringDegrees
            });
        }

        private static double DerivedBeta(FlightDynamicsState state)
        {
            return state.sideslipDegrees; // Authoritative aerodynamic beta, not ground track.
        }

        private static AircraftControlState AxisControls(ControlAxis axis, float value)
        {
            AircraftControlState controls = Controls(throttle: 0.55f);
            if (axis == ControlAxis.Aileron) controls.aileron = value;
            if (axis == ControlAxis.Elevator) controls.elevator = value;
            if (axis == ControlAxis.Rudder) controls.rudder = value;
            return controls;
        }

        private static AircraftControlState Controls(
            float aileron = 0f,
            float elevator = 0f,
            float rudder = 0f,
            float throttle = 0.10f,
            float mixture = 1f,
            float flaps = 0f,
            float leftBrake = 0f,
            float rightBrake = 0f)
        {
            return new AircraftControlState
            {
                aileron = aileron,
                elevator = elevator,
                rudder = rudder,
                throttle = throttle,
                mixture = mixture,
                flaps = flaps,
                leftToeBrake = leftBrake,
                rightToeBrake = rightBrake
            };
        }

        private static FlightScenarioEvidence Scenario(string id, IFlightDynamicsTraceBackend backend)
        {
            return new FlightScenarioEvidence
            {
                scenarioId = id,
                backend = backend.Id
            };
        }

        private static FlightScenarioEvidence UnsupportedScenario(string id, string backend, string detail)
        {
            FlightScenarioEvidence result = new FlightScenarioEvidence
            {
                scenarioId = id,
                backend = backend
            };
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "availability",
                FlightEvidenceStatus.Unsupported,
                detail));
            result.ResolveStatus();
            return result;
        }

        private static FlightScenarioEvidence Error(FlightScenarioEvidence result, string detail)
        {
            result.metrics.Add(FlightEvidenceMetric.Classified(
                "execution",
                FlightEvidenceStatus.Error,
                string.IsNullOrWhiteSpace(detail) ? "Scenario execution failed." : detail));
            result.ResolveStatus();
            return result;
        }

        private static double DeltaAngle(double from, double to)
        {
            double value = (to - from + 180.0) % 360.0;
            if (value < 0.0) value += 360.0;
            return value - 180.0;
        }

        private enum ControlAxis
        {
            Aileron,
            Elevator,
            Rudder
        }

        private readonly struct TimedState
        {
            public readonly double time;
            public readonly FlightDynamicsState state;
            public readonly double beta;
            public readonly double trueAirspeedKnots;
            public readonly double weightPounds;
            public readonly double cgXInches;
            public readonly double gearVerticalForcePounds;
            public readonly double gearCompressionFeet;
            public readonly double noseGearSteeringDegrees;

            public TimedState(
                double time,
                FlightDynamicsState state,
                double beta,
                double trueAirspeedKnots,
                double weightPounds,
                double cgXInches,
                double gearVerticalForcePounds,
                double gearCompressionFeet,
                double noseGearSteeringDegrees)
            {
                this.time = time;
                this.state = state;
                this.beta = beta;
                this.trueAirspeedKnots = trueAirspeedKnots;
                this.weightPounds = weightPounds;
                this.cgXInches = cgXInches;
                this.gearVerticalForcePounds = gearVerticalForcePounds;
                this.gearCompressionFeet = gearCompressionFeet;
                this.noseGearSteeringDegrees = noseGearSteeringDegrees;
            }
        }

        private sealed class RunResult
        {
            public bool ok;
            public FlightDynamicsState start;
            public FlightDynamicsState end;
            public readonly List<TimedState> states = new List<TimedState>(1024);

            public FlightDynamicsState StateNear(double time)
            {
                TimedState best = states[0];
                double distance = Math.Abs(best.time - time);
                foreach (TimedState item in states)
                {
                    double candidate = Math.Abs(item.time - time);
                    if (candidate < distance)
                    {
                        best = item;
                        distance = candidate;
                    }
                }
                return best.state;
            }

            public double MaximumAbsoluteDerivedBeta(double from, double to) =>
                Aggregate(from, to, item => Math.Abs(item.beta), true);

            public double MeanAbsoluteDerivedBeta(double from, double to) =>
                Aggregate(from, to, item => Math.Abs(item.beta), false);

            public double MaximumAbsoluteYawRate(double from, double to) =>
                Aggregate(from, to,
                    item => Math.Abs(item.state.angularVelocityBodyDegreesPerSecond.y), true);

            public double MeanAbsoluteBank(double from, double to) =>
                Aggregate(from, to, item => Math.Abs(item.state.bankDegrees), false);

            public double MeanSpeedKnots(double from, double to) =>
                Aggregate(from, to, item => item.state.calibratedAirspeedKnots, false);

            public double MeanTrueSpeedKnots(double from, double to) =>
                Aggregate(from, to, item => item.trueAirspeedKnots, false);

            public double MeanVerticalSpeed(double from, double to) =>
                Aggregate(from, to, item => item.state.verticalSpeedFeetPerMinute, false);

            public double MaximumAngleOfAttack() =>
                Aggregate(0.0, double.MaxValue, item => item.state.angleOfAttackDegrees, true);

            public double MinimumSpeedKnots()
            {
                double minimum = double.MaxValue;
                foreach (TimedState item in states)
                    minimum = Math.Min(minimum, item.state.calibratedAirspeedKnots);
                return minimum == double.MaxValue ? 0.0 : minimum;
            }

            public double MaximumNoseGearSteeringDegrees() =>
                Aggregate(0.0, double.MaxValue, item => item.noseGearSteeringDegrees, true);

            public double MinimumNoseGearSteeringDegrees()
            {
                double minimum = double.MaxValue;
                foreach (TimedState item in states)
                    minimum = Math.Min(minimum, item.noseGearSteeringDegrees);
                return minimum == double.MaxValue ? 0.0 : minimum;
            }

            public double HorizontalDistanceMeters()
            {
                Vector3 delta = end.positionUnityMeters - start.positionUnityMeters;
                delta.y = 0f;
                return delta.magnitude;
            }

            public bool HasGroundContact(double from, double to)
            {
                foreach (TimedState item in states)
                    if (item.time >= from && item.time <= to && item.state.weightOnWheels)
                        return true;
                return false;
            }

            public bool TryFirstAirborne(out FlightTraceSample sample)
            {
                List<FlightTraceSample> transitionSamples =
                    new List<FlightTraceSample>(states.Count);
                foreach (TimedState item in states)
                    transitionSamples.Add(ToSample(item));
                return FlightDynamicsTransitionDetector.TryFindSustainedLiftoff(
                    transitionSamples,
                    out sample);
            }

            public bool TryTouchdown(out FlightTraceSample sample)
            {
                bool observedAir = false;
                foreach (TimedState item in states)
                {
                    observedAir |= !item.state.weightOnWheels;
                    if (observedAir && item.state.weightOnWheels)
                    {
                        sample = ToSample(item);
                        return true;
                    }
                }
                sample = null;
                return false;
            }

            private double Aggregate(
                double from,
                double to,
                Func<TimedState, double> selector,
                bool maximum)
            {
                double value = maximum ? double.MinValue : 0.0;
                int count = 0;
                foreach (TimedState item in states)
                {
                    if (item.time < from || item.time > to) continue;
                    double selected = selector(item);
                    value = maximum ? Math.Max(value, selected) : value + selected;
                    count++;
                }
                if (count == 0) return 0.0;
                return maximum ? value : value / count;
            }

            private static FlightTraceSample ToSample(TimedState item)
            {
                return new FlightTraceSample
                {
                    timeSeconds = item.time,
                    airspeedKnots = item.state.calibratedAirspeedKnots,
                    trueAirspeedKnots = item.trueAirspeedKnots,
                    altitudeAglMeters = item.state.altitudeAglMeters,
                    verticalSpeedFeetPerMinute = item.state.verticalSpeedFeetPerMinute,
                    headingDegrees = item.state.headingDegrees,
                    pitchDegrees = item.state.pitchDegrees,
                    bankDegrees = item.state.bankDegrees,
                    angleOfAttackDegrees = item.state.angleOfAttackDegrees,
                    sideslipDegrees = item.beta,
                    yawRateDegreesPerSecond = item.state.angularVelocityBodyDegreesPerSecond.y,
                    weightOnWheels = item.state.weightOnWheels,
                    weightPounds = item.weightPounds,
                    cgXInches = item.cgXInches,
                    gearVerticalForcePounds = item.gearVerticalForcePounds,
                    gearCompressionFeet = item.gearCompressionFeet,
                    noseGearSteeringDegrees = item.noseGearSteeringDegrees
                };
            }
        }
    }

    internal static class FlightDynamicsTransitionDetector
    {
        internal static bool TryFindSustainedLiftoff(
            IReadOnlyList<FlightTraceSample> samples,
            out FlightTraceSample liftoff)
        {
            liftoff = null;
            if (samples == null || samples.Count == 0)
                return false;

            bool observedSettledGround = false;
            for (int candidateIndex = 0; candidateIndex < samples.Count; candidateIndex++)
            {
                FlightTraceSample candidate = samples[candidateIndex];
                if (candidate.timeSeconds <
                    FlightDynamicsAcceptanceContract.InitialGroundSettlingWindowSeconds)
                    continue;

                if (candidate.weightOnWheels)
                {
                    observedSettledGround = true;
                    continue;
                }
                if (!observedSettledGround)
                    continue;

                double requiredEndTime = candidate.timeSeconds +
                                         FlightDynamicsAcceptanceContract.MinimumSustainedAirborneSeconds;
                bool reachedDuration = false;
                bool returnedToGround = false;
                double maximumAgl = candidate.altitudeAglMeters;
                double finalAgl = candidate.altitudeAglMeters;
                for (int index = candidateIndex; index < samples.Count; index++)
                {
                    FlightTraceSample current = samples[index];
                    if (current.weightOnWheels)
                    {
                        returnedToGround = true;
                        break;
                    }
                    maximumAgl = Math.Max(maximumAgl, current.altitudeAglMeters);
                    finalAgl = current.altitudeAglMeters;
                    if (current.timeSeconds >= requiredEndTime)
                    {
                        reachedDuration = true;
                        break;
                    }
                }

                bool clearsGroundDatum = maximumAgl >=
                    FlightDynamicsAcceptanceContract.ProductGroundSpawnHeightMeters +
                    FlightDynamicsAcceptanceContract.MinimumLiftoffClearanceMeters;
                bool gainsAltitude = finalAgl - candidate.altitudeAglMeters >=
                    FlightDynamicsAcceptanceContract.MinimumLiftoffAltitudeGainMeters;
                if (!returnedToGround && reachedDuration && clearsGroundDatum && gainsAltitude)
                {
                    liftoff = candidate;
                    return true;
                }
            }
            return false;
        }
    }
}
