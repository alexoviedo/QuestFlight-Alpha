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

        public bool HasRecommendedLesson=>TryRecommendedLesson(out _,out _);

        public bool TryRecommendedLesson(out bool glider,out string id)

        {

            glider=false;id=null;if(!HasHistory)return false;

            var record=Current.flights[historyIndex];

            if(record.aircraftId!="c172"&&record.aircraftId!="sgs233.reference")return false;

            if(string.IsNullOrEmpty(record.recommendedLessonId))return false;

            glider=record.aircraftId=="sgs233.reference";id=record.recommendedLessonId;return modes.AuthoredTraining.FindLesson(glider,id)!=null;

        }

        public bool HasRepeatActivity=>TryRepeatActivity(out _,out _,out _,out _);
        public bool TryRepeatActivity(out bool glider,out string recipe,out string lesson,out int runway)
        {
            glider=false;recipe=lesson=null;runway=0;if(!HasHistory)return false;
            var r=Current.flights[historyIndex];if(r.aircraftId!="c172"&&r.aircraftId!="sgs233.reference")return false;
            glider=r.aircraftId=="sgs233.reference";recipe=r.startRecipe;lesson=r.startLessonId;runway=r.startRunway;
            return recipe=="ground"&&glider&&runway>=0&&runway<modes.AuthoredFlightCoordinator.soaringOperations.Length || recipe=="free" || (recipe=="demo"||recipe=="showcase")&&!glider || recipe=="lesson"&&modes.AuthoredTraining.FindLesson(glider,lesson)!=null;
        }
        public int RecommendedLesson=>HasRecommendedLesson?System.Array.FindIndex(modes.AuthoredTraining.Lessons,l=>l.lessonId==Current.flights[historyIndex].recommendedLessonId):-1;

        public string RecommendedLessonName=>TryRecommendedLesson(out var glider,out var id)?modes.AuthoredTraining.FindLesson(glider,id).displayName:"choose aircraft and lesson";

        public static string FriendlyTimestamp(string value)=>DateTime.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var date)?date.ToString("dd MMM · HH:mm 'UTC'",CultureInfo.InvariantCulture):value;

        public string ChecklistText

        {

            get

            {

                if(modes.IsGlider)return "SGS REFERENCE STATUS\n"+(modes.AuthoredFlightCoordinator.Operation?.Instruction??"No ground checks are scored at an airborne checkpoint. Keep a safe unpowered landing plan.")+"\nMenu pauses controls and traffic. Return to cockpit to operate them. Historical release test is not an instruction to release again. This is not an operator-approved checklist.";

                if(cockpitSystems==null||!cockpitSystems.HasValidSystemsV2Snapshot)return "Aircraft indications are not ready. Wait for the flight to load.";

                var s=cockpitSystems.CurrentSystemsV2Snapshot;

                string fuel=!s.Has(CockpitSystemsAvailabilityV2.FuelSelectorControl)?"unavailable":s.fuelSelectorPosition.ToString().ToUpperInvariant();

                string ignition=!s.Has(CockpitSystemsAvailabilityV2.IgnitionAndStarterControl)?"unavailable":s.ignitionSwitchPosition.ToString().ToUpperInvariant();

                string bus=!s.Has(CockpitSystemsAvailabilityV2.ElectricalBusPower)?"unavailable":s.avionicsBusPowered?"powered":"off";

                string flap=!s.Has(CockpitSystemsAvailabilityV2.ActualFlapPosition)?"unavailable":s.actualFlapDegrees.ToString("0")+"°";

                return $"OBSERVED NOW\nFuel selector: {fuel} · verify BOTH\nIgnition: {ignition} · verify BOTH\nAvionics: {bus} · Flaps: {flap}\n\nPILOT CHECKS\nSet flaps for the active lesson; check controls and trim. Fuel, ignition, mixture and avionics are prepared by the checkpoint; this menu does not operate them. Look outside for traffic and confirm runway alignment.\n\nThis is a simulator reminder, not a complete aircraft checklist. Pilot checks are never marked complete automatically.";

            }

        }

    }

}
