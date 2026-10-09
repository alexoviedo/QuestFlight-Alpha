using System;
using NUnit.Framework;
using System.Linq;
using System.Reflection;
using QuestFlightLab.Environment;
using UnityEditor.SceneManagement;
using UnityEngine;
using QuestFlightLab.Runtime;
using QuestFlightLab.Flight.Backends;
using UnityEditor;

namespace QuestFlightLab.Editor.Tests
{
    public sealed class VisualUpgradeCaptureRequestTests
    {
        [Test]
        public void TemporalReadinessObservesRealNativeAuthorityAndLatchesFailures()
        {
            GameObject world = PrefabUtility.LoadPrefabContents("Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");
            try
            {
                FlightDynamicsCoordinator authority = world.GetComponentInChildren<FlightDynamicsCoordinator>(true);
                Assert.That(authority, Is.Not.Null);
                Assert.That(authority.TryGetAuthorityReadiness(out string error), Is.False);
                Assert.That(error, Does.Contain("preparing"));
                string missing = string.Empty;
                Assert.That(QuestTemporalVisualGateRecorder.ObserveAuthority(null, ref missing), Is.False);
                Assert.That(missing, Does.Contain("missing"));
                Assert.That(authority.InitializeSelectedBackend(), Is.True, authority.LastError);
                Assert.That(authority.TryGetAuthorityReadiness(out error), Is.True, error);
                Assert.That(authority.NativePayloadIdentity, Has.Length.EqualTo(64));
                authority.controls.GetType().GetProperty("Paused").SetValue(authority.controls, true);
                Assert.That(authority.TryGetAuthorityReadiness(out error), Is.True, "An explicitly labeled initialized menu can be paused.");
                string latched = string.Empty;
                authority.enabled = false;
                Assert.That(QuestTemporalVisualGateRecorder.ObserveAuthority(authority, ref latched), Is.False);
                authority.enabled = true;
                Assert.That(authority.TryGetAuthorityReadiness(out error), Is.True, error);
                Assert.That(QuestTemporalVisualGateRecorder.ObserveAuthority(authority, ref latched), Is.False, "Recovery must not validate an interrupted observation.");
                FlightDynamicsState invalid = authority.CurrentState;
                invalid.latitudeDegrees = double.NaN;
                typeof(FlightDynamicsCoordinator).GetField("_currentState", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(authority, invalid);
                Assert.That(authority.TryGetAuthorityReadiness(out error), Is.False);
                Assert.That(error, Does.Contain("non-finite"));
                typeof(FlightDynamicsCoordinator).GetProperty("LastError").SetValue(authority, "Injected native initialization failure");
                Assert.That(authority.TryGetAuthorityReadiness(out error), Is.False);
                Assert.That(error, Does.Contain("Injected native"));
            }
            finally { PrefabUtility.UnloadPrefabContents(world); }
        }

        private static readonly DateTime Now = new DateTime(2026, 9, 6, 18, 0, 0, DateTimeKind.Utc);
        private static string Request(DateTime expires, string profile = "production") =>
            "{\"schema\":\"questflight.acceptance-request.v1\",\"expiresUtc\":\"" +
            expires.ToString("O") + "\",\"isolationProfile\":\"" + profile + "\",\"requestedQualityMode\":\"Native72\"}";

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

        [TestCase("Native72", 1, false)]
        [TestCase("SpaceWarp36_72", 0, true)]
        public void TimedCaptureSelectsExplicitModeWithoutChangingSavedSpaceWarp(string mode, int saved, bool requested)
        {
            const string key = "QuestFlight.SpaceWarp";
            bool hadKey = PlayerPrefs.HasKey(key); int previous = PlayerPrefs.GetInt(key);
            try
            {
                PlayerPrefs.SetInt(key, saved);
                string json = Request(Now.AddMinutes(10)).Replace("Native72", mode);
                uint before = QuestFlight.Rendering.ProductSpaceWarpController.HistoryGeneration;
                var scope = QuestTemporalVisualGateRecorder.BeginCaptureQualityScope(json, Now, out string echo);
                try
                {
                    Assert.That(scope, Is.Not.Null);
                    Assert.That(echo, Is.EqualTo(mode));
                    Assert.That(QuestFlight.Rendering.ProductSpaceWarpController.Requested, Is.EqualTo(requested));
                    Assert.That(PlayerPrefs.GetInt(key), Is.EqualTo(saved));
                    Assert.That(QuestFlight.Rendering.ProductSpaceWarpController.HistoryGeneration, Is.Not.EqualTo(before));
                    Assert.Throws<InvalidOperationException>(() => QuestTemporalVisualGateRecorder.BeginCaptureQualityScope(json, Now, out _));
                    // A settings change during capture remains the user's choice
                    // after the override ends; disposal must not restore stale prefs.
                    PlayerPrefs.SetInt(key, 1 - saved);
                    Assert.That(QuestFlight.Rendering.ProductSpaceWarpController.Requested, Is.EqualTo(requested));
                }
                finally { scope?.Dispose(); scope?.Dispose(); }
                Assert.That(PlayerPrefs.GetInt(key), Is.EqualTo(1 - saved));
                Assert.That(QuestFlight.Rendering.ProductSpaceWarpController.Requested, Is.EqualTo(saved == 0));
            }
            finally { if (hadKey) PlayerPrefs.SetInt(key, previous); else PlayerPrefs.DeleteKey(key); }
        }

        [TestCase("unknown")]
        [TestCase("")]
        public void MissingOrUnsupportedCaptureModeCannotOverrideGameplay(string mode)
        {
            string json = Request(Now.AddMinutes(10)).Replace("Native72", mode);
            Assert.That(QuestTemporalVisualGateRecorder.TryValidateCaptureRequest(json, Now, out _), Is.False);
            Assert.That(QuestTemporalVisualGateRecorder.BeginCaptureQualityScope(json, Now, out string echo), Is.Null);
            Assert.That(echo, Is.EqualTo("Unspecified"));
            string missing = Request(Now.AddMinutes(10)).Replace(",\"requestedQualityMode\":\"Native72\"", "");
            Assert.That(QuestTemporalVisualGateRecorder.TryValidateCaptureRequest(missing, Now, out _), Is.False);
        }

        [Test]
        public void ExpiredGroveOrOrdinaryLaunchCannotOverrideQualityAndExceptionRestoresPreference()
        {
            string valid = Request(Now.AddMinutes(10));
            string grove = valid.Substring(0, valid.Length - 1) + ",\"groveObservation\":true}";
            Assert.That(QuestTemporalVisualGateRecorder.BeginCaptureQualityScope(valid, Now.AddMinutes(11), out _), Is.Null);
            Assert.That(QuestTemporalVisualGateRecorder.BeginCaptureQualityScope("", Now, out _), Is.Null);
            Assert.That(QuestTemporalVisualGateRecorder.TryValidateCaptureRequest(grove, Now, out _), Is.True);
            Assert.That(QuestTemporalVisualGateRecorder.BeginCaptureQualityScope(grove, Now, out _), Is.Null);
            bool before = QuestFlight.Rendering.ProductSpaceWarpController.Requested;
            Assert.Throws<InvalidOperationException>(() =>
            {
                using var scope = QuestTemporalVisualGateRecorder.BeginCaptureQualityScope(valid, Now, out _);
                throw new InvalidOperationException("Injected capture failure");
            });
            Assert.That(QuestFlight.Rendering.ProductSpaceWarpController.Requested, Is.EqualTo(before));
            Assert.That(new QuestTemporalVisualGateReport().requestedQualityMode, Is.EqualTo("Unspecified"));
        }
    }
}
