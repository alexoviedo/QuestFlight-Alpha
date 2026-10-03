using System;
using System.Collections.Generic;
using QuestFlight.Product;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Reflection;
using QuestFlight.Product.Training;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Audio;
using UnityEngine.Audio;

namespace QuestFlight.Editor
{
    public static class DemoTourAuthoring
    {
        [Serializable] private sealed class VoiceEntry {public string id,text;}
        [Serializable] private sealed class VoiceManifest {public VoiceEntry[] clips;}
        public static void ApplyProduct()
        {
            try
            {
                AssetDatabase.Refresh();AuthorRoutes();
                var world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);
                try
                {
                    var pilot=world.GetComponentInChildren<ProductDemoPilot>(true);
                    pilot.ConfigureRouteProfileForAuthoring(AssetDatabase.LoadAssetAtPath<FlightLessonRouteProfile>("Assets/Production/Profiles/KBDU_ScenicDemo8.asset"));
                    pilot.ConfigureTourForAuthoring(AssetDatabase.LoadAssetAtPath<AmbientAircraftRoute>("Assets/Production/Environment/AmbientAircraftV1/Route_0_8.asset"),world.GetComponentInChildren<AmbientTrafficDirector>(true));
                    pilot.ConfigureRunway26ForAuthoring(AssetDatabase.LoadAssetAtPath<FlightLessonRouteProfile>("Assets/Production/Profiles/KBDU_ScenicDemo26.asset"),AssetDatabase.LoadAssetAtPath<AmbientAircraftRoute>("Assets/Production/Environment/AmbientAircraftV1/Route_0_26.asset"));
                    var training=world.GetComponentInChildren<ProductTrainingController>(true);
                    var serialized=new SerializedObject(training);
                    var voice=(AudioSource)serialized.FindProperty("voice").objectReferenceValue;
                    var captions=(GameObject)serialized.FindProperty("cueRoot").objectReferenceValue;
                    var instruction=(ProductUiInstruction)serialized.FindProperty("polishedInstruction").objectReferenceValue;
                    var mixer=AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Production/Audio/C172/ProductionC172Audio.mixer");
                    var groups=mixer.FindMatchingGroups("Instructor");
                    if(groups.Length==0)
                    {
                        Type type=mixer.GetType();
                        object group=type.GetMethod("CreateNewGroup",BindingFlags.Public|BindingFlags.Instance).Invoke(mixer,new object[]{"Instructor",false});
                        object master=type.GetProperty("masterGroup",BindingFlags.Public|BindingFlags.Instance).GetValue(mixer);
                        type.GetMethod("AddChildToParent",BindingFlags.Public|BindingFlags.Instance).Invoke(mixer,new[]{group,master});
                        EditorUtility.SetDirty(mixer);groups=mixer.FindMatchingGroups("Instructor");
                    }
                    voice.outputAudioMixerGroup=groups[0];voice.spatialBlend=0;voice.volume=1f;
                    var audio=world.GetComponentInChildren<ProductionC172AudioController>(true);audio.ConfigureInstructorVoice(voice);
                    world.GetComponentInChildren<ProductAppNavigation>(true).ConfigureAudio(voice,audio);
                    var narration=world.GetComponentInChildren<ProductDemoNarration>(true);
                    if(narration==null)narration=pilot.gameObject.AddComponent<ProductDemoNarration>();
                    string repo=Directory.GetParent(Application.dataPath).Parent.FullName;
                    var scripts=JsonUtility.FromJson<VoiceManifest>(File.ReadAllText(Path.Combine(repo,"tools/audio/demo_voice_scripts.json")));
                    var cues=new List<ProductDemoNarration.Cue>();
                    foreach(var entry in scripts.clips)
                    {
                        var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(ProductTrainingAuthoring.Root+"/Voice/"+entry.id+".wav");
                        if(clip==null)throw new Exception("Generate tour voice first: "+entry.id);
                        var cue=new ProductDemoNarration.Cue{id=entry.id,text=entry.text,clip=clip};
                        if(entry.id=="tour_reservoir"){cue.landmark=new Vector3(250,20,4308);cue.distance=3500;}
                        if(entry.id=="tour_flatirons"){cue.landmark=new Vector3(-6500,650,-4600);cue.distance=8500;}
                        if(entry.id=="tour_east_boulder"){cue.landmark=new Vector3(900,30,-2300);cue.distance=3800;}
                        cues.Add(cue);
                    }
                    narration.Configure(world.GetComponentInChildren<ProductFlightModeCoordinator>(true),pilot,world.GetComponentInChildren<FlightDynamicsCoordinator>(true),
                        world.GetComponentInChildren<AmbientTrafficDirector>(true).pilotCamera,voice,captions,instruction,cues.ToArray());
                    PrefabUtility.SaveAsPrefabAsset(world,ProductTrainingAuthoring.World);
                }
                finally{PrefabUtility.UnloadPrefabContents(world);}
                foreach(string guid in AssetDatabase.FindAssets("t:AudioClip",new[]{ProductTrainingAuthoring.Root+"/Voice"}))
                {
                    var importer=(AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                    importer.forceToMono=true;var settings=importer.defaultSampleSettings;
                    settings.loadType=AudioClipLoadType.CompressedInMemory;settings.compressionFormat=AudioCompressionFormat.Vorbis;
                    settings.sampleRateSetting=AudioSampleRateSetting.OverrideSampleRate;settings.sampleRateOverride=24000;settings.quality=.65f;
                    importer.defaultSampleSettings=settings;importer.SaveAndReimport();
                }
                AssetDatabase.SaveAssets();Debug.Log("[DemoTour] Authored route, shared instructor source, captions and voice mix.");EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void RoutesAndValidate()
        {
            try { AuthorRoutes();DemoFlightNativeValidation.RunFullFlight(); }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void AuthorRoutes()
        {
            foreach(int runway in new[]{8,26})
            {
                int sign=runway==8?1:-1;
                var w=new List<FlightLessonWaypoint>();var s=new List<FlightLessonSegment>();
                void Point(string id,float east,float north,float altitude,float speed,float radius=100)
                    =>w.Add(new FlightLessonWaypoint(id,new Vector3(east,0,north),altitude,speed,radius));
                Point("runway_start",-630*sign,-30,5290,55,40);
                Point("departure_gate",1400*sign,-30,6000,74,80);
                Point("climb_gate",1800*sign,2000,6900,85,180);
                Point("reservoir_entry",800,3600,7500,90,180);
                Point("reservoir_vista",-700,4300,7500,90,180);
                Point("foothills_entry",-1900,2300,7500,90,180);
                Point("flatirons_vista",-2200,-1900,7500,90,180);
                Point("city_vista",1500,-2700,7500,90,180);
                Point("east_return",3900,-1600,7200,90,180);
                Point("arrival_gate",3800*sign,950,6500,85,130);
                Point("downwind_entry",1900*sign,950,6300,80,100);
                Point("downwind_end",-1500*sign,950,6100,75,50);
                Point("base_entry",-2050*sign,400,5800,70,100);
                Point("base_end",-2050*sign,250,5700,68,70);
                Point("final_intercept",-1770*sign,-30,5600,65,100);
                Point("runway_threshold",-630*sign,-30,5290,65,35);
                Point("rollout",350*sign,-30,5290,15,35);
                for(int i=0;i<w.Count-1;i++)
                {
                    var kind=i==0?FlightLessonSegmentKind.Departure:i<3?FlightLessonSegmentKind.Climb:i<8?FlightLessonSegmentKind.ScenicOverflight:i==8?FlightLessonSegmentKind.Return:i==9?FlightLessonSegmentKind.PatternEntry:i==10?FlightLessonSegmentKind.Downwind:i<13?FlightLessonSegmentKind.Base:i<15?FlightLessonSegmentKind.Final:FlightLessonSegmentKind.Rollout;
                    s.Add(new FlightLessonSegment(w[i+1].id,kind,i,i+1,i<9?350:150,i==0?8:23,i==0?1100:800));
                }
                var arc=s[11];arc.geometry=FlightLessonPathGeometry.Arc;arc.arcCenterUnityMeters=new Vector3(-1500*sign,0,400);arc.arcClockwise=sign<0;s[11]=arc;
                arc=s[13];arc.geometry=FlightLessonPathGeometry.Arc;arc.arcCenterUnityMeters=new Vector3(-1770*sign,0,250);arc.arcClockwise=sign<0;arc.lookAheadMeters=100;s[13]=arc;
                var route=new FlightLessonRouteDefinition{lessonId="boulder_scenic_demo_"+runway,displayName="Boulder scenic tour",operationalDisclaimer="Simulator sightseeing route; not an official airport procedure or navigation aid.",waypoints=w.ToArray(),segments=s.ToArray(),initialRecoveryCheckpointIndex=0,
                    recoveryCheckpoints=new[]{new FlightLessonRecoveryCheckpoint("departure",0,w[0].positionUnityMeters,5290,0,sign>0?90:270)}};
                if(!route.TryValidate(out var failure))throw new Exception(failure);
                string path=$"Assets/Production/Profiles/KBDU_ScenicDemo{runway}.asset";
                var profile=AssetDatabase.LoadAssetAtPath<FlightLessonRouteProfile>(path);
                if(profile==null){profile=ScriptableObject.CreateInstance<FlightLessonRouteProfile>();AssetDatabase.CreateAsset(profile,path);}
                profile.SetDefinitionForAuthoring(route);EditorUtility.SetDirty(profile);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
