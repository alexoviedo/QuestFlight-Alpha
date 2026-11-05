using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Flight.Soaring
{
    // Existing licensed generic piston recording, not a PA18 acoustic model.
    // RPM and running state come only from the physical tug session.
    public sealed class NativeTugAudio : MonoBehaviour
    {
        public FlightDynamicsCoordinator flight;
        public AudioSource engine;
        public AudioListener listener;

        // Traffic updates the same authored tug transform in Update. Sound follows
        // that interpolated aircraft, never the glider or tracked camera.
        private void LateUpdate() => RefreshAudio();

        public void RefreshAudio()
        {
            if(engine==null)return;
            var pair=flight==null?null:flight.TowSession;
            if(pair==null || !flight.IsInitialized || !flight.isActiveAndEnabled || pair.Faulted || !pair.TugState.IsFinite ||
                flight.aircraftDefinition==null || !flight.aircraftDefinition.IsGlider ||
                listener==null || !listener.isActiveAndEnabled)
            { Silence(false); return; }
            float distance=Vector3.Distance(listener.transform.position,engine.transform.position);
            float preference=Mathf.Clamp01(PlayerPrefs.GetFloat("QuestFlight.AircraftVolume",1));
            if(Time.timeScale<=0 || flight.RecoveryHold || AudioListener.pause ||
                !float.IsFinite(distance) || distance>=engine.maxDistance || preference<=0)
            { Silence(true); return; }
            float rpm=(float)pair.TugState.engineRpm;
            if(rpm<=100){Silence(false);return;}
            engine.pitch=Mathf.Clamp(rpm/2200,.35f,1.3f);
            engine.volume=.25f*Mathf.Clamp01((rpm-100)/900)*preference;
            if(!engine.isPlaying){engine.UnPause();if(!engine.isPlaying)engine.Play();}
        }

        private void Silence(bool pause)
        {
            if(engine==null)return;
            engine.volume=0;
            if(pause)engine.Pause();else engine.Stop();
        }

        private void OnDisable() => Silence(false);
    }
}
