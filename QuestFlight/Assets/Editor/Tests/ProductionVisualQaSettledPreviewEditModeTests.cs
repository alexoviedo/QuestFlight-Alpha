#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using QuestFlightLab.Editor;
using QuestFlightLab.Environment;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class ProductionVisualQaSettledPreviewEditModeTests
    {
        const string AircraftPath = "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        const string WorldPath = "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";

        [Test]
        public void ActualReportDtoSerializesExactSourceAndConfigurationFieldsRequiredByWrapper()
        {
            const string sourceVariable = "QFL_VISUAL_QA_SOURCE_IDENTITY_SHA256";
            const string configVariable = "QFL_VISUAL_QA_CONFIGURATION_SHA256";
            string oldSource = System.Environment.GetEnvironmentVariable(sourceVariable);
            string oldConfig = System.Environment.GetEnvironmentVariable(configVariable);
            string source = new string('a', 64), config = new string('b', 64);
            try
            {
                System.Environment.SetEnvironmentVariable(sourceVariable, source);
                System.Environment.SetEnvironmentVariable(configVariable, config);
                Type dto = typeof(ProductionVisualQaBatchRunner).GetNestedType("ProductionVisualQaReport", BindingFlags.Public | BindingFlags.NonPublic);
                Assert.That(dto, Is.Not.Null);
                string json = JsonUtility.ToJson(Activator.CreateInstance(dto));
                Assert.That(json, Does.Contain("\"sourceIdentitySha256\":\"" + source + "\""));
                Assert.That(json, Does.Contain("\"configurationSha256\":\"" + config + "\""));
                Assert.That(json, Does.Not.Contain("qualityConfigurationSha256"));
                string wrapper = File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../scripts/run_production_visual_qa.ps1")));
                Assert.That(wrapper, Does.Contain("$report.sourceIdentitySha256 -ne $env:" + sourceVariable));
                Assert.That(wrapper, Does.Contain("$report.configurationSha256 -ne $env:" + configVariable));
            }
            finally
            {
                System.Environment.SetEnvironmentVariable(sourceVariable, oldSource);
                System.Environment.SetEnvironmentVariable(configVariable, oldConfig);
            }
        }

        [Test]
        public void UnposedSdkCounterexampleIsSuppressedAndEveryStateRestoredAfterCaptureFailure()
        {
            byte[] originalPrefab = File.ReadAllBytes(AircraftPath);
            GameObject aircraft = PrefabUtility.LoadPrefabContents(AircraftPath);
            try
            {
                var helpers = aircraft.GetComponentsInChildren<OVRControllerHelper>(true);
                Assert.That(helpers.Length, Is.EqualTo(2));
                foreach (var helper in helpers)
                {
                    // The demonstrated EditMode counterexample: both handed models
                    // remain active before the SDK's runtime selection callbacks.
                    Assert.That(helper.m_modelMetaTouchPlusLeftController.activeSelf, Is.True);
                    Assert.That(helper.m_modelMetaTouchPlusRightController.activeSelf, Is.True);
                }
                var sdk = aircraft.GetComponentsInChildren<Transform>(true).Single(t => t.name == "OVRComprehensiveInteractionRig");
                var states = sdk.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r, r => r.enabled);
                var outside = aircraft.GetComponentsInChildren<Renderer>(true).First(r => !r.transform.IsChildOf(sdk) && r.enabled);
                int hierarchyCount = aircraft.GetComponentsInChildren<Transform>(true).Length;
                var transforms = aircraft.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => (t.localPosition, t.localRotation, t.localScale));
                try
                {
                    using (var scope = new ProductionVisualQaSettledSdkPreview(aircraft.transform))
                    {
                        Assert.That(scope.SuppressedRendererCount, Is.EqualTo(states.Count));
                        Assert.That(states.Keys.All(r => !r.enabled), Is.True);
                        Assert.That(outside.enabled, Is.True, "Ordinary cockpit/aircraft rendering must be retained.");
                        foreach (var helper in helpers)
                        {
                            Assert.That(helper.m_modelMetaTouchPlusLeftController.activeSelf,
                                Is.EqualTo(helper.m_controller == OVRInput.Controller.LTouch));
                            Assert.That(helper.m_modelMetaTouchPlusRightController.activeSelf,
                                Is.EqualTo(helper.m_controller == OVRInput.Controller.RTouch));
                        }
                        throw new InvalidOperationException("fixture capture failure");
                    }
                }
                catch (InvalidOperationException e) { Assert.That(e.Message, Is.EqualTo("fixture capture failure")); }
                foreach (var state in states) Assert.That(state.Key.enabled, Is.EqualTo(state.Value));
                foreach (var helper in helpers)
                {
                    Assert.That(helper.m_modelMetaTouchPlusLeftController.activeSelf, Is.True);
                    Assert.That(helper.m_modelMetaTouchPlusRightController.activeSelf, Is.True);
                }
                Assert.That(aircraft.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(hierarchyCount));
                foreach (var item in transforms)
                    Assert.That((item.Key.localPosition, item.Key.localRotation, item.Key.localScale), Is.EqualTo(item.Value));
                Assert.That(File.ReadAllBytes(AircraftPath), Is.EqualTo(originalPrefab));
            }
            finally { PrefabUtility.UnloadPrefabContents(aircraft); }
        }

        [Test]
        public void InvalidHandedSelectionRejectsAndRestoresAlreadySuppressedRenderers()
        {
            GameObject aircraft = PrefabUtility.LoadPrefabContents(AircraftPath);
            try
            {
                var states = aircraft.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r, r => r.enabled);
                var helper = aircraft.GetComponentsInChildren<OVRControllerHelper>(true).First();
                helper.m_controller = OVRInput.Controller.None;
                Assert.Throws<InvalidOperationException>(() => new ProductionVisualQaSettledSdkPreview(aircraft.transform));
                foreach (var state in states) Assert.That(state.Key.enabled, Is.EqualTo(state.Value));
            }
            finally { PrefabUtility.UnloadPrefabContents(aircraft); }
        }

        [Test]
        public void DeclaredGroundViewUsesExistingShadowWhileDemoThresholdIsOutsideAdmission()
        {
            GameObject world = PrefabUtility.LoadPrefabContents(WorldPath);
            try
            {
                var shadow = world.GetComponentInChildren<AircraftRunwayBlobShadow>(true);
                var route = AssetDatabase.LoadAssetAtPath<FlightLessonRouteProfile>("Assets/Production/Profiles/KBDU_ScenicDemo8.asset");
                Vector3 demoStart = route.Definition.waypoints[0].positionUnityMeters;
                shadow.AircraftSimulationRoot.position = new Vector3(demoStart.x, 1.35f, demoStart.z);
                shadow.UpdateShadowPoseNow();
                Assert.That(shadow.BlobRenderer.enabled, Is.False,
                    "The original native/demo threshold pose is before the runway: hiding is correct.");
                Pose pose = ProductionVisualQaSettledSdkPreview.GroundPresentationPose(shadow.Environment);
                shadow.AircraftSimulationRoot.SetPositionAndRotation(pose.position, pose.rotation);
                shadow.UpdateShadowPoseNow();
                Assert.That(shadow.BlobRenderer.enabled, Is.True);
                Assert.That(Vector3.ProjectOnPlane(pose.position - shadow.transform.position, shadow.transform.up).magnitude, Is.LessThan(.01f));
                shadow.AircraftSimulationRoot.position = pose.position + shadow.transform.up * (shadow.FadeOutHeightMeters + 5f);
                shadow.UpdateShadowPoseNow();
                Assert.That(shadow.BlobRenderer.enabled, Is.False, "The existing altitude fade must remain effective.");
            }
            finally { PrefabUtility.UnloadPrefabContents(world); }
        }

        [Test]
        public void RefreshBudgetUpdatesOnlyThreeCertificateFieldsWithoutGeometryOrHierarchyChange()
        {
            byte[] originalWorld = File.ReadAllBytes(WorldPath);
            GameObject world = PrefabUtility.LoadPrefabContents(WorldPath);
            try
            {
                var environment = world.GetComponentInChildren<ProductionEnvironmentRoot>(true);
                var budget = environment.CalculateBudget();
                environment.bakedTriangleCount = budget.triangleCount - 150;
                Assert.That(environment.TryValidateContract(out string failure), Is.False);
                Assert.That(failure, Does.Contain("serialized triangle budget is stale"));
                string before = WithoutBudgetFields(JsonUtility.ToJson(environment));
                var filters = world.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f => f, f => f.sharedMesh);
                var transforms = world.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => (t.localPosition, t.localRotation, t.localScale));
                ProductMenuFlowValidation.RefreshBudgetFields(environment);
                Assert.That(environment.bakedRendererCount, Is.EqualTo(budget.rendererCount));
                Assert.That(environment.bakedTriangleCount, Is.EqualTo(budget.triangleCount));
                Assert.That(environment.bakedMaterialCount, Is.EqualTo(budget.materialCount));
                Assert.That(environment.TryValidateContract(out failure), Is.True, failure);
                Assert.That(WithoutBudgetFields(JsonUtility.ToJson(environment)), Is.EqualTo(before));
                Assert.That(world.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(transforms.Count));
                foreach (var transform in transforms)
                    Assert.That((transform.Key.localPosition, transform.Key.localRotation, transform.Key.localScale), Is.EqualTo(transform.Value));
                foreach (var filter in filters) Assert.That(filter.Key.sharedMesh, Is.SameAs(filter.Value));
                Assert.That(File.ReadAllBytes(WorldPath), Is.EqualTo(originalWorld));
            }
            finally { PrefabUtility.UnloadPrefabContents(world); }
        }

        private static string WithoutBudgetFields(string json) => Regex.Replace(json,
            "\"baked(?:Renderer|Triangle|Material)Count\":-?[0-9]+", "\"budget\":0");
    }
}
#endif
