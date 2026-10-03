using System;
using System.IO;
using System.Linq;
using QuestFlight.Product;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlightLab.Editor
{
    public static class HabitatBirdAuthoring
    {
        public const string Root="Assets/Production/Environment/Wildlife/HabitatBirds";
        public const string World="Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
        public static void RunBatch()
        {
            try{Author();EditorApplication.Exit(0);}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void AuthorAndValidate()
        {
            try{Author();HabitatBirdValidation.RunBatch();}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void Author()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(string species in new[]{"RedTailedHawk","Osprey","CanadaGoose"})Prepare(species);
            var world=PrefabUtility.LoadPrefabContents(World);
            try
            {
                var traffic=world.GetComponentInChildren<AmbientTrafficDirector>(true);
                var old=world.GetComponentInChildren<GoldenEagleThermalSoaring>(true);
                old.enabled=false; // Retain serialized environment validation contract and accepted visual GUIDs.
                var existing=world.GetComponentInChildren<HabitatBirdDirector>(true);
                if(existing!=null)UnityEngine.Object.DestroyImmediate(existing.gameObject);
                var root=new GameObject("AuthoredHabitatWildlife");root.transform.SetParent(old.transform.parent,false);
                var director=root.AddComponent<HabitatBirdDirector>();
                director.traffic=traffic;director.flight=traffic.player;director.pilotCamera=traffic.pilotCamera;director.modes=traffic.flightMode;
                director.birds=new HabitatBirdActor[24];
                var eagle=old.GetComponent<HabitatBirdActor>()??old.gameObject.AddComponent<HabitatBirdActor>();
                eagle.birdId="golden_eagle";eagle.hero=true;eagle.animator=old.animator;
                eagle.lods=old.GetComponentsInChildren<SkinnedMeshRenderer>(true);eagle.triangles=eagle.lods.Select(r=>r.sharedMesh.triangles.Length/3).ToArray();
                eagle.span=2.1f;eagle.airspeed=15;eagle.flapPlaybackSpeed=.88f;eagle.phase=.7f;eagle.orbitRadius=75;eagle.cruiseAgl=330;
                eagle.habitatCenter=new Vector3(-5200,430,1800);eagle.secondaryCenter=new Vector3(-4200,330,1100);eagle.thermalIndex=1;
                director.birds[0]=eagle;
                director.birds[1]=Create(root,"RedTailedHawk","hawk_airport_fields",new Vector3(1700,350,2600),new Vector3(2300,230,2050),0,1.25f,12,1.6f);
                director.birds[2]=Create(root,"RedTailedHawk","hawk_east_grassland",new Vector3(3200,180,1100),new Vector3(2600,160,1800),-1,1.25f,12,3.4f);
                director.birds[3]=Create(root,"RedTailedHawk","hawk_foothill",new Vector3(-5200,500,1800),new Vector3(-4300,400,1200),1,1.25f,12,4.8f);
                director.birds[4]=Create(root,"Osprey","osprey_reservoir",new Vector3(100,145,4300),new Vector3(600,135,3800),-1,1.65f,12,2.2f);
                director.birds[5]=Create(root,"Osprey","osprey_ponds",new Vector3(3100,125,2350),new Vector3(2750,115,2800),-1,1.65f,12,4.1f);
                Vector3[] water={new Vector3(500,130,3900),new Vector3(3150,115,2400),new Vector3(2850,100,3200)};
                Vector3[] grass={new Vector3(1500,140,3450),new Vector3(3600,115,1650),new Vector3(3450,120,3550)};
                for(int g=0;g<3;g++)for(int member=0;member<6;member++)
                {
                    var b=Create(root,"CanadaGoose","goose_"+g+"_"+member,water[g],grass[g],-1,1.65f,17,.7f+g*1.8f+member*.71f);
                    b.goose=true;b.orbitRadius=210;b.cruiseAgl=90;b.flapPlaybackSpeed=4.6f;
                    if(member>0){b.leader=director.birds[6+g*6];int rank=(member+1)/2;b.formationOffset=new Vector2((member%2==0?1:-1)*rank*5,-rank*7);}
                    director.birds[6+g*6+member]=b;
                }
                foreach(var b in director.birds)foreach(var renderer in b.lods)
                {renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.updateWhenOffscreen=false;}
                var narration=world.GetComponentInChildren<ProductDemoNarration>(true);
                narration.ConfigureWildlife(director);
                var so=new SerializedObject(narration);var cues=so.FindProperty("cues");
                string[] ids={"tour_eagle_left","tour_eagle_right","tour_eagle_ahead"};
                string[] text={"Golden eagle ahead, slightly left.","Golden eagle ahead, slightly right.","Golden eagle straight ahead."};
                for(int i=0;i<ids.Length;i++)
                {
                    int at=-1;for(int n=0;n<cues.arraySize;n++)if(cues.GetArrayElementAtIndex(n).FindPropertyRelative("id").stringValue==ids[i])at=n;
                    if(at<0){at=cues.arraySize;cues.InsertArrayElementAtIndex(at);}
                    var cue=cues.GetArrayElementAtIndex(at);cue.FindPropertyRelative("id").stringValue=ids[i];cue.FindPropertyRelative("text").stringValue=text[i];
                    var clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Production/Training/Voice/"+ids[i]+".wav");
                    if(clip==null)throw new Exception("Generate Michael cue: "+ids[i]);
                    cue.FindPropertyRelative("clip").objectReferenceValue=clip;cue.FindPropertyRelative("distance").floatValue=0;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(world,World);
                Debug.Log("[Wildlife] Authored 24 birds, 4 species, 3 goose groups; both demo corridors and moving-subject cues.");
            }
            finally{PrefabUtility.UnloadPrefabContents(world);}
            AssetDatabase.SaveAssets();
        }
        private static HabitatBirdActor Create(GameObject root,string species,string id,Vector3 home,Vector3 away,int thermal,float span,float speed,float phase)
        {
            var actor=new GameObject(id);actor.transform.SetParent(root.transform,false);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/"+species+".fbx"),actor.transform);
            var b=actor.AddComponent<HabitatBirdActor>();b.birdId=id;b.habitatCenter=home;b.secondaryCenter=away;b.thermalIndex=thermal;b.span=span;b.airspeed=speed;b.phase=phase;
            b.cruiseAgl=species=="Osprey"?110:160;b.orbitRadius=species=="Osprey"?180:90;
            b.animator=model.GetComponent<Animator>();if(b.animator==null)b.animator=model.AddComponent<Animator>();b.animator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Root+"/"+species+".controller");
            b.animator.applyRootMotion=false;b.animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            b.lods=model.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderBy(r=>r.name).ToArray();
            if(b.lods.Length!=3)throw new Exception(species+" must have exactly three skinned LOD renderers");
            foreach(var renderer in b.lods){renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/"+species+".mat");renderer.enabled=false;}
            b.triangles=b.lods.Select(r=>r.sharedMesh.triangles.Length/3).ToArray();
            return b;
        }
        private static void Prepare(string species)
        {
            string path=Root+"/"+species;
            var texture=(TextureImporter)AssetImporter.GetAtPath(path+"_Albedo.png");
            texture.maxTextureSize=1024;texture.mipmapEnabled=true;texture.streamingMipmaps=true;texture.sRGBTexture=true;texture.alphaSource=TextureImporterAlphaSource.None;
            texture.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name="Android",overridden=true,maxTextureSize=1024,format=TextureImporterFormat.ASTC_6x6});texture.SaveAndReimport();
            var model=(ModelImporter)AssetImporter.GetAtPath(path+".fbx");model.animationType=ModelImporterAnimationType.Generic;model.importAnimation=true;model.importCameras=false;model.importLights=false;
            model.materialImportMode=ModelImporterMaterialImportMode.None;model.optimizeGameObjects=false;model.isReadable=false;
            var clips=model.defaultClipAnimations;
            foreach(var clip in clips){clip.name=clip.name.Contains("FlapLoop")?"FlapLoop":"SoarIdle";clip.loopTime=true;clip.loopPose=true;}
            model.clipAnimations=clips;model.SaveAndReimport();
            var material=AssetDatabase.LoadAssetAtPath<Material>(path+".mat");
            if(material==null){material=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(material,path+".mat");}
            material.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(path+"_Albedo.png");material.SetFloat("_Glossiness",.12f);material.SetFloat("_Metallic",0);
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path+".controller");
            if(controller==null)controller=AnimatorController.CreateAnimatorControllerAtPath(path+".controller");
            var sm=controller.layers[0].stateMachine;
            foreach(var state in sm.states)sm.RemoveState(state.state);
            foreach(string clipName in new[]{"FlapLoop","SoarIdle"})
            {
                var clip=AssetDatabase.LoadAllAssetsAtPath(path+".fbx").OfType<AnimationClip>().FirstOrDefault(c=>c.name==clipName);
                if(clip==null)throw new Exception(species+" missing real "+clipName+" clip");
                var state=sm.AddState(clipName);state.motion=clip;if(clipName=="SoarIdle")sm.defaultState=state;
            }
            EditorUtility.SetDirty(material);EditorUtility.SetDirty(controller);
        }
    }
}
