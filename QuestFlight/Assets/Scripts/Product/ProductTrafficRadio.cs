using System;
using QuestFlightLab.Environment;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.SystemsV2;
using UnityEngine;

namespace QuestFlight.Product
{
    /// <summary>Offline actor-specific advisories. No ATC authority, queues or invented traffic.</summary>
    public sealed class ProductTrafficRadio : MonoBehaviour
    {
        [Serializable] public sealed class Call { public string aircraftId,text;public AmbientFlightPhase phase;public int runway;public AudioClip clip; }
        [SerializeField] private AmbientTrafficDirector traffic;
        [SerializeField] private ProductFlightModeCoordinator modes;
        [SerializeField] private ProductionCockpitSystemsController systems;
        [SerializeField] private AudioSource source,instructor;
        [SerializeField] private Call[] calls=Array.Empty<Call>();
        [SerializeField] private TMPro.TextMeshPro caption;
        public void ConfigureCaption(TMPro.TextMeshPro value) => caption=value;
        private float decideAt,nextCall,gain;
        private AmbientAircraftActor speaking;
        private AmbientFlightPhase spokenPhase;
        private readonly int[] heardSegment=new int[5];
        private readonly int[] heardGeneration=new int[5];
        public void Configure(AmbientTrafficDirector t,ProductFlightModeCoordinator m,ProductionCockpitSystemsController s,AudioSource radio,AudioSource voice,Call[] content)
        {traffic=t;modes=m;systems=s;source=radio;instructor=voice;calls=content;}
        private void Start(){for(int i=0;i<5;i++)heardSegment[i]=-1;nextCall=Time.time+12;}
        private bool CanHear()
        {
            if(PlayerPrefs.GetInt("QuestFlight.TrafficRadio",1)==0||Time.timeScale<=0||!Application.isFocused||modes.AuthoredNavigation.IsOpen||
                modes.State==ProductFlightModeCoordinator.FlightModeState.Selection||modes.State==ProductFlightModeCoordinator.FlightModeState.Blocked||
                systems==null||!systems.TryGetCockpitSystemsSnapshotV2(out var s))return false;
            return s.avionicsBusPowered && s.com1ActiveKHz==CockpitSystemsPhysicalConfigurationV2.KbduCtafKHz;
        }
        private bool Relevant(AmbientAircraftActor a)
        {return a!=null && a.Started && !a.sailplane && !a.Suppressed && !a.Escaping && !a.Retired && a.Route!=null && Vector3.Distance(a.Position,traffic.player.CurrentState.positionUnityMeters)<12000;}
        private void Update()
        {
            if(source==null||traffic==null||modes==null)return;
            bool allowed=CanHear() && (instructor==null||!instructor.isPlaying) &&
                !modes.AuthoredTraining.VoicePlaying && !modes.AuthoredTraining.InstructionPanelVisible;
            bool relevant=speaking==null||Relevant(speaking)&&speaking.Phase==spokenPhase;
            gain=Mathf.MoveTowards(gain,allowed&&relevant?.48f*PlayerPrefs.GetFloat("QuestFlight.VoiceVolume",1):0,Time.unscaledDeltaTime*8);
            source.volume=gain;
            if(caption!=null)
            {
                bool visible=allowed&&relevant&&source.isPlaying&&ProductAppNavigation.CaptionsEnabled;
                if(caption.transform.parent.gameObject.activeSelf!=visible)caption.transform.parent.gameObject.SetActive(visible);
                caption.fontSize=.20f*ProductComfortPreferences.TextScale;
                caption.color=Color.white;
            }
            if((!allowed||!relevant)&&gain<=0){source.Stop();speaking=null;nextCall=Mathf.Max(nextCall,Time.time+8);}
            if(Time.time<decideAt)return;decideAt=Time.time+.25f;
            if(!allowed||source.isPlaying||Time.time<nextCall)return;
            for(int i=0;i<traffic.actors.Length&&i<5;i++)
            {
                var a=traffic.actors[i];if(!Relevant(a))continue;
                if(heardGeneration[i]==a.Generation&&heardSegment[i]==a.SegmentIndex)continue;
                foreach(var call in calls)
                {
                    if(call.aircraftId!=a.aircraftId||call.phase!=a.Phase||call.runway!=a.Route.runwayDirection||call.clip==null)continue;
                    heardGeneration[i]=a.Generation;heardSegment[i]=a.SegmentIndex;spokenPhase=a.Phase;speaking=a;
                    if(caption!=null)caption.text="RADIO  /  122.725\n"+call.text;
                    source.clip=call.clip;source.Play();nextCall=Time.time+35;return;
                }
            }
        }
        private void OnDisable(){if(source!=null)source.Stop();if(caption!=null)caption.transform.parent.gameObject.SetActive(false);}
    }
}
