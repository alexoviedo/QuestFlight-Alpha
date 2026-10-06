using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QuestFlightLab.Editor.Tests
{
    // Isolated Editor material review only; not simulator or Quest acceptance.
    public static class OfflineRemasterTrafficReview
    {
        [Serializable] private sealed class AlphaData { public int[] alpha; }

        public static void Run()
        {
            string root = System.Environment.GetEnvironmentVariable("QFL_REMASTER_ROOT");
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("QFL_REMASTER_ROOT required");
            string output = Path.Combine(root, "traffic_review");
            Directory.CreateDirectory(output);
            string modelRoot = "Assets/Production/Environment/TrafficV1/Models";
            Material sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Production/Environment/TrafficV1/Materials/KBDU_TrafficVehicle.mat");
            if (sourceMaterial == null || ShaderUtil.ShaderHasError(sourceMaterial.shader))
                throw new InvalidOperationException("Traffic material/shader unavailable");
            Scene preview = EditorSceneManager.NewPreviewScene();
            int captures = 0;
            try
            {
                var cameraObject = new GameObject("OfflineRemasterReviewCamera");
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.scene = preview;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.24f, .25f, .26f);
                camera.fieldOfView = 45;
                camera.nearClipPlane = .05f;
                camera.farClipPlane = 200;
                var lightObject = new GameObject("OfflineReviewLight");
                SceneManager.MoveGameObjectToScene(lightObject, preview);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                light.transform.rotation = Quaternion.Euler(35, -30, 0);
                foreach (string path in Directory.GetFiles(modelRoot, "*.asset"))
                {
                    Mesh source = AssetDatabase.LoadAssetAtPath<Mesh>(path.Replace('\\', '/'));
                    if (source == null) throw new InvalidOperationException(path);
                    string name = Path.GetFileNameWithoutExtension(path);
                    AlphaData data = JsonUtility.FromJson<AlphaData>(File.ReadAllText(
                        Path.Combine(root, "traffic_ao", name + ".json")));
                    if (data.alpha.Length != source.vertexCount) throw new InvalidOperationException(name);
                    Mesh mesh = UnityEngine.Object.Instantiate(source);
                    Material material = new Material(sourceMaterial);
                    var vehicle = new GameObject(name);
                    SceneManager.MoveGameObjectToScene(vehicle, preview);
                    vehicle.AddComponent<MeshFilter>().sharedMesh = mesh;
                    vehicle.AddComponent<MeshRenderer>().sharedMaterial = material;
                    try
                    {
                        Color32[] colors = mesh.colors32;
                        for (int i = 0; i < colors.Length; i++)
                        {
                            if (colors[i].a < 216 || data.alpha[i] < 216 || data.alpha[i] > 255)
                                throw new InvalidOperationException("Occupied/invalid alpha " + name);
                            colors[i].a = (byte)data.alpha[i];
                        }
                        mesh.colors32 = colors;
                        material.SetColor("_BodyTint", new Color(.62f, .63f, .65f, 1));
                        foreach (float distance in new[] { 12f, 40f })
                        foreach (bool after in new[] { false, true })
                        foreach (int eye in new[] { -1, 1 })
                        {
                            for (int i = 0; i < colors.Length; i++)
                                colors[i].a = after ? (byte)data.alpha[i] : (byte)255;
                            mesh.colors32 = colors;
                            Vector3 center = mesh.bounds.center;
                            Vector3 direction = new Vector3(1, .6f, 1).normalized;
                            camera.transform.position = center + direction * distance;
                            camera.transform.LookAt(center);
                            camera.transform.position += camera.transform.right * (eye * .032f);
                            Capture(camera, Path.Combine(output,
                                $"{name}_{distance}_{(after ? "after" : "before")}_{(eye < 0 ? "left" : "right")}.png"));
                            captures++;
                        }
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(vehicle);
                        UnityEngine.Object.DestroyImmediate(mesh);
                        UnityEngine.Object.DestroyImmediate(material);
                    }
                }
                File.WriteAllText(Path.Combine(output, "result.json"),
                    "{\"captures\":" + captures + ",\"scope\":\"Editor isolated stereo material review; not simulator/device acceptance\"}");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static void Capture(Camera camera, string path)
        {
            var target = new RenderTexture(1024, 768, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(1024, 768, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                QuestFlight.Rendering.ProductRenderCapture.Render(camera);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1024, 768), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
