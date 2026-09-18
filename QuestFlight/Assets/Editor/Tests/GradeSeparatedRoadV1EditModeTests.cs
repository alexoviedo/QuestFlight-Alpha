using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QuestFlightLab.EditorTests
{
    public sealed class GradeSeparatedRoadV1EditModeTests
    {
        private const string AuthorityPath =
            "Assets/Production/Environment/GradeSeparatedRoadsV1/Data/KBDU_GradeSeparatedRoadAuthorityV1.json";
        private const string TerrainPath =
            "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset";
        private const string ScenePath = "Assets/Scenes/ProductionVerticalSlice.unity";
        private KbduGradeSeparatedRoadAuthorityV1 authority;

        [SetUp]
        public void SetUp()
        {
            authority = new KbduGradeSeparatedRoadAuthorityV1(
                AssetDatabase.LoadAssetAtPath<TextAsset>(AuthorityPath));
        }

        [Test]
        public void OverlappingApproachesSupportVehiclesOnTheVisibleUpperRoad()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roadRoot = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Single(t => t.name == "KBDU_GradeSeparatedRoadsV1");
            var patches = roadRoot.GetComponentsInChildren<MeshFilter>(true).Select(f =>
                new ProductionTerrainSurfaceAsset.TrianglePatch {
                    stableId = f.name, outerHalfExtentMeters = 20000f,
                    vertices = f.sharedMesh.vertices.Select(v => f.transform.TransformPoint(v)).ToArray(),
                    triangles = f.sharedMesh.triangles, normals = f.sharedMesh.normals }).ToArray();
            const string route = "canonical:osm_bounded_roads:way/362700162:0:0:0";
            // The old nearest-profile rule alternated between coincident centerlines
            // belonging to two overlapping approaches at these source-road positions.
            foreach (Vector2 point in new[] { new Vector2(-1569.0511f, -2041.9478f),
                new Vector2(-1573.8110f, -2022.2643f), new Vector2(-1589.2056f, -1902.5682f) })
            {
                float visible = float.NegativeInfinity;
                foreach (var patch in patches)
                    if (patch.TrySample(point.x, point.y, out float y, out _)) visible = Mathf.Max(visible, y);
                Assert.That(float.IsNegativeInfinity(visible), Is.False);
                Assert.That(authority.TryEvaluate(route, point.x, point.y, Vector3.forward, out var support), Is.True);
                Assert.That(support.position.y, Is.EqualTo(visible).Within(0.08f),
                    "Vehicle support differs from serialized opaque road triangles at " + point);
            }
        }

        [Test]
        public void InventoryContainsEveryVerifiedStructureAndNoUpperLowerAlias()
        {
            Assert.That(authority.ProfileCount, Is.EqualTo(31));
            Assert.That(authority.Data.profiles.Select(profile => profile.source_cdot_structure_id).Distinct().Count(),
                Is.EqualTo(19));
            foreach (KbduGradeSeparatedRoadAuthorityV1.Profile profile in authority.Data.profiles)
            {
                Assert.That(profile.source_bridge, Is.Not.EqualTo("no"), profile.span_id);
                Assert.That(profile.source_layer, Is.EqualTo("1"), profile.span_id);
                Assert.That(profile.source_tunnel, Is.Not.EqualTo("yes"), profile.span_id);
                Assert.That(profile.source_year_built, Is.LessThanOrEqualTo(2020), profile.span_id);
                Assert.That(profile.upper_route_ids.Intersect(profile.lower_route_ids), Is.Empty, profile.span_id);
            }
        }

        [Test]
        public void ProfilesAreDeterministicContinuousSagFreeAndGradeBounded()
        {
            ProductionTerrainSurfaceAsset terrain =
                AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(TerrainPath);
            Assert.That(terrain, Is.Not.Null);
            foreach (KbduGradeSeparatedRoadAuthorityV1.Profile profile in authority.Data.profiles)
            {
                Assert.That(profile.maximum_grade, Is.LessThanOrEqualTo(0.06f), profile.span_id);
                if(profile.lower_feature_kind=="railway")
                    Assert.That(profile.minimum_clearance_meters,Is.Zero,"Rail clearance is explicitly unverified");
                else Assert.That(profile.minimum_clearance_meters, Is.GreaterThanOrEqualTo(4.75f), profile.span_id);
                Evaluate(profile, 0f, out Vector3 start, out Vector3 startNormal);
                Evaluate(profile, profile.profile_length_meters, out Vector3 end, out Vector3 endNormal);
                Assert.That(terrain.TrySample(start.x, start.z, out ProductionTerrainSample startTerrain), Is.True);
                Assert.That(terrain.TrySample(end.x, end.z, out ProductionTerrainSample endTerrain), Is.True);
                Assert.That(start.y, Is.EqualTo(startTerrain.heightMeters).Within(0.001f), profile.span_id);
                Assert.That(end.y, Is.EqualTo(endTerrain.heightMeters).Within(0.001f), profile.span_id);
                Assert.That(Vector3.Dot(startNormal, startTerrain.normal), Is.GreaterThan(0.999f), profile.span_id);
                Assert.That(Vector3.Dot(endNormal, endTerrain.normal), Is.GreaterThan(0.999f), profile.span_id);
                float bridgeStart = profile.bridge_interval_meters[0];
                float bridgeEnd = profile.bridge_interval_meters[1];
                Evaluate(profile, bridgeStart, out Vector3 a, out _);
                Evaluate(profile, bridgeEnd, out Vector3 b, out _);
                float minimum = Mathf.Min(a.y, b.y) - 0.001f;
                for (int index = 0; index <= 16; index++)
                {
                    float distance = Mathf.Lerp(bridgeStart, bridgeEnd, index / 16f);
                    Evaluate(profile, distance, out Vector3 first, out Vector3 firstNormal);
                    Evaluate(profile, distance, out Vector3 second, out Vector3 secondNormal);
                    Assert.That(first, Is.EqualTo(second), profile.span_id);
                    Assert.That(firstNormal, Is.EqualTo(secondNormal), profile.span_id);
                    Assert.That(first.y, Is.GreaterThanOrEqualTo(minimum), profile.span_id);
                }
            }
        }

        [Test]
        public void AuthoredDeckAndAuthorityAgreeWithinOneCentimeter()
        {
            Mesh[] meshes =
            {
                AssetDatabase.LoadAssetAtPath<Mesh>(
                    "Assets/Production/Environment/GradeSeparatedRoadsV1/Generated/KBDU_GradeSeparatedRoads_Near_V1.asset"),
                AssetDatabase.LoadAssetAtPath<Mesh>(
                    "Assets/Production/Environment/GradeSeparatedRoadsV1/Generated/KBDU_GradeSeparatedRoads_Outer_V1.asset")
            };
            Assert.That(meshes.All(mesh => mesh != null), Is.True);
            foreach (KbduGradeSeparatedRoadAuthorityV1.Profile profile in authority.Data.profiles)
            {
                float distance = (profile.bridge_interval_meters[0] + profile.bridge_interval_meters[1]) * 0.5f;
                Evaluate(profile, distance, out Vector3 center, out _);
                Assert.That(TryHighestMeshHeight(meshes, center.x, center.z, out float height), Is.True, profile.span_id);
                Assert.That(height, Is.EqualTo(center.y).Within(0.01f), profile.span_id);
            }
        }

        [Test]
        public void VehicleContactOffsetsMatchCurrentMeshContactPlanes()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            KbduTrafficV2Runtime runtime = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<KbduTrafficV2Runtime>(true)).Single();
            Assert.That(runtime.GradeSeparatedRoadAuthority, Is.Not.Null);
            Mesh[] meshes = runtime.AcceptedVehicleAssets.NearArchetypeMeshes;
            Assert.That(meshes.Length, Is.EqualTo(6));
            for (int archetype = 0; archetype < meshes.Length; archetype++)
            {
                Assert.That(authority.ContactOffsetMeters(archetype), Is.EqualTo(0f).Within(0.0001f));
                Assert.That(meshes[archetype].vertices.Min(vertex => vertex.y),
                    Is.EqualTo(0f).Within(0.0001f), meshes[archetype].name);
                Assert.That(authority.ContactHalfLengthMeters(archetype),
                    Is.EqualTo(meshes[archetype].bounds.extents.z).Within(0.001f), meshes[archetype].name);
            }
        }

        [Test]
        public void EveryVehicleFootprintIsSupportedAcrossEveryProfile()
        {
            for (int archetype = 0; archetype < authority.Data.vehicle_contact_half_lengths_meters.Length;
                 archetype++)
            {
                float halfLength = authority.ContactHalfLengthMeters(archetype);
                foreach (KbduGradeSeparatedRoadAuthorityV1.Profile profile in authority.Data.profiles)
                {
                    for (float distance = 0f; distance <= profile.profile_length_meters; distance += 2.5f)
                    {
                        Evaluate(profile, distance, out Vector3 center, out _);
                        Evaluate(profile, Mathf.Min(profile.profile_length_meters, distance + halfLength),
                            out Vector3 front, out _);
                        Evaluate(profile, Mathf.Max(0f, distance - halfLength), out Vector3 rear, out _);
                        float chordCenter = (front.y + rear.y) * 0.5f;
                        float supportedRoot = Mathf.Max(center.y, chordCenter);
                        float halfRise = (front.y - rear.y) * 0.5f;
                        Assert.That(supportedRoot - center.y, Is.GreaterThanOrEqualTo(-0.0001f), profile.span_id);
                        Assert.That(supportedRoot + halfRise - front.y,
                            Is.GreaterThanOrEqualTo(-0.0001f), profile.span_id);
                        Assert.That(supportedRoot - halfRise - rear.y,
                            Is.GreaterThanOrEqualTo(-0.0001f), profile.span_id);
                        float pitch = Mathf.Abs(Mathf.Atan2(front.y - rear.y, halfLength * 2f) * Mathf.Rad2Deg);
                        Assert.That(pitch, Is.LessThanOrEqualTo(3.5f), profile.span_id);
                    }
                }
            }
        }

        [Test]
        public void ProductUsesTwoStaticOpaqueBatchesAndNoRuntimePhysicsPath()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform root = scene.GetRootGameObjects()
                .SelectMany(item => item.GetComponentsInChildren<Transform>(true))
                .Single(transform => transform.name == "KBDU_GradeSeparatedRoadsV1");
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            Assert.That(renderers.Length, Is.EqualTo(2));
            Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
            Assert.That(renderers.Select(renderer => renderer.sharedMaterial).Distinct().Count(), Is.EqualTo(1));
            Material terrainMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat");
            Assert.That(renderers.All(renderer => renderer.sharedMaterial == terrainMaterial), Is.True);
            Assert.That(renderers.All(renderer => renderer.sharedMaterial != null &&
                renderer.sharedMaterial.renderQueue < 3000), Is.True);
            Assert.That(renderers.Sum(renderer =>
                renderer.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3), Is.LessThanOrEqualTo(6500));

            string runtimeSource = File.ReadAllText(
                Path.GetFullPath(Path.Combine(Application.dataPath,
                    "Scripts/Environment/KbduTrafficV2Runtime.cs")));
            Assert.That(runtimeSource, Does.Not.Contain("Physics.Raycast"));
            Assert.That(runtimeSource, Does.Not.Contain("Physics.SphereCast"));
            Assert.That(runtimeSource, Does.Not.Contain("GetComponent<Collider"));
            Assert.That(runtimeSource, Does.Not.Contain("+ RoadSurfaceOffsetMeters"));
        }

        [Test]
        public void MeshesContainOnlyFiniteNondegenerateTriangles()
        {
            foreach (string path in new[]
            {
                "Assets/Production/Environment/GradeSeparatedRoadsV1/Generated/KBDU_GradeSeparatedRoads_Near_V1.asset",
                "Assets/Production/Environment/GradeSeparatedRoadsV1/Generated/KBDU_GradeSeparatedRoads_Outer_V1.asset"
            })
            {
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                foreach (Vector3 vertex in vertices)
                    Assert.That(float.IsNaN(vertex.x) || float.IsNaN(vertex.y) || float.IsNaN(vertex.z) ||
                                float.IsInfinity(vertex.x) || float.IsInfinity(vertex.y) || float.IsInfinity(vertex.z),
                        Is.False, path);
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 cross = Vector3.Cross(
                        vertices[triangles[index + 1]] - vertices[triangles[index]],
                        vertices[triangles[index + 2]] - vertices[triangles[index]]);
                    Assert.That(cross.sqrMagnitude, Is.GreaterThan(0.000001f), path + " triangle " + index / 3);
                }
            }
        }

        private static void Evaluate(
            KbduGradeSeparatedRoadAuthorityV1.Profile profile,
            float distance,
            out Vector3 position,
            out Vector3 normal)
        {
            Assert.That(KbduGradeSeparatedRoadAuthorityV1.TryEvaluateProfile(
                profile, distance, out position, out normal), Is.True, profile.span_id);
        }

        private static bool TryHighestMeshHeight(Mesh[] meshes, float x, float z, out float height)
        {
            height = float.NegativeInfinity;
            bool found = false;
            foreach (Mesh mesh in meshes)
            {
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = vertices[triangles[index]];
                    Vector3 b = vertices[triangles[index + 1]];
                    Vector3 c = vertices[triangles[index + 2]];
                    float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(denominator) < 0.000001f) continue;
                    float wa = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / denominator;
                    float wb = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / denominator;
                    float wc = 1f - wa - wb;
                    if (wa < -0.0001f || wb < -0.0001f || wc < -0.0001f) continue;
                    float candidate = wa * a.y + wb * b.y + wc * c.y;
                    if (candidate <= height) continue;
                    height = candidate;
                    found = true;
                }
            }
            return found;
        }
    }
}
