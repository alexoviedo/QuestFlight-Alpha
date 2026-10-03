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
                case Page.Summary:CurrentPage=Page.App;return true;
                default:return false;
            }
        }
        private bool InvokePolishAction(int action)
        {
            if(AdjustSetting(action,1))return true;
            switch(action)
            {
                case 40:CurrentPage=Page.Tools;break;
                case 41:controlsReturn=CurrentPage;CurrentPage=Page.Controls;break;
                case 42:CurrentPage=Page.Comfort;break;
                case 43:CurrentPage=Page.Briefing;break;
                case 44:mapReturn=CurrentPage;experience?.ShowHistoryOnMap(CurrentPage==Page.History||CurrentPage==Page.Summary);CurrentPage=Page.Map;focus=0;break;
                case 45:CurrentPage=Page.Kneeboard;break;
                case 46:CurrentPage=Page.Activities;break;
                case 47:CurrentPage=Page.History;break;
                case 48:CurrentPage=Page.Report;break;
                case 49:experience?.FinishFlight("Flight ended by pilot");ShowFlightSummary();return true;
                case 50:ProductComfortPreferences.RelaxedPreset();break;
                case 51:Cycle("Touch.Deadzone",ProductComfortPreferences.RudderDeadzone,.04f,.04f,.24f);break;
                case 52:Cycle("Touch.Curve",ProductComfortPreferences.RudderCurve,.25f,1,2);break;
                case 53:Cycle("Touch.PitchTravel",ProductComfortPreferences.PitchTravel,.25f,.75f,1.5f);break;
                case 54:Cycle("Touch.RollTravel",ProductComfortPreferences.RollTravel,.25f,.75f,1.5f);break;
                case 55:Cycle("Touch.Trim",ProductComfortPreferences.TrimRate(true),.25f,.5f,1.5f);break;
                case 56:ProductComfortPreferences.ResetControls();input.RequireTouchRelease();break;
                case 57:Cycle("TextScale",ProductComfortPreferences.TextScale,.1f,1,1.2f);break;
                case 58:PlayerPrefs.SetInt("QuestFlight.HighContrast",ProductComfortPreferences.HighContrast?0:1);PlayerPrefs.Save();
                    foreach(var surface in panel.GetComponentsInChildren<ProductUiSurface>(true))surface.RefreshStyle();break;
                case 59:Cycle("Haptics",ProductComfortPreferences.Haptics,.25f,0,1);break;
                case 60:PlayerPrefs.SetInt("QuestFlight.InterfaceFeedback",FeedbackEnabled?0:1);PlayerPrefs.Save();break;
                case 61:experience?.NextHistory(1);break;
                case 62:experience?.NextHistory(-1);break;
                case 63:StartActivity(5);return true;
                case 64:StartActivity(4);return true;
                case 65:StartActivity(-2);return true;
                case 66:StartActivity(-3);return true;
                case 67:experience?.Report("Visual issue");break;
                case 68:experience?.Report("Audio / instruction issue");break;
                case 69:experience?.Report("Controls / comfort issue");break;
                case 70:experience?.Report("Other issue");break;
                case 71:Cycle("Gamepad.Trim",ProductComfortPreferences.TrimRate(false),.25f,.5f,1.5f);break;
                case 72:PlayerPrefs.SetInt("QuestFlight.TrafficRadio",PlayerPrefs.GetInt("QuestFlight.TrafficRadio",1)==0?1:0);PlayerPrefs.Save();break;
                case 120:CurrentPage=Page.Bindings;focus=0;break;
                case 121:CurrentPage=Page.InputPreview;focus=0;break;
                case 122:ProductComfortPreferences.SetFlag("LeftStickSteering",!ProductComfortPreferences.LeftStickSteering);input.RequireTouchRelease();break;
                case 123:ProductComfortPreferences.SetFlag("CombinedBrakes",!ProductComfortPreferences.CombinedBrakes);input.RequireTouchRelease();break;
                case 124:ProductComfortPreferences.SetFlag("ReverseTrim",!ProductComfortPreferences.ReverseTrim);input.RequireTouchRelease();break;
                case 125:CurrentPage=Page.InstrumentHelp;instrumentHelpIndex=0;focus=0;ShowInstrumentHelp();break;
                case 126:instrumentHelpIndex=(instrumentHelpIndex+1)%InstrumentReferences.Length;ShowInstrumentHelp();break;
                case 127:instrumentHelpIndex=(instrumentHelpIndex+InstrumentReferences.Length-1)%InstrumentReferences.Length;ShowInstrumentHelp();break;
                case 128:ProductComfortPreferences.ResetControls();ProductComfortPreferences.SetFlag("LeftStickSteering",true);input.RequireTouchRelease();break;
                case 129:experience?.RetryHistorySave();break;
                case 130:CurrentPage=Page.Captions;focus=0;break;
                case 132:ProductComfortPreferences.SetFlag("SolidCaptions",!ProductComfortPreferences.SolidCaptions);break;
                case 133:ProductComfortPreferences.SetFlag("ToggleYoke",!ProductComfortPreferences.ToggleYoke);input.RequireTouchRelease();break;
                case 134:CurrentPage=Page.ControlDiagram;focus=0;break;
                case 135:CurrentPage=Page.Checklist;focus=0;break;
                case 136:StartActivity(-4);return true;
                case 137:StartActivity(experience.RecommendedLesson);return true;
                default:return false;
            }
            if(action>=40&&action<=49)focus=0;
            return true;
        }
        private static void Cycle(string key,float value,float step,float min,float max)
        {ProductComfortPreferences.Set(key,ProductComfortPreferences.Adjust(value,step,min,max));}
        private void StartActivity(int activity)
        {
            // Existing selection/recovery APIs keep one native flight authority and fresh starts.
            ReturnMain();
            if(activity==-2)modes.SelectDemo();
            else if(activity==-4)modes.SelectShortShowcase();
            else if(activity==-3)modes.SelectFreeFlight();
            else {pendingActivity=activity;modes.SelectTraining();}
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
                    Choice(3,"Recent flights",47);Choice(4,"Mark an issue",48);Choice(5,"Settings / controls",25);
                    if(modes.State==ProductFlightModeCoordinator.FlightModeState.FreeFlight||modes.State==ProductFlightModeCoordinator.FlightModeState.Demo)Choice(6,"Finish flight / summary",49);
                    Choice(7,"Back",5);return true;
                case Page.Controls:
                    heading.text="Controls and comfort";
                    var raw=input.AuthoredGamepad.Current;
                    float rudder=ProductComfortPreferences.RawRudder;
                    detail.text=FocusedControlHint();
                    footer.text="Stick up / down: choose · left / right: adjust\n[A] Increase / open   [B / Y] Back";
                    Choice(0,"Assignments / presets / restore defaults",120);
                    Choice(1,$"Rudder deadzone: {ProductComfortPreferences.RudderDeadzone:P0}",51);
                    Choice(2,$"Rudder center response: {ProductComfortPreferences.RudderCurve:0.00} (1 = linear)",52);
                    Choice(3,$"Yoke pitch travel: {ProductComfortPreferences.PitchTravel:0.00}x",53);
                    Choice(4,$"Yoke roll travel: {ProductComfortPreferences.RollTravel:0.00}x",54);
                    Choice(5,$"Touch trim speed: {ProductComfortPreferences.TrimRate(true):0.00}x",55);
                    Choice(6,"Live input preview",121);Choice(7,"Back",5);return true;
                case Page.Bindings:
                    heading.text="Flight assignments";
                    detail.text=$"{ProductComfortPreferences.RudderHand} stick: rudder · {ProductComfortPreferences.TrimHand} stick: trim\nStick assignments swap together; cockpit grabbing stays unchanged.";
                    Choice(0,"Standard preset / restore defaults",56);Choice(1,"Gentler response preset",50);Choice(2,"Left-stick steering preset",128);
                    Choice(3,"Steering stick: "+ProductComfortPreferences.RudderHand,122);
                    Choice(4,"Brakes: "+(ProductComfortPreferences.CombinedBrakes?"either trigger applies both":"independent left / right"),123);
                    Choice(5,"Trim direction: "+(ProductComfortPreferences.ReverseTrim?"reversed":"standard"),124);
                    Choice(6,"Controller diagram / yoke grip",134);Choice(7,"Back",5);return true;
                case Page.InputPreview:
                    heading.text="Live control preview";
                    float r=ProductComfortPreferences.RawRudder,t=ProductComfortPreferences.RawTrim;
                    float l=OVRInput.Get(OVRInput.RawAxis1D.LIndexTrigger),b=OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger);
                    detail.text=$"Aircraft paused. Release inputs before resuming.\nRudder {r:+0.00;-0.00;0.00} → {ProductComfortPreferences.Axis(r,ProductComfortPreferences.RudderDeadzone,ProductComfortPreferences.RudderCurve):+0.00;-0.00;0.00} · Trim {((Mathf.Abs(t)>.25f?t:0)*ProductComfortPreferences.TrimRate(true)):+0.00;-0.00;0.00}x\nBrakes L / R: {l:0.00} / {b:0.00} → {(ProductComfortPreferences.CombinedBrakes?Mathf.Max(l,b):l):0.00} / {(ProductComfortPreferences.CombinedBrakes?Mathf.Max(l,b):b):0.00}";
                    Choice(0,"Back to controls",5);return true;
                case Page.InstrumentHelp:
                    heading.text="Cockpit guide · "+InstrumentNames[instrumentHelpIndex];detail.text=training.ExplainReference(InstrumentReferences[instrumentHelpIndex]);
                    if(modes.State!=ProductFlightModeCoordinator.FlightModeState.Training)detail.text=detail.text.Replace("Use the target in the current instruction.","Keep scanning outside as well as the instruments.");
                    Choice(0,"Next instrument",126);Choice(1,"Previous instrument",127);Choice(2,"Back",5);return true;
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
                    detail.text="NORTH UP · cyan: route / target · gold: recent path\nSchematic orientation only; not an aeronautical chart.";
                    Choice(0,mapReturn==Page.Kneeboard?"Back to kneeboard":mapReturn==Page.History?"Back to recent flights":mapReturn==Page.Summary?"Back to summary":"Back to briefing",5);return true;
                case Page.Kneeboard:
                    heading.text="Pilot's kneeboard";detail.text=experience==null?"":experience.Reference;
                    Choice(0,"Local map",44);Choice(1,"Cockpit readiness checklist",135);Choice(7,"Back",5);return true;
                case Page.Activities:
                    heading.text="A short flight";
                    detail.text="Each starts a fresh flight using the existing authored route or lesson.\nLanding: about 3–5 min · circuit: about 8–12 min.\nScenic tour duration varies with wind and landing traffic. Your current session will end when you select one.";
                    Choice(0,"Landing practice · guided final approach",63);Choice(1,"Local circuit · guided traffic pattern",64);Choice(2,"Sightseeing · demo tour and landing",65);Choice(3,"Free flight · fresh airport start",66);Choice(4,"Short showcase · airborne arrival and landing",136);Choice(7,"Back",5);return true;
                case Page.History:
                    heading.text=training.IsGuestSession?"Guest flight history":"Recent flights";detail.text=experience==null?"":experience.HistoryText+"\n"+experience.SaveStatus;
                    Choice(0,"Older flight",experience!=null&&experience.HasOlderFlight?61:0);Choice(1,"Newer flight",experience!=null&&experience.HasNewerFlight?62:0);Choice(2,"Practice: "+experience?.RecommendedLessonName,experience!=null&&experience.HasHistory?137:0);Choice(3,"View flight path",experience!=null&&experience.HasHistory?44:0);if(!string.IsNullOrEmpty(experience?.SaveStatus))Choice(4,"Retry saving history",129);Choice(7,"Back",5);return true;
                case Page.Summary:
                    heading.text="Flight complete";detail.text=experience?.LastRecord==null?"This flight is paused. Choose your next flight when ready.":experience.LastRecord.Display;
                    detail.text+="\n"+experience?.SaveStatus;
                    Choice(0,"Fly again · free flight",66);Choice(1,"Practice: "+experience?.RecommendedLessonName,137);Choice(2,"View flight path",44);if(!string.IsNullOrEmpty(experience?.SaveStatus))Choice(3,"Retry saving history",129);Choice(7,"Main menu",4);return true;
                case Page.Report:
                    heading.text="Mark an issue";detail.text="Build "+experience?.Build+"\n"+experience?.ReportStatus;
                    Choice(0,"Visual issue",67);Choice(1,"Audio / instruction issue",68);Choice(2,"Controls / comfort issue",69);Choice(3,"Other issue",70);Choice(4,"Back",5);return true;
                default:return false;
            }
        }
        private static string Meter(float value)
        {int n=Mathf.RoundToInt((value+1)*5);return new string('·',n)+"|"+new string('·',10-n)+$" {value:+0.00;-0.00;0.00}";}
        private void ResetSetupCheck(){setupPassed=setupNegative=setupPositive=setupStarted=false;setupValue=0;}
        private void UpdateSetupChecks()
        {
            if(pendingActivity>=0 && training.State==TrainingSessionState.Menu)
            {int index=pendingActivity;pendingActivity=-1;training.SelectLesson(index);Refresh();}
            if(CurrentPage!=Page.QuickStart||guideStep<3||guideStep>7)return;
            bool external=input.AuthoredGamepad.Current.connected;
            if(guideStep==6)
            {setupValue=ProductComfortPreferences.RawRudder;if(external)setupValue=input.AuthoredGamepad.Current.rightStickX;}
            else if(guideStep==7)
            {
                float l=OVRInput.Get(OVRInput.RawAxis1D.LIndexTrigger),r=OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger);
                if(external){l=input.AuthoredGamepad.Current.leftTrigger;r=input.AuthoredGamepad.Current.rightTrigger;}
                setupNegative|=l>.65f;setupPositive|=r>.65f;setupPassed|=setupNegative&&setupPositive&&l<.15f&&r<.15f;return;
            }
            else
            {
                Transform hand=guideStep==5?setupRightHand:setupLeftHand,target=guideStep==5?setupThrottle:setupYoke;
                bool grip=OVRInput.Get(guideStep==5?OVRInput.RawAxis1D.RHandTrigger:OVRInput.RawAxis1D.LHandTrigger)>.6f;
                if(external){setupValue=guideStep==3?input.AuthoredGamepad.Current.leftStickY:guideStep==4?input.AuthoredGamepad.Current.leftStickX:input.AuthoredGamepad.Current.rightStickY;setupNegative|=setupValue<-.65f;setupPositive|=setupValue>.65f;setupPassed|=setupNegative&&setupPositive&&Mathf.Abs(setupValue)<.2f;return;}
                if(hand==null||setupFrame==null||target==null)return;
                Vector3 pos=setupFrame.InverseTransformPoint(hand.position);
                if(!grip){setupStarted=false;return;}
                if(!setupStarted)
                {if(Vector3.Distance(hand.position,target.position)>.25f)return;setupStart=pos;setupStarted=true;}
                setupValue=guideStep==4?(pos.x-setupStart.x)/.08f:(pos.z-setupStart.z)/.045f;
            }
            setupNegative|=setupValue<-.65f;setupPositive|=setupValue>.65f;
            setupPassed|=setupNegative&&setupPositive&&Mathf.Abs(setupValue)<.2f;
        }
        private void PresentSetup()
        {
            heading.text=$"Welcome aboard   /   {guideStep+1} of 9";
            string[] instructions={
                "Move either thumbstick up or down to highlight an option. Press A on the right controller to select it.",
                "Menu on the left controller pauses flight and opens help. B or Y goes back; in flight, either opens the pause menu.",
                "Sit comfortably and look forward. Adjust the seat until you can see over the panel and reach the black yoke and throttle knob.",
                "With your left hand near the yoke, hold grip. Pull back a little, push forward, then return to where you started. This paused check practices pitch motion without moving the aircraft.",
                "Hold the left grip near the yoke. Move the hand slightly left, then right, then center, following the yoke's turning arc. This paused check practices roll motion.",
                "Find the black throttle knob in the lower center of the panel, next to the red mixture knob. Hold right grip near it. Push forward, pull back, then return to your starting position.",
                $"Move the {ProductComfortPreferences.RudderHand} thumbstick left, then right, then release it to center. This controls the rudder and ground steering. The {ProductComfortPreferences.TrimHand} stick up/down adjusts trim during flight.",
                "Squeeze and release each index trigger. "+(ProductComfortPreferences.CombinedBrakes?"Either trigger applies both brakes to stop straight.":"The left and right triggers apply the corresponding toe brakes. Use both to stop straight."),
                "Ready to fly. Demo flies for you; Free flight gives you control. Menu shows who has the controls and provides Take control / Let the demo fly. Controls and comfort lets you adjust travel and response."
            };
            detail.text=instructions[Mathf.Clamp(guideStep,0,8)];
            if(guideStep>=3 && guideStep<=7)
            {
                detail.text+="\n\n"+(setupPassed?"Check complete · controls returned to neutral.":"Practice check: "+(setupNegative?"first motion done":"first motion pending")+" · "+(setupPositive?"second motion done":"second motion pending"));
                if(setupPassed)Choice(0,"Continue",31);
                Choice(1,"Skip this check",31);
            }
            else if(guideStep==2){Choice(0,"Adjust seat",2);Choice(1,"My seat is comfortable",31);}
            else Choice(0,guideStep==8?"Ready to fly":"Continue",guideStep==8?32:31);
            Choice(3,"Skip guide",32);
        }
    }
}
