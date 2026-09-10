using UnityEngine;
using UnityEngine.SceneManagement;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Flight.Backends;

namespace QuestFlightLab.Runtime
{
    /// <summary>
    /// Identifies the explicitly authored product scene. Legacy runtime repair
    /// code must treat this marker as an ownership boundary and leave the scene
    /// hierarchy untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionVerticalSliceRoot : MonoBehaviour
    {
        public const string SceneName = "ProductionVerticalSlice";
        public const string ArchitectureVersion = "production_vertical_slice_v2";

        [SerializeField] private string architectureVersion = ArchitectureVersion;
        [SerializeField] private AircraftReferenceFrameRig aircraftRig;
        [SerializeField] private PilotSeatProfile pilotSeatProfile;
        [SerializeField] private ProductionSeatCalibrationController seatCalibration;
        [SerializeField] private AircraftVisibilityProfileController visibilityProfiles;
        [SerializeField] private ProductionAircraftControlAnimator controlAnimator;
        [SerializeField] private FlightDynamicsCoordinator flightDynamicsCoordinator;
        [SerializeField] private Transform environmentRoot;
        [SerializeField] private Transform runwayRoot;
        [SerializeField] private OVRManager.FoveatedRenderingLevel fixedFoveationLevel = OVRManager.FoveatedRenderingLevel.High;

#if UNITY_ANDROID && !UNITY_EDITOR
        private Coroutine foveationRoutine;
        private void OnEnable() => ScheduleFoveation();
        private void OnDisable()
        {
            if (foveationRoutine != null) StopCoroutine(foveationRoutine);
            foveationRoutine = null;
        }
        private void OnApplicationPause(bool paused)
        {
            if (!paused && isActiveAndEnabled) ScheduleFoveation();
        }
        private void ScheduleFoveation()
        {
            if (foveationRoutine != null) StopCoroutine(foveationRoutine);
            foveationRoutine = StartCoroutine(ApplyFoveation());
        }
        private System.Collections.IEnumerator ApplyFoveation()
        {
            var displays = new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>(1);
            float deadline = Time.realtimeSinceStartup + 30f;
            bool running = false;
            while (!running && Time.realtimeSinceStartup < deadline)
            {
                SubsystemManager.GetSubsystems(displays);
                foreach (var display in displays) running |= display.running;
                if (!running) yield return null;
            }
            var settings = UnityEngine.XR.OpenXR.OpenXRSettings.Instance;
            var feature = settings == null ? null : settings.GetFeature<Meta.XR.MetaXRFoveationFeature>();
            if (!running || feature == null || !feature.enabled)
            {
                Debug.LogWarning("[QuestFlight][Foveation] Display or authored Meta foveation feature unavailable; no level applied.");
                yield break;
            }
            // Apply after session creation. No camera or hierarchy is created or moved.
            Meta.XR.MetaXRFoveationFeature.useDynamicFoveatedRendering = false;
            Meta.XR.MetaXRFoveationFeature.foveatedRenderingLevel = fixedFoveationLevel;
            yield return null;
            var actual = Meta.XR.MetaXRFoveationFeature.foveatedRenderingLevel;
            Debug.Log($"[QuestFlight][Foveation] requested={fixedFoveationLevel}; actual={actual}; dynamicRequested=false");
            if (actual != fixedFoveationLevel)
                Debug.LogWarning("[QuestFlight][Foveation] Runtime level differs from request; check support and diagnostic overrides.");
            foveationRoutine = null;
        }
#endif

        public string AuthoredArchitectureVersion => architectureVersion;
        public AircraftReferenceFrameRig AircraftRig => aircraftRig;
        public PilotSeatProfile PilotSeatProfile => pilotSeatProfile;
        public ProductionSeatCalibrationController SeatCalibration => seatCalibration;
        public AircraftVisibilityProfileController VisibilityProfiles => visibilityProfiles;
        public ProductionAircraftControlAnimator ControlAnimator => controlAnimator;
        public FlightDynamicsCoordinator FlightDynamicsCoordinator => flightDynamicsCoordinator;
        public Transform EnvironmentRoot => environmentRoot;
        public Transform RunwayRoot => runwayRoot;

        public void ConfigureAuthoredReferences(
            AircraftReferenceFrameRig authoredAircraftRig,
            PilotSeatProfile authoredPilotSeatProfile,
            ProductionSeatCalibrationController authoredSeatCalibration,
            AircraftVisibilityProfileController authoredVisibilityProfiles,
            ProductionAircraftControlAnimator authoredControlAnimator,
            FlightDynamicsCoordinator authoredCoordinator,
            Transform authoredEnvironmentRoot,
            Transform authoredRunwayRoot)
        {
            aircraftRig = authoredAircraftRig;
            pilotSeatProfile = authoredPilotSeatProfile;
            seatCalibration = authoredSeatCalibration;
            visibilityProfiles = authoredVisibilityProfiles;
            controlAnimator = authoredControlAnimator;
            flightDynamicsCoordinator = authoredCoordinator;
            environmentRoot = authoredEnvironmentRoot;
            runwayRoot = authoredRunwayRoot;
        }

        public static bool IsProductionSceneLoaded()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.name == SceneName) return true;

            return Object.FindFirstObjectByType<ProductionVerticalSliceRoot>(FindObjectsInactive.Include) != null;
        }
    }
}
