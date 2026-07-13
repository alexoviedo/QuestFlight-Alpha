using System;

namespace QuestFlightLab.Runtime
{
    [Serializable]
    public class AircraftControlState
    {
        public float aileron;
        public float elevator;
        public float rudder;
        public float throttle;
        public float mixture = 1f;
        public float carbHeat;
        public float trim;
        public float flaps;
        public float leftToeBrake;
        public float rightToeBrake;

        public bool markerPressed;
        public bool resetPressed;
        public bool pausePressed;
        public bool telemetryTogglePressed;

        public static AircraftControlState Neutral(float throttle = 0.72f)
        {
            return new AircraftControlState
            {
                throttle = throttle,
                mixture = 1f,
                carbHeat = 0f
            };
        }
    }
}
