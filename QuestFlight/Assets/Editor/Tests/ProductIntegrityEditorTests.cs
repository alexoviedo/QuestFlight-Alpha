using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using QuestFlight.Product;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;

namespace QuestFlight.Editor.Tests
{
    public sealed class ProductIntegrityEditorTests
    {
        [Test]
        [Timeout(600000)]
        public void FullProductIntegrityGatePasses()
        {
            // Android texture-compression selection is Editor session state. Make
            // this gate deterministic when run in a fresh batch process instead of
            // relying on a prior build having selected ASTC in the same process.
            ProductProjectConfigurator.ConfigureProject();
            ProductIntegrityReport report = ProductIntegrityValidator.Validate();
            Assert.That(report.passed, Is.True, report.ToString());
            Assert.That(report.rendererCount, Is.LessThanOrEqualTo(180));
            Assert.That(ProductIntegrityValidator.MaximumSerializedSceneTriangles, Is.EqualTo(700000),
                "The historical/default product-integrity ceiling is frozen.");
            Assert.That(report.triangleCount, Is.LessThanOrEqualTo(report.serializedTriangleCeiling));
            Assert.That(report.materialCount, Is.LessThanOrEqualTo(40));
        }

        [Test]
        public void ProductIntegrityValidationPreservesEditorAndGlobalState()
        {
            string[] sceneSetup = EditorSceneManager.GetSceneManagerSetup()
                .Select(item => $"{item.path}|{item.isLoaded}|{item.isActive}")
                .ToArray();
            Scene activeScene = SceneManager.GetActiveScene();
            string activeScenePath = activeScene.path;
            string activeSceneName = activeScene.name;
            bool activeSceneLoaded = activeScene.isLoaded;
            bool activeSceneDirty = activeScene.isDirty;
            int qualityLevel = QualitySettings.GetQualityLevel();
            float timeScale = Time.timeScale;
            int targetFrameRate = Application.targetFrameRate;

            ProductIntegrityValidator.Validate();

            Assert.That(EditorSceneManager.GetSceneManagerSetup()
                .Select(item => $"{item.path}|{item.isLoaded}|{item.isActive}"),
                Is.EqualTo(sceneSetup), "Integrity validation changed the open-scene setup.");
            Scene activeSceneAfterValidation = SceneManager.GetActiveScene();
            Assert.That(activeSceneAfterValidation.path, Is.EqualTo(activeScenePath));
            Assert.That(activeSceneAfterValidation.name, Is.EqualTo(activeSceneName));
            Assert.That(activeSceneAfterValidation.isLoaded, Is.EqualTo(activeSceneLoaded));
            Assert.That(activeSceneAfterValidation.isDirty, Is.EqualTo(activeSceneDirty));
            Assert.That(QualitySettings.GetQualityLevel(), Is.EqualTo(qualityLevel));
            Assert.That(Time.timeScale, Is.EqualTo(timeScale));
            Assert.That(Application.targetFrameRate, Is.EqualTo(targetFrameRate));
        }

        [Test]
        public void QuestSettingsAreProductOnlyAndVulkanArm64()
        {
            Assert.That(
                PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android),
                Is.EqualTo(ProductProjectConfigurator.AndroidApplicationIdentifier));
            Assert.That(PlayerSettings.Android.targetArchitectures, Is.EqualTo(AndroidArchitecture.ARM64));
            Assert.That(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android), Is.EqualTo(ScriptingImplementation.IL2CPP));
            Assert.That(PlayerSettings.GetGraphicsAPIs(BuildTarget.Android), Is.EqualTo(new[] { GraphicsDeviceType.Vulkan }));
            Assert.That(PlayerSettings.colorSpace, Is.EqualTo(ColorSpace.Linear));
            Assert.That(EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path),
                Is.EqualTo(new[] { ProductProjectConfigurator.ProductionScenePath }));
        }

        [Test]
        public void PackageManifestIsPinnedAndContainsNoRAndDRendererPackage()
        {
            string manifest = File.ReadAllText("Packages/manifest.json");
            Assert.That(manifest, Does.Contain("\"com.unity.inputsystem\": \"1.18.0\""));
            Assert.That(manifest, Does.Contain("\"com.unity.cloud.gltfast\": \"6.13.0\""));
            Assert.That(manifest, Does.Contain("\"com.unity.xr.core-utils\": \"2.5.3\""));
            Assert.That(manifest, Does.Contain("\"com.unity.xr.openxr\": \"1.16.1\""));
            Assert.That(manifest, Does.Not.Contain("gaussian-splatting"));
            Assert.That(manifest, Does.Not.Contain("xr.interaction.toolkit"));
        }

        [Test]
        public void ProductBuildEntryPointCannotSelectLegacyScene()
        {
            Assert.That(ProductionBuild.ApkRelativePath, Is.EqualTo("Builds/Android/QuestFlight-production.apk"));
            string source = File.ReadAllText("Assets/Editor/ProductionBuild.cs");
            Assert.That(source, Does.Not.Contain("InputLab"));
            Assert.That(source, Does.Not.Contain("QuestFlightLab"));
            Assert.That(source, Does.Contain("ProductProjectConfigurator.ProductionScenePath"));
        }

        [Test]
        public void ProductBuildUnconditionallyRequiresNativeJsbsim()
        {
            string source = File.ReadAllText("Assets/Editor/ProductionBuild.cs");
            Assert.That(source, Does.Contain("QUESTFLIGHT_REQUIRE_NATIVE_JSBSIM"));
            Assert.That(source, Does.Not.Contain("FORCE_UNITY_PROTOTYPE"));
            Assert.That(source, Does.Not.Contain("ForceUnityPrototype"));
            Assert.That(source, Does.Not.Contain("EnableNativeJsbsim"));
        }

        [Test]
        public void EveryQualityLevelUsesFourTimesMsaa()
        {
            int original = QualitySettings.GetQualityLevel();
            try
            {
                for (int index = 0; index < QualitySettings.names.Length; index++)
                {
                    QualitySettings.SetQualityLevel(index, false);
                    Assert.That(QualitySettings.antiAliasing, Is.EqualTo(4), QualitySettings.names[index]);
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(Mathf.Clamp(original, 0, QualitySettings.names.Length - 1), false);
            }
        }

        [Test]
        public void ProductCreditsExposeRequiredAttributionWithoutLegacyUi()
        {
            GameObject target = new GameObject("AttributionTest");
            try
            {
                string credits = target.AddComponent<ProductAttribution>().CreditsText;
                Assert.That(credits, Does.Contain("KOG_THORNS"));
                Assert.That(credits, Does.Contain("https://creativecommons.org/licenses/by/4.0/"));
                Assert.That(credits, Does.Contain(ProductAttribution.ParkedSceneryChangeNotice));
                Assert.That(credits, Does.Contain("© OpenStreetMap contributors"));
                Assert.That(credits, Does.Contain("https://www.openstreetmap.org/copyright"));
                Assert.That(credits, Does.Contain("No endorsement"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ProductDemoUsesControlsOnlyAndCoversRequiredLoopPhases()
        {
            float[] sampleTimes = { 0f, 5f, 12f, 20f, 33f, 42f, 55f, 68f };
            string[] phases = sampleTimes.Select(time =>
                {
                    ProductDemoPilot.ScheduledControls(time, out string phase);
                    return phase;
                }).ToArray();
            Assert.That(phases, Is.EqualTo(new[]
            {
                "idle_brake_hold", "taxi", "taxi_brake", "takeoff_roll", "rotation", "vy_climb",
                "shallow_left_turn", "climb_stabilize"
            }));
            string source = File.ReadAllText("Assets/Scripts/Product/ProductDemoPilot.cs");
            Assert.That(source, Does.Not.Contain("SetPositionAndRotation"));
            Assert.That(source, Does.Not.Contain("Main Camera"));
            Assert.That(source, Does.Not.Contain("XR Origin"));
        }

        [Test]
        public void ProductDemoAndInputMappingReuseSteadyStateSnapshots()
        {
            GamepadInputSnapshot input = new GamepadInputSnapshot
            {
                connected = true,
                leftStickX = 0.25f,
                leftStickY = -0.4f,
                rightStickX = 0.1f,
                leftTrigger = 0.2f,
                rightTrigger = 0.3f
            };
            AircraftControlState mapped = AircraftControlState.Neutral();
            ProductControlInput.MapSnapshotInto(input, true, 0.8f, 0.05f, 0.25f, 1f, 0f, mapped);
            AircraftControlState mappedIdentity = mapped;
            ProductControlInput.MapSnapshotInto(input, true, 0.7f, -0.05f, 0f, 0.9f, 0.1f, mapped);
            Assert.That(mapped, Is.SameAs(mappedIdentity));
            Assert.That(mapped.throttle, Is.EqualTo(0.7f).Within(0.0001f));

            AircraftControlState scheduled = AircraftControlState.Neutral();
            ProductDemoPilot.ScheduledControlsInto(42f, out string phase, scheduled);
            AircraftControlState scheduledIdentity = scheduled;
            ProductDemoPilot.ScheduledControlsInto(55f, out phase, scheduled);
            Assert.That(scheduled, Is.SameAs(scheduledIdentity));
            Assert.That(phase, Is.EqualTo("shallow_left_turn"));

            GameObject target = new GameObject("DeterministicInputReuseTest");
            try
            {
                DeterministicGamepadInputSource source = target.AddComponent<DeterministicGamepadInputSource>();
                source.SetControls(scheduled);
                GamepadInputSnapshot first = source.Current;
                source.SetControls(mapped);
                Assert.That(source.Current, Is.SameAs(first));
                Assert.That(source.Current.leftStickX, Is.EqualTo(mapped.aileron).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ProductDemoRunwayRouteUsesFeedbackAndTurnIsCoordinated()
        {
            AircraftControlState taxi =
                ProductDemoPilot.ScheduledControls(5f, out string taxiPhase);
            AircraftControlState takeoff =
                ProductDemoPilot.ScheduledControls(20f, out string takeoffPhase);
            AircraftControlState rollIn =
                ProductDemoPilot.ScheduledControls(53f, out string turnPhase);
            AircraftControlState rollOut =
                ProductDemoPilot.ScheduledControls(61f, out _);

            Assert.That(taxiPhase, Is.EqualTo("taxi"));
            Assert.That(taxi.rudder, Is.EqualTo(0f));
            Assert.That(takeoffPhase, Is.EqualTo("takeoff_roll"));
            Assert.That(takeoff.rudder, Is.EqualTo(0f));
            Assert.That(turnPhase, Is.EqualTo("shallow_left_turn"));
            Assert.That(rollIn.aileron, Is.LessThan(0f));
            Assert.That(rollIn.rudder, Is.LessThan(0f));
            Assert.That(rollOut.aileron, Is.GreaterThan(0f));
            Assert.That(rollOut.rudder, Is.GreaterThan(0f));

            Assert.That(ProductDemoPilot.ComputeRunwayTrackingRudder(
                    85.0, 90.0, 0f, 0.025f, 0.015f, 0.14f),
                Is.GreaterThan(0f));
            Assert.That(ProductDemoPilot.ComputeRunwayTrackingRudder(
                    95.0, 90.0, 0f, 0.025f, 0.015f, 0.14f),
                Is.LessThan(0f));
            Assert.That(ProductDemoPilot.ComputeRunwayTrackingRudder(
                    90.0, 90.0, 2f, 0.025f, 0.015f, 0.14f),
                Is.LessThan(0f));

            AircraftControlState target = AircraftControlState.Neutral(1f);
            target.rudder = 1f;
            target.leftToeBrake = 1f;
            target.rightToeBrake = 1f;
            AircraftControlState current = AircraftControlState.Neutral(0.1f);
            ProductDemoPilot.RateLimitControlsInto(target, current, 0.1f);
            Assert.That(current.rudder,
                Is.EqualTo(ProductDemoPilot.PrimaryControlRatePerSecond * 0.1f)
                    .Within(0.0001f));
            Assert.That(current.leftToeBrake,
                Is.EqualTo(ProductDemoPilot.ToeBrakeRatePerSecond * 0.1f)
                    .Within(0.0001f));
            Assert.That(current.rudder, Is.LessThan(target.rudder));
        }
    }
}
