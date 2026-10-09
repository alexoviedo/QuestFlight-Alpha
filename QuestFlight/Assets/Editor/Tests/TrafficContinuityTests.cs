using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests
{
    public sealed class TrafficContinuityTests
    {
        private KbduTrafficV2Runtime traffic;
        private Array corridors;
        private static T Field<T>(object value, string name) => (T)value.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);

        [OneTimeSetUp] public void OpenAuthoredScene()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity", OpenSceneMode.Single);
            traffic = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<KbduTrafficV2Runtime>(true)).Single();
            Assert.That(traffic.TryValidateContract(out string report), Is.True, report);
            corridors = Field<Array>(Field<object>(traffic, "parsedManifest"), "corridors");
        }

        [Test] public void ExistingVehiclesKeepTheirLaneWhenANewVehicleDeparts()
        {
            int checkedVehicles = 0;
            for (int i = 0; i < corridors.Length; i++)
            {
                object c = corridors.GetValue(i);
                int lanes = Field<int>(c, "lane_count") / (Field<string>(c, "direction_mode") == "BIDIRECTIONAL" ? 2 : 1);
                float headway = Field<float>(c, "headway_seconds");
                if (lanes < 2 || Field<float>(c, "length_meters") / Field<float>(c, "speed_mps") <= headway + 0.1) continue;
                for (int direction = 0; direction < 2; direction++)
                {
                    double boundary = Field<double>(Field<Array>(c, "streams").GetValue(direction), "phase") + headway;
                    if (!traffic.TryEvaluateVehicleForVisualQa(i, direction, 0, boundary - 0.001, out Vector3 a, out _, out int laneA, out _)) continue;
                    Assert.That(traffic.TryEvaluateVehicleForVisualQa(i, direction, 0, boundary + 0.001, out Vector3 b, out _, out int laneB, out _), Is.True);
                    Assert.That(laneB, Is.EqualTo(laneA), "Lane changed at a new departure on corridor " + i);
                    Assert.That(Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z)),
                        Is.LessThan(0.3f), "Vehicle jumped laterally on corridor " + i);
                    checkedVehicles++;
                }
            }
            Assert.That(checkedVehicles, Is.GreaterThan(100));
            Debug.Log("Traffic continuity: " + checkedVehicles + " existing multilane vehicles checked across a departure boundary.");
        }

        [Test] public void JoinedRoadEndpointsPreserveVehicleIdentityAndFullVisibility()
        {
            var exits = new Dictionary<(int, int, double), (int corridor, int direction)>();
            for (int i = 0; i < corridors.Length; i++)
                for (int d = 0; d < 2; d++)
                {
                    object c = corridors.GetValue(i), s = Field<Array>(c, "streams").GetValue(d);
                    double duration = Field<float>(c, "length_meters") / (double)Field<float>(c, "speed_mps");
                    exits[(Field<int>(s, "hash"), Field<int>(s, "direction"), Math.Round(Field<double>(s, "phase") + duration, 6))] = (i, d);
                }
            int checkedJoins = 0;
            for (int i = 0; i < corridors.Length; i++)
                for (int d = 0; d < 2; d++)
                {
                    object c = corridors.GetValue(i), s = Field<Array>(c, "streams").GetValue(d);
                    if (Field<double>(s, "ageOffset") <= 0) continue;
                    double boundary = Field<double>(s, "phase");
                    Assert.That(exits.TryGetValue((Field<int>(s, "hash"), Field<int>(s, "direction"), Math.Round(boundary, 6)), out var from), Is.True);
                    Assert.That(traffic.TryEvaluateVehicleForVisualQa(from.corridor, from.direction, 0, boundary - 0.001, out Vector3 a, out _, out int laneA, out float alphaA), Is.True);
                    Assert.That(traffic.TryEvaluateVehicleForVisualQa(i, d, 0, boundary + 0.001, out Vector3 b, out _, out int laneB, out float alphaB), Is.True);
                    Assert.That(laneB, Is.EqualTo(laneA));
                    Assert.That(Vector3.Distance(a, b), Is.LessThan(0.8f), $"Discontinuous joined road {from.corridor} -> {i}");
                    Assert.That(Mathf.Abs(alphaA - alphaB), Is.LessThan(0.01f));
                    Assert.That(alphaA, Is.GreaterThan(0f));
                    checkedJoins++;
                }
            Assert.That(checkedJoins, Is.GreaterThan(100));
            Debug.Log("Traffic continuity: " + checkedJoins + " directed road joins preserve vehicles without an endpoint fade/reset.");
        }

        [Test] public void AuthoredSemiAxlesRemainContinuousAcrossThe4125To2630Join()
        {
            const int from = 4125, to = 2630, direction = 1;
            object target = corridors.GetValue(to);
            double boundary = Field<double>(Field<Array>(target, "streams").GetValue(direction), "phase");
            var select = typeof(KbduTrafficV2Runtime).GetMethod("SelectArchetype", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(select.Invoke(null, new[] { corridors.GetValue(from), (object)0L, direction }), Is.EqualTo(5));
            Assert.That(select.Invoke(null, new[] { target, (object)0L, direction }), Is.EqualTo(5));
            Assert.That(traffic.TryEvaluateVehicleForVisualQa(from, direction, 0, boundary - .001,
                out Vector3 a, out _, out int laneA, out float alphaA), Is.True);
            Assert.That(traffic.TryEvaluateVehicleForVisualQa(to, direction, 0, boundary + .001,
                out Vector3 b, out _, out int laneB, out float alphaB), Is.True);
            Assert.That(laneA, Is.EqualTo(laneB));
            // 8m/s advances16mm in2ms; the existing accepted endpoint allowance is20mm.
            Assert.That(Vector3.Distance(a, b), Is.LessThan(.05f));
            Assert.That(alphaB, Is.EqualTo(alphaA).Within(.01f));
        }

        [TestCase(false)] [TestCase(true)]
        public void FootprintSupportTraversesSeveralShortAcceptedSegments(bool reverse)
        {
            WithFootprintFixture(new[] { new[] { 0f, 1f }, new[] { 1f, 2f }, new[] { 2f, 3f } }, (runtime, routes) =>
            {
                Vector3 sample = SampleFootprint(runtime, routes.GetValue(reverse ? 2 : 0), reverse ? 1 : 0,
                    .5f, reverse ? -2f : 2f);
                Assert.That(sample.x, Is.EqualTo(reverse ? .5f : 2.5f).Within(.00001f));
                Assert.That(sample.y, Is.EqualTo(sample.x * 2f).Within(.00001f));
            });
        }

        [Test] public void AcceptedJoinCanReverseTheNextCorridorsAuthoredPointOrder()
        {
            WithFootprintFixture(new[] { new[] { 0f, 1f }, new[] { 2f, 1f } }, (runtime, routes) =>
            {
                Vector3 sample = SampleFootprint(runtime, routes.GetValue(0), 0, .5f, 1f);
                Assert.That(sample.x, Is.EqualTo(1.5f).Within(.00001f));
                Assert.That(sample.y, Is.EqualTo(3f).Within(.00001f));
            });
        }

        [Test] public void FootprintSupportKeepsTrueFreeEndsClamped()
        {
            WithFootprintFixture(new[] { new[] { 0f, 1f }, new[] { 1f, 2f }, new[] { 2f, 3f } }, (runtime, routes) =>
            {
                Assert.That(SampleFootprint(runtime, routes.GetValue(0), 0, 0f, -10f).x, Is.Zero);
                Assert.That(SampleFootprint(runtime, routes.GetValue(2), 0, 1f, 10f).x, Is.EqualTo(3f));
                Assert.That(SampleFootprint(runtime, routes.GetValue(0), 1, 0f, -10f).x, Is.Zero);
                Assert.That(SampleFootprint(runtime, routes.GetValue(2), 1, 1f, 10f).x, Is.EqualTo(3f));
            });
        }

        [Test] public void AmbiguousJunctionDoesNotAcquireAnInventedFootprintConnection()
        {
            WithFootprintFixture(new[] { new[] { 0f, 1f }, new[] { 1f, 2f }, new[] { 1f, 3f } }, (runtime, routes) =>
            {
                Assert.That(SampleFootprint(runtime, routes.GetValue(0), 0, .5f, 2f).x, Is.EqualTo(1f));
            });
        }

        private static Vector3 SampleFootprint(KbduTrafficV2Runtime runtime, object route,
            int direction, float fraction, float offset)
        {
            object[] args = { route, direction, 0, fraction, offset, default(Vector3), default(Vector3) };
            bool sampled = (bool)typeof(KbduTrafficV2Runtime).GetMethod("TrySampleVehicleFootprint",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(runtime, args);
            Assert.That(sampled, Is.True);
            return (Vector3)args[5];
        }

        private static void WithFootprintFixture(float[][] endpoints, Action<KbduTrafficV2Runtime, Array> check)
        {
            const string identity = "isolated-footprint-regression";
            var target = new GameObject("Traffic footprint fixture") { hideFlags = HideFlags.DontSave };
            target.SetActive(false);
            TextAsset bankAsset = null;
            try
            {
                var runtime = target.AddComponent<KbduTrafficV2Runtime>();
                Type manifestType = typeof(KbduTrafficV2Runtime).GetNestedType("Manifest", BindingFlags.NonPublic);
                Type corridorType = typeof(KbduTrafficV2Runtime).GetNestedType("Corridor", BindingFlags.NonPublic);
                object manifest = Activator.CreateInstance(manifestType, true);
                Array routes = Array.CreateInstance(corridorType, endpoints.Length);
                void Set(object obj, string name, object value) => obj.GetType().GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(obj, value);
                for (int i = 0; i < endpoints.Length; i++)
                {
                    object route = Activator.CreateInstance(corridorType, true);float[] p = endpoints[i];
                    Set(route, "authoredIndex", i);Set(route, "corridor_id", "fixture:" + i);
                    Set(route, "stableHash", i + 1);Set(route, "direction_mode", "BIDIRECTIONAL");
                    Set(route, "points_xz_meters", new[] { p[0], 0f, p[1], 0f });
                    Set(route, "length_meters", Mathf.Abs(p[1] - p[0]));Set(route, "speed_mps", 1f);
                    Set(route, "headway_seconds", 10f);Set(route, "lane_count", 2);Set(route, "road_class", "residential");
                    routes.SetValue(route, i);
                }
                using var output = new MemoryStream();
                using (var zip = new GZipStream(output, CompressionMode.Compress, true))
                using (var writer = new BinaryWriter(zip, Encoding.UTF8))
                {
                    writer.Write(TrafficLaneSurfaceBank.Magic);writer.Write(2);writer.Write(endpoints.Length);writer.Write(identity);
                    foreach (float[] p in endpoints)
                    {
                        writer.Write(2);
                        for (int slot = 0; slot < 2; slot++)
                        {
                            writer.Write(2);
                            for (int point = 0; point < 2; point++)
                            {
                                writer.Write(point == 0 ? 0f : Mathf.Abs(p[1] - p[0]));
                                writer.Write(p[point]);writer.Write(p[point] * 2f);writer.Write(0f);
                                writer.Write(0f);writer.Write(1f);writer.Write(0f);
                            }
                        }
                    }
                }
                // TextAsset(byte[]) is supported by the pinned Unity6000 test toolchain.
                bankAsset = new TextAsset(output.ToArray());
                Set(manifest, "corridors", routes);Set(runtime, "parsedManifest", manifest);
                Set(runtime, "authoredLaneSurfaces", new TrafficLaneSurfaceBank(bankAsset, endpoints.Length, identity));
                Set(runtime, "gradeSeparatedRoadAuthority", AssetDatabase.LoadAssetAtPath<TextAsset>(
                    "Assets/Production/Environment/GradeSeparatedRoadsV1/Data/KBDU_GradeSeparatedRoadAuthorityV1.json"));
                typeof(KbduTrafficV2Runtime).GetMethod("BuildContinuousStreams", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(runtime, null);
                check(runtime, routes);
            }
            finally
            {
                if (bankAsset != null) UnityEngine.Object.DestroyImmediate(bankAsset);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test] public void OverlappingBridgeApproachesDoNotChangeVehicleHeightAbruptly()
        {
            const int index = 28577;
            object c = corridors.GetValue(index);
            double start = Field<double>(Field<Array>(c, "streams").GetValue(0), "phase");
            double lifetime = Field<float>(c, "length_meters") / (double)Field<float>(c, "speed_mps");
            bool previousValid = false;
            Vector3 previous = default;
            for (double age = 0.01; age < lifetime - 0.01; age += 0.002)
            {
                Assert.That(traffic.TryEvaluateVehicleForVisualQa(index, 0, 0, start + age,
                    out Vector3 current, out _, out _, out _), Is.True);
                if (previousValid) Assert.That(Mathf.Abs(current.y - previous.y), Is.LessThan(0.03f),
                    "Bridge support jump at age " + age);
                previous = current;
                previousValid = true;
            }
        }

        [TestCase(1.0)] [TestCase(4.0)] [TestCase(30.0)]
        public void UnconnectedEndpointsAndRangeBoundaryHaveContinuousCoverage(double lifetime)
        {
            Assert.That(KbduTrafficV2Runtime.EvaluateVisibilityEnvelope(0, lifetime, 50), Is.Zero);
            Assert.That(KbduTrafficV2Runtime.EvaluateVisibilityEnvelope(lifetime, lifetime, 50), Is.Zero);
            Assert.That(KbduTrafficV2Runtime.EvaluateVisibilityEnvelope(lifetime / 2, lifetime, 50), Is.EqualTo(1));
            float previous = 0;
            for (double age = 0; age <= lifetime; age += 1.0 / 72)
            {
                float alpha = KbduTrafficV2Runtime.EvaluateVisibilityEnvelope(age, lifetime, 50);
                Assert.That(Math.Abs(alpha - previous), Is.LessThan(0.085f));
                previous = alpha;
            }
            Assert.That(KbduTrafficV2Runtime.EvaluateVisibilityEnvelope(lifetime / 2, lifetime, 4500), Is.Zero);
            Assert.That(KbduTrafficV2Runtime.EvaluateVisibilityEnvelope(lifetime / 2, lifetime, 4499.99f), Is.LessThan(0.00001));
        }

        [Test] public void SmallCameraMovementsCannotOscillateVehicleLod()
        {
            bool near = true, far = false;
            foreach (float distance in new[] { 999f, 1001f, 998f, 1040f, 980f, 1099f, 901f })
            {
                near = KbduTrafficV2Runtime.EvaluateNearLod(near, distance);
                far = KbduTrafficV2Runtime.EvaluateNearLod(far, distance);
                Assert.That(near, Is.True);
                Assert.That(far, Is.False);
            }
            Assert.That(KbduTrafficV2Runtime.EvaluateNearLod(true, 1101), Is.False);
            Assert.That(KbduTrafficV2Runtime.EvaluateNearLod(false, 899), Is.True);
        }

        [Test] public void FrustumRejectionRetainsVisibleVehiclesAcrossHeadTurns()
        {
            var prepare = (Action<Camera, double>)Delegate.CreateDelegate(typeof(Action<Camera, double>), traffic,
                typeof(KbduTrafficV2Runtime).GetMethod("PrepareVisibleInstances", BindingFlags.Instance | BindingFlags.NonPublic));
            var target = new GameObject("Traffic culling test") { hideFlags = HideFlags.DontSave };
            var camera = target.AddComponent<Camera>(); camera.enabled = false;
            camera.fieldOfView = 95; camera.aspect = 1; camera.farClipPlane = 6000;
            var near = Field<Matrix4x4[][]>(traffic, "nearMatrices");
            var far = Field<Matrix4x4[][]>(traffic, "farMatrices");
            var nearCounts = Field<int[]>(traffic, "nearCounts");
            var farCounts = Field<int[]>(traffic, "farCounts");
            List<Vector3> Positions()
            {
                var result = new List<Vector3>();
                for (int group = 0; group < nearCounts.Length; group++)
                    for (int i = 0; i < nearCounts[group]; i++) result.Add(near[group][i].GetColumn(3));
                for (int group = 0; group < farCounts.Length; group++)
                    for (int i = 0; i < farCounts[group]; i++) result.Add(far[group][i].GetColumn(3));
                return result;
            }
            int retained = 0, rejected = 0;
            try
            {
                foreach (var position in new[] { new Vector3(0, 120, 0), new Vector3(-1600, 100, -2000), new Vector3(0, 500, 3500) })
                foreach (int yaw in new[] { 0, 90, 180, 270 })
                foreach (int pitch in new[] { -35, 0, 35 })
                {
                    camera.transform.SetPositionAndRotation(position, Quaternion.Euler(pitch, yaw, 0));
                    traffic.DisableFrustumCullingForVisualQa = true; prepare(camera, 60);
                    var baseline = Positions();
                    traffic.DisableFrustumCullingForVisualQa = false; prepare(camera, 60);
                    var candidate = new HashSet<Vector3>(Positions());
                    rejected += baseline.Count - candidate.Count;
                    foreach (var point in baseline)
                    {
                        var viewport = camera.WorldToViewportPoint(point);
                        if (viewport.z <= 0 || viewport.x < -0.02 || viewport.x > 1.02 || viewport.y < -0.02 || viewport.y > 1.02) continue;
                        Assert.That(candidate.Contains(point), Is.True, "Visible vehicle rejected after head turn at " + point);
                        retained++;
                    }
                }
                Assert.That(retained, Is.GreaterThan(100));
                Assert.That(rejected, Is.GreaterThan(100));
                Debug.Log($"Traffic frustum regression: 36 head poses, {retained} visible vehicle positions retained exactly; {rejected} offscreen submissions avoided.");
            }
            finally { traffic.DisableFrustumCullingForVisualQa = false; UnityEngine.Object.DestroyImmediate(target); }
        }

        [Test] public void FullMapTrafficStaysWithinSubmissionCapsWithoutDroppingVehicles()
        {
            var prepare = (Action<Camera, double>)Delegate.CreateDelegate(typeof(Action<Camera, double>), traffic,
                typeof(KbduTrafficV2Runtime).GetMethod("PrepareVisibleInstances", BindingFlags.Instance | BindingFlags.NonPublic));
            var target = new GameObject("Traffic draw-budget diagnostic") { hideFlags = HideFlags.DontSave };
            var camera = target.AddComponent<Camera>();
            camera.enabled = false;
            int maximumDraws = 0;
            try
            {
                for (int x = -10000; x <= 10000; x += 5000)
                    for (int z = -10000; z <= 10000; z += 5000)
                        foreach (double time in new[] { 0d, 60d, 120d })
                        {
                            camera.transform.position = new Vector3(x, 300, z);
                            prepare(camera, time);
                            Assert.That(traffic.DroppedInstanceCount, Is.Zero, $"Dropped at {x},{z} t={time}");
                            Assert.That(traffic.PreparedDrawCallCount, Is.LessThanOrEqualTo(KbduTrafficV2Runtime.MaximumDrawCalls));
                            maximumDraws = Math.Max(maximumDraws, traffic.PreparedDrawCallCount);
                        }
                Debug.Log("Traffic submission: 75 map/time cases, maximum " + maximumDraws + " draws, no dropped vehicles.");
            }
            finally { UnityEngine.Object.DestroyImmediate(target); }
        }
    }
}
