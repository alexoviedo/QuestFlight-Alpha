using System;
using NUnit.Framework;
using QuestFlight.Editor;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests
{
    public sealed class TrafficSelfOcclusionTests
    {
        [TestCase("CargoVan")]
        [TestCase("Crossover")]
        [TestCase("Hatchback")]
        [TestCase("PassengerProxy")]
        [TestCase("Pickup")]
        [TestCase("Sedan")]
        [TestCase("Semi")]
        [TestCase("SemiProxy")]
        public void BakePreservesGeometryAndSemanticRgb(string archetype)
        {
            Mesh source = AssetDatabase.LoadAssetAtPath<Mesh>(
                "Assets/Production/Environment/TrafficV1/Models/KBDU_Traffic" + archetype + ".asset");
            Assert.That(source, Is.Not.Null);
            Mesh candidate = UnityEngine.Object.Instantiate(source);
            candidate.name = source.name;
            try
            {
                TrafficSelfOcclusionAuthoring.ApplyIfPresent(candidate);
                Assert.That(TrafficSelfOcclusionAuthoring.GeometryIdentity(candidate),
                    Is.EqualTo(TrafficSelfOcclusionAuthoring.GeometryIdentity(source)));
                CollectionAssert.AreEqual(source.vertices, candidate.vertices);
                CollectionAssert.AreEqual(source.normals, candidate.normals);
                CollectionAssert.AreEqual(source.tangents, candidate.tangents);
                CollectionAssert.AreEqual(source.uv, candidate.uv);
                CollectionAssert.AreEqual(source.triangles, candidate.triangles);
                CollectionAssert.AreEqual(source.GetVertexAttributes(), candidate.GetVertexAttributes());
                Assert.That(candidate.GetVertexBufferStride(0), Is.EqualTo(source.GetVertexBufferStride(0)));
                Color32[] before = source.colors32, after = candidate.colors32;
                for (int i = 0; i < before.Length; i++)
                {
                    Assert.That(after[i].r, Is.EqualTo(before[i].r));
                    Assert.That(after[i].g, Is.EqualTo(before[i].g));
                    Assert.That(after[i].b, Is.EqualTo(before[i].b));
                    Assert.That(after[i].a, Is.InRange(216, 255));
                    if (before[i].r < 128 || before[i].b >= 128)
                        Assert.That(after[i].a, Is.EqualTo(255), "Only body/trim may receive AO");
                }
                Color32[] first = candidate.colors32;
                TrafficSelfOcclusionAuthoring.ApplyIfPresent(candidate);
                CollectionAssert.AreEqual(first, candidate.colors32, "Reapplication must be idempotent");
                Vector3[] moved = candidate.vertices;
                moved[0].x += .1f;
                candidate.vertices = moved;
                Assert.Throws<InvalidOperationException>(() => TrafficSelfOcclusionAuthoring.ApplyIfPresent(candidate));
            }
            finally { UnityEngine.Object.DestroyImmediate(candidate); }
        }
    }
}
