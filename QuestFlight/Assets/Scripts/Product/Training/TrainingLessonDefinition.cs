using System;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlight.Product.Training
{
    public enum TrainingTask
    {
        RollControl, PitchControl, RudderControl, BrakeControl, Taxi, Stop,
        Level, Turn, Climb, Descend, Trim, TakeoffConfiguration, TakeoffRoll,
        Rotation, Departure, Downwind, Abeam, Base, Final, Landing, Rollout,
        GoAroundPower, GoAroundClimb, GoAroundClean
    }

    [Serializable]
    public sealed class TrainingStage
    {
        public string title;
        [TextArea] public string instruction;
        public AudioClip narration;
        [TextArea] public string successFeedback;
        [TextArea] public string retryAdvice;
        public TrainingTask task;
        public TrainingReference reference;
        public bool attitudeOnly;
        public float heading = 90;
        public float altitudeFeet = 7000;
        public float speedKnots = 90;
        public float dwellSeconds = 6;
        public float timeoutSeconds = 180;
        public Vector2 targetEnu;
        public float captureMeters = 200;
    }

    /// <summary>Offline-authored curriculum; contains no aircraft or XR hierarchy.</summary>
    [CreateAssetMenu(menuName = "QuestFlight/Training Lesson")]
    public sealed class TrainingLessonDefinition : ScriptableObject
    {
        public string lessonId;
        public string displayName;
        [TextArea] public string briefing;
        public AudioClip briefingNarration;
        [TextArea] public string learningGoal;
        [TextArea] public string reflection;
        public FlightDynamicsInitialConditions start;
        public float initialThrottle = .62f;
        public float initialTrim;
        public float initialFlaps;
        public bool allowDemonstration;
        public bool airportExercise;
        public int goAroundStage = -1;
        public TrainingStage[] stages = Array.Empty<TrainingStage>();
        public TrainingReference[] orientation = Array.Empty<TrainingReference>();

        public bool IsValid => !string.IsNullOrWhiteSpace(lessonId) &&
            !string.IsNullOrWhiteSpace(displayName) && stages != null && stages.Length > 0 &&
            Array.TrueForAll(stages, s => s != null && !string.IsNullOrWhiteSpace(s.instruction) &&
                s.dwellSeconds > 0 && s.timeoutSeconds > s.dwellSeconds) &&
            (goAroundStage == -1 || goAroundStage > 0 && goAroundStage < stages.Length);
    }
}
