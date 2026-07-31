#if UNITY_EDITOR
using System;
using System.Reflection;
using NUnit.Framework;
using QuestFlightLab.Aircraft.CockpitDynamics.SystemsV2;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class CockpitSystemsAuthorityV2EditModeTests
    {
        [Test]
        public void ScenarioAndCommandsProduceDeterministicState()
        {
            CockpitSystemsAuthorityV2Model first =
                new CockpitSystemsAuthorityV2Model();
            CockpitSystemsAuthorityV2Model second =
                new CockpitSystemsAuthorityV2Model();
            CockpitSystemsScenarioV2 scenario = Scenario();
            CockpitSystemsAuthorityInputV2 input = Input(
                CockpitElectricalPowerSourceInputV2.Unavailable);

            Assert.That(first.Reset(scenario, input), Is.True);
            Assert.That(second.Reset(scenario, input), Is.True);

            CockpitSystemsCommandV2[] commands =
            {
                CockpitSystemsCommandV2.Boolean(
                    10, CockpitSystemsCommandKindV2.SetBatteryMaster, false),
                CockpitSystemsCommandV2.Discrete(
                    11, CockpitSystemsCommandKindV2.SetIgnitionSwitch,
                    (int)CockpitIgnitionSwitchPositionV2.Start),
                CockpitSystemsCommandV2.Normalized(
                    12, CockpitSystemsCommandKindV2.SetPrimerPosition, 0.75f),
                CockpitSystemsCommandV2.Discrete(
                    13, CockpitSystemsCommandKindV2.SetFuelSelector,
                    (int)CockpitFuelSelectorPositionV2.Left)
            };
            for (int index = 0; index < commands.Length; index++)
            {
                Assert.That(first.ApplyCommand(commands[index]), Is.True);
                Assert.That(second.ApplyCommand(commands[index]), Is.True);
            }

            input.flightState.simulationTimeSeconds = 42.25;
            Assert.That(first.Step(input), Is.True);
            Assert.That(second.Step(input), Is.True);
            AssertSnapshotsEqual(first.Current, second.Current);

            CockpitSystemsSnapshotV2 snapshot = first.Current;
            Assert.That(snapshot.scenarioUtcSecondsSinceUnixEpoch,
                Is.EqualTo(scenario.scenarioUtcUnixSeconds + 42.25).Within(0.0001));
            Assert.That(snapshot.lastAppliedCommandSequence, Is.EqualTo(13ul));
            Assert.That(snapshot.batteryMasterOn, Is.False);
            Assert.That(snapshot.ignitionSwitchPosition,
                Is.EqualTo(CockpitIgnitionSwitchPositionV2.Start));
            Assert.That(snapshot.starterSwitchEngaged, Is.True);
            Assert.That(snapshot.primerPositionNormalized,
                Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(snapshot.fuelSelectorPosition,
                Is.EqualTo(CockpitFuelSelectorPositionV2.Left));
        }

        [Test]
        public void ElectricalPowerFailsClosedUntilSourceAuthorityExists()
        {
            CockpitSystemsAuthorityV2Model model =
                new CockpitSystemsAuthorityV2Model();
            CockpitSystemsAuthorityInputV2 input = Input(
                CockpitElectricalPowerSourceInputV2.Unavailable);
            Assert.That(model.Reset(Scenario(), input), Is.True);

            CockpitSystemsSnapshotV2 unavailable = model.Current;
            Assert.That(unavailable.Has(
                CockpitSystemsAvailabilityV2.ElectricalControlPositions), Is.True);
            Assert.That(unavailable.Has(
                CockpitSystemsAvailabilityV2.ElectricalBusPower), Is.False);
            Assert.That(unavailable.mainBusPowered, Is.False);
            Assert.That(unavailable.avionicsBusPowered, Is.False);
            Assert.That(unavailable.starterMotorPowered, Is.False);

            input.electricalPowerSources =
                new CockpitElectricalPowerSourceInputV2
                {
                    hasAuthoritativeState = true,
                    batteryCanPowerMainBus = true,
                    alternatorCanPowerMainBus = false
                };
            Assert.That(model.Step(input), Is.True);
            Assert.That(model.Current.Has(
                CockpitSystemsAvailabilityV2.ElectricalBusPower), Is.True);
            Assert.That(model.Current.mainBusPowered, Is.True);
            Assert.That(model.Current.avionicsBusPowered, Is.True);

            Assert.That(model.ApplyCommand(
                CockpitSystemsCommandV2.Discrete(
                    1,
                    CockpitSystemsCommandKindV2.SetIgnitionSwitch,
                    (int)CockpitIgnitionSwitchPositionV2.Start)), Is.True);
            Assert.That(model.Step(input), Is.True);
            Assert.That(model.Current.starterMotorPowered, Is.True);
        }

        [Test]
        public void MastersOffProveBusOffWithoutInventingSourceHealth()
        {
            CockpitSystemsScenarioV2 scenario = Scenario();
            scenario.batteryMasterOn = false;
            scenario.alternatorMasterOn = false;
            CockpitSystemsAuthorityV2Model model =
                new CockpitSystemsAuthorityV2Model();
            Assert.That(model.Reset(
                scenario,
                Input(CockpitElectricalPowerSourceInputV2.Unavailable)), Is.True);

            Assert.That(model.Current.Has(
                CockpitSystemsAvailabilityV2.ElectricalBusPower), Is.True);
            Assert.That(model.Current.mainBusPowered, Is.False);
            Assert.That(model.Current.avionicsBusPowered, Is.False);
        }

        [Test]
        public void InvalidAuthorityOrNonMonotonicTimeFailsClosed()
        {
            CockpitSystemsAuthorityV2Model model =
                new CockpitSystemsAuthorityV2Model();
            CockpitSystemsAuthorityInputV2 invalid = Input(
                CockpitElectricalPowerSourceInputV2.Unavailable);
            invalid.hasAuthoritativeFlightState = false;
            Assert.That(model.Reset(Scenario(), invalid), Is.False);
            Assert.That(model.Current.isValid, Is.False);

            CockpitSystemsAuthorityInputV2 valid = Input(
                CockpitElectricalPowerSourceInputV2.Unavailable);
            Assert.That(model.Reset(Scenario(), valid), Is.True);
            valid.flightState.simulationTimeSeconds = 1.0;
            Assert.That(model.Step(valid), Is.False);
            Assert.That(model.Current.isValid, Is.False);
            Assert.That(model.Current.availability,
                Is.EqualTo(CockpitSystemsAvailabilityV2.None));

            Assert.That(model.Reset(Scenario(), Input(
                CockpitElectricalPowerSourceInputV2.Unavailable)), Is.True);
            CockpitSystemsAuthorityInputV2 nonfinite = Input(
                CockpitElectricalPowerSourceInputV2.Unavailable);
            nonfinite.flightState.engineRpm = double.NaN;
            Assert.That(model.Step(nonfinite), Is.False);
            Assert.That(model.Current.isValid, Is.False);
        }

        [Test]
        public void InvalidOrDuplicateCommandsDoNotMutateAuthority()
        {
            CockpitSystemsAuthorityV2Model model =
                new CockpitSystemsAuthorityV2Model();
            CockpitSystemsAuthorityInputV2 input = Input(
                CockpitElectricalPowerSourceInputV2.Unavailable);
            Assert.That(model.Reset(Scenario(), input), Is.True);

            CockpitSystemsCommandV2 accepted =
                CockpitSystemsCommandV2.Boolean(
                    20,
                    CockpitSystemsCommandKindV2.SetBatteryMaster,
                    false);
            Assert.That(model.ApplyCommand(accepted), Is.True);
            Assert.That(model.ApplyCommand(accepted), Is.False);
            Assert.That(model.ApplyCommand(
                CockpitSystemsCommandV2.Normalized(
                    21,
                    CockpitSystemsCommandKindV2.SetPrimerPosition,
                    float.NaN)), Is.False);
            Assert.That(model.ApplyCommand(
                CockpitSystemsCommandV2.Discrete(
                    22,
                    CockpitSystemsCommandKindV2.SetFuelSelector,
                    99)), Is.False);
            Assert.That(model.Step(input), Is.True);

            Assert.That(model.Current.batteryMasterOn, Is.False);
            Assert.That(model.Current.primerPositionNormalized,
                Is.EqualTo(0f));
            Assert.That(model.Current.fuelSelectorPosition,
                Is.EqualTo(CockpitFuelSelectorPositionV2.Both));
            Assert.That(model.Current.lastAppliedCommandSequence,
                Is.EqualTo(20ul));
        }

        [Test]
        public void SnapshotContainsNoUnsupportedFantasyIndications()
        {
            string[] forbidden =
            {
                "FuelQuantity", "FuelFlow", "OilPressure", "OilTemperature",
                "Vacuum", "Voltage", "Amps", "Navigation", "Radio",
                "Transponder", "EngineRunning", "ActualFlap"
            };
            FieldInfo[] fields = typeof(CockpitSystemsSnapshotV2).GetFields(
                BindingFlags.Instance | BindingFlags.Public);
            for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
            {
                for (int forbiddenIndex = 0;
                     forbiddenIndex < forbidden.Length;
                     forbiddenIndex++)
                {
                    Assert.That(
                        fields[fieldIndex].Name.IndexOf(
                            forbidden[forbiddenIndex],
                            StringComparison.OrdinalIgnoreCase),
                        Is.LessThan(0));
                }
            }

            CockpitSystemsAuthorityV2Model model =
                new CockpitSystemsAuthorityV2Model();
            Assert.That(model.Reset(
                Scenario(),
                Input(CockpitElectricalPowerSourceInputV2.Unavailable)), Is.True);
            CockpitSystemsAvailabilityV2 expected =
                CockpitSystemsAvailabilityV2.ScenarioClock |
                CockpitSystemsAvailabilityV2.FlightControlCommands |
                CockpitSystemsAvailabilityV2.EngineRpm |
                CockpitSystemsAvailabilityV2.ElectricalControlPositions |
                CockpitSystemsAvailabilityV2.IgnitionAndStarterControl |
                CockpitSystemsAvailabilityV2.PrimerControl |
                CockpitSystemsAvailabilityV2.FuelSelectorControl |
                CockpitSystemsAvailabilityV2.FlapSelectorCommand;
            Assert.That(model.Current.availability, Is.EqualTo(expected));
        }

        [Test]
        public void SteadyStateStepAndCommandsAllocateNoManagedMemory()
        {
            CockpitSystemsAuthorityV2Model model =
                new CockpitSystemsAuthorityV2Model();
            CockpitSystemsAuthorityInputV2 input = Input(
                new CockpitElectricalPowerSourceInputV2
                {
                    hasAuthoritativeState = true,
                    batteryCanPowerMainBus = true,
                    alternatorCanPowerMainBus = true
                });
            Assert.That(model.Reset(Scenario(), input), Is.True);
            for (int warmup = 0; warmup < 64; warmup++)
            {
                input.flightState.simulationTimeSeconds += 1.0 / 120.0;
                model.Step(input);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 4096; index++)
            {
                input.flightState.simulationTimeSeconds += 1.0 / 120.0;
                model.Step(input);
                if ((index & 1023) == 0)
                {
                    CockpitSystemsCommandV2 command =
                        CockpitSystemsCommandV2.Boolean(
                            (ulong)index + 1ul,
                            CockpitSystemsCommandKindV2.SetAvionicsMaster,
                            (index & 2047) == 0);
                    model.ApplyCommand(command);
                }
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.Zero);
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

        private static CockpitSystemsAuthorityInputV2 Input(
            CockpitElectricalPowerSourceInputV2 power)
        {
            return new CockpitSystemsAuthorityInputV2
            {
                hasAuthoritativeFlightState = true,
                flightState = new FlightDynamicsState
                {
                    simulationTimeSeconds = 12.5,
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
                },
                flightControls = new FlightDynamicsControlSnapshot
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
                },
                electricalPowerSources = power
            };
        }

        private static void AssertSnapshotsEqual(
            CockpitSystemsSnapshotV2 first,
            CockpitSystemsSnapshotV2 second)
        {
            Assert.That(first.isValid, Is.EqualTo(second.isValid));
            Assert.That(first.availability, Is.EqualTo(second.availability));
            Assert.That(first.flightSimulationTimeSeconds,
                Is.EqualTo(second.flightSimulationTimeSeconds));
            Assert.That(first.scenarioUtcSecondsSinceUnixEpoch,
                Is.EqualTo(second.scenarioUtcSecondsSinceUnixEpoch));
            Assert.That(first.lastAppliedCommandSequence,
                Is.EqualTo(second.lastAppliedCommandSequence));
            Assert.That(first.engineRpm, Is.EqualTo(second.engineRpm));
            Assert.That(first.flapSelectorCommandNormalized,
                Is.EqualTo(second.flapSelectorCommandNormalized));
            Assert.That(first.batteryMasterOn, Is.EqualTo(second.batteryMasterOn));
            Assert.That(first.alternatorMasterOn,
                Is.EqualTo(second.alternatorMasterOn));
            Assert.That(first.avionicsMasterOn, Is.EqualTo(second.avionicsMasterOn));
            Assert.That(first.mainBusPowered, Is.EqualTo(second.mainBusPowered));
            Assert.That(first.avionicsBusPowered,
                Is.EqualTo(second.avionicsBusPowered));
            Assert.That(first.ignitionSwitchPosition,
                Is.EqualTo(second.ignitionSwitchPosition));
            Assert.That(first.starterSwitchEngaged,
                Is.EqualTo(second.starterSwitchEngaged));
            Assert.That(first.starterMotorPowered,
                Is.EqualTo(second.starterMotorPowered));
            Assert.That(first.primerPositionNormalized,
                Is.EqualTo(second.primerPositionNormalized));
            Assert.That(first.fuelSelectorPosition,
                Is.EqualTo(second.fuelSelectorPosition));
        }
    }
}
#endif
