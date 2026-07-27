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
            "Assets/Production/Environment/TrafficV1/Materials/KBDU_TrafficVehicle.mat";

        [Test]
        public void TrafficRoot_IsSerializedOnceAndUsesNoPerVehicleHierarchy()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrafficPrefab);
            Assert.That(prefab, Is.Not.Null);
            KbduTrafficRuntime runtime = prefab.GetComponent<KbduTrafficRuntime>();
            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime.TryValidateContract(out string report), Is.True, report);
            Assert.That(runtime.AuthoredParkedCount, Is.EqualTo(40));
            Assert.That(runtime.AuthoredDynamicCount, Is.GreaterThanOrEqualTo(600));
            Assert.That(runtime.AuthoredRouteCount, Is.GreaterThanOrEqualTo(300));
            Assert.That(runtime.AuthoredMaximumDrawCount, Is.EqualTo(5));
            Assert.That(
                runtime.AuthoredMaximumVisibleTriangles,
                Is.LessThanOrEqualTo(KbduTrafficRuntime.MaximumVisibleTriangles));
            Assert.That(runtime.PerVehicleGameObjects, Is.False);
            Assert.That(runtime.RealtimeVehicleShadows, Is.False);
            Assert.That(runtime.DeterministicMotion, Is.True);
            Assert.That(prefab.transform.childCount, Is.Zero);
            Assert.That(prefab.GetComponentsInChildren<Renderer>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
        }

        [Test]
        public void TrafficMeshes_AreDistinctOptimizedCc0DerivativesWithOneMaterial()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrafficPrefab);
            KbduTrafficRuntime runtime = prefab.GetComponent<KbduTrafficRuntime>();
            Material expected = AssetDatabase.LoadAssetAtPath<Material>(SharedMaterial);
            Assert.That(runtime.SharedOpaqueMaterial, Is.SameAs(expected));
            Assert.That(expected.enableInstancing, Is.True);
            Assert.That(expected.shader.name, Is.EqualTo("QuestFlight/KBDU Traffic Vehicle"));
            Assert.That(expected.GetTexture("_SurfaceDetail"), Is.Not.Null);
            Assert.That(runtime.NearArchetypeMeshes, Has.Length.EqualTo(5));
            Assert.That(runtime.NearArchetypeMeshes.Distinct().Count(), Is.EqualTo(5));
            Assert.That(runtime.NearArchetypeMeshes.All(mesh => mesh != null && mesh.subMeshCount == 1), Is.True);
            Assert.That(runtime.NearArchetypeMeshes.All(mesh =>
                mesh.bounds.size.x >= 1.7f && mesh.bounds.size.x <= 2.05f), Is.True);
            Assert.That(runtime.NearArchetypeMeshes.All(mesh =>
                mesh.bounds.size.y >= 1.4f && mesh.bounds.size.y <= 2.15f), Is.True);
            Assert.That(runtime.NearArchetypeMeshes.All(mesh =>
                mesh.bounds.size.z >= 4.3f && mesh.bounds.size.z <= 5.4f), Is.True);
            Assert.That(runtime.NearArchetypeMeshes.All(mesh =>
                mesh.GetIndexCount(0) / 3u >= 500u &&
                mesh.GetIndexCount(0) / 3u <= 1100u), Is.True);
            Assert.That(runtime.NearArchetypeMeshes.All(mesh =>
                mesh.colors32 != null &&
                mesh.colors32.Length == mesh.vertexCount), Is.True,
                "Every optimized vehicle needs semantic body/glass/tire/light color data.");
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
            Assert.That(manifest.status, Is.EqualTo("STREAMED_MAJOR_ROAD_NETWORK"));
            Assert.That(manifest.parked, Has.Length.EqualTo(40));
            Assert.That(manifest.routes.Length, Is.GreaterThanOrEqualTo(300));
            Assert.That(manifest.moving.Length, Is.GreaterThanOrEqualTo(600));
            Assert.That(manifest.parked.All(item =>
                item.source_pixel != null && item.source_pixel.Length == 2 &&
                item.source_axis_pixels != null && item.source_axis_pixels.Length == 4 &&
                item.archetype >= 0 && item.archetype < 5 &&
                item.paint_index >= 0 && item.paint_index < 8), Is.True);
            Assert.That(manifest.parked.Count(item =>
                item.x >= -500f && item.x <= 500f &&
                item.z >= -600f && item.z <= 100f), Is.GreaterThanOrEqualTo(20),
                "The normal cockpit/takeoff corridor must contain visible parked traffic.");
            Assert.That(manifest.routes.All(route =>
                route.samples != null &&
                route.samples.Length >= 20 &&
                route.lane_offset_meters + 1.125f + 0.35f <=
                route.road_width_meters * 0.5f + 0.001f), Is.True,
                "Traffic lanes must retain vehicle-width and edge clearance.");
            string[] roadNames = manifest.routes
                .Select(route => route.road_name)
                .Distinct()
                .ToArray();
            Assert.That(roadNames, Does.Contain("Airport Boulevard"));
            Assert.That(roadNames, Does.Contain("Diagonal Highway"));
            Assert.That(roadNames, Does.Contain("Jay Road"));
            Assert.That(roadNames, Does.Contain("Valmont Road"));
            Assert.That(manifest.routes.All(route =>
                route.highway_class == "trunk" ||
                route.highway_class == "primary" ||
                route.highway_class == "secondary" ||
                route.highway_class == "tertiary" ||
                route.highway_class == "residential"), Is.True);
            Assert.That(manifest.routes.Count(route =>
                route.road_name == "Airport Boulevard"), Is.EqualTo(2),
                "Moving traffic must occupy the normal airport demo corridor.");
            int[] trunkRoutes = manifest.routes
                .Select((route, index) => new { route, index })
                .Where(item => item.route.highway_class == "trunk")
                .Select(item => item.index)
                .ToArray();
            Assert.That(manifest.moving.Count(item =>
                trunkRoutes.Contains(item.route_index)), Is.GreaterThanOrEqualTo(200),
                "Diagonal Highway must read as an active traffic corridor.");
            Assert.That(manifest.moving.All(item =>
                item.desired_speed_scale >= 0.8f &&
                item.desired_speed_scale <= 1.12f &&
                item.time_headway_seconds >= 0.9f &&
                item.time_headway_seconds <= 2.4f &&
                item.minimum_gap_meters >= 2f &&
                item.minimum_gap_meters <= 4f &&
                item.maximum_acceleration_mps2 >= 0.8f &&
                item.maximum_acceleration_mps2 <= 2.4f &&
                item.comfortable_deceleration_mps2 >= 1.8f &&
                item.comfortable_deceleration_mps2 <= 3f &&
                item.emergency_deceleration_mps2 >= 5.5f &&
                item.emergency_deceleration_mps2 <= 7.5f &&
                item.acceleration_response_seconds >= 0.25f &&
                item.acceleration_response_seconds <= 0.55f &&
                item.vehicle_length_meters >= 4.3f &&
                item.vehicle_length_meters <= 5.4f &&
                item.archetype >= 0 && item.archetype < 5 &&
                item.paint_index >= 0 && item.paint_index < 8), Is.True,
                "Driver profiles must remain realistic, bounded, and collision-safe.");
            Assert.That(manifest.moving.Select(item => item.archetype).Distinct().Count(),
                Is.EqualTo(5), "All market-relevant body styles must appear.");
            Assert.That(manifest.moving.Select(item => item.paint_index).Distinct().Count(),
                Is.EqualTo(8), "All market-weighted paint colors must appear.");
            Assert.That(
                manifest.moving.Count(item => item.paint_index == 4) /
                (float)manifest.moving.Length,
                Is.LessThanOrEqualTo(0.13f),
                "Blue must not dominate the visible fleet.");
            Assert.That(manifest.moving
                .Select(item => (
                    item.desired_speed_scale,
                    item.time_headway_seconds,
                    item.minimum_gap_meters))
                .Distinct()
                .Count(), Is.GreaterThanOrEqualTo(100),
                "Traffic needs stable deterministic driver variation.");

            int variableGapRoutes = 0;
            foreach (IGrouping<int, MovingRecord> group in
                manifest.moving.GroupBy(item => item.route_index))
            {
                MovingRecord[] vehicles = group
                    .OrderBy(item => item.initial_distance_meters)
                    .ToArray();
                if (vehicles.Length <= 1)
                {
                    continue;
                }
                float routeLength = manifest.routes[group.Key]
                    .samples.Last().distance;
                float[] gaps = new float[vehicles.Length];
                for (int index = 0; index < vehicles.Length; index++)
                {
                    MovingRecord follower = vehicles[index];
                    MovingRecord leader = vehicles[(index + 1) % vehicles.Length];
                    float centerGap = Mathf.Repeat(
                        leader.initial_distance_meters -
                        follower.initial_distance_meters,
                        routeLength);
                    gaps[index] = centerGap -
                        0.5f * (
                            follower.vehicle_length_meters +
                            leader.vehicle_length_meters);
                }
                Assert.That(gaps.Min(), Is.GreaterThanOrEqualTo(13.99f));
                if (gaps.Max() - gaps.Min() >= 10f)
                {
                    variableGapRoutes++;
                }
            }
            Assert.That(variableGapRoutes, Is.GreaterThanOrEqualTo(100),
                "Traffic must start in varied, collision-safe platoons rather than a fixed lattice.");
        }

        [Test]
        public void CarFollowing_HeadwayRespondsToSpeedAndClosingRate()
        {
            float slowGap = KbduTrafficRuntime.CalculateDesiredGap(
                8f, 0f, 1.5f, 2.5f, 1.8f, 2.4f);
            float highwayGap = KbduTrafficRuntime.CalculateDesiredGap(
                20f, 0f, 1.5f, 2.5f, 1.8f, 2.4f);
            float closingGap = KbduTrafficRuntime.CalculateDesiredGap(
                20f, 5f, 1.5f, 2.5f, 1.8f, 2.4f);
            Assert.That(highwayGap, Is.GreaterThan(slowGap));
            Assert.That(closingGap, Is.GreaterThan(highwayGap));
        }

        [Test]
        public void CarFollowing_BrakesForLeaderAndDoesNotBrakeOnOpenGap()
        {
            float freeRoad = KbduTrafficRuntime.CalculateCarFollowingAcceleration(
                12f, 12f, 18f, 200f, 1.4f, 2.5f, 1.8f, 2.4f, 6.5f);
            float closingOnLeader = KbduTrafficRuntime.CalculateCarFollowingAcceleration(
                18f, 10f, 20f, 18f, 1.4f, 2.5f, 1.8f, 2.4f, 6.5f);
            Assert.That(freeRoad, Is.GreaterThan(0f));
            Assert.That(closingOnLeader, Is.LessThan(0f));
            Assert.That(closingOnLeader, Is.GreaterThanOrEqualTo(-6.5f));
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
            string derivatives = Path.Combine(
                repository,
                "tools",
                "kbdu_traffic",
                "traffic_vehicle_derivatives_v1.json");
            string preparer = Path.Combine(
                repository,
                "tools",
                "kbdu_traffic",
                "prepare_cc0_vehicle_assets.py");
            Assert.That(File.Exists(source), Is.True);
            Assert.That(File.Exists(compiler), Is.True);
            Assert.That(File.Exists(validator), Is.True);
            Assert.That(File.Exists(derivatives), Is.True);
            Assert.That(File.Exists(preparer), Is.True);
            string sourceText = File.ReadAllText(source);
            Assert.That(sourceText, Does.Contain("© OpenStreetMap contributors"));
            Assert.That(sourceText, Does.Contain("Open Database License 1.0"));
            Assert.That(sourceText, Does.Contain("RGSDev Free Low Poly Vehicles Pack"));
            Assert.That(sourceText, Does.Contain("CC0 1.0 Universal"));
            Assert.That(sourceText, Does.Contain("9091e478bd211a7cf8b562f04e8f061a2188d323e2fa0878f3f1641e5e67c800"));
            Assert.That(sourceText, Does.Contain("Project-owned"));
            Assert.That(sourceText, Does.Contain("143c4946c06464dbdea1567cd5fe284acc4c5ba7718a38900a2e83a8eccbfb53"));
            Assert.That(
                Directory.GetFiles(
                    Path.Combine(
                        Application.dataPath,
                        "Production",
                        "Environment",
                        "TrafficV1"),
                    "*.fbx",
                    SearchOption.AllDirectories),
                Is.Empty,
                "Raw or prepared interchange sources must stay outside product assets.");
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
            public float x;
            public float z;
            public int archetype;
            public int paint_index;
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
            public int archetype;
            public int paint_index;
            public float initial_distance_meters;
            public float desired_speed_scale;
            public float time_headway_seconds;
            public float minimum_gap_meters;
            public float maximum_acceleration_mps2;
            public float comfortable_deceleration_mps2;
            public float emergency_deceleration_mps2;
            public float acceleration_response_seconds;
            public float vehicle_length_meters;
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
