using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Runtime;
using AlexOviedo.QuestFlight.JSBSim;
namespace QuestFlightLab.Editor
{
    public static class SharedAtmosphereNativeValidation
    {
        [Serializable] public class Result { public string id,error;public bool passed,ground,airborne,touchdown;public float finalAgl,maxBank,minAgl,minTerrain=100,maxTerrain=-100,maxThermal,touchdownSink,touchdownSpeed;public Vector3 finalPosition; }
        [Serializable] public class Report { public Result[] cases; }
        public static void RunBatch()
        {
            try
            {
                string output=System.Environment.GetEnvironmentVariable("QFL_ATMOSPHERE_EVIDENCE");Directory.CreateDirectory(output);
                var world=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");
                var player=world.GetComponentInChildren<FlightDynamicsCoordinator>(true);
                var results=new System.Collections.Generic.List<Result>();
                foreach(string id in new[]{"takeoff","crosswind_takeoff","approach","thermal","ridge","lee"})results.Add(Run(player,output,id));
                File.WriteAllText(Path.Combine(output,"native_field_results.json"),JsonUtility.ToJson(new Report{cases=results.ToArray()},true));
                EditorApplication.Exit(results.TrueForAll(r=>r.passed)?0:2);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        static Result Run(FlightDynamicsCoordinator player,string output,string id)
        {
            var r=new Result{id=id,minAgl=10000};
            var wind=UnityEngine.Object.Instantiate(player.windField);
            if(id=="crosswind_takeoff") {wind.surfaceWindEastNorth=new Vector2(-2,2);wind.ridgeWindEastNorth=new Vector2(-4,3);}
            using(var backend=new JSBSimNativeDevelopmentBackend(JSBSimRuntimeData.EditorPackageDataRoot))
            try
            {
                if(!backend.Initialize(new FlightDynamicsBackendContext{aircraftDefinition=player.aircraftDefinition,simulationRoot=player.simulationRoot,aircraftState=player.aircraftState,terrainSurface=player.terrainSurface}))throw new Exception(backend.LastError);
                bool takeoff=id.Contains("takeoff"), approach=id=="approach";
                Vector3 at=takeoff?new Vector3(-555,0,-30):approach?new Vector3(-1800,80,-30):id=="thermal"?new Vector3(1350,450,2600):new Vector3(id=="ridge"?-6200:-7000,0,id=="ridge"?1600:3400);
                player.terrainSurface.TrySample(at.x,at.z,out var ground);
                if(takeoff)at.y=ground.heightMeters+1.325f;else if(!approach&&id!="thermal")at.y=ground.heightMeters+220;
                var geo=FlightFrameConversions.UnityToGeodetic(at,GeodeticReference.Kbdu);
                var ic=new FlightDynamicsInitialConditions{latitudeDegrees=geo.latitudeDegrees,longitudeDegrees=geo.longitudeDegrees,altitudeMslMeters=geo.altitudeMslMeters,terrainElevationMslMeters=GeodeticReference.Kbdu.altitudeMslMeters+ground.heightMeters,headingDegrees=id=="lee"?0:90,calibratedAirspeedKnots=takeoff?0:70,pitchDegrees=approach?-3:0,flightPathAngleDegrees=approach?-3:0,engineRunning=true};
                backend.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(wind.Sample(at,0)));
                if(!backend.Reset(ic))throw new Exception(backend.LastError);
                float duration=takeoff?60:approach?70:25;
                double priorPitch=backend.CurrentState.pitchDegrees,priorBank=backend.CurrentState.bankDegrees;
                using(var csv=new StreamWriter(Path.Combine(output,"native_"+id+".csv")))
                {
                    csv.WriteLine("time,east,up,north,agl,airspeed,pitch,bank,vertical,windEast,windUp,windNorth,wow");
                    for(int step=0;step<duration*120;step++)
                    {
                        var state=backend.CurrentState;var component=wind.SampleComponents(state.positionUnityMeters,state.simulationTimeSeconds);var w=component.Total;
                        r.minTerrain=Mathf.Min(r.minTerrain,component.terrain);r.maxTerrain=Mathf.Max(r.maxTerrain,component.terrain);r.maxThermal=Mathf.Max(r.maxThermal,component.thermal);
                        float desiredHeading=(float)ic.headingDegrees+(takeoff||approach?Mathf.Clamp((state.positionUnityMeters.z+30)*.04f,-10,10):0);
                        float headingError=Mathf.DeltaAngle((float)state.headingDegrees,desiredHeading);
                        float bankTarget=state.weightOnWheels?0:Mathf.Clamp(headingError*.5f,-8,8);
                        float pitchTarget=takeoff?(state.calibratedAirspeedKnots<55?0:6):approach?(state.altitudeAglMeters<8?3:-3):0;
                        float pitchRate=(float)(state.pitchDegrees-priorPitch)*120;float bankRate=(float)(state.bankDegrees-priorBank)*120;
                        priorPitch=state.pitchDegrees;priorBank=state.bankDegrees;
                        var controls=AircraftControlState.Neutral();controls.mixture=1;
                        controls.throttle=takeoff?1:approach?(state.altitudeAglMeters<10?.05f:.22f):.6f;
                        controls.elevator=Mathf.Clamp((pitchTarget-(float)state.pitchDegrees)*.07f-pitchRate*.025f,-.5f,.5f);
                        controls.aileron=Mathf.Clamp((bankTarget-(float)state.bankDegrees)*.045f-bankRate*.02f,-.4f,.4f);
                        controls.rudder=Mathf.Clamp(headingError*.035f,-.35f,.35f);
                        controls.flaps=approach?.67f:0;
                        if(takeoff&&step<600)controls.leftToeBrake=controls.rightToeBrake=1;
                        backend.SetControls(controls);backend.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(w));
                        if(!backend.Advance(1.0/120))throw new Exception(backend.LastError);
                        state=backend.CurrentState;if(!state.IsFinite)throw new Exception("Nonfinite native state");
                        r.maxBank=Mathf.Max(r.maxBank,(float)Math.Abs(state.bankDegrees));r.minAgl=Mathf.Min(r.minAgl,(float)state.altitudeAglMeters);
                        if(step>600&&state.weightOnWheels)r.ground=true;
                        if(step>600&&!state.weightOnWheels&&state.altitudeAglMeters>10)r.airborne=true;
                        if(approach&&step>120&&state.weightOnWheels){r.touchdown=true;r.touchdownSink=(float)Math.Abs(state.verticalSpeedFeetPerMinute);r.touchdownSpeed=(float)state.calibratedAirspeedKnots;break;}
                        if(step%120==0)csv.WriteLine(FormattableString.Invariant($"{state.simulationTimeSeconds:F2},{state.positionUnityMeters.x:F2},{state.positionUnityMeters.y:F2},{state.positionUnityMeters.z:F2},{state.altitudeAglMeters:F2},{state.calibratedAirspeedKnots:F2},{state.pitchDegrees:F2},{state.bankDegrees:F2},{state.verticalSpeedFeetPerMinute:F2},{w.x:F3},{w.y:F3},{w.z:F3},{state.weightOnWheels}"));
                    }
                }
                r.finalAgl=(float)backend.CurrentState.altitudeAglMeters;r.finalPosition=backend.CurrentState.positionUnityMeters;
                r.passed=r.minAgl>.2f&&r.maxBank<20&&(takeoff?r.ground&&r.airborne&&r.finalAgl>30:approach?r.touchdown&&r.touchdownSink<450&&r.touchdownSpeed<75:r.finalAgl>75&&(id=="lee"?r.minTerrain<-.2f:id=="ridge"?r.maxTerrain>.5f:r.maxThermal>.5f));
            }
            catch(Exception e){r.error=e.Message;r.passed=false;}
            finally{UnityEngine.Object.DestroyImmediate(wind);}
            Debug.Log("[NativeAtmosphere] "+JsonUtility.ToJson(r));return r;
        }
    }
}
