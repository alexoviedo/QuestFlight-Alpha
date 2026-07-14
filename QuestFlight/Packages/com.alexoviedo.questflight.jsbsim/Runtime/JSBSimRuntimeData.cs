using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace AlexOviedo.QuestFlight.JSBSim
{
    /// <summary>
    /// Resolves package-owned JSBSim data without changing Android shipping
    /// settings. Android development players extract the bounded data set from
    /// StreamingAssets because native JSBSim requires ordinary filesystem paths.
    /// </summary>
    public static class JSBSimRuntimeData
    {
        public const string PackageName = "com.alexoviedo.questflight.jsbsim";
        public const string DataFolderName = "JSBSim-1.3.1";
        public const string RuntimeManifestFileName = "runtime_manifest.txt";
        private const string SourceRevisionFileName = "SOURCE_REVISION.txt";

        public static string EditorPackageDataRoot => Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "..",
            "Packages",
            PackageName,
            "RuntimeData",
            DataFolderName));

        public static bool TryGetPreparedDataRoot(out string dataRoot, out string error)
        {
#if UNITY_EDITOR
            dataRoot = EditorPackageDataRoot;
#elif UNITY_ANDROID
            dataRoot = Path.Combine(Application.persistentDataPath, PackageName, DataFolderName);
#else
            dataRoot = Path.Combine(Application.streamingAssetsPath, DataFolderName);
#endif
            return ValidateDataRoot(dataRoot, out error);
        }

        public static IEnumerator Prepare(Action<bool, string, string> completed)
        {
            if (TryGetPreparedDataRoot(out string preparedRoot, out string preparedError))
            {
                completed?.Invoke(true, preparedRoot, string.Empty);
                yield break;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            string destination = Path.Combine(Application.persistentDataPath, PackageName, DataFolderName);
            string manifestUri = CombineStreamingUri(DataFolderName + "/" + RuntimeManifestFileName);
            using (UnityWebRequest manifestRequest = UnityWebRequest.Get(manifestUri))
            {
                yield return manifestRequest.SendWebRequest();
                if (manifestRequest.result != UnityWebRequest.Result.Success)
                {
                    completed?.Invoke(false, string.Empty,
                        "Could not read packaged JSBSim runtime manifest: " + manifestRequest.error);
                    yield break;
                }

                string manifest = manifestRequest.downloadHandler.text;
                if (!TryParseManifest(manifest, out List<string> relativePaths, out string manifestError))
                {
                    completed?.Invoke(false, string.Empty, manifestError);
                    yield break;
                }

                string temporary = destination + ".extracting";
                DeleteDirectoryIfPresent(temporary);
                Directory.CreateDirectory(temporary);
                bool failed = false;
                string failure = string.Empty;
                foreach (string relativePath in relativePaths)
                {
                    string sourceUri = CombineStreamingUri(DataFolderName + "/" + relativePath);
                    using (UnityWebRequest request = UnityWebRequest.Get(sourceUri))
                    {
                        yield return request.SendWebRequest();
                        if (request.result != UnityWebRequest.Result.Success)
                        {
                            failed = true;
                            failure = "Could not extract packaged JSBSim data '" + relativePath + "': " + request.error;
                            break;
                        }

                        string target = SafeDestination(temporary, relativePath);
                        if (target == null)
                        {
                            failed = true;
                            failure = "Unsafe path in JSBSim runtime manifest: " + relativePath;
                            break;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.WriteAllBytes(target, request.downloadHandler.data);
                    }
                }

                if (failed)
                {
                    DeleteDirectoryIfPresent(temporary);
                    completed?.Invoke(false, string.Empty, failure);
                    yield break;
                }

                File.WriteAllText(Path.Combine(temporary, RuntimeManifestFileName), manifest);
                if (!ValidateDataRoot(temporary, out string validationError))
                {
                    DeleteDirectoryIfPresent(temporary);
                    completed?.Invoke(false, string.Empty, validationError);
                    yield break;
                }

                DeleteDirectoryIfPresent(destination);
                Directory.Move(temporary, destination);
                completed?.Invoke(true, destination, string.Empty);
            }
#else
            completed?.Invoke(false, string.Empty, preparedError);
#endif
        }

        public static bool ValidateDataRoot(string dataRoot, out string error)
        {
            if (string.IsNullOrWhiteSpace(dataRoot) || !Directory.Exists(dataRoot))
            {
                error = "JSBSim data root is absent: " + dataRoot;
                return false;
            }

            string manifestPath = Path.Combine(dataRoot, RuntimeManifestFileName);
            if (!File.Exists(manifestPath))
            {
                error = "JSBSim runtime manifest is absent: " + manifestPath;
                return false;
            }

            if (!TryParseManifest(File.ReadAllText(manifestPath), out List<string> paths, out error))
                return false;
            foreach (string relativePath in paths)
            {
                string filePath = SafeDestination(dataRoot, relativePath);
                if (filePath == null || !File.Exists(filePath))
                {
                    error = "JSBSim runtime data is incomplete: " + relativePath;
                    return false;
                }
            }

            string sourceRevisionPath = Path.Combine(dataRoot, SourceRevisionFileName);
            string sourceRevision = File.ReadAllText(sourceRevisionPath);
            if (sourceRevision.IndexOf(JSBSimNativeSession.PinnedRevision, StringComparison.Ordinal) < 0)
            {
                error = "JSBSim runtime data revision does not match the pinned native library.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryParseManifest(string manifest, out List<string> paths, out string error)
        {
            paths = new List<string>();
            string[] lines = (manifest ?? string.Empty).Split(new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in lines)
            {
                string path = raw.Trim().Replace('\\', '/');
                if (path.Length == 0) continue;
                if (Path.IsPathRooted(path) || path.StartsWith("/", StringComparison.Ordinal) ||
                    path.Contains("../") || path.Contains("/..") || path == "..")
                {
                    error = "Unsafe path in JSBSim runtime manifest: " + path;
                    return false;
                }
                paths.Add(path);
            }

            if (paths.Count == 0 || !paths.Contains(SourceRevisionFileName))
            {
                error = "JSBSim runtime manifest is empty or omits source provenance.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static string SafeDestination(string root, string relativePath)
        {
            string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            return candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ? candidate : null;
        }

        private static string CombineStreamingUri(string relativePath)
        {
            return Application.streamingAssetsPath.TrimEnd('/', '\\') + "/" + relativePath.Replace('\\', '/');
        }

        private static void DeleteDirectoryIfPresent(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }
}
