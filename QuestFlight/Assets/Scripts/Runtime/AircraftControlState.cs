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
        public float airbrake;
        public float wheelBrake;
        public bool releasePressed;

        public bool markerPressed;
        public bool resetPressed;
        public bool pausePressed;
        public bool telemetryTogglePressed;

        public static AircraftControlState Neutral(float throttle = 0.72f)
        {
            AircraftControlState state = new AircraftControlState();
            state.SetNeutral(throttle);
            return state;
        }

        public void SetNeutral(float neutralThrottle = 0.72f)
        {
            aileron = 0f;
            elevator = 0f;
            rudder = 0f;
            throttle = neutralThrottle;
            mixture = 1f;
            carbHeat = 0f;
            trim = 0f;
            flaps = 0f;
            leftToeBrake = 0f;
            rightToeBrake = 0f;
            airbrake = 0f;
            wheelBrake = 0f;
            releasePressed = false;
            markerPressed = false;
            resetPressed = false;
            pausePressed = false;
            telemetryTogglePressed = false;
        }

        public void SetGliderNeutral()
        {
            SetNeutral(0f);
            mixture = 0f;
        }
    }
}
