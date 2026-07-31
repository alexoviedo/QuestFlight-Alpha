using NUnit.Framework;

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
        public void CurrentProduct_FailsClosedOnTruthfulKnownBlockers()
        {
            CockpitAvionicsV2Authoring.AuditReport report =
                CockpitAvionicsV2Authoring.AuditCurrentProduct();

            Assert.That(report.materialOpaque, Is.True);
            Assert.That(report.materialUsesAuditedAtlas, Is.True);
            Assert.That(report.textureBudgetPass, Is.True);
            Assert.That(report.fixedGlyphManifestPresent, Is.False);
            Assert.That(report.concreteAuthorityProviderPresent, Is.True);
            Assert.That(report.liveChannelAuthorityManifestPresent, Is.False);
            Assert.That(report.canGenerateRuntimeLayout, Is.False);
            Assert.That(report.blockers, Has.Some.Contains(
                "composed static GPS/radio/transponder image"));
            Assert.That(report.blockers, Has.Some.Contains(
                "provider exposes electrical power only"));
        }
    }
}
