using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.SystemsV2;
using QuestFlightLab.Flight;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Audio
{
    /// <summary>
    /// Allocation-free, backend-neutral C172 cockpit sound presentation.  All
    /// hierarchy and sources are authored in the aircraft prefab; this class
    /// only maps the authoritative flight/system snapshots into those sources.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProductionC172AudioController : MonoBehaviour
    {
        [SerializeField] private ProductionC172AudioProfile profile;
        [SerializeField] private FlightDynamicsCoordinator coordinator;
        [SerializeField] private ProductionCockpitSystemsController cockpitSystems;
        [SerializeField] private AircraftState legacyAircraftState;

        [Header("Authored continuous sources")]
        [SerializeField] private AudioSource engineIdle;
        [SerializeField] private AudioSource engineCruise;
        [SerializeField] private AudioSource propeller;
        [SerializeField] private AudioSource windLow;
        [SerializeField] private AudioSource windHigh;
        [SerializeField] private AudioSource groundRoll;
        [SerializeField] private AudioSource flapMotor;
        [SerializeField] private AudioSource stallHorn;

        [Header("Authored one-shot sources")]
        [SerializeField] private AudioSource cockpitOneShot;
        [SerializeField] private AudioSource airframeOneShot;

        private bool _initialized;
        private bool _previousWeightOnWheels;
        private bool _previousStarter;
        private bool _previousBattery;
        private bool _previousAlternator;
        private bool _previousAvionics;
        private CockpitIgnitionSwitchPositionV2 _previousIgnition;
        private CockpitFuelSelectorPositionV2 _previousFuelSelector;
        private float _previousFlapDegrees;
        private float _smoothedRpm;
        private float _smoothedAirspeed;
        private float _smoothedGroundSpeed;
        private float _smoothedAoA;

        public ProductionC172AudioProfile Profile => profile;

        public void ConfigureAuthoredReferences(
            ProductionC172AudioProfile authoredProfile,
            FlightDynamicsCoordinator authoredCoordinator,
            ProductionCockpitSystemsController authoredCockpitSystems,
            AircraftState authoredLegacyAircraftState,
            AudioSource authoredEngineIdle,
            AudioSource authoredEngineCruise,
            AudioSource authoredPropeller,
            AudioSource authoredWindLow,
            AudioSource authoredWindHigh,
            AudioSource authoredGroundRoll,
            AudioSource authoredFlapMotor,
            AudioSource authoredStallHorn,
            AudioSource authoredCockpitOneShot,
            AudioSource authoredAirframeOneShot)
        {
            profile = authoredProfile;
            coordinator = authoredCoordinator;
            cockpitSystems = authoredCockpitSystems;
            legacyAircraftState = authoredLegacyAircraftState;
            engineIdle = authoredEngineIdle;
            engineCruise = authoredEngineCruise;
            propeller = authoredPropeller;
            windLow = authoredWindLow;
            windHigh = authoredWindHigh;
            groundRoll = authoredGroundRoll;
            flapMotor = authoredFlapMotor;
            stallHorn = authoredStallHorn;
            cockpitOneShot = authoredCockpitOneShot;
            airframeOneShot = authoredAirframeOneShot;
        }

        public bool HasAuthoredReferences(out string error)
        {
            if (profile == null || !profile.HasAllRequiredClips || coordinator == null ||
                cockpitSystems == null || engineIdle == null || engineCruise == null ||
                propeller == null || windLow == null || windHigh == null ||
                groundRoll == null || flapMotor == null || stallHorn == null ||
                cockpitOneShot == null || airframeOneShot == null)
            {
                error = "C172 audio requires its profile, authority, cockpit systems, and ten distinct authored sources.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void OnEnable()
        {
            StartLoop(engineIdle);
            StartLoop(engineCruise);
            StartLoop(propeller);
            StartLoop(windLow);
            StartLoop(windHigh);
            StartLoop(groundRoll);
            StartLoop(flapMotor);
            StartLoop(stallHorn);
        }

        private void Update()
        {
            if (profile == null || coordinator == null) return;

            FlightDynamicsState state = coordinator.CurrentState;
            if (!state.IsFinite) return;

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            float response = 1f - Mathf.Exp(-5f * dt);
            _smoothedRpm = Mathf.Lerp(_smoothedRpm, (float)state.engineRpm, response);
            _smoothedAirspeed = Mathf.Lerp(_smoothedAirspeed, (float)state.calibratedAirspeedKnots, response);
            _smoothedGroundSpeed = Mathf.Lerp(_smoothedGroundSpeed, (float)state.groundSpeedKnots, response);
            _smoothedAoA = Mathf.Lerp(_smoothedAoA, (float)state.angleOfAttackDegrees, response);

            EngineMix engine = EvaluateEngineMix(_smoothedRpm, profile.idleRpm, profile.maximumRpm);
            Set(engineIdle, engine.idleGain * 0.62f, engine.idlePitch);
            Set(engineCruise, engine.cruiseGain * 0.67f, engine.cruisePitch);
            Set(propeller, engine.propellerGain * 0.30f, engine.propellerPitch);

            float wind = EvaluateWind(_smoothedAirspeed, profile.windFullScaleKnots);
            Set(windLow, Mathf.Sqrt(wind) * 0.27f, Mathf.Lerp(0.86f, 1.12f, wind));
            Set(windHigh, wind * wind * 0.21f, Mathf.Lerp(0.90f, 1.18f, wind));

            float roll = state.weightOnWheels
                ? Mathf.Clamp01(_smoothedGroundSpeed / profile.groundRollFullScaleKnots)
                : 0f;
            Set(groundRoll, roll * 0.24f, Mathf.Lerp(0.82f, 1.18f, roll));

            float stall = EvaluateStallCue(
                state.weightOnWheels,
                _smoothedAoA,
                profile.stallHornStartAoADegrees,
                profile.stallHornFullAoADegrees,
                legacyAircraftState != null && legacyAircraftState.stallWarning,
                legacyAircraftState != null ? legacyAircraftState.stallIntensity : 0f);
            Set(stallHorn, stall * 0.42f, 1f);

            UpdateSystemCues(state, dt);
            _previousWeightOnWheels = state.weightOnWheels;
        }

        private void UpdateSystemCues(in FlightDynamicsState state, float dt)
        {
            if (cockpitSystems == null || !cockpitSystems.HasValidSystemsV2Snapshot) return;
            CockpitSystemsSnapshotV2 systems = cockpitSystems.CurrentSystemsV2Snapshot;

            float flapRate = Mathf.Abs(systems.actualFlapDegrees - _previousFlapDegrees) / Mathf.Max(dt, 0.001f);
            float flapGain = Mathf.SmoothStep(0f, 0.18f, Mathf.Clamp01(flapRate / 2.0f));
            Set(flapMotor, flapGain, 1f);

            if (!_initialized)
            {
                _previousStarter = systems.starterMotorPowered;
                _previousBattery = systems.batteryMasterOn;
                _previousAlternator = systems.alternatorMasterOn;
                _previousAvionics = systems.avionicsMasterOn;
                _previousIgnition = systems.ignitionSwitchPosition;
                _previousFuelSelector = systems.fuelSelectorPosition;
                _previousFlapDegrees = systems.actualFlapDegrees;
                _previousWeightOnWheels = state.weightOnWheels;
                _smoothedRpm = systems.engineRpm;
                _initialized = true;
                return;
            }

            if (systems.starterMotorPowered && !_previousStarter && profile.engineStart != null)
                cockpitOneShot.PlayOneShot(profile.engineStart, 0.55f);

            bool changed =
                systems.batteryMasterOn != _previousBattery ||
                systems.alternatorMasterOn != _previousAlternator ||
                systems.avionicsMasterOn != _previousAvionics ||
                systems.ignitionSwitchPosition != _previousIgnition ||
                systems.fuelSelectorPosition != _previousFuelSelector;
            if (changed && profile.switchClick != null)
                cockpitOneShot.PlayOneShot(profile.switchClick, 0.65f);

            if (!_previousWeightOnWheels && state.weightOnWheels && profile.touchdown != null)
            {
                float impact = Mathf.InverseLerp(40f, 650f, Mathf.Abs((float)state.verticalSpeedFeetPerMinute));
                airframeOneShot.PlayOneShot(profile.touchdown, Mathf.Lerp(0.16f, 0.48f, impact));
            }

            _previousStarter = systems.starterMotorPowered;
            _previousBattery = systems.batteryMasterOn;
            _previousAlternator = systems.alternatorMasterOn;
            _previousAvionics = systems.avionicsMasterOn;
            _previousIgnition = systems.ignitionSwitchPosition;
            _previousFuelSelector = systems.fuelSelectorPosition;
            _previousFlapDegrees = systems.actualFlapDegrees;
        }

        private static void StartLoop(AudioSource source)
        {
            if (source == null || source.clip == null) return;
            source.loop = true;
            if (!source.isPlaying) source.Play();
        }

        private static void Set(AudioSource source, float volume, float pitch)
        {
            if (source == null) return;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = Mathf.Clamp(pitch, 0.5f, 1.6f);
        }

        public static float EvaluateWind(float airspeedKnots, float fullScaleKnots)
        {
            return Mathf.Clamp01(Mathf.Max(0f, airspeedKnots - 12f) / Mathf.Max(1f, fullScaleKnots - 12f));
        }

        public static float EvaluateStallCue(
            bool weightOnWheels,
            float angleOfAttackDegrees,
            float startAoA,
            float fullAoA,
            bool authoritativeWarning,
            float authoritativeIntensity)
        {
            if (weightOnWheels) return 0f;
            float derived = Mathf.InverseLerp(startAoA, Mathf.Max(startAoA + 0.1f, fullAoA), angleOfAttackDegrees);
            return Mathf.Clamp01(Mathf.Max(derived, authoritativeWarning ? Mathf.Max(0.2f, authoritativeIntensity) : 0f));
        }

        public static EngineMix EvaluateEngineMix(float rpm, float idleRpm, float maximumRpm)
        {
            float running = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(120f, idleRpm, rpm));
            float normalized = Mathf.InverseLerp(idleRpm, maximumRpm, rpm);
            float cruise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.72f, normalized));
            float idle = Mathf.Sqrt(Mathf.Clamp01(1f - cruise * 0.88f));
            return new EngineMix
            {
                idleGain = running * idle,
                cruiseGain = running * Mathf.Sqrt(cruise),
                propellerGain = running * Mathf.SmoothStep(0f, 1f, normalized),
                idlePitch = Mathf.Lerp(0.80f, 1.17f, normalized),
                cruisePitch = Mathf.Lerp(0.78f, 1.13f, normalized),
                propellerPitch = Mathf.Lerp(0.74f, 1.10f, normalized)
            };
        }

        public struct EngineMix
        {
            public float idleGain;
            public float cruiseGain;
            public float propellerGain;
            public float idlePitch;
            public float cruisePitch;
            public float propellerPitch;
        }
    }
}
