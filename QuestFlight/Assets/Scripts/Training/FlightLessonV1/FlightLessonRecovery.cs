using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Training.FlightLessonV1
{
    public enum FlightLessonRecoveryPhase
    {
        Idle = 0,
        FadingOut = 1,
        AwaitingAuthoritativeReset = 2,
        Stabilizing = 3,
        FadingIn = 4,
        Failed = 5
    }

    public readonly struct FlightLessonRecoveryRequest
    {
        public readonly FlightLessonCrashReason reason;
        public readonly int checkpointIndex;
        public readonly FlightLessonRecoveryCheckpoint checkpoint;

        public FlightLessonRecoveryRequest(
            FlightLessonCrashReason reason,
            int checkpointIndex,
            FlightLessonRecoveryCheckpoint checkpoint)
        {
            this.reason = reason;
            this.checkpointIndex = checkpointIndex;
            this.checkpoint = checkpoint;
        }
    }

    public readonly struct FlightLessonRecoveryOutput
    {
        public readonly FlightLessonRecoveryPhase phase;
        public readonly float comfortFadeOpacity;
        public readonly bool suppressPlayerInput;
        public readonly bool cancelControlOwnership;
        public readonly bool requestAuthoritativeReset;
        public readonly FlightLessonRecoveryRequest request;

        public FlightLessonRecoveryOutput(
            FlightLessonRecoveryPhase phase,
            float comfortFadeOpacity,
            bool suppressPlayerInput,
            bool cancelControlOwnership,
            bool requestAuthoritativeReset,
            FlightLessonRecoveryRequest request)
        {
            this.phase = phase;
            this.comfortFadeOpacity = comfortFadeOpacity;
            this.suppressPlayerInput = suppressPlayerInput;
            this.cancelControlOwnership = cancelControlOwnership;
            this.requestAuthoritativeReset = requestAuthoritativeReset;
            this.request = request;
        }
    }

    /// <summary>
    /// Integration seam owned by the existing serialized product authority. Implementations
    /// must delegate the actual reset to the single FlightDynamicsCoordinator; they must not
    /// construct a backend, XR rig, camera, aircraft root, or runtime replacement hierarchy.
    /// </summary>
    public interface IFlightLessonRecoveryAuthority
    {
        void CancelAllPlayerControlOwnership();
        bool TryApplyAuthoritativeRecovery(in FlightLessonRecoveryRequest request);
        void RestoreLessonSegment(int segmentIndex);
        void SetComfortFade(float opacity);
    }

    public struct FlightLessonRecoveryTimings
    {
        public float fadeOutSeconds;
        public float stabilizationSeconds;
        public float fadeInSeconds;

        public static FlightLessonRecoveryTimings Default => new FlightLessonRecoveryTimings
        {
            fadeOutSeconds = 0.25f,
            stabilizationSeconds = 0.50f,
            fadeInSeconds = 0.35f
        };
    }

    public enum FlightLessonRecoveryLivenessStatus
    {
        Pending = 0,
        Passed = 1,
        Failed = 2
    }

    /// <summary>
    /// Verifies that a reset flight authority is actually advancing before the
    /// comfort fade is released. Placement alone is not recovery success.
    /// </summary>
    public sealed class FlightLessonRecoveryLivenessGate
    {
        public const int RequiredConsecutiveSteps = 3;
        public const float MinimumObservationSeconds = .5f;
        public const float TimeoutSeconds = 1f;
        private const double MinimumTimeAdvanceSeconds = 0.000001d;
        private const float MinimumMotionMeters = 0.05f;
        private const float MinimumVelocityMetersPerSecond = 1f;

        private FlightDynamicsState baseline;
        private double lastSimulationTime;
        private float elapsedSeconds;
        private int consecutiveAdvancingSteps;
        private bool requiresMotion;
        private bool active;

        public bool IsActive => active;
        public int ConsecutiveAdvancingSteps => consecutiveAdvancingSteps;
        public float ElapsedSeconds => elapsedSeconds;

        public void Begin(
            in FlightDynamicsState resetState,
            bool requireAuthoritativeMotion)
        {
            baseline = resetState;
            lastSimulationTime = resetState.simulationTimeSeconds;
            elapsedSeconds = 0f;
            consecutiveAdvancingSteps = 0;
            requiresMotion = requireAuthoritativeMotion;
            active = true;
        }

        public FlightLessonRecoveryLivenessStatus Observe(
            in FlightDynamicsState state,
            bool authorityEnabled,
            bool authorityInitialized,
            float deltaTimeSeconds,
            out string failure)
        {
            failure = string.Empty;
            if (!active)
            {
                failure = "Recovery liveness verification was not started.";
                return FlightLessonRecoveryLivenessStatus.Failed;
            }

            elapsedSeconds += Mathf.Max(0f, deltaTimeSeconds);
            if (!authorityEnabled)
            {
                active = false;
                failure = "The flight-authority component disabled before recovery advanced.";
                return FlightLessonRecoveryLivenessStatus.Failed;
            }
            if (!authorityInitialized)
            {
                active = false;
                failure = "The flight-dynamics backend became uninitialized during recovery.";
                return FlightLessonRecoveryLivenessStatus.Failed;
            }
            if (!state.IsFinite)
            {
                active = false;
                failure = "The flight-dynamics backend produced non-finite recovery state.";
                return FlightLessonRecoveryLivenessStatus.Failed;
            }

            if (System.Math.Abs(state.pitchDegrees) > 35 || System.Math.Abs(state.bankDegrees) > 55 ||
                state.angularVelocityBodyDegreesPerSecond.magnitude > 120)
            {
                active = false;
                failure = "Recovery attitude or angular rate is unsafe.";
                return FlightLessonRecoveryLivenessStatus.Failed;
            }
            bool timeAdvanced =
                state.simulationTimeSeconds >
                lastSimulationTime + MinimumTimeAdvanceSeconds;
            bool velocityValid = !requiresMotion ||
                                 state.velocityUnityMetersPerSecond.magnitude >=
                                 MinimumVelocityMetersPerSecond;
            consecutiveAdvancingSteps = timeAdvanced && velocityValid
                ? consecutiveAdvancingSteps + 1
                : 0;
            if (timeAdvanced)
                lastSimulationTime = state.simulationTimeSeconds;

            bool moved = !requiresMotion ||
                         Vector3.Distance(
                             baseline.positionUnityMeters,
                             state.positionUnityMeters) >=
                         MinimumMotionMeters;
            if (consecutiveAdvancingSteps >= RequiredConsecutiveSteps && moved &&
                state.simulationTimeSeconds - baseline.simulationTimeSeconds >= MinimumObservationSeconds &&
                elapsedSeconds >= MinimumObservationSeconds)
            {
                active = false;
                return FlightLessonRecoveryLivenessStatus.Passed;
            }

            if (elapsedSeconds >= TimeoutSeconds)
            {
                active = false;
                failure = timeAdvanced
                    ? "Recovery state advanced in time but did not demonstrate authoritative motion."
                    : "Recovery simulation time did not advance before the liveness timeout.";
                return FlightLessonRecoveryLivenessStatus.Failed;
            }

            return FlightLessonRecoveryLivenessStatus.Pending;
        }
    }

    public sealed class FlightLessonRecoveryStateMachine
    {
        private FlightLessonRecoveryPhase phase;
        private FlightLessonRecoveryRequest request;
        private FlightLessonRecoveryTimings timings;
        private float elapsed;
        private bool emittedResetRequest;

        public FlightLessonRecoveryPhase Phase => phase;
        public bool IsActive => phase != FlightLessonRecoveryPhase.Idle && phase != FlightLessonRecoveryPhase.Failed;

        public void Cancel() { phase = FlightLessonRecoveryPhase.Idle; elapsed = 0f; emittedResetRequest = false; request = default; }

        public bool TryBegin(
            FlightLessonCrashReason reason,
            FlightLessonRouteDefinition route,
            in FlightLessonProgressState progress,
            in FlightLessonRecoveryTimings requestedTimings)
        {
            if (reason == FlightLessonCrashReason.None || IsActive || route?.recoveryCheckpoints == null ||
                route.recoveryCheckpoints.Length == 0)
            {
                return false;
            }

            int checkpointIndex = FlightLessonRecoverySelector.SelectCheckpointIndex(route, progress);
            if (checkpointIndex < 0) return false;
            request = new FlightLessonRecoveryRequest(reason, checkpointIndex, route.recoveryCheckpoints[checkpointIndex]);
            timings = Sanitize(requestedTimings);
            phase = FlightLessonRecoveryPhase.FadingOut;
            elapsed = 0f;
            emittedResetRequest = false;
            return true;
        }

        public FlightLessonRecoveryOutput Step(float deltaTimeSeconds)
        {
            float dt = Mathf.Max(0f, deltaTimeSeconds);
            bool cancelOwnership = false;
            bool requestReset = false;

            switch (phase)
            {
                case FlightLessonRecoveryPhase.Idle:
                    return Output(0f, false, false, false);

                case FlightLessonRecoveryPhase.FadingOut:
                    elapsed += dt;
                    float fadeOut = Mathf.Clamp01(elapsed / timings.fadeOutSeconds);
                    if (elapsed >= timings.fadeOutSeconds)
                    {
                        phase = FlightLessonRecoveryPhase.AwaitingAuthoritativeReset;
                        elapsed = 0f;
                        cancelOwnership = true;
                    }
                    return Output(fadeOut, true, cancelOwnership, false);

                case FlightLessonRecoveryPhase.AwaitingAuthoritativeReset:
                    if (!emittedResetRequest)
                    {
                        emittedResetRequest = true;
                        requestReset = true;
                    }
                    return Output(1f, true, false, requestReset);

                case FlightLessonRecoveryPhase.Stabilizing:
                    elapsed += dt;
                    if (elapsed >= timings.stabilizationSeconds)
                    {
                        phase = FlightLessonRecoveryPhase.FadingIn;
                        elapsed = 0f;
                    }
                    return Output(1f, true, false, false);

                case FlightLessonRecoveryPhase.FadingIn:
                    elapsed += dt;
                    float fadeIn = 1f - Mathf.Clamp01(elapsed / timings.fadeInSeconds);
                    if (elapsed >= timings.fadeInSeconds)
                    {
                        phase = FlightLessonRecoveryPhase.Idle;
                        elapsed = 0f;
                        fadeIn = 0f;
                    }
                    return Output(fadeIn, phase != FlightLessonRecoveryPhase.Idle, false, false);

                default:
                    return Output(1f, true, false, false);
            }
        }

        public void AcknowledgeAuthoritativeReset(bool succeeded)
        {
            if (phase != FlightLessonRecoveryPhase.AwaitingAuthoritativeReset) return;
            phase = succeeded ? FlightLessonRecoveryPhase.Stabilizing : FlightLessonRecoveryPhase.Failed;
            elapsed = 0f;
        }

        public void ClearFailure()
        {
            if (phase == FlightLessonRecoveryPhase.Failed) phase = FlightLessonRecoveryPhase.Idle;
        }

        private FlightLessonRecoveryOutput Output(float fade, bool suppress, bool cancel, bool reset)
        {
            return new FlightLessonRecoveryOutput(phase, fade, suppress, cancel, reset, request);
        }

        private static FlightLessonRecoveryTimings Sanitize(FlightLessonRecoveryTimings value)
        {
            value.fadeOutSeconds = Mathf.Max(0.01f, value.fadeOutSeconds);
            value.stabilizationSeconds = Mathf.Max(0.01f, value.stabilizationSeconds);
            value.fadeInSeconds = Mathf.Max(0.01f, value.fadeInSeconds);
            return value;
        }
    }

    public static class FlightLessonRecoverySelector
    {
        public static int SelectCheckpointIndex(
            FlightLessonRouteDefinition route,
            in FlightLessonProgressState progress)
        {
            if (route?.recoveryCheckpoints == null || route.recoveryCheckpoints.Length == 0) return -1;
            if (progress.latestRecoveryCheckpointIndex >= 0 &&
                progress.latestRecoveryCheckpointIndex < route.recoveryCheckpoints.Length)
            {
                return progress.latestRecoveryCheckpointIndex;
            }

            int best = -1;
            int bestResumeSegment = int.MinValue;
            for (int i = 0; i < route.recoveryCheckpoints.Length; i++)
            {
                int resume = route.recoveryCheckpoints[i].resumeSegmentIndex;
                if (resume <= progress.currentSegmentIndex && resume > bestResumeSegment)
                {
                    best = i;
                    bestResumeSegment = resume;
                }
            }

            return best;
        }
    }
}
