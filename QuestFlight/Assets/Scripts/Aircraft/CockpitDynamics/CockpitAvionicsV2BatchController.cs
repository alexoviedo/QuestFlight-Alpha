using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Aircraft.CockpitDynamics.V2
{
    /// <summary>
    /// Updates UVs on one authored glyph mesh. This is the sole V2 avionics
    /// presenter; controls and display apertures never receive per-item Updates.
    /// </summary>
    [DefaultExecutionOrder(CockpitSystemsExecutionOrder.Presentation)]
    [DisallowMultipleComponent]
    public sealed class CockpitAvionicsV2BatchController : MonoBehaviour
    {
        private const MeshUpdateFlags UploadFlags =
            MeshUpdateFlags.DontRecalculateBounds |
            MeshUpdateFlags.DontValidateIndices |
            MeshUpdateFlags.DontNotifyMeshUsers;

        [SerializeField] private MonoBehaviour authorityProvider;
        [SerializeField] private CockpitAvionicsV2Layout layout;
        [SerializeField] private MeshFilter combinedGlyphFilter;
        [SerializeField] private MeshRenderer combinedGlyphRenderer;

        private ICockpitAvionicsV2AuthorityProvider _authority;
        private readonly CockpitAvionicsV2Formatter _formatter =
            new CockpitAvionicsV2Formatter();
        private Mesh _runtimeMesh;
        private List<Vector2> _workingUv;

        public CockpitAvionicsV2Layout Layout => layout;
        public MonoBehaviour AuthorityProvider => authorityProvider;

        public void ConfigureAuthoredReferences(
            MonoBehaviour authoredAuthorityProvider,
            CockpitAvionicsV2Layout authoredLayout,
            MeshFilter authoredCombinedGlyphFilter,
            MeshRenderer authoredCombinedGlyphRenderer)
        {
            authorityProvider = authoredAuthorityProvider;
            layout = authoredLayout;
            combinedGlyphFilter = authoredCombinedGlyphFilter;
            combinedGlyphRenderer = authoredCombinedGlyphRenderer;
        }

        public bool ValidateAuthoredReferences(out string error)
        {
            if (!(authorityProvider is ICockpitAvionicsV2AuthorityProvider))
            {
                error = "V2 avionics require one serialized authoritative provider.";
                return false;
            }
            if (layout == null)
            {
                error = "The V2 avionics layout is missing.";
                return false;
            }
            if (!layout.Validate(out error))
                return false;
            if (combinedGlyphFilter == null || combinedGlyphRenderer == null)
            {
                error = "V2 avionics combined renderer references are incomplete.";
                return false;
            }
            Mesh mesh = _runtimeMesh != null
                ? _runtimeMesh
                : combinedGlyphFilter.sharedMesh;
            if (mesh != layout.CombinedGlyphMesh && mesh != _runtimeMesh)
            {
                error = "V2 avionics combined renderer has the wrong mesh.";
                return false;
            }
            if (combinedGlyphRenderer.sharedMaterials.Length != 1 ||
                combinedGlyphRenderer.sharedMaterial !=
                    layout.ExistingReadoutMaterial)
            {
                error =
                    "V2 avionics must reuse exactly one existing readout material.";
                return false;
            }
            if (combinedGlyphRenderer.shadowCastingMode !=
                    ShadowCastingMode.Off ||
                combinedGlyphRenderer.receiveShadows)
            {
                error = "V2 avionics glyphs must not add shadow cost.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void Awake()
        {
            if (!ValidateAuthoredReferences(out string error))
            {
                Debug.LogError("[QuestFlight][CockpitAvionicsV2] " + error, this);
                enabled = false;
                return;
            }
            _authority =
                (ICockpitAvionicsV2AuthorityProvider)authorityProvider;
            _runtimeMesh = Instantiate(layout.CombinedGlyphMesh);
            _runtimeMesh.name = layout.CombinedGlyphMesh.name + "_RuntimeV2";
            _runtimeMesh.MarkDynamic();
            combinedGlyphFilter.sharedMesh = _runtimeMesh;
            _workingUv = new List<Vector2>(
                layout.CombinedGlyphMesh.uv);
            ApplyCharacters(_formatter.Characters, true);
        }

        private void OnDestroy()
        {
            if (_runtimeMesh == null) return;
            Destroy(_runtimeMesh);
            _runtimeMesh = null;
        }

        private void LateUpdate()
        {
            if (_runtimeMesh == null || _authority == null)
                return;
            bool hasSnapshot = _authority.TryGetAvionicsV2Snapshot(
                out CockpitAvionicsV2Snapshot snapshot);
            if (!hasSnapshot) snapshot = default;
            _formatter.Format(snapshot);
            ApplyCharacters(_formatter.Characters, true);
        }

        public void ApplySnapshotForTest(
            in CockpitAvionicsV2Snapshot snapshot,
            bool uploadToMesh)
        {
            EnsureWorkingDataForTest();
            _formatter.Format(snapshot);
            ApplyCharacters(_formatter.Characters, uploadToMesh);
        }

        private void ApplyCharacters(char[] characters, bool uploadToMesh)
        {
            CockpitAvionicsV2GlyphSlot[] slots = layout.GlyphSlots;
            float cellWidth = 1f / layout.AtlasColumns;
            float cellHeight = 1f / layout.AtlasRows;
            float insetU = 0.5f / layout.ExistingGlyphAtlas.width;
            float insetV = 0.5f / layout.ExistingGlyphAtlas.height;

            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                CockpitAvionicsV2GlyphSlot slot = slots[slotIndex];
                int ascii = characters[slot.characterIndex];
                int glyph = ascii - layout.FirstAsciiCode;
                if (glyph < 0 || glyph >= layout.GlyphCount)
                    glyph = '?' - layout.FirstAsciiCode;
                if (glyph < 0 || glyph >= layout.GlyphCount)
                    glyph = 0;

                int column = glyph % layout.AtlasColumns;
                int row = glyph / layout.AtlasColumns;
                float u0 = column * cellWidth + insetU;
                float u1 = (column + 1) * cellWidth - insetU;
                float v0 = 1f - (row + 1) * cellHeight + insetV;
                float v1 = 1f - row * cellHeight - insetV;
                int first = slot.firstVertex;
                _workingUv[first] = new Vector2(u0, v0);
                _workingUv[first + 1] = new Vector2(u1, v0);
                _workingUv[first + 2] = new Vector2(u1, v1);
                _workingUv[first + 3] = new Vector2(u0, v1);
            }

            if (!uploadToMesh || _runtimeMesh == null) return;
            _runtimeMesh.SetUVs(
                0, _workingUv, 0, _workingUv.Count, UploadFlags);
        }

        private void EnsureWorkingDataForTest()
        {
            if (_workingUv != null) return;
            if (layout == null || layout.CombinedGlyphMesh == null)
                throw new System.InvalidOperationException(
                    "A V2 avionics layout is required.");
            _workingUv = new List<Vector2>(layout.CombinedGlyphMesh.uv);
        }
    }
}
