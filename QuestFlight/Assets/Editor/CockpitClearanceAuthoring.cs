using System;
using QuestFlight.Product;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestFlight.Editor
{
    public static class CockpitClearanceAuthoring
    {
        public static void RunBatch()
        {
            try
            {
                CockpitPedalCompletionAuthoring.Author();
                const string path="Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
                var world=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var anchor=world.GetComponentInChildren<AircraftReferenceFrameRig>(true).PilotSeatAnchor;
                    foreach(var panel in world.GetComponentsInChildren<ProductUiPanelLayout>(true))
                    { panel.ConfigureCockpitClearance(anchor);panel.Apply(false);EditorUtility.SetDirty(panel); }
                    foreach(var instruction in world.GetComponentsInChildren<ProductUiInstruction>(true))
                    { instruction.ConfigureCockpitClearance(anchor);EditorUtility.SetDirty(instruction); }
                    PrefabUtility.SaveAsPrefabAsset(world,path);
                }
                finally { PrefabUtility.UnloadPrefabContents(world); }
                foreach(string name in new[]{"ModalSurface","ModalAction","InterRegularOverlay","InterSemiboldOverlay"})
                {
                    var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Production/UI/Interface/"+name+".mat");
                    if(name.StartsWith("Inter"))
                    { material.shader=Shader.Find("TextMeshPro/Mobile/Distance Field");material.SetFloat("_ZTestMode",(float)CompareFunction.LessEqual);material.renderQueue=3510; }
                    else { material.SetFloat("_ZTest",(float)CompareFunction.LessEqual);material.SetFloat("_ZWrite",0); }
                    EditorUtility.SetDirty(material);
                }
                AssetDatabase.SaveAssets();
                Debug.Log("[CockpitClearance] Authored cabin-space menus, instructions and complete articulated pedals.");
                EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
