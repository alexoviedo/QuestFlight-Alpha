using NUnit.Framework;
using QuestFlightLab.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuestFlight.Editor.Tests
{
    public sealed class QuestRenderBudgetAuditEditorTests
    {
        [Test]
        public void MultiSubmeshRendererCountsEachEffectiveSlotWithoutCountingWholeMeshTwice()
        {
            RenderBudgetSnapshot baseline = QuestRenderBudgetAudit.Capture(null);
            GameObject target = new GameObject("QuestRenderBudgetAudit_MultiSubmesh");
            Mesh mesh = CreateTwoTriangleSubmeshMesh();
            Material first = CreateMaterial();
            Material second = CreateMaterial();
            try
            {
                target.AddComponent<MeshFilter>().sharedMesh = mesh;
                target.AddComponent<MeshRenderer>().sharedMaterials = new[] { first, second };

                RenderBudgetSnapshot actual = QuestRenderBudgetAudit.Capture(null);

                Assert.That(actual.estimatedInstancedDrawCalls - baseline.estimatedInstancedDrawCalls, Is.EqualTo(2));
                Assert.That(actual.estimatedFrustumTriangles - baseline.estimatedFrustumTriangles, Is.EqualTo(2));
                Assert.That(actual.uniqueMaterialCount - baseline.uniqueMaterialCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void SkinnedMeshRendererContributesItsRenderedTrianglesAndDrawSlot()
        {
            RenderBudgetSnapshot baseline = QuestRenderBudgetAudit.Capture(null);
            GameObject target = new GameObject("QuestRenderBudgetAudit_SkinnedMesh");
            Mesh mesh = CreateOneTriangleMesh();
            Material material = CreateMaterial();
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            try
            {
                SkinnedMeshRenderer renderer = target.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.sharedMaterial = material;

                RenderBudgetSnapshot actual = QuestRenderBudgetAudit.Capture(null);

                Assert.That(actual.estimatedInstancedDrawCalls - baseline.estimatedInstancedDrawCalls, Is.EqualTo(1));
                Assert.That(actual.estimatedFrustumTriangles - baseline.estimatedFrustumTriangles, Is.EqualTo(1));
                Assert.That(actual.uniqueMaterialCount - baseline.uniqueMaterialCount, Is.EqualTo(1));
                Assert.That(actual.transparentMaterialSlots - baseline.transparentMaterialSlots, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
            }
        }

        private static Mesh CreateTwoTriangleSubmeshMesh()
        {
            Mesh mesh = new Mesh { name = "QuestRenderBudgetAudit_TwoSubmeshes" };
            mesh.vertices = new[]
            {
                new Vector3(-1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(1f, 0f, 0f),
                new Vector3(-1f, 0f, 1f), new Vector3(0f, 1f, 1f), new Vector3(1f, 0f, 1f)
            };
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetTriangles(new[] { 3, 4, 5 }, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateOneTriangleMesh()
        {
            Mesh mesh = new Mesh { name = "QuestRenderBudgetAudit_OneTriangle" };
            mesh.vertices = new[]
            {
                new Vector3(-1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(1f, 0f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material CreateMaterial()
        {
            Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null, "A built-in test shader is required.");
            return new Material(shader);
        }
    }
}
