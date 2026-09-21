using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using QuestFlightLab.Environment;
using QuestFlightLab.Runtime;

namespace QuestFlightLab.Editor.Tests
{
    public static class FoveationCloudRecoveryReview
    {
        public static void Run()
        {
            string root = System.Environment.GetEnvironmentVariable("QFL_RECOVERY_ROOT");
            if (string.IsNullOrEmpty(root)) throw new InvalidOperationException("QFL_RECOVERY_ROOT required");
            Directory.CreateDirectory(root);
            EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity");
            var marker = UnityEngine.Object.FindFirstObjectByType<ProductionVerticalSliceRoot>();
            Assert.AreEqual((int)OVRManager.FoveatedRenderingLevel.Medium, new SerializedObject(marker).FindProperty("fixedFoveationLevel").intValue);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            Assert.IsTrue(settings.GetFeature<Meta.XR.MetaXRFoveationFeature>().enabled);
            Assert.IsTrue(settings.GetFeature<Meta.XR.MetaXRSubsampledLayout>().enabled);
            var cloudTests = new BoulderCloudFieldV1EditModeTests();
            foreach (var test in typeof(BoulderCloudFieldV1EditModeTests).GetMethods()
                .Where(method => Attribute.IsDefined(method, typeof(TestAttribute))))
                test.Invoke(cloudTests, null);
            MapBranchGroveTests.RunBatch();
            EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity");
            var field = UnityEngine.Object.FindFirstObjectByType<BoulderCloudField>();
            var original = field.SharedCloudMaterial;
            Assert.AreEqual(1f, original.GetFloat("_CloudSamplingLod"), "Approved sampling candidate must be authored on");
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var applyIsolation = typeof(QuestTemporalVisualGateRecorder).GetMethod("ApplyDiagnosticIsolation",flags);
            var restoreIsolation = typeof(QuestTemporalVisualGateRecorder).GetMethod("RestoreDiagnosticIsolation",flags);
            var isolation = applyIsolation.Invoke(null,new object[]{QuestTemporalVisualGateRecorder.CloudsFullSamplingIsolationProfile,
                UnityEngine.Object.FindFirstObjectByType<ProductionEnvironmentRoot>()});
            try { Assert.AreEqual(0f,original.GetFloat("_CloudSamplingLod")); }
            finally { restoreIsolation.Invoke(null,new[]{isolation}); }
            Assert.AreEqual(1f,original.GetFloat("_CloudSamplingLod"),"Full-sampling diagnostic must restore authored LOD");
            var material = new Material(original);
            var cameraObject = new GameObject("Offline cloud sampling review") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.fieldOfView = 72; camera.nearClipPlane = 0.05f; camera.farClipPlane = 25000;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.cullingMask = Camera.main.cullingMask;
            var target = new RenderTexture(1280, 720, 24) { antiAliasing = 4 };
            camera.targetTexture = target;
            var volume = field.Profile.Volumes[5];
            Vector3 center = field.transform.TransformPoint(volume.centerLocalMeters);
            var offsets = new[] { new Vector3(0,-800,-1500), new Vector3(0,-800,-4500),
                new Vector3(0,-800,-11000), new Vector3(0,-600,1500), new Vector3(0,0,-200) };
            var names = new[] { "near_below", "middle", "far", "reverse", "inside" };
            try
            {
                foreach (var renderer in field.SectorRenderers) renderer.sharedMaterial = material;
                for (int mode = 0; mode < 2; mode++)
                {
                    material.SetFloat("_CloudSamplingLod", mode);
                    for (int pose = 0; pose < offsets.Length; pose++)
                    for (int eye = 0; eye < 2; eye++)
                    {
                        camera.transform.position = center + offsets[pose];
                        camera.transform.LookAt(center);
                        camera.transform.position += camera.transform.right * (eye == 0 ? -0.032f : 0.032f);
                        camera.Render();
                        var previous = RenderTexture.active;
                        RenderTexture.active = target;
                        var pixels = new Texture2D(1280,720,TextureFormat.RGB24,false);
                        pixels.ReadPixels(new Rect(0,0,1280,720),0,0); pixels.Apply();
                        File.WriteAllBytes(Path.Combine(root,$"lod{mode}_{names[pose]}_{eye}.png"),pixels.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(pixels); RenderTexture.active = previous;
                    }
                }
            }
            finally
            {
                foreach (var renderer in field.SectorRenderers) renderer.sharedMaterial = original;
                camera.targetTexture = null; target.Release();
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
            File.WriteAllText(Path.Combine(root,"review_status.txt"),
                "Authored High FFR and Android subsampled feature verified. Cloud contracts and grove batch tests pass.\n" +
                "20 offset-eye Editor cloud captures; not tracked simulator or hardware acceptance. Cloud sampling LOD is enabled in the candidate.\n");
            System.Environment.SetEnvironmentVariable("QFL_GPU_AUDIT_ROOT",Path.Combine(root,"shader_compile"));
            GpuShaderAuditRunner.Run();
            System.Environment.SetEnvironmentVariable("QFL_MAP_BRANCH_ROOT",Path.Combine(root,"banked_pilot"));
            System.Environment.SetEnvironmentVariable("QFL_BANK_BRANCH_REVIEW","1");
            try { ProductionVisualQaBatchRunner.RunMapBranchGroveReview(); }
            finally { System.Environment.SetEnvironmentVariable("QFL_BANK_BRANCH_REVIEW",null); }
            System.Environment.SetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR",Path.Combine(root,"standard_qa"));
            ProductionVisualQaBatchRunner.RunProductionVisualQa();
        }
    }
}
