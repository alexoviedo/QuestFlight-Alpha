using System;
using System.Linq;
using Oculus.Interaction;
using QuestFlight.Product;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    public static class MenuRepairAuthoring
    {
        public static void ConfigureScope(GameObject root)
        {
            var nav=root.GetComponentInChildren<ProductAppNavigation>(true);if(nav==null)return;
            var controls=root.GetComponentsInChildren<MonoBehaviour>(true).Where(b=>b is IInteractableView&&
                b.GetComponentInParent<ProductActionButton>(true)==null&&b.GetComponentInParent<ProductFlightModeMenuButton>(true)==null).ToArray();
            if(controls.Length==0)throw new InvalidOperationException("No authored cockpit interaction scope.");
            nav.ConfigureMenuInteractionScope(controls);EditorUtility.SetDirty(nav);
        }
        public static void AuthorMenuScopeBatch()
        {
            try
            {
                string path=ProductTrainingAuthoring.World;var root=PrefabUtility.LoadPrefabContents(path);
                try{ConfigureScope(root);PrefabUtility.SaveAsPrefabAsset(root,path);}
                finally{PrefabUtility.UnloadPrefabContents(root);}
                AssetDatabase.SaveAssets();EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
