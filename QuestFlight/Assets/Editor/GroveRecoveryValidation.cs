using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using QuestFlightLab.Environment;

namespace QuestFlightLab.Editor
{
    public static class GroveRecoveryValidation
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private struct GpuRecord {public Vector4 positionHeight,tintRadius,rotationFade;}
        public static void Run()
        {
            string root=System.Environment.GetEnvironmentVariable("QFL_TREE_TURN_ROOT");
            Directory.CreateDirectory(root+"/cpu_final");
            System.Environment.SetEnvironmentVariable("QFL_TREE_TURN_ROOT",root+"/cpu_final");
            try{GroveClusterAuthoring.ReviewCpuOnly();}
            finally{System.Environment.SetEnvironmentVariable("QFL_TREE_TURN_ROOT",root);}
            ValidateSwitches();
            var tests=new Tests.MapBranchGroveTests();
            tests.PageSeamAndOuterMapViewsLoadAndDisableRestoresTreeOwnership();
            GroveClusterAuthoring.Review();
            System.Environment.SetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR",root+"/standard_visual_qa");
            ProductionVisualQaBatchRunner.RunProductionVisualQa();
        }
        public static void ValidateSwitches()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity");
            var grove=UnityEngine.Object.FindFirstObjectByType<MapBranchGroveRenderer>();
            var host=new GameObject("Offline cluster restoration check");var camera=host.AddComponent<Camera>();
            camera.enabled=false;camera.fieldOfView=90;camera.aspect=1;camera.farClipPlane=25000;
            var select=typeof(MapBranchGroveRenderer).GetMethod("Select",Private);
            try
            {
                var spawn=grove.observationStarts[6].spawnTransform;
                host.transform.SetPositionAndRotation(spawn.position+Vector3.up*1.5f,spawn.rotation*Quaternion.Euler(12,0,0));
                grove.PrepareForVisualQa(camera);
                if(grove.ClusterInstances==0)throw new Exception("No clusters in dense mountain check");
                grove.ValidateClustersForVisualQa();
                var source=grove.SelectedSourceRecordsForVisualQa();
                byte[] ownership=((byte[])typeof(MapBranchGroveRenderer).GetField("ownership",Private).GetValue(grove)).ToArray();
                grove.distantClustersEnabled=false;select.Invoke(grove,new object[]{camera,true});
                var data=new GpuRecord[grove.SelectedCells];
                ((ComputeBuffer)typeof(MapBranchGroveRenderer).GetField("buffer",Private).GetValue(grove)).GetData(data,0,0,data.Length);
                if(!source.SequenceEqual(data.SelectMany(c=>new[]{c.positionHeight,c.tintRadius,c.rotationFade})))throw new Exception("Restoring source geometry corrupted GPU records");
                if(!ownership.SequenceEqual((byte[])typeof(MapBranchGroveRenderer).GetField("ownership",Private).GetValue(grove)))
                    throw new Exception("Representation switch changed tree ownership");
                grove.distantClustersEnabled=true;select.Invoke(grove,new object[]{camera,true});grove.ValidateClustersForVisualQa();
                if(grove.ClusterInstances==0)throw new Exception("Cluster restoration failed");
                // Switching in a sky-only view uploads no records. Returning to
                // the forest must still invalidate the old compacted GPU buffer.
                var rotation=host.transform.rotation;
                host.transform.rotation=Quaternion.Euler(-90,0,0);select.Invoke(grove,new object[]{camera,true});
                if(grove.SelectedCells!=0)throw new Exception("Expected empty sky selection");
                grove.distantClustersEnabled=false;select.Invoke(grove,new object[]{camera,true});
                host.transform.rotation=rotation;select.Invoke(grove,new object[]{camera,true});
                data=new GpuRecord[grove.SelectedCells];
                ((ComputeBuffer)typeof(MapBranchGroveRenderer).GetField("buffer",Private).GetValue(grove)).GetData(data,0,0,data.Length);
                if(!grove.SelectedSourceRecordsForVisualQa().SequenceEqual(data.SelectMany(c=>new[]{c.positionHeight,c.tintRadius,c.rotationFade})))
                    throw new Exception("Empty-view switch left a stale GPU buffer");
                Debug.Log("[GroveRecovery] PASS: live representation switches preserve source records and ownership.");
            }
            finally{grove.ClearForVisualQa();UnityEngine.Object.DestroyImmediate(host);}
        }
    }
}
