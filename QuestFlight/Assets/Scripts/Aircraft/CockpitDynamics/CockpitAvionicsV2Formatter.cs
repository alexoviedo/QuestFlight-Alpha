using System;

namespace QuestFlightLab.Aircraft.CockpitDynamics.V2
{
    /// <summary>
    /// Fixed-capacity, allocation-free formatter for the represented avionics
    /// apertures. It intentionally exposes no general-purpose rich text.
    /// </summary>
    public sealed class CockpitAvionicsV2Formatter
    {
        public const int Columns = 20;
        public const int Rows = 8;
        public const int CharacterCapacity = Columns * Rows;

        private readonly char[] _characters = new char[CharacterCapacity];

        public char[] Characters => _characters;

        public bool Format(in CockpitAvionicsV2Snapshot snapshot)
        {
            Clear();
            if (!snapshot.isValid ||
                !snapshot.Has(CockpitAvionicsV2Availability.ElectricalPower) ||
                !snapshot.avionicsBusPowered)
                return false;

            bool any = false;
            if (snapshot.Has(CockpitAvionicsV2Availability.GpsNavigation) &&
                GpsIsFinite(snapshot))
            {
                FormatGps(snapshot);
                any = true;
            }
            if (snapshot.Has(CockpitAvionicsV2Availability.ComRadios) &&
                FrequenciesAreValid(snapshot))
            {
                FormatComRadios(snapshot);
                any = true;
            }
            if (snapshot.Has(CockpitAvionicsV2Availability.Dme) &&
                DmeIsValid(snapshot))
            {
                FormatDme(snapshot);
                any = true;
            }
            if (snapshot.Has(CockpitAvionicsV2Availability.Transponder) &&
                TransponderIsValid(snapshot))
            {
                FormatTransponder(snapshot);
                any = true;
            }
            if (snapshot.Has(CockpitAvionicsV2Availability.AudioPanel))
            {
                FormatAudio(snapshot.audioSelectionMask);
                any = true;
            }
            return any;
        }

        private void FormatGps(CockpitAvionicsV2Snapshot snapshot)
        {
            int row0 = Row(0);
            PutLiteral(row0, "GPS ");
            PutPackedIdentifier(row0 + 4, snapshot.gpsDestinationIdentAscii);
            PutLiteral(row0 + 9, "BRG ");
            PutUnsigned(row0 + 13, 3, Round(snapshot.gpsBearingDegrees), true);

            int row1 = Row(1);
            PutLiteral(row1, "DST ");
            PutTenths(row1 + 4, 5, snapshot.gpsDistanceNauticalMiles);
            PutLiteral(row1 + 10, "TRK ");
            if (snapshot.gpsGroundTrackAvailable)
                PutUnsigned(row1 + 14, 3, Round(snapshot.gpsGroundTrackDegrees) % 360, true);
            else
                PutLiteral(row1 + 14, "---");

            int row2 = Row(2);
            PutLiteral(row2, "ALT ");
            PutSigned(row2 + 4, 6, Round(snapshot.gpsAltitudeMslFeet));
        }

        private void FormatComRadios(CockpitAvionicsV2Snapshot snapshot)
        {
            int row3 = Row(3);
            PutLiteral(row3, "C1 ");
            PutFrequency(row3 + 3, snapshot.com1ActiveKHz);
            PutLiteral(row3 + 11, ">");
            PutFrequency(row3 + 12, snapshot.com1StandbyKHz);

            int row4 = Row(4);
            PutLiteral(row4, "C2 ");
            PutFrequency(row4 + 3, snapshot.com2ActiveKHz);
            PutLiteral(row4 + 11, ">");
            PutFrequency(row4 + 12, snapshot.com2StandbyKHz);
        }

        private void FormatDme(CockpitAvionicsV2Snapshot snapshot)
        {
            int row5 = Row(5);
            PutLiteral(row5, "DME ");
            PutTenths(row5 + 4, 5, snapshot.dmeDistanceNauticalMiles);
            PutLiteral(row5 + 10, "KT ");
            PutUnsigned(
                row5 + 13, 3, Round(snapshot.dmeGroundSpeedKnots), false);
            PutLiteral(row5 + 17, " ");
            int minutes = Math.Min(
                99, Math.Max(0, snapshot.dmeTimeToStationSeconds / 60));
            PutUnsigned(row5 + 18, 2, minutes, true);
        }

        private void FormatTransponder(CockpitAvionicsV2Snapshot snapshot)
        {
            int row6 = Row(6);
            PutLiteral(row6, "XPDR ");
            for (int digit = 0; digit < 4; digit++)
            {
                int shift = (3 - digit) * 4;
                _characters[row6 + 5 + digit] =
                    (char)('0' + ((snapshot.transponderCodeBcd >> shift) & 0xF));
            }
            PutLiteral(row6 + 10, ModeLabel(snapshot.transponderMode));
        }

        private void FormatAudio(ushort mask)
        {
            int row7 = Row(7);
            PutLiteral(row7, "AUD ");
            int cursor = row7 + 4;
            if ((mask & 0x1) != 0) cursor = PutToken(cursor, "C1");
            if ((mask & 0x2) != 0) cursor = PutToken(cursor, "C2");
            if ((mask & 0x4) != 0) cursor = PutToken(cursor, "NAV");
            if ((mask & 0x8) != 0) PutToken(cursor, "MKR");
        }

        private int PutToken(int offset, string value)
        {
            if (offset >= CharacterCapacity) return offset;
            if (offset > 0 && _characters[offset - 1] != ' ')
                _characters[offset++] = ' ';
            PutLiteral(offset, value);
            return offset + value.Length;
        }

        private void PutFrequency(int offset, int kHz)
        {
            int mhz = kHz / 1000;
            int fractional = kHz % 1000;
            PutUnsigned(offset, 3, mhz, true);
            _characters[offset + 3] = '.';
            PutUnsigned(offset + 4, 3, fractional, true);
        }

        private void PutTenths(int offset, int width, float value)
        {
            int tenths = Math.Max(0, Round(value * 10f));
            int integer = tenths / 10;
            int fractional = tenths % 10;
            PutUnsigned(offset, width - 2, integer, false);
            _characters[offset + width - 2] = '.';
            _characters[offset + width - 1] = (char)('0' + fractional);
        }

        private void PutSigned(int offset, int width, int value)
        {
            int magnitude = Math.Abs(value);
            _characters[offset] = value < 0 ? '-' : ' ';
            PutUnsigned(offset + 1, width - 1, magnitude, false);
        }

        private void PutUnsigned(
            int offset,
            int width,
            int value,
            bool zeroPad)
        {
            value = Math.Max(0, value);
            for (int index = width - 1; index >= 0; index--)
            {
                _characters[offset + index] = (char)('0' + value % 10);
                value /= 10;
            }
            if (zeroPad) return;
            for (int index = 0; index < width - 1; index++)
            {
                if (_characters[offset + index] != '0') break;
                _characters[offset + index] = ' ';
            }
        }

        private void PutPackedIdentifier(int offset, uint packed)
        {
            for (int index = 0; index < 4; index++)
            {
                char character = (char)((packed >> (index * 8)) & 0x7F);
                _characters[offset + index] =
                    character >= 32 && character <= 126 ? character : ' ';
            }
        }

        private void PutLiteral(int offset, string value)
        {
            int count = Math.Min(value.Length, CharacterCapacity - offset);
            for (int index = 0; index < count; index++)
                _characters[offset + index] = value[index];
        }

        private void Clear()
        {
            for (int index = 0; index < _characters.Length; index++)
                _characters[index] = ' ';
        }

        private static int Row(int row) => row * Columns;

        private static int Round(float value)
        {
            return (int)Math.Round(
                value, MidpointRounding.AwayFromZero);
        }

        private static string ModeLabel(
            CockpitAvionicsV2TransponderMode mode)
        {
            switch (mode)
            {
                case CockpitAvionicsV2TransponderMode.Off:
                    return "OFF";
                case CockpitAvionicsV2TransponderMode.Standby:
                    return "STBY";
                case CockpitAvionicsV2TransponderMode.On:
                    return "ON";
                case CockpitAvionicsV2TransponderMode.Altitude:
                    return "ALT";
                default:
                    return string.Empty;
            }
        }

        private static bool GpsIsFinite(CockpitAvionicsV2Snapshot snapshot)
        {
            return IsFiniteInRange(snapshot.gpsBearingDegrees, 0f, 360f) &&
                   IsFiniteInRange(
                       snapshot.gpsDistanceNauticalMiles, 0f, 999.9f) &&
                   IsFiniteInRange(
                       snapshot.gpsGroundTrackDegrees, 0f, 360f) &&
                   IsFiniteInRange(
                       snapshot.gpsAltitudeMslFeet, -99999f, 99999f);
        }

        private static bool FrequenciesAreValid(
            CockpitAvionicsV2Snapshot snapshot)
        {
            return IsComFrequency(snapshot.com1ActiveKHz) &&
                   IsComFrequency(snapshot.com1StandbyKHz) &&
                   IsComFrequency(snapshot.com2ActiveKHz) &&
                   IsComFrequency(snapshot.com2StandbyKHz);
        }

        private static bool IsComFrequency(int kHz)
        {
            return kHz >= 118000 && kHz <= 136990;
        }

        private static bool DmeIsValid(CockpitAvionicsV2Snapshot snapshot)
        {
            return IsFiniteInRange(
                       snapshot.dmeDistanceNauticalMiles, 0f, 999.9f) &&
                   IsFiniteInRange(
                       snapshot.dmeGroundSpeedKnots, 0f, 999f) &&
                   snapshot.dmeTimeToStationSeconds >= 0;
        }

        private static bool TransponderIsValid(
            CockpitAvionicsV2Snapshot snapshot)
        {
            for (int digit = 0; digit < 4; digit++)
                if (((snapshot.transponderCodeBcd >> (digit * 4)) & 0xF) > 7)
                    return false;
            return snapshot.transponderMode >=
                       CockpitAvionicsV2TransponderMode.Off &&
                   snapshot.transponderMode <=
                       CockpitAvionicsV2TransponderMode.Altitude;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFiniteInRange(
            float value,
            float inclusiveMinimum,
            float exclusiveMaximum)
        {
            return IsFinite(value) &&
                   value >= inclusiveMinimum &&
                   value < exclusiveMaximum;
        }
    }
}
