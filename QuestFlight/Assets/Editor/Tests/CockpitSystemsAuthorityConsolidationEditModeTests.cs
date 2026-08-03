#if UNITY_EDITOR
using NUnit.Framework;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.Presentation;
using QuestFlightLab.Aircraft.CockpitDynamics.SystemsV2;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class CockpitSystemsAuthorityConsolidationEditModeTests
    {
        [Test]
        public void ExistingControllerIsTheSingleV2StateFacade()
        {
            System.Type type =
                typeof(ProductionCockpitSystemsController);
            Assert.That(
                typeof(ICockpitSystemsStateProviderV2).IsAssignableFrom(type),
                Is.True);
            Assert.That(
                typeof(ICockpitInstrumentV2AuthorityProvider)
                    .IsAssignableFrom(type),
                Is.True);
            Assert.That(
                typeof(ICockpitAvionicsV2AuthorityProvider)
                    .IsAssignableFrom(type),
                Is.True);
            Assert.That(
                typeof(ICockpitTouchInteractionV2CommandConsumer)
                    .IsAssignableFrom(type),
                Is.False,
                "Touch has no authoritative ProductControlInput arbitration yet.");
        }

        [Test]
        public void ConsolidatedSnapshotExposesOnlyTruthfulInstrumentChannels()
        {
            WithController(controller =>
            {
                FlightDynamicsState state = State(12.5);
                Assert.That(
                    controller.StepForTest(
                        state,
                        Controls(),
                        CockpitElectricalPowerInput.Unavailable,
                        Scenario(),
                        1f / 72f),
                    Is.True);

                Assert.That(
                    controller.TryGetCockpitSystemsSnapshotV2(
                        out CockpitSystemsSnapshotV2 systems),
                    Is.True);
                Assert.That(
                    systems.scenarioUtcSecondsSinceUnixEpoch,
                    Is.EqualTo(
                        Scenario().scenarioUtcUnixSeconds +
                        state.simulationTimeSeconds).Within(0.0001));

                Assert.That(
                    controller.TryGetInstrumentV2Snapshot(
                        out CockpitInstrumentV2Snapshot instruments),
                    Is.True);
                CockpitInstrumentV2Availability expected =
                    CockpitInstrumentV2Availability.AirData |
                    CockpitInstrumentV2Availability.BarometricSetting |
                    CockpitInstrumentV2Availability.Attitude |
                    CockpitInstrumentV2Availability.TrueHeading |
                    CockpitInstrumentV2Availability.MagneticVariation |
                    CockpitInstrumentV2Availability.ActualFlapPosition |
                    CockpitInstrumentV2Availability.BrakeHydraulics |
                    CockpitInstrumentV2Availability.ClockUtc |
                    CockpitInstrumentV2Availability.EngineRpm |
                    CockpitInstrumentV2Availability.FuelSystem |
                    CockpitInstrumentV2Availability.OilSystem |
                    CockpitInstrumentV2Availability.VacuumSuction |
                    CockpitInstrumentV2Availability.TurnAndSlip;
                Assert.That(instruments.availability, Is.EqualTo(expected));
                Assert.That(
                    instruments.pitchDegrees,
                    Is.EqualTo((float)state.pitchDegrees).Within(0.001f));
                Assert.That(
                    instruments.bankDegrees,
                    Is.EqualTo((float)state.bankDegrees).Within(0.001f));
                Assert.That(
                    instruments.trueHeadingDegrees,
                    Is.EqualTo((float)state.headingDegrees).Within(0.001f));
                Assert.That(
                    instruments.indicatedAirspeedKnots,
                    Is.EqualTo((float)state.calibratedAirspeedKnots)
                        .Within(0.001f));
                Assert.That(
                    instruments.indicatedVerticalSpeedFeetPerMinute,
                    Is.EqualTo((float)state.verticalSpeedFeetPerMinute)
                        .Within(0.001f));
                Assert.That(
                    instruments.pressureAltitudeFeet,
                    Is.EqualTo(
                        (float)(state.altitudeMslMeters *
                                FlightFrameConversions.MetersToFeet))
                        .Within(0.01f));
                Assert.That(instruments.barometricSettingInHg,
                    Is.EqualTo(29.92f).Within(0.001f));
                Assert.That(
                    instruments.turnRateDegreesPerSecond,
                    Is.EqualTo(
                        state.angularVelocityBodyDegreesPerSecond.y)
                        .Within(0.001f));
                Assert.That(
                    instruments.slipSkidNormalized,
                    Is.EqualTo((float)state.sideslipDegrees / 8f)
                        .Within(0.001f));
                Assert.That(
                    instruments.Has(
                        CockpitInstrumentV2Availability.AirData),
                    Is.True);
                Assert.That(
                    instruments.Has(
                        CockpitInstrumentV2Availability.BarometricSetting),
                    Is.True);
                Assert.That(
                    instruments.Has(
                        CockpitInstrumentV2Availability.ActualFlapPosition),
                    Is.True);
                Assert.That(
                    instruments.Has(
                        CockpitInstrumentV2Availability.BrakeHydraulics),
                    Is.True);
                Assert.That(
                    instruments.Has(
                        CockpitInstrumentV2Availability.MagneticVariation),
                    Is.True);
            });
        }

        [Test]
        public void AvionicsProviderExposesOnlyAuthoritativeBusPower()
        {
            WithController(controller =>
            {
                CockpitElectricalPowerInput power =
                    new CockpitElectricalPowerInput
                    {
                        hasAuthoritativeState = true,
                        // These legacy control positions are deliberately
                        // opposite the V2 scenario and must not win.
                        batteryMasterOn = false,
                        alternatorMasterOn = false,
                        avionicsMasterOn = false,
                        batteryCanPowerBus = true,
                        alternatorProducingPower = true
                    };
                Assert.That(
                    controller.StepForTest(
                        State(20.0),
                        Controls(),
                        power,
                        Scenario(),
                        1f / 72f),
                    Is.True);

                Assert.That(
                    controller.TryGetAvionicsV2Snapshot(
                        out CockpitAvionicsV2Snapshot avionics),
                    Is.True);
                Assert.That(
                    avionics.availability,
                    Is.EqualTo(
                        CockpitAvionicsV2Availability.ElectricalPower |
                        CockpitAvionicsV2Availability.GpsNavigation |
                        CockpitAvionicsV2Availability.ComRadios |
                        CockpitAvionicsV2Availability.Transponder));
                Assert.That(avionics.avionicsBusPowered, Is.True);
                Assert.That(
                    avionics.Has(
                        CockpitAvionicsV2Availability.GpsNavigation),
                    Is.True);
                Assert.That(
                    avionics.Has(
                        CockpitAvionicsV2Availability.ComRadios),
                    Is.True);
                Assert.That(
                    avionics.Has(
                        CockpitAvionicsV2Availability.Dme),
                    Is.False);
                Assert.That(
                    avionics.Has(
                        CockpitAvionicsV2Availability.Transponder),
                    Is.True);
                Assert.That(
                    avionics.Has(
                        CockpitAvionicsV2Availability.AudioPanel),
                    Is.False);
                Assert.That(avionics.gpsDestinationIdentAscii,
                    Is.EqualTo(0x5542444Bu));
                Assert.That(avionics.com1ActiveKHz, Is.EqualTo(122725));
                Assert.That(avionics.com1StandbyKHz, Is.EqualTo(118825));
                Assert.That(avionics.com2ActiveKHz, Is.EqualTo(121500));
                Assert.That(avionics.transponderCodeBcd,
                    Is.EqualTo(0x1200));
                Assert.That(avionics.transponderMode,
                    Is.EqualTo(
                        CockpitAvionicsV2TransponderMode.Altitude));
            });
        }

        [Test]
        public void V2MasterCommandsDriveLegacyElectricalPresentation()
        {
            WithController(controller =>
            {
                CockpitElectricalPowerInput power =
                    new CockpitElectricalPowerInput
                    {
                        hasAuthoritativeState = true,
                        batteryMasterOn = true,
                        alternatorMasterOn = true,
                        avionicsMasterOn = true,
                        batteryCanPowerBus = true,
                        alternatorProducingPower = true
                    };
                Assert.That(
                    controller.StepForTest(
                        State(30.0),
                        Controls(),
                        power,
                        Scenario(),
                        1f / 72f),
                    Is.True);
                Assert.That(
                    controller.TryApplyCommand(
                        CockpitSystemsCommandV2.Boolean(
                            1,
                            CockpitSystemsCommandKindV2.SetBatteryMaster,
                            false)),
                    Is.True);
                Assert.That(
                    controller.TryApplyCommand(
                        CockpitSystemsCommandV2.Boolean(
                            2,
                            CockpitSystemsCommandKindV2.SetAlternatorMaster,
                            false)),
                    Is.True);

                Assert.That(
                    controller.StepForTest(
                        State(30.1),
                        Controls(),
                        power,
                        1f / 72f),
                    Is.True);

                Assert.That(
                    controller.CurrentSnapshot.electricalPower.dataAvailable,
                    Is.True);
                Assert.That(
                    controller.CurrentSnapshot.electricalPower
                        .batteryMasterOn,
                    Is.False);
                Assert.That(
                    controller.CurrentSnapshot.electricalPower
                        .alternatorMasterOn,
                    Is.False);
                Assert.That(
                    controller.CurrentSnapshot.electricalPower.mainBusPowered,
                    Is.False);
            });
        }

        [Test]
        public void RepresentedSwitchChannelsReadFromSystemsAuthority()
        {
            WithController(controller =>
            {
                Assert.That(
                    controller.StepForTest(
                        State(35.0),
                        Controls(),
                        CockpitElectricalPowerInput.Unavailable,
                        Scenario(),
                        1f / 72f),
                    Is.True);
                Assert.That(controller.TryGetCockpitPresentationValue(
                    CockpitPresentationChannel.BatteryMasterOn,
                    out float battery), Is.True);
                Assert.That(battery, Is.EqualTo(1f));
                Assert.That(controller.TryGetCockpitPresentationValue(
                    CockpitPresentationChannel.IgnitionSwitchPosition,
                    out float ignition), Is.True);
                Assert.That(ignition, Is.EqualTo(
                    (float)CockpitIgnitionSwitchPositionV2.Both));
                Assert.That(controller.TryGetCockpitPresentationValue(
                    CockpitPresentationChannel.FuelSelectorPosition,
                    out float selector), Is.True);
                Assert.That(selector, Is.EqualTo(
                    (float)CockpitFuelSelectorPositionV2.Both));
            });
        }

        [Test]
        public void UnavailableSourceDoesNotInventAvionicsPower()
        {
            WithController(controller =>
            {
                Assert.That(
                    controller.StepForTest(
                        State(40.0),
                        Controls(),
                        CockpitElectricalPowerInput.Unavailable,
                        Scenario(),
                        1f / 72f),
                    Is.True);
                Assert.That(
                    controller.TryGetAvionicsV2Snapshot(out _),
                    Is.False);
                Assert.That(
                    controller.CurrentSnapshot.Has(
                        CockpitDataAvailability.ElectricalPower),
                    Is.False);
            });
        }

        [Test]
        public void ConsolidatedSteadyStateSamplingAllocatesNoManagedMemory()
        {
            WithController(controller =>
            {
                CockpitElectricalPowerInput power =
                    new CockpitElectricalPowerInput
                    {
                        hasAuthoritativeState = true,
                        batteryCanPowerBus = true,
                        alternatorProducingPower = true
                    };
                FlightDynamicsState state = State(50.0);
                FlightDynamicsControlSnapshot controls = Controls();
                Assert.That(
                    controller.StepForTest(
                        state,
                        controls,
                        power,
                        Scenario(),
                        1f / 120f),
                    Is.True);
                for (int warmup = 0; warmup < 64; warmup++)
                {
                    state.simulationTimeSeconds += 1.0 / 120.0;
                    controller.StepForTest(
                        state, controls, power, 1f / 120f);
                    controller.TryGetCockpitSystemsSnapshotV2(out _);
                    controller.TryGetInstrumentV2Snapshot(out _);
                    controller.TryGetAvionicsV2Snapshot(out _);
                }

                long before =
                    System.GC.GetAllocatedBytesForCurrentThread();
                for (int sample = 0; sample < 4096; sample++)
                {
                    state.simulationTimeSeconds += 1.0 / 120.0;
                    controller.StepForTest(
                        state, controls, power, 1f / 120f);
                    controller.TryGetCockpitSystemsSnapshotV2(out _);
                    controller.TryGetInstrumentV2Snapshot(out _);
                    controller.TryGetAvionicsV2Snapshot(out _);
                }
                long after =
                    System.GC.GetAllocatedBytesForCurrentThread();

                Assert.That(after - before, Is.Zero);
            });
        }

        private static void WithController(
            System.Action<ProductionCockpitSystemsController> action)
        {
            GameObject root =
                new GameObject("CockpitSystemsAuthorityConsolidationTest");
            try
            {
                action(
                    root.AddComponent<
                        ProductionCockpitSystemsController>());
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static CockpitSystemsScenarioV2 Scenario()
        {
            return new CockpitSystemsScenarioV2
            {
                hasAuthoritativeConfiguration = true,
                scenarioUtcUnixSeconds = 1_780_000_000L,
                batteryMasterOn = true,
                alternatorMasterOn = true,
                avionicsMasterOn = true,
                ignitionSwitchPosition =
                    CockpitIgnitionSwitchPositionV2.Both,
                primerPositionNormalized = 0f,
                fuelSelectorPosition =
                    CockpitFuelSelectorPositionV2.Both
            };
        }

        private static FlightDynamicsState State(double simulationTime)
        {
            return new FlightDynamicsState
            {
                simulationTimeSeconds = simulationTime,
                latitudeDegrees = 40.03936527,
                longitudeDegrees = -105.22608958,
                altitudeMslMeters = 1700.0,
                altitudeAglMeters = 88.0,
                calibratedAirspeedKnots = 92.0,
                groundSpeedKnots = 90.0,
                verticalSpeedFeetPerMinute = 450.0,
                headingDegrees = 84.0,
                pitchDegrees = 4.0,
                bankDegrees = -12.0,
                angleOfAttackDegrees = 5.0,
                sideslipDegrees = 1.25,
                engineRpm = 2350.0,
                loadFactorG = 1.1,
                weightOnWheels = false,
                positionUnityMeters = new Vector3(25f, 88f, 600f),
                rotationUnity = Quaternion.Euler(-4f, 84f, 12f),
                velocityUnityMetersPerSecond =
                    new Vector3(2f, 2.2f, 46f),
                angularVelocityBodyDegreesPerSecond =
                    new Vector3(0.5f, 2.2f, -1.1f)
            };
        }

        private static FlightDynamicsControlSnapshot Controls()
        {
            return new FlightDynamicsControlSnapshot
            {
                aileron = 0.15f,
                elevator = -0.08f,
                rudder = 0.12f,
                throttle = 0.78f,
                mixture = 1f,
                carbHeat = 0f,
                trim = -0.05f,
                flaps = 0.5f,
                leftToeBrake = 0.1f,
                rightToeBrake = 0.2f
            };
        }
    }
}
#endif
