#if UNITY_INCLUDE_TESTS
using System.Collections;
using NUnit.Framework;
using QuestFlight.Product;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace QuestFlight.Editor.Tests
{
    public sealed class NarrationPlaybackTests
    {
        [UnityTest]
        public IEnumerator RoutineSpeechCompletesAndUrgentReplacementFades()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            yield return new EnterPlayMode();
            yield return CheckPlayback();
            yield return new ExitPlayMode();
        }
        private static IEnumerator CheckPlayback()
        {
            var go=new GameObject("NarrationRegressionProbe");
            go.AddComponent<AudioListener>();AudioListener.pause=false;Time.timeScale=1;
            var source=go.AddComponent<AudioSource>();source.volume=.7f;
            var first=AudioClip.Create("first",24000,1,24000,false);
            var second=AudioClip.Create("second",24000,1,24000,false);
            var speech=new NarrationPlayback(source);
            int started=0;
            speech.Request(first,()=>started++);
            yield return null;
            Assert.IsTrue(source.isPlaying);
            speech.Request(second,()=>started++);
            float deadline=Time.realtimeSinceStartup+4;
            float previous=Time.realtimeSinceStartup;
            while(started<2 && Time.realtimeSinceStartup<deadline)
            {
                if(source.isPlaying && source.clip==first)Assert.AreEqual(1,started);
                float now=Time.realtimeSinceStartup;speech.Tick(now-previous);previous=now;yield return null;
            }
            Assert.AreEqual(2,started,"Queued narration never started; audio time="+source.time+" playing="+source.isPlaying);
            Assert.AreSame(second,source.clip);
            speech.Request(first,()=>started++,true);
            speech.Tick(.09f);
            Assert.AreSame(second,source.clip,"Urgent interruption must fade before replacement");
            Assert.That(source.volume,Is.InRange(.2f,.5f));
            speech.Tick(.1f);speech.Tick(.2f);
            Assert.AreSame(first,source.clip);Assert.AreEqual(.7f,source.volume,.001f);
            // A paused line must resume instead of allowing pending speech to replace it.
            speech.Stop();speech.Request(first);speech.Request(second);
            source.Pause();speech.Tick(10,true);Assert.AreSame(first,source.clip);
            source.UnPause();speech.Stop();
            Object.Destroy(go);Object.Destroy(first);Object.Destroy(second);
        }
    }
}
#endif
