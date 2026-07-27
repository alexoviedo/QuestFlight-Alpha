using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Environment.Editor.Tests
{
    public sealed class KbduTrafficV1EditModeTests
    {
        private const string TrafficPrefab =
            "Assets/Production/Environment/TrafficV1/KBDU_TrafficRoot.prefab";
        private const string ProductPrefab =
            "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        private const string Manifest =
            "Assets/Production/Environment/TrafficV1/Data/KBDU_TrafficManifestV1.json";
        private const string SharedMaterial =
            "Assets/Production/Environment/PhotorealTerrainV1/KBDU_ParkedAircraft.mat";

        [Test]
        public void TrafficRoot_IsSerializedOnceAndUsesNoPerVehicleHierarchy()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrafficPrefab);
            Assert.That(prefab, Is.Not.Null);
            KbduTrafficRuntime runtime = prefab.GetComponent<KbduTrafficRuntime>();
            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime.TryValidateContract(out string report), Is.True, report);
            Assert.That(runtime.AuthoredParkedCount, Is.EqualTo(30));
            Assert.That(runtime.AuthoredDynamicCount, Is.EqualTo(12));
            Assert.That(runtime.AuthoredRouteCount, Is.EqualTo(12));
            Assert.That(runtime.AuthoredMaximumDrawCount, Is.EqualTo(4));
            Assert.That(runtime.AuthoredMaximumVisibleTriangles, Is.LessThanOrEqualTo(8000));
            Assert.That(runtime.PerVehicleGameObjects, Is.False);
            Assert.That(runtime.RealtimeVehicleShadows, Is.False);
            Assert.That(runtime.DeterministicMotion, Is.True);
            Assert.That(prefab.transform.childCount, Is.Zero);
            Assert.That(prefab.GetComponentsInChildren<Renderer>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
        }

        [Test]
        public void TrafficMeshes_AreDistinctSingleSubmeshAndShareExistingMaterial()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrafficPrefab);
            KbduTrafficRuntime runtime = prefab.GetComponent<KbduTrafficRuntime>();
            Material expected = AssetDatabase.LoadAssetAtPath<Material>(SharedMaterial);
            Assert.That(runtime.SharedOpaqueMaterial, Is.SameAs(expected));
            Assert.That(expected.enableInstancing, Is.True);
            Assert.That(runtime.NearArchetypeMeshes, Has.Length.EqualTo(3));
            Assert.That(runtime.NearArchetypeMeshes.Distinct().Count(), Is.EqualTo(3));
            Assert.That(runtime.NearArchetypeMeshes.All(mesh => mesh != null && mesh.subMeshCount == 1), Is.True);
            Assert.That(runtime.NearArchetypeMeshes.All(mesh =>
                mesh.bounds.size.x >= 1.7f && mesh.bounds.size.x <= 2.25f), Is.True);
            Assert.That(runtime.NearArchetypeMeshes.All(mesh =>
                mesh.bounds.size.z >= 4.1f && mesh.bounds.size.z <= 5.0f), Is.True);
            Assert.That(runtime.NearArchetypeMeshes.All(mesh => mesh.GetIndexCount(0) / 3u <= 260u), Is.True);
            Assert.That(runtime.MidDistanceMesh, Is.Not.Null);
            Assert.That(runtime.MidDistanceMesh.GetIndexCount(0) / 3u, Is.LessThanOrEqualTo(30u));
        }

        [Test]
        public void ProductEnvironment_BindsExactlyOneTrafficRoot()
        {
            GameObject product = AssetDatabase.LoadAssetAtPath<GameObject>(ProductPrefab);
            ProductionEnvironmentRoot environment = product.GetComponent<ProductionEnvironmentRoot>();
            Assert.That(environment, Is.Not.Null);
            Assert.That(environment.trafficRoot, Is.Not.Null);
            Assert.That(environment.trafficRoot.name, Is.EqualTo("TrafficRoot"));
            Assert.That(
                product.GetComponentsInChildren<KbduTrafficRuntime>(true),
                Has.Length.EqualTo(1));
            Assert.That(
                product.GetComponentsInChildren<KbduTrafficRuntime>(true)[0].transform,
                Is.SameAs(environment.trafficRoot));
        }

        [Test]
        public void CompiledManifest_PreservesParkingEvidenceAndRoadCorridors()
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Manifest);
            Assert.That(asset, Is.Not.Null);
            ManifestRecord manifest = JsonUtility.FromJson<ManifestRecord>(asset.text);
            Assert.That(manifest.schema, Is.EqualTo("questflight.kbdu-traffic-v1"));
            Assert.That(manifest.status, Is.EqualTo("BOUNDED_AUTHORED_BENCHMARK"));
            Assert.That(manifest.parked, Has.Length.EqualTo(30));
            Assert.That(manifest.routes, Has.Length.EqualTo(12));
            Assert.That(manifest.moving, Has.Length.EqualTo(12));
            Assert.That(manifest.parked.All(item =>
                item.source_pixel != null && item.source_pixel.Length == 2 &&
                item.source_axis_pixels != null && item.source_axis_pixels.Length == 4), Is.True);
            Assert.That(manifest.routes.All(route =>
                route.samples != null &&
                route.samples.Length >= 20 &&
                route.lane_offset_meters + 1.0f <= route.road_width_meters * 0.5f), Is.True);
            Assert.That(
                manifest.routes.Select(route => route.road_name).Distinct().OrderBy(value => value).ToArray(),
                Is.EqualTo(new[]
                {
                    "55th Street",
                    "Diagonal Highway",
                    "Jay Road",
                    "North 61st Street",
                    "Valmont Road"
                }));
            Assert.That(manifest.routes.All(route =>
                route.highway_class == "trunk" ||
                route.highway_class == "secondary" ||
                route.highway_class == "tertiary" ||
                route.highway_class == "residential"), Is.True);
        }

        [Test]
        public void TrafficSourceAndLicenseRecords_AreCommittedAndExplicit()
        {
            string unityProject = Directory.GetParent(Application.dataPath)?.FullName;
            string repository = unityProject == null
                ? null
                : Directory.GetParent(unityProject)?.FullName;
            Assert.That(repository, Is.Not.Null);
            string source = Path.Combine(repository, "tools", "kbdu_traffic", "traffic_sources_v1.json");
            string compiler = Path.Combine(repository, "tools", "kbdu_traffic", "build_traffic_manifest.py");
            string validator = Path.Combine(repository, "tools", "kbdu_traffic", "validate_traffic_manifest.py");
            Assert.That(File.Exists(source), Is.True);
            Assert.That(File.Exists(compiler), Is.True);
            Assert.That(File.Exists(validator), Is.True);
            string sourceText = File.ReadAllText(source);
            Assert.That(sourceText, Does.Contain("© OpenStreetMap contributors"));
            Assert.That(sourceText, Does.Contain("Open Database License 1.0"));
            Assert.That(sourceText, Does.Contain("Project-owned"));
            Assert.That(sourceText, Does.Contain("143c4946c06464dbdea1567cd5fe284acc4c5ba7718a38900a2e83a8eccbfb53"));
        }

        [Serializable]
        private sealed class ManifestRecord
        {
            public string schema;
            public string status;
            public ParkedRecord[] parked;
            public RouteRecord[] routes;
            public MovingRecord[] moving;
        }

        [Serializable]
        private sealed class ParkedRecord
        {
            public int[] source_pixel;
            public int[] source_axis_pixels;
        }

        [Serializable]
        private sealed class RouteRecord
        {
            public string road_name;
            public string highway_class;
            public float lane_offset_meters;
            public float road_width_meters;
            public SampleRecord[] samples;
        }

        [Serializable]
        private sealed class MovingRecord
        {
            public int route_index;
        }

        [Serializable]
        private sealed class SampleRecord
        {
            public float x;
            public float y;
            public float z;
            public float distance;
        }
    }
}
