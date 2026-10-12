using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlightLab.Aircraft;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace QuestFlight.Editor.Tests
{
    public sealed class ProductionAircraftExteriorBatchTests
    {
        [Test]
        public void AuthoredBatchPreservesGeometryBindingsAndRepeatedVisibility()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ProductionAircraftExteriorBatchAuthoring.PrefabPath);
            Mesh mesh = null;
            try
            {
                var sources = ProductionAircraftExteriorBatchAuthoring.GetSources(root);
                var sourceMeshes = sources.Select(r => r.GetComponent<MeshFilter>().sharedMesh).ToArray();
                var transforms = root.GetComponentsInChildren<Transform>(true);
                var parents = transforms.Select(t => t.parent).ToArray();
                var poses = transforms.Select(t => t.localToWorldMatrix).ToArray();
                string animatorBefore = EditorJsonUtility.ToJson(root.GetComponentInChildren<ProductionAircraftControlAnimator>(true));
                var visibility = root.GetComponentInChildren<AircraftVisibilityProfileController>(true);
                visibility.SetCockpitView();
                // The test also runs against the already-authored prefab. The
                // owned batch is not one of the preserved source renderers.
                var originalRenderers = root.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.name != ProductionAircraftExteriorBatchAuthoring.BatchName).ToArray();
                var cockpitEnabled = originalRenderers.Select(r => r.enabled).ToArray();
                visibility.SetExternalView();
                var externalEnabled = originalRenderers.Select(r => r.enabled).ToArray();
                mesh = ProductionAircraftExteriorBatchAuthoring.CreateMesh(root);
                Assert.That(mesh.GetIndexCount(0) / 3, Is.EqualTo(4457));
                ProductionAircraftExteriorBatchAuthoring.Configure(root, mesh);
                ProductionAircraftExteriorBatchAuthoring.Configure(root, mesh);
                Assert.That(originalRenderers.Select(r => r.enabled), Is.EqualTo(externalEnabled), "Authoring must preserve existing enabled flags.");
                Assert.That(root.GetComponentsInChildren<Transform>(true).Count(t => t.name == ProductionAircraftExteriorBatchAuthoring.BatchName), Is.EqualTo(1));
                Renderer combined = root.transform.Find("AircraftVisualRoot/" + ProductionAircraftExteriorBatchAuthoring.BatchName).GetComponent<Renderer>();
                Assert.That(combined.sharedMaterial, Is.SameAs(sources[0].sharedMaterial));
                Assert.That(combined.motionVectorGenerationMode, Is.EqualTo(sources[0].motionVectorGenerationMode));
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    visibility.SetExternalView();
                    Assert.That(combined.enabled, Is.True);
                    Assert.That(sources.All(r => !r.enabled), Is.True);
                    for (int i = 0; i < originalRenderers.Length; i++)
                        if (!sources.Contains(originalRenderers[i])) Assert.That(originalRenderers[i].enabled, Is.EqualTo(externalEnabled[i]), originalRenderers[i].name);
                    visibility.SetCockpitView();
                    Assert.That(combined.enabled, Is.False);
                    Assert.That(originalRenderers.Select(r => r.enabled), Is.EqualTo(cockpitEnabled));
                }
                for (int i = 0; i < transforms.Length; i++)
                {
                    Assert.That(transforms[i].parent, Is.EqualTo(parents[i]));
                    Assert.That(transforms[i].localToWorldMatrix, Is.EqualTo(poses[i]));
                }
                Assert.That(sources.Select(r => r.GetComponent<MeshFilter>().sharedMesh), Is.EqualTo(sourceMeshes));
                Assert.That(EditorJsonUtility.ToJson(root.GetComponentInChildren<ProductionAircraftControlAnimator>(true)), Is.EqualTo(animatorBefore));
                var menu = root.GetComponentInChildren<ProductFlightModeMenuView>(true);
                Assert.That(menu.ValidateAuthoredReferences(out string error), Is.True, error);
                typeof(ProductFlightModeMenuView).GetMethod("HideAircraftPresentation", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
                Assert.That(combined.forceRenderingOff, Is.True);
                Assert.That(sources.All(r => r.forceRenderingOff), Is.True);
                typeof(ProductFlightModeMenuView).GetMethod("RestoreAircraftPresentation", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, null);
                Assert.That(combined.forceRenderingOff, Is.False);
                Assert.That(sources.All(r => !r.forceRenderingOff), Is.True);
                ProductionAircraftExteriorBatchAuthoring.ValidateGeometry(root, mesh);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
                if (mesh != null) Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void IndependentMaterialOverrideRejectsBatching()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ProductionAircraftExteriorBatchAuthoring.PrefabPath);
            try
            {
                var source = ProductionAircraftExteriorBatchAuthoring.GetSources(root)[0];
                var block = new MaterialPropertyBlock(); block.SetFloat("_TestOverride", 0.5f); source.SetPropertyBlock(block);
                var exception = Assert.Throws<InvalidOperationException>(() => ProductionAircraftExteriorBatchAuthoring.CreateMesh(root));
                Assert.That(exception.Message, Does.Contain("material property override"));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void ChangedGeometryIsRejected()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ProductionAircraftExteriorBatchAuthoring.PrefabPath);
            Mesh mesh = null;
            try
            {
                mesh = ProductionAircraftExteriorBatchAuthoring.CreateMesh(root);
                var vertices = mesh.vertices; vertices[0] += Vector3.right * 0.01f; mesh.vertices = vertices;
                var exception = Assert.Throws<InvalidOperationException>(() => ProductionAircraftExteriorBatchAuthoring.ValidateGeometry(root, mesh));
                Assert.That(exception.Message, Does.Contain("Position changed"));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); if (mesh != null) Object.DestroyImmediate(mesh); }
        }
    }
}
