using System;
using UnityEngine;
using QuestFlightLab.Runtime;
using QuestFlightLab.Flight;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;

namespace QuestFlight.Product
{
    // Controls only. Native JSBSim remains the sole aircraft state authority.
    public sealed class SoaringDemoGuidance
    {
        public enum Stage { Preparation, Tow, Release, Thermal, Sightseeing, Descent, Circuit, Final, Complete, Held }
        public Stage Phase {get;private set;}
        public string Failure {get;private set;}
        public float ThermalGainMeters {get;private set;}
        public float ThermalSeconds {get;private set;}
        public int ScenicIndex {get;private set;}
        public int LandingIndex {get;private set;}
        public bool Short {get;private set;}
        public bool EarlyReturn {get;private set;}
        public bool ReleaseRequested {get;private set;}
        public double Elapsed {get;private set;}
        public Vector3 ThermalCenter {get;private set;}
        private SoaringDemoProfile profile;
        private float integral,phaseSeconds,stableSeconds;
        private double initialEnergy;
        private bool energyStarted,wasAirborne;
        private float previousSink,marginAt; private SoaringReturnEstimate cachedMargin;
        public bool CanTakeControl(in FlightDynamicsState s)=>Phase==Stage.Sightseeing&&!s.weightOnWheels&&s.altitudeAglMeters>250&&s.calibratedAirspeedKnots>42&&Math.Abs(s.bankDegrees)<25;
        public void Begin(SoaringDemoProfile value,bool shortFlight)
        {profile=value;Short=shortFlight;Phase=shortFlight?Stage.Thermal:Stage.Preparation;integral=phaseSeconds=stableSeconds=ThermalSeconds=ThermalGainMeters=0;marginAt=-1;Elapsed=0;initialEnergy=0;energyStarted=wasAirborne=ReleaseRequested=EarlyReturn=false;ScenicIndex=LandingIndex=0;Failure=null;}
        private void Set(Stage value){if(Phase!=value){Phase=value;phaseSeconds=0;}}
        public void Hold(string reason){Failure=reason;Set(Stage.Held);}
        public void ReturnEarly(){EarlyReturn=true;if(Phase<Stage.Descent){LandingIndex=0;Set(Stage.Descent);}}
        public bool Resume(in FlightDynamicsState s,SoaringReturnEstimate margin,bool returnNow)
        {
            if(!s.IsFinite||s.weightOnWheels||s.altitudeAglMeters<250||s.calibratedAirspeedKnots<42||s.calibratedAirspeedKnots>65||Math.Abs(s.bankDegrees)>35||!margin.reachable||margin.marginMeters<100)return false;
            Failure=null;integral=0;marginAt=-1;Set(Stage.Sightseeing);if(returnNow)ReturnEarly();return true;
        }
        private static double Energy(FlightDynamicsCoordinator f)=>f.SoaringInstruments.PressureAltitudeFeet*.3048+Math.Pow(f.NativeDiagnostics.trueAirspeedKnots*.514444,2)/(2*9.80665);
        public void Step(FlightDynamicsCoordinator f,float dt,AircraftControlState c)
        {
            if(dt<=0)return;
            var s=f.CurrentState;var p=s.positionUnityMeters;var op=profile.operation;
            Elapsed+=dt;phaseSeconds+=dt;ReleaseRequested=false;
            c.SetGliderNeutral();c.trim=f.aircraftDefinition.referenceTrimNoseUp;
            if(!s.IsFinite||s.engineRpm!=0||!op.Contains(p)||f.RecoveryHold){Hold("Native flight held or outside supported area. Restart or end this demonstration.");return;}
            wasAirborne|=!s.weightOnWheels&&s.altitudeAglMeters>5;
            if(wasAirborne&&s.weightOnWheels&&Phase<Stage.Final){Hold("Unexpected early touchdown. Restart or end this demonstration.");return;}
            if(Elapsed>2400){Hold("Demonstration exceeded its flight window. Restart or end flight.");return;}
            if(Phase==Stage.Held)return;
            if(Phase==Stage.Complete){c.airbrake=c.wheelBrake=1;return;}
            if(Phase==Stage.Preparation)
            {
                c.airbrake=c.wheelBrake=phaseSeconds<4?1:0;
                if(f.Operation==null){Hold("Ground launch configuration unavailable.");return;}
                if(f.Operation.Phase==SoaringOperationPhase.Aborted){Hold(f.Operation.StopReason);return;}
                if(f.Operation.LaunchAuthorized)Set(Stage.Tow);
                return;
            }
            if(Phase==Stage.Tow)
            {
                if(f.TowSession==null||!f.TowSession.RopeConnected){Hold("Tow separated before the planned release. Restart or end flight.");return;}
                FollowTow(s,f.TowSession.TugState,(float)f.TowSession.NaturalLengthMeters+6,c);
                if(f.Operation.Phase==SoaringOperationPhase.ReleaseReady){ReleaseRequested=true;Set(Stage.Release);}
                return;
            }
            Vector3 wind=f.SampleWind(p,s.simulationTimeSeconds);
            var source=f.SoaringAtmosphere.Source(0);
            var background=f.SampleAtmosphereComponents(p,s.simulationTimeSeconds).background;
            Vector2 center=SoaringAtmosphereSession.Center(source,p.y,new Vector2(background.x,background.z));
            ThermalCenter=new Vector3(center.x,p.y,center.y);
            float bank=0,speed=48,targetHeight=p.y;
            if(Phase==Stage.Release)
            {
                if(f.TowSession!=null&&f.TowSession.RopeConnected){ReleaseRequested=true;return;}
                if(phaseSeconds>12)Set(Stage.Thermal);
            }
            if(Phase==Stage.Thermal)
            {
                speed=40;Vector3 radial=p-ThermalCenter;
                var tangent=new Vector3(radial.z,0,-radial.x).normalized;
                Vector3 desired=(tangent-radial.normalized*Mathf.Clamp((radial.magnitude-profile.thermalRadius)*.006f,-1,1))*25-new Vector3(wind.x,0,wind.z);
                float gs=new Vector2(s.velocityUnityMetersPerSecond.x,s.velocityUnityMetersPerSecond.z).magnitude;
                bank=Mathf.Clamp(Mathf.Atan2(gs*gs,9.80665f*profile.thermalRadius)*Mathf.Rad2Deg+Mathf.DeltaAngle((float)s.headingDegrees,Heading(desired))*.7f,-35,35);
                bool inLift=f.SampleAtmosphereComponents(p,s.simulationTimeSeconds).thermal>.3f;
                if(inLift&&!energyStarted){initialEnergy=Energy(f);energyStarted=true;}
                if(energyStarted){ThermalSeconds+=dt;ThermalGainMeters=(float)(Energy(f)-initialEnergy);}
                if(ThermalSeconds>=profile.minimumThermalSeconds&&ThermalGainMeters>=profile.thermalGain)Set(Stage.Sightseeing);
                else if(phaseSeconds>profile.maximumThermalSeconds){EarlyReturn=true;Set(Stage.Descent);}
            }
            if(Elapsed>=marginAt){cachedMargin=SoaringReturnPlanner.Evaluate(op,f.terrainSurface,f.SoaringAtmosphere,s,f.NativeDiagnostics.weightPounds*.45359237);marginAt=(float)Elapsed+1;}
            var margin=cachedMargin;
            if(Phase==Stage.Thermal&&p.y<520)ReturnEarly();
            if(Phase==Stage.Sightseeing)
            {
                var points=Short?profile.shortPoints:profile.scenicPoints;
                if(ScenicIndex>=points.Length||!margin.reachable||margin.marginMeters<150){if(ScenicIndex<points.Length)EarlyReturn=true;Set(Stage.Descent);}
                else {bank=Track(s,points[ScenicIndex],wind,25);if(Vector2.Distance(new Vector2(p.x,p.z),points[ScenicIndex])<180)ScenicIndex++;}
            }
            if(Phase==Stage.Descent)
            {
                Vector3 radial=p-new Vector3(0,p.y,1700),tangent=new Vector3(radial.z,0,-radial.x).normalized;
                var desired=(tangent-radial.normalized*Mathf.Clamp((radial.magnitude-550)*.004f,-1,1))*28-new Vector3(wind.x,0,wind.z);
                bank=Mathf.Clamp(8+Mathf.DeltaAngle((float)s.headingDegrees,Heading(desired))*.8f,-25,25);targetHeight=430;
                c.airbrake=radial.magnitude<850?Mathf.Clamp01(.2f+(p.y-targetHeight)*.018f):0;
                if(p.y<470&&radial.magnitude<800){LandingIndex=1;Set(Stage.Circuit);}
            }
            if(Phase==Stage.Circuit||Phase==Stage.Final)
            {
                Vector2 target=LandingIndex==1?new Vector2(650,550):LandingIndex==2?new Vector2(-850,550):LandingIndex==3?new Vector2(-1250,200):LandingIndex==4?new Vector2(-1200,-30):new Vector2(p.x+350,-30);
                if(LandingIndex<5&&Vector2.Distance(new Vector2(p.x,p.z),target)<120)LandingIndex++;
                if(LandingIndex>=5)Set(Stage.Final);
                bank=Track(s,target,wind,28);targetHeight=LandingIndex<=2?350:LandingIndex==3?220:LandingIndex==4?90:Mathf.Max(0,(-350-p.x)*.09f);
                c.airbrake=Mathf.Clamp01(.2f+(p.y-targetHeight)*.018f);
                if(LandingIndex>=4&&!f.soaringTraffic.SoaringRunwayClear()){Hold("Landing surface occupied. Demonstration paused; no powered go-around or relocation.");return;}
            }
            float error=(float)s.calibratedAirspeedKnots-speed;integral=Mathf.Clamp(integral+error*.015f*dt,-5,6);
            float pitch=.2f+integral+Mathf.Clamp(error*.65f,-10,9);
            if(Phase==Stage.Final&&s.altitudeAglMeters<4){c.airbrake=.25f;pitch=Mathf.Lerp(6,1,(float)s.altitudeAglMeters/4);bank=0;}
            if(Phase==Stage.Final&&s.weightOnWheels)
            {
                if(previousSink<-600||Math.Abs(s.bankDegrees)>10||!op.OnLandingSurface(p)){Hold("Unsafe or off-runway touchdown. Restart or end flight.");return;}
                c.airbrake=c.wheelBrake=1;pitch=1;bank=0;stableSeconds=s.groundSpeedKnots<1?stableSeconds+dt:0;if(stableSeconds>3)Set(Stage.Complete);
            }
            previousSink=(float)s.verticalSpeedFeetPerMinute;
            Attitude(s,pitch,bank,c);
        }
        private static float Heading(Vector3 desired)=>Mathf.Atan2(desired.x,desired.z)*Mathf.Rad2Deg;
        private static float Track(in FlightDynamicsState s,Vector2 target,Vector3 wind,float maxBank)
        {Vector2 desired=(target-new Vector2(s.positionUnityMeters.x,s.positionUnityMeters.z)).normalized*28-new Vector2(wind.x,wind.z);return Mathf.Clamp(Mathf.DeltaAngle((float)s.headingDegrees,Mathf.Atan2(desired.x,desired.y)*Mathf.Rad2Deg)*1.1f,-maxBank,maxBank);}
        private static void Attitude(in FlightDynamicsState s,float pitch,float bank,AircraftControlState c)
        {c.elevator=Mathf.Clamp(.09f*(pitch-(float)s.pitchDegrees)+.8f*s.angularVelocityBodyDegreesPerSecond.x*Mathf.Deg2Rad,-.75f,.75f);c.aileron=Mathf.Clamp(.06f*(bank-(float)s.bankDegrees)+.4f*s.angularVelocityBodyDegreesPerSecond.z*Mathf.Deg2Rad,-.6f,.6f);c.rudder=Mathf.Clamp(.04f*(float)s.sideslipDegrees-.2f*s.angularVelocityBodyDegreesPerSecond.y*Mathf.Deg2Rad,-.4f,.4f);}
        private static void FollowTow(in FlightDynamicsState s,in FlightDynamicsState tug,float separation,AircraftControlState c)
        {
            Vector3 forward=tug.velocityUnityMetersPerSecond;forward.y=0;float speed=Mathf.Max(15,forward.magnitude);forward.Normalize();
            var right=new Vector3(forward.z,0,-forward.x);var relative=tug.positionUnityMeters-s.positionUnityMeters;
            float curvature=9.80665f*Mathf.Tan((float)tug.bankDegrees*Mathf.Deg2Rad)/(speed*speed);
            float cross=Vector3.Dot(relative,right)-.5f*separation*separation*curvature;
            float rate=Vector3.Dot(tug.velocityUnityMetersPerSecond-s.velocityUnityMetersPerSecond,right);
            float bank=s.altitudeAglMeters>5?Mathf.Clamp((float)tug.bankDegrees+cross*.7f+rate*2.5f,-30,30):0;
            float climb=tug.velocityUnityMetersPerSecond.y,height=tug.positionUnityMeters.y-climb/speed*separation+8;
            float pitch=s.altitudeAglMeters>3?2.7f+Mathf.Atan2(climb,speed)*Mathf.Rad2Deg+Mathf.Clamp((height-s.positionUnityMeters.y)*.15f+(climb-s.velocityUnityMetersPerSecond.y)*.7f,-6,6):s.calibratedAirspeedKnots>30?5:2;
            if(tug.weightOnWheels&&!s.weightOnWheels&&s.altitudeAglMeters>1)pitch=.2f+Mathf.Clamp((tug.positionUnityMeters.y+1.2f-s.positionUnityMeters.y)*.6f-s.velocityUnityMetersPerSecond.y*.9f,-4,4);
            Attitude(s,pitch,bank,c);c.elevator=Mathf.Clamp(.08f*(pitch-(float)s.pitchDegrees)+.8f*s.angularVelocityBodyDegreesPerSecond.x*Mathf.Deg2Rad,-.65f,.65f);
        }
    }
}
