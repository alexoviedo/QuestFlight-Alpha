using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using QuestFlight.Product;
using AlexOviedo.QuestFlight.JSBSim;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEditor;
using UnityEngine;
namespace QuestFlight.Editor
{
    public static class LocalRecoveryNativeValidation
    {
        [Serializable] public class Result { public string name,error;public bool passed;public float displacement,minAgl=10000,maxBank,maxPitch;public Vector3 before,after; }
        [Serializable] class Report {public List<Result> cases;public string note="Production scene, actual coordinator/native reset and terrain. Actual building/aerial bundles loaded before recovery. Headset acceptance separate.";}
        static readonly List<Result> results=new List<Result>();
        static readonly Stack<IEnumerator> routines=new Stack<IEnumerator>();
        static object waiting;static GameObject root;static double deadline;
        static FlightDynamicsCoordinator flight;static ProductMapBoundaryRecovery recovery;static ProductDemoPilot demo;
        static ProductFlightModeCoordinator modes;static AircraftCenteredBuildingClipmapController buildings;
        static AircraftCenteredAerialClipmapController aerial;static FlightLessonRecoveryRuntimeAdapter legacy;
        static string output;
        static object Call(object target,string name,params object[] args) => target.GetType().GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Invoke(target,args);
        static void Set(object target,string field,object value) => target.GetType().GetField(field,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).SetValue(target,value);
        static void Check(bool pass,string message) {if(!pass)throw new Exception(message);}
        public static void RunBatch()
        {
            output=System.Environment.GetEnvironmentVariable("QFL_RECOVERY_EVIDENCE");Directory.CreateDirectory(output);
            deadline=EditorApplication.timeSinceStartup+480;routines.Push(Run());EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Native recovery validation timeout");
                if(waiting is AsyncOperation asyncOp&&!asyncOp.isDone)return;
                waiting=null;
                while(routines.Count>0)
                {
                    var e=routines.Peek();if(!e.MoveNext()){(e as IDisposable)?.Dispose();routines.Pop();continue;}
                    if(e.Current is IEnumerator nested){routines.Push(nested);continue;}
                    waiting=e.Current;return;
                }
                Finish(null);
            }
            catch(Exception e){Finish(e.ToString());}
        }
        static void Finish(string failure)
        {
            EditorApplication.update-=Tick;
            if(failure!=null)results.Add(new Result{name="runner",error=failure});
            File.WriteAllText(Path.Combine(output,"native_production_recovery.json"),JsonUtility.ToJson(new Report{cases=results},true));
            if(flight!=null)Call(flight,"OnDestroy");
            Time.timeScale=1;
            EditorApplication.Exit(results.All(r=>r.passed)?0:2);
        }
        static IEnumerator Run()
        {
            var scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity",UnityEditor.SceneManagement.OpenSceneMode.Single);
            root=scene.GetRootGameObjects().Single(r=>r.GetComponentInChildren<FlightDynamicsCoordinator>(true)!=null);
            flight=root.GetComponentsInChildren<FlightDynamicsCoordinator>(true).Single();
            recovery=root.GetComponentsInChildren<ProductMapBoundaryRecovery>(true).Single();
            demo=root.GetComponentsInChildren<ProductDemoPilot>(true).Single();
            modes=root.GetComponentsInChildren<ProductFlightModeCoordinator>(true).Single();
            buildings=root.GetComponentsInChildren<AircraftCenteredBuildingClipmapController>(true).Single();
            aerial=root.GetComponentsInChildren<AircraftCenteredAerialClipmapController>(true).Single();
            legacy=root.GetComponentsInChildren<FlightLessonRecoveryRuntimeAdapter>(true).Single();
            Check(recovery.HasAuthoredReferences,"Authored recovery refs");
            Check(flight.InitializeSelectedBackend(),flight.LastError);
            Call(buildings,"Awake");Set(aerial,"writeDevelopmentTrace",false);
            yield return (IEnumerator)Call(buildings,"LoadBundleIndex");
            yield return (IEnumerator)Call(aerial,"LoadGlobalPage");
            Check(aerial.HasGlobalCoverage,"Actual aerial bundle readiness");
            Call(legacy,"Start");
            SetMode(true);demo.StartDemo();demo.TakeUserControl();Time.timeScale=1;
            var historical=Make(new Vector3(-4826.0664f,534.8002f,-7596.8379f),206,5,20);
            Check(flight.TryRecoverToInitialConditions(historical),flight.LastError);
            var at=flight.CurrentState.positionUnityMeters;
            Call(legacy,"FixedUpdate");recovery.ObserveFlight(.02f);
            results.Add(new Result{name="historical_9km_takeover_no_checkpoint",passed=demo.UserHasControls&&Vector3.Distance(at,flight.CurrentState.positionUnityMeters)<.01f&&recovery.Phase==ProductMapBoundaryRecoveryPhase.Idle});
            foreach(var position in new[]{new Vector3(-10520,0,0),new Vector3(10520,0,0),new Vector3(0,0,-10520),new Vector3(0,0,10520),new Vector3(-10520,0,-10520),new Vector3(10520,0,10520),new Vector3(-10520,0,10520),new Vector3(10520,0,-10520)})
            {
                Call(recovery,"Cancel");Set(recovery,"armed",true);SetMode(false);demo.StopDemo();Time.timeScale=1;
                flight.terrainSurface.TrySample(position.x,position.z,out var ground);
                Vector3 point=position;point.y=ground.heightMeters+450;
                Check(flight.TryRecoverToInitialConditions(Make(point,Mathf.Atan2(point.x,point.z)*Mathf.Rad2Deg,-12,28)),flight.LastError);
                yield return (IEnumerator)Call(buildings,"RefreshWindow",true);
                RunRecovery("manual_edge_"+position,point);
            }
            // Takeover path with live rates and a modest held input, then a later independent event.
            foreach(bool manual in new[]{true,false})
            {
                Call(recovery,"Cancel");Set(recovery,"armed",true);SetMode(true);demo.StartDemo();if(manual)demo.TakeUserControl();Time.timeScale=1;
                Vector3 p=new Vector3(-10520,0,1600);flight.terrainSurface.TrySample(p.x,p.z,out var ground);p.y=ground.heightMeters+450;
                Check(flight.TryRecoverToInitialConditions(Make(p,270,8,25)),flight.LastError);
                var control=AircraftControlState.Neutral(.65f);control.aileron=.03f;control.elevator=.02f;
                for(int n=0;n<60;n++)Check(flight.StepForTest(control,1d/120),flight.LastError);
                yield return (IEnumerator)Call(buildings,"RefreshWindow",true);
                RunRecovery(manual?"demo_takeover_held_input":"active_demo_boundary",flight.CurrentState.positionUnityMeters,manual);
            }
            Call(recovery,"Cancel");SetMode(false);demo.StopDemo();Time.timeScale=1;
            Vector3 valid=new Vector3(-10300,0,0);flight.terrainSurface.TrySample(valid.x,valid.z,out var validGround);valid.y=validGround.heightMeters+450;
            Check(flight.TryRecoverToInitialConditions(Make(valid,90,0,0)),flight.LastError);
            yield return (IEnumerator)Call(buildings,"RefreshWindow",true);
            recovery.ObserveFlight(.02f);flight.enabled=false;recovery.ObserveFlight(.02f);
            RunRecovery("invalid_native_nearby_checkpoint",flight.CurrentState.positionUnityMeters,true,true);
            Set(recovery,"lastRecoveryTime",Time.unscaledTime-6f);
            recovery.ObserveFlight(.02f);
            Check((bool)typeof(ProductMapBoundaryRecovery).GetField("armed",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(recovery),"Independent recovery did not re-arm");
            Check(recovery.RequestLocalRecovery(flight.CurrentState,"later_independent"),recovery.LastError);
            Time.timeScale=0;recovery.AdvanceRecovery(1);recovery.CommitAtFixedBoundary();
            results.Add(new Result{name="paused_transition_does_not_commit",passed=recovery.Phase==ProductMapBoundaryRecoveryPhase.FadingOut&&flight.RecoveryHold});
            Time.timeScale=1;Call(recovery,"OnDisable");Call(recovery,"OnEnable");
            results.Add(new Result{name="disable_reenable_requires_assistance",passed=recovery.Phase==ProductMapBoundaryRecoveryPhase.Failed&&flight.RecoveryHold&&modes.AuthoredNavigation.IsOpen});
            // No safe candidate must be a visible assistance state, not a far-away reset.
            Call(recovery,"Cancel");SetMode(false);Time.timeScale=1;
            var far=new Vector3(-11990,1600,-11990);
            Check(flight.TryRecoverToInitialConditions(Make(far,225,0,0)),flight.LastError);
            var before=flight.CurrentState.positionUnityMeters;
            bool accepted=recovery.RequestLocalRecovery(flight.CurrentState,"no_safe_candidate_test");
            results.Add(new Result{name="bounded_failure_pauses_without_teleport",passed=!accepted&&recovery.Phase==ProductMapBoundaryRecoveryPhase.Failed&&flight.RecoveryHold&&Vector3.Distance(before,flight.CurrentState.positionUnityMeters)<.01f&&modes.AuthoredNavigation.IsOpen});
            bool restart=recovery.RestartAtRunway();Time.timeScale=1;
            var r=new Result{name="explicit_runway_restart_after_failure",passed=restart};
            if(restart)StepAndMeasure(r,AircraftControlState.Neutral(.1f),5,false);
            results.Add(r);
            // Existing collision policy is distinct; corrected checkpoint remains stable for five seconds.
            var cp=demo.RouteDefinition.recoveryCheckpoints[0];
            Check(FlightLessonRecoveryRuntimeAdapter.TryBuildInitialConditions(cp,flight.terrainSurface,out var groundIc),"Departure checkpoint invalid");
            Check(flight.TryRecoverToInitialConditions(groundIc),flight.LastError);
            r=new Result{name="demo_collision_ground_checkpoint",passed=true};StepAndMeasure(r,AircraftControlState.Neutral(.1f),5,false);results.Add(r);
            // Start the actual demo controller and run taxi/takeoff guidance through the coordinator.
            Call(recovery,"Cancel");SetMode(true);Time.timeScale=1;demo.StartDemo();
            var takeoff=new Result{name="normal_demo_start_taxi_takeoff",passed=true};
            var command=AircraftControlState.Neutral(.1f);
            for(int tick=0;tick<90*120;tick++)
            {
                if(tick%2==0 && demo.TryEvaluateGuidance(flight.CurrentState,1f/60,out var guidance,out _,true))
                    ProductControlInput.CopyFlightLessonCommandInto(guidance,command);
                if(!flight.StepForTest(command,1d/120)){takeoff.passed=false;takeoff.error=flight.LastError;break;}
                if(!flight.CurrentState.IsFinite||Math.Abs(flight.CurrentState.bankDegrees)>45||Math.Abs(flight.CurrentState.pitchDegrees)>35)
                {takeoff.passed=false;takeoff.error="Unsafe demo startup trajectory";break;}
            }
            takeoff.minAgl=(float)flight.CurrentState.altitudeAglMeters;
            takeoff.passed &= takeoff.minAgl>30&&!flight.CurrentState.weightOnWheels;
            results.Add(takeoff);
        }
        static void SetMode(bool isDemo) => Set(modes,"<State>k__BackingField",isDemo?ProductFlightModeCoordinator.FlightModeState.Demo:ProductFlightModeCoordinator.FlightModeState.FreeFlight);
        static FlightDynamicsInitialConditions Make(Vector3 p,float heading,float pitch,float bank)
        {
            flight.terrainSurface.TrySample(p.x,p.z,out var terrain);var geo=FlightFrameConversions.UnityToGeodetic(p,GeodeticReference.Kbdu);
            return new FlightDynamicsInitialConditions{latitudeDegrees=geo.latitudeDegrees,longitudeDegrees=geo.longitudeDegrees,altitudeMslMeters=geo.altitudeMslMeters,terrainElevationMslMeters=GeodeticReference.Kbdu.altitudeMslMeters+terrain.heightMeters,calibratedAirspeedKnots=85,headingDegrees=heading,pitchDegrees=pitch,bankDegrees=bank,engineRunning=true};
        }
        static void RunRecovery(string name,Vector3 before,bool manual=true,bool alreadyRequested=false)
        {
            var c=new bool[buildings.PageColumns*buildings.PageRows];var p=new bool[c.Length];
            bool has=buildings.TryCopyPageResidency(c,p);
            Debug.Log("[RecoveryReadiness] global="+aerial.HasGlobalCoverage+" visible="+has+" committed="+c.Count(v=>v)+" cached="+p.Count(v=>v)+" buildingRef="+System.Object.ReferenceEquals(buildings,typeof(ProductMapBoundaryRecovery).GetField("buildings",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(recovery))+" aerialRef="+System.Object.ReferenceEquals(aerial,typeof(ProductMapBoundaryRecovery).GetField("aerial",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(recovery))+" tracked="+(buildings.trackedRig.transform==flight.simulationRoot));
            var r=new Result{name=name,before=before};
            try
            {
                if(!alreadyRequested) Check(recovery.RequestLocalRecovery(flight.CurrentState,"native_regression"),recovery.LastError);
                var backend=typeof(FlightDynamicsCoordinator).GetField("_backend",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(flight);
                var native=(JSBSimNativeSession)backend.GetType().GetField("_session",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(backend);
                var fuelBefore=native.Diagnostics;
                int inputVersion=flight.controls.FlightLessonResetVersion;
                var held=FlightDynamicsControlSnapshot.From(flight.controls.Current);
                Check(!recovery.RequestLocalRecovery(flight.CurrentState,"duplicate"),"Duplicate recovery accepted");
                recovery.AdvanceRecovery(.3f);recovery.CommitAtFixedBoundary();
                if(!manual&&recovery.Phase==ProductMapBoundaryRecoveryPhase.Failed)
                {Check(!demo.UserHasControls&&flight.RecoveryHold,"Demo failure changed ownership");r.passed=true;r.error="Safe local native reset; route rejoin unavailable, assistance pause verified.";results.Add(r);return;}
                Check(recovery.Phase==ProductMapBoundaryRecoveryPhase.Stabilizing,recovery.LastError);
                r.after=flight.CurrentState.positionUnityMeters;r.displacement=Vector3.Distance(before,r.after);
                Check(r.displacement<=recovery.MaximumDisplacementMeters+.5f,"Recovery exceeded displacement bound");
                Check(flight.CurrentState.angularVelocityBodyDegreesPerSecond.magnitude<5,"Angular rates retained");
                Check(Math.Abs(native.Diagnostics.leftFuelPounds-fuelBefore.leftFuelPounds)<.01 && Math.Abs(native.Diagnostics.rightFuelPounds-fuelBefore.rightFuelPounds)<.01,"Recovery changed native fuel");
                if(manual){Check(flight.controls.FlightLessonResetVersion==inputVersion,"Input handoff was reset");Check(Math.Abs(flight.LastAppliedControls.elevator-held.elevator)<.001&&Math.Abs(flight.LastAppliedControls.throttle-held.throttle)<.001,"Held controls were discarded");}
                Check(Vector3.Distance(flight.simulationRoot.position,r.after)<.01f,"Native/presentation position mismatch");
                Check(Quaternion.Angle(flight.simulationRoot.rotation,flight.CurrentState.rotationUnity)<.01f,"Native/presentation attitude mismatch");
                var controls=AircraftControlState.Neutral(.65f);controls.elevator=.02f;controls.aileron=.01f;
                r.passed=true;StepAndMeasure(r,controls,8,true);
                Check(recovery.Phase==ProductMapBoundaryRecoveryPhase.Idle,"Recovery did not finish");
                Check(modes.State!=ProductFlightModeCoordinator.FlightModeState.Demo||demo.UserHasControls==manual,"Control ownership changed");
            }
            catch(Exception e){r.error=e.Message;r.passed=false;}
            results.Add(r);Debug.Log("[LocalRecoveryNative] "+JsonUtility.ToJson(r));
        }
        static void StepAndMeasure(Result r,AircraftControlState controls,int seconds,bool air)
        {
            for(int n=0;n<seconds*120;n++)
            {
                if(!flight.StepForTest(controls,1d/120)){r.passed=false;r.error=flight.LastError;break;}
                var s=flight.CurrentState;r.minAgl=Mathf.Min(r.minAgl,(float)s.altitudeAglMeters);r.maxBank=Mathf.Max(r.maxBank,(float)Math.Abs(s.bankDegrees));r.maxPitch=Mathf.Max(r.maxPitch,(float)Math.Abs(s.pitchDegrees));
                if(!s.IsFinite||r.maxPitch>(air?35:10)||r.maxBank>(air?55:10)||r.minAgl<(air?50:.3f)){r.passed=false;r.error="Native flight stability bounds failed";break;}
                if(air){recovery.AdvanceRecovery(1f/120);if(recovery.Phase==ProductMapBoundaryRecoveryPhase.Failed){r.passed=false;r.error=recovery.LastError;break;}}
            }
        }
    }
}
