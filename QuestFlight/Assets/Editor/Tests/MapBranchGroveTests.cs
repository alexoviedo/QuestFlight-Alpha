using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using QuestFlightLab.Environment;

namespace QuestFlightLab.Editor.Tests
{
    public sealed class MapBranchGroveTests
    {
        [Test] public void AuthoredProductUsesOneBoundCameraAndNoOldGroveOverlay()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity");
            var all=UnityEngine.Object.FindObjectsByType<MapBranchGroveRenderer>(FindObjectsSortMode.None);
            Assert.AreEqual(1,all.Length);var grove=all[0];Assert.IsTrue(grove.enabled);Assert.AreSame(Camera.main,grove.viewingCamera);
            Assert.IsNotNull(grove.branchMesh);Assert.IsNotNull(grove.branchMaterial);Assert.AreEqual(0,grove.branchMaterial.GetFloat("_Diagnostic"));
            Assert.AreEqual(9,grove.observationStarts.Length);
            var boundary=UnityEngine.Object.FindFirstObjectByType<QuestFlight.Product.ProductMapBoundaryRecovery>();
            Assert.IsNotNull(boundary);Assert.IsTrue(boundary.HasAuthoredReferences);
            foreach(var pose in grove.observationStarts)
            {
                Assert.IsNotNull(pose);Assert.IsNotNull(pose.spawnTransform);
                Assert.IsFalse(QuestFlight.Product.ProductMapBoundaryRecoveryPolicy.ShouldRecover(
                    pose.spawnTransform.position,boundary.AuthoredTerrainSurface.macroMinimumEnuMeters,
                    boundary.AuthoredTerrainSurface.macroSizeMeters,boundary.TriggerInsetMeters),
                    "Paused observation would trigger normal boundary recovery: "+pose.name);
            }
            var dependencies=AssetDatabase.GetDependencies("Assets/Scenes/ProductionVerticalSlice.unity",true);
            foreach(var dependency in dependencies)
            {
                StringAssert.DoesNotContain("AngularGrovesV1",dependency);
                StringAssert.DoesNotContain("ConnectedGrovePilotV1",dependency);
            }
        }
        [Test] public void EveryPageIsPresentAndHashesValidateAndCorruptionFailsClosed()
        {
            var source=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Production/Environment/MapBranchGrovesV1/SOURCE_MANIFEST.json");
            var manifest=JsonUtility.FromJson<MapBranchGroveRenderer.Manifest>(source.text);Assert.AreEqual(1296,manifest.pages.Length);
            byte[] example=null;MapBranchGroveRenderer.Record record=null;
            foreach(var r in manifest.pages)
            {
                byte[] data=File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath,"QuestFlightBranchGroves",r.file));
                MapBranchGroveRenderer.ValidatePageForVisualQa(data,r);
                if(example==null&&r.count>0){example=data;record=r;}
            }
            Assert.IsNotNull(example);example[example.Length-1]^=1;
            Assert.Throws<InvalidDataException>(()=>MapBranchGroveRenderer.ValidatePageForVisualQa(example,record));
        }
        [Test] public void DistanceHandoffIsContinuousAndLeavesFullMiddleCoverage()
        {
            Assert.AreEqual(1,MapBranchGroveRenderer.DistanceCoverage(400));Assert.AreEqual(1,MapBranchGroveRenderer.DistanceCoverage(520));
            Assert.AreEqual(1,MapBranchGroveRenderer.DistanceCoverage(1400));Assert.AreEqual(0,MapBranchGroveRenderer.DistanceCoverage(1800));float previous=1;
            for(float d=1600;d<=1800;d+=.25f){float next=MapBranchGroveRenderer.DistanceCoverage(d);Assert.LessOrEqual(next,previous);Assert.Less(previous-next,.01f);previous=next;}
        }
        [Test] public void PageSeamAndOuterMapViewsLoadAndDisableRestoresTreeOwnership()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity");
            var grove=UnityEngine.Object.FindFirstObjectByType<MapBranchGroveRenderer>();
            var host=new GameObject("Branch QA Camera");var camera=host.AddComponent<Camera>();camera.fieldOfView=80;camera.farClipPlane=25000;
            try
            {
                foreach(var p in new[]{new Vector3(-1,120,-900),new Vector3(1,120,-900),new Vector3(11900,200,0),new Vector3(-11900,800,0)})
                {
                    host.transform.SetPositionAndRotation(p,Quaternion.LookRotation(new Vector3(-p.x,0,500).normalized));
                    grove.PrepareForVisualQa(camera);Assert.AreEqual(MapBranchGroveRenderer.MaximumPages,grove.ResidentPages);Assert.LessOrEqual(grove.SelectedCells,MapBranchGroveRenderer.MaximumCells);
                }
                grove.ClearForVisualQa();Assert.AreEqual(0,Shader.GetGlobalFloat("_MapGroveOwnershipActive"));Assert.IsFalse(grove.Ready);
            }
            finally{grove.ClearForVisualQa();UnityEngine.Object.DestroyImmediate(host);}
        }
        [Test] public void DenseViewsReserveDistantCoverageWithinTheOriginalTriangleCap()
        {
            var counts=new int[3];
            foreach(var demand in new[]{new[]{0,0,200000},new[]{200000,200000,200000},new[]{7000,10000,30000},new[]{0,200000,0}})
            {
                MapBranchGroveRenderer.AllocateTierCounts(demand[0],demand[1],demand[2],counts);
                Assert.LessOrEqual(counts[0]*12+counts[1]*6+counts[2]*4,393216);
                Assert.LessOrEqual(counts[0]+counts[1]+counts[2],MapBranchGroveRenderer.MaximumCells);
                for(int i=0;i<3;i++)Assert.LessOrEqual(counts[i],demand[i]);
                if(demand[2]>0)Assert.GreaterOrEqual(counts[2],Math.Min(demand[2],24576));
            }
        }
        [Test] public void OrdinaryPilotViewContainsDistantCanopyAndUsesSubsetMeshes()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity");
            var grove=UnityEngine.Object.FindFirstObjectByType<MapBranchGroveRenderer>();
            var host=new GameObject("Distant branch QA camera");var camera=host.AddComponent<Camera>();
            camera.fieldOfView=72;camera.aspect=16f/9;camera.farClipPlane=25000;
            try
            {
                for(int i=0;i<3;i++)Assert.AreEqual(new[]{36,18,12}[i],grove.branchMesh.GetIndexCount(i));
                host.transform.SetPositionAndRotation(new Vector3(-404,100,-1089),Quaternion.Euler(12,0,0));
                grove.PrepareForVisualQa(camera);
                Assert.Greater(grove.FarCells,1000);Assert.Greater(grove.FarthestSelectedMeters,1600);
                Assert.LessOrEqual(grove.SelectedTriangles,393216);
                Assert.Greater(grove.NearCells,1000);
                // Looking away from the terrain must not spend this geometry budget.
                host.transform.rotation=Quaternion.Euler(-90,0,0);grove.PrepareForVisualQa(camera);
                Assert.AreEqual(0,grove.SelectedCells);
            }
            finally{grove.ClearForVisualQa();UnityEngine.Object.DestroyImmediate(host);}
        }
        [Test] public void PureBankAndReversalRefreshSelectionBeforeTimerExpires()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity");
            var grove=UnityEngine.Object.FindFirstObjectByType<MapBranchGroveRenderer>();
            var original=grove.viewingCamera;
            var host=new GameObject("Bank selection regression camera");var camera=host.AddComponent<Camera>();
            camera.enabled=false;camera.fieldOfView=72;camera.aspect=16f/9;camera.farClipPlane=25000;
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var draw=typeof(MapBranchGroveRenderer).GetMethod("DrawForCamera",flags);
            var timer=typeof(MapBranchGroveRenderer).GetField("nextUpdate",flags);
            try
            {
                host.transform.SetPositionAndRotation(new Vector3(-404,100,-1089),Quaternion.Euler(12,0,0));
                grove.viewingCamera=camera;grove.PrepareForVisualQa(camera);
                var initial=host.transform.rotation;var forward=host.transform.forward;
                long selections=grove.SelectionCount;
                timer.SetValue(grove,float.MaxValue);draw.Invoke(grove,new object[]{camera});
                Assert.AreEqual(selections,grove.SelectionCount,"Stationary view must retain selection");
                foreach(float roll in new[]{30f,-30f,0f})
                {
                    host.transform.rotation=initial*Quaternion.AngleAxis(roll,Vector3.forward);
                    Assert.Greater(Vector3.Dot(forward,host.transform.forward),.99999f,"Exercise roll without translation/yaw/pitch");
                    timer.SetValue(grove,float.MaxValue);draw.Invoke(grove,new object[]{camera});
                    Assert.AreEqual(++selections,grove.SelectionCount,"Bank/reversal must refresh before timer");
                    Assert.Greater(grove.SelectedCells,0);Assert.LessOrEqual(grove.SelectedTriangles,393216);
                }
                host.transform.rotation=initial*Quaternion.AngleAxis(.1f,Vector3.forward);
                timer.SetValue(grove,float.MaxValue);draw.Invoke(grove,new object[]{camera});
                Assert.AreEqual(selections,grove.SelectionCount,"Sub-guard jitter must not force a rebuild");
            }
            finally{grove.ClearForVisualQa();grove.viewingCamera=original;UnityEngine.Object.DestroyImmediate(host);}
        }
        public static void RunBatch()
        {
            var t=new MapBranchGroveTests();t.AuthoredProductUsesOneBoundCameraAndNoOldGroveOverlay();t.EveryPageIsPresentAndHashesValidateAndCorruptionFailsClosed();t.DistanceHandoffIsContinuousAndLeavesFullMiddleCoverage();t.PageSeamAndOuterMapViewsLoadAndDisableRestoresTreeOwnership();t.DenseViewsReserveDistantCoverageWithinTheOriginalTriangleCap();t.OrdinaryPilotViewContainsDistantCanopyAndUsesSubsetMeshes();
            t.PureBankAndReversalRefreshSelectionBeforeTimerExpires();
            Debug.Log("[MapBranchTests] PASS: authored camera/activation; all 1296 pages; corruption rejection; continuous handoff; edge/seam loading; ownership reset; pure roll/reversal refresh and jitter guard");
        }

        // Run without -quit: advances the actual streaming iterator across Editor
        // frames and real UnityWebRequests while camera selection mutates its window.
        // This checks asynchronous residency, not tracked XR or GPU performance.
        public static void RunStreamingBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity");
            var grove=UnityEngine.Object.FindFirstObjectByType<MapBranchGroveRenderer>();
            var original=grove.viewingCamera;
            var host=new GameObject("Streaming regression QA camera");
            var camera=host.AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=80;camera.farClipPlane=25000;
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var initialize=typeof(MapBranchGroveRenderer).GetMethod("Initialize",flags);
            var select=typeof(MapBranchGroveRenderer).GetMethod("Select",flags);
            var stream=typeof(MapBranchGroveRenderer).GetMethod("Stream",flags);
            initialize.Invoke(grove,null);grove.viewingCamera=camera;
            var iterator=(IEnumerator)stream.Invoke(grove,null);
            AsyncOperation pending=null;int stage=0,updates=0;
            double started=EditorApplication.timeSinceStartup,next=0;
            EditorApplication.CallbackFunction update=null;
            void Finish(Exception failure)
            {
                EditorApplication.update-=update;
                (iterator as IDisposable)?.Dispose();grove.ClearForVisualQa();grove.viewingCamera=original;
                UnityEngine.Object.DestroyImmediate(host);
                if(failure!=null)Debug.LogException(failure);
                else Debug.Log("[MapBranchStreamingTests] PASS: actual async requests survive selection updates and interrupted window changes; bounded 49-page windows at both remote stations; no failures. Updates="+updates);
                EditorApplication.Exit(failure==null?0:1);
            }
            update=()=>
            {
                try
                {
                    updates++;
                    if(EditorApplication.timeSinceStartup-started>120)throw new TimeoutException("Branch asynchronous residency did not settle");
                    Vector3 position=stage==0?new Vector3(-400,100,-1000):stage==1?new Vector3(11020,300,-300):new Vector3(-11180,800,-300);
                    host.transform.SetPositionAndRotation(position,Quaternion.Euler(15,Mathf.Sin(updates*.15f)*10,0));
                    select.Invoke(grove,new object[]{camera,false});
                    Assert.LessOrEqual(grove.ResidentPages,MapBranchGroveRenderer.MaximumPages);
                    Assert.AreEqual(0,grove.LoadFailures);
                    // Interrupt the first window while a request is outstanding.
                    if(stage==0&&grove.ResidentPages>=2&&pending!=null){stage=1;}
                    if(pending!=null&&!pending.isDone)return;
                    if(EditorApplication.timeSinceStartup<next)return;
                    pending=null;
                    if(!iterator.MoveNext())throw new InvalidOperationException("Streaming iterator stopped before completion");
                    pending=iterator.Current as AsyncOperation;
                    if(iterator.Current is WaitForSecondsRealtime)
                    {
                        next=EditorApplication.timeSinceStartup+.25;
                        if(grove.ResidentPages==MapBranchGroveRenderer.MaximumPages&&grove.Ready)
                        {
                            if(stage==1)stage=2;
                            else if(stage==2){Finish(null);return;}
                        }
                    }
                }
                catch(Exception ex){Finish(ex);}
            };
            EditorApplication.update+=update;
        }
    }
}
