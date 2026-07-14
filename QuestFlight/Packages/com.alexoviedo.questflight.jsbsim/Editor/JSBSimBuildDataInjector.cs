using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AlexOviedo.QuestFlight.JSBSim.Editor
{
    /// <summary>
    /// Stages only the bounded package runtime data into StreamingAssets for a
    /// player build, then removes the generated copy. Existing unowned data is
    /// never overwritten or deleted.
    /// </summary>
    public sealed class JSBSimBuildDataInjector : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private const string OwnershipMarker = ".questflight_jsbsim_package_injection";
        private const string MarkerContents = "com.alexoviedo.questflight.jsbsim/0.1.0";

        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            JSBSimPluginImporterConfigurator.ConfigureAndValidate();
            Inject();
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            RemoveOwnedInjection();
        }

        public static void Inject()
        {
            string source = JSBSimRuntimeData.EditorPackageDataRoot;
            if (!JSBSimRuntimeData.ValidateDataRoot(source, out string sourceError))
                throw new BuildFailedException("Cannot inject JSBSim runtime data: " + sourceError);

            string destination = DestinationRoot;
            if (Directory.Exists(destination))
            {
                string marker = Path.Combine(destination, OwnershipMarker);
                if (!File.Exists(marker) || !string.Equals(File.ReadAllText(marker), MarkerContents,
                        StringComparison.Ordinal))
                {
                    throw new BuildFailedException(
                        "Refusing to overwrite unowned StreamingAssets data at " + destination);
                }
                Directory.Delete(destination, true);
            }

            CopyDirectoryWithoutMetadata(source, destination);
            File.WriteAllText(Path.Combine(destination, OwnershipMarker), MarkerContents);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[QuestFlight][JSBSim] Injected bounded runtime data for player build: " + destination);
        }

        public static void RemoveOwnedInjection()
        {
            string destination = DestinationRoot;
            string marker = Path.Combine(destination, OwnershipMarker);
            if (!Directory.Exists(destination)) return;
            if (!File.Exists(marker) || !string.Equals(File.ReadAllText(marker), MarkerContents,
                    StringComparison.Ordinal))
            {
                Debug.LogWarning("[QuestFlight][JSBSim] Leaving unowned StreamingAssets directory untouched: " +
                                 destination);
                return;
            }
            Directory.Delete(destination, true);
            string metadata = destination + ".meta";
            if (File.Exists(metadata)) File.Delete(metadata);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static string DestinationRoot => Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "StreamingAssets",
            JSBSimRuntimeData.DataFolderName));

        private static void CopyDirectoryWithoutMetadata(string sourceRoot, string destinationRoot)
        {
            foreach (string sourceFile in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                if (sourceFile.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string relative = sourceFile.Substring(sourceRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string destination = Path.Combine(destinationRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(sourceFile, destination, true);
            }
        }
    }
}
