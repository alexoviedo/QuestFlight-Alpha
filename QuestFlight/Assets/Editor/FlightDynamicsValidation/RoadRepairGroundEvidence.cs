using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.FlightDynamicsValidation
{
    public static class RoadRepairGroundEvidence
    {
        [Serializable] sealed class Record { public Vector2 roadPoint; public float terrainHeight; public double agl,compressionFeet,gearForcePounds; }
        [Serializable] sealed class Evidence { public int checkedTriangles; public float maximumQueryError; public List<Record> nativeGroundCases=new List<Record>(); public string scope="Visible repaired triangle query consistency and native JSBSim static ground settling at sampled source elevations. Not a flight handling or sloped-road landing acceptance."; }
        public static void RunBatch()
        {
            string output=System.Environment.GetEnvironmentVariable("QFL_ROAD_REPAIR_DIR");
            var terrain=AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>("Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset");
            var patch=terrain.transitionPatches.Single(p=>p.stableId=="airport_road_repair_visible_triangles");
            var evidence=new Evidence();
            for(int i=0;i<patch.triangles.Length;i+=150)
            {
                Vector3 p=(patch.vertices[patch.triangles[i]]+patch.vertices[patch.triangles[i+1]]+patch.vertices[patch.triangles[i+2]])/3;
                if(!terrain.TrySample(p.x,p.z,out var sample))throw new InvalidOperationException("Missing repaired ground query");
                evidence.maximumQueryError=Mathf.Max(evidence.maximumQueryError,Mathf.Abs(p.y-sample.heightMeters));evidence.checkedTriangles++;
            }
            if(evidence.maximumQueryError>.01f)throw new InvalidOperationException("Repaired ground query differs from visible triangles: "+evidence.maximumQueryError);
            foreach(var point in new[]{new Vector2(-1590.18f,-487.89f),new Vector2(-640,-540),Vector2.zero})
            {
                if(!terrain.TrySample(point.x,point.y,out var sample))throw new InvalidOperationException("Missing ground case");
                using var backend=new NativeJsbsimTraceBackend();
                if(!backend.IsAvailable)throw new InvalidOperationException(backend.LastError);
                var initial=FlightScenarioInitialState.Ground();initial.engineRunning=false;
                initial.terrainElevationMslMeters=GeodeticReference.Kbdu.altitudeMslMeters+sample.heightMeters;
                if(!backend.Reset(initial))throw new InvalidOperationException(backend.LastError);
                backend.SetControls(new AircraftControlState{leftToeBrake=1,rightToeBrake=1});
                for(int step=0;step<480;step++)if(!backend.Step(1.0/120))throw new InvalidOperationException(backend.LastError);
                if(!backend.State.IsFinite||backend.GearCompressionFeet<=0||backend.State.altitudeAglMeters<0||backend.State.altitudeAglMeters>4)
                    throw new InvalidOperationException("Native ground settling failed");
                evidence.nativeGroundCases.Add(new Record{roadPoint=point,terrainHeight=sample.heightMeters,agl=backend.State.altitudeAglMeters,compressionFeet=backend.GearCompressionFeet,gearForcePounds=backend.GearVerticalForcePounds});
            }
            File.WriteAllText(Path.Combine(output,"native_ground_evidence.json"),JsonUtility.ToJson(evidence,true));
            Debug.Log("[RoadRepairGroundEvidence] passed");
            QuestFlightLab.Editor.ProductionVisualQaBatchRunner.RunProductionVisualQa();
        }
    }
}
