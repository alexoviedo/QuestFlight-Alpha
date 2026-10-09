using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static class AmbientTrafficScenarioBatch
    {
        [Serializable] public sealed class Report { public Result[] scenarios; }
        [Serializable] public sealed class Result
        {
            public string id; public int suppressions; public bool passed;
            public string[] phaseCoverage; public string[] finalStates;
            public float closestPlayerHorizontal=100000, maxGroundAcceleration, maxAirAcceleration;
            public string maxGroundEvent, maxAirEvent;
            public int totalSteps;
            public float[] firstTaxiSeconds={-1,-1,-1};
            public bool[] completedCycle={false,false,false};
            public float concurrentTaxiFlightSeconds;
        }
        public static void RunBatch()
        {
            try
            {
                // Scenario diagnostics consume committed authored data without rebuilding it.
                var authored = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");
                var director = authored != null ? authored.GetComponentInChildren<AmbientTrafficDirector>(true) : null;
                if (director == null || director.actors == null || director.actors.Length != 5 ||
                    authored.GetComponentsInChildren<HabitatBirdDirector>(true).Any(bird => bird.traffic != director))
                    throw new InvalidOperationException("Authored ambient/wildlife traffic references are incomplete.");
                string output=System.Environment.GetEnvironmentVariable("QFL_AMBIENT_EVIDENCE") ??
                    Path.GetFullPath("../ambient_traffic_evidence");
                Directory.CreateDirectory(output);
                var results=new List<Result>();
                foreach(string id in new[]{"clear_calm_30min","clear_soaring_20min","runway_occupied_then_clear","taxi_blocked_then_clear","reverse_runway","airborne_conflict","schedule_offset_45","schedule_offset_90"})
                    results.Add(Run(id,output));
                File.WriteAllText(Path.Combine(output,"traffic_scenarios.json"),JsonUtility.ToJson(new Report {scenarios=results.ToArray()},true));
                EditorApplication.Exit(results.All(r=>r.passed)?0:1);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        static Result Run(string id,string output)
        {
            var world=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");
            var source=world.GetComponentInChildren<AmbientTrafficDirector>(true);
            var go=UnityEngine.Object.Instantiate(source.gameObject);
            var d=go.GetComponent<AmbientTrafficDirector>();
            var cameraObject=new GameObject("AmbientScenarioObserver");
            var camera=cameraObject.AddComponent<Camera>();cameraObject.transform.position=new Vector3(4000,700,3000);camera.enabled=false;d.pilotCamera=camera;
            var wind=UnityEngine.Object.Instantiate(d.windField);d.windField=wind;
            if(id=="reverse_runway") {wind.surfaceWindEastNorth=-wind.surfaceWindEastNorth;wind.ridgeWindEastNorth=-wind.ridgeWindEastNorth;}
            if(id.StartsWith("schedule_offset"))foreach(var a in d.actors)if(!a.sailplane)a.initialDelay+=id.EndsWith("45")?45:90;
            var visited=new HashSet<string>[5];for(int i=0;i<5;i++)visited[i]=new HashSet<string>();
            var result=new Result {id=id};
            float duration=id=="clear_calm_30min"?1800:1200;
            var weather=id.Contains("soaring")||id=="reverse_runway"?BoulderWeatherPreset.FoothillSoaring:BoulderWeatherPreset.Calm;
            Vector3 intercept=Vector3.zero,interceptVelocity=Vector3.zero;
            var previous=new Vector3[5];
            var generations=new int[5];var ages=new int[5];
            try
            {
                using(var trace=new StreamWriter(Path.Combine(output,id+".csv")))
                {
                    trace.WriteLine("time,id,phase,east,up,north,speed,suppressed,runwayOwner,groundOwner,lod,blocked");
                    for(int step=0;step<duration*10;step++)
                    {
                        float t=step*.1f;
                        var player=new FlightDynamicsState {simulationTimeSeconds=t,positionUnityMeters=new Vector3(4000,700,3000),rotationUnity=Quaternion.identity};
                        if(id=="runway_occupied_then_clear"&&t<300) {player.positionUnityMeters=new Vector3(-450,2,-30);player.weightOnWheels=true;}
                        if(id=="taxi_blocked_then_clear"&&t<120) {player.positionUnityMeters=new Vector3(-540,2,-138);player.weightOnWheels=true;}
                        if(id=="airborne_conflict")
                        {
                            // Inject while the authored first actor is airborne on downwind,
                            // not at a fixed old-route time which can now be a ground rollout.
                            if(step==600) {var a=d.actors[0];if(a.OnGround)throw new InvalidOperationException("Air conflict fixture is not airborne");intercept=a.Position+a.Velocity.normalized*900;interceptVelocity=-a.Velocity.normalized*40;}
                            if(step>=600&&step<1000){player.positionUnityMeters=intercept+interceptVelocity*(t-60);player.velocityUnityMetersPerSecond=interceptVelocity;}
                        }
                        d.StepTraffic(player,.1f,weather);
                        if(d.actors.Any(a=>!a.sailplane&&!a.Suppressed&&a.Phase==AmbientFlightPhase.TaxiOut&&a.Velocity.magnitude>.2f)&&d.actors.Any(a=>!a.Suppressed&&!a.OnGround))result.concurrentTaxiFlightSeconds+=.1f;
                        for(int i=0;i<5;i++)
                        {
                            var a=d.actors[i];
                            if(i<3){if(a.Phase==AmbientFlightPhase.TaxiOut&&a.Velocity.magnitude>.2f&&result.firstTaxiSeconds[i]<0)result.firstTaxiSeconds[i]=t;result.completedCycle[i]|=a.Phase==AmbientFlightPhase.Shutdown;}
                            visited[i].Add(a.sailplane?a.SoaringPhase.ToString():a.Phase.ToString());
                            if(!a.Suppressed)
                            {
                                float acceleration=(a.Velocity-previous[i]).magnitude/.1f;
                                if(generations[i]!=a.Generation){generations[i]=a.Generation;ages[i]=0;}else ages[i]++;
                                if(ages[i]>2)
                                {
                                    string detail=$"t={t:F1} id={i} phase={a.Phase} position={a.Position} oldVelocity={previous[i]} velocity={a.Velocity}";
                                    if(a.OnGround && acceleration>result.maxGroundAcceleration){result.maxGroundAcceleration=acceleration;result.maxGroundEvent=detail;}
                                    if(!a.OnGround && acceleration>result.maxAirAcceleration){result.maxAirAcceleration=acceleration;result.maxAirEvent=detail;}
                                }
                                var relative=a.Position-player.positionUnityMeters;
                                if(Mathf.Abs(relative.y)<10)result.closestPlayerHorizontal=Mathf.Min(result.closestPlayerHorizontal,new Vector2(relative.x,relative.z).magnitude);
                            }
                            previous[i]=a.Velocity;
                            if(step%10==0)trace.WriteLine(FormattableString.Invariant($"{t:F1},{i},{(a.sailplane?a.SoaringPhase.ToString():a.Phase.ToString())},{a.Position.x:F2},{a.Position.y:F2},{a.Position.z:F2},{a.Velocity.magnitude:F2},{a.Suppressed},{d.RunwayOwner},{d.GroundMovementOwner},{a.SelectedLod},{a.BlockingReason}"));
                        }
                    }
                }
                result.totalSteps=(int)(duration*10);result.suppressions=d.EmergencySuppressions;
                result.phaseCoverage=visited.Select(v=>string.Join(",",v.OrderBy(s=>s))).ToArray();
                result.finalStates=d.actors.Select(a=>$"{a.aircraftId}: {a.Phase}, suppressed={a.Suppressed}, position={a.Position}, speed={a.Velocity.magnitude:F1}").ToArray();
                result.passed=result.suppressions==0 && result.maxGroundAcceleration<8 && result.maxAirAcceleration<18 &&
                    visited.Take(3).Any(v=>v.Contains("Takeoff")) && visited.Take(3).Any(v=>v.Contains("Shutdown"));
                if(id.StartsWith("clear_"))result.passed &= result.firstTaxiSeconds[1]<=30 && result.completedCycle.All(x=>x) && result.concurrentTaxiFlightSeconds>10;
                Debug.Log("[AmbientScenarios] "+JsonUtility.ToJson(result));
                return result;
            }
            finally {UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(wind);}
        }
    }
}
