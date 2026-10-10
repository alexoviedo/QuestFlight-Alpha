using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.OpenXR;

namespace QuestFlight.Editor
{
    public static class ProductSpaceWarpAudit
    {
        // Read-only inspection of the runway capture's reconstructed world points.
        public static void InspectRunwayHistoryLocations()
        {
            EditorSceneManager.OpenScene(ProductProjectConfigurator.ProductionScenePath);
            var points = new[] { new Vector3(-81.42f,4.74f,-122.93f) };
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!points.Any(p => renderer.bounds.SqrDistance(p) < 100)) continue;
                string path = renderer.name;
                for (var parent = renderer.transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
                Debug.Log("[HistoryLocation] " + path + " bounds=" + renderer.bounds + " static=" + renderer.gameObject.isStatic +
                    " shaders=" + string.Join(",", renderer.sharedMaterials.Where(m => m != null).Select(m => m.shader.name)));
            }
        }
        [Serializable] public class Entry { public string path,shader,policy; public string[] sceneUsers; public bool supported, motion; }
        [Serializable] public class Report
        {
            public string schema="questflight.spacewarp.coverage.v1";
            public string hardware="NOT RUN: headset acceptance deferred";
            public List<Entry> materials=new List<Entry>();
            public List<string> errors=new List<string>();
            public bool passed;
        }
        static bool Background(string shader) => shader.Contains("Window Reflection") || shader.Contains("Glass Reflection") || shader.Contains("Propeller") || shader.Contains("Blob Shadow") || shader.StartsWith("Skybox/");
        public static string[] Inputs() => AssetDatabase.GetDependencies(ProductProjectConfigurator.ProductionScenePath,true)
            .Concat(AssetDatabase.FindAssets("t:Material",new[]{"Assets/Resources"}).Select(AssetDatabase.GUIDToAssetPath)).Distinct().ToArray();
        public static void ConvertMaterials()
        {
            foreach(string path in Inputs().Where(p => p.StartsWith("Assets/",StringComparison.Ordinal) && p.EndsWith(".mat",StringComparison.OrdinalIgnoreCase)))
            foreach(var material in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                ConvertMaterial(material);
        }
        public static void ConvertSceneMaterials(GameObject root)
        {
            var replacements=new Dictionary<Material,Material>();
            Material Resolve(Material source)
            {
                if(source==null||source.shader==null)return source;
                if(replacements.TryGetValue(source,out var existing))return existing;
                string shader=source.shader.name;
                if(shader!="Interaction/OculusRayVisual"&&shader!="Interaction/OculusRayCursor"&&shader!="Interaction/OculusHand"&&shader!="Legacy Shaders/Diffuse"&&shader!="Meta/Lit"&&shader!="Unlit/TunnelingEffect"&&shader!="Unlit/Hotspot"&&shader!="Unlit/TransparentVertexTexture"&&shader!="Unlit/TransparentTexture"&&shader!="GUI/Text Shader"&&shader!="UI/Default"&&shader!="Unlit/Transparent"&&shader!="Unlit/Color")return source;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long id);
                const string folder="Assets/Production/Rendering/Materials";Directory.CreateDirectory(folder);
                string path=folder+"/"+guid+"_"+id+".mat";
                var clone=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(clone==null){clone=new Material(source);clone.name=source.name+" XR";AssetDatabase.CreateAsset(clone,path);}
                // SDK poke visuals use Unlit/Color. Convert only the local clone,
                // retaining its color/queue through the existing URP converter.
                if(shader=="Unlit/Color")ConvertMaterial(clone);
                else clone.shader=Shader.Find((shader=="Meta/Lit"||shader=="Legacy Shaders/Diffuse")?"Universal Render Pipeline/Lit":(shader=="GUI/Text Shader"||shader=="UI/Default")?"QuestFlight/Interface/World Text":shader=="Unlit/Transparent"?"QuestFlight/XR/Unlit/TransparentTexture":"QuestFlight/XR/"+shader);
                if(shader=="UI/Default")clone.SetFloat("_UseTextureRGB",1);
                if(clone.shader==null)throw new InvalidOperationException("Missing product XR shader for "+shader);
                clone.SetFloat("_XRMotionVectorsPass",1);clone.SetShaderPassEnabled("XRMotionVectors",true);
                EditorUtility.SetDirty(clone);replacements.Add(source,clone);return clone;
            }
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats=renderer.sharedMaterials;bool changed=false;
                for(int i=0;i<mats.Length;i++){var replacement=Resolve(mats[i]);changed|=replacement!=mats[i];mats[i]=replacement;}
                if(changed)renderer.sharedMaterials=mats;
                renderer.motionVectorGenerationMode=MotionVectorGenerationMode.Object;
                if(renderer is SkinnedMeshRenderer skin)skin.skinnedMotionVectors=true;
                EditorUtility.SetDirty(renderer);
            }
            // SDK visual components may retain serialized base-material fields.
            foreach(var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if(component==null)continue;
                var serialized=new SerializedObject(component);var property=serialized.GetIterator();
                while(property.Next(true))if(property.propertyType==SerializedPropertyType.ObjectReference&&property.objectReferenceValue is Material mat)
                {var replacement=Resolve(mat);if(replacement!=mat)property.objectReferenceValue=replacement;}
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        public static void ConvertMaterial(Material material)
        {
            if(material==null||material.shader==null)return;
            string original=material.shader.name;
            if(original=="Shader Graphs/glTF-pbrMetallicRoughness")
            {
                if(material.HasProperty("emissiveTexture")&&material.GetTexture("emissiveTexture")!=null)material.EnableKeyword("_EMISSIVE");
                material.SetFloat("_XRMotionVectorsPass",1);material.SetShaderPassEnabled("XRMotionVectors",true);EditorUtility.SetDirty(material);return;
            }
            if(original=="glTF/PbrMetallicRoughness")
            {
                // glTFast keeps glTF inputs under the same names across pipelines.
                bool transparent=material.renderQueue>=3000;
                bool masked=material.IsKeywordEnabled("_ALPHATEST_ON");
                int originalQueue=material.renderQueue;
                float cull=material.HasProperty("_Cull")?material.GetFloat("_Cull"):2;
                material.shader=AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.cloud.gltfast/Runtime/Shader/glTF-pbrMetallicRoughness.shadergraph");
                if(material.shader==null)throw new InvalidOperationException("Pinned glTF URP shader missing");
                material.SetFloat("_Cull",cull);material.SetFloat("_Surface",transparent?1:0);
                material.SetFloat("_AlphaClip",masked?1:0);
                material.SetFloat("_ZWrite",transparent?0:1);
                material.SetFloat("_SrcBlend",transparent?(float)BlendMode.SrcAlpha:1);
                material.SetFloat("_DstBlend",transparent?(float)BlendMode.OneMinusSrcAlpha:0);
                if(transparent)material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetFloat("_XRMotionVectorsPass",1);material.SetShaderPassEnabled("XRMotionVectors",true);
                material.renderQueue=originalQueue;
                if(material.GetTexture("emissiveTexture")!=null)material.EnableKeyword("_EMISSIVE");
                EditorUtility.SetDirty(material);return;
            }
            if(original=="GUI/Text Shader")
            { material.shader=Shader.Find("QuestFlight/Interface/World Text");EditorUtility.SetDirty(material);return; }
            if(original!="Standard" && original!="Unlit/Color" && original!="Unlit/Texture")return;
            Color color=material.HasProperty("_Color")?material.GetColor("_Color"):Color.white;
            Texture texture=material.HasProperty("_MainTex")?material.GetTexture("_MainTex"):null;
            Texture normal=material.HasProperty("_BumpMap")?material.GetTexture("_BumpMap"):null;
            float normalScale=material.HasProperty("_BumpScale")?material.GetFloat("_BumpScale"):1;
            float smooth=material.HasProperty("_Glossiness")?material.GetFloat("_Glossiness"):0;
            float metal=material.HasProperty("_Metallic")?material.GetFloat("_Metallic"):0;
            int queue=material.renderQueue;
            material.shader=Shader.Find(original=="Standard"?"Universal Render Pipeline/Lit":"Universal Render Pipeline/Unlit");
            material.SetColor("_BaseColor",color);material.SetTexture("_BaseMap",texture);
            if(original=="Standard")
            {
                material.SetFloat("_Smoothness",smooth);material.SetFloat("_Metallic",metal);material.SetTexture("_BumpMap",normal);material.SetFloat("_BumpScale",normalScale);
                if(normal!=null)material.EnableKeyword("_NORMALMAP");
            }
            material.SetFloat("_XRMotionVectorsPass",1);material.SetShaderPassEnabled("XRMotionVectors",true);
            material.renderQueue=queue;EditorUtility.SetDirty(material);
        }
        public static Report Inspect()
        {
            var report=new Report();
            var sceneRenderers=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            var referenced=new HashSet<Material>(sceneRenderers.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null));
            // Inspect resolved serialized references, not every unused material
            // embedded in a source FBX or an inherited SDK prefab dependency.
            foreach(var component in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                var serialized=new SerializedObject(component);var property=serialized.GetIterator();
                while(property.Next(true))if(property.propertyType==SerializedPropertyType.ObjectReference && property.objectReferenceValue is Material material)referenced.Add(material);
            }
            foreach(string resource in AssetDatabase.FindAssets("t:Material",new[]{"Assets/Resources"}).Select(AssetDatabase.GUIDToAssetPath))
                foreach(var material in AssetDatabase.LoadAllAssetsAtPath(resource).OfType<Material>())referenced.Add(material);
            if(!(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset pipeline))report.errors.Add("Production URP asset missing");
            else if(pipeline.msaaSampleCount!=4 || Mathf.Abs(pipeline.renderScale-1)>.0001f)report.errors.Add("Image quality changed");
            if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)report.errors.Add("Audit requires Android: ShaderGraph XR passes are platform-specific");
            var renderGraph=GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>();
            if(renderGraph==null || renderGraph.enableRenderCompatibilityMode)report.errors.Add("RenderGraph is required");
            var settings=OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var features=settings.GetFeatures<UnityEngine.XR.OpenXR.Features.OpenXRFeature>();
            if(features.Count(f=>f.enabled&&f.GetType().FullName=="Meta.XR.MetaXRSpaceWarp")!=1 ||
                features.Any(f=>f.enabled&&f.GetType().FullName=="UnityEngine.XR.OpenXR.Features.SpaceWarpFeature"))report.errors.Add("Exactly one Meta SpaceWarp feature must own submission");
            if(UnityEngine.Object.FindObjectsByType<QuestFlight.Rendering.ProductSpaceWarpController>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length!=1)report.errors.Add("Exactly one authored SpaceWarp controller required");
            var stable=Shader.Find(QuestFlightLab.Aircraft.ProductionAircraftLightingController.StableCockpitShaderName);
            if(stable==null || !stable.isSupported)report.errors.Add("Runtime cockpit shader is unavailable");
            foreach(var material in referenced.OrderBy(m=>AssetDatabase.GetAssetPath(m)).ThenBy(m=>m.name))
            {
                string path=AssetDatabase.GetAssetPath(material);
                string shader=material.shader!=null?material.shader.name:"MISSING";
                bool background=Background(shader);
                bool motion=material.FindPass("XRMotionVectors")>=0 && material.GetShaderPassEnabled("XRMotionVectors");
                var entry=new Entry{path=path+"#"+material.name,shader=shader,motion=motion,policy=background?"Background-owned motion/depth":"Matching surface motion/depth",supported=material.shader!=null&&material.shader.isSupported};
                entry.sceneUsers=sceneRenderers.Where(r=>r.sharedMaterials.Contains(material)).Select(r=>r.name+" ("+r.enabled+", active="+r.gameObject.activeInHierarchy+")").ToArray();
                report.materials.Add(entry);
                if(!entry.supported || (!background&&!motion))report.errors.Add(entry.path+": unsupported motion policy: "+shader);
                if(material.shader!=null)foreach(var message in ShaderUtil.GetShaderMessages(material.shader))
                    if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)report.errors.Add(shader+": "+message.message);
            }
            report.passed=report.errors.Count==0;return report;
        }
        public static void RunProbeBatch()
        {
            try
            {
                EditorSceneManager.OpenScene(ProductProjectConfigurator.ProductionScenePath);
                var rig=UnityEngine.Object.FindFirstObjectByType<QuestFlightLab.Runtime.AircraftReferenceFrameRig>(FindObjectsInactive.Include);
                var lighting=UnityEngine.Object.FindFirstObjectByType<QuestFlightLab.Aircraft.ProductionAircraftLightingController>(FindObjectsInactive.Include);
                lighting.ApplyAuthoredLightingNow();lighting.RenderCockpitLocalShadowNow();
                var go=new GameObject("Offline render compatibility probe");var camera=go.AddComponent<Camera>();
                camera.transform.SetPositionAndRotation(rig.PilotSeatAnchor.position,rig.PilotSeatAnchor.rotation);
                camera.nearClipPlane=.03f;camera.farClipPlane=40000;camera.fieldOfView=75;
                var target=new RenderTexture(1024,768,24);camera.targetTexture=target;
                var errors=new List<string>();Application.LogCallback onLog=(message,stack,type)=>{if(type==LogType.Exception||type==LogType.Error)errors.Add(message);};
                Application.logMessageReceived+=onLog;
                QuestFlight.Rendering.ProductRenderCapture.Render(camera);
                Application.logMessageReceived-=onLog;
                RenderTexture.active=target;var image=new Texture2D(1024,768,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1024,768),0,0);image.Apply();
                string dir=Environment.GetEnvironmentVariable("QFL_SPACEWARP_ARTIFACTS");
                File.WriteAllBytes(Path.Combine(dir,"render_probe.png"),image.EncodeToPNG());
                var report=Inspect();report.errors.AddRange(errors);report.passed=report.errors.Count==0;
                File.WriteAllText(Path.Combine(dir,"render_coverage.json"),JsonUtility.ToJson(report,true));
                EditorApplication.Exit(report.passed?0:1);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void RunBatch()
        {
            try
            {
                EditorSceneManager.OpenScene(ProductProjectConfigurator.ProductionScenePath);
                // Instantiate the selected pipeline before querying active shader passes.
                var go=new GameObject("Coverage audit camera");var camera=go.AddComponent<Camera>();
                var target=new RenderTexture(32,32,24);camera.targetTexture=target;
                QuestFlight.Rendering.ProductRenderCapture.Render(camera);
                UnityEngine.Object.DestroyImmediate(go);target.Release();UnityEngine.Object.DestroyImmediate(target);
                var report=Inspect();
                string dir=Environment.GetEnvironmentVariable("QFL_SPACEWARP_ARTIFACTS");
                if(string.IsNullOrEmpty(dir))throw new InvalidOperationException("QFL_SPACEWARP_ARTIFACTS must be external to Assets");
                Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"render_coverage.json"),JsonUtility.ToJson(report,true));
                foreach(string error in report.errors)Debug.LogError(error);
                EditorApplication.Exit(report.passed?0:1);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
