using System;
using System.IO;
using System.Security.Cryptography;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

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
        // The earlier 77/78 note identified two triangles on the attitude
        // sphere, not the represented compass card. Texture-ownership and
        // pilot-view projection audits identify this complete, independently
        // connected curved card instead.
        public const int MagneticCompassCardSeed = 2339;
        public const int MagneticCompassAuxiliaryCardSeed = 2279;
        public const int ExpectedMagneticCompassCardVertexCount = 84;
        public const int ExpectedMagneticCompassCardTriangleCount = 130;
        public const int ExpectedMagneticCompassAuxiliaryVertexCount = 42;
        public const int ExpectedMagneticCompassAuxiliaryTriangleCount = 60;
        public const float MagneticCompassAuthoredHeadingDegrees = 330f;
        public const int BrakePressureFaceSeed = 2906;
        public static readonly int[] AttitudeSphereComponentSeeds =
            { 70, 525, 697, 1153, 1355 };
        public const int ExpectedAttitudeSphereVertexCount = 998;
        public const int ExpectedAttitudeSphereTriangleCount = 1740;
        public const float MaximumAttitudeSphereFitRms = 0.00006f;

        // Six authored blanking discs are appended to frozen source vertices.
        // Only the independently partitioned attitude sphere uses the z selector.
        public static bool ValidateSourcePresentationSelectors(Mesh mesh, out string error)
        {
            var selectors = new List<Vector4>();
            if (mesh != null) mesh.GetUVs(3, selectors);
            const int blankVertices = 864;
            int attitude = 0;
            if (mesh == null || selectors.Count != mesh.vertexCount || selectors.Count < blankVertices)
            { error = "Source presentation selector channel is missing."; return false; }
            for (int i = 0; i < selectors.Count; i++)
            {
                Vector4 value = selectors[i];
                if (i >= selectors.Count - blankVertices)
                {
                    if (value != new Vector4(1, 3, 0, 0))
                    { error = "Trainer blanking plate selector ownership changed."; return false; }
                }
                else if (value == new Vector4(0, 0, 1, 0)) attitude++;
                else if (value != Vector4.zero)
                { error = "Unreviewed atlas sampling in frozen source vertices."; return false; }
            }
            error = attitude == ExpectedAttitudeSphereVertexCount ? string.Empty : "Attitude selector partition changed.";
            return string.IsNullOrEmpty(error);
        }

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
                    CockpitInstrumentV2Authoring.MaterialPath,
                    StringComparison.Ordinal))
            {
                error =
                    "V2 instruments do not use the one audited opaque batch material.";
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
                case CockpitInstrumentV2Function.IndicatedAirspeedNeedleDegrees:
                case CockpitInstrumentV2Function.IndicatedVerticalSpeedNeedleDegrees:
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
                case CockpitInstrumentV2Function.EngineRpmNeedleDegrees:
                case CockpitInstrumentV2Function.TurnCoordinatorBankDegrees:
                case CockpitInstrumentV2Function.SlipSkidNormalized:
                case CockpitInstrumentV2Function.ClockHourHandDegrees:
                case CockpitInstrumentV2Function.ClockMinuteHandDegrees:
                case CockpitInstrumentV2Function.ClockSecondHandDegrees:
                    return true;
                default:
                    return false;
            }
        }

        public const string ExcludedAuthorityContract =
            "Fuel, oil, GYRO/LOC, MACH/AS hybrids, duplicate VSI, " +
            "and unlabeled/fantasy faces remain static unless positive identity, " +
            "separable topology, units, calibration, and authoritative state are proven.";
    }
}
