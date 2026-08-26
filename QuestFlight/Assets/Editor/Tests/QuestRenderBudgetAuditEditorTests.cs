using NUnit.Framework;
using QuestFlightLab.Environment;
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

        [Test]
        public void AuthoredVegetationBatchCountsOneDrawAndEverySubmittedInstance()
        {
            RenderBudgetSnapshot baseline = QuestRenderBudgetAudit.Capture(null);
            GameObject target = new GameObject("QuestRenderBudgetAudit_AerialVegetationBatch");
            Mesh mesh = CreateOneTriangleMesh();
            Material material = CreateMaterial();
            try
            {
                AerialVegetationBatchRenderer renderer = target.AddComponent<AerialVegetationBatchRenderer>();
                renderer.Configure(
                    material,
                    null,
                    new[]
                    {
                        new AerialVegetationBatchRenderer.Batch(
                            "test",
                            1,
                            mesh,
                            new[] { Matrix4x4.identity, Matrix4x4.Translate(Vector3.right) },
                            new[] { Vector4.one, Vector4.one })
                    },
                    2);

                RenderBudgetSnapshot actual = QuestRenderBudgetAudit.Capture(null);

                Assert.That(actual.activeRendererCount - baseline.activeRendererCount, Is.EqualTo(1));
                Assert.That(actual.estimatedVisibleRendererCount - baseline.estimatedVisibleRendererCount, Is.EqualTo(1));
                Assert.That(actual.estimatedInstancedDrawCalls - baseline.estimatedInstancedDrawCalls, Is.EqualTo(1));
                Assert.That(actual.estimatedFrustumTriangles - baseline.estimatedFrustumTriangles, Is.EqualTo(2));
                Assert.That(actual.uniqueMaterialCount - baseline.uniqueMaterialCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void CameraCaptureCountsOnlyTheSelectedLodAndCullsBelowTheFinalThreshold()
        {
            GameObject cameraObject = new GameObject("QuestRenderBudgetAudit_Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500f;
            GameObject root = new GameObject("QuestRenderBudgetAudit_LODGroup");
            Material material = CreateMaterial();
            Mesh[] meshes = { CreateTriangleMesh(1), CreateTriangleMesh(2), CreateTriangleMesh(3) };
            Renderer[] renderers = new Renderer[3];
            float previousLodBias = QualitySettings.lodBias;
            try
            {
                QualitySettings.lodBias = 1f;
                root.transform.position = new Vector3(0f, 0f, 2f);
                for (int index = 0; index < renderers.Length; index++)
                {
                    GameObject child = new GameObject("LOD" + index);
                    child.transform.SetParent(root.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = meshes[index];
                    MeshRenderer renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderers[index] = renderer;
                }
                LODGroup group = root.AddComponent<LODGroup>();
                group.SetLODs(new[]
                {
                    new LOD(0.2f, new[] { renderers[0] }),
                    new LOD(0.08f, new[] { renderers[1] }),
                    new LOD(0.02f, new[] { renderers[2] })
                });
                group.localReferencePoint = Vector3.zero;
                group.size = 2f;

                AssertSelectedTriangles(camera, root.transform, 2f, 1);
                AssertSelectedTriangles(camera, root.transform, 10f, 2);
                AssertSelectedTriangles(camera, root.transform, 40f, 3);
                AssertSelectedTriangles(camera, root.transform, 120f, 0);
            }
            finally
            {
                QualitySettings.lodBias = previousLodBias;
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraObject);
                foreach (Mesh mesh in meshes) Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void CameraCaptureHonorsCullingMaskAndForceRenderingOff()
        {
            GameObject cameraObject = new GameObject("QuestRenderBudgetAudit_Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = 1 << 0;
            GameObject target = new GameObject("QuestRenderBudgetAudit_FilteredRenderer");
            target.transform.position = new Vector3(0f, 0f, 3f);
            Mesh mesh = CreateOneTriangleMesh();
            Material material = CreateMaterial();
            try
            {
                target.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = target.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                RenderBudgetSnapshot baseline = QuestRenderBudgetAudit.Capture(camera);

                target.layer = 1;
                RenderBudgetSnapshot masked = QuestRenderBudgetAudit.Capture(camera);
                Assert.That(masked.estimatedFrustumTriangles, Is.EqualTo(baseline.estimatedFrustumTriangles - 1));

                target.layer = 0;
                renderer.forceRenderingOff = true;
                RenderBudgetSnapshot forcedOff = QuestRenderBudgetAudit.Capture(camera);
                Assert.That(forcedOff.estimatedFrustumTriangles, Is.EqualTo(masked.estimatedFrustumTriangles));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void CameraCaptureCountsLegacyTextMeshWithoutRequiringMeshFilter()
        {
            GameObject cameraObject = new GameObject("QuestRenderBudgetAudit_TextMeshCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = 1 << 0;
            GameObject target = new GameObject("QuestRenderBudgetAudit_TextMesh");
            Material material = CreateMaterial();
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            try
            {
                RenderBudgetSnapshot baseline = QuestRenderBudgetAudit.Capture(camera);
                TextMesh text = target.AddComponent<TextMesh>();
                text.text = "AB C\nD";
                MeshRenderer renderer = target.GetComponent<MeshRenderer>();
                Assert.That(renderer, Is.Not.Null);
                Assert.That(target.GetComponent<MeshFilter>(), Is.Null,
                    "Legacy TextMesh legitimately owns generated glyph geometry without a MeshFilter.");
                renderer.sharedMaterial = material;

                RenderBudgetSnapshot visible = QuestRenderBudgetAudit.Capture(camera);
                Assert.That(visible.activeRendererCount - baseline.activeRendererCount, Is.EqualTo(1));
                Assert.That(visible.estimatedVisibleRendererCount - baseline.estimatedVisibleRendererCount,
                    Is.EqualTo(1));
                Assert.That(visible.estimatedInstancedDrawCalls - baseline.estimatedInstancedDrawCalls,
                    Is.EqualTo(1));
                Assert.That(visible.estimatedFrustumTriangles - baseline.estimatedFrustumTriangles,
                    Is.EqualTo(8), "Four non-whitespace glyphs conservatively contribute two triangles each.");
                Assert.That(visible.uniqueMaterialCount - baseline.uniqueMaterialCount, Is.EqualTo(1));
                Assert.That(visible.transparentMaterialSlots - baseline.transparentMaterialSlots,
                    Is.EqualTo(1));

                target.layer = 1;
                RenderBudgetSnapshot masked = QuestRenderBudgetAudit.Capture(camera);
                Assert.That(masked.activeRendererCount - baseline.activeRendererCount, Is.EqualTo(1));
                Assert.That(masked.estimatedVisibleRendererCount, Is.EqualTo(baseline.estimatedVisibleRendererCount));
                Assert.That(masked.estimatedInstancedDrawCalls, Is.EqualTo(baseline.estimatedInstancedDrawCalls));
                Assert.That(masked.estimatedFrustumTriangles, Is.EqualTo(baseline.estimatedFrustumTriangles));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cameraObject);
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

        private static Mesh CreateTriangleMesh(int triangleCount)
        {
            Vector3[] vertices = new Vector3[triangleCount * 3];
            int[] triangles = new int[triangleCount * 3];
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                int index = triangle * 3;
                float offset = triangle * 0.05f;
                vertices[index] = new Vector3(-0.5f + offset, -0.5f, 0f);
                vertices[index + 1] = new Vector3(offset, 0.5f, 0f);
                vertices[index + 2] = new Vector3(0.5f + offset, -0.5f, 0f);
                triangles[index] = index;
                triangles[index + 1] = index + 1;
                triangles[index + 2] = index + 2;
            }
            Mesh mesh = new Mesh { name = "QuestRenderBudgetAudit_" + triangleCount + "Triangles" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AssertSelectedTriangles(Camera camera, Transform root, float distance, long expected)
        {
            root.position = new Vector3(0f, 0f, distance);
            RenderBudgetSnapshot snapshot = QuestRenderBudgetAudit.Capture(camera);
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers) renderer.forceRenderingOff = true;
            RenderBudgetSnapshot baseline = QuestRenderBudgetAudit.Capture(camera);
            foreach (Renderer renderer in renderers) renderer.forceRenderingOff = false;
            Assert.That(snapshot.estimatedFrustumTriangles - baseline.estimatedFrustumTriangles,
                Is.EqualTo(expected), "Unexpected selected LOD at distance " + distance);
        }

        private static Material CreateMaterial()
        {
            Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null, "A built-in test shader is required.");
            return new Material(shader);
        }
    }
}
