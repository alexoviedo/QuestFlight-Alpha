using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using QuestFlightLab.Flight;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.FlightDynamicsValidation
{
    public enum FlightEvidenceStatus
    {
        Pass,
        Fail,
        Unsupported,
        Unproven,
        Error
    }

    [Serializable]
    public sealed class FlightEvidenceMetric
    {
        public string id = string.Empty;
        public string status = FlightEvidenceStatus.Unproven.ToString();
        public double value;
        public string unit = string.Empty;
        public double minimum;
        public double maximum;
        public string detail = string.Empty;

        public static FlightEvidenceMetric Bounded(
            string id,
            double value,
            string unit,
            double minimum,
            double maximum,
            string detail = "")
        {
            bool finite = !double.IsNaN(value) && !double.IsInfinity(value);
            return new FlightEvidenceMetric
            {
                id = id,
                status = finite && value >= minimum && value <= maximum
                    ? FlightEvidenceStatus.Pass.ToString()
                    : FlightEvidenceStatus.Fail.ToString(),
                value = value,
                unit = unit,
                minimum = minimum,
                maximum = maximum,
                detail = detail
            };
        }

        public static FlightEvidenceMetric Classified(
            string id,
            FlightEvidenceStatus status,
            string detail,
            double value = 0.0,
            string unit = "")
        {
            return new FlightEvidenceMetric
            {
                id = id,
                status = status.ToString(),
                value = value,
                unit = unit,
                detail = detail
            };
        }
    }

    [Serializable]
    public sealed class FlightScenarioEvidence
    {
        public string scenarioId = string.Empty;
        public string backend = string.Empty;
        public string status = FlightEvidenceStatus.Unproven.ToString();
        public string detail = string.Empty;
        public List<FlightEvidenceMetric> metrics = new List<FlightEvidenceMetric>();

        public void ResolveStatus()
        {
            bool anyPass = false;
            bool anyUnproven = false;
            bool anyUnsupported = false;
            bool anyFail = false;
            bool anyError = false;
            StringBuilder nonPass = new StringBuilder();
            foreach (FlightEvidenceMetric metric in metrics)
            {
                if (!Enum.TryParse(metric.status, out FlightEvidenceStatus value))
                    value = FlightEvidenceStatus.Error;
                anyError |= value == FlightEvidenceStatus.Error;
                anyFail |= value == FlightEvidenceStatus.Fail;
                anyPass |= value == FlightEvidenceStatus.Pass;
                anyUnproven |= value == FlightEvidenceStatus.Unproven;
                anyUnsupported |= value == FlightEvidenceStatus.Unsupported;
                if (value != FlightEvidenceStatus.Pass)
                {
                    if (nonPass.Length > 0) nonPass.Append(", ");
                    nonPass.Append(metric.id).Append(" (").Append(value).Append(')');
                }
            }

            FlightEvidenceStatus resolved = anyError
                ? FlightEvidenceStatus.Error
                : anyFail
                    ? FlightEvidenceStatus.Fail
                    : anyUnproven
                        ? FlightEvidenceStatus.Unproven
                        : anyUnsupported
                            ? FlightEvidenceStatus.Unsupported
                            : anyPass
                                ? FlightEvidenceStatus.Pass
                                : FlightEvidenceStatus.Unproven;
            status = resolved.ToString();
            if (string.IsNullOrWhiteSpace(detail))
            {
                detail = resolved == FlightEvidenceStatus.Pass
                    ? "All metrics with defined acceptance bounds passed."
                    : nonPass.Length > 0
                        ? "Non-pass evidence: " + nonPass + "."
                        : "No acceptance evidence was recorded.";
            }
        }
    }

    [Serializable]
    public sealed class FlightTraceSample
    {
        public string backend = string.Empty;
        public string scenario = string.Empty;
        public double timeSeconds;
        public float aileron;
        public float elevator;
        public float rudder;
        public float throttle;
        public float flaps;
        public double airspeedKnots;
        public double trueAirspeedKnots;
        public double altitudeAglMeters;
        public double verticalSpeedFeetPerMinute;
        public double headingDegrees;
        public double pitchDegrees;
        public double bankDegrees;
        public double angleOfAttackDegrees;
        public double sideslipDegrees;
        public double groundTrackDegrees;
        public bool groundTrackAvailable;
        public double yawRateDegreesPerSecond;
        public bool weightOnWheels;
        public double weightPounds;
        public double cgXInches;
        public double gearVerticalForcePounds;
        public double gearCompressionFeet;
        public double noseGearSteeringDegrees;
    }

    [Serializable]
    public sealed class ProductionFlightConfigEvidence
    {
        public string status = FlightEvidenceStatus.Unproven.ToString();
        public string assetPath = string.Empty;
        public string schemaVersion = FlightDynamicsAcceptanceContract.ContractVersion;
        public string hashAlgorithm = "SHA-256";
        public string effectiveParameterSha256 = string.Empty;
        public string parameterScope = "Unity UI/training-reference configuration; not native aircraft aerodynamic inputs.";
        public bool nestedGroupsSerialized;
        public bool duplicateLegacyFieldsDetected;
        public string detail = string.Empty;
    }

    [Serializable]
    public sealed class FlightDynamicsEvidenceSuite
    {
        public string contractVersion = FlightDynamicsAcceptanceContract.ContractVersion;
        public string generatedUtc = string.Empty;
        public double fixedStepSeconds = FlightDynamicsAcceptanceContract.FixedStepSeconds;
        public string scope = "Engineering regression evidence only; not certification, training-credit, or handling-fidelity approval.";
        public ProductionFlightConfigEvidence productionConfig = new ProductionFlightConfigEvidence();
        public NativeFlightInputEvidence nativeInputs = new NativeFlightInputEvidence();
        public List<FlightScenarioInputEvidence> scenarioInputs = new List<FlightScenarioInputEvidence>();
        public List<FlightScenarioEvidence> scenarios = new List<FlightScenarioEvidence>();

        public int CountStatus(FlightEvidenceStatus wanted)
        {
            int count = 0;
            foreach (FlightScenarioEvidence scenario in scenarios)
                if (string.Equals(scenario.status, wanted.ToString(), StringComparison.Ordinal))
                    count++;
            return count;
        }
    }

    public static class FlightDynamicsAcceptanceContract
    {
        public const string ContractVersion = "questflight-flight-dynamics-evidence-v3";
        public const double FixedStepSeconds = 1.0 / 120.0;
        // Matches FlightDynamicsInitialConditions.KbduRunway and the serialized
        // production spawn datum. This is CG/reference-point AGL, not an
        // assertion that every backend's gear is perfectly settled at reset.
        public const double ProductGroundSpawnHeightMeters = 1.25;
        public const double InitialGroundSettlingWindowSeconds = 2.0;
        public const double MinimumSustainedAirborneSeconds = 1.0;
        public const double MinimumLiftoffClearanceMeters = 0.30;
        public const double MinimumLiftoffAltitudeGainMeters = 0.15;
        public const double MinimumControlResponseDegrees = 0.25;
        public const double MaximumNeutralBankDriftDegrees = 10.0;
        public const double MaximumNeutralHeadingDriftDegrees = 15.0;
        public const double MaximumNeutralVerticalSpeedFpm = 900.0;
        public const double MaximumSideslipDampingRatio = 0.55;
        public const double MaximumCoordinatedTurnSideslipDegrees = 5.0;
        public const double MaximumTurnRateRelativeError = 0.40;
        public const double MinimumTakeoffSpeedKnots = 45.0;
        public const double MaximumTakeoffSpeedKnots = 70.0;
        public const double MinimumClimbRateFpm = 150.0;
        public const double MaximumTouchdownSinkRateFpm = 600.0;
    }

    public static class ProductionFlightConfigInspector
    {
        public const string ResourcePath = "C172StyleAircraftConfig";

        public static ProductionFlightConfigEvidence Inspect()
        {
            C172StyleAircraftConfig config = Resources.Load<C172StyleAircraftConfig>(ResourcePath);
            if (config == null)
            {
                return new ProductionFlightConfigEvidence
                {
                    status = FlightEvidenceStatus.Error.ToString(),
                    detail = "The production C172 configuration resource is missing."
                };
            }

            string assetPath = AssetDatabase.GetAssetPath(config);
            string yaml = File.Exists(assetPath) ? File.ReadAllText(assetPath) : string.Empty;
            string[] requiredNestedGroups =
            {
                "referenceSpeeds", "aero", "engine", "controls",
                "landingGear", "trainingTargets"
            };
            bool nestedSerialized = true;
            foreach (string group in requiredNestedGroups)
                nestedSerialized &= HasTopLevelSerializedField(yaml, group);

            // These names may also exist legitimately inside a nested parameter
            // group. Only fields directly below MonoBehaviour are legacy scalar
            // duplicates; substring matching would make a migrated asset
            // impossible to pass when a nested field shares the same name.
            bool duplicateLegacy =
                HasTopLevelSerializedField(yaml, "stallSpeedCleanKts") ||
                HasTopLevelSerializedField(yaml, "pitchRateMaxDegPerSec");
            string canonical = CanonicalEffectiveParameters(config);
            string hash = Sha256(canonical);
            bool pass = nestedSerialized && !duplicateLegacy && !string.IsNullOrWhiteSpace(hash);
            return new ProductionFlightConfigEvidence
            {
                status = pass ? FlightEvidenceStatus.Pass.ToString() : FlightEvidenceStatus.Fail.ToString(),
                assetPath = assetPath,
                effectiveParameterSha256 = hash,
                nestedGroupsSerialized = nestedSerialized,
                duplicateLegacyFieldsDetected = duplicateLegacy,
                detail = pass
                    ? "The active parameter groups are explicitly serialized and hashable."
                    : "The active model reads nested parameter groups, but the production YAML does not serialize the complete nested contract and/or retains duplicate legacy fields. The hash records effective runtime values; it does not promote the asset."
            };
        }

        internal static bool HasTopLevelSerializedField(string yaml, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(yaml) || string.IsNullOrWhiteSpace(fieldName))
                return false;

            bool insideMonoBehaviour = false;
            using (StringReader reader = new StringReader(yaml))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!insideMonoBehaviour)
                    {
                        if (string.Equals(line.Trim(), "MonoBehaviour:", StringComparison.Ordinal) &&
                            LeadingSpaces(line) == 0)
                            insideMonoBehaviour = true;
                        continue;
                    }

                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                        continue;
                    if (trimmed.StartsWith("---", StringComparison.Ordinal))
                        break;

                    int indentation = LeadingSpaces(line);
                    if (indentation == 0)
                        break;
                    if (indentation != 2)
                        continue;

                    int colon = trimmed.IndexOf(':');
                    if (colon <= 0)
                        continue;
                    string candidate = trimmed.Substring(0, colon).Trim();
                    if (string.Equals(candidate, fieldName, StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }

        private static int LeadingSpaces(string value)
        {
            int count = 0;
            while (count < value.Length && value[count] == ' ') count++;
            return count;
        }

        public static string CanonicalEffectiveParameters(C172StyleAircraftConfig config)
        {
            if (config == null) return string.Empty;
            StringBuilder text = new StringBuilder(2048);
            Append(text, "contract", FlightDynamicsAcceptanceContract.ContractVersion);
            Append(text, "massKg", config.massKg);
            Append(text, "wingAreaM2", config.wingAreaM2);
            Append(text, "wingSpanM", config.wingSpanM);
            Append(text, "aspectRatio", config.aspectRatio);
            C172ReferenceSpeeds s = config.Speeds;
            Append(text, "speeds.maxGrossWeightKg", s.maxGrossWeightKg);
            Append(text, "speeds.stallCleanKts", s.stallCleanKts);
            Append(text, "speeds.stallLandingKts", s.stallLandingKts);
            Append(text, "speeds.rotationKts", s.rotationKts);
            Append(text, "speeds.bestRateClimbKts", s.bestRateClimbKts);
            Append(text, "speeds.bestGlideKts", s.bestGlideKts);
            Append(text, "speeds.approachKts", s.approachKts);
            AeroCoefficients a = config.Aero;
            Append(text, "aero.airDensityKgM3", a.airDensityKgM3);
            Append(text, "aero.zeroLiftAoADeg", a.zeroLiftAoADeg);
            Append(text, "aero.liftCoefficientBase", a.liftCoefficientBase);
            Append(text, "aero.liftCurveSlopePerRad", a.liftCurveSlopePerRad);
            Append(text, "aero.maximumLiftCoefficientClean", a.maximumLiftCoefficientClean);
            Append(text, "aero.maximumLiftCoefficientLanding", a.maximumLiftCoefficientLanding);
            Append(text, "aero.stallBreakAoADeg", a.stallBreakAoADeg);
            Append(text, "aero.dragCoefficientBase", a.dragCoefficientBase);
            Append(text, "aero.inducedDragFactor", a.inducedDragFactor);
            Append(text, "aero.flapLiftBonus", a.flapLiftBonus);
            Append(text, "aero.flapDragBonus", a.flapDragBonus);
            Append(text, "aero.sideSlipDragFactor", a.sideSlipDragFactor);
            Append(text, "aero.sideForceCoefficientPerBetaRad", a.sideForceCoefficientPerBetaRad);
            EnginePropModelConfig e = config.Engine;
            Append(text, "engine.maxEnginePowerHp", e.maxEnginePowerHp);
            Append(text, "engine.propEfficiency", e.propEfficiency);
            Append(text, "engine.staticThrustNewtons", e.staticThrustNewtons);
            Append(text, "engine.maxThrustNewtons", e.maxThrustNewtons);
            Append(text, "engine.idleRpm", e.idleRpm);
            Append(text, "engine.maxRpm", e.maxRpm);
            ControlStabilityConfig c = config.Control;
            Append(text, "control.elevatorAoAAuthorityDeg", c.elevatorAoAAuthorityDeg);
            Append(text, "control.trimAoAAuthorityDeg", c.trimAoAAuthorityDeg);
            Append(text, "control.pitchRateMaxDegPerSec", c.pitchRateMaxDegPerSec);
            Append(text, "control.rollRateMaxDegPerSec", c.rollRateMaxDegPerSec);
            Append(text, "control.yawRateMaxDegPerSec", c.yawRateMaxDegPerSec);
            Append(text, "control.pitchDamping", c.pitchDamping);
            Append(text, "control.rollDamping", c.rollDamping);
            Append(text, "control.yawDamping", c.yawDamping);
            Append(text, "control.coordinatedTurnCoupling", c.coordinatedTurnCoupling);
            LandingGearConfig g = config.Gear;
            Append(text, "gear.runwayFrictionCoefficient", g.runwayFrictionCoefficient);
            Append(text, "gear.lateralGroundFriction", g.lateralGroundFriction);
            Append(text, "gear.brakeStrengthNewtons", g.brakeStrengthNewtons);
            Append(text, "gear.nosewheelSteerDegPerSec", g.nosewheelSteerDegPerSec);
            if (config.flapSettingsDeg != null)
                for (int i = 0; i < config.flapSettingsDeg.Length; i++)
                    Append(text, "flapSettingsDeg[" + i + "]", config.flapSettingsDeg[i]);
            return text.ToString();
        }

        private static void Append(StringBuilder text, string key, float value)
        {
            text.Append(key).Append('=').Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }

        private static void Append(StringBuilder text, string key, string value)
        {
            text.Append(key).Append('=').Append(value ?? string.Empty).Append('\n');
        }

        private static string Sha256(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                StringBuilder result = new StringBuilder(digest.Length * 2);
                foreach (byte item in digest) result.Append(item.ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }
    }
}
