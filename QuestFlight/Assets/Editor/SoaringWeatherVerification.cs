using System;
using System.Collections.Generic;
using System.Globalization;
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
    public static class SoaringWeatherVerification
    {
        [Serializable] private sealed class Sample {public FlightDynamicsState state;public Vector3 wind;public double energy,pressureAltitude,teFpm;public float thermal,ridge;}
        [Serializable] private sealed class Case
        {
            public string id,error;public bool passed;public float minimumAgl=100000,maximumThermal,minimumThermal,maximumRidge;
            public double initialEnergy,finalEnergy,maximumEnergy,tailMeanClimbMps,seconds;public int outwardTurns;public long allocatedBytes;public List<Sample> samples=new List<Sample>();
        }
        [Serializable] private sealed class Report {public string scope="Native glider and production atmosphere/incremental loads with isolated editor test controls. No operator or Quest acceptance.";public float maximumWindGridTerrainDifference,maximumRidgeCorridorTerrainDifference;public int terrainGridSamples;public List<Case> cases=new List<Case>();public bool passed;}
        public static void Run()
        {
            string output=System.Environment.GetEnvironmentVariable("QF_SOARING_QA");Directory.CreateDirectory(output);var report=new Report();
            var world=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");var authored=world.GetComponentInChildren<FlightDynamicsCoordinator>(true);
            var aircraft=AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(FlightAircraftDefinition.DefinitionFolder+"/SGS233A_Reference.asset");
            var field=authored.windField;var terrain=authored.terrainSurface;
            for(int row=0;row<field.height;row++)for(int col=0;col<field.width;col++)
            {
                if(terrain.TrySample(field.minimumEnu.x+col*field.spacingMeters,field.minimumEnu.y+row*field.spacingMeters,out var ground))
                {
                    float error=Mathf.Abs(ground.heightMeters-field.groundHeights[row*field.width+col]);report.maximumWindGridTerrainDifference=Mathf.Max(report.maximumWindGridTerrainDifference,error);report.terrainGridSamples++;
                    float east=field.minimumEnu.x+col*field.spacingMeters,north=field.minimumEnu.y+row*field.spacingMeters;
                    if(east>=-6600&&east<=-5600&&north>=700&&north<=2500)report.maximumRidgeCorridorTerrainDifference=Mathf.Max(report.maximumRidgeCorridorTerrainDifference,error);
                }
            }
            using(var csv=new StreamWriter(Path.Combine(output,"ridge_sections.csv")))
            {
                csv.WriteLine("east,north,agl,ground,lift,reverse,parallel,calm");
                for(int north=800;north<=2600;north+=300)for(int east=-7300;east<=-5400;east+=50)foreach(int agl in new[]{50,100,150,220})
                {
                    if(!terrain.TrySample(east,north,out var ground))continue;var at=new Vector3(east,ground.heightMeters+agl,north);
                    var wind=new Vector2(-5.5f,0);var normal=field.SampleConfiguredFlow(at,0,wind,wind,0);var reverse=field.SampleConfiguredFlow(at,0,-wind,-wind,0);var parallel=field.SampleConfiguredFlow(at,0,new Vector2(0,5.5f),new Vector2(0,5.5f),0);
                    csv.WriteLine(FormattableString.Invariant($"{east},{north},{agl},{ground.heightMeters},{normal.terrain},{reverse.terrain},{parallel.terrain},0"));
                }
            }
            void RunCase(string id,string profile,string kind,float height=500)
            {
                var c=new Case{id=id};report.cases.Add(c);var definition=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<SoaringWeatherDefinition>(SoaringAtmosphereAuthoring.Folder+"/"+profile+".asset"));
                var root=new GameObject("Isolated soaring native scenario");
                try
                {
                    if(kind=="decay"){var source=definition.sources[0];source.lifetime=300;source.period=2400;source.initialAge=130;definition.sources[0]=source;}
                    if(kind=="miss")definition.thermalEnabled=false;
                    if(kind=="ridge_reverse")definition.surfaceWind=-definition.surfaceWind;
                    if(kind=="ridge_reverse")definition.upperWind=-definition.upperWind;
                    if(kind=="ridge_parallel"){definition.surfaceWind=new Vector2(0,2.5f);definition.upperWind=new Vector2(0,5.5f);}
                    if(kind=="ridge_calm"){definition.surfaceWind=definition.upperWind=Vector2.zero;definition.gustAmplitude=0;}
                    if(kind.StartsWith("ridge"))definition.thermalEnabled=false;
                    var atmosphere=new SoaringAtmosphereSession(field,definition);bool ridge=kind.StartsWith("ridge");bool traverse=kind=="traverse";
                    var source0=atmosphere.Source(0);float y=source0.groundHeight+height;
                    Vector2 center=SoaringAtmosphereSession.Center(source0,y,definition.upperWind);
                    Vector3 start=ridge?new Vector3(-6150,0,1400):new Vector3(center.x+(traverse?-650:100),y,center.y);
                    if(!terrain.TrySample(start.x,start.z,out var floor))throw new InvalidOperationException("Start terrain missing");if(ridge)start.y=floor.heightMeters+120;
                    var geo=FlightFrameConversions.UnityToGeodetic(start,GeodeticReference.Kbdu);
                    var initial=aircraft.airborneReference;initial.latitudeDegrees=geo.latitudeDegrees;initial.longitudeDegrees=geo.longitudeDegrees;initial.altitudeMslMeters=geo.altitudeMslMeters;initial.terrainElevationMslMeters=GeodeticReference.Kbdu.altitudeMslMeters+floor.heightMeters;
                    initial.calibratedAirspeedKnots=40;initial.headingDegrees=ridge?0:traverse?90:166;initial.pitchDegrees=traverse||ridge?2:4;initial.bankDegrees=traverse||ridge?0:25;
                    using(var backend=new JSBSimNativeDevelopmentBackend(JSBSimRuntimeData.EditorPackageDataRoot))
                    {
                        if(!backend.Initialize(new FlightDynamicsBackendContext{simulationRoot=root.transform,aircraftState=root.AddComponent<AircraftState>(),terrainSurface=terrain,aircraftDefinition=aircraft,localOrigin=GeodeticReference.Kbdu}))throw new InvalidOperationException(backend.LastError);
                        if(!backend.NativeSession.SetConditions(atmosphere.Conditions))throw new InvalidOperationException(backend.NativeSession.LastError);
                        backend.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(atmosphere.Sample(start,0)));if(!backend.Reset(initial))throw new InvalidOperationException(backend.LastError);
                        var controls=new AircraftControlState();controls.SetGliderNeutral();controls.trim=aircraft.referenceTrimNoseUp;var instruments=new SoaringFlightInstruments();
                        int duration=traverse?90:ridge&&kind!="ridge"?60:kind=="orbit"?600:300,tailCount=0,direction=1;bool turning=false,turnedEast=false;double lastTurn=-100;float pitchIntegral=0;
                        double Energy()=>backend.NativeSession.AirData.pressureAltitudeM+backend.NativeSession.AirData.trueAirspeedMps*backend.NativeSession.AirData.trueAirspeedMps/(2*9.80665);
                        c.initialEnergy=Energy();c.maximumEnergy=c.initialEnergy;
                        for(int tick=0;tick<duration*120;tick++)
                        {
                            var s=backend.CurrentState;var components=atmosphere.SampleComponents(s.positionUnityMeters,s.simulationTimeSeconds);Vector3 wind=components.Total;
                            float desiredHeading=90,bank=0;
                            if(ridge)
                            {
                                if(!turning&&s.simulationTimeSeconds-lastTurn>12 && (s.positionUnityMeters.z-1550)*direction>350){turning=true;turnedEast=false;lastTurn=s.simulationTimeSeconds;c.outwardTurns++;}
                                if(turning)
                                {
                                    desiredHeading=90;
                                    if(Mathf.Abs(Mathf.DeltaAngle((float)s.headingDegrees,90))<20)turnedEast=true;
                                    if(turnedEast)desiredHeading=direction==1?180:0;
                                    bank=direction*28;
                                    if(turnedEast&&Mathf.Abs(Mathf.DeltaAngle((float)s.headingDegrees,desiredHeading))<10){turning=false;direction=-direction;}
                                }
                                else
                                {
                                    Vector3 desired=new Vector3(Mathf.Clamp((-6150-s.positionUnityMeters.x)*.05f,-5,5),0,direction*25)-new Vector3(wind.x,0,wind.z);
                                    desiredHeading=Mathf.Atan2(desired.x,desired.z)*Mathf.Rad2Deg;bank=Mathf.Clamp(Mathf.DeltaAngle((float)s.headingDegrees,desiredHeading)*1.2f,-30,30);
                                }
                            }
                            else if(!traverse)
                            {
                                center=SoaringAtmosphereSession.Center(source0,s.positionUnityMeters.y,new Vector2(components.background.x,components.background.z));
                                Vector3 radial=s.positionUnityMeters-new Vector3(center.x,s.positionUnityMeters.y,center.y);float radius=radial.magnitude;
                                Vector3 tangent=new Vector3(radial.z,0,-radial.x).normalized;
                                Vector3 desired=(tangent-radial.normalized*Mathf.Clamp((radius-100)*.006f,-1,1))*25-new Vector3(wind.x,0,wind.z);
                                float groundSpeed=new Vector2(s.velocityUnityMetersPerSecond.x,s.velocityUnityMetersPerSecond.z).magnitude;
                                float feedforward=Mathf.Atan2(groundSpeed*groundSpeed,9.80665f*100)*Mathf.Rad2Deg;
                                desiredHeading=Mathf.Atan2(desired.x,desired.z)*Mathf.Rad2Deg;bank=Mathf.Clamp(feedforward+Mathf.DeltaAngle((float)s.headingDegrees,desiredHeading)*.7f,-40,40);
                            }
                            else bank=Mathf.Clamp(Mathf.DeltaAngle((float)s.headingDegrees,desiredHeading),-20,20);
                            if(ridge&&kind!="ridge"&&s.simulationTimeSeconds>25)bank=Mathf.Clamp(Mathf.DeltaAngle((float)s.headingDegrees,90),-25,25);
                            float speedError=(float)s.calibratedAirspeedKnots-40;pitchIntegral=Mathf.Clamp(pitchIntegral+speedError*.03f/120,-6,8);
                            float targetPitch=2+pitchIntegral+Mathf.Clamp(speedError*.7f,-6,7);
                            controls.elevator=Mathf.Clamp(.09f*(targetPitch-(float)s.pitchDegrees)+.8f*s.angularVelocityBodyDegreesPerSecond.x*Mathf.Deg2Rad,-.7f,.7f);
                            controls.aileron=Mathf.Clamp(.06f*(bank-(float)s.bankDegrees)+.4f*s.angularVelocityBodyDegreesPerSecond.z*Mathf.Deg2Rad,-.6f,.6f);
                            controls.rudder=Mathf.Clamp(.04f*(float)s.sideslipDegrees-.2f*s.angularVelocityBodyDegreesPerSecond.y*Mathf.Deg2Rad,-.4f,.4f);
                            long before=GC.GetAllocatedBytesForCurrentThread();
                            var load=AircraftAirflowSampler.Sample(s,backend.NativeSession.Diagnostics,atmosphere,wind,null,true);
                            if(!AircraftAirflowSampler.Submit(backend.NativeSession,load))throw new InvalidOperationException(backend.NativeSession.LastError);
                            backend.SetControls(controls);backend.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(wind));if(!backend.Advance(1d/120))throw new InvalidOperationException(backend.LastError);
                            if(tick>120)c.allocatedBytes+=GC.GetAllocatedBytesForCurrentThread()-before;
                            s=backend.CurrentState;instruments.Sample(backend.NativeSession.AirData,s.simulationTimeSeconds);c.seconds=s.simulationTimeSeconds;c.finalEnergy=Energy();c.maximumEnergy=Math.Max(c.maximumEnergy,c.finalEnergy);
                            c.minimumAgl=Mathf.Min(c.minimumAgl,(float)s.altitudeAglMeters);c.maximumThermal=Mathf.Max(c.maximumThermal,components.thermal);c.minimumThermal=Mathf.Min(c.minimumThermal,components.thermal);c.maximumRidge=Mathf.Max(c.maximumRidge,components.terrain);
                            if(tick>(duration-60)*120){c.tailMeanClimbMps+=s.verticalSpeedFeetPerMinute*.00508;tailCount++;}
                            if(tick%120==0)c.samples.Add(new Sample{state=s,wind=wind,energy=c.finalEnergy,pressureAltitude=backend.NativeSession.AirData.pressureAltitudeM,teFpm=instruments.IdealTotalEnergyFpm,thermal=components.thermal,ridge=components.terrain});
                            if(s.altitudeAglMeters<15)throw new InvalidOperationException("Test pilot reached minimum clearance; no pose/energy recovery applied.");
                        }
                        c.tailMeanClimbMps/=Math.Max(1,tailCount);
                        c.passed=c.allocatedBytes==0 && c.seconds>=duration-.01 && c.minimumAgl>=15;
                        if(traverse)c.passed &= c.maximumThermal>.3 && c.minimumThermal<-.03;
                        else if(ridge)c.passed &= kind=="ridge"?c.outwardTurns>=3&&c.finalEnergy>=c.initialEnergy-20&&c.maximumThermal==0&&c.minimumAgl>=60:c.finalEnergy<c.initialEnergy;
                        else c.passed &= kind=="miss"||kind=="decay"?c.tailMeanClimbMps<-.2:c.finalEnergy>c.initialEnergy+20;
                    }
                }
                catch(Exception ex){c.error=ex.ToString();Debug.LogException(ex);}
                finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(definition);}
                File.WriteAllText(Path.Combine(output,id+".json"),JsonUtility.ToJson(c,true));Debug.Log("[SoaringWeather] "+id+" pass="+c.passed+" deltaEnergy="+(c.finalEnergy-c.initialEnergy)+" clearance="+c.minimumAgl);
            }
            RunCase("traverse200","ModerateThermal","traverse",200);RunCase("traverse500","ModerateThermal","traverse",500);RunCase("traverse900","ModerateThermal","traverse",900);
            RunCase("thermal_orbit","ModerateThermal","orbit");RunCase("thermal_miss","ModerateThermal","miss");RunCase("thermal_decay","ModerateThermal","decay");
            RunCase("ridge300","ModerateRidge","ridge");RunCase("ridge_reverse","ModerateRidge","ridge_reverse");RunCase("ridge_parallel","ModerateRidge","ridge_parallel");RunCase("ridge_calm","ModerateRidge","ridge_calm");
            RunCase("ridge_supported300","StrongerSupported","ridge");
            report.passed=report.cases.All(c=>c.passed);File.WriteAllText(Path.Combine(output,"weather_verification.json"),JsonUtility.ToJson(report,true));EditorApplication.Exit(report.passed?0:1);
        }
    }
}
