using System;
using QuestFlightLab.Flight.Backends;

namespace QuestFlightLab.Aircraft.CockpitDynamics.SystemsV2
{
    /// <summary>
    /// Explicit availability for the V2 subordinate cockpit-systems snapshot.
    /// An enum member means a channel has a truthful authority contract; it
    /// does not make an unrepresented instrument or aircraft system exist.
    /// </summary>
    [Flags]
    public enum CockpitSystemsAvailabilityV2 : ulong
    {
        None = 0,
        ScenarioClock = 1ul << 0,
        FlightControlCommands = 1ul << 1,
        EngineRpm = 1ul << 2,
        ElectricalControlPositions = 1ul << 3,
        IgnitionAndStarterControl = 1ul << 4,
        PrimerControl = 1ul << 5,
        FuelSelectorControl = 1ul << 6,
        FlapSelectorCommand = 1ul << 7,
        ElectricalBusPower = 1ul << 8,
        StarterMotorPower = 1ul << 9,
        PressureAltitude = 1ul << 10,
        BarometricSetting = 1ul << 11,
        ActualFlapPosition = 1ul << 12,
        BrakeHydraulicPressure = 1ul << 13,
        MagneticVariation = 1ul << 14,
        MagneticHeading = 1ul << 15,
        VacuumSuction = 1ul << 16,
        FuelQuantity = 1ul << 17,
        FuelFlow = 1ul << 18,
        OilPressure = 1ul << 19,
        OilTemperature = 1ul << 20,
        ElectricalIndications = 1ul << 21,
        GpsDepartureStatus = 1ul << 22,
        ComRadios = 1ul << 23,
        Transponder = 1ul << 24
    }

    public enum CockpitIgnitionSwitchPositionV2
    {
        Off = 0,
        Right = 1,
        Left = 2,
        Both = 3,
        Start = 4
    }

    public enum CockpitFuelSelectorPositionV2
    {
        Off = 0,
        Left = 1,
        Both = 2,
        Right = 3
    }

    public enum CockpitTransponderModeV2
    {
        Off = 0,
        Standby = 1,
        On = 2,
        Altitude = 3
    }

    public enum CockpitSystemsCommandKindV2
    {
        None = 0,
        SetBatteryMaster = 1,
        SetAlternatorMaster = 2,
        SetAvionicsMaster = 3,
        SetIgnitionSwitch = 4,
        SetPrimerPosition = 5,
        SetFuelSelector = 6,
        SetFlapSelector = 7
    }

    /// <summary>
    /// Deterministic operational starting state. This is scenario state, not a
    /// persisted user preference. Product reset restores these exact values.
    /// </summary>
    [Serializable]
    public struct CockpitSystemsScenarioV2
    {
        public bool hasAuthoritativeConfiguration;
        public long scenarioUtcUnixSeconds;
        public bool batteryMasterOn;
        public bool alternatorMasterOn;
        public bool avionicsMasterOn;
        public CockpitIgnitionSwitchPositionV2 ignitionSwitchPosition;
        public float primerPositionNormalized;
        public CockpitFuelSelectorPositionV2 fuelSelectorPosition;

        public bool IsValid =>
            hasAuthoritativeConfiguration &&
            scenarioUtcUnixSeconds >= 0L &&
            CockpitSystemsAuthorityV2Math.IsFinite(primerPositionNormalized) &&
            primerPositionNormalized >= 0f &&
            primerPositionNormalized <= 1f &&
            CockpitSystemsAuthorityV2Math.IsIgnitionPosition(
                ignitionSwitchPosition) &&
            CockpitSystemsAuthorityV2Math.IsFuelSelectorPosition(
                fuelSelectorPosition);
    }

    /// <summary>
    /// Fixed development-aircraft parameters used by the deterministic
    /// subordinate systems reducer. They are presentation-grade engineering
    /// approximations, not certified C172 performance data. Keeping them in one
    /// immutable contract prevents presenter-specific invention and preserves
    /// existing serialized scenarios, whose newly added fields deserialize to
    /// zero and therefore select this documented default.
    /// </summary>
    public readonly struct CockpitSystemsPhysicalConfigurationV2
    {
        // NOAA NCEI WMM-2025, KBDU, 2026-07-30: 7.66139 degrees E
        // (reported uncertainty 0.36831 degrees). Frozen for replay parity.
        public const float KbduMagneticVariationEastDegrees = 7.66f;
        public const float StandardBarometricSettingInHg = 29.92f;
        public const float MaximumFlapDegrees = 30f;
        public const float FlapFullTravelSeconds = 10f;
        public const float MaximumBrakePressurePsi = 600f;
        public const float BrakePressureRiseSeconds = 0.18f;
        public const float BrakePressureReleaseSeconds = 0.12f;
        public const float InitialFuelGallonsPerTank = 25f;
        public const float MaximumFuelFlowGallonsPerHour = 13.5f;
        public const float NominalAlternatorBusVolts = 28.2f;
        public const float NominalBatteryBusVolts = 24.5f;
        public const float AmbientOilTemperatureCelsius = 20f;
        public const float NormalOilTemperatureCelsius = 95f;
        public const int KbduCtafKHz = 122725;
        public const int KbduAwosKHz = 118825;
        public const int EmergencyFrequencyKHz = 121500;
        public const ushort VisualFlightRulesTransponderCodeBcd = 0x1200;
        public const float KbduLatitudeDegrees = 40.03936527f;
        public const float KbduLongitudeDegrees = -105.22608958f;
    }

    /// <summary>
    /// Optional external electrical-source truth. The V2 authority never
    /// guesses battery condition or alternator output from throttle or RPM.
    /// When unavailable, powered-bus indications fail closed.
    /// </summary>
    [Serializable]
    public struct CockpitElectricalPowerSourceInputV2
    {
        public bool hasAuthoritativeState;
        public bool batteryCanPowerMainBus;
        public bool alternatorCanPowerMainBus;

        public static CockpitElectricalPowerSourceInputV2 Unavailable => default;
    }

    /// <summary>
    /// Backend-neutral input sampled from the single FlightDynamicsCoordinator.
    /// The explicit authority bit prevents a default all-zero struct from being
    /// mistaken for valid product state.
    /// </summary>
    public struct CockpitSystemsAuthorityInputV2
    {
        public bool hasAuthoritativeFlightState;
        public FlightDynamicsState flightState;
        public FlightDynamicsControlSnapshot flightControls;
        public CockpitElectricalPowerSourceInputV2 electricalPowerSources;
    }

    /// <summary>
    /// Fixed command payload. Fields are a compact tagged union; command
    /// sequence is strictly monotonic so duplicate XR/input events are ignored.
    /// </summary>
    public struct CockpitSystemsCommandV2
    {
        public ulong sequence;
        public CockpitSystemsCommandKindV2 kind;
        public int discreteValue;
        public float normalizedValue;

        public static CockpitSystemsCommandV2 Boolean(
            ulong sequence,
            CockpitSystemsCommandKindV2 kind,
            bool value)
        {
            return new CockpitSystemsCommandV2
            {
                sequence = sequence,
                kind = kind,
                discreteValue = value ? 1 : 0
            };
        }

        public static CockpitSystemsCommandV2 Discrete(
            ulong sequence,
            CockpitSystemsCommandKindV2 kind,
            int value)
        {
            return new CockpitSystemsCommandV2
            {
                sequence = sequence,
                kind = kind,
                discreteValue = value
            };
        }

        public static CockpitSystemsCommandV2 Normalized(
            ulong sequence,
            CockpitSystemsCommandKindV2 kind,
            float value)
        {
            return new CockpitSystemsCommandV2
            {
                sequence = sequence,
                kind = kind,
                normalizedValue = value
            };
        }
    }

    /// <summary>
    /// Allocation-free subordinate state. Derived indications are explicitly
    /// availability-gated and remain subordinate to the single flight
    /// coordinator. Navigation/radio state is intentionally absent.
    /// </summary>
    [Serializable]
    public struct CockpitSystemsSnapshotV2
    {
        public bool isValid;
        public CockpitSystemsAvailabilityV2 availability;
        public double flightSimulationTimeSeconds;
        public double scenarioUtcSecondsSinceUnixEpoch;
        public ulong lastAppliedCommandSequence;

        public float engineRpm;
        public float flapSelectorCommandNormalized;
        public float pressureAltitudeFeet;
        public float barometricSettingInHg;
        public float magneticVariationEastDegrees;
        public float magneticHeadingDegrees;
        public float actualFlapDegrees;
        public float leftBrakePressurePsi;
        public float rightBrakePressurePsi;
        public float vacuumInHg;
        public float leftFuelGallons;
        public float rightFuelGallons;
        public float fuelFlowGallonsPerHour;
        public float oilPressurePsi;
        public float oilTemperatureCelsius;
        public float electricalBusVolts;
        public float alternatorAmps;
        public uint gpsDestinationIdentAscii;
        public float gpsBearingDegrees;
        public float gpsDistanceNauticalMiles;
        public float gpsGroundTrackDegrees;
        public bool gpsGroundTrackAvailable;
        public float gpsAltitudeMslFeet;
        public int com1ActiveKHz;
        public int com1StandbyKHz;
        public int com2ActiveKHz;
        public int com2StandbyKHz;
        public ushort transponderCodeBcd;
        public CockpitTransponderModeV2 transponderMode;

        public bool batteryMasterOn;
        public bool alternatorMasterOn;
        public bool avionicsMasterOn;
        public bool mainBusPowered;
        public bool avionicsBusPowered;

        public CockpitIgnitionSwitchPositionV2 ignitionSwitchPosition;
        public bool starterSwitchEngaged;
        public bool starterMotorPowered;
        public float primerPositionNormalized;
        public CockpitFuelSelectorPositionV2 fuelSelectorPosition;

        public bool Has(CockpitSystemsAvailabilityV2 channels)
        {
            return (availability & channels) == channels;
        }
    }

    public interface ICockpitSystemsStateProviderV2
    {
        bool TryGetCockpitSystemsSnapshotV2(out CockpitSystemsSnapshotV2 snapshot);
    }

    internal static class CockpitSystemsAuthorityV2Math
    {
        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        internal static bool IsIgnitionPosition(
            CockpitIgnitionSwitchPositionV2 value)
        {
            return value >= CockpitIgnitionSwitchPositionV2.Off &&
                   value <= CockpitIgnitionSwitchPositionV2.Start;
        }

        internal static bool IsFuelSelectorPosition(
            CockpitFuelSelectorPositionV2 value)
        {
            return value >= CockpitFuelSelectorPositionV2.Off &&
                   value <= CockpitFuelSelectorPositionV2.Right;
        }

        internal static float Clamp01(float value)
        {
            if (value <= 0f) return 0f;
            return value >= 1f ? 1f : value;
        }
    }
}
