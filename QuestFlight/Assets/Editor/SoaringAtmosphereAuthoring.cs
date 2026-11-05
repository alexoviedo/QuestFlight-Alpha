using System;
using System.IO;
using System.Security.Cryptography;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static class SoaringAtmosphereAuthoring
    {
        public const string Folder="Assets/Production/SoaringV1/Weather";
        public static void Run()
        {
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            var field=AssetDatabase.LoadAssetAtPath<BoulderWindFieldProfile>("Assets/Production/Environment/AmbientAircraftV1/BoulderWind.asset");
            string terrainPath="Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset";
            var terrain=AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(terrainPath);
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(terrainPath))).Replace("-","").ToLowerInvariant();
            var choices=new SoaringWeatherDefinition[4];
            for(int i=0;i<4;i++)
            {
                string name=new[]{"CalmValidation","ModerateThermal","ModerateRidge","StrongerSupported"}[i];string path=Folder+"/"+name+".asset";
                var d=AssetDatabase.LoadAssetAtPath<SoaringWeatherDefinition>(path);
                if(d==null){d=ScriptableObject.CreateInstance<SoaringWeatherDefinition>();AssetDatabase.CreateAsset(d,path);}
                d.displayName=new[]{"CALM · VALIDATION ONLY","MODERATE THERMAL · REFERENCE","MODERATE RIDGE · REFERENCE","STRONGER · REFERENCE LIMIT"}[i];
                d.surfaceWind=i==0?Vector2.zero:new Vector2(i==3?-3.5f:-2.5f,0);d.upperWind=i==0?Vector2.zero:new Vector2(i==3?-7:-5.5f,0);
                d.gustAmplitude=i==0?0:i==3?.35f:.2f;d.ridgeEnabled=i>=2;d.thermalEnabled=i==1||i==3;d.isaOffsetKelvin=i==0?0:i==3?20:10;
                d.terrainSha256=hash;d.seed=233;d.differentialAirflow=true;d.wakeEnabled=true;
                d.sources=new SoaringWeatherDefinition.ThermalSource[2];
                for(int source=0;source<2;source++)
                {
                    Vector2 at=source==0?new Vector2(field.legacyThermal.centerUnityMeters.x,field.legacyThermal.centerUnityMeters.z):field.foothillThermals[0].centerEnu;
                    if(!terrain.TrySample(at.x,at.y,out var ground))throw new InvalidOperationException("Thermal source outside authored terrain.");
                    d.sources[source]=new SoaringWeatherDefinition.ThermalSource{position=at,groundHeight=ground.heightMeters,mixingHeight=1400,convectiveScale=i==3?3.3f:2.56f,radiusScale=3.0f,lifetime=1200,period=1500,initialAge=240+source*240};
                }
                if(!d.IsValid(out string error))throw new InvalidOperationException(error);EditorUtility.SetDirty(d);choices[i]=d;
            }
            foreach(string path in new[]{"Assets/Production/Prefabs/ProductionC172AircraftRig.prefab","Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab"})
            {
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var flight=root.GetComponentInChildren<FlightDynamicsCoordinator>(true);flight.soaringWeatherChoices=choices;flight.soaringWeatherDefinition=choices[1];
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();Debug.Log("[SoaringWeather] Four authored bounded reference profiles; terrain SHA256="+hash);
        }
    }
}
