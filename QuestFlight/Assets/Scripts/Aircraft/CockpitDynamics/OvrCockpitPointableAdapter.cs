using System;
using Oculus.Interaction;
using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics
{
    /// <summary>
    /// Thin, allocation-free adapter from official Meta pointable events to
    /// the existing authoritative cockpit command boundary. Meta owns device
    /// tracking, hand visibility, interactor activation, and selection.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OvrCockpitPointableAdapter : MonoBehaviour
    {
        [SerializeField] private string bindingId;
        [SerializeField] private CockpitTouchInteractionV2Target target;
        [SerializeField] private CockpitTouchInteractionV2Hand allowedHand =
            CockpitTouchInteractionV2Hand.Either;
        [SerializeField] private CockpitTouchInteractionV2Mode mode;
        [SerializeField, Range(0f, 1f)] private float normalizedValue = 1f;
        [SerializeField] private Collider targetCollider;
        [SerializeField] private OvrPointerSourceRegistry sourceRegistry;
        [SerializeField] private MonoBehaviour commandConsumer;
        [SerializeField] private MonoBehaviour[] pointables =
            Array.Empty<MonoBehaviour>();

        private ICockpitTouchInteractionV2CommandConsumer _consumer;
        private int _activeIdentifier = int.MinValue;
        private CockpitTouchInteractionV2Hand _activeHand;
        private CockpitInputSourceKind _activeKind;

        public string BindingId => bindingId;
        public CockpitTouchInteractionV2Target Target => target;
        public int PointableCount => pointables?.Length ?? 0;

        public void ConfigureAuthoredReferences(
            string authoredBindingId,
            CockpitTouchInteractionV2Target authoredTarget,
            CockpitTouchInteractionV2Hand authoredAllowedHand,
            CockpitTouchInteractionV2Mode authoredMode,
            float authoredNormalizedValue,
            Collider authoredTargetCollider,
            OvrPointerSourceRegistry authoredSourceRegistry,
            MonoBehaviour authoredCommandConsumer,
            MonoBehaviour[] authoredPointables)
        {
            bindingId = authoredBindingId;
            target = authoredTarget;
            allowedHand = authoredAllowedHand;
            mode = authoredMode;
            normalizedValue = authoredNormalizedValue;
            targetCollider = authoredTargetCollider;
            sourceRegistry = authoredSourceRegistry;
            commandConsumer = authoredCommandConsumer;
            pointables = authoredPointables ?? Array.Empty<MonoBehaviour>();
            _consumer = commandConsumer as
                ICockpitTouchInteractionV2CommandConsumer;
        }

        public bool ValidateAuthoredReferences(out string error)
        {
            if (string.IsNullOrWhiteSpace(bindingId) ||
                target == CockpitTouchInteractionV2Target.None ||
                targetCollider == null || sourceRegistry == null ||
                !(commandConsumer is
                    ICockpitTouchInteractionV2CommandConsumer) ||
                pointables == null || pointables.Length == 0)
            {
                error = $"OVR cockpit adapter '{name}' has incomplete serialized references.";
                return false;
            }

            for (int i = 0; i < pointables.Length; i++)
            {
                if (!(pointables[i] is IPointable))
                {
                    error = $"OVR cockpit adapter '{name}' pointable {i} is invalid.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private void Awake()
        {
            _consumer = commandConsumer as
                ICockpitTouchInteractionV2CommandConsumer;
            if (!ValidateAuthoredReferences(out string error))
            {
                Debug.LogError("[QuestFlight][OVRInput] " + error, this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            Subscribe(true);
        }

        private void OnDisable()
        {
            Subscribe(false);
            CancelActiveSelection();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                CancelActiveSelection();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
                CancelActiveSelection();
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
            if (_consumer == null || sourceRegistry == null ||
                !sourceRegistry.TryResolve(
                    evt.Identifier,
                    out CockpitTouchInteractionV2Hand hand,
                    out CockpitInputSourceKind kind) ||
                !AllowsHand(hand))
                return;

            switch (evt.Type)
            {
                case PointerEventType.Select:
                    HandleSelect(evt, hand, kind);
                    break;
                case PointerEventType.Move:
                    if (_activeIdentifier == evt.Identifier &&
                        mode == CockpitTouchInteractionV2Mode.GrabPose)
                    {
                        Send(evt, hand, kind,
                            CockpitTouchInteractionV2Operation.UpdateGrab);
                    }
                    break;
                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    if (_activeIdentifier == evt.Identifier)
                        EndActive(evt);
                    break;
            }
        }

        private void HandleSelect(
            PointerEvent evt,
            CockpitTouchInteractionV2Hand hand,
            CockpitInputSourceKind kind)
        {
            if (_activeIdentifier != int.MinValue) return;

            CockpitTouchInteractionV2Operation operation;
            switch (mode)
            {
                case CockpitTouchInteractionV2Mode.GrabPose:
                    operation = CockpitTouchInteractionV2Operation.BeginGrab;
                    break;
                case CockpitTouchInteractionV2Mode.Momentary:
                    operation = CockpitTouchInteractionV2Operation.Press;
                    break;
                case CockpitTouchInteractionV2Mode.Toggle:
                    operation = CockpitTouchInteractionV2Operation.Toggle;
                    break;
                case CockpitTouchInteractionV2Mode.SetNormalized:
                    operation = CockpitTouchInteractionV2Operation.SetNormalized;
                    break;
                default:
                    return;
            }

            bool accepted = Send(evt, hand, kind, operation);
            if (!accepted ||
                (mode != CockpitTouchInteractionV2Mode.GrabPose &&
                 mode != CockpitTouchInteractionV2Mode.Momentary))
                return;
            _activeIdentifier = evt.Identifier;
            _activeHand = hand;
            _activeKind = kind;
        }

        private void EndActive(PointerEvent evt)
        {
            CockpitTouchInteractionV2Operation operation =
                mode == CockpitTouchInteractionV2Mode.GrabPose
                    ? CockpitTouchInteractionV2Operation.EndGrab
                    : CockpitTouchInteractionV2Operation.Release;
            Send(evt, _activeHand, _activeKind, operation);
            _activeIdentifier = int.MinValue;
        }

        private void CancelActiveSelection()
        {
            if (_activeIdentifier == int.MinValue || _consumer == null) return;
            var command = BuildCommand(
                _activeIdentifier,
                0,
                _activeHand,
                _activeKind,
                mode == CockpitTouchInteractionV2Mode.GrabPose
                    ? CockpitTouchInteractionV2Operation.EndGrab
                    : CockpitTouchInteractionV2Operation.Release,
                new Pose(transform.position, transform.rotation));
            _consumer.TryConsumeCockpitTouchCommand(in command);
            _activeIdentifier = int.MinValue;
        }

        private bool Send(
            PointerEvent evt,
            CockpitTouchInteractionV2Hand hand,
            CockpitInputSourceKind kind,
            CockpitTouchInteractionV2Operation operation)
        {
            CockpitTouchInteractionV2Command command = BuildCommand(
                evt.Identifier,
                evt.EventId,
                hand,
                kind,
                operation,
                evt.Pose);
            bool accepted =
                _consumer.TryConsumeCockpitTouchCommand(in command);
            OvrConcurrentInputTrace.RecordInteraction(
                command,
                accepted,
                gameObject.name);
            return accepted;
        }

        private CockpitTouchInteractionV2Command BuildCommand(
            int identifier,
            ulong eventId,
            CockpitTouchInteractionV2Hand hand,
            CockpitInputSourceKind kind,
            CockpitTouchInteractionV2Operation operation,
            Pose pose)
        {
            return new CockpitTouchInteractionV2Command
            {
                bindingId = bindingId,
                target = target,
                hand = hand,
                operation = operation,
                normalizedValue = normalizedValue,
                pointerWorldPosition = pose.position,
                pointerWorldRotation = pose.rotation,
                hitWorldPosition = targetCollider != null
                    ? targetCollider.ClosestPoint(pose.position)
                    : pose.position,
                frameNumber = Time.frameCount,
                unscaledTime = Time.unscaledTime,
                sourceIdentifier = identifier,
                sourceKind = kind,
                pointerEventId = eventId
            };
        }

        private bool AllowsHand(CockpitTouchInteractionV2Hand hand)
        {
            return allowedHand == CockpitTouchInteractionV2Hand.Either ||
                   allowedHand == hand ||
                   IsContinuousControl(target);
        }

        private static bool IsContinuousControl(
            CockpitTouchInteractionV2Target value)
        {
            return value == CockpitTouchInteractionV2Target.PilotYoke ||
                   value == CockpitTouchInteractionV2Target.Throttle ||
                   value == CockpitTouchInteractionV2Target.Mixture;
        }
    }
}
