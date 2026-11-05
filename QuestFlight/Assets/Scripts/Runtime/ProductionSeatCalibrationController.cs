using System;

using UnityEngine;



namespace QuestFlightLab.Runtime

{

    /// <summary>

    /// Touch-controller-only seat calibration for the authored production rig.

    /// It writes UserViewCalibrationOffset and, for explicit recentering only,

    /// XR Origin. It never writes the tracked camera transform.

    /// </summary>

    [DisallowMultipleComponent]

    public sealed class ProductionSeatCalibrationController : MonoBehaviour

    {

        [SerializeField] private AircraftReferenceFrameRig referenceFrameRig;

        [SerializeField] private PilotSeatProfile seatProfile;

        [SerializeField] private GameObject panelRoot;

        [SerializeField] private TextMesh panelText;

        [SerializeField, Min(0.001f)] private float smallStepMeters = 0.005f;

        [SerializeField, Min(0.001f)] private float largeStepMeters = 0.02f;

        [SerializeField, Min(1f)] private float repeatStepsPerSecond = 8f;

        [SerializeField, Range(0f, 0.95f)] private float stickDeadzone = 0.35f;

        [SerializeField] private bool appNavigationOwnsButtons;

        public void ConfigureAppNavigation(bool enabled) => appNavigationOwnsButtons = enabled;



        public bool IsOpen { get; private set; }

        public bool UsesSmallIncrement { get; private set; } = true;

        public Vector3 CalibrationOffset { get; private set; }

        public float CalibrationYawDegrees { get; private set; }

        public string Status { get; private set; } = "ready";

        public AircraftReferenceFrameRig AuthoredReferenceFrameRig => referenceFrameRig;

        public PilotSeatProfile AuthoredSeatProfile => seatProfile;

        public GameObject AuthoredPanelRoot => panelRoot;

        public TextMesh AuthoredPanelText => panelText;

        public bool HasAuthoredReferences =>

            referenceFrameRig != null && seatProfile != null && panelRoot != null && panelText != null;



        private Vector3 _draftStartOffset;

        private float _draftStartYaw;

        private Vector3 _draftStartOriginPosition;

        private Quaternion _draftStartOriginRotation = Quaternion.identity;

        private bool _menuWasPressed;

        private bool _leftPrimaryWasPressed;

        private bool _leftSecondaryWasPressed;

        private bool _rightPrimaryWasPressed;

        private bool _rightSecondaryWasPressed;

        private bool _incrementWasPressed;



        public void ConfigureAuthoredReferences(

            AircraftReferenceFrameRig rig,

            PilotSeatProfile profile,

            GameObject authoredPanelRoot,

            TextMesh authoredPanelText)

        {

            referenceFrameRig = rig;

            seatProfile = profile;

            panelRoot = authoredPanelRoot;

            panelText = authoredPanelText;

        }



        private void Awake()

        {

            if (!HasAuthoredReferences)

            {

                Debug.LogError("[QuestFlight][ProductionSeat] Serialized rig, profile, panel root, and panel text are required.", this);

                enabled = false;

                return;

            }



            CalibrationOffset = Vector3.zero;

            CalibrationYawDegrees = 0f;

            if (CockpitViewpointPersistence.TryLoadCurrent(

                    out CockpitViewpointCalibrationState saved,

                    out string path,

                    out string error,

                    aircraftId: seatProfile.aircraftId))

            {

                CalibrationOffset = seatProfile.ClampCalibrationOffset(saved.calibrationOffset);

                CalibrationYawDegrees = seatProfile.ClampCalibrationYaw(saved.calibrationYawDeg);

                Status = string.IsNullOrWhiteSpace(error) ? "loaded " + path : error;

            }

            else if (!string.IsNullOrWhiteSpace(error))

            {

                Status = "saved calibration ignored: " + error;

            }



            referenceFrameRig.ApplyCalibration(CalibrationOffset, CalibrationYawDegrees);

            SetPanelVisible(false);

            RefreshPanel();

        }



        private void Update()

        {

            bool menu = ReadButtonDown(

                OVRInput.Button.Start,

                OVRInput.Controller.LTouch,

                ref _menuWasPressed);

            if (menu && !appNavigationOwnsButtons)

            {

                if (IsOpen) Cancel();

                else Open();

            }



            if (!IsOpen) return;



            bool leftPrimary = ReadButtonDown(

                OVRInput.Button.Three,

                OVRInput.Controller.LTouch,

                ref _leftPrimaryWasPressed);

            bool leftSecondary = ReadButtonDown(

                OVRInput.Button.Four,

                OVRInput.Controller.LTouch,

                ref _leftSecondaryWasPressed);

            bool rightPrimary = ReadButtonDown(

                OVRInput.Button.One,

                OVRInput.Controller.RTouch,

                ref _rightPrimaryWasPressed);

            bool rightSecondary = ReadButtonDown(

                OVRInput.Button.Two,

                OVRInput.Controller.RTouch,

                ref _rightSecondaryWasPressed);

            bool incrementPressed = ReadEitherGripDown(

                ref _incrementWasPressed);



            if (incrementPressed && !appNavigationOwnsButtons) ToggleIncrementSize();

            if (!appNavigationOwnsButtons)

            {

                if (leftPrimary) Recenter();

                if (leftSecondary || rightSecondary) Cancel();

                if (rightPrimary) Save();

            }

            if (!IsOpen) return;

            if (appNavigationOwnsButtons) SetPanelVisible(false);



            if (appNavigationOwnsButtons && !IsAdjustingSeat) return;



            Vector2 left = ApplyDeadzone(OVRInput.Get(

                OVRInput.Axis2D.PrimaryThumbstick,

                OVRInput.Controller.LTouch));

            Vector2 right = ApplyDeadzone(OVRInput.Get(

                OVRInput.Axis2D.SecondaryThumbstick,

                OVRInput.Controller.RTouch));

            float step = CurrentStepMeters * repeatStepsPerSecond * Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            Vector3 delta = new Vector3(left.x, left.y, right.y) * step;

            if (delta.sqrMagnitude > 0.00000001f)

            {

                ApplyOffset(CalibrationOffset + delta, CalibrationYawDegrees, "adjusting");

            }

        }



        public bool DraftEditingEnabled {get;set;}=true;
        public bool IsAdjustingSeat => DraftEditingEnabled && IsOpen && (OVRInput.Get(OVRInput.RawAxis1D.LHandTrigger)>.5f || OVRInput.Get(OVRInput.RawAxis1D.RHandTrigger)>.5f);



        public float CurrentStepMeters => UsesSmallIncrement ? smallStepMeters : largeStepMeters;



        public void Open()

        {

            if (IsOpen) return;

            _draftStartOffset = CalibrationOffset;

            _draftStartYaw = CalibrationYawDegrees;

            IsOpen = true;

            Status = "calibration open";

            SetPanelVisible(true);

            RefreshPanel();

        }



        public void AdjustUp() => Adjust(Vector3.up);

        public void AdjustDown() => Adjust(Vector3.down);

        public void AdjustLeft() => Adjust(Vector3.left);

        public void AdjustRight() => Adjust(Vector3.right);

        public void AdjustForward() => Adjust(Vector3.forward);

        public void AdjustAft() => Adjust(Vector3.back);

        public void UseSmallIncrement() { UsesSmallIncrement = true; Status = "small increment"; RefreshPanel(); }

        public void UseLargeIncrement() { UsesSmallIncrement = false; Status = "large increment"; RefreshPanel(); }

        public void ToggleIncrementSize() { if (UsesSmallIncrement) UseLargeIncrement(); else UseSmallIncrement(); }



        public void Recenter()

        {

            var systems=new System.Collections.Generic.List<UnityEngine.XR.XRInputSubsystem>();

            SubsystemManager.GetSubsystems(systems);

            bool recentered=false;foreach(var system in systems)if(system.running)recentered|=system.TryRecenter();

            Status=recentered?"Tracking recentered by XR runtime":"Use the Quest system recenter if tracking recenter is unavailable.";

            RefreshPanel();

        }



        public void ResetToDefault()

        {

            ApplyOffset(Vector3.zero, 0f, "corrected aircraft default restored");



        }



        #if UNITY_EDITOR
        public string EditorVerificationSaveRoot {get;set;}
#endif
        public void Save()

        {

            try

            {

                CockpitViewpointCalibrationState state = new CockpitViewpointCalibrationState

                {

                    schemaVersion = CockpitViewpointPersistence.SchemaVersion,

                    aircraftId = seatProfile.aircraftId,

                    generatedUtc = DateTime.UtcNow.ToString("O"),

                    importedC172SeatReferenceLocal = seatProfile.seatAnchorLocalPosition,

                    importedC172DefaultPilotViewOffset = seatProfile.nominalEyeLocalPosition - seatProfile.seatAnchorLocalPosition,

                    calibrationOffset = CalibrationOffset,

                    calibrationYawDeg = CalibrationYawDegrees,

                    importedC172PilotViewOffset = CalibrationOffset,

                    importedC172CockpitYawDeg = CalibrationYawDegrees,

                    pilotEyeLocal = seatProfile.nominalEyeLocalPosition + CalibrationOffset,

                    instructions = "Menu: app menu. Seat calibration: sticks adjust, hold grip to adjust; X toggles increments; use labeled Save, Recenter or Reset. B/Y cancel."

                };

                string saveRoot=null;
#if UNITY_EDITOR
                saveRoot=EditorVerificationSaveRoot;
#endif
                string path = CockpitViewpointPersistence.SaveCurrent(state,saveRoot);

                Status = "saved " + path;

                IsOpen = false;

                SetPanelVisible(false);

            }

            catch (Exception ex)

            {

                Status = "save failed: " + ex.Message;

                RefreshPanel();

            }

        }



        public void Cancel()

        {

            ApplyOffset(_draftStartOffset, _draftStartYaw, "cancelled");

            IsOpen = false;

            SetPanelVisible(false);

        }



        private void Adjust(Vector3 direction)

        {

            if (!IsOpen) Open();

            ApplyOffset(CalibrationOffset + direction * CurrentStepMeters, CalibrationYawDegrees, "adjusting");

        }



        private void ApplyOffset(Vector3 offset, float yaw, string status)

        {

            CalibrationOffset = seatProfile.ClampCalibrationOffset(offset);

            CalibrationYawDegrees = seatProfile.ClampCalibrationYaw(yaw);

            referenceFrameRig.ApplyCalibration(CalibrationOffset, CalibrationYawDegrees);

            Status = status;

            RefreshPanel();

        }



        private void SetPanelVisible(bool visible)

        {

            if (panelRoot != null) panelRoot.SetActive(visible && !appNavigationOwnsButtons);

        }



        private void RefreshPanel()

        {

            if (panelText == null) return;

            panelText.text =

                "SEAT / VIEW  (Touch only)\n" +

                $"Offset  L/R {CalibrationOffset.x:+0.000;-0.000;0.000}  U/D {CalibrationOffset.y:+0.000;-0.000;0.000}  F/A {CalibrationOffset.z:+0.000;-0.000;0.000} m\n" +

                $"Increment  {(UsesSmallIncrement ? "SMALL" : "LARGE")}  {CurrentStepMeters * 100f:0.0} cm   Grip: toggle\n" +

                "Left stick: left/right + up/down\n" +

                "Right stick: forward/aft\n" +

                "Use the labeled Save, Recenter and Reset buttons.\n" +

                "B/Y: cancel    Menu: app menu\n" +

                Status;

        }



        private Vector2 ApplyDeadzone(Vector2 value) => value.magnitude >= stickDeadzone ? value : Vector2.zero;



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



        private static bool ReadEitherGripDown(ref bool wasPressed)

        {

            bool pressed =

                OVRInput.Get(

                    OVRInput.Axis1D.PrimaryHandTrigger,

                    OVRInput.Controller.LTouch) > 0.55f ||

                OVRInput.Get(

                    OVRInput.Axis1D.SecondaryHandTrigger,

                    OVRInput.Controller.RTouch) > 0.55f;

            bool down = pressed && !wasPressed;

            wasPressed = pressed;

            return down;

        }

    }

}
