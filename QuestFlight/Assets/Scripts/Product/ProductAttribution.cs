using UnityEngine;

namespace QuestFlight.Product
{
    /// <summary>Serialized, runtime-accessible Credits/About metadata for the product scene.</summary>
    [DisallowMultipleComponent]
    public sealed class ProductAttribution : MonoBehaviour
    {
        public const string AircraftCredit =
            "Cessna172 3D model by KOG_THORNS (@ioai25312), licensed CC BY 4.0.";
        public const string AircraftLicenseUrl = "https://creativecommons.org/licenses/by/4.0/";
        public const string AircraftSourceUrl =
            "https://sketchfab.com/3d-models/cessna172-d1b15841c29c43d0862667300bad55a4";
        public const string OsmCredit = "© OpenStreetMap contributors";
        public const string OsmLicenseUrl = "https://www.openstreetmap.org/copyright";
        public const string BrandingCaveat =
            "No endorsement by Cessna or Textron Aviation is claimed; trademarks remain with their owners.";

        public string CreditsText =>
            AircraftCredit + " " + AircraftLicenseUrl + " Source: " + AircraftSourceUrl + "\n" +
            OsmCredit + " — ODbL attribution: " + OsmLicenseUrl + "\n" + BrandingCaveat;
    }
}
