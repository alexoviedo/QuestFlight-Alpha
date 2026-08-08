#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace QuestFlight.Editor.FlightDynamicsValidation.Tests
{
    public sealed class FlightDynamicsValidationEditModeTests
    {
        private static readonly string[] RequiredScenarioIds =
        {
            "control_sign_pulse_doublet",
            "neutral_trim",
            "beta_yaw_damping",
            "coordinated_turn",
            "slow_flight_stall_recovery",
            "power_off_glide",
            "flap_response",
            "takeoff_climb",
            "approach_landing",
            "ground_steering",
            "mass_cg_matrix",
            "wind_matrix"
        };

        [Test]
        public void ContractUsesAnExact120HzFixedStepAndVersionedSchema()
        {
            Assert.That(FlightDynamicsAcceptanceContract.ContractVersion, Is.Not.Empty);
            Assert.That(
                FlightDynamicsAcceptanceContract.FixedStepSeconds,
                Is.EqualTo(1.0 / 120.0).Within(1e-12));
        }

        [Test]
        public void GroundScenarioUsesThePinnedProductionSpawnDatum()
        {
            FlightScenarioInitialState ground = FlightScenarioInitialState.Ground();
            Assert.That(
                ground.altitudeAglMeters,
                Is.EqualTo(FlightDynamicsAcceptanceContract.ProductGroundSpawnHeightMeters)
                    .Within(1e-12));
            Assert.That(
                ground.headingDegrees,
                Is.EqualTo(QuestFlightLab.Flight.Backends.FlightDynamicsInitialConditions
                    .KbduRunwayTrueHeadingDegrees).Within(1e-12));
        }

        [Test]
        public void PrototypeResetKeepsPitchAttitudeSeparateFromFlightPathAngle()
        {
            using (UnityPrototypeTraceBackend backend =
                   new UnityPrototypeTraceBackend())
            {
                FlightScenarioInitialState initial =
                    FlightScenarioInitialState.Airborne(
                        speedKnots: 75.0,
                        altitudeAglMeters: 500.0,
                        headingDegrees: 90.0,
                        flightPathAngleDegrees: 0.0);
                initial.pitchDegrees = 8.0;

                Assert.That(backend.Reset(initial), Is.True, backend.LastError);
                Assert.That(
                    backend.State.velocityUnityMetersPerSecond.y,
                    Is.EqualTo(0f).Within(0.001f),
                    "A nose-up recovery attitude must not invent a climb when the authored flight-path angle is level.");
                Assert.That(
                    backend.State.pitchDegrees,
                    Is.EqualTo(8.0).Within(0.05));
            }
        }

        [Test]
        public void LiftoffDetectorRejectsResetBounceAndRequiresSustainedClimbingSeparation()
        {
            List<FlightTraceSample> trace = new List<FlightTraceSample>
            {
                Transition(0.0, true, 1.25),
                Transition(0.2, false, 1.60), // Reset/gear-settling bounce: ignored.
                Transition(0.8, true, 1.25),
                Transition(2.0, true, 1.25), // Settled contact after the guard window.
                Transition(3.0, false, 1.26),
                Transition(3.5, false, 1.42),
                Transition(4.0, false, 1.70)
            };

            Assert.That(
                FlightDynamicsTransitionDetector.TryFindSustainedLiftoff(
                    trace,
                    out FlightTraceSample liftoff),
                Is.True);
            Assert.That(liftoff.timeSeconds, Is.EqualTo(3.0).Within(1e-12));

            List<FlightTraceSample> settlingOnly = new List<FlightTraceSample>
            {
                Transition(0.0, true, 1.25),
                Transition(2.0, true, 1.25),
                Transition(2.1, false, 1.65),
                Transition(2.5, true, 1.25),
                Transition(3.0, true, 1.25)
            };
            Assert.That(
                FlightDynamicsTransitionDetector.TryFindSustainedLiftoff(
                    settlingOnly,
                    out _),
                Is.False);
        }

        [Test]
        public void ProductionConfigHashIsDeterministicAndNeverMistakesSchemaDebtForPass()
        {
            ProductionFlightConfigEvidence first = ProductionFlightConfigInspector.Inspect();
            ProductionFlightConfigEvidence second = ProductionFlightConfigInspector.Inspect();

            Assert.That(first.effectiveParameterSha256, Is.Not.Empty);
            Assert.That(first.effectiveParameterSha256, Is.EqualTo(second.effectiveParameterSha256));
            Assert.That(first.schemaVersion, Is.EqualTo(FlightDynamicsAcceptanceContract.ContractVersion));
            if (!first.nestedGroupsSerialized || first.duplicateLegacyFieldsDetected)
                Assert.That(first.status, Is.EqualTo(FlightEvidenceStatus.Fail.ToString()));
        }

        [Test]
        public void ConfigYamlInspectionDistinguishesNestedFieldsFromLegacyTopLevelDuplicates()
        {
            const string nestedOnly =
                "MonoBehaviour:\n" +
                "  referenceSpeeds:\n" +
                "    stallSpeedCleanKts: 48\n" +
                "  controls:\n" +
                "    pitchRateMaxDegPerSec: 36\n";
            Assert.That(
                ProductionFlightConfigInspector.HasTopLevelSerializedField(
                    nestedOnly, "referenceSpeeds"),
                Is.True);
            Assert.That(
                ProductionFlightConfigInspector.HasTopLevelSerializedField(
                    nestedOnly, "stallSpeedCleanKts"),
                Is.False);
            Assert.That(
                ProductionFlightConfigInspector.HasTopLevelSerializedField(
                    nestedOnly, "pitchRateMaxDegPerSec"),
                Is.False);

            const string withLegacyDuplicates =
                nestedOnly +
                "  stallSpeedCleanKts: 48\n" +
                "  pitchRateMaxDegPerSec: 36\n";
            Assert.That(
                ProductionFlightConfigInspector.HasTopLevelSerializedField(
                    withLegacyDuplicates, "stallSpeedCleanKts"),
                Is.True);
            Assert.That(
                ProductionFlightConfigInspector.HasTopLevelSerializedField(
                    withLegacyDuplicates, "pitchRateMaxDegPerSec"),
                Is.True);
        }

        [Test]
        public void PrototypeSuiteIncludesEveryRequiredScenarioAndExplicitCapabilityStatuses()
        {
            FlightDynamicsScenarioSuiteRunner runner = new FlightDynamicsScenarioSuiteRunner();
            FlightDynamicsEvidenceSuite suite = runner.RunPrototypeOnly();
            Dictionary<string, FlightScenarioEvidence> scenarios = suite.scenarios
                .Where(item => item.backend == "UnityPrototype")
                .ToDictionary(item => item.scenarioId, item => item, StringComparer.Ordinal);

            CollectionAssert.IsSubsetOf(RequiredScenarioIds, scenarios.Keys);
            Assert.That(scenarios["mass_cg_matrix"].status,
                Is.EqualTo(FlightEvidenceStatus.Unsupported.ToString()));
            Assert.That(scenarios["wind_matrix"].status,
                Is.EqualTo(FlightEvidenceStatus.Unsupported.ToString()));
            Assert.That(
                scenarios["beta_yaw_damping"].metrics.Single(item => item.id == "reported_sideslip_unit_contract").status,
                Is.EqualTo(FlightEvidenceStatus.Pass.ToString()),
                "The prototype adapter must expose conventional sideslip beta in degrees rather than the legacy normalized slip-ball ratio.");
            Assert.That(runner.Trace.Count, Is.GreaterThan(0));
        }

        [Test]
        public void ScenarioStatusResolutionDoesNotPromoteUnsupportedOrUnprovenEvidence()
        {
            FlightScenarioEvidence unsupported = new FlightScenarioEvidence();
            unsupported.metrics.Add(FlightEvidenceMetric.Classified(
                "capability", FlightEvidenceStatus.Unsupported, "Not exposed."));
            unsupported.ResolveStatus();
            Assert.That(unsupported.status, Is.EqualTo(FlightEvidenceStatus.Unsupported.ToString()));

            FlightScenarioEvidence unproven = new FlightScenarioEvidence();
            unproven.metrics.Add(FlightEvidenceMetric.Classified(
                "trace", FlightEvidenceStatus.Unproven, "Recorded without an acceptance envelope."));
            unproven.ResolveStatus();
            Assert.That(unproven.status, Is.EqualTo(FlightEvidenceStatus.Unproven.ToString()));

            FlightScenarioEvidence partial = new FlightScenarioEvidence();
            partial.metrics.Add(FlightEvidenceMetric.Classified(
                "bounded", FlightEvidenceStatus.Pass, "Passed bounded observation."));
            partial.metrics.Add(FlightEvidenceMetric.Classified(
                "physical_contact", FlightEvidenceStatus.Unsupported, "Not exposed."));
            partial.ResolveStatus();
            Assert.That(partial.status, Is.EqualTo(FlightEvidenceStatus.Unsupported.ToString()),
                "A passing sub-metric must not promote a scenario with an unsupported required capability.");
        }

        [Test]
        public void EvidenceWriterProducesParseableJsonAndTraceWithoutChangingScenarioTruth()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "questflight_flight_dynamics_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                FlightDynamicsEvidenceSuite suite = new FlightDynamicsEvidenceSuite();
                FlightScenarioEvidence scenario = new FlightScenarioEvidence
                {
                    backend = "TestBackend",
                    scenarioId = "unsupported_example"
                };
                scenario.metrics.Add(FlightEvidenceMetric.Classified(
                    "capability", FlightEvidenceStatus.Unsupported, "Deliberate test."));
                scenario.ResolveStatus();
                suite.scenarios.Add(scenario);
                FlightDynamicsEvidenceBatch.WriteSuite(
                    directory,
                    suite,
                    new List<FlightTraceSample>
                    {
                        new FlightTraceSample
                        {
                            backend = "TestBackend",
                            scenario = "unsupported_example",
                            timeSeconds = 0.0
                        }
                    });

                string jsonPath = Path.Combine(directory, "flight_dynamics_evidence.json");
                Assert.That(File.Exists(jsonPath), Is.True);
                FlightDynamicsEvidenceSuite restored =
                    JsonUtility.FromJson<FlightDynamicsEvidenceSuite>(File.ReadAllText(jsonPath));
                Assert.That(restored.contractVersion, Is.EqualTo(FlightDynamicsAcceptanceContract.ContractVersion));
                Assert.That(restored.scenarios.Single().status,
                    Is.EqualTo(FlightEvidenceStatus.Unsupported.ToString()));
                Assert.That(File.Exists(Path.Combine(directory, "flight_dynamics_trace.csv")), Is.True);
                Assert.That(File.Exists(Path.Combine(directory, "flight_dynamics_summary.md")), Is.True);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static FlightTraceSample Transition(
            double timeSeconds,
            bool weightOnWheels,
            double altitudeAglMeters)
        {
            return new FlightTraceSample
            {
                timeSeconds = timeSeconds,
                weightOnWheels = weightOnWheels,
                altitudeAglMeters = altitudeAglMeters
            };
        }
    }
}
#endif
