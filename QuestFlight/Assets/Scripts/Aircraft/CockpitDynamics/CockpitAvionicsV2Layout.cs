using System;
using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics.V2
{
    [Flags]
    public enum CockpitAvionicsV2Availability : ushort
    {
        None = 0,
        ElectricalPower = 1 << 0,
        GpsNavigation = 1 << 1,
        ComRadios = 1 << 2,
        Dme = 1 << 3,
        Transponder = 1 << 4,
        AudioPanel = 1 << 5
    }

    public enum CockpitAvionicsV2TransponderMode : byte
    {
        Off = 0,
        Standby = 1,
        On = 2,
        Altitude = 3
    }

    /// <summary>
    /// Allocation-free state for the represented GPS/radio/DME/transponder/audio
    /// apertures. Packed identifiers and integer frequencies avoid runtime
    /// strings. Unavailable systems remain blank.
    /// </summary>
    [Serializable]
    public struct CockpitAvionicsV2Snapshot
    {
        public bool isValid;
        public CockpitAvionicsV2Availability availability;
        public bool avionicsBusPowered;

        // Four 7-bit ASCII characters, least-significant byte first.
        public uint gpsDestinationIdentAscii;
        public float gpsBearingDegrees;
        public float gpsDistanceNauticalMiles;
        public float gpsGroundTrackDegrees;
        public bool gpsGroundTrackAvailable;
        public float gpsAltitudeMslFeet;

        public int com1ActiveKHz;
        public int com1StandbyKHz;
        public int com2ActiveKHz;
        public int com2StandbyKHz;

        public float dmeDistanceNauticalMiles;
        public float dmeGroundSpeedKnots;
        public int dmeTimeToStationSeconds;

        // Four BCD nibbles; each must be in the transponder range 0..7.
        public ushort transponderCodeBcd;
        public CockpitAvionicsV2TransponderMode transponderMode;
        public ushort audioSelectionMask;

        public bool Has(CockpitAvionicsV2Availability required)
        {
            return isValid && (availability & required) == required;
        }
    }

    public interface ICockpitAvionicsV2AuthorityProvider
    {
        bool TryGetAvionicsV2Snapshot(out CockpitAvionicsV2Snapshot snapshot);
    }

    [Serializable]
    public struct CockpitAvionicsV2GlyphSlot
    {
        public int characterIndex;
        public int firstVertex;
    }

    /// <summary>
    /// Authored one-draw glyph mesh using an existing readout material/atlas.
    /// The runtime changes UVs only; no Canvas, TMP, camera, render texture, or
    /// per-control renderer is permitted.
    /// </summary>
    public sealed class CockpitAvionicsV2Layout : ScriptableObject
    {
        [SerializeField] private Mesh combinedGlyphMesh;
        [SerializeField] private Material existingReadoutMaterial;
        [SerializeField] private Texture2D existingGlyphAtlas;
        [SerializeField] private CockpitAvionicsV2GlyphSlot[] glyphSlots =
            Array.Empty<CockpitAvionicsV2GlyphSlot>();
        [SerializeField, Min(1)] private int atlasColumns = 16;
        [SerializeField, Min(1)] private int atlasRows = 6;
        [SerializeField] private int firstAsciiCode = 32;
        [SerializeField] private int glyphCount = 95;
        [SerializeField, Min(1)] private int estimatedTextureBytes;
        [SerializeField] private string sourceAssetGuid = string.Empty;
        [SerializeField] private string sourceSha256 = string.Empty;

        public Mesh CombinedGlyphMesh => combinedGlyphMesh;
        public Material ExistingReadoutMaterial => existingReadoutMaterial;
        public Texture2D ExistingGlyphAtlas => existingGlyphAtlas;
        public CockpitAvionicsV2GlyphSlot[] GlyphSlots => glyphSlots;
        public int AtlasColumns => atlasColumns;
        public int AtlasRows => atlasRows;
        public int FirstAsciiCode => firstAsciiCode;
        public int GlyphCount => glyphCount;
        public int EstimatedTextureBytes => estimatedTextureBytes;
        public string SourceAssetGuid => sourceAssetGuid;
        public string SourceSha256 => sourceSha256;

        public void ConfigureGenerated(
            Mesh authoredCombinedGlyphMesh,
            Material authoredExistingReadoutMaterial,
            Texture2D authoredExistingGlyphAtlas,
            CockpitAvionicsV2GlyphSlot[] authoredGlyphSlots,
            int authoredAtlasColumns,
            int authoredAtlasRows,
            int authoredFirstAsciiCode,
            int authoredGlyphCount,
            int authoredEstimatedTextureBytes,
            string authoredSourceAssetGuid,
            string authoredSourceSha256)
        {
            combinedGlyphMesh = authoredCombinedGlyphMesh;
            existingReadoutMaterial = authoredExistingReadoutMaterial;
            existingGlyphAtlas = authoredExistingGlyphAtlas;
            glyphSlots = authoredGlyphSlots ?? Array.Empty<CockpitAvionicsV2GlyphSlot>();
            atlasColumns = authoredAtlasColumns;
            atlasRows = authoredAtlasRows;
            firstAsciiCode = authoredFirstAsciiCode;
            glyphCount = authoredGlyphCount;
            estimatedTextureBytes = authoredEstimatedTextureBytes;
            sourceAssetGuid = authoredSourceAssetGuid ?? string.Empty;
            sourceSha256 = authoredSourceSha256 ?? string.Empty;
        }

        public bool Validate(out string error)
        {
            if (combinedGlyphMesh == null ||
                existingReadoutMaterial == null ||
                existingGlyphAtlas == null)
            {
                error = "V2 avionics glyph mesh/material/atlas references are incomplete.";
                return false;
            }
            if (combinedGlyphMesh.subMeshCount != 1)
            {
                error = "V2 avionics require one combined glyph submesh.";
                return false;
            }
            if (existingReadoutMaterial.renderQueue >=
                    (int)UnityEngine.Rendering.RenderQueue.Transparent ||
                string.Equals(
                    existingReadoutMaterial.GetTag(
                        "RenderType", false, string.Empty),
                    "Transparent",
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    "V2 avionics require an opaque material; transparent sorting is prohibited.";
                return false;
            }
            Vector2[] sourceUv = combinedGlyphMesh.uv;
            if (sourceUv == null || sourceUv.Length != combinedGlyphMesh.vertexCount)
            {
                error = "V2 avionics glyph mesh requires one UV per vertex.";
                return false;
            }
            if (glyphSlots == null || glyphSlots.Length == 0 ||
                glyphSlots.Length > CockpitAvionicsV2Formatter.CharacterCapacity)
            {
                error = "V2 avionics glyph slots are missing or exceed the fixed buffer.";
                return false;
            }
            if (atlasColumns <= 0 || atlasRows <= 0 ||
                firstAsciiCode < 0 || glyphCount <= 0 ||
                glyphCount > atlasColumns * atlasRows)
            {
                error = "V2 avionics atlas metadata is invalid.";
                return false;
            }
            if (existingGlyphAtlas.width > 1024 ||
                existingGlyphAtlas.height > 1024 ||
                estimatedTextureBytes <= 0 ||
                estimatedTextureBytes > 1024 * 1024)
            {
                error = "V2 avionics atlas exceeds the 1 MiB-class Quest contract.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(sourceAssetGuid) ||
                sourceSha256 == null || sourceSha256.Length != 64)
            {
                error = "V2 avionics source identity is incomplete.";
                return false;
            }

            bool[] usedCharacters =
                new bool[CockpitAvionicsV2Formatter.CharacterCapacity];
            bool[] usedVertices = new bool[combinedGlyphMesh.vertexCount];
            for (int index = 0; index < glyphSlots.Length; index++)
            {
                CockpitAvionicsV2GlyphSlot slot = glyphSlots[index];
                if (slot.characterIndex < 0 ||
                    slot.characterIndex >= usedCharacters.Length ||
                    usedCharacters[slot.characterIndex])
                {
                    error = $"V2 glyph slot {index} has a duplicate/bad character index.";
                    return false;
                }
                if (slot.firstVertex < 0 ||
                    slot.firstVertex + 4 > usedVertices.Length)
                {
                    error = $"V2 glyph slot {index} has a bad vertex range.";
                    return false;
                }
                usedCharacters[slot.characterIndex] = true;
                for (int vertex = slot.firstVertex;
                     vertex < slot.firstVertex + 4;
                     vertex++)
                {
                    if (usedVertices[vertex])
                    {
                        error = $"V2 glyph slot {index} overlaps another slot.";
                        return false;
                    }
                    usedVertices[vertex] = true;
                }
            }

            error = string.Empty;
            return true;
        }
    }
}
