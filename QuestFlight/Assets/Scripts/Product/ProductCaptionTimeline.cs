using System;
using System.Collections.Generic;
using UnityEngine;

namespace QuestFlight.Product
{
    /// <summary>Offline authenticated word timings, driven by the playing recording's sample clock.</summary>
    public sealed class ProductCaptionTimeline
    {
        [Serializable] public sealed class Phrase {public float start,end;public string text;}
        [Serializable] public sealed class Clip {public string id,sha256;public float seconds;public Phrase[] phrases;}
        [Serializable] private sealed class Catalog {public Clip[] clips;}
        private readonly Dictionary<string,Clip> clips=new Dictionary<string,Clip>();
        public ProductCaptionTimeline(TextAsset data)
        {if(data==null)return;var catalog=JsonUtility.FromJson<Catalog>(data.text);foreach(var c in catalog.clips)clips[c.id]=c;}
        public string At(AudioSource source,string fallback)
        {
            if(source==null||source.clip==null||!clips.TryGetValue(source.clip.name,out var clip))return fallback;
            return PhraseAt(clip,source.isPlaying?source.time:clip.seconds);
        }
        public static string PhraseAt(Clip clip,float time)
        {
            if(clip?.phrases==null||clip.phrases.Length==0)return "";
            int index=0;while(index+1<clip.phrases.Length&&time>=clip.phrases[index+1].start)index++;
            return clip.phrases[index].text;
        }
    }
}
