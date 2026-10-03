using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEngine;

namespace QuestFlight.Product
{
    /// <summary>Demo-only ground-track and energy guidance; does not change lesson assistance.</summary>
    public sealed class DemoEnrouteGuidance
    {
        private float pitchIntegral, rollIntegral, speedIntegral, verticalIntegral;
        public void Reset() { pitchIntegral=rollIntegral=speedIntegral=verticalIntegral=0; }
        public FlightLessonControlCommand Step(in FlightDynamicsState state, in FlightLessonGuidanceIntent intent,float dt,float profileVerticalSpeed=0)
        {
            var s=ProductDemoPilot.BuildGuidanceAircraftState(state);
            Vector3 destination=intent.lookAheadPointUnityMeters-state.positionUnityMeters;
            float trackTarget=Mathf.Atan2(destination.x,destination.z)*Mathf.Rad2Deg;
            float track=Mathf.Atan2(state.velocityUnityMetersPerSecond.x,state.velocityUnityMetersPerSecond.z)*Mathf.Rad2Deg;
            float horizontalSpeed=new Vector2(state.velocityUnityMetersPerSecond.x,state.velocityUnityMetersPerSecond.z).magnitude;
            float lookDistance=Mathf.Max(80,new Vector2(destination.x,destination.z).magnitude);
            float error=Mathf.DeltaAngle(track,trackTarget);
            float lateral=2*horizontalSpeed*horizontalSpeed*Mathf.Sin(Mathf.Clamp(error,-90,90)*Mathf.Deg2Rad)/lookDistance;
            float bank=Mathf.Clamp(Mathf.Atan(lateral/9.80665f)*Mathf.Rad2Deg,-intent.maximumBankDegrees,intent.maximumBankDegrees);
            float vs=Mathf.Clamp(profileVerticalSpeed+(intent.targetAltitudeMslFeet-s.altitudeMslFeet)*2f,-intent.maximumClimbRateFeetPerMinute,intent.maximumClimbRateFeetPerMinute);
            verticalIntegral=Mathf.Clamp(verticalIntegral+(vs-s.verticalSpeedFeetPerMinute)*dt*.0008f,-5,5);
            float pitch=Mathf.Clamp(2+vs*.006f+(vs-s.verticalSpeedFeetPerMinute)*.004f+verticalIntegral,-6,10);
            if(intent.segmentKind==FlightLessonSegmentKind.Departure) pitch=Mathf.Clamp(6+(s.calibratedAirspeedKnots-74)*.2f,2,12);
            pitchIntegral=Mathf.Clamp(pitchIntegral+(pitch-s.pitchDegrees)*dt*.01f,-.3f,.3f);
            rollIntegral=Mathf.Clamp(rollIntegral+(bank-s.bankDegrees)*dt*.003f,-.12f,.12f);
            speedIntegral=Mathf.Clamp(speedIntegral+(intent.targetCalibratedAirspeedKnots-s.calibratedAirspeedKnots)*dt*.003f,-.3f,.3f);
            return new FlightLessonControlCommand {
                aileron=Mathf.Clamp((bank-s.bankDegrees)*.045f-s.rollRateDegreesPerSecond*.022f+rollIntegral,-.6f,.6f),
                elevator=Mathf.Clamp((pitch-s.pitchDegrees)*.075f-s.pitchRateDegreesPerSecond*.03f+pitchIntegral,-.6f,.65f),
                rudder=Mathf.Clamp(-s.sideslipDegrees*.06f-s.yawRateDegreesPerSecond*.015f,-.3f,.3f),
                throttle=Mathf.Clamp01(.62f+(intent.targetCalibratedAirspeedKnots-s.calibratedAirspeedKnots)*.025f+speedIntegral+Mathf.Max(0,vs)*.0003f),
                mixture=1,trim=-.08f
            };
        }
    }
}
