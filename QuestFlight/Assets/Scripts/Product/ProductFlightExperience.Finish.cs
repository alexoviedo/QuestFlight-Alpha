using System;
using System.Globalization;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.SystemsV2;
using UnityEngine;

namespace QuestFlight.Product
{
    public sealed partial class ProductFlightExperience
    {
        [SerializeField] private ProductionCockpitSystemsController cockpitSystems;
        public void ConfigureReadiness(ProductionCockpitSystemsController systems){cockpitSystems=systems;}
        public bool HasHistory=>Current.flights.Count>0;
        public bool HasOlderFlight=>historyIndex+1<Current.flights.Count;
        public bool HasNewerFlight=>historyIndex>0;
        public int RecommendedLesson=>HasHistory?Mathf.Clamp(Current.flights[historyIndex].recommendedLesson,0,5):0;
        public string RecommendedLessonName=>modes.AuthoredTraining.Lessons[RecommendedLesson].displayName;
        public static string FriendlyTimestamp(string value)=>DateTime.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var date)?date.ToString("dd MMM · HH:mm 'UTC'",CultureInfo.InvariantCulture):value;
        public string ChecklistText
        {
            get
            {
                if(cockpitSystems==null||!cockpitSystems.HasValidSystemsV2Snapshot)return "Aircraft indications are not ready. Wait for the flight to load.";
                var s=cockpitSystems.CurrentSystemsV2Snapshot;
                string fuel=!s.Has(CockpitSystemsAvailabilityV2.FuelSelectorControl)?"unavailable":s.fuelSelectorPosition.ToString().ToUpperInvariant();
                string ignition=!s.Has(CockpitSystemsAvailabilityV2.IgnitionAndStarterControl)?"unavailable":s.ignitionSwitchPosition.ToString().ToUpperInvariant();
                string bus=!s.Has(CockpitSystemsAvailabilityV2.ElectricalBusPower)?"unavailable":s.avionicsBusPowered?"powered":"off";
                string flap=!s.Has(CockpitSystemsAvailabilityV2.ActualFlapPosition)?"unavailable":s.actualFlapDegrees.ToString("0")+"°";
                return $"OBSERVED NOW\nFuel selector: {fuel} · verify BOTH\nIgnition: {ignition} · verify BOTH\nAvionics: {bus} · Flaps: {flap}\n\nPILOT CHECKS\nSet flaps for the active lesson; check controls, trim and mixture. Look outside for traffic and confirm runway alignment.\n\nThis is a simulator reminder, not a complete aircraft checklist. Pilot checks are never marked complete automatically.";
            }
        }
    }
}
