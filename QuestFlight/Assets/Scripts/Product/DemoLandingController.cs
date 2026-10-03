using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEngine;

namespace QuestFlight.Product
{
    /// <summary>Control-only landing guidance. The native backend remains the flight authority.</summary>
    public sealed class DemoLandingController
    {
        public enum LandingPhase { Approach, Flare, Rollout, Stopped, GoAround, TaxiExit }
        public LandingPhase Phase { get; private set; }
        public float TouchdownSinkFpm { get; private set; }
        public bool HasTouchedDown { get; private set; }
        public string GoAroundReason { get; private set; }
        private Vector3 threshold, runwayEnd;
        private Vector3 forward, right;
        private float contactSeconds, stoppedSeconds, pitchIntegral, speedIntegral, previousSink;
        private Vector3[] exitPath;
        private int exitIndex;

        public void Reset(Vector3 runwayThreshold, Vector3 farEnd, Vector3[] authoredExitPath = null)
        {
            threshold = runwayThreshold; runwayEnd = farEnd;
            forward = runwayEnd - threshold; forward.y = 0; forward.Normalize();
            right = new Vector3(forward.z, 0, -forward.x);
            Phase = LandingPhase.Approach;
            contactSeconds = stoppedSeconds = pitchIntegral = speedIntegral = previousSink = 0;
            HasTouchedDown = false; TouchdownSinkFpm = 0; GoAroundReason = null;
            exitPath = authoredExitPath; exitIndex = 0;
        }

        public FlightLessonControlCommand Step(in FlightDynamicsState state, float dt, bool runwayClear)
        {
            dt = Mathf.Clamp(dt, .001f, .05f);
            var aircraft = ProductDemoPilot.BuildGuidanceAircraftState(state);
            Vector3 offset = state.positionUnityMeters - threshold;
            float along = Vector3.Dot(offset, forward), cross = Vector3.Dot(offset, right);
            float runwayLength = Vector3.Dot(runwayEnd - threshold, forward);
            float heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float track = Mathf.Atan2(state.velocityUnityMetersPerSecond.x, state.velocityUnityMetersPerSecond.z) * Mathf.Rad2Deg;
            float targetTrack = heading - Mathf.Clamp(cross * .15f, -40, 40);
            float bankLimit=state.altitudeAglMeters>30?20:10;
            float bankTarget = Mathf.Clamp(Mathf.DeltaAngle(track, targetTrack) * .8f, -bankLimit, bankLimit);
            float agl = (float)state.altitudeAglMeters;
            if (state.weightOnWheels)
            {
                if (!HasTouchedDown) { HasTouchedDown = true; TouchdownSinkFpm = previousSink; }
                contactSeconds += dt;
                if ((Phase == LandingPhase.Approach || Phase == LandingPhase.Flare) && contactSeconds > .15f) Phase = LandingPhase.Rollout;
            }
            else contactSeconds = 0;
            previousSink = aircraft.verticalSpeedFeetPerMinute;

            if (Phase == LandingPhase.Approach && !state.weightOnWheels)
            {
                if (!runwayClear) GoAround("Runway occupied");
                else if (along > runwayLength * .45f) GoAround("Landing distance exhausted");
                else if (agl < 25 && (Mathf.Abs(cross) > 16 || Mathf.Abs(aircraft.bankDegrees) > 15 || aircraft.calibratedAirspeedKnots > 80)) GoAround("Unstable final");
                else if (agl < 7 && along > -120) Phase = LandingPhase.Flare;
            }
            if (Phase == LandingPhase.Flare && (!runwayClear || along > runwayLength * .55f)) GoAround("Landing unavailable");

            var command = new FlightLessonControlCommand { mixture = 1, flaps = .666667f, trim = -.08f };
            float pitchTarget;
            if (Phase == LandingPhase.Rollout && state.weightOnWheels && state.groundSpeedKnots < 9 && exitPath != null && exitPath.Length > 1)
            {
                float best = float.MaxValue;
                for (int i=0;i<exitPath.Length;i++)
                {
                    float distance = (exitPath[i]-state.positionUnityMeters).sqrMagnitude;
                    if(distance<best) { best=distance; exitIndex=i; }
                }
                Phase = LandingPhase.TaxiExit;
            }
            if (Phase == LandingPhase.TaxiExit)
            {
                // Follow the existing surveyed ground curve; no direct shortcut across the grass.
                while(exitIndex<exitPath.Length-1 && HorizontalDistance(state.positionUnityMeters,exitPath[exitIndex])<7) exitIndex++;
                Vector3 destination=exitPath[exitIndex];
                float taxiHeading=Mathf.Atan2(destination.x-state.positionUnityMeters.x,destination.z-state.positionUnityMeters.z)*Mathf.Rad2Deg;
                float error=Mathf.DeltaAngle(aircraft.headingDegreesTrue,taxiHeading);
                float desiredSpeed=Mathf.Abs(error)>12?3:6;
                bool atEnd=exitIndex==exitPath.Length-1 && HorizontalDistance(state.positionUnityMeters,destination)<4;
                command.throttle=atEnd?0:Mathf.Clamp(.15f+(desiredSpeed-(float)state.groundSpeedKnots)*.05f,0,.3f);
                float brake=atEnd?.6f:Mathf.Clamp(((float)state.groundSpeedKnots-desiredSpeed)*.12f,0,.5f);
                command.leftToeBrake=command.rightToeBrake=state.weightOnWheels?brake:0;
                command.rudder=Mathf.Clamp(error*.055f,-1,1);
                command.elevator=0; command.aileron=0;
                stoppedSeconds=atEnd && state.weightOnWheels && state.groundSpeedKnots<.5?stoppedSeconds+dt:0;
                if(stoppedSeconds>1) Phase=LandingPhase.Stopped;
                return command;
            }
            if (Phase == LandingPhase.Rollout || Phase == LandingPhase.Stopped)
            {
                // Loss of contact removes brakes immediately. A bounce is not another completed landing.
                float brake = state.weightOnWheels ? (Phase==LandingPhase.Stopped?.6f:Mathf.Clamp01(((float)state.groundSpeedKnots - 5) * .04f + .16f)) : 0;
                command.throttle = 0; command.leftToeBrake = command.rightToeBrake = brake;
                command.rudder = Mathf.Clamp(Mathf.DeltaAngle(aircraft.headingDegreesTrue, heading) * .045f - cross * .035f, -.6f, .6f);
                pitchTarget = (float)state.groundSpeedKnots > 30 ? 3 : 0;
                stoppedSeconds = state.weightOnWheels && state.groundSpeedKnots < 1 ? stoppedSeconds + dt : 0;
                if (stoppedSeconds > 1) Phase = LandingPhase.Stopped;
            }
            else if (Phase == LandingPhase.GoAround)
            {
                command.throttle = 1; command.flaps = agl > 50 && aircraft.calibratedAirspeedKnots > 65 ? .3333f : .6667f;
                pitchTarget = Mathf.Clamp(5 + (aircraft.calibratedAirspeedKnots - 70) * .2f, 0, 9);
            }
            else
            {
                float heightTarget = 1.5f + Mathf.Max(0, 160 - along) * .07f;
                float groundAlong = Mathf.Max(15, Vector3.Dot(state.velocityUnityMetersPerSecond, forward));
                float desiredSink = Mathf.Clamp(-groundAlong * .07f + (heightTarget - offset.y) * .35f, -5, -.4f);
                float actualSink = aircraft.verticalSpeedFeetPerMinute * .00508f;
                pitchTarget = Mathf.Clamp(-2 + (desiredSink - actualSink) * 2.2f, -6, 7);
                speedIntegral = Mathf.Clamp(speedIntegral + (65 - aircraft.calibratedAirspeedKnots) * dt * .003f, -.15f, .25f);
                command.throttle = Mathf.Clamp(.22f + (65 - aircraft.calibratedAirspeedKnots) * .025f + speedIntegral, .02f, .65f);
                if (Phase == LandingPhase.Flare)
                {
                    float flareSink = -Mathf.Lerp(.25f, 1.1f, Mathf.Clamp01((agl - 1.4f) / 5.6f));
                    pitchTarget = Mathf.Clamp(3 + (flareSink - actualSink) * 1.6f, 1, 9);
                    command.throttle = 0;
                }
            }
            command.aileron = Mathf.Clamp((bankTarget - aircraft.bankDegrees) * .045f - aircraft.rollRateDegreesPerSecond * .022f, -.5f, .5f);
            if (Phase != LandingPhase.Rollout && Phase != LandingPhase.Stopped)
                command.rudder = Mathf.Clamp(-aircraft.sideslipDegrees * .06f - aircraft.yawRateDegreesPerSecond * .015f, -.3f, .3f);
            pitchIntegral = Mathf.Clamp(pitchIntegral + (pitchTarget - aircraft.pitchDegrees) * dt * .006f, -.2f, .2f);
            command.elevator = Mathf.Clamp((pitchTarget - aircraft.pitchDegrees) * .075f - aircraft.pitchRateDegreesPerSecond * .03f + pitchIntegral, -.55f, .65f);
            return command;
        }

        private void GoAround(string reason) { Phase = LandingPhase.GoAround; GoAroundReason = reason; pitchIntegral = speedIntegral = 0; }
        private static float HorizontalDistance(Vector3 a,Vector3 b) { a.y=b.y=0;return Vector3.Distance(a,b); }
    }
}
