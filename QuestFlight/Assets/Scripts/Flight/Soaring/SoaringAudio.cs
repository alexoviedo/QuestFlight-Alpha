using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Flight.Soaring
{
    // Authored sources reuse the generic airframe/ground clips. Ideal TE audio
    // is an explicit optional training aid, not the reference pressure VSI.
    public sealed class SoaringAudio : MonoBehaviour
    {
        public FlightDynamicsCoordinator flight;
        public AudioSource air,ground,totalEnergyAid;
        public bool totalEnergyTrainingAid;
        private AudioClip tone;
        private void Awake()
        {
            if(flight==null||air==null||ground==null||totalEnergyAid==null){enabled=false;return;}
            const int count=2205;var samples=new float[count];
            for(int i=0;i<count;i++)samples[i]=Mathf.Sin(i*2*Mathf.PI*600/22050)*.25f;
            tone=AudioClip.Create("Ideal TE training tone",count,1,22050,false);tone.SetData(samples,0);totalEnergyAid.clip=tone;
        }
        private static void Apply(AudioSource source,float volume,float pitch)
        {
            source.volume=volume*Mathf.Clamp01(PlayerPrefs.GetFloat("QuestFlight.AircraftVolume",1));source.pitch=pitch;
            if(volume>.001f){if(!source.isPlaying)source.Play();}else if(source.isPlaying)source.Stop();
        }
        private void Update()
        {
            bool active=flight.IsInitialized&&flight.aircraftDefinition.IsGlider&&!flight.RecoveryHold&&Time.timeScale>0;
            if(!active){Apply(air,0,1);Apply(ground,0,1);Apply(totalEnergyAid,0,1);return;}
            var state=flight.CurrentState;float tas=(float)flight.NativeDiagnostics.trueAirspeedKnots;
            Apply(air,Mathf.Clamp01((tas-12)/65)*.22f,Mathf.Lerp(.75f,1.25f,Mathf.Clamp01(tas/80)));
            Apply(ground,state.weightOnWheels?Mathf.Clamp01((float)state.groundSpeedKnots/35)*.18f:0,1);
            float te=(float)flight.SoaringInstruments.IdealTotalEnergyFpm;
            double time=state.simulationTimeSeconds;float period=Mathf.Lerp(1,.22f,Mathf.Clamp01(te/800));
            bool pulse=te>50&&(time%period)<period*.45;
            Apply(totalEnergyAid,totalEnergyTrainingAid&&pulse?.08f:0,Mathf.Clamp(1+te/1500,.5f,1.8f));
        }
        private void OnDisable(){if(air!=null)air.Stop();if(ground!=null)ground.Stop();if(totalEnergyAid!=null)totalEnergyAid.Stop();}
        private void OnDestroy(){if(tone!=null)Destroy(tone);}
    }
}
