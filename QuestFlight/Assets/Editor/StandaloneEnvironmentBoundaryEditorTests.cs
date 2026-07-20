using System;
using System.Linq;
using NUnit.Framework;
using QuestFlight.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuestFlight.EditorTests
{
    public sealed class StandaloneEnvironmentBoundaryEditorTests
    {
        private const string PrefabPath = "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        [Test]
        public void AerialV1AirportAndLidarNearMeshesMeetWithoutLegacyTransition()
        {
            WithInstance(root =>
            {
                MeshFilter patch = Required(root, "Terrain_AirportPatch_USGS");
                MeshFilter near = Required(root, "Terrain_Near4km_USGS");
                MeshFilter transition = Required(root, "Terrain_AirportPatch_Near_Transition");
                Assert.That(transition.gameObject.activeSelf, Is.False,
                    "The legacy strip transition must remain serialized but disabled for aligned V2 grids.");
                ProjectedSurface patchSurface = new ProjectedSurface(patch);
                ProjectedSurface nearSurface = new ProjectedSurface(near);
                foreach (float sign in new[] { -1f, 1f })
                {
                    for (float x = -795f; x <= 795f; x += 10f)
                    {
                        Assert.That(patchSurface.Covers(x, sign * 299f), Is.True);
                        Assert.That(nearSurface.Covers(x, sign * 301f), Is.True);
                        Assert.That(nearSurface.HeightAt(x, sign * 300f),
                            Is.EqualTo(patchSurface.HeightAt(x, sign * 300f)).Within(0.03f));
                    }
                    for (float z = -295f; z <= 295f; z += 10f)
                    {
                        Assert.That(patchSurface.Covers(sign * 799f, z), Is.True);
                        Assert.That(nearSurface.Covers(sign * 801f, z), Is.True);
                        Assert.That(nearSurface.HeightAt(sign * 800f, z),
                            Is.EqualTo(patchSurface.HeightAt(sign * 800f, z)).Within(0.03f));
                    }
                }
            });
        }

        [Test]
        public void EveryAuthoredTerrainInterfaceHasProjectedHorizontalCoverage()
        {
            WithInstance(root =>
            {
                ProjectedSurface patch = new ProjectedSurface(Required(root, "Terrain_AirportPatch_USGS"));
                ProjectedSurface near = new ProjectedSurface(Required(root, "Terrain_Near4km_USGS"));
                ProjectedSurface mid = new ProjectedSurface(Required(root, "Terrain_Mid12km_USGS"));
                ProjectedSurface far = new ProjectedSurface(Required(root, "Terrain_Far24km_Immutable_USGS"));

                foreach (float sign in new[] { -1f, 1f })
                {
                    for (float x = -795f; x <= 795f; x += 10f)
                        Assert.That(patch.Covers(x, sign * 299f) && near.Covers(x, sign * 301f), Is.True,
                            "Airport patch-to-near Z interface must be continuously covered");
                    for (float z = -290f; z <= 290f; z += 20f)
                        Assert.That(patch.Covers(sign * 799f, z) && near.Covers(sign * 801f, z), Is.True,
                            "Airport patch-to-near X interface must be continuously covered");

                    for (float along = -1900f; along <= 1900f; along += 100f)
                    {
                        AssertInterfaceCoverage(near, mid, sign * 1999f, sign * 2001f, along, false, "near-mid X");
                        AssertInterfaceCoverage(near, mid, sign * 1999f, sign * 2001f, along, true, "near-mid Z");
                    }
                    for (float along = -5800f; along <= 5800f; along += 200f)
                    {
                        AssertInterfaceCoverage(mid, far, sign * 5999f, sign * 6001f, along, false, "mid-far X");
                        AssertInterfaceCoverage(mid, far, sign * 5999f, sign * 6001f, along, true, "mid-far Z");
                    }
                }

                // The four coarse-mid 6-edge components are collinear T-junctions caused by
                // the 150 m center-mask grid, not projected holes. Audit the full corner boxes.
                foreach (float xSign in new[] { -1f, 1f })
                foreach (float zSign in new[] { -1f, 1f })
                for (float xMagnitude = 1955f; xMagnitude <= 2095f; xMagnitude += 10f)
                for (float zMagnitude = 1955f; zMagnitude <= 2095f; zMagnitude += 10f)
                {
                    float x = xSign * xMagnitude;
                    float z = zSign * zMagnitude;
                    Assert.That(near.Covers(x, z) || mid.Covers(x, z), Is.True,
                        $"Coarse mid-grid corner T-junction exposes a projected hole at ({x:F1}, {z:F1})");
                }
            });
        }

        [Test]
        public void AerialV1UsesConformedAirportAndLidarDerivativesWhileOuterTerrainRemainsUnchanged()
        {
            WithInstance(root =>
            {
                Assert.That(AssetDatabase.GetAssetPath(Required(root, "Terrain_AirportPatch_USGS").sharedMesh),
                    Is.EqualTo(AirportPatchLidarConformanceAuthoring.OutputMeshPath));
                Mesh sourceAirport = AssetDatabase.LoadAssetAtPath<Mesh>(
                    EnvironmentReconstructionV2Authoring.AirportMeshPath);
                Assert.That(sourceAirport, Is.Not.Null);
                Assert.That(sourceAirport.triangles.Length / 3, Is.EqualTo(76800));
                Assert.That(AssetDatabase.GetAssetPath(Required(root, "Terrain_Near4km_USGS").sharedMesh),
                    Is.EqualTo(AerialSceneryV1IntegrationAuthoring.NearTerrainPath));
                Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Required(root,
                    "Terrain_Mid12km_USGS").sharedMesh)), Is.EqualTo("4f672962bf8d5274ab31831c906c0bb3"));
                Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Required(root,
                    "Terrain_Far24km_Immutable_USGS").sharedMesh)), Is.EqualTo("f7e9defd4f2f8134ea4ed04d496c0495"));
                Assert.That(Required(root, "Terrain_AirportPatch_USGS").sharedMesh.triangles.Length / 3,
                    Is.InRange(1, AirportPatchLidarConformanceAuthoring.MaximumOutputTriangles));
                Assert.That(Required(root, "Terrain_Near4km_USGS").sharedMesh.triangles.Length / 3,
                    Is.EqualTo(92000));
                Assert.That(Required(root, "Terrain_Mid12km_USGS").sharedMesh.triangles.Length / 3, Is.EqualTo(11752));
                Assert.That(Required(root, "Terrain_Far24km_Immutable_USGS").sharedMesh.triangles.Length / 3, Is.EqualTo(89280));
                TextAsset conformance = AssetDatabase.LoadAssetAtPath<TextAsset>(
                    AirportPatchLidarConformanceAuthoring.OutputReportPath);
                Assert.That(conformance, Is.Not.Null);
                AirportPatchLidarConformanceAuthoring.ConformanceReport parsed =
                    JsonUtility.FromJson<AirportPatchLidarConformanceAuthoring.ConformanceReport>(conformance.text);
                Assert.That(parsed.boundaryProbeCount, Is.GreaterThanOrEqualTo(4000));
                Assert.That(parsed.maximumBoundaryMismatchMeters,
                    Is.LessThanOrEqualTo(AirportPatchLidarConformanceAuthoring.MaximumBoundaryMismatchMeters));
            });
        }

        private static void AssertInterfaceCoverage(
            ProjectedSurface inner,
            ProjectedSurface outer,
            float inside,
            float outside,
            float along,
            bool swap,
            string label)
        {
            bool innerCovered = swap ? inner.Covers(along, inside) : inner.Covers(inside, along);
            bool outerCovered = swap ? outer.Covers(along, outside) : outer.Covers(outside, along);
            Assert.That(innerCovered && outerCovered, Is.True,
                $"{label} interface has no projected horizontal coverage at along={along:F1}");
        }

        private static MeshFilter Required(GameObject root, string name)
        {
            MeshFilter filter = root.GetComponentsInChildren<MeshFilter>(true)
                .SingleOrDefault(candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));
            Assert.That(filter, Is.Not.Null, "Required authored terrain object is missing: " + name);
            Assert.That(filter.sharedMesh, Is.Not.Null, "Required authored terrain mesh is missing: " + name);
            return filter;
        }

        private static void WithInstance(Action<GameObject> assertion)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try { assertion(instance); }
            finally { Object.DestroyImmediate(instance); }
        }

        private sealed class ProjectedSurface
        {
            private readonly Vector3[] vertices;
            private readonly int[] triangles;

            public ProjectedSurface(MeshFilter filter)
            {
                vertices = filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
                triangles = filter.sharedMesh.triangles;
            }

            public bool Covers(float x, float z) => TryHeight(x, z, out _);

            public float HeightAt(float x, float z)
            {
                Assert.That(TryHeight(x, z, out float height), Is.True,
                    $"Surface has no projected coverage at ({x:F1}, {z:F1})");
                return height;
            }

            private bool TryHeight(float x, float z, out float height)
            {
                height = 0f;
                for (int offset = 0; offset < triangles.Length; offset += 3)
                {
                    Vector3 a = vertices[triangles[offset]];
                    Vector3 b = vertices[triangles[offset + 1]];
                    Vector3 c = vertices[triangles[offset + 2]];
                    if (Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y) <= 0.15f) continue;
                    float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(denominator) <= 0.000001f) continue;
                    float weightA = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / denominator;
                    float weightB = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / denominator;
                    float weightC = 1f - weightA - weightB;
                    const float epsilon = 0.0001f;
                    if (weightA < -epsilon || weightB < -epsilon || weightC < -epsilon) continue;
                    height = weightA * a.y + weightB * b.y + weightC * c.y;
                    return true;
                }
                return false;
            }
        }
    }
}
