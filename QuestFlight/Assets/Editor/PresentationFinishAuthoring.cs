using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using QuestFlight.Product;
using QuestFlightLab.Aircraft.CockpitDynamics;

namespace QuestFlight.Editor
{
    public static class PresentationFinishAuthoring
    {
        const string Root="Assets/Production/UI/ReleasePolish";
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void RunVisualQaBatch()
        {
            try {Run();QuestFlightLab.Editor.ProductionVisualQaBatchRunner.RunReleasePolishQa();QuestFlightLab.Editor.ProductionVisualQaBatchRunner.RunProductionVisualQa();EditorApplication.Exit(0);}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void RunInterfaceQaBatch()
        {
            try {Run();QuestFlightLab.Editor.ProductionVisualQaBatchRunner.RunReleasePolishQa();QuestFlightLab.Editor.ProductionVisualQaBatchRunner.RunPresentationMaterialReview();EditorApplication.Exit(0);}
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            AssetDatabase.Refresh();
            var world=PrefabUtility.LoadPrefabContents("Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");
            try
            {
                var nav=world.GetComponentInChildren<ProductAppNavigation>(true);
                var panel=(GameObject)new SerializedObject(nav).FindProperty("panel").objectReferenceValue;
                var theme=AssetDatabase.LoadAssetAtPath<ProductUiTheme>("Assets/Production/UI/Interface/QuestFlightTheme.asset");
                if(theme==null){var ids=AssetDatabase.FindAssets("t:ProductUiTheme");theme=AssetDatabase.LoadAssetAtPath<ProductUiTheme>(AssetDatabase.GUIDToAssetPath(ids[0]));}
                var visuals=Child(panel.transform,"ControlVisuals");
                var diagram=Child(visuals.transform,"Diagram");var graph=Child(visuals.transform,"Graph");
                graph.transform.localPosition=new Vector3(0,.08f,0);
                Color cyan=theme.accent,gold=new Color(.96f,.72f,.32f);
                for(int n=0;n<2;n++)
                {
                    float x=n==0?-.17f:.17f;
                    Line(diagram.transform,n==0?"LeftController":"RightController",cyan,new[]{new Vector3(x-.04f,.08f),new Vector3(x-.05f,.14f),new Vector3(x-.025f,.17f),new Vector3(x+.025f,.17f),new Vector3(x+.05f,.14f),new Vector3(x+.035f,.03f),new Vector3(x-.015f,.025f),new Vector3(x-.04f,.08f)});
                    Label(diagram.transform,n==0?"LeftButtons":"RightButtons",n==0?"Y    X":"B    A",new Vector3(x-.033f,.11f,-.002f),theme,.017f,.12f);
                    Line(diagram.transform,"Stick"+n,gold,new[]{new Vector3(x-.012f,.145f,-.002f),new Vector3(x+.012f,.145f,-.002f)});
                }
                var assignments=Label(diagram.transform,"Assignments","",new Vector3(-.34f,0,-.002f),theme,.020f,.70f);
                Line(graph.transform,"Axes",new Color(.4f,.48f,.52f),new[]{new Vector3(-.28f,0),new Vector3(.28f,0)});
                Line(graph.transform,"Vertical",new Color(.4f,.48f,.52f),new[]{new Vector3(0,-.085f),new Vector3(0,.085f)});
                var curve=Line(graph.transform,"Response",cyan,new Vector3[33]);
                var marker=Line(graph.transform,"InputMarker",gold,new[]{new Vector3(-.005f,0),new Vector3(0,.005f),new Vector3(.005f,0),new Vector3(0,-.005f),new Vector3(-.005f,0)});
                Label(graph.transform,"GraphLegend","LEFT RUDDER          stick input          RIGHT RUDDER\nCurve: rudder output · gold marker: live input",new Vector3(-.34f,-.095f,0),theme,.018f,.70f);
                var component=visuals.GetComponent<ProductControlVisuals>();if(component==null)component=visuals.AddComponent<ProductControlVisuals>();component.Configure(diagram,graph,curve,marker.transform,assignments);
                nav.ConfigureControlVisuals(component);((ProductUiPanelLayout)new SerializedObject(nav).FindProperty("polishedLayout").objectReferenceValue).ConfigureSupplement(visuals.transform);visuals.SetActive(false);
                var timing=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Production/Training/CaptionTimings.json");if(timing==null)throw new Exception("Missing authenticated caption timings");
                var captionMaterial=Material("CaptionBackground",Shader.Find("QuestFlight/UI/CaptionSurface"),theme.panel);captionMaterial.SetFloat("_ZTest",8);captionMaterial.renderQueue=3500;
                foreach(var instruction in world.GetComponentsInChildren<ProductUiInstruction>(true))
                {
                    instruction.ConfigureCaptionTimings(timing);
                    var surface=(Transform)new SerializedObject(instruction).FindProperty("surface").objectReferenceValue;
                    var renderer=surface.GetComponent<Renderer>();renderer.sharedMaterial=captionMaterial;
                    var style=surface.GetComponent<ProductUiSurface>();if(style==null)style=surface.gameObject.AddComponent<ProductUiSurface>();style.Configure(renderer,theme,false);style.ConfigureCaptionBackground();
                }
                var experience=world.GetComponentInChildren<ProductFlightExperience>(true);experience.ConfigureReadiness(world.GetComponentInChildren<ProductionCockpitSystemsController>(true));
                var narration=world.GetComponentInChildren<ProductDemoNarration>(true);var serialized=new SerializedObject(narration);var cues=serialized.FindProperty("cues");int index=-1;
                for(int n=0;n<cues.arraySize;n++)if(cues.GetArrayElementAtIndex(n).FindPropertyRelative("id").stringValue=="showcase_intro")index=n;
                if(index<0){index=cues.arraySize;cues.InsertArrayElementAtIndex(index);}
                var cue=cues.GetArrayElementAtIndex(index);cue.FindPropertyRelative("id").stringValue="showcase_intro";
                cue.FindPropertyRelative("text").stringValue="Welcome aboard. We're already flying toward Boulder Municipal Airport. I'll fly the arrival, land, and stop clear of the runway. Enjoy the view. Menu pauses the flight and lets you take control.";
                var clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Production/Training/Voice/showcase_intro.wav");if(clip==null)throw new Exception("Showcase intro missing");
                cue.FindPropertyRelative("clip").objectReferenceValue=clip;cue.FindPropertyRelative("distance").floatValue=0;serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(world,"Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");
            }
            finally {PrefabUtility.UnloadPrefabContents(world);}
            File.WriteAllText(Root+"/PresentationFinish_SOURCE.md","# Presentation finish assets\nController outlines, graph lines, labels and caption material/shader are original project-authored geometry/code; same terms as project source. No external art. Caption timings derive from the existing pinned Kokoro Michael model and are tied to the shipped PCM recordings; audio sources and hashes are recorded in Training/Voice/KOKORO_SOURCE_MANIFEST.json. No runtime models or external service. All objects are explicitly authored in the product prefab.\n");
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();Debug.Log("[PresentationFinish] Authored all presentation references.");
        }
        static GameObject Child(Transform parent,string name){var t=parent.Find(name);if(t!=null)return t.gameObject;var go=new GameObject(name);go.transform.SetParent(parent,false);return go;}
        static Material Material(string name,Shader shader,Color color){string path=Root+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m==null){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}m.shader=shader;m.color=color;EditorUtility.SetDirty(m);return m;}
        static LineRenderer Line(Transform parent,string name,Color color,Vector3[] points)
        {var go=Child(parent,name);var l=go.GetComponent<LineRenderer>();if(l==null)l=go.AddComponent<LineRenderer>();l.sharedMaterial=Material(color.r>.9f?"ControlGold":color.g>.6f?"ControlCyan":"ControlMuted",Shader.Find("Unlit/Color"),color);l.sharedMaterial.renderQueue=3511;l.useWorldSpace=false;l.alignment=LineAlignment.TransformZ;l.widthMultiplier=.0018f;l.numCapVertices=0;l.numCornerVertices=0;l.positionCount=points.Length;l.SetPositions(points);l.shadowCastingMode=ShadowCastingMode.Off;l.receiveShadows=false;return l;}
        static TextMeshPro Label(Transform parent,string name,string text,Vector3 pos,ProductUiTheme theme,float size,float width)
        {var go=Child(parent,name);go.transform.localPosition=pos;var t=go.GetComponent<TextMeshPro>();if(t==null)t=go.AddComponent<TextMeshPro>();t.font=theme.regular;t.fontSharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Production/UI/Interface/InterRegularOverlay.mat");t.fontSize=size*10;t.text=text;t.color=theme.text;t.rectTransform.pivot=new Vector2(0,1);t.rectTransform.sizeDelta=new Vector2(width,.12f);t.textWrappingMode=TextWrappingModes.Normal;return t;}
    }
}
