using System;
using System.IO;
using System.Linq;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using QuestFlightLab.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore.LowLevel;
using Object=UnityEngine.Object;

namespace QuestFlight.Editor
{
    // Presentation-only migration. Does not rebuild lessons, voices, flight trim or scenery.
    public static class ProductInterfacePolishAuthoring
    {
        const string Root="Assets/Production/UI/Interface";
        const string World="Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
        static ProductUiTheme theme;
        static Material surfaceMaterial;
        static Material modalSurface,modalAction,modalRegular,modalSemibold;
        static Mesh quad;
        static bool captureAfterAuthor;
        public static void RunAndCaptureBatch() { captureAfterAuthor=true;RunBatch(); }
        public static void RunBatch()
        {
            try
            {
                if(Shader.Find("TextMeshPro/Mobile/Distance Field")==null)
                {
                    string package=Directory.GetFiles("Library/PackageCache","TMP Essential Resources.unitypackage",SearchOption.AllDirectories).Single();
                    AssetDatabase.ImportPackage(package,false);
                    AssetDatabase.Refresh();
                }
                if(Resources.Load<TMP_Settings>("TMP Settings")==null)
                {
                    var settings=ScriptableObject.CreateInstance<TMP_Settings>();
                    AssetDatabase.CreateAsset(settings,"Assets/Resources/TMP Settings.asset");
                    AssetDatabase.SaveAssets();
                }
                theme=AssetDatabase.LoadAssetAtPath<ProductUiTheme>(Root+"/InterfaceTheme.asset");
                if(theme==null){theme=ScriptableObject.CreateInstance<ProductUiTheme>();AssetDatabase.CreateAsset(theme,Root+"/InterfaceTheme.asset");}
                theme.regular=Font("Regular"); theme.semibold=Font("Semibold"); EditorUtility.SetDirty(theme);
                var tmpSettings=new SerializedObject(TMP_Settings.instance);
                tmpSettings.FindProperty("m_defaultFontAsset").objectReferenceValue=theme.regular;
                tmpSettings.FindProperty("assetVersion").stringValue="2";
                tmpSettings.FindProperty("m_GetFontFeaturesAtRuntime").boolValue=false;
                tmpSettings.ApplyModifiedPropertiesWithoutUndo();
                surfaceMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/InterfaceSurface.mat");
                if(surfaceMaterial==null){surfaceMaterial=new Material(Shader.Find("QuestFlight/UI/InterfaceSurface"));AssetDatabase.CreateAsset(surfaceMaterial,Root+"/InterfaceSurface.mat");}
                surfaceMaterial.SetColor("_Color",theme.panel);EditorUtility.SetDirty(surfaceMaterial);
                modalSurface=ModalMaterial("ModalSurface",surfaceMaterial,false,3500);
                modalAction=ModalMaterial("ModalAction",surfaceMaterial,false,3501);
                modalRegular=ModalMaterial("InterRegularOverlay",theme.regular.material,true,3510);
                modalSemibold=ModalMaterial("InterSemiboldOverlay",theme.semibold.material,true,3510);
                quad=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/InterfaceQuad.asset");
                if(quad==null)
                {
                    quad=new Mesh{name="InterfaceQuad"};
                    quad.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};
                    quad.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};quad.triangles=new[]{0,2,1,0,3,2};quad.RecalculateBounds();
                    AssetDatabase.CreateAsset(quad,Root+"/InterfaceQuad.asset");
                }
                var world=PrefabUtility.LoadPrefabContents(World);
                try
                {
                    var view=world.GetComponentInChildren<ProductFlightModeMenuView>(true);
                    Startup(view);
                    Navigation(world);
                    Instruction(world);
                    References(world);
                    // New UI renderers participate in the same startup suppression as the cockpit.
                    var rig=world.GetComponentInChildren<AircraftReferenceFrameRig>(true);
                    var serialized=new SerializedObject(view);
                    var existing=serialized.FindProperty("aircraftPresentationRenderers");
                    var renderers=Enumerable.Range(0,existing.arraySize).Select(i=>existing.GetArrayElementAtIndex(i).objectReferenceValue as Renderer)
                        .Concat(rig.GetComponentsInChildren<ProductUiLabel>(true).Where(l=>!l.transform.IsChildOf(view.transform)).Select(l=>l.Display.GetComponent<Renderer>()))
                        .Concat(rig.GetComponentsInChildren<ProductUiSurface>(true).Where(s=>!s.transform.IsChildOf(view.transform)).Select(s=>s.GetComponent<Renderer>()))
                        .Concat(rig.GetComponentsInChildren<ProductUiInstruction>(true).SelectMany(i=>i.GetComponentsInChildren<Renderer>(true)))
                        .Where(r=>r!=null).Distinct().ToArray();
                    existing.arraySize=renderers.Length;for(int i=0;i<renderers.Length;i++)existing.GetArrayElementAtIndex(i).objectReferenceValue=renderers[i];
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(world,World);
                    Debug.Log("[InterfacePolish] Authored shared typography, startup, navigation, instruction and cockpit labels.");
                }
                finally { PrefabUtility.UnloadPrefabContents(world); }
                AssetDatabase.SaveAssets();
                if(captureAfterAuthor)QuestFlightLab.Editor.ProductionVisualQaBatchRunner.RunInterfaceVisualQa();
                EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }

        static TMP_FontAsset Font(string weight)
        {
            string path=Root+"/Inter-"+weight+"-SDF.asset";
            var asset=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path); if(asset!=null)return asset;
            asset=TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>(Root+"/Inter-"+weight+".ttf"),72,8,GlyphRenderMode.SDFAA,1024,1024,AtlasPopulationMode.Dynamic,false);
            asset.name="Inter "+weight+" SDF";
            string glyphs=new string(Enumerable.Range(32,95).Select(n=>(char)n).ToArray())+"°·—–’‘“”…←→↑↓✓";
            if(!asset.TryAddCharacters(glyphs,out string missing,true))throw new InvalidOperationException("Missing Inter glyphs: "+missing);
            asset.atlasPopulationMode=AtlasPopulationMode.Static;
            asset.material.shader=Shader.Find("TextMeshPro/Mobile/Distance Field");
            asset.material.SetFloat("_CullMode",0);
            asset.material.SetFloat("_ZTestMode",(float)CompareFunction.LessEqual);
            asset.material.renderQueue=3020;
            AssetDatabase.CreateAsset(asset,path);
            foreach(var texture in asset.atlasTextures){texture.name=asset.name+" Atlas";texture.filterMode=FilterMode.Bilinear;AssetDatabase.AddObjectToAsset(texture,asset);}
            AssetDatabase.AddObjectToAsset(asset.material,asset);
            EditorUtility.SetDirty(asset);return asset;
        }
        static Transform Child(Transform root,string name)
        {var t=root.Find(name);if(t!=null)return t;var go=new GameObject(name);go.transform.SetParent(root,false);return go.transform;}
        static Material ModalMaterial(string name,Material source,bool text,int queue)
        {
            string path=Root+"/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){material=new Material(source);AssetDatabase.CreateAsset(material,path);}
            material.CopyPropertiesFromMaterial(source);
            if(text)material.shader=Shader.Find("TextMeshPro/Mobile/Distance Field Overlay");
            else{material.SetFloat("_ZTest",(float)CompareFunction.Always);material.SetFloat("_ZWrite",0);}
            material.renderQueue=queue;EditorUtility.SetDirty(material);return material;
        }
        static void ModalText(TextMeshPro text,bool bold=false) => text.fontSharedMaterial=bold?modalSemibold:modalRegular;
        static void ModalSurface(ProductUiSurface surface,bool action=false)=>surface.GetComponent<Renderer>().sharedMaterial=action?modalAction:modalSurface;
        static TextMeshPro Text(Transform root,string name,string value,Vector3 position,float size,float width,bool bold=false,Color? color=null)
        {
            var t=Child(root,name);t.localPosition=position;t.localRotation=Quaternion.identity;t.localScale=Vector3.one;
            var text=t.GetComponent<TextMeshPro>();if(text==null)text=t.gameObject.AddComponent<TextMeshPro>();
            text.font=bold?theme.semibold:theme.regular;text.fontSharedMaterial=text.font.material;
            text.fontSize=size*.1f;text.enableAutoSizing=false;text.richText=true;
            text.alignment=TextAlignmentOptions.TopLeft;text.textWrappingMode=TextWrappingModes.Normal;text.overflowMode=TextOverflowModes.Overflow;
            text.rectTransform.pivot=new Vector2(0,1);text.rectTransform.sizeDelta=new Vector2(width,1);
            text.transform.localPosition=position;
            text.color=color??theme.text;text.text=value;
            text.renderer.shadowCastingMode=ShadowCastingMode.Off;text.renderer.receiveShadows=false;
            text.renderer.lightProbeUsage=LightProbeUsage.Off;text.renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            text.ForceMeshUpdate(true,true);return text;
        }
        static ProductUiLabel Label(TextMesh source,float size,float width,bool bold=false,Color? color=null)
        {
            source.color=color??theme.text;source.GetComponent<Renderer>().enabled=false;
            var text=Text(source.transform,"InterfaceText",source.text,Vector3.zero,size,width,bold,source.color);
            var label=source.GetComponent<ProductUiLabel>();if(label==null)label=source.gameObject.AddComponent<ProductUiLabel>();
            label.Configure(source,text);return label;
        }
        static ProductUiSurface Surface(Transform t,Vector3 pos,Vector2 size,bool action=false)
        {
            t.localPosition=pos;t.localScale=new Vector3(size.x,size.y,1);t.localRotation=Quaternion.identity;
            var filter=t.GetComponent<MeshFilter>();if(filter==null)filter=t.gameObject.AddComponent<MeshFilter>();filter.sharedMesh=quad;
            var renderer=t.GetComponent<MeshRenderer>();if(renderer==null)renderer=t.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=surfaceMaterial;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
            var visual=t.GetComponent<ProductUiSurface>();if(visual==null)visual=t.gameObject.AddComponent<ProductUiSurface>();visual.Configure(renderer,theme,action);return visual;
        }
        static TextMesh FindText(Transform root,string name)=>root.GetComponentsInChildren<TextMesh>(true).Single(t=>t.name==name);

        static void Startup(ProductFlightModeMenuView view)
        {
            var selection=view.SelectionContentRoot.transform;
            Surface(selection.Find("MenuBackplate"),new Vector3(-.45f,0,1.47f),new Vector2(1.40f,1.20f));
            selection.Find("MenuAccentRule").gameObject.SetActive(false);
            void L(string name,string value,Vector3 p,float size,float width,bool bold=false,Color? color=null)
            {var old=FindText(selection,name);old.text=value;old.transform.localPosition=p;Label(old,size,width,bold,color);}
            L("QuestFlightTitle","QuestFlight",new Vector3(-1.08f,.52f,1.44f),6.1f,1.2f,true);
            L("AircraftSubtitle","Boulder Municipal  ·  C172",new Vector3(-1.075f,.422f,1.44f),2.45f,1.15f,false,theme.secondary);
            string[] names={"DemoFlightCard","FreeFlightCard","LessonsCard","SeatCalibrationCard"};
            string[] titles={"Demo flight","Free flight","Flight lessons","Seat position"};
            string[] descriptions={"A guided departure from Boulder","Explore Colorado at your own pace","Six exercises, one step at a time",""};
            for(int i=0;i<4;i++)
            {
                float y=.24f-i*.205f;float h=i==3?.085f:.175f;
                var card=selection.Find(names[i]);
                var visual=Surface(card,new Vector3(-.45f,y,1.43f),new Vector2(1.25f,h),true);
                // Scale changed from a cube to a planar mesh. Keep ray/poke hit volumes physical.
                card.GetComponent<BoxCollider>().size=new Vector3(1,1,.035f);
                card.GetComponent<ProductFlightModeMenuButton>().ConfigurePolishedSurface(visual);
                L(names[i]+"Title",titles[i],new Vector3(-1.005f,y+(i==3?.022f:.048f),1.404f),i==3?2.55f:3.35f,1.08f,true);
                if(i<3)L(names[i]+"Subtitle",descriptions[i],new Vector3(-1.005f,y-.017f,1.404f),2.15f,1.08f,false,theme.secondary);
            }
            L("StartupStatus","Ready to fly",new Vector3(-1.065f,-.48f,1.44f),2.15f,1.19f,false,theme.success);
            L("StartupNavigationHint","[A] Select   [B / Y] Back   [Menu] Help\nThumbstick up / down moves selection",new Vector3(-1.065f,-.525f,1.44f),2.15f,1.24f,false,theme.secondary);
            var transition=view.TransitionContentRoot.transform;
            Surface(transition.Find("TransitionBackplate"),new Vector3(0,0,1.38f),new Vector2(1.25f,.28f));
            var title=FindText(transition,"TransitionTitle");title.text="Entering the cockpit";title.transform.localPosition=new Vector3(-.55f,.075f,1.35f);Label(title,4.1f,1.1f,true);
            var status=FindText(transition,"TransitionStatus");status.text="Preparing your flight";status.transform.localPosition=new Vector3(-.55f,-.025f,1.35f);Label(status,2.35f,1.1f,false,theme.secondary);
        }
        static void Navigation(GameObject world)
        {
            var nav=world.GetComponentInChildren<ProductAppNavigation>(true);
            var so=new SerializedObject(nav);
            var panel=(so.FindProperty("panel").objectReferenceValue as GameObject).transform;
            panel.localScale=Vector3.one;
            var title=Label(so.FindProperty("heading").objectReferenceValue as TextMesh,3.7f,.73f,true);
            var body=Label(so.FindProperty("detail").objectReferenceValue as TextMesh,2.05f,.73f);
            var hint=Label(so.FindProperty("footer").objectReferenceValue as TextMesh,1.65f,.73f,false,theme.secondary);
            ModalText(title.Display,true);ModalText(body.Display);ModalText(hint.Display);
            var backing=so.FindProperty("backplate").objectReferenceValue as Transform;
            ModalSurface(Surface(backing,new Vector3(0,0,1.045f),new Vector2(.81f,.6f)));
            var buttons=panel.GetComponentsInChildren<ProductActionButton>(true);
            foreach(var button in buttons)
            {
                button.transform.localScale=Vector3.one;
                button.GetComponent<BoxCollider>().size=new Vector3(.73f,.047f,.012f);
                var text=button.GetComponentInChildren<TextMesh>(true);text.transform.localPosition=new Vector3(-.345f,.0165f,-.008f);
                ModalText(Label(text,2.1f,.69f,true).Display,true);
                var visual=Surface(Child(button.transform,"ActionSurface"),new Vector3(0,0,.004f),new Vector2(.75f,.046f),true);
                ModalSurface(visual,true);
                button.ConfigureVisual(visual);
            }
            var layout=panel.GetComponent<ProductUiPanelLayout>();if(layout==null)layout=panel.gameObject.AddComponent<ProductUiPanelLayout>();
            layout.Configure(title,body,hint,backing,buttons);nav.ConfigurePolishedLayout(layout);
            EditorUtility.SetDirty(nav);
        }
        static void Instruction(GameObject world)
        {
            var controller=world.GetComponentInChildren<ProductTrainingController>(true);
            var so=new SerializedObject(controller);
            var old=so.FindProperty("cueText").objectReferenceValue as TextMesh;
            old.GetComponent<Renderer>().enabled=false;
            var root=old.transform.parent;root.localPosition=new Vector3(.29f,.23f,.91f);
            var surface=Surface(root.Find("Backplate"),Vector3.zero,new Vector2(.54f,.2f));
            var status=Text(root,"ControlStatus","Your controls   /   01 / 07",new Vector3(-.24f,.017f,-.003f),1.55f,.48f,false,theme.accent);
            var title=Text(root,"StepTitle","Check the yoke",new Vector3(-.24f,-.022f,-.003f),2.65f,.48f,true);
            var instruction=Text(root,"CurrentInstruction","Grasp the yoke. Move it left and right, then center it.",new Vector3(-.24f,-.077f,-.003f),2.05f,.48f);
            var hint=Text(root,"InstructionHelp","[X] Repeat / show    [Menu] Pause / help",new Vector3(-.24f,-.155f,-.003f),1.45f,.48f,false,theme.secondary);
            var progress=Surface(Child(root,"StepProgress"),new Vector3(0,-.18f,-.004f),new Vector2(.48f,.002f));
            ModalSurface(surface);ModalSurface(progress,true);progress.SetAccentOnly();
            ModalText(status);ModalText(title,true);ModalText(instruction);ModalText(hint);
            var component=root.GetComponent<ProductUiInstruction>();if(component==null)component=root.gameObject.AddComponent<ProductUiInstruction>();
            component.Configure(status,title,instruction,hint,surface.transform,progress.transform,theme);
            component.Present("Your controls","Check the yoke",instruction.text,0,7,true,false);
            controller.ConfigurePolishedInstruction(component);EditorUtility.SetDirty(controller);
        }
        static void References(GameObject world)
        {
            var rig=world.GetComponentInChildren<AircraftReferenceFrameRig>(true);
            var group=rig.AircraftVisualRoot.Find("TrainingCockpitReferences");
            if(group==null)throw new InvalidOperationException("Accepted cockpit label anchor is missing.");
            foreach(var old in group.GetComponentsInChildren<TextMesh>(true))
            {
                var label=Label(old,1.30f,.21f,true);
                float width=Mathf.Min(.225f,label.Display.GetPreferredValues().x+.014f);
                float height=label.Display.GetPreferredValues().y+.011f;
                Surface(Child(old.transform,"LabelBacking"),new Vector3(width*.5f-.006f,-height*.5f+.004f,.003f),new Vector2(width,height));
            }
        }
    }
}
