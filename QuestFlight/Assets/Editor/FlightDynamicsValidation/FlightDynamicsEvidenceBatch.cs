using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.FlightDynamicsValidation
{
    public static class FlightDynamicsEvidenceBatch
    {
        public const string ArtifactEnvironmentVariable = "QUESTFLIGHT_FLIGHT_DYNAMICS_ARTIFACT_DIR";

        [MenuItem("QuestFlight/Validation/Flight Dynamics Evidence (Native JSBSim)")]
        public static void RunBatch()
        {
            string artifactDirectory = ResolveArtifactDirectory();
            Directory.CreateDirectory(artifactDirectory);

            FlightDynamicsScenarioSuiteRunner runner = new FlightDynamicsScenarioSuiteRunner();
            FlightDynamicsEvidenceSuite suite = runner.RunNativeOnly();
            WriteSuite(artifactDirectory, suite, runner.Trace);

            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "Flight-dynamics evidence written to {0}. Scenarios: {1}; pass: {2}; fail: {3}; unsupported: {4}; unproven: {5}; error: {6}.",
                artifactDirectory,
                suite.scenarios.Count,
                suite.CountStatus(FlightEvidenceStatus.Pass),
                suite.CountStatus(FlightEvidenceStatus.Fail),
                suite.CountStatus(FlightEvidenceStatus.Unsupported),
                suite.CountStatus(FlightEvidenceStatus.Unproven),
                suite.CountStatus(FlightEvidenceStatus.Error)));
        }

        public static void WriteSuite(
            string artifactDirectory,
            FlightDynamicsEvidenceSuite suite,
            IReadOnlyList<FlightTraceSample> trace)
        {
            if (suite == null) throw new ArgumentNullException(nameof(suite));
            if (trace == null) throw new ArgumentNullException(nameof(trace));
            if (string.IsNullOrWhiteSpace(artifactDirectory))
                throw new ArgumentException("An external artifact directory is required.", nameof(artifactDirectory));

            Directory.CreateDirectory(artifactDirectory);
            File.WriteAllText(
                Path.Combine(artifactDirectory, "flight_dynamics_evidence.json"),
                JsonUtility.ToJson(suite, true),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(artifactDirectory, "flight_dynamics_trace.csv"),
                BuildCsv(trace),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(artifactDirectory, "flight_dynamics_summary.md"),
                BuildMarkdown(suite),
                new UTF8Encoding(false));
        }

        private static string ResolveArtifactDirectory()
        {
            string configured = Environment.GetEnvironmentVariable(ArtifactEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configured))
                return Path.GetFullPath(configured);

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string parent = Directory.GetParent(projectRoot)?.FullName ?? Path.GetTempPath();
            return Path.Combine(
                parent,
                "T2-QuestFlight-product-artifacts",
                "flight_dynamics_validation_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        }

        private static string BuildCsv(IReadOnlyList<FlightTraceSample> trace)
        {
            StringBuilder csv = new StringBuilder(Math.Max(2048, trace.Count * 180));
            csv.AppendLine("backend,scenario,time_s,aileron,elevator,rudder,throttle,flaps,calibrated_airspeed_kt,true_airspeed_kt,altitude_agl_m,vertical_speed_fpm,heading_deg,pitch_deg,bank_deg,aoa_deg,beta_deg,yaw_rate_deg_s,weight_on_wheels,weight_lb,cg_x_in,gear_vertical_force_lb,gear_compression_ft,nose_gear_steering_deg");
            foreach (FlightTraceSample sample in trace)
            {
                AppendCsv(csv, sample.backend);
                AppendCsv(csv, sample.scenario);
                AppendCsv(csv, sample.timeSeconds);
                AppendCsv(csv, sample.aileron);
                AppendCsv(csv, sample.elevator);
                AppendCsv(csv, sample.rudder);
                AppendCsv(csv, sample.throttle);
                AppendCsv(csv, sample.flaps);
                AppendCsv(csv, sample.airspeedKnots);
                AppendCsv(csv, sample.trueAirspeedKnots);
                AppendCsv(csv, sample.altitudeAglMeters);
                AppendCsv(csv, sample.verticalSpeedFeetPerMinute);
                AppendCsv(csv, sample.headingDegrees);
                AppendCsv(csv, sample.pitchDegrees);
                AppendCsv(csv, sample.bankDegrees);
                AppendCsv(csv, sample.angleOfAttackDegrees);
                AppendCsv(csv, sample.sideslipDegrees);
                AppendCsv(csv, sample.yawRateDegreesPerSecond);
                csv.Append(sample.weightOnWheels ? "true," : "false,");
                AppendCsv(csv, sample.weightPounds);
                AppendCsv(csv, sample.cgXInches);
                AppendCsv(csv, sample.gearVerticalForcePounds);
                AppendCsv(csv, sample.gearCompressionFeet);
                csv.Append(sample.noseGearSteeringDegrees.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
            }
            return csv.ToString();
        }

        private static void AppendCsv(StringBuilder csv, string value)
        {
            value = value ?? string.Empty;
            csv.Append('"').Append(value.Replace("\"", "\"\"")).Append("\",");
        }

        private static void AppendCsv(StringBuilder csv, double value)
        {
            csv.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(',');
        }

        private static void AppendCsv(StringBuilder csv, float value)
        {
            csv.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(',');
        }

        private static string BuildMarkdown(FlightDynamicsEvidenceSuite suite)
        {
            StringBuilder text = new StringBuilder(8192);
            text.AppendLine("# Flight-dynamics validation evidence");
            text.AppendLine();
            text.AppendLine("This is engineering regression evidence. It is not certification, training-credit, or handling-fidelity approval.");
            text.AppendLine();
            text.Append("- Contract: `").Append(suite.contractVersion).AppendLine("`");
            text.Append("- Fixed step: `").Append(suite.fixedStepSeconds.ToString("R", CultureInfo.InvariantCulture)).AppendLine(" s` (120 Hz)");
            text.Append("- Production config status: **").Append(suite.productionConfig.status).AppendLine("**");
            text.Append("- Effective config SHA-256: `").Append(suite.productionConfig.effectiveParameterSha256).AppendLine("`");
            text.AppendLine();
            text.AppendLine("| Backend | Scenario | Status | Detail |");
            text.AppendLine("|---|---|---:|---|");
            foreach (FlightScenarioEvidence scenario in suite.scenarios)
            {
                text.Append("| ").Append(EscapeMarkdown(scenario.backend))
                    .Append(" | ").Append(EscapeMarkdown(scenario.scenarioId))
                    .Append(" | **").Append(EscapeMarkdown(scenario.status))
                    .Append("** | ").Append(EscapeMarkdown(scenario.detail)).AppendLine(" |");
            }
            text.AppendLine();
            text.AppendLine("`Unsupported` means the backend has no exposed capability for that matrix. `Unproven` means a trace was recorded but no validated acceptance envelope exists. Neither is a pass.");
            return text.ToString();
        }

        private static string EscapeMarkdown(string value)
        {
            return (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
