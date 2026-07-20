using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using QuestFlight.Product;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;

namespace QuestFlight.Editor.Tests
{
    public sealed class ProductIntegrityEditorTests
    {
        [Test]
        public void FullProductIntegrityGatePasses()
        {
            ProductIntegrityReport report = ProductIntegrityValidator.Validate();
            Assert.That(report.passed, Is.True, report.ToString());
            Assert.That(report.rendererCount, Is.LessThanOrEqualTo(180));
            Assert.That(ProductIntegrityValidator.MaximumSerializedSceneTriangles, Is.EqualTo(700000),
                "The historical/default product-integrity ceiling is frozen.");
            Assert.That(report.triangleCount, Is.LessThanOrEqualTo(report.serializedTriangleCeiling));
            Assert.That(report.materialCount, Is.LessThanOrEqualTo(40));
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
    }
}
