using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace QuestFlightLab.Environment.Editor.Tests
{
    /// <summary>
    /// Focused full-map candidate QA. It uses only a transient diagnostic camera,
    /// never writes the OpenXR Main Camera, never saves the scene, and records the
    /// actual instanced near/far counts prepared for each image.
    /// </summary>
    public static class TrafficV2FullMapVisualQaBatchRunner
    {
        private const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        private const string OutputVariable = "QFL_TRAFFIC_V2_FULL_MAP_QA_DIR";
        private const int Width = 1280;
        private const int Height = 720;

        private static readonly View[] Views =
        {
            new View("airport_boulevard", new Vector3(-30f, 120f, -650f),
                new Vector3(32.5f, 342.5f, 0f), 52f),
            new View("diagonal_highway", new Vector3(-312f, 8.2f, 1578f),
                new Vector3(16.2f, 326.8f, 0f), 40f),
            new View("downtown", new Vector3(-3400f, 550f, -2300f),
                new Vector3(44.3f, 225f, 0f), 55f),
            new View("residential", new Vector3(-2000f, 350f, -1000f),
                new Vector3(26.3f, 225f, 0f), 55f),
            new View("map_periphery", new Vector3(10500f, 900f, 10000f),
                new Vector3(3.6f, 226.4f, 0f), 58f)
        };

        private static readonly float[] Times = { 0f, 60f, 120f };

        public static void Run()
        {
            bool temporal = System.Environment.GetEnvironmentVariable("QFL_TRAFFIC_TEMPORAL_QA") == "1";
            View[] views = temporal ? Views.Take(2).ToArray() : Views;
            float[] times = temporal ? Enumerable.Range(0, 31).Select(i => 58f + i / 6f).ToArray() : Times;
            string output = System.Environment.GetEnvironmentVariable(OutputVariable);
            if (string.IsNullOrWhiteSpace(output))
            {
                throw new InvalidOperationException(OutputVariable + " is required.");
            }
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            Report report = new Report
            {
                schema = "questflight.traffic-v2.full-map-focused-visual-qa.v1",
                status = "RUNNING",
                manifestSha256 = KbduTrafficV2Runtime.ExpectedManifestSha256,
                scenePath = ScenePath,
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };
            try
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                KbduTrafficV2Runtime runtime = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<KbduTrafficV2Runtime>(true))
                    .Single();
                if (!runtime.TryValidateContract(out string contract))
                {
                    throw new InvalidOperationException("Traffic V2 contract failed: " + contract);
                }
                float endpointStart = 60f;
                if (temporal)
                {
                    View endpoint = FindUnconnectedEndpointView(runtime, out endpointStart);
                    views = views.Concat(new[] { endpoint }).ToArray();
                }
                bool baseline = System.Environment.GetEnvironmentVariable("QFL_TRAFFIC_CONTINUITY_BASELINE") == "1";
                runtime.ConfigureContinuityBaselineForVisualQa(baseline);
                report.scope = baseline ? "Editor emulation of accepted traffic scheduling/visibility; not an APK capture" : "Current candidate Editor traffic";
                report.contract = contract;
                foreach (Camera existing in scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Camera>(true)))
                {
                    existing.enabled = false;
                }
                foreach (AudioListener listener in scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<AudioListener>(true)))
                {
                    listener.enabled = false;
                }
                foreach (View view in views)
                {
                    foreach (float time in view.id == "endpoint_close"
                        ? Enumerable.Range(0, 31).Select(i => endpointStart - 0.5f + i / 6f).ToArray() : times)
                    {
                        Capture(scene, runtime, output, view, time, report);
                    }
                }
                bool allPrepared = report.captures.Count == views.Length * times.Length &&
                    report.captures.All(value => value.nearCount + value.farCount > 0 &&
                        value.droppedCount == 0 && value.bytes > 0);
                bool airportDensity = report.captures
                    .Where(value => value.viewId == "airport_boulevard")
                    .All(value => value.nearCount >= 5);
                report.status = allPrepared && airportDensity
                    ? "PASS_FOCUSED_CAPTURE_REQUIRES_HUMAN_INSPECTION"
                    : "FAIL_FOCUSED_CAPTURE";
            }
            catch (Exception exception)
            {
                report.status = "FAIL_FOCUSED_CAPTURE";
                report.errors.Add(exception.ToString());
            }
            File.WriteAllText(Path.Combine(output, "traffic_v2_full_map_visual_qa.json"),
                JsonUtility.ToJson(report, true));
            if (report.status.StartsWith("FAIL", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Traffic V2 full-map focused QA failed. See " + output);
            }
        }

        private static View FindUnconnectedEndpointView(KbduTrafficV2Runtime runtime, out float start)
        {
            object manifest = typeof(KbduTrafficV2Runtime).GetField("parsedManifest", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runtime);
            Array corridors = (Array)manifest.GetType().GetField("corridors").GetValue(manifest);
            float bestDistance = float.MaxValue;
            View best = null;
            start = 0;
            for (int i = 0; i < corridors.Length; i++)
            {
                object c = corridors.GetValue(i);
                var type = c.GetType();
                float speed = (float)type.GetField("speed_mps").GetValue(c);
                float lifetime = (float)type.GetField("length_meters").GetValue(c) / speed;
                if (lifetime < 8) continue;
                object stream = ((Array)type.GetField("streams").GetValue(c)).GetValue(0);
                double offset = (double)stream.GetType().GetField("ageOffset").GetValue(stream);
                if (offset != 0 || Math.Abs((double)stream.GetType().GetField("lifetime").GetValue(stream) - lifetime) > 0.01) continue;
                float phase = (float)type.GetField("phase_seconds").GetValue(c);
                if (!runtime.TryEvaluateVehicleForVisualQa(i, 0, 0, phase + 0.01, out Vector3 point, out Quaternion rotation, out _, out _)) continue;
                float distance = new Vector2(point.x, point.z).sqrMagnitude;
                if (distance >= bestDistance) continue;
                Vector3 forward = rotation * Vector3.forward;
                Vector3 camera = point - forward * 60 + rotation * Vector3.right * 45 + Vector3.up * 35;
                best = new View("endpoint_close", camera, Quaternion.LookRotation(point + forward * 15 - camera).eulerAngles, 50);
                bestDistance = distance;
                start = phase;
            }
            return best ?? throw new InvalidOperationException("No near-airport endpoint diagnostic found");
        }

        private static void Capture(
            Scene scene,
            KbduTrafficV2Runtime runtime,
            string output,
            View view,
            float time,
            Report report)
        {
            SetPrivateField(runtime, "scheduleClockSeconds", time);
            SetPrivateField(runtime, "lastPreparedFrame", -1);
            SetPrivateField(runtime, "lastPreparedCamera", null);
            GameObject cameraObject = new GameObject("Traffic V2 Full Map QA Camera")
            {
                hideFlags = HideFlags.DontSave
            };
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(view.position, Quaternion.Euler(view.euler));
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 24000f;
            camera.fieldOfView = view.fieldOfView;
            camera.aspect = Width / (float)Height;
            camera.clearFlags = RenderSettings.skybox != null
                ? CameraClearFlags.Skybox
                : CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.68f, 0.82f);
            camera.enabled = false;
            RenderTexture render = new RenderTexture(Width, Height, 24,
                RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                hideFlags = HideFlags.DontSave
            };
            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGB24,
                false, false)
            {
                hideFlags = HideFlags.DontSave
            };
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = render;
                camera.Render();
                RenderTexture.active = render;
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                texture.Apply(false, false);
                byte[] png = texture.EncodeToPNG();
                string id = view.id + "_t" + time.ToString("000.000", CultureInfo.InvariantCulture);
                string path = Path.Combine(output, id + ".png");
                File.WriteAllBytes(path, png);
                report.captures.Add(new CaptureResult
                {
                    viewId = view.id,
                    scheduleTimeSeconds = time,
                    nearCount = runtime.PreparedNearInstanceCount,
                    farCount = runtime.PreparedFarInstanceCount,
                    droppedCount = runtime.DroppedInstanceCount,
                    cameraPosition = view.position,
                    cameraEuler = view.euler,
                    fieldOfView = view.fieldOfView,
                    path = path,
                    bytes = png.LongLength,
                    sha256 = Sha256(png)
                });
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(render);
                Object.DestroyImmediate(cameraObject);
            }
        }

        private static void SetPrivateField(Object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().FullName, name);
            }
            field.SetValue(target, value);
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(bytes)
                    .Select(value => value.ToString("x2")));
            }
        }

        private sealed class View
        {
            public readonly string id;
            public readonly Vector3 position;
            public readonly Vector3 euler;
            public readonly float fieldOfView;

            public View(string id, Vector3 position, Vector3 euler, float fieldOfView)
            {
                this.id = id;
                this.position = position;
                this.euler = euler;
                this.fieldOfView = fieldOfView;
            }
        }

        [Serializable]
        private sealed class Report
        {
            public string schema;
            public string scope;
            public string status;
            public string manifestSha256;
            public string scenePath;
            public string generatedUtc;
            public string contract;
            public List<CaptureResult> captures = new List<CaptureResult>();
            public List<string> errors = new List<string>();
        }

        [Serializable]
        private sealed class CaptureResult
        {
            public string viewId;
            public float scheduleTimeSeconds;
            public int nearCount;
            public int farCount;
            public int droppedCount;
            public Vector3 cameraPosition, cameraEuler;
            public float fieldOfView;
            public string path;
            public long bytes;
            public string sha256;
        }
    }
}
