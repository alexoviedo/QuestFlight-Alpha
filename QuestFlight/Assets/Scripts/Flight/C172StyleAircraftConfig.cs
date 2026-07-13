using UnityEngine;

namespace QuestFlightLab.Flight
{
    [System.Serializable]
    public class C172ReferenceSpeeds
    {
        public float maxGrossWeightLb = 2550f;
        public float maxGrossWeightKg = 1157f;
        public float stallCleanKts = 48f;
        public float stallLandingKts = 40f;
        public float rotationKts = 55f;
        public float bestAngleClimbKts = 62f;
        public float bestRateClimbKts = 74f;
        public float normalClimbKts = 79f;
        public float bestGlideKts = 68f;
        public float approachKts = 65f;
        public float maxFlapExtendedKts = 85f;
        public float neverExceedKts = 163f;
        public float cruiseKts = 105f;
    }

    [System.Serializable]
    public class AeroCoefficients
    {
        public float airDensityKgM3 = 1.0f;
        public float zeroLiftAoADeg = -2f;
        public float liftCoefficientBase = 0.44f;
        public float liftCurveSlopePerRad = 5.14f;
        public float maximumLiftCoefficientClean = 1.55f;
        public float maximumLiftCoefficientLanding = 1.92f;
        public float stallWarningAoADeg = 12f;
        public float stallBreakAoADeg = 15.5f;
        public float stallWarningSpeedMarginKts = 5f;
        public float postStallLiftMultiplier = 0.52f;
        public float dragCoefficientBase = 0.038f;
        public float inducedDragFactor = 0.058f;
        public float flapLiftBonus = 0.54f;
        public float flapDragBonus = 0.18f;
        public float sideSlipDragFactor = 0.048f;
    }

    [System.Serializable]
    public class EnginePropModelConfig
    {
        public float maxEnginePowerHp = 180f;
        public float propEfficiency = 0.74f;
        public float staticThrustNewtons = 3550f;
        public float maxThrustNewtons = 4050f;
        public float idleRpm = 700f;
        public float maxRpm = 2700f;
        public float carbHeatPowerLoss = 0.08f;
        public float minimumMixturePower = 0.35f;
        public float climbPowerScalar = 0.91f;
    }

    [System.Serializable]
    public class ControlStabilityConfig
    {
        public float elevatorAoAAuthorityDeg = 7.0f;
        public float trimAoAAuthorityDeg = 5.0f;
        public float pitchRateMaxDegPerSec = 36f;
        public float rollRateMaxDegPerSec = 62f;
        public float yawRateMaxDegPerSec = 27f;
        public float pitchDamping = 2.65f;
        public float rollDamping = 2.75f;
        public float yawDamping = 2.3f;
        public float pitchAttitudeStability = 1.0f;
        public float rollAttitudeStability = 0.5f;
        public float maxPrototypePitchDeg = 24f;
        public float maxPrototypeBankDeg = 58f;
        public float coordinatedTurnCoupling = 0.84f;
        public float flapPitchMomentDegPerSec = -7f;
        public float maxTrim = 1f;
    }

    [System.Serializable]
    public class LandingGearConfig
    {
        public float runwayFrictionCoefficient = 0.036f;
        public float lateralGroundFriction = 3.0f;
        public float brakeStrengthNewtons = 7000f;
        public float nosewheelSteerDegPerSec = 24f;
        public float groundEffectHeightM = 4f;
        public float centerlineWarningMeters = 8f;
        public float takeoffGroundRollTargetFt = 960f;
    }

    [System.Serializable]
    public class TrainingReferenceTargets
    {
        public float takeoffHeadingDeg = 78f;
        public float climbAirspeedToleranceKts = 10f;
        public float climbRateTargetFpm = 500f;
        public float minimumPositiveClimbFpm = 150f;
        public float shallowTurnBankTargetDeg = 15f;
        public float shallowTurnBankLimitDeg = 30f;
        public float stallRecoveryMinimumAltitudeFt = 1200f;
        public float patternHeadingChangeDeg = 70f;
        public float finalApproachTargetKts = 65f;
        public float finalApproachSpeedHighToleranceKts = 10f;
        public float finalApproachSpeedLowToleranceKts = 5f;
        public float stableApproachGateAglFt = 300f;
        public float approachDescentRateTargetFpm = -650f;
        public float approachDescentRateMinFpm = -1000f;
        public float approachDescentRateMaxFpm = -300f;
        public float finalApproachBankLimitDeg = 15f;
        public float glidePathTargetDeg = 3f;
        public float goAroundPitchTargetDeg = 7f;
        public float goAroundMinimumPositiveClimbFpm = 100f;
    }

    [CreateAssetMenu(menuName = "Quest Flight Lab/C172 Style Aircraft Config")]
    public class C172StyleAircraftConfig : ScriptableObject
    {
        [Header("v0.3 data-driven target groups")]
        public C172ReferenceSpeeds referenceSpeeds = new C172ReferenceSpeeds();
        public AeroCoefficients aero = new AeroCoefficients();
        public EnginePropModelConfig engine = new EnginePropModelConfig();
        public ControlStabilityConfig controls = new ControlStabilityConfig();
        public LandingGearConfig landingGear = new LandingGearConfig();
        public TrainingReferenceTargets trainingTargets = new TrainingReferenceTargets();

        [Header("Approximate C172-style reference values")]
        public float massKg = 1111f;
        public float wingAreaM2 = 16.2f;
        public float wingSpanM = 11.0f;
        public float aspectRatio = 7.45f;
        public float stallSpeedCleanKts = 48f;
        public float stallSpeedLandingKts = 40f;
        public float cruiseSpeedKts = 105f;
        public float bestAngleClimbSpeedKts = 62f;
        public float rotationSpeedKts = 55f;
        public float bestClimbSpeedKts = 74f;
        public float maxFlapExtendedSpeedKts = 85f;
        public float neverExceedSpeedKts = 163f;

        [Header("Prototype engine and propeller")]
        public float maxEnginePowerHp = 180f;
        public float propEfficiency = 0.74f;
        public float staticThrustNewtons = 3550f;
        public float maxThrustNewtons = 4050f;
        public float idleRpm = 700f;
        public float maxRpm = 2700f;
        public float carbHeatPowerLoss = 0.08f;
        public float minimumMixturePower = 0.35f;

        [Header("Prototype aerodynamics")]
        public float airDensityKgM3 = 1.0f;
        public float zeroLiftAoADeg = -2f;
        public float criticalAoADeg = 15f;
        public float liftCoefficientBase = 0.44f;
        public float liftCurveSlopePerRad = 5.14f;
        public float maximumLiftCoefficient = 1.50f;
        public float postStallLiftMultiplier = 0.55f;
        public float elevatorAoAAuthorityDeg = 7.5f;
        public float trimAoAAuthorityDeg = 4.5f;
        public float flapLiftBonus = 0.54f;
        public float dragCoefficientBase = 0.038f;
        public float inducedDragFactor = 0.058f;
        public float flapDragBonus = 0.18f;
        public float sideSlipDragFactor = 0.048f;

        [Header("Prototype controls and stability")]
        public float pitchRateMaxDegPerSec = 36f;
        public float rollRateMaxDegPerSec = 62f;
        public float yawRateMaxDegPerSec = 27f;
        public float pitchDamping = 2.65f;
        public float rollDamping = 2.75f;
        public float yawDamping = 2.3f;
        public float pitchAttitudeStability = 1.0f;
        public float rollAttitudeStability = 0.5f;
        public float maxPrototypePitchDeg = 24f;
        public float maxPrototypeBankDeg = 58f;
        public float coordinatedTurnCoupling = 0.84f;
        public float flapPitchMomentDegPerSec = -7f;

        [Header("Prototype flaps and trim")]
        public float[] flapSettingsDeg = { 0f, 10f, 20f, 30f };
        public float maxTrim = 1f;

        [Header("Prototype ground handling")]
        public float runwayFrictionCoefficient = 0.035f;
        public float lateralGroundFriction = 2.7f;
        public float brakeStrengthNewtons = 6500f;
        public float nosewheelSteerDegPerSec = 22f;
        public float groundEffectHeightM = 4f;

        public C172ReferenceSpeeds Speeds => referenceSpeeds ??= new C172ReferenceSpeeds();
        public AeroCoefficients Aero => aero ??= new AeroCoefficients();
        public EnginePropModelConfig Engine => engine ??= new EnginePropModelConfig();
        public ControlStabilityConfig Control => controls ??= new ControlStabilityConfig();
        public LandingGearConfig Gear => landingGear ??= new LandingGearConfig();
        public TrainingReferenceTargets Training => trainingTargets ??= new TrainingReferenceTargets();

        public static C172StyleAircraftConfig CreateRuntimeDefault()
        {
            C172StyleAircraftConfig config = CreateInstance<C172StyleAircraftConfig>();
            config.name = "C172StyleRuntimeDefault";
            config.massKg = config.Speeds.maxGrossWeightKg;
            config.wingAreaM2 = 16.2f;
            config.wingSpanM = 11.0f;
            config.aspectRatio = 7.45f;
            config.stallSpeedCleanKts = config.Speeds.stallCleanKts;
            config.stallSpeedLandingKts = config.Speeds.stallLandingKts;
            config.rotationSpeedKts = config.Speeds.rotationKts;
            config.bestAngleClimbSpeedKts = config.Speeds.bestAngleClimbKts;
            config.bestClimbSpeedKts = config.Speeds.bestRateClimbKts;
            config.maxFlapExtendedSpeedKts = config.Speeds.maxFlapExtendedKts;
            config.neverExceedSpeedKts = config.Speeds.neverExceedKts;
            return config;
        }
    }
}
