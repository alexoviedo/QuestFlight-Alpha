using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlight.Product
{
    /// <summary>
    /// Serialized, startup-only owner of the product flight-mode selection.
    /// It coordinates existing lifecycle and input APIs and never writes flight controls
    /// or any aircraft, XR, camera, seat, or calibration transform.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-10000)]
    public sealed class ProductFlightModeCoordinator : MonoBehaviour
    {
        public enum FlightModeState
        {
            Selection = 0,
            Demo = 1,
            FreeFlight = 2,
            Blocked = 3,
            Training = 4
        }

        public const string SelectionText =
            "QUESTFLIGHT\n" +
            "THUMBSTICK - CHOOSE A FLIGHT\n" +
            "A - SELECT    B / Y - BACK\n" +
            "MENU - APP MENU";
        public const string PreparingText = "QUESTFLIGHT\nPreparing your aircraft…";
        public const string LessonsUnavailableText =
            "FLIGHT LESSONS ARE UNAVAILABLE. RETURN TO THE MAIN MENU AND TRY AGAIN.";

        [SerializeField] private ProductDemoPilot demoPilot;
        [SerializeField] private ProductControlInput productInput;
        [SerializeField] private FlightDynamicsCoordinator flightCoordinator;
        [SerializeField] private ProductionSeatCalibrationController seatCalibration;
        [SerializeField] private GameObject modePanelRoot;
        [SerializeField] private TextMesh modePanelText;
        [SerializeField] private ProductFlightModeMenuView modeMenuView;
        [SerializeField] private Training.ProductTrainingController training;
        [SerializeField] private ProductAppNavigation navigation;
        public ProductAppNavigation AuthoredNavigation => navigation;
        public void ConfigureNavigationForAuthoring(ProductAppNavigation value) => navigation = value;
        public Training.ProductTrainingController AuthoredTraining => training;
        public void ConfigureTrainingForAuthoring(Training.ProductTrainingController value) => training = value;
        [SerializeField, Min(0.1f)] private float lessonsMessageSeconds = 2.5f;

        public FlightModeState State { get; private set; } = FlightModeState.Selection;
        public string LastError { get; private set; } = string.Empty;
        public int FlightSessionVersion { get; private set; }
        public ProductDemoPilot AuthoredDemoPilot => demoPilot;
        public ProductControlInput AuthoredProductInput => productInput;
        public FlightDynamicsCoordinator AuthoredFlightCoordinator => flightCoordinator;
        public ProductionSeatCalibrationController AuthoredSeatCalibration => seatCalibration;
        public GameObject AuthoredModePanelRoot => modePanelRoot;
        public TextMesh AuthoredModePanelText => modePanelText;
        public ProductFlightModeMenuView AuthoredModeMenuView => modeMenuView;
        public bool OwnsTimeScalePause => _ownsTimeScalePause;
        public float SavedTimeScale => _savedTimeScale;
        public bool HasAuthoredReferences =>
            demoPilot != null &&
            productInput != null &&
            flightCoordinator != null &&
            seatCalibration != null &&
            modePanelRoot != null &&
            modePanelText != null &&
            modeMenuView != null &&
            modeMenuView.HasAuthoredReferences;

        private float _savedTimeScale;
        private float _lessonsMessageUntil;
        private bool _ownsTimeScalePause;
        private bool _selectionStarted;
        private bool _loggedBlockedError;
        private bool _demoWasPressed;
        private bool _freeFlightWasPressed;
        private bool _lessonsWasPressed;
        private bool _demoCheckpointPending;
        private float _demoCheckpointAtUnscaledTime;
        private FlightDynamicsInitialConditions _startupInitialConditions;
        private bool _hasStartupInitialConditions;

        public void ConfigureAuthoredReferences(
            ProductDemoPilot authoredDemoPilot,
            ProductControlInput authoredProductInput,
            FlightDynamicsCoordinator authoredFlightCoordinator,
            ProductionSeatCalibrationController authoredSeatCalibration,
            GameObject authoredModePanelRoot,
            TextMesh authoredModePanelText,
            ProductFlightModeMenuView authoredModeMenuView)
        {
            demoPilot = authoredDemoPilot;
            productInput = authoredProductInput;
            flightCoordinator = authoredFlightCoordinator;
            seatCalibration = authoredSeatCalibration;
            modePanelRoot = authoredModePanelRoot;
            modePanelText = authoredModePanelText;
            modeMenuView = authoredModeMenuView;
        }

        private void Awake()
        {
            BeginSelection();
        }

        private void BeginSelection()
        {
            if (_selectionStarted)
                return;

            _selectionStarted = true;
            State = FlightModeState.Selection;
            // A serialized editor/test pause is not a requested paused flight.
            // Selection owns the startup pause and must have a running value
            // to restore on the very first mode selection.
            _savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            _ownsTimeScalePause = true;
            Time.timeScale = 0f;

            if (!HasAuthoredReferences)
            {
                EnterBlocked(
                    "Flight mode startup references are incomplete. Re-author the two production prefabs.");
                return;
            }
            if (modePanelText.gameObject != modePanelRoot &&
                !modePanelText.transform.IsChildOf(modePanelRoot.transform))
            {
                EnterBlocked(
                    "The serialized flight mode TextMesh is not owned by the serialized mode panel.");
                return;
            }
            if (demoPilot.IsRunning)
            {
                EnterBlocked(
                    "Demo was already running before startup selection. ProductDemoPilot auto-start must be disabled.");
                return;
            }

            if (flightCoordinator.initialConditionProvider == null)
            {
                EnterBlocked("The authored initial aircraft state is missing.");
                return;
            }
            // Capture before flight moves the provider's aircraft-relative spawn transform.
            // Rebuilding it after a lesson would turn that lesson's location into the new start.
            _startupInitialConditions = flightCoordinator.initialConditionProvider.Build(GeodeticReference.Kbdu);
            _hasStartupInitialConditions = true;

            productInput.ReleaseAllTouchOverrides();
            productInput.SetFlightLessonInputSuppressed(true);
            modeMenuView.TransitionFailed += HandleTransitionFailure;
            SetPanelVisible(true);
            SetPanelText(PreparingText);
        }

        private void Update()
        {
            if (State == FlightModeState.Demo)
            {
                LogDemoCheckpointWhenDue();
                return;
            }
            if (State != FlightModeState.Selection)
                return;

            if (navigation != null && navigation.IsOpen) return;

            if (seatCalibration != null && seatCalibration.IsOpen)
            {
                SetPanelVisible(false);
                modeMenuView?.ReadTouchNavigationInput(true);
                ReadSelectionButtons(out _, out _, out _);
                return;
            }

            SetPanelVisible(true);
            if (navigation == null) modeMenuView?.ReadTouchNavigationInput(false);
            if (flightCoordinator == null)
            {
                EnterBlocked("The native flight authority reference is missing.");
                return;
            }
            if (!flightCoordinator.IsInitialized)
            {
                if (!string.IsNullOrWhiteSpace(flightCoordinator.LastError) ||
                    !flightCoordinator.enabled)
                {
                    string detail = string.IsNullOrWhiteSpace(flightCoordinator.LastError)
                        ? "The native JSBSim authority was disabled before initialization."
                        : flightCoordinator.LastError;
                    EnterBlocked("Native JSBSim did not initialize: " + detail);
                }
                else
                {
                    SetPanelText(PreparingText);
                }
                ReadSelectionButtons(out _, out _, out _);
                return;
            }

            if (Time.unscaledTime >= _lessonsMessageUntil)
                SetPanelText(SelectionText);

            // A activates a labeled selection through the shared navigation owner.
            // B/Y are reserved for Back everywhere, including startup.
        }

        public void SelectDemo()=>SelectDemoCore(false);
        public void SelectShortShowcase()=>SelectDemoCore(true);
        private void SelectDemoCore(bool shortShowcase)
        {
            if (!CanSelectInitializedMode())
                return;

            productInput.ReleaseAllTouchOverrides();
            demoPilot.StartDemo();
            if (!demoPilot.IsRunning || (shortShowcase&&!demoPilot.StartShortArrival()))
            {
                EnterBlocked(
                    "Demo failed to start through ProductDemoPilot.StartDemo(). Inspect its serialized route and sources.");
                return;
            }

            State = FlightModeState.Demo;
            RestoreSavedTimeScaleOnce();
            _demoCheckpointAtUnscaledTime = Time.unscaledTime + 3f;
            _demoCheckpointPending = true;
            BeginSuccessfulTransition("DEMO FLIGHT");
        }

        private void LogDemoCheckpointWhenDue()
        {
            if (!_demoCheckpointPending ||
                Time.unscaledTime < _demoCheckpointAtUnscaledTime)
                return;

            _demoCheckpointPending = false;
            FlightDynamicsControlSnapshot controls =
                flightCoordinator.LastAppliedControls;
            FlightDynamicsState flight = flightCoordinator.CurrentState;
            Debug.Log(
                $"[QuestFlight][DemoCheckpoint] State={State} Running={demoPilot.IsRunning} " +
                $"TimeScale={Time.timeScale:F3} Initialized={flightCoordinator.IsInitialized} " +
                $"Applied=A{controls.aileron:F3}/E{controls.elevator:F3}/" +
                $"R{controls.rudder:F3}/T{controls.throttle:F3}/M{controls.mixture:F3}/" +
                $"LB{controls.leftToeBrake:F3}/RB{controls.rightToeBrake:F3} " +
                $"Flight=GS{flight.groundSpeedKnots:F2}/CAS{flight.calibratedAirspeedKnots:F2}/" +
                $"RPM{flight.engineRpm:F0}/WOW{flight.weightOnWheels} " +
                demoPilot.BuildFailureDiagnostic() + " " +
                productInput.BuildFlightLessonFailureDiagnostic(),
                this);
        }

        public bool CompleteExplicitRunwayRestart()
        {
            if ((State != FlightModeState.Demo && State != FlightModeState.FreeFlight) ||
                !flightCoordinator.IsInitialized || !flightCoordinator.CurrentState.IsFinite ||
                flightCoordinator.CurrentState.calibratedAirspeedKnots > 5) return false;
            State = FlightModeState.FreeFlight;
            _demoCheckpointPending = false;
            FlightSessionVersion++;
            return true;
        }

        public void SelectFreeFlight()
        {
            if (!CanSelectInitializedMode())
                return;

            demoPilot.StopDemo();
            productInput.ReleaseAllTouchOverrides();
            productInput.DisableFlightLessonGuidance();
            productInput.SetDeterministicMode(false);
            productInput.SetFlightLessonInputSuppressed(false);

            string failure = FreeFlightPostconditionFailure();
            if (!string.IsNullOrEmpty(failure))
            {
                EnterBlocked("Free Flight could not establish its manual-control postconditions: " + failure);
                return;
            }

            State = FlightModeState.FreeFlight;
            RestoreSavedTimeScaleOnce();
            BeginSuccessfulTransition("FREE FLIGHT");
        }

        public string WeatherLabel
        {
            get
            {
                if (flightCoordinator == null || flightCoordinator.windField == null) return "WIND UNAVAILABLE";
                Vector3 w = flightCoordinator.windField.Sample(new Vector3(-555, 10, -30), 0);
                float knots = new Vector2(w.x, w.z).magnitude * 1.943844f;
                float from = Mathf.Repeat(Mathf.Atan2(-w.x, -w.z) * Mathf.Rad2Deg, 360);
                return $"WIND {from:000}° / {knots:0} KT";
            }
        }

        // Existing serialized Weather buttons are informational; weather is no longer a physics toggle.
        public void CycleWeather() => SetPanelText(SelectionText);

        public void ShowLessonsUnavailable()
        {
            if (training != null) { SelectTraining(); return; }
            if (State != FlightModeState.Selection ||
                (seatCalibration != null && seatCalibration.IsOpen))
                return;

            SetPanelText(LessonsUnavailableText);
            _lessonsMessageUntil = Time.unscaledTime + lessonsMessageSeconds;
        }

        public void SelectTraining()
        {
            if (!CanSelectInitializedMode()) return;
            if (training == null || !training.HasAuthoredReferences)
            { EnterBlocked("The authored lesson catalog is incomplete."); return; }
            demoPilot.StopDemo();
            productInput.ReleaseAllTouchOverrides();
            productInput.DisableFlightLessonGuidance();
            productInput.SetDeterministicMode(false);
            State = FlightModeState.Training;
            RestoreSavedTimeScaleOnce();
            BeginSuccessfulTransition("FLIGHT LESSONS");
            training.OpenLessons();
        }

        private bool CanSelectInitializedMode()
        {
            if (State != FlightModeState.Selection ||
                (navigation != null && navigation.IsOpen) ||
                (seatCalibration != null && seatCalibration.IsOpen) ||
                flightCoordinator == null ||
                !flightCoordinator.IsInitialized)
                return false;
            return true;
        }

        public void SetNavigationOverlayVisible(bool visible)
        {
            if (State == FlightModeState.Selection && modeMenuView != null)
                modeMenuView.SelectionContentRoot.SetActive(!visible);
        }

        public void ReturnToMainMenu()
        {
            demoPilot.StopDemo();
            productInput.ReleaseAllTouchOverrides();
            productInput.DisableFlightLessonGuidance();
            productInput.SetDeterministicMode(false);
            productInput.SetFlightLessonInputSuppressed(true);
            productInput.PrepareForFlightLessonRecovery(new QuestFlightLab.Training.FlightLessonV1.FlightLessonControlCommand { throttle=.1f,mixture=1 });
            productInput.RequireTouchRelease();
            _demoCheckpointPending=false;
            _savedTimeScale=1; _ownsTimeScalePause=true; Time.timeScale=0;
            State=FlightModeState.Selection;
            if (!_hasStartupInitialConditions || !flightCoordinator.TryRecoverToInitialConditions(
                _startupInitialConditions))
            { EnterBlocked("Unable to restore the native initial aircraft state."); return; }
            LastError=string.Empty; _loggedBlockedError=false;
            modeMenuView.ShowMainSelection();
            SetPanelText(SelectionText);
        }

        private string FreeFlightPostconditionFailure()
        {
            if (demoPilot.IsRunning)
                return "ProductDemoPilot is still running.";
            if (productInput.DeterministicMode)
                return "deterministic input is still enabled.";
            if (productInput.FlightLessonGuidanceActive)
                return "flight-lesson guidance is still active.";
            if (productInput.FlightLessonInputSuppressed)
                return "manual product input is still suppressed.";
            if (productInput.ActiveTouchOverrideCount != 0)
                return "one or more Touch overrides are still leased.";
            if (!flightCoordinator.IsInitialized)
                return "native JSBSim is no longer initialized.";
            if (flightCoordinator.initialConditionProvider == null ||
                !flightCoordinator.initialConditionProvider.engineRunning)
                return "the serialized initial condition is not engine-running.";
            if (!flightCoordinator.LastResetInitialConditions.engineRunning)
                return "the native initial condition did not report engineRunning true.";
            return string.Empty;
        }

        private void EnterBlocked(string error)
        {
            if (State == FlightModeState.Blocked)
                return;

            State = FlightModeState.Blocked;
            if (!_ownsTimeScalePause && Time.timeScale > 0f)
            {
                _savedTimeScale = Time.timeScale;
                _ownsTimeScalePause = true;
            }
            Time.timeScale = 0f;
            LastError = string.IsNullOrWhiteSpace(error)
                ? "Flight mode startup failed. Inspect the serialized product configuration."
                : error;
            demoPilot?.StopDemo();
            if (productInput != null)
            {
                productInput.ReleaseAllTouchOverrides();
                productInput.SetFlightLessonInputSuppressed(true);
            }
            SetPanelVisible(true);
            SetPanelText("Flight could not start\nReturn to the main menu and try again. If this continues, restart QuestFlight and report the build number. Your saved progress is kept.");
            if (!_loggedBlockedError)
            {
                Debug.LogError("[QuestFlight][FlightModes] " + LastError, this);
                _loggedBlockedError = true;
            }
        }

        private void RestoreSavedTimeScaleOnce()
        {
            if (!_ownsTimeScalePause)
                return;
            Time.timeScale = _savedTimeScale;
            _ownsTimeScalePause = false;
        }

        private void OnDestroy()
        {
            if (modeMenuView != null)
                modeMenuView.TransitionFailed -= HandleTransitionFailure;
            RestoreSavedTimeScaleOnce();
        }

        private void HandleTransitionFailure(string reason)
        {
            training?.LeaveTraining();
            EnterBlocked(reason);
            navigation?.OpenMenu();
        }

        private void ReadSelectionButtons(
            out bool demoDown,
            out bool freeFlightDown,
            out bool lessonsDown)
        {
            demoDown = ReadButtonDown(
                OVRInput.Button.One,
                OVRInput.Controller.RTouch,
                ref _demoWasPressed);
            freeFlightDown = ReadButtonDown(
                OVRInput.Button.Two,
                OVRInput.Controller.RTouch,
                ref _freeFlightWasPressed);
            lessonsDown = ReadButtonDown(
                OVRInput.Button.Three,
                OVRInput.Controller.LTouch,
                ref _lessonsWasPressed);
        }

        private static bool ReadButtonDown(
            OVRInput.Button button,
            OVRInput.Controller controller,
            ref bool wasPressed)
        {
            bool pressed = OVRInput.Get(button, controller);
            bool down = pressed && !wasPressed;
            wasPressed = pressed;
            return down;
        }

        private void SetPanelVisible(bool visible)
        {
            if (modePanelRoot != null && modePanelRoot.activeSelf != visible)
                modePanelRoot.SetActive(visible);
        }

        private void SetPanelText(string value)
        {
            if (modePanelText != null && modePanelText.text != value)
                modePanelText.text = value;
            if(modeMenuView!=null)
            {
                modeMenuView.SetMessage(value);
            }
        }

        private void BeginSuccessfulTransition(string selectedMode)
        {
            FlightSessionVersion++;
            if (modeMenuView != null)
                modeMenuView.BeginModeTransition(selectedMode);
            else
                SetPanelVisible(false);
        }
    }
}
