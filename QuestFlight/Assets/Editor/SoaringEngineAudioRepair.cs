using System;
using System.IO;
using System.Linq;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Soaring;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;
using UnityEngine.Audio;

namespace QuestFlightLab.Editor
{
    public static class SoaringEngineAudioRepair
    {
        public const string WorldPath = "Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
        private const string MixerPath = "Assets/Production/Audio/C172/ProductionC172Audio.mixer";

        public static void Configure(NativeTugAudio audio, AudioListener listener)
        {
            var source = audio.engine;
            audio.listener = listener;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.spread = 0f;
            source.dopplerLevel = 0f; // No transform-derived Doppler spikes on resets/repositioning.
            source.minDistance = 8f;
            source.maxDistance = 1200f;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.reverbZoneMix = 0f;
            source.volume = 0f;
            source.outputAudioMixerGroup = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath)
                .FindMatchingGroups("ExternalWorld").Single();

            // Reference mix: spherical spreading, then a smooth finite audibility
            // taper. This is not a measured PA18 noise contour or atmospheric model.
            float[] distances = { 0, 8, 16, 32, 64, 128, 256, 512, 600, 800, 1000, 1200 };
            var keys = distances.Select(distance => new Keyframe(distance / 1200f,
                Mathf.Min(1f, 8f / Mathf.Max(8f, distance)) *
                (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(600f, 1200f, distance))))).ToArray();
            // Piecewise linear slopes avoid spline overshoot below silence.
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].inTangent = i == 0 ? 0 : (keys[i].value - keys[i-1].value) / (keys[i].time - keys[i-1].time);
                keys[i].outTangent = i == keys.Length-1 ? 0 : (keys[i+1].value - keys[i].value) / (keys[i+1].time - keys[i].time);
            }
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(keys));
            var actor = audio.GetComponentInParent<AmbientAircraftActor>();
            if (actor == null || actor.sailplane) throw new InvalidOperationException("Tug sound must belong to the powered actor.");
            var propeller = actor.propellers.FirstOrDefault(p => p != null);
            if (propeller == null) throw new InvalidOperationException("Authored tug propeller location missing.");
            source.transform.position = propeller.position;
        }

        public static void Author()
        {
            var root = PrefabUtility.LoadPrefabContents(WorldPath);
            try
            {
                var listener = root.GetComponentsInChildren<AudioListener>(true).Single();
                foreach (var audio in root.GetComponentsInChildren<NativeTugAudio>(true)) Configure(audio, listener);
                PrefabUtility.SaveAsPrefabAsset(root, WorldPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }

        public static void BuildAndroidAudio()
        {
            string output = System.Environment.GetEnvironmentVariable("QF_ENGINE_AUDIO_QA");
            Directory.CreateDirectory(output);
            string scripts = Path.Combine(output, "android_scripts");
            Directory.CreateDirectory(scripts);
            var compiled = PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings
            { target = BuildTarget.Android, group = BuildTargetGroup.Android, options = ScriptCompilationOptions.None }, scripts);
            if (compiled.assemblies == null || compiled.assemblies.Count == 0) throw new Exception("Android script compilation failed.");
            string assets = Path.Combine(output, "android_audio");
            Directory.CreateDirectory(assets);
            var manifest = BuildPipeline.BuildAssetBundles(assets, new[] { new AssetBundleBuild
            {
                assetBundleName = "engine_audio",
                assetNames = new[] { MixerPath, "Assets/Production/Audio/C172/ProductionC172AudioProfile.asset" }
            } }, BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.Android);
            if (manifest == null) throw new Exception("Android audio asset build failed.");
            File.WriteAllText(Path.Combine(output, "android_validation.txt"),
                "PASS: Android player script compilation; strict Android audio asset bundle. Not APK/IL2CPP or headset acceptance.\nAssemblies=" + compiled.assemblies.Count);
        }
    }
}
