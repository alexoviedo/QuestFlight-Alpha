using UnityEngine;
using QuestFlightLab.Aircraft.CockpitDynamics.Analog;

namespace QuestFlightLab.Aircraft.CockpitDynamics.V2
{
    /// <summary>
    /// Pure deterministic instrument calculations. Values are evaluated only
    /// when the snapshot explicitly declares the necessary authority.
    /// </summary>
    public static class CockpitInstrumentV2Kinematics
    {
        public const float StandardBarometricSettingInHg = 29.92f;
        public const float ApproximateFeetPerInHg = 1000f;

        // Shared with offline artwork; counterclockwise from instrument-right.
        public static float EvaluateTrainerAirspeedAngle(float knots) =>
            225f - Mathf.Clamp(knots, 0f, 180f) * (320f / 180f);

        public static float EvaluateTrainerVerticalSpeedAngle(float feetPerMinute) =>
            180f - Mathf.Clamp(feetPerMinute, -2000f, 2000f) * (160f / 2000f);

        /// <summary>
        /// Returns the world-stabilizing gyro-ball rotation in the instrument's
        /// local frame. Positive aircraft pitch and bank move the horizon in
        /// the opposite direction while the fixed miniature aircraft remains
        /// attached to the case.
        /// </summary>
        public static Quaternion EvaluateAttitudeBallRotation(
            float pitchDegrees,
            float bankDegrees,
            Vector3 pitchAxisLocal,
            Vector3 bankAxisLocal)
        {
            float pitch = IsFinite(pitchDegrees)
                ? Mathf.Clamp(pitchDegrees, -30f, 30f)
                : 0f;
            float bank = IsFinite(bankDegrees)
                ? SignedAngle(bankDegrees)
                : 0f;
            Quaternion pitchRotation = Quaternion.AngleAxis(
                -pitch, pitchAxisLocal.normalized);
            Quaternion bankRotation = Quaternion.AngleAxis(
                -bank, bankAxisLocal.normalized);
            return bankRotation * pitchRotation;
        }

        public static bool TryEvaluate(
            CockpitInstrumentV2Function function,
            in CockpitInstrumentV2Snapshot snapshot,
            out float value)
        {
            value = 0f;
            if (!snapshot.isValid)
                return false;

            switch (function)
            {
                case CockpitInstrumentV2Function.None:
                    return false;
                case CockpitInstrumentV2Function
                    .IndicatedAirspeedNeedleDegrees:
                    if (!snapshot.Has(CockpitInstrumentV2Availability.AirData) ||
                        !IsFinite(snapshot.indicatedAirspeedKnots))
                        return false;
                    value = EvaluateTrainerAirspeedAngle(
                        Mathf.Max(0f, snapshot.indicatedAirspeedKnots));
                    return true;
                case CockpitInstrumentV2Function
                    .IndicatedVerticalSpeedNeedleDegrees:
                    if (!snapshot.Has(CockpitInstrumentV2Availability.AirData) ||
                        !IsFinite(
                            snapshot.indicatedVerticalSpeedFeetPerMinute))
                        return false;
                    value =
                        EvaluateTrainerVerticalSpeedAngle(
                            snapshot.indicatedVerticalSpeedFeetPerMinute);
                    return true;
                case CockpitInstrumentV2Function.AltimeterHundredsNeedleDegrees:
                case CockpitInstrumentV2Function.AltimeterThousandsNeedleDegrees:
                case CockpitInstrumentV2Function.AltimeterTenThousandsNeedleDegrees:
                case CockpitInstrumentV2Function.AltimeterBarometricDrumInHg:
                    return TryEvaluateAltimeter(function, snapshot, out value);
                case CockpitInstrumentV2Function.AttitudeBankDegrees:
                    if (!snapshot.Has(CockpitInstrumentV2Availability.Attitude))
                        return false;
                    if (!IsFinite(snapshot.bankDegrees))
                        return false;
                    value = SignedAngle(snapshot.bankDegrees);
                    return true;
                case CockpitInstrumentV2Function.AttitudePitchDegrees:
                    if (!snapshot.Has(CockpitInstrumentV2Availability.Attitude))
                        return false;
                    if (!IsFinite(snapshot.pitchDegrees))
                        return false;
                    value = Mathf.Clamp(snapshot.pitchDegrees, -30f, 30f);
                    return true;
                case CockpitInstrumentV2Function.DirectionalGyroHeadingDegrees:
                    if (!snapshot.Has(CockpitInstrumentV2Availability.TrueHeading))
                        return false;
                    if (!IsFinite(snapshot.trueHeadingDegrees))
                        return false;
                    value = Heading(snapshot.trueHeadingDegrees);
                    return true;
                case CockpitInstrumentV2Function.MagneticCompassHeadingDegrees:
                    if (!snapshot.Has(
                        CockpitInstrumentV2Availability.TrueHeading |
                            CockpitInstrumentV2Availability.MagneticVariation))
                        return false;
                    if (!IsFinite(snapshot.trueHeadingDegrees) ||
                        !IsFinite(snapshot.magneticVariationEastDegrees))
                        return false;
                    // Aviation convention: east variation is subtracted from true.
                    value = Heading(
                        snapshot.trueHeadingDegrees -
                        snapshot.magneticVariationEastDegrees);
                    return true;
                case CockpitInstrumentV2Function.ActualFlapDegrees:
                    if (!snapshot.Has(
                            CockpitInstrumentV2Availability.ActualFlapPosition))
                        return false;
                    if (!IsFinite(snapshot.actualFlapDegrees))
                        return false;
                    value = Mathf.Clamp(snapshot.actualFlapDegrees, 0f, 40f);
                    return true;
                case CockpitInstrumentV2Function.AverageBrakePressurePsi:
                    if (!snapshot.Has(
                            CockpitInstrumentV2Availability.BrakeHydraulics))
                        return false;
                    if (!IsFinite(snapshot.leftBrakePressurePsi) ||
                        !IsFinite(snapshot.rightBrakePressurePsi))
                        return false;
                    value = 0.5f * (
                        Mathf.Max(0f, snapshot.leftBrakePressurePsi) +
                        Mathf.Max(0f, snapshot.rightBrakePressurePsi));
                    return true;
                case CockpitInstrumentV2Function.EngineRpmNeedleDegrees:
                    if (!snapshot.Has(CockpitInstrumentV2Availability.EngineRpm) ||
                        !IsFinite(snapshot.engineRpm))
                        return false;
                    // The represented C172-style tachometer is a 0-3000 RPM,
                    // 300-degree sweep. The product aircraft red line remains
                    // the separately authored 2700 RPM mark.
                    value = Mathf.Clamp(snapshot.engineRpm, 0f, 3000f) * 0.1f;
                    return true;
                case CockpitInstrumentV2Function.TurnCoordinatorBankDegrees:
                    if (!snapshot.Has(CockpitInstrumentV2Availability.TurnAndSlip) ||
                        !IsFinite(snapshot.turnRateDegreesPerSecond))
                        return false;
                    // A standard-rate turn (3 degrees/second) reaches the
                    // instrument's left/right reference mark.
                    value = Mathf.Clamp(
                        snapshot.turnRateDegreesPerSecond / 3f, -1f, 1f) * 20f;
                    return true;
                case CockpitInstrumentV2Function.SlipSkidNormalized:
                    if (!snapshot.Has(CockpitInstrumentV2Availability.TurnAndSlip) ||
                        !IsFinite(snapshot.slipSkidNormalized))
                        return false;
                    value = Mathf.Clamp(snapshot.slipSkidNormalized, -1f, 1f);
                    return true;
                case CockpitInstrumentV2Function.ClockHourHandDegrees:
                case CockpitInstrumentV2Function.ClockMinuteHandDegrees:
                case CockpitInstrumentV2Function.ClockSecondHandDegrees:
                    return TryEvaluateClock(function, snapshot, out value);
                default:
                    return false;
            }
        }

        private static bool TryEvaluateAltimeter(
            CockpitInstrumentV2Function function,
            in CockpitInstrumentV2Snapshot snapshot,
            out float value)
        {
            value = 0f;
            if (!snapshot.Has(
                    CockpitInstrumentV2Availability.AirData |
                    CockpitInstrumentV2Availability.BarometricSetting))
                return false;
            if (!IsFinite(snapshot.pressureAltitudeFeet) ||
                !IsFinite(snapshot.barometricSettingInHg))
                return false;
            if (snapshot.barometricSettingInHg < 27.5f ||
                snapshot.barometricSettingInHg > 31.5f)
                return false;

            if (!TryEvaluateIndicatedAltitudeFeet(snapshot, out float indicatedFeet))
                return false;
            switch (function)
            {
                case CockpitInstrumentV2Function.AltimeterHundredsNeedleDegrees:
                    value = CycleDegrees(indicatedFeet, 1000f);
                    return true;
                case CockpitInstrumentV2Function.AltimeterThousandsNeedleDegrees:
                    value = CycleDegrees(indicatedFeet, 10000f);
                    return true;
                case CockpitInstrumentV2Function.AltimeterTenThousandsNeedleDegrees:
                    value = CycleDegrees(indicatedFeet, 100000f);
                    return true;
                case CockpitInstrumentV2Function.AltimeterBarometricDrumInHg:
                    value = snapshot.barometricSettingInHg;
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryEvaluateIndicatedAltitudeFeet(
            in CockpitInstrumentV2Snapshot snapshot,
            out float indicatedFeet)
        {
            indicatedFeet = 0f;
            if (!snapshot.Has(
                    CockpitInstrumentV2Availability.AirData |
                    CockpitInstrumentV2Availability.BarometricSetting) ||
                !IsFinite(snapshot.pressureAltitudeFeet) ||
                !IsFinite(snapshot.barometricSettingInHg) ||
                snapshot.barometricSettingInHg < 27.5f ||
                snapshot.barometricSettingInHg > 31.5f)
                return false;

            indicatedFeet =
                snapshot.pressureAltitudeFeet +
                (snapshot.barometricSettingInHg -
                 StandardBarometricSettingInHg) *
                ApproximateFeetPerInHg;
            return IsFinite(indicatedFeet);
        }

        /// <summary>
        /// Stabilizes the four-digit mechanical-style readout at ten-foot
        /// resolution. A six-foot Schmitt threshold prevents state noise near
        /// a rounding boundary from alternating adjacent numerals in the HMD.
        /// </summary>
        public static int StabilizeAltitudeReadoutFeet(
            float indicatedFeet,
            int previousDisplayFeet,
            bool hasPreviousDisplay)
        {
            if (!IsFinite(indicatedFeet))
                return Mathf.Clamp(previousDisplayFeet, 0, 9990);

            int target = Mathf.Clamp(
                Mathf.RoundToInt(indicatedFeet / 10f) * 10, 0, 9990);
            if (!hasPreviousDisplay)
                return target;
            int previous = Mathf.Clamp(previousDisplayFeet, 0, 9990);
            return indicatedFeet >= previous + 6f ||
                   indicatedFeet <= previous - 6f
                ? target
                : previous;
        }

        private static bool TryEvaluateClock(
            CockpitInstrumentV2Function function,
            in CockpitInstrumentV2Snapshot snapshot,
            out float value)
        {
            value = 0f;
            if (!snapshot.Has(CockpitInstrumentV2Availability.ClockUtc) ||
                double.IsNaN(snapshot.clockUtcSeconds) ||
                double.IsInfinity(snapshot.clockUtcSeconds))
                return false;

            double daySeconds = Repeat(snapshot.clockUtcSeconds, 86400.0);
            switch (function)
            {
                case CockpitInstrumentV2Function.ClockHourHandDegrees:
                    value = (float)(Repeat(daySeconds, 43200.0) / 43200.0 * 360.0);
                    return true;
                case CockpitInstrumentV2Function.ClockMinuteHandDegrees:
                    value = (float)(Repeat(daySeconds, 3600.0) / 3600.0 * 360.0);
                    return true;
                case CockpitInstrumentV2Function.ClockSecondHandDegrees:
                    value = (float)(Repeat(daySeconds, 60.0) / 60.0 * 360.0);
                    return true;
                default:
                    return false;
            }
        }

        private static float CycleDegrees(float value, float period)
        {
            return Mathf.Repeat(value, period) / period * 360f;
        }

        public static float NormalizeHeadingDegrees(float value)
        {
            return IsFinite(value) ? Mathf.Repeat(value, 360f) : 0f;
        }

        /// <summary>
        /// Applies a bounded first-order response on the shortest angular path.
        /// This models the represented liquid compass's visual damping without
        /// inventing unvalidated turning or acceleration error.
        /// </summary>
        public static float DampHeadingDegrees(
            float currentDegrees,
            float targetDegrees,
            float deltaTimeSeconds,
            float responseSeconds)
        {
            float current = NormalizeHeadingDegrees(currentDegrees);
            float target = NormalizeHeadingDegrees(targetDegrees);
            if (!IsFinite(deltaTimeSeconds) || deltaTimeSeconds <= 0f ||
                !IsFinite(responseSeconds) || responseSeconds <= 0f)
                return target;
            float blend = 1f - Mathf.Exp(
                -deltaTimeSeconds / responseSeconds);
            return NormalizeHeadingDegrees(
                current + Mathf.DeltaAngle(current, target) * blend);
        }

        private static float Heading(float value)
        {
            return NormalizeHeadingDegrees(value);
        }

        private static float SignedAngle(float value)
        {
            return Mathf.DeltaAngle(0f, value);
        }

        private static double Repeat(double value, double period)
        {
            return value - System.Math.Floor(value / period) * period;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
