using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Audio;
using QuestFlightLab.Flight;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace QuestFlightLab.Editor
{
    public static class ProductionC172AudioAuthoring
    {
        private const string PrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";
        private const string AudioRoot = "Assets/Production/Audio/C172";
        private const string ClipRoot = AudioRoot + "/Clips";
        private const string ProfilePath = AudioRoot + "/ProductionC172AudioProfile.asset";
        private const string MixerPath = AudioRoot + "/ProductionC172Audio.mixer";
        private const string ObjectName = "ProductionC172AudioRoot";

        [MenuItem("QuestFlight/Audio/Author C172 Cockpit Soundscape")]
        public static void AuthorAndIntegrate()
        {
            Directory.CreateDirectory(AudioRoot);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureClipImports();
            ProductionC172AudioProfile profile = CreateOrUpdateProfile();
            AudioMixer mixer = CreateOrUpdateMixer();
            IntegratePrefab(profile, mixer);
            AssetDatabase.SaveAssets();
            ValidateProductPrefab();
            Debug.Log("[QuestFlight][Audio] Authored backend-neutral C172 cockpit soundscape with eight loop voices and two one-shot voices.");
        }

        public static void ValidateProductPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ProductionC172AudioController[] controllers =
                    root.GetComponentsInChildren<ProductionC172AudioController>(true);
                Require(controllers.Length == 1,
                    $"Expected one C172 audio controller; found {controllers.Length}.");
                Require(controllers[0].HasAuthoredReferences(out string error), error);
                AudioSource[] authored = controllers[0].GetComponentsInChildren<AudioSource>(true);
                Require(authored.Length == 10,
                    $"C172 audio root must own exactly ten bounded sources; found {authored.Length}.");
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (AudioSource source in authored)
                {
                    Require(names.Add(source.name), $"Duplicate audio source role: {source.name}");
                    Require(source.dopplerLevel == 0f,
                        $"Ownship source {source.name} must not Doppler-shift against its listener.");
                    Require(source.outputAudioMixerGroup != null,
                        $"Source {source.name} must route through the authored mixer.");
                    Require(!source.playOnAwake,
                        $"Source {source.name} playback is owned by the controller, not Play On Awake.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureClipImports()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { ClipRoot });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) continue;
                importer.forceToMono = false;
                importer.loadInBackground = false;
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                bool oneShot = path.Contains("Switch_Click") || path.Contains("Touchdown");
                settings.loadType = oneShot ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.CompressedInMemory;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = oneShot ? 0.72f : 0.58f;
                settings.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
        }

        private static ProductionC172AudioProfile CreateOrUpdateProfile()
        {
            ProductionC172AudioProfile profile =
                AssetDatabase.LoadAssetAtPath<ProductionC172AudioProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<ProductionC172AudioProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            profile.engineStart = Clip("C172_Engine_Start.wav");
            profile.engineIdleLoop = Clip("C172_Engine_Idle_Loop.wav");
            profile.engineCruiseLoop = Clip("C172_Engine_Cruise_Loop.wav");
            profile.propellerHighLoop = Clip("C172_Propeller_High_Loop.wav");
            profile.windLowLoop = Clip("C172_Wind_Low_Loop.wav");
            profile.windHighLoop = Clip("C172_Wind_High_Loop.wav");
            profile.groundRollLoop = Clip("C172_Ground_Roll_Loop.wav");
            profile.flapMotorLoop = Clip("C172_Flap_Motor_Loop.wav");
            profile.stallHornLoop = Clip("C172_Stall_Horn_Loop.wav");
            profile.switchClick = Clip("C172_Switch_Click.wav");
            profile.touchdown = Clip("C172_Touchdown.wav");
            Require(profile.HasAllRequiredClips, "Generated C172 audio profile is incomplete.");
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static AudioMixer CreateOrUpdateMixer()
        {
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer != null) return mixer;

            // Unity exposes mixer playback publicly but keeps deterministic
            // editor construction internal. Invoke the exact Unity editor
            // factory rather than serializing undocumented mixer YAML.
            Type controllerType = Type.GetType(
                "UnityEditor.Audio.AudioMixerController, UnityEditor",
                throwOnError: true);
            MethodInfo create = controllerType.GetMethod(
                "CreateMixerControllerAtPath",
                BindingFlags.Public | BindingFlags.Static);
            object controller = create.Invoke(null, new object[] { MixerPath });
            mixer = (AudioMixer)controller;
            MethodInfo createGroup = controllerType.GetMethod(
                "CreateNewGroup", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo addChild = controllerType.GetMethod(
                "AddChildToParent", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo masterProperty = controllerType.GetProperty(
                "masterGroup", BindingFlags.Public | BindingFlags.Instance);
            object master = masterProperty.GetValue(controller);
            foreach (string name in new[] { "Ownship", "Cockpit", "Warnings", "ExternalWorld" })
            {
                object group = createGroup.Invoke(controller, new object[] { name, false });
                addChild.Invoke(controller, new[] { group, master });
            }
            EditorUtility.SetDirty(mixer);
            return mixer;
        }

        private static void IntegratePrefab(
            ProductionC172AudioProfile profile,
            AudioMixer mixer)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                FlightDynamicsCoordinator[] coordinators =
                    root.GetComponentsInChildren<FlightDynamicsCoordinator>(true);
                ProductionCockpitSystemsController[] systems =
                    root.GetComponentsInChildren<ProductionCockpitSystemsController>(true);
                Require(coordinators.Length == 1,
                    $"Expected one flight coordinator; found {coordinators.Length}.");
                Require(systems.Length == 1,
                    $"Expected one cockpit systems controller; found {systems.Length}.");

                Transform existing = root.transform.Find(ObjectName);
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                var audioRoot = new GameObject(ObjectName);
                audioRoot.transform.SetParent(root.transform, false);
                ProductionC172AudioController controller =
                    audioRoot.AddComponent<ProductionC172AudioController>();

                AudioMixerGroup ownship = Group(mixer, "Ownship");
                AudioMixerGroup cockpit = Group(mixer, "Cockpit");
                AudioMixerGroup warnings = Group(mixer, "Warnings");
                AudioSource engineIdle = Source(audioRoot.transform, "EngineIdle", profile.engineIdleLoop, ownship, false);
                AudioSource engineCruise = Source(audioRoot.transform, "EngineCruise", profile.engineCruiseLoop, ownship, false);
                AudioSource propeller = Source(audioRoot.transform, "Propeller", profile.propellerHighLoop, ownship, false);
                AudioSource windLow = Source(audioRoot.transform, "WindLow", profile.windLowLoop, ownship, false);
                AudioSource windHigh = Source(audioRoot.transform, "WindHigh", profile.windHighLoop, ownship, false);
                AudioSource groundRoll = Source(audioRoot.transform, "GroundRoll", profile.groundRollLoop, ownship, false);
                AudioSource flapMotor = Source(audioRoot.transform, "FlapMotor", profile.flapMotorLoop, cockpit, false);
                AudioSource stallHorn = Source(audioRoot.transform, "StallHorn", profile.stallHornLoop, warnings, false);
                AudioSource cockpitOneShot = Source(audioRoot.transform, "CockpitOneShot", null, cockpit, false);
                AudioSource airframeOneShot = Source(audioRoot.transform, "AirframeOneShot", null, ownship, false);

                controller.ConfigureAuthoredReferences(
                    profile, coordinators[0], systems[0],
                    root.GetComponentInChildren<AircraftState>(true),
                    engineIdle, engineCruise, propeller, windLow, windHigh,
                    groundRoll, flapMotor, stallHorn, cockpitOneShot, airframeOneShot);
                Require(controller.HasAuthoredReferences(out string error), error);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                Require(success && saved != null, "Unity failed to save the authored aircraft audio prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static AudioSource Source(
            Transform parent,
            string name,
            AudioClip clip,
            AudioMixerGroup group,
            bool spatial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = clip != null && !name.Contains("OneShot");
            source.playOnAwake = false;
            source.volume = 0f;
            source.spatialBlend = spatial ? 1f : 0f;
            source.dopplerLevel = 0f;
            source.priority = name == "StallHorn" ? 32 : 96;
            source.outputAudioMixerGroup = group;
            return source;
        }

        private static AudioMixerGroup Group(AudioMixer mixer, string name)
        {
            AudioMixerGroup[] groups = mixer.FindMatchingGroups(name);
            Require(groups.Length == 1, $"Audio mixer group {name} is missing or ambiguous.");
            return groups[0];
        }

        private static AudioClip Clip(string name)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{ClipRoot}/{name}");
            Require(clip != null, $"Required generated audio clip is missing: {name}");
            return clip;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
