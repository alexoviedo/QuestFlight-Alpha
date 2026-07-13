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
