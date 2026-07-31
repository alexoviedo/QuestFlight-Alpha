using System;
using NUnit.Framework;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;

namespace QuestFlight.Editor.Tests
{
    public sealed class CockpitInstrumentV2EditModeTests
    {
        [Test]
        public void Altimeter_UsesPressureAltitudeAndExplicitBaroAuthority()
        {
            CockpitInstrumentV2Snapshot snapshot =
                Valid(CockpitInstrumentV2Availability.AirData |
                      CockpitInstrumentV2Availability.BarometricSetting);
            snapshot.pressureAltitudeFeet = 5432f;
            snapshot.barometricSettingInHg = 30.12f;

            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.AltimeterHundredsNeedleDegrees,
                    snapshot), Is.EqualTo(227.52f).Within(0.01f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.AltimeterThousandsNeedleDegrees,
                    snapshot), Is.EqualTo(202.752f).Within(0.01f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.AltimeterTenThousandsNeedleDegrees,
                    snapshot), Is.EqualTo(20.2752f).Within(0.01f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.AltimeterBarometricDrumInHg,
                    snapshot), Is.EqualTo(30.12f).Within(0.001f));
        }

        [Test]
        public void IndividualChannels_FailClosedWithoutTheirAuthority()
        {
            CockpitInstrumentV2Snapshot snapshot =
                Valid(CockpitInstrumentV2Availability.TrueHeading);
            snapshot.trueHeadingDegrees = 370f;
            snapshot.pitchDegrees = float.NaN;

            Assert.That(CockpitInstrumentV2Kinematics.TryEvaluate(
                CockpitInstrumentV2Function.DirectionalGyroHeadingDegrees,
                snapshot, out float heading), Is.True);
            Assert.That(heading, Is.EqualTo(10f));
            Assert.That(CockpitInstrumentV2Kinematics.TryEvaluate(
                CockpitInstrumentV2Function.AttitudePitchDegrees,
                snapshot, out _), Is.False);
            Assert.That(CockpitInstrumentV2Kinematics.TryEvaluate(
                CockpitInstrumentV2Function.MagneticCompassHeadingDegrees,
                snapshot, out _), Is.False);
        }

        [Test]
        public void MagneticCompass_UsesEastVariationConvention()
        {
            CockpitInstrumentV2Snapshot snapshot =
                Valid(CockpitInstrumentV2Availability.TrueHeading |
                      CockpitInstrumentV2Availability.MagneticVariation);
            snapshot.trueHeadingDegrees = 3f;
            snapshot.magneticVariationEastDegrees = 8f;

            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.MagneticCompassHeadingDegrees,
                    snapshot), Is.EqualTo(355f));
        }

        [Test]
        public void ActualFlapsBrakePressureAndUtcClock_AreDeterministic()
        {
            CockpitInstrumentV2Snapshot snapshot = Valid(
                CockpitInstrumentV2Availability.ActualFlapPosition |
                CockpitInstrumentV2Availability.BrakeHydraulics |
                CockpitInstrumentV2Availability.ClockUtc);
            snapshot.actualFlapDegrees = 17f;
            snapshot.leftBrakePressurePsi = 500f;
            snapshot.rightBrakePressurePsi = 300f;
            snapshot.clockUtcSeconds = 3 * 3600 + 15 * 60 + 30;

            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.ActualFlapDegrees, snapshot),
                Is.EqualTo(17f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.AverageBrakePressurePsi, snapshot),
                Is.EqualTo(400f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.ClockHourHandDegrees, snapshot),
                Is.EqualTo(97.75f).Within(0.001f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.ClockMinuteHandDegrees, snapshot),
                Is.EqualTo(93f).Within(0.001f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.ClockSecondHandDegrees, snapshot),
                Is.EqualTo(180f).Within(0.001f));
        }

        [Test]
        public void PureKinematics_SteadyStateAllocatesZeroManagedBytes()
        {
            CockpitInstrumentV2Snapshot snapshot =
                Valid(CockpitInstrumentV2Availability.Attitude);
            snapshot.pitchDegrees = 5f;
            snapshot.bankDegrees = -12f;
            CockpitInstrumentV2Kinematics.TryEvaluate(
                CockpitInstrumentV2Function.AttitudePitchDegrees,
                snapshot, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 4096; index++)
            {
                CockpitInstrumentV2Kinematics.TryEvaluate(
                    CockpitInstrumentV2Function.AttitudePitchDegrees,
                    snapshot, out _);
                CockpitInstrumentV2Kinematics.TryEvaluate(
                    CockpitInstrumentV2Function.AttitudeBankDegrees,
                    snapshot, out _);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.EqualTo(0));
        }

        private static CockpitInstrumentV2Snapshot Valid(
            CockpitInstrumentV2Availability availability)
        {
            return new CockpitInstrumentV2Snapshot
            {
                isValid = true,
                availability = availability
            };
        }

        private static float Evaluate(
            CockpitInstrumentV2Function function,
            CockpitInstrumentV2Snapshot snapshot)
        {
            Assert.That(CockpitInstrumentV2Kinematics.TryEvaluate(
                function, snapshot, out float value), Is.True);
            return value;
        }
    }
}
