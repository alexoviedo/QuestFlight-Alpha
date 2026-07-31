using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Aircraft.CockpitDynamics.V2
{
    /// <summary>
    /// One-controller/one-renderer presenter for all positively identified V2
    /// instrument geometry. Runtime never discovers or creates hierarchy.
    /// Unsupported channels hold their authored rest pose.
    /// </summary>
    [DefaultExecutionOrder(CockpitSystemsExecutionOrder.Presentation)]
    [DisallowMultipleComponent]
    public sealed class CockpitInstrumentV2BatchController : MonoBehaviour
    {
        private const MeshUpdateFlags UploadFlags =
            MeshUpdateFlags.DontRecalculateBounds |
            MeshUpdateFlags.DontValidateIndices |
            MeshUpdateFlags.DontNotifyMeshUsers;

        [SerializeField] private MonoBehaviour authorityProvider;
        [SerializeField] private CockpitInstrumentV2Layout layout;
        [SerializeField] private MeshFilter neutralizedSourceFilter;
        [SerializeField] private MeshRenderer neutralizedSourceRenderer;
        [SerializeField] private MeshFilter combinedDynamicFilter;
        [SerializeField] private MeshRenderer combinedDynamicRenderer;

        private ICockpitInstrumentV2AuthorityProvider _authority;
        private Mesh _runtimeMesh;
        private Vector3[] _baseVertices;
        private Vector3[] _baseNormals;
        private Vector4[] _baseTangents;
        private List<Vector3> _workingVertices;
        private List<Vector3> _workingNormals;
        private List<Vector4> _workingTangents;

        public CockpitInstrumentV2Layout Layout => layout;
        public MonoBehaviour AuthorityProvider => authorityProvider;

        public void ConfigureAuthoredReferences(
            MonoBehaviour authoredAuthorityProvider,
            CockpitInstrumentV2Layout authoredLayout,
            MeshFilter authoredNeutralizedSourceFilter,
            MeshRenderer authoredNeutralizedSourceRenderer,
            MeshFilter authoredCombinedDynamicFilter,
            MeshRenderer authoredCombinedDynamicRenderer)
        {
            authorityProvider = authoredAuthorityProvider;
            layout = authoredLayout;
            neutralizedSourceFilter = authoredNeutralizedSourceFilter;
            neutralizedSourceRenderer = authoredNeutralizedSourceRenderer;
            combinedDynamicFilter = authoredCombinedDynamicFilter;
            combinedDynamicRenderer = authoredCombinedDynamicRenderer;
        }

        public bool ValidateAuthoredReferences(out string error)
        {
            if (!(authorityProvider is ICockpitInstrumentV2AuthorityProvider))
            {
                error =
                    "V2 instruments require one serialized authoritative provider.";
                return false;
            }
            if (layout == null)
            {
                error = "The V2 instrument layout is missing.";
                return false;
            }
            if (!layout.Validate(out error))
                return false;
            if (neutralizedSourceFilter == null ||
                neutralizedSourceRenderer == null ||
                combinedDynamicFilter == null ||
                combinedDynamicRenderer == null)
            {
                error = "V2 instrument renderer references are incomplete.";
                return false;
            }
            if (neutralizedSourceFilter.sharedMesh != layout.NeutralizedSourceMesh)
            {
                error = "The V2 source renderer is not neutralized.";
                return false;
            }

            Mesh dynamicMesh = _runtimeMesh != null
                ? _runtimeMesh
                : combinedDynamicFilter.sharedMesh;
            if (dynamicMesh != layout.CombinedDynamicMesh &&
                dynamicMesh != _runtimeMesh)
            {
                error = "The V2 combined renderer has the wrong mesh.";
                return false;
            }
            if (neutralizedSourceRenderer.sharedMaterials.Length != 1 ||
                combinedDynamicRenderer.sharedMaterials.Length != 1 ||
                neutralizedSourceRenderer.sharedMaterial !=
                    layout.ExistingSourceMaterial ||
                combinedDynamicRenderer.sharedMaterial !=
                    layout.ExistingSourceMaterial)
            {
                error =
                    "V2 static and dynamic renderers must share the existing source material.";
                return false;
            }
            if (combinedDynamicRenderer.shadowCastingMode !=
                    ShadowCastingMode.Off ||
                combinedDynamicRenderer.receiveShadows)
            {
                error = "V2 dynamic instruments must not add cockpit shadow cost.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void Awake()
        {
            if (!ValidateAuthoredReferences(out string error))
            {
                Debug.LogError("[QuestFlight][CockpitInstrumentV2] " + error, this);
                enabled = false;
                return;
            }
            _authority =
                (ICockpitInstrumentV2AuthorityProvider)authorityProvider;
            _runtimeMesh = Instantiate(layout.CombinedDynamicMesh);
            _runtimeMesh.name = layout.CombinedDynamicMesh.name + "_RuntimeV2";
            _runtimeMesh.MarkDynamic();
            combinedDynamicFilter.sharedMesh = _runtimeMesh;

            _baseVertices = layout.CombinedDynamicMesh.vertices;
            _baseNormals = layout.CombinedDynamicMesh.normals;
            _baseTangents = layout.CombinedDynamicMesh.tangents;
            if (_baseVertices.Length != layout.CombinedDynamicMesh.vertexCount ||
                _baseNormals.Length != _baseVertices.Length ||
                _baseTangents.Length != _baseVertices.Length)
            {
                Debug.LogError(
                    "[QuestFlight][CockpitInstrumentV2] Authored position, normal, " +
                    "or tangent streams are incomplete.", this);
                enabled = false;
                return;
            }
            _workingVertices = new List<Vector3>(_baseVertices);
            _workingNormals = new List<Vector3>(_baseNormals);
            _workingTangents = new List<Vector4>(_baseTangents);
        }

        private void OnDestroy()
        {
            if (_runtimeMesh == null) return;
            Destroy(_runtimeMesh);
            _runtimeMesh = null;
        }

        private void LateUpdate()
        {
            if (_runtimeMesh == null ||
                _authority == null ||
                !_authority.TryGetInstrumentV2Snapshot(
                    out CockpitInstrumentV2Snapshot snapshot) ||
                !snapshot.isValid)
            {
                ApplyAuthoredRestPose();
                return;
            }

            ApplySnapshot(snapshot);
        }

        public void ApplySnapshotForTest(
            in CockpitInstrumentV2Snapshot snapshot,
            bool uploadToMesh)
        {
            EnsureWorkingDataForTest();
            ApplySnapshot(snapshot, uploadToMesh);
        }

        private void ApplySnapshot(
            in CockpitInstrumentV2Snapshot snapshot)
        {
            ApplySnapshot(snapshot, true);
        }

        private void ApplySnapshot(
            in CockpitInstrumentV2Snapshot snapshot,
            bool uploadToMesh)
        {
            CockpitInstrumentV2MeshBinding[] bindings = layout.Bindings;
            for (int bindingIndex = 0;
                 bindingIndex < bindings.Length;
                 bindingIndex++)
            {
                CockpitInstrumentV2MeshBinding binding = bindings[bindingIndex];
                float rotationValue = 0f;
                float translationValue = 0f;
                bool hasRotation = binding.HasRotation &&
                    CockpitInstrumentV2Kinematics.TryEvaluate(
                        binding.rotationFunction, snapshot,
                        out rotationValue);
                bool hasTranslation = binding.HasTranslation &&
                    CockpitInstrumentV2Kinematics.TryEvaluate(
                        binding.translationFunction, snapshot,
                        out translationValue);

                float rotationDegrees = hasRotation
                    ? (rotationValue - binding.authoredRotationValue) *
                      binding.rotationDegreesPerUnit
                    : 0f;
                float translationMeters = hasTranslation
                    ? (translationValue - binding.authoredTranslationValue) *
                      binding.translationMetersPerUnit
                    : 0f;
                Quaternion rotation = Quaternion.AngleAxis(
                    rotationDegrees, binding.rotationAxisLocal);
                Vector3 translation =
                    binding.translationAxisLocal * translationMeters;

                int end = binding.firstVertex + binding.vertexCount;
                for (int vertex = binding.firstVertex; vertex < end; vertex++)
                {
                    _workingVertices[vertex] =
                        binding.pivotLocal +
                        rotation * (
                            _baseVertices[vertex] -
                            binding.pivotLocal +
                            translation);
                    _workingNormals[vertex] =
                        rotation * _baseNormals[vertex];
                    Vector4 source = _baseTangents[vertex];
                    Vector3 tangent = rotation * new Vector3(
                        source.x, source.y, source.z);
                    _workingTangents[vertex] = new Vector4(
                        tangent.x, tangent.y, tangent.z, source.w);
                }
            }

            if (!uploadToMesh) return;
            Upload();
        }

        private void ApplyAuthoredRestPose()
        {
            if (_runtimeMesh == null || _workingVertices == null) return;
            for (int index = 0; index < _baseVertices.Length; index++)
            {
                _workingVertices[index] = _baseVertices[index];
                _workingNormals[index] = _baseNormals[index];
                _workingTangents[index] = _baseTangents[index];
            }
            Upload();
        }

        private void Upload()
        {
            _runtimeMesh.SetVertices(
                _workingVertices, 0, _workingVertices.Count, UploadFlags);
            _runtimeMesh.SetNormals(
                _workingNormals, 0, _workingNormals.Count, UploadFlags);
            _runtimeMesh.SetTangents(
                _workingTangents, 0, _workingTangents.Count, UploadFlags);
        }

        private void EnsureWorkingDataForTest()
        {
            if (_baseVertices != null) return;
            if (layout == null || layout.CombinedDynamicMesh == null)
                throw new System.InvalidOperationException(
                    "A V2 instrument layout is required.");
            Mesh mesh = layout.CombinedDynamicMesh;
            _baseVertices = mesh.vertices;
            _baseNormals = mesh.normals;
            _baseTangents = mesh.tangents;
            _workingVertices = new List<Vector3>(_baseVertices);
            _workingNormals = new List<Vector3>(_baseNormals);
            _workingTangents = new List<Vector4>(_baseTangents);
        }
    }
}
