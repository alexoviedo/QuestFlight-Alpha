using System;
using System.IO;
using System.Linq;
using QuestFlight.Product;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        public static void VerifySoaringYawString()
        {
            string output=System.Environment.GetEnvironmentVariable("QF_YAW_STRING_QA");Directory.CreateDirectory(output);
            var c=LoadContext(1280,960,new ProductionVisualQaReport());
            var mode=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
            Time.timeScale=0;InvokePrivate(mode,"BeginSelection");
            var flight=mode.AuthoredFlightCoordinator;
            if(!flight.InitializeSelectedBackend()||!mode.PrepareGroundTow())throw new Exception("Native ground preparation failed.");
            mode.SelectFreeFlight();mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();
            // Hide only the editor preview's paused menu, so the entire resting
            // strand is inspectable. This does not bypass player focus/presence.
            var nav=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductAppNavigation>(true)).Single();
            var panel=(GameObject)new SerializedObject(nav).FindProperty("panel").objectReferenceValue;
            panel.SetActive(false);
            var p=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SoaringInstrumentPresenter>(true)).Single();
            var part=p.parts.Single(x=>x.name=="YawString");
            var mesh=UnityEngine.Object.Instantiate(p.moving.sharedMesh);p.moving.sharedMesh=mesh;
            var rest=mesh.vertices;var rn=mesh.normals;var v=(Vector3[])rest.Clone();var n=(Vector3[])rn.Clone();
            var yarn=new SoaringYawString();var originalEye=c.rig.PilotSeatAnchor.position;float originalFov=c.camera.fieldOfView;
            var report=new System.Collections.Generic.List<string>();
            using(var preview=new ProductionVisualQaSettledSdkPreview(c.rig.transform))
            {
                string[] names={"still_air","coordinated","slip_left","slip_right","reversal"};
                for(int pose=0;pose<names.Length;pose++)
                {
                    yarn.Reset();float speed=pose==0?0:25, beta=pose==2?-15:pose>=3?15:0;
                    for(int step=0;step<=180;step++)yarn.Sample(step/90.0,SoaringYawString.LocalAirflow(speed,4,beta,Vector3.zero,part.anchor),Vector3.down,part.anchor,part.rotationAxis);
                    if(pose==4)yarn.Sample(2+2.0/90,SoaringYawString.LocalAirflow(25,4,-15,Vector3.zero,part.anchor),Vector3.down,part.anchor,part.rotationAxis);
                    yarn.WriteVertices(part,rest,rn,v,n);mesh.vertices=v;mesh.normals=n;
                    report.Add(names[pose]+" tip="+yarn.Tip.ToString("F6"));
                    for(int eye=0;eye<2;eye++)
                    {
                        // c.camera is the established editor-only QA camera,
                        // never the OpenXR-owned product Main Camera.
                        c.camera.transform.SetPositionAndRotation(originalEye+c.rig.transform.right*(eye==0?-.032f:.032f),c.rig.transform.rotation);
                        var pixels=Render(c.camera,1280,960,out _);File.WriteAllBytes(Path.Combine(output,names[pose]+"_"+eye+".png"),pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);
                        c.camera.fieldOfView=28;
                        c.camera.transform.rotation=Quaternion.LookRotation(p.transform.TransformPoint(part.anchor)-(originalEye+c.rig.transform.right*(eye==0?-.032f:.032f)),c.rig.transform.up);
                        pixels=Render(c.camera,960,720,out _);File.WriteAllBytes(Path.Combine(output,names[pose]+"_close_"+eye+".png"),pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);
                        c.camera.fieldOfView=originalFov;
                    }
                }
            }
            UnityEngine.Object.DestroyImmediate(mesh);
            File.WriteAllLines(Path.Combine(output,"visual_poses.txt"),report);
            Time.timeScale=1;
            string scripts=Path.Combine(output,"android_scripts");Directory.CreateDirectory(scripts);
            var compiled=PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings {target=BuildTarget.Android,group=BuildTargetGroup.Android,options=ScriptCompilationOptions.None},scripts);
            if(compiled.assemblies==null||compiled.assemblies.Count==0)throw new Exception("Android player compilation failed.");
            string assets=Path.Combine(output,"android_assets");Directory.CreateDirectory(assets);
            var manifest=BuildPipeline.BuildAssetBundles(assets,new[]{new AssetBundleBuild {assetBundleName="yaw_string",assetNames=new[]{SoaringAircraftAuthoring.Folder+"/SGS_Local_Moving.asset",SoaringAircraftAuthoring.Folder+"/SGS_Local_Static.asset",SoaringAircraftAuthoring.Folder+"/SGS_Paint.mat"}}},BuildAssetBundleOptions.StrictMode|BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.Android);
            if(manifest==null)throw new Exception("Android asset build failed.");
            File.WriteAllText(Path.Combine(output,"android_validation.txt"),"PASS Android player scripts ("+compiled.assemblies.Count+") and strict mesh/material asset bundle. Not a full APK or hardware measurement.");
        }
    }
}
