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
                    "9f80c6af748ede9c0addff022ce78337dfa2930046e3a8cfe155cc610b942bc7",
                ["Runtime/Plugins/Windows/x86_64/qfl_jsbsim_native.dll"] =
                    "5ee4ccd81cc296a946c843cacf0ea5ab0d132c78c3dba3c9056eeddb612c83d4",
                ["Runtime/Plugins/Android/arm64-v8a/libJSBSim.so"] =
                    "38c2fe56ba86568763caab81d62a4e3096d4b8fd2222d19ee824934fbc4af8a1",
                ["Runtime/Plugins/Android/arm64-v8a/libqfl_jsbsim_native.so"] =
                    "bf5f10df987a2411e0889b307eca4cd7fe08feef3c1427bfc1da7ce098162bb5"
            };

        [Test]
        public void ReleasePolicyAlwaysResolvesToUnityPrototype()
        {
            foreach (FlightDynamicsBackendKind request in Enum.GetValues(typeof(FlightDynamicsBackendKind)))
            {
                Assert.That(FlightDynamicsCoordinator.ResolveEffectiveBackend(request, false, false),
                    Is.EqualTo(FlightDynamicsBackendKind.UnityPrototype));
                Assert.That(FlightDynamicsCoordinator.ResolveEffectiveBackend(request, true, false),
                    Is.EqualTo(FlightDynamicsBackendKind.UnityPrototype));
            }
        }

        [Test]
        public void EditorPolicyUsesNativeOnlyWhenDevelopmentPreferenceAllowsIt()
        {
            Assert.That(FlightDynamicsCoordinator.ResolveEffectiveBackend(
                    FlightDynamicsBackendKind.UnityPrototype, true, true),
                Is.EqualTo(FlightDynamicsBackendKind.JSBSimNativeDevelopment));
            Assert.That(FlightDynamicsCoordinator.ResolveEffectiveBackend(
                    FlightDynamicsBackendKind.UnityPrototype, false, true),
                Is.EqualTo(FlightDynamicsBackendKind.UnityPrototype));
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
