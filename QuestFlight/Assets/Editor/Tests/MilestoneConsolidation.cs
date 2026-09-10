using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using QuestFlightLab.Environment;

namespace QuestFlightLab.Editor.Tests
{
    public static class MilestoneConsolidation
    {
        public static void Audit()
        {
            string root=System.Environment.GetEnvironmentVariable("QFL_MILESTONE_ROOT");
            if(string.IsNullOrEmpty(root))throw new InvalidOperationException("QFL_MILESTONE_ROOT required");
            Directory.CreateDirectory(root);
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity");
            var environment=UnityEngine.Object.FindFirstObjectByType<ProductionEnvironmentRoot>();
            var lines=environment.GetComponentsInChildren<Transform>(true)
                .Where(t=>t.name.Contains("Grove")||t.name.Contains("Neighborhood"))
                .Select(t=>t.name+" active="+t.gameObject.activeInHierarchy+" components="+
                    string.Join(",",t.GetComponents<Component>().Where(c=>c!=null).Select(c=>c.GetType().Name))+" renderers="+
                    string.Join(",",t.GetComponentsInChildren<Renderer>(true).Select(r=>r.name+":"+r.enabled+":"+r.gameObject.activeInHierarchy)));
            File.WriteAllLines(Path.Combine(root,"grove_hierarchy_before.txt"),lines);
            File.WriteAllLines(Path.Combine(root,"scene_dependencies_before.txt"),AssetDatabase.GetDependencies(scene.path,true).OrderBy(p=>p));
            Debug.Log("[Milestone] Authored dependency audit: "+root);
        }

        public static void RemoveRetiredNodes()
        {
            string path=ProductionEnvironmentRoot.PrefabAssetPath;
            var prefab=PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach(string name in new[]{"ConnectedGrovePilotV1","AngularGrovesV1"})
                {
                    var node=prefab.transform.Find(name);
                    if(node==null)continue;
                    if(node.GetComponentsInChildren<Renderer>(true).Any(r=>r.enabled&&r.gameObject.activeInHierarchy))
                        throw new InvalidOperationException("Retired node still has an active renderer: "+name);
                    UnityEngine.Object.DestroyImmediate(node.gameObject);
                }
                PrefabUtility.SaveAsPrefabAsset(prefab,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(prefab);}
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity");
            EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Debug.Log("[Milestone] Removed only disabled retired grove nodes");
        }

        public static void Validate()
        {
            RemoveRetiredNodes();
            string root=System.Environment.GetEnvironmentVariable("QFL_MILESTONE_ROOT");
            if(string.IsNullOrEmpty(root))throw new InvalidOperationException("QFL_MILESTONE_ROOT required");
            Directory.CreateDirectory(root);
            var dependencies=AssetDatabase.GetDependencies("Assets/Scenes/ProductionVerticalSlice.unity",true).OrderBy(p=>p).ToArray();
            if(dependencies.Any(p=>p.Contains("AngularGrovesV1")||p.Contains("ConnectedGrovePilotV1")))
                throw new InvalidOperationException("Retired grove assets remain in product dependency closure");
            var grove=UnityEngine.Object.FindFirstObjectByType<MapBranchGroveRenderer>();
            if(grove==null||!grove.enabled)throw new InvalidOperationException("Accepted map canopy renderer missing");
            File.WriteAllLines(Path.Combine(root,"scene_dependencies_after.txt"),dependencies);
            System.Environment.SetEnvironmentVariable("QFL_RECOVERY_ROOT",Path.Combine(root,"visual_qa"));
            FoveationCloudRecoveryReview.Run();
            Debug.Log("[Milestone] Consolidated dependency and visual validation completed");
        }
    }
}
