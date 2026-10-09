using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QuestFlight.Editor;
using QuestFlightLab.Environment;
using QuestFlightLab.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuestFlight.EditorTests
{
    public sealed class TerrainDefectRepairV1EditModeTests
    {
        [Test]
        public void ExactReproducedDefectsUseContinuousSharedEdgesAndQueries()
        {
            WithTerrain((surface, near, middle, far) =>
            {
                AssertSharedEdge(surface, near, middle, 2048f, 4f, 0.001f);
                AssertSharedEdge(surface, middle, far, 6000f, 5f, 0.001f);

                AssertSampleAgreement(surface, middle, -6000f, -4650f, 0.001f,
                    "The exact former west-mountain hole must remain sealed.");
                AssertSampleAgreement(surface, near, 2000f, -810f, 0.001f,
                    "The exact former east flat-area overlap must use visible near geometry.");
                AssertSampleAgreement(surface, middle, 2070f, -810f, 0.001f,
                    "The near/middle transition interior must share its rendered triangles.");
                AssertSampleAgreement(surface, far, -6100f, -4650f, 0.001f,
                    "The middle/far transition interior must share its rendered triangles.");
            });
        }

        [Test]
        public void RepairedMeshesHaveNoInvalidTrianglesOrUnexpectedOpenComponents()
        {
            WithTerrain((_, near, middle, far) =>
            {
                AssertMeshTriangles(near, allowOuterWorldSkirt: false);
                AssertMeshTriangles(middle, allowOuterWorldSkirt: false);
                AssertMeshTriangles(far, allowOuterWorldSkirt: true);

                AssertOpenEdgesOnlyAt(middle, 2048f, 6000f);
                AssertOpenEdgesOnlyAt(far, 6000f, 12000f);
            });
        }

        [Test]
        public void QueryPatchesAreBoundedAndDoNotChangeWorldExtent()
        {
            ProductionTerrainSurfaceAsset surface = AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(
                TerrainDefectRepairV1Authoring.SurfacePath);
            Assert.That(surface, Is.Not.Null);
            string[] originalTransitionIds =
            {
                "near_rendered_outer_band_v1",
                "near_middle_transition_v1",
                "middle_rendered_outer_edge_v1",
                "middle_far_transition_v1"
            };
            Assert.That(surface.transitionPatches, Has.Length.EqualTo(5));
            var originalTransitions = surface.transitionPatches.Where(item => originalTransitionIds.Contains(item.stableId)).ToArray();
            Assert.That(originalTransitions.Select(item => item.stableId), Is.EquivalentTo(originalTransitionIds));
            Assert.That(surface.transitionPatches.All(item => item.IsValid), Is.True);
            Assert.That(originalTransitions.Sum(item => item.triangles.Length / 3),
                Is.InRange(1000, 8000));
            // The accepted local road milestone adds one bounded explicit query
            // patch, independently of the original four transition contracts.
            Mesh nearMesh = AssetDatabase.LoadAssetAtPath<Mesh>(TerrainFloat32IntegrationRepairAuthoring.NearPath);
            var source = TerrainFloat32IntegrationRepairAuthoring.ValidateCurrentRoadTerrainIdentity(nearMesh);
            var road = surface.transitionPatches.Single(item => item.stableId == TerrainFloat32IntegrationRepairAuthoring.RoadPatchId);
            Assert.That(road.priority, Is.EqualTo(-100));
            Assert.That(road.vertices, Has.Length.EqualTo(6901));
            Assert.That(road.triangles.Length / 3,
                Is.EqualTo(source.terrainFloat32Repair?.queryTrianglesAfter ?? 13110));
            Assert.That(road.vertices.All(vertex => Mathf.Abs(vertex.x) <= 2048f && Mathf.Abs(vertex.z) <= 2048f), Is.True);
            Assert.That(surface.macroMinimumEnuMeters, Is.EqualTo(new Vector2(-12000f, -12000f)));
            Assert.That(surface.macroSizeMeters, Is.EqualTo(new Vector2(24000f, 24000f)));

            foreach ((float x, float z) in new[]
                     {
                         (0f, 0f), (1500f, -1200f), (3000f, 2500f),
                         (-4500f, 3500f), (8000f, -7000f), (12000f, 12000f)
                     })
                Assert.That(surface.TrySample(x, z, out _), Is.True, $"World extent lost ({x},{z}).");
        }

        private static void AssertSharedEdge(
            ProductionTerrainSurfaceAsset query,
            Surface inner,
            Surface outer,
            float boundary,
            float step,
            float tolerance)
        {
            foreach (float sign in new[] { -1f, 1f })
            for (float along = -boundary; along <= boundary + 0.01f; along += step)
            {
                foreach ((float x, float z) in new[]
                         {
                             (sign * boundary, along), (along, sign * boundary)
                         })
                {
                    Assert.That(inner.TrySample(x, z, out SurfaceHit innerHit), Is.True);
                    Assert.That(outer.TrySample(x, z, out SurfaceHit outerHit), Is.True);
                    Assert.That(Mathf.Abs(innerHit.height - outerHit.height), Is.LessThanOrEqualTo(tolerance),
                        $"Shared edge mismatch at ({x:F1},{z:F1}).");
                    Assert.That(query.TrySample(x, z, out ProductionTerrainSample terrain), Is.True);
                    Assert.That(Mathf.Abs(terrain.heightMeters - innerHit.height), Is.LessThanOrEqualTo(tolerance));
                }
            }
        }

        private static void AssertSampleAgreement(
            ProductionTerrainSurfaceAsset query,
            Surface visible,
            float x,
            float z,
            float tolerance,
            string message)
        {
            Assert.That(visible.TrySample(x, z, out SurfaceHit rendered), Is.True, message);
            Assert.That(query.TrySample(x, z, out ProductionTerrainSample terrain), Is.True, message);
            Assert.That(terrain.heightMeters, Is.EqualTo(rendered.height).Within(tolerance), message);
            Assert.That(Vector3.Angle(terrain.normal, rendered.normal), Is.LessThanOrEqualTo(0.05f), message);
        }

        private static void AssertMeshTriangles(Surface surface, bool allowOuterWorldSkirt)
        {
            for (int offset = 0; offset < surface.indices.Length; offset += 3)
            {
                Vector3 a = surface.vertices[surface.indices[offset]];
                Vector3 b = surface.vertices[surface.indices[offset + 1]];
                Vector3 c = surface.vertices[surface.indices[offset + 2]];
                Assert.That(Finite(a) && Finite(b) && Finite(c), Is.True);
                Vector3 cross = Vector3.Cross(b - a, c - a);
                bool acceptedRoadFace = surface.acceptedRoadDerivative &&
                    (surface.indices[offset] >= 46426 || surface.indices[offset + 1] >= 46426 || surface.indices[offset + 2] >= 46426);
                if (acceptedRoadFace)
                {
                    // GEOS road subdivision has legitimate nonzero slivers
                    // below this earlier coarse-mesh area heuristic. Require
                    // exact stored-coordinate positive XZ area, which catches
                    // every inversion/collapse, and retain the old thresholds
                    // for every original/outer-terrain triangle below.
                    Assert.That(cross.sqrMagnitude, Is.GreaterThan(0f), $"Zero-area accepted road face {offset / 3}.");
                    Assert.That(TerrainFloat32HeightfieldPolicy.ProjectedCrossY(a, b, c), Is.GreaterThan(0d),
                        $"Inverted or collapsed accepted road face {offset / 3}.");
                    continue;
                }
                Assert.That(cross.sqrMagnitude, Is.GreaterThan(0.000001f),
                    $"Zero-area triangle {offset / 3}.");
                if (cross.y < -0.0001f)
                    Assert.Fail($"Downward triangle {offset / 3}.");
                if (Mathf.Abs(cross.y) <= 0.0001f)
                {
                    Assert.That(allowOuterWorldSkirt, Is.True, $"Unexpected vertical triangle {offset / 3}.");
                    Assert.That(new[] { a, b, c }.All(point =>
                            Mathf.Abs(Mathf.Max(Mathf.Abs(point.x), Mathf.Abs(point.z)) - 12000f) <= 0.01f),
                        Is.True, $"Internal vertical skirt triangle {offset / 3}.");
                }
            }
        }

        private static void AssertOpenEdgesOnlyAt(Surface surface, params float[] expectedRadii)
        {
            Dictionary<Edge, int> incidence = new Dictionary<Edge, int>();
            for (int offset = 0; offset < surface.indices.Length; offset += 3)
            {
                Add(surface.indices[offset], surface.indices[offset + 1]);
                Add(surface.indices[offset + 1], surface.indices[offset + 2]);
                Add(surface.indices[offset + 2], surface.indices[offset]);
            }
            Assert.That(incidence.Values.All(value => value <= 2), Is.True, "Nonmanifold edge found.");
            foreach (Edge edge in incidence.Where(item => item.Value == 1).Select(item => item.Key))
            {
                foreach (Vector3 point in new[] { edge.a.Vector, edge.b.Vector })
                {
                    float radius = Mathf.Max(Mathf.Abs(point.x), Mathf.Abs(point.z));
                    Assert.That(expectedRadii.Any(expected => Mathf.Abs(radius - expected) <= 0.01f), Is.True,
                        $"Unexpected open edge at {point}.");
                }
            }

            void Add(int first, int second)
            {
                Edge edge = new Edge(surface.vertices[first], surface.vertices[second]);
                incidence.TryGetValue(edge, out int count);
                incidence[edge] = count + 1;
            }
        }

        private static bool Finite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static void WithTerrain(Action<ProductionTerrainSurfaceAsset, Surface, Surface, Surface> assertion)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TerrainDefectRepairV1Authoring.EnvironmentPath);
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                ProductionEnvironmentRoot environment = root.GetComponent<ProductionEnvironmentRoot>();
                assertion(
                    environment.authoritativeTerrainSurface,
                    new Surface(Required(root, "Terrain_Near4km_USGS")),
                    new Surface(Required(root, "Terrain_Mid12km_USGS")),
                    new Surface(Required(root, "Terrain_Far24km_Immutable_USGS")));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static MeshFilter Required(GameObject root, string name) =>
            root.GetComponentsInChildren<MeshFilter>(true)
                .Single(item => string.Equals(item.name, name, StringComparison.Ordinal));

        private sealed class Surface
        {
            public readonly Vector3[] vertices;
            public readonly Vector3[] normals;
            public readonly int[] indices;
            public readonly bool acceptedRoadDerivative;

            public Surface(MeshFilter filter)
            {
                acceptedRoadDerivative = AssetDatabase.GetAssetPath(filter.sharedMesh) == TerrainFloat32IntegrationRepairAuthoring.NearPath;
                if (acceptedRoadDerivative) TerrainFloat32IntegrationRepairAuthoring.ValidateCurrentRoadTerrainIdentity(filter.sharedMesh);
                vertices = filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
                normals = filter.sharedMesh.normals.Select(filter.transform.TransformDirection).ToArray();
                indices = filter.sharedMesh.triangles;
            }

            public bool TrySample(float x, float z, out SurfaceHit hit)
            {
                hit = default;
                bool found = false;
                for (int offset = 0; offset < indices.Length; offset += 3)
                {
                    int ia = indices[offset];
                    int ib = indices[offset + 1];
                    int ic = indices[offset + 2];
                    Vector3 a = vertices[ia];
                    Vector3 b = vertices[ib];
                    Vector3 c = vertices[ic];
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    if (cross.y <= 0.0001f) continue;
                    float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(denominator) <= 0.000001f) continue;
                    float wa = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / denominator;
                    float wb = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / denominator;
                    float wc = 1f - wa - wb;
                    if (wa < -0.00005f || wb < -0.00005f || wc < -0.00005f) continue;
                    float height = wa * a.y + wb * b.y + wc * c.y;
                    if (found && height <= hit.height) continue;
                    hit = new SurfaceHit
                    {
                        height = height,
                        normal = (wa * normals[ia] + wb * normals[ib] + wc * normals[ic]).normalized
                    };
                    found = true;
                }
                return found;
            }
        }

        private struct SurfaceHit
        {
            public float height;
            public Vector3 normal;
        }

        private readonly struct Point : IEquatable<Point>, IComparable<Point>
        {
            private readonly int x, y, z;
            public Point(Vector3 value)
            {
                x = Mathf.RoundToInt(value.x * 1000f);
                y = Mathf.RoundToInt(value.y * 1000f);
                z = Mathf.RoundToInt(value.z * 1000f);
            }
            public Vector3 Vector => new Vector3(x, y, z) * 0.001f;
            public bool Equals(Point other) => x == other.x && y == other.y && z == other.z;
            public int CompareTo(Point other)
            {
                int result = x.CompareTo(other.x);
                if (result != 0) return result;
                result = y.CompareTo(other.y);
                return result != 0 ? result : z.CompareTo(other.z);
            }
            public override bool Equals(object obj) => obj is Point other && Equals(other);
            public override int GetHashCode() => unchecked((x * 397 ^ y) * 397 ^ z);
        }

        private readonly struct Edge : IEquatable<Edge>
        {
            public readonly Point a, b;
            public Edge(Vector3 first, Vector3 second)
            {
                Point firstPoint = new Point(first);
                Point secondPoint = new Point(second);
                if (firstPoint.CompareTo(secondPoint) <= 0) { a = firstPoint; b = secondPoint; }
                else { a = secondPoint; b = firstPoint; }
            }
            public bool Equals(Edge other) => a.Equals(other.a) && b.Equals(other.b);
            public override bool Equals(object obj) => obj is Edge other && Equals(other);
            public override int GetHashCode() => unchecked(a.GetHashCode() * 397 ^ b.GetHashCode());
        }
    }
}
