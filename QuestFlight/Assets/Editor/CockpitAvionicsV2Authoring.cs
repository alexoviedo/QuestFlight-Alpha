using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Eligibility audit and bounded entry point for the V2 avionics
    /// presentation. The authoring implementation owns one fixed glyph atlas,
    /// one opaque material, and one combined mesh over audited apertures.
    /// </summary>
    public static class CockpitAvionicsV2Authoring
    {
        public const string ExistingReadoutMeshPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/Meshes/C172_InstrumentDisplayReadouts.asset";
        public const string ExistingReadoutMaterialPath =
            CockpitAvionicsV2EditorContract.ExistingReadoutMaterialPath;
        public const string ExistingReadoutAtlasPath =
            CockpitAvionicsV2EditorContract.ExistingReadableBasePath;
        public const string RequiredGlyphManifestPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/Textures/C172_V2GlyphAtlasManifest.json";
        public const string RequiredAuthorityManifestPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/C172_V2AvionicsAuthorityManifest.json";
        public const string FrozenStaticAtlasSha256 =
            "b2f2ef58b36ba20856eb35a095db4d79cf8dd44f75da726a5fa7c5b7228c3f92";
        public const int ExpectedReadoutVertices = 36;
        public const int ExpectedReadoutTriangles = 12;
        public const int MaximumTextureBytes = 1024 * 1024;

        public static readonly int[] OwnedCharacterRows = { 0, 1, 2, 3, 4, 6 };
        public static readonly int[] UnownedCharacterRows = { 5, 7 };

        [Serializable]
        public sealed class AuditReport
        {
            public bool sourceMeshExact;
            public int sourceVertices;
            public int sourceTriangles;
            public int gpsApertureTriangles;
            public int radioApertureTriangles;
            public int transponderApertureTriangles;
            public bool onlyOwnedAperturesSelected;
            public bool materialOpaque;
            public bool materialUsesAuditedAtlas;
            public int atlasWidth;
            public int atlasHeight;
            public int estimatedAndroidTextureBytes;
            public bool textureBudgetPass;
            public bool fixedGlyphManifestPresent;
            public bool concreteAuthorityProviderPresent;
            public int concreteAuthorityProviderTypes;
            public bool liveChannelAuthorityManifestPresent;
            public bool canGenerateRuntimeLayout;
            public string[] blockers = Array.Empty<string>();
            public string[] ownedChannels =
            {
                "GPS rows 0-2",
                "COM1/COM2 rows 3-4",
                "transponder row 6"
            };
            public string[] excludedChannels =
            {
                "DME row 5: no independently proven physical aperture",
                "audio row 7: no independently proven physical aperture"
            };
        }

        [MenuItem("QuestFlight/Cockpit/Audit V2 Avionics Authoring Eligibility")]
        public static void AuditMenu()
        {
            AuditReport report = AuditCurrentProduct();
            Debug.Log(
                "[QuestFlight][CockpitAvionicsV2] " +
                JsonUtility.ToJson(report, true));
        }

        [MenuItem("QuestFlight/Cockpit/Build V2 Avionics Layout If Eligible")]
        public static void BuildAssetsIfEligible()
        {
            CockpitAvionicsV2PresentationAuthoring.BuildAssetsOnly();
            AuditReport report = AuditCurrentProduct();
            if (!report.canGenerateRuntimeLayout)
                throw new InvalidOperationException(
                    "V2 avionics authoring is fail-closed:\n- " +
                    string.Join("\n- ", report.blockers));

            CockpitAvionicsV2Layout layout =
                AssetDatabase.LoadAssetAtPath<CockpitAvionicsV2Layout>(
                    CockpitAvionicsV2PresentationAuthoring.LayoutPath);
            if (!CockpitAvionicsV2EditorContract.ValidateLayout(
                    layout, out string error))
                throw new InvalidOperationException(error);
        }

        public static AuditReport AuditCurrentProduct()
        {
            var report = new AuditReport();
            var blockers = new List<string>();

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(
                ExistingReadoutMeshPath);
            if (mesh != null)
            {
                report.sourceVertices = mesh.vertexCount;
                report.sourceTriangles = mesh.triangles.Length / 3;
                report.sourceMeshExact =
                    report.sourceVertices == ExpectedReadoutVertices &&
                    report.sourceTriangles == ExpectedReadoutTriangles &&
                    mesh.subMeshCount == 1;
                CountOwnedApertureTriangles(mesh, report);
                report.onlyOwnedAperturesSelected =
                    report.gpsApertureTriangles > 0 &&
                    report.radioApertureTriangles > 0 &&
                    report.transponderApertureTriangles > 0 &&
                    report.gpsApertureTriangles +
                    report.radioApertureTriangles +
                    report.transponderApertureTriangles ==
                    report.sourceTriangles;
            }
            if (!report.sourceMeshExact || !report.onlyOwnedAperturesSelected)
                blockers.Add(
                    "Existing readout aperture mesh identity/UV ownership is not exact.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                ExistingReadoutMaterialPath);
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(
                ExistingReadoutAtlasPath);
            report.materialOpaque =
                material != null &&
                material.renderQueue < (int)UnityEngine.Rendering.RenderQueue.Transparent &&
                string.Equals(
                    material.GetTag("RenderType", false), "Opaque",
                    StringComparison.Ordinal);
            report.materialUsesAuditedAtlas =
                material != null && atlas != null &&
                material.mainTexture == atlas;
            if (!report.materialOpaque || !report.materialUsesAuditedAtlas)
                blockers.Add(
                    "The existing one-draw material/atlas binding is not the audited opaque contract.");

            if (atlas != null)
            {
                report.atlasWidth = atlas.width;
                report.atlasHeight = atlas.height;
                // Android is pinned to ASTC 6x6; include complete mip overhead.
                int blocksX = (atlas.width + 5) / 6;
                int blocksY = (atlas.height + 5) / 6;
                report.estimatedAndroidTextureBytes =
                    (int)Math.Ceiling(blocksX * blocksY * 16.0 * 4.0 / 3.0);
                report.textureBudgetPass =
                    report.estimatedAndroidTextureBytes <= MaximumTextureBytes;
            }
            if (!report.textureBudgetPass)
                blockers.Add("The readout atlas exceeds the 1 MiB Quest ceiling.");

            TextAsset glyphManifest = AssetDatabase.LoadAssetAtPath<TextAsset>(
                RequiredGlyphManifestPath);
            report.fixedGlyphManifestPresent =
                glyphManifest != null &&
                glyphManifest.text.Contains("\"glyph_count\": 95") &&
                glyphManifest.text.Contains("\"first_ascii_code\": 32");
            if (!report.fixedGlyphManifestPresent)
                blockers.Add(
                    "No fixed 95-glyph ASCII manifest exists; the current 512 atlas is " +
                    "a composed static GPS/radio/transponder image, not a glyph atlas.");

            report.concreteAuthorityProviderTypes = TypeCache
                .GetTypesDerivedFrom<ICockpitAvionicsV2AuthorityProvider>()
                .Count(type => !type.IsAbstract && !type.IsInterface);
            report.concreteAuthorityProviderPresent =
                report.concreteAuthorityProviderTypes > 0;
            if (!report.concreteAuthorityProviderPresent)
                blockers.Add(
                    "No concrete ICockpitAvionicsV2AuthorityProvider exists.");

            TextAsset authorityManifest =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    RequiredAuthorityManifestPath);
            report.liveChannelAuthorityManifestPresent =
                authorityManifest != null &&
                authorityManifest.text.Contains(
                    "\"schema\":\"questflight.cockpit.avionics_authority.v2\"") &&
                authorityManifest.text.Contains("\"live_channels\"");
            if (!report.liveChannelAuthorityManifestPresent)
                blockers.Add(
                    "No reviewed live-channel authority manifest exists. The current " +
                    "provider exposes electrical power only, so GPS/COM/DME/transponder/" +
                    "audio must remain blank.");

            report.blockers = blockers.ToArray();
            report.canGenerateRuntimeLayout = blockers.Count == 0;
            return report;
        }

        private static void CountOwnedApertureTriangles(
            Mesh mesh,
            AuditReport report)
        {
            Vector2[] uv = mesh.uv;
            int[] triangles = mesh.triangles;
            if (uv == null || uv.Length != mesh.vertexCount)
                return;
            for (int triangle = 0; triangle < triangles.Length; triangle += 3)
            {
                float centerV =
                    (uv[triangles[triangle]].y +
                     uv[triangles[triangle + 1]].y +
                     uv[triangles[triangle + 2]].y) / 3f;
                if (centerV < 0.5f)
                    report.gpsApertureTriangles++;
                else if (centerV < 0.75f)
                    report.radioApertureTriangles++;
                else
                    report.transponderApertureTriangles++;
            }
        }
    }
}
