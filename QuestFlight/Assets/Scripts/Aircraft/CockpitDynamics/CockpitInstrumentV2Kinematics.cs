using UnityEngine;

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

            float indicatedFeet =
                snapshot.pressureAltitudeFeet +
                (snapshot.barometricSettingInHg -
                 StandardBarometricSettingInHg) *
                ApproximateFeetPerInHg;
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

        private static float Heading(float value)
        {
            return Mathf.Repeat(value, 360f);
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
