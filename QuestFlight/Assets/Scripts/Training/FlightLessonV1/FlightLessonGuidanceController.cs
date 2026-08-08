using UnityEngine;

namespace QuestFlightLab.Training.FlightLessonV1
{
    /// <summary>
    /// Backend-neutral aircraft state in conventional aviation signs: heading increases
    /// clockwise, bank/roll rate are positive right-wing-down, pitch/pitch rate are
    /// positive nose-up, and yaw rate/control are positive nose-right. Backend adapters
    /// must normalize their native axes before supplying this value.
    /// </summary>
    public struct FlightLessonGuidanceAircraftState
    {
        public float headingDegreesTrue;
        public float bankDegrees;
        public float pitchDegrees;
        public float rollRateDegreesPerSecond;
        public float pitchRateDegreesPerSecond;
        public float yawRateDegreesPerSecond;
        public float altitudeMslFeet;
        public float verticalSpeedFeetPerMinute;
        public float calibratedAirspeedKnots;
        public float sideslipDegrees;

        public bool IsFinite =>
            Finite(headingDegreesTrue) && Finite(bankDegrees) && Finite(pitchDegrees) &&
            Finite(rollRateDegreesPerSecond) && Finite(pitchRateDegreesPerSecond) &&
            Finite(yawRateDegreesPerSecond) && Finite(altitudeMslFeet) &&
            Finite(verticalSpeedFeetPerMinute) && Finite(calibratedAirspeedKnots) &&
            Finite(sideslipDegrees);

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public struct FlightLessonGuidanceControllerSettings
    {
        public float crossTrackToHeadingDegreesPerMeter;
        public float maximumCrossTrackHeadingCorrectionDegrees;
        public float headingErrorToBankGain;
        public float absoluteMaximumBankDegrees;
        public float bankErrorToRollRateGain;
        public float maximumRollRateDegreesPerSecond;
        public float rollRateProportionalGain;
        public float rollRateIntegralGain;
        public float rollIntegralLimit;

        public float altitudeErrorToVerticalSpeedGain;
        public float verticalSpeedErrorToPitchGain;
        public float minimumPitchDegrees;
        public float maximumPitchDegrees;
        public float pitchErrorToPitchRateGain;
        public float maximumPitchRateDegreesPerSecond;
        public float pitchRateProportionalGain;
        public float pitchRateIntegralGain;
        public float pitchIntegralLimit;

        public float cruiseThrottle;
        public float airspeedProportionalGain;
        public float airspeedIntegralGain;
        public float throttleIntegralLimit;
        public float sideslipToRudderGain;
        public float yawRateDampingGain;

        public static FlightLessonGuidanceControllerSettings Default => new FlightLessonGuidanceControllerSettings
        {
            crossTrackToHeadingDegreesPerMeter = 0.035f,
            maximumCrossTrackHeadingCorrectionDegrees = 18f,
            headingErrorToBankGain = 0.45f,
            absoluteMaximumBankDegrees = 25f,
            bankErrorToRollRateGain = 1.4f,
            maximumRollRateDegreesPerSecond = 24f,
            rollRateProportionalGain = 0.045f,
            rollRateIntegralGain = 0.010f,
            rollIntegralLimit = 0.35f,

            altitudeErrorToVerticalSpeedGain = 1.15f,
            verticalSpeedErrorToPitchGain = 0.005f,
            minimumPitchDegrees = -6f,
            maximumPitchDegrees = 10f,
            pitchErrorToPitchRateGain = 1.5f,
            maximumPitchRateDegreesPerSecond = 15f,
            pitchRateProportionalGain = 0.055f,
            pitchRateIntegralGain = 0.008f,
            pitchIntegralLimit = 0.30f,

            cruiseThrottle = 0.62f,
            airspeedProportionalGain = 0.018f,
            airspeedIntegralGain = 0.004f,
            throttleIntegralLimit = 0.30f,
            sideslipToRudderGain = 0.08f,
            yawRateDampingGain = 0.025f
        };
    }

    public readonly struct FlightLessonGuidanceControllerDiagnostics
    {
        public readonly float targetBankDegrees;
        public readonly float targetRollRateDegreesPerSecond;
        public readonly float targetVerticalSpeedFeetPerMinute;
        public readonly float targetPitchDegrees;
        public readonly float targetPitchRateDegreesPerSecond;
        public readonly float rollIntegral;
        public readonly float pitchIntegral;
        public readonly float throttleIntegral;

        public FlightLessonGuidanceControllerDiagnostics(
            float targetBankDegrees,
            float targetRollRateDegreesPerSecond,
            float targetVerticalSpeedFeetPerMinute,
            float targetPitchDegrees,
            float targetPitchRateDegreesPerSecond,
            float rollIntegral,
            float pitchIntegral,
            float throttleIntegral)
        {
            this.targetBankDegrees = targetBankDegrees;
            this.targetRollRateDegreesPerSecond = targetRollRateDegreesPerSecond;
            this.targetVerticalSpeedFeetPerMinute = targetVerticalSpeedFeetPerMinute;
            this.targetPitchDegrees = targetPitchDegrees;
            this.targetPitchRateDegreesPerSecond = targetPitchRateDegreesPerSecond;
            this.rollIntegral = rollIntegral;
            this.pitchIntegral = pitchIntegral;
            this.throttleIntegral = throttleIntegral;
        }
    }

    /// <summary>
    /// Deterministic bounded cascaded controller. It produces an intent-level command only;
    /// it never writes transforms or bypasses the product's single flight authority.
    /// </summary>
    public sealed class FlightLessonGuidanceController
    {
        private float rollIntegral;
        private float pitchIntegral;
        private float throttleIntegral;

        public FlightLessonGuidanceControllerDiagnostics Diagnostics { get; private set; }

        public void Reset()
        {
            rollIntegral = 0f;
            pitchIntegral = 0f;
            throttleIntegral = 0f;
            Diagnostics = default;
        }

        public bool TryStep(
            in FlightLessonGuidanceIntent intent,
            in FlightLessonGuidanceAircraftState state,
            float deltaTimeSeconds,
            in FlightLessonGuidanceControllerSettings settings,
            out FlightLessonControlCommand command)
        {
            if (!IntentIsValid(intent) || !state.IsFinite || !Finite(deltaTimeSeconds) || deltaTimeSeconds <= 0f ||
                !SettingsAreValid(settings))
            {
                command = default;
                return false;
            }

            float crossTrackCorrection = Mathf.Clamp(
                intent.crossTrackErrorMeters * settings.crossTrackToHeadingDegreesPerMeter,
                -settings.maximumCrossTrackHeadingCorrectionDegrees,
                settings.maximumCrossTrackHeadingCorrectionDegrees);
            float correctedTrack = intent.desiredTrackDegreesTrue + crossTrackCorrection;
            float headingError = FlightLessonPathMath.HeadingErrorDegrees(correctedTrack, state.headingDegreesTrue);
            float permittedBank = Mathf.Min(
                Mathf.Abs(intent.maximumBankDegrees),
                settings.absoluteMaximumBankDegrees);
            float targetBank = Mathf.Clamp(
                headingError * settings.headingErrorToBankGain,
                -permittedBank,
                permittedBank);
            float targetRollRate = Mathf.Clamp(
                (targetBank - state.bankDegrees) * settings.bankErrorToRollRateGain,
                -settings.maximumRollRateDegreesPerSecond,
                settings.maximumRollRateDegreesPerSecond);
            float aileron = StepBoundedPi(
                targetRollRate - state.rollRateDegreesPerSecond,
                deltaTimeSeconds,
                settings.rollRateProportionalGain,
                settings.rollRateIntegralGain,
                settings.rollIntegralLimit,
                ref rollIntegral,
                -1f,
                1f);

            float maximumVerticalSpeed = Mathf.Max(1f, Mathf.Abs(intent.maximumClimbRateFeetPerMinute));
            float targetVerticalSpeed = Mathf.Clamp(
                (intent.targetAltitudeMslFeet - state.altitudeMslFeet) * settings.altitudeErrorToVerticalSpeedGain,
                -maximumVerticalSpeed,
                maximumVerticalSpeed);
            float targetPitch = Mathf.Clamp(
                (targetVerticalSpeed - state.verticalSpeedFeetPerMinute) * settings.verticalSpeedErrorToPitchGain,
                settings.minimumPitchDegrees,
                settings.maximumPitchDegrees);
            float targetPitchRate = Mathf.Clamp(
                (targetPitch - state.pitchDegrees) * settings.pitchErrorToPitchRateGain,
                -settings.maximumPitchRateDegreesPerSecond,
                settings.maximumPitchRateDegreesPerSecond);
            float elevator = StepBoundedPi(
                targetPitchRate - state.pitchRateDegreesPerSecond,
                deltaTimeSeconds,
                settings.pitchRateProportionalGain,
                settings.pitchRateIntegralGain,
                settings.pitchIntegralLimit,
                ref pitchIntegral,
                -1f,
                1f);

            float speedError = intent.targetCalibratedAirspeedKnots - state.calibratedAirspeedKnots;
            float throttleDelta = StepBoundedPi(
                speedError,
                deltaTimeSeconds,
                settings.airspeedProportionalGain,
                settings.airspeedIntegralGain,
                settings.throttleIntegralLimit,
                ref throttleIntegral,
                -settings.cruiseThrottle,
                1f - settings.cruiseThrottle);
            float throttle = Mathf.Clamp01(settings.cruiseThrottle + throttleDelta);

            float rudder = Mathf.Clamp(
                state.sideslipDegrees * settings.sideslipToRudderGain -
                state.yawRateDegreesPerSecond * settings.yawRateDampingGain,
                -1f,
                1f);

            command = new FlightLessonControlCommand
            {
                aileron = aileron,
                elevator = elevator,
                rudder = rudder,
                throttle = throttle,
                mixture = 1f,
                trim = 0f,
                flaps = 0f,
                leftToeBrake = 0f,
                rightToeBrake = 0f
            };
            Diagnostics = new FlightLessonGuidanceControllerDiagnostics(
                targetBank,
                targetRollRate,
                targetVerticalSpeed,
                targetPitch,
                targetPitchRate,
                rollIntegral,
                pitchIntegral,
                throttleIntegral);
            return true;
        }

        private static float StepBoundedPi(
            float error,
            float dt,
            float proportionalGain,
            float integralGain,
            float integralLimit,
            ref float integral,
            float minimumOutput,
            float maximumOutput)
        {
            float proportional = error * proportionalGain;
            float candidateIntegral = Mathf.Clamp(
                integral + error * integralGain * dt,
                -integralLimit,
                integralLimit);
            float candidateOutput = proportional + candidateIntegral;
            bool saturatingHigh = candidateOutput > maximumOutput && error > 0f;
            bool saturatingLow = candidateOutput < minimumOutput && error < 0f;
            if (!saturatingHigh && !saturatingLow)
            {
                integral = candidateIntegral;
            }

            return Mathf.Clamp(proportional + integral, minimumOutput, maximumOutput);
        }

        private static bool SettingsAreValid(in FlightLessonGuidanceControllerSettings settings)
        {
            return Finite(settings.crossTrackToHeadingDegreesPerMeter) &&
                settings.maximumCrossTrackHeadingCorrectionDegrees >= 0f &&
                settings.headingErrorToBankGain >= 0f && settings.absoluteMaximumBankDegrees > 0f &&
                settings.bankErrorToRollRateGain >= 0f && settings.maximumRollRateDegreesPerSecond > 0f &&
                settings.rollRateProportionalGain >= 0f && settings.rollRateIntegralGain >= 0f &&
                settings.rollIntegralLimit >= 0f && settings.altitudeErrorToVerticalSpeedGain >= 0f &&
                settings.verticalSpeedErrorToPitchGain >= 0f &&
                settings.minimumPitchDegrees < settings.maximumPitchDegrees &&
                settings.pitchErrorToPitchRateGain >= 0f && settings.maximumPitchRateDegreesPerSecond > 0f &&
                settings.pitchRateProportionalGain >= 0f && settings.pitchRateIntegralGain >= 0f &&
                settings.pitchIntegralLimit >= 0f && settings.cruiseThrottle >= 0f && settings.cruiseThrottle <= 1f &&
                settings.airspeedProportionalGain >= 0f && settings.airspeedIntegralGain >= 0f &&
                settings.throttleIntegralLimit >= 0f && settings.sideslipToRudderGain >= 0f &&
                settings.yawRateDampingGain >= 0f;
        }

        private static bool IntentIsValid(in FlightLessonGuidanceIntent intent)
        {
            return Finite(intent.lookAheadPointUnityMeters.x) &&
                Finite(intent.lookAheadPointUnityMeters.y) &&
                Finite(intent.lookAheadPointUnityMeters.z) &&
                Finite(intent.desiredTrackDegreesTrue) && Finite(intent.crossTrackErrorMeters) &&
                Finite(intent.targetAltitudeMslFeet) && Finite(intent.targetCalibratedAirspeedKnots) &&
                Finite(intent.maximumBankDegrees) && intent.maximumBankDegrees > 0f &&
                Finite(intent.maximumClimbRateFeetPerMinute) && intent.maximumClimbRateFeetPerMinute > 0f;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
