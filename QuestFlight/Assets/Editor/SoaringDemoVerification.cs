using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using QuestFlightLab.Runtime;
using QuestFlight.Product;
using QuestFlightLab.Flight;
using QuestFlightLab.Input;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Soaring;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        [Serializable] private sealed class SoaringDemoSample {public float time,x,y,z,speed,bank,sink,thermalGain,margin,birdX,birdY,birdZ;public string phase;public int scenic,landing;}
        [Serializable] private sealed class SoaringDemoCase {public string scenario;public bool requestedReturn,takenControls,handedBack,expectedHold;public int guidanceHz=72;public double guidanceMilliseconds,maximumGuidanceMilliseconds;public long guidanceAllocatedBytes;public int guidanceCalls;public bool shortFlight,passed,released,landed,tugParked,eagleEncounter,eagleCue;public float nearestEagle=99999;public int maxBirds,maxBirdTriangles;public List<SoaringViewResources> resources=new List<SoaringViewResources>();public List<string> visiblePois=new List<string>();public string error;public double seconds;public float gain;public int scenery;public List<SoaringDemoSample> samples=new List<SoaringDemoSample>();}
        public static void RunSoaringDemo()
        {
            var output=System.Environment.GetEnvironmentVariable("QF_SOARING_QA");Directory.CreateDirectory(output);bool passed=true;
            SoaringDemoAuthoring.Author();
            bool capture=System.Environment.GetEnvironmentVariable("QF_SOARING_CAPTURE")=="1";
            foreach(string scenario in (System.Environment.GetEnvironmentVariable("QF_SOARING_SCENARIOS")??"full,short").Split(','))
            {
                bool shortFlight=scenario!="full";
                var report=new SoaringDemoCase{shortFlight=shortFlight,scenario=scenario};
                try
                {
                    var context=LoadContext(1280,960,new ProductionVisualQaReport());Time.timeScale=0;
                    using var sdkPreview=new ProductionVisualQaSettledSdkPreview(context.rig.transform);
                    var mode=context.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                    InvokePrivate(mode,"BeginSelection");var flight=mode.AuthoredFlightCoordinator;
                    if(!flight.InitializeSelectedBackend()||!mode.SelectAircraft(true))throw new Exception(flight.LastError+mode.LastError);
                    var originalWeather=mode.AuthoredDemoPilot.SoaringProfile.operation.weather;
                    SoaringWeatherDefinition weakWeather=null;
                    if(scenario=="weak_lift")
                    {weakWeather=UnityEngine.Object.Instantiate(originalWeather);for(int i=0;i<weakWeather.sources.Length;i++){var source=weakWeather.sources[i];source.convectiveScale=.4f;weakWeather.sources[i]=source;}mode.AuthoredDemoPilot.SoaringProfile.operation.weather=weakWeather;}
                    try{if(shortFlight)mode.SelectShortShowcase();else mode.SelectDemo();}
                    finally{mode.AuthoredDemoPilot.SoaringProfile.operation.weather=originalWeather;}
                    mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();
                    InvokePrivate(mode.AircraftPresentation.instruments,"Awake");
                    var pilot=mode.AuthoredDemoPilot;if(!pilot.IsRunning||!pilot.IsSoaringDemo)throw new Exception("Demo failed to start "+mode.LastError);
                    flight.EditorVerificationExternalClock=true;
                    var controls=new AircraftControlState();controls.SetGliderNeutral();var target=new AircraftControlState();target.SetGliderNeutral();
                    var birds=context.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<HabitatBirdDirector>(true)).Single();birds.pilotCamera=context.camera;birds.ResetSession();
                    var narration=context.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductDemoNarration>(true)).Single();
                    var ns=new SerializedObject(narration);ns.FindProperty("pilotCamera").objectReferenceValue=context.camera;ns.ApplyModifiedPropertiesWithoutUndo();
                    var cues=(ProductDemoNarration.Cue[])typeof(ProductDemoNarration).GetField("cues",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(narration);
                    void Pair(string id)
                    {
                        if(!capture||scenario!="full"&&scenario!="short")return;
                        var input=pilot.AuthoredProductInput;input.SetFlightLessonGuidance(true,ProductControlInput.ToFlightLessonCommand(controls));
                        typeof(ProductControlInput).GetMethod("UpdateGliderControls",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(input,new object[]{null,true});
                        InvokePrivate(mode.AircraftPresentation.instruments,"LateUpdate");
                        for(int eye=0;eye<2;eye++)
                        {
                            context.camera.transform.SetPositionAndRotation(context.rig.PilotSeatAnchor.position+context.rig.transform.right*(eye==0?-.032f:.032f),context.rig.transform.rotation);
                            var warm=Render(context.camera,1280,960,out _);UnityEngine.Object.DestroyImmediate(warm);
                            var shot=Render(context.camera,1280,960,out var stats);string name=(shortFlight?"short_":"full_")+id+(eye==0?"_left":"_right")+".png";
                            File.WriteAllBytes(Path.Combine(output,name),shot.EncodeToPNG());UnityEngine.Object.DestroyImmediate(shot);
                            report.resources.Add(new SoaringViewResources{view=name,staticEstimate=CaptureBudgetWithContributions(context.camera,Path.Combine(output,name+".resources.csv")),editorFrame=stats});
                        }
                    }
                    if(capture)
                    {
                        mode.AuthoredNavigation.EditorVerificationFocus=true;mode.AuthoredNavigation.OpenMenu();
                        typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(mode.AuthoredNavigation,ProductAppNavigation.Page.SoaringDemo);mode.AuthoredNavigation.RefreshMenu();
                        foreach(var text in context.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<TMPro.TextMeshPro>(true)))text.ForceMeshUpdate(true,true);
                        Pair("menu");InvokePrivate(mode.AuthoredNavigation,"CompleteResume");mode.AuthoredModePanelRoot.SetActive(false);
                    }
                    double nextGuidance=0,manualUntil=0;
                    for(int tick=0;tick<120*2400;tick++)
                    {
                        if(scenario=="early_return"&&!report.requestedReturn&&pilot.SoaringGuidance.Phase==SoaringDemoGuidance.Stage.Sightseeing)
                        {if(!pilot.ResumeDemoControl(true))throw new Exception("Early return rejected");report.requestedReturn=true;}
                        if(scenario=="handover"&&!report.takenControls&&pilot.SoaringGuidance.CanTakeControl(flight.CurrentState))
                        {pilot.TakeUserControl();report.takenControls=pilot.UserHasControls;manualUntil=flight.CurrentState.simulationTimeSeconds+3;}
                        if(report.takenControls&&!report.handedBack&&flight.CurrentState.simulationTimeSeconds>=manualUntil)
                        {report.handedBack=pilot.ResumeDemoControl(false);if(!report.handedBack)throw new Exception("Safe handback rejected");}
                        if(scenario=="occupied"&&pilot.SoaringGuidance.LandingIndex>=4)
                        {var obstruction=flight.CurrentState;obstruction.positionUnityMeters=new Vector3(0,1,-30);obstruction.weightOnWheels=true;obstruction.velocityUnityMetersPerSecond=Vector3.zero;flight.soaringTraffic.actors.First(a=>a!=flight.soaringTraffic.reservedNativeTug&&!a.sailplane).SetNativeSnapshot(obstruction,obstruction,1);}
                        if(tick/120d+1e-8>=nextGuidance)
                        {nextGuidance+=1d/72;long timer=System.Diagnostics.Stopwatch.GetTimestamp(),allocated=GC.GetAllocatedBytesForCurrentThread();if(pilot.TryEvaluateGuidance(flight.CurrentState,1f/72,out var command,out var phase)){ProductControlInput.CopyFlightLessonCommandInto(command,target);ProductDemoPilot.RateLimitControlsInto(target,controls,1f/72);}
                            double milliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-timer)*1000d/System.Diagnostics.Stopwatch.Frequency;report.guidanceMilliseconds+=milliseconds;report.maximumGuidanceMilliseconds=Math.Max(report.maximumGuidanceMilliseconds,milliseconds);report.guidanceAllocatedBytes+=GC.GetAllocatedBytesForCurrentThread()-allocated;report.guidanceCalls++;}
                        if(pilot.NeedsAssistance){if(scenario=="occupied"&&pilot.SoaringGuidance.Failure.StartsWith("Landing surface occupied")){report.expectedHold=Time.timeScale==0;break;}throw new Exception(pilot.SoaringGuidance.Failure);}
                        if(!flight.StepForTest(controls,1d/120))throw new Exception(flight.LastError);
                        if(tick%2==0)flight.soaringTraffic.StepTraffic(flight.CurrentState,1f/60,flight.WeatherPreset);
                        var s=flight.CurrentState;var guidance=pilot.SoaringGuidance;
                        if(tick%2==0){SetAircraftPose(context,s.positionUnityMeters,s.rotationUnity);context.camera.transform.SetPositionAndRotation(context.rig.PilotSeatAnchor.position,context.rig.transform.rotation);birds.Step(1f/60);report.eagleEncounter|=birds.EncounterStarted;bool cueNow=birds.TryGetEagleCue(out _);if(cueNow&&!report.eagleCue)Pair("eagle");report.eagleCue|=cueNow;report.maxBirds=Mathf.Max(report.maxBirds,birds.VisibleBirds);report.maxBirdTriangles=Mathf.Max(report.maxBirdTriangles,birds.VisibleTriangles);report.nearestEagle=Mathf.Min(report.nearestEagle,Vector3.Distance(birds.Eagle.transform.position,s.positionUnityMeters));}
                        report.released|=shortFlight||flight.TowSession?.RopeState==TowlineState.ReleasedByGlider;
                        report.tugParked|=shortFlight||flight.TowSession?.LaunchPhase==GroundLaunchPhase.Parked;
                        report.landed=guidance.Phase==SoaringDemoGuidance.Stage.Complete;
                        report.seconds=s.simulationTimeSeconds;report.gain=guidance.ThermalGainMeters;report.scenery=guidance.ScenicIndex;
                        if(tick%120==0)foreach(var cue in cues)if((guidance.Phase==SoaringDemoGuidance.Stage.Thermal||guidance.Phase==SoaringDemoGuidance.Stage.Sightseeing)&&cue.id.StartsWith("sgs_")&&cue.distance>0&&narration.CueVisible(cue)&&!report.visiblePois.Contains(cue.id)){report.visiblePois.Add(cue.id);if(guidance.Phase==SoaringDemoGuidance.Stage.Thermal||guidance.Phase==SoaringDemoGuidance.Stage.Sightseeing)Pair(cue.id);}
                        if(tick%120==0)report.samples.Add(new SoaringDemoSample{birdX=birds.Eagle.transform.position.x,birdY=birds.Eagle.transform.position.y,birdZ=birds.Eagle.transform.position.z,time=(float)s.simulationTimeSeconds,x=s.positionUnityMeters.x,y=s.positionUnityMeters.y,z=s.positionUnityMeters.z,speed=(float)s.calibratedAirspeedKnots,bank=(float)s.bankDegrees,sink=(float)s.verticalSpeedFeetPerMinute,phase=guidance.Phase.ToString(),thermalGain=guidance.ThermalGainMeters,scenic=guidance.ScenicIndex,landing=guidance.LandingIndex,margin=flight.Operation?.ReturnEstimate.marginMeters??0});
                        if(flight.RecoveryHold)throw new Exception(flight.Operation?.StopReason??flight.LastError);
                        if(report.landed){Pair("landed");break;}
                    }
                    report.passed=scenario=="weak_lift"?report.landed&&pilot.SoaringGuidance.EarlyReturn&&report.gain<150&&flight.soaringTraffic.EmergencySuppressions==0:scenario=="occupied"?report.expectedHold:report.released&&report.landed&&report.gain>=pilot.SoaringProfile.thermalGain&&
                        (scenario=="early_return"?report.requestedReturn:report.scenery==(shortFlight?pilot.SoaringProfile.shortPoints.Length:pilot.SoaringProfile.scenicPoints.Length))&&
                        (scenario=="handover"?report.handedBack:true)&&report.eagleCue&&report.tugParked&&flight.soaringTraffic.EmergencySuppressions==0;
                    mode.ReturnToMainMenu();mode.SelectAircraft(false);
                }
                catch(Exception ex){report.error=ex.ToString();Debug.LogException(ex);}
                passed&=report.passed;File.WriteAllText(Path.Combine(output,scenario+".json"),JsonUtility.ToJson(report,true));
                Debug.Log("[SoaringDemo] short="+shortFlight+" pass="+report.passed+" seconds="+report.seconds+" gain="+report.gain+" scenery="+report.scenery+" error="+report.error);
            }
            Time.timeScale=1;EditorApplication.Exit(passed?0:1);
        }
    }
}
