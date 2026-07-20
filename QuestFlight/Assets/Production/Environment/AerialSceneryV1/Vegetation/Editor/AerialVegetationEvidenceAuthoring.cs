using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace QuestFlightLab.Environment.Editor
{
    /// <summary>
    /// Captures deterministic, product-independent evidence of the two near
    /// vegetation meshes. Tier 2 intentionally has no geometry. This tool never
    /// edits or saves the product scene.
    /// </summary>
    public static class AerialVegetationEvidenceAuthoring
    {
        private const string DistributionPath =
            "Assets/Production/Environment/AerialSceneryV1/Vegetation/AerialVegetation_Distribution.json";
        private const string MaterialPath =
            "Assets/Production/Environment/AerialSceneryV1/Vegetation/Materials/AerialVegetation_AtlasCutout.mat";
        private const string MeshRoot =
            "Assets/Production/Environment/AerialSceneryV1/Vegetation/Generated/AerialVegetation_Tier";
        private const int Width = 1600;
        private const int Height = 900;

        [MenuItem("QuestFlight/Aerial Scenery V1/Capture Vegetation Evidence")]
        public static void CaptureBatch()
        {
            string outputDirectory = CommandLineValue("-aerialVegetationEvidenceDir");
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                outputDirectory = Path.GetFullPath(Path.Combine(
                    Application.dataPath,
                    "../../QuestFlight-product-artifacts/aerial_vegetation_evidence"));
            }
            Directory.CreateDirectory(outputDirectory);

            TextAsset distributionAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(DistributionPath);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (distributionAsset == null || material == null || material.shader == null)
            {
                throw new InvalidOperationException("Vegetation evidence source assets are missing.");
            }
            DistributionRecord distribution = JsonUtility.FromJson<DistributionRecord>(distributionAsset.text);
            if (distribution?.batches == null || distribution.batches.Length != 2)
            {
                throw new InvalidOperationException("Vegetation distribution must contain two near batches.");
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject meshObject = new GameObject("EVIDENCE_VegetationMesh");
            MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "EVIDENCE_NeutralGround";
            ground.transform.localScale = new Vector3(160f, 1f, 160f);
            UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());
            Material groundMaterial = new Material(Shader.Find("Standard"))
            {
                name = "EVIDENCE_NeutralGroundMaterial",
                color = new Color(0.39f, 0.43f, 0.27f, 1f)
            };
            groundMaterial.SetFloat("_Glossiness", 0.08f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            GameObject sunObject = new GameObject("EVIDENCE_Sun");
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1.0f, 0.94f, 0.84f, 1f);
            sun.intensity = 1.08f;
            sun.shadows = LightShadows.None;
            sunObject.transform.rotation = Quaternion.Euler(43f, -128f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.46f, 0.58f, 0.72f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.39f, 0.43f, 0.36f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.22f, 0.16f, 1f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;

            GameObject cameraObject = new GameObject("EVIDENCE_Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 52f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 6000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.48f, 0.62f, 0.76f, 1f);
            camera.allowHDR = false;
            camera.allowMSAA = true;

            List<CaptureRecord> captures = new List<CaptureRecord>();
            try
            {
                DistributionInstanceRecord tier0 = Nearest(distribution, 0);
                DistributionInstanceRecord tier1 = Nearest(distribution, 1);
                captures.Add(Capture(
                    outputDirectory, camera, ground, meshFilter, 0, "tier0_microgrove_oblique",
                    Target(tier0, 0.48f), new Vector3(-38f, 24f, -42f)));
                captures.Add(Capture(
                    outputDirectory, camera, ground, meshFilter, 1, "tier1_microgrove_oblique",
                    Target(tier1, 0.44f), new Vector3(-52f, 33f, -58f)));
                string reportPath = Path.Combine(outputDirectory, "aerial_vegetation_evidence.json");
                File.WriteAllText(reportPath, JsonUtility.ToJson(new EvidenceReport
                {
                    schema = "questflight.aerial-scenery-v1.vegetation-evidence.v1",
                    status = "DETERMINISTIC_SOURCE_REPRESENTATION_EVIDENCE_NOT_PRODUCT_VISUAL_ACCEPTANCE",
                    width = Width,
                    height = Height,
                    draw_count = 2,
                    material_count = 1,
                    selected_triangles = 12860,
                    tier2_geometry_draw_count = 0,
                    tier2_geometry_triangle_count = 0,
                    tier2_representation = "orthophoto_plus_lidar_canopy_normal_occlusion_terrain_shading",
                    captures = captures.ToArray(),
                }, true) + System.Environment.NewLine);
                Debug.Log("Vegetation evidence captured: " + reportPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(groundMaterial);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (scene.IsValid())
                {
                    Resources.UnloadUnusedAssets();
                }
            }
        }

        private static CaptureRecord Capture(
            string outputDirectory,
            Camera camera,
            GameObject ground,
            MeshFilter meshFilter,
            int tier,
            string name,
            Vector3 target,
            Vector3 cameraOffset,
            bool topDown = false)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshRoot + tier + "_Combined.asset");
            if (mesh == null)
            {
                throw new InvalidOperationException("Generated vegetation mesh is missing for tier " + tier + ".");
            }
            meshFilter.sharedMesh = mesh;
            ground.transform.position = new Vector3(target.x, target.y - 0.5f, target.z);
            Vector3 cameraPosition = target + cameraOffset;
            camera.transform.position = cameraPosition;
            camera.transform.rotation = Quaternion.LookRotation(
                target - cameraPosition,
                topDown ? Vector3.forward : Vector3.up);

            RenderTexture renderTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4,
                useMipMap = false,
                autoGenerateMips = false,
                name = "EVIDENCE_" + name,
            };
            Texture2D screenshot = new Texture2D(Width, Height, TextureFormat.RGB24, false, true);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                screenshot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                screenshot.Apply(false, false);
                string path = Path.Combine(outputDirectory, name + ".png");
                File.WriteAllBytes(path, screenshot.EncodeToPNG());
                return new CaptureRecord
                {
                    name = name,
                    tier = tier,
                    path = path.Replace('\\', '/'),
                    mesh_triangles = mesh.triangles.Length / 3,
                    camera_position = cameraPosition,
                    target_position = target,
                };
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(screenshot);
            }
        }

        private static Vector3 Target(DistributionInstanceRecord instance, float heightFraction)
        {
            return new Vector3(
                instance.x,
                instance.y + Mathf.Clamp(instance.mean_canopy_hag_m > 0f ? instance.mean_canopy_hag_m : 12f, 4f, 24f)
                    * heightFraction,
                instance.z);
        }

        private static DistributionInstanceRecord Nearest(DistributionRecord distribution, int tier)
        {
            return Instances(distribution, tier)
                .OrderBy(instance => instance.x * instance.x + instance.z * instance.z)
                .First();
        }

        private static DistributionInstanceRecord[] Instances(DistributionRecord distribution, int tier)
        {
            DistributionBatchRecord batch = distribution.batches.Single(record => record.tier == tier);
            return batch.instances ?? Array.Empty<DistributionInstanceRecord>();
        }

        private static string CommandLineValue(string key)
        {
            string[] arguments = System.Environment.GetCommandLineArgs();
            for (int index = 0; index + 1 < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], key, StringComparison.Ordinal))
                {
                    return arguments[index + 1];
                }
            }
            return null;
        }

        [Serializable]
        private sealed class DistributionRecord
        {
            public DistributionBatchRecord[] batches;
        }

        [Serializable]
        private sealed class DistributionBatchRecord
        {
            public int tier;
            public DistributionInstanceRecord[] instances;
        }

        [Serializable]
        private sealed class DistributionInstanceRecord
        {
            public float x;
            public float y;
            public float z;
            public float mean_canopy_hag_m;
        }

        [Serializable]
        private sealed class EvidenceReport
        {
            public string schema;
            public string status;
            public int width;
            public int height;
            public int draw_count;
            public int material_count;
            public int selected_triangles;
            public int tier2_geometry_draw_count;
            public int tier2_geometry_triangle_count;
            public string tier2_representation;
            public CaptureRecord[] captures;
        }

        [Serializable]
        private sealed class CaptureRecord
        {
            public string name;
            public int tier;
            public string path;
            public int mesh_triangles;
            public Vector3 camera_position;
            public Vector3 target_position;
        }
    }
}
