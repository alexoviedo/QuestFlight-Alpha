using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlight.Product;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;
using QuestFlightLab.Environment;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        [Serializable] private sealed class TowSample
        {public FlightDynamicsState glider,tug;public TowlineSnapshot line;public GroundLaunchPhase phase;public bool wingSupport;public FlightDynamicsControlSnapshot playerControls;}
        [Serializable] private sealed class TowRun
        {
            public string scope="Actual authored product native pair, provisional ground launch, isolated test glider pilot. No human or operator acceptance.";
            public string error;public bool gliderLiftOff,tugLiftOff,releaseSucceeded,trafficRolePreserved,runwayReserved,pauseHeld,resetClearedPair,c172Restored;public double peakTension,peakExtension,completedSeconds;
            public double minimumUnrelatedTrafficDistance=double.MaxValue;public List<string> captures=new List<string>();
            public List<TowSample> samples=new List<TowSample>();
            public List<SoaringViewResources> resources=new List<SoaringViewResources>();
        }
        public static void RunSoaringTowDebug()
        {
            string output=System.Environment.GetEnvironmentVariable("QF_SOARING_QA");Directory.CreateDirectory(output);var report=new TowRun();
            try
            {
                var c=LoadContext(1440,1080,new ProductionVisualQaReport());Time.timeScale=0;
                var mode=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                InvokePrivate(mode,"BeginSelection");var flight=mode.AuthoredFlightCoordinator;
                if(!flight.InitializeSelectedBackend() || !mode.PrepareGroundTow())throw new InvalidOperationException(flight.LastError+mode.LastError);
                mode.SelectFreeFlight();if(mode.State!=ProductFlightModeCoordinator.FlightModeState.FreeFlight)throw new InvalidOperationException(mode.LastError);
                mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();mode.AuthoredModePanelRoot.SetActive(false);
                foreach(var renderer in c.rig.XrOrigin.GetComponentsInChildren<Renderer>(true))renderer.forceRenderingOff=true;
                var presenter=mode.AircraftPresentation.instruments;InvokePrivate(presenter,"Awake");
                var traffic=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<AmbientTrafficDirector>(true)).Single();
                var rope=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<TowlinePresentation>(true)).Single();InvokePrivate(rope,"Awake");
                if(!traffic.Validate(out string trafficError))throw new InvalidOperationException(trafficError);
                report.trafficRolePreserved=true;
                void Capture(string id)
                {
                    InvokePrivate(presenter,"LateUpdate");InvokePrivate(rope,"LateUpdate");
                    for(int eye=0;eye<2;eye++)
                    {
                        c.camera.transform.SetPositionAndRotation(c.rig.PilotSeatAnchor.position+c.rig.transform.TransformVector(Vector3.right*(eye==0?-.032f:.032f)),c.rig.transform.rotation);
                        c.camera.fieldOfView=82;c.camera.useOcclusionCulling=false;
                        var warm=Render(c.camera,1440,1080,out _);UnityEngine.Object.DestroyImmediate(warm);
                        var image=Render(c.camera,1440,1080,out var stats);string file=id+(eye==0?"_left":"_right")+".png";
                        report.resources.Add(new SoaringViewResources{view=file,staticEstimate=CaptureBudgetWithContributions(c.camera,Path.Combine(output,file+".resources.csv")),editorFrame=stats});
                        File.WriteAllBytes(Path.Combine(output,file),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);report.captures.Add(file);
                    }
                }
                var controls=new AircraftControlState();controls.SetGliderNeutral();controls.trim=flight.aircraftDefinition.referenceTrimNoseUp;
                for(int tick=0;tick<120*130;tick++)
                {
                    var pair=flight.TowSession;var s=flight.CurrentState;var tug=pair.TugState;
                    if(tick==120*90){flight.controls.SampleTowRelease(false,true);flight.controls.SampleTowRelease(true,true);}
                    SoaringTestPilot.Follow(s,tug,controls,tick>=120*90);
                    if(!flight.StepForTest(controls,1d/120))throw new InvalidOperationException(flight.LastError);
                    s=flight.CurrentState;tug=pair.TugState;
                    if(tick%2==0)
                    {
                        traffic.StepTraffic(s,1f/60,flight.WeatherPreset);
                        report.trafficRolePreserved &= traffic.reservedNativeTug.ExternallySimulated && !traffic.reservedNativeTug.Suppressed && Vector3.Distance(traffic.reservedNativeTug.Position,tug.positionUnityMeters)<.001;
                        if(tick<120*10)report.runwayReserved|=traffic.PlayerProtectsRunway;
                        foreach(var actor in traffic.actors)if(actor!=traffic.reservedNativeTug && !actor.Suppressed)
                            report.minimumUnrelatedTrafficDistance=Math.Min(report.minimumUnrelatedTrafficDistance,Math.Min(Vector3.Distance(actor.Position,s.positionUnityMeters),Vector3.Distance(actor.Position,tug.positionUnityMeters)));
                    }
                    if(tick==1 || tick==120*15 || tick==120*35 || tick==120*75 || tick==120*92)Capture("tow_"+(tick/120).ToString("000"));
                    if(tick==120*50)
                    {
                        double before=s.simulationTimeSeconds;Time.timeScale=0;InvokePrivate(flight,"FixedUpdate");InvokePrivate(flight,"FixedUpdate");
                        report.pauseHeld=flight.CurrentState.simulationTimeSeconds==before && pair.TugState.simulationTimeSeconds==before;Time.timeScale=1;
                    }
                    report.gliderLiftOff|=!s.weightOnWheels && s.altitudeAglMeters>3;report.tugLiftOff|=!tug.weightOnWheels && tug.altitudeAglMeters>3;
                    report.peakTension=pair.PeakTensionNewtons;report.peakExtension=pair.PeakExtensionMeters;
                    report.releaseSucceeded|=pair.Line.state==TowlineState.ReleasedByGlider;
                    report.completedSeconds=s.simulationTimeSeconds;
                    if(tick%60==0)report.samples.Add(new TowSample{glider=s,tug=tug,line=pair.Line,phase=pair.LaunchPhase,wingSupport=pair.WingSupportActive,playerControls=flight.LastAppliedControls});
                }
                if(!report.gliderLiftOff||!report.tugLiftOff||!report.releaseSucceeded||!report.trafficRolePreserved||!report.runwayReserved||!report.pauseHeld)throw new InvalidOperationException("Required production tow outcome missing.");
                mode.ReturnToMainMenu();report.resetClearedPair=flight.TowSession==null;
                report.c172Restored=mode.SelectAircraft(false) && !flight.aircraftDefinition.IsGlider && FlightDynamicsAuthority.ActiveAuthorityCount==1;
                if(!report.resetClearedPair||!report.c172Restored)throw new InvalidOperationException("Pair teardown or C172 restoration failed.");
            }
            catch(Exception ex){report.error=ex.ToString();Debug.LogException(ex);}
            File.WriteAllText(Path.Combine(output,"production_tow_trace.json"),JsonUtility.ToJson(report,true));Time.timeScale=1;EditorApplication.Exit(string.IsNullOrEmpty(report.error)?0:1);
        }
    }
}
