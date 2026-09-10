using System;
using System.Collections.Generic;
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
