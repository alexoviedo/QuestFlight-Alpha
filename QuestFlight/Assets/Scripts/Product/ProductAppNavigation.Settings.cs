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
        private void ShowInstrumentHelp() => training.ShowCockpitReference(InstrumentReferences[instrumentHelpIndex]);

        private void AdjustFocusedSetting(bool suppressed)
        {
            if(releaseRequired||suppressed||!IsOpen){adjustmentDirection=0;return;}
            Vector2 l=OVRInput.Get(OVRInput.RawAxis2D.LThumbstick),r=OVRInput.Get(OVRInput.RawAxis2D.RThumbstick);
            Vector2 stick=Mathf.Abs(l.x)>Mathf.Abs(r.x)?l:r;
            int direction=Mathf.Abs(stick.x)>.65f&&Mathf.Abs(stick.y)<.35f?(stick.x>0?1:-1):0;
            if(direction==0){adjustmentDirection=0;return;}
            bool fresh=direction!=adjustmentDirection;
            if(!fresh&&Time.unscaledTime<nextAdjustment)return;
            if(AdjustSetting(actions[focus],direction)){ProductUiFeedback.Play(ProductUiFeedback.Cue.Focus);Refresh();}
            adjustmentDirection=direction;nextAdjustment=Time.unscaledTime+(fresh?.45f:.2f);
        }
        private bool AdjustSetting(int action,int direction)
        {
            string key;float value,step,min,max;
            switch(action)
            {
                case 51:key="Touch.Deadzone";value=ProductComfortPreferences.RudderDeadzone;step=.04f;min=.04f;max=.24f;break;
                case 52:key="Touch.Curve";value=ProductComfortPreferences.RudderCurve;step=.25f;min=1;max=2;break;
                case 53:key="Touch.PitchTravel";value=ProductComfortPreferences.PitchTravel;step=.25f;min=.75f;max=1.5f;break;
                case 54:key="Touch.RollTravel";value=ProductComfortPreferences.RollTravel;step=.25f;min=.75f;max=1.5f;break;
                case 55:key="Touch.Trim";value=ProductComfortPreferences.TrimRate(true);step=.25f;min=.5f;max=1.5f;break;
                case 71:key="Gamepad.Trim";value=ProductComfortPreferences.TrimRate(false);step=.25f;min=.5f;max=1.5f;break;
                case 57:key="TextScale";value=ProductComfortPreferences.TextScale;step=.1f;min=1;max=1.2f;break;
                case 131:key="CaptionScale";value=ProductComfortPreferences.CaptionScale;step=.2f;min=1;max=1.6f;break;
                case 59:key="Haptics";value=ProductComfortPreferences.Haptics;step=.25f;min=0;max=1;break;
                case 23:voiceLevel=ProductComfortPreferences.Adjust(voiceLevel,direction*.1f,0,1);ApplyAudioLevels();return true;
                case 24:aircraftLevel=ProductComfortPreferences.Adjust(aircraftLevel,direction*.1f,0,1);ApplyAudioLevels();return true;
                default:return false;
            }
            ProductComfortPreferences.Set(key,ProductComfortPreferences.Adjust(value,direction*step,min,max));
            input.RequireTouchRelease();return true;
        }
    }
}
