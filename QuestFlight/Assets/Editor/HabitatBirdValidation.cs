using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using QuestFlight.Product;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    /// <summary>Replays retained native trajectories through the production wildlife step. No alternative aircraft physics.</summary>
    public static class HabitatBirdValidation
    {
        public static void RunBatch()
        {
            try
            {
                string output=System.Environment.GetEnvironmentVariable("QFL_BIRD_EVIDENCE");Directory.CreateDirectory(output);
                foreach(string route in new[]{"default","runway26"})Replay(route,output);
                EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        static float F(string s)=>float.Parse(s,CultureInfo.InvariantCulture);
        public static void Replay(string route,string output)
        {
            var world=PrefabUtility.LoadPrefabContents(HabitatBirdAuthoring.World);
            var cameraObject=new GameObject("EvidenceCamera_NotTracked");var camera=cameraObject.AddComponent<Camera>();camera.fieldOfView=85;camera.aspect=1;
            try
            {
                var d=world.GetComponentInChildren<HabitatBirdDirector>(true);
                var modes=d.modes;typeof(ProductFlightModeCoordinator).GetProperty("State").SetValue(modes,ProductFlightModeCoordinator.FlightModeState.Demo);
                var wind=UnityEngine.Object.Instantiate(d.flight.windField);d.flight.windField=wind;
                if(route=="runway26"){wind.surfaceWindEastNorth=new Vector2(4,1.5f);wind.ridgeWindEastNorth=new Vector2(4,1.5f);}
                d.pilotCamera=camera;
                foreach(var p in d.traffic.actors)typeof(AmbientAircraftActor).GetProperty("Suppressed").SetValue(p,true);
                d.ResetSession();foreach(var b in d.birds){b.animator.enabled=true;b.animator.Rebind();b.animator.Update(0);}
                foreach(int index in new[]{1,4,6})
                {
                    var b=d.birds[index];var mesh=new Mesh();b.animator.Play("FlapLoop",0,0);b.animator.Update(0);b.lods[0].BakeMesh(mesh);var before=mesh.vertices;
                    b.animator.Update(.4f);b.lods[0].BakeMesh(mesh);var after=mesh.vertices;float motion=0;
                    for(int v=0;v<before.Length;v++)motion=Mathf.Max(motion,Vector3.Distance(b.lods[0].transform.TransformPoint(before[v]),b.lods[0].transform.TransformPoint(after[v])));
                    UnityEngine.Object.DestroyImmediate(mesh);Debug.Log("[BirdEvidence] "+b.birdId+" animated vertex displacement="+motion);
                    if(motion<.08f)throw new Exception("Frozen imported wings: "+b.birdId);
                }
                string traces=Path.Combine(Directory.GetParent(output).FullName,"demo_completion_20260921","full_flight_"+route+".csv");
                var rows=File.ReadAllLines(traces).Skip(1).Select(s=>s.Split(',')).ToArray();
                float closest=99999,viewSeconds=0,cueSeconds=0,minClearance=99999;int maxTris=0,maxDraws=0;
                var stateField=typeof(FlightDynamicsCoordinator).GetField("_currentState",BindingFlags.NonPublic|BindingFlags.Instance);
                using(var csv=new StreamWriter(Path.Combine(output,"birds_"+route+".csv")))
                {
                    csv.WriteLine("time,aircraftX,aircraftY,aircraftZ,eagleX,eagleY,eagleZ,distance,bearing,elevation,started,finished,cue,visible,triangles");
                    for(int n=0;n<Math.Min(250,rows.Length-1);n++)for(int sub=0;sub<30;sub++)
                    {
                        float t=sub/30f;var a=rows[n];var z=rows[n+1];
                        Vector3 P(string[] r)=>new Vector3(F(r[3]),F(r[6])*.3048f-1611.172f,F(r[4]));
                        Vector3 p=Vector3.Lerp(P(a),P(z),t),velocity=P(z)-P(a);float heading=Mathf.Atan2(velocity.x,velocity.z)*Mathf.Rad2Deg;
                        Quaternion rotation=Quaternion.Euler(-F(a[9]),heading,-F(a[10]));
                        var state=new FlightDynamicsState{simulationTimeSeconds=n+t,positionUnityMeters=p,velocityUnityMetersPerSecond=velocity,rotationUnity=rotation,
                            bankDegrees=F(a[10]),altitudeAglMeters=F(a[5]),weightOnWheels=bool.Parse(a[12])};
                        stateField.SetValue(d.flight,state);
                        d.flight.transform.SetPositionAndRotation(p,rotation);
                        camera.transform.SetPositionAndRotation(p+rotation*new Vector3(-.3f,1,0),rotation);
                        d.Step(1f/30);
                        foreach(var b in d.birds)
                        {
                            b.animator.Update(1f/30);
                            if(!BoulderWindFieldProfile.Finite(b.transform.position))throw new Exception("Nonfinite bird");
                            if(d.flight.terrainSurface.TrySample(b.transform.position.x,b.transform.position.z,out var h))minClearance=Mathf.Min(minClearance,b.transform.position.y-h.heightMeters);
                        }
                        Vector3 delta=d.Eagle.transform.position-camera.transform.position,local=Quaternion.Inverse(rotation)*delta;
                        float distance=delta.magnitude,bearing=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,elevation=Mathf.Asin(local.y/distance)*Mathf.Rad2Deg;
                        bool cue=d.TryGetEagleCue(out _);
                        closest=Mathf.Min(closest,distance);
                        if(distance<320&&distance>65&&Mathf.Abs(bearing)<35&&elevation>-7&&elevation<30)viewSeconds+=1f/30;
                        if(cue)cueSeconds+=1f/30;
                        maxTris=Mathf.Max(maxTris,d.VisibleTriangles);maxDraws=Mathf.Max(maxDraws,d.VisibleBirds);
                        if(sub==0)csv.WriteLine($"{n},{p.x},{p.y},{p.z},{d.Eagle.transform.position.x},{d.Eagle.transform.position.y},{d.Eagle.transform.position.z},{distance},{bearing},{elevation},{d.EncounterStarted},{d.EncounterFinished},{cue},{d.VisibleBirds},{d.VisibleTriangles}");
                    }
                }
                string summary=$"{route}: closest={closest:F1}m, usefulView={viewSeconds:F1}s, cueEligible={cueSeconds:F1}s, minTerrainClearance={minClearance:F1}m, peakBirdTriangles={maxTris}, peakRenderers={maxDraws}";
                File.WriteAllText(Path.Combine(output,route+"_summary.txt"),summary);Debug.Log("[BirdEvidence] "+summary);
                if(!d.EncounterStarted||viewSeconds<6||cueSeconds<1||closest<45||minClearance<40||maxTris>30000||maxDraws>16)throw new Exception("Bird encounter acceptance failed: "+summary);
                d.ResetSession();if(d.EncounterStarted)throw new Exception("Encounter did not rearm");
                UnityEngine.Object.DestroyImmediate(wind);
            }
            finally{UnityEngine.Object.DestroyImmediate(cameraObject);PrefabUtility.UnloadPrefabContents(world);}
        }
    }
}
