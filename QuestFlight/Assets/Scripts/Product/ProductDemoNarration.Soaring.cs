using System;
using System.Linq;
using UnityEngine;

namespace QuestFlight.Product
{
    public sealed partial class ProductDemoNarration
    {
        private bool climbSpoken;
        public void ConfigureSoaringCues(Cue[] content)
        { cues=cues.Where(c=>!c.id.StartsWith("sgs_")).Concat(content).ToArray(); }

        private void UpdateSoaring()
        {
            bool demo=modes.State==ProductFlightModeCoordinator.FlightModeState.Demo&&pilot.IsRunning;
            if(!demo){if(wasDemo){speech.Stop();captionRoot.SetActive(false);}wasDemo=false;return;}
            if(!wasDemo)
            {
                Array.Clear(played,0,played.Length);Array.Clear(visible,0,visible.Length);
                wasDemo=true;wasManual=eagleSpoken=climbSpoken=false;lastPhase="";nextFact=Time.time+30;
                Speak(pilot.ShortShowcase?"sgs_short_intro":"sgs_intro");
            }
            if(Time.timeScale<=0||modes.AuthoredNavigation.IsOpen)
            {if(!paused){voice.Pause();paused=true;}captionRoot.SetActive(false);return;}
            if(paused){voice.UnPause();paused=false;}
            speech.Tick(Time.unscaledDeltaTime);
            if(wasManual!=pilot.UserHasControls)
            {wasManual=pilot.UserHasControls;Speak(wasManual?"sgs_your_controls":"sgs_my_controls",true);nextFact=Time.time+20;}
            string phase=pilot.Phase;
            if(phase!=lastPhase)
            {
                if(phase=="sgs_tow"||phase=="sgs_release"||phase=="sgs_thermal"||phase=="sgs_sightseeing"||phase=="sgs_circuit"||phase=="sgs_final"||phase=="sgs_complete")
                    Speak(phase,phase=="sgs_release"||phase=="sgs_final");
                else if(phase=="sgs_descent")Speak(pilot.SoaringGuidance.EarlyReturn?"sgs_early_return":phase);
                lastPhase=phase;nextFact=Mathf.Max(nextFact,Time.time+20);
            }
            bool repeat=OVRInput.Get(OVRInput.RawButton.X);
            if(repeat&&!repeatHeld&&currentClip!=null)Speak(currentId,true);
            repeatHeld=repeat;
            if(!climbSpoken&&phase=="sgs_thermal"&&pilot.SoaringGuidance.ThermalGainMeters>25&&!speech.Busy)
            {Speak("sgs_climbing");climbSpoken=true;nextFact=Time.time+20;}
            if(Time.time>=nextDecision)
            {
                nextDecision=Time.time+.2f;
                bool touring=(phase=="sgs_sightseeing"||phase=="sgs_thermal")&&!pilot.NeedsAssistance&&!wasManual;
                if(touring&&wildlife!=null&&!eagleSpoken&&!speech.Busy&&wildlife.TryGetEagleCue(out string birdCue))
                {Speak(birdCue.Replace("tour_","sgs_"));eagleSpoken=true;nextFact=Time.time+25;}
                for(int i=0;i<cues.Length;i++)
                {
                    Cue cue=cues[i];if(!cue.id.StartsWith("sgs_")||cue.distance<=0)continue;
                    bool eligible=touring&&CueVisible(cue)&&(cue.id!="sgs_airport"||phase=="sgs_sightseeing"&&pilot.SoaringGuidance.ScenicIndex>=2);
                    visible[i]=eligible?visible[i]+.2f:0;
                    if(!played[i]&&visible[i]>=2&&!speech.Busy&&Time.time>=nextFact)
                    {Speak(cue.id);played[i]=true;nextFact=Time.time+30;break;}
                }
            }
            if(voice.isPlaying)captionUntil=Time.time+3;
            bool show=ProductAppNavigation.CaptionsEnabled&&(voice.isPlaying||Time.time<captionUntil);
            captionRoot.SetActive(show);
            if(show)instruction.Present(pilot.OwnershipLabel,pilot.ShortShowcase?"Short soaring tour":"Boulder soaring tour",currentText,(int)pilot.SoaringGuidance.Phase,9,true,pilot.NeedsAssistance,voice);
        }
        public bool CueVisible(Cue cue)
        {
            Vector3 delta=cue.landmark-pilotCamera.transform.position;
            Vector3 viewport=pilotCamera.WorldToViewportPoint(cue.landmark);
            if(delta.magnitude>cue.distance||delta.magnitude<300||viewport.z<=0||viewport.x<.08f||viewport.x>.92f||viewport.y<.1f||viewport.y>.9f)return false;
            for(int n=1;n<12;n++)
            {
                Vector3 p=Vector3.Lerp(pilotCamera.transform.position,cue.landmark,n/12f);
                if(!flight.terrainSurface.TrySample(p.x,p.z,out var ground)||ground.heightMeters>p.y)return false;
            }
            return true;
        }
    }
}
