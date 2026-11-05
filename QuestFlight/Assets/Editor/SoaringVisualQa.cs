using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using QuestFlight.Product;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Runtime;
using UnityEngine;
using UnityEditor;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        [Serializable] private sealed class SoaringQaEvidence
        {
            public string scope="Editor production mode/native path and stereo preview. Not Meta XR Simulator or physical Quest timing.";
            public string payloadIdentity,error;
            public bool switchedRepeatedly,calibrationPreserved,noDuplicateAuthority,engineFree,landed;
            public double finalSimulationTime,maximumEnergyGainMeters;
            public List<FlightDynamicsState> samples=new List<FlightDynamicsState>();
            public List<string> captures=new List<string>();
            public List<SoaringViewResources> resources=new List<SoaringViewResources>();
        }
        [Serializable] private sealed class SoaringViewResources
        {public string view;public RenderBudgetSnapshot staticEstimate;public EditorFrameRenderStats editorFrame;}
        public static void RunSoaringVisualQa()
        {
            string output=System.Environment.GetEnvironmentVariable("QF_SOARING_QA");
            if(string.IsNullOrEmpty(output))throw new InvalidOperationException("QF_SOARING_QA external directory required.");
            Directory.CreateDirectory(output);var evidence=new SoaringQaEvidence();
            try
            {
                var c=LoadContext(1440,1080,new ProductionVisualQaReport());Time.timeScale=0;
                var mode=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                var flight=mode.AuthoredFlightCoordinator;
                InvokePrivate(mode,"BeginSelection");
                if(!flight.InitializeSelectedBackend())throw new InvalidOperationException(flight.LastError);
                var calibration=c.rig.UserViewCalibrationOffset.localPosition;var calibrationRotation=c.rig.UserViewCalibrationOffset.localRotation;
                for(int n=0;n<3;n++)
                {
                    if(!mode.SelectAircraft(true)||!mode.SelectAircraft(false))throw new InvalidOperationException("Aircraft selection failed: "+mode.LastError);
                }
                evidence.switchedRepeatedly=true;
                if(!mode.SelectAircraft(true))throw new InvalidOperationException(mode.LastError);
                evidence.calibrationPreserved=calibration==c.rig.UserViewCalibrationOffset.localPosition && calibrationRotation==c.rig.UserViewCalibrationOffset.localRotation;
                evidence.noDuplicateAuthority=FlightDynamicsAuthority.ActiveAuthorityCount==1;
                mode.SelectFreeFlight();
                if(mode.State!=ProductFlightModeCoordinator.FlightModeState.FreeFlight)throw new InvalidOperationException("Manual mode failed: "+mode.LastError);
                mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();mode.AuthoredModePanelRoot.SetActive(false);
                // No simulated hand/controller pose is available in this
                // offline preview. Suppress their untracked bind-pose meshes;
                // interaction visibility is checked in the simulator separately.
                foreach(var renderer in c.rig.XrOrigin.GetComponentsInChildren<Renderer>(true))renderer.forceRenderingOff=true;
                var presenter=mode.AircraftPresentation.instruments;InvokePrivate(presenter,"Awake");
                InvokePrivate(presenter,"LateUpdate");
                evidence.payloadIdentity=flight.NativePayloadIdentity;
                // Aircraft position comes only from native state. The separate
                // editor QA camera follows the nominal authored eye, not OpenXR.
                void Capture(string id,Vector3 localOffset,Quaternion relative,float fov)
                {
                    for(int eye=0;eye<2;eye++)
                    {
                        c.camera.transform.SetPositionAndRotation(c.rig.PilotSeatAnchor.position+c.rig.transform.TransformVector(localOffset+Vector3.right*(eye==0?-.032f:.032f)),c.rig.transform.rotation*relative);
                        c.camera.fieldOfView=fov;c.camera.useOcclusionCulling=false;
                        var warm=Render(c.camera,1440,1080,out _);UnityEngine.Object.DestroyImmediate(warm);
                        var image=Render(c.camera,1440,1080,out var stats);string file=id+(eye==0?"_left":"_right")+".png";
                        evidence.resources.Add(new SoaringViewResources{view=file,staticEstimate=CaptureBudgetWithContributions(c.camera,Path.Combine(output,file+".resources.csv")),editorFrame=stats});
                        File.WriteAllBytes(Path.Combine(output,file),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);evidence.captures.Add(file);
                    }
                }
                Capture("normal_seat",Vector3.zero,Quaternion.identity,82);
                Capture("panel_controls",Vector3.zero,Quaternion.Euler(30,0,0),75);
                Capture("left_strut",Vector3.zero,Quaternion.Euler(0,-70,0),82);
                Capture("right_lean",new Vector3(.12f,.04f,.08f),Quaternion.Euler(0,50,0),82);
                Capture("left_controls",new Vector3(-.05f,0,.1f),Quaternion.Euler(45,-40,0),75);
                Capture("rear_tandem",Vector3.zero,Quaternion.Euler(0,180,0),82);
                Capture("bright_sky",Vector3.zero,Quaternion.Euler(-30,0,0),82);
                var control=new AircraftControlState();control.SetGliderNeutral();control.trim=flight.aircraftDefinition.referenceTrimNoseUp;
                float pitchRate=0;double previousPitch=flight.CurrentState.pitchDegrees;
                // Isolated repeatable test pilot, actual native controls only.
                // Descent/flare uses measured AGL; no pose/energy correction.
                for(int tick=0;tick<120*260;tick++)
                {
                    var s=flight.CurrentState;
                    control.airbrake=s.altitudeAglMeters>25?.65f:0;
                    float targetSpeed=s.altitudeAglMeters>6?48:34;
                    float targetPitch=-3.8f*control.airbrake+Mathf.Clamp(((float)s.calibratedAirspeedKnots-targetSpeed)*.65f,-8,10);
                    if(s.altitudeAglMeters<4)targetPitch=Mathf.Clamp(4-(float)s.altitudeAglMeters,0,5);
                    control.elevator=Mathf.Clamp((targetPitch-(float)s.pitchDegrees)*.09f-pitchRate*.02f,-.75f,.75f);
                    control.aileron=Mathf.Clamp(-(float)s.bankDegrees*.055f,-.5f,.5f);
                    control.rudder=Mathf.Clamp(-(float)s.sideslipDegrees*.04f,-.35f,.35f);
                    control.wheelBrake=s.weightOnWheels?1:0;
                    if(!flight.StepForTest(control,1d/120))throw new InvalidOperationException("Native flight trace stopped: "+flight.LastError);
                    var next=flight.CurrentState;pitchRate=(float)(next.pitchDegrees-previousPitch)*120;previousPitch=next.pitchDegrees;
                    if(tick%120==0)evidence.samples.Add(next);
                    if(next.engineRpm!=0)throw new InvalidOperationException("Unexpected glider engine RPM.");
                    if(tick==120*12) {InvokePrivate(presenter,"LateUpdate");Capture("airbrake_descent",Vector3.zero,Quaternion.Euler(15,0,0),82);}
                    if(next.weightOnWheels && next.groundSpeedKnots<3 && tick>120*5){evidence.landed=true;break;}
                }
                evidence.finalSimulationTime=flight.CurrentState.simulationTimeSeconds;evidence.engineFree=flight.CurrentState.engineRpm==0;
                InvokePrivate(presenter,"LateUpdate");Capture("trace_final",Vector3.zero,Quaternion.Euler(15,0,0),82);
                mode.ReturnToMainMenu();
                if(!mode.SelectAircraft(false))throw new InvalidOperationException("Final C172 restoration failed.");
            }
            catch(Exception ex){evidence.error=ex.ToString();Debug.LogException(ex);}
            finally {File.WriteAllText(Path.Combine(output,"production_glider_trace.json"),JsonUtility.ToJson(evidence,true));Time.timeScale=1;}
            EditorApplication.Exit(string.IsNullOrEmpty(evidence.error)?0:1);
        }
        private static void InvokePrivate(object target,string method)
        {target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);}
    }
}
