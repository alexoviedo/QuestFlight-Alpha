using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    // Observes the real lesson predicates against native outcomes. It never
    // changes a state, grants credit, or enters the player assembly.
    internal sealed class SoaringLessonWitness
    {
        public readonly TrainingLessonDefinition Lesson;
        public readonly SoaringTrainingProgram Program=new SoaringTrainingProgram();
        public int StageIndex {get;private set;}
        public bool Complete=>StageIndex==Lesson.stages.Length;
        public string Failure=>Program.Failure ?? (!Complete&&Program.TimedOut?"Timed out at "+Lesson.stages[StageIndex].task:null);
        public TrainingTask Task=>Complete?TrainingTask.SoaringStop:Lesson.stages[StageIndex].task;
        private int samples;
        public SoaringLessonWitness(TrainingLessonDefinition lesson,FlightDynamicsCoordinator flight)
        {Lesson=lesson;Program.BeginAttempt(flight.CurrentState);Program.BeginStage(flight.CurrentState,flight.LastAppliedControls.trim,flight);}
        public void Observe(FlightDynamicsCoordinator flight)
        {
            if(Complete||Failure!=null)return;
            Program.ObserveNative(flight.CurrentState,flight);if(++samples%12!=0)return;
            Program.Step(Lesson.stages[StageIndex],flight.CurrentState,flight.LastAppliedControls,.1f,flight);
            if(Program.Complete){StageIndex++;if(!Complete)Program.BeginStage(flight.CurrentState,flight.LastAppliedControls.trim,flight);}
        }
        public static TrainingLessonDefinition Load(int number)=>AssetDatabase.LoadAssetAtPath<TrainingLessonDefinition>(
            AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:TrainingLessonDefinition",new[]{SoaringTrainingAuthoring.Folder})
                .Single(g=>Path.GetFileName(AssetDatabase.GUIDToAssetPath(g)).StartsWith(number.ToString("00")+"_"))));
    }

    internal sealed class SoaringSkillTestPilot
    {
        private float pitchIntegral;
        private bool outwardEast;
        public void Step(SoaringLessonWitness witness,FlightDynamicsCoordinator flight,AircraftControlState c)
        {
            var s=flight.CurrentState;var task=witness.Task;float targetSpeed=50,bank=0,heading=90;
            var wind=flight.SampleWind(s.positionUnityMeters,s.simulationTimeSeconds);
            c.airbrake=c.wheelBrake=0;
            if(task==TrainingTask.SoaringTrim)c.trim=.15f;
            if(task==TrainingTask.SoaringAirbrake)c.airbrake=.7f;
            if(task==TrainingTask.SoaringLift||task==TrainingTask.SoaringEnergyGain)
            {
                targetSpeed=40;var source=flight.SoaringAtmosphere.Source(0);
                var background=flight.SampleAtmosphereComponents(s.positionUnityMeters,s.simulationTimeSeconds).background;
                var center=SoaringAtmosphereSession.Center(source,s.positionUnityMeters.y,new Vector2(background.x,background.z));
                Vector3 radial=s.positionUnityMeters-new Vector3(center.x,s.positionUnityMeters.y,center.y);
                Vector3 tangent=new Vector3(radial.z,0,-radial.x).normalized;
                Vector3 desired=(tangent-radial.normalized*Mathf.Clamp((radial.magnitude-100)*.006f,-1,1))*25-new Vector3(wind.x,0,wind.z);
                heading=Mathf.Atan2(desired.x,desired.z)*Mathf.Rad2Deg;
                float groundSpeed=new Vector2(s.velocityUnityMetersPerSecond.x,s.velocityUnityMetersPerSecond.z).magnitude;
                bank=Mathf.Clamp(Mathf.Atan2(groundSpeed*groundSpeed,9.80665f*100)*Mathf.Rad2Deg+Mathf.DeltaAngle((float)s.headingDegrees,heading)*.7f,-40,40);
            }
            else if(task==TrainingTask.SoaringRidgeTrack)
            {
                targetSpeed=40;
                Vector3 desired=new Vector3(Mathf.Clamp((-6150-s.positionUnityMeters.x)*.05f,-5,5),0,25)-new Vector3(wind.x,0,wind.z);
                heading=Mathf.Atan2(desired.x,desired.z)*Mathf.Rad2Deg;
                bank=Mathf.Clamp(Mathf.DeltaAngle((float)s.headingDegrees,heading)*1.2f,-30,30);
            }
            else if(task==TrainingTask.SoaringOutwardTurn)
            {
                targetSpeed=40;if(Mathf.Abs(Mathf.DeltaAngle((float)s.headingDegrees,90))<20)outwardEast=true;
                heading=outwardEast?180:90;bank=28;
                if(outwardEast&&Mathf.Abs(Mathf.DeltaAngle((float)s.headingDegrees,180))<10)bank=0;
            }
            else
            {
                if(task==TrainingTask.SoaringRidgeExit)targetSpeed=40;
                bank=Mathf.Clamp(Mathf.DeltaAngle((float)s.headingDegrees,heading),-25,25);
            }
            float speedError=(float)s.calibratedAirspeedKnots-targetSpeed;
            pitchIntegral=Mathf.Clamp(pitchIntegral+speedError*.03f/120,-6,8);
            float pitch=(targetSpeed==40?2:.2f)+pitchIntegral+Mathf.Clamp(speedError*.7f,-8,8);
            if(task==TrainingTask.SoaringSlip&&!witness.Program.SlipReadyForRecovery)bank=-8;
            c.elevator=Mathf.Clamp(.09f*(pitch-(float)s.pitchDegrees)+.8f*s.angularVelocityBodyDegreesPerSecond.x*Mathf.Deg2Rad,-.7f,.7f);
            c.aileron=Mathf.Clamp(.06f*(bank-(float)s.bankDegrees)+.4f*s.angularVelocityBodyDegreesPerSecond.z*Mathf.Deg2Rad,-.6f,.6f);
            c.rudder=task==TrainingTask.SoaringSlip&&!witness.Program.SlipReadyForRecovery?.55f:Mathf.Clamp(.04f*(float)s.sideslipDegrees-.2f*s.angularVelocityBodyDegreesPerSecond.y*Mathf.Deg2Rad,-.4f,.4f);
        }
    }

    public static partial class ProductionVisualQaBatchRunner
    {
        [Serializable] private sealed class LessonEvidence
        {
            public int lesson,stages;public bool passed,expectedFailure;public string error,task;
            public double seconds;public float minAgl=100000;
            public List<FlightDynamicsState> samples=new List<FlightDynamicsState>();
        }
        public static void RunSoaringLessonOutcomes()
        {
            string output=System.Environment.GetEnvironmentVariable("QF_SOARING_QA");Directory.CreateDirectory(output);bool pass=true;
            foreach(int number in System.Environment.GetEnvironmentVariable("QF_SOARING_LESSON_ONLY")=="3"?new[]{3}:new[]{3,4,5,7,-3})
            {
                var report=new LessonEvidence{lesson=Math.Abs(number),expectedFailure=number<0};
                try
                {
                    var context=LoadContext(640,480,new ProductionVisualQaReport());Time.timeScale=0;
                    var mode=context.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                    InvokePrivate(mode,"BeginSelection");var flight=mode.AuthoredFlightCoordinator;
                    if(!flight.InitializeSelectedBackend()||!mode.SelectAircraft(true))throw new InvalidOperationException(flight.LastError+mode.LastError);
                    var lesson=SoaringLessonWitness.Load(Math.Abs(number));var program=new SoaringTrainingProgram();
                    if(!program.Prepare(lesson,flight))throw new InvalidOperationException("Actual authored lesson preparation failed: "+flight.LastError);
                    mode.SelectFreeFlight();mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();
                    var witness=new SoaringLessonWitness(lesson,flight);var pilot=new SoaringSkillTestPilot();
                    var controls=new AircraftControlState();controls.SetGliderNeutral();controls.trim=lesson.initialTrim;bool rehooked=false;
                    for(int tick=0;tick<120*1000;tick++)
                    {
                        var s=flight.CurrentState;var op=flight.Operation;
                        if(number==7)
                        {
                            if(op.Phase==SoaringOperationPhase.ControlsCheck)
                            {
                                controls.aileron=controls.elevator=controls.rudder=tick/120%2==0?-.5f:.5f;
                                controls.airbrake=controls.wheelBrake=1;controls.trim=.17f;
                                if(tick==300){flight.controls.SampleTowRelease(false,true);flight.controls.SampleTowRelease(true,true);}
                            }
                            else if(op.Phase==SoaringOperationPhase.CrewRehook&&!rehooked)
                            {Time.timeScale=0;if(!flight.TryCrewReconnect())throw new InvalidOperationException("Crew reconnect failed.");Time.timeScale=1;rehooked=true;controls.SetGliderNeutral();controls.trim=lesson.initialTrim;}
                            else if(op.Phase==SoaringOperationPhase.Ready)flight.TryClearSoaringLaunch();
                            else if(op.Phase==SoaringOperationPhase.Launch&&s.groundSpeedKnots>3)
                            {if(!flight.TryAbortSoaringGroundLaunch())throw new InvalidOperationException("Supported ground abort refused.");controls.airbrake=controls.wheelBrake=1;}
                        }
                        else if(number<0)
                        {
                            // Real native uncontrolled descent outside the runway
                            // must fail the lesson; no synthetic touchdown data.
                            controls.airbrake=1;controls.elevator=-.3f;
                        }
                        else pilot.Step(witness,flight,controls);
                        if(!flight.StepForTest(controls,1d/120))throw new InvalidOperationException(flight.LastError);
                        if(tick%2==0)flight.soaringTraffic.StepTraffic(flight.CurrentState,1f/60,flight.WeatherPreset);
                        witness.Observe(flight);s=flight.CurrentState;
                        report.seconds=s.simulationTimeSeconds;report.minAgl=Mathf.Min(report.minAgl,(float)s.altitudeAglMeters);report.stages=witness.StageIndex;report.task=witness.Task.ToString();
                        if(tick%120==0)report.samples.Add(s);
                        if(witness.Failure!=null){report.error=witness.Failure;report.passed=number<0;break;}
                        if(witness.Complete){report.passed=number>0;break;}
                    }
                    mode.ReturnToMainMenu();mode.SelectAircraft(false);
                }
                catch(Exception ex){report.error=ex.ToString();Debug.LogException(ex);}
                pass&=report.passed;File.WriteAllText(Path.Combine(output,"lesson_"+number+".json"),JsonUtility.ToJson(report,true));
            }
            Time.timeScale=1;EditorApplication.Exit(pass?0:1);
        }
    }
}
