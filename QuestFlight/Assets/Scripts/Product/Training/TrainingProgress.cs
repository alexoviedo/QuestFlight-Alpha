using System;
using System.IO;
using UnityEngine;

namespace QuestFlight.Product.Training
{
    [Serializable] public sealed class TrainingProgressEntry
    {
        public string lessonId;
        public bool independentCompletion;
        public int attempts;
        public string lastResult;
        public string lastDebrief;
    }

    [Serializable] public sealed class TrainingProgress
    {
        public int schemaVersion = 1;
        public TrainingProgressEntry[] lessons = Array.Empty<TrainingProgressEntry>();

        public static TrainingProgress Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new TrainingProgress();
                var value = JsonUtility.FromJson<TrainingProgress>(File.ReadAllText(path));
                return value != null && value.schemaVersion == 1 && value.lessons != null ? value : new TrainingProgress();
            }
            catch (Exception e) { Debug.LogWarning("[Training] Progress could not be read: " + e.Message); return new TrainingProgress(); }
        }

        public TrainingProgressEntry Find(string id) => Array.Find(lessons, e => e != null && e.lessonId == id);

        public void Record(string id, string result, string debrief, bool independent)
        {
            var entry = Find(id);
            if (entry == null)
            {
                entry = new TrainingProgressEntry { lessonId = id };
                Array.Resize(ref lessons, lessons.Length + 1);
                lessons[lessons.Length - 1] = entry;
            }
            entry.attempts++;
            entry.independentCompletion |= independent;
            entry.lastResult = result;
            entry.lastDebrief = debrief;
        }

        public bool Save(string path)
        {
            try
            {
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(this, true));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                return true;
            }
            catch (Exception e) { Debug.LogWarning("[Training] Progress could not be saved: " + e.Message); return false; }
        }
    }
}
