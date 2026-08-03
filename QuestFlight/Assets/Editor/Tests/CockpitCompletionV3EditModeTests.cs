#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QuestFlight.Editor;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class CockpitCompletionV3EditModeTests
    {
        [Test]
        public void GeneratedLayoutsAreBoundedValidatedSingleBatchAssets()
        {
            CockpitInstrumentV2Layout instruments =
                AssetDatabase.LoadAssetAtPath<CockpitInstrumentV2Layout>(
                    CockpitInstrumentV2Authoring.LayoutPath);
            Assert.That(instruments, Is.Not.Null);
            Assert.That(instruments.Validate(out string instrumentError),
                Is.True, instrumentError);
            Assert.That(instruments.Bindings.Length, Is.EqualTo(12));
            Assert.That(instruments.AltitudeDigitSlots, Is.Empty,
                "The principal ALT readout must use one stable face surface.");
            Assert.That(
                instruments.Bindings.Where(binding =>
                    binding.label.StartsWith("altimeter_"))
                    .Select(binding => binding.sourceTriangleSeed)
                    .Distinct().ToArray(),
                Is.EqualTo(new[] { 2713 }),
                "All animated altimeter hands must occupy the authored six-pack " +
                "aperture selected in pilot-view space.");
            Assert.That(instruments.CombinedDynamicMesh.subMeshCount,
                Is.EqualTo(1));
            Assert.That(instruments.ExistingSourceMaterial, Is.Not.Null);
            var selectors = new List<Vector2>();
            instruments.NeutralizedSourceMesh.GetUVs(3, selectors);
            Assert.That(selectors.Count,
                Is.EqualTo(instruments.NeutralizedSourceMesh.vertexCount));
            Assert.That(selectors, Has.All.EqualTo(Vector2.zero),
                "The neutralized source mesh must never sample the presentation atlas.");

            Assert.That(instruments.StaticOverlayVertexCount,
                Is.GreaterThan(0));
            Assert.That(instruments.NeutralizedTriangleCount,
                Is.EqualTo(
                    instruments.NeutralizedSourceMesh.triangles.Length / 3));
            Assert.That(instruments.DynamicTriangleCount,
                Is.EqualTo(
                    instruments.CombinedDynamicMesh.triangles.Length / 3));
            Assert.That(instruments.NeutralizedTriangleCount,
                Is.LessThan(instruments.SourceTriangleCount),
                "The exact source faces and their static indications must be removed " +
                "before the animated replacement batch is drawn.");

            CockpitAvionicsV2Layout avionics =
                AssetDatabase.LoadAssetAtPath<CockpitAvionicsV2Layout>(
                    CockpitAvionicsV2PresentationAuthoring.LayoutPath);
            Assert.That(avionics, Is.Not.Null);
            Assert.That(avionics.Validate(out string avionicsError),
                Is.True, avionicsError);
            Assert.That(
                avionics.GlyphSlots.Length,
                Is.EqualTo(
                    CockpitAvionicsV2PresentationAuthoring
                        .ExpectedGlyphSlots));
            Assert.That(
                avionics.EstimatedTextureBytes,
                Is.EqualTo(
                    CockpitAvionicsV2PresentationAuthoring
                        .EstimatedTextureBytes));
        }

        [Test]
        public void ProductPrefabSerializesOneAuthorityAndBothBatchPresenters()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(
                CockpitDynamicsIntegrationAuthoring.AircraftPrefabPath);
            try
            {
                Assert.That(
                    CockpitCompletionV3Authoring.ValidatePrefabContents(
                        root, out string error),
                    Is.True,
                    error);
                Assert.That(
                    root.GetComponentsInChildren<
                        ProductionCockpitSystemsController>(true).Length,
                    Is.EqualTo(1));
                Assert.That(
                    root.GetComponentsInChildren<
                        CockpitInstrumentV2BatchController>(true).Length,
                    Is.EqualTo(1));
                Assert.That(
                    root.GetComponentsInChildren<
                        CockpitAvionicsV2BatchController>(true).Length,
                    Is.EqualTo(1));
                CockpitTouchInteractionV2Controller touch =
                    root.GetComponentInChildren<
                        CockpitTouchInteractionV2Controller>(true);
                Assert.That(touch, Is.Not.Null);
                Assert.That(
                    touch.BindingCount,
                    Is.EqualTo(
                        CockpitTouchInteractionV2Authoring
                            .ExpectedBindingCount));

                Transform lens = root.transform.Find(
                    CockpitCompletionV3Authoring.InstrumentLensObjectPath);
                Assert.That(lens, Is.Not.Null);
                Assert.That(lens.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(lens.localRotation, Is.EqualTo(Quaternion.identity));
                Assert.That(lens.localScale, Is.EqualTo(Vector3.one));
                Mesh lensMesh = lens.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(lensMesh.vertexCount, Is.EqualTo(13 * 25));
                Assert.That(lensMesh.triangles.Length / 3,
                    Is.EqualTo(13 * 24));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
#endif
