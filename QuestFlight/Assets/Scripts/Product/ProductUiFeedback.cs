using UnityEngine;

namespace QuestFlight.Product
{
    /// <summary>One editor-authored interface source; no runtime objects or audio assets.</summary>
    [DisallowMultipleComponent]
    public sealed class ProductUiFeedback : MonoBehaviour
    {
        public enum Cue { Focus, Select, Back, Confirm }
        [SerializeField] private AudioSource source;
        [SerializeField] private AudioClip focus, select, back, confirm;
        private static ProductUiFeedback current;
        private OVRInput.Controller vibrating;
        private float stopAt, lastFocus = -10;

        public void Configure(AudioSource audio, AudioClip f, AudioClip s, AudioClip b, AudioClip c)
        { source = audio; focus = f; select = s; back = b; confirm = c; }

        private void OnEnable()
        {
            if (current != null && current != this)
            { Debug.LogError("Duplicate authored interface feedback source.", this); enabled = false; return; }
            current = this;
        }

        private void OnDisable()
        { StopHaptics(); if (current == this) current = null; }

        private void Update()
        { if (vibrating != OVRInput.Controller.None && Time.unscaledTime >= stopAt) StopHaptics(); }

        public static void Play(Cue cue)
        {
            if (current != null) current.Emit(cue);
        }

        private void Emit(Cue cue)
        {
            if (cue == Cue.Focus && Time.unscaledTime - lastFocus < .065f) return;
            if (cue == Cue.Focus) lastFocus = Time.unscaledTime;
            AudioClip clip = cue == Cue.Focus ? focus : cue == Cue.Select ? select : cue == Cue.Back ? back : confirm;
            if (ProductAppNavigation.FeedbackEnabled && source != null && clip != null) source.PlayOneShot(clip);
            if (Application.platform != RuntimePlatform.Android) return;
            // Touch is supplementary; it never drives an action or changes aircraft controls.
            var connected = OVRInput.GetConnectedControllers();
            bool left = Mathf.Abs(OVRInput.Get(OVRInput.RawAxis2D.LThumbstick).y) >
                        Mathf.Abs(OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).y);
            if (OVRInput.Get(OVRInput.RawButton.Start) || OVRInput.Get(OVRInput.RawButton.Y) || OVRInput.Get(OVRInput.RawButton.X)) left = true;
            if (OVRInput.Get(OVRInput.RawButton.A) || OVRInput.Get(OVRInput.RawButton.B)) left = false;
            var hand = left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            if ((connected & hand) == 0) hand = left ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
            if ((connected & hand) == 0) return;
            StopHaptics(); vibrating = hand;
            OVRInput.SetControllerVibration(.35f, (cue == Cue.Focus ? .045f : .10f)*ProductComfortPreferences.Haptics, hand);
            stopAt = Time.unscaledTime + (cue == Cue.Focus ? .018f : .035f);
        }

        public static void FlightPulse(float amplitude,float seconds)
        {
            if(current==null||Application.platform!=RuntimePlatform.Android||!Application.isFocused||!OVRManager.hasInputFocus)return;
            current.StopHaptics();current.vibrating=OVRInput.Controller.Touch;
            OVRInput.SetControllerVibration(.28f,Mathf.Clamp(amplitude,0,.18f)*ProductComfortPreferences.Haptics,OVRInput.Controller.Touch);
            current.stopAt=Time.unscaledTime+Mathf.Clamp(seconds,.015f,.12f);
        }
        private void StopHaptics()
        {
            if (vibrating == OVRInput.Controller.None) return;
            OVRInput.SetControllerVibration(0, 0, vibrating); vibrating = OVRInput.Controller.None;
        }
    }
}
