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
        private const float MagneticCompassResponseSeconds = 0.45f;

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
        private MaterialPropertyBlock _materialProperties;
        private MaterialPropertyBlock _sourceMaterialProperties;
        private int _displayAltitudeFeet;
        private bool _hasDisplayAltitude;
        private float _magneticCompassHeadingDegrees;
        private bool _hasMagneticCompassHeading;

        private static readonly int AltitudeDigitsId =
            Shader.PropertyToID("_AltitudeDigits");
        private static readonly int AttitudePivotId =
            Shader.PropertyToID("_AttitudePivot");
        private static readonly int AttitudeRotationId =
            Shader.PropertyToID("_AttitudeRotation");

        public CockpitInstrumentV2Layout Layout => layout;
        public MonoBehaviour AuthorityProvider => authorityProvider;
        public MeshRenderer NeutralizedSourceRenderer =>
            neutralizedSourceRenderer;

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
            _materialProperties = new MaterialPropertyBlock();
            combinedDynamicRenderer.GetPropertyBlock(_materialProperties);
            _sourceMaterialProperties = new MaterialPropertyBlock();
            neutralizedSourceRenderer.GetPropertyBlock(_sourceMaterialProperties);
            _materialProperties.SetFloat("_QFPreviousVertices",1);
            QuestFlight.Rendering.ProductSpaceWarpController.FrameCompleted += FinishMotionFrame;
            ApplyAttitudeBallRotation(Quaternion.identity);
            ApplyAltitudeDigitsProperty(0);
        }

        private void OnDestroy()
        {
            motionHistory?.Dispose();
            QuestFlight.Rendering.ProductSpaceWarpController.FrameCompleted -= FinishMotionFrame;
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
            ApplySnapshot(snapshot, uploadToMesh, float.PositiveInfinity);
        }

        private void ApplySnapshot(
            in CockpitInstrumentV2Snapshot snapshot)
        {
            ApplySnapshot(snapshot, true, Time.deltaTime);
        }

        private void ApplySnapshot(
            in CockpitInstrumentV2Snapshot snapshot,
            bool uploadToMesh,
            float deltaTimeSeconds)
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
                if (hasRotation &&
                    binding.rotationFunction == CockpitInstrumentV2Function
                        .MagneticCompassHeadingDegrees)
                {
                    rotationValue = StabilizeMagneticCompassHeading(
                        rotationValue,
                        deltaTimeSeconds);
                }
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

            ApplyAltitudeDigits(snapshot);
            ApplyAttitude(snapshot);

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
            ApplyAttitudeBallRotation(Quaternion.identity);
            _hasMagneticCompassHeading = false;
            Upload();
        }

        private float StabilizeMagneticCompassHeading(
            float targetHeadingDegrees,
            float deltaTimeSeconds)
        {
            if (!_hasMagneticCompassHeading ||
                float.IsPositiveInfinity(deltaTimeSeconds))
            {
                _magneticCompassHeadingDegrees =
                    CockpitInstrumentV2Kinematics.NormalizeHeadingDegrees(
                        targetHeadingDegrees);
                _hasMagneticCompassHeading = true;
                return _magneticCompassHeadingDegrees;
            }

            _magneticCompassHeadingDegrees =
                CockpitInstrumentV2Kinematics.DampHeadingDegrees(
                    _magneticCompassHeadingDegrees,
                    targetHeadingDegrees,
                    deltaTimeSeconds,
                    MagneticCompassResponseSeconds);
            return _magneticCompassHeadingDegrees;
        }

        private void ApplyAttitude(
            in CockpitInstrumentV2Snapshot snapshot)
        {
            bool hasPitch = CockpitInstrumentV2Kinematics.TryEvaluate(
                CockpitInstrumentV2Function.AttitudePitchDegrees,
                snapshot,
                out float pitchDegrees);
            bool hasBank = CockpitInstrumentV2Kinematics.TryEvaluate(
                CockpitInstrumentV2Function.AttitudeBankDegrees,
                snapshot,
                out float bankDegrees);
            if (!hasPitch || !hasBank)
            {
                ApplyAttitudeBallRotation(Quaternion.identity);
                return;
            }

            CockpitInstrumentV2AttitudeContract contract =
                layout.AttitudeContract;
            ApplyAttitudeBallRotation(
                CockpitInstrumentV2Kinematics.EvaluateAttitudeBallRotation(
                    pitchDegrees,
                    bankDegrees,
                    contract.pitchAxisLocal,
                    contract.bankAxisLocal));
        }

        private void ApplyAttitudeBallRotation(Quaternion rotation)
        {
            if (neutralizedSourceRenderer == null || layout == null)
                return;
            if (_sourceMaterialProperties == null)
            {
                _sourceMaterialProperties = new MaterialPropertyBlock();
                neutralizedSourceRenderer.GetPropertyBlock(
                    _sourceMaterialProperties);
            }
            currentAttitude = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
            _sourceMaterialProperties.SetVector("_QFPreviousAttitudeRotation",presentedAttitude);
            CockpitInstrumentV2AttitudeContract contract =
                layout.AttitudeContract;
            _sourceMaterialProperties.SetVector(
                AttitudePivotId,
                contract.pivotLocal);
            _sourceMaterialProperties.SetVector(
                AttitudeRotationId,
                new Vector4(rotation.x, rotation.y, rotation.z, rotation.w));
            neutralizedSourceRenderer.SetPropertyBlock(
                _sourceMaterialProperties);
        }

        private QuestFlight.Rendering.ProductDeformedMeshHistory motionHistory;
        private Vector4 presentedAttitude = new Vector4(0,0,0,1), currentAttitude = new Vector4(0,0,0,1);
        private void FinishMotionFrame(Camera camera) { presentedAttitude = currentAttitude; }
        private void Upload()
        {
            (motionHistory ??= new QuestFlight.Rendering.ProductDeformedMeshHistory()).Upload(_runtimeMesh,_workingVertices);
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

        private void ApplyAltitudeDigits(
            in CockpitInstrumentV2Snapshot snapshot)
        {
            if (!CockpitInstrumentV2Kinematics.TryEvaluateIndicatedAltitudeFeet(
                    snapshot, out float indicatedFeet))
                return;

            int stabilized =
                CockpitInstrumentV2Kinematics.StabilizeAltitudeReadoutFeet(
                    indicatedFeet,
                    _displayAltitudeFeet,
                    _hasDisplayAltitude);
            if (_hasDisplayAltitude && stabilized == _displayAltitudeFeet)
                return;
            _displayAltitudeFeet = stabilized;
            _hasDisplayAltitude = true;
            ApplyAltitudeDigitsProperty(stabilized);
        }

        private void ApplyAltitudeDigitsProperty(int altitudeFeet)
        {
            if (combinedDynamicRenderer == null)
                return;
            if (_materialProperties == null)
            {
                _materialProperties = new MaterialPropertyBlock();
                combinedDynamicRenderer.GetPropertyBlock(_materialProperties);
            }
            int altitude = Mathf.Clamp(altitudeFeet, 0, 9990);
            _materialProperties.SetVector(
                AltitudeDigitsId,
                new Vector4(
                    altitude / 1000 % 10,
                    altitude / 100 % 10,
                    altitude / 10 % 10,
                    altitude % 10));
            combinedDynamicRenderer.SetPropertyBlock(_materialProperties);
        }
    }
}
