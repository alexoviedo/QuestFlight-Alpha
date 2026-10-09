using System;
using NUnit.Framework;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;

namespace QuestFlight.Editor.Tests
{
    public sealed class CockpitAvionicsV2EditModeTests
    {
        [Test]
        public void UnpoweredOrUnavailableAvionics_AreBlank()
        {
            CockpitAvionicsV2Formatter formatter =
                new CockpitAvionicsV2Formatter();
            CockpitAvionicsV2Snapshot snapshot = CompleteSnapshot();
            snapshot.avionicsBusPowered = false;

            Assert.That(formatter.Format(snapshot), Is.False);
            Assert.That(new string(formatter.Characters).Trim(), Is.Empty);

            snapshot.avionicsBusPowered = true;
            snapshot.availability =
                CockpitAvionicsV2Availability.ElectricalPower;
            Assert.That(formatter.Format(snapshot), Is.False);
            Assert.That(new string(formatter.Characters).Trim(), Is.Empty);
        }

        [Test]
        public void RepresentedPanels_UseFixedDeterministicFields()
        {
            CockpitAvionicsV2Formatter formatter =
                new CockpitAvionicsV2Formatter();
            CockpitAvionicsV2Snapshot snapshot = CompleteSnapshot();

            Assert.That(formatter.Format(snapshot), Is.True);
            Assert.That(Row(formatter, 0), Does.StartWith("GPS KBDU BRG 123"));
            Assert.That(Row(formatter, 3), Is.EqualTo("C1 118.300 >125.700 "));
            Assert.That(Row(formatter, 4), Is.EqualTo("C2 121.500 >132.100 "));
            Assert.That(Row(formatter, 6), Does.StartWith("XPDR 1200 ALT"));
            Assert.That(Row(formatter, 7), Does.StartWith("AUD C1 NAV"));
        }

        [Test]
        public void InvalidField_BlanksOnlyItsAuthorityGroup()
        {
            CockpitAvionicsV2Formatter formatter =
                new CockpitAvionicsV2Formatter();
            CockpitAvionicsV2Snapshot snapshot = CompleteSnapshot();
            snapshot.com1ActiveKHz = 117999;

            Assert.That(formatter.Format(snapshot), Is.True);
            Assert.That(Row(formatter, 3).Trim(), Is.Empty);
            Assert.That(Row(formatter, 4).Trim(), Is.Empty);
            Assert.That(Row(formatter, 0), Does.StartWith("GPS KBDU"));
        }

        [Test]
        public void Formatter_SteadyStateAllocatesZeroManagedBytes()
        {
            CockpitAvionicsV2Formatter formatter =
                new CockpitAvionicsV2Formatter();
            CockpitAvionicsV2Snapshot snapshot = CompleteSnapshot();
            formatter.Format(snapshot);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 4096; index++)
                formatter.Format(snapshot);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.EqualTo(0));
        }

        private static CockpitAvionicsV2Snapshot CompleteSnapshot()
        {
            return new CockpitAvionicsV2Snapshot
            {
                isValid = true,
                availability =
                    CockpitAvionicsV2Availability.ElectricalPower |
                    CockpitAvionicsV2Availability.GpsNavigation |
                    CockpitAvionicsV2Availability.ComRadios |
                    CockpitAvionicsV2Availability.Dme |
                    CockpitAvionicsV2Availability.Transponder |
                    CockpitAvionicsV2Availability.AudioPanel,
                avionicsBusPowered = true,
                gpsDestinationIdentAscii =
                    (uint)'K' | ((uint)'B' << 8) |
                    ((uint)'D' << 16) | ((uint)'U' << 24),
                gpsBearingDegrees = 123f,
                gpsDistanceNauticalMiles = 12.3f,
                gpsGroundTrackDegrees = 118f,
                gpsGroundTrackAvailable = true,
                gpsAltitudeMslFeet = 5340f,
                com1ActiveKHz = 118300,
                com1StandbyKHz = 125700,
                com2ActiveKHz = 121500,
                com2StandbyKHz = 132100,
                dmeDistanceNauticalMiles = 11.7f,
                dmeGroundSpeedKnots = 92f,
                dmeTimeToStationSeconds = 420,
                transponderCodeBcd = 0x1200,
                transponderMode = CockpitAvionicsV2TransponderMode.Altitude,
                audioSelectionMask = 0x5
            };
        }

        private static string Row(
            CockpitAvionicsV2Formatter formatter,
            int row)
        {
            return new string(
                formatter.Characters,
                row * CockpitAvionicsV2Formatter.Columns,
                CockpitAvionicsV2Formatter.Columns);
        }
    }
}
