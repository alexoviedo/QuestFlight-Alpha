using System;
using Oculus.Interaction;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlight.Product
{
    /// <summary>Serialized Meta pointable adapter for one startup-menu action.</summary>
    [DisallowMultipleComponent]
    public sealed class ProductFlightModeMenuButton : MonoBehaviour
    {
        public enum MenuAction
        {
            Demo,
            FreeFlight,
            Lessons,
            SeatCalibration
        }

        [SerializeField] private MenuAction action;
        [SerializeField] private ProductFlightModeCoordinator coordinator;
        [SerializeField] private ProductFlightModeMenuView menuView;
        [SerializeField] private ProductionSeatCalibrationController seatCalibration;
        [SerializeField] private OvrPointerSourceRegistry sourceRegistry;
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private AudioSource feedbackAudio;
        [SerializeField] private Color normalColor = new Color(0.055f, 0.08f, 0.12f, 0.96f);
        [SerializeField] private Color focusedColor = new Color(0.065f, 0.15f, 0.22f, 1f);
        [SerializeField] private Color hoverColor = new Color(0.08f, 0.22f, 0.31f, 1f);
        [SerializeField] private Color pressedColor = new Color(0.95f, 0.52f, 0.12f, 1f);
        [SerializeField] private MonoBehaviour[] pointables = Array.Empty<MonoBehaviour>();
        [SerializeField, Range(0.05f, 0.35f)] private float hoverExitGraceSeconds = 0.15f;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock _properties;
        private int _hoverCount;
        private float _hoverReleaseTime;
        private bool _hoverVisualActive;
        private bool _isFocused;
        private bool _isPressed;
        private float _hapticStopTime;
        private OVRInput.Controller _hapticController;

        public MenuAction Action => action;
        public ProductFlightModeCoordinator AuthoredCoordinator => coordinator;
        public ProductFlightModeMenuView AuthoredMenuView => menuView;
        public ProductionSeatCalibrationController AuthoredSeatCalibration =>
            seatCalibration;
        public Renderer TargetRenderer => targetRenderer;
        public int PointableCount => pointables?.Length ?? 0;
        public bool IsFocused => _isFocused;
        public bool IsPointerHoverVisible => _hoverVisualActive;
        public bool HasAuthoredReferences =>
            coordinator != null &&
            menuView != null &&
            seatCalibration != null &&
            sourceRegistry != null &&
            targetRenderer != null &&
            feedbackAudio != null &&
            pointables != null &&
            pointables.Length == 2;

        public void ConfigureLocalReferences(
            MenuAction authoredAction,
            ProductFlightModeMenuView authoredMenuView,
            ProductionSeatCalibrationController authoredSeatCalibration,
            OvrPointerSourceRegistry authoredSourceRegistry,
            Renderer authoredTargetRenderer,
            AudioSource authoredFeedbackAudio,
            Color authoredNormalColor,
            Color authoredFocusedColor,
            Color authoredHoverColor,
            Color authoredPressedColor,
            MonoBehaviour[] authoredPointables)
        {
            action = authoredAction;
            menuView = authoredMenuView;
            seatCalibration = authoredSeatCalibration;
            sourceRegistry = authoredSourceRegistry;
            targetRenderer = authoredTargetRenderer;
            feedbackAudio = authoredFeedbackAudio;
            normalColor = authoredNormalColor;
            focusedColor = authoredFocusedColor;
            hoverColor = authoredHoverColor;
            pressedColor = authoredPressedColor;
            pointables = authoredPointables ?? Array.Empty<MonoBehaviour>();
        }

        public void BindCoordinator(ProductFlightModeCoordinator authoredCoordinator)
        {
            coordinator = authoredCoordinator;
        }

        public bool ValidateAuthoredReferences(out string error)
        {
            if (!HasAuthoredReferences)
            {
                error = $"Startup menu button '{name}' has incomplete serialized references.";
                return false;
            }
            for (int i = 0; i < pointables.Length; i++)
            {
                if (!(pointables[i] is IPointable))
                {
                    error = $"Startup menu button '{name}' pointable {i} is invalid.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private void Awake()
        {
            _properties = new MaterialPropertyBlock();
            RefreshVisual();
            if (!ValidateAuthoredReferences(out string error))
            {
                Debug.LogError("[QuestFlight][FlightModeMenu] " + error, this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            Subscribe(true);
            _isFocused = menuView != null && menuView.IsFocused(this);
            _isPressed = false;
            RefreshVisual();
        }

        private void OnDisable()
        {
            Subscribe(false);
            StopHaptics();
            _hoverCount = 0;
            _hoverVisualActive = false;
            _isPressed = false;
        }

        private void Update()
        {
            if (_hapticController != OVRInput.Controller.None &&
                Time.unscaledTime >= _hapticStopTime)
                StopHaptics();
            if (_hoverVisualActive && _hoverCount == 0 &&
                Time.unscaledTime >= _hoverReleaseTime)
            {
                _hoverVisualActive = false;
                RefreshVisual();
            }
        }

        private void Subscribe(bool subscribe)
        {
            if (pointables == null) return;
            for (int i = 0; i < pointables.Length; i++)
            {
                if (!(pointables[i] is IPointable pointable)) continue;
                if (subscribe)
                    pointable.WhenPointerEventRaised += HandlePointerEvent;
                else
                    pointable.WhenPointerEventRaised -= HandlePointerEvent;
            }
        }

        private void HandlePointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Hover:
                    _hoverCount++;
                    _hoverVisualActive = true;
                    menuView?.FocusButton(this);
                    RefreshVisual();
                    break;
                case PointerEventType.Unhover:
                    _hoverCount = Mathf.Max(0, _hoverCount - 1);
                    if (_hoverCount == 0)
                        _hoverReleaseTime = Time.unscaledTime + hoverExitGraceSeconds;
                    break;
                case PointerEventType.Select:
                    _isPressed = true;
                    RefreshVisual();
                    PlayFeedback(evt.Identifier);
                    InvokeAction();
                    break;
                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    _isPressed = false;
                    RefreshVisual();
                    break;
            }
        }

        public void SetFocused(bool focused)
        {
            if (_isFocused == focused)
                return;
            _isFocused = focused;
            RefreshVisual();
        }

        public void InvokeAction()
        {
            if (coordinator == null ||
                coordinator.State !=
                    ProductFlightModeCoordinator.FlightModeState.Selection)
                return;

            switch (action)
            {
                case MenuAction.Demo:
                    coordinator.SelectDemo();
                    break;
                case MenuAction.FreeFlight:
                    coordinator.SelectFreeFlight();
                    break;
                case MenuAction.Lessons:
                    coordinator.ShowLessonsUnavailable();
                    break;
                case MenuAction.SeatCalibration:
                    seatCalibration.Open();
                    break;
            }
        }

        private void PlayFeedback(int pointerIdentifier)
        {
            if (feedbackAudio != null && feedbackAudio.clip != null)
                feedbackAudio.PlayOneShot(feedbackAudio.clip, 0.42f);

            if (sourceRegistry == null ||
                !sourceRegistry.TryResolve(
                    pointerIdentifier,
                    out CockpitTouchInteractionV2Hand hand,
                    out CockpitInputSourceKind kind) ||
                kind != CockpitInputSourceKind.OvrTouchController)
                return;

            _hapticController = hand == CockpitTouchInteractionV2Hand.Left
                ? OVRInput.Controller.LTouch
                : OVRInput.Controller.RTouch;
            OVRInput.SetControllerVibration(0.32f, 0.18f, _hapticController);
            _hapticStopTime = Time.unscaledTime + 0.045f;
        }

        private void StopHaptics()
        {
            if (_hapticController == OVRInput.Controller.None) return;
            OVRInput.SetControllerVibration(0f, 0f, _hapticController);
            _hapticController = OVRInput.Controller.None;
        }

        private void ApplyColor(Color color)
        {
            if (targetRenderer == null) return;
            if (_properties == null)
                _properties = new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(_properties);
            _properties.SetColor(ColorId, color);
            targetRenderer.SetPropertyBlock(_properties);
        }

        private void RefreshVisual()
        {
            ApplyColor(_isPressed
                ? pressedColor
                : _hoverVisualActive
                    ? hoverColor
                    : _isFocused
                        ? focusedColor
                        : normalColor);
        }
    }
}
