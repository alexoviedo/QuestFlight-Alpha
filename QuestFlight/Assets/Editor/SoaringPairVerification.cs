using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlexOviedo.QuestFlight.JSBSim;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    // Editor-only controls driver; cannot enter a player assembly or manual flight.
    internal static class SoaringTestPilot
    {
        public static void FollowRoute(in FlightDynamicsState s,in FlightDynamicsState tug,AircraftControlState controls)
        {
            Vector3 forward=tug.velocityUnityMetersPerSecond;forward.y=0;float speed=Mathf.Max(15,forward.magnitude);forward.Normalize();
            Vector3 right=new Vector3(forward.z,0,-forward.x),relative=tug.positionUnityMeters-s.positionUnityMeters;
            float turnCurvature=9.80665f*Mathf.Tan((float)tug.bankDegrees*Mathf.Deg2Rad)/(speed*speed);
            float expectedCross=.5f*66*66*turnCurvature;
            float cross=Vector3.Dot(relative,right)-expectedCross;
            float lateralRate=Vector3.Dot(tug.velocityUnityMetersPerSecond-s.velocityUnityMetersPerSecond,right);
            float bank=s.altitudeAglMeters>5?Mathf.Clamp((float)tug.bankDegrees+cross*.7f+lateralRate*2.5f,-30,30):0;
            float climb=tug.velocityUnityMetersPerSecond.y;
            float desiredHeight=tug.positionUnityMeters.y-climb/speed*66+8;
            float pitch=s.altitudeAglMeters>3?2.7f+Mathf.Atan2(climb,speed)*Mathf.Rad2Deg+
                Mathf.Clamp((desiredHeight-s.positionUnityMeters.y)*.15f+(climb-s.velocityUnityMetersPerSecond.y)*.7f,-6,6):s.calibratedAirspeedKnots>30?5:2;
            if(tug.weightOnWheels && !s.weightOnWheels && s.altitudeAglMeters>1)
                pitch=.2f+Mathf.Clamp((tug.positionUnityMeters.y+1.2f-s.positionUnityMeters.y)*.6f-s.velocityUnityMetersPerSecond.y*.9f,-4,4);
            controls.elevator=Mathf.Clamp(.08f*(pitch-(float)s.pitchDegrees)+.8f*s.angularVelocityBodyDegreesPerSecond.x*Mathf.Deg2Rad,-.65f,.65f);
            controls.aileron=Mathf.Clamp(.06f*(bank-(float)s.bankDegrees)+.4f*s.angularVelocityBodyDegreesPerSecond.z*Mathf.Deg2Rad,-.6f,.6f);
            controls.rudder=Mathf.Clamp(.04f*(float)s.sideslipDegrees-.2f*s.angularVelocityBodyDegreesPerSecond.y*Mathf.Deg2Rad,-.4f,.4f);
        }
        public static void Follow(in FlightDynamicsState s,in FlightDynamicsState tug,AircraftControlState controls,bool released,float heightOffset=0)
        {
            float targetPitch=2;
            if(s.altitudeAglMeters>3)
            {
                float speed=Mathf.Max(15,(float)tug.groundSpeedKnots*.514444f),climb=(float)tug.verticalSpeedFeetPerMinute*.00508f;
                float desiredHeight=(float)tug.altitudeMslMeters-climb/speed*60+heightOffset;
                targetPitch=2.7f+Mathf.Atan2(climb,speed)*Mathf.Rad2Deg+Mathf.Clamp((desiredHeight-(float)s.altitudeMslMeters)*.12f,-6,6);
            }
            if(released)targetPitch=.2f+Mathf.Clamp(((float)s.calibratedAirspeedKnots-50)*.6f,-7,8);
            controls.elevator=Mathf.Clamp(.08f*(targetPitch-(float)s.pitchDegrees)+.8f*s.angularVelocityBodyDegreesPerSecond.x*Mathf.Deg2Rad,-.65f,.65f);
            Vector3 toward=tug.positionUnityMeters-s.positionUnityMeters;
            float desiredHeading=!released?Mathf.Atan2(toward.x,toward.z)*Mathf.Rad2Deg:90;
            float bank=s.altitudeAglMeters>5?Mathf.Clamp((released?0:(float)tug.bankDegrees)+Mathf.DeltaAngle((float)s.headingDegrees,desiredHeading)*.8f,-30,30):0;
            controls.aileron=Mathf.Clamp(.06f*(bank-(float)s.bankDegrees)+.4f*s.angularVelocityBodyDegreesPerSecond.z*Mathf.Deg2Rad,-.6f,.6f);
            controls.rudder=Mathf.Clamp(.04f*(float)s.sideslipDegrees-.2f*s.angularVelocityBodyDegreesPerSecond.y*Mathf.Deg2Rad,-.4f,.4f);
        }
    }

    public static class SoaringPairVerification
    {
        private static bool withWeather;
        public static void RunWithWeather(){withWeather=true;Run();}
        [Serializable] public sealed class Sample { public FlightDynamicsState glider,tug;public TowlineSnapshot line; }
        [Serializable] public sealed class Case
        {
            public string name,error;public int hz;public bool swapped,gliderLiftOff,tugLiftOff,passed;
            public double peakTension,peakExtension,maximumPositionDelta,maximumAttitudeDelta,peakTensionRelativeDelta;
            public long measuredManagedBytes;public double nativePairMeanMilliseconds;
            public double configuredGliderPounds,gliderLiftOffSeconds,tugLiftOffSeconds;
            public GroundLaunchPhase phase;public List<Sample> samples=new List<Sample>();
        }
        [Serializable] private sealed class Report
        {
            public string scope="Production C# coupling and native aircraft, editor-only controls driver; desktop CPU measurements are not Quest performance.";
            public string toleranceFile="docs/soaring/TOW_NUMERICAL_ACCEPTANCE.json";public List<Case> cases=new List<Case>();public bool passed;
        }
        public static void Run()
        {
            var report=new Report();string output=System.Environment.GetEnvironmentVariable("QF_SOARING_QA");Directory.CreateDirectory(output);
            var g=AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(FlightAircraftDefinition.DefinitionFolder+"/SGS233A_Reference.asset");
            var t=AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(FlightAircraftDefinition.DefinitionFolder+"/PA18_Tug_Reference.asset");
            var terrain=AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>("Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset");
            var weather=AssetDatabase.FindAssets("t:BoulderWindFieldProfile").Select(id=>AssetDatabase.LoadAssetAtPath<BoulderWindFieldProfile>(AssetDatabase.GUIDToAssetPath(id))).First();
            Case Run(string name,int hz=120,bool swapped=false,float drag=0,float crosswind=0,float offset=0,string action="",bool heavy=false,int releaseSecond=70)
            {
                var c=new Case{name=name,hz=hz,swapped=swapped};report.cases.Add(c);
                var root=new GameObject("Isolated native pair verification");var w=UnityEngine.Object.Instantiate(weather);var line=ScriptableObject.CreateInstance<TowlineDefinition>();
                var gliderDefinition=UnityEngine.Object.Instantiate(g);
                if(heavy)foreach(var index in Enumerable.Range(0,gliderDefinition.nativeConfiguration.stations.Length))
                    gliderDefinition.nativeConfiguration.stations[index].pounds=215;
                c.configuredGliderPounds=610+gliderDefinition.nativeConfiguration.stations.Sum(station=>station.pounds);
                SoaringWeatherDefinition weatherDefinition=null;SoaringAtmosphereSession atmosphere=null;
                if(withWeather)
                {
                    weatherDefinition=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<SoaringWeatherDefinition>(SoaringAtmosphereAuthoring.Folder+"/ModerateThermal.asset"));
                    weatherDefinition.surfaceWind.y=weatherDefinition.upperWind.y=crosswind;
                    atmosphere=new SoaringAtmosphereSession(w,weatherDefinition);
                }
                if(crosswind!=0){w.surfaceWindEastNorth.y=crosswind;w.ridgeWindEastNorth.y=crosswind;}
                if(action=="weak_link")line.gliderWeakLinkNewtons=800;
                var timer=new System.Diagnostics.Stopwatch();long allocated=0;int measured=0;
                try
                {
                    FlightDynamicsInitialConditions Initial(float east,float above,bool engine)
                    {
                        if(!terrain.TrySample(east,-30,out var ground))throw new InvalidOperationException("Missing terrain");
                        var geo=FlightFrameConversions.UnityToGeodetic(new Vector3(east,ground.heightMeters+above,-30),GeodeticReference.Kbdu);
                        var ic=FlightDynamicsInitialConditions.KbduRunway();ic.latitudeDegrees=geo.latitudeDegrees;ic.longitudeDegrees=geo.longitudeDegrees;ic.altitudeMslMeters=geo.altitudeMslMeters;
                        ic.terrainElevationMslMeters=GeodeticReference.Kbdu.altitudeMslMeters+ground.heightMeters;ic.engineRunning=engine;ic.pitchDegrees=engine?8:0;return ic;
                    }
                    using(var player=new JSBSimNativeDevelopmentBackend(JSBSimRuntimeData.EditorPackageDataRoot))
                    {
                        if(!player.Initialize(new FlightDynamicsBackendContext{simulationRoot=root.transform,aircraftState=root.AddComponent<AircraftState>(),aircraftDefinition=gliderDefinition,terrainSurface=terrain,localOrigin=GeodeticReference.Kbdu,fixedDeltaTimeSeconds=1d/hz}))throw new InvalidOperationException(player.LastError);
                        if(atmosphere!=null && !player.NativeSession.SetConditions(atmosphere.Conditions))throw new InvalidOperationException(player.NativeSession.LastError);
                        if(!player.Reset(Initial(-555,.75f,false)))throw new InvalidOperationException(player.LastError);
                        using(var pair=new AerotowSimulation(JSBSimRuntimeData.EditorPackageDataRoot,t,line,terrain,Initial(-490.4f,1.2f,true),hz,atmosphere))
                        {
                            pair.SwapIntegrationOrderForTest=swapped;var controls=new AircraftControlState();controls.SetGliderNeutral();controls.trim=g.referenceTrimNoseUp;
                            for(int tick=0;tick<120*90;tick++)
                            {
                                bool released=pair.Line.state>=TowlineState.ReleasedByGlider;
                                if(action=="loading")SoaringTestPilot.FollowRoute(player.CurrentState,pair.TugState,controls);
                                else SoaringTestPilot.Follow(player.CurrentState,pair.TugState,controls,released,tick>40*120?offset:0);
                                controls.airbrake=tick>40*120?drag:0;
                                if(action=="abort" && tick==6*120)pair.AbortLaunch();
                                if(action=="tug_release" && tick==releaseSecond*120)pair.ReleaseTug();
                                if(action=="atmosphere_fault" && tick==10*120)
                                {if(atmosphere==null)w.surfaceWindEastNorth.x=float.NaN;else w.groundHeights[0]=float.NaN;}
                                if(action=="fault" && tick==10*120)player.NativeSession.Dispose();
                                player.SetControls(controls);
                                long before=GC.GetAllocatedBytesForCurrentThread();timer.Start();
                                bool ok=pair.Step(player,w,1d/120,action=="glider_release" && tick==releaseSecond*120);
                                timer.Stop();if(tick>120){allocated+=GC.GetAllocatedBytesForCurrentThread()-before;measured++;}
                                if(!ok)
                                {
                                    if((action=="fault" || action=="atmosphere_fault") && pair.Faulted && !pair.Step(player,w,1d/120,false)){c.passed=true;break;}
                                    throw new InvalidOperationException(pair.LastError);
                                }
                                var s=player.CurrentState;var ts=pair.TugState;
                                if(!c.gliderLiftOff&&!s.weightOnWheels&&s.altitudeAglMeters>3)c.gliderLiftOffSeconds=(tick+1)/120d;
                                if(!c.tugLiftOff&&!ts.weightOnWheels&&ts.altitudeAglMeters>3)c.tugLiftOffSeconds=(tick+1)/120d;
                                c.gliderLiftOff|=!s.weightOnWheels&&s.altitudeAglMeters>3;c.tugLiftOff|=!ts.weightOnWheels&&ts.altitudeAglMeters>3;
                                c.peakTension=pair.PeakTensionNewtons;c.peakExtension=pair.PeakExtensionMeters;c.phase=pair.LaunchPhase;
                                if(tick%60==0)c.samples.Add(new Sample{glider=s,tug=ts,line=pair.Line});
                            }
                            c.measuredManagedBytes=allocated;c.nativePairMeanMilliseconds=timer.Elapsed.TotalMilliseconds/Math.Max(1,measured+121);
                            if(action!="fault" && action!="atmosphere_fault")c.passed=string.IsNullOrEmpty(c.error) && c.peakExtension<2 && (action=="abort" ? !c.gliderLiftOff&&!c.tugLiftOff&&pair.LaunchPhase==GroundLaunchPhase.Aborted&&pair.TugState.groundSpeedKnots<1 : action=="weak_link" ? pair.Line.state==TowlineState.BrokenAtGlider : c.gliderLiftOff&&c.tugLiftOff);
                            if(action=="tug_release")c.passed &= pair.Line.state==TowlineState.ReleasedByTug && pair.Line.gliderEndAttached && !pair.Line.tugEndAttached && pair.Line.tensionNewtons==0;
                            if(action=="glider_release")c.passed &= pair.Line.state==TowlineState.ReleasedByGlider && !pair.Line.gliderEndAttached && pair.Line.tugEndAttached && pair.Line.tensionNewtons==0;
                        }
                    }
                }
                catch(Exception ex){c.error=ex.ToString();c.passed=false;Debug.LogException(ex);}
                finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(w);UnityEngine.Object.DestroyImmediate(line);UnityEngine.Object.DestroyImmediate(gliderDefinition);if(weatherDefinition!=null)UnityEngine.Object.DestroyImmediate(weatherDefinition);}
                File.WriteAllText(Path.Combine(output,name+".json"),JsonUtility.ToJson(c,true));Debug.Log("[SoaringPair] "+name+" pass="+c.passed+" peak="+c.peakTension);return c;
            }
            var reference=Run("rate480",480);var intermediate=Run("rate240",240);var production=Run("rate120",120);var swappedCase=Run("swapped120",120,true);
            void Compare(Case a,Case b,double distance,double attitude,double tension)
            {
                for(int i=0;i<Math.Min(a.samples.Count,b.samples.Count);i++)
                {
                    a.maximumPositionDelta=Math.Max(a.maximumPositionDelta,Math.Max(Vector3.Distance(a.samples[i].glider.positionUnityMeters,b.samples[i].glider.positionUnityMeters),Vector3.Distance(a.samples[i].tug.positionUnityMeters,b.samples[i].tug.positionUnityMeters)));
                    a.maximumAttitudeDelta=Math.Max(a.maximumAttitudeDelta,Math.Max(Quaternion.Angle(a.samples[i].glider.rotationUnity,b.samples[i].glider.rotationUnity),Quaternion.Angle(a.samples[i].tug.rotationUnity,b.samples[i].tug.rotationUnity)));
                }
                a.peakTensionRelativeDelta=Math.Abs(a.peakTension-b.peakTension)/Math.Max(1,b.peakTension);a.passed &= a.samples.Count==b.samples.Count && a.maximumPositionDelta<=distance && a.maximumAttitudeDelta<=attitude && a.peakTensionRelativeDelta<=tension;
            }
            Compare(intermediate,reference,5,3,.10);Compare(production,reference,5,3,.10);Compare(swappedCase,production,.000001,.001,.0000000001);
            var extraDrag=Run("extra_drag",drag:.5f);extraDrag.passed &= extraDrag.samples.Last().tug.altitudeMslMeters<production.samples.Last().tug.altitudeMslMeters-5;
            // Loading comparison uses the same full-flight high-tow driver for
            // both masses. The basic offset driver can enter the wake at maximum
            // mass; that earlier failure is retained as a test-pilot limitation.
            var loadingReference=Run("loading_950lb",action:"loading");
            var heavyLoad=Run("heavy_1040lb",action:"loading",heavy:true);
            heavyLoad.passed &= heavyLoad.samples.Last().tug.altitudeMslMeters<loadingReference.samples.Last().tug.altitudeMslMeters-1
                && heavyLoad.tugLiftOffSeconds>loadingReference.tugLiftOffSeconds;
            Run("early_release_60s",action:"glider_release",releaseSecond:60);
            Run("crosswind3",crosswind:3);Run("high_offset",offset:12);Run("low_offset",offset:-12);Run("ground_abort",action:"abort");Run("tug_release",action:"tug_release");Run("glider_release",action:"glider_release");Run("weak_link",action:"weak_link");Run("native_fault",action:"fault");if(!withWeather)Run("atmosphere_fault",action:"atmosphere_fault");
            report.passed=report.cases.All(c=>c.passed);File.WriteAllText(Path.Combine(output,"pair_verification.json"),JsonUtility.ToJson(report,true));EditorApplication.Exit(report.passed?0:1);
        }
    }
}
