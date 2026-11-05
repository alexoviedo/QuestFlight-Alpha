using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Runtime;
using QuestFlight.Product;
using UnityEngine;

namespace QuestFlightLab.Input
{
    public sealed partial class ProductControlInput
    {
        public bool GliderMode { get; private set; }
        public float GliderLeverPosition => _airbrakeLever;
        private TouchOverrideState _touchAirbrake, _touchGliderTrim;
        private float _airbrakeLever;
        private bool _releaseArmed, _releaseHeld, _releaseQueued;
        public void ConfigureAircraft(FlightAircraftDefinition definition)
        {
            ReleaseAllTouchOverrides(); ClearTowRelease();
            GliderMode = definition != null && definition.IsGlider;
            _airbrakeLever=0;_resetQueued=false;
            if (GliderMode) { Current.SetGliderNeutral();_throttle=0;_mixture=0;_flaps=0;_trim=definition.referenceTrimNoseUp;Current.trim=_trim; }
            else { Current.SetNeutral(defaultThrottle);_throttle=defaultThrottle;_mixture=1;_trim=0;_flaps=0;_carbHeat=0; }
            RequireTouchRelease();
        }
        private bool AircraftSupportsTouchChannel(ProductTouchControlChannel channel) =>
            (channel==ProductTouchControlChannel.GliderAirbrakeLever || channel==ProductTouchControlChannel.GliderTrimLever) ? GliderMode :
            !GliderMode || channel==ProductTouchControlChannel.PilotYoke;

        // This edge is independent of frame snapshots. The sole native clock
        // consumes it once; held/repeated input cannot reconnect a released rope.
        public void SampleTowRelease(bool pressed, bool available)
        {
            if (!GliderMode || !available) { ClearTowRelease();return; }
            if (!pressed) _releaseArmed=true;
            if (pressed && !_releaseHeld && _releaseArmed) _releaseQueued=true;
            _releaseHeld=pressed;
        }
        public bool RequestTowRelease()
        {
            if (!GliderMode || !CanAcceptTouchOverrides || Paused) return false;
            _releaseQueued=true;return true;
        }
        public bool ConsumeTowRelease() { bool value=_releaseQueued;_releaseQueued=false;return value; }
        private void ClearTowRelease() { _releaseArmed=_releaseHeld=_releaseQueued=false; }
        public void ClearSoaringTransientInput() { ClearTowRelease(); RequireTouchRelease(); }
        public static void ResolveGliderLever(float position, out float airbrake, out float brake)
        {
            position=Mathf.Clamp01(position);
            airbrake=Mathf.Clamp01(position/.85f);
            brake=Mathf.Clamp01((position-.85f)/.15f);
        }
        private void PublishGliderChannels()
        {
            Current.throttle=Current.mixture=Current.carbHeat=Current.flaps=0;
            Current.leftToeBrake=Current.rightToeBrake=0;
            ResolveGliderLever(_airbrakeLever,out Current.airbrake,out Current.wheelBrake);
            Current.releasePressed=_releaseQueued;
        }
        private void UpdateGliderControls(GamepadInputSnapshot snapshot,bool touchAvailable)
        {
            if(DemoOwnsControls&&_flightLessonGuidanceActive)
            {
                Current.throttle=Current.mixture=Current.carbHeat=Current.flaps=Current.leftToeBrake=Current.rightToeBrake=0;
                Current.airbrake=_flightLessonGuidance.airbrake;Current.wheelBrake=_flightLessonGuidance.wheelBrake;
                _airbrakeLever=Current.wheelBrake>0?.85f+.15f*Current.wheelBrake:.85f*Current.airbrake;
                Current.releasePressed=_releaseQueued;return;
            }
            bool available=!Paused && !_flightLessonInputSuppressed && !_touchControlsRequireRelease && Application.isFocused &&
                (Application.platform!=RuntimePlatform.Android || (OVRManager.hasInputFocus && (OVRManager.instance==null || OVRManager.instance.isUserPresent)));
            if (!_touchAirbrake.active && available)
            {
                float axis=snapshot.connected ? snapshot.rightStickY : 0;
                if (available && OVRInput.IsControllerConnected(ProductComfortPreferences.TrimController) &&
                    OVRInput.GetControllerOrientationTracked(ProductComfortPreferences.TrimController))
                    axis=ProductComfortPreferences.RawTrim;
                if (Mathf.Abs(axis)>.2f)_airbrakeLever=Mathf.Clamp01(_airbrakeLever-axis*.5f*Mathf.Min(Time.unscaledDeltaTime,.05f));
            }
            bool release=(snapshot.connected && snapshot.buttonSouth) || (available && OVRInput.Get(OVRInput.RawButton.A));
            SampleTowRelease(release,available);
            PublishGliderChannels();
        }
    }
}
