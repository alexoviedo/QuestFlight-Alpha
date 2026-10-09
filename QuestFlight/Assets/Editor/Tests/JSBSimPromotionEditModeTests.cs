#if UNITY_EDITOR
// Keep this fixture under Assets/Editor so Unity's implicit Editor test assembly discovers it.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using AlexOviedo.QuestFlight.JSBSim;
using AlexOviedo.QuestFlight.JSBSim.Editor;
using NUnit.Framework;
using QuestFlightLab.Flight.Backends;
using UnityEditor;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class JSBSimPromotionEditModeTests
    {
        private static readonly string[] ExpectedBinaryPaths =
        {
            "Runtime/Plugins/Windows/x86_64/JSBSim.dll",
            "Runtime/Plugins/Windows/x86_64/qfl_jsbsim_native.dll",
            "Runtime/Plugins/Android/arm64-v8a/libJSBSim.so",
            "Runtime/Plugins/Android/arm64-v8a/libqfl_jsbsim_native.so"
        };
        [Serializable] private sealed class BinaryRecord { public string path; public long bytes; public string sha256; }
        [Serializable] private sealed class PackageRecord { public BinaryRecord[] binaries; }

        private static bool BinaryMatches(string path, BinaryRecord record) =>
            File.Exists(path) && new FileInfo(path).Length == record.bytes &&
            string.Equals(Sha256(path), record.sha256, StringComparison.Ordinal);

        [Test]
        public void ProductCoordinatorDefaultsToNativeJsbsim()
        {
            UnityEngine.GameObject target = new UnityEngine.GameObject("NativeCoordinator");
            try
            {
                Assert.That(target.AddComponent<FlightDynamicsCoordinator>().requestedBackend,
                    Is.EqualTo(FlightDynamicsBackendKind.JSBSimNativeDevelopment));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void PackageRuntimeDataAndPromotedBinariesMatchPinnedManifest()
        {
            Assert.That(JSBSimRuntimeData.ValidateDataRoot(JSBSimRuntimeData.EditorPackageDataRoot,
                out string dataError), Is.True, dataError);
            string packageRoot = Path.GetFullPath(Path.Combine(
                JSBSimRuntimeData.EditorPackageDataRoot,
                "..",
                ".."));
            string sourceManifest = Path.Combine(packageRoot, "SOURCE_MANIFEST.json");
            Assert.That(File.Exists(sourceManifest), Is.True);
            string manifestText = File.ReadAllText(sourceManifest);
            Assert.That(manifestText, Does.Contain(JSBSimNativeSession.PinnedRevision));
            Assert.That(manifestText, Does.Contain("GNU LGPL-2.1-or-later"));
            Assert.That(File.Exists(Path.Combine(JSBSimRuntimeData.EditorPackageDataRoot, "COPYING.txt")), Is.True);

            PackageRecord manifest = UnityEngine.JsonUtility.FromJson<PackageRecord>(manifestText);
            Assert.That(manifest.binaries, Has.Length.EqualTo(4));
            CollectionAssert.AreEquivalent(ExpectedBinaryPaths, manifest.binaries.Select(record => record.path));
            foreach (BinaryRecord record in manifest.binaries)
            {
                string path = Path.Combine(packageRoot, record.path.Replace('/', Path.DirectorySeparatorChar));
                Assert.That(BinaryMatches(path, record), Is.True, record.path);
            }

        }

        [Test]
        public void BinaryIdentityRejectsModifiedContentAndIncorrectLength()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
                var record = new BinaryRecord { bytes = 4, sha256 = Sha256(path) };
                Assert.That(BinaryMatches(path, record), Is.True);
                File.WriteAllBytes(path, new byte[] { 1, 2, 3, 5 });
                Assert.That(BinaryMatches(path, record), Is.False);
                record.sha256 = Sha256(path); record.bytes = 3;
                Assert.That(BinaryMatches(path, record), Is.False);
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void NativePluginImportersAreStrictlyPlatformAndArchitectureScoped()
        {
            JSBSimPluginImporterConfigurator.ConfigureAndValidate();
            foreach (string path in JSBSimPluginImporterConfigurator.AndroidPlugins)
            {
                PluginImporter importer = AssetImporter.GetAtPath(path) as PluginImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.GetCompatibleWithAnyPlatform(), Is.False, path);
                Assert.That(importer.GetCompatibleWithEditor(), Is.False, path);
                Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64), Is.False, path);
                Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.Android), Is.True, path);
                Assert.That(importer.GetPlatformData(BuildTarget.Android, "CPU"), Is.EqualTo("ARM64"), path);
            }
            foreach (string path in JSBSimPluginImporterConfigurator.WindowsPlugins)
            {
                PluginImporter importer = AssetImporter.GetAtPath(path) as PluginImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.GetCompatibleWithAnyPlatform(), Is.False, path);
                Assert.That(importer.GetCompatibleWithEditor(), Is.True, path);
                Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64), Is.True, path);
                Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.Android), Is.False, path);
                Assert.That(importer.GetEditorData("OS"), Is.EqualTo("Windows"), path);
                Assert.That(importer.GetEditorData("CPU"), Is.EqualTo("x86_64"), path);
                Assert.That(importer.GetPlatformData(BuildTarget.StandaloneWindows64, "CPU"),
                    Is.EqualTo("x86_64"), path);
            }
        }

        [Test]
        public void NativePackageCanProbeResetAndAdvanceFiniteC172State()
        {
            Assert.That(JSBSimNativeSession.TryProbe(out string probeError), Is.True, probeError);
            using (JSBSimNativeSession session = new JSBSimNativeSession())
            {
                Assert.That(session.Initialize(JSBSimRuntimeData.EditorPackageDataRoot), Is.True, session.LastError);
                Assert.That(session.Reset(new JSBSimNativeInitialConditions
                {
                    latitudeDegrees = 40.03936527,
                    longitudeDegrees = -105.22608958,
                    altitudeMslFeet = (1611.7824 + 1.25) / 0.3048,
                    terrainElevationMslFeet = 1611.7824 / 0.3048,
                    headingDegrees = FlightDynamicsInitialConditions.KbduRunwayTrueHeadingDegrees,
                    engineRunning = 1
                }), Is.True, session.LastError);
                session.SetControls(new JSBSimNativeControls
                {
                    throttle = 0.2,
                    mixture = 1.0
                });
                for (int step = 0; step < 120; step++)
                    Assert.That(session.Advance(1.0 / 120.0), Is.True, session.LastError);
                Assert.That(session.State.IsFinite, Is.True);
                Assert.That(session.State.simulationTimeSeconds, Is.EqualTo(1.0).Within(0.01));
            }
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
    }
}
#endif
