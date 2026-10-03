using System;
using System.IO;
using System.Linq;
using QuestFlight.Product.Training;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    // Content-only migration. Preserve accepted UI, anchors, routes, native trims and GUIDs.
    public static class ProductLessonTeachingAuthoring
    {
        [Serializable] public sealed class StageText { public string title,instruction,success,retry; }
        [Serializable] public sealed class LessonText { public string id,briefing,goal,reflection; public StageText[] stages; }
        [Serializable] public sealed class ReferenceText { public string id,text; }
        [Serializable] public sealed class CueText { public string id,reference,text; }
        [Serializable] public sealed class Content { public LessonText[] lessons; public ReferenceText[] references; public CueText[] cues; }
        public static void RunBatch()
        {
            try { Apply(); Debug.Log("[LessonTeaching] Six lessons and coaching updated without reauthoring hierarchy or trim."); EditorApplication.Exit(0); }
            catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        public static void Apply()
        {
            AssetDatabase.Refresh();
            var content=JsonUtility.FromJson<Content>(File.ReadAllText(ProductTrainingAuthoring.Root+"/LessonTeachingContent.json"));
            if(content.lessons.Length!=6)throw new InvalidOperationException("Six lessons required.");
            foreach(var item in content.lessons)
            {
                var lesson=AssetDatabase.LoadAssetAtPath<TrainingLessonDefinition>(ProductTrainingAuthoring.Root+"/"+item.id+".asset");
                if(lesson==null||lesson.stages.Length!=item.stages.Length)throw new InvalidOperationException("Stage mismatch: "+item.id);
                lesson.briefing=item.briefing;lesson.learningGoal=item.goal;lesson.reflection=item.reflection;
                lesson.briefingNarration=Voice(item.id+"_briefing");
                for(int n=0;n<item.stages.Length;n++)
                {
                    var text=item.stages[n];var stage=lesson.stages[n];
                    stage.title=text.title;stage.instruction=text.instruction;stage.successFeedback=text.success;stage.retryAdvice=text.retry;
                    stage.narration=Voice(item.id+"_"+n.ToString("00"));
                    if(string.IsNullOrWhiteSpace(stage.retryAdvice)||string.IsNullOrWhiteSpace(stage.successFeedback))throw new InvalidOperationException("Missing feedback.");
                }
                // In-flight branching uses the same landing/go-around paths. No new maneuver is introduced.
                EditorUtility.SetDirty(lesson);
            }
            var world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);
            try
            {
                var coach=world.GetComponentInChildren<TrainingCoach>(true);
                var so=new SerializedObject(coach);
                var markers=so.FindProperty("markers");
                for(int n=0;n<markers.arraySize;n++)
                {
                    var entry=markers.GetArrayElementAtIndex(n);
                    string id=((TrainingReference)entry.FindPropertyRelative("reference").intValue).ToString();
                    var text=content.references.Single(r=>r.id==id);
                    entry.FindPropertyRelative("explanation").stringValue=text.text;
                    entry.FindPropertyRelative("voice").objectReferenceValue=Voice("reference_"+id.ToLowerInvariant());
                }
                var lines=so.FindProperty("lines");lines.arraySize=content.cues.Length;
                for(int n=0;n<content.cues.Length;n++)
                {
                    var entry=lines.GetArrayElementAtIndex(n);var text=content.cues[n];
                    entry.FindPropertyRelative("cue").intValue=(int)Enum.Parse(typeof(TrainingCue),text.id);
                    entry.FindPropertyRelative("reference").intValue=(int)Enum.Parse(typeof(TrainingReference),text.reference);
                    entry.FindPropertyRelative("text").stringValue=text.text;
                    entry.FindPropertyRelative("voice").objectReferenceValue=Voice("coach_"+text.id.ToLowerInvariant());
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(world,ProductTrainingAuthoring.World);
            }
            finally {PrefabUtility.UnloadPrefabContents(world);}
            AssetDatabase.SaveAssets();
        }
        private static AudioClip Voice(string id)
        {
            string path=ProductTrainingAuthoring.Root+"/Voice/"+id+".wav";
            var importer=AssetImporter.GetAtPath(path) as AudioImporter;
            if(importer==null)throw new InvalidOperationException("Generate narration before authoring: "+path);
            importer.forceToMono=true;
            var settings=importer.defaultSampleSettings;
            settings.loadType=AudioClipLoadType.CompressedInMemory;settings.compressionFormat=AudioCompressionFormat.Vorbis;
            settings.quality=.55f;settings.sampleRateSetting=AudioSampleRateSetting.OverrideSampleRate;settings.sampleRateOverride=File.Exists(ProductTrainingAuthoring.Root+"/Voice/KOKORO_SOURCE_MANIFEST.json")?24000u:22050u;
            importer.defaultSampleSettings=settings;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
