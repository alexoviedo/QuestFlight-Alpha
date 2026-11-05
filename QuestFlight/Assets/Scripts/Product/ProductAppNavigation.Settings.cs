using QuestFlight.Product.Training;

using UnityEngine;

namespace QuestFlight.Product

{

    public sealed partial class ProductAppNavigation

    {

        private int adjustmentDirection,instrumentHelpIndex;

        private float nextAdjustment;

        private static readonly TrainingReference[] InstrumentReferences={TrainingReference.Airspeed,TrainingReference.Attitude,TrainingReference.Altimeter,TrainingReference.Heading,TrainingReference.VerticalSpeed,TrainingReference.Slip,TrainingReference.Throttle,TrainingReference.Yoke,TrainingReference.Flaps,TrainingReference.Trim};

        private static readonly string[] InstrumentNames={"Airspeed","Attitude","Altimeter","Heading","Vertical speed","Slip ball","Throttle","Yoke","Flaps","Trim"};

        private void ShowInstrumentHelp() { if(!modes.IsGlider)training.ShowCockpitReference(InstrumentReferences[instrumentHelpIndex]); }

        private void AdjustFocusedSetting(bool suppressed,float horizontal)

        {

            if(releaseRequired||suppressed||!IsOpen){adjustmentDirection=0;return;}

            int direction=Mathf.Abs(horizontal)>.65f?(horizontal>0?1:-1):0;
            if(direction==0){adjustmentDirection=0;return;}

            bool fresh=direction!=adjustmentDirection;

            if(!fresh&&Time.unscaledTime<nextAdjustment)return;

            if(AdjustSetting(CurrentPage==Page.SettingAdjustment?adjustmentAction:actions[focus],direction)){ProductUiFeedback.Play(ProductUiFeedback.Cue.Focus);Refresh();}

            adjustmentDirection=direction;nextAdjustment=Time.unscaledTime+(fresh?.45f:.2f);

        }

        private bool AdjustSetting(int action,int direction)

        {

            string key;float value,step,min,max;

            switch(action)

            {

                case (int)MenuActionId.Deadzone:key="Touch.Deadzone";value=ProductComfortPreferences.RudderDeadzone;step=.04f;min=.04f;max=.24f;break;

                case (int)MenuActionId.RudderCurve:key="Touch.Curve";value=ProductComfortPreferences.RudderCurve;step=.25f;min=1;max=2;break;

                case (int)MenuActionId.PitchTravel:key="Touch.PitchTravel";value=ProductComfortPreferences.PitchTravel;step=.25f;min=.75f;max=1.5f;break;

                case (int)MenuActionId.RollTravel:key="Touch.RollTravel";value=ProductComfortPreferences.RollTravel;step=.25f;min=.75f;max=1.5f;break;

                case (int)MenuActionId.TrimSpeed:key="Touch.Trim";value=ProductComfortPreferences.TrimRate(true);step=.25f;min=.5f;max=1.5f;break;

                case (int)MenuActionId.ExternalTrim:key="Gamepad.Trim";value=ProductComfortPreferences.TrimRate(false);step=.25f;min=.5f;max=1.5f;break;

                case (int)MenuActionId.TextSize:key="TextScale";value=ProductComfortPreferences.TextScale;step=.1f;min=1;max=1.2f;break;

                case (int)MenuActionId.CaptionSize:key="CaptionScale";value=ProductComfortPreferences.CaptionScale;step=.2f;min=1;max=1.6f;break;

                case (int)MenuActionId.Haptics:key="Haptics";value=ProductComfortPreferences.Haptics;step=.25f;min=0;max=1;break;

                case (int)MenuActionId.VoiceVolume:voiceLevel=ProductComfortPreferences.Adjust(voiceLevel,direction*.1f,0,1);ApplyAudioLevels();return true;

                case (int)MenuActionId.AircraftVolume:aircraftLevel=ProductComfortPreferences.Adjust(aircraftLevel,direction*.1f,0,1);ApplyAudioLevels();return true;

                default:return false;

            }

            float next=ProductComfortPreferences.Adjust(value,direction*step,min,max);
            pauseNotice=Mathf.Approximately(next,value)?(direction>0?"Maximum value reached.":"Minimum value reached."):"Setting saved.";
            ProductComfortPreferences.Set(key,next);

            input.RequireTouchRelease();return true;

        }

    }

}
