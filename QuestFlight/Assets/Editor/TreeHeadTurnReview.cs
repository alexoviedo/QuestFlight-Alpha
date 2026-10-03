using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using QuestFlightLab.Environment;

namespace QuestFlightLab.Editor
{
    // Exercises the real selection code and authored pages at a fixed pilot
    // position. These temporary offline cameras never write the tracked camera.
    public static class TreeHeadTurnReview
    {
        private struct Cell { public Vector4 positionHeight,tintRadius,rotationFade; }
        [Serializable] private sealed class View
        { public int site,yaw,selected,triangles,common,missing,coverageChanged; public float farthest;
          public double selectionMs,gatherMs,fillMs,bufferMs,ownershipMs; public long uploadBytes; public string cellDigest;
          public int clusterInstances,replacedCells,sourceTriangles,submittedBranchCells; }
        [Serializable] private sealed class Report { public List<View> views=new List<View>(); }
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static void RunBaseline()=>Run(false);
        public static void RunFixed()=>Run(true);
        public static void RunAcceptance()
        {
            Run(true);
            var tests=new Tests.MapBranchGroveTests();
            tests.DistanceHandoffIsContinuousAndLeavesFullMiddleCoverage();
            tests.DenseViewsReserveDistantCoverageWithinTheOriginalTriangleCap();
            tests.OrdinaryPilotViewContainsDistantCanopyAndUsesSubsetMeshes();
            tests.PureBankAndReversalRefreshSelectionBeforeTimerExpires();
            var culling=new global::TreeDrawCullingTests();
            culling.OffscreenBoundsAreRejectedButBoundaryIsRetained();
            culling.OffsetRotatedNonuniformMeshesCannotLoseVisibleCorners();
            culling.EitherEyeVisibilityMustRetainInstance();
            ProductionVisualQaBatchRunner.RunTreeHeadTurnVisualReview();
            Debug.Log("[TreeHeadTurn] PASS: overlap continuity, triangle caps, distance handoff, bank/reversal, both-eye culling, pilot stereo captures");
        }
        private static void Run(bool requireStable)
        {
            string root=System.Environment.GetEnvironmentVariable("QFL_TREE_TURN_ROOT");
            Directory.CreateDirectory(root);
            EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity");
            var grove=UnityEngine.Object.FindFirstObjectByType<MapBranchGroveRenderer>();
            GroveClusterAuthoring.BindForReview(grove);
            var host=new GameObject("Offline head turn regression camera");
            var camera=host.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=90;camera.aspect=1;camera.farClipPlane=25000;
            var select=typeof(MapBranchGroveRenderer).GetMethod("Select",Private);
            var bufferField=typeof(MapBranchGroveRenderer).GetField("buffer",Private);
            var report=new Report();int failures=0;
            try
            {
                for(int site=0;site<grove.observationStarts.Length;site++)
                {
                    var spawn=grove.observationStarts[site].spawnTransform;
                    host.transform.SetPositionAndRotation(spawn.position+Vector3.up*1.5f,spawn.rotation*Quaternion.Euler(12,0,0));
                    grove.PrepareForVisualQa(camera);
                    Dictionary<Vector2,Cell> previous=null;Plane[] previousPlanes=null;
                    Dictionary<Vector3,float> previousClusters=null;
                    foreach(int yaw in new[]{0,-10,-25,-10,0,10,25,10,0})
                    {
                        host.transform.rotation=spawn.rotation*Quaternion.Euler(12,yaw,0);
                        double gather=grove.GatherTotalMs,fill=grove.FillTotalMs,buffer=grove.BufferUploadTotalMs,ownership=grove.OwnershipUploadTotalMs;
                        long uploaded=grove.UploadedBytes;
                        select.Invoke(grove,new object[]{camera,true});
                        var clusterFades=grove.ValidateClustersForVisualQa();
                        if(previousClusters!=null)foreach(var item in clusterFades)
                            if(previousClusters.TryGetValue(item.Key,out float prior)&&Mathf.Abs(prior-item.Value)>.001f)
                                throw new Exception("Head turn changed retained cluster representation coverage");
                        previousClusters=clusterFades;
                        var cells=new Cell[grove.SelectedCells];
                        if(grove.distantClustersEnabled)
                        {
                            var source=grove.SelectedSourceRecordsForVisualQa();
                            for(int i=0;i<cells.Length;i++)cells[i]=new Cell{positionHeight=source[i*3],tintRadius=source[i*3+1],rotationFade=source[i*3+2]};
                        }
                        else if(cells.Length>0)((ComputeBuffer)bufferField.GetValue(grove)).GetData(cells,0,0,cells.Length);
                        var current=new Dictionary<Vector2,Cell>(cells.Length);
                        foreach(var c in cells)current.Add(new Vector2(c.positionHeight.x,c.positionHeight.z),c);
                        var planes=GeometryUtility.CalculateFrustumPlanes(camera);
                        var view=new View{site=site,yaw=yaw,selected=cells.Length,triangles=grove.SelectedTriangles,farthest=grove.FarthestSelectedMeters};
                        view.selectionMs=grove.SelectionMilliseconds;view.gatherMs=grove.GatherTotalMs-gather;view.fillMs=grove.FillTotalMs-fill;
                        view.bufferMs=grove.BufferUploadTotalMs-buffer;view.ownershipMs=grove.OwnershipUploadTotalMs-ownership;view.uploadBytes=grove.UploadedBytes-uploaded;
                        view.clusterInstances=grove.ClusterInstances;view.replacedCells=grove.ReplacedBranchCells;view.sourceTriangles=grove.SourceSelectedTriangles;view.submittedBranchCells=grove.SubmittedBranchCells;
                        // Outside the timed selection: exact GPU records, including order,
                        // ownership fade, seeded artwork and family, must be preserved.
                        using(var bytes=new MemoryStream())using(var writer=new BinaryWriter(bytes))
                        {
                            foreach(var c in cells)foreach(var v in new[]{c.positionHeight,c.tintRadius,c.rotationFade})
                            {writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);writer.Write(v.w);}
                            writer.Flush();using(var sha=System.Security.Cryptography.SHA256.Create())
                                view.cellDigest=BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-","").ToLowerInvariant();
                        }
                        if(previous!=null)foreach(var kv in previous)
                        {
                            var c=kv.Value;var center=new Vector3(c.positionHeight.x,c.positionHeight.y+c.positionHeight.w*.75f,c.positionHeight.z);
                            var bounds=new Bounds(center,Vector3.one*.1f);
                            if(!GeometryUtility.TestPlanesAABB(previousPlanes,bounds)||!GeometryUtility.TestPlanesAABB(planes,bounds)||c.rotationFade.z<.05f)continue;
                            view.common++;
                            if(!current.TryGetValue(kv.Key,out var now))view.missing++;
                            else if(Mathf.Abs(c.rotationFade.z-now.rotationFade.z)>.01f)view.coverageChanged++;
                        }
                        if(view.missing+view.coverageChanged>0)failures++;
                        if(view.triangles>MapBranchGroveRenderer.MaximumTriangleCount)throw new Exception("Grove triangle cap exceeded");
                        report.views.Add(view);previous=current;previousPlanes=planes;
                    }
                    if(requireStable)
                    {
                        // Aircraft motion can promote detail tiers while the head
                        // turns. Preserve the hard cap and finite GPU coverage.
                        for(int step=1;step<=24;step++)
                        {
                            host.transform.SetPositionAndRotation(spawn.position+Vector3.up*1.5f+spawn.forward*(step*8),spawn.rotation*Quaternion.Euler(12,Mathf.Sin(step)*35,Mathf.Sin(step*.5f)*25));
                            select.Invoke(grove,new object[]{camera,true});
                            grove.ValidateClustersForVisualQa();
                            if(grove.SelectedTriangles>MapBranchGroveRenderer.MaximumTriangleCount||grove.SelectedCells>MapBranchGroveRenderer.MaximumCells)
                                throw new Exception("Moving/banking selection exceeded hard cap");
                        }
                        host.transform.rotation=Quaternion.Euler(-90,0,0);
                        select.Invoke(grove,new object[]{camera,true});
                        if(grove.SelectedCells!=0)throw new Exception("Sky view retained offscreen grove draws");
                    }
                    grove.ClearForVisualQa();
                }
                File.WriteAllText(Path.Combine(root,requireStable?"fixed.json":"baseline.json"),JsonUtility.ToJson(report,true));
                Debug.Log("[TreeHeadTurn] views="+report.views.Count+" changing-overlap-views="+failures);
                if(requireStable&&failures>0)throw new Exception("Head rotation changed already visible grove coverage");
            }
            finally{grove.ClearForVisualQa();UnityEngine.Object.DestroyImmediate(host);}
        }
    }

    public static partial class ProductionVisualQaBatchRunner
    {
        public static void RunTreeHeadTurnVisualReview()
        {
            string root=Path.Combine(System.Environment.GetEnvironmentVariable("QFL_TREE_TURN_ROOT"),"pilot_stereo");
            Directory.CreateDirectory(Path.Combine(root,"screenshots"));
            var report=new ProductionVisualQaReport{generatedUtc=DateTime.UtcNow.ToString("O"),unityVersion=Application.unityVersion,scenePath=ProductionScenePath,outputDirectory=root,renderWidth=1440,renderHeight=1080,humanHeadsetAcceptance="PENDING",flightPhaseImageSemantics="Sequential pilot-position head turns. Shared center selection for both offline eyes; not Quest timing."};
            var context=LoadContext(1440,1080,report);
            // Untracked editor hand-preview meshes can surround the offline eye;
            // they are not pilot scenery and are hidden only in this QA scene.
            foreach(var renderer in context.rig.GetComponentsInChildren<Renderer>(true))
                if(renderer.sharedMaterials.Any(m=>m!=null&&m.shader!=null&&m.shader.name.StartsWith("Interaction/")))renderer.gameObject.SetActive(false);
            var grove=context.environment.GetComponentInChildren<MapBranchGroveRenderer>(true);
            GroveClusterAuthoring.BindForReview(grove);
            var select=typeof(MapBranchGroveRenderer).GetMethod("Select",BindingFlags.Instance|BindingFlags.NonPublic);
            Camera.CameraCallback draw=c=>{if(c==context.camera)grove.DrawForVisualQa(c);};
            string old=System.Environment.GetEnvironmentVariable("QFL_SETTLED_SCENERY_PREVIEW"),oldBranch=System.Environment.GetEnvironmentVariable("QFL_MAP_BRANCH_REVIEW_ACTIVE");
            System.Environment.SetEnvironmentVariable("QFL_SETTLED_SCENERY_PREVIEW","1");System.Environment.SetEnvironmentVariable("QFL_MAP_BRANCH_REVIEW_ACTIVE","1");
            Camera.onPreCull+=draw;
            try
            {
                foreach(int site in new[]{0,6,7})
                {
                    var spawn=grove.observationStarts[site].spawnTransform;
                    SetAircraftPose(context,spawn.position,spawn.rotation);
                    Vector3 seat=context.rig.PilotSeatAnchor.position;
                    context.camera.transform.SetPositionAndRotation(seat,spawn.rotation*Quaternion.Euler(12,0,0));context.camera.fieldOfView=90;
                    grove.PrepareForVisualQa(context.camera);
                    foreach(int yaw in new[]{0,-25,25})
                    {
                        Quaternion orientation=spawn.rotation*Quaternion.Euler(12,yaw,0);
                        context.camera.transform.SetPositionAndRotation(seat,orientation);
                        select.Invoke(grove,new object[]{context.camera,true});
                        foreach(int eye in new[]{-1,1})
                        {
                            Vector3 position=seat+orientation*Vector3.right*(eye*.032f);
                            string id=$"headturn_{site}_{yaw}_"+(eye<0?"left":"right");
                            Capture(context,report,root,1440,1080,new ShotDefinition(id,"Sequential head turn / stable mountain canopy","tree_head_turn",position,orientation,90,ViewProfile.Cockpit));
                        }
                    }
                    grove.ClearForVisualQa();
                }
                WriteReportFiles(report,root);
                foreach(var shot in report.shots)if(!string.IsNullOrEmpty(shot.error)||!File.Exists(shot.screenshotPath))throw new Exception("Tree head-turn capture failed: "+shot.id);
            }
            finally
            {
                Camera.onPreCull-=draw;grove.ClearForVisualQa();
                System.Environment.SetEnvironmentVariable("QFL_SETTLED_SCENERY_PREVIEW",old);System.Environment.SetEnvironmentVariable("QFL_MAP_BRANCH_REVIEW_ACTIVE",oldBranch);
            }
        }
    }
}
