using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor
{
    /// <summary>Opens every owned bundle and asset before activating an ignored build cache.</summary>
    public static class AuthoringBundleVerifier
    {
        [Serializable] public sealed class AssetEvidence
        {
            public string name, format;
            public int width, height, mipmap_count, vertex_count;
            public long index_count;
        }
        [Serializable] public sealed class BundleEvidence
        {
            public string name, sha256;
            public long bytes;
            public uint crc;
            public AssetEvidence[] assets;
        }
        [Serializable] public sealed class CompilerEvidence { public string path, sha256; }
        [Serializable] public sealed class Report
        {
            public string schema = "questflight.bundle-verification.v1";
            public string build_target, unity_version, manifest_sha256;
            public string operation = "build_and_verify";
            public bool verified;
            public CompilerEvidence[] compiler_inputs;
            public BundleEvidence[] bundles;
        }
        public static string Sha256(string path)
        {
            using var hash = SHA256.Create();
            using var input = File.OpenRead(path);
            return string.Concat(hash.ComputeHash(input).Select(value => value.ToString("x2")));
        }

        public static BundleEvidence[] Verify(string directory, AssetBundleBuild[] expected,
            string ownedPrefix, bool textures, Dictionary<string, Vector2Int> dimensions = null)
        {
            if (expected == null || expected.Length == 0 || expected.Any(build =>
                string.IsNullOrEmpty(build.assetBundleName) || Path.GetFileName(build.assetBundleName) != build.assetBundleName ||
                !build.assetBundleName.StartsWith(ownedPrefix, StringComparison.Ordinal)) ||
                expected.Select(build => build.assetBundleName).Distinct().Count() != expected.Length)
                throw new InvalidOperationException("Invalid exact bundle specification.");
            var names = new HashSet<string>(expected.Select(build => build.assetBundleName), StringComparer.Ordinal);
            var actual = Directory.EnumerateFiles(directory).Select(Path.GetFileName)
                .Where(name => name.StartsWith(ownedPrefix, StringComparison.Ordinal) &&
                    !name.EndsWith(".manifest", StringComparison.Ordinal) && !name.EndsWith(".meta", StringComparison.Ordinal));
            if (!names.SetEquals(actual)) throw new InvalidOperationException("Incomplete or unexpected binary bundle set.");
            var result = new List<BundleEvidence>();
            foreach (var build in expected.OrderBy(value => value.assetBundleName, StringComparer.Ordinal))
            {
                string path = Path.Combine(directory, build.assetBundleName);
                if (new FileInfo(path).Length <= 32 || !BuildPipeline.GetCRCForAssetBundle(path, out uint crc))
                    throw new InvalidOperationException("Invalid binary bundle: " + build.assetBundleName);
                var bundle = AssetBundle.LoadFromFile(path, crc);
                if (bundle == null) throw new InvalidOperationException("Unable to load binary bundle: " + build.assetBundleName);
                try
                {
                    var addresses = (build.addressableNames ?? build.assetNames).Select(value => value.ToLowerInvariant()).ToArray();
                    if (addresses.Length == 0 || addresses.Distinct().Count() != addresses.Length ||
                        !new HashSet<string>(addresses).SetEquals(bundle.GetAllAssetNames()))
                        throw new InvalidOperationException("Binary bundle membership differs: " + build.assetBundleName);
                    var assets = new List<AssetEvidence>();
                    foreach (string address in addresses.OrderBy(value => value, StringComparer.Ordinal))
                    {
                        if (textures)
                        {
                            var asset = bundle.LoadAsset<Texture2D>(address);
                            if (asset == null || asset.format != TextureFormat.ASTC_10x10 ||
                                dimensions == null || !dimensions.TryGetValue(address, out Vector2Int size) ||
                                asset.width != size.x || asset.height != size.y ||
                                asset.mipmapCount != ExpectedMipCount(size.x,size.y))
                                throw new InvalidOperationException("Compiled aerial texture differs: " + address);
                            assets.Add(new AssetEvidence { name=address, width=asset.width, height=asset.height,
                                format=asset.format.ToString(), mipmap_count=asset.mipmapCount });
                        }
                        else
                        {
                            var mesh = bundle.LoadAsset<Mesh>(address);
                            if (mesh == null || mesh.subMeshCount != 1)
                                throw new InvalidOperationException("Compiled building mesh missing or invalid: " + address);
                            assets.Add(new AssetEvidence { name=address, vertex_count=mesh.vertexCount,
                                index_count=(long)mesh.GetIndexCount(0), format="Mesh" });
                        }
                    }
                    result.Add(new BundleEvidence { name=build.assetBundleName, bytes=new FileInfo(path).Length,
                        sha256=Sha256(path), crc=crc, assets=assets.ToArray() });
                }
                finally { bundle.Unload(true); }
            }
            return result.ToArray();
        }

        static int ExpectedMipCount(int width,int height)
        {
            int remaining=Math.Max(width,height), count=0;
            while (remaining>0) { count++; remaining>>=1; }
            return count;
        }

        public static Report WriteReport(string directory, BundleEvidence[] bundles, BuildTarget target,
            string manifestPath, params string[] compilerPaths)
        {
            var report = new Report { build_target=target.ToString(), unity_version=Application.unityVersion,
                verified=true, manifest_sha256=Sha256(manifestPath), bundles=bundles,
                compiler_inputs=compilerPaths.Select(path => new CompilerEvidence { path=path.Replace("../", ""), sha256=Sha256(path) }).ToArray() };
            File.WriteAllText(Path.Combine(directory,"bundle_verification.json"), JsonUtility.ToJson(report,true)+"\n");
            return report;
        }

        // Staging/backups live outside StreamingAssets. Failed or prior bundles
        // cannot accidentally enter the APK. No recursive deletion is performed.
        public static void Activate(string staging, string destination, string stagingParent)
        {
            staging=Path.GetFullPath(staging); destination=Path.GetFullPath(destination);
            stagingParent=Path.GetFullPath(stagingParent);
            if (Path.GetDirectoryName(staging) != stagingParent ||
                !Path.GetFileName(staging).StartsWith(Path.GetFileName(destination)+".staging-",StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected generated cache activation paths.");
            string backup=Path.Combine(stagingParent,Path.GetFileName(destination)+".previous-"+Guid.NewGuid().ToString("N"));
            bool moved=Directory.Exists(destination);
            if (moved) Directory.Move(destination,backup);
            try { Directory.Move(staging,destination); }
            catch { if (moved) Directory.Move(backup,destination); throw; }
        }
    }
}
