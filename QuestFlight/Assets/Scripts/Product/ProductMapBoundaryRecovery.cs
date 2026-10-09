using System;
using System.IO;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEngine;

namespace QuestFlight.Product
{
    public enum ProductMapBoundaryRecoveryPhase { Idle, FadingOut, Stabilizing, FadingIn, Failed }

    // Serialized recovery owner for the finite world. Native JSBSim remains the only flight authority.
    [DisallowMultipleComponent, DefaultExecutionOrder(600)]
    public sealed class ProductMapBoundaryRecovery : MonoBehaviour
    {
        [SerializeField] private ProductFlightModeCoordinator modeCoordinator;
        [SerializeField] private ProductControlInput productInput;
        [SerializeField] private FlightDynamicsCoordinator flightCoordinator;
        [SerializeField] private ProductionTerrainSurfaceAsset terrainSurface;
        [SerializeField] private AircraftCenteredBuildingClipmapController buildings;
        [SerializeField] private AircraftCenteredAerialClipmapController aerial;
        [SerializeField] private Bounds[] obstacleBounds = Array.Empty<Bounds>();
        [SerializeField] private float triggerInsetMeters = 1500f;
        [SerializeField] private float returnInsetMeters = 1800f;
        [SerializeField] private float maximumDisplacementMeters = 750f;
        [SerializeField] private float minimumRecoveryAglMeters = 300f;
        [SerializeField] private float minimumRecoveryAirspeedKnots = 80f;
        [SerializeField] private float fadeOutSeconds = .25f;
        [SerializeField] private float stabilizationSeconds = 1f;
        [SerializeField] private float fadeInSeconds = .35f;
        public ProductMapBoundaryRecoveryPhase Phase { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        public int CompletedRecoveryCount { get; private set; }
        public ProductFlightModeCoordinator AuthoredModeCoordinator => modeCoordinator;
        public ProductControlInput AuthoredProductInput => productInput;
        public FlightDynamicsCoordinator AuthoredFlightCoordinator => flightCoordinator;
        public ProductionTerrainSurfaceAsset AuthoredTerrainSurface => terrainSurface;
        public float TriggerInsetMeters => triggerInsetMeters;
        public float ReturnInsetMeters => returnInsetMeters;
        public float MaximumDisplacementMeters => maximumDisplacementMeters;
        public bool HasAuthoredReferences => modeCoordinator != null && productInput != null &&
            flightCoordinator != null && terrainSurface != null && buildings != null && aerial != null &&
            modeCoordinator.AuthoredNavigation != null && modeCoordinator.AuthoredFlightCoordinator == flightCoordinator &&
            flightCoordinator.controls == productInput && flightCoordinator.terrainSurface == terrainSurface &&
            ProductMapBoundaryRecoveryPolicy.HasValidInsets(terrainSurface.macroSizeMeters, triggerInsetMeters, returnInsetMeters) &&
            maximumDisplacementMeters >= (returnInsetMeters-triggerInsetMeters)*1.415f;
        private float elapsed, lastRecoveryTime = -100, checkpointTime = -100;
        private int session = -1, eventId, observations;
        private double resetTime;
        private bool armed = true, wasManual, traceFailed;
        private FlightDynamicsState checkpoint, incident;
        private FlightDynamicsInitialConditions pending;
        private bool[] committed, prefetched;
        private string reason;
        private readonly FlightLessonCrashClassifier crashes = new FlightLessonCrashClassifier();

        public void ConfigureAuthoredReferences(ProductFlightModeCoordinator modes, ProductControlInput input,
            FlightDynamicsCoordinator flight, ProductionTerrainSurfaceAsset terrain)
        { modeCoordinator=modes; productInput=input; flightCoordinator=flight; terrainSurface=terrain; }
        public void ConfigureLocalSafety(AircraftCenteredBuildingClipmapController pages,
            AircraftCenteredAerialClipmapController imagery, Bounds[] obstacles)
        { buildings=pages; aerial=imagery; obstacleBounds=obstacles; returnInsetMeters=1800; maximumDisplacementMeters=750; minimumRecoveryAirspeedKnots=80; stabilizationSeconds=1; }
        private void Awake()
        {
            if (!HasAuthoredReferences) { Debug.LogError("[LocalRecovery] Authored safety references invalid.",this); enabled=false; return; }
            committed=new bool[buildings.PageColumns*buildings.PageRows]; prefetched=new bool[committed.Length];
        }
        private bool FlightActive => modeCoordinator.State == ProductFlightModeCoordinator.FlightModeState.FreeFlight ||
            modeCoordinator.State == ProductFlightModeCoordinator.FlightModeState.Demo;
        private bool Manual => modeCoordinator.State == ProductFlightModeCoordinator.FlightModeState.FreeFlight || modeCoordinator.AuthoredDemoPilot.UserHasControls;
        private void FixedUpdate() { CommitAtFixedBoundary(); ObserveFlight(Time.fixedDeltaTime); }
        // Same entry point used by the native production regression runner, not a replacement planner.
        public void ObserveFlight(float deltaTime)
        {
            if (session != modeCoordinator.FlightSessionVersion || !FlightActive)
            {
                Cancel(); session=modeCoordinator.FlightSessionVersion;
                checkpointTime=-100; crashes.Reset(); armed=true;
            }
            if (!FlightActive || !flightCoordinator.IsInitialized || Phase!=ProductMapBoundaryRecoveryPhase.Idle || Time.timeScale<=0) return;
            // The existing active-demo collision response owns its checkpoint transition.
            // It must never race the automatic boundary transition.
            if (!Manual && modeCoordinator.AuthoredDemoPilot.GuidanceSuppressed) return;
            var state=flightCoordinator.CurrentState;
            bool invalid=!state.IsFinite || !flightCoordinator.enabled;
            if (invalid)
            {
                if (Time.unscaledTime-checkpointTime>2f || !checkpoint.IsFinite ||
                    !BoulderWindFieldProfile.Finite(state.positionUnityMeters) ||
                    Vector3.Distance(checkpoint.positionUnityMeters,state.positionUnityMeters)>100f)
                { PauseUnsafe("Native flight state is invalid and no nearby verified state is available."); return; }
                var nearby=checkpoint;
                nearby.positionUnityMeters=state.positionUnityMeters;
                nearby.altitudeMslMeters=FlightFrameConversions.UnityToGeodetic(nearby.positionUnityMeters,GeodeticReference.Kbdu).altitudeMslMeters;
                RequestLocalRecovery(nearby,"InvalidNativeState"); return;
            }
            if (Manual)
            {
                var settings=FlightLessonCrashSettings.ConservativeTrainingDefaults;
                settings.maximumLessonRadiusMeters=float.MaxValue;
                FlightLessonRecoveryRuntimeAdapter.TryMapTerrainSample(terrainSurface,state.positionUnityMeters,out var ground);
                var crash=crashes.Evaluate(FlightLessonRecoveryRuntimeAdapter.BuildSafetyState(state),ground,deltaTime,settings);
                if (crash!=FlightLessonCrashReason.None)
                { PauseUnsafe("Aircraft contact / unsafe flight: "+crash+". Choose Restart at runway when ready."); return; }
            }
            bool outside=ProductMapBoundaryRecoveryPolicy.ShouldRecover(state.positionUnityMeters,
                terrainSurface.macroMinimumEnuMeters,terrainSurface.macroSizeMeters,triggerInsetMeters);
            if (!armed && !outside && Time.unscaledTime-lastRecoveryTime>=5f) armed=true;
            if (outside)
            {
                if (!armed) { PauseUnsafe("The aircraft returned to the boundary before recovery re-armed. Restart or return to the menu."); return; }
                RequestLocalRecovery(state,"WorldBoundary"); return;
            }
            if (!state.weightOnWheels && state.altitudeAglMeters>minimumRecoveryAglMeters &&
                Math.Abs(state.bankDegrees)<30 && Math.Abs(state.pitchDegrees)<15 && state.calibratedAirspeedKnots>=70)
            { checkpoint=state; checkpointTime=Time.unscaledTime; }
        }
        public bool RequestLocalRecovery(in FlightDynamicsState state, string trigger)
        {
            if (Phase!=ProductMapBoundaryRecoveryPhase.Idle) return false;
            session=modeCoordinator.FlightSessionVersion;
            incident=state; reason=trigger; eventId++; wasManual=Manual;
            WriteTrace("requested",state,"");
            bool ready=HasAuthoredReferences && RefreshCoverage();
            if (state.weightOnWheels || !ready)
            {
                WriteTrace("candidate_rejected",state,"WOW="+state.weightOnWheels+", refs="+HasAuthoredReferences+", coverage="+ready);
                PauseUnsafe("A safe airborne recovery is unavailable here. The aircraft is on the ground or nearby scenery is not ready. Restart at runway or return to the menu.");
                return false;
            }
            var wind=flightCoordinator.windField.Sample(state.positionUnityMeters,state.simulationTimeSeconds);
            var planningState=state;
            if(productInput.Current.flaps>.05f) planningState.calibratedAirspeedKnots=Math.Min(80,planningState.calibratedAirspeedKnots);
            if (!ProductMapBoundaryRecoveryPolicy.TryBuildLocalRecovery(planningState,terrainSurface,returnInsetMeters,
                maximumDisplacementMeters,minimumRecoveryAglMeters,minimumRecoveryAirspeedKnots,
                wind, IsSceneryReady, obstacleBounds,out pending,out string failure))
            {
                WriteTrace("candidate_rejected",state,failure);
                PauseUnsafe("No safe nearby flight corridor is available. Restart at runway or return to the menu.");
                return false;
            }
            // Freeze only simulation integration during the comfort fade. Do not discard held manual input.
            flightCoordinator.SetRecoveryHold(true);
            Phase=ProductMapBoundaryRecoveryPhase.FadingOut; elapsed=0;
            return true;
        }
        private bool RefreshCoverage()
        {
            if(committed==null) {committed=new bool[buildings.PageColumns*buildings.PageRows];prefetched=new bool[committed.Length];}
            if(!aerial.HasGlobalCoverage)return false;
            // Empty but successfully loaded pages are valid scenery. The existing return
            // value counts visible building renderers, which can be zero in the mountains.
            buildings.TryCopyPageResidency(committed,prefetched);
            foreach(bool ready in committed)if(ready)return true;
            return false;
        }
        private bool IsSceneryReady(Vector3 at)
        {
            int x=Mathf.FloorToInt((at.x-buildings.MapMinimumX)/buildings.PageSizeMeters);
            int z=Mathf.FloorToInt((at.z-buildings.MapMinimumZ)/buildings.PageSizeMeters);
            return x>=0&&z>=0&&x<buildings.PageColumns&&z<buildings.PageRows&&committed[z*buildings.PageColumns+x];
        }
        private void Update() => AdvanceRecovery(Time.unscaledDeltaTime);
        public void AdvanceRecovery(float dt)
        {
            if(Phase==ProductMapBoundaryRecoveryPhase.Idle||Phase==ProductMapBoundaryRecoveryPhase.Failed)return;
            if(!FlightActive || session!=modeCoordinator.FlightSessionVersion){Cancel();return;}
            if(Time.timeScale<=0)return;
            elapsed+=Mathf.Max(0,dt);
            if(Phase==ProductMapBoundaryRecoveryPhase.FadingOut)
            {
                Fade(Mathf.Clamp01(elapsed/fadeOutSeconds));
                // Reset is committed on the following fixed simulation boundary.
            }
            else if(Phase==ProductMapBoundaryRecoveryPhase.Stabilizing)
            {
                var s=flightCoordinator.CurrentState;
                if(!s.IsFinite||!flightCoordinator.enabled||s.weightOnWheels||s.altitudeAglMeters<50||Math.Abs(s.pitchDegrees)>35||Math.Abs(s.bankDegrees)>55)
                {PauseUnsafe("The recovered aircraft did not remain safely airborne.");return;}
                if(elapsed>=stabilizationSeconds && s.simulationTimeSeconds>resetTime+.5)
                {Phase=ProductMapBoundaryRecoveryPhase.FadingIn;elapsed=0;}
                else if(elapsed>3){PauseUnsafe("Native flight simulation did not advance after recovery.");}
            }
            else if(Phase==ProductMapBoundaryRecoveryPhase.FadingIn)
            {
                Fade(1-Mathf.Clamp01(elapsed/fadeInSeconds));
                if(elapsed>=fadeInSeconds)
                {Phase=ProductMapBoundaryRecoveryPhase.Idle;CompletedRecoveryCount++;lastRecoveryTime=Time.unscaledTime;armed=false;Fade(0);WriteTrace("completed",flightCoordinator.CurrentState,"");}
            }
        }
        private void OnEnable() { if(flightCoordinator!=null)flightCoordinator.NativeStepCompleted+=ObservePostReset; }
        private void ObservePostReset(FlightDynamicsState s, FlightDynamicsControlSnapshot controls, float dt)
        { if(observations>0){WriteTrace("post_reset_tick",s,"");observations--;} }
        public void CommitAtFixedBoundary()
        {
            if(Phase!=ProductMapBoundaryRecoveryPhase.FadingOut||elapsed<fadeOutSeconds||Time.timeScale<=0)return;
            Fade(1);
            if(!RefreshCoverage() || !IsSceneryReady(FlightFrameConversions.GeodeticToUnity(pending.latitudeDegrees,pending.longitudeDegrees,pending.altitudeMslMeters,GeodeticReference.Kbdu)))
            {PauseUnsafe("Recovery scenery became unavailable during the fade.");return;}
            if(!flightCoordinator.TryRecoverToInitialConditions(pending, true))
            {PauseUnsafe("Native recovery rejected: "+flightCoordinator.LastError);return;}
            var after=flightCoordinator.CurrentState;
            if(!after.IsFinite||Vector3.Distance(after.positionUnityMeters,incident.positionUnityMeters)>maximumDisplacementMeters+.5f||after.angularVelocityBodyDegreesPerSecond.magnitude>5f)
            {PauseUnsafe("Native recovery readback failed locality or angular-rate checks.");return;}
            if(!wasManual && !modeCoordinator.AuthoredDemoPilot.TryResumeFromState(after,
                ProductControlInput.ToFlightLessonCommand(productInput.Current),false))
            {PauseUnsafe("Demo cannot safely rejoin from this local position. Restart or return to the menu.");return;}
            if(wasManual && modeCoordinator.State==ProductFlightModeCoordinator.FlightModeState.Demo && !modeCoordinator.AuthoredDemoPilot.UserHasControls)
            {PauseUnsafe("Manual control ownership changed during recovery.");return;}
            crashes.Reset();checkpointTime=-100; resetTime=after.simulationTimeSeconds;observations=4;
            flightCoordinator.SetRecoveryHold(false);Phase=ProductMapBoundaryRecoveryPhase.Stabilizing;elapsed=0;
            WriteTrace("native_reset_applied",after,"");
        }
        private void PauseUnsafe(string message)
        {
            LastError=message;Phase=ProductMapBoundaryRecoveryPhase.Failed;
            flightCoordinator.SetRecoveryHold(true);Fade(0);
            WriteTrace("paused",flightCoordinator.CurrentState,message);
            modeCoordinator.AuthoredNavigation.ShowRecoveryAssistance(this,message);
        }
        public bool RestartAtRunway()
        {
            // Explicit user choice, never an automatic fallback. Uses the current route's departure profile.
            var checkpoints=modeCoordinator.AuthoredDemoPilot.RouteDefinition?.recoveryCheckpoints;
            if(checkpoints==null||checkpoints.Length==0||!FlightLessonRecoveryRuntimeAdapter.TryBuildInitialConditions(checkpoints[0],terrainSurface,out var ground)||ground.calibratedAirspeedKnots>0)return false;
            modeCoordinator.AuthoredDemoPilot.StopDemo();
            if(!flightCoordinator.TryRecoverToInitialConditions(ground))return false;
            productInput.PrepareForFlightLessonRecovery(new FlightLessonControlCommand{throttle=.1f,mixture=1});
            Cancel();crashes.Reset(true);armed=true;checkpointTime=-100;
            return true;
        }
        private void Cancel()
        { flightCoordinator?.SetRecoveryHold(false);Phase=ProductMapBoundaryRecoveryPhase.Idle;elapsed=0;observations=0;Fade(0); }
        private static void Fade(float opacity)
        { float v=1-Mathf.Clamp01(opacity);OVRManager.SetColorScaleAndOffset(new Vector4(v,v,v,v),Vector4.zero,true); }
        private void OnDisable()
        {
            if(flightCoordinator!=null)flightCoordinator.NativeStepCompleted-=ObservePostReset;
            if(Phase!=ProductMapBoundaryRecoveryPhase.Idle && FlightActive)
                PauseUnsafe("Recovery was interrupted. Restart or return to the menu.");
            else Cancel();
        }
        [Serializable] private struct Trace
        {
            public string utc,stage,reason,detail,mode,owner,caller;public int id; public FlightDynamicsState native;
            public long simulationTick; public bool terrainValid,insideBoundary; public float terrainHeight;
            public Vector3 presentationPosition;public Quaternion presentationRotation;public FlightDynamicsInitialConditions destination;
            public float displacement;public bool hold;
        }
        private void WriteTrace(string stage,FlightDynamicsState s,string detail)
        {
            if(traceFailed)return;
            try
            {
                bool terrainValid=terrainSurface.TrySample(s.positionUnityMeters.x,s.positionUnityMeters.z,out var ground);
                var record=new Trace{utc=DateTime.UtcNow.ToString("O"),stage=stage,reason=reason,detail=detail,id=eventId,native=s,
                    caller="ProductMapBoundaryRecovery."+stage,simulationTick=(long)(s.simulationTimeSeconds*flightCoordinator.simulationRateHz),
                    terrainValid=terrainValid,terrainHeight=terrainValid?ground.heightMeters:0,
                    insideBoundary=!ProductMapBoundaryRecoveryPolicy.ShouldRecover(s.positionUnityMeters,terrainSurface.macroMinimumEnuMeters,terrainSurface.macroSizeMeters,triggerInsetMeters),
                    mode=modeCoordinator.State.ToString(),owner=Manual?"manual":"demo",destination=pending,
                    presentationPosition=flightCoordinator.simulationRoot.position,presentationRotation=flightCoordinator.simulationRoot.rotation,
                    displacement=Vector3.Distance(s.positionUnityMeters,incident.positionUnityMeters),hold=flightCoordinator.RecoveryHold};
                File.AppendAllText(Path.Combine(Application.persistentDataPath,"questflight_local_recovery.jsonl"),JsonUtility.ToJson(record)+System.Environment.NewLine);
            }
            catch(Exception e){traceFailed=true;Debug.LogWarning("[LocalRecovery] Trace unavailable: "+e.Message);}
        }
    }

    public static class ProductMapBoundaryRecoveryPolicy
    {
        public static bool HasValidInsets(Vector2 size,float trigger,float target) => size.x>0&&size.y>0&&trigger>0&&target>trigger&&target<Mathf.Min(size.x,size.y)*.5f;
        public static bool ShouldRecover(Vector3 p,Vector2 min,Vector2 size,float inset) => p.x<=min.x+inset||p.z<=min.y+inset||p.x>=min.x+size.x-inset||p.z>=min.y+size.y-inset;
        // Kept for existing callers; now uses the same bounded planner as production.
        public static bool TryBuildRecoveryInitialConditions(in FlightDynamicsState state,ProductionTerrainSurfaceAsset terrain,float inset,float agl,float speed,out FlightDynamicsInitialConditions ic,out string failure)
            => TryBuildLocalRecovery(state,terrain,inset,750,agl,speed,Vector3.zero,null,Array.Empty<Bounds>(),out ic,out failure);
        public static bool TryBuildLocalRecovery(in FlightDynamicsState state,ProductionTerrainSurfaceAsset terrain,
            float inset,float maxDisplacement,float minimumAgl,float minimumSpeed,Vector3 wind,
            Func<Vector3,bool> sceneryReady,Bounds[] obstacles,out FlightDynamicsInitialConditions ic,out string failure)
        {
            ic=default; failure="No safe local flight corridor within the configured recovery distance. Restart at runway or return to the menu.";
            if(terrain==null||!state.IsFinite||state.weightOnWheels||!BoulderWindFieldProfile.Finite(wind))return false;
            Vector2 min=terrain.macroMinimumEnuMeters,max=min+terrain.macroSizeMeters;
            Vector3 origin=state.positionUnityMeters;
            Vector3 interior=new Vector3(Mathf.Clamp(origin.x,min.x+inset,max.x-inset),origin.y,Mathf.Clamp(origin.z,min.y+inset,max.y-inset));
            float inward=Mathf.Atan2((min.x+max.x)*.5f-interior.x,(min.y+max.y)*.5f-interior.z)*Mathf.Rad2Deg;
            double speed=Math.Max(minimumSpeed,Math.Min(110,state.calibratedAirspeedKnots));
            // At Boulder density altitude TAS exceeds CAS. 1.4 plus bounded wind over eight seconds.
            float distance=((float)speed*.514444f*1.4f+wind.magnitude)*8f;
            int missing=0,blocked=0,tooFar=0;
            foreach(float heading in new[]{(float)state.headingDegrees,inward,inward-45,inward+45,inward-90,inward+90})
            foreach(float offset in new[]{0f,150f,300f})
            {
                Vector3 f=new Vector3(Mathf.Sin(heading*Mathf.Deg2Rad),0,Mathf.Cos(heading*Mathf.Deg2Rad));
                Vector3 side=Vector3.Cross(Vector3.up,f);
                Vector3 target=interior+f*offset;
                if(ShouldRecover(target,min,terrain.macroSizeMeters,inset-1))continue;
                float floor=origin.y;
                bool valid=true;
                for(float along=-6;along<=distance+6;along+=5)
                for(int lane=-1;lane<=1;lane++)
                {
                    Vector3 p=target+f*along+side*(lane*7f);
                    if(ShouldRecover(p,min,terrain.macroSizeMeters,inset-280)||!terrain.TrySample(p.x,p.z,out var ground)||sceneryReady!=null&&!sceneryReady(p))
                    {valid=false;missing++;break;}
                    // 7 m half-width covers the 10.97 m span plus margins; 3 m underside margin.
                    floor=Mathf.Max(floor,ground.heightMeters+Mathf.Max(100,minimumAgl)+3);
                }
                if(!valid)continue;
                // Static landmark envelopes are baked by the editor. Procedural buildings <=53 m;
                // the minimum terrain clearance also covers the authored trees and road structures.
                if(obstacles!=null)foreach(var b in obstacles)
                {
                    var expanded=b;expanded.Expand(new Vector3(14,6,14));
                    Vector3 a=new Vector3(target.x,expanded.center.y,target.z);
                    if(expanded.Contains(a)||expanded.IntersectRay(new Ray(a,f),out float hit)&&hit<=distance+6)
                        floor=Mathf.Max(floor,b.max.y+30);
                }
                target.y=floor;
                if(Vector3.Distance(target,origin)>maxDisplacement){tooFar++;continue;}
                if(!terrain.TrySample(target.x,target.z,out var under)){blocked++;continue;}
                var geo=FlightFrameConversions.UnityToGeodetic(target,GeodeticReference.Kbdu);
                var groundGeo=FlightFrameConversions.UnityToGeodetic(new Vector3(target.x,under.heightMeters,target.z),GeodeticReference.Kbdu);
                ic=new FlightDynamicsInitialConditions{latitudeDegrees=geo.latitudeDegrees,longitudeDegrees=geo.longitudeDegrees,
                    altitudeMslMeters=geo.altitudeMslMeters,terrainElevationMslMeters=groundGeo.altitudeMslMeters,
                    calibratedAirspeedKnots=speed,headingDegrees=FlightFrameConversions.UnityHeadingToTrue(Mathf.Repeat(heading,360),
                        geo.latitudeDegrees,geo.longitudeDegrees,GeodeticReference.Kbdu),pitchDegrees=2,bankDegrees=0,flightPathAngleDegrees=0,engineRunning=state.engineRpm>=300};
                failure="";return true;
            }
            failure+=$" (coverage={missing}, locality={tooFar}, terrain={blocked})";return false;
        }
        public static FlightLessonControlCommand BuildStabilizationCommand(bool engineRunning) => new FlightLessonControlCommand{throttle=engineRunning?.65f:0,mixture=engineRunning?1:0,trim=.05f};
    }
}
