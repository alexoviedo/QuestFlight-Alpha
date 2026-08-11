using UnityEngine;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Deterministic, allocation-free visual behavior for one authored raptor.
    /// It reads the same ThermalFieldProfile used by JSBSim; it does not create
    /// hierarchy, forces, or simulation state at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GoldenEagleThermalSoaring : MonoBehaviour
    {
        public ThermalFieldProfile thermalField;
        public Transform visualRoot;
        public Animator animator;
        [Min(20f)] public float orbitRadiusMeters = 105f;
        [Min(5f)] public float soaringAltitudeUnityMeters = 515f;
        [Min(5f)] public float airspeedMetersPerSecond = 16f;
        [Range(0f, 35f)] public float bankDegrees = 18f;
        [Range(0f, 1f)] public float glidePoseNormalizedTime = 0.25f;
        [Min(2f)] public float flapIntervalSeconds = 12f;
        [Min(0.1f)] public float flapDurationSeconds = 1.4f;
        [Min(0f)] public float phaseOffsetSeconds = 2.3f;

        public float LastSampledUpdraftMetersPerSecond { get; private set; }
        public bool IsFlapping { get; private set; }

        private int _flyStateHash;
        private bool _animationStateInitialized;
        private bool _lastFlapping;

        private void Awake()
        {
            _flyStateHash = Animator.StringToHash("Fly");
            if (!HasAuthoredReferences(out string failure))
            {
                Debug.LogError("[QuestFlight][GoldenEagle] " + failure, this);
                enabled = false;
                return;
            }
            SetAnimationMode(false, true);
        }

        private void Update()
        {
            float elapsed = Time.time + phaseOffsetSeconds;
            float angularSpeed = airspeedMetersPerSecond / Mathf.Max(orbitRadiusMeters, 1f);
            float angle = elapsed * angularSpeed;
            float sine = Mathf.Sin(angle);
            float cosine = Mathf.Cos(angle);
            Vector3 center = thermalField.centerUnityMeters;
            Vector3 position = new Vector3(
                center.x + cosine * orbitRadiusMeters,
                soaringAltitudeUnityMeters,
                center.z + sine * orbitRadiusMeters);
            Vector3 tangent = new Vector3(-sine, 0f, cosine);
            Quaternion heading = Quaternion.LookRotation(tangent, Vector3.up);
            Quaternion inwardBank = Quaternion.AngleAxis(-bankDegrees, Vector3.forward);
            transform.SetPositionAndRotation(position, heading * inwardBank);

            LastSampledUpdraftMetersPerSecond = thermalField.SampleUpdraftMetersPerSecond(position);
            float cycle = Mathf.Max(flapIntervalSeconds, flapDurationSeconds + 0.1f);
            IsFlapping = Mathf.Repeat(elapsed, cycle) < flapDurationSeconds ||
                         LastSampledUpdraftMetersPerSecond < thermalField.peakUpdraftMetersPerSecond * 0.35f;
            if (!_animationStateInitialized || IsFlapping != _lastFlapping)
                SetAnimationMode(IsFlapping, false);
        }

        public bool HasAuthoredReferences(out string failure)
        {
            if (thermalField == null || visualRoot == null || animator == null)
            {
                failure = "Thermal field, visual root, and Animator must be serialized.";
                return false;
            }
            if (!thermalField.IsValid(out failure)) return false;
            if (visualRoot != animator.transform)
            {
                failure = "The authored visual root must own the Animator.";
                return false;
            }
            failure = string.Empty;
            return true;
        }

        private void SetAnimationMode(bool flapping, bool force)
        {
            if (!force && _animationStateInitialized && flapping == _lastFlapping) return;
            if (flapping)
            {
                animator.Play(_flyStateHash, 0, 0f);
                animator.speed = 0.85f;
            }
            else
            {
                animator.Play(_flyStateHash, 0, glidePoseNormalizedTime);
                animator.Update(0f);
                animator.speed = 0f;
            }
            _lastFlapping = flapping;
            _animationStateInitialized = true;
        }
    }
}
