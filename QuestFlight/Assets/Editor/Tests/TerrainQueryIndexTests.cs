using System;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests
{
    public sealed class TerrainQueryIndexTests
    {
        [Test]
        public void AuthoredTransitionsMatchOriginalQueriesAtVerticesCentersAndRouteSamples()
        {
            var terrain = AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>(
                "Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset");
            var random = new System.Random(60906);
            int compared = 0;
            foreach (var patch in terrain.transitionPatches)
            {
                foreach (Vector3 vertex in patch.vertices)
                {
                    Compare(patch, vertex.x, vertex.z);
                    compared++;
                }
                for (int i = 0; i < patch.triangles.Length; i += 3)
                {
                    Vector3 center = (patch.vertices[patch.triangles[i]] + patch.vertices[patch.triangles[i + 1]] +
                        patch.vertices[patch.triangles[i + 2]]) / 3f;
                    Compare(patch, center.x, center.z);
                    compared++;
                }
                for (int i = 0; i < 2000; i++)
                {
                    float radius = Mathf.Lerp(patch.innerHalfExtentMeters, patch.outerHalfExtentMeters, (float)random.NextDouble());
                    float tangent = (float)(random.NextDouble() * 2 - 1) * radius;
                    float x = i % 2 == 0 ? radius : tangent;
                    float z = i % 2 == 0 ? tangent : radius;
                    if (i % 4 < 2) x = -x;
                    if (i % 4 >= 2) z = -z;
                    Compare(patch, x, z);
                    compared++;
                }
            }
            Assert.That(compared, Is.GreaterThan(8000));
            Debug.Log($"Terrain query index: {compared} exact height/normal comparisons passed.");
        }

        private static void Compare(ProductionTerrainSurfaceAsset.TrianglePatch patch, float x, float z)
        {
            bool expected = patch.TrySampleReference(x, z, out float height, out Vector3 normal);
            Assert.That(patch.TrySample(x, z, out float actualHeight, out Vector3 actualNormal), Is.EqualTo(expected), patch.stableId);
            Assert.That(actualHeight, Is.EqualTo(height), $"{patch.stableId} ({x},{z}) height");
            Assert.That(actualNormal, Is.EqualTo(normal), $"{patch.stableId} ({x},{z}) normal");
        }

        [Test]
        public void OverlapSelectsHighestSurfaceAndReplacedGeometryInvalidatesIndex()
        {
            var patch = new ProductionTerrainSurfaceAsset.TrianglePatch
            {
                stableId = "overlap", innerHalfExtentMeters = 0, outerHalfExtentMeters = 1024,
                vertices = new[] { new Vector3(-300, 1, -300), new Vector3(300, 1, -300), new Vector3(0, 1, 300),
                    new Vector3(-300, 5, -300), new Vector3(300, 5, -300), new Vector3(0, 5, 300) },
                normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up },
                triangles = new[] { 0, 1, 2, 3, 4, 5 }
            };
            Assert.That(patch.TrySample(0, 0, out float height, out _), Is.True);
            Assert.That(height, Is.EqualTo(5));
            patch.triangles = new[] { 0, 1, 2 };
            Assert.That(patch.TrySample(0, 0, out height, out _), Is.True);
            Assert.That(height, Is.EqualTo(1));
            foreach (float x in new[] { -256.0001f, -256f, -255.9999f, 0f, 255.9999f, 256f, 256.0001f })
                Compare(patch, x, -280);
        }
    }
}
