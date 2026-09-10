using NUnit.Framework;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using UnityEditor;

namespace QuestFlight.Editor.Tests
{
    public sealed class CockpitAvionicsV2AuthoringEditModeTests
    {
        [Test]
        public void CurrentProduct_AuditsPhysicalAperturesWithoutClaimingDmeOrAudio()
        {
            CockpitAvionicsV2Authoring.AuditReport report =
                CockpitAvionicsV2Authoring.AuditCurrentProduct();

            Assert.That(report.sourceMeshExact, Is.True);
            Assert.That(report.sourceVertices,
                Is.EqualTo(CockpitAvionicsV2Authoring.ExpectedReadoutVertices));
            Assert.That(report.sourceTriangles,
                Is.EqualTo(CockpitAvionicsV2Authoring.ExpectedReadoutTriangles));
            Assert.That(report.gpsApertureTriangles, Is.GreaterThan(0));
            Assert.That(report.radioApertureTriangles, Is.GreaterThan(0));
            Assert.That(report.transponderApertureTriangles, Is.GreaterThan(0));
            Assert.That(report.onlyOwnedAperturesSelected, Is.True);
            CollectionAssert.AreEqual(
                new[] { 0, 1, 2, 3, 4, 6 },
                CockpitAvionicsV2Authoring.OwnedCharacterRows);
            CollectionAssert.AreEqual(
                new[] { 5, 7 },
                CockpitAvionicsV2Authoring.UnownedCharacterRows);
        }

        [Test]
        public void CurrentProduct_HasReviewedOneDrawRuntimePresentation()
        {
            CockpitAvionicsV2Authoring.AuditReport report =
                CockpitAvionicsV2Authoring.AuditCurrentProduct();

            Assert.That(report.materialOpaque, Is.True);
            Assert.That(report.materialUsesAuditedAtlas, Is.True);
            Assert.That(report.textureBudgetPass, Is.True);
            Assert.That(report.fixedGlyphManifestPresent, Is.True);
            Assert.That(report.concreteAuthorityProviderPresent, Is.True);
            Assert.That(report.liveChannelAuthorityManifestPresent, Is.True);
            Assert.That(report.canGenerateRuntimeLayout, Is.True);
            Assert.That(report.blockers, Is.Empty);

            CockpitAvionicsV2Layout layout =
                AssetDatabase.LoadAssetAtPath<CockpitAvionicsV2Layout>(
                    CockpitAvionicsV2PresentationAuthoring.LayoutPath);
            Assert.That(
                CockpitAvionicsV2EditorContract.ValidateLayout(
                    layout, out string error),
                Is.True, error);
            // The accepted depth repair adds 140 nonoverlapping black background quads.
            Assert.That(layout.CombinedGlyphMesh.subMeshCount, Is.EqualTo(1));
            Assert.That(
                layout.CombinedGlyphMesh.vertexCount,
                Is.EqualTo(
                    (CockpitAvionicsV2PresentationAuthoring.ExpectedGlyphSlots + 140) * 4));
            Assert.That(
                layout.GlyphSlots.Length,
                Is.EqualTo(
                    CockpitAvionicsV2PresentationAuthoring
                        .ExpectedGlyphSlots));
            Assert.That(layout.EstimatedTextureBytes,
                Is.LessThanOrEqualTo(1024 * 1024));
        }
    }
}
