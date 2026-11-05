#if UNITY_INCLUDE_TESTS
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QuestFlightLab.Audio;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace QuestFlightLab.Editor.Tests
{
    public sealed class SoaringEngineAudioTests
    {
        [Test]
        public void EveryEngineSourceHasAnOwnerAndExternalSoundHasFiniteSpatialFalloff()
        {
            var world=AssetDatabase.LoadAssetAtPath<GameObject>(SoaringEngineAudioRepair.WorldPath);
            var tug=world.GetComponentsInChildren<NativeTugAudio>(true).Single();
            var actor=tug.GetComponentInParent<AmbientAircraftActor>(true);
            Assert.IsFalse(actor.sailplane);
            Assert.IsNotNull(tug.listener.GetComponent<Camera>());
            Assert.AreSame(world.GetComponentsInChildren<AudioListener>(true).Single(),tug.listener);
            Assert.AreEqual(1,tug.engine.spatialBlend);
            Assert.AreEqual(0,tug.engine.spread);
            Assert.AreEqual(AudioRolloffMode.Custom,tug.engine.rolloffMode);
            Assert.AreEqual("ExternalWorld",tug.engine.outputAudioMixerGroup.name);
            Assert.Less(Vector3.Distance(tug.engine.transform.position,actor.propellers.First(p=>p!=null).position),.01f);
            var curve=tug.engine.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            float previous=1;
            for(int distance=0;distance<=2400;distance+=2)
            {
                float gain=curve.Evaluate(distance/tug.engine.maxDistance);
                Assert.That(gain,Is.InRange(0f,previous+.000001f));previous=gain;
                if(distance>=1200)Assert.AreEqual(0,gain,.000001f);
            }
            Assert.Greater(curve.Evaluate(60f/1200),curve.Evaluate(600f/1200)*8);
            Assert.AreEqual(.5f,curve.Evaluate(16f/1200),.001f);
            var ownship=world.GetComponentInChildren<ProductionC172AudioController>(true);
            var engineClips=new[]{ownship.Profile.engineIdleLoop,ownship.Profile.engineCruiseLoop,ownship.Profile.propellerHighLoop,ownship.Profile.engineStart};
            var sources=world.GetComponentsInChildren<AudioSource>(true).Where(s=>s.clip!=null&&engineClips.Contains(s.clip)).ToArray();
            Assert.AreEqual(4,sources.Length,"Engine inventory changed: review every new sound owner.");
            foreach(var source in sources)
            {
                Assert.IsFalse(source.playOnAwake);
                Assert.IsTrue(source.GetComponentInParent<ProductionC172AudioController>(true)!=null || source==tug.engine,source.name);
            }
        }

        [UnityTest]
        public IEnumerator C172DisableAndGliderSelectionStopAllOwnedPlaybackAndAllowC172Resume()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            yield return new EnterPlayMode();
            yield return CheckPlayback();
            yield return new ExitPlayMode();
        }

        private static IEnumerator CheckPlayback()
        {
            var go=new GameObject("Owned audio lifecycle probe");
            var authority=new GameObject("Inactive state fixture");authority.SetActive(false);
            var profile=ScriptableObject.CreateInstance<ProductionC172AudioProfile>();
            var glider=ScriptableObject.CreateInstance<FlightAircraftDefinition>();glider.kind=FlightAircraftKind.Sgs233Reference;
            var clip=AudioClip.Create("Silent lifecycle probe",48000*2,1,48000,false);
            var voiceObject=new GameObject("Unrelated instructor voice");
            try
            {
                go.AddComponent<AudioListener>();
                var coordinator=authority.AddComponent<FlightDynamicsCoordinator>();
                var controller=go.AddComponent<ProductionC172AudioController>();
                var sources=Enumerable.Range(0,10).Select(_=>go.AddComponent<AudioSource>()).ToArray();
                foreach(var source in sources){source.clip=clip;source.loop=true;}
                controller.ConfigureAuthoredReferences(profile,coordinator,null,null,sources[0],sources[1],sources[2],sources[3],sources[4],sources[5],sources[6],sources[7],sources[8],sources[9]);
                const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(FlightDynamicsCoordinator).GetField("_currentState",flags).SetValue(coordinator,new FlightDynamicsState{engineRpm=2400,rotationUnity=Quaternion.identity});
                var voice=voiceObject.AddComponent<AudioSource>();voice.clip=clip;voice.loop=true;voice.Play();
                foreach(var source in sources){source.volume=.5f;source.Play();}
                yield return null;
                Assert.IsTrue(sources[1].isPlaying);
                controller.enabled=false;
                foreach(var source in sources){Assert.IsFalse(source.isPlaying);Assert.AreEqual(0,source.volume);}
                Assert.IsTrue(voice.isPlaying,"Stopping C172 must not stop narration.");
                controller.enabled=true;yield return new WaitForSecondsRealtime(.5f);
                Assert.IsTrue(sources[1].isPlaying,"C172 loops did not restart after reselecting it.");
                Assert.Greater(sources[1].volume,0);
                coordinator.aircraftDefinition=glider;
                yield return new WaitForSecondsRealtime(.05f);
                foreach(var source in sources){Assert.IsFalse(source.isPlaying);Assert.AreEqual(0,source.volume);}
                coordinator.aircraftDefinition=null;
                yield return new WaitForSecondsRealtime(.5f);
                Assert.IsTrue(sources[1].isPlaying);
                Assert.Greater(sources[1].volume,0);
            }
            finally
            {
                Object.Destroy(go);Object.Destroy(authority);Object.Destroy(profile);Object.Destroy(glider);Object.Destroy(clip);Object.Destroy(voiceObject);
            }
        }
    }
}
#endif
