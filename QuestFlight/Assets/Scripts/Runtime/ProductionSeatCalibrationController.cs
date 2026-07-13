using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

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
        private static readonly List<InputDevice> DeviceScratch = new List<InputDevice>();

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
                Status = "loaded " + path;
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
            bool menu = ReadButtonDown(XRNode.LeftHand, CommonUsages.menuButton, ref _menuWasPressed);
            if (menu)
            {
                if (IsOpen) Cancel();
                else Open();
            }

            if (!IsOpen) return;

            bool leftPrimary = ReadButtonDown(XRNode.LeftHand, CommonUsages.primaryButton, ref _leftPrimaryWasPressed);
            bool leftSecondary = ReadButtonDown(XRNode.LeftHand, CommonUsages.secondaryButton, ref _leftSecondaryWasPressed);
            bool rightPrimary = ReadButtonDown(XRNode.RightHand, CommonUsages.primaryButton, ref _rightPrimaryWasPressed);
            bool rightSecondary = ReadButtonDown(XRNode.RightHand, CommonUsages.secondaryButton, ref _rightSecondaryWasPressed);
            bool incrementPressed = ReadEitherButtonDown(CommonUsages.gripButton, ref _incrementWasPressed);

            if (incrementPressed) ToggleIncrementSize();
            if (leftPrimary) Recenter();
            if (leftSecondary) ResetToDefault();
            if (rightPrimary) Save();
            else if (rightSecondary) Cancel();
            if (!IsOpen) return;

            Vector2 left = ApplyDeadzone(ReadAxis(XRNode.LeftHand));
            Vector2 right = ApplyDeadzone(ReadAxis(XRNode.RightHand));
            float step = CurrentStepMeters * repeatStepsPerSecond * Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            Vector3 delta = new Vector3(left.x, left.y, right.y) * step;
            if (delta.sqrMagnitude > 0.00000001f)
            {
                ApplyOffset(CalibrationOffset + delta, CalibrationYawDegrees, "adjusting");
            }
        }

        public float CurrentStepMeters => UsesSmallIncrement ? smallStepMeters : largeStepMeters;

        public void Open()
        {
            if (IsOpen) return;
            _draftStartOffset = CalibrationOffset;
            _draftStartYaw = CalibrationYawDegrees;
            if (referenceFrameRig.XrOrigin != null)
            {
                _draftStartOriginPosition = referenceFrameRig.XrOrigin.localPosition;
                _draftStartOriginRotation = referenceFrameRig.XrOrigin.localRotation;
            }
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
            bool recentered = referenceFrameRig.RecenterTrackingSpaceToSeat();
            Status = recentered ? "tracking space recentered" : "recenter unavailable";
            RefreshPanel();
        }

        public void ResetToDefault()
        {
            ApplyOffset(Vector3.zero, 0f, "corrected aircraft default restored");
            referenceFrameRig.RecenterTrackingSpaceToSeat();
        }

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
                    instructions = "Touch UI: sticks move the view; grip toggles small/large; X recenter; Y reset; A save; B cancel."
                };
                string path = CockpitViewpointPersistence.SaveCurrent(state);
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
            if (referenceFrameRig.XrOrigin != null)
            {
                referenceFrameRig.XrOrigin.SetLocalPositionAndRotation(
                    _draftStartOriginPosition,
                    _draftStartOriginRotation);
            }
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
            if (panelRoot != null) panelRoot.SetActive(visible);
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
                "X: recenter   Y: reset default\n" +
                "A: save       B/Menu: cancel\n" +
                Status;
        }

        private Vector2 ApplyDeadzone(Vector2 value) => value.magnitude >= stickDeadzone ? value : Vector2.zero;

        private static Vector2 ReadAxis(XRNode node)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 value)
                ? value
                : Vector2.zero;
        }

        private static bool ReadButtonDown(XRNode node, InputFeatureUsage<bool> usage, ref bool wasPressed)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            bool pressed = device.isValid && device.TryGetFeatureValue(usage, out bool value) && value;
            bool down = pressed && !wasPressed;
            wasPressed = pressed;
            return down;
        }

        private static bool ReadEitherButtonDown(InputFeatureUsage<bool> usage, ref bool wasPressed)
        {
            DeviceScratch.Clear();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, DeviceScratch);
            bool pressed = false;
            for (int i = 0; i < DeviceScratch.Count; i++)
            {
                pressed |= DeviceScratch[i].TryGetFeatureValue(usage, out bool value) && value;
            }
            bool down = pressed && !wasPressed;
            wasPressed = pressed;
            return down;
        }
    }
}
