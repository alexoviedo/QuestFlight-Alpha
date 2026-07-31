using System;

namespace QuestFlightLab.Aircraft.CockpitDynamics
{
    /// <summary>
    /// Identifies which cockpit presentation channels are backed by current
    /// authoritative product state. Unsupported systems stay explicitly absent
    /// rather than being populated with plausible-looking invented values.
    /// </summary>
    [Flags]
    public enum CockpitDataAvailability : uint
    {
        None = 0,
        AirData = 1u << 0,
        Attitude = 1u << 1,
        Heading = 1u << 2,
        BodyTurnRate = 1u << 3,
        AerodynamicSideslip = 1u << 4,
        EngineRpm = 1u << 5,
        FlightControlCommands = 1u << 6,
        WeightOnWheels = 1u << 7,
        ElectricalPower = 1u << 8,

        // These flags define the honest boundary for future expansion. The
        // current coordinator does not provide these values.
        BarometricSetting = 1u << 16,
        SlipSkidInclinometer = 1u << 17,
        ActualFlapPosition = 1u << 18,
        OilSystem = 1u << 19,
        FuelSystem = 1u << 20,
        Avionics = 1u << 21,
        Navigation = 1u << 22
    }

    /// <summary>
    /// Optional authoritative electrical input. The current flight-dynamics
    /// boundary has no electrical model, so the default value is unavailable.
    /// An authored cockpit electrical controller may provide this later.
    /// </summary>
    [Serializable]
    public struct CockpitElectricalPowerInput
    {
        public bool hasAuthoritativeState;
        public bool batteryMasterOn;
        public bool alternatorMasterOn;
        public bool avionicsMasterOn;
        public bool batteryCanPowerBus;
        public bool alternatorProducingPower;

        public static CockpitElectricalPowerInput Unavailable => default;
    }

    /// <summary>
    /// Derived bus state. This performs Boolean power routing only; it does not
    /// invent voltage, current, battery charge, or alternator performance.
    /// </summary>
    [Serializable]
    public struct CockpitElectricalPowerState
    {
        public bool dataAvailable;
        public bool batteryMasterOn;
        public bool alternatorMasterOn;
        public bool avionicsMasterOn;
        public bool mainBusPowered;
        public bool avionicsBusPowered;

        public static CockpitElectricalPowerState From(CockpitElectricalPowerInput input)
        {
            if (!input.hasAuthoritativeState) return default;

            bool batteryFeed = input.batteryMasterOn && input.batteryCanPowerBus;
            bool alternatorFeed = input.alternatorMasterOn && input.alternatorProducingPower;
            bool mainBus = batteryFeed || alternatorFeed;
            return new CockpitElectricalPowerState
            {
                dataAvailable = true,
                batteryMasterOn = input.batteryMasterOn,
                alternatorMasterOn = input.alternatorMasterOn,
                avionicsMasterOn = input.avionicsMasterOn,
                mainBusPowered = mainBus,
                avionicsBusPowered = mainBus && input.avionicsMasterOn
            };
        }
    }
}
