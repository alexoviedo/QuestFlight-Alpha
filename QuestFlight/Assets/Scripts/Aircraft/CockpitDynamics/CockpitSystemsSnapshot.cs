using System;

namespace QuestFlightLab.Aircraft.CockpitDynamics
{
    /// <summary>
    /// The exact normalized control commands handed to the active backend.
    /// Values describe commanded cockpit-control positions; they must not be
    /// presented as measured flap travel or other feedback that is unavailable.
    /// </summary>
    [Serializable]
    public struct CockpitControlPositions
    {
        public float aileron;
        public float elevator;
        public float rudder;
        public float throttle;
        public float mixture;
        public float elevatorTrim;
        public float flapCommand;
        public float leftToeBrake;
        public float rightToeBrake;
    }

    /// <summary>
    /// Allocation-free presentation snapshot for cockpit instruments and
    /// positively identified controls. "Indicated" fields contain deterministic
    /// instrument response filtering, while control positions remain exact.
    /// </summary>
    [Serializable]
    public struct CockpitSystemsSnapshot
    {
        public bool isValid;
        public double simulationTimeSeconds;
        public CockpitDataAvailability availability;

        public float indicatedAirspeedKnots;
        public float indicatedAltitudeMslFeet;
        public float indicatedVerticalSpeedFeetPerMinute;
        public float indicatedPitchDegrees;
        public float indicatedBankDegrees;
        public float indicatedHeadingDegrees;
        public float indicatedBodyTurnRateDegreesPerSecond;
        public float aerodynamicSideslipDegrees;
        public float indicatedEngineRpm;
        public bool weightOnWheels;

        public CockpitControlPositions controlPositions;
        public CockpitElectricalPowerState electricalPower;

        public bool Has(CockpitDataAvailability channel)
        {
            return (availability & channel) == channel;
        }
    }
}
