using UnityEngine;

namespace QuestFlightLab.Aircraft
{
    [DisallowMultipleComponent]
    public sealed class AircraftVisibilityProfileController : MonoBehaviour
    {
        public enum VisibilityProfile
        {
            Cockpit,
            External
        }

        [SerializeField] private Renderer[] interiorRenderers;
        [SerializeField] private Renderer[] exteriorRenderers;
        [SerializeField] private Renderer[] cockpitOccludingExteriorRenderers;
        [SerializeField] private Renderer externalBatchRenderer;
        [SerializeField] private Renderer[] externalBatchSourceRenderers;
        [SerializeField] private bool showInteriorInExternalView;
        [SerializeField] private VisibilityProfile initialProfile = VisibilityProfile.Cockpit;

        public VisibilityProfile ActiveProfile { get; private set; }

        public void ConfigureAuthoredRenderers(
            Renderer[] interior,
            Renderer[] exterior,
            Renderer[] cockpitOccluders)
        {
            interiorRenderers = interior;
            exteriorRenderers = exterior;
            cockpitOccludingExteriorRenderers = cockpitOccluders;
        }

        private void Awake() => ApplyProfile(initialProfile);

        public void ConfigureAuthoredExternalBatch(Renderer combined, Renderer[] sources)
        {
            externalBatchRenderer = combined;
            externalBatchSourceRenderers = sources;
        }

        public void SetCockpitView() => ApplyProfile(VisibilityProfile.Cockpit);
        public void SetExternalView() => ApplyProfile(VisibilityProfile.External);

        public void ApplyProfile(VisibilityProfile profile)
        {
            ActiveProfile = profile;
            SetEnabled(exteriorRenderers, true);
            if (profile == VisibilityProfile.Cockpit)
            {
                SetEnabled(interiorRenderers, true);
                SetEnabled(cockpitOccludingExteriorRenderers, false);
            }
            else
            {
                SetEnabled(interiorRenderers, showInteriorInExternalView);
            }
            // The optional batch is authored offline. Original geometry remains in
            // the cockpit profile so its fine-grained culling is unchanged.
            if (externalBatchRenderer != null)
            {
                bool external = profile == VisibilityProfile.External;
                externalBatchRenderer.enabled = external;
                SetEnabled(externalBatchSourceRenderers, !external);
            }
        }

        private static void SetEnabled(Renderer[] renderers, bool value)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null) renderers[i].enabled = value;
            }
        }
    }
}
