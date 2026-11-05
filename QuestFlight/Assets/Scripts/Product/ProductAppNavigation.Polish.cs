using QuestFlight.Product.Training;

using UnityEngine;

namespace QuestFlight.Product

{

    public sealed partial class ProductAppNavigation

    {

        [SerializeField] private Transform setupLeftHand,setupRightHand,setupFrame,setupYoke,setupThrottle;

        private bool setupPassed,setupNegative,setupPositive,setupStarted;

        private Vector3 setupStart;

        private float setupValue;

        private int pendingActivity=-1;

        private Page mapReturn=Page.Briefing;

        private Page controlsReturn=Page.Settings;

        public void ConfigureSetup(Transform left,Transform right,Transform frame,Transform yoke,Transform throttle)

        {setupLeftHand=left;setupRightHand=right;setupFrame=frame;setupYoke=yoke;setupThrottle=throttle;}

        public void ShowFlightSummary()

        {ReturnMain();OpenMenu();CurrentPage=Page.Summary;focus=0;Consume();Refresh();}

        private bool BackFromPolishPage()

        {

            switch(CurrentPage)

            {

                case Page.Graphics:CurrentPage=Page.Settings;return true;

                case Page.Tools:CurrentPage=Page.App;return true;

                case Page.Controls:CurrentPage=controlsReturn;return true;

                case Page.Comfort:CurrentPage=Page.Settings;return true;

                case Page.Captions:CurrentPage=Page.Comfort;return true;

                case Page.ControlDiagram:CurrentPage=Page.Bindings;return true;

                case Page.Checklist:CurrentPage=Page.Kneeboard;return true;

                case Page.Bindings:case Page.InputPreview:CurrentPage=Page.Controls;return true;

                case Page.InstrumentHelp:CurrentPage=Page.App;return true;

                case Page.Map:CurrentPage=mapReturn;return true;

                case Page.Briefing:case Page.Kneeboard:case Page.Activities:case Page.History:case Page.Report:CurrentPage=Page.Tools;return true;

                case Page.Summary:CurrentPage=Page.Home;return true;

                default:return false;

            }

        }

        private bool InvokePolishAction(int action)

        {

            if(AdjustableAction(action)){adjustmentAction=action;NavigateTo(Page.SettingAdjustment);return true;}

            switch(action)

            {

                case (int)MenuActionId.Graphics:NavigateTo(Page.Graphics);break;

                case (int)MenuActionId.ExperimentalRendering:Reject("Experimental rendering is unavailable in this product profile.");break;

                case (int)MenuActionId.Tools:NavigateTo(Page.Tools);break;

                case (int)MenuActionId.Controls:controlsReturn=CurrentPage;NavigateTo(Page.Controls);break;

                case (int)MenuActionId.Comfort:NavigateTo(Page.Comfort);break;

                case (int)MenuActionId.Briefing:NavigateTo(Page.Briefing);break;

                case (int)MenuActionId.Map:mapReturn=CurrentPage;experience?.ShowHistoryOnMap(CurrentPage==Page.History||CurrentPage==Page.Summary);NavigateTo(Page.Map);focus=0;break;

                case (int)MenuActionId.Kneeboard:NavigateTo(Page.Kneeboard);break;

                case (int)MenuActionId.Activities:NavigateTo(Page.Activities);break;

                case (int)MenuActionId.History:NavigateTo(Page.History);break;

                case (int)MenuActionId.Report:NavigateTo(Page.Report);break;

                case (int)MenuActionId.FinishSummary:experience?.FinishFlight("Flight ended by pilot");ShowFlightSummary();return true;

                case (int)MenuActionId.GentlePreset:ProductComfortPreferences.RelaxedPreset();break;

                case (int)MenuActionId.Deadzone:Cycle("Touch.Deadzone",ProductComfortPreferences.RudderDeadzone,.04f,.04f,.24f);break;

                case (int)MenuActionId.RudderCurve:Cycle("Touch.Curve",ProductComfortPreferences.RudderCurve,.25f,1,2);break;

                case (int)MenuActionId.PitchTravel:Cycle("Touch.PitchTravel",ProductComfortPreferences.PitchTravel,.25f,.75f,1.5f);break;

                case (int)MenuActionId.RollTravel:Cycle("Touch.RollTravel",ProductComfortPreferences.RollTravel,.25f,.75f,1.5f);break;

                case (int)MenuActionId.TrimSpeed:Cycle("Touch.Trim",ProductComfortPreferences.TrimRate(true),.25f,.5f,1.5f);break;

                case (int)MenuActionId.ResetControls:ProductComfortPreferences.ResetControls();input.RequireTouchRelease();break;

                case (int)MenuActionId.TextSize:Cycle("TextScale",ProductComfortPreferences.TextScale,.1f,1,1.2f);break;

                case (int)MenuActionId.Contrast:PlayerPrefs.SetInt("QuestFlight.HighContrast",ProductComfortPreferences.HighContrast?0:1);PlayerPrefs.Save();

                    foreach(var surface in panel.GetComponentsInChildren<ProductUiSurface>(true))surface.RefreshStyle();break;

                case (int)MenuActionId.Haptics:Cycle("Haptics",ProductComfortPreferences.Haptics,.25f,0,1);break;

                case (int)MenuActionId.MenuSounds:PlayerPrefs.SetInt("QuestFlight.InterfaceFeedback",FeedbackEnabled?0:1);PlayerPrefs.Save();break;

                case (int)MenuActionId.OlderHistory:experience?.NextHistory(1);break;

                case (int)MenuActionId.NewerHistory:experience?.NextHistory(-1);break;

                case (int)MenuActionId.LandingActivity:StartActivity(5);return true;

                case (int)MenuActionId.CircuitActivity:StartActivity(4);return true;

                case (int)MenuActionId.ScenicActivity:StartActivity(-2);return true;

                case (int)MenuActionId.FreeActivity:StartActivity(-3);return true;

                case (int)MenuActionId.VisualIssue:experience?.Report("Visual issue");if(experience==null||!experience.LastReportSucceeded)Reject(experience?.ReportStatus??"Report storage unavailable.");break;

                case (int)MenuActionId.AudioIssue:experience?.Report("Audio / instruction issue");if(experience==null||!experience.LastReportSucceeded)Reject(experience?.ReportStatus??"Report storage unavailable.");break;

                case (int)MenuActionId.ControlIssue:experience?.Report("Controls / comfort issue");if(experience==null||!experience.LastReportSucceeded)Reject(experience?.ReportStatus??"Report storage unavailable.");break;

                case (int)MenuActionId.OtherIssue:experience?.Report("Other issue");if(experience==null||!experience.LastReportSucceeded)Reject(experience?.ReportStatus??"Report storage unavailable.");break;

                case (int)MenuActionId.ExternalTrim:Cycle("Gamepad.Trim",ProductComfortPreferences.TrimRate(false),.25f,.5f,1.5f);break;

                case (int)MenuActionId.TrafficRadio:PlayerPrefs.SetInt("QuestFlight.TrafficRadio",PlayerPrefs.GetInt("QuestFlight.TrafficRadio",1)==0?1:0);PlayerPrefs.Save();break;

                case (int)MenuActionId.Bindings:NavigateTo(Page.Bindings);focus=0;break;

                case (int)MenuActionId.InputPreview:NavigateTo(Page.InputPreview);focus=0;break;

                case (int)MenuActionId.SteeringHand:ProductComfortPreferences.SetFlag("LeftStickSteering",!ProductComfortPreferences.LeftStickSteering);input.RequireTouchRelease();break;

                case (int)MenuActionId.CombinedBrakes:ProductComfortPreferences.SetFlag("CombinedBrakes",!ProductComfortPreferences.CombinedBrakes);input.RequireTouchRelease();break;

                case (int)MenuActionId.ReverseTrim:ProductComfortPreferences.SetFlag("ReverseTrim",!ProductComfortPreferences.ReverseTrim);input.RequireTouchRelease();break;

                case (int)MenuActionId.Instruments:training.RecordHelpRequest();instrumentHelpIndex=CurrentPage==Page.Explanation?RelevantInstrumentIndex:0;NavigateTo(Page.InstrumentHelp);focus=0;ShowInstrumentHelp();break;

                case (int)MenuActionId.NextInstrument:instrumentHelpIndex=(instrumentHelpIndex+1)%ActiveInstrumentCount;ShowInstrumentHelp();break;

                case (int)MenuActionId.PreviousInstrument:instrumentHelpIndex=(instrumentHelpIndex+ActiveInstrumentCount-1)%ActiveInstrumentCount;ShowInstrumentHelp();break;

                case (int)MenuActionId.LeftSteeringPreset:ProductComfortPreferences.ResetControls();ProductComfortPreferences.SetFlag("LeftStickSteering",true);input.RequireTouchRelease();break;

                case (int)MenuActionId.RetrySave:experience?.RetryHistorySave();if(!string.IsNullOrEmpty(experience?.SaveStatus))Reject(experience.SaveStatus);break;

                case (int)MenuActionId.CaptionSettings:NavigateTo(Page.Captions);focus=0;break;

                case (int)MenuActionId.CaptionBackground:ProductComfortPreferences.SetFlag("SolidCaptions",!ProductComfortPreferences.SolidCaptions);break;

                case (int)MenuActionId.ControlDiagram:NavigateTo(Page.ControlDiagram);focus=0;break;

                case (int)MenuActionId.Checklist:NavigateTo(Page.Checklist);focus=0;break;

                case (int)MenuActionId.Showcase:StartActivity(-4);return true;

                case (int)MenuActionId.RecommendedLesson:StartRecommendedLesson();return true;

                default:return false;

            }

            return true;

        }

        private static void Cycle(string key,float value,float step,float min,float max)

        {ProductComfortPreferences.Set(key,ProductComfortPreferences.Adjust(value,step,min,max));}

        private void StartActivity(int activity)

        {

            string lessonId=activity>=0?(modes.IsGlider?(activity==5?"sgs233.reference.circuit_landing":"sgs233.reference.ridge"):(activity==5?"06_landing":"05_pattern")):null;

            StartActivityById(activity,lessonId);

        }

        private void StartActivityById(int activity,string lessonId)

        {

            bool glider=modes.IsGlider;

            // Both aircraft share demo entry; each uses its own native control guidance.

            experience?.FinishFlight("Session replaced by selected activity");ReturnMain();NavigateTo(Page.None);

            if(activity==-2)modes.SelectDemo();

            else if(activity==-4)modes.SelectShortShowcase();

            else if(activity==-3)modes.SelectFreeFlight();

            else {pendingLessonId=lessonId;modes.SelectTraining();}

            if(modes.State==ProductFlightModeCoordinator.FlightModeState.Blocked)Reject(modes.LastError);

            input.RequireTouchRelease();

        }

        private bool PresentPolishPage()

        {

            if(CurrentPage!=Page.InstrumentHelp)training.HideCockpitReference();

            experience?.ShowMap(CurrentPage==Page.Map);

            if(polishedLayout!=null)polishedLayout.SetSupplementHeight(CurrentPage==Page.Map?.38f:CurrentPage==Page.ControlDiagram||CurrentPage==Page.InputPreview?.29f:0);

            if(PresentFinishPage())return true;

            switch(CurrentPage)

            {

                case Page.Tools:

                    heading.text="Flight tools";

                    detail.text=(experience==null?"":experience.OwnerLabel+" · Build "+experience.Build)+"\nMenu pauses the flight. B / Y returns.";

                    Choice(0,"Flight briefing / local map",43);Choice(1,"Kneeboard / reference",45);Choice(2,"Short activities",46);

                    Choice(3,"Recent flights",47);Choice(4,"Mark an issue",48);Choice(5,"Seat position",2);

                    if(modes.State==ProductFlightModeCoordinator.FlightModeState.FreeFlight||modes.State==ProductFlightModeCoordinator.FlightModeState.Demo)Choice(6,"Finish flight / summary",49);

                    Choice(7,"Back",5);return true;

                case Page.Controls:

                    heading.text="Controls and comfort";

                    var raw=input.AuthoredGamepad.Current;

                    float rudder=ProductComfortPreferences.RawRudder;

                    detail.text=FocusedControlHint();

                    footer.text="Stick up / down: choose · left / right: adjust\n[A] Increase / open   [B / Y] Back";

                    Choice(0,"Assignments / presets / restore defaults",120);

                    Choice(1,$"Touch rudder deadzone: {ProductComfortPreferences.RudderDeadzone:P0}",51);

                    Choice(2,$"Touch rudder center response: {ProductComfortPreferences.RudderCurve:0.00} (1 = linear)",52);

                    Choice(3,(modes.IsGlider?"Stick":"Yoke")+$" pitch travel: {ProductComfortPreferences.PitchTravel:0.00}x",53);

                    Choice(4,(modes.IsGlider?"Stick":"Yoke")+$" roll travel: {ProductComfortPreferences.RollTravel:0.00}x",54);

                    if(!modes.IsGlider)Choice(5,$"Touch trim speed: {ProductComfortPreferences.TrimRate(true):0.00}x",55);

                    Choice(6,"Live input preview",121);Choice(7,"Back",5);return true;

                case Page.Bindings:
                    heading.text="Flight assignments";
                    detail.text=$"{ProductComfortPreferences.RudderHand} stick: rudder - {ProductComfortPreferences.TrimHand} stick: "+(modes.IsGlider?"airbrake / wheel-brake lever. Grab the separate trim lever on the left wall below the brake handle for trim.":"trim. Stick assignments swap together; cockpit grabbing stays unchanged.");
                    Choice(0,"Standard preset / restore defaults",56);Choice(1,"Gentler response preset",50);Choice(2,"Left-stick steering preset",128);

                    Choice(3,"Steering stick: "+ProductComfortPreferences.RudderHand,122);

                    if(!modes.IsGlider)Choice(4,"Brakes: "+(ProductComfortPreferences.CombinedBrakes?"either trigger applies both":"independent left / right"),123);

                    Choice(5,(modes.IsGlider?"Airbrake stick direction: ":"Trim direction: ")+(ProductComfortPreferences.ReverseTrim?"reversed":"standard"),124);

                    Choice(6,modes.IsGlider?"Controller diagram / stick grip":"Controller diagram / yoke grip",134);Choice(7,"Back",5);return true;

                case Page.InputPreview:

                    heading.text="Raw input preview · flight paused";

                    var sample=input.AuthoredGamepad.Current;
                    float r=sample.connected?sample.rightStickX:ProductComfortPreferences.RawRudder,t=sample.connected?sample.dpadY:ProductComfortPreferences.RawTrim;

                    float l=sample.connected?sample.leftTrigger:OVRInput.Get(OVRInput.RawAxis1D.LIndexTrigger),b=sample.connected?sample.rightTrigger:OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger);

                    detail.text=modes.IsGlider?$"SGS controls - paused.\nRudder stick: {r:+0.00;-0.00;0.00}\nAirbrake stick: {(sample.connected?sample.rightStickY:ProductComfortPreferences.RawTrim):+0.00;-0.00;0.00} - lever: {input.GliderLeverPosition:P0}\nThis reads raw controller inputs only. Cockpit grabs and flight controls are disabled while this menu is open.":$"Aircraft paused. Release inputs before resuming.\nRudder {r:+0.00;-0.00;0.00} → {(sample.connected?Mathf.Clamp(r,-1,1):ProductComfortPreferences.Axis(r,ProductComfortPreferences.RudderDeadzone,ProductComfortPreferences.RudderCurve)):+0.00;-0.00;0.00} · Trim {((Mathf.Abs(t)>.25f?t:0)*ProductComfortPreferences.TrimRate(!sample.connected)):+0.00;-0.00;0.00}x\nBrakes L / R: {l:0.00} / {b:0.00} → {(ProductComfortPreferences.CombinedBrakes?Mathf.Max(l,b):l):0.00} / {(ProductComfortPreferences.CombinedBrakes?Mathf.Max(l,b):b):0.00}";

                    Choice(0,"Back to controls",5);return true;

                case Page.InstrumentHelp:
                    instrumentHelpIndex%=ActiveInstrumentCount;
                    heading.text="Cockpit guide - "+ActiveInstrumentName;detail.text=ActiveInstrumentExplanation;
                    if(modes.State!=ProductFlightModeCoordinator.FlightModeState.Training)detail.text=detail.text.Replace("Use the target in the current instruction.","Keep scanning outside as well as the instruments.");
                    Choice(0,"Next instrument",126);Choice(1,"Previous instrument",127);Choice(2,"Back",5);return true;
                case Page.Graphics:

                    heading.text="Display and readability";

                    detail.text="Adjust text, contrast, captions and feedback. Experimental rendering controls are not available here.";

                    Choice(0,"Readability and feedback",42);Choice(7,"Back",5);return true;

                case Page.Comfort:

                    heading.text="Readability and feedback";detail.text="Menu and narration sizes are separate. Preferences are saved on this headset.";

                    footer.text="Stick up / down: choose · left / right: adjust\n[A] Change / select   [B / Y] Back";

                    Choice(0,$"Text size: {ProductComfortPreferences.TextScale:P0}",57);

                    Choice(1,"High contrast: "+(ProductComfortPreferences.HighContrast?"on":"off"),58);

                    Choice(2,$"Vibration strength: {ProductComfortPreferences.Haptics:P0}",59);

                    Choice(3,"Menu sounds: "+(FeedbackEnabled?"on":"off"),60);

                    Choice(4,"Narration size / background / captions",130);

                    Choice(5,"Traffic radio: "+(PlayerPrefs.GetInt("QuestFlight.TrafficRadio",1)!=0?"on":"off"),72);

                    Choice(6,$"External controller trim speed: {ProductComfortPreferences.TrimRate(false):0.00}x",71);Choice(7,"Back",5);return true;

                case Page.Briefing:

                    heading.text="Before you fly";detail.text=experience==null?"":experience.Briefing;

                    Choice(0,"View local map",44);Choice(1,"Choose a short activity",46);Choice(2,"Back",5);return true;

                case Page.Map:

                    heading.text="Boulder / local orientation";

                    detail.text="NORTH UP · cyan: route / target · gold: recent path\nSchematic only. Origin: modeled KBDU field reference; north up, east right. Not an aeronautical chart."+(modes.IsGlider&&modes.State==ProductFlightModeCoordinator.FlightModeState.Training?"\n"+SoaringLessonReference.Description(training.CurrentStage.task):"");

                    Choice(0,mapReturn==Page.Kneeboard?"Back to kneeboard":mapReturn==Page.History?"Back to recent flights":mapReturn==Page.Summary?"Back to summary":"Back to briefing",5);return true;

                case Page.Kneeboard:

                    heading.text="Pilot's kneeboard";detail.text=experience==null?"":experience.Reference;

                    Choice(0,"Local map",44);Choice(1,"Cockpit readiness checklist",135);Choice(7,"Back",5);return true;

                case Page.Activities:

                    heading.text="Activities - "+AircraftName;

                    detail.text="Each starts a fresh flight. An active session requires confirmation.";

                    Choice(0,modes.IsGlider?"Circuit and unpowered landing":"Landing practice - final approach",63);

                    Choice(1,modes.IsGlider?"Ridge soaring - supported lesson":"Local circuit - traffic pattern",64);

                    if(!modes.IsGlider){Choice(2,"Scenic demonstration",65);Choice(4,"Short showcase - airborne arrival",136);}

                    Choice(3,modes.IsGlider?"Airborne gliding practice":"Free flight - airport start",66);Choice(7,"Back",5);return true;

                case Page.History:

                    heading.text=training.IsGuestSession?"Guest flight history":"Recent flights";detail.text=experience==null?"":experience.HistoryText+"\n"+experience.SaveStatus;

                    Choice(0,"Older flight",experience!=null&&experience.HasOlderFlight?61:0);Choice(1,"Newer flight",experience!=null&&experience.HasNewerFlight?62:0);Choice(2,"Practice: "+experience?.RecommendedLessonName,experience!=null&&experience.HasRecommendedLesson?137:0);Choice(3,"View flight path",experience!=null&&experience.HasHistory?44:0);Choice(5,"Repeat selected activity",experience!=null&&experience.HasRepeatActivity?334:0);if(!string.IsNullOrEmpty(experience?.SaveStatus))Choice(4,"Retry saving history",129);Choice(7,"Back",5);return true;

                case Page.Summary:

                    heading.text="Flight complete";detail.text=experience?.LastRecord==null?"This flight is paused. Choose your next flight when ready.":experience.LastRecord.Display;

                    detail.text+="\n"+experience?.SaveStatus;

                    Choice(0,"Repeat this activity",experience!=null&&experience.HasRepeatActivity?334:0);Choice(1,"Practice: "+experience?.RecommendedLessonName,experience!=null&&experience.HasRecommendedLesson?137:0);Choice(2,"View flight path",44);if(!string.IsNullOrEmpty(experience?.SaveStatus))Choice(3,"Retry saving history",129);Choice(7,"Main menu",4);return true;

                case Page.Report:

                    heading.text="Mark an issue";detail.text="Build "+experience?.Build+"\n"+experience?.ReportStatus;

                    Choice(0,"Visual issue",67);Choice(1,"Audio / instruction issue",68);Choice(2,"Controls / comfort issue",69);Choice(3,"Other issue",70);Choice(4,"Back",5);return true;

                default:return false;

            }

        }

        private void ResetSetupCheck(){setupPassed=setupNegative=setupPositive=setupStarted=false;setupValue=0;}

        private void UpdateSetupChecks()

        {

            if(pendingLessonId!=null && training.State==TrainingSessionState.Menu)

            {string id=pendingLessonId;pendingLessonId=null;int index=System.Array.FindIndex(training.Lessons,l=>l.lessonId==id);if(index<0)Reject("Requested lesson is not in this aircraft catalog. Choose another lesson.");else training.SelectLesson(index);Refresh();}

        }
        private void PresentSetup()

        {

            heading.text=$"Welcome aboard   /   {guideStep+1} of 9";

            string[] instructions={

                "Move either thumbstick up or down to highlight an option. Press A on the right controller to select it.",

                "Menu on the left controller pauses flight and opens help. B or Y goes back; in flight, either opens the pause menu.",

                "Sit comfortably and look forward. Adjust the seat until you can see over the panel and reach the black yoke and throttle knob.",

                "With your left hand near the yoke, hold grip. Pull back a little, push forward, then return to where you started. Use the cockpit controls lesson to practice this with actual controls.",

                "Hold the left grip near the yoke. Move the hand slightly left, then right, then center, following the yoke's turning arc. Practice this after leaving the menu in the cockpit controls lesson.",

                "Find the black throttle knob in the lower center of the panel, next to the red mixture knob. Hold right grip near it. Push forward, pull back, then return to your starting position.",

                $"Move the {ProductComfortPreferences.RudderHand} thumbstick left, then right, then release it to center. This controls the rudder and ground steering. The {ProductComfortPreferences.TrimHand} stick up/down adjusts trim during flight.",

                "Squeeze and release each index trigger. "+(ProductComfortPreferences.CombinedBrakes?"Either trigger applies both brakes to stop straight.":"The left and right triggers apply the corresponding toe brakes. Use both to stop straight."),

                "Ready to fly. Demo flies for you; Free flight gives you control. Menu shows who has the controls and provides Take control / Let the demo fly. Controls and comfort lets you adjust travel and response."

            };

            detail.text=modes.IsGlider?SgsSetupInstruction(guideStep):instructions[Mathf.Clamp(guideStep,0,8)];

            detail.text="READING GUIDE · cockpit controls are disabled while this menu is open.\n\n"+detail.text;
            if(guideStep==2){Choice(0,"Adjust seat",2);Choice(1,"Continue reading",31);}
            else Choice(0,guideStep==8?"Finish reading guide":"Next page",guideStep==8?32:31);
            Choice(2,"Practice actual cockpit controls",328);
            Choice(3,"Skip guide for now",327);
            Choice(7,"Previous / Back",5);
        }

    }

}
