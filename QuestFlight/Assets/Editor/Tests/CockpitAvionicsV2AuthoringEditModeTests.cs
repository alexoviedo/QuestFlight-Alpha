using NUnit.Framework;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using UnityEditor;

namespace QuestFlight.Editor.Tests
{
    public sealed class CockpitAvionicsV2AuthoringEditModeTests
    {
        // Immutable intended bitmap rows from the audited author-designed font,
        // not a replay of the encoder or its former uint arithmetic.
        private static readonly System.Collections.Generic.Dictionary<char, string> IntendedGlyphs =
            new System.Collections.Generic.Dictionary<char, string>
            {
                { ' ', "00000000000000000000000000000000000" },
                { '.', "00000000000000000000000000011000110" },
                { '-', "00000000000000011111000000000000000" },
                { '>', "10000010000010000010001000100010000" },
                { '/', "00001000100010001000100000000000000" },
                { '0', "01110100011001110101110011000101110" },
                { '1', "00100011000010000100001000010001110" },
                { '2', "01110100010000100010001000100011111" },
                { '3', "11110000010000101110000010000111110" },
                { '4', "00010001100101010010111110001000010" },
                { '5', "11111100001000011110000010000111110" },
                { '6', "01110100001000011110100011000101110" },
                { '7', "11111000010001000100010000100001000" },
                { '8', "01110100011000101110100011000101110" },
                { '9', "01110100011000101111000010000101110" },
                { 'A', "01110100011000111111100011000110001" },
                { 'B', "11110100011000111110100011000111110" },
                { 'C', "01111100001000010000100001000001111" },
                { 'D', "11110100011000110001100011000111110" },
                { 'E', "11111100001000011110100001000011111" },
                { 'F', "11111100001000011110100001000010000" },
                { 'G', "01111100001000010111100011000101111" },
                { 'H', "10001100011000111111100011000110001" },
                { 'I', "01110001000010000100001000010001110" },
                { 'J', "00001000010000100001100011000101110" },
                { 'K', "10001100101010011000101001001010001" },
                { 'L', "10000100001000010000100001000011111" },
                { 'M', "10001110111010110101100011000110001" },
                { 'N', "10001110011010110011100011000110001" },
                { 'O', "01110100011000110001100011000101110" },
                { 'P', "11110100011000111110100001000010000" },
                { 'Q', "01110100011000110001101011001001101" },
                { 'R', "11110100011000111110101001001010001" },
                { 'S', "01111100001000001110000010000111110" },
                { 'T', "11111001000010000100001000010000100" },
                { 'U', "10001100011000110001100011000101110" },
                { 'V', "10001100011000110001100010101000100" },
                { 'W', "10001100011000110101101011010101010" },
                { 'X', "10001100010101000100010101000110001" },
                { 'Y', "10001100010101000100001000010000100" },
                { 'Z', "11111000010001000100010001000011111" },
            };
        private const string IntendedFallback = "01110100010001000100001000000000100";

        [Test]
        public void EveryGlyphMatchesAll35IntendedPixelsAndRegeneratedAtlasBytes()
        {
            const int width = 256, height = 96;
            var expected = new byte[width * height * 4];
            for (int i = 0; i < expected.Length; i += 4)
            { expected[i] = 2; expected[i + 1] = 5; expected[i + 2] = 2; expected[i + 3] = 255; }
            for (int code = 32; code < 127; code++)
            {
                char character = char.ToUpperInvariant((char)code);
                string rows = IntendedGlyphs.TryGetValue(character, out string designed) ? designed : IntendedFallback;
                ulong mask = CockpitAvionicsV2PresentationAuthoring.GlyphBits((char)code);
                for (int bit = 0; bit < 35; bit++)
                {
                    bool on = rows[bit] == '1';
                    Assert.That((mask & (1UL << bit)) != 0, Is.EqualTo(on), $"ASCII{code} bit{bit}");
                    if (!on) continue;
                    int glyph = code - 32;
                    int x = glyph % 16 * 16 + 3 + bit % 5 * 2;
                    int y = height - (glyph / 16 + 1) * 16 + 1 + (6 - bit / 5) * 2;
                    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
                    {
                        int index = ((y + dy) * width + x + dx) * 4;
                        expected[index] = 164; expected[index + 1] = 218; expected[index + 2] = 132;
                    }
                }
            }
            string text = System.IO.File.ReadAllText(CockpitAvionicsV2PresentationAuthoring.GlyphAtlasPath);
            string hex = System.Text.RegularExpressions.Regex.Match(text, @"(?m)^  _typelessdata: ([0-9a-f]+)$").Groups[1].Value;
            Assert.That(hex.Length, Is.GreaterThanOrEqualTo(expected.Length * 2));
            var observed = new byte[expected.Length];
            for (int i = 0; i < observed.Length; i++) observed[i] = byte.Parse(hex.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber);
            CollectionAssert.AreEqual(expected, observed, "Complete base-mip byte comparison, all95cells and margins.");
        }

        [Test]
        public void Every35BitBasisRowIsIndependentIncludingBottomRightPixels()
        {
            ulong[] expected = { 0x1UL, 0x2UL, 0x4UL, 0x8UL, 0x10UL, 0x20UL, 0x40UL, 0x80UL, 0x100UL, 0x200UL, 0x400UL, 0x800UL, 0x1000UL, 0x2000UL, 0x4000UL, 0x8000UL, 0x10000UL, 0x20000UL, 0x40000UL, 0x80000UL, 0x100000UL, 0x200000UL, 0x400000UL, 0x800000UL, 0x1000000UL, 0x2000000UL, 0x4000000UL, 0x8000000UL, 0x10000000UL, 0x20000000UL, 0x40000000UL, 0x80000000UL, 0x100000000UL, 0x200000000UL, 0x400000000UL };
            for (int bit = 0; bit < 35; bit++)
            {
                var rows = new[] { "00000", "00000", "00000", "00000", "00000", "00000", "00000" };
                rows[bit / 5] = rows[bit / 5].Remove(bit % 5, 1).Insert(bit % 5, "1");
                Assert.That(CockpitAvionicsV2PresentationAuthoring.Rows(rows), Is.EqualTo(expected[bit]), $"basis{bit}");
            }
        }

        [Test]
        public void CurrentProduct_RemovesLegacyDigitsAndLowestPinkScreen()
        {
            CockpitDepthOwnershipEditorContract.ValidateProductPrefab();
        }
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
            // 140 live-screen backgrounds plus one blank unsupported lower display.
            Assert.That(layout.CombinedGlyphMesh.subMeshCount, Is.EqualTo(1));
            Assert.That(
                layout.CombinedGlyphMesh.vertexCount,
                Is.EqualTo(
                    (CockpitAvionicsV2PresentationAuthoring.ExpectedGlyphSlots + 141) * 4));
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
