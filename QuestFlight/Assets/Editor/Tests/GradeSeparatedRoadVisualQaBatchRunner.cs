using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace QuestFlightLab.Editor.Tests
{
    public static class GradeSeparatedRoadVisualQaBatchRunner
    {
        private const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        private const string OutputVariable = "QFL_GRADE_SEPARATED_VISUAL_QA_DIR";
        private const int Width = 1920;
        private const int Height = 1080;

        [Serializable] private sealed class Manifest { public Corridor[] corridors; }
        [Serializable] private sealed class Corridor
        {
            public string corridor_id;
            public string direction_mode;
            public float headway_seconds;
            public int lane_count;
            public float length_meters;
            public float phase_seconds;
            public float[] points_xz_meters;
            public string road_class;
            public float speed_mps;
        }
        [Serializable] private sealed class Report
        {
            public string schema = "questflight.grade-separated-road-visual-qa.v1";
            public string generatedUtc;
            public int width = Width;
            public int height = Height;
            public string scenePath = ScenePath;
            public List<CaptureRecord> captures = new List<CaptureRecord>();
            public List<SequenceRecord> sequences = new List<SequenceRecord>();
        }
        [Serializable] private sealed class CaptureRecord
        {
            public string id;
            public string mode;
            public Vector3 cameraPosition;
            public Vector3 targetPosition;
            public float scheduleSeconds;
            public string path;
            public long bytes;
        }
        [Serializable] private sealed class SequenceRecord
        {
            public string id;
            public string routeId;
            public int archetype;
            public int direction;
            public float targetScheduleSeconds;
            public List<string> baselineFrames = new List<string>();
            public List<string> candidateFrames = new List<string>();
        }
        private readonly struct View
        {
            public readonly string id;
            public readonly Vector3 camera;
            public readonly Vector3 target;
            public readonly float fieldOfView;
            public View(string id, Vector3 camera, Vector3 target, float fieldOfView)
            {
                this.id = id;
                this.camera = camera;
                this.target = target;
                this.fieldOfView = fieldOfView;
            }
        }
        private readonly struct ScheduledVehicle
        {
            public readonly string routeId;
            public readonly int corridorIndex;
            public readonly int direction;
            public readonly int archetype;
            public readonly float schedule;
            public ScheduledVehicle(string routeId, int corridorIndex, int direction, int archetype, float schedule)
            {
                this.routeId = routeId;
                this.corridorIndex = corridorIndex;
                this.direction = direction;
                this.archetype = archetype;
                this.schedule = schedule;
            }
        }

        public static void RunBatch()
        {
            string output = System.Environment.GetEnvironmentVariable(OutputVariable);
            if (string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException(OutputVariable + " is required.");
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Camera mainCamera = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                .Single(camera => camera.name == "Main Camera" && camera.CompareTag("MainCamera"));
            KbduTrafficV2Runtime runtime = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<KbduTrafficV2Runtime>(true)).Single();
            MeshRenderer[] bridgeRenderers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Single(transform => transform.name == "KBDU_GradeSeparatedRoadsV1")
                .GetComponentsInChildren<MeshRenderer>(true);
            KbduGradeSeparatedRoadAuthorityV1 authority =
                new KbduGradeSeparatedRoadAuthorityV1(runtime.GradeSeparatedRoadAuthority);
            Manifest manifest = JsonUtility.FromJson<Manifest>(runtime.TrajectoryManifest.text);
            KbduGradeSeparatedRoadAuthorityV1.Profile first = authority.Data.profiles.Single(
                profile => profile.span_id.Contains("D-16-DL"));
            KbduGradeSeparatedRoadAuthorityV1.Profile second = authority.Data.profiles.First(
                profile => profile.span_id.Contains("D-16-DF"));
            Vector3 firstCenter = ProfileCenter(first);
            Vector3 secondCenter = ProfileCenter(second);
            ScheduledVehicle sedan = FindScheduledVehicle(manifest, first, firstCenter, 0);
            ScheduledVehicle truck = FindScheduledVehicle(manifest, second, secondCenter, 5);

            Report report = new Report
            {
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };
            GameObject cameraObject = new GameObject("Grade-Separated Road Visual QA Camera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(mainCamera);
                camera.enabled = false;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.aspect = Width / (float)Height;
                View[] views =
                {
                    new View("01_obvious_47th_overpass", firstCenter + new Vector3(105f, 40f, -125f), firstCenter, 48f),
                    new View("02_foothills_over_diagonal", secondCenter + new Vector3(120f, 38f, -115f), secondCenter, 48f),
                    new View("03_overhead_upper_lower_alignment", firstCenter + new Vector3(0f, 210f, 0.1f), firstCenter, 44f),
                    new View("04_low_oblique_clearance", firstCenter + new Vector3(90f, 8f, 4f), firstCenter + Vector3.down * 1.5f, 42f),
                    new View("07_lower_and_upper_traffic", secondCenter + new Vector3(95f, 24f, 80f), secondCenter, 48f),
                    new View("08_ordinary_graded_road_contact", new Vector3(-80f, 22f, -720f), new Vector3(-30f, 2f, -650f), 42f),
                    new View("09_unaffected_at_grade_crossing", new Vector3(250f, 75f, -850f), new Vector3(70f, 0f, -690f), 48f),
                    new View("10_unaffected_ordinary_route", new Vector3(210f, 28f, 2850f), new Vector3(130f, 3f, 2940f), 42f),
                    new View("11_cdot_e16_hw_unmodeled_ramp", new Vector3(-410f, 72f, -6145f), new Vector3(-531f, 4f, -6021f), 46f),
                    new View("12_cdot_e16_pq_unmodeled_emma", new Vector3(10645f, 78f, -5140f), new Vector3(10529f, 4f, -5020f), 46f),
                    new View("13_cdot_e16_zj_unmodeled_ramps", new Vector3(5160f, 78f, -9200f), new Vector3(5036f, 4f, -9083f), 46f)
                };
                foreach (string mode in new[] { "baseline", "candidate" })
                {
                    bool baseline = mode == "baseline";
                    SetMode(runtime, bridgeRenderers, baseline);
                    foreach (View view in views)
                        Capture(camera, runtime, view, sedan.schedule, output, mode, report);
                }
                report.sequences.Add(CaptureSequence(
                    "05_sedan_approach_span_departure", sedan,
                    firstCenter + new Vector3(62f, 18f, -72f), firstCenter,
                    camera, runtime, bridgeRenderers, output));
                report.sequences.Add(CaptureSequence(
                    "06_longest_truck_approach_span_departure", truck,
                    secondCenter + new Vector3(68f, 19f, -72f), secondCenter,
                    camera, runtime, bridgeRenderers, output));
            }
            finally
            {
                SetMode(runtime, bridgeRenderers, false);
                Object.DestroyImmediate(cameraObject);
            }
            File.WriteAllText(Path.Combine(output, "grade_separated_road_visual_qa.json"),
                JsonUtility.ToJson(report, true));
            Debug.Log("[QuestFlight][GradeSeparatedRoadVisualQA] " + output);
            EditorApplication.Exit(0);
        }

        private static SequenceRecord CaptureSequence(
            string id,
            ScheduledVehicle vehicle,
            Vector3 cameraPosition,
            Vector3 target,
            Camera camera,
            KbduTrafficV2Runtime runtime,
            MeshRenderer[] renderers,
            string output)
        {
            SequenceRecord result = new SequenceRecord
            {
                id = id,
                routeId = vehicle.routeId,
                archetype = vehicle.archetype,
                direction = vehicle.direction,
                targetScheduleSeconds = vehicle.schedule
            };
            camera.transform.SetPositionAndRotation(
                cameraPosition, Quaternion.LookRotation(target - cameraPosition, Vector3.up));
            camera.fieldOfView = 40f;
            float[] offsets = { -4f, -2f, 0f, 2f, 4f };
            foreach (string mode in new[] { "baseline", "candidate" })
            {
                bool baseline = mode == "baseline";
                SetMode(runtime, renderers, baseline);
                string directory = Path.Combine(output, "sequences", mode, id);
                Directory.CreateDirectory(directory);
                for (int index = 0; index < offsets.Length; index++)
                {
                    runtime.SetDiagnosticScheduleClock(vehicle.schedule + offsets[index]);
                    string path = Path.Combine(directory, $"frame_{index:00}.png");
                    SaveRender(camera, path);
                    string relative = Relative(output, path);
                    if (baseline) result.baselineFrames.Add(relative);
                    else result.candidateFrames.Add(relative);
                }
            }
            return result;
        }

        private static void Capture(
            Camera camera,
            KbduTrafficV2Runtime runtime,
            View view,
            float schedule,
            string output,
            string mode,
            Report report)
        {
            camera.transform.SetPositionAndRotation(
                view.camera, Quaternion.LookRotation(view.target - view.camera, Vector3.up));
            camera.fieldOfView = view.fieldOfView;
            runtime.SetDiagnosticScheduleClock(schedule);
            string directory = Path.Combine(output, "stills", mode);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, view.id + ".png");
            SaveRender(camera, path);
            report.captures.Add(new CaptureRecord
            {
                id = view.id,
                mode = mode,
                cameraPosition = view.camera,
                targetPosition = view.target,
                scheduleSeconds = schedule,
                path = Relative(output, path),
                bytes = new FileInfo(path).Length
            });
        }

        private static void SetMode(
            KbduTrafficV2Runtime runtime,
            MeshRenderer[] renderers,
            bool baseline)
        {
            runtime.SetGradeSeparatedRoadDiagnosticBypass(baseline);
            foreach (MeshRenderer renderer in renderers) renderer.enabled = !baseline;
        }

        private static void SaveRender(Camera camera, string path)
        {
            RenderTexture target = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            RenderTexture prior = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            Texture2D image = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply(false, false);
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
            camera.targetTexture = null;
            RenderTexture.active = prior;
            RenderTexture.ReleaseTemporary(target);
        }

        private static Vector3 ProfileCenter(KbduGradeSeparatedRoadAuthorityV1.Profile profile)
        {
            float distance = (profile.bridge_interval_meters[0] + profile.bridge_interval_meters[1]) * 0.5f;
            if (!KbduGradeSeparatedRoadAuthorityV1.TryEvaluateProfile(
                    profile, distance, out Vector3 position, out _))
                throw new InvalidOperationException("Could not evaluate " + profile.span_id);
            return position;
        }

        private static ScheduledVehicle FindScheduledVehicle(
            Manifest manifest,
            KbduGradeSeparatedRoadAuthorityV1.Profile profile,
            Vector3 target,
            int requestedArchetype)
        {
            HashSet<string> routeIds = new HashSet<string>(profile.upper_route_ids, StringComparer.Ordinal);
            int bestIndex = -1;
            float bestDistance = float.PositiveInfinity;
            float bestRouteDistance = 0f;
            for (int corridorIndex = 0; corridorIndex < manifest.corridors.Length; corridorIndex++)
            {
                Corridor corridor = manifest.corridors[corridorIndex];
                if (!routeIds.Contains(corridor.corridor_id)) continue;
                float cumulative = 0f;
                for (int point = 0; point + 3 < corridor.points_xz_meters.Length; point += 2)
                {
                    Vector2 a = new Vector2(corridor.points_xz_meters[point], corridor.points_xz_meters[point + 1]);
                    Vector2 b = new Vector2(corridor.points_xz_meters[point + 2], corridor.points_xz_meters[point + 3]);
                    Vector2 ab = b - a;
                    float ratio = ab.sqrMagnitude > 0.0001f
                        ? Mathf.Clamp01(Vector2.Dot(new Vector2(target.x, target.z) - a, ab) / ab.sqrMagnitude)
                        : 0f;
                    float distance = Vector2.Distance(new Vector2(target.x, target.z), a + ab * ratio);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestIndex = corridorIndex;
                        bestRouteDistance = cumulative + ab.magnitude * ratio;
                    }
                    cumulative += ab.magnitude;
                }
            }
            if (bestIndex < 0 || bestDistance > 20f)
                throw new InvalidOperationException("No traffic corridor reaches " + profile.span_id);
            Corridor selected = manifest.corridors[bestIndex];
            int direction = selected.direction_mode == "REVERSE" ? 1 : 0;
            float travelDistance = direction == 0 ? bestRouteDistance : selected.length_meters - bestRouteDistance;
            double vehicleAge = travelDistance / selected.speed_mps;
            int streamIndex = (int)Math.Floor(vehicleAge / selected.headway_seconds);
            float age = (float)(vehicleAge - streamIndex * selected.headway_seconds);
            long departure = 0;
            while (departure < 1000 && SelectArchetype(selected, departure, direction) != requestedArchetype)
                departure++;
            if (departure >= 1000)
                throw new InvalidOperationException("Could not schedule requested archetype.");
            float schedule = selected.phase_seconds +
                (departure + streamIndex) * selected.headway_seconds + age;
            return new ScheduledVehicle(selected.corridor_id, bestIndex, direction, requestedArchetype, schedule);
        }

        private static int SelectArchetype(Corridor corridor, long departure, int direction)
        {
            int value = PositiveModulo(
                StableHash(corridor.corridor_id) + (int)(departure & 0x7fffffff) * 17 + direction * 71,
                100);
            int heavyPercent = corridor.road_class == "motorway" || corridor.road_class == "trunk"
                ? 12 : corridor.road_class == "primary" ? 6 : 2;
            if (value < heavyPercent) return 5;
            switch (value % 4)
            {
                case 0: return 0;
                case 1: return 2;
                case 2: return 3;
                default: return 0;
            }
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char character in value)
                {
                    hash ^= character;
                    hash *= 16777619u;
                }
                return (int)(hash & 0x7fffffff);
            }
        }

        private static int PositiveModulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private static string Relative(string root, string path) =>
            path.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length + 1)
                .Replace('\\', '/');
    }
}
