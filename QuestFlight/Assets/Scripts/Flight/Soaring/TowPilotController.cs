using System;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlightLab.Flight.Soaring
{
    public enum GroundLaunchPhase { Staging,TakeUpSlack,Roll,Climb,Separated,Aborted,Returning,Pattern,Final,Stopped,Taxi,Parked }
    // Finite proportional/rate feedback; no integrator or pose authority. Its
    // physical aircraft must carry the additional rope load and lose energy.
    public sealed partial class TowPilotController
    {
        public GroundLaunchPhase Phase { get; private set; }
        public readonly AircraftControlState Controls=new AircraftControlState();
        private double phaseStart,heading,powerStart;
        public void Reset(double initialHeading) {heading=initialHeading;phaseStart=0;Phase=GroundLaunchPhase.Staging;}
        public void Abort() {Phase=GroundLaunchPhase.Aborted;}
        public void Evaluate(in FlightDynamicsState s,in TowlineSnapshot line,double time)
        {
            if(Phase==GroundLaunchPhase.Staging && LaunchAuthorized && time>phaseStart+2)SetPhase(GroundLaunchPhase.TakeUpSlack,time);
            if(Phase==GroundLaunchPhase.TakeUpSlack && line.tensionNewtons>25 && time-phaseStart>.4)SetPhase(GroundLaunchPhase.Roll,time);
            if(Phase==GroundLaunchPhase.Roll && !s.weightOnWheels && s.altitudeAglMeters>4)SetPhase(GroundLaunchPhase.Climb,time);
            if(LaunchAuthorized && Phase<=GroundLaunchPhase.Climb && (line.state==TowlineState.ReleasedByGlider || line.state==TowlineState.ReleasedByTug || line.state==TowlineState.BrokenAtGlider || line.state==TowlineState.BrokenAtTug))SetPhase(GroundLaunchPhase.Separated,time);
            if(Phase==GroundLaunchPhase.TakeUpSlack && time-phaseStart>30)Phase=GroundLaunchPhase.Aborted;
            float targetHeading=(float)(heading+(s.altitudeAglMeters>120?-30:0)+(Phase==GroundLaunchPhase.Separated?-30:0));
            float missionAltitude=0;bool returning=EvaluateOperation(s,time,ref targetHeading,out missionAltitude);
            float headingError=Mathf.DeltaAngle((float)s.headingDegrees,targetHeading);
            float bankLimit=returning?25:18;
            float targetBank=s.altitudeAglMeters>25?Mathf.Clamp(headingError*.7f,-bankLimit,bankLimit):0;
            if(operation==null)targetBank=s.altitudeAglMeters>25?Mathf.Clamp(headingError*.7f,-12,12):0;
            if(Phase==GroundLaunchPhase.Final && !s.weightOnWheels && s.altitudeAglMeters<=25)
                targetBank=Mathf.Clamp(headingError*.7f,-8,8);
            float targetPitch=s.calibratedAirspeedKnots<25?8:Mathf.Clamp(3+((float)s.calibratedAirspeedKnots-55)*.45f,-4,8);
            if(s.weightOnWheels && s.calibratedAirspeedKnots<40 && time>3)targetPitch=3;
            if(returning)targetPitch=-2+Mathf.Clamp(((float)s.calibratedAirspeedKnots-50)*.8f,-10,12);
            if(Phase==GroundLaunchPhase.Final && s.altitudeAglMeters<5)targetPitch=Mathf.Lerp(7,1,(float)s.altitudeAglMeters/5);
            // BodyRatesRadiansToUnityDegrees maps native q to -Unity X,
            // native p to -Unity Z, and native r to +Unity Y.
            Controls.aileron=Mathf.Clamp(.045f*(targetBank-(float)s.bankDegrees)+.30f*s.angularVelocityBodyDegreesPerSecond.z*Mathf.Deg2Rad,-.5f,.5f);
            Controls.elevator=Mathf.Clamp(.15f+.06f*(targetPitch-(float)s.pitchDegrees)+.9f*s.angularVelocityBodyDegreesPerSecond.x*Mathf.Deg2Rad,-.7f,.7f);
            Controls.rudder=Mathf.Clamp((s.altitudeAglMeters<3?.025f*headingError:0)+.04f*(float)s.sideslipDegrees-.25f*s.angularVelocityBodyDegreesPerSecond.y*Mathf.Deg2Rad,-.5f,.5f);
            Controls.mixture=.8f;
            Controls.throttle=Phase==GroundLaunchPhase.Staging?.2f:Phase==GroundLaunchPhase.TakeUpSlack?Mathf.Clamp(.2f+(3-(float)s.groundSpeedKnots)*.035f,.15f,.36f):Phase==GroundLaunchPhase.Aborted?0:Mathf.Clamp01(.3f+(float)(time-powerStart)*.15f);
            Controls.flaps=returning&&s.calibratedAirspeedKnots<70?1:0;
            if(returning)
            {
                float desiredClimb=Mathf.Clamp((missionAltitude-s.positionUnityMeters.y)*.055f,-5,3);
                Controls.throttle=Mathf.Clamp(.48f+desiredClimb*.08f+(desiredClimb-(float)s.verticalSpeedFeetPerMinute*.00508f)*.06f,0,.85f);
            }
            if(Phase==GroundLaunchPhase.Final && s.altitudeAglMeters<5 || Phase==GroundLaunchPhase.Stopped)Controls.throttle=0;
            Controls.leftToeBrake=Controls.rightToeBrake=Phase==GroundLaunchPhase.Staging || Phase==GroundLaunchPhase.Aborted?1:0;
            if(Phase==GroundLaunchPhase.Stopped){Controls.leftToeBrake=Controls.rightToeBrake=.65f;Controls.elevator=.35f;}
            EvaluateTaxiControls(s,time);
        }
        private void SetPhase(GroundLaunchPhase phase,double t){Phase=phase;phaseStart=t;if(phase==GroundLaunchPhase.Roll)powerStart=t;}
    }
}
