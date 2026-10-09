#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using System.Reflection;
using System.IO;
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class TesterReadinessEditModeTests
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [TestCase("quest|quest2|cambria|eureka|quest3s")]
        [TestCase("eureka")]
        [TestCase("quest3")]
        public void FinalQuestManifestUsesCanonicalQuest3Only(string originalDevices)
        {
            string source = "<manifest xmlns:android='http://schemas.android.com/apk/res/android'><application><meta-data android:name='com.oculus.supportedDevices' android:value='" + originalDevices + "'/><meta-data android:name='unrelated' android:value='preserve'/></application></manifest>";
            var xml = new System.Xml.XmlDocument();
            xml.LoadXml(ProductReleasePolicy.Quest3Manifest(source));
            var nodes = xml.SelectNodes("/manifest/application/meta-data");
            Assert.That(nodes.Count, Is.EqualTo(2));
            Assert.That(((System.Xml.XmlElement)nodes[0]).GetAttribute("value", "http://schemas.android.com/apk/res/android"), Is.EqualTo("quest3"));
            Assert.That(((System.Xml.XmlElement)nodes[1]).GetAttribute("value", "http://schemas.android.com/apk/res/android"), Is.EqualTo("preserve"));
            Assert.That(ProductReleasePolicy.Quest3Manifest(xml.OuterXml), Is.EqualTo(xml.OuterXml));
        }

        [Test]
        public void ReleaseAgentSanitationRejectsSdkInjectedDummyTokenAndPreservesHost()
        {
            UnityEngine.Object clone = UnityEngine.Object.Instantiate(AssetDatabase.LoadMainAssetAtPath(ProductReleasePolicy.DevAgentAssetPath));
            try
            {
                var asset = new SerializedObject(clone);
                asset.FindProperty("serverAddress").stringValue = "owner-local-host";
                asset.FindProperty("accessToken").stringValue = "test-only-not-a-credential";
                asset.FindProperty("witClientAccessToken").stringValue = "test-only-wit-not-a-credential";
                asset.FindProperty("enabled").boolValue = false;
                asset.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(ProductReleasePolicy.IsSanitizedAgent(asset), Is.False);
                ProductReleasePolicy.SanitizeAgent(asset);
                Assert.That(ProductReleasePolicy.IsSanitizedAgent(asset), Is.True);
                Assert.That(asset.FindProperty("serverAddress").stringValue, Is.EqualTo("owner-local-host"));
            }
            finally { UnityEngine.Object.DestroyImmediate(clone); }
        }

        [Test]
        public void AuthoredDevelopmentAgentTemplateIsDisabledAndContainsNoCredential()
        {
            UnityEngine.Object template = AssetDatabase.LoadMainAssetAtPath(ProductReleasePolicy.DevAgentAssetPath);
            Assert.That(template, Is.Not.Null);
            Assert.That(ProductReleasePolicy.IsSanitizedAgent(new SerializedObject(template)), Is.True,
                "Actual authored template must be sanitized, independently of clone and build-callback tests (values redacted).");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SerializedAgentInspectionRejectsPopulatedBinaryCredential(bool populated)
        {
            UnityEngine.Object clone = UnityEngine.Object.Instantiate(AssetDatabase.LoadMainAssetAtPath(ProductReleasePolicy.DevAgentAssetPath));
            string path = Path.Combine("Library", "agent-inspection-" + Guid.NewGuid().ToString("N") + ".assets");
            try
            {
                var asset = new SerializedObject(clone);
                asset.FindProperty("enabled").boolValue = false;
                asset.FindProperty("accessToken").stringValue = populated ? "fixture-only-never-a-service-token" : string.Empty;
                asset.FindProperty("witClientAccessToken").stringValue = string.Empty;
                asset.ApplyModifiedPropertiesWithoutUndo();
                UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(new[] { clone }, path, false);
                var result = ProductReleasePolicy.InspectSerializedAgentResources(path);
                Assert.That(result.decoded_agent_templates, Is.EqualTo(1));
                Assert.That(result.verified, Is.EqualTo(!populated), "Actual serialized field decoding must reject a nonempty dummy credential.");
                Assert.That(result.serialized_file_sha256.Length, Is.EqualTo(64));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clone);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void SerializedAgentNativeLoaderRejectsPlayerTargetBeforeDeserialization()
        {
            UnityEngine.Object clone = UnityEngine.Object.Instantiate(AssetDatabase.LoadMainAssetAtPath(ProductReleasePolicy.DevAgentAssetPath));
            string path = Path.Combine("Library", "agent-player-target-" + Guid.NewGuid().ToString("N") + ".assets");
            try
            {
                UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(new[] { clone }, path, false);
                byte[] fixture = File.ReadAllBytes(path);
                Assert.That(fixture[11], Is.EqualTo(22), "This regression must use the current v22 format.");
                int engineEnd = Array.IndexOf(fixture, (byte)0, 48);
                Assert.That(engineEnd, Is.GreaterThanOrEqualTo(48));
                byte[] androidTarget = BitConverter.GetBytes((int)BuildTarget.Android);
                Array.Copy(androidTarget, 0, fixture, engineEnd + 1, 4);
                File.WriteAllBytes(path, fixture);
                Assert.Throws<NotSupportedException>(() => ProductReleasePolicy.InspectSerializedAgentResources(path),
                    "Native Editor deserialization of stripped player fields can crash before a managed catch.");
                File.WriteAllBytes(path, new byte[24]);
                Assert.Throws<NotSupportedException>(() => ProductReleasePolicy.InspectSerializedAgentResources(path));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clone);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void BuildPreflightClearsInjectedTemplateBeforeAnyEditorUpdate()
        {
            UnityEngine.Object template = AssetDatabase.LoadMainAssetAtPath(ProductReleasePolicy.DevAgentAssetPath);
            var asset = new SerializedObject(template);
            string host = asset.FindProperty("serverAddress").stringValue;
            MobileTextureSubtarget previousCompression = EditorUserBuildSettings.androidBuildSubtarget;
            try
            {
                EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
                asset.FindProperty("accessToken").stringValue = "fixture-only-sdk-refill-before-first-update";
                asset.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(ProductReleasePolicy.IsSanitizedAgent(asset), Is.False);
                Assert.That(ProductIntegrityValidator.Validate().failures.Any(x => x.Contains("Development-agent template")), Is.True,
                    "The original immediate validator must reject the actual injected template.");
                ProductIntegrityReport report = ProductionBuild.ValidateBuildPreflight();
                Assert.That(report.passed, Is.True, report.ToString());
                asset.Update();
                Assert.That(ProductReleasePolicy.IsSanitizedAgent(asset), Is.True);
                Assert.That(asset.FindProperty("serverAddress").stringValue, Is.EqualTo(host));
            }
            finally
            {
                ProductReleasePolicy.SanitizeAuthoredAgentTemplateBatch();
                EditorUserBuildSettings.androidBuildSubtarget = previousCompression;
            }
        }

        [Test]
        public void ProductSettingsPinSdk34AndConstrainPinnedSdkDeviceList()
        {
            Assert.That(PlayerSettings.Android.targetSdkVersion, Is.EqualTo(AndroidSdkVersions.AndroidApiLevel34));
            var settings = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            UnityEngine.Object clone = UnityEngine.Object.Instantiate(settings.GetFeature<UnityEngine.XR.OpenXR.Features.MetaQuestSupport.MetaQuestFeature>());
            try
            {
                var feature = new SerializedObject(clone);
                var devices = feature.FindProperty("targetDevices");
                for (int i = 0; i < devices.arraySize; i++) devices.GetArrayElementAtIndex(i).FindPropertyRelative("enabled").boolValue = true;
                feature.ApplyModifiedPropertiesWithoutUndo();
                ProductProjectConfigurator.ConfigureQuest3Devices(feature);
                int enabled = 0;
                for (int i = 0; i < devices.arraySize; i++)
                {
                    var device = devices.GetArrayElementAtIndex(i);
                    if (!device.FindPropertyRelative("enabled").boolValue) continue;
                    enabled++;
                    Assert.That(device.FindPropertyRelative("manifestName").stringValue, Is.EqualTo("eureka"));
                }
                Assert.That(enabled, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(clone); }
        }

        [TestCase("before_timestamp_write")]
        [TestCase("before_temporary_write")]
        [TestCase("before_replace")]
        [TestCase("after_replace")]
        public void SeatCalibrationInterruptionRetainsAValidCommittedRecord(string boundary)
        {
            string root = Path.Combine(Path.GetTempPath(), "qfl-seat-" + Guid.NewGuid());
            try
            {
                var oldState = new QuestFlightLab.Runtime.CockpitViewpointCalibrationState { calibrationOffset = new Vector3(.1f, .2f, .3f), calibrationYawDeg = 4 };
                var newState = new QuestFlightLab.Runtime.CockpitViewpointCalibrationState { calibrationOffset = new Vector3(.4f, .5f, .6f), calibrationYawDeg = 8 };
                QuestFlightLab.Runtime.CockpitViewpointPersistence.SaveCurrent(oldState, root);
                Assert.Throws<IOException>(() => QuestFlightLab.Runtime.CockpitViewpointPersistence.SaveCurrent(newState, root, step => { if (step == boundary) throw new IOException("Injected interruption"); }));
                Assert.That(QuestFlightLab.Runtime.CockpitViewpointPersistence.TryLoadCurrent(out var restored, out _, out string error, root), Is.True, error);
                Assert.That(restored.calibrationOffset, Is.EqualTo(boundary == "after_replace" ? newState.calibrationOffset : oldState.calibrationOffset));
                Assert.That(restored.calibrationYawDeg, Is.EqualTo(boundary == "after_replace" ? 8 : 4));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void SeatCalibrationRecoversValidatedBackupAndRejectsWrongAircraft()
        {
            string root = Path.Combine(Path.GetTempPath(), "qfl-seat-backup-" + Guid.NewGuid());
            try
            {
                var state = new QuestFlightLab.Runtime.CockpitViewpointCalibrationState { calibrationOffset = new Vector3(.1f, .2f, .3f), calibrationYawDeg = 4 };
                string current = QuestFlightLab.Runtime.CockpitViewpointPersistence.SaveCurrent(state, root);
                state.calibrationOffset = Vector3.one;
                QuestFlightLab.Runtime.CockpitViewpointPersistence.SaveCurrent(state, root);
                File.WriteAllText(current, "{truncated");
                Assert.That(QuestFlightLab.Runtime.CockpitViewpointPersistence.TryLoadCurrent(out var restored, out string path, out string error, root), Is.True, error);
                Assert.That(path, Does.EndWith(".bak"));
                Assert.That(error, Does.Contain("Recovered validated"));
                Assert.That(restored.calibrationYawDeg, Is.EqualTo(4));
                File.WriteAllText(current + ".bak", "{\"schemaVersion\":5,\"aircraftId\":\"different_aircraft\"}");
                Assert.That(QuestFlightLab.Runtime.CockpitViewpointPersistence.TryLoadCurrent(out _, out _, out _, root), Is.False);
                state.calibrationOffset = new Vector3(float.NaN, 0, 0);
                Assert.Throws<InvalidDataException>(() => QuestFlightLab.Runtime.CockpitViewpointPersistence.SaveCurrent(state, root));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void ResetCalibrationDoesNotResurrectItsBackupOnNextLaunch()
        {
            string root = Path.Combine(Path.GetTempPath(), "qfl-seat-delete-" + Guid.NewGuid());
            try
            {
                var state = new QuestFlightLab.Runtime.CockpitViewpointCalibrationState();
                QuestFlightLab.Runtime.CockpitViewpointPersistence.SaveCurrent(state, root);
                QuestFlightLab.Runtime.CockpitViewpointPersistence.SaveCurrent(state, root);
                Assert.That(QuestFlightLab.Runtime.CockpitViewpointPersistence.DeleteCurrent(out _, out string error, root), Is.True, error);
                Assert.That(QuestFlightLab.Runtime.CockpitViewpointPersistence.TryLoadCurrent(out _, out _, out _, root), Is.False);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void TouchHelpAdvertisesTheAuthoredHoldLifecycleWithoutAnUnsupportedToggle()
        {
            var world = PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);
            try
            {
                var nav = world.GetComponentInChildren<ProductAppNavigation>(true);
                typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav, ProductAppNavigation.Page.ControlDiagram);
                typeof(ProductAppNavigation).GetMethod("Refresh", Private).Invoke(nav, null);
                var actions = (int[])typeof(ProductAppNavigation).GetField("actions", Private).GetValue(nav);
                CollectionAssert.DoesNotContain(actions, 133);
                var detail = (TextMesh)typeof(ProductAppNavigation).GetField("detail", Private).GetValue(nav);
                Assert.That(detail.text, Does.Contain("hold a grip"));
                Assert.That(detail.text, Does.Contain("release it to let go"));
            }
            finally { PrefabUtility.UnloadPrefabContents(world); }
        }
        [Test] public void CombinedTouchBrakesDoNotRemapExternalControllerBrakes()
        {
            QuestFlightLab.Input.ProductControlInput.ResolveTouchBrakes(.8f,0,0,0,true,out float left,out float right);
            Assert.That(left,Is.EqualTo(.8f));Assert.That(right,Is.Zero);
            QuestFlightLab.Input.ProductControlInput.ResolveTouchBrakes(0,0,.6f,0,true,out left,out right);
            Assert.That(left,Is.EqualTo(.6f));Assert.That(right,Is.EqualTo(.6f));
        }
        [Test] public void AdjustmentStopsAtLimitsAndRudderKeepsFullAuthority()
        {
            Assert.That(ProductComfortPreferences.Adjust(.24f,.04f,.04f,.24f),Is.EqualTo(.24f));
            Assert.That(ProductComfortPreferences.Adjust(.04f,-.04f,.04f,.24f),Is.EqualTo(.04f));
            foreach(float dz in new[]{.04f,.12f,.24f})foreach(float exponent in new[]{1f,1.5f,2f})
            {Assert.That(ProductComfortPreferences.Axis(-1,dz,exponent),Is.EqualTo(-1));Assert.That(ProductComfortPreferences.Axis(1,dz,exponent),Is.EqualTo(1));Assert.That(ProductComfortPreferences.Axis(dz,dz,exponent),Is.Zero);}
        }
        [Test] public void SteeringAssignmentsRemainDistinctAndRestoreTheirSavedValue()
        {
            string key="QuestFlight.LeftStickSteering";bool had=PlayerPrefs.HasKey(key);int previous=PlayerPrefs.GetInt(key);
            try
            {
                foreach(bool left in new[]{false,true})
                {ProductComfortPreferences.SetFlag("LeftStickSteering",left);Assert.That(ProductComfortPreferences.RudderController,Is.Not.EqualTo(ProductComfortPreferences.TrimController));Assert.That(ProductComfortPreferences.BindingSummary,Does.Contain(left?"left stick: rudder":"right stick: rudder"));}
            }
            finally{if(had)PlayerPrefs.SetInt(key,previous);else PlayerPrefs.DeleteKey(key);PlayerPrefs.Save();}
        }
        [Test] public void AuthoredMapAndNewPagesHaveReferencesAndExits()
        {
            var world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);float time=Time.timeScale;
            try
            {
                var nav=world.GetComponentInChildren<ProductAppNavigation>(true);
                foreach(var page in new[]{ProductAppNavigation.Page.Bindings,ProductAppNavigation.Page.InputPreview,ProductAppNavigation.Page.InstrumentHelp})
                {
                    typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,page);
                    typeof(ProductAppNavigation).GetMethod("Refresh",Private).Invoke(nav,null);
                    Assert.That(((int[])typeof(ProductAppNavigation).GetField("actions",Private).GetValue(nav)).Contains(5),Is.True,page.ToString());
                }
                var e=world.GetComponentInChildren<ProductFlightExperience>(true);
                foreach(string name in new[]{"flownPath","selectedRoute","lessonDestination"})Assert.That(typeof(ProductFlightExperience).GetField(name,Private).GetValue(e),Is.Not.Null,name);
            }
            finally{PrefabUtility.UnloadPrefabContents(world);Time.timeScale=time;}
        }
        [Test] public void OffRunwayTouchdownGetsAlignmentAdviceAndTraceRemainsBounded()
        {
            var world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);float time=Time.timeScale;
            try
            {
                var mode=world.GetComponentInChildren<ProductFlightModeCoordinator>(true);
                typeof(ProductFlightModeCoordinator).GetProperty("State").SetValue(mode,ProductFlightModeCoordinator.FlightModeState.FreeFlight);
                typeof(ProductTrainingController).GetProperty("IsGuestSession").SetValue(mode.AuthoredTraining,true);
                var e=world.GetComponentInChildren<ProductFlightExperience>(true);
                typeof(ProductFlightExperience).GetField("active",Private).SetValue(e,new ProductFlightExperience.Record{mode="QA manual flight"});
                var sample=typeof(ProductFlightExperience).GetMethod("SampleFlight",Private);
                for(int n=0;n<300;n++)sample.Invoke(e,new object[]{new FlightDynamicsState{positionUnityMeters=new Vector3(n*30,300,0),altitudeAglMeters=300,calibratedAirspeedKnots=90},.5f});
                typeof(ProductFlightExperience).GetMethod("RecordTouchdown",Private).Invoke(e,new object[]{new FlightDynamicsState{positionUnityMeters=new Vector3(0,0,100),weightOnWheels=true,headingDegrees=90}});
                e.FinishFlight("QA finished");
                Assert.That(e.LastRecord.path.Length,Is.EqualTo(256));Assert.That(e.LastRecord.advice,Does.Contain("alignment"));Assert.That(e.LastRecord.observations,Does.Contain("outside the modeled runway"));
            }
            finally{PrefabUtility.UnloadPrefabContents(world);Time.timeScale=time;}
        }
        [Test] public void ObservationFlightDoesNotReceiveFalsePilotGrade()
        {
            var world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);
            try
            {
                var mode=world.GetComponentInChildren<ProductFlightModeCoordinator>(true);typeof(ProductTrainingController).GetProperty("IsGuestSession").SetValue(mode.AuthoredTraining,true);
                var e=world.GetComponentInChildren<ProductFlightExperience>(true);typeof(ProductFlightExperience).GetField("active",Private).SetValue(e,new ProductFlightExperience.Record{mode="Demo",assisted=true});
                e.FinishFlight("Tour complete");Assert.That(e.LastRecord.observations,Does.Contain("not evaluated"));
            }
            finally{PrefabUtility.UnloadPrefabContents(world);}
        }
    }
}
#endif
