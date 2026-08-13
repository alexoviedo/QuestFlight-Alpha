#if UNITY_EDITOR
// Keep this fixture under Assets/Editor so Unity's implicit Editor test assembly discovers it.
using System;
using System.Collections.Generic;
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
        private static readonly IReadOnlyDictionary<string, string> ExpectedBinaryHashes =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Runtime/Plugins/Windows/x86_64/JSBSim.dll"] =
                    "eeb8852bf7aabb33ab2f4527a3a48cc10b952a248e308804803399cc75de5e85",
                ["Runtime/Plugins/Windows/x86_64/qfl_jsbsim_native.dll"] =
                    "78bea2ff25aae003e8f866cd32c9b99fb0346ab29dc25a9e368c3465744f8c5b",
                ["Runtime/Plugins/Android/arm64-v8a/libJSBSim.so"] =
                    "1f15983e3fcff3a8dfa44180c5beb6ea7b490cb88d831214ed858babc81cac83",
                ["Runtime/Plugins/Android/arm64-v8a/libqfl_jsbsim_native.so"] =
                    "432ce6b14b0c3d9b5fc36ae3362937ca9dd3687d59830d71c3da7c84a31f1f6c"
            };

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

            foreach (KeyValuePair<string, string> expected in ExpectedBinaryHashes)
            {
                string path = Path.Combine(packageRoot, expected.Key.Replace('/', Path.DirectorySeparatorChar));
                Assert.That(File.Exists(path), Is.True, expected.Key);
                Assert.That(Sha256(path), Is.EqualTo(expected.Value), expected.Key);
                Assert.That(manifestText, Does.Contain(expected.Value), expected.Key);
            }
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
