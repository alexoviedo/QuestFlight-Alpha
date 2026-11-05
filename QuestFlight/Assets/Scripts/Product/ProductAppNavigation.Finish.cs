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

                case (int)MenuActionId.Resume:return "Deadzone ignores small stick drift near the center.";

                case (int)MenuActionId.SeatSetup:return "Higher response softens small steering movements. Full steering stays available.";

                case (int)MenuActionId.ConfirmHome:case (int)MenuActionId.EndToHome:return "Longer travel means more hand movement for the same yoke deflection.";

                case (int)MenuActionId.Back:return "Lower trim speed makes small pitch-balance adjustments easier.";

                case (int)MenuActionId.SaveSeat:return "Move the steering stick to see how input becomes rudder output.";

                default:return "Choose a preset or inspect what each controller does.";

            }

        }

        private bool PresentFinishPage()

        {

            switch(CurrentPage)

            {

                case Page.Captions:

                    heading.text="Narration presentation";

                    detail.text="Captions control spoken phrases. Operational status, safety cues and guided lesson instructions remain visible. No-coaching practice is selected at the briefing.";

                    footer.text="Stick up / down: choose · left / right: adjust\nA: select / increase · B / Y: back";

                    Choice(0,$"Caption size: {ProductComfortPreferences.CaptionScale:P0}",131);

                    Choice(1,"Caption background: "+(ProductComfortPreferences.SolidCaptions?"solid":"subtle transparency"),132);

                    Choice(2,"Narration captions: "+(CaptionsEnabled?"on":"off"),14);Choice(7,"Back",5);return true;

                case Page.ControlDiagram:

                    heading.text="Your Touch controls";detail.text=modes.IsGlider?$"{ProductComfortPreferences.RudderHand} stick: rudder. {ProductComfortPreferences.TrimHand} stick: airbrake / wheel-brake lever. Grip: grab stick or levers. Lower left trim lever: pull aft for nose-up trim, push forward for nose-down trim. Red handle / A while flying: tow release. A in menus: select.":"Thumbsticks steer and trim; hold a grip to grab the yoke and release it to let go. Ignition and fuel selectors are inactive; flights start with the engine running.";

                    Choice(7,"Back",5);return true;

                case Page.Checklist:

                    heading.text="Cockpit readiness";detail.text=experience?.ChecklistText;

                    Choice(0,"Cockpit guide",125);Choice(7,"Back",5);return true;

                default:return false;

            }

        }

    }

}
