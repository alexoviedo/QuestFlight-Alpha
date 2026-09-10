using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace QuestFlightLab.Environment.Editor.Tests
{
    public sealed class KbduTrafficV2Gate2EditModeTests
    {
        private const string TrafficPrefab =
            "Assets/Production/Environment/TrafficV1/KBDU_TrafficRoot.prefab";
        private const string ProductPrefab =
            "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        private const string RuntimeSource =
            "Assets/Scripts/Environment/KbduTrafficV2Runtime.cs";

        [Test]
        public void RoadAuthority_AcceptsGitAndWindowsLineEndingsButRejectsChangedContent()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TrafficPrefab));
            var runtime = instance.GetComponent<KbduTrafficV2Runtime>();
            var source = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Production/Environment/GradeSeparatedRoadsV1/Data/KBDU_GradeSeparatedRoadAuthorityV1.json");
            TextAsset variant = null;
            try
            {
                foreach (string text in new[] { source.text.Replace("\r\n", "\n"),
                    source.text.Replace("\r\n", "\n").Replace("\n", "\r\n") })
                {
                    variant = new TextAsset(text);
                    runtime.ConfigureGradeSeparatedRoadAuthority(variant);
                    Assert.That(runtime.TryValidateContract(out string report), Is.True, report);
                    Object.DestroyImmediate(variant);
                }
                variant = new TextAsset(source.text + " ");
                runtime.ConfigureGradeSeparatedRoadAuthority(variant);
                Assert.That(runtime.TryValidateContract(out string failure), Is.False);
                Assert.That(failure, Does.Contain("hash mismatch"));
            }
            finally
            {
                Object.DestroyImmediate(instance);
                if (variant != null) Object.DestroyImmediate(variant);
            }
        }

        [Test]
        public void SerializedShippingScene_ValidatesTrafficAndAdvancesVehicles()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity", OpenSceneMode.Single);
            var runtime = scene.GetRootGameObjects().SelectMany(root =>
                root.GetComponentsInChildren<KbduTrafficV2Runtime>(true)).Single();
            Assert.That(runtime.TryValidateContract(out string report), Is.True, report);
            bool moved = false;
            for (int corridor = 0; corridor < 100 && !moved; corridor++)
                for (int direction = 0; direction < 2 && !moved; direction++)
                    for (int second = 0; second < 120 && !moved; second++)
                        if (runtime.TryEvaluateCorridor(corridor, direction, second, out Vector3 a, out _) &&
                            runtime.TryEvaluateCorridor(corridor, direction, second + 1, out Vector3 b, out _))
                            moved = Vector3.Distance(a, b) > 1f;
            Assert.That(moved, Is.True, "No authored vehicle moved over one second in the shipping scene.");
        }

        [Test]
        public void AuthoredTrafficRoot_ReplacesOnlyV1Motion()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrafficPrefab);
            Assert.That(prefab, Is.Not.Null);
            KbduTrafficRuntime v1 = prefab.GetComponent<KbduTrafficRuntime>();
            KbduTrafficV2Runtime v2 = prefab.GetComponent<KbduTrafficV2Runtime>();
            Assert.That(v1, Is.Not.Null);
            Assert.That(v2, Is.Not.Null);
            Assert.That(v1.MovingTrafficEnabled, Is.False);
            Assert.That(v1.AuthoredParkedCount, Is.EqualTo(24347));
            Assert.That(v2.TryValidateContract(out string report), Is.True, report);
            Assert.That(v2.AcceptedVehicleAssets, Is.SameAs(v1));
            Assert.That(v2.AuthoredCorridorCount, Is.GreaterThan(100));
            Assert.That(v2.AuthoredMaximumDrawCount,
                Is.EqualTo(KbduTrafficV2Runtime.MaximumDrawCalls));
            Assert.That(prefab.transform.childCount, Is.Zero);
            Assert.That(prefab.GetComponentsInChildren<Renderer>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
        }

        [Test]
        public void ProductEnvironment_BindsOneV1ParkedAndOneV2MovingRuntime()
        {
            GameObject product = AssetDatabase.LoadAssetAtPath<GameObject>(ProductPrefab);
            Assert.That(product, Is.Not.Null);
            ProductionEnvironmentRoot environment =
                product.GetComponent<ProductionEnvironmentRoot>();
            Assert.That(environment, Is.Not.Null);
            Assert.That(product.GetComponentsInChildren<KbduTrafficRuntime>(true),
                Has.Length.EqualTo(1));
            Assert.That(product.GetComponentsInChildren<KbduTrafficV2Runtime>(true),
                Has.Length.EqualTo(1));
            Assert.That(environment.TryValidateContract(out string report), Is.True, report);
        }

        [Test]
        public void ExactFullMapManifestAndProvenance_AreHashBound()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrafficPrefab);
            KbduTrafficV2Runtime v2 = prefab.GetComponent<KbduTrafficV2Runtime>();
            Assert.That(Sha256(v2.TrajectoryManifest.bytes),
                Is.EqualTo(KbduTrafficV2Runtime.ExpectedManifestSha256));
            Assert.That(Sha256(v2.SourceAndLicenseRecord.bytes),
                Is.EqualTo(KbduTrafficV2Runtime.ExpectedProvenanceSha256));
            Assert.That(v2.TrajectoryManifest.bytes.Length, Is.GreaterThan(1000000));
            Assert.That(v2.SourceAndLicenseRecord.text,
                Does.Contain("Open Database License 1.0 (ODbL-1.0)"));
            Assert.That(v2.SourceAndLicenseRecord.text,
                Does.Contain("© OpenStreetMap contributors"));
        }

        [Test]
        public void ContinuousSchedule_HasAnEvaluableAuthoredCorridor()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrafficPrefab);
            KbduTrafficV2Runtime v2 = prefab.GetComponent<KbduTrafficV2Runtime>();
            bool evaluated = false;
            Vector3 position = default;
            for (int corridor = 0; corridor < Mathf.Min(32, v2.AuthoredCorridorCount) && !evaluated;
                corridor++)
            {
                for (int direction = 0; direction < 2 && !evaluated; direction++)
                {
                    for (int second = 0; second <= 240 && !evaluated; second++)
                    {
                        evaluated = v2.TryEvaluateCorridor(
                            corridor, direction, second, out position, out _);
                    }
                }
            }
            Assert.That(evaluated, Is.True, "No continuous full-map departure was evaluable.");
            Assert.That(position.y, Is.Not.EqualTo(0f));
        }

        [Test]
        public void RuntimeSource_HasNoHierarchyBootstrapOrCameraWrites()
        {
            string source = File.ReadAllText(RuntimeSource);
            Assert.That(source, Does.Not.Contain("new GameObject"));
            Assert.That(source, Does.Not.Contain("Camera.main"));
            Assert.That(source, Does.Not.Contain("camera.transform.position ="));
            Assert.That(source, Does.Not.Contain("transform.SetParent"));
            Assert.That(source, Does.Not.Contain("AddComponent"));
            Assert.That(source, Does.Contain("DrawMeshInstancedLimit = 1023"));
            Assert.That(source, Does.Contain("for (int offset = 0; offset < count; offset += DrawMeshInstancedLimit)"));
            Assert.That(source, Does.Contain("CONTINUOUS_DETERMINISTIC_DEPARTURE_STREAMS"));
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(bytes).Select(item => item.ToString("x2")));
            }
        }
    }
}
