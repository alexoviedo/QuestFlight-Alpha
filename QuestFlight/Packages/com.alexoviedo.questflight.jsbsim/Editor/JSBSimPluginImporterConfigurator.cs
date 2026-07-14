using System;
using UnityEditor;
using UnityEngine;

namespace AlexOviedo.QuestFlight.JSBSim.Editor
{
    [InitializeOnLoad]
    public static class JSBSimPluginImporterConfigurator
    {
        public static readonly string[] WindowsPlugins =
        {
            "Packages/com.alexoviedo.questflight.jsbsim/Runtime/Plugins/Windows/x86_64/JSBSim.dll",
            "Packages/com.alexoviedo.questflight.jsbsim/Runtime/Plugins/Windows/x86_64/qfl_jsbsim_native.dll"
        };

        public static readonly string[] AndroidPlugins =
        {
            "Packages/com.alexoviedo.questflight.jsbsim/Runtime/Plugins/Android/arm64-v8a/libJSBSim.so",
            "Packages/com.alexoviedo.questflight.jsbsim/Runtime/Plugins/Android/arm64-v8a/libqfl_jsbsim_native.so"
        };

        static JSBSimPluginImporterConfigurator()
        {
            EditorApplication.delayCall += Configure;
        }

        [MenuItem("QuestFlight/Development/Configure promoted JSBSim plugins")]
        public static void Configure()
        {
            ConfigureAndValidate(false);
        }

        public static void ConfigureAndValidate(bool failIfMissing = true)
        {
            foreach (string path in WindowsPlugins)
                ConfigurePlugin(path, editor: true, windows: true, android: false, failIfMissing);
            foreach (string path in AndroidPlugins)
                ConfigurePlugin(path, editor: false, windows: false, android: true, failIfMissing);
        }

        private static void ConfigurePlugin(
            string path,
            bool editor,
            bool windows,
            bool android,
            bool failIfMissing)
        {
            if (!(AssetImporter.GetAtPath(path) is PluginImporter importer))
            {
                string message = "Promoted native plugin importer is missing: " + path;
                if (failIfMissing) throw new InvalidOperationException(message);
                Debug.LogWarning("[QuestFlight][JSBSim] " + message);
                return;
            }

            bool changed = false;
            changed |= Set(importer.GetCompatibleWithAnyPlatform(), false, importer.SetCompatibleWithAnyPlatform);
            changed |= Set(importer.GetCompatibleWithEditor(), editor, importer.SetCompatibleWithEditor);
            changed |= Set(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64), windows,
                value => importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, value));
            changed |= Set(importer.GetCompatibleWithPlatform(BuildTarget.Android), android,
                value => importer.SetCompatibleWithPlatform(BuildTarget.Android, value));

            if (editor)
            {
                changed |= SetData(importer.GetEditorData("OS"), "Windows",
                    value => importer.SetEditorData("OS", value));
                changed |= SetData(importer.GetEditorData("CPU"), "x86_64",
                    value => importer.SetEditorData("CPU", value));
            }
            if (windows)
            {
                changed |= SetData(importer.GetPlatformData(BuildTarget.StandaloneWindows64, "CPU"), "x86_64",
                    value => importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", value));
            }
            if (android)
            {
                changed |= SetData(importer.GetPlatformData(BuildTarget.Android, "CPU"), "ARM64",
                    value => importer.SetPlatformData(BuildTarget.Android, "CPU", value));
            }

            if (changed) importer.SaveAndReimport();
            ValidateImporter(path, importer, editor, windows, android);
        }

        private static void ValidateImporter(
            string path,
            PluginImporter importer,
            bool editor,
            bool windows,
            bool android)
        {
            if (importer.GetCompatibleWithAnyPlatform() ||
                importer.GetCompatibleWithEditor() != editor ||
                importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64) != windows ||
                importer.GetCompatibleWithPlatform(BuildTarget.Android) != android)
            {
                throw new InvalidOperationException("Native plugin platform isolation failed: " + path);
            }
            if (editor && (importer.GetEditorData("OS") != "Windows" ||
                           importer.GetEditorData("CPU") != "x86_64"))
                throw new InvalidOperationException("Windows editor plugin architecture is not x86_64: " + path);
            if (windows && importer.GetPlatformData(BuildTarget.StandaloneWindows64, "CPU") != "x86_64")
                throw new InvalidOperationException("Windows player plugin architecture is not x86_64: " + path);
            if (android && importer.GetPlatformData(BuildTarget.Android, "CPU") != "ARM64")
                throw new InvalidOperationException("Android plugin architecture is not ARM64: " + path);
        }

        private static bool Set(bool current, bool target, Action<bool> setter)
        {
            if (current == target) return false;
            setter(target);
            return true;
        }

        private static bool SetData(string current, string target, Action<string> setter)
        {
            if (current == target) return false;
            setter(target);
            return true;
        }
    }
}
