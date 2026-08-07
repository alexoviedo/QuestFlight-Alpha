using Oculus.Interaction;
using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics
{
    /// <summary>
    /// Development evidence for one continuous Meta interaction route. It
    /// observes SDK pointer events only and never changes interaction state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OvrContinuousControlTraceProbe : MonoBehaviour
    {
        [SerializeField] private CockpitTouchInteractionV2Target target;
        [SerializeField] private OvrPointerSourceRegistry sourceRegistry;
        [SerializeField] private Grabbable grabbable;
        [SerializeField] private MonoBehaviour[] pointables;

        public void ConfigureAuthoredReferences(
            CockpitTouchInteractionV2Target authoredTarget,
            OvrPointerSourceRegistry authoredRegistry,
            Grabbable authoredGrabbable,
            MonoBehaviour[] authoredPointables)
        {
            target = authoredTarget;
            sourceRegistry = authoredRegistry;
            grabbable = authoredGrabbable;
            pointables = authoredPointables;
        }

        public bool ValidateAuthoredReferences(out string error)
        {
            if (sourceRegistry == null || grabbable == null ||
                pointables == null || pointables.Length == 0)
            {
                error = "The OVR continuous-control trace probe is incomplete.";
                return false;
            }
            for (int i = 0; i < pointables.Length; i++)
            {
                if (!(pointables[i] is IPointable))
                {
                    error = "The OVR trace probe contains a non-pointable source.";
                    return false;
                }
            }
            error = string.Empty;
            return true;
        }

        private void OnEnable()
        {
            Subscribe(true);
        }

        private void OnDisable()
        {
            Subscribe(false);
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
            CockpitTouchInteractionV2Hand hand =
                CockpitTouchInteractionV2Hand.Either;
            CockpitInputSourceKind source = CockpitInputSourceKind.Unknown;
            sourceRegistry.TryResolve(evt.Identifier, out hand, out source);
            OvrConcurrentInputTrace.RecordPointerEvent(
                evt,
                target,
                hand,
                source,
                gameObject.name,
                grabbable.GrabPoints?.Count ?? 0);
        }
    }
}
