namespace QuestFlightLab.Aircraft.CockpitDynamics.Presentation
{
    /// <summary>
    /// Stable presentation channels for authored cockpit controls and indicators.
    /// Declaring a channel does not add a cockpit feature: only serialized bindings
    /// to positively identified product-model parts are applied.
    /// </summary>
    public enum CockpitPresentationChannel
    {
        None = 0,

        AileronNormalized = 10,
        ElevatorNormalized = 11,
        RudderNormalized = 12,
        YokeRollNormalized = 13,
        YokePitchNormalized = 14,
        RudderPedalsNormalized = 15,
        LeftToeBrakeNormalized = 16,
        RightToeBrakeNormalized = 17,
        ThrottleNormalized = 18,
        MixtureNormalized = 19,
        AlternateAirNormalized = 20,
        TrimNormalized = 21,
        FlapsCommandNormalized = 22,
        NoseSteeringNormalized = 23,

        SimulationTimeSeconds = 100,
        CalibratedAirspeedKnots = 101,
        GroundSpeedKnots = 102,
        AltitudeMslFeet = 103,
        AltitudeAglFeet = 104,
        VerticalSpeedFeetPerMinute = 105,
        HeadingDegrees = 106,
        PitchDegrees = 107,
        BankDegrees = 108,
        AngleOfAttackDegrees = 109,
        SideslipDegrees = 110,
        EngineRpm = 111,
        LoadFactorG = 112,
        WeightOnWheels = 113,

        ActualFlapDegrees = 200,
        TurnRateDegreesPerSecond = 201,
        SlipSkidNormalized = 202,
        MagneticHeadingDegrees = 203,
        BarometricSettingInHg = 204,
        LeftFuelGallons = 205,
        RightFuelGallons = 206,
        FuelFlowGallonsPerHour = 207,
        OilPressurePsi = 208,
        OilTemperatureFahrenheit = 209,
        CylinderHeadTemperatureFahrenheit = 210,
        ExhaustGasTemperatureFahrenheit = 211,
        ElectricalBusVolts = 212,
        AlternatorAmps = 213,
        VacuumInHg = 214,
        InstrumentLightNormalized = 215,
        PanelFloodLightNormalized = 216,
        BatteryMasterOn = 217,
        AlternatorMasterOn = 218,
        AvionicsMasterOn = 219,
        IgnitionSwitchPosition = 220,
        PrimerPositionNormalized = 221,
        FuelSelectorPosition = 222
    }
}
