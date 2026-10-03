using System;
using System.IO;
using System.Linq;
using QuestFlight.Product;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    public static class DemoFinishAuthoring
    {
        const string World = "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
        const string Audio = "Assets/Production/UI/Interface/Audio/";
        public static void RunBatch()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                foreach (string name in new[] { "Focus", "Select", "Back", "Confirm" })
                {
                    var importer = AssetImporter.GetAtPath(Audio + name + ".wav") as AudioImporter;
                    if (importer == null) throw new FileNotFoundException(Audio + name);
                    var settings = importer.defaultSampleSettings;
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    settings.compressionFormat = AudioCompressionFormat.PCM;
                    importer.defaultSampleSettings = settings;
                    importer.forceToMono = true; importer.SaveAndReimport();
                }
                // Author the nested aircraft before updating the world menu's
                // renderer suppression bindings. Otherwise a newly added cowling
                // remains visible through the startup/settings showroom.
                QuestFlightLab.Editor.CockpitEngineCowlAuthoring.AuthorExteriorBatch();
                var world = PrefabUtility.LoadPrefabContents(World);
                try
                {
                    ProductGuestFinishAuthoring.Apply(world);
                    BindCowlingForStartup(world);
                    var feedback = world.GetComponentInChildren<ProductUiFeedback>(true);
                    if (feedback == null)
                    {
                        var node = new GameObject("InterfaceFeedback"); node.transform.SetParent(world.transform, false);
                        feedback = node.AddComponent<ProductUiFeedback>();
                    }
                    var source = feedback.GetComponent<AudioSource>();
                    if (source == null) source = feedback.gameObject.AddComponent<AudioSource>();
                    source.playOnAwake = false; source.loop = false; source.spatialBlend = 0;
                    source.volume = .28f; source.dopplerLevel = 0; source.priority = 64;
                    source.ignoreListenerPause = true;
                    AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + name + ".wav")
                        ?? throw new FileNotFoundException(name);
                    feedback.Configure(source, Clip("Focus"), Clip("Select"), Clip("Back"), Clip("Confirm"));
                    if (world.GetComponentsInChildren<ProductUiFeedback>(true).Length != 1)
                        throw new InvalidOperationException("Exactly one interface feedback owner required.");
                    PrefabUtility.SaveAsPrefabAsset(world, World);
                }
                finally { PrefabUtility.UnloadPrefabContents(world); }
                AssetDatabase.SaveAssets();
                Debug.Log("[DemoFinish] Guest/settings, shared interface feedback and original cowling authored.");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        private static void BindCowlingForStartup(GameObject world)
        {
            var view = world.GetComponentInChildren<ProductFlightModeMenuView>(true);
            var cowling = world.GetComponentsInChildren<MeshRenderer>(true).Single(renderer =>
                renderer.name == QuestFlightLab.Editor.CockpitEngineCowlAuthoring.ExteriorCowlObjectName);
            var serialized = new SerializedObject(view);
            var hidden = serialized.FindProperty("aircraftPresentationRenderers");
            if (!Enumerable.Range(0, hidden.arraySize).Any(index =>
                    hidden.GetArrayElementAtIndex(index).objectReferenceValue == cowling))
            {
                int index = hidden.arraySize++;
                hidden.GetArrayElementAtIndex(index).objectReferenceValue = cowling;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(view);
            }
            // Preserve ProductGuestFinishAuthoring's navigation-panel exclusion;
            // only the exact new physical aircraft renderer is appended here.
        }
    }
}
