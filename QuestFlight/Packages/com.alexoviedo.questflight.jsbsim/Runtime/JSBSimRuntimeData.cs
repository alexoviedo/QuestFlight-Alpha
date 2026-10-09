using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
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
        public const string RuntimeHashesFileName = "runtime_hashes.sha256";
        private const string SourceRevisionFileName = "SOURCE_REVISION.txt";
        // Set only from the current APK, never from resident cache metadata.
        private static string packagedPayloadIdentity = string.Empty;
        public static string PreparedPayloadIdentity { get; private set; } = string.Empty;

        public static string EditorPackageDataRoot => Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "..",
            "Packages",
            PackageName,
            "RuntimeData",
            DataFolderName));

        public static bool TryGetPreparedDataRoot(out string dataRoot, out string error)
        {
            try { return TryGetPreparedDataRootUnchecked(out dataRoot, out error); }
            catch (Exception exception) when (IsStorageFailure(exception))
            { dataRoot = string.Empty; error = StorageError(exception); return false; }
        }

        private static bool TryGetPreparedDataRootUnchecked(out string dataRoot, out string error)
        {
#if UNITY_EDITOR
            dataRoot = EditorPackageDataRoot;
#elif UNITY_ANDROID
            if (string.IsNullOrEmpty(packagedPayloadIdentity))
            {
                dataRoot = string.Empty;
                error = "Current APK JSBSim payload identity has not been read; run Prepare first.";
                return false;
            }
            dataRoot = CacheRoot(packagedPayloadIdentity);
#else
            dataRoot = Path.Combine(Application.streamingAssetsPath, DataFolderName);
#endif
            bool valid = ValidateDataRoot(dataRoot, out error);
#if UNITY_ANDROID && !UNITY_EDITOR
            valid = valid && ValidatePayloadIdentity(dataRoot, packagedPayloadIdentity, out error);
#endif
            if (!valid) dataRoot = string.Empty;
            else PreparedPayloadIdentity = PayloadIdentity(dataRoot);
            return valid;
        }

        public static IEnumerator Prepare(Action<bool, string, string> completed)
        {
            // Drive the iterator here so expected synchronous storage failures, including
            // those after a request yield, complete the same terminal callback exactly once.
            bool notified = false;
            Action<bool, string, string> finish = (success, root, error) =>
            {
                if (notified) return;
                notified = true;
                completed?.Invoke(success, success ? root : string.Empty, error);
            };
            using (IEnumeratorPreparation preparation = new IEnumeratorPreparation(PrepareUnchecked(finish)))
            {
                while (true)
                {
                    bool next = false;
                    string failure = null;
                    try { next = preparation.Iterator.MoveNext(); }
                    catch (Exception exception) when (IsStorageFailure(exception))
                    { failure = StorageError(exception); }
                    if (failure != null) { finish(false, string.Empty, failure); yield break; }
                    if (!next) break;
                    yield return preparation.Iterator.Current;
                }
            }
            if (!notified) finish(false, string.Empty, "JSBSim data preparation ended without a result.");
        }

        private sealed class IEnumeratorPreparation : IDisposable
        {
            public readonly IEnumerator Iterator;
            public IEnumeratorPreparation(IEnumerator iterator) { Iterator = iterator; }
            public void Dispose() { (Iterator as IDisposable)?.Dispose(); }
        }

        private static IEnumerator PrepareUnchecked(Action<bool, string, string> completed)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Reading the packaged hash manifest anchors both its complete file set and
            // every payload byte to this APK, even when the upstream engine SHA is unchanged.
            using (UnityWebRequest identityRequest = UnityWebRequest.Get(
                       CombineStreamingUri(DataFolderName + "/" + RuntimeHashesFileName)))
            {
                identityRequest.timeout = 30;
                yield return identityRequest.SendWebRequest();
                if (identityRequest.result != UnityWebRequest.Result.Success ||
                    !TryParseHashes(identityRequest.downloadHandler.text, out _, out _))
                {
                    completed?.Invoke(false, string.Empty, "Could not read current APK JSBSim payload identity: " + identityRequest.error);
                    yield break;
                }
                packagedPayloadIdentity = HashBytes(identityRequest.downloadHandler.data);
            }
#endif
            if (TryGetPreparedDataRoot(out string preparedRoot, out string preparedError))
            {
                completed?.Invoke(true, preparedRoot, string.Empty);
                yield break;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            string destination = CacheRoot(packagedPayloadIdentity);
            string manifestUri = CombineStreamingUri(DataFolderName + "/" + RuntimeManifestFileName);
            using (UnityWebRequest manifestRequest = UnityWebRequest.Get(manifestUri))
            {
                manifestRequest.timeout = 30;
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

                string temporary = destination + ".extracting-" + Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(temporary);
                bool failed = false;
                string failure = string.Empty;
                foreach (string relativePath in relativePaths)
                {
                    string sourceUri = CombineStreamingUri(DataFolderName + "/" + relativePath);
                    using (UnityWebRequest request = UnityWebRequest.Get(sourceUri))
                    {
                        request.timeout = 30;
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

                // Preserve the exact packaged manifest bytes: its hash covers encoding too.
                File.WriteAllBytes(Path.Combine(temporary, RuntimeManifestFileName), manifestRequest.downloadHandler.data);
                if (!TryActivateCache(temporary, destination, packagedPayloadIdentity, out string validationError))
                {
                    DeleteDirectoryIfPresent(temporary);
                    completed?.Invoke(false, string.Empty, validationError);
                    yield break;
                }

                PreparedPayloadIdentity = packagedPayloadIdentity;
                completed?.Invoke(true, destination, string.Empty);
            }
#else
            completed?.Invoke(false, string.Empty, preparedError);
#endif
        }

        public static bool ValidateDataRoot(string dataRoot, out string error)
        {
            try { return ValidateDataRootUnchecked(dataRoot, out error); }
            catch (Exception exception) when (IsStorageFailure(exception))
            { error = StorageError(exception); return false; }
        }

        private static bool ValidateDataRootUnchecked(string dataRoot, out string error)
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

            if (!ValidateHashes(dataRoot, paths, out error))
                return false;

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
            HashSet<string> uniquePaths = new HashSet<string>(StringComparer.Ordinal);
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
                if (!uniquePaths.Add(path))
                {
                    error = "Duplicate path in JSBSim runtime manifest: " + path;
                    return false;
                }
                paths.Add(path);
            }

            if (paths.Count == 0 || !paths.Contains(SourceRevisionFileName) ||
                !paths.Contains(RuntimeHashesFileName))
            {
                error = "JSBSim runtime manifest is empty or omits provenance/integrity data.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static bool ValidateHashes(string dataRoot, IReadOnlyList<string> manifestPaths, out string error)
        {
            string hashesPath = Path.Combine(dataRoot, RuntimeHashesFileName);
            if (!File.Exists(hashesPath))
            {
                error = "JSBSim runtime hash manifest is absent: " + hashesPath;
                return false;
            }

            if (!TryParseHashes(File.ReadAllText(hashesPath), out Dictionary<string, string> hashes, out error))
                return false;

            HashSet<string> required = new HashSet<string>(StringComparer.Ordinal)
            {
                RuntimeManifestFileName
            };
            foreach (string relativePath in manifestPaths)
            {
                if (!string.Equals(relativePath, RuntimeHashesFileName, StringComparison.Ordinal))
                    required.Add(relativePath);
            }

            if (hashes.Count != required.Count)
            {
                error = "JSBSim runtime hash manifest does not describe the exact runtime payload.";
                return false;
            }

            foreach (string relativePath in required)
            {
                if (!hashes.TryGetValue(relativePath, out string expected))
                {
                    error = "JSBSim runtime hash manifest omits: " + relativePath;
                    return false;
                }

                string filePath = SafeDestination(dataRoot, relativePath);
                if (filePath == null || !File.Exists(filePath))
                {
                    error = "JSBSim runtime hash target is absent: " + relativePath;
                    return false;
                }

                string actual = Sha256(filePath);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    error = "JSBSim runtime hash mismatch: " + relativePath;
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private static bool TryParseHashes(string contents, out Dictionary<string, string> hashes, out string error)
        {
            hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] lines = (contents ?? string.Empty).Split(new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                int separator = line.IndexOf(' ');
                if (separator <= 0)
                {
                    error = "Malformed JSBSim runtime hash entry: " + line;
                    return false;
                }

                string hash = line.Substring(0, separator).Trim().ToLowerInvariant();
                string path = line.Substring(separator).Trim().Replace('\\', '/');
                if (!IsSha256(hash) || SafeDestination(".", path) == null || hashes.ContainsKey(path))
                {
                    error = "Invalid or duplicate JSBSim runtime hash entry: " + path;
                    return false;
                }
                hashes.Add(path, hash);
            }

            if (hashes.Count == 0)
            {
                error = "JSBSim runtime hash manifest is empty.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char character in value)
            {
                bool digit = character >= '0' && character <= '9';
                bool lowerHex = character >= 'a' && character <= 'f';
                if (!digit && !lowerHex) return false;
            }
            return true;
        }

        public static string PayloadIdentity(string dataRoot) =>
            Sha256(Path.Combine(dataRoot, RuntimeHashesFileName));

        public static bool ValidatePayloadIdentity(string dataRoot, string expectedIdentity, out string error)
        {
            try
            {
                if (!IsSha256(expectedIdentity) || !string.Equals(PayloadIdentity(dataRoot), expectedIdentity,
                        StringComparison.Ordinal))
                {
                    error = "JSBSim resident payload identity does not match the current APK.";
                    return false;
                }
                error = string.Empty;
                return true;
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            { error = StorageError(exception); return false; }
        }

        // Same-filesystem directory rename publishes only a fully verified tree.
        // Retain a corrupt resident tree for diagnosis; never fall back to an older APK.
        public static bool TryActivateCache(string staging, string destination, string expectedIdentity, out string error)
        {
            try
            {
                if (!ValidatePayloadIdentity(staging, expectedIdentity, out error) ||
                    !ValidateDataRoot(staging, out error)) return false;
                if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(staging)),
                        Path.GetDirectoryName(Path.GetFullPath(destination)), StringComparison.Ordinal))
                { error = "JSBSim cache activation requires staging on the destination filesystem."; return false; }
                if (Directory.Exists(destination))
                    Directory.Move(destination, destination + ".invalid-" + Guid.NewGuid().ToString("N"));
                Directory.Move(staging, destination);
                error = string.Empty;
                return true;
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            { error = StorageError(exception); return false; }
        }

        private static string CacheRoot(string identity) => Path.Combine(
            Application.persistentDataPath, PackageName, DataFolderName + "-" + identity);

        private static bool IsStorageFailure(Exception exception) => exception is IOException ||
            exception is UnauthorizedAccessException || exception is System.Security.SecurityException ||
            exception is ArgumentException || exception is NotSupportedException;

        private static string StorageError(Exception exception) =>
            "JSBSim runtime data storage failed (" + exception.GetType().Name +
            "). Check available storage and app data access, then restart to retry.";

        private static string HashBytes(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static string SafeDestination(string root, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) || relativePath.Contains(":") ||
                relativePath.StartsWith("/", StringComparison.Ordinal) || relativePath.StartsWith("\\", StringComparison.Ordinal))
                return null;
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
