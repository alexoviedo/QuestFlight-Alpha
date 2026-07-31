using System;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using UnityEditor;

namespace QuestFlight.Editor
{
    /// <summary>
    /// Asset and budget contract for the single-draw V2 avionics presenter.
    /// It accepts only the represented source assemblies and the existing
    /// opaque cockpit readout material.
    /// </summary>
    public static class CockpitAvionicsV2EditorContract
    {
        public const string SourceGlbPath =
            CockpitInstrumentV2EditorContract.SourceGlbPath;
        public const string ExpectedSourceGuid =
            CockpitInstrumentV2EditorContract.ExpectedSourceGuid;
        public const string ExpectedSourceSha256 =
            CockpitInstrumentV2EditorContract.ExpectedSourceSha256;
        public const string ExistingReadoutMaterialPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/Materials/ProductionInstrumentDisplayReadout.mat";
        public const string ExistingReadableBasePath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/Textures/C172_StaticDisplayReadable_BaseColor.png";
        public const string ExistingReadableEmissionPath =
            "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
            "Reflection/Cockpit/Textures/C172_StaticDisplayReadable_Emission.png";

        public static readonly string[] PositivelyRepresentedAssemblies =
        {
            "Static_Display_New_Mat_01_0_New_Mat_01_0",
            "Static_TXT_New_Mat_01_0_New_Mat_01_0",
            "ButtonPlate_New_Mat_01_0_New_Mat_01_0",
            "Buttons_New_Mat_01_0_New_Mat_01_0"
        };

        public static bool ValidateLayout(
            CockpitAvionicsV2Layout layout,
            out string error)
        {
            if (layout == null)
            {
                error = "V2 avionics layout is missing.";
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
                error = "V2 avionics layout is not tied to the frozen C172 source.";
                return false;
            }
            if (!string.Equals(
                    AssetDatabase.GetAssetPath(layout.ExistingReadoutMaterial),
                    ExistingReadoutMaterialPath,
                    StringComparison.Ordinal))
            {
                error = "V2 avionics do not reuse the existing readout material.";
                return false;
            }
            if (layout.EstimatedTextureBytes > 1024 * 1024)
            {
                error = "V2 avionics exceed the 1 MiB texture ceiling.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public const string FailClosedContract =
            "GPS, COM1/COM2, DME, transponder, and audio indications are blank " +
            "when the electrical bus, field authority, source identity, or " +
            "fixed glyph mapping is unavailable. No invented navigation state.";
    }
}
