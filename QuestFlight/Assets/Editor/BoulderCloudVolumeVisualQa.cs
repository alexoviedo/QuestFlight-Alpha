using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using QuestFlight.Rendering;

namespace QuestFlightLab.Editor
{
    public static class BoulderCloudVolumeVisualQa
    {
        public static void RunBatch()
        {
            try { Capture(); EditorApplication.Exit(0); }
            catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Capture()
        {
            string output=System.Environment.GetEnvironmentVariable("QFL_CLOUD_QA_DIR");
            if(string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("External QFL_CLOUD_QA_DIR required.");
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("Offline cloud shader fixture");
            var filter=root.AddComponent<MeshFilter>();
            filter.sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(BoulderCloudVolumeAuthoring.Root+"Generated/KBDU_BoulderCloudSector_0.asset");
            var renderer=root.AddComponent<MeshRenderer>();
            var material=new Material(AssetDatabase.LoadAssetAtPath<Material>(BoulderCloudVolumeAuthoring.Root+"KBDU_BoulderCloudShell.mat"));
            renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
            var light=new GameObject("Fixture sun").AddComponent<Light>();
            light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(35,150,0);RenderSettings.sun=light;
            var camera=new GameObject("Offline fixture camera").AddComponent<Camera>();
            camera.fieldOfView=75;camera.nearClipPlane=.1f;camera.farClipPlane=20000;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.3f,.45f,.65f,1);
            var target=new RenderTexture(960,640,24,RenderTextureFormat.ARGB32){antiAliasing=4};
            camera.targetTexture=target;target.Create();
            var pixels=new Texture2D(960,640,TextureFormat.RGB24,false);
            var positions=new[]{new Vector3(-4800,250,-7200),new Vector3(-4800,1900,-6500),new Vector3(-5000,1000,-4480),new Vector3(0,1000,0)};
            var lookAt=new Vector3(-4800,1000,-4480);
            Shader.SetGlobalVector("_CloudDrift",Vector4.zero);
            for(int view=0;view<positions.Length;view++)for(int eye=0;eye<2;eye++)for(int mode=0;mode<3;mode++)
            {
                camera.transform.SetPositionAndRotation(positions[view],Quaternion.LookRotation(lookAt-positions[view]));
                camera.transform.position+=camera.transform.right*(eye==0?-.032f:.032f);
                material.SetFloat("_CloudVolumeSampling",mode>0?1:0);
                material.SetFloat("_CloudEmptySkipping",mode==2?1:0);
                ProductRenderCapture.Render(camera);
                var previous=RenderTexture.active;RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,960,640),0,0);pixels.Apply();RenderTexture.active=previous;
                File.WriteAllBytes(Path.Combine(output,$"view{view}_eye{eye}_mode{mode}.png"),pixels.EncodeToPNG());
            }
            camera.targetTexture=null;target.Release();
            UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
