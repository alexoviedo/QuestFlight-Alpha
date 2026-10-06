using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace QuestFlightLab.Environment.Editor.Tests
{
    /// <summary>
    /// Focused Gate-3 diagnostic only. It opens the authored product scene and
    /// renders the exact Gate-1 schedule without saving or changing the scene.
    /// </summary>
    public static class TrafficV2Gate3VisualQaBatchRunner
    {
        private const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        private const int Width = 1280;
        private const int Height = 720;
        private const float FirstTime = 8f;
        private const float SecondTime = 10f;

        public static void Run()
        {
            string output = System.Environment.GetEnvironmentVariable("QFL_TRAFFIC_V2_GATE3_DIR");
            if (string.IsNullOrWhiteSpace(output))
            {
                throw new InvalidOperationException("QFL_TRAFFIC_V2_GATE3_DIR is required.");
            }
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);

            Gate3Report report = new Gate3Report
            {
                schema = "questflight.traffic-v2.gate3-focused-visual-qa.v1",
                status = "RUNNING",
                unityVersion = Application.unityVersion,
                scenePath = ScenePath,
                outputDirectory = output,
                width = Width,
                height = Height,
                manifestSha256 = KbduTrafficV2Runtime.ExpectedManifestSha256,
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

                AreaDefinition[] areas =
                {
                    new AreaDefinition("airport", 0),
                    new AreaDefinition("divided_highway", 4),
                    new AreaDefinition("residential", 8)
                };
                foreach (AreaDefinition area in areas)
                {
                    CaptureArea(scene, runtime, output, area, report);
                }

                report.status = report.areas.Count == 3 &&
                    report.areas.All(area =>
                        area.beforeBytes > 0 &&
                        area.afterBytes > 0 &&
                        area.meanAbsoluteRgbDifference > 0.000001f)
                    ? "PASS_FOCUSED_CAPTURE_REQUIRES_HUMAN_INSPECTION"
                    : "FAIL_FOCUSED_CAPTURE";
            }
            catch (Exception exception)
            {
                report.status = "FAIL_FOCUSED_CAPTURE";
                report.errors.Add(exception.ToString());
            }

            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(output, "traffic_v2_gate3_visual_qa.json"), json);
            File.WriteAllText(
                Path.Combine(output, "traffic_v2_gate3_visual_qa.md"),
                BuildMarkdown(report));
            Debug.Log(
                $"[QuestFlight][TrafficV2][Gate3] status={report.status}; " +
                $"areas={report.areas.Count}; output={output}");
            if (report.status.StartsWith("FAIL", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Traffic V2 Gate-3 focused capture failed. See " + output);
            }
        }

        private static void CaptureArea(
            Scene scene,
            KbduTrafficV2Runtime runtime,
            string output,
            AreaDefinition area,
            Gate3Report report)
        {
            if (!runtime.TryEvaluateTrajectory(
                    area.trajectoryIndex,
                    FirstTime,
                    out Vector3 before,
                    out Quaternion beforeRotation) ||
                !runtime.TryEvaluateTrajectory(
                    area.trajectoryIndex,
                    SecondTime,
                    out Vector3 after,
                    out _))
            {
                throw new InvalidOperationException(
                    "Could not evaluate Gate-1 trajectory for " + area.id);
            }

            Vector3 travel = Vector3.ProjectOnPlane(after - before, Vector3.up);
            Vector3 forward = travel.sqrMagnitude > 0.01f
                ? travel.normalized
                : Vector3.ProjectOnPlane(beforeRotation * Vector3.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 midpoint = Vector3.Lerp(before, after, 0.5f);
            Vector3 cameraPosition = midpoint - forward * 8f + right * 24f + Vector3.up * 10f;
            Quaternion cameraRotation = Quaternion.LookRotation(
                midpoint + Vector3.up * 0.8f - cameraPosition,
                Vector3.up);

            CapturedImage first = Capture(
                scene,
                runtime,
                output,
                area.id + "_t008",
                FirstTime,
                cameraPosition,
                cameraRotation);
            CapturedImage second = Capture(
                scene,
                runtime,
                output,
                area.id + "_t010",
                SecondTime,
                cameraPosition,
                cameraRotation);

            report.areas.Add(new AreaResult
            {
                id = area.id,
                trajectoryIndex = area.trajectoryIndex,
                firstTimeSeconds = FirstTime,
                secondTimeSeconds = SecondTime,
                firstVehiclePosition = before,
                secondVehiclePosition = after,
                displacementMeters = Vector3.Distance(before, after),
                cameraPosition = cameraPosition,
                beforePath = first.path,
                afterPath = second.path,
                beforeBytes = first.bytes,
                afterBytes = second.bytes,
                beforeSha256 = first.sha256,
                afterSha256 = second.sha256,
                meanAbsoluteRgbDifference = MeanAbsoluteRgbDifference(
                    first.pixels,
                    second.pixels)
            });
        }

        private static CapturedImage Capture(
            Scene scene,
            KbduTrafficV2Runtime runtime,
            string output,
            string id,
            float scheduleTime,
            Vector3 position,
            Quaternion rotation)
        {
            SetPrivateField(runtime, "scheduleClockSeconds", scheduleTime);
            SetPrivateField(runtime, "lastPreparedFrame", -1);
            SetPrivateField(runtime, "lastPreparedCamera", null);

            GameObject cameraObject = new GameObject("Traffic V2 Gate3 QA Camera")
            {
                hideFlags = HideFlags.DontSave
            };
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 24000f;
            camera.fieldOfView = 58f;
            camera.aspect = Width / (float)Height;
            camera.clearFlags = RenderSettings.skybox != null
                ? CameraClearFlags.Skybox
                : CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.68f, 0.82f);
            camera.enabled = false;

            RenderTexture render = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                hideFlags = HideFlags.DontSave
            };
            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGB24, false, false)
            {
                hideFlags = HideFlags.DontSave
            };
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = render;
                QuestFlight.Rendering.ProductRenderCapture.Render(camera);
                RenderTexture.active = render;
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                texture.Apply(false, false);
                byte[] png = texture.EncodeToPNG();
                string path = Path.Combine(output, id + ".png");
                File.WriteAllBytes(path, png);
                return new CapturedImage
                {
                    path = path,
                    bytes = png.LongLength,
                    sha256 = Sha256(png),
                    pixels = texture.GetPixels32()
                };
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
            FieldInfo field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().FullName, name);
            }
            field.SetValue(target, value);
        }

        private static float MeanAbsoluteRgbDifference(Color32[] first, Color32[] second)
        {
            if (first == null || second == null || first.Length != second.Length)
            {
                return 0f;
            }
            double total = 0d;
            for (int index = 0; index < first.Length; index++)
            {
                total += Math.Abs(first[index].r - second[index].r);
                total += Math.Abs(first[index].g - second[index].g);
                total += Math.Abs(first[index].b - second[index].b);
            }
            return (float)(total / (first.Length * 3d * 255d));
        }

        private static string BuildMarkdown(Gate3Report report)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("# Traffic V2 Gate 3 Focused Visual QA");
            text.AppendLine();
            text.AppendLine("Status: **" + report.status + "**");
            text.AppendLine();
            text.AppendLine("This diagnostic requires human inspection and is not headset or Quest-performance evidence.");
            text.AppendLine();
            text.AppendLine("| Area | Displacement | RGB difference | Before | After |");
            text.AppendLine("|---|---:|---:|---|---|");
            foreach (AreaResult area in report.areas)
            {
                text.AppendLine(
                    $"| {area.id} | {area.displacementMeters:0.000} m | " +
                    $"{area.meanAbsoluteRgbDifference:0.000000} | " +
                    $"`{Path.GetFileName(area.beforePath)}` | `{Path.GetFileName(area.afterPath)}` |");
            }
            foreach (string error in report.errors)
            {
                text.AppendLine();
                text.AppendLine("- ERROR: " + error.Replace('\n', ' '));
            }
            return text.ToString();
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
            }
        }

        private readonly struct AreaDefinition
        {
            public readonly string id;
            public readonly int trajectoryIndex;

            public AreaDefinition(string id, int trajectoryIndex)
            {
                this.id = id;
                this.trajectoryIndex = trajectoryIndex;
            }
        }

        private sealed class CapturedImage
        {
            public string path;
            public long bytes;
            public string sha256;
            public Color32[] pixels;
        }

        [Serializable]
        private sealed class Gate3Report
        {
            public string schema;
            public string status;
            public string generatedUtc;
            public string unityVersion;
            public string scenePath;
            public string outputDirectory;
            public int width;
            public int height;
            public string manifestSha256;
            public string contract;
            public List<AreaResult> areas = new List<AreaResult>();
            public List<string> errors = new List<string>();
        }

        [Serializable]
        private sealed class AreaResult
        {
            public string id;
            public int trajectoryIndex;
            public float firstTimeSeconds;
            public float secondTimeSeconds;
            public Vector3 firstVehiclePosition;
            public Vector3 secondVehiclePosition;
            public float displacementMeters;
            public Vector3 cameraPosition;
            public string beforePath;
            public string afterPath;
            public long beforeBytes;
            public long afterBytes;
            public string beforeSha256;
            public string afterSha256;
            public float meanAbsoluteRgbDifference;
        }
    }
}
