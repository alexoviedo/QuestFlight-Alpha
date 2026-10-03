using System;
using System.Linq;
using QuestFlight.Product;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    /// <summary>Bind existing authored UI only; no runtime panel or rig construction.</summary>
    public static class ProductGuestFinishAuthoring
    {
        public static void Apply(GameObject world)
        {
            var navigation=world.GetComponentInChildren<ProductAppNavigation>(true);
            var view=world.GetComponentInChildren<ProductFlightModeMenuView>(true);
            if(navigation==null || view==null)throw new InvalidOperationException("Guest presentation requires the authored navigation and startup view.");
            var instructions=world.GetComponentsInChildren<ProductUiInstruction>(true);
            if(instructions.Length!=1)throw new InvalidOperationException("Expected the existing shared lesson and demo instruction panel.");
            navigation.ConfigureGuestPresentation(instructions);
            var navData=new SerializedObject(navigation);
            var panel=navData.FindProperty("panel").objectReferenceValue as GameObject;
            if(panel==null)throw new InvalidOperationException("Authored navigation panel missing.");
            var panelRenderers=panel.GetComponentsInChildren<Renderer>(true);
            var viewData=new SerializedObject(view);
            var hidden=viewData.FindProperty("aircraftPresentationRenderers");
            var retained=Enumerable.Range(0,hidden.arraySize)
                .Select(i=>hidden.GetArrayElementAtIndex(i).objectReferenceValue as Renderer)
                .Where(r=>r!=null && !panelRenderers.Contains(r)).ToArray();
            // Settings and first-use help must remain visible over the showroom. All cockpit
            // geometry retains its original suppression; inactive UI still submits no draws.
            hidden.arraySize=retained.Length;
            for(int n=0;n<retained.Length;n++)hidden.GetArrayElementAtIndex(n).objectReferenceValue=retained[n];
            viewData.ApplyModifiedPropertiesWithoutUndo();
            var hint=view.SelectionContentRoot.GetComponentsInChildren<TextMesh>(true)
                .SingleOrDefault(t=>t.name=="StartupNavigationHint");
            if(hint!=null)
            {
                hint.text="[A] Select   [B / Y] Back   [Menu] Settings / help\nThumbstick up / down moves selection";
                ProductUiLabel.Sync(hint);EditorUtility.SetDirty(hint);
            }
            EditorUtility.SetDirty(navigation);EditorUtility.SetDirty(view);
            Debug.Log("[GuestFinish] Bound shared instruction sizing; navigation remains visible in the showroom. Existing panels and flight authority preserved.");
        }
    }
}
