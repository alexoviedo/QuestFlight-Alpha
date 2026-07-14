using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuestFlight.EditorTests
{
    public sealed class StandaloneEnvironmentBoundaryEditorTests
    {
        private const string PrefabPath = "Assets/Production/Environment/ProductionEnvironmentRoot.prefab";
        private const string TransitionAssetPath =
            "Assets/Production/Environment/Generated/StandaloneRecovery/Terrain_AirportPatch_Near_Transition_Mesh.asset";

        [Test]
        public void AirportTransitionFillsMeasuredTwentyMeterGapWithoutPlanarOverlap()
        {
            WithInstance(root =>
            {
                MeshFilter patch = Required(root, "Terrain_AirportPatch_USGS");
                MeshFilter near = Required(root, "Terrain_Near4km_USGS");
                MeshFilter transition = Required(root, "Terrain_AirportPatch_Near_Transition");
                Assert.That(AssetDatabase.GetAssetPath(transition.sharedMesh), Is.EqualTo(TransitionAssetPath));
                Assert.That(transition.sharedMesh.vertexCount, Is.EqualTo(644));
                Assert.That(transition.sharedMesh.triangles.Length / 3, Is.EqualTo(640));
                Assert.That(transition.GetComponent<MeshRenderer>().sharedMaterial,
                    Is.SameAs(patch.GetComponent<MeshRenderer>().sharedMaterial));
                Assert.That(transition.GetComponent<Collider>(), Is.Null);
                Assert.That(transition.GetComponent<LODGroup>(), Is.Null);

                ProjectedSurface transitionSurface = new ProjectedSurface(transition);
                ProjectedSurface patchSurface = new ProjectedSurface(patch);
                ProjectedSurface nearSurface = new ProjectedSurface(near);
                foreach (float sign in new[] { -1f, 1f })
                {
                    for (float x = -795f; x <= 795f; x += 10f)
                        Assert.That(transitionSurface.Covers(x, sign * 310f), Is.True,
                            $"Airport transition has an exposed point at ({x:F1}, {sign * 310f:F1})");
                    for (float x = -800f; x <= 800f; x += 10f)
                    {
                        Assert.That(transitionSurface.HeightAt(x, sign * 300f),
                            Is.EqualTo(patchSurface.HeightAt(x, sign * 300f)).Within(0.02f));
                        Assert.That(transitionSurface.HeightAt(x, sign * 320f),
                            Is.EqualTo(nearSurface.HeightAt(x, sign * 320f)).Within(0.02f));
                    }
                }

                Vector3[] vertices = transition.sharedMesh.vertices;
                int[] triangles = transition.sharedMesh.triangles;
                for (int offset = 0; offset < triangles.Length; offset += 3)
                {
                    Vector3 a = transition.transform.TransformPoint(vertices[triangles[offset]]);
                    Vector3 b = transition.transform.TransformPoint(vertices[triangles[offset + 1]]);
                    Vector3 c = transition.transform.TransformPoint(vertices[triangles[offset + 2]]);
                    float centroidAbsZ = Mathf.Abs((a.z + b.z + c.z) / 3f);
                    Assert.That(centroidAbsZ, Is.GreaterThan(300f).And.LessThan(320f),
                        "Transition triangles must occupy only the measured uncovered strips");
                    Assert.That(Vector3.Cross(b - a, c - a).y, Is.GreaterThan(0f));
                }
            });
        }

        [Test]
        public void EveryAuthoredTerrainInterfaceHasProjectedHorizontalCoverage()
        {
            WithInstance(root =>
            {
                ProjectedSurface patch = new ProjectedSurface(Required(root, "Terrain_AirportPatch_USGS"));
                ProjectedSurface transition = new ProjectedSurface(Required(root, "Terrain_AirportPatch_Near_Transition"));
                ProjectedSurface near = new ProjectedSurface(Required(root, "Terrain_Near4km_USGS"));
                ProjectedSurface mid = new ProjectedSurface(Required(root, "Terrain_Mid12km_USGS"));
                ProjectedSurface far = new ProjectedSurface(Required(root, "Terrain_Far24km_Immutable_USGS"));

                foreach (float sign in new[] { -1f, 1f })
                {
                    for (float x = -795f; x <= 795f; x += 10f)
                        Assert.That(patch.Covers(x, sign * 299f) && transition.Covers(x, sign * 310f) &&
                                    near.Covers(x, sign * 321f), Is.True,
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
        public void FrozenSourceTerrainAssetsAndFarSealingRemainUnchanged()
        {
            WithInstance(root =>
            {
                Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Required(root,
                    "Terrain_AirportPatch_USGS").sharedMesh)), Is.EqualTo("cb947caef262d0f428df3da9e67f2f29"));
                Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Required(root,
                    "Terrain_Near4km_USGS").sharedMesh)), Is.EqualTo("ac227d332fd5abf489d5ed0739ba982c"));
                Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Required(root,
                    "Terrain_Mid12km_USGS").sharedMesh)), Is.EqualTo("4f672962bf8d5274ab31831c906c0bb3"));
                Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(Required(root,
                    "Terrain_Far24km_Immutable_USGS").sharedMesh)), Is.EqualTo("f7e9defd4f2f8134ea4ed04d496c0495"));
                Assert.That(Required(root, "Terrain_Near4km_USGS").sharedMesh.triangles.Length / 3, Is.EqualTo(18720));
                Assert.That(Required(root, "Terrain_Mid12km_USGS").sharedMesh.triangles.Length / 3, Is.EqualTo(11752));
                Assert.That(Required(root, "Terrain_Far24km_Immutable_USGS").sharedMesh.triangles.Length / 3, Is.EqualTo(89280));
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
