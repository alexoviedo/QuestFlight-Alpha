using System;
using System.IO;
using System.Linq;
using QuestFlight.Rendering;
using QuestFlight.Product;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.OpenXR;

namespace QuestFlight.Editor
{
    public static class ProductSpaceWarpAuthoring
    {
        const string Root = "Assets/Production/Rendering";
        public static void ConfigurePipeline()
        {
            Directory.CreateDirectory(Root);
            string rendererPath = Root + "/ProductForwardRenderer.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null) { renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(renderer, rendererPath); }
            if (!renderer.rendererFeatures.OfType<ProductSpaceWarpRenderFeature>().Any())
            {
                var feature = ScriptableObject.CreateInstance<ProductSpaceWarpRenderFeature>(); feature.name = "Product procedural and XR motion";
                AssetDatabase.AddObjectToAsset(feature, renderer); renderer.rendererFeatures.Add(feature); renderer.SetDirty(); EditorUtility.SetDirty(renderer);
            }
            var productFeature=renderer.rendererFeatures.OfType<ProductSpaceWarpRenderFeature>().Single();
            productFeature.ConfigureDiagnostic(AssetDatabase.LoadAssetAtPath<Shader>(Root+"/SpaceWarpDiagnostic.shader"));EditorUtility.SetDirty(productFeature);
            string pipelinePath = Root + "/ProductQuestURP.asset";
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null) { pipeline = UniversalRenderPipelineAsset.Create(renderer); AssetDatabase.CreateAsset(pipeline, pipelinePath); }
            pipeline.msaaSampleCount = 4; pipeline.renderScale = 1; pipeline.supportsHDR = false;
            pipeline.supportsCameraDepthTexture = true; pipeline.supportsCameraOpaqueTexture = false;
            // Accepted shaders retain their lightmap/reflection equations and
            // legacy per-draw uniforms. Do not claim SRP-batcher compatibility.
            pipeline.useSRPBatcher = false;
            var pipelineSettings=new SerializedObject(pipeline);
            pipelineSettings.FindProperty("m_ReflectionProbeBlending").boolValue=true;
            pipelineSettings.FindProperty("m_ReflectionProbeBoxProjection").boolValue=true;
            pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
            pipeline.shadowDistance=QualitySettings.shadowDistance;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            int level = QualitySettings.GetQualityLevel();
            for (int i=0;i<QualitySettings.names.Length;i++) { QualitySettings.SetQualityLevel(i,false); QualitySettings.renderPipeline = pipeline; }
            QualitySettings.SetQualityLevel(level,false);
            var rg = GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>();
            if (rg != null && rg.enableRenderCompatibilityMode)
                throw new InvalidOperationException("SpaceWarp requires RenderGraph; compatibility mode is forbidden.");
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (xr != null) foreach (var feature in xr.GetFeatures<UnityEngine.XR.OpenXR.Features.OpenXRFeature>())
            {
                if (feature.GetType().FullName == "Meta.XR.MetaXRSpaceWarp") { feature.enabled = true; EditorUtility.SetDirty(feature); }
                if (feature.GetType().FullName == "UnityEngine.XR.OpenXR.Features.SpaceWarpFeature") { feature.enabled = false; EditorUtility.SetDirty(feature); }
            }
            EditorUtility.SetDirty(pipeline); AssetDatabase.SaveAssets();
        }
        public static void ConfigureBatch()
        {
            try
            {
                ConfigurePipeline();
                EditorSceneManager.OpenScene(ProductProjectConfigurator.ProductionScenePath);
                var rig = UnityEngine.Object.FindFirstObjectByType<AircraftReferenceFrameRig>(FindObjectsInactive.Include);
                var modes = UnityEngine.Object.FindFirstObjectByType<ProductFlightModeCoordinator>(FindObjectsInactive.Include);
                if (rig == null || modes == null) throw new InvalidOperationException("Authored production rig/modes absent");
                ProductSpaceWarpAudit.ConvertMaterials();
                foreach(var root in rig.gameObject.scene.GetRootGameObjects())ProductSpaceWarpAudit.ConvertSceneMaterials(root);
                const string aircraftPath="Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
                var aircraft=PrefabUtility.LoadPrefabContents(aircraftPath);
                try {ProductSpaceWarpAudit.ConvertSceneMaterials(aircraft);PrefabUtility.SaveAsPrefabAsset(aircraft,aircraftPath);}
                finally{PrefabUtility.UnloadPrefabContents(aircraft);}
                foreach(string path in ProductSpaceWarpAudit.Inputs().Where(p=>p.StartsWith("Assets/")&&p.EndsWith(".glb")))
                    AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
                var controller = rig.GetComponent<ProductSpaceWarpController>();
                if (controller == null) controller = rig.gameObject.AddComponent<ProductSpaceWarpController>();
                controller.Configure(rig,modes); EditorUtility.SetDirty(controller);
                foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                    if (renderer is SkinnedMeshRenderer skin) skin.skinnedMotionVectors = true;
                    EditorUtility.SetDirty(renderer);
                }
                EditorSceneManager.MarkSceneDirty(rig.gameObject.scene); EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
                Debug.Log("[QuestFlight][SpaceWarp] Authored pipeline and controller saved"); EditorApplication.Exit(0);
            }
            catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
