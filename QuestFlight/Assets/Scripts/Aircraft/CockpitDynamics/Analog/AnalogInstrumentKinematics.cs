using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics.Analog
{
    /// <summary>
    /// Deterministic Quest-safe dial mappings. These functions contain no
    /// allocations, reflection, curves, delegates, or per-frame lookups.
    /// </summary>
    public static class AnalogInstrumentKinematics
    {
        public static bool TryEvaluateDialAngleDegrees(
            AnalogInstrumentFunction function,
            in CockpitSystemsSnapshot snapshot,
            out float angleDegrees)
        {
            angleDegrees = 0f;
            if (!snapshot.isValid) return false;

            switch (function)
            {
                case AnalogInstrumentFunction.IndicatedAirspeedKnots:
                    if (!snapshot.Has(CockpitDataAvailability.AirData)) return false;
                    angleDegrees = EvaluateAirspeedAngle(
                        Mathf.Max(0f, snapshot.indicatedAirspeedKnots));
                    return true;

                case AnalogInstrumentFunction.IndicatedVerticalSpeedFeetPerMinute:
                    if (!snapshot.Has(CockpitDataAvailability.AirData)) return false;
                    angleDegrees = EvaluateVerticalSpeedAngle(
                        snapshot.indicatedVerticalSpeedFeetPerMinute);
                    return true;

                default:
                    return false;
            }
        }

        public static float EvaluateAirspeedAngle(float knots)
        {
            if (knots <= 40f) return Lerp(180f, 130f, knots / 40f);
            if (knots <= 60f) return Lerp(130f, 90f, (knots - 40f) / 20f);
            if (knots <= 80f) return Lerp(90f, 68f, (knots - 60f) / 20f);
            if (knots <= 100f) return Lerp(68f, 45f, (knots - 80f) / 20f);
            if (knots <= 120f) return Lerp(45f, 24f, (knots - 100f) / 20f);
            if (knots <= 140f) return Lerp(24f, 4f, (knots - 120f) / 20f);
            if (knots <= 160f) return Lerp(4f, -16f, (knots - 140f) / 20f);
            if (knots <= 180f) return Lerp(-16f, -36f, (knots - 160f) / 20f);
            if (knots <= 200f) return Lerp(-36f, -58f, (knots - 180f) / 20f);
            if (knots <= 220f) return Lerp(-58f, -80f, (knots - 200f) / 20f);
            if (knots <= 240f) return Lerp(-80f, -101f, (knots - 220f) / 20f);
            if (knots <= 250f) return Lerp(-101f, -110f, (knots - 240f) / 10f);
            if (knots <= 300f) return Lerp(-110f, -130f, (knots - 250f) / 50f);
            if (knots <= 350f) return Lerp(-130f, -146f, (knots - 300f) / 50f);
            if (knots <= 400f) return Lerp(-146f, -160f, (knots - 350f) / 50f);
            return -160f;
        }

        public static float EvaluateVerticalSpeedAngle(float feetPerMinute)
        {
            // The audited source face is a zero-at-left, split-scale VSI.
            // Positive climb traverses the upper semicircle from 9 to 3
            // o'clock; negative descent traverses the lower semicircle.
            float magnitude = Mathf.Abs(feetPerMinute);
            float magnitudeAngle;
            if (magnitude <= 500f)
                magnitudeAngle = Lerp(180f, 135f, magnitude / 500f);
            else if (magnitude <= 1000f)
                magnitudeAngle = Lerp(135f, 90f, (magnitude - 500f) / 500f);
            else if (magnitude <= 2000f)
                magnitudeAngle = Lerp(90f, 45f, (magnitude - 1000f) / 1000f);
            else if (magnitude <= 4000f)
                magnitudeAngle = Lerp(45f, 20f, (magnitude - 2000f) / 2000f);
            else if (magnitude <= 6000f)
                magnitudeAngle = Lerp(20f, 0f, (magnitude - 4000f) / 2000f);
            else
                magnitudeAngle = 0f;

            return feetPerMinute < 0f ? -magnitudeAngle : magnitudeAngle;
        }

        private static float Lerp(float from, float to, float t)
        {
            return from + (to - from) * Mathf.Clamp01(t);
        }
    }
}
