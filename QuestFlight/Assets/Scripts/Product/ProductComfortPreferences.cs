using UnityEngine;

namespace QuestFlight.Product
{
    /// <summary>Small input-transfer preferences; never changes flight dynamics or control endpoints.</summary>
    public static class ProductComfortPreferences
    {
        public static float RudderDeadzone => Get("Touch.Deadzone", .12f, .04f, .24f);
        public static float RudderCurve => Get("Touch.Curve", 1f, 1f, 2f);
        public static float PitchTravel => Get("Touch.PitchTravel", 1f, .75f, 1.5f);
        public static float RollTravel => Get("Touch.RollTravel", 1f, .75f, 1.5f);
        public static float TrimRate(bool touch) => Get(touch?"Touch.Trim":"Gamepad.Trim", 1f, .5f, 1.5f);
        public static float TextScale => Get("TextScale", 1f, 1f, 1.2f);
        public static float CaptionScale => Get("CaptionScale",1,1,1.6f);
        public static bool SolidCaptions => PlayerPrefs.GetInt("QuestFlight.SolidCaptions",1)!=0;
        public static bool ToggleYoke => PlayerPrefs.GetInt("QuestFlight.ToggleYoke",0)!=0;
        public static float Haptics => Get("Haptics", .65f, 0, 1);
        public static bool HighContrast => PlayerPrefs.GetInt("QuestFlight.HighContrast",0)!=0;
        public static bool LeftStickSteering => PlayerPrefs.GetInt("QuestFlight.LeftStickSteering",0)!=0;
        public static bool CombinedBrakes => PlayerPrefs.GetInt("QuestFlight.CombinedBrakes",0)!=0;
        public static bool ReverseTrim => PlayerPrefs.GetInt("QuestFlight.ReverseTrim",0)!=0;
        public static string RudderHand => LeftStickSteering?"left":"right";
        public static string TrimHand => LeftStickSteering?"right":"left";
        public static OVRInput.Controller RudderController => LeftStickSteering?OVRInput.Controller.LTouch:OVRInput.Controller.RTouch;
        public static OVRInput.Controller TrimController => LeftStickSteering?OVRInput.Controller.RTouch:OVRInput.Controller.LTouch;
        public static float RawRudder => OVRInput.Get(LeftStickSteering?OVRInput.RawAxis2D.LThumbstick:OVRInput.RawAxis2D.RThumbstick,RudderController).x;
        public static float RawTrim => OVRInput.Get(LeftStickSteering?OVRInput.RawAxis2D.RThumbstick:OVRInput.RawAxis2D.LThumbstick,TrimController).y*(ReverseTrim?-1:1);
        public static string BindingSummary => $"{RudderHand} stick: rudder · {TrimHand} stick: trim\n"+(CombinedBrakes?"Either index trigger: both brakes":"Left / right index trigger: corresponding brake");
        public static void SetFlag(string key,bool value) {PlayerPrefs.SetInt("QuestFlight."+key,value?1:0);PlayerPrefs.Save();}
        public static float Adjust(float value,float step,float min,float max) => Mathf.Clamp(value+step,min,max);
        public static string MapInstruction(string text) => string.IsNullOrEmpty(text)?text:
            text.Replace("left thumbstick","{trim_hand} thumbstick").Replace("right thumbstick","{rudder_hand} thumbstick").Replace("{trim_hand}",TrimHand).Replace("{rudder_hand}",RudderHand).Replace("right stick: rudder",RudderHand+" stick: rudder");
        public static float Get(string key,float fallback,float min,float max)
        { float v=PlayerPrefs.GetFloat("QuestFlight."+key,fallback);return float.IsNaN(v)||float.IsInfinity(v)?fallback:Mathf.Clamp(v,min,max); }
        public static void Set(string key,float value) { PlayerPrefs.SetFloat("QuestFlight."+key,value);PlayerPrefs.Save(); }
        public static float Axis(float value,float deadzone,float exponent)
        {
            if(float.IsNaN(value)||float.IsInfinity(value))return 0;
            return Mathf.Sign(value)*Mathf.Pow(Mathf.Clamp01((Mathf.Abs(value)-Mathf.Clamp(deadzone,0,.5f))/(1-Mathf.Clamp(deadzone,0,.5f))),Mathf.Clamp(exponent,1,2));
        }
        public static void ResetControls()
        {
            foreach(string k in new[]{"Touch.Deadzone","Touch.Curve","Touch.PitchTravel","Touch.RollTravel","Touch.Trim","Gamepad.Trim"})PlayerPrefs.DeleteKey("QuestFlight."+k);
            foreach(string k in new[]{"LeftStickSteering","CombinedBrakes","ReverseTrim","ToggleYoke"})PlayerPrefs.DeleteKey("QuestFlight."+k);
            PlayerPrefs.Save();
        }
        public static void RelaxedPreset()
        { Set("Touch.Deadzone",.12f);Set("Touch.Curve",1.5f);Set("Touch.PitchTravel",1.25f);Set("Touch.RollTravel",1.25f);Set("Touch.Trim",.75f); }
    }
}
