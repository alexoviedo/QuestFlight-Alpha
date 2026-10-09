using QuestFlightLab.Aircraft.CockpitDynamics;
using UnityEngine;

namespace QuestFlight.Product
{
    /// <summary>
    /// Serialized presentation-only startup showroom. Flight-mode authority remains
    /// in ProductFlightModeCoordinator; this component never writes flight, XR,
    /// aircraft, camera, seat, or calibration transforms.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    public sealed class ProductFlightModeMenuView : MonoBehaviour
    {
        public const int CurrentLayoutVersion = 9;

        [SerializeField] private int layoutVersion = CurrentLayoutVersion;
        [SerializeField] private GameObject showroomContentRoot;
        [SerializeField] private GameObject selectionContentRoot;
        [SerializeField] private GameObject transitionContentRoot;
        [SerializeField] private TextMesh statusText;
        [SerializeField] private TextMesh transitionText;
        [SerializeField] private Renderer fadeOverlayRenderer;
        [SerializeField] private ProductionCockpitSystemsController cockpitSystems;
        [SerializeField] private ProductFlightModeMenuButton[] menuButtons;
        [SerializeField] private Renderer[] aircraftPresentationRenderers;
        [SerializeField] private Renderer[] interactionPresentationRenderers;
        [SerializeField] private Renderer[] worldPresentationRenderers;
        [SerializeField, Min(0.01f)] private float fadeToBlackSeconds = 0.12f;
        [SerializeField, Min(0.01f)] private float fadeFromBlackSeconds = 0.18f;
        [SerializeField, Range(1, 8)] private int requiredReadyFrames = 2;
        [SerializeField, Range(0.25f, 0.95f)] private float navigationActuation = 0.58f;
        [SerializeField, Range(0.05f, 0.5f)] private float navigationRelease = 0.24f;
        [SerializeField, Min(0.1f)] private float navigationRepeatDelay = 0.45f;
        [SerializeField, Min(0.05f)] private float navigationRepeatRate = 0.20f;

        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private MaterialPropertyBlock _fadeProperties;
        private TransitionPhase _transitionPhase;
        private float _transitionProgress;
        private int _readyFrames;
        private float _transitionWaitSeconds;
        public const float MaximumPreparationSeconds = 10f;
        public event System.Action<string> TransitionFailed;
        private bool[] _aircraftForceRenderingOffStates;
        private bool[] _worldForceRenderingOffStates;
        private bool _aircraftPresentationHidden;
        private bool _worldPresentationHidden;
        private bool _cockpitPreview;
        private bool _selectionPlaced;
        public bool WorldPresentationEligible => !_worldPresentationHidden;

        public void SetSelectionPresentation(bool cockpitPreview, Transform eye)
        {
            if(cockpitPreview!=_cockpitPreview)
            {
                _cockpitPreview=cockpitPreview;
                showroomContentRoot.SetActive(!cockpitPreview);
                if(cockpitPreview) { RestoreAircraftPresentation(); RestoreWorldPresentation(); }
                else { HideAircraftPresentation(); HideWorldPresentation(); }
            }
            if(!_selectionPlaced && eye!=null)
            {
                // Snapshot only on entry. UI follows neither head pitch nor head turns.
                selectionContentRoot.transform.localPosition=showroomContentRoot.transform.InverseTransformPoint(eye.position)+new Vector3(.45f,-.12f,0);
                _selectionPlaced=true;
            }
        }
        private int _focusedIndex;
        private int _navigationDirection;
        private float _nextNavigationTime;
        private bool _primaryStickClickWasPressed;
        private bool _secondaryStickClickWasPressed;
        private bool _navigationRequiresRelease;

        private enum TransitionPhase
        {
            Idle,
            WaitingForCockpit,
            FadingToBlack,
            FadingFromBlack,
            Complete
        }

        public int LayoutVersion => layoutVersion;
        public bool HasAuthoredReferences =>
            layoutVersion == CurrentLayoutVersion &&
            showroomContentRoot != null &&
            selectionContentRoot != null &&
            transitionContentRoot != null &&
            statusText != null &&
            transitionText != null &&
            fadeOverlayRenderer != null &&
            cockpitSystems != null &&
            menuButtons != null &&
            menuButtons.Length == 4 &&
            aircraftPresentationRenderers != null &&
            aircraftPresentationRenderers.Length > 0 &&
            interactionPresentationRenderers != null &&
            interactionPresentationRenderers.Length > 0;
        public bool TransitionPending =>
            _transitionPhase != TransitionPhase.Idle &&
            _transitionPhase != TransitionPhase.Complete;
        public bool CockpitPresentationReady =>
            cockpitSystems != null &&
            cockpitSystems.HasValidSnapshot &&
            cockpitSystems.HasValidSystemsV2Snapshot;
        public GameObject ShowroomContentRoot => showroomContentRoot;
        public GameObject SelectionContentRoot => selectionContentRoot;
        public GameObject TransitionContentRoot => transitionContentRoot;
        public TextMesh StatusText => statusText;
        public TextMesh TransitionText => transitionText;
        public Renderer FadeOverlayRenderer => fadeOverlayRenderer;
        public ProductionCockpitSystemsController AuthoredCockpitSystems =>
            cockpitSystems;
        public int AircraftPresentationRendererCount =>
            aircraftPresentationRenderers == null ? 0 : aircraftPresentationRenderers.Length;
        public int InteractionPresentationRendererCount =>
            interactionPresentationRenderers == null ? 0 : interactionPresentationRenderers.Length;
        public int WorldPresentationRendererCount =>
            worldPresentationRenderers == null ? 0 : worldPresentationRenderers.Length;
        public int MenuButtonCount => menuButtons == null ? 0 : menuButtons.Length;
        public int FocusedIndex => _focusedIndex;
        public ProductFlightModeMenuButton FocusedButton =>
            menuButtons != null && _focusedIndex >= 0 && _focusedIndex < menuButtons.Length
                ? menuButtons[_focusedIndex]
                : null;

        public void ConfigureAuthoredReferences(
            GameObject authoredShowroomContentRoot,
            GameObject authoredSelectionContentRoot,
            GameObject authoredTransitionContentRoot,
            TextMesh authoredStatusText,
            TextMesh authoredTransitionText,
            Renderer authoredFadeOverlayRenderer,
            ProductionCockpitSystemsController authoredCockpitSystems,
            ProductFlightModeMenuButton[] authoredMenuButtons,
            Renderer[] authoredAircraftPresentationRenderers,
            Renderer[] authoredInteractionPresentationRenderers)
        {
            layoutVersion = CurrentLayoutVersion;
            showroomContentRoot = authoredShowroomContentRoot;
            selectionContentRoot = authoredSelectionContentRoot;
            transitionContentRoot = authoredTransitionContentRoot;
            statusText = authoredStatusText;
            transitionText = authoredTransitionText;
            fadeOverlayRenderer = authoredFadeOverlayRenderer;
            cockpitSystems = authoredCockpitSystems;
            menuButtons = authoredMenuButtons;
            aircraftPresentationRenderers = authoredAircraftPresentationRenderers;
            interactionPresentationRenderers = authoredInteractionPresentationRenderers;
        }

        public void ConfigureWorldPresentationRenderers(
            Renderer[] authoredWorldPresentationRenderers)
        {
            worldPresentationRenderers = authoredWorldPresentationRenderers;
        }

        public bool ValidateWorldPresentationReferences(out string error)
        {
            if (worldPresentationRenderers == null ||
                worldPresentationRenderers.Length == 0)
            {
                error = "Startup showroom world-presentation renderer references are missing.";
                return false;
            }
            for (int index = 0; index < worldPresentationRenderers.Length; index++)
            {
                Renderer renderer = worldPresentationRenderers[index];
                if (renderer == null || renderer.transform.IsChildOf(transform))
                {
                    error = "Startup showroom world-presentation renderer references are invalid.";
                    return false;
                }
            }
            error = string.Empty;
            return true;
        }

        public bool ValidateAuthoredReferences(out string error)
        {
            if (!HasAuthoredReferences)
            {
                error = "Startup showroom references are incomplete or the layout version is stale.";
                return false;
            }
            if (!showroomContentRoot.transform.IsChildOf(transform) ||
                !selectionContentRoot.transform.IsChildOf(showroomContentRoot.transform) ||
                !transitionContentRoot.transform.IsChildOf(showroomContentRoot.transform) ||
                !fadeOverlayRenderer.transform.IsChildOf(transform))
            {
                error = "Startup showroom objects are outside their serialized presentation root.";
                return false;
            }
            for (int index = 0; index < aircraftPresentationRenderers.Length; index++)
            {
                Renderer renderer = aircraftPresentationRenderers[index];
                if (renderer == null || renderer.transform.IsChildOf(transform))
                {
                    error = "Startup showroom aircraft-presentation renderer references are invalid.";
                    return false;
                }
            }
            for (int index = 0; index < menuButtons.Length; index++)
            {
                ProductFlightModeMenuButton button = menuButtons[index];
                if (button == null || !button.transform.IsChildOf(selectionContentRoot.transform))
                {
                    error = "Startup showroom menu-button order contains an invalid reference.";
                    return false;
                }
            }
            for (int index = 0; index < interactionPresentationRenderers.Length; index++)
            {
                Renderer renderer = interactionPresentationRenderers[index];
                if (renderer == null || renderer.transform.IsChildOf(transform) ||
                    System.Array.IndexOf(aircraftPresentationRenderers, renderer) >= 0)
                {
                    error = "Startup showroom interaction visuals are invalid or still suppressed.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private void Awake()
        {
            if (!ValidateAuthoredReferences(out string error))
            {
                Debug.LogError("[QuestFlight][FlightModeMenu] " + error, this);
                enabled = false;
                return;
            }
            if (!ValidateWorldPresentationReferences(out error))
            {
                Debug.LogError("[QuestFlight][FlightModeMenu] " + error, this);
                enabled = false;
                return;
            }

            _fadeProperties = new MaterialPropertyBlock();
            SetFadeOpacity(0f);
            showroomContentRoot.SetActive(true);
            selectionContentRoot.SetActive(true);
            transitionContentRoot.SetActive(false);
            SetFocusIndex(0, true);
            HideAircraftPresentation();
        }

        private void OnDisable()
        {
            _cockpitPreview=false;
            _selectionPlaced=false;
            ResetNavigationState();
            RestoreAircraftPresentation();
            RestoreWorldPresentation();
        }

        private void Start() => HideWorldPresentation();

        private void Update()
        {
            AdvanceTransition(Time.unscaledDeltaTime);
        }

        public void AdvanceTransition(float unscaledDeltaTime)
        {
            switch (_transitionPhase)
            {
                case TransitionPhase.WaitingForCockpit:
                    _transitionWaitSeconds += unscaledDeltaTime;
                    if (CockpitPresentationReady)
                        _readyFrames++;
                    else
                        _readyFrames = 0;
                    if (_readyFrames >= requiredReadyFrames)
                    {
                        _transitionProgress = 0f;
                        _transitionPhase = TransitionPhase.FadingToBlack;
                    }
                    else if (cockpitSystems == null || !cockpitSystems.isActiveAndEnabled ||
                             _transitionWaitSeconds >= MaximumPreparationSeconds)
                    {
                        string reason = "Cockpit presentation did not become ready within the flight preparation deadline.";
                        ShowMainSelection();
                        TransitionFailed?.Invoke(reason);
                    }
                    break;

                case TransitionPhase.FadingToBlack:
                    _transitionProgress += unscaledDeltaTime /
                                           Mathf.Max(0.01f, fadeToBlackSeconds);
                    SetFadeOpacity(Mathf.Clamp01(_transitionProgress));
                    if (_transitionProgress >= 1f)
                    {
                        RestoreAircraftPresentation();
                        RestoreWorldPresentation();
                        showroomContentRoot.SetActive(false);
                        _transitionProgress = 0f;
                        _transitionPhase = TransitionPhase.FadingFromBlack;
                    }
                    break;

                case TransitionPhase.FadingFromBlack:
                    _transitionProgress += unscaledDeltaTime /
                                           Mathf.Max(0.01f, fadeFromBlackSeconds);
                    SetFadeOpacity(1f - Mathf.Clamp01(_transitionProgress));
                    if (_transitionProgress >= 1f)
                    {
                        SetFadeOpacity(0f);
                        _transitionPhase = TransitionPhase.Complete;
                        gameObject.SetActive(false);
                    }
                    break;
            }
        }

        public void SetMessage(string message)
        {
            if (statusText == null) return;
            if (message == ProductFlightModeCoordinator.PreparingText)
            {
                statusText.text = "Preparing your aircraft";
                statusText.color = new Color(0.44f, 0.82f, 1f, 1f);
            }
            else if (message != null && message.StartsWith(ProductFlightModeCoordinator.SelectionText))
            {
                statusText.text = "Ready to fly";
                statusText.color = new Color(0.68f, 0.92f, 0.82f, 1f);
            }
            else if (message == ProductFlightModeCoordinator.LessonsUnavailableText)
            {
                statusText.text = "Lessons are unavailable";
                statusText.color = new Color(1f, 0.72f, 0.26f, 1f);
            }
            else if (!string.IsNullOrWhiteSpace(message) &&
                     message.StartsWith("FLIGHT MODE STARTUP BLOCKED"))
            {
                Debug.LogError(message);
                statusText.text = "Unable to prepare the aircraft. Please restart QuestFlight.";
                statusText.color = new Color(1f, 0.34f, 0.28f, 1f);
            }
            else
            {
                statusText.text = message ?? string.Empty;
            }
            ProductUiLabel.Sync(statusText);
        }

        public void BeginModeTransition(string selectedMode)
        {
            if (_transitionPhase != TransitionPhase.Idle)
                return;

            // Calibration can have hidden this root before its final Update.
            // A successful selection must own activation of its transition.
            gameObject.SetActive(true);
            showroomContentRoot.SetActive(true);
            selectionContentRoot.SetActive(false);
            transitionContentRoot.SetActive(true);
            transitionText.text = string.IsNullOrWhiteSpace(selectedMode)
                ? "Entering the cockpit"
                : "Starting " + selectedMode;
            ProductUiLabel.Sync(transitionText);
            _readyFrames = 0;
            _transitionWaitSeconds = 0f;
            _transitionProgress = 0f;
            _transitionPhase = TransitionPhase.WaitingForCockpit;
        }

        public void ShowSelectionForVisualQa()
        { ShowMainSelection(); }

        public void ShowMainSelection()
        {
            _cockpitPreview=false;
            _selectionPlaced=false;
            gameObject.SetActive(true);
            showroomContentRoot.SetActive(true);
            selectionContentRoot.SetActive(true);
            transitionContentRoot.SetActive(false);
            _transitionPhase = TransitionPhase.Idle;
            _transitionProgress = 0f;
            _readyFrames = 0;
            _transitionWaitSeconds = 0f;
            SetFadeOpacity(0f);
            SetMessage(ProductFlightModeCoordinator.SelectionText);
            SetFocusIndex(0, true);
            HideAircraftPresentation();
            HideWorldPresentation();
        }

#if UNITY_EDITOR
        // Batch captures do not tick the fade coroutine/Update lifecycle.
        // Complete its presentation state explicitly without touching flight or XR.
        public void CompleteTransitionForVisualQa()
        {
            RestoreAircraftPresentation();
            RestoreWorldPresentation();
            SetFadeOpacity(0f);
            showroomContentRoot.SetActive(false);
            _transitionPhase = TransitionPhase.Complete;
            gameObject.SetActive(false);
        }
#endif

        public bool IsFocused(ProductFlightModeMenuButton button)
        {
            return button != null && FocusedButton == button;
        }

        public void FocusButton(ProductFlightModeMenuButton button)
        {
            if (button == null || menuButtons == null)
                return;
            int index = System.Array.IndexOf(menuButtons, button);
            if (index >= 0)
                SetFocusIndex(index, false);
        }

        public void MoveFocus(int direction)
        {
            if (menuButtons == null || menuButtons.Length == 0 || direction == 0)
                return;
            SetFocusIndex(
                Mathf.Clamp(_focusedIndex + (direction > 0 ? 1 : -1),
                    0,
                    menuButtons.Length - 1),
                false);
        }

        public void ActivateFocused()
        {
            FocusedButton?.InvokeAction();
        }

        public void ReadTouchNavigationInput(bool suppressed)
        {
            Vector2 left = OVRInput.Get(
                OVRInput.Axis2D.PrimaryThumbstick,
                OVRInput.Controller.LTouch);
            Vector2 right = OVRInput.Get(
                OVRInput.Axis2D.SecondaryThumbstick,
                OVRInput.Controller.RTouch);
            float vertical = Mathf.Abs(left.y) >= Mathf.Abs(right.y)
                ? left.y
                : right.y;
            ProcessNavigationSample(
                vertical,
                OVRInput.Get(
                    OVRInput.Button.PrimaryThumbstick,
                    OVRInput.Controller.LTouch),
                OVRInput.Get(
                    OVRInput.Button.SecondaryThumbstick,
                    OVRInput.Controller.RTouch),
                Time.unscaledTime,
                suppressed);
        }

        public void ProcessNavigationSample(
            float vertical,
            bool primaryStickClick,
            bool secondaryStickClick,
            float unscaledNow,
            bool suppressed)
        {
            if (suppressed)
            {
                _navigationDirection = 0;
                _navigationRequiresRelease = Mathf.Abs(vertical) > navigationRelease;
                _primaryStickClickWasPressed = primaryStickClick;
                _secondaryStickClickWasPressed = secondaryStickClick;
                return;
            }

            if (_navigationRequiresRelease)
            {
                if (Mathf.Abs(vertical) <= navigationRelease)
                    _navigationRequiresRelease = false;
            }
            else
            {
                int direction = vertical >= navigationActuation
                    ? -1
                    : vertical <= -navigationActuation
                        ? 1
                        : 0;
                if (direction != 0 && direction != _navigationDirection)
                {
                    MoveFocus(direction);
                    _navigationDirection = direction;
                    _nextNavigationTime = unscaledNow + navigationRepeatDelay;
                }
                else if (direction != 0 && unscaledNow >= _nextNavigationTime)
                {
                    MoveFocus(direction);
                    _nextNavigationTime = unscaledNow + navigationRepeatRate;
                }
                else if (direction == 0 && Mathf.Abs(vertical) <= navigationRelease)
                {
                    _navigationDirection = 0;
                }
            }

            bool clickDown =
                (primaryStickClick && !_primaryStickClickWasPressed) ||
                (secondaryStickClick && !_secondaryStickClickWasPressed);
            _primaryStickClickWasPressed = primaryStickClick;
            _secondaryStickClickWasPressed = secondaryStickClick;
            if (clickDown)
                ActivateFocused();
        }

        private void SetFocusIndex(int index, bool force)
        {
            if (menuButtons == null || menuButtons.Length == 0)
                return;
            index = Mathf.Clamp(index, 0, menuButtons.Length - 1);
            if (!force && index == _focusedIndex)
                return;

            for (int buttonIndex = 0; buttonIndex < menuButtons.Length; buttonIndex++)
                menuButtons[buttonIndex]?.SetFocused(buttonIndex == index);
            _focusedIndex = index;
            if (!force) ProductUiFeedback.Play(ProductUiFeedback.Cue.Focus);
        }

        private void ResetNavigationState()
        {
            _navigationDirection = 0;
            _nextNavigationTime = 0f;
            _primaryStickClickWasPressed = false;
            _secondaryStickClickWasPressed = false;
            _navigationRequiresRelease = false;
        }

        private void HideAircraftPresentation()
        {
            if (_aircraftPresentationHidden || aircraftPresentationRenderers == null)
                return;
            if (_aircraftForceRenderingOffStates == null ||
                _aircraftForceRenderingOffStates.Length != aircraftPresentationRenderers.Length)
                _aircraftForceRenderingOffStates =
                    new bool[aircraftPresentationRenderers.Length];
            for (int index = 0; index < aircraftPresentationRenderers.Length; index++)
            {
                Renderer renderer = aircraftPresentationRenderers[index];
                if (renderer == null) continue;
                _aircraftForceRenderingOffStates[index] = renderer.forceRenderingOff;
                renderer.forceRenderingOff = true;
            }
            _aircraftPresentationHidden = true;
        }

        private void HideWorldPresentation()
        {
            QuestFlight.Rendering.ProductRenderSubmission.SetWorldPresentationVisible(false);
            if (_worldPresentationHidden || worldPresentationRenderers == null)
                return;
            if (_worldForceRenderingOffStates == null ||
                _worldForceRenderingOffStates.Length !=
                    worldPresentationRenderers.Length)
                _worldForceRenderingOffStates =
                    new bool[worldPresentationRenderers.Length];
            for (int index = 0; index < worldPresentationRenderers.Length; index++)
            {
                Renderer renderer = worldPresentationRenderers[index];
                if (renderer == null) continue;
                _worldForceRenderingOffStates[index] = renderer.forceRenderingOff;
                renderer.forceRenderingOff = true;
            }
            _worldPresentationHidden = true;
        }

        private void RestoreAircraftPresentation()
        {
            if (!_aircraftPresentationHidden || aircraftPresentationRenderers == null ||
                _aircraftForceRenderingOffStates == null)
                return;
            for (int index = 0; index < aircraftPresentationRenderers.Length; index++)
            {
                Renderer renderer = aircraftPresentationRenderers[index];
                if (renderer != null)
                    renderer.forceRenderingOff =
                        _aircraftForceRenderingOffStates[index];
            }
            _aircraftPresentationHidden = false;
        }

        private void RestoreWorldPresentation()
        {
            QuestFlight.Rendering.ProductRenderSubmission.SetWorldPresentationVisible(true);
            if (!_worldPresentationHidden || worldPresentationRenderers == null ||
                _worldForceRenderingOffStates == null)
                return;
            if (_worldForceRenderingOffStates.Length ==
                worldPresentationRenderers.Length)
            {
                for (int index = 0; index < worldPresentationRenderers.Length; index++)
                {
                    Renderer renderer = worldPresentationRenderers[index];
                    if (renderer != null)
                        renderer.forceRenderingOff =
                            _worldForceRenderingOffStates[index];
                }
            }
            _worldPresentationHidden = false;
        }

        private void SetFadeOpacity(float opacity)
        {
            if (fadeOverlayRenderer == null) return;
            float clampedOpacity = Mathf.Clamp01(opacity);
            if (_fadeProperties == null)
                _fadeProperties = new MaterialPropertyBlock();
            fadeOverlayRenderer.GetPropertyBlock(_fadeProperties);
            _fadeProperties.SetFloat(OpacityId, clampedOpacity);
            fadeOverlayRenderer.SetPropertyBlock(_fadeProperties);
            // A transparent renderer surrounding the viewer is not safe to leave in
            // the Quest stereo render queue. Remove its geometry completely except
            // while the short comfort transition is actually visible.
            fadeOverlayRenderer.enabled = clampedOpacity > 0f;
        }
    }
}
