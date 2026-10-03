using UnityEngine;

namespace QuestFlight.Product
{
    public sealed partial class ProductAppNavigation
    {
        [SerializeField] private ProductControlVisuals controlVisuals;
        public void ConfigureControlVisuals(ProductControlVisuals value){controlVisuals=value;}
        private string FocusedControlHint()
        {
            switch(focus)
            {
                case 1:return "Deadzone ignores small stick drift near the center.";
                case 2:return "Higher response softens small steering movements. Full steering stays available.";
                case 3:case 4:return "Longer travel means more hand movement for the same yoke deflection.";
                case 5:return "Lower trim speed makes small pitch-balance adjustments easier.";
                case 6:return "Move the steering stick to see how input becomes rudder output.";
                default:return "Choose a preset or inspect what each controller does.";
            }
        }
        private bool PresentFinishPage()
        {
            switch(CurrentPage)
            {
                case Page.Captions:
                    heading.text="Narration presentation";
                    detail.text="The objective stays visible above short spoken phrases. Full instructions remain in Help.";
                    footer.text="Stick up / down: choose · left / right: adjust\nA: select / increase · B / Y: back";
                    Choice(0,$"Caption size: {ProductComfortPreferences.CaptionScale:P0}",131);
                    Choice(1,"Caption background: "+(ProductComfortPreferences.SolidCaptions?"solid":"subtle transparency"),132);
                    Choice(2,"Narration captions: "+(CaptionsEnabled?"on":"off"),14);Choice(7,"Back",5);return true;
                case Page.ControlDiagram:
                    heading.text="Your Touch controls";detail.text="Thumbsticks steer and trim; grips hold cockpit controls.";
                    Choice(0,"Yoke grip: "+(ProductComfortPreferences.ToggleYoke?"press to grab / press to release":"hold to grab"),133);
                    Choice(7,"Back",5);return true;
                case Page.Checklist:
                    heading.text="Cockpit readiness";detail.text=experience?.ChecklistText;
                    Choice(0,"Cockpit guide",125);Choice(7,"Back",5);return true;
                default:return false;
            }
        }
    }
}
