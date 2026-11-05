using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using QuestFlight.Product;
using QuestFlightLab.Flight.Soaring;

namespace QuestFlightLab.Editor
{
    public static class SoaringDemoAuthoring
    {
        public const string Root="Assets/Production/SoaringV1/Demo";
        [Serializable] private sealed class VoiceEntry {public string id,text;}
        [Serializable] private sealed class VoiceManifest {public VoiceEntry[] clips;}
        public static void Author()
        {
            Directory.CreateDirectory(Root);AssetDatabase.Refresh();
            T Copy<T>(string source,string name) where T:ScriptableObject
            {var p=Root+"/"+name+".asset";var asset=AssetDatabase.LoadAssetAtPath<T>(p);if(asset==null){AssetDatabase.CopyAsset(source,p);asset=AssetDatabase.LoadAssetAtPath<T>(p);}return asset;}
            var weather=Copy<SoaringWeatherDefinition>("Assets/Production/SoaringV1/Weather/ModerateThermal.asset","ScenicThermal");
            weather.displayName="SCENIC THERMAL · AUTHORED REFERENCE";
            weather.referenceScope="Warm-season scripted demonstration, not observed Boulder weather. Existing supported convective strength limit (3.3), light easterly wind; longer-lived sources support a continuous tour.";
            for(int i=0;i<weather.sources.Length;i++){var source=weather.sources[i];source.convectiveScale=3.3f;source.mixingHeight=2000;source.radiusScale=3.5f;source.lifetime=1800;source.period=2100;source.initialAge=240;weather.sources[i]=source;}
            weather.surfaceWind=new Vector2(-1.5f,0);weather.upperWind=new Vector2(-2.5f,0);EditorUtility.SetDirty(weather);
            var operation=Copy<BoulderSoaringOperationDefinition>("Assets/Production/SoaringV1/Operations/DevelopmentRunway8.asset","ScenicRunway08");
            operation.operationId="boulder.reference.scenic08";operation.displayName="Boulder soaring tour · reference";operation.weather=weather;operation.releaseFieldHeight=914;operation.releaseCenter=new Vector2(700,2200);operation.releaseRadius=2200;EditorUtility.SetDirty(operation);
            string path=Root+"/BoulderSoaringTour.asset";var profile=AssetDatabase.LoadAssetAtPath<SoaringDemoProfile>(path);
            if(profile==null){profile=ScriptableObject.CreateInstance<SoaringDemoProfile>();AssetDatabase.CreateAsset(profile,path);}
            profile.operation=operation;profile.airborneStart=new Vector3(1575,750,2600);profile.airborneHeading=180;profile.airborneSpeed=40;
            profile.scenicPoints=new[]{new Vector2(-100,3400),new Vector2(-1800,2500),new Vector2(-2100,700),new Vector2(-600,-600),new Vector2(650,550)};
            profile.shortPoints=new[]{new Vector2(0,3000),new Vector2(-1800,900),new Vector2(650,550)};
            profile.thermalRadius=100;profile.thermalGain=150;profile.minimumThermalSeconds=180;profile.maximumThermalSeconds=480;
            profile.provenance="Inspired by publicly described Mile High local sightseeing/discovery flights; not an operator route or endorsement. Existing development launch/landing surface retained pending verified glider-strip staging. Native SGS reference model; no certified fidelity. POI anchors require stereo inspection.";
            EditorUtility.SetDirty(profile);
            const string world="Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
            var root=PrefabUtility.LoadPrefabContents(world);
            try{root.GetComponentInChildren<ProductDemoPilot>(true).ConfigureSoaringDemo(profile);AuthorNarration(root);PrefabUtility.SaveAsPrefabAsset(root,world);}
            finally{PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();Debug.Log("[SoaringDemo] Authored profile and existing demo pilot binding.");
        }
        private static void AuthorNarration(GameObject root)
        {
            string repo=Directory.GetParent(Application.dataPath).Parent.FullName;
            var scripts=JsonUtility.FromJson<VoiceManifest>(File.ReadAllText(Path.Combine(repo,"tools/audio/soaring_demo_voice_scripts.json")));
            var cues=new List<ProductDemoNarration.Cue>();
            foreach(var entry in scripts.clips)
            {
                string path="Assets/Production/Training/Voice/"+entry.id+".wav";
                var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path);if(clip==null)throw new Exception("Missing voice "+entry.id);
                var importer=(AudioImporter)AssetImporter.GetAtPath(path);importer.forceToMono=true;
                var settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.CompressedInMemory;settings.compressionFormat=AudioCompressionFormat.Vorbis;settings.sampleRateSetting=AudioSampleRateSetting.OverrideSampleRate;settings.sampleRateOverride=24000;settings.quality=.65f;importer.defaultSampleSettings=settings;importer.SaveAndReimport();
                var cue=new ProductDemoNarration.Cue{id=entry.id,text=entry.text,clip=clip};
                switch(entry.id)
                {
                    case "sgs_reservoir":cue.landmark=new Vector3(250,20,4308);cue.distance=5500;break;
                    case "sgs_sanitas":cue.landmark=new Vector3(-6800,470,-350);cue.distance=10000;break;
                    case "sgs_flatirons":cue.landmark=new Vector3(-6500,650,-4600);cue.distance=11000;break;
                    case "sgs_downtown":cue.landmark=new Vector3(-4580,60,-2315);cue.distance=7000;break;
                    case "sgs_cu":cue.landmark=new Vector3(-3400,60,-3500);cue.distance=7000;break;
                    case "sgs_airport":cue.landmark=new Vector3(0,5,-30);cue.distance=5000;break;
                }
                cues.Add(cue);
            }
            root.GetComponentInChildren<ProductDemoNarration>(true).ConfigureSoaringCues(cues.ToArray());
        }
    }
}
