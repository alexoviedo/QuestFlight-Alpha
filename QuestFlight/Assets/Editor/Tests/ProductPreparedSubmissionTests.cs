using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using QuestFlightLab.Environment;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class ProductPreparedSubmissionTests
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static object Get(object instance, string name) => instance.GetType().GetField(name, Flags).GetValue(instance);
        static void Set(object instance, string name, object value) => instance.GetType().GetField(name, Flags).SetValue(instance, value);
        static void Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, Flags).Invoke(instance, args);

        [Test]
        public void TrafficPlaneTestMatchesUnityAcrossMapScaleViewsAndRetainsTouchingBounds()
        {
            var test = (Func<Plane[], Bounds, bool>)Delegate.CreateDelegate(typeof(Func<Plane[], Bounds, bool>),
                typeof(KbduTrafficV2Runtime).GetMethod("BoundsInsidePlanes", BindingFlags.Static | BindingFlags.NonPublic));
            var random = new System.Random(739);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
            int retained = 0, rejected = 0;
            for (int view = 0; view < 32; view++)
            {
                var pose = Matrix4x4.TRS(new Vector3(Range(-12000, 12000), Range(10, 2500), Range(-12000, 12000)),
                    Quaternion.Euler(Range(-75, 75), Range(-180, 180), Range(-60, 60)), Vector3.one);
                var planes = GeometryUtility.CalculateFrustumPlanes(Matrix4x4.Perspective(100, .96f, .03f, 24000) * pose.inverse);
                for (int instance = 0; instance < 512; instance++)
                {
                    var bounds = new Bounds(new Vector3(Range(-12000, 12000), Range(0, 3500), Range(-12000, 12000)),
                        new Vector3(Range(1, 2500), Range(1, 4000), Range(1, 2500)));
                    bool expected = GeometryUtility.TestPlanesAABB(planes, bounds);
                    Assert.That(test(planes, bounds), Is.EqualTo(expected), $"view={view}, bounds={bounds}");
                    if (expected) retained++; else rejected++;
                }
            }
            Assert.That(retained, Is.GreaterThan(100)); Assert.That(rejected, Is.GreaterThan(100));
            var boundary = new[] {new Plane(Vector3.right, 0)};
            Assert.That(test(boundary, new Bounds(new Vector3(-1, 0, 0), Vector3.one * 2)), Is.True);
            Assert.That(test(boundary, new Bounds(new Vector3(-1.0005f, 0, 0), Vector3.one * 2)), Is.True);
            Assert.That(test(boundary, new Bounds(new Vector3(-1.01f, 0, 0), Vector3.one * 2)), Is.False);
        }

        [Test]
        public void TreeChunksRetainCullingFadeFrameAndIndependentPropertiesAcrossPasses()
        {
            var owner = new GameObject("Tree chunks test");
            var view = new GameObject("View");
            var mesh = new Mesh { bounds = new Bounds(Vector3.up, Vector3.one * 2) };
            try
            {
                var renderer = owner.AddComponent<AirportCloseTreeLodRenderer>();
                var camera = view.AddComponent<Camera>();
                camera.farClipPlane = 1000;
                Call(renderer, "PrepareDrawFrustum", camera);
                const int count = 2050;
                var matrices = new Matrix4x4[count]; var tints = new Vector4[count];
                var fades = new float[count]; var frames = new float[count];
                for (int i = 0; i < count; i++)
                {
                    matrices[i] = Matrix4x4.Translate(new Vector3(0, 0, 100 + i * .01f));
                    tints[i] = new Vector4(i, .3f, .4f, 1); fades[i] = .2f + i * .0001f; frames[i] = i % 8;
                }
                matrices[count - 1] = Matrix4x4.Translate(new Vector3(10000, 0, 100));
                Call(renderer, "DrawTier", mesh, matrices, tints, fades, frames, count, camera, 0);
                var batches = (IList)Get(renderer, "preparedBatches");
                Assert.AreEqual(3, Get(renderer, "preparedBatchCount"));
                int source = 0;
                foreach (object batch in batches)
                {
                    var properties = (MaterialPropertyBlock)Get(batch, "Properties");
                    var packedTints = properties.GetVectorArray("_InstanceTint");
                    var packedFades = properties.GetFloatArray("_InstanceFade");
                    var packedFrames = properties.GetFloatArray("_ImpostorFrame");
                    for (int i = 0; i < (int)Get(batch, "Count"); i++, source++)
                    {
                        Assert.AreEqual(matrices[source], ((Matrix4x4[])Get(batch, "Matrices"))[i]);
                        Assert.AreEqual(tints[source], packedTints[i]);
                        Assert.AreEqual(fades[source], packedFades[i]);
                        Assert.AreEqual(frames[source], packedFrames[i]);
                    }
                }
                Assert.AreEqual(count - 1, source, "Offscreen instance must remain rejected.");
                Assert.AreNotSame(Get(batches[0], "Properties"), Get(batches[1], "Properties"));
                // Submission must neither repack nor mutate any prepared value.
                Assert.IsNotNull(GraphicsSettings.currentRenderPipeline);
                tints[0] = Vector4.zero; matrices[0] = Matrix4x4.zero;
                Call(renderer, "DrawPrepared", camera, 0);
                Call(renderer, "DrawPrepared", camera, 0);
                Assert.AreNotEqual(Matrix4x4.zero, ((Matrix4x4[])Get(batches[0], "Matrices"))[0]);
                Assert.AreEqual(.3f, ((MaterialPropertyBlock)Get(batches[0], "Properties")).GetVectorArray("_InstanceTint")[0].y);
                Set(renderer, "preparedBatchCount", 0);
                Call(renderer, "DrawTier", mesh, matrices, tints, fades, frames, 0, camera, 0);
                Assert.AreEqual(0, Get(renderer, "preparedBatchCount"), "No stale chunks when selection empties.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(view); UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void TrafficChunksPreserveEveryPreviousMatrixRowAndReuseStorage()
        {
            var owner = new GameObject("Traffic chunks test");
            owner.SetActive(false); // Exercise packing without booting an unauthored traffic scene.
            var mesh = new Mesh();
            try
            {
                var renderer = owner.AddComponent<KbduTrafficV2Runtime>();
                const int count = 2050;
                var matrices = new Matrix4x4[count]; var previous = new Matrix4x4[count];
                var body = new Vector4[count]; var trailer = new Vector4[count];
                for (int i = 0; i < count; i++)
                {
                    matrices[i] = Matrix4x4.TRS(new Vector3(i, 2, 3), Quaternion.Euler(2, i % 360, 4), Vector3.one);
                    previous[i] = Matrix4x4.TRS(new Vector3(i - .5f, 1, 3), Quaternion.Euler(1, i % 360 - 2, 3), Vector3.one);
                    body[i] = new Vector4(i, 2, 3, 4); trailer[i] = new Vector4(i, 5, 6, 7);
                }
                Call(renderer, "PackChunks", mesh, matrices, previous, body, trailer, count);
                var batches = (IList)Get(renderer, "preparedBatches");
                Assert.AreEqual(3, Get(renderer, "preparedBatchCount"));
                int source = 0;
                foreach (object batch in batches)
                {
                    var properties = (MaterialPropertyBlock)Get(batch, "Properties");
                    Assert.AreEqual(1, properties.GetFloat("_QFHasPreviousInstances"));
                    for (int i = 0; i < (int)Get(batch, "Count"); i++, source++)
                    {
                        Assert.AreEqual(matrices[source], ((Matrix4x4[])Get(batch, "Matrices"))[i]);
                        Assert.AreEqual(body[source], ((Vector4[])Get(batch, "BodyTints"))[i]);
                        Assert.AreEqual(trailer[source], ((Vector4[])Get(batch, "TrailerTints"))[i]);
                        for (int row = 0; row < 4; row++)
                            Assert.AreEqual(previous[source].GetRow(row), ((Vector4[][])Get(batch, "PreviousRows"))[row][i]);
                    }
                    for (int row = 0; row < 4; row++)
                        Assert.AreEqual(((Vector4[][])Get(batch, "PreviousRows"))[row][0], properties.GetVectorArray("_QFPreviousRow" + row)[0]);
                }
                Assert.AreEqual(count, source);
                var first = batches[0];
                Set(renderer, "preparedBatchCount", 0);
                Call(renderer, "PackChunks", mesh, matrices, previous, body, trailer, 1);
                Assert.AreSame(first, batches[0]);
                Assert.AreEqual(1, Get(renderer, "preparedBatchCount"));
                Assert.AreEqual(1, Get(first, "Count"));
                // Quantify the work removed from the second pass. This is an
                // Editor microbenchmark, never a Quest frame-rate measurement.
                var pack = (Action<Mesh, Matrix4x4[], Matrix4x4[], Vector4[], Vector4[], int>)
                    renderer.GetType().GetMethod("PackChunks", Flags).CreateDelegate(
                        typeof(Action<Mesh, Matrix4x4[], Matrix4x4[], Vector4[], Vector4[], int>), renderer);
                var countField = renderer.GetType().GetField("preparedBatchCount", Flags);
                var timer = Stopwatch.StartNew();
                for (int iteration = 0; iteration < 200; iteration++)
                {
                    countField.SetValue(renderer, 0);
                    pack(mesh, matrices, previous, body, trailer, count);
                }
                timer.Stop();
                TestContext.Out.WriteLine($"EDITOR_ONLY repeated traffic packing removed per second pass: {timer.Elapsed.TotalMilliseconds / 200:F4} ms for {count} instances; native submission cost excluded.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
