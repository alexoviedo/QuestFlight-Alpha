using System;
using UnityEngine;
using UnityEngine.XR;

namespace QuestFlightLab.Aircraft.CockpitDynamics
{
    public enum CockpitTouchInteractionV2Hand
    {
        Left = 0,
        Right = 1,
        Either = 2
    }

    public enum CockpitTouchInteractionV2Target
    {
        None = 0,
        PilotYoke = 1,
        Throttle = 2,
        Mixture = 3,
        AltBatMaster = 4,
        Primer = 5,
        IgnitionStart = 6,
        AvionicsPower = 7,
        FlapSelector = 8,
        FuelSelector = 9
    }

    public enum CockpitTouchInteractionV2Mode
    {
        Momentary = 0,
        Toggle = 1,
        SetNormalized = 2,
        GrabPose = 3
    }

    public enum CockpitTouchInteractionV2Operation
    {
        Press = 0,
        Release = 1,
        Toggle = 2,
        SetNormalized = 3,
        BeginGrab = 4,
        UpdateGrab = 5,
        EndGrab = 6
    }

    [Serializable]
    public struct CockpitTouchInteractionV2Command
    {
        public string bindingId;
        public CockpitTouchInteractionV2Target target;
        public CockpitTouchInteractionV2Hand hand;
        public CockpitTouchInteractionV2Operation operation;
        public float normalizedValue;
        public Vector3 pointerWorldPosition;
        public Quaternion pointerWorldRotation;
        public Vector3 hitWorldPosition;
        public int frameNumber;
        public float unscaledTime;

        public bool IsTerminal =>
            operation == CockpitTouchInteractionV2Operation.Release ||
            operation == CockpitTouchInteractionV2Operation.EndGrab;
    }

    /// <summary>
    /// Optional serialized consumer boundary. Consumers must reject commands
    /// they cannot apply authoritatively instead of inventing state.
    /// </summary>
    public interface ICockpitTouchInteractionV2CommandConsumer
    {
        bool TryConsumeCockpitTouchCommand(
            in CockpitTouchInteractionV2Command command);
    }

    /// <summary>
    /// One allocation-free Touch interaction loop for all positively
    /// represented cockpit controls. References, colliders, and commands are
    /// serialized by Editor authoring. Runtime performs no discovery and
    /// constructs no hierarchy.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class CockpitTouchInteractionV2Controller : MonoBehaviour
    {
        public const int RaycastHitCapacity = 8;
        public const int MinimumQueueCapacity = 8;
        public const int MaximumQueueCapacity = 128;
        public const float DefaultMaximumRayDistanceMeters = 0.8f;
        public const float MinimumRayDistanceMeters = 0.05f;
        public const float MaximumRayDistanceMeters = 2f;
        public const float DirectTouchRadiusMeters = 0.11f;
        public const float SelectPressThreshold = 0.55f;
        public const float SelectReleaseThreshold = 0.35f;

        private static readonly InputFeatureUsage<Vector3>
            PointerPositionUsage =
                new InputFeatureUsage<Vector3>("pointerPosition");
        private static readonly InputFeatureUsage<Quaternion>
            PointerRotationUsage =
                new InputFeatureUsage<Quaternion>("pointerRotation");

        private struct PointerSample
        {
            public Vector3 gripWorldPosition;
            public Quaternion gripWorldRotation;
            public Vector3 aimWorldPosition;
            public Vector3 aimWorldDirection;

            public static PointerSample FromTransform(Transform value)
            {
                return new PointerSample
                {
                    gripWorldPosition =
                        value != null ? value.position : Vector3.zero,
                    gripWorldRotation =
                        value != null ? value.rotation : Quaternion.identity,
                    aimWorldPosition =
                        value != null ? value.position : Vector3.zero,
                    aimWorldDirection =
                        value != null ? value.forward : Vector3.forward
                };
            }
        }

        [Serializable]
        public sealed class Binding
        {
            public string id;
            public Collider targetCollider;
            public CockpitTouchInteractionV2Target target;
            public CockpitTouchInteractionV2Hand allowedHand =
                CockpitTouchInteractionV2Hand.Either;
            public CockpitTouchInteractionV2Mode mode =
                CockpitTouchInteractionV2Mode.GrabPose;
            [Range(0f, 1f)] public float normalizedValue = 1f;

            public bool IsValid()
            {
                if (string.IsNullOrWhiteSpace(id) ||
                    targetCollider == null ||
                    target == CockpitTouchInteractionV2Target.None ||
                    !IsFinite(normalizedValue) ||
                    normalizedValue < 0f ||
                    normalizedValue > 1f)
                    return false;

                switch (target)
                {
                    case CockpitTouchInteractionV2Target.PilotYoke:
                        return mode == CockpitTouchInteractionV2Mode.GrabPose;
                    case CockpitTouchInteractionV2Target.Throttle:
                    case CockpitTouchInteractionV2Target.Mixture:
                        return mode == CockpitTouchInteractionV2Mode.GrabPose ||
                               mode ==
                               CockpitTouchInteractionV2Mode.SetNormalized;
                    case CockpitTouchInteractionV2Target.AltBatMaster:
                    case CockpitTouchInteractionV2Target.AvionicsPower:
                        return mode == CockpitTouchInteractionV2Mode.Toggle ||
                               mode ==
                               CockpitTouchInteractionV2Mode.SetNormalized;
                    case CockpitTouchInteractionV2Target.Primer:
                        return mode ==
                                   CockpitTouchInteractionV2Mode.SetNormalized ||
                               mode ==
                                   CockpitTouchInteractionV2Mode.Momentary;
                    case CockpitTouchInteractionV2Target.FlapSelector:
                    case CockpitTouchInteractionV2Target.FuelSelector:
                        return mode ==
                                   CockpitTouchInteractionV2Mode.SetNormalized ||
                               mode ==
                                   CockpitTouchInteractionV2Mode.Toggle;
                    case CockpitTouchInteractionV2Target.IgnitionStart:
                        return mode ==
                                   CockpitTouchInteractionV2Mode.SetNormalized ||
                               mode ==
                                   CockpitTouchInteractionV2Mode.Toggle ||
                               mode ==
                                   CockpitTouchInteractionV2Mode.Momentary;
                    default:
                        return false;
                }
            }

            private static bool IsFinite(float value)
            {
                return !float.IsNaN(value) &&
                       !float.IsInfinity(value);
            }
        }

        [Header("OpenXR-owned controller poses")]
        [SerializeField] private Transform leftRayOrigin;
        [SerializeField] private Transform rightRayOrigin;

        [Header("One centralized authored interaction set")]
        [SerializeField] private LayerMask interactionLayers = ~0;
        [SerializeField, Range(
            MinimumRayDistanceMeters,
            MaximumRayDistanceMeters)]
        private float maximumRayDistanceMeters =
            DefaultMaximumRayDistanceMeters;
        [SerializeField] private QueryTriggerInteraction triggerInteraction =
            QueryTriggerInteraction.Collide;
        [SerializeField] private Binding[] bindings;
        [SerializeField] private MonoBehaviour commandConsumer;
        [SerializeField, Range(MinimumQueueCapacity, MaximumQueueCapacity)]
        private int commandQueueCapacity = 32;

        private readonly RaycastHit[] _raycastHits =
            new RaycastHit[RaycastHitCapacity];
        private CockpitTouchInteractionV2Command[] _commands;
        private ICockpitTouchInteractionV2CommandConsumer _consumer;
        private int _queueRead;
        private int _queueWrite;
        private int _queueCount;
        private int _leftActiveBinding = -1;
        private int _rightActiveBinding = -1;
        private Vector3 _leftHitWorldPosition;
        private Vector3 _rightHitWorldPosition;
        private bool _leftWasPressed;
        private bool _rightWasPressed;

        public Transform LeftRayOrigin => leftRayOrigin;
        public Transform RightRayOrigin => rightRayOrigin;
        public MonoBehaviour CommandConsumer => commandConsumer;
        public int BindingCount => bindings?.Length ?? 0;
        public int QueuedCommandCount => _queueCount;
        public int DroppedCommandCount { get; private set; }

        public void ConfigureAuthoredReferences(
            Transform authoredLeftRayOrigin,
            Transform authoredRightRayOrigin,
            LayerMask authoredInteractionLayers,
            float authoredMaximumRayDistanceMeters,
            Binding[] authoredBindings,
            MonoBehaviour authoredCommandConsumer,
            int authoredQueueCapacity = 32)
        {
            leftRayOrigin = authoredLeftRayOrigin;
            rightRayOrigin = authoredRightRayOrigin;
            interactionLayers = authoredInteractionLayers;
            maximumRayDistanceMeters =
                authoredMaximumRayDistanceMeters;
            bindings = authoredBindings;
            commandConsumer = authoredCommandConsumer;
            commandQueueCapacity = authoredQueueCapacity;
            PrepareRuntimeState();
        }

        public bool ValidateAuthoredReferences(out string error)
        {
            if (leftRayOrigin == null || rightRayOrigin == null)
            {
                error =
                    "Both serialized OpenXR Touch controller ray origins are required.";
                return false;
            }
            if (leftRayOrigin == rightRayOrigin)
            {
                error = "Left and right Touch ray origins must be distinct.";
                return false;
            }
            if (!IsFinite(maximumRayDistanceMeters) ||
                maximumRayDistanceMeters < MinimumRayDistanceMeters ||
                maximumRayDistanceMeters > MaximumRayDistanceMeters)
            {
                error = "Touch interaction ray distance is outside the bounded contract.";
                return false;
            }
            if (commandQueueCapacity < MinimumQueueCapacity ||
                commandQueueCapacity > MaximumQueueCapacity)
            {
                error = "Touch interaction command queue capacity is outside bounds.";
                return false;
            }
            if (commandConsumer != null &&
                !(commandConsumer is
                    ICockpitTouchInteractionV2CommandConsumer))
            {
                error =
                    "Serialized Touch command consumer does not implement the V2 consumer boundary.";
                return false;
            }
            if (bindings == null || bindings.Length == 0)
            {
                error = "At least one represented Touch binding is required.";
                return false;
            }

            for (int index = 0; index < bindings.Length; index++)
            {
                Binding binding = bindings[index];
                if (binding == null || !binding.IsValid())
                {
                    error = $"Touch binding {index} is not a represented, valid control.";
                    return false;
                }
                for (int prior = 0; prior < index; prior++)
                {
                    Binding previous = bindings[prior];
                    if (previous == null) continue;
                    if (previous.targetCollider == binding.targetCollider)
                    {
                        error =
                            $"Touch collider '{binding.targetCollider.name}' is bound twice.";
                        return false;
                    }
                    if (string.Equals(
                            previous.id,
                            binding.id,
                            StringComparison.Ordinal))
                    {
                        error =
                            $"Touch binding id '{binding.id}' is duplicated.";
                        return false;
                    }
                }
            }

            error = string.Empty;
            return true;
        }

        private void Awake()
        {
            PrepareRuntimeState();
            if (ValidateAuthoredReferences(out string error))
                return;
            Debug.LogError(
                "[QuestFlight][CockpitTouchInteractionV2] " + error,
                this);
            enabled = false;
        }

        private void OnDisable()
        {
            ReleaseActiveCaptures();
            ClearInteractionState();
        }

        private void Update()
        {
            InputDevice leftDevice =
                InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            InputDevice rightDevice =
                InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool leftPressed =
                ReadSelectButton(leftDevice, _leftWasPressed);
            bool rightPressed =
                ReadSelectButton(rightDevice, _rightWasPressed);
            ProcessPointer(
                CockpitTouchInteractionV2Hand.Left,
                BuildPointerSample(leftRayOrigin, leftDevice),
                leftPressed,
                ref _leftWasPressed,
                ref _leftActiveBinding,
                ref _leftHitWorldPosition,
                Time.unscaledTime,
                Time.frameCount);
            ProcessPointer(
                CockpitTouchInteractionV2Hand.Right,
                BuildPointerSample(rightRayOrigin, rightDevice),
                rightPressed,
                ref _rightWasPressed,
                ref _rightActiveBinding,
                ref _rightHitWorldPosition,
                Time.unscaledTime,
                Time.frameCount);
            FlushToSerializedConsumer();
        }

        /// <summary>
        /// Deterministic test seam. Product input still comes only from the
        /// serialized OpenXR ray origins and controller trigger features.
        /// </summary>
        public void ProcessPointerForTest(
            CockpitTouchInteractionV2Hand hand,
            bool pressed,
            float unscaledTime,
            int frameNumber)
        {
            if (hand == CockpitTouchInteractionV2Hand.Left)
            {
                ProcessPointer(
                    hand,
                    PointerSample.FromTransform(leftRayOrigin),
                    pressed,
                    ref _leftWasPressed,
                    ref _leftActiveBinding,
                    ref _leftHitWorldPosition,
                    unscaledTime,
                    frameNumber);
            }
            else if (hand == CockpitTouchInteractionV2Hand.Right)
            {
                ProcessPointer(
                    hand,
                    PointerSample.FromTransform(rightRayOrigin),
                    pressed,
                    ref _rightWasPressed,
                    ref _rightActiveBinding,
                    ref _rightHitWorldPosition,
                    unscaledTime,
                    frameNumber);
            }
        }

        public void ProcessPointerSampleForTest(
            CockpitTouchInteractionV2Hand hand,
            bool pressed,
            Vector3 gripWorldPosition,
            Quaternion gripWorldRotation,
            Vector3 aimWorldPosition,
            Vector3 aimWorldDirection,
            float unscaledTime,
            int frameNumber)
        {
            PointerSample sample = new PointerSample
            {
                gripWorldPosition = gripWorldPosition,
                gripWorldRotation = gripWorldRotation,
                aimWorldPosition = aimWorldPosition,
                aimWorldDirection =
                    NormalizeOrFallback(
                        aimWorldDirection,
                        gripWorldRotation * Vector3.forward)
            };
            if (hand == CockpitTouchInteractionV2Hand.Left)
            {
                ProcessPointer(
                    hand,
                    sample,
                    pressed,
                    ref _leftWasPressed,
                    ref _leftActiveBinding,
                    ref _leftHitWorldPosition,
                    unscaledTime,
                    frameNumber);
            }
            else if (hand == CockpitTouchInteractionV2Hand.Right)
            {
                ProcessPointer(
                    hand,
                    sample,
                    pressed,
                    ref _rightWasPressed,
                    ref _rightActiveBinding,
                    ref _rightHitWorldPosition,
                    unscaledTime,
                    frameNumber);
            }
        }

        public bool TryDequeueCommand(
            out CockpitTouchInteractionV2Command command)
        {
            EnsureQueueAllocated();
            if (_queueCount <= 0)
            {
                command = default;
                return false;
            }

            command = _commands[_queueRead];
            _commands[_queueRead] = default;
            _queueRead = (_queueRead + 1) % _commands.Length;
            _queueCount--;
            return true;
        }

        private void PrepareRuntimeState()
        {
            _consumer =
                commandConsumer as
                    ICockpitTouchInteractionV2CommandConsumer;
            EnsureQueueAllocated();
            ResetCommandQueue();
            ClearInteractionState();
        }

        private void EnsureQueueAllocated()
        {
            int boundedCapacity = Mathf.Clamp(
                commandQueueCapacity,
                MinimumQueueCapacity,
                MaximumQueueCapacity);
            if (_commands != null &&
                _commands.Length == boundedCapacity)
                return;
            _commands =
                new CockpitTouchInteractionV2Command[
                    boundedCapacity];
        }

        private void ResetCommandQueue()
        {
            if (_commands != null)
                Array.Clear(_commands, 0, _commands.Length);
            _queueRead = 0;
            _queueWrite = 0;
            _queueCount = 0;
            DroppedCommandCount = 0;
        }

        private void ClearInteractionState()
        {
            _leftActiveBinding = -1;
            _rightActiveBinding = -1;
            _leftHitWorldPosition = Vector3.zero;
            _rightHitWorldPosition = Vector3.zero;
            _leftWasPressed = false;
            _rightWasPressed = false;
        }

        private void ProcessPointer(
            CockpitTouchInteractionV2Hand hand,
            PointerSample pointer,
            bool pressed,
            ref bool wasPressed,
            ref int activeBinding,
            ref Vector3 activeHitWorldPosition,
            float unscaledTime,
            int frameNumber)
        {
            bool pressedThisFrame = pressed && !wasPressed;
            bool releasedThisFrame = !pressed && wasPressed;
            wasPressed = pressed;

            // Direct grabs commonly begin with the grip/trigger already held
            // while the hand moves onto a control. Re-evaluate acquisition
            // until a binding is captured instead of requiring a one-frame
            // button-down/raycast coincidence.
            if ((pressedThisFrame ||
                 (pressed && activeBinding < 0)) &&
                activeBinding < 0)
            {
                if (!TryResolveNearestBinding(
                        hand,
                        pointer,
                        out int bindingIndex,
                        out Vector3 hitWorldPosition))
                    return;

                Binding binding = bindings[bindingIndex];
                activeBinding = bindingIndex;
                activeHitWorldPosition = hitWorldPosition;
                switch (binding.mode)
                {
                    case CockpitTouchInteractionV2Mode.Momentary:
                        Enqueue(
                            Command(
                                binding,
                                hand,
                                CockpitTouchInteractionV2Operation.Press,
                                pointer,
                                hitWorldPosition,
                                unscaledTime,
                                frameNumber));
                        break;
                    case CockpitTouchInteractionV2Mode.Toggle:
                        Enqueue(
                            Command(
                                binding,
                                hand,
                                CockpitTouchInteractionV2Operation.Toggle,
                                pointer,
                                hitWorldPosition,
                                unscaledTime,
                                frameNumber));
                        break;
                    case CockpitTouchInteractionV2Mode.SetNormalized:
                        Enqueue(
                            Command(
                                binding,
                                hand,
                                CockpitTouchInteractionV2Operation.SetNormalized,
                                pointer,
                                hitWorldPosition,
                                unscaledTime,
                                frameNumber));
                        break;
                    case CockpitTouchInteractionV2Mode.GrabPose:
                        Enqueue(
                            Command(
                                binding,
                                hand,
                                CockpitTouchInteractionV2Operation.BeginGrab,
                                pointer,
                                hitWorldPosition,
                                unscaledTime,
                                frameNumber));
                        break;
                }
            }
            else if (pressed &&
                     activeBinding >= 0 &&
                     activeBinding < BindingCount &&
                     bindings[activeBinding].mode ==
                     CockpitTouchInteractionV2Mode.GrabPose)
            {
                Enqueue(
                    Command(
                        bindings[activeBinding],
                        hand,
                        CockpitTouchInteractionV2Operation.UpdateGrab,
                        pointer,
                        activeHitWorldPosition,
                        unscaledTime,
                        frameNumber));
            }

            if (!releasedThisFrame || activeBinding < 0)
                return;

            Binding released = bindings[activeBinding];
            if (released.mode ==
                CockpitTouchInteractionV2Mode.GrabPose ||
                released.mode ==
                CockpitTouchInteractionV2Mode.Momentary)
            {
                CockpitTouchInteractionV2Operation operation =
                    released.mode ==
                    CockpitTouchInteractionV2Mode.GrabPose
                        ? CockpitTouchInteractionV2Operation.EndGrab
                        : CockpitTouchInteractionV2Operation.Release;
                Enqueue(
                    Command(
                        released,
                        hand,
                        operation,
                        pointer,
                        activeHitWorldPosition,
                        unscaledTime,
                        frameNumber));
            }
            activeBinding = -1;
            activeHitWorldPosition = Vector3.zero;
        }

        private bool TryResolveNearestBinding(
            CockpitTouchInteractionV2Hand hand,
            PointerSample pointer,
            out int bindingIndex,
            out Vector3 hitWorldPosition)
        {
            bindingIndex = -1;
            hitWorldPosition = Vector3.zero;
            if (bindings == null ||
                bindings.Length == 0)
                return false;

            float directDistanceSquared =
                DirectTouchRadiusMeters *
                DirectTouchRadiusMeters;
            for (int candidate = 0;
                 candidate < bindings.Length;
                 candidate++)
            {
                Binding binding = bindings[candidate];
                if (!IsBindingAvailable(
                        binding,
                        candidate,
                        hand,
                        true))
                    continue;

                Vector3 closest =
                    binding.targetCollider.ClosestPoint(
                        pointer.gripWorldPosition);
                float distanceSquared =
                    (closest -
                     pointer.gripWorldPosition).sqrMagnitude;
                if (distanceSquared >
                    directDistanceSquared)
                    continue;

                directDistanceSquared = distanceSquared;
                bindingIndex = candidate;
                hitWorldPosition = closest;
            }
            if (bindingIndex >= 0)
                return true;

            Vector3 direction =
                NormalizeOrFallback(
                    pointer.aimWorldDirection,
                    pointer.gripWorldRotation *
                    Vector3.forward);
            int hitCount = Physics.RaycastNonAlloc(
                pointer.aimWorldPosition,
                direction,
                _raycastHits,
                maximumRayDistanceMeters,
                interactionLayers,
                triggerInteraction);
            float nearestDistance = float.PositiveInfinity;
            for (int hitIndex = 0;
                 hitIndex < hitCount;
                 hitIndex++)
            {
                RaycastHit hit = _raycastHits[hitIndex];
                for (int candidate = 0;
                     candidate < bindings.Length;
                     candidate++)
                {
                    Binding binding = bindings[candidate];
                    if (!IsBindingAvailable(
                            binding,
                            candidate,
                            hand,
                            false) ||
                        binding.targetCollider != hit.collider ||
                        hit.distance >= nearestDistance)
                        continue;
                    bindingIndex = candidate;
                    hitWorldPosition = hit.point;
                    nearestDistance = hit.distance;
                }
            }
            return bindingIndex >= 0;
        }

        private bool IsBindingAvailable(
            Binding binding,
            int bindingIndex,
            CockpitTouchInteractionV2Hand hand,
            bool direct)
        {
            if (binding == null ||
                binding.targetCollider == null ||
                !binding.targetCollider.enabled ||
                !binding.targetCollider.gameObject.activeInHierarchy ||
                IsCapturedByOtherHand(bindingIndex, hand))
                return false;

            int layerBit =
                1 << binding.targetCollider.gameObject.layer;
            if ((interactionLayers.value & layerBit) == 0)
                return false;

            // A physically direct grab is safe with either controller for the
            // three positively represented continuous controls. The authored
            // hand remains authoritative for distant aim-ray selection.
            return direct && IsRepresentedGrabTarget(binding.target)
                ? true
                : AllowsHand(binding.allowedHand, hand);
        }

        private static bool IsRepresentedGrabTarget(
            CockpitTouchInteractionV2Target target)
        {
            return target ==
                   CockpitTouchInteractionV2Target.PilotYoke ||
                   target ==
                   CockpitTouchInteractionV2Target.Throttle ||
                   target ==
                   CockpitTouchInteractionV2Target.Mixture;
        }

        private bool IsCapturedByOtherHand(
            int bindingIndex,
            CockpitTouchInteractionV2Hand hand)
        {
            return hand == CockpitTouchInteractionV2Hand.Left
                ? _rightActiveBinding == bindingIndex
                : _leftActiveBinding == bindingIndex;
        }

        private static bool AllowsHand(
            CockpitTouchInteractionV2Hand allowed,
            CockpitTouchInteractionV2Hand actual)
        {
            return allowed == CockpitTouchInteractionV2Hand.Either ||
                   allowed == actual;
        }

        private static CockpitTouchInteractionV2Command Command(
            Binding binding,
            CockpitTouchInteractionV2Hand hand,
            CockpitTouchInteractionV2Operation operation,
            PointerSample pointer,
            Vector3 hitWorldPosition,
            float unscaledTime,
            int frameNumber)
        {
            return new CockpitTouchInteractionV2Command
            {
                bindingId = binding.id,
                target = binding.target,
                hand = hand,
                operation = operation,
                normalizedValue = binding.normalizedValue,
                pointerWorldPosition =
                    pointer.gripWorldPosition,
                pointerWorldRotation =
                    pointer.gripWorldRotation,
                hitWorldPosition = hitWorldPosition,
                frameNumber = frameNumber,
                unscaledTime = unscaledTime
            };
        }

        private bool Enqueue(
            CockpitTouchInteractionV2Command command)
        {
            EnsureQueueAllocated();
            if (_queueCount >= _commands.Length)
            {
                DroppedCommandCount++;
                if (!command.IsTerminal)
                    return false;

                // Never strand a grabbed/momentary control because an update
                // filled the queue. A terminal command replaces the oldest
                // queued sample deterministically.
                _commands[_queueRead] = default;
                _queueRead = (_queueRead + 1) % _commands.Length;
                _queueCount--;
            }

            _commands[_queueWrite] = command;
            _queueWrite = (_queueWrite + 1) % _commands.Length;
            _queueCount++;
            return true;
        }

        private void FlushToSerializedConsumer()
        {
            if (_consumer == null)
                return;
            while (_queueCount > 0)
            {
                CockpitTouchInteractionV2Command command =
                    _commands[_queueRead];
                if (!_consumer.TryConsumeCockpitTouchCommand(
                        in command))
                    return;
                TryDequeueCommand(out _);
            }
        }

        private void ReleaseActiveCaptures()
        {
            float unscaledTime = Time.unscaledTime;
            int frameNumber = Time.frameCount;
            ReleaseActiveCapture(
                CockpitTouchInteractionV2Hand.Left,
                PointerSample.FromTransform(leftRayOrigin),
                ref _leftActiveBinding,
                ref _leftHitWorldPosition,
                unscaledTime,
                frameNumber);
            ReleaseActiveCapture(
                CockpitTouchInteractionV2Hand.Right,
                PointerSample.FromTransform(rightRayOrigin),
                ref _rightActiveBinding,
                ref _rightHitWorldPosition,
                unscaledTime,
                frameNumber);
            FlushToSerializedConsumer();
        }

        private void ReleaseActiveCapture(
            CockpitTouchInteractionV2Hand hand,
            PointerSample pointer,
            ref int activeBinding,
            ref Vector3 activeHitWorldPosition,
            float unscaledTime,
            int frameNumber)
        {
            if (activeBinding < 0 ||
                activeBinding >= BindingCount)
                return;

            Binding binding = bindings[activeBinding];
            if (binding.mode ==
                CockpitTouchInteractionV2Mode.GrabPose ||
                binding.mode ==
                CockpitTouchInteractionV2Mode.Momentary)
            {
                Enqueue(
                    Command(
                        binding,
                        hand,
                        binding.mode ==
                        CockpitTouchInteractionV2Mode.GrabPose
                            ? CockpitTouchInteractionV2Operation.EndGrab
                            : CockpitTouchInteractionV2Operation.Release,
                        pointer,
                        activeHitWorldPosition,
                        unscaledTime,
                        frameNumber));
            }
            activeBinding = -1;
            activeHitWorldPosition = Vector3.zero;
        }

        private static bool ReadSelectButton(
            InputDevice device,
            bool wasPressed)
        {
            if (!device.isValid)
                return false;

            bool digitalPressed =
                TryReadBoolean(
                    device,
                    CommonUsages.triggerButton) ||
                TryReadBoolean(
                    device,
                    CommonUsages.gripButton);
            if (digitalPressed)
                return true;

            float analogValue = 0f;
            if (device.TryGetFeatureValue(
                    CommonUsages.trigger,
                    out float trigger))
                analogValue = Mathf.Max(analogValue, trigger);
            if (device.TryGetFeatureValue(
                    CommonUsages.grip,
                    out float grip))
                analogValue = Mathf.Max(analogValue, grip);
            return analogValue >=
                   (wasPressed
                       ? SelectReleaseThreshold
                       : SelectPressThreshold);
        }

        private static bool TryReadBoolean(
            InputDevice device,
            InputFeatureUsage<bool> usage)
        {
            return device.TryGetFeatureValue(
                       usage,
                       out bool value) &&
                   value;
        }

        private static PointerSample BuildPointerSample(
            Transform gripTransform,
            InputDevice device)
        {
            PointerSample fallback =
                PointerSample.FromTransform(gripTransform);
            if (gripTransform == null ||
                !device.isValid)
                return fallback;

            bool hasGripPosition =
                device.TryGetFeatureValue(
                    CommonUsages.devicePosition,
                    out Vector3 gripTrackingPosition);
            bool hasGripRotation =
                device.TryGetFeatureValue(
                    CommonUsages.deviceRotation,
                    out Quaternion gripTrackingRotation);
            bool hasPointerPosition =
                device.TryGetFeatureValue(
                    PointerPositionUsage,
                    out Vector3 pointerTrackingPosition);
            bool hasPointerRotation =
                device.TryGetFeatureValue(
                    PointerRotationUsage,
                    out Quaternion pointerTrackingRotation);
            if (!hasGripRotation ||
                !IsFinite(gripTrackingRotation))
                return fallback;

            Quaternion trackingToWorld =
                gripTransform.rotation *
                Quaternion.Inverse(gripTrackingRotation);
            if (hasPointerRotation &&
                IsFinite(pointerTrackingRotation))
            {
                Quaternion aimWorldRotation =
                    trackingToWorld *
                    pointerTrackingRotation;
                fallback.aimWorldDirection =
                    aimWorldRotation * Vector3.forward;
            }
            if (hasGripPosition &&
                hasPointerPosition &&
                IsFinite(gripTrackingPosition) &&
                IsFinite(pointerTrackingPosition))
            {
                fallback.aimWorldPosition =
                    gripTransform.position +
                    trackingToWorld *
                    (pointerTrackingPosition -
                     gripTrackingPosition);
            }
            return fallback;
        }

        private static Vector3 NormalizeOrFallback(
            Vector3 value,
            Vector3 fallback)
        {
            return IsFinite(value) &&
                   value.sqrMagnitude > 0.000001f
                ? value.normalized
                : fallback.normalized;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }
    }
}
