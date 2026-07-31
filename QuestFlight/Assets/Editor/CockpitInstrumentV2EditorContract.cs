using System;
using System.IO;
using System.Security.Cryptography;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using UnityEditor;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Frozen source evidence and fail-closed eligibility rules for the V2
    /// instrument partition. This class deliberately does not edit a prefab or
    /// scene; the lead must integrate generated assets explicitly.
    /// </summary>
    public static class CockpitInstrumentV2EditorContract
    {
        public const string SourceGlbPath =
            "Assets/Resources/QuestFlightLab/ImportedAssets/" +
            "Cessna172KogThorns/cessna172.glb";
        public const string SourceMeterMaterialPath =
            "Assets/Production/Materials/Cockpit/" +
            "Cockpit_Meter_01_MAT_7515756384451954381.mat";
        public const string SourceMeshName =
            "Cessna_Interior_Meter_01_MAT_0_Meter_01_MAT_0";
        public const string ExpectedSourceGuid =
            "d312be0fd0ba928458dd86cbe967956b";
        public const string ExpectedSourceSha256 =
            "a22acb34b7611e35dec891c6977e66383c470e6f8f36f014dba54df4f9a83872";

        public static readonly int[] AltimeterFaceSeeds =
            { 2667, 2621, 2713 };
        public const int ClockFaceSeed = 2849;
        public const int FlapIndicatorFaceSeed = 2759;
        public const int DirectionalGyroFaceSeed = 2891;
        public static readonly int[] MagneticCompassSeeds = { 77, 78 };
        public const int BrakePressureFaceSeed = 2906;

        public static bool ValidateFrozenSource(out string error)
        {
            string guid = AssetDatabase.AssetPathToGUID(SourceGlbPath);
            if (!string.Equals(
                    guid, ExpectedSourceGuid, StringComparison.Ordinal))
            {
                error = "The frozen C172 source GUID changed.";
                return false;
            }

            string fullPath = Path.GetFullPath(SourceGlbPath);
            if (!File.Exists(fullPath))
            {
                error = "The frozen C172 source is missing.";
                return false;
            }
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(fullPath))
            {
                string actual = BitConverter.ToString(
                    sha.ComputeHash(stream)).Replace("-", string.Empty)
                    .ToLowerInvariant();
                if (!string.Equals(
                        actual, ExpectedSourceSha256,
                        StringComparison.Ordinal))
                {
                    error = "The frozen C172 source hash changed.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        public static bool ValidateLayoutIdentity(
            CockpitInstrumentV2Layout layout,
            out string error)
        {
            if (layout == null)
            {
                error = "V2 instrument layout is missing.";
                return false;
            }
            if (!layout.Validate(out error))
                return false;
            if (!string.Equals(
                    layout.SourceAssetGuid, ExpectedSourceGuid,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    layout.SourceSha256, ExpectedSourceSha256,
                    StringComparison.Ordinal))
            {
                error = "V2 instrument layout is not tied to the frozen C172 source.";
                return false;
            }
            if (!string.Equals(
                    AssetDatabase.GetAssetPath(layout.ExistingSourceMaterial),
                    SourceMeterMaterialPath,
                    StringComparison.Ordinal))
            {
                error = "V2 instruments do not reuse the frozen meter material.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public static bool IsConditionallyEligible(
            CockpitInstrumentV2Function function)
        {
            switch (function)
            {
                case CockpitInstrumentV2Function.AltimeterHundredsNeedleDegrees:
                case CockpitInstrumentV2Function.AltimeterThousandsNeedleDegrees:
                case CockpitInstrumentV2Function.AltimeterTenThousandsNeedleDegrees:
                case CockpitInstrumentV2Function.AltimeterBarometricDrumInHg:
                case CockpitInstrumentV2Function.AttitudeBankDegrees:
                case CockpitInstrumentV2Function.AttitudePitchDegrees:
                case CockpitInstrumentV2Function.DirectionalGyroHeadingDegrees:
                case CockpitInstrumentV2Function.MagneticCompassHeadingDegrees:
                case CockpitInstrumentV2Function.ActualFlapDegrees:
                case CockpitInstrumentV2Function.AverageBrakePressurePsi:
                case CockpitInstrumentV2Function.ClockHourHandDegrees:
                case CockpitInstrumentV2Function.ClockMinuteHandDegrees:
                case CockpitInstrumentV2Function.ClockSecondHandDegrees:
                    return true;
                default:
                    return false;
            }
        }

        public const string ExcludedAuthorityContract =
            "Fuel, oil, tachometer, GYRO/LOC, MACH/AS hybrids, duplicate VSI, " +
            "and unlabeled/fantasy faces remain static unless positive identity, " +
            "separable topology, units, calibration, and authoritative state are proven.";
    }
}
