using System;
using UnityEngine;

namespace QuestFlight.Product
{
    /// <summary>One bounded pending utterance. Routine speech finishes; urgent replacements fade.</summary>
    public sealed class NarrationPlayback
    {
        private readonly AudioSource source;
        private AudioClip pending;
        private Action started;
        private Func<bool> relevant;
        private float fade=-1, gain=1, gap;
        private bool wasPlaying;
        public bool Busy => source.isPlaying || pending!=null;
        public NarrationPlayback(AudioSource source) { this.source=source; }
        public void Request(AudioClip clip,Action onStart=null,bool interrupt=false,Func<bool> stillRelevant=null)
        {
            if(clip==null)return;
            pending=clip;started=onStart;relevant=stillRelevant;
            if(interrupt && source.isPlaying && fade<0){gain=source.volume;fade=0;}
            if(!source.isPlaying && fade<0 && gap<=0)StartPending();
        }
        public void Tick(float dt,bool paused=false)
        {
            if(paused)return;
            if(fade>=0)
            {
                fade+=dt;source.volume=gain*(1-Mathf.SmoothStep(0,1,fade/.18f));
                if(fade<.18f)return;
                source.Stop();source.volume=gain;fade=-1;gap=.12f;wasPlaying=false;
            }
            if(source.isPlaying){wasPlaying=true;return;}
            if(wasPlaying){gap=.35f;wasPlaying=false;}
            gap=Mathf.Max(0,gap-dt);
            if(gap<=0)StartPending();
        }
        private void StartPending()
        {
            if(pending==null)return;
            var clip=pending;var callback=started;bool valid=relevant==null||relevant();
            pending=null;started=null;relevant=null;
            if(!valid)return;
            source.clip=clip;source.Play();wasPlaying=true;callback?.Invoke();
        }
        public void Stop()
        {
            pending=null;started=null;relevant=null;source.Stop();
            if(fade>=0)source.volume=gain;
            fade=-1;gap=0;wasPlaying=false;
        }
    }
}
