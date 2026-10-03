using System;
using QuestFlightLab.Aircraft.CockpitDynamics.SystemsV2;
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

        public const string PilotYokeBindingId = "pilot_yoke_grab";
        public const string ThrottleBindingId = "throttle_grab";
        public const string MixtureBindingId = "mixture_grab";
        public const string AltBatMasterBindingId =
            "alt_bat_master_toggle";
        public const string PrimerBindingIdPrefix = "primer_set";
        public const string IgnitionBindingIdPrefix = "ignition_set";
        public const string AvionicsPowerBindingId =
            "avionics_power_toggle";
        public const string FlapSelectorBindingIdPrefix =
            "flap_selector_set";
        public const string FuelSelectorBindingIdPrefix =
            "fuel_selector_set";

        [Header("Serialized single flight/input authority")]
        [SerializeField] private FlightDynamicsCoordinator coordinator;
        [SerializeField]
        private ProductionCockpitSystemsController systemsAuthority;

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
        private ulong _systemsCommandSequence;

        public FlightDynamicsCoordinator Coordinator => coordinator;
        public ProductControlInput InputAuthority =>
            coordinator != null ? coordinator.controls : null;
        public ProductionCockpitSystemsController SystemsAuthority =>
            systemsAuthority;
        public Transform ThrottleReferenceFrame => throttleReferenceFrame;
        public Transform MixtureReferenceFrame => mixtureReferenceFrame;
        public Vector3 ThrottleFullForwardAxisLocal =>
            throttleFullForwardAxisLocal;
        public Vector3 MixtureFullRichAxisLocal =>
            mixtureFullRichAxisLocal;
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

        public void ConfigureSystemsAuthority(
            ProductionCockpitSystemsController authoredSystemsAuthority)
        {
            systemsAuthority = authoredSystemsAuthority;
            SynchronizeCommandSequence();
        }

        public bool ValidateAuthoredReferences(out string error)
        {
            if (coordinator == null)
            {
                error =
                    "Touch V2 requires the serialized sole FlightDynamicsCoordinator.";
                return false;
            }
            if (systemsAuthority == null)
            {
                error =
                    "Touch V2 requires the serialized ProductionCockpitSystemsController command authority.";
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
            SynchronizeCommandSequence();
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

            if (IsSystemsTarget(command.target))
                return ConsumeSystemsCommand(command);

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
            {
                ProductTouchControlChannel capturedChannel =
                    ProductChannel(command.target);
                if (inputAuthority.IsTouchOverrideOwnedBy(
                        capturedChannel,
                        _ownerToken))
                    return false;

                // ProductControlInput deliberately expires a continuous
                // lease after tracking silence. The Meta pointable selection
                // can outlive that watchdog (for example across a long frame
                // or modality transition), so never let the bridge retain a
                // phantom capture that permanently rejects the next grab.
                capture = default;
            }
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
            if (!inputAuthority.CanAcceptTouchOverrides)
                return false;

            ProductTouchControlChannel channel =
                ProductChannel(command.target);
            if (!capture.active ||
                capture.hand != command.hand ||
                !inputAuthority.IsTouchOverrideOwnedBy(
                    channel,
                    _ownerToken))
            {
                // A selected Meta interactor continues emitting Move after a
                // watchdog expiry. Reacquire from the current pointer pose so
                // the held control becomes live again instead of remaining
                // dead until the application restarts.
                capture = default;
                return BeginCapture(command);
            }

            switch (channel)
            {
                case ProductTouchControlChannel.PilotYoke:
                    if (!CockpitTouchInputV2Kinematics.TryEvaluateYoke(
                            yokeReferenceFrame,
                            yokePitchAxisLocal,
                            yokeRollAxisLocal,
                            YokePitchHalfTravelMeters * QuestFlight.Product.ProductComfortPreferences.PitchTravel,
                            YokeRollMaximumDegrees * QuestFlight.Product.ProductComfortPreferences.RollTravel,
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

            ProductTouchControlChannel channel =
                ProductChannel(command.target);
            bool stillOwned =
                inputAuthority.IsTouchOverrideOwnedBy(
                    channel,
                    _ownerToken);
            bool released = !stillOwned ||
                inputAuthority.TryEndTouchOverride(
                    channel,
                    _ownerToken);
            capture = default;
            return released;
        }

        private bool ValidateFixedBinding(
            in CockpitTouchInteractionV2Command command)
        {
            switch (command.target)
            {
                case CockpitTouchInteractionV2Target.PilotYoke:
                    return IsPhysicalHand(command.hand) &&
                           string.Equals(
                               command.bindingId,
                               PilotYokeBindingId,
                               StringComparison.Ordinal);
                case CockpitTouchInteractionV2Target.Throttle:
                    return IsPhysicalHand(command.hand) &&
                           string.Equals(
                               command.bindingId,
                               ThrottleBindingId,
                               StringComparison.Ordinal);
                case CockpitTouchInteractionV2Target.Mixture:
                    return IsPhysicalHand(command.hand) &&
                           string.Equals(
                               command.bindingId,
                               MixtureBindingId,
                               StringComparison.Ordinal);
                case CockpitTouchInteractionV2Target.AltBatMaster:
                    return IsPhysicalHand(command.hand) &&
                           string.Equals(
                               command.bindingId,
                               AltBatMasterBindingId,
                               StringComparison.Ordinal);
                case CockpitTouchInteractionV2Target.Primer:
                    return IsPhysicalHand(command.hand) &&
                           HasBindingPrefix(
                               command.bindingId,
                               PrimerBindingIdPrefix);
                case CockpitTouchInteractionV2Target.IgnitionStart:
                    return IsPhysicalHand(command.hand) &&
                           HasBindingPrefix(
                               command.bindingId,
                               IgnitionBindingIdPrefix);
                case CockpitTouchInteractionV2Target.AvionicsPower:
                    return IsPhysicalHand(command.hand) &&
                           string.Equals(
                               command.bindingId,
                               AvionicsPowerBindingId,
                               StringComparison.Ordinal);
                case CockpitTouchInteractionV2Target.FlapSelector:
                    return IsPhysicalHand(command.hand) &&
                           HasBindingPrefix(
                               command.bindingId,
                               FlapSelectorBindingIdPrefix);
                case CockpitTouchInteractionV2Target.FuelSelector:
                    return IsPhysicalHand(command.hand) &&
                           HasBindingPrefix(
                               command.bindingId,
                               FuelSelectorBindingIdPrefix);
                default:
                    return false;
            }
        }

        private bool ConsumeSystemsCommand(
            in CockpitTouchInteractionV2Command command)
        {
            if (systemsAuthority == null)
                return false;

            switch (command.target)
            {
                case CockpitTouchInteractionV2Target.AltBatMaster:
                    if (command.operation !=
                            CockpitTouchInteractionV2Operation.Toggle &&
                        command.operation !=
                            CockpitTouchInteractionV2Operation.SetNormalized)
                        return false;
                    if (!systemsAuthority
                            .TryGetCockpitSystemsSnapshotV2(
                                out CockpitSystemsSnapshotV2 electrical))
                        return false;
                    bool mastersOn =
                        command.operation ==
                        CockpitTouchInteractionV2Operation.Toggle
                            ? !(electrical.batteryMasterOn &&
                                electrical.alternatorMasterOn)
                            : command.normalizedValue >= 0.5f;
                    return ApplyBoolean(
                               CockpitSystemsCommandKindV2.SetBatteryMaster,
                               mastersOn) &&
                           ApplyBoolean(
                               CockpitSystemsCommandKindV2
                                   .SetAlternatorMaster,
                               mastersOn);

                case CockpitTouchInteractionV2Target.Primer:
                    if (command.operation ==
                        CockpitTouchInteractionV2Operation.Press)
                    {
                        return ApplyNormalized(
                            CockpitSystemsCommandKindV2.SetPrimerPosition,
                            1f);
                    }
                    if (command.operation ==
                        CockpitTouchInteractionV2Operation.Release)
                    {
                        return ApplyNormalized(
                            CockpitSystemsCommandKindV2.SetPrimerPosition,
                            0f);
                    }
                    return command.operation ==
                               CockpitTouchInteractionV2Operation.SetNormalized &&
                           ApplyNormalized(
                               CockpitSystemsCommandKindV2.SetPrimerPosition,
                               command.normalizedValue);

                case CockpitTouchInteractionV2Target.IgnitionStart:
                    if (command.operation ==
                        CockpitTouchInteractionV2Operation.Press)
                    {
                        return ApplyDiscrete(
                            CockpitSystemsCommandKindV2.SetIgnitionSwitch,
                            (int)CockpitIgnitionSwitchPositionV2.Start);
                    }
                    if (command.operation ==
                        CockpitTouchInteractionV2Operation.Release)
                    {
                        return ApplyDiscrete(
                            CockpitSystemsCommandKindV2.SetIgnitionSwitch,
                            (int)CockpitIgnitionSwitchPositionV2.Both);
                    }
                    if (command.operation ==
                        CockpitTouchInteractionV2Operation.SetNormalized)
                    {
                        return ApplyDiscrete(
                            CockpitSystemsCommandKindV2.SetIgnitionSwitch,
                            Mathf.RoundToInt(
                                Mathf.Clamp01(
                                    command.normalizedValue) *
                                (int)CockpitIgnitionSwitchPositionV2.Start));
                    }
                    if (command.operation !=
                            CockpitTouchInteractionV2Operation.Toggle ||
                        !systemsAuthority
                            .TryGetCockpitSystemsSnapshotV2(
                                out CockpitSystemsSnapshotV2 ignition))
                        return false;
                    return ApplyDiscrete(
                        CockpitSystemsCommandKindV2.SetIgnitionSwitch,
                        (int)NextIgnitionToggle(
                            ignition.ignitionSwitchPosition));

                case CockpitTouchInteractionV2Target.AvionicsPower:
                    if (command.operation !=
                            CockpitTouchInteractionV2Operation.Toggle &&
                        command.operation !=
                            CockpitTouchInteractionV2Operation.SetNormalized)
                        return false;
                    if (!systemsAuthority
                            .TryGetCockpitSystemsSnapshotV2(
                                out CockpitSystemsSnapshotV2 avionics))
                        return false;
                    bool avionicsOn =
                        command.operation ==
                        CockpitTouchInteractionV2Operation.Toggle
                            ? !avionics.avionicsMasterOn
                            : command.normalizedValue >= 0.5f;
                    return ApplyBoolean(
                        CockpitSystemsCommandKindV2.SetAvionicsMaster,
                        avionicsOn);

                case CockpitTouchInteractionV2Target.FlapSelector:
                    if ((command.operation !=
                             CockpitTouchInteractionV2Operation.SetNormalized &&
                         command.operation !=
                             CockpitTouchInteractionV2Operation.Toggle) ||
                        InputAuthority == null ||
                        !InputAuthority.CanAcceptTouchOverrides)
                        return false;
                    float flapCommand;
                    if (command.operation ==
                        CockpitTouchInteractionV2Operation.Toggle)
                    {
                        if (!systemsAuthority
                                .TryGetCockpitSystemsSnapshotV2(
                                    out CockpitSystemsSnapshotV2 flaps))
                            return false;
                        int currentDetent =
                            Mathf.RoundToInt(
                                Mathf.Clamp01(
                                    flaps.flapSelectorCommandNormalized) *
                                3f);
                        flapCommand =
                            ((currentDetent + 1) % 4) / 3f;
                    }
                    else
                    {
                        flapCommand =
                            Mathf.Clamp01(command.normalizedValue);
                    }
                    if (!ApplyNormalized(
                            CockpitSystemsCommandKindV2.SetFlapSelector,
                            flapCommand))
                        return false;
                    EnsureOwnerToken();
                    return InputAuthority.TryApplyTouchDiscreteValue(
                        ProductTouchControlChannel.FlapSelector,
                        _ownerToken,
                        flapCommand);

                case CockpitTouchInteractionV2Target.FuelSelector:
                    if (command.operation ==
                        CockpitTouchInteractionV2Operation.SetNormalized)
                    {
                        return ApplyDiscrete(
                            CockpitSystemsCommandKindV2.SetFuelSelector,
                            Mathf.RoundToInt(
                                Mathf.Clamp01(
                                    command.normalizedValue) *
                                (int)CockpitFuelSelectorPositionV2.Right));
                    }
                    if (command.operation !=
                            CockpitTouchInteractionV2Operation.Toggle ||
                        !systemsAuthority
                            .TryGetCockpitSystemsSnapshotV2(
                                out CockpitSystemsSnapshotV2 fuel))
                        return false;
                    int nextFuel =
                        ((int)fuel.fuelSelectorPosition + 1) %
                        ((int)CockpitFuelSelectorPositionV2.Right + 1);
                    return ApplyDiscrete(
                        CockpitSystemsCommandKindV2.SetFuelSelector,
                        nextFuel);

                default:
                    return false;
            }
        }

        private bool ApplyBoolean(
            CockpitSystemsCommandKindV2 kind,
            bool value)
        {
            return systemsAuthority.TryApplyCommand(
                CockpitSystemsCommandV2.Boolean(
                    NextSystemsCommandSequence(),
                    kind,
                    value));
        }

        private bool ApplyDiscrete(
            CockpitSystemsCommandKindV2 kind,
            int value)
        {
            return systemsAuthority.TryApplyCommand(
                CockpitSystemsCommandV2.Discrete(
                    NextSystemsCommandSequence(),
                    kind,
                    value));
        }

        private bool ApplyNormalized(
            CockpitSystemsCommandKindV2 kind,
            float value)
        {
            return systemsAuthority.TryApplyCommand(
                CockpitSystemsCommandV2.Normalized(
                    NextSystemsCommandSequence(),
                    kind,
                    value));
        }

        private static CockpitIgnitionSwitchPositionV2
            NextIgnitionToggle(
                CockpitIgnitionSwitchPositionV2 current)
        {
            switch (current)
            {
                case CockpitIgnitionSwitchPositionV2.Off:
                    return CockpitIgnitionSwitchPositionV2.Right;
                case CockpitIgnitionSwitchPositionV2.Right:
                    return CockpitIgnitionSwitchPositionV2.Left;
                case CockpitIgnitionSwitchPositionV2.Left:
                    return CockpitIgnitionSwitchPositionV2.Both;
                case CockpitIgnitionSwitchPositionV2.Start:
                    return CockpitIgnitionSwitchPositionV2.Both;
                default:
                    return CockpitIgnitionSwitchPositionV2.Off;
            }
        }

        private ulong NextSystemsCommandSequence()
        {
            ulong minimum = 1ul;
            if (systemsAuthority != null &&
                systemsAuthority.TryGetCockpitSystemsSnapshotV2(
                    out CockpitSystemsSnapshotV2 snapshot) &&
                snapshot.lastAppliedCommandSequence < ulong.MaxValue)
            {
                minimum =
                    snapshot.lastAppliedCommandSequence + 1ul;
            }
            if (_systemsCommandSequence < minimum)
                _systemsCommandSequence = minimum;
            else if (_systemsCommandSequence < ulong.MaxValue)
                _systemsCommandSequence++;
            return _systemsCommandSequence;
        }

        private void SynchronizeCommandSequence()
        {
            _systemsCommandSequence = 0ul;
            if (systemsAuthority != null &&
                systemsAuthority.TryGetCockpitSystemsSnapshotV2(
                    out CockpitSystemsSnapshotV2 snapshot))
            {
                _systemsCommandSequence =
                    snapshot.lastAppliedCommandSequence;
            }
        }

        private static bool IsSystemsTarget(
            CockpitTouchInteractionV2Target target)
        {
            return target >=
                       CockpitTouchInteractionV2Target.AltBatMaster &&
                   target <=
                       CockpitTouchInteractionV2Target.FuelSelector;
        }

        private static bool IsPhysicalHand(
            CockpitTouchInteractionV2Hand hand)
        {
            return hand == CockpitTouchInteractionV2Hand.Left ||
                   hand == CockpitTouchInteractionV2Hand.Right;
        }

        private static bool HasBindingPrefix(
            string bindingId,
            string prefix)
        {
            return !string.IsNullOrEmpty(bindingId) &&
                   (string.Equals(
                        bindingId,
                        prefix,
                        StringComparison.Ordinal) ||
                    (bindingId.Length > prefix.Length &&
                     bindingId[prefix.Length] == '_' &&
                     bindingId.StartsWith(
                         prefix,
                         StringComparison.Ordinal)));
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
