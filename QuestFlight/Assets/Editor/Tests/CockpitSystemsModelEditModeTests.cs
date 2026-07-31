#if UNITY_EDITOR
using System;
using NUnit.Framework;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.Presentation;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class CockpitSystemsModelEditModeTests
    {
        [Test]
        public void SnapshotUsesMslAltitudeAndOnlyAvailableAuthoritativeChannels()
        {
            CockpitSystemsModel model = new CockpitSystemsModel();
            FlightDynamicsState state = RepresentativeState();
            state.altitudeMslMeters = 1612.0;
            state.altitudeAglMeters = 7.0;

            Assert.That(model.Reset(
                state,
                RepresentativeControls(),
                CockpitElectricalPowerInput.Unavailable), Is.True);

            CockpitSystemsSnapshot snapshot = model.Current;
            Assert.That(snapshot.isValid, Is.True);
            Assert.That(snapshot.indicatedAltitudeMslFeet,
                Is.EqualTo(1612.0 * FlightFrameConversions.MetersToFeet).Within(0.01));
            Assert.That(snapshot.indicatedAltitudeMslFeet,
                Is.Not.EqualTo(7.0 * FlightFrameConversions.MetersToFeet).Within(0.01));
            Assert.That(snapshot.Has(CockpitDataAvailability.AirData), Is.True);
            Assert.That(snapshot.Has(CockpitDataAvailability.Attitude), Is.True);
            Assert.That(snapshot.Has(CockpitDataAvailability.EngineRpm), Is.True);
            Assert.That(snapshot.Has(CockpitDataAvailability.FlightControlCommands), Is.True);
            Assert.That(snapshot.Has(CockpitDataAvailability.ElectricalPower), Is.False);
            Assert.That(snapshot.Has(CockpitDataAvailability.ActualFlapPosition), Is.False);
            Assert.That(snapshot.Has(CockpitDataAvailability.SlipSkidInclinometer), Is.False);
            Assert.That(snapshot.Has(CockpitDataAvailability.OilSystem), Is.False);
            Assert.That(snapshot.Has(CockpitDataAvailability.FuelSystem), Is.False);
            Assert.That(snapshot.Has(CockpitDataAvailability.Avionics), Is.False);
            Assert.That(snapshot.Has(CockpitDataAvailability.Navigation), Is.False);
        }

        [Test]
        public void ControlsAreClampedWithoutInventingMeasuredFlapTravel()
        {
            CockpitSystemsModel model = new CockpitSystemsModel();
            FlightDynamicsControlSnapshot controls = RepresentativeControls();
            controls.aileron = 2f;
            controls.elevator = -2f;
            controls.throttle = 1.5f;
            controls.mixture = -0.5f;
            controls.flaps = 0.67f;
            controls.leftToeBrake = 4f;

            Assert.That(model.Reset(
                RepresentativeState(), controls,
                CockpitElectricalPowerInput.Unavailable), Is.True);

            CockpitControlPositions positions = model.Current.controlPositions;
            Assert.That(positions.aileron, Is.EqualTo(1f));
            Assert.That(positions.elevator, Is.EqualTo(-1f));
            Assert.That(positions.throttle, Is.EqualTo(1f));
            Assert.That(positions.mixture, Is.EqualTo(0f));
            Assert.That(positions.flapCommand, Is.EqualTo(0.67f).Within(0.0001f));
            Assert.That(positions.leftToeBrake, Is.EqualTo(1f));
            Assert.That(model.Current.Has(
                CockpitDataAvailability.ActualFlapPosition), Is.False);
        }

        [Test]
        public void VerticalSpeedHasDeterministicInstrumentLag()
        {
            CockpitSystemsModel model = new CockpitSystemsModel();
            FlightDynamicsState state = RepresentativeState();
            state.verticalSpeedFeetPerMinute = 0.0;
            Assert.That(model.Reset(
                state, RepresentativeControls(),
                CockpitElectricalPowerInput.Unavailable), Is.True);

            state.verticalSpeedFeetPerMinute = 1200.0;
            Assert.That(model.Step(
                state, RepresentativeControls(),
                CockpitElectricalPowerInput.Unavailable, 1f), Is.True);

            float expected = 1200f *
                (1f - Mathf.Exp(-1f / CockpitSystemsModel.VerticalSpeedResponseSeconds));
            Assert.That(model.Current.indicatedVerticalSpeedFeetPerMinute,
                Is.EqualTo(expected).Within(0.001f));
            Assert.That(model.Current.indicatedVerticalSpeedFeetPerMinute,
                Is.GreaterThan(0f).And.LessThan(1200f));
        }

        [Test]
        public void HeadingResponseUsesShortestPathAcrossNorth()
        {
            CockpitSystemsModel model = new CockpitSystemsModel();
            FlightDynamicsState state = RepresentativeState();
            state.headingDegrees = 359.0;
            Assert.That(model.Reset(
                state, RepresentativeControls(),
                CockpitElectricalPowerInput.Unavailable), Is.True);

            state.headingDegrees = 1.0;
            Assert.That(model.Step(
                state, RepresentativeControls(),
                CockpitElectricalPowerInput.Unavailable, 0.05f), Is.True);

            float heading = model.Current.indicatedHeadingDegrees;
            Assert.That(heading, Is.GreaterThan(359f).Or.LessThan(1f));
        }

        [Test]
        public void ElectricalBusesAppearOnlyWithAuthoritativeInput()
        {
            CockpitSystemsModel model = new CockpitSystemsModel();
            CockpitElectricalPowerInput power = new CockpitElectricalPowerInput
            {
                hasAuthoritativeState = true,
                batteryMasterOn = true,
                alternatorMasterOn = true,
                avionicsMasterOn = true,
                batteryCanPowerBus = false,
                alternatorProducingPower = true
            };

            Assert.That(model.Reset(
                RepresentativeState(), RepresentativeControls(), power), Is.True);

            CockpitSystemsSnapshot snapshot = model.Current;
            Assert.That(snapshot.Has(CockpitDataAvailability.ElectricalPower), Is.True);
            Assert.That(snapshot.electricalPower.dataAvailable, Is.True);
            Assert.That(snapshot.electricalPower.mainBusPowered, Is.True);
            Assert.That(snapshot.electricalPower.avionicsBusPowered, Is.True);

            power.hasAuthoritativeState = false;
            Assert.That(model.Step(
                RepresentativeState(), RepresentativeControls(), power, 0.02f), Is.True);
            Assert.That(model.Current.Has(
                CockpitDataAvailability.ElectricalPower), Is.False);
            Assert.That(model.Current.electricalPower.dataAvailable, Is.False);
        }

        [Test]
        public void InvalidInputFailsClosedAndInvalidatesSnapshot()
        {
            CockpitSystemsModel model = new CockpitSystemsModel();
            Assert.That(model.Reset(
                RepresentativeState(), RepresentativeControls(),
                CockpitElectricalPowerInput.Unavailable), Is.True);

            FlightDynamicsState invalid = RepresentativeState();
            invalid.engineRpm = double.NaN;
            Assert.That(model.Step(
                invalid, RepresentativeControls(),
                CockpitElectricalPowerInput.Unavailable, 0.02f), Is.False);
            Assert.That(model.Current.isValid, Is.False);
            Assert.That(model.Current.availability,
                Is.EqualTo(CockpitDataAvailability.None));
        }

        [Test]
        public void ControllerProviderExposesOnlySupportedPresentationChannels()
        {
            GameObject root = new GameObject("CockpitSystemsProviderTest");
            try
            {
                ProductionCockpitSystemsController controller =
                    root.AddComponent<ProductionCockpitSystemsController>();
                Assert.That(controller.StepForTest(
                    RepresentativeState(),
                    RepresentativeControls(),
                    CockpitElectricalPowerInput.Unavailable,
                    1f / 72f), Is.True);

                Assert.That(controller.TryGetCockpitPresentationValue(
                    CockpitPresentationChannel.CalibratedAirspeedKnots,
                    out float airspeed), Is.True);
                Assert.That(airspeed, Is.EqualTo(92f).Within(0.001f));
                Assert.That(controller.TryGetCockpitPresentationValue(
                    CockpitPresentationChannel.ThrottleNormalized,
                    out float throttle), Is.True);
                Assert.That(throttle, Is.EqualTo(0.78f).Within(0.001f));
                Assert.That(controller.TryGetCockpitPresentationValue(
                    CockpitPresentationChannel.OilPressurePsi,
                    out _), Is.False);
                Assert.That(controller.TryGetCockpitPresentationValue(
                    CockpitPresentationChannel.LeftFuelGallons,
                    out _), Is.False);
                Assert.That(controller.TryGetCockpitPresentationValue(
                    CockpitPresentationChannel.ActualFlapDegrees,
                    out _), Is.False);
                Assert.That(controller.TryGetCockpitPresentationValue(
                    CockpitPresentationChannel.SlipSkidNormalized,
                    out _), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SteadyStateStepDoesNotAllocateManagedMemory()
        {
            CockpitSystemsModel model = new CockpitSystemsModel();
            FlightDynamicsState state = RepresentativeState();
            FlightDynamicsControlSnapshot controls = RepresentativeControls();
            CockpitElectricalPowerInput power =
                CockpitElectricalPowerInput.Unavailable;
            Assert.That(model.Reset(state, controls, power), Is.True);

            for (int i = 0; i < 32; i++)
                model.Step(state, controls, power, 1f / 72f);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4096; i++)
            {
                state.simulationTimeSeconds += 1.0 / 72.0;
                model.Step(state, controls, power, 1f / 72f);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.Zero);
        }

        private static FlightDynamicsState RepresentativeState()
        {
            return new FlightDynamicsState
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
                velocityUnityMetersPerSecond = new Vector3(2f, 2.2f, 46f),
                angularVelocityBodyDegreesPerSecond =
                    new Vector3(0.5f, 2.2f, -1.1f)
            };
        }

        private static FlightDynamicsControlSnapshot RepresentativeControls()
        {
            return new FlightDynamicsControlSnapshot
            {
                aileron = 0.15f,
                elevator = -0.08f,
                rudder = 0.12f,
                throttle = 0.78f,
                mixture = 1f,
                trim = -0.05f,
                flaps = 0.33f,
                leftToeBrake = 0.1f,
                rightToeBrake = 0.2f
            };
        }
    }
}
#endif
