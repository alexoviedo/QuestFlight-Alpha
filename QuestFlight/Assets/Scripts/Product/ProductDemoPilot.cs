using QuestFlightLab.Input;
using QuestFlightLab.Flight;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Runtime;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEngine;
using QuestFlightLab.Environment;

namespace QuestFlight.Product
{
    /// <summary>
    /// Minimal deterministic acceptance loop. It supplies controls through the same product input
    /// boundary as a gamepad and never writes aircraft, camera, seat, or XR transforms.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class ProductDemoPilot : MonoBehaviour
    {
        public const float PrimaryControlRatePerSecond = 0.9f;
        public const float EngineControlRatePerSecond = 0.65f;
        public const float ToeBrakeRatePerSecond = 1.8f;
        public const float TakeoffRotationPitchDegrees = 8f;
        public const float TakeoffRotationLeadKnots = 0f;
        public const float TakeoffMinimumGroundRotationElevator = 0.30f;
        public const float TakeoffAirborneConfirmationSeconds = 0.60f;
        public const float TakeoffGuidanceHandoffSeconds = 2f;
        public const float TakeoffHandoffMinimumAglMeters = 15.24f;
        public const float TakeoffHandoffMinimumClimbFeetPerMinute = 200f;

        private enum GuidedTakeoffPhase
        {
            GroundRoll = 0,
            Rotation = 1,
            InitialClimb = 2,
            GuidanceHandoff = 3,
            Complete = 4
        }

        [SerializeField] private ProductControlInput productInput;
        [SerializeField] private DeterministicGamepadInputSource deterministicInput;
        [SerializeField] private FlightDynamicsCoordinator flightCoordinator;
        [SerializeField] private FlightLessonRouteProfile routeProfile;
        [SerializeField] private bool autoStartDemo;
        [SerializeField] private AmbientAircraftRoute exitRoute;
        [SerializeField] private AmbientTrafficDirector traffic;
        [SerializeField] private FlightLessonRouteProfile runway26Profile;
        [SerializeField] private AmbientAircraftRoute runway26Exit;
        private AmbientAircraftRoute activeExitRoute;
        private bool rejoining;
        private Vector3 rejoinPoint;
        private float rejoinAltitude;
        private readonly DemoLandingController landing = new DemoLandingController();
        private readonly DemoEnrouteGuidance enroute = new DemoEnrouteGuidance();
        private bool landingActive;
        private int goAroundCount;
        public bool UserHasControls { get; private set; }
        public bool NeedsAssistance { get; private set; }
        public bool HadRecovery { get; private set; }
        public bool ShortShowcase { get; private set; }
        public void RecordRecovery() { HadRecovery=true; }
        public bool ReservesRunway => IsRunning && !UserHasControls && !NeedsAssistance &&
            ((landingActive && landing.Phase!=DemoLandingController.LandingPhase.Stopped) || (_routeDefinition != null && _progress.currentSegmentIndex == 0));
        public string OwnershipLabel => NeedsAssistance ? "Demo needs assistance" : UserHasControls ? "Your controls" : landingActive ? "Demo landing" : "Demo flying";
        public void ConfigureTourForAuthoring(AmbientAircraftRoute exit, AmbientTrafficDirector director) { exitRoute=exit; traffic=director; }
        public void ConfigureRunway26ForAuthoring(FlightLessonRouteProfile profile,AmbientAircraftRoute exit) { runway26Profile=profile;runway26Exit=exit; }
        [SerializeField, Min(0f)] private float runwayHeadingGain = 0.025f;
        [SerializeField, Min(0f)] private float runwayCrossTrackGain = 0.015f;
        [SerializeField, Range(0.02f, 0.25f)] private float maximumRunwayRudder = 0.14f;

        public bool IsRunning { get; private set; }
        public string Phase { get; private set; } = "ready";
        public string PresentationPhase => !IsRunning ? "Ready for the next flight" :
            Phase=="complete_clear_of_runway" ? "Parked clear of the runway" :
            Phase=="stopped_after_recovery" ? "Stopped safely after recovery" :
            landingActive ? landing.Phase==DemoLandingController.LandingPhase.GoAround ? "Going around for another approach" :
                landing.Phase==DemoLandingController.LandingPhase.Flare ? "Landing" :
                landing.Phase==DemoLandingController.LandingPhase.Rollout ? "Slowing down on the runway" :
                landing.Phase==DemoLandingController.LandingPhase.TaxiExit ? "Leaving the runway" : "Approaching the runway" :
            Phase.Contains("taxi") ? "Taxiing to the runway" : Phase.Contains("takeoff")||Phase.Contains("rotation") ? "Taking off" :
            Phase.Contains("climb") ? "Climbing away from the airport" : "Following the Boulder route";
        public ProductControlInput AuthoredProductInput => productInput;
        public DeterministicGamepadInputSource AuthoredDeterministicInput => deterministicInput;
        public bool HasAuthoredSources => productInput != null &&
                                          deterministicInput != null &&
                                          flightCoordinator != null;
        public bool AutoStartDemo => autoStartDemo;
        public FlightLessonRouteProfile AuthoredRouteProfile => routeProfile;
        public FlightLessonRouteDefinition RouteDefinition => _routeDefinition;
        public FlightLessonProgressState ProgressState => _progress;
        public bool GuidanceSuppressed => _guidanceSuppressed;

        private readonly AircraftControlState _scheduledControls = AircraftControlState.Neutral(0.10f);
        private readonly AircraftControlState _appliedControls = AircraftControlState.Neutral(0.10f);
        private readonly FlightLessonGuidanceController _guidanceController =
            new FlightLessonGuidanceController();
        private FlightLessonRouteDefinition _routeDefinition;
        private FlightLessonProgressState _progress;
        private bool _guidanceSuppressed;
        private int _lastInputResetVersion;
        private GuidedTakeoffPhase _takeoffPhase;
        private float _takeoffAirborneSeconds;
        private float _takeoffHandoffSeconds;
        private int _guidanceApplyCount;
        private float _guidanceAccumulatedDeltaTime;
        private int _controlResetCount;
        private string _lastControlResetReason = "not_started";
        private FlightLessonControlCommand _lastGuidanceTarget;

        public string BuildFailureDiagnostic()
        {
            return
                $"Demo[Phase={Phase} Takeoff={_takeoffPhase} Segment={_progress.currentSegmentIndex} " +
                $"ApplyCount={_guidanceApplyCount} ApplyDt={_guidanceAccumulatedDeltaTime:F3} " +
                $"ResetCount={_controlResetCount} LastReset={_lastControlResetReason} " +
                $"Target=A{_lastGuidanceTarget.aileron:F3}/E{_lastGuidanceTarget.elevator:F3}/" +
                $"R{_lastGuidanceTarget.rudder:F3}/T{_lastGuidanceTarget.throttle:F3} " +
                $"Applied=A{_appliedControls.aileron:F3}/E{_appliedControls.elevator:F3}/" +
                $"R{_appliedControls.rudder:F3}/T{_appliedControls.throttle:F3}]";
        }

        public void ConfigureAuthoredSources(
            ProductControlInput authoredProductInput,
            DeterministicGamepadInputSource authoredDeterministicInput,
            FlightDynamicsCoordinator authoredFlightCoordinator)
        {
            productInput = authoredProductInput;
            deterministicInput = authoredDeterministicInput;
            flightCoordinator = authoredFlightCoordinator;
        }

        public void ConfigureAuthoredAutoStart(bool enabled) => autoStartDemo = enabled;

        public void ConfigureRouteProfileForAuthoring(
            FlightLessonRouteProfile authoredRouteProfile) =>
            routeProfile = authoredRouteProfile;

        private void Start()
        {
            if (autoStartDemo) StartDemo();
        }

        private void OnDisable()
        {
            if (IsRunning)
                StopDemo();
        }

        public void StartDemo()
        {
            ShortShowcase=false;
            if (!HasAuthoredSources)
            {
                Debug.LogError("[QuestFlight][Demo] Authored product input references are missing.", this);
                return;
            }
            _routeDefinition=null;
            activeExitRoute=exitRoute;rejoining=false;
            if(flightCoordinator.IsInitialized && runway26Profile!=null)
            {
                Vector3 wind=flightCoordinator.windField.Sample(Vector3.zero,0);
                if(new Vector2(wind.x,wind.z).magnitude>8 || Mathf.Abs(wind.z)>4)
                {Debug.LogError("[Demo] Wind is outside the validated automated-flight envelope.");return;}
                if(wind.x>1) { _routeDefinition=runway26Profile.Definition;activeExitRoute=runway26Exit; }
            }
            if (!EnsureRouteDefinition(out string routeError))
            {
                Debug.LogError("[QuestFlight][Demo] " + routeError, this);
                return;
            }
            _scheduledControls.SetNeutral(0.10f);
            _appliedControls.SetNeutral(0.10f);
            RecordControlReset("StartDemo");
            _progress = FlightLessonProgressState.InitialFor(_routeDefinition);
            _guidanceController.Reset();
            enroute.Reset();
            ResetTakeoffGuidance(true);
            _guidanceSuppressed = false;
            UserHasControls=false; NeedsAssistance=false; landingActive=false;goAroundCount=0;HadRecovery=false;
            if(flightCoordinator.IsInitialized && runway26Profile!=null)
            {
                Vector3 p=_routeDefinition.waypoints[0].positionUnityMeters;
                if(!flightCoordinator.terrainSurface.TrySample(p.x,p.z,out var ground))return;
                p.y=ground.heightMeters+1.4f;
                var geo=FlightFrameConversions.UnityToGeodetic(p,GeodeticReference.Kbdu);
                var initial=new FlightDynamicsInitialConditions {latitudeDegrees=geo.latitudeDegrees,longitudeDegrees=geo.longitudeDegrees,altitudeMslMeters=geo.altitudeMslMeters,
                    terrainElevationMslMeters=GeodeticReference.Kbdu.altitudeMslMeters+ground.heightMeters,headingDegrees=activeExitRoute.runwayDirection==26?270:90,engineRunning=true};
                if(!flightCoordinator.TryResetToInitialConditions(initial))return;
            }
            _lastInputResetVersion = productInput.FlightLessonResetVersion;
            deterministicInput.SetControls(_appliedControls);
            IsRunning = true;
            Phase = "awaiting_flight_state";
            // Preserve the established demo lifecycle contract for QA callers. While
            // flight-lesson guidance is active ProductControlInput still samples the
            // live gamepad for shared authority; the deterministic source mirrors the
            // bounded guidance command as the no-player fallback.
            productInput.SetDeterministicMode(true);
            productInput.DemoOwnsControls=true;
            productInput.SetFlightLessonInputSuppressed(false);
            productInput.SetFlightLessonGuidance(
                true,
                ProductControlInput.ToFlightLessonCommand(_appliedControls));
        }

        public bool StartShortArrival()
        {
            if(!IsRunning||!TryBuildShortArrival(_routeDefinition,flightCoordinator.terrainSurface,out var initial,out int segment))return false;
            if(!flightCoordinator.TryResetToInitialConditions(initial))return false;
            if(!RestoreLessonSegment(segment))return false;
            _scheduledControls.SetNeutral(.5f);_appliedControls.SetNeutral(.5f);
            ShortShowcase=true;return true;
        }
        public static bool TryBuildShortArrival(FlightLessonRouteDefinition route,ProductionTerrainSurfaceAsset terrain,out FlightDynamicsInitialConditions initial,out int segment)
        {
            initial=default;segment=-1;if(route==null||terrain==null)return false;
            for(int i=0;i<route.segments.Length;i++)
            {
                var s=route.segments[i];var start=route.waypoints[s.startWaypointIndex];
                if(start.id!="arrival_gate")continue;
                Vector3 p=start.positionUnityMeters;p.y=start.altitudeMslFeet*.3048f-(float)GeodeticReference.Kbdu.altitudeMslMeters;
                if(!terrain.TrySample(p.x,p.z,out var ground)||p.y-ground.heightMeters<120)return false;
                Vector3 delta=route.waypoints[s.endWaypointIndex].positionUnityMeters-p;
                var geo=FlightFrameConversions.UnityToGeodetic(p,GeodeticReference.Kbdu);
                initial=new FlightDynamicsInitialConditions{latitudeDegrees=geo.latitudeDegrees,longitudeDegrees=geo.longitudeDegrees,altitudeMslMeters=geo.altitudeMslMeters,terrainElevationMslMeters=GeodeticReference.Kbdu.altitudeMslMeters+ground.heightMeters,headingDegrees=Mathf.Repeat(Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg,360),calibratedAirspeedKnots=start.calibratedAirspeedKnots,engineRunning=true};
                segment=i;return true;
            }
            return false;
        }

        public void StopDemo()
        {
            IsRunning = false;
            Phase = "ready";
            _guidanceSuppressed = false;
            UserHasControls=false; NeedsAssistance=false; landingActive=false;
            _guidanceController.Reset();
            ResetTakeoffGuidance(true);
            deterministicInput?.SetControls(AircraftControlState.Neutral(0.10f));
            if (productInput != null)
            {
                productInput.SetFlightLessonInputSuppressed(false);
                productInput.DemoOwnsControls=false;
                productInput.DisableFlightLessonGuidance();
                productInput.SetDeterministicMode(false);
            }
        }

        private void Update()
        {
            if (Time.timeScale <= 0f) return;
            if (!IsRunning || deterministicInput == null || productInput == null)
                return;
            if (_guidanceSuppressed)
                return;
            if (UserHasControls) return;
            if (NeedsAssistance) { traffic?.flightMode?.AuthoredNavigation?.OpenMenu();return; }

            if (_lastInputResetVersion != productInput.FlightLessonResetVersion)
            {
                _lastInputResetVersion = productInput.FlightLessonResetVersion;
                _progress = FlightLessonProgressState.InitialFor(_routeDefinition);
                _guidanceController.Reset();
                ResetTakeoffGuidance(true);
                _scheduledControls.SetNeutral(0.10f);
                _appliedControls.SetNeutral(0.10f);
                RecordControlReset("InputResetVersionChanged");
            }

            if (flightCoordinator == null || !flightCoordinator.IsInitialized)
            {
                Phase = "awaiting_flight_state";
                return;
            }

            FlightDynamicsState state = flightCoordinator.CurrentState;
            if (!state.IsFinite)
            {
                Phase = "invalid_flight_state";
                return;
            }

            if(TryEvaluateGuidance(state,Mathf.Max(.0001f,Time.unscaledDeltaTime),out var evaluated,out var evaluatedPhase))
                ApplyGuidanceCommand(evaluated,evaluatedPhase);
        }

        public bool TryEvaluateGuidance(in FlightDynamicsState state,float guidanceDeltaTime,out FlightLessonControlCommand evaluated,out string evaluatedPhase,bool? runwayClearOverride=null)
        {
            evaluated=default;evaluatedPhase="guidance_unavailable";
            if(UserHasControls || NeedsAssistance || !state.IsFinite) return false;
            if(rejoining)
            {
                Vector3 delta=rejoinPoint-state.positionUnityMeters;delta.y=0;
                if(delta.magnitude<150) {rejoining=false;_progress.currentSegmentIndex++;enroute.Reset();}
                else
                {
                    var join=new FlightLessonGuidanceIntent("rejoining_tour",FlightLessonSegmentKind.Cruise,rejoinPoint,0,0,rejoinAltitude,85,20,700,0);
                    evaluated=enroute.Step(state,join,guidanceDeltaTime);evaluatedPhase="rejoining_tour";return true;
                }
            }
            FlightLessonAircraftNavigationState navigation =
                BuildNavigationState(state);
            int finalIndex=FinalSegmentIndex();
            if(!landingActive && !(_progress.currentSegmentIndex==0 && state.altitudeMslMeters/.3048<5800)) _progress = FlightLessonNavigator.AdvanceIfCaptured(
                _routeDefinition,
                _progress,
                navigation);
            if(landingActive || finalIndex>=0 && _progress.currentSegmentIndex>=finalIndex)
            {
                if(!landingActive)
                {
                    var threshold=_routeDefinition.waypoints[_routeDefinition.segments[finalIndex].endWaypointIndex].positionUnityMeters;
                    var end=threshold+Vector3.right*(activeExitRoute!=null && activeExitRoute.runwayDirection==26?-1260:1260);
                    if(flightCoordinator.terrainSurface.TrySample(threshold.x,threshold.z,out var surface)) threshold.y=surface.heightMeters;
                    landing.Reset(threshold,end,activeExitRoute!=null?activeExitRoute.segments[10].points:null);landingActive=true;
                }
                evaluated=landing.Step(state,guidanceDeltaTime,runwayClearOverride ?? (traffic==null || traffic.RunwayOwner<0));
                evaluatedPhase="landing_"+landing.Phase.ToString().ToLowerInvariant();
                if(landing.Phase==DemoLandingController.LandingPhase.GoAround && state.altitudeAglMeters>120 && state.verticalSpeedFeetPerMinute>100)
                {
                    if(++goAroundCount>2) { NeedsAssistance=true;Phase="Automatic landing unavailable - pause and restart";return false; }
                    int pattern=FindSegment(FlightLessonSegmentKind.PatternEntry);
                    if(pattern>=0) { _progress.currentSegmentIndex=pattern;_progress.lessonComplete=false;landingActive=false;_guidanceController.Reset(); }
                }
                if(landing.Phase==DemoLandingController.LandingPhase.Stopped) { _progress.lessonComplete=!HadRecovery;evaluatedPhase=HadRecovery?"stopped_after_recovery":"complete_clear_of_runway"; }
                return true;
            }
            if (_progress.lessonComplete)
            {
                FlightLessonControlCommand complete = LessonCompleteCommand();
                evaluated=complete;evaluatedPhase="lesson_complete";
                return true;
            }

            if (!FlightLessonNavigator.TryEvaluate(
                    _routeDefinition,
                    _progress,
                    navigation,
                    out FlightLessonGuidanceIntent intent))
            {
                Phase = "guidance_unavailable";
                return false;
            }
            var activeSegment=_routeDefinition.segments[_progress.currentSegmentIndex];
            if(activeSegment.geometry==FlightLessonPathGeometry.Arc)
            {
                var start=_routeDefinition.waypoints[activeSegment.startWaypointIndex];
                var end=_routeDefinition.waypoints[activeSegment.endWaypointIndex];
                Vector3 a=start.positionUnityMeters-activeSegment.arcCenterUnityMeters;
                Vector3 b=end.positionUnityMeters-activeSegment.arcCenterUnityMeters;
                Vector3 p=state.positionUnityMeters-activeSegment.arcCenterUnityMeters;
                float first=Mathf.Atan2(a.z,a.x)*Mathf.Rad2Deg;
                float last=Mathf.Atan2(b.z,b.x)*Mathf.Rad2Deg;
                float current=Mathf.Atan2(p.z,p.x)*Mathf.Rad2Deg;
                float direction=activeSegment.arcClockwise?-1:1;
                float total=Mathf.Abs(Mathf.DeltaAngle(first,last));
                float travel=Mathf.Clamp(Mathf.DeltaAngle(first,current)*direction,0,total);
                float radius=new Vector2(a.x,a.z).magnitude;
                float look=Mathf.Min(total,travel+activeSegment.lookAheadMeters/radius*Mathf.Rad2Deg);
                float angle=(first+look*direction)*Mathf.Deg2Rad;
                Vector3 lookPoint=activeSegment.arcCenterUnityMeters+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*radius;
                float fraction=travel/total;
                intent=new FlightLessonGuidanceIntent(intent.segmentId,intent.segmentKind,lookPoint,intent.desiredTrackDegreesTrue,intent.crossTrackErrorMeters,
                    Mathf.Lerp(start.altitudeMslFeet,end.altitudeMslFeet,fraction),Mathf.Lerp(start.calibratedAirspeedKnots,end.calibratedAirspeedKnots,fraction),intent.maximumBankDegrees,intent.maximumClimbRateFeetPerMinute,fraction);
            }

            if (TryBuildGuidedTakeoffCommand(
                    intent,
                    state,
                    guidanceDeltaTime,
                    out FlightLessonControlCommand takeoffCommand,
                    out string takeoffPhase))
            {
                evaluated=takeoffCommand;evaluatedPhase=takeoffPhase;
                return true;
            }

            var profileStart=_routeDefinition.waypoints[activeSegment.startWaypointIndex];
            var profileEnd=_routeDefinition.waypoints[activeSegment.endWaypointIndex];
            float profileLength=Vector3.Distance(profileStart.positionUnityMeters,profileEnd.positionUnityMeters);
            if(activeSegment.geometry==FlightLessonPathGeometry.Arc)
            {
                Vector3 a=profileStart.positionUnityMeters-activeSegment.arcCenterUnityMeters,b=profileEnd.positionUnityMeters-activeSegment.arcCenterUnityMeters;
                profileLength=a.magnitude*Vector3.Angle(a,b)*Mathf.Deg2Rad;
            }
            float feedforward=intent.normalizedSegmentProgress<.99f?(profileEnd.altitudeMslFeet-profileStart.altitudeMslFeet)/Mathf.Max(1,profileLength)*(float)state.groundSpeedKnots*.514444f*60:0;
            FlightLessonControlCommand command=enroute.Step(state,intent,guidanceDeltaTime,feedforward);

            ConfigureCommandForSegment(intent, ref command);
            evaluated=command;evaluatedPhase=intent.segmentId;
            return true;
        }

        private int FinalSegmentIndex()
        {
            for(int i=0;i<_routeDefinition.segments.Length-1;i++)
                if(_routeDefinition.segments[i].kind==FlightLessonSegmentKind.Final && _routeDefinition.segments[i+1].kind==FlightLessonSegmentKind.Rollout)return i;
            return -1;
        }
        private int FindSegment(FlightLessonSegmentKind kind)
        { for(int i=0;i<_routeDefinition.segments.Length;i++)if(_routeDefinition.segments[i].kind==kind)return i;return -1; }

        public void TakeUserControl()
        {
            if(!IsRunning)return;
            var controls=ProductControlInput.ToFlightLessonCommand(flightCoordinator.LastAppliedControls);
            productInput.DemoOwnsControls=false;productInput.DisableFlightLessonGuidance();productInput.SetDeterministicMode(false);
            productInput.PrepareForFlightLessonRecovery(controls);productInput.SetFlightLessonInputSuppressed(false);productInput.RequireTouchRelease();
            UserHasControls=true;NeedsAssistance=false;Phase="your_controls";
        }

        public bool ResumeDemoControl(bool returnToAirport)
        {
            if(!IsRunning || !flightCoordinator.IsInitialized)return false;
            var state=flightCoordinator.CurrentState;
            return TryResumeFromState(state,ProductControlInput.ToFlightLessonCommand(flightCoordinator.LastAppliedControls),returnToAirport);
        }

        public bool TryResumeFromState(in FlightDynamicsState state,in FlightLessonControlCommand applied,bool returnToAirport)
        {
            if(!IsRunning || !state.IsFinite)return false;
            if(state.weightOnWheels || state.altitudeAglMeters<60 || state.calibratedAirspeedKnots<45 || Mathf.Abs((float)state.bankDegrees)>60)
            { Phase="Cannot safely rejoin here - keep control or restart";return false; }
            int first=returnToAirport?FindSegment(FlightLessonSegmentKind.Return):Mathf.Max(1,_progress.currentSegmentIndex);
            int last=FindSegment(FlightLessonSegmentKind.PatternEntry);
            int selected=-1;float best=float.MaxValue;
            for(int i=Mathf.Max(1,first);i<=last;i++)
            {
                Vector3 point=_routeDefinition.waypoints[_routeDefinition.segments[i].endWaypointIndex].positionUnityMeters;
                float altitude=_routeDefinition.waypoints[_routeDefinition.segments[i].endWaypointIndex].altitudeMslFeet*.3048f-(float)GeodeticReference.Kbdu.altitudeMslMeters;
                point.y=Mathf.Max(state.positionUnityMeters.y,altitude);
                float distance=Vector3.Distance(state.positionUnityMeters,point);
                if(distance>6500 || distance<350 || distance>=best)continue;
                bool clear=true;
                for(int n=1;n<=24;n++)
                {
                    Vector3 p=Vector3.Lerp(state.positionUnityMeters,point,n/24f);
                    if(!flightCoordinator.terrainSurface.TrySample(p.x,p.z,out var ground) || p.y-ground.heightMeters<100) {clear=false;break;}
                    Vector3 across=Vector3.Cross(Vector3.up,(point-state.positionUnityMeters).normalized)*400;
                    foreach(float side in new[]{-1f,1f})
                        if(!flightCoordinator.terrainSurface.TrySample(p.x+across.x*side,p.z+across.z*side,out var edge) || p.y-edge.heightMeters<100)clear=false;
                }
                if(clear){best=distance;selected=i;rejoinPoint=point;}
            }
            if(selected<0){Phase="No safe rejoin corridor - keep control or restart";return false;}
            _progress.currentSegmentIndex=selected;_progress.lessonComplete=false;landingActive=false;
            rejoining=true;rejoinAltitude=(rejoinPoint.y+(float)GeodeticReference.Kbdu.altitudeMslMeters)/.3048f;
            _guidanceController.Reset();enroute.Reset();ResetTakeoffGuidance(false);
            ProductControlInput.CopyFlightLessonCommandInto(applied,_appliedControls);
            UserHasControls=false;NeedsAssistance=false;_guidanceSuppressed=false;
            productInput.SetDeterministicMode(true);productInput.DemoOwnsControls=true;
            productInput.SetFlightLessonGuidance(true,ProductControlInput.ToFlightLessonCommand(_appliedControls));
            Phase=returnToAirport?"returning_to_airport":"rejoining_tour";return true;
        }

        private void ApplyGuidanceCommand(
            in FlightLessonControlCommand command,
            string phase)
        {
            _lastGuidanceTarget = command;
            _guidanceApplyCount++;
            _guidanceAccumulatedDeltaTime += Mathf.Max(0f, Time.unscaledDeltaTime);
            ProductControlInput.CopyFlightLessonCommandInto(
                command,
                _scheduledControls);
            RateLimitControlsInto(
                _scheduledControls,
                _appliedControls,
                Time.unscaledDeltaTime);
            Phase = phase;
            deterministicInput.SetControls(_appliedControls);
            productInput.SetFlightLessonGuidance(
                true,
                ProductControlInput.ToFlightLessonCommand(_appliedControls));
        }

        public bool RestoreLessonSegment(int segmentIndex)
        {
            if (UserHasControls) return false;
            if (!EnsureRouteDefinition(out _) ||
                segmentIndex < 0 ||
                segmentIndex >= _routeDefinition.segments.Length)
                return false;

            _progress.currentSegmentIndex = segmentIndex;
            landingActive=false;rejoining=false;enroute.Reset();
            _progress.lessonComplete = false;
            _progress.latestRecoveryCheckpointIndex = LatestCheckpointBefore(
                _routeDefinition,
                segmentIndex);
            _guidanceController.Reset();
            ResetTakeoffGuidance(segmentIndex == 0);
            _scheduledControls.SetNeutral(0.10f);
            _appliedControls.SetNeutral(0.10f);
            RecordControlReset("RestoreLessonSegment");
            if (productInput != null)
            {
                _lastInputResetVersion = productInput.FlightLessonResetVersion;
                productInput.SetFlightLessonGuidance(
                    true,
                    ProductControlInput.ToFlightLessonCommand(_appliedControls));
            }
            Phase = _routeDefinition.segments[segmentIndex].id;
            return true;
        }

        public void SetGuidanceSuppressed(bool suppressed)
        {
            if (_guidanceSuppressed == suppressed)
                return;
            _guidanceSuppressed = suppressed;
            _guidanceController.Reset();
            if (suppressed)
                ResetTakeoffGuidance(_progress.currentSegmentIndex == 0);
            _scheduledControls.SetNeutral(0.10f);
            _appliedControls.SetNeutral(0.10f);
            RecordControlReset(suppressed
                ? "GuidanceSuppressed"
                : "GuidanceResumed");
            if (productInput != null)
                productInput.SetFlightLessonInputSuppressed(suppressed);
            Phase = suppressed ? "recovery_suppressed" :
                (_routeDefinition != null && !_progress.lessonComplete &&
                 _progress.currentSegmentIndex >= 0 &&
                 _progress.currentSegmentIndex < _routeDefinition.segments.Length
                    ? _routeDefinition.segments[_progress.currentSegmentIndex].id
                    : "awaiting_flight_state");
        }

        public static FlightLessonAircraftNavigationState BuildNavigationState(
            in FlightDynamicsState state)
        {
            return new FlightLessonAircraftNavigationState
            {
                positionUnityMeters = state.positionUnityMeters,
                altitudeMslFeet = (float)(state.altitudeMslMeters *
                                           FlightFrameConversions.MetersToFeet),
                calibratedAirspeedKnots = (float)state.calibratedAirspeedKnots,
                headingDegreesTrue = (float)state.headingDegrees
            };
        }

        public static FlightLessonGuidanceAircraftState BuildGuidanceAircraftState(
            in FlightDynamicsState state)
        {
            Vector3 rates = state.angularVelocityBodyDegreesPerSecond;
            return new FlightLessonGuidanceAircraftState
            {
                headingDegreesTrue = (float)state.headingDegrees,
                bankDegrees = (float)state.bankDegrees,
                pitchDegrees = (float)state.pitchDegrees,
                rollRateDegreesPerSecond = -rates.z,
                pitchRateDegreesPerSecond = -rates.x,
                yawRateDegreesPerSecond = rates.y,
                altitudeMslFeet = (float)(state.altitudeMslMeters *
                                           FlightFrameConversions.MetersToFeet),
                verticalSpeedFeetPerMinute = (float)state.verticalSpeedFeetPerMinute,
                calibratedAirspeedKnots = (float)state.calibratedAirspeedKnots,
                sideslipDegrees = (float)state.sideslipDegrees
            };
        }

        private bool EnsureRouteDefinition(out string error)
        {
            if (_routeDefinition == null)
            {
                if (routeProfile == null)
                {
                    error = "The authored flight-lesson route profile is missing.";
                    return false;
                }
                _routeDefinition = routeProfile.Definition;
            }
            if (_routeDefinition == null)
            {
                error = "The route definition is missing.";
                return false;
            }
            return _routeDefinition.TryValidate(out error);
        }

        private static int LatestCheckpointBefore(
            FlightLessonRouteDefinition route,
            int segmentIndex)
        {
            int checkpoint = route != null
                ? route.initialRecoveryCheckpointIndex
                : -1;
            for (int i = 0; i < segmentIndex; i++)
            {
                int candidate = route.segments[i].recoveryCheckpointOnCompletion;
                if (candidate >= 0)
                    checkpoint = candidate;
            }
            return checkpoint;
        }

        private float AuthoredRotationSpeedKnots()
        {
            C172StyleAircraftConfig config = flightCoordinator != null
                ? flightCoordinator.aircraftConfiguration
                : null;
            return config != null
                ? Mathf.Max(1f, config.Speeds.rotationKts)
                : 55f;
        }

        private static void ConfigureCommandForSegment(
            in FlightLessonGuidanceIntent intent,
            ref FlightLessonControlCommand command)
        {
            command.mixture = 1f;
            switch (intent.segmentKind)
            {
                case FlightLessonSegmentKind.Departure:
                    // A normally aspirated C172 departure remains at takeoff power.
                    // Airspeed is controlled with pitch until the departure leg is
                    // captured; handing throttle to the cruise-speed loop here caused
                    // the aircraft to settle back onto the runway.
                    command.throttle = 1f;
                    break;
                case FlightLessonSegmentKind.Downwind:
                    command.flaps = 0.25f;
                    break;
                case FlightLessonSegmentKind.Base:
                    command.flaps = 0.50f;
                    break;
                case FlightLessonSegmentKind.Final:
                    command.flaps = intent.normalizedSegmentProgress > 0.45f
                        ? 1f
                        : 0.75f;
                    break;
                case FlightLessonSegmentKind.Rollout:
                    command.throttle = 0.10f;
                    command.flaps = 1f;
                    command.leftToeBrake = 0.45f;
                    command.rightToeBrake = 0.45f;
                    break;
                default:
                    command.flaps = 0f;
                    break;
            }
        }

        private bool TryBuildGuidedTakeoffCommand(
            in FlightLessonGuidanceIntent intent,
            in FlightDynamicsState state,
            float deltaTime,
            out FlightLessonControlCommand command,
            out string phase)
        {
            command = default;
            phase = intent.segmentId;
            if (intent.segmentKind != FlightLessonSegmentKind.Departure)
                return false;

            float rotationSpeed = AuthoredRotationSpeedKnots();
            if (state.weightOnWheels)
            {
                // Wheel contact during the departure segment always re-arms the
                // takeoff controller. Previously Complete was checked first, so a
                // brief liftoff followed by runway contact permanently selected the
                // cruise controller and its reduced power command.
                _takeoffAirborneSeconds = 0f;
                _takeoffHandoffSeconds = 0f;
                _takeoffPhase = state.calibratedAirspeedKnots >=
                    rotationSpeed - TakeoffRotationLeadKnots
                    ? GuidedTakeoffPhase.Rotation
                    : GuidedTakeoffPhase.GroundRoll;
                command = BuildTakeoffCommand(
                    intent,
                    state,
                    _takeoffPhase == GuidedTakeoffPhase.Rotation,
                    runwayHeadingGain,
                    runwayCrossTrackGain,
                    maximumRunwayRudder);
                phase = intent.segmentId + (_takeoffPhase == GuidedTakeoffPhase.Rotation
                    ? "_rotation"
                    : "_ground_roll");
                return true;
            }

            if (_takeoffPhase == GuidedTakeoffPhase.Complete)
                return false;

            if (_takeoffPhase == GuidedTakeoffPhase.GroundRoll ||
                _takeoffPhase == GuidedTakeoffPhase.Rotation)
            {
                _takeoffPhase = GuidedTakeoffPhase.InitialClimb;
                _takeoffAirborneSeconds = 0f;
            }

            FlightLessonControlCommand stabilized = BuildTakeoffCommand(
                intent,
                state,
                true,
                runwayHeadingGain,
                runwayCrossTrackGain,
                maximumRunwayRudder);

            bool establishedClimb =
                state.altitudeAglMeters >= TakeoffHandoffMinimumAglMeters &&
                state.verticalSpeedFeetPerMinute >=
                    TakeoffHandoffMinimumClimbFeetPerMinute &&
                state.calibratedAirspeedKnots >= rotationSpeed;
            if (!establishedClimb)
            {
                _takeoffPhase = GuidedTakeoffPhase.InitialClimb;
                _takeoffAirborneSeconds = 0f;
                _takeoffHandoffSeconds = 0f;
                command = stabilized;
                phase = intent.segmentId + "_initial_climb";
                return true;
            }

            _takeoffAirborneSeconds += Mathf.Max(0f, deltaTime);
            if (_takeoffAirborneSeconds < TakeoffAirborneConfirmationSeconds)
            {
                command = stabilized;
                phase = intent.segmentId + "_initial_climb";
                return true;
            }

            if (_takeoffPhase != GuidedTakeoffPhase.GuidanceHandoff)
            {
                _takeoffPhase = GuidedTakeoffPhase.GuidanceHandoff;
                _takeoffHandoffSeconds = 0f;
                _guidanceController.Reset();
            }

            if (!_guidanceController.TryStep(
                    intent,
                    BuildGuidanceAircraftState(state),
                    deltaTime,
                    FlightLessonGuidanceControllerSettings.Default,
                    out FlightLessonControlCommand guided))
            {
                command = stabilized;
                phase = intent.segmentId + "_initial_climb";
                return true;
            }

            ConfigureCommandForSegment(intent, ref guided);

            _takeoffHandoffSeconds += Mathf.Max(0f, deltaTime);
            float blend = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(_takeoffHandoffSeconds /
                              TakeoffGuidanceHandoffSeconds));
            command = LerpCommand(stabilized, guided, blend);
            phase = intent.segmentId + "_guidance_handoff";
            if (blend >= 1f)
                _takeoffPhase = GuidedTakeoffPhase.Complete;
            return true;
        }

        public static FlightLessonControlCommand BuildTakeoffCommand(
            in FlightLessonGuidanceIntent intent,
            in FlightDynamicsState state,
            bool rotating,
            float headingGain = 0.025f,
            float crossTrackGain = 0.015f,
            float maximumRudder = 0.14f)
        {
            float pitchRate = -state.angularVelocityBodyDegreesPerSecond.x;
            float elevator = rotating
                ? Mathf.Clamp(
                    (TakeoffRotationPitchDegrees - (float)state.pitchDegrees) * 0.055f -
                    pitchRate * 0.018f,
                    0f,
                    0.42f)
                : 0.02f;
            if (rotating && state.weightOnWheels)
            {
                // Maintain enough elevator authority to establish the authored
                // rotation attitude and wing loading until wheel contact is
                // actually released. The validated prototype takeoff scenario
                // uses the same bounded 0.30 command. Liftoff remains aerodynamic;
                // this does not inject position or vertical velocity.
                elevator = Mathf.Max(
                    elevator,
                    TakeoffMinimumGroundRotationElevator);
            }
            return new FlightLessonControlCommand
            {
                aileron = Mathf.Clamp(
                    -(float)state.bankDegrees * 0.035f -
                    (-state.angularVelocityBodyDegreesPerSecond.z) * 0.015f,
                    -0.20f,
                    0.20f),
                elevator = elevator,
                rudder = Mathf.Clamp(
                    FlightLessonPathMath.HeadingErrorDegrees(
                        intent.desiredTrackDegreesTrue,
                        (float)state.headingDegrees) * headingGain -
                    intent.crossTrackErrorMeters * crossTrackGain,
                    -maximumRudder,
                    maximumRudder),
                throttle = 1f,
                mixture = 1f,
                trim = 0.08f,
                flaps = 0f,
                leftToeBrake = 0f,
                rightToeBrake = 0f
            };
        }

        private static FlightLessonControlCommand LerpCommand(
            in FlightLessonControlCommand from,
            in FlightLessonControlCommand to,
            float amount)
        {
            float t = Mathf.Clamp01(amount);
            return new FlightLessonControlCommand
            {
                aileron = Mathf.Lerp(from.aileron, to.aileron, t),
                elevator = Mathf.Lerp(from.elevator, to.elevator, t),
                rudder = Mathf.Lerp(from.rudder, to.rudder, t),
                throttle = Mathf.Lerp(from.throttle, to.throttle, t),
                mixture = Mathf.Lerp(from.mixture, to.mixture, t),
                trim = Mathf.Lerp(from.trim, to.trim, t),
                flaps = Mathf.Lerp(from.flaps, to.flaps, t),
                leftToeBrake = Mathf.Lerp(from.leftToeBrake, to.leftToeBrake, t),
                rightToeBrake = Mathf.Lerp(from.rightToeBrake, to.rightToeBrake, t)
            };
        }

        private void ResetTakeoffGuidance(bool enabledForDeparture)
        {
            _takeoffPhase = enabledForDeparture
                ? GuidedTakeoffPhase.GroundRoll
                : GuidedTakeoffPhase.Complete;
            _takeoffAirborneSeconds = 0f;
            _takeoffHandoffSeconds = 0f;
        }

        private void RecordControlReset(string reason)
        {
            _controlResetCount++;
            _guidanceApplyCount = 0;
            _guidanceAccumulatedDeltaTime = 0f;
            _lastControlResetReason = reason ?? "unknown";
            _lastGuidanceTarget = default;
        }

        private static FlightLessonControlCommand LessonCompleteCommand()
        {
            return new FlightLessonControlCommand
            {
                throttle = 0.10f,
                mixture = 1f,
                flaps = 1f,
                leftToeBrake = 0.65f,
                rightToeBrake = 0.65f
            };
        }

        public static AircraftControlState ScheduledControls(float seconds, out string phase)
        {
            AircraftControlState controls = new AircraftControlState();
            ScheduledControlsInto(seconds, out phase, controls);
            return controls;
        }

        public static void ScheduledControlsInto(
            float seconds,
            out string phase,
            AircraftControlState controls)
        {
            if (controls == null)
            {
                phase = "invalid_controls_target";
                return;
            }

            controls.SetNeutral(0.10f);
            if (seconds < 3f)
            {
                phase = "idle_brake_hold";
                controls.leftToeBrake = 1f;
                controls.rightToeBrake = 1f;
            }
            else if (seconds < 11f)
            {
                phase = "taxi";
                controls.throttle = 0.28f;
                controls.rudder = 0f;
            }
            else if (seconds < 14f)
            {
                phase = "taxi_brake";
                controls.throttle = 0.10f;
                controls.leftToeBrake = 0.65f;
                controls.rightToeBrake = 0.65f;
            }
            else if (seconds < 31f)
            {
                phase = "takeoff_roll";
                controls.throttle = 1f;
                controls.trim = 0.08f;
                controls.rudder = 0f;
            }
            else if (seconds < 37f)
            {
                phase = "rotation";
                controls.throttle = 1f;
                controls.elevator = 0.32f;
                controls.trim = 0.08f;
                controls.rudder = 0f;
            }
            else if (seconds < 50f)
            {
                phase = "vy_climb";
                controls.throttle = 1f;
                controls.elevator = 0.10f;
                controls.trim = 0.10f;
            }
            else if (seconds < 62f)
            {
                phase = "shallow_left_turn";
                controls.throttle = 0.86f;
                controls.elevator = 0.05f;
                controls.trim = 0.08f;
                float turnTime = seconds - 50f;
                if (turnTime < 4f)
                {
                    float rollIn = Smooth01(turnTime / 4f);
                    controls.aileron = Mathf.Lerp(0f, -0.18f, rollIn);
                    controls.rudder = Mathf.Lerp(0f, -0.17f, rollIn);
                }
                else if (turnTime < 9f)
                {
                    controls.aileron = -0.05f;
                    controls.rudder = -0.055f;
                }
                else
                {
                    float rollOut = Smooth01((turnTime - 9f) / 3f);
                    controls.aileron = Mathf.Lerp(-0.05f, 0.13f, rollOut);
                    controls.rudder = Mathf.Lerp(-0.055f, 0.11f, rollOut);
                }
            }
            else
            {
                phase = "climb_stabilize";
                controls.throttle = 0.88f;
                controls.elevator = 0.04f;
                controls.trim = 0.08f;
            }
        }

        public static float ComputeRunwayTrackingRudder(
            double currentHeadingDegrees,
            double referenceHeadingDegrees,
            float crossTrackMeters,
            float headingGain,
            float crossTrackGain,
            float maximumRudder)
        {
            float headingError = Mathf.DeltaAngle(
                (float)currentHeadingDegrees,
                (float)referenceHeadingDegrees);
            float command = headingError * Mathf.Max(0f, headingGain) -
                            crossTrackMeters * Mathf.Max(0f, crossTrackGain);
            return Mathf.Clamp(
                command,
                -Mathf.Abs(maximumRudder),
                Mathf.Abs(maximumRudder));
        }

        /// <summary>
        /// Allocation-free pilot-input rate limiter. The deterministic route
        /// and cockpit presentation consume the same resulting control state.
        /// </summary>
        public static void RateLimitControlsInto(
            AircraftControlState target,
            AircraftControlState current,
            float deltaTime)
        {
            if (target == null || current == null || deltaTime <= 0f) return;
            float primaryStep = PrimaryControlRatePerSecond * deltaTime;
            float engineStep = EngineControlRatePerSecond * deltaTime;
            float brakeStep = ToeBrakeRatePerSecond * deltaTime;
            current.aileron = Mathf.MoveTowards(current.aileron, target.aileron, primaryStep);
            current.elevator = Mathf.MoveTowards(current.elevator, target.elevator, primaryStep);
            current.rudder = Mathf.MoveTowards(current.rudder, target.rudder, primaryStep);
            current.throttle = Mathf.MoveTowards(current.throttle, target.throttle, engineStep);
            current.mixture = Mathf.MoveTowards(current.mixture, target.mixture, engineStep);
            current.carbHeat = Mathf.MoveTowards(current.carbHeat, target.carbHeat, engineStep);
            current.trim = Mathf.MoveTowards(current.trim, target.trim, primaryStep);
            current.flaps = Mathf.MoveTowards(current.flaps, target.flaps, engineStep);
            current.leftToeBrake = Mathf.MoveTowards(
                current.leftToeBrake, target.leftToeBrake, brakeStep);
            current.rightToeBrake = Mathf.MoveTowards(
                current.rightToeBrake, target.rightToeBrake, brakeStep);
            current.markerPressed = target.markerPressed;
            current.resetPressed = target.resetPressed;
            current.pausePressed = target.pausePressed;
            current.telemetryTogglePressed = target.telemetryTogglePressed;
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }
}
