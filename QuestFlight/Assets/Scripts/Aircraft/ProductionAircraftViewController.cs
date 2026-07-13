using UnityEngine;

namespace QuestFlightLab.Aircraft
{
    /// <summary>
    /// Switches between two authored cameras and the matching renderer profile.
    /// Camera transforms are never synthesized from the tracked head pose.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionAircraftViewController : MonoBehaviour
    {
        [SerializeField] private Camera cockpitCamera;
        [SerializeField] private Camera externalCamera;
        [SerializeField] private AircraftVisibilityProfileController visibilityProfiles;

        public bool IsExternalView { get; private set; }

        public void ConfigureAuthoredReferences(
            Camera authoredCockpitCamera,
            Camera authoredExternalCamera,
            AircraftVisibilityProfileController authoredVisibilityProfiles)
        {
            cockpitCamera = authoredCockpitCamera;
            externalCamera = authoredExternalCamera;
            visibilityProfiles = authoredVisibilityProfiles;
        }

        private void Awake() => SetCockpitView();

        public void SetCockpitView()
        {
            IsExternalView = false;
            if (cockpitCamera != null) cockpitCamera.enabled = true;
            if (externalCamera != null) externalCamera.enabled = false;
            visibilityProfiles?.SetCockpitView();
        }

        public void SetExternalView()
        {
            IsExternalView = true;
            if (cockpitCamera != null) cockpitCamera.enabled = false;
            if (externalCamera != null) externalCamera.enabled = true;
            visibilityProfiles?.SetExternalView();
        }
    }
}
