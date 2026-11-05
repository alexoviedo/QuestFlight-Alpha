using UnityEngine;
using QuestFlight.Product.Training;

namespace QuestFlight.Product
{
    public sealed partial class ProductAppNavigation
    {
        private static readonly string[] SgsInstrumentNames={"Airspeed","Altimeter","Pressure variometer","Yaw string","Stick and rudder","Trim lever","Airbrake / wheel brake","Tow release"};
        private static readonly string[] SgsInstrumentHelp={
            "Airspeed is indicated in knots. Follow the current reference lesson target; keep looking outside.",
            "The altimeter indicates pressure altitude with its barometric setting. It does not show terrain clearance.",
            "The pressure variometer indicates climb or sink. An ideal total-energy tone, when explicitly enabled for development, is a separate training aid.",
            "Use the yaw string with outside references to coordinate flight. Correct gently with the rudder.",
            "Grip the stick to control pitch and roll. The assigned steering thumbstick controls rudder. Release menu inputs before flying.",
            "Grab the separate trim lever on the left wall below the brake handle to balance pitch forces in the reference configuration. The airbrake thumbstick does not control trim.",
            "The combined lever opens airbrakes through most of its travel, then applies the wheel brake near full travel. The assigned vertical stick also moves this lever.",
            "Pull the red handle, or press A while flying, to release the towline. Menu A selects only; it must be released before flight input is armed. Reconnect is an explicit stopped crew-preparation action."};
        public static int SgsReferenceCount=>SgsInstrumentNames.Length;
        public static string SgsReference(int index)=>SgsInstrumentNames[Mathf.Clamp(index,0,SgsReferenceCount-1)]+"\n"+SgsInstrumentHelp[Mathf.Clamp(index,0,SgsReferenceCount-1)];
        private int RelevantInstrumentIndex=>!modes.IsGlider?Mathf.Max(0,System.Array.IndexOf(InstrumentReferences,training.CurrentStage.reference)):training.CurrentStage.task switch
        {
            TrainingTask.SoaringPreparation=>4,TrainingTask.SoaringCrewReady or TrainingTask.SoaringGroundAbort or TrainingTask.SoaringAirbrake=>6,
            TrainingTask.SoaringRelease=>7,TrainingTask.SoaringTrim=>5,TrainingTask.SoaringSlip=>3,
            TrainingTask.SoaringLift or TrainingTask.SoaringEnergyGain or TrainingTask.SoaringLiftExit=>2,_=>0
        };
        private int ActiveInstrumentCount=>modes.IsGlider?SgsReferenceCount:InstrumentReferences.Length;
        private string ActiveInstrumentName=>modes.IsGlider?SgsInstrumentNames[instrumentHelpIndex]:InstrumentNames[instrumentHelpIndex];
        private string ActiveInstrumentExplanation=>modes.IsGlider?"Text reference (no cockpit highlight).\n"+training.BindingAwareInstruction(SgsInstrumentHelp[instrumentHelpIndex]):training.ExplainReference(InstrumentReferences[instrumentHelpIndex]);
        private void StartRecommendedLesson()
        {
            if(experience==null||!experience.TryRecommendedLesson(out bool glider,out string id)){Reject("Choose an aircraft and lesson from Home; this old record has no aircraft identity.");return;}
            experience.FinishFlight("Session replaced by recommended practice");ReturnMain();
            if(!modes.SelectAircraft(glider)){Reject("The recommended aircraft could not prepare. Choose it from Home.");return;}
            StartActivityById(0,id);
        }
        private string SgsSetupInstruction(int step)=>step switch
        {
            0=>"Use either thumbstick up/down and A to select, or point at a button and squeeze the index trigger. B/Y returns to the previous page.",
            1=>"Menu opens the flight pause menu. Resume is an explicit choice. Home offers SGS ground aerotow, airborne practice and soaring lessons.",
            2=>"Sit comfortably and look forward. Adjust the seat until you can read the instruments and reach the stick, trim, combined brake lever and red tow release.",
            3=>"Hold the stick grip and move it gently aft and forward, then center. Perform flight control checks during ground preparation; this guide does not certify them.",
            4=>"With the stick gripped, move it gently left and right, then center. Keep the aircraft paused while reviewing this guide.",
            5=>"Grab the trim lever for pitch trim. There is no engine throttle or mixture in this sailplane.",
            6=>$"The {ProductComfortPreferences.RudderHand} stick steers the rudder. The {ProductComfortPreferences.TrimHand} stick up/down moves the airbrake/wheel-brake lever. Release both sticks before selecting another page.",
            7=>"Most combined-lever travel opens airbrakes. Full travel adds the wheel brake. Pull the red handle or press A during flight to release tow. A in menus only selects.",
            _=>"Choose Ground aerotow for stopped checks, crew reconnect and launch clearance. Airborne practice starts in flight. Soaring lessons provide separate reference exercises. This is a provisional reference configuration."
        };
    }
}
