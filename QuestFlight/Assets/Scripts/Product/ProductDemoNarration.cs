using System;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlight.Product
{
    /// <summary>Authored, bounded narration. Shares the existing instructor source across mutually exclusive modes.</summary>
    public sealed partial class ProductDemoNarration : MonoBehaviour
    {
        [Serializable] public sealed class Cue
        {
            public string id,text;
            public AudioClip clip;
            public Vector3 landmark;
            public float distance;
        }
        [SerializeField] private ProductFlightModeCoordinator modes;
        [SerializeField] private ProductDemoPilot pilot;
        [SerializeField] private FlightDynamicsCoordinator flight;
        [SerializeField] private Camera pilotCamera;
        [SerializeField] private AudioSource voice;
        [SerializeField] private GameObject captionRoot;
        [SerializeField] private ProductUiInstruction instruction;
        [SerializeField] private Cue[] cues=Array.Empty<Cue>();
        [SerializeField] private QuestFlightLab.Environment.HabitatBirdDirector wildlife;
        private bool eagleSpoken;
        private int narrationSession=-1;
        public void ConfigureWildlife(QuestFlightLab.Environment.HabitatBirdDirector value) => wildlife=value;
        private bool wasDemo,wasManual,paused,repeatHeld;
        private string lastPhase,currentText="",currentId="";
        private AudioClip currentClip;
        private float nextDecision,nextFact,captionUntil;
        private bool[] played;
        private float[] visible;
        private NarrationPlayback speech;
        public void Configure(ProductFlightModeCoordinator m,ProductDemoPilot p,FlightDynamicsCoordinator f,Camera camera,AudioSource source,GameObject captions,ProductUiInstruction view,Cue[] content)
        { modes=m;pilot=p;flight=f;pilotCamera=camera;voice=source;captionRoot=captions;instruction=view;cues=content; }
        private void Awake() { played=new bool[cues.Length];visible=new float[cues.Length];speech=new NarrationPlayback(voice); }
        private void Update()
        {
            if(narrationSession!=modes.FlightSessionVersion){if(speech!=null)speech.Stop();wasDemo=false;narrationSession=modes.FlightSessionVersion;}
            if(pilot.IsSoaringDemo){UpdateSoaring();return;}
            bool demo=modes.State==ProductFlightModeCoordinator.FlightModeState.Demo && pilot.IsRunning;
            if(!demo)
            {
                if(wasDemo){speech.Stop();captionRoot.SetActive(false);paused=false;}
                wasDemo=false;return;
            }
            if(!wasDemo)
            {
                Array.Clear(played,0,played.Length);Array.Clear(visible,0,visible.Length);
                wasDemo=true;wasManual=false;eagleSpoken=pilot.ShortShowcase;lastPhase="";nextFact=Time.time+30;Speak(pilot.ShortShowcase?"showcase_intro":"tour_intro");
            }
            if(Time.timeScale<=0 || modes.AuthoredNavigation.IsOpen)
            { if(!paused){voice.Pause();paused=true;}captionRoot.SetActive(false);return; }
            if(paused){voice.UnPause();paused=false;}
            speech.Tick(Time.unscaledDeltaTime);
            if(pilot.UserHasControls!=wasManual)
            { wasManual=pilot.UserHasControls;Speak(wasManual?"tour_your_controls":"tour_my_controls",true);nextFact=Time.time+20; }
            string phase=pilot.Phase;
            if(phase!=lastPhase)
            {
                if(phase=="landing_goaround")Speak("tour_goaround",true);
                else if(phase=="landing_approach")Speak("tour_final");
                else if(phase=="landing_taxiexit")Speak("tour_exit");
                else if(phase=="complete_clear_of_runway")Speak("tour_complete");
                lastPhase=phase;
            }
            bool repeat=OVRInput.Get(OVRInput.RawButton.X);
            if(repeat&&!repeatHeld && currentClip!=null && Time.time<captionUntil)Speak(currentId,true);
            repeatHeld=repeat;
            if(Time.time>=nextDecision)
            {
                nextDecision=Time.time+.2f;
                var state=flight.CurrentState;
                bool sightseeing=state.altitudeAglMeters>150 && Mathf.Abs((float)state.bankDegrees)<25 && !phase.StartsWith("landing") && !pilot.NeedsAssistance;
                if(sightseeing && wildlife!=null && !eagleSpoken && !speech.Busy && wildlife.TryGetEagleCue(out string eagleCue))
                {Speak(eagleCue);eagleSpoken=true;nextFact=Time.time+25;}
                // View eligibility controls when a fact starts, never whether its sentence finishes.
                for(int i=0;i<cues.Length;i++)
                {
                    var cue=cues[i];if(cue.distance<=0||cue.id.StartsWith("sgs_"))continue;
                    Vector3 delta=cue.landmark-pilotCamera.transform.position;
                    bool inView=sightseeing && delta.magnitude<cue.distance && delta.magnitude>300 && Vector3.Dot(pilotCamera.transform.forward,delta.normalized)>.65f;
                    if(inView)
                    {
                        for(int n=1;n<6;n++)
                        {
                            Vector3 point=Vector3.Lerp(pilotCamera.transform.position,cue.landmark,n/6f);
                            if(!flight.terrainSurface.TrySample(point.x,point.z,out var terrain) || terrain.heightMeters>point.y){inView=false;break;}
                        }
                    }
                    visible[i]=inView?visible[i]+.2f:Mathf.Max(0,visible[i]-.2f);
                    if(!played[i] && visible[i]>=2 && !speech.Busy && Time.time>=nextFact)
                    {Speak(cue.id);played[i]=true;nextFact=Time.time+35;break;}
                }
            }
            if(voice.isPlaying)captionUntil=Time.time+3;
            bool show=ProductAppNavigation.CaptionsEnabled && (voice.isPlaying || Time.time<captionUntil);
            captionRoot.SetActive(show);
            if(show)instruction.Present(pilot.OwnershipLabel,pilot.ShortShowcase?"Boulder arrival and landing":"Boulder scenic flight",currentText,
                pilot.ProgressState.currentSegmentIndex,pilot.RouteDefinition.segments.Length,true,pilot.NeedsAssistance,voice);
        }
        private void Speak(string id,bool interrupt=false)
        {
            foreach(var cue in cues)
                if(cue.id==id && cue.clip!=null)
                {
                    string requestedPhase=pilot.Phase;
                    speech.Request(cue.clip,()=>
                    {
                        currentId=id;currentText=cue.text;currentClip=cue.clip;captionUntil=Time.time+cue.clip.length+3;
                        Debug.Log("[DemoNarration] "+id+" phase="+pilot.Phase);
                    },interrupt,()=>!id.StartsWith("tour_final") || pilot.Phase==requestedPhase);
                    return;
                }
        }
    }
}
