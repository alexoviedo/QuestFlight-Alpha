using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Flight.Soaring
{
    public sealed partial class TowPilotController
    {
        private BoulderSoaringOperationDefinition operation;
        private int routeIndex,returnIndex;
        private int taxiIndex;
        private float taxiPowerIntegral;
        private double taxiLastTime;
        public bool LaunchAuthorized {get;set;}=true;
        public bool ReturnRunwayClear {get;set;}=true;
        public Vector3 LocalWind {get;set;}
        public void ConfigureOperation(BoulderSoaringOperationDefinition value)
        {operation=value;routeIndex=returnIndex=0;LaunchAuthorized=false;}
        private bool EvaluateOperation(in FlightDynamicsState s,double time,ref float targetHeading,out float altitude)
        {
            altitude=0;if(operation==null)return false;
            int sign=operation.DirectionSign;Vector2 at=new Vector2(s.positionUnityMeters.x,s.positionUnityMeters.z);
            if(Phase<=GroundLaunchPhase.Climb)
            {
                targetHeading=operation.trueHeading;
                if(s.positionUnityMeters.y<operation.minimumTurnFieldHeight)return false;
                Vector2 target=operation.towRoute[routeIndex];
                if(Vector2.Distance(at,target)<260){routeIndex=(routeIndex+1)%operation.towRoute.Length;target=operation.towRoute[routeIndex];}
                targetHeading=HeadingTo(at,target,32);return false;
            }
            if(Phase==GroundLaunchPhase.Aborted||Phase==GroundLaunchPhase.Staging)return false;
            if(Phase==GroundLaunchPhase.Separated)
            {
                targetHeading=(float)heading-30*sign;
                if(time-phaseStart<12)return false;
                SetPhase(GroundLaunchPhase.Returning,time);returnIndex=0;
            }
            if(Phase==GroundLaunchPhase.Returning)
            {
                // Descend around the outer development circuit before joining
                // the published pattern altitude, never teleport or disappear.
                Vector2 target=ReturnPoint(returnIndex,sign);
                if(returnIndex==1 && s.positionUnityMeters.y<operation.patternFieldHeight+20 && ReturnRunwayClear && at.x*sign>0)
                    SetPhase(GroundLaunchPhase.Pattern,time);
                if(Vector2.Distance(at,target)<(returnIndex==1?100:320))
                {
                    returnIndex=(returnIndex+1)%4;target=ReturnPoint(returnIndex,sign);
                    if(returnIndex==1&&s.positionUnityMeters.y<operation.patternFieldHeight+20&&ReturnRunwayClear)
                        SetPhase(GroundLaunchPhase.Pattern,time);
                }
                targetHeading=HeadingTo(at,target,34);altitude=operation.patternFieldHeight;return true;
            }
            if(Phase==GroundLaunchPhase.Pattern)
            {
                if(!ReturnRunwayClear){SetPhase(GroundLaunchPhase.Returning,time);returnIndex=0;return true;}
                Vector2 target=ReturnPoint(returnIndex,sign);
                if(Vector2.Distance(at,target)<250){returnIndex++;if(returnIndex>=4){SetPhase(GroundLaunchPhase.Final,time);returnIndex=3;}target=ReturnPoint(returnIndex,sign);}
                targetHeading=HeadingTo(at,target,32);
                altitude=returnIndex<=1?Mathf.Lerp(125,operation.patternFieldHeight,Mathf.Clamp01((at.x*sign+750)/2250)):returnIndex==2?85:55;return true;
            }
            if(Phase==GroundLaunchPhase.Final)
            {
                if(s.weightOnWheels&&s.altitudeAglMeters<2){SetPhase(GroundLaunchPhase.Stopped,time);}
                if(!ReturnRunwayClear&&s.altitudeAglMeters>10){SetPhase(GroundLaunchPhase.Returning,time);returnIndex=0;altitude=operation.patternFieldHeight;return true;}
                if(at.x*sign>400 && !s.weightOnWheels){SetPhase(GroundLaunchPhase.Returning,time);returnIndex=0;altitude=operation.patternFieldHeight;return true;}
                Vector2 target=new Vector2(at.x+300*sign,operation.centerlineNorth);
                targetHeading=HeadingTo(at,target,30);
                altitude=Mathf.Max(0,(-420*sign-at.x)*sign*.065f);return true;
            }
            targetHeading=operation.trueHeading;altitude=0;return true;
        }
        private static Vector2 ReturnPoint(int index,int sign)=>index switch
        {0=>new Vector2(1500*sign,750),1=>new Vector2(-750*sign,750),2=>new Vector2(-1350*sign,450),_=>new Vector2(-1450*sign,-30)};
        private float HeadingTo(Vector2 at,Vector2 target,float speed)
        {Vector2 desired=(target-at).normalized*speed-new Vector2(LocalWind.x,LocalWind.z);return Mathf.Atan2(desired.x,desired.y)*Mathf.Rad2Deg;}
        private void EvaluateTaxiControls(in FlightDynamicsState s,double time)
        {
            if(operation==null)return;
            if(Phase==GroundLaunchPhase.Stopped&&s.groundSpeedKnots<4){SetPhase(GroundLaunchPhase.Taxi,time);taxiIndex=0;taxiPowerIntegral=0;taxiLastTime=time;}
            if(Phase==GroundLaunchPhase.Parked){Controls.throttle=0;Controls.leftToeBrake=Controls.rightToeBrake=1;return;}
            if(Phase!=GroundLaunchPhase.Taxi)return;
            float exit=operation.runwayDirection==8?265:-48;
            Vector2 at=new Vector2(s.positionUnityMeters.x,s.positionUnityMeters.z);
            Vector2 target=taxiIndex==0?new Vector2(exit,-30):taxiIndex==1?new Vector2(exit,-91):new Vector2(exit-25,-121);
            if(Vector2.Distance(at,target)<9){taxiIndex++;if(taxiIndex>=3){SetPhase(GroundLaunchPhase.Parked,time);return;}target=taxiIndex==1?new Vector2(exit,-91):new Vector2(exit-25,-121);}
            float headingError=Mathf.DeltaAngle((float)s.headingDegrees,Mathf.Atan2(target.x-at.x,target.y-at.y)*Mathf.Rad2Deg);
            Controls.aileron=0;Controls.elevator=.35f;Controls.rudder=Mathf.Clamp(headingError*.035f,-1,1);
            float wanted=Mathf.Abs(headingError)>30?3:5;
            float dt=Mathf.Clamp((float)(time-taxiLastTime),0,.05f);taxiLastTime=time;
            taxiPowerIntegral=Mathf.Clamp(taxiPowerIntegral+(wanted-(float)s.groundSpeedKnots)*dt*.025f,-.1f,.35f);
            Controls.flaps=0;
            Controls.throttle=Mathf.Clamp(.18f+(wanted-(float)s.groundSpeedKnots)*.04f+taxiPowerIntegral,.02f,.65f);
            Controls.leftToeBrake=Controls.rightToeBrake=s.groundSpeedKnots>wanted+1?.25f:0;
            if(Mathf.Abs(headingError)>30 && s.groundSpeedKnots>1)
            {if(headingError>0)Controls.rightToeBrake=Mathf.Max(Controls.rightToeBrake,.15f);else Controls.leftToeBrake=Mathf.Max(Controls.leftToeBrake,.15f);}
        }
    }
}
