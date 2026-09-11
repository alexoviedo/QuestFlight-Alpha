using QuestFlight.Product;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    /// <summary>
    /// Deterministic, allocation-free visual behavior for one authored raptor.
    /// The encounter is a fixed world-space flight path: the bird is never
    /// dragged by the aircraft or camera. It reads the same ThermalFieldProfile
    /// used by JSBSim and does not create hierarchy, forces, or simulation state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GoldenEagleThermalSoaring : MonoBehaviour
    {
        private const int PathSamples = 64;
        private const float AnimationBlendSeconds = 0.18f;

        public ThermalFieldProfile thermalField;
        public BoulderWindFieldProfile windField;
        public QuestFlightLab.Flight.Backends.FlightDynamicsCoordinator windAuthority;
        public Transform visualRoot;
        public Animator animator;
        [Tooltip("Explicitly authored aircraft root used only to stage the first encounter. No runtime discovery is permitted.")]
        public Transform encounterObserver;
        [Tooltip("Explicitly authored lesson authority. The product encounter is armed on the climb leg and begins only after entering the bounded thermal approach distance.")]
        public ProductDemoPilot encounterRoutePilot;
        [Min(0)] public int encounterRouteSegmentIndex = 2;
        [Min(20f)] public float orbitRadiusMeters = 55f;
        [Min(5f)] public float soaringAltitudeUnityMeters = 578f;
        [Min(5f)] public float airspeedMetersPerSecond = 13.2f;
        [Range(0f, 35f)] public float bankDegrees = 18f;
        [Min(2f)] public float flapIntervalSeconds = 12f;
        [Min(0.1f)] public float flapDurationSeconds = 1.4f;
        [Min(0f)] public float phaseOffsetSeconds = 2.3f;
        [Min(100f)] public float encounterStagingDistanceMeters = 1250f;
        [Min(20f)] public float encounterForwardOffsetMeters = 120f;
        [Min(5f)] public float encounterLateralOffsetMeters = 22f;
        [Min(2f)] public float encounterVerticalSeparationMeters = 8f;
        [Min(5f)] public float encounterGlideSpeedMetersPerSecond = 15f;
        [Tooltip("Legacy serialized field retained for prefab compatibility. World-path duration is derived from path length and airspeed.")]
        [Min(1f)] public float encounterGlideDurationSeconds = 7f;
        [Tooltip("Legacy serialized field retained for prefab compatibility. The world path joins the thermal orbit without a positional blend.")]
        [Min(0.5f)] public float encounterOrbitBlendSeconds = 4f;
        [Tooltip("Normalized horizontal direction of the authored climb-to-reservoir route through the thermal.")]
        public Vector3 encounterApproachDirection = new Vector3(-0.3162278f, 0f, 0.9486833f);

        public float LastSampledUpdraftMetersPerSecond { get; private set; }
        public bool IsFlapping { get; private set; }
        public bool EncounterStaged => _encounterStaged;
        public float EncounterPathLengthMeters => _pathLengthMeters;
        public float EncounterClosestDistanceMeters => _encounterClosestDistanceMeters;

        private readonly Vector3[] _pathPoints = new Vector3[PathSamples + 1];
        private readonly float[] _pathDistances = new float[PathSamples + 1];
        private int _soarStateHash;
        private int _flapStateHash;
        private int _bankLeftStateHash;
        private int _bankRightStateHash;
        private int _transitionStateHash;
        private FlightAnimation _animationState;
        private bool _animationStateInitialized;
        private bool _encounterStaged;
        private float _encounterStartTime;
        private float _pathLengthMeters;
        private float _orbitStartTime;
        private float _orbitStartAngleRadians;
        private float _orbitDirection = 1f;
        private float _forcedFlapEndTime;
        private Vector3 _pathStart;
        private Vector3 _pathControlOne;
        private Vector3 _pathControlTwo;
        private Vector3 _pathEnd;
        private Vector3 _lastForward = Vector3.forward;
        private float _smoothedBankDegrees;
        private float _encounterClosestDistanceMeters = float.PositiveInfinity;
        private bool _encounterCompletionLogged;

        private enum FlightAnimation
        {
            Soar,
            Flap,
            BankLeft,
            BankRight,
            Transition
        }

        private void Awake()
        {
            _soarStateHash = Animator.StringToHash("SoarIdle");
            _flapStateHash = Animator.StringToHash("FlapLoop");
            _bankLeftStateHash = Animator.StringToHash("BankLeft");
            _bankRightStateHash = Animator.StringToHash("BankRight");
            _transitionStateHash = Animator.StringToHash("GlideTransition");
            if (!HasAuthoredReferences(out string failure))
            {
                Debug.LogError("[QuestFlight][GoldenEagle] " + failure, this);
                enabled = false;
                return;
            }
            SetAnimation(FlightAnimation.Soar, true);
        }

        private void Update()
        {
            StageEncounterIfNeeded();

            Vector3 position;
            Vector3 velocity;
            float pathBank;
            if (_encounterStaged && TryEvaluateEncounter(Time.time, out position, out velocity, out pathBank))
            {
                _encounterClosestDistanceMeters = Mathf.Min(
                    _encounterClosestDistanceMeters,
                    Vector3.Distance(position, encounterObserver.position));
            }
            else
            {
                EvaluateOrbit(Time.time, out position, out velocity);
                pathBank = -bankDegrees * _orbitDirection;
                if (_encounterStaged && !_encounterCompletionLogged)
                {
                    Debug.Log(
                        $"[QuestFlight][GoldenEagle] Encounter complete; fixed world path " +
                        $"{_pathLengthMeters:0.0} m; closest visual distance " +
                        $"{_encounterClosestDistanceMeters:0.0} m.",
                        this);
                    _encounterCompletionLogged = true;
                }
            }

            Vector3 wind = windField != null && windAuthority != null
                ? windField.Sample(position, windAuthority.CurrentState.simulationTimeSeconds, windAuthority.WeatherPreset)
                : new Vector3(0, thermalField.SampleUpdraftMetersPerSecond(position), 0);
            // Preserve the accepted calm encounter; crab into horizontal wind along its fixed ground track.
            Vector3 airVelocity = velocity - new Vector3(wind.x, 0, wind.z);
            Vector3 forward = airVelocity.sqrMagnitude > 0.001f
                ? airVelocity.normalized
                : _lastForward;
            _smoothedBankDegrees = Mathf.MoveTowards(
                _smoothedBankDegrees,
                Mathf.Clamp(pathBank, -bankDegrees, bankDegrees),
                55f * Time.deltaTime);
            transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(forward, Vector3.up) *
                Quaternion.AngleAxis(_smoothedBankDegrees, Vector3.forward));
            _lastForward = forward;

            LastSampledUpdraftMetersPerSecond = wind.y;
            UpdateAnimation(Time.time, pathBank);
        }

        private void StageEncounterIfNeeded()
        {
            if (_encounterStaged || encounterObserver == null) return;

            Vector3 center = thermalField.centerUnityMeters;
            Vector3 offset = encounterObserver.position - center;
            offset.y = 0f;
            Vector3 routeDirection = HorizontalNormalized(encounterApproachDirection);
            if (routeDirection.sqrMagnitude < 0.99f) return;
            float alongRoute = Vector3.Dot(offset, routeDirection);
            bool insideStagingEnvelope = alongRoute < 0f &&
                                         offset.sqrMagnitude <=
                                         encounterStagingDistanceMeters * encounterStagingDistanceMeters;
            bool routeGate = encounterRoutePilot != null &&
                             encounterRoutePilot.IsRunning &&
                             encounterRoutePilot.ProgressState.currentSegmentIndex >=
                             encounterRouteSegmentIndex &&
                             insideStagingEnvelope;
            bool proximityFallback = encounterRoutePilot == null &&
                                     insideStagingEnvelope;
            if (!routeGate && !proximityFallback) return;

            Vector3 observerForward = HorizontalNormalized(encounterObserver.forward);
            if (observerForward.sqrMagnitude < 0.99f) observerForward = routeDirection;
            Vector3 observerRight = Vector3.Cross(Vector3.up, observerForward).normalized;
            Vector3 routeRight = Vector3.Cross(Vector3.up, routeDirection).normalized;
            _pathStart = encounterObserver.position +
                         observerForward * encounterForwardOffsetMeters -
                         observerRight * encounterLateralOffsetMeters +
                         Vector3.up * encounterVerticalSeparationMeters;
            _pathControlOne = _pathStart +
                              observerForward * 105f +
                              observerRight * (encounterLateralOffsetMeters * 2.5f) +
                              Vector3.up * 6f;
            _pathEnd = new Vector3(center.x, soaringAltitudeUnityMeters, center.z) -
                       routeRight * orbitRadiusMeters;
            _pathControlTwo = _pathEnd - routeDirection * 180f;
            CompileWorldPath();
            _orbitStartAngleRadians = Mathf.Atan2(
                _pathEnd.z - center.z,
                _pathEnd.x - center.x);
            // The chosen entry radial has a route-aligned clockwise tangent.
            _orbitDirection = -1f;
            _orbitStartTime = _encounterStartTime +
                              _pathLengthMeters / Mathf.Max(encounterGlideSpeedMetersPerSecond, 0.1f);
            _forcedFlapEndTime = _encounterStartTime + 1.65f;
            _encounterStaged = true;
            _lastForward = (_pathPoints[1] - _pathPoints[0]).normalized;
            transform.SetPositionAndRotation(
                _pathStart,
                Quaternion.LookRotation(_lastForward, Vector3.up));
            Debug.Log(
                $"[QuestFlight][GoldenEagle] Encounter staged as a fixed world path at " +
                $"{_pathStart}; aircraft {encounterObserver.position}; " +
                $"length {_pathLengthMeters:0.0} m; airspeed " +
                $"{encounterGlideSpeedMetersPerSecond:0.0} m/s; " +
                $"source {(routeGate ? "route" : "proximity")}.",
                this);
        }

        private void CompileWorldPath()
        {
            _pathPoints[0] = _pathStart;
            _pathDistances[0] = 0f;
            for (int index = 1; index <= PathSamples; index++)
            {
                float t = index / (float)PathSamples;
                _pathPoints[index] = EvaluateCubicBezier(
                    _pathStart, _pathControlOne, _pathControlTwo, _pathEnd, t);
                _pathDistances[index] = _pathDistances[index - 1] +
                                        Vector3.Distance(_pathPoints[index - 1], _pathPoints[index]);
            }
            _pathLengthMeters = _pathDistances[PathSamples];
            _encounterStartTime = Time.time;
        }

        private bool TryEvaluateEncounter(
            float time,
            out Vector3 position,
            out Vector3 velocity,
            out float rollDegrees)
        {
            float distance = Mathf.Max(0f, time - _encounterStartTime) *
                             encounterGlideSpeedMetersPerSecond;
            if (distance >= _pathLengthMeters)
            {
                position = _pathEnd;
                velocity = EvaluateCubicBezierTangent(
                    _pathStart, _pathControlOne, _pathControlTwo, _pathEnd, 1f).normalized *
                           encounterGlideSpeedMetersPerSecond;
                rollDegrees = 0f;
                return false;
            }

            int upper = 1;
            while (upper < PathSamples && _pathDistances[upper] < distance) upper++;
            int lower = upper - 1;
            float segmentLength = Mathf.Max(
                _pathDistances[upper] - _pathDistances[lower], 0.0001f);
            float segmentT = (distance - _pathDistances[lower]) / segmentLength;
            position = Vector3.LerpUnclamped(_pathPoints[lower], _pathPoints[upper], segmentT);
            Vector3 direction = (_pathPoints[upper] - _pathPoints[lower]).normalized;
            velocity = direction * encounterGlideSpeedMetersPerSecond;

            int next = Mathf.Min(upper + 1, PathSamples);
            Vector3 nextDirection = (_pathPoints[next] - _pathPoints[upper]).normalized;
            float signedTurn = Vector3.SignedAngle(direction, nextDirection, Vector3.up);
            rollDegrees = Mathf.Clamp(-signedTurn * 6f, -bankDegrees, bankDegrees);
            return true;
        }

        private void EvaluateOrbit(float time, out Vector3 position, out Vector3 velocity)
        {
            float startTime = _encounterStaged ? _orbitStartTime : -phaseOffsetSeconds;
            float startAngle = _encounterStaged ? _orbitStartAngleRadians : 0f;
            float angularSpeed = airspeedMetersPerSecond / Mathf.Max(orbitRadiusMeters, 1f);
            float angle = startAngle + (time - startTime) * angularSpeed * _orbitDirection;
            float sine = Mathf.Sin(angle);
            float cosine = Mathf.Cos(angle);
            Vector3 center = thermalField.centerUnityMeters;
            position = new Vector3(
                center.x + cosine * orbitRadiusMeters,
                soaringAltitudeUnityMeters,
                center.z + sine * orbitRadiusMeters);
            velocity = new Vector3(-sine, 0f, cosine) *
                       (airspeedMetersPerSecond * _orbitDirection);
        }

        private void UpdateAnimation(float time, float pathBank)
        {
            float elapsed = time + phaseOffsetSeconds;
            float cycle = Mathf.Max(flapIntervalSeconds, flapDurationSeconds + 0.1f);
            IsFlapping = time < _forcedFlapEndTime ||
                         Mathf.Repeat(elapsed, cycle) < flapDurationSeconds ||
                         LastSampledUpdraftMetersPerSecond <
                         thermalField.peakUpdraftMetersPerSecond * 0.22f;
            FlightAnimation requested;
            bool transitionComplete = _animationState == FlightAnimation.Transition &&
                                      animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.96f;
            if (_animationState == FlightAnimation.Transition && !transitionComplete)
                requested = FlightAnimation.Transition;
            else if (IsFlapping)
                requested = FlightAnimation.Flap;
            else if (_animationState == FlightAnimation.Flap)
                requested = FlightAnimation.Transition;
            else if (pathBank < -5f)
                requested = FlightAnimation.BankLeft;
            else if (pathBank > 5f)
                requested = FlightAnimation.BankRight;
            else
                requested = FlightAnimation.Soar;

            SetAnimation(requested, false);
        }

        private void SetAnimation(FlightAnimation animation, bool force)
        {
            if (!force && _animationStateInitialized && animation == _animationState) return;
            int hash;
            float speed;
            switch (animation)
            {
                case FlightAnimation.Flap:
                    hash = _flapStateHash;
                    speed = 0.88f;
                    break;
                case FlightAnimation.BankLeft:
                    hash = _bankLeftStateHash;
                    speed = 1f;
                    break;
                case FlightAnimation.BankRight:
                    hash = _bankRightStateHash;
                    speed = 1f;
                    break;
                case FlightAnimation.Transition:
                    hash = _transitionStateHash;
                    speed = 0.85f;
                    break;
                default:
                    hash = _soarStateHash;
                    speed = 1f;
                    break;
            }
            if (force)
                animator.Play(hash, 0, 0f);
            else
                animator.CrossFadeInFixedTime(hash, AnimationBlendSeconds, 0, 0f);
            animator.speed = speed;
            _animationState = animation;
            _animationStateInitialized = true;
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

        public static Vector3 EvaluateCubicBezier(
            Vector3 start,
            Vector3 controlOne,
            Vector3 controlTwo,
            Vector3 end,
            float t)
        {
            float clamped = Mathf.Clamp01(t);
            float inverse = 1f - clamped;
            return inverse * inverse * inverse * start +
                   3f * inverse * inverse * clamped * controlOne +
                   3f * inverse * clamped * clamped * controlTwo +
                   clamped * clamped * clamped * end;
        }

        public static Vector3 EvaluateCubicBezierTangent(
            Vector3 start,
            Vector3 controlOne,
            Vector3 controlTwo,
            Vector3 end,
            float t)
        {
            float clamped = Mathf.Clamp01(t);
            float inverse = 1f - clamped;
            return 3f * inverse * inverse * (controlOne - start) +
                   6f * inverse * clamped * (controlTwo - controlOne) +
                   3f * clamped * clamped * (end - controlTwo);
        }

        private static Vector3 HorizontalNormalized(Vector3 value)
        {
            value.y = 0f;
            return value.sqrMagnitude > 0.0001f ? value.normalized : Vector3.zero;
        }
    }
}
