using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using QuestFlightLab.Environment;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.TouchInputV2;
using QuestFlightLab.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    public static class ProductReleasePolishAuthoring
    {
        const string Root="Assets/Production/UI/ReleasePolish";
        const string World="Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
        public static void RunBatch()
        {
            try {Apply();Debug.Log("[ReleasePolish] Authored all release polish references.");EditorApplication.Exit(0);}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void Apply()
        {
            Directory.CreateDirectory(Root);AssetDatabase.Refresh();
            var world=PrefabUtility.LoadPrefabContents(World);
            try
            {
                var nav=world.GetComponentInChildren<ProductAppNavigation>(true);
                var modes=world.GetComponentInChildren<ProductFlightModeCoordinator>(true);
                var training=world.GetComponentInChildren<ProductTrainingController>(true);
                var voice=(AudioSource)new SerializedObject(training).FindProperty("voice").objectReferenceValue;
                var experience=modes.GetComponent<ProductFlightExperience>();if(experience==null)experience=modes.gameObject.AddComponent<ProductFlightExperience>();
                var panel=(GameObject)new SerializedObject(nav).FindProperty("panel").objectReferenceValue;
                var old=panel.transform.Find("LocalOrientationMap");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
                var map=new GameObject("LocalOrientationMap");map.transform.SetParent(panel.transform,false);map.transform.localPosition=new Vector3(-.15f,.028f,1.02f);map.transform.localScale=Vector3.one*.56f;
                var terrain=modes.AuthoredFlightCoordinator.terrainSurface;
                Vector2 center=terrain.macroMinimumEnuMeters+terrain.macroSizeMeters*.5f;float span=Mathf.Max(terrain.macroSizeMeters.x,terrain.macroSizeMeters.y);
                AuthorTerrainOverview(map.transform,terrain);
                Material mat=AssetDatabase.LoadAssetAtPath<Material>(Root+"/MapInk.mat");
                if(mat==null){mat=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Production/UI/Interface/ModalSurface.mat"));AssetDatabase.CreateAsset(mat,Root+"/MapInk.mat");}
                mat.shader=Shader.Find("Unlit/Color");mat.SetColor("_Color",new Color(.47f,.78f,.9f,1));mat.renderQueue=3510;
                var verts=new List<Vector3>();var indices=new List<int>();
                Action<Vector2,Vector2,float> line=(a,b,w)=>{Vector2 d=(b-a).normalized;Vector2 n=new Vector2(-d.y,d.x)*w;int k=verts.Count;verts.Add(a+n);verts.Add(a-n);verts.Add(b+n);verts.Add(b-n);indices.AddRange(new[]{k,k+2,k+1,k+2,k+3,k+1});};
                Vector2 lo=(terrain.macroMinimumEnuMeters-center)/span*.58f,hi=lo+terrain.macroSizeMeters/span*.58f;
                line(lo,new Vector2(hi.x,lo.y),.0015f);line(new Vector2(hi.x,lo.y),hi,.0015f);line(hi,new Vector2(lo.x,hi.y),.0015f);line(new Vector2(lo.x,hi.y),lo,.0015f);
                var points=new[]{new Vector2(-555,0),new Vector2(250,4308),new Vector2(-6500,-4600),new Vector2(900,-2300)};
                string[] names={"KBDU 08 / 26","Reservoir","Flatirons","East Boulder"};
                var theme=AssetDatabase.LoadAssetAtPath<ProductUiTheme>(AssetDatabase.FindAssets("t:ProductUiTheme").Select(AssetDatabase.GUIDToAssetPath).Single());
                for(int i=0;i<points.Length;i++)
                {
                    Vector2 p=(points[i]-center)/span*.58f;line(p+Vector2.left*.006f,p+Vector2.right*.006f,.002f);line(p+Vector2.down*.006f,p+Vector2.up*.006f,.002f);
                    Label(map.transform,(i+1).ToString(),new Vector3(p.x+.009f,p.y+.009f,-.002f),theme,.28f);
                    Label(map.transform,(i+1)+"  "+names[i],new Vector3(.38f,.20f-i*.11f,-.002f),theme,.34f);
                }
                Label(map.transform,"N",new Vector3(-.01f,.32f,-.002f),theme,.25f);
                Label(map.transform,$"{span/1000:0} km",new Vector3(-.29f,-.32f,-.002f),theme,.25f);
                Mesh mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/LocalMap.asset");if(mesh==null){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,Root+"/LocalMap.asset");}else mesh.Clear();
                mesh.SetVertices(verts);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();mesh.RecalculateNormals();EditorUtility.SetDirty(mesh);
                var filter=map.AddComponent<MeshFilter>();filter.sharedMesh=mesh;var renderer=map.AddComponent<MeshRenderer>();renderer.sharedMaterial=mat;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                var marker=new GameObject("AircraftPosition");marker.transform.SetParent(map.transform,false);
                Mesh arrow=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/AircraftArrow.asset");if(arrow==null){arrow=new Mesh();AssetDatabase.CreateAsset(arrow,Root+"/AircraftArrow.asset");}else arrow.Clear();
                arrow.vertices=new[]{new Vector3(-.008f,-.010f,0),new Vector3(0,.020f,0),new Vector3(.008f,-.010f,0)};arrow.triangles=new[]{0,1,2};arrow.RecalculateBounds();EditorUtility.SetDirty(arrow);
                marker.AddComponent<MeshFilter>().sharedMesh=arrow;var arrowRenderer=marker.AddComponent<MeshRenderer>();arrowRenderer.sharedMaterial=mat;arrowRenderer.shadowCastingMode=ShadowCastingMode.Off;

                experience.Configure(modes,voice,map,marker.transform,center,span,"0.1 · polish 2026-09-27");nav.ConfigureExperience(experience);
                var touch=world.GetComponentInChildren<CockpitTouchInteractionV2Controller>(true);
                var yoke=world.GetComponentInChildren<OvrConstrainedYokeGrabTransformer>(true);
                var throttle=touch.AuthoredBindings.First(b=>b.target==CockpitTouchInteractionV2Target.Throttle);
                nav.ConfigureSetup(touch.LeftRayOrigin,touch.RightRayOrigin,yoke.StableReferenceFrame,yoke.RollCenter,throttle.targetCollider.transform);
                var radio=modes.GetComponent<ProductTrafficRadio>();if(radio==null)radio=modes.gameObject.AddComponent<ProductTrafficRadio>();
                var radioTransform=modes.transform.Find("LocalTrafficRadio");
                if(radioTransform==null){var go=new GameObject("LocalTrafficRadio");go.transform.SetParent(modes.transform,false);radioTransform=go.transform;}
                var audio=radioTransform.GetComponent<AudioSource>();if(audio==null)audio=radioTransform.gameObject.AddComponent<AudioSource>();
                world.GetComponentInChildren<QuestFlightLab.Audio.ProductionC172AudioController>(true).ConfigureTrafficRadio(audio);
                audio.playOnAwake=false;audio.spatialBlend=0;audio.volume=.48f;audio.priority=160;audio.outputAudioMixerGroup=voice.outputAudioMixerGroup;
                var oldCaption=world.GetComponentInChildren<AircraftReferenceFrameRig>(true).PilotSeatAnchor.Find("TrafficRadioCaption");if(oldCaption!=null)UnityEngine.Object.DestroyImmediate(oldCaption.gameObject);
                var captionRoot=new GameObject("TrafficRadioCaption");captionRoot.transform.SetParent(modes.transform,false);
                var seat=world.GetComponentInChildren<AircraftReferenceFrameRig>(true).PilotSeatAnchor;
                captionRoot.transform.SetParent(seat,false);captionRoot.transform.localPosition=new Vector3(.10f,.10f,.44f);captionRoot.transform.localScale=Vector3.one*.46f;
                var caption=Label(captionRoot.transform,"",new Vector3(-.27f,.045f,-.004f),theme,.20f);caption.rectTransform.sizeDelta=new Vector2(.54f,.20f);caption.textWrappingMode=TextWrappingModes.Normal;
                var backing=GameObject.CreatePrimitive(PrimitiveType.Quad);backing.name="RadioCaptionSurface";UnityEngine.Object.DestroyImmediate(backing.GetComponent<Collider>());backing.transform.SetParent(captionRoot.transform,false);backing.transform.localPosition=new Vector3(0,-.035f,.003f);backing.transform.localScale=new Vector3(.60f,.19f,1);
                var backRenderer=backing.GetComponent<Renderer>();backRenderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Production/UI/Interface/ModalSurface.mat");backRenderer.shadowCastingMode=ShadowCastingMode.Off;
                radio.ConfigureCaption(caption);captionRoot.SetActive(false);
                var calls=new List<ProductTrafficRadio.Call>();
                string[] ids={"C172_0","SuperCub_1","C172_2"},keys={"cessna0","cub1","cessna2"},phaseKeys={"hold","takeoff","approach","clear"};
                var phases=new[]{AmbientFlightPhase.HoldShort,AmbientFlightPhase.Takeoff,AmbientFlightPhase.Approach,AmbientFlightPhase.TaxiIn};
                for(int a=0;a<3;a++)foreach(int r in new[]{8,26})for(int p=0;p<4;p++)
                {
                    string path=$"Assets/Production/Training/Voice/radio_{keys[a]}_{phaseKeys[p]}_{r}.wav";
                    var importer=AssetImporter.GetAtPath(path) as AudioImporter;
                    if(importer!=null){importer.forceToMono=true;var settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.CompressedInMemory;settings.compressionFormat=AudioCompressionFormat.Vorbis;settings.quality=.7f;importer.defaultSampleSettings=settings;importer.SaveAndReimport();}
                    var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path);if(clip==null)throw new InvalidOperationException("Missing radio clip "+path);
                    string identity=a==0?"Cessna two one alpha":a==1?"Cub six charlie":"Cessna three two bravo";
                    string phrase=p==0?"holding short of":p==1?"departing":p==2?"on final for":"clear of";
                    calls.Add(new ProductTrafficRadio.Call{aircraftId=ids[a],phase=phases[p],runway=r,clip=clip,text=$"Boulder traffic, {identity}, {phrase} runway {r:00}, Boulder."});
                }
                radio.Configure(world.GetComponentInChildren<AmbientTrafficDirector>(true),modes,world.GetComponentInChildren<ProductionCockpitSystemsController>(true),audio,voice,calls.ToArray());
                map.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(world,World);
            }
            finally {PrefabUtility.UnloadPrefabContents(world);}
            // Existing opaque source materials and textures: no new shader variants, maps or draw calls.
            MaterialTune("Cockpit_Steering_MAT_-1494450411544164266",.64f,0,.40f);
            MaterialTune("Cockpit_Paddle_MAT_-8680491767069482703",.78f,.18f,1);
            MaterialTune("Cockpit_Interior_Body_MAT_3992800033531730471",.90f,0,1);
            File.WriteAllText(Root+"/SOURCE.md","# Release polish assets\nLocal map mesh, 256px shaded terrain overview and labels: original generated geometry/data visualization from existing authored terrain bounds and demo landmark coordinates; derived from the existing terrain asset; no external imagery. Original project-authored geometry and pixels derive from the already recorded public-domain USGS 3DEP terrain; terrain source provenance remains in the existing scenery source records. Map is schematic, not an aeronautical chart. Map materials use built-in unlit color and texture shaders.\nCockpit refinement: existing source textures and GUIDs retained. Yoke roughness .64, normal .40; pedals roughness .78, metallic .18; interior baked occlusion .88. No new texture samples or rendering passes. Original aircraft source/provenance remains in the existing cockpit source records.\n");
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();
        }
        private static TextMeshPro Label(Transform parent,string text,Vector3 pos,ProductUiTheme theme,float size)
        {
            var go=new GameObject(text);go.transform.SetParent(parent,false);go.transform.localPosition=pos;
            var label=go.AddComponent<TextMeshPro>();label.font=theme.regular;label.fontSharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Production/UI/Interface/InterRegularOverlay.mat");label.text=text;label.fontSize=size;label.color=theme.text;label.rectTransform.pivot=new Vector2(0,1);label.rectTransform.sizeDelta=new Vector2(.55f,.12f);label.textWrappingMode=TextWrappingModes.NoWrap;return label;
        }
        private static void MaterialTune(string name,float rough,float metal,float normal)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Production/Materials/Cockpit/"+name+".mat");
            if(material==null)throw new InvalidOperationException("Missing cockpit material "+name);
            material.SetFloat("roughnessFactor",rough);material.SetFloat("metallicFactor",metal);material.SetFloat("normalTexture_scale",normal);
            if(name.Contains("Interior"))material.SetFloat("occlusionTexture_strength",.88f);EditorUtility.SetDirty(material);
        }
        private static void AuthorTerrainOverview(Transform parent,ProductionTerrainSurfaceAsset terrain)
        {
            const int size=256;var heights=new float[size*size];var colors=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                Vector2 p=terrain.macroMinimumEnuMeters+Vector2.Scale(terrain.macroSizeMeters,new Vector2(x/(float)(size-1),y/(float)(size-1)));
                heights[y*size+x]=terrain.TrySample(p.x,p.y,out var sample)?sample.heightMeters:0;
            }
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float h=heights[y*size+x],dx=heights[y*size+Mathf.Min(size-1,x+1)]-heights[y*size+Mathf.Max(0,x-1)],dy=heights[Mathf.Min(size-1,y+1)*size+x]-heights[Mathf.Max(0,y-1)*size+x];
                float shade=Mathf.Clamp(1+(dx-dy)*.008f,.50f,1.35f);
                Color color=Color.Lerp(new Color(.09f,.15f,.17f),new Color(.30f,.33f,.27f),Mathf.InverseLerp(0,1800,h))*shade;
                if(Mathf.Repeat(h,150)<7)color*=.83f;color.a=1;colors[y*size+x]=color;
            }
            var texture=new Texture2D(size,size,TextureFormat.RGB24,false);texture.SetPixels(colors);texture.Apply();
            string path=Root+"/BoulderTerrainOverview.png";File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=256;importer.SaveAndReimport();
            var material=AssetDatabase.LoadAssetAtPath<Material>(Root+"/TerrainOverview.mat");if(material==null){material=new Material(Shader.Find("Unlit/Texture"));AssetDatabase.CreateAsset(material,Root+"/TerrainOverview.mat");}
            material.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);material.renderQueue=3509;EditorUtility.SetDirty(material);
            var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.name="BakedTerrainOverview";UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());quad.transform.SetParent(parent,false);quad.transform.localPosition=new Vector3(0,0,.001f);quad.transform.localScale=new Vector3(.58f,.58f,1);
            var renderer=quad.GetComponent<Renderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
    }
}
