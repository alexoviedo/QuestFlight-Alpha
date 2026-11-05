using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlight.Product;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    internal sealed class SoaringLandingTestPilot
    {
        public int Phase {get;private set;}
        private float integral;
        private double sinceRelease;
        public void Step(in FlightDynamicsState s,Vector3 wind,int sign,AircraftControlState controls)
        {
            sinceRelease+=1d/120;
            Vector3 p=s.positionUnityMeters;float bank=0,targetHeight=0;Vector2 target=Vector2.zero;
            if(Phase==0)
            {
                Vector3 radial=p-new Vector3(0,p.y,1700);Vector3 tangent=new Vector3(radial.z,0,-radial.x).normalized;
                Vector3 desired=(tangent-radial.normalized*Mathf.Clamp((radial.magnitude-550)*.004f,-1,1))*28-new Vector3(wind.x,0,wind.z);
                float heading=Mathf.Atan2(desired.x,desired.z)*Mathf.Rad2Deg;
                bank=Mathf.Clamp(8+Mathf.DeltaAngle((float)s.headingDegrees,heading)*.8f,-25,25);targetHeight=430;
                if(p.y<470&&sinceRelease>=360)Phase=1;
            }
            else
            {
                target=Phase==1?new Vector2(650*sign,550):Phase==2?new Vector2(-850*sign,550):Phase==3?new Vector2(-1250*sign,200):Phase==4?new Vector2(-1200*sign,-30):new Vector2(300*sign,-30);
                if(Phase>=5)target.x=p.x+350*sign;
                if(Phase<5&&Vector2.Distance(new Vector2(p.x,p.z),target)<120)Phase++;
                Vector2 desired=(target-new Vector2(p.x,p.z)).normalized*28-new Vector2(wind.x,wind.z);
                float heading=Mathf.Atan2(desired.x,desired.y)*Mathf.Rad2Deg;
                bank=Mathf.Clamp(Mathf.DeltaAngle((float)s.headingDegrees,heading)*1.1f,-28,28);
                targetHeight=Phase<=2?350:Phase==3?220:Phase==4?90:Mathf.Max(0,(-350*sign-p.x)*sign*.09f);
            }
            float error=(float)s.calibratedAirspeedKnots-48;integral=Mathf.Clamp(integral+error*.015f/120,-5,6);
            float pitch=.2f+integral+Mathf.Clamp(error*.65f,-10,9);
            controls.airbrake=Mathf.Clamp01(.2f+(p.y-targetHeight)*.018f);
            if(Phase==0&&sinceRelease<360)controls.airbrake=0;
            if(Phase>=5&&s.altitudeAglMeters<4){controls.airbrake=.25f;pitch=Mathf.Lerp(6,1,(float)s.altitudeAglMeters/4);bank=0;}
            if(s.weightOnWheels && Phase>=5){controls.wheelBrake=1;controls.airbrake=1;pitch=1;bank=0;}
            controls.elevator=Mathf.Clamp(.09f*(pitch-(float)s.pitchDegrees)+.8f*s.angularVelocityBodyDegreesPerSecond.x*Mathf.Deg2Rad,-.75f,.75f);
            controls.aileron=Mathf.Clamp(.06f*(bank-(float)s.bankDegrees)+.4f*s.angularVelocityBodyDegreesPerSecond.z*Mathf.Deg2Rad,-.6f,.6f);
            controls.rudder=Mathf.Clamp(.04f*(float)s.sideslipDegrees-.2f*s.angularVelocityBodyDegreesPerSecond.y*Mathf.Deg2Rad,-.4f,.4f);
        }
    }

    public static partial class ProductionVisualQaBatchRunner
    {
        private static bool completeFlightWithoutWake;
        public static void RunSoaringCompleteFlightNoWake(){completeFlightWithoutWake=true;RunSoaringCompleteFlight();}
        [Serializable] private sealed class CompleteFlightSample {public FlightDynamicsState glider,tug;public SoaringOperationPhase operation;public GroundLaunchPhase tugPhase;public int testPhase;public FlightDynamicsControlSnapshot controls;public float margin;public bool runwayClear,playerFinal;}
        [Serializable] private sealed class CompleteFlightCase
        {
            public int runway,emergencySuppressions;public string error;public bool passed,checks,release,gliderLanded,tugLanded;public double seconds,peakTension;
            public float minimumUnrelatedSeparation=100000;public bool tugUnsafeTouchdown;
            public string closestActor;public Vector3 closestActorPosition,closestPlayerPosition,closestTugPosition;
            public bool preparationLesson,aerotowLesson,circuitLesson;
            public string lessonFailure;
            public List<CompleteFlightSample> samples=new List<CompleteFlightSample>();
        }
        public static void RunSoaringCompleteFlight()
        {
            string output=System.Environment.GetEnvironmentVariable("QF_SOARING_QA");Directory.CreateDirectory(output);bool pass=true;
            foreach(int runway in new[]{8,26})
            {
                var report=new CompleteFlightCase{runway=runway};
                try
                {
                    var c=LoadContext(640,480,new ProductionVisualQaReport());Time.timeScale=0;
                    var mode=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                    InvokePrivate(mode,"BeginSelection");var flight=mode.AuthoredFlightCoordinator;
                    if(!flight.InitializeSelectedBackend()||!mode.SelectAircraft(true))throw new InvalidOperationException(flight.LastError+mode.LastError);
                    var operation=flight.soaringOperations[runway==8?0:1];
                    if(completeFlightWithoutWake){operation=UnityEngine.Object.Instantiate(operation);operation.weather=UnityEngine.Object.Instantiate(operation.weather);operation.weather.wakeEnabled=false;}
                    if(!flight.TryPrepareSoaringOperation(operation))throw new InvalidOperationException(flight.LastError+mode.LastError);
                    mode.SelectFreeFlight();mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();
                    var controls=new AircraftControlState();controls.SetGliderNeutral();controls.trim=flight.aircraftDefinition.referenceTrimNoseUp;
                    var preparation=new SoaringLessonWitness(SoaringLessonWitness.Load(1),flight);
                    var aerotow=new SoaringLessonWitness(SoaringLessonWitness.Load(2),flight);
                    SoaringLessonWitness circuit=null;
                    var landing=new SoaringLandingTestPilot();bool rehooked=false,tugAirborne=false,tugPreviousWow=true;double releasedAt=-1,tugPreviousSink=0;
                    for(int tick=0;tick<120*1800;tick++)
                    {
                        var pair=flight.TowSession;var s=flight.CurrentState;var op=flight.Operation;
                        if(op.Phase==SoaringOperationPhase.ControlsCheck)
                        {
                            int check=tick/120;controls.aileron=controls.elevator=controls.rudder=check%2==0?-.5f:.5f;
                            controls.airbrake=controls.wheelBrake=1;controls.trim=.17f;
                            if(tick==300){flight.controls.SampleTowRelease(false,true);flight.controls.SampleTowRelease(true,true);}
                        }
                        else if(op.Phase==SoaringOperationPhase.CrewRehook && !rehooked)
                        {
                            Time.timeScale=0;if(!flight.TryCrewReconnect())throw new InvalidOperationException("Crew reconnection failed.");
                            Time.timeScale=1;rehooked=true;report.checks=true;controls.SetGliderNeutral();controls.trim=flight.aircraftDefinition.referenceTrimNoseUp;continue;
                        }
                        else if(op.Phase==SoaringOperationPhase.Ready){flight.TryClearSoaringLaunch();}
                        else if(releasedAt<0)
                        {
                            SoaringTestPilot.FollowRoute(s,pair.TugState,controls);
                            if(op.Phase==SoaringOperationPhase.ReleaseReady){flight.controls.SampleTowRelease(false,true);flight.controls.SampleTowRelease(true,true);releasedAt=s.simulationTimeSeconds;}
                        }
                        else landing.Step(s,flight.SampleWind(s.positionUnityMeters,s.simulationTimeSeconds),runway==8?1:-1,controls);
                        if(!flight.StepForTest(controls,1d/120))throw new InvalidOperationException(flight.LastError);
                        if(tick%2==0)flight.soaringTraffic.StepTraffic(flight.CurrentState,1f/60,flight.WeatherPreset);
                        pair=flight.TowSession;op=flight.Operation;s=flight.CurrentState;
                        preparation.Observe(flight);aerotow.Observe(flight);
                        if(runway==8 && landing.Phase>0){if(circuit==null)circuit=new SoaringLessonWitness(SoaringLessonWitness.Load(6),flight);circuit.Observe(flight);}
                        report.preparationLesson=preparation.Complete;report.aerotowLesson=aerotow.Complete;report.circuitLesson=runway==26||circuit!=null&&circuit.Complete;
                        report.lessonFailure=preparation.Failure??aerotow.Failure??circuit?.Failure;
                        var tug=pair.TugState;tugAirborne|=!tug.weightOnWheels&&tug.altitudeAglMeters>10;
                        if(tugAirborne && tug.weightOnWheels && !tugPreviousWow)
                            report.tugUnsafeTouchdown|=!operation.OnLandingSurface(tug.positionUnityMeters)||tugPreviousSink<-600||Math.Abs(tug.bankDegrees)>10;
                        tugPreviousWow=tug.weightOnWheels;tugPreviousSink=tug.verticalSpeedFeetPerMinute;
                        report.emergencySuppressions=flight.soaringTraffic.EmergencySuppressions;
                        if(tick%120==0)foreach(var actor in flight.soaringTraffic.actors)
                            if(actor!=flight.soaringTraffic.reservedNativeTug&&!actor.Suppressed)
                            {
                                float distance=Mathf.Min(Vector3.Distance(actor.Position,s.positionUnityMeters),Vector3.Distance(actor.Position,tug.positionUnityMeters));
                                if(distance<report.minimumUnrelatedSeparation){report.minimumUnrelatedSeparation=distance;report.closestActor=actor.aircraftId;report.closestActorPosition=actor.Position;report.closestPlayerPosition=s.positionUnityMeters;report.closestTugPosition=tug.positionUnityMeters;}
                            }
                        report.release|=pair.Line.state==TowlineState.ReleasedByGlider && releasedAt>=0;
                        if(releasedAt<0 && (pair.Line.state==TowlineState.BrokenAtGlider||pair.Line.state==TowlineState.BrokenAtTug))throw new InvalidOperationException("Nominal tow test pilot overloaded weak link at "+s.simulationTimeSeconds);
                        report.gliderLanded|=op.Phase==SoaringOperationPhase.Landed;
                        report.tugLanded|=pair.LaunchPhase==GroundLaunchPhase.Parked && pair.TugState.groundSpeedKnots<1;
                        report.seconds=s.simulationTimeSeconds;report.peakTension=pair.PeakTensionNewtons;
                        if(tick%120==0)report.samples.Add(new CompleteFlightSample{glider=s,tug=pair.TugState,operation=op.Phase,tugPhase=pair.LaunchPhase,testPhase=landing.Phase,controls=flight.LastAppliedControls,margin=op.ReturnEstimate.marginMeters,
                            runwayClear=flight.soaringTraffic.SoaringRunwayClear(),playerFinal=AmbientTrafficDirector.ProtectsRunway(s.positionUnityMeters,s.velocityUnityMetersPerSecond,s.weightOnWheels)});
                        if(op.RequiresPause)throw new InvalidOperationException(op.StopReason);
                        if(report.gliderLanded&&report.tugLanded&&report.circuitLesson)break;
                    }
                    report.passed=report.checks&&report.release&&report.gliderLanded&&report.tugLanded&&!report.tugUnsafeTouchdown&&report.emergencySuppressions==0&&report.minimumUnrelatedSeparation>30&&report.preparationLesson&&report.aerotowLesson&&report.circuitLesson;
                    mode.ReturnToMainMenu();mode.SelectAircraft(false);
                }
                catch(Exception ex){report.error=ex.ToString();Debug.LogException(ex);}
                pass&=report.passed;File.WriteAllText(Path.Combine(output,"complete_runway"+runway+".json"),JsonUtility.ToJson(report,true));
            }
            Time.timeScale=1;EditorApplication.Exit(pass?0:1);
        }
    }
}
