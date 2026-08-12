using UnityEngine;

namespace QuestFlightLab.Audio
{
    [CreateAssetMenu(menuName = "QuestFlight/Audio/C172 Audio Profile")]
    public sealed class ProductionC172AudioProfile : ScriptableObject
    {
        [Header("Recorded engine layers")]
        public AudioClip engineStart;
        public AudioClip engineIdleLoop;
        public AudioClip engineCruiseLoop;
        public AudioClip propellerHighLoop;

        [Header("Airframe and cockpit cues")]
        public AudioClip windLowLoop;
        public AudioClip windHighLoop;
        public AudioClip groundRollLoop;
        public AudioClip flapMotorLoop;
        public AudioClip stallHornLoop;
        public AudioClip switchClick;
        public AudioClip touchdown;

        [Header("Calibrated C172 envelope")]
        [Min(400f)] public float idleRpm = 650f;
        [Min(1800f)] public float maximumRpm = 2700f;
        [Min(20f)] public float windFullScaleKnots = 125f;
        [Min(1f)] public float groundRollFullScaleKnots = 55f;
        [Min(1f)] public float stallHornStartAoADegrees = 11.5f;
        [Min(1f)] public float stallHornFullAoADegrees = 16f;

        public bool HasAllRequiredClips =>
            engineStart != null && engineIdleLoop != null &&
            engineCruiseLoop != null && propellerHighLoop != null &&
            windLowLoop != null && windHighLoop != null &&
            groundRollLoop != null && flapMotorLoop != null &&
            stallHornLoop != null && switchClick != null && touchdown != null;
    }
}
