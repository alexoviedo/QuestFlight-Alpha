using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace QuestFlight.Editor
{
    // Refresh only the authored scene's existing bake. Never invoke airport integration.
    public static class ProductLightingRefreshBatch
    {
        public static void RunBatch()
        {
            try
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/ProductionVerticalSlice.unity");
                var renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
                var contributors = renderers.Where(r => r.gameObject.activeInHierarchy && r.enabled &&
                    (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.ContributeGI) != 0).ToArray();
                Debug.Log("[LightingRefresh] Existing GI contributors: " + contributors.Length + " / " + renderers.Length);
                foreach (var r in contributors)
                    Debug.Log("[LightingRefresh] Contributor " + r.name + " scale=" + r.scaleInLightmap);
                if (Lightmapping.lightingSettings == null || !Lightmapping.lightingSettings.bakedGI)
                    throw new InvalidOperationException("Authored baked lighting is unavailable.");
                var importMetadata = LightmapSettings.lightmaps.Where(m => m.lightmapColor != null)
                    .Select(m => AssetDatabase.GetAssetPath(m.lightmapColor) + ".meta")
                    .ToDictionary(path => path, File.ReadAllBytes);
                bool invalid = false;
                Application.LogCallback check = (message, trace, type) =>
                {
                    if (message.IndexOf("non finite", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        message.IndexOf("non-finite", StringComparison.OrdinalIgnoreCase) >= 0) invalid = true;
                };
                Application.logMessageReceived += check;
                bool success;
                try { success = Lightmapping.Bake(); }
                finally { Application.logMessageReceived -= check; }
                if (!success || invalid) throw new InvalidOperationException("Lighting bake failed or reported non-finite output.");
                var maps = LightmapSettings.lightmaps;
                if (maps.Length != 1 || maps[0].lightmapColor == null || maps[0].lightmapDir != null ||
                    maps[0].lightmapColor.width > 1024 || maps[0].lightmapColor.height > 1024)
                    throw new InvalidOperationException("Bake exceeded the existing single non-directional atlas budget.");
                // Unity resets Android overrides on generated textures. Preserve authored compression.
                foreach (var entry in importMetadata)
                {
                    File.WriteAllBytes(entry.Key, entry.Value);
                    AssetDatabase.ImportAsset(entry.Key.Substring(0, entry.Key.Length - 5), ImportAssetOptions.ForceUpdate);
                }
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[LightingRefresh] SUCCESS " + AssetDatabase.GetAssetPath(maps[0].lightmapColor) +
                    " " + maps[0].lightmapColor.width + "x" + maps[0].lightmapColor.height);
                EditorApplication.Exit(0);
            }
            catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
        }
    }
}
