using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics.Analog
{
    /// <summary>
    /// Serialized adapter for one neutralized meter renderer and one combined
    /// dynamic analog renderer. Runtime code never creates GameObjects, searches
    /// hierarchy, writes the XR camera, or assigns unproven instrument channels.
    /// </summary>
    [DefaultExecutionOrder(CockpitSystemsExecutionOrder.Presentation)]
    [DisallowMultipleComponent]
    public sealed class ProductionAnalogInstrumentController : MonoBehaviour
    {
        [SerializeField] private ProductionCockpitSystemsController systems;
        [SerializeField] private ProductionAnalogInstrumentLayout layout;
        [SerializeField] private MeshFilter neutralizedMeterFilter;
        [SerializeField] private MeshRenderer neutralizedMeterRenderer;
        [SerializeField] private MeshFilter dynamicInstrumentFilter;
        [SerializeField] private MeshRenderer dynamicInstrumentRenderer;

        private Mesh _runtimeDynamicMesh;
        private AnalogInstrumentMeshAnimator _animator;

        public ProductionAnalogInstrumentLayout Layout => layout;
        public ProductionCockpitSystemsController Systems => systems;

        public void ConfigureAuthoredReferences(
            ProductionCockpitSystemsController authoredSystems,
            ProductionAnalogInstrumentLayout authoredLayout,
            MeshFilter authoredNeutralizedMeterFilter,
            MeshRenderer authoredNeutralizedMeterRenderer,
            MeshFilter authoredDynamicInstrumentFilter,
            MeshRenderer authoredDynamicInstrumentRenderer)
        {
            systems = authoredSystems;
            layout = authoredLayout;
            neutralizedMeterFilter = authoredNeutralizedMeterFilter;
            neutralizedMeterRenderer = authoredNeutralizedMeterRenderer;
            dynamicInstrumentFilter = authoredDynamicInstrumentFilter;
            dynamicInstrumentRenderer = authoredDynamicInstrumentRenderer;
        }

        public bool ValidateAuthoredReferences(out string error)
        {
            if (systems == null)
            {
                error = "Analog instruments require the authored cockpit systems source.";
                return false;
            }
            if (layout == null)
            {
                error = "The authored analog instrument layout is missing.";
                return false;
            }
            if (!layout.Validate(out error)) return false;
            if (neutralizedMeterFilter == null || neutralizedMeterRenderer == null)
            {
                error = "The authored neutralized meter renderer is missing.";
                return false;
            }
            if (dynamicInstrumentFilter == null || dynamicInstrumentRenderer == null)
            {
                error = "The authored combined dynamic instrument renderer is missing.";
                return false;
            }
            if (neutralizedMeterFilter.sharedMesh != layout.NeutralizedMeterMesh)
            {
                error = "The meter renderer is not using the neutralized source mesh.";
                return false;
            }
            Mesh dynamicMesh = Application.isPlaying && _runtimeDynamicMesh != null
                ? _runtimeDynamicMesh
                : dynamicInstrumentFilter.sharedMesh;
            if (dynamicMesh != layout.DynamicInstrumentMesh &&
                dynamicMesh != _runtimeDynamicMesh)
            {
                error = "The dynamic renderer is not using the authored combined mesh.";
                return false;
            }
            if (neutralizedMeterRenderer.sharedMaterials.Length != 1 ||
                dynamicInstrumentRenderer.sharedMaterials.Length != 1 ||
                neutralizedMeterRenderer.sharedMaterial !=
                    layout.SharedSourceMeterMaterial ||
                dynamicInstrumentRenderer.sharedMaterial !=
                    layout.SharedSourceMeterMaterial)
            {
                error =
                    "Both meter renderers must share exactly the existing source meter " +
                    "material; unique analog materials are prohibited.";
                return false;
            }
            if (dynamicInstrumentRenderer.shadowCastingMode !=
                    UnityEngine.Rendering.ShadowCastingMode.Off ||
                dynamicInstrumentRenderer.receiveShadows)
            {
                error =
                    "The tiny dynamic pointer renderer must not add cockpit shadow cost.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void Awake()
        {
            if (!ValidateAuthoredReferences(out string error))
            {
                Debug.LogError("[QuestFlight][AnalogInstruments] " + error, this);
                enabled = false;
                return;
            }

            _runtimeDynamicMesh = Instantiate(layout.DynamicInstrumentMesh);
            _runtimeDynamicMesh.name =
                layout.DynamicInstrumentMesh.name + "_Runtime";
            _runtimeDynamicMesh.MarkDynamic();
            dynamicInstrumentFilter.sharedMesh = _runtimeDynamicMesh;
            var motionProperties = new MaterialPropertyBlock();
            dynamicInstrumentRenderer.GetPropertyBlock(motionProperties);motionProperties.SetFloat("_QFPreviousVertices",1);dynamicInstrumentRenderer.SetPropertyBlock(motionProperties);
            _animator = new AnalogInstrumentMeshAnimator(
                layout.DynamicInstrumentMesh, layout.Bindings);
            dynamicInstrumentRenderer.enabled = false;
        }

        private void OnDisable()
        {
            if (dynamicInstrumentRenderer != null)
                dynamicInstrumentRenderer.enabled = false;
        }

        private void OnDestroy()
        {
            _animator?.Dispose();
            if (_runtimeDynamicMesh == null) return;
            Destroy(_runtimeDynamicMesh);
            _runtimeDynamicMesh = null;
        }

        private void LateUpdate()
        {
            if (_animator == null || systems == null || !systems.HasValidSnapshot)
            {
                if (dynamicInstrumentRenderer != null)
                    dynamicInstrumentRenderer.enabled = false;
                return;
            }
            dynamicInstrumentRenderer.enabled = true;
            CockpitSystemsSnapshot snapshot = systems.CurrentSnapshot;
            _animator.Apply(snapshot, _runtimeDynamicMesh, true);
        }
    }
}
