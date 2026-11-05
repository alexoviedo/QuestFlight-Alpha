using System;
using System.Collections;
using AlexOviedo.QuestFlight.JSBSim;
using QuestFlightLab.Environment;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using QuestFlightLab.Flight.Soaring;
using UnityEngine;

namespace QuestFlightLab.Flight.Backends
{
    /// <summary>
    /// Sole product fixed-step authority. Native JSBSim is the only permitted
    /// product backend. A native data, initialization, reset, or step failure
    /// fails closed; it can never switch to the Unity prototype.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class FlightDynamicsCoordinator : MonoBehaviour
    {
        [Header("Single product authority")]
        public FlightDynamicsBackendKind requestedBackend = FlightDynamicsBackendKind.JSBSimNativeDevelopment;
        public Transform simulationRoot;
        public Transform presentationRoot;
        public AircraftState aircraftState;
        [Tooltip("Authored C172 reference values used by route guidance only; JSBSim owns all simulation state.")]
        public C172StyleAircraftConfig aircraftConfiguration;
        public FlightAircraftDefinition aircraftDefinition;
        public FlightAircraftDefinition tugDefinition;
        public QuestFlightLab.Flight.Soaring.TowlineDefinition towlineDefinition;
        public Vector3 developmentGliderStartEnu=new Vector3(-555,0,-30);
        public QuestFlightLab.Flight.Soaring.AerotowSimulation TowSession { get; private set; }
        public FlightDynamicsState PreviousTugPresentationState { get; private set; }
        public double SimulationBacklogSeconds=>_accumulator?.BacklogSeconds ?? 0;
        private Func<double,bool> _checkedSoaringStep;
        public Rigidbody authoritativeRigidbody;
        public ProductControlInput controls;
        [Tooltip("Offline-authored ENU terrain authority used by physical ground contact. No runtime raycasts or hierarchy discovery.")]
        public ProductionTerrainSurfaceAsset terrainSurface;
        [Tooltip("Shared authored thermal supplied to the native JSBSim air-mass input.")]
        public ThermalFieldProfile thermalField;
        [Tooltip("Required shared atmosphere, independent of flight mode.")]
        public BoulderWindFieldProfile windField;
        public SoaringWeatherDefinition soaringWeatherDefinition;
        public SoaringWeatherDefinition[] soaringWeatherChoices=Array.Empty<SoaringWeatherDefinition>();
        public SoaringAtmosphereSession SoaringAtmosphere { get; private set; }
        public readonly SoaringFlightInstruments SoaringInstruments=new SoaringFlightInstruments();
        public Vector3 SampleWind(Vector3 position,double time)=>SoaringAtmosphere!=null?SoaringAtmosphere.Sample(position,time):windField.Sample(position,time);
        public BoulderWindFieldProfile.Components SampleAtmosphereComponents(Vector3 position,double time)=>SoaringAtmosphere!=null?SoaringAtmosphere.SampleComponents(position,time):windField.SampleComponents(position,time);
        public BoulderWeatherPreset WeatherPreset => BoulderWeatherPreset.FoothillSoaring;
        public Vector3 LastWindUnityMetersPerSecond { get; private set; }

        [Header("Authored initial conditions")]
        public FlightDynamicsInitialConditionProvider initialConditionProvider;

        [Header("Fixed step")]
        [Min(30f)] public float simulationRateHz = 120f;
        [Range(1, 16)] public int maxStepsPerFixedUpdate = 8;

        public FlightDynamicsBackendKind ActiveBackendKind =>
            _backend?.Kind ?? FlightDynamicsBackendKind.JSBSimNativeDevelopment;
        public bool RecoveryHold { get; private set; }
        public void SetRecoveryHold(bool hold) => RecoveryHold = hold;
        public bool IsInitialized => _backend != null && _backend.IsInitialized;
        public string NativePayloadIdentity { get; private set; } = string.Empty;
        public bool TryGetAuthorityReadiness(out string error)
        {
            if (!string.IsNullOrEmpty(LastError)) { error = LastError; return false; }
            if (!isActiveAndEnabled) { error = "Native JSBSim coordinator is inactive or disabled."; return false; }
            if (!IsInitialized || !_backend.IsAvailable)
            { error = "Native JSBSim authority is still preparing or is unavailable."; return false; }
            if (requestedBackend != FlightDynamicsBackendKind.JSBSimNativeDevelopment ||
                _backend.Kind != FlightDynamicsBackendKind.JSBSimNativeDevelopment)
            { error = "The flight authority is not native JSBSim."; return false; }
            if (!string.IsNullOrEmpty(_backend.LastError)) { error = _backend.LastError; return false; }
            if (!_currentState.IsFinite || !_backend.CurrentState.IsFinite)
            { error = "Native JSBSim authority has non-finite state."; return false; }
            error = string.Empty;
            return true;
        }
        public bool CoordinatorOwnsPresentationInterpolation => ShouldCoordinatorInterpolate(simulationRoot, presentationRoot);
        public string LastError { get; private set; } = string.Empty;
        public FlightDynamicsState CurrentState => _currentState;
        public AlexOviedo.QuestFlight.JSBSim.JSBSimNativeDiagnostics NativeDiagnostics =>
            (_backend as JSBSimNativeDevelopmentBackend)?.NativeSession.Diagnostics ?? default;
        internal AlexOviedo.QuestFlight.JSBSim.JSBSimNativeSession NativeSession =>
            (_backend as JSBSimNativeDevelopmentBackend)?.NativeSession;
        public int NativeSessionVersion { get; private set; }
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
        private readonly AircraftControlState _gliderNeutralControls = new AircraftControlState { mixture=0 };
        private AircraftControlState AircraftNeutralControls
        {
            get
            {
                if (aircraftDefinition == null || !aircraftDefinition.IsGlider) return NeutralIdleControls;
                _gliderNeutralControls.trim = aircraftDefinition.referenceTrimNoseUp;
                return _gliderNeutralControls;
            }
        }

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
                aircraftDefinition == null || authoritativeRigidbody == null || controls == null ||
                initialConditionProvider == null || terrainSurface == null || thermalField == null)
            {
                error = "All flight-authority references must be serialized in the product prefab.";
                return false;
            }
            if (!aircraftDefinition.IsValid(out error)) return false;
            if (aircraftDefinition.kind == FlightAircraftKind.C172 && aircraftConfiguration == null)
            { error = "C172 guidance requires the authored C172 configuration."; return false; }
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
            if (!InitializeSelectedBackend()) enabled = false;
        }

        public bool InitializeSelectedBackend()
        {
            ShutdownBackend();
            LastError = string.Empty;
            if (aircraftDefinition == null || !aircraftDefinition.IsValid(out _)) return Fail("A valid authored aircraft definition is required.");
            controls.ConfigureAircraft(aircraftDefinition);
            _checkedSoaringStep=StepBackend;
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
            NativePayloadIdentity = JSBSimRuntimeData.PreparedPayloadIdentity;
            Debug.Log("[QuestFlight][FlightAuthority] Loaded native payload SHA256=" + NativePayloadIdentity);
            return true;
        }

#if UNITY_EDITOR
        // Accelerated native scenarios supply every 120 Hz tick through StepForTest.
        // Prevent a second Editor wall-clock tick with unrelated device input.
        // This switch is absent from Android and is never Quest timing evidence.
        public bool EditorVerificationExternalClock {get;set;}
#endif
        private void FixedUpdate()
        {
#if UNITY_EDITOR
            if(EditorVerificationExternalClock)return;
#endif

            if (!IsInitialized || RecoveryHold || Time.timeScale<=0) return;
            if(TowSession!=null)PreviousTugPresentationState=TowSession.TugState;
            if (controls != null && controls.ConsumeResetRequest())
            {
                ResetToConfiguredInitialConditions();
                return;
            }
            if (controls != null && controls.Paused) return;
            ApplyControls(controls != null ? controls.Current : AircraftNeutralControls);
            if(aircraftDefinition.IsGlider)
            {
                if(!_accumulator.ConsumeChecked(Time.fixedDeltaTime,_checkedSoaringStep,out _))
                {if(_accumulator.CapacityExceeded)Fail("Soaring simulation capacity exceeded; backlog retained and flight paused.");enabled=false;RecoveryHold=true;}
            }
            else _accumulator.Consume(Time.fixedDeltaTime, _stepBackendAction);
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
            if (!IsInitialized || RecoveryHold) return false;
            if(TowSession!=null)PreviousTugPresentationState=TowSession.TugState;
            ApplyControls(controlState ?? AircraftNeutralControls);
            return StepBackend(fixedDeltaTimeSeconds);
        }

        public void RefreshConfiguredInitialConditions()
        {
            _configuredInitialConditions = aircraftDefinition != null && aircraftDefinition.IsGlider
                ? aircraftDefinition.airborneReference : initialConditionProvider != null
                ? initialConditionProvider.Build(GeodeticReference.Kbdu)
                : FlightDynamicsInitialConditions.KbduRunway();
            _hasConfiguredInitialConditions = true;
        }

        public bool TryPrepareAircraft(FlightAircraftDefinition definition, in FlightDynamicsInitialConditions initial)
        {
            if (Time.timeScale > 0 || definition == null || !definition.IsValid(out _))
                return Fail("Aircraft preparation requires a paused menu and a valid authored definition.");
            aircraftDefinition = definition;
            if (!InitializeSelectedBackend()) return false;
            if (!TryStartNewFlight(initial)) return false;
            _configuredInitialConditions = initial;_hasConfiguredInitialConditions = true;
            return true;
        }

        public bool TryPrepareGroundTow(BoulderSoaringOperationDefinition operation=null)
        {
            if(Time.timeScale>0 || !IsInitialized || !aircraftDefinition.IsGlider || tugDefinition==null || towlineDefinition==null || !towlineDefinition.IsValid)
                return Fail("Ground tow requires paused, authored glider/tug/line definitions.");
            var initial=FlightDynamicsInitialConditions.KbduRunway();initial.engineRunning=false;initial.pitchDegrees=0;
            Vector3 launch=operation!=null?operation.Launch:developmentGliderStartEnu;
            Vector3 forward=operation!=null?operation.Forward:Vector3.right;
            if(operation!=null)initial.headingDegrees=operation.trueHeading;
            if(!terrainSurface.TrySample(launch.x,launch.z,out var ground))return Fail("Launch terrain unavailable.");
            var location=FlightFrameConversions.UnityToGeodetic(new Vector3(launch.x,ground.heightMeters+.75f,launch.z),GeodeticReference.Kbdu);
            initial.latitudeDegrees=location.latitudeDegrees;initial.longitudeDegrees=location.longitudeDegrees;initial.altitudeMslMeters=location.altitudeMslMeters;
            initial.terrainElevationMslMeters=GeodeticReference.Kbdu.altitudeMslMeters+ground.heightMeters;
            if(!TryStartNewFlight(initial))return false;
            var tugPosition=launch+forward*(float)(towlineDefinition.naturalLengthMeters+4.6);
            if(!terrainSurface.TrySample(tugPosition.x,tugPosition.z,out ground))return Fail("Tug launch terrain unavailable.");
            location=FlightFrameConversions.UnityToGeodetic(new Vector3(tugPosition.x,ground.heightMeters+1.2f,tugPosition.z),GeodeticReference.Kbdu);
            var tugInitial=initial;tugInitial.engineRunning=true;tugInitial.pitchDegrees=8;
            tugInitial.latitudeDegrees=location.latitudeDegrees;tugInitial.longitudeDegrees=location.longitudeDegrees;tugInitial.altitudeMslMeters=location.altitudeMslMeters;
            tugInitial.terrainElevationMslMeters=GeodeticReference.Kbdu.altitudeMslMeters+ground.heightMeters;
            try {TowSession=new QuestFlightLab.Flight.Soaring.AerotowSimulation(_preparedNativeDataRoot,tugDefinition,towlineDefinition,terrainSurface,tugInitial,120,SoaringAtmosphere);}
            catch(Exception ex){return Fail("Native tug preparation failed: "+ex.Message);}
            if(operation!=null){TowSession.ConfigureOperation(operation);Operation=new SoaringOperationSession(operation,aircraftDefinition.referenceTrimNoseUp);}
            PreviousTugPresentationState=TowSession.TugState;RecoveryHold=false;return true;
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

        /// <summary>A new menu flight or lesson restores configured loading and native histories.</summary>
        public bool TryStartNewFlight(in FlightDynamicsInitialConditions initialConditions)
        {
            if (!TryResetToInitialConditions(initialConditions)) return false;
            enabled = true;
            return true;
        }

        /// <summary>
        /// Recovery-only reset seam. A prior backend step failure disables this
        /// component to fail closed; an accepted authored checkpoint may revive
        /// that same initialized authority after its backend successfully resets.
        /// No backend or product hierarchy is constructed here.
        /// </summary>
        public bool TryRecoverToInitialConditions(
            in FlightDynamicsInitialConditions initialConditions, bool preservePlayerControls = false)
        {
            if (!IsInitialized) return Fail("Cannot relocate before backend initialization.");
            if (!InitialConditionsAreFinite(initialConditions)) return Fail("Recovery initial conditions contain a non-finite value.");
            if (!ResetToInitialConditions(initialConditions, true)) return false;
            enabled = true;
            if (preservePlayerControls && controls != null) ApplyControls(controls.Current);
            return true;
        }

        private bool ResetToInitialConditions(in FlightDynamicsInitialConditions initialConditions, bool preserveOperationalState = false)
        {
            Operation=null;_nextReturnPlan=0;
            TowSession?.Dispose();TowSession=null;
            NativeSession?.ClearExternalLoads();
            SoaringInstruments.Reset();
            if (!SetInitialAtmosphere(_backend, initialConditions, out string windError)) return Fail(windError);
            if (!(preserveOperationalState ? _backend.Relocate(initialConditions) : _backend.Reset(initialConditions)))
                return Fail(_backend.LastError);
            LastWindUnityMetersPerSecond = SampleWind(_backend.CurrentState.positionUnityMeters, _backend.CurrentState.simulationTimeSeconds);
            if (!BoulderWindFieldProfile.Finite(LastWindUnityMetersPerSecond) || LastWindUnityMetersPerSecond.sqrMagnitude > 900)
                return Fail("Invalid reset atmosphere.");
            _backend.SetAtmosphere(AtmosphereFromWind(LastWindUnityMetersPerSecond));
            LastResetInitialConditions = initialConditions;
            NativeSessionVersion++;
            controls?.ClearSoaringTransientInput();
            ApplyControls(AircraftNeutralControls);
            _currentState = _backend.CurrentState;
            _previousState = _currentState;
            if(aircraftDefinition.IsGlider)SoaringInstruments.Sample(NativeSession.AirData,_currentState.simulationTimeSeconds);
            _accumulator?.Reset();
            ApplyBackendPose(_currentState);
            ApplyLegacyState(_currentState);
            simulationRoot.GetComponent<AircraftReferenceFrameRig>()?.ResetPresentationHistory();
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
            Vector3 wind = SampleWind(at, 0);
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
            SoaringAtmosphere=null;SoaringInstruments.Reset();
            if(context.aircraftDefinition.IsGlider)
            {
                try {SoaringAtmosphere=new SoaringAtmosphereSession(windField,soaringWeatherDefinition);}
                catch(Exception ex){error=ex.Message;return false;}
                if(!((JSBSimNativeDevelopmentBackend)candidate).NativeSession.SetConditions(SoaringAtmosphere.Conditions))
                {error=((JSBSimNativeDevelopmentBackend)candidate).NativeSession.LastError;return false;}
            }
            if (!SetInitialAtmosphere(candidate, initialConditions, out error)) return false;
            if (!candidate.Reset(initialConditions))
            {
                error = candidate.LastError;
                return false;
            }
            Vector3 initialWind = SampleWind(candidate.CurrentState.positionUnityMeters, candidate.CurrentState.simulationTimeSeconds);
            if (!BoulderWindFieldProfile.Finite(initialWind) || initialWind.sqrMagnitude > 900)
            { error = "Invalid initial shared atmosphere."; return false; }
            candidate.SetAtmosphere(AtmosphereFromWind(initialWind));
            LastWindUnityMetersPerSecond = initialWind;
            candidate.SetControls(AircraftNeutralControls);
            _backend = candidate;
            LastResetInitialConditions = initialConditions;
            LastAppliedControls = FlightDynamicsControlSnapshot.From(AircraftNeutralControls);
            _currentState = candidate.CurrentState;
            _previousState = _currentState;
            if(aircraftDefinition.IsGlider)SoaringInstruments.Sample(((JSBSimNativeDevelopmentBackend)candidate).NativeSession.AirData,_currentState.simulationTimeSeconds);
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
                aircraftDefinition = aircraftDefinition,
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
            LastWindUnityMetersPerSecond = SampleWind(_currentState.positionUnityMeters, _currentState.simulationTimeSeconds);
            if (!BoulderWindFieldProfile.Finite(LastWindUnityMetersPerSecond) ||
                LastWindUnityMetersPerSecond.sqrMagnitude > 900)
                return Fail("Shared wind field returned invalid or out-of-envelope air velocity.");
            _backend.SetAtmosphere(AtmosphereFromWind(LastWindUnityMetersPerSecond));
            bool release=aircraftDefinition.IsGlider && controls!=null && controls.ConsumeTowRelease();
            if(Operation!=null)
            {
                Operation.ObserveChecks(_currentState,LastAppliedControls,release);
                if(release)Operation.ReconcileConnection(false,false,_currentState);
                if(!UpdateSoaringSafety())return false;
            }
            if(TowSession!=null)
            {
                if(!TowSession.Step((JSBSimNativeDevelopmentBackend)_backend,windField,fixedDeltaTimeSeconds,release))
                    return Fail(TowSession.LastError);
            }
            else
            {
                if(SoaringAtmosphere!=null && aircraftDefinition.differentialAirflow)
                {
                    var incremental=AircraftAirflowSampler.Sample(_currentState,NativeDiagnostics,SoaringAtmosphere,LastWindUnityMetersPerSecond,null,true);
                    if(!AircraftAirflowSampler.Submit(NativeSession,incremental))return Fail(NativeSession.LastError);
                }
                if (!_backend.Advance(fixedDeltaTimeSeconds))return Fail(_backend.LastError);
            }
            _currentState = _backend.CurrentState;
            ObserveSoaringOperation((float)fixedDeltaTimeSeconds);
            if(aircraftDefinition.IsGlider)SoaringInstruments.Sample(NativeSession.AirData,_currentState.simulationTimeSeconds);
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
            if (!IsInitialized || RecoveryHold || !enabled) return;
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
            TowSession?.Dispose();TowSession=null;
            SoaringAtmosphere=null;SoaringInstruments.Reset();
            _backend?.Dispose();
            _backend = null;
            NativePayloadIdentity = string.Empty;
            LastAppliedControls = FlightDynamicsControlSnapshot.NeutralIdle;
            _authorityLease?.Dispose();
            _authorityLease = null;
        }
    }
}
