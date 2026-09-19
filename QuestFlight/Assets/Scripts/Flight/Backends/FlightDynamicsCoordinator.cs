using System;
using System.Collections;
using AlexOviedo.QuestFlight.JSBSim;
using QuestFlightLab.Environment;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlightLab.Flight.Backends
{
    /// <summary>
    /// Sole product fixed-step authority. Native JSBSim is the only permitted
    /// product backend. A native data, initialization, reset, or step failure
    /// fails closed; it can never switch to the Unity prototype.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlightDynamicsCoordinator : MonoBehaviour
    {
        [Header("Single product authority")]
        public FlightDynamicsBackendKind requestedBackend = FlightDynamicsBackendKind.JSBSimNativeDevelopment;
        public Transform simulationRoot;
        public Transform presentationRoot;
        public AircraftState aircraftState;
        [Tooltip("Authored C172 reference values used by route guidance only; JSBSim owns all simulation state.")]
        public C172StyleAircraftConfig aircraftConfiguration;
        public Rigidbody authoritativeRigidbody;
        public ProductControlInput controls;
        [Tooltip("Offline-authored ENU terrain authority used by physical ground contact. No runtime raycasts or hierarchy discovery.")]
        public ProductionTerrainSurfaceAsset terrainSurface;
        [Tooltip("Shared authored thermal supplied to the native JSBSim air-mass input.")]
        public ThermalFieldProfile thermalField;
        [Tooltip("Required shared atmosphere, independent of flight mode.")]
        public BoulderWindFieldProfile windField;
        public BoulderWeatherPreset WeatherPreset => BoulderWeatherPreset.FoothillSoaring;
        public Vector3 LastWindUnityMetersPerSecond { get; private set; }

        [Header("Authored initial conditions")]
        public FlightDynamicsInitialConditionProvider initialConditionProvider;

        [Header("Fixed step")]
        [Min(30f)] public float simulationRateHz = 120f;
        [Range(1, 16)] public int maxStepsPerFixedUpdate = 8;

        public FlightDynamicsBackendKind ActiveBackendKind =>
            _backend?.Kind ?? FlightDynamicsBackendKind.JSBSimNativeDevelopment;
        public bool IsInitialized => _backend != null && _backend.IsInitialized;
        public bool CoordinatorOwnsPresentationInterpolation => ShouldCoordinatorInterpolate(simulationRoot, presentationRoot);
        public string LastError { get; private set; } = string.Empty;
        public FlightDynamicsState CurrentState => _currentState;
        public FlightDynamicsControlSnapshot LastAppliedControls { get; private set; } =
            FlightDynamicsControlSnapshot.NeutralIdle;
        public FlightDynamicsInitialConditions LastResetInitialConditions { get; private set; }
        public double DroppedSimulationSeconds => _accumulator?.DroppedSeconds ?? 0.0;
        // Read-only observation after each native step; training never becomes a flight authority.
        public event Action<FlightDynamicsState, FlightDynamicsControlSnapshot, float> NativeStepCompleted;

        private IFlightDynamicsBackend _backend;
        private FlightDynamicsAuthorityLease _authorityLease;
        private FixedStepAccumulator _accumulator;
        private FlightDynamicsState _previousState;
        private FlightDynamicsState _currentState;
        private FlightDynamicsInitialConditions _configuredInitialConditions;
        private bool _hasConfiguredInitialConditions;
        private Action<double> _stepBackendAction;
        private string _preparedNativeDataRoot = string.Empty;
        private string _nativePreparationError = string.Empty;
        private static readonly AircraftControlState NeutralIdleControls = AircraftControlState.Neutral(0.10f);

        private void Awake()
        {
            if (!HasAuthoredReferences(out string error))
            {
                LastError = error;
                Debug.LogError("[QuestFlight][FlightAuthority] " + error, this);
                enabled = false;
                return;
            }
            _stepBackendAction = StepBackendUnchecked;
            EnforceSinglePresentationOwner();
        }

        public bool HasAuthoredReferences(out string error)
        {
            if (simulationRoot == null || presentationRoot == null || aircraftState == null ||
                aircraftConfiguration == null || authoritativeRigidbody == null || controls == null ||
                initialConditionProvider == null || terrainSurface == null || thermalField == null)
            {
                error = "All flight-authority references must be serialized in the product prefab.";
                return false;
            }
            if (!thermalField.IsValid(out string thermalFailure))
            {
                error = "The serialized thermal profile is invalid: " + thermalFailure;
                return false;
            }
            error = null;
            if ((windField == null) ||
                (windField != null && (!windField.IsValid(out error) || windField.legacyThermal != thermalField)))
            {
                error = "Invalid shared wind field or duplicate thermal source: " + error;
                return false;
            }
            if (simulationRoot != transform || aircraftState.transform != simulationRoot ||
                authoritativeRigidbody.transform != simulationRoot)
            {
                error = "Flight-authority components must share AircraftSimulationRoot.";
                return false;
            }
            if (requestedBackend != FlightDynamicsBackendKind.JSBSimNativeDevelopment)
            {
                error = "The serialized product coordinator must request native JSBSim.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private IEnumerator Start()
        {
            if (!JSBSimRuntimeData.TryGetPreparedDataRoot(out _preparedNativeDataRoot, out _nativePreparationError))
            {
                bool prepared = false;
                yield return JSBSimRuntimeData.Prepare((success, root, error) =>
                {
                    prepared = success;
                    _preparedNativeDataRoot = root ?? string.Empty;
                    _nativePreparationError = error ?? string.Empty;
                });
                if (!prepared && string.IsNullOrWhiteSpace(_nativePreparationError))
                    _nativePreparationError = "Native JSBSim runtime data preparation failed.";
            }
            InitializeSelectedBackend();
        }

        public bool InitializeSelectedBackend()
        {
            ShutdownBackend();
            LastError = string.Empty;
            RefreshConfiguredInitialConditions();
            if (!FlightDynamicsAuthority.TryAcquire(simulationRoot, this, out _authorityLease, out string authorityError))
                return Fail(authorityError);

            double fixedStep = 1.0 / Mathf.Max(30f, simulationRateHz);
            _accumulator = new FixedStepAccumulator(fixedStep, maxStepsPerFixedUpdate);
            FlightDynamicsBackendContext context = CreateContext(fixedStep);
            if (string.IsNullOrWhiteSpace(_preparedNativeDataRoot))
                JSBSimRuntimeData.TryGetPreparedDataRoot(out _preparedNativeDataRoot, out _nativePreparationError);
            if (string.IsNullOrWhiteSpace(_preparedNativeDataRoot))
            {
                return ReleaseAndFail("Native JSBSim data preparation failed: " +
                                      _nativePreparationError);
            }
            IFlightDynamicsBackend native = new JSBSimNativeDevelopmentBackend(_preparedNativeDataRoot);
            if (!TryInitializeBackend(native, context, _configuredInitialConditions, out string nativeError))
            {
                native.Dispose();
                return ReleaseAndFail("Native JSBSim initialization failed: " + nativeError);
            }
            CompleteInitialization(fixedStep);
            return true;
        }

        private void FixedUpdate()
        {
            if (!IsInitialized) return;
            if (controls != null && controls.ConsumeResetRequest())
            {
                ResetToConfiguredInitialConditions();
                return;
            }
            if (controls != null && controls.Paused) return;
            ApplyControls(controls != null ? controls.Current : NeutralIdleControls);
            _accumulator.Consume(Time.fixedDeltaTime, _stepBackendAction);
        }

        private void LateUpdate()
        {
            EnforceSinglePresentationOwner();
            if (!IsInitialized || !CoordinatorOwnsPresentationInterpolation) return;
            float alpha = (float)_accumulator.InterpolationAlpha;
            presentationRoot.SetPositionAndRotation(
                Vector3.LerpUnclamped(_previousState.positionUnityMeters, _currentState.positionUnityMeters, alpha),
                Quaternion.SlerpUnclamped(_previousState.rotationUnity, _currentState.rotationUnity, alpha));
        }

        public bool StepForTest(AircraftControlState controlState, double fixedDeltaTimeSeconds)
        {
            if (!IsInitialized) return false;
            ApplyControls(controlState ?? NeutralIdleControls);
            return StepBackend(fixedDeltaTimeSeconds);
        }

        public void RefreshConfiguredInitialConditions()
        {
            _configuredInitialConditions = initialConditionProvider != null
                ? initialConditionProvider.Build(GeodeticReference.Kbdu)
                : FlightDynamicsInitialConditions.KbduRunway();
            _hasConfiguredInitialConditions = true;
        }

        public bool ResetToConfiguredInitialConditions()
        {
            if (!IsInitialized) return Fail("Cannot reset flight dynamics before backend initialization.");
            if (!_hasConfiguredInitialConditions) RefreshConfiguredInitialConditions();
            return ResetToInitialConditions(_configuredInitialConditions);
        }

        /// <summary>
        /// Applies a caller-provided recovery state through the one active backend and the
        /// existing authority lease. This is the only supported in-flight recovery seam;
        /// callers must not move the simulation root, presentation root, XR Origin, or camera.
        /// </summary>
        public bool TryResetToInitialConditions(in FlightDynamicsInitialConditions initialConditions)
        {
            if (!IsInitialized) return Fail("Cannot reset flight dynamics before backend initialization.");
            if (!InitialConditionsAreFinite(initialConditions))
                return Fail("Recovery initial conditions contain a non-finite value.");
            return ResetToInitialConditions(initialConditions);
        }

        /// <summary>
        /// Recovery-only reset seam. A prior backend step failure disables this
        /// component to fail closed; an accepted authored checkpoint may revive
        /// that same initialized authority after its backend successfully resets.
        /// No backend or product hierarchy is constructed here.
        /// </summary>
        public bool TryRecoverToInitialConditions(
            in FlightDynamicsInitialConditions initialConditions)
        {
            if (!TryResetToInitialConditions(initialConditions))
                return false;
            enabled = true;
            return true;
        }

        private bool ResetToInitialConditions(in FlightDynamicsInitialConditions initialConditions)
        {
            if (!SetInitialAtmosphere(_backend, initialConditions, out string windError)) return Fail(windError);
            if (!_backend.Reset(initialConditions))
                return Fail(_backend.LastError);
            LastWindUnityMetersPerSecond = windField.Sample(_backend.CurrentState.positionUnityMeters, _backend.CurrentState.simulationTimeSeconds);
            if (!BoulderWindFieldProfile.Finite(LastWindUnityMetersPerSecond) || LastWindUnityMetersPerSecond.sqrMagnitude > 900)
                return Fail("Invalid reset atmosphere.");
            _backend.SetAtmosphere(AtmosphereFromWind(LastWindUnityMetersPerSecond));
            LastResetInitialConditions = initialConditions;
            ApplyControls(NeutralIdleControls);
            _currentState = _backend.CurrentState;
            _previousState = _currentState;
            _accumulator?.Reset();
            ApplyBackendPose(_currentState);
            ApplyLegacyState(_currentState);
            LastError = string.Empty;
            return true;
        }

        private static bool InitialConditionsAreFinite(in FlightDynamicsInitialConditions value)
        {
            return IsFinite(value.latitudeDegrees) &&
                   IsFinite(value.longitudeDegrees) &&
                   IsFinite(value.altitudeMslMeters) &&
                   IsFinite(value.terrainElevationMslMeters) &&
                   IsFinite(value.calibratedAirspeedKnots) &&
                   IsFinite(value.headingDegrees) &&
                   IsFinite(value.pitchDegrees) &&
                   IsFinite(value.bankDegrees) &&
                   IsFinite(value.flightPathAngleDegrees) &&
                   value.calibratedAirspeedKnots >= 0d &&
                   value.altitudeMslMeters >= value.terrainElevationMslMeters;
        }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private bool SetInitialAtmosphere(IFlightDynamicsBackend backend, in FlightDynamicsInitialConditions initial, out string error)
        {
            error = null;
            if (windField == null) { error = "Missing shared atmosphere."; return false; }
            Vector3 at = FlightFrameConversions.GeodeticToUnity(initial.latitudeDegrees, initial.longitudeDegrees,
                initial.altitudeMslMeters, GeodeticReference.Kbdu);
            Vector3 wind = windField.Sample(at, 0);
            if (!BoulderWindFieldProfile.Finite(wind) || wind.sqrMagnitude > 900)
            { error = "Invalid initial shared atmosphere."; return false; }
            backend.SetAtmosphere(AtmosphereFromWind(wind));
            return true;
        }

        private bool TryInitializeBackend(
            IFlightDynamicsBackend candidate,
            FlightDynamicsBackendContext context,
            FlightDynamicsInitialConditions initialConditions,
            out string error)
        {
            if (!candidate.Initialize(context))
            {
                error = candidate.LastError;
                return false;
            }
            if (!SetInitialAtmosphere(candidate, initialConditions, out error)) return false;
            if (!candidate.Reset(initialConditions))
            {
                error = candidate.LastError;
                return false;
            }
            Vector3 initialWind = windField.Sample(candidate.CurrentState.positionUnityMeters, candidate.CurrentState.simulationTimeSeconds);
            if (!BoulderWindFieldProfile.Finite(initialWind) || initialWind.sqrMagnitude > 900)
            { error = "Invalid initial shared atmosphere."; return false; }
            candidate.SetAtmosphere(AtmosphereFromWind(initialWind));
            LastWindUnityMetersPerSecond = initialWind;
            candidate.SetControls(NeutralIdleControls);
            _backend = candidate;
            LastResetInitialConditions = initialConditions;
            LastAppliedControls = FlightDynamicsControlSnapshot.From(NeutralIdleControls);
            _currentState = candidate.CurrentState;
            _previousState = _currentState;
            ApplyBackendPose(_currentState);
            ApplyLegacyState(_currentState);
            error = string.Empty;
            return true;
        }

        private void CompleteInitialization(double fixedStep)
        {
            LastError = string.Empty;
            Debug.Log($"[QuestFlight][FlightAuthority] {_backend.DisplayName}; " +
                      $"fixed step={fixedStep * 1000.0:0.###} ms; " +
                      $"release request={requestedBackend}.");
        }

        private FlightDynamicsBackendContext CreateContext(double fixedStep)
        {
            return new FlightDynamicsBackendContext
            {
                simulationRoot = simulationRoot,
                aircraftState = aircraftState,
                terrainSurface = terrainSurface,
                localOrigin = GeodeticReference.Kbdu,
                fixedDeltaTimeSeconds = fixedStep
            };
        }

        private void ApplyControls(AircraftControlState state)
        {
            LastAppliedControls = FlightDynamicsControlSnapshot.From(state);
            _backend.SetControls(state);
        }

        private bool StepBackend(double fixedDeltaTimeSeconds)
        {
            if (_backend == null || !_backend.IsInitialized) return false;
            _previousState = _currentState;
            LastWindUnityMetersPerSecond = windField.Sample(_currentState.positionUnityMeters, _currentState.simulationTimeSeconds);
            if (!BoulderWindFieldProfile.Finite(LastWindUnityMetersPerSecond) ||
                LastWindUnityMetersPerSecond.sqrMagnitude > 900)
                return Fail("Shared wind field returned invalid or out-of-envelope air velocity.");
            _backend.SetAtmosphere(AtmosphereFromWind(LastWindUnityMetersPerSecond));
            if (!_backend.Advance(fixedDeltaTimeSeconds))
                return Fail(_backend.LastError);
            _currentState = _backend.CurrentState;
            ApplyBackendPose(_currentState);
            ApplyLegacyState(_currentState);
            NativeStepCompleted?.Invoke(_currentState, LastAppliedControls, (float)fixedDeltaTimeSeconds);
            return true;
        }

        public static FlightDynamicsAtmosphere AtmosphereFromUpdraft(float updraftMetersPerSecond)
        {
            return new FlightDynamicsAtmosphere
            {
                windNorthMetersPerSecond = 0d,
                windEastMetersPerSecond = 0d,
                // Aviation NED uses positive down; a rising air mass is negative down.
                windDownMetersPerSecond = -Math.Max(0d, updraftMetersPerSecond)
            };
        }

        public static FlightDynamicsAtmosphere AtmosphereFromWind(Vector3 enuWind) => new FlightDynamicsAtmosphere
        {
            windNorthMetersPerSecond = enuWind.z,
            windEastMetersPerSecond = enuWind.x,
            windDownMetersPerSecond = -enuWind.y
        };

        private void StepBackendUnchecked(double fixedDeltaTimeSeconds)
        {
            if (!IsInitialized) return;
            if (!StepBackend(fixedDeltaTimeSeconds)) enabled = false;
        }

        private void ApplyBackendPose(FlightDynamicsState state)
        {
            if (simulationRoot == null) return;
            simulationRoot.SetPositionAndRotation(state.positionUnityMeters, state.rotationUnity);
        }

        private void ApplyLegacyState(FlightDynamicsState state)
        {
            if (aircraftState == null) return;
            aircraftState.velocityWorld = state.velocityUnityMetersPerSecond;
            aircraftState.angularVelocityDeg = state.angularVelocityBodyDegreesPerSecond;
            aircraftState.airspeedKts = (float)state.calibratedAirspeedKnots;
            aircraftState.altitudeFt = (float)(state.altitudeAglMeters * FlightFrameConversions.MetersToFeet);
            aircraftState.verticalSpeedFpm = (float)state.verticalSpeedFeetPerMinute;
            aircraftState.headingDeg = (float)state.headingDegrees;
            aircraftState.pitchDeg = (float)state.pitchDegrees;
            aircraftState.bankDeg = (float)state.bankDegrees;
            aircraftState.angleOfAttackDeg = (float)state.angleOfAttackDegrees;
            aircraftState.slipSkid = (float)state.sideslipDegrees;
            aircraftState.engineRpm = (float)state.engineRpm;
            aircraftState.loadFactorG = (float)state.loadFactorG;
            aircraftState.onGround = state.weightOnWheels;
        }

        public static bool ShouldCoordinatorInterpolate(Transform candidateSimulationRoot, Transform candidatePresentationRoot)
        {
            if (candidateSimulationRoot == null || candidatePresentationRoot == null ||
                candidatePresentationRoot == candidateSimulationRoot) return false;
            AircraftReferenceFrameRig rig = candidateSimulationRoot.GetComponent<AircraftReferenceFrameRig>();
            return rig == null || !rig.enabled;
        }

        private void EnforceSinglePresentationOwner()
        {
            if (simulationRoot == null) return;
            AircraftReferenceFrameRig rig = simulationRoot.GetComponent<AircraftReferenceFrameRig>();
            if (rig != null && rig.enabled) presentationRoot = simulationRoot;
        }

        private bool ReleaseAndFail(string message)
        {
            _backend?.Dispose();
            _backend = null;
            _authorityLease?.Dispose();
            _authorityLease = null;
            return Fail(message);
        }

        private bool Fail(string message)
        {
            LastError = string.IsNullOrWhiteSpace(message) ? "Flight dynamics backend failed." : message;
            Debug.LogError("[QuestFlight][FlightAuthority] " + LastError, this);
            return false;
        }

        private void OnDestroy()
        {
            ShutdownBackend();
        }

        private void ShutdownBackend()
        {
            _backend?.Dispose();
            _backend = null;
            LastAppliedControls = FlightDynamicsControlSnapshot.NeutralIdle;
            _authorityLease?.Dispose();
            _authorityLease = null;
        }
    }
}
