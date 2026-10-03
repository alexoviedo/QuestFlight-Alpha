using System;
using System.IO;
using System.Reflection;
using AlexOviedo.QuestFlight.JSBSim;
using QuestFlight.Product;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Runtime;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    /// <summary>Runs the actual native model with the authored route and production guidance.</summary>
    public static class DemoFlightNativeValidation
    {
        private static string scenario="default";
        public static void RunFullFlight() { RunFullFlightCore();EditorApplication.Exit(0); }
        public static void RunShortShowcaseSuite()
        {
            try {foreach(string name in new[]{"short8","short26"}){scenario=name;RunFullFlightCore();}EditorApplication.Exit(0);}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void RunSuite()
        {
            try
            {
                foreach(string name in new[]{"default","wind40","wind80","runway26","occupied","handoff","early_return"}) {scenario=name;RunFullFlightCore();}
                EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        private static Vector3 ScenarioWind(FlightDynamicsCoordinator player,Vector3 position,double time)
        {
            float offset=scenario=="wind40"?40:scenario=="wind80"?80:0;
            Vector3 wind=player.windField.Sample(position,time+offset);
            if(scenario=="runway26"||scenario=="short26") {wind.x=4+Mathf.Sin((float)time*.13f)*.6f;wind.z=1.5f+Mathf.Sin((float)time*.08f)*.4f;}
            return wind;
        }
        private static void RunFullFlightCore()
        {
            try
            {
                string output=System.Environment.GetEnvironmentVariable("QFL_DEMO_EVIDENCE");
                bool runway26=scenario=="runway26"||scenario=="short26";
                var world=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");
                var player=world.GetComponentInChildren<FlightDynamicsCoordinator>(true);
                var pilot=world.GetComponentInChildren<ProductDemoPilot>(true);
                var scenic=AssetDatabase.LoadAssetAtPath<FlightLessonRouteProfile>("Assets/Production/Profiles/KBDU_ScenicDemo"+(runway26?26:8)+".asset");
                if(scenic!=null)pilot.ConfigureRouteProfileForAuthoring(scenic);
                var exitRoute=AssetDatabase.LoadAssetAtPath<QuestFlightLab.Environment.AmbientAircraftRoute>("Assets/Production/Environment/AmbientAircraftV1/Route_0_"+(runway26?26:8)+".asset");
                pilot.ConfigureTourForAuthoring(exitRoute,null);pilot.StartDemo();
                if(!pilot.IsRunning)throw new Exception("Demo did not initialize");
                using(var backend=new JSBSimNativeDevelopmentBackend(JSBSimRuntimeData.EditorPackageDataRoot))
                {
                    if(!backend.Initialize(new FlightDynamicsBackendContext{simulationRoot=player.simulationRoot,aircraftState=player.aircraftState,terrainSurface=player.terrainSurface}))throw new Exception(backend.LastError);
                    Vector3 position=pilot.RouteDefinition.waypoints[0].positionUnityMeters;
                    player.terrainSurface.TrySample(position.x,position.z,out var ground);position.y=ground.heightMeters+1.4f;
                    var geo=FlightFrameConversions.UnityToGeodetic(position,GeodeticReference.Kbdu);
                    var initial=new FlightDynamicsInitialConditions{latitudeDegrees=geo.latitudeDegrees,longitudeDegrees=geo.longitudeDegrees,altitudeMslMeters=geo.altitudeMslMeters,terrainElevationMslMeters=GeodeticReference.Kbdu.altitudeMslMeters+ground.heightMeters,headingDegrees=scenario=="runway26"?270:90,engineRunning=true};
                    backend.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(ScenarioWind(player,position,0)));
                    if(scenario.StartsWith("short"))
                    {if(!ProductDemoPilot.TryBuildShortArrival(pilot.RouteDefinition,player.terrainSurface,out initial,out int segment))throw new Exception("Invalid showcase start");pilot.RestoreLessonSegment(segment);}
                    if(!backend.Reset(initial))throw new Exception(backend.LastError);
                    var controls=AircraftControlState.Neutral(.1f);var target=AircraftControlState.Neutral();
                    if(scenario.StartsWith("short"))controls.throttle=.5f;
                    string phase="",failure=null;bool airborne=false,completed=false;float sink=0;bool landed=false;bool taken=false,handedBack=false;int manualSegment=-1;
                    using(var csv=new StreamWriter(Path.Combine(output,"full_flight_"+scenario+".csv")))
                    {
                        csv.WriteLine("time,phase,segment,east,north,agl,msl,cas,gs,pitch,bank,vs,wow,throttle,elevator");
                        for(int tick=0;tick<1800*120;tick++)
                        {
                            var s=backend.CurrentState;float priorVs=(float)s.verticalSpeedFeetPerMinute;
                            if(tick%2==0)
                            {
                                bool manualCase=scenario=="handoff" || scenario=="early_return";
                                if(manualCase && !taken && s.simulationTimeSeconds>320)
                                {pilot.TakeUserControl();taken=true;manualSegment=pilot.ProgressState.currentSegmentIndex;}
                                if(taken && !handedBack && s.simulationTimeSeconds>332)
                                {
                                    if(!pilot.TryResumeFromState(s,QuestFlightLab.Input.ProductControlInput.ToFlightLessonCommand(controls),scenario=="early_return"))
                                    {failure="Rejoin rejected: "+pilot.Phase;break;}
                                    handedBack=true;
                                }
                                FlightLessonControlCommand command;
                                if(taken && !handedBack)
                                {
                                    if(pilot.TryEvaluateGuidance(s,1f/60,out _,out _) || pilot.ProgressState.currentSegmentIndex!=manualSegment)
                                    {failure="Guidance fought manual ownership";break;}
                                    var a=ProductDemoPilot.BuildGuidanceAircraftState(s);
                                    command=QuestFlightLab.Input.ProductControlInput.ToFlightLessonCommand(controls);
                                    command.aileron=Mathf.Clamp((15-a.bankDegrees)*.045f-a.rollRateDegreesPerSecond*.022f,-.4f,.4f);
                                    command.elevator=Mathf.Clamp((3-a.pitchDegrees)*.075f-a.pitchRateDegreesPerSecond*.03f,-.4f,.4f);
                                    command.throttle=.7f;phase="manual_flight";
                                }
                                else if(!pilot.TryEvaluateGuidance(s,1f/60,out command,out phase,scenario!="occupied" || s.simulationTimeSeconds>780)){failure=pilot.Phase;break;}
                                QuestFlightLab.Input.ProductControlInput.CopyFlightLessonCommandInto(command,target);
                                ProductDemoPilot.RateLimitControlsInto(target,controls,1f/60);
                            }
                            backend.SetControls(controls);backend.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(ScenarioWind(player,s.positionUnityMeters,s.simulationTimeSeconds)));
                            if(!backend.Advance(1.0/120))throw new Exception(backend.LastError);
                            s=backend.CurrentState;airborne|=!s.weightOnWheels && s.altitudeAglMeters>20;
                            if(airborne&&!landed&&s.weightOnWheels){landed=true;sink=priorVs;if(priorVs< -600)failure="Hard touchdown";}
                            if(tick%120==0)csv.WriteLine(FormattableString.Invariant($"{s.simulationTimeSeconds:F1},{phase},{pilot.ProgressState.currentSegmentIndex},{s.positionUnityMeters.x:F1},{s.positionUnityMeters.z:F1},{s.altitudeAglMeters:F1},{s.altitudeMslMeters/.3048:F0},{s.calibratedAirspeedKnots:F1},{s.groundSpeedKnots:F1},{s.pitchDegrees:F1},{s.bankDegrees:F1},{s.verticalSpeedFeetPerMinute:F0},{s.weightOnWheels},{controls.throttle:F3},{controls.elevator:F3}"));
                            if(!s.IsFinite || s.altitudeAglMeters<0 || Math.Abs(s.bankDegrees)>65)failure="Invalid trajectory";
                            if(phase=="complete_clear_of_runway"){completed=true;break;}
                            if(failure!=null)break;
                        }
                    }
                    File.WriteAllText(Path.Combine(output,"full_flight_"+scenario+"_summary.txt"),$"Complete={completed}\nFailure={failure}\nPhase={phase}\nTouchdownSink={sink}\nFinal={backend.CurrentState.positionUnityMeters}\nSeconds={backend.CurrentState.simulationTimeSeconds}\n");
                    if (!completed || failure != null)
                        throw new InvalidOperationException($"Demo scenario {scenario} failed: {failure ?? phase}");
                }
                pilot.StopDemo();
            }
            catch(Exception e){Debug.LogException(e);throw;}
        }

        public static void RunBaseline()
            => RunApproach(false);
        public static void RunCandidate()
            => RunApproach(true);

        private static void RunApproach(bool candidate)
        {
            try
            {
                string output=System.Environment.GetEnvironmentVariable("QFL_DEMO_EVIDENCE");
                Directory.CreateDirectory(output);
                var world=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");
                var player=world.GetComponentInChildren<FlightDynamicsCoordinator>(true);
                var route=world.GetComponentInChildren<ProductDemoPilot>(true).AuthoredRouteProfile.Definition;
                var configure=typeof(ProductDemoPilot).GetMethod("ConfigureCommandForSegment",BindingFlags.NonPublic|BindingFlags.Static);
                using(var backend=new JSBSimNativeDevelopmentBackend(JSBSimRuntimeData.EditorPackageDataRoot))
                {
                    if(!backend.Initialize(new FlightDynamicsBackendContext{simulationRoot=player.simulationRoot,aircraftState=player.aircraftState,terrainSurface=player.terrainSurface}))throw new Exception(backend.LastError);
                    var waypoint=route.waypoints[10];
                    var position=waypoint.positionUnityMeters;
                    position.y=waypoint.altitudeMslFeet*.3048f-(float)GeodeticReference.Kbdu.altitudeMslMeters;
                    player.terrainSurface.TrySample(position.x,position.z,out var ground);
                    var geo=FlightFrameConversions.UnityToGeodetic(position,GeodeticReference.Kbdu);
                    var initial=new FlightDynamicsInitialConditions{latitudeDegrees=geo.latitudeDegrees,longitudeDegrees=geo.longitudeDegrees,altitudeMslMeters=geo.altitudeMslMeters,terrainElevationMslMeters=GeodeticReference.Kbdu.altitudeMslMeters+ground.heightMeters,headingDegrees=90,calibratedAirspeedKnots=70,pitchDegrees=-3,flightPathAngleDegrees=-3,engineRunning=true};
                    backend.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(player.windField.Sample(position,0)));
                    if(!backend.Reset(initial))throw new Exception(backend.LastError);
                    var progress=FlightLessonProgressState.InitialFor(route);progress.currentSegmentIndex=10;
                    var guidance=new FlightLessonGuidanceController();
                    var controls=AircraftControlState.Neutral(.25f);controls.flaps=.75f;
                    var target=AircraftControlState.Neutral();
                    var landing=new DemoLandingController();
                    player.terrainSurface.TrySample(-630,-30,out var thresholdGround);
                    player.terrainSurface.TrySample(630,-30,out var endGround);
                    var exitRoute=AssetDatabase.LoadAssetAtPath<QuestFlightLab.Environment.AmbientAircraftRoute>("Assets/Production/Environment/AmbientAircraftV1/Route_0_8.asset");
                    landing.Reset(new Vector3(-630,thresholdGround.heightMeters,-30),new Vector3(630,endGround.heightMeters,-30),exitRoute.segments[10].points);
                    string prefix=candidate?"candidate_approach":"original_approach";
                    bool touchdown=false,airborneRollout=false;float contactSink=0;int endTick=0;
                    using(var csv=new StreamWriter(Path.Combine(output,prefix+".csv")))
                    {
                        csv.WriteLine("time,segment,east,north,agl,altitude_ft,cas,groundspeed,pitch,bank,vs,wow,throttle,elevator,brake");
                        for(int tick=0;tick<300*120;tick++)
                        {
                            var s=backend.CurrentState;float priorVs=(float)s.verticalSpeedFeetPerMinute;
                            if(tick%2==0)
                            {
                                if(!candidate) progress=FlightLessonNavigator.AdvanceIfCaptured(route,progress,ProductDemoPilot.BuildNavigationState(s));
                                FlightLessonControlCommand command;
                                if(candidate) command=landing.Step(s,1f/60,true);
                                else if(progress.lessonComplete)command=new FlightLessonControlCommand{throttle=.1f,mixture=1,flaps=1,leftToeBrake=.65f,rightToeBrake=.65f};
                                else
                                {
                                    if(!FlightLessonNavigator.TryEvaluate(route,progress,ProductDemoPilot.BuildNavigationState(s),out var intent))throw new Exception("Invalid route");
                                    if(!guidance.TryStep(intent,ProductDemoPilot.BuildGuidanceAircraftState(s),1f/60,FlightLessonGuidanceControllerSettings.Default,out command))throw new Exception("Invalid guidance");
                                    object[] args={intent,command};configure.Invoke(null,args);command=(FlightLessonControlCommand)args[1];
                                }
                                QuestFlightLab.Input.ProductControlInput.CopyFlightLessonCommandInto(command,target);
                                ProductDemoPilot.RateLimitControlsInto(target,controls,1f/60);
                            }
                            airborneRollout|=progress.currentSegmentIndex==11&&!s.weightOnWheels;
                            backend.SetControls(controls);backend.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(player.windField.Sample(s.positionUnityMeters,s.simulationTimeSeconds)));
                            if(!backend.Advance(1.0/120))throw new Exception(backend.LastError);
                            s=backend.CurrentState;endTick=tick;
                            if(!touchdown&&s.weightOnWheels){touchdown=true;contactSink=priorVs;}
                            if(tick%12==0)csv.WriteLine(FormattableString.Invariant($"{s.simulationTimeSeconds:F2},{(candidate?(int)landing.Phase:progress.currentSegmentIndex)},{s.positionUnityMeters.x:F2},{s.positionUnityMeters.z:F2},{s.altitudeAglMeters:F2},{s.altitudeMslMeters/.3048:F1},{s.calibratedAirspeedKnots:F1},{s.groundSpeedKnots:F1},{s.pitchDegrees:F2},{s.bankDegrees:F2},{s.verticalSpeedFeetPerMinute:F1},{s.weightOnWheels},{controls.throttle:F3},{controls.elevator:F3},{controls.leftToeBrake:F3}"));
                            if(!s.IsFinite||s.altitudeAglMeters<0||Math.Abs(s.bankDegrees)>65||(candidate?landing.Phase==DemoLandingController.LandingPhase.Stopped:touchdown&&s.groundSpeedKnots<1))break;
                        }
                    }
                    var final=backend.CurrentState;
                    File.WriteAllText(Path.Combine(output,prefix+"_summary.txt"),FormattableString.Invariant($"Seconds={endTick/120f:F2}\nAirborneRollout={airborneRollout}\nTouchdown={touchdown}\nPrecontactSinkFpm={contactSink:F1}\nFinalPosition={final.positionUnityMeters}\nFinalGroundSpeed={final.groundSpeedKnots:F1}\nFinite={final.IsFinite}\nPhase={landing.Phase}\nGoAround={landing.GoAroundReason}\nNote=Native authored final scenario; no manual input or runtime crash recovery, not headset acceptance.\n"));
                }
                EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
