using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestFlight.Product;
using QuestFlightLab.Audio;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Soaring;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor
{
    public static partial class ProductionVisualQaBatchRunner
    {
        public static void VerifySoaringEngineAudio()
        {
            string output=System.Environment.GetEnvironmentVariable("QF_ENGINE_AUDIO_QA");
            Directory.CreateDirectory(output);
            var checks=new List<string>();
            bool hadVolume=PlayerPrefs.HasKey("QuestFlight.AircraftVolume");
            float oldVolume=PlayerPrefs.GetFloat("QuestFlight.AircraftVolume",1);
            void Require(bool pass,string check){checks.Add((pass?"PASS ":"FAIL ")+check);if(!pass)throw new Exception(check);}
            try
            {
                var c=LoadContext(960,720,new ProductionVisualQaReport());
                var mode=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ProductFlightModeCoordinator>(true)).Single();
                Time.timeScale=0;InvokePrivate(mode,"BeginSelection");
                var flight=mode.AuthoredFlightCoordinator;
                Require(flight.InitializeSelectedBackend(),"native C172 initialization");
                Require(mode.PrepareGroundTow(),"native ground tow selection");
                mode.SelectFreeFlight();mode.AuthoredModeMenuView.CompleteTransitionForVisualQa();
                Time.timeScale=1;flight.SetRecoveryHold(false);PlayerPrefs.SetFloat("QuestFlight.AircraftVolume",1);
                var tug=c.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<NativeTugAudio>(true)).Single();
                tug.listener.enabled=true; // Visual QA disables listeners; restore the authored listener for this audio probe.
                var actor=tug.GetComponentInParent<AmbientAircraftActor>();
                var ownship=c.rig.GetComponentInChildren<ProductionC172AudioController>(true);
                Require(ownship!=null&&!ownship.enabled,"glider selection disables C172 audio owner");
                Require(flight.TowSession.TugState.engineRpm>100,"actual native tug engine running");
                var source=tug.engine;Vector3 initial=actor.transform.position;
                var originalEye=c.rig.PilotSeatAnchor.position;
                foreach(float distance in new[]{60f,250f,600f,1199f,1201f,5000f,60f})
                {
                    // Editor presentation probe moves the emitter's aircraft only.
                    // Neither native state nor the tracked camera is written.
                    actor.transform.position+=tug.listener.transform.position+Vector3.right*distance-source.transform.position;
                    tug.RefreshAudio();
                    float actual=Vector3.Distance(source.transform.position,tug.listener.transform.position);
                    float effective=source.volume*source.GetCustomCurve(AudioSourceCurveType.CustomRolloff).Evaluate(actual/source.maxDistance);
                    Require(distance>=1200?source.volume==0&&effective==0:source.volume>0,$"range {distance}m effective gain={effective:F7}");
                }
                Time.timeScale=0;tug.RefreshAudio();Require(source.volume==0,"pause silence");
                Time.timeScale=1;tug.RefreshAudio();Require(source.volume>0,"resume restores nearby tug");
                flight.SetRecoveryHold(true);tug.RefreshAudio();Require(source.volume==0,"recovery hold silence");flight.SetRecoveryHold(false);
                PlayerPrefs.SetFloat("QuestFlight.AircraftVolume",0);tug.RefreshAudio();Require(source.volume==0,"aircraft-volume mute includes tug");PlayerPrefs.SetFloat("QuestFlight.AircraftVolume",1);
                flight.TowSession.ReleaseTug();tug.RefreshAudio();Require(source.volume>0,"release does not silence a still-nearby running tug");
                Require(c.rig.PilotSeatAnchor.position==originalEye,"audio checks do not move pilot view");
                actor.transform.position=initial;
                using(var preview=new ProductionVisualQaSettledSdkPreview(c.rig.transform))
                {
                    for(int eye=0;eye<2;eye++)
                    {
                        c.camera.transform.SetPositionAndRotation(originalEye+c.rig.transform.right*(eye==0?-.032f:.032f),c.rig.transform.rotation);
                        var pixels=Render(c.camera,960,720,out _);File.WriteAllBytes(Path.Combine(output,"sgs_audio_check_"+eye+".png"),pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);
                    }
                }
                Time.timeScale=0;mode.ReturnToMainMenu();Require(mode.SelectAircraft(false),"return to C172");
                tug.RefreshAudio();Require(source.volume==0,"disposed tow session cannot leave engine sound");
                Require(ownship.enabled,"C172 sound owner restored");
            }
            finally
            {
                if(hadVolume)PlayerPrefs.SetFloat("QuestFlight.AircraftVolume",oldVolume);else PlayerPrefs.DeleteKey("QuestFlight.AircraftVolume");
                Time.timeScale=1;File.WriteAllLines(Path.Combine(output,"native_audio_scenarios.txt"),checks);
            }
            SoaringEngineAudioRepair.BuildAndroidAudio();
        }
    }
}
