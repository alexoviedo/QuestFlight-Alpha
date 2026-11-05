using System;
using System.IO;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static class SoaringOperationAuthoring
    {
        public const string Folder="Assets/Production/SoaringV1/Operations";
        public static void Run()
        {
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            var baseWeather=AssetDatabase.LoadAssetAtPath<SoaringWeatherDefinition>(SoaringAtmosphereAuthoring.Folder+"/ModerateThermal.asset");
            string westPath=SoaringAtmosphereAuthoring.Folder+"/WestWindTowValidation.asset";
            var west=AssetDatabase.LoadAssetAtPath<SoaringWeatherDefinition>(westPath);
            if(west==null){west=UnityEngine.Object.Instantiate(baseWeather);AssetDatabase.CreateAsset(west,westPath);}
            west.displayName="WEST WIND · TOW VALIDATION";west.surfaceWind=new Vector2(2.5f,0);west.upperWind=new Vector2(5.5f,0);
            west.thermalEnabled=west.ridgeEnabled=false;EditorUtility.SetDirty(west);
            var terrain=AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>("Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset");
            var operations=new BoulderSoaringOperationDefinition[2];
            for(int i=0;i<2;i++)
            {
                int direction=i==0?8:26,sign=i==0?1:-1;string path=Folder+"/DevelopmentRunway"+direction+".asset";
                var op=AssetDatabase.LoadAssetAtPath<BoulderSoaringOperationDefinition>(path);
                if(op==null){op=ScriptableObject.CreateInstance<BoulderSoaringOperationDefinition>();AssetDatabase.CreateAsset(op,path);}
                op.operationId="boulder.reference.runway"+direction;op.displayName="BOULDER "+direction+" · DEVELOPMENT TOW";
                op.runwayDirection=direction;op.trueHeading=i==0?89.972f:269.972f;op.launchEast=-555*sign;
                op.centerlineNorth=-30;op.weather=i==0?baseWeather:west;op.terrainSha256=baseWeather.terrainSha256;
                op.provenance="PROVISIONAL_LOCAL_PROCEDURE. GeodeticReference.Kbdu and existing authored paved runway -30m north, true089.972/269.972. City Airport/Traffic Pattern pages inspected2026-10-13: shared08/08G occupancy, inside glider pattern, downwind south of Jay Road, light-wind08 and west-only26. Paved-strip paired launch/landing is development geometry, not verified MHG staging or SOP. Release914.4m above airport and corridor are engineering references; no airspace clearance or TFR assurance.";
                // Western departure turns before the existing -1650m circuit
                // boundary; 08 continues straight before entering its local loop.
                op.towRoute=i==0?new[]{new Vector2(2200,-30),new Vector2(2800,2200),new Vector2(0,3000),new Vector2(-1000,1700),new Vector2(1800,1300)}:
                    new[]{new Vector2(-1300,100),new Vector2(-1350,1900),new Vector2(1800,3000),new Vector2(2800,1700),new Vector2(0,1300)};
                op.terrainVerified=true;
                for(float x=-680;x<=640;x+=20)
                    if(!terrain.TrySample(x,op.centerlineNorth,out var floor)||floor.IsWater)op.terrainVerified=false;
                foreach(var at in op.towRoute)if(!terrain.TrySample(at.x,at.y,out _))op.terrainVerified=false;
                if(!op.IsValid)throw new InvalidOperationException("Unsupported operation authoring "+direction);
                EditorUtility.SetDirty(op);operations[i]=op;
            }
            foreach(string path in new[]{"Assets/Production/Prefabs/ProductionC172AircraftRig.prefab","Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab"})
            {
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var flight=root.GetComponentInChildren<FlightDynamicsCoordinator>(true);flight.soaringOperations=operations;
                    flight.soaringTraffic=root.GetComponentInChildren<AmbientTrafficDirector>(true);
                    PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            AssetDatabase.SaveAssets();Debug.Log("[SoaringOperation] Both reference definitions authored; native complete-flight validation still required.");
        }
    }
}
