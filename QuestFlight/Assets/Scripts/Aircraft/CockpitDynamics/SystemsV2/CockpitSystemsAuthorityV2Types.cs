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
        StarterMotorPower = 1ul << 9
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

    public enum CockpitSystemsCommandKindV2
    {
        None = 0,
        SetBatteryMaster = 1,
        SetAlternatorMaster = 2,
        SetAvionicsMaster = 3,
        SetIgnitionSwitch = 4,
        SetPrimerPosition = 5,
        SetFuelSelector = 6
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
    /// Allocation-free subordinate state. It intentionally contains no fuel
    /// quantity, fuel flow, oil, vacuum, voltage, current, navigation, radio,
    /// or engine-running claim because current product authority exposes none.
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
