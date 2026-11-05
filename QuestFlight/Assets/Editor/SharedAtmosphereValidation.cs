using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
namespace QuestFlightLab.Editor
{
    public static class SharedAtmosphereValidation
    {
        static void Set(object o,string name,object value)
        {
            var p=o.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
            if(p!=null)p.SetValue(o,value);else o.GetType().GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,value);
        }
        public static void RunBatch()
        {
            try
            {
                BoulderAmbientAircraftAuthoring.Build();
                string output=System.Environment.GetEnvironmentVariable("QFL_ATMOSPHERE_EVIDENCE");
                if(string.IsNullOrEmpty(output))throw new InvalidOperationException("Evidence output required.");
                Directory.CreateDirectory(output);
                var wind=AssetDatabase.LoadAssetAtPath<BoulderWindFieldProfile>(BoulderAmbientAircraftAuthoring.WindPath);
                File.WriteAllText(Path.Combine(output,"wind.json"),JsonUtility.ToJson(wind));
                WriteSections(wind,output);
                var world=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");
                var source=world.GetComponentInChildren<AmbientTrafficDirector>(true);
                bool ridge=RunRidge(source,output,false), loss=RunRidge(source,output,true);
                File.WriteAllText(Path.Combine(output,"ridge_result.json"),"{\"ridge\":"+ridge.ToString().ToLower()+",\"loss\":"+loss.ToString().ToLower()+"}");
                EditorApplication.Exit(ridge&&loss?0:2);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        static void WriteSections(BoulderWindFieldProfile source,string output)
        {
            var field=UnityEngine.Object.Instantiate(source);field.gustAmplitude=0;field.thermalStrength=0;
            try
            {
                using(var csv=new StreamWriter(Path.Combine(output,"terrain_sections.csv")))
                {
                    csv.WriteLine("direction,east,up,north,ground,terrain");
                    foreach(int direction in new[]{0,90,180,270})
                    {
                        float angle=direction*Mathf.Deg2Rad;
                        field.surfaceWindEastNorth=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*source.surfaceWindEastNorth.magnitude;
                        field.ridgeWindEastNorth=field.surfaceWindEastNorth.normalized*source.ridgeWindEastNorth.magnitude;
                        for(float x=-7800;x<=-4000;x+=50)
                        {
                            field.SampleGrid(x,1600,out float ground,out _);
                            for(float agl=25;agl<=600;agl+=25)
                            {
                                var at=new Vector3(x,ground+agl,1600);var c=field.SampleComponents(at,0);
                                csv.WriteLine(FormattableString.Invariant($"{direction},{x},{at.y:F2},1600,{ground:F2},{c.terrain:F4}"));
                            }
                        }
                    }
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(field);}
        }
        static bool RunRidge(AmbientTrafficDirector source,string output,bool loss)
        {
            var go=UnityEngine.Object.Instantiate(source.gameObject);
            var d=go.GetComponent<AmbientTrafficDirector>();
            var wind=UnityEngine.Object.Instantiate(source.windField);d.windField=wind;
            var originalPlayer=d.player;var originalField=originalPlayer.windField;
            originalPlayer.windField=wind; // Actor sampling now resolves through the shared authority.
            wind.thermalStrength=0;wind.gustAmplitude=0;
            var player=new FlightDynamicsState{positionUnityMeters=new Vector3(4000,700,3000),rotationUnity=Quaternion.identity};
            d.StepTraffic(player,.1f,BoulderWeatherPreset.FoothillSoaring);
            for(int i=0;i<4;i++)Set(d.actors[i],"Suppressed",true);
            var a=d.actors[4];Set(a,"Position",a.soaringCenter);Set(a,"AirHeading",Vector3.forward);Set(a,"SoaringPhase",AmbientSoaringPhase.Ridge);
            float start=a.Position.y,minClearance=10000,maxThermal=0;int ridgeSteps=0,consecutiveRidge=0,longestRidge=0;
            try
            {
                using(var csv=new StreamWriter(Path.Combine(output,loss?"ridge_loss.csv":"ridge_only.csv")))
                {
                    csv.WriteLine("time,east,up,north,terrain,thermal,sink,clearance,phase,turns,suppressed");
                    for(int step=1;step<=6000;step++)
                    {
                        float t=step*.1f;player.simulationTimeSeconds=t;
                        if(loss&&step==3000){wind.surfaceWindEastNorth=Vector2.zero;wind.ridgeWindEastNorth=Vector2.zero;}
                        d.StepTraffic(player,.1f,BoulderWeatherPreset.FoothillSoaring);
                        var c=wind.SampleComponents(a.Position,t);d.terrain.TrySample(a.Position.x,a.Position.z,out var ground);
                        float clearance=a.Position.y-ground.heightMeters;minClearance=Mathf.Min(minClearance,clearance);maxThermal=Mathf.Max(maxThermal,Mathf.Abs(c.thermal));
                        if(a.SoaringPhase==AmbientSoaringPhase.Ridge){ridgeSteps++;consecutiveRidge++;longestRidge=Mathf.Max(longestRidge,consecutiveRidge);}else consecutiveRidge=0;
                        if(step%10==0)csv.WriteLine(FormattableString.Invariant($"{t:F1},{a.Position.x:F2},{a.Position.y:F2},{a.Position.z:F2},{c.terrain:F3},{c.thermal:F3},{a.LastSink:F3},{clearance:F2},{a.SoaringPhase},{a.RidgeTurns},{a.Suppressed}"));
                        if(a.Suppressed)break;
                    }
                }
                bool pass=!a.Suppressed&&d.EmergencySuppressions==0&&minClearance>=75&&maxThermal==0&&
                    (loss?a.Position.x>a.soaringCenter.x+2000&&a.Position.y<start-20:longestRidge>=3000&&a.RidgeTurns>=3&&a.Position.y>=start-20);
                Debug.Log($"[SharedAtmosphere] loss={loss} pass={pass} ridgeSeconds={ridgeSteps*.1f:F1} turns={a.RidgeTurns} dh={a.Position.y-start:F1} clearance={minClearance:F1} suppressed={a.Suppressed}");
                return pass;
            }
            finally{originalPlayer.windField=originalField;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(wind);}
        }
    }
}
