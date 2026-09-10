using System;
using NUnit.Framework;
using System.Linq;
using System.Reflection;
using QuestFlightLab.Environment;
using UnityEditor.SceneManagement;
using UnityEngine;
using QuestFlightLab.Runtime;

namespace QuestFlightLab.Editor.Tests
{
    public sealed class VisualUpgradeCaptureRequestTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 6, 18, 0, 0, DateTimeKind.Utc);
        private static string Request(DateTime expires, string profile = "production") =>
            "{\"schema\":\"questflight.acceptance-request.v1\",\"expiresUtc\":\"" +
            expires.ToString("O") + "\",\"isolationProfile\":\"" + profile + "\"}";

        [TestCase("sky_only")]
        [TestCase("clouds_off")]
        [TestCase("traffic_off")]
        [TestCase("vegetation_off")]
        [TestCase("buildings_off")]
        [TestCase("water_off")]
        public void IsolationTargetsAuthoredSystemsAndRestoresState(string profile)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity", OpenSceneMode.Single);
            var environment = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ProductionEnvironmentRoot>(true)).Single();
            var renderers = environment.GetComponentsInChildren<Renderer>(true);
            var flags = renderers.Select(r => r.forceRenderingOff).ToArray();
            var behaviours = environment.GetComponentsInChildren<Behaviour>(true);
            var enabled = behaviours.Select(b => b.enabled).ToArray();
            var type = typeof(QuestTemporalVisualGateRecorder);
            var apply = type.GetMethod("ApplyDiagnosticIsolation", BindingFlags.NonPublic | BindingFlags.Static);
            var restore = type.GetMethod("RestoreDiagnosticIsolation", BindingFlags.NonPublic | BindingFlags.Static);
            object state = apply.Invoke(null, new object[] { profile, environment });
            try
            {
                var trees = environment.GetComponentsInChildren<AirportCloseTreeLodRenderer>(true);
                var moving = environment.GetComponentsInChildren<KbduTrafficV2Runtime>(true);
                Assert.That(trees.Length, Is.GreaterThan(0));
                Assert.That(moving.Length, Is.GreaterThan(0));
                if (profile == "sky_only" || profile == "traffic_off")
                    Assert.That(moving.All(t => !t.enabled), Is.True);
                if (profile == "sky_only" || profile == "vegetation_off")
                    Assert.That(trees.All(t => !t.enabled), Is.True);
                if (profile == "sky_only") Assert.That(renderers.All(r => r.forceRenderingOff), Is.True);
                if (profile == "buildings_off")
                {
                    var controllers = environment.GetComponentsInChildren<AircraftCenteredBuildingClipmapController>(true);
                    Assert.That(controllers.Length, Is.GreaterThan(0));
                    Assert.That(controllers.SelectMany(c => c.pageRenderers).All(r => r.forceRenderingOff), Is.True);
                }
                if (profile == "clouds_off")
                {
                    var clouds = environment.GetComponentsInChildren<BoulderCloudField>(true);
                    Assert.That(clouds.Length, Is.GreaterThan(0));
                    Assert.That(clouds.SelectMany(c => c.SectorRenderers).All(r => r.forceRenderingOff), Is.True);
                    Assert.That(trees.Select(t => t.enabled), Is.EqualTo(trees.Select(t => enabled[System.Array.IndexOf(behaviours, t)])));
                }
                if (profile == "water_off") Assert.That(Shader.GetGlobalFloat("_QflDiagnosticWaterDisabled"), Is.EqualTo(1));
                Debug.Log("Isolation verified: " + profile);
            }
            finally { restore.Invoke(null, new[] { state }); }
            Assert.That(renderers.Select(r => r.forceRenderingOff), Is.EqualTo(flags));
            Assert.That(behaviours.Select(b => b.enabled), Is.EqualTo(enabled));
            Assert.That(Shader.GetGlobalFloat("_QflDiagnosticWaterDisabled"), Is.Zero);
        }

        [Test]
        public void ExplicitTimedRequestPreservesRequestedProfile()
        {
            Assert.That(QuestTemporalVisualGateRecorder.TryValidateCaptureRequest(
                Request(Now.AddMinutes(10), "water_off"), Now, out string profile), Is.True);
            Assert.That(profile, Is.EqualTo("water_off"));
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(16)]
        public void ExpiredOrUnboundedRequestCannotActivateDiagnostics(int minutes)
        {
            Assert.That(QuestTemporalVisualGateRecorder.TryValidateCaptureRequest(
                Request(Now.AddMinutes(minutes)), Now, out _), Is.False);
        }

        [Test]
        public void UnknownProfileAndInvalidJsonFailClosed()
        {
            Assert.That(QuestTemporalVisualGateRecorder.TryValidateCaptureRequest(
                Request(Now.AddMinutes(10), "unsupported"), Now, out _), Is.False);
            Assert.That(QuestTemporalVisualGateRecorder.TryValidateCaptureRequest("invalid", Now, out _), Is.False);
        }

        [Test] public void DemoStartRequiresAnExplicitUnexpiredDiagnosticRequest()
        {
            string valid = Request(Now.AddMinutes(10));
            Assert.That(QuestTemporalVisualGateRecorder.CaptureRequestStartsDemo(valid, Now), Is.False);
            string demo = valid.Substring(0, valid.Length - 1) + ",\"startDemo\":true}";
            Assert.That(QuestTemporalVisualGateRecorder.CaptureRequestStartsDemo(demo, Now), Is.True);
            Assert.That(QuestTemporalVisualGateRecorder.CaptureRequestStartsDemo(demo, Now.AddMinutes(11)), Is.False);
            Assert.That(QuestTemporalVisualGateRecorder.CaptureRequestStartsDemo("invalid", Now), Is.False);
        }
    }
}
