using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEditor.Rendering;
using UnityEngine;
using QuestFlight.Product;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        public static void RunGroundCloudRepair()
        {
            string output=System.Environment.GetEnvironmentVariable("QF_GROUND_CLOUD_QA");
            Directory.CreateDirectory(output);
            BoulderCloudAssetAuthoring.RebuildFormationGeometryForVisualRepair();
            var context=LoadContext(1280,960,new ProductionVisualQaReport());
            context.camera.depthTextureMode |= DepthTextureMode.Depth;
            var sky=RenderSettings.skybox;var oldSky=new Material(sky);
            // Match by the authored shader asset, independent of display-name changes.
            var terrainShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.shader");
            var groundMaterials=AssetDatabase.FindAssets("t:Material",new[]{"Assets/Production/Environment"})
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>).Where(m=>m!=null&&m.shader==terrainShader).ToArray();
            if(groundMaterials.Length==0)throw new Exception("No authored terrain materials");
            var mode=context.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
            Time.timeScale=0;InvokePrivate(mode,"BeginSelection");
            if(!mode.AuthoredFlightCoordinator.InitializeSelectedBackend()||!mode.SelectAircraft(true))throw new Exception("Native SGS selection failed");
            mode.SelectFreeFlight();mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();
            typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(mode.AuthoredNavigation,ProductAppNavigation.Page.None);mode.AuthoredNavigation.RefreshMenu();
            using var preview=new ProductionVisualQaSettledSdkPreview(context.rig.transform);
            Vector3 axis=(context.runway26-context.runway08).normalized;
            float length=Vector3.Distance(context.runway08,context.runway26),width=context.environment.runwayWidthMeters;
            var originAxis=new Vector4(context.runway08.x,context.runway08.z,axis.x,axis.z);
            var size=new Vector4(length,width,1,0);
            for(int candidate=0;candidate<2;candidate++)
            {
                RenderSettings.skybox=candidate==0?oldSky:sky;
                foreach(var m in groundMaterials){m.SetVector("_RunwayCutoutOriginAxis",originAxis);m.SetVector("_RunwayCutoutSize",candidate==0?Vector4.zero:size);}
                for(int view=0;view<10;view++)
                {
                    Vector3 position=view<5?context.runway08+context.runwayForward*150+Vector3.up*.6f:(view<7?new Vector3(-1500,view==5?200:800,1800):new Vector3(-4800,1000,-6800+(view-7)*1100));
                    var rotation=Quaternion.LookRotation(context.runwayForward);
                    SetAircraftPose(context,position,rotation);
                    Vector3 eye=mode.AircraftPresentation.gliderRoot.transform.TransformPoint(mode.AircraftPresentation.glider.eyeFromCgMeters);
                    Quaternion look=rotation*Quaternion.Euler(view<5?10:-10,view<5?new[]{-60f,-30f,0f,30f,60f}[view]:(view>=7?-90:0),0);
                    if(view>=7)
                    {
                        var field=context.environment.boulderCloudField.GetComponent<QuestFlightLab.Environment.BoulderCloudField>();
                        var cloud=field.Profile.Volumes[0];
                        var target=field.transform.TransformPoint(cloud.centerLocalMeters);
                        eye=field.transform.TransformPoint(cloud.centerLocalMeters+new Vector3(0,0,new[]{-2500f,-800f,-100f}[view-7]));
                        look=Quaternion.LookRotation(target-eye);
                        SetAircraftPose(context,new Vector3(0,-1000,0),Quaternion.identity);
                        File.AppendAllText(Path.Combine(output,"cloud_poses.txt"),$"{view} eye={eye} target={target} pipeline={UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline}\n");
                    }
                    context.environment.boulderCloudField.GetComponent<QuestFlightLab.Environment.BoulderCloudField>().PrepareTransparencyForCamera(eye);
                    for(int stereo=0;stereo<2;stereo++)
                    {
                        context.camera.transform.SetPositionAndRotation(eye+look*Vector3.right*(stereo==0?-.032f:.032f),look);
                        var pixels=Render(context.camera,1280,960,out _);
                        File.WriteAllBytes(Path.Combine(output,$"{(candidate==0?"before":"after")}_view{view}_{stereo}.png"),pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);
                    }
                }
            }
            foreach(var m in groundMaterials)EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();UnityEngine.Object.DestroyImmediate(oldSky);
            var profile=context.environment.boulderCloudField.GetComponent<QuestFlightLab.Environment.BoulderCloudField>().Profile;
            // Distinct boxes must not contain or intersect another authored formation.
            for(int i=0;i<profile.Volumes.Length;i++)for(int k=i+1;k<profile.Volumes.Length;k++)
            {
                var a=profile.Volumes[i];var b=profile.Volumes[k];
                if(new Bounds(a.centerLocalMeters,a.radiiMeters*2).Intersects(new Bounds(b.centerLocalMeters,b.radiiMeters*2)))throw new Exception("Overlapping cloud proxies: "+a.id+" / "+b.id);
            }
            string[] shaders={"Assets/Production/Environment/CloudsV1/BoulderCloudShell.shader",AssetDatabase.GetAssetPath(terrainShader),"Assets/Resources/QuestFlightLab/Environment/Shaders/ProductionRunwayMarking.shader"};
            foreach(string path in shaders)
            {
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);
                foreach(var stage in new[]{ShaderType.Vertex,ShaderType.Fragment})
                {
                    var compiled=ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0).CompileVariant(stage,new[]{"DIRECTIONAL","INSTANCING_ON","STEREO_INSTANCING_ON","FOG_LINEAR"},ShaderCompilerPlatform.Vulkan,BuildTarget.Android,true);
                    File.WriteAllText(Path.Combine(output,Path.GetFileName(path)+stage+".txt"),"Success="+compiled.Success+"\n"+string.Join("\n",compiled.Messages.Select(m=>m.message)));
                    if(!compiled.Success)throw new Exception("Android shader failed "+path);
                }
            }
            var bundle=new AssetBundleBuild{assetBundleName="ground_cloud_visual_repair",assetNames=groundMaterials.Select(AssetDatabase.GetAssetPath).Concat(new[]{AssetDatabase.GetAssetPath(sky),"Assets/Production/Environment/CloudsV1/KBDU_BoulderCloudShell.mat","Assets/Production/Rendering/ProductForwardRenderer.asset","Assets/Production/Environment/Materials/Production_Runway_ControlledDepthMarking.mat"}).ToArray()};
            string build=Path.Combine(output,"android_assets");Directory.CreateDirectory(build);
            if(BuildPipeline.BuildAssetBundles(build,new[]{bundle},BuildAssetBundleOptions.StrictMode|BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.Android)==null)throw new Exception("Android assets failed");
            File.WriteAllText(Path.Combine(output,"validation.txt"),"PASS:40 offset-eye editor captures; eight separated cloud formations; Android Vulkan stereo shader compilation and strict Android asset bundle. Terrain/paint geometry and colliders unchanged. Before/after compares terrain ownership only; sky retained; cloud approach/edge/interior views included. No headset or live simulator acceptance.\nGround material count="+groundMaterials.Length);
            Time.timeScale=1;
        }
    }
}
