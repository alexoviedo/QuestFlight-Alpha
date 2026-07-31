using System;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics.TouchInputV2
{
    /// <summary>
    /// No-Update command consumer that arbitrates the three represented Touch
    /// controls into ProductControlInput. It never writes visual transforms;
    /// the normal flight authority and presentation path remain authoritative.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CockpitTouchInputV2Bridge :
        MonoBehaviour,
        ICockpitTouchInteractionV2CommandConsumer
    {
        public const float YokePitchHalfTravelMeters = 0.04f;
        public const float YokeRollMaximumDegrees = 55f;
        public const float EngineControlFullTravelMeters = 0.045f;

        public static readonly Vector3 AuthoredYokeAxisLocal =
            Vector3.forward;
        public static readonly Vector3 AuthoredEngineFullForwardAxisLocal =
            new Vector3(0f, 0.08715622f, 0.9961947f);

        private const string PilotYokeBindingId = "pilot_yoke_grab";
        private const string ThrottleBindingId = "throttle_grab";
        private const string MixtureBindingId = "mixture_grab";

        [Header("Serialized single flight/input authority")]
        [SerializeField] private FlightDynamicsCoordinator coordinator;

        [Header("Stable authored aircraft-local frames")]
        [SerializeField] private Transform yokeReferenceFrame;
        [SerializeField] private Transform throttleReferenceFrame;
        [SerializeField] private Transform mixtureReferenceFrame;

        [Header("Source-proven local motion axes")]
        [SerializeField] private Vector3 yokePitchAxisLocal =
            new Vector3(0f, 0f, 1f);
        [SerializeField] private Vector3 yokeRollAxisLocal =
            new Vector3(0f, 0f, 1f);
        [SerializeField] private Vector3 throttleFullForwardAxisLocal =
            new Vector3(0f, 0.08715622f, 0.9961947f);
        [SerializeField] private Vector3 mixtureFullRichAxisLocal =
            new Vector3(0f, 0.08715622f, 0.9961947f);

        private struct Capture
        {
            public bool active;
            public CockpitTouchInteractionV2Hand hand;
            public Vector3 startPointerLocalPosition;
            public Quaternion startPointerLocalRotation;
            public float baselinePrimary;
            public float baselineSecondary;
        }

        private Capture _yokeCapture;
        private Capture _throttleCapture;
        private Capture _mixtureCapture;
        private int _ownerToken;

        public FlightDynamicsCoordinator Coordinator => coordinator;
        public ProductControlInput InputAuthority =>
            coordinator != null ? coordinator.controls : null;
        public int ActiveCaptureCount =>
            (_yokeCapture.active ? 1 : 0) +
            (_throttleCapture.active ? 1 : 0) +
            (_mixtureCapture.active ? 1 : 0);

        public void ConfigureAuthoredReferences(
            FlightDynamicsCoordinator authoredCoordinator,
            Transform authoredYokeReferenceFrame,
            Transform authoredThrottleReferenceFrame,
            Transform authoredMixtureReferenceFrame,
            Vector3 authoredYokePitchAxisLocal,
            Vector3 authoredYokeRollAxisLocal,
            Vector3 authoredThrottleFullForwardAxisLocal,
            Vector3 authoredMixtureFullRichAxisLocal)
        {
            ReleaseAllCaptures();
            coordinator = authoredCoordinator;
            yokeReferenceFrame = authoredYokeReferenceFrame;
            throttleReferenceFrame =
                authoredThrottleReferenceFrame;
            mixtureReferenceFrame =
                authoredMixtureReferenceFrame;
            yokePitchAxisLocal =
                authoredYokePitchAxisLocal;
            yokeRollAxisLocal =
                authoredYokeRollAxisLocal;
            throttleFullForwardAxisLocal =
                authoredThrottleFullForwardAxisLocal;
            mixtureFullRichAxisLocal =
                authoredMixtureFullRichAxisLocal;
            EnsureOwnerToken();
        }

        public bool ValidateAuthoredReferences(out string error)
        {
            if (coordinator == null)
            {
                error =
                    "Touch V2 requires the serialized sole FlightDynamicsCoordinator.";
                return false;
            }
            if (yokeReferenceFrame == null ||
                throttleReferenceFrame == null ||
                mixtureReferenceFrame == null)
            {
                error =
                    "Touch V2 requires stable serialized yoke, throttle, and mixture reference frames.";
                return false;
            }
            if (!CockpitTouchInputV2Kinematics.TryNormalizeAxis(
                    yokePitchAxisLocal,
                    out _) ||
                !CockpitTouchInputV2Kinematics.TryNormalizeAxis(
                    yokeRollAxisLocal,
                    out _) ||
                !CockpitTouchInputV2Kinematics.TryNormalizeAxis(
                    throttleFullForwardAxisLocal,
                    out _) ||
                !CockpitTouchInputV2Kinematics.TryNormalizeAxis(
                    mixtureFullRichAxisLocal,
                    out _))
            {
                error =
                    "Touch V2 contains an invalid authored control axis.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void Awake()
        {
            EnsureOwnerToken();
            if (ValidateAuthoredReferences(out string error) &&
                InputAuthority != null &&
                InputAuthority.HasAuthoredSources)
                return;
            if (string.IsNullOrEmpty(error))
            {
                error =
                    "The product-level coordinator override does not reference the authored ProductControlInput.";
            }
            Debug.LogError(
                "[QuestFlight][CockpitTouchInputV2] " + error,
                this);
            enabled = false;
        }

        private void OnDisable()
        {
            ReleaseAllCaptures();
        }

        public bool TryConsumeCockpitTouchCommand(
            in CockpitTouchInteractionV2Command command)
        {
            ProductControlInput inputAuthority = InputAuthority;
            if (inputAuthority == null ||
                !ValidateFixedBinding(command) ||
                !CockpitTouchInputV2Kinematics.IsFinite(
                    command.pointerWorldPosition) ||
                !CockpitTouchInputV2Kinematics.IsFinite(
                    command.pointerWorldRotation))
                return false;

            switch (command.operation)
            {
                case CockpitTouchInteractionV2Operation.BeginGrab:
                    return BeginCapture(command);
                case CockpitTouchInteractionV2Operation.UpdateGrab:
                    return UpdateCapture(command);
                case CockpitTouchInteractionV2Operation.EndGrab:
                    return EndCapture(command);
                default:
                    return false;
            }
        }

        private bool BeginCapture(
            in CockpitTouchInteractionV2Command command)
        {
            ProductControlInput inputAuthority = InputAuthority;
            if (inputAuthority == null)
                return false;
            if (!inputAuthority.CanAcceptTouchOverrides ||
                IsHandCaptured(command.hand))
                return false;

            ref Capture capture =
                ref CaptureFor(command.target);
            if (capture.active)
                return false;
            Transform frame = FrameFor(command.target);
            if (!CockpitTouchInputV2Kinematics
                    .TryPointerPoseInFrame(
                        frame,
                        command.pointerWorldPosition,
                        command.pointerWorldRotation,
                        out Vector3 localPosition,
                        out Quaternion localRotation))
                return false;

            float primary;
            float secondary;
            ProductTouchControlChannel channel =
                ProductChannel(command.target);
            switch (channel)
            {
                case ProductTouchControlChannel.PilotYoke:
                    primary = inputAuthority.Current.aileron;
                    secondary = inputAuthority.Current.elevator;
                    break;
                case ProductTouchControlChannel.Throttle:
                    primary = inputAuthority.Current.throttle;
                    secondary = 0f;
                    break;
                case ProductTouchControlChannel.Mixture:
                    primary = inputAuthority.Current.mixture;
                    secondary = 0f;
                    break;
                default:
                    return false;
            }

            EnsureOwnerToken();
            if (!inputAuthority.TryBeginTouchOverride(
                    channel,
                    _ownerToken,
                    primary,
                    secondary))
                return false;

            capture.active = true;
            capture.hand = command.hand;
            capture.startPointerLocalPosition =
                localPosition;
            capture.startPointerLocalRotation =
                localRotation;
            capture.baselinePrimary = primary;
            capture.baselineSecondary = secondary;
            return true;
        }

        private bool UpdateCapture(
            in CockpitTouchInteractionV2Command command)
        {
            ProductControlInput inputAuthority = InputAuthority;
            if (inputAuthority == null)
                return false;
            ref Capture capture =
                ref CaptureFor(command.target);
            if (!capture.active ||
                capture.hand != command.hand ||
                !inputAuthority.CanAcceptTouchOverrides)
                return false;

            ProductTouchControlChannel channel =
                ProductChannel(command.target);
            switch (channel)
            {
                case ProductTouchControlChannel.PilotYoke:
                    if (!CockpitTouchInputV2Kinematics.TryEvaluateYoke(
                            yokeReferenceFrame,
                            yokePitchAxisLocal,
                            yokeRollAxisLocal,
                            YokePitchHalfTravelMeters,
                            YokeRollMaximumDegrees,
                            capture.startPointerLocalPosition,
                            capture.startPointerLocalRotation,
                            command.pointerWorldPosition,
                            command.pointerWorldRotation,
                            capture.baselinePrimary,
                            capture.baselineSecondary,
                            out float aileron,
                            out float elevator))
                        return false;
                    return inputAuthority.TryUpdateTouchOverride(
                        channel,
                        _ownerToken,
                        aileron,
                        elevator);

                case ProductTouchControlChannel.Throttle:
                    if (!CockpitTouchInputV2Kinematics
                            .TryEvaluateLinearControl(
                                throttleReferenceFrame,
                                throttleFullForwardAxisLocal,
                                EngineControlFullTravelMeters,
                                capture.startPointerLocalPosition,
                                command.pointerWorldPosition,
                                capture.baselinePrimary,
                                out float throttle))
                        return false;
                    return inputAuthority.TryUpdateTouchOverride(
                        channel,
                        _ownerToken,
                        throttle);

                case ProductTouchControlChannel.Mixture:
                    if (!CockpitTouchInputV2Kinematics
                            .TryEvaluateLinearControl(
                                mixtureReferenceFrame,
                                mixtureFullRichAxisLocal,
                                EngineControlFullTravelMeters,
                                capture.startPointerLocalPosition,
                                command.pointerWorldPosition,
                                capture.baselinePrimary,
                                out float mixture))
                        return false;
                    return inputAuthority.TryUpdateTouchOverride(
                        channel,
                        _ownerToken,
                        mixture);

                default:
                    return false;
            }
        }

        private bool EndCapture(
            in CockpitTouchInteractionV2Command command)
        {
            ProductControlInput inputAuthority = InputAuthority;
            if (inputAuthority == null)
                return false;
            ref Capture capture =
                ref CaptureFor(command.target);
            if (!capture.active ||
                capture.hand != command.hand)
                return false;

            if (!inputAuthority.TryEndTouchOverride(
                    ProductChannel(command.target),
                    _ownerToken))
                return false;
            capture = default;
            return true;
        }

        private bool ValidateFixedBinding(
            in CockpitTouchInteractionV2Command command)
        {
            switch (command.target)
            {
                case CockpitTouchInteractionV2Target.PilotYoke:
                    return command.hand ==
                           CockpitTouchInteractionV2Hand.Left &&
                           string.Equals(
                               command.bindingId,
                               PilotYokeBindingId,
                               StringComparison.Ordinal);
                case CockpitTouchInteractionV2Target.Throttle:
                    return command.hand ==
                           CockpitTouchInteractionV2Hand.Right &&
                           string.Equals(
                               command.bindingId,
                               ThrottleBindingId,
                               StringComparison.Ordinal);
                case CockpitTouchInteractionV2Target.Mixture:
                    return command.hand ==
                           CockpitTouchInteractionV2Hand.Right &&
                           string.Equals(
                               command.bindingId,
                               MixtureBindingId,
                               StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        private bool IsHandCaptured(
            CockpitTouchInteractionV2Hand hand)
        {
            return (_yokeCapture.active &&
                    _yokeCapture.hand == hand) ||
                   (_throttleCapture.active &&
                    _throttleCapture.hand == hand) ||
                   (_mixtureCapture.active &&
                    _mixtureCapture.hand == hand);
        }

        private void ReleaseAllCaptures()
        {
            ProductControlInput inputAuthority = InputAuthority;
            if (inputAuthority != null)
            {
                EnsureOwnerToken();
                inputAuthority.ReleaseTouchOverridesOwnedBy(
                    _ownerToken);
            }
            _yokeCapture = default;
            _throttleCapture = default;
            _mixtureCapture = default;
        }

        private void EnsureOwnerToken()
        {
            if (_ownerToken == 0)
                _ownerToken = GetInstanceID();
        }

        private ref Capture CaptureFor(
            CockpitTouchInteractionV2Target target)
        {
            switch (target)
            {
                case CockpitTouchInteractionV2Target.PilotYoke:
                    return ref _yokeCapture;
                case CockpitTouchInteractionV2Target.Throttle:
                    return ref _throttleCapture;
                case CockpitTouchInteractionV2Target.Mixture:
                    return ref _mixtureCapture;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(target));
            }
        }

        private Transform FrameFor(
            CockpitTouchInteractionV2Target target)
        {
            switch (target)
            {
                case CockpitTouchInteractionV2Target.PilotYoke:
                    return yokeReferenceFrame;
                case CockpitTouchInteractionV2Target.Throttle:
                    return throttleReferenceFrame;
                case CockpitTouchInteractionV2Target.Mixture:
                    return mixtureReferenceFrame;
                default:
                    return null;
            }
        }

        private static ProductTouchControlChannel ProductChannel(
            CockpitTouchInteractionV2Target target)
        {
            switch (target)
            {
                case CockpitTouchInteractionV2Target.PilotYoke:
                    return ProductTouchControlChannel.PilotYoke;
                case CockpitTouchInteractionV2Target.Throttle:
                    return ProductTouchControlChannel.Throttle;
                case CockpitTouchInteractionV2Target.Mixture:
                    return ProductTouchControlChannel.Mixture;
                default:
                    return ProductTouchControlChannel.None;
            }
        }
    }
}
