using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Collections;
using System.Security.Cryptography;
using NUnit.Framework;

namespace AlexOviedo.QuestFlight.JSBSim.Tests
{
    public sealed class JSBSimPackageContractTests
    {
        [Test]
        public void FreshResetMatchesFreshNativeSessionAcrossDirtyFlightsAndRelocationPreservesFuel()
        {
#if UNITY_EDITOR_WIN
            using (var dirty = new JSBSimNativeSession())
            using (var fresh = new JSBSimNativeSession())
            {
                Assert.That(dirty.Initialize(JSBSimRuntimeData.EditorPackageDataRoot), Is.True, dirty.LastError);
                Assert.That(fresh.Initialize(JSBSimRuntimeData.EditorPackageDataRoot), Is.True, fresh.LastError);
                var mass = JSBSimNativeSession.DefaultMass; mass.leftFuelPounds = 80; mass.rightFuelPounds = 90;
                Assert.That(dirty.SetMassConfiguration(mass), Is.True);
                Assert.That(fresh.SetMassConfiguration(mass), Is.True);
                // Repeat the same scenarios in three orders, including reverse order.
                // Each comparison includes dirty histories and the first 120 native steps.
                foreach (double[] order in new[]
                {
                    new[] { 90d, 270d, 45d },
                    new[] { 45d, 270d, 90d },
                    new[] { 270d, 90d, 45d }
                })
                foreach (double heading in order)
                {
                    var initial = new JSBSimNativeInitialConditions
                    {
                        latitudeDegrees = 40.03936527, longitudeDegrees = -105.22608958,
                        altitudeMslFeet = 7288, terrainElevationMslFeet = 5288,
                        calibratedAirspeedKnots = 90, headingDegrees = heading, engineRunning = 1
                    };
                    Assert.That(dirty.Reset(initial), Is.True, dirty.LastError);
                    dirty.SetControls(new JSBSimNativeControls { throttle = .8, mixture = 1, flaps = 1, elevatorTrimNoseUp = .2 });
                    for (int step = 0; step < 1200; step++) Assert.That(dirty.Advance(1d / 120), Is.True, dirty.LastError);
                    double fuelBefore = dirty.Diagnostics.leftFuelPounds + dirty.Diagnostics.rightFuelPounds;
                    Assert.That(fuelBefore, Is.LessThan(170));
                    Assert.That(dirty.Diagnostics.flapPosition, Is.GreaterThan(0));
                    Assert.That(dirty.Relocate(initial), Is.True, dirty.LastError);
                    Assert.That(dirty.Diagnostics.leftFuelPounds + dirty.Diagnostics.rightFuelPounds, Is.EqualTo(fuelBefore).Within(.001));
                    Assert.That(dirty.Diagnostics.flapPosition, Is.GreaterThan(0), "Recovery retains actual flap actuator state.");
                    Assert.That(dirty.Reset(initial), Is.True, dirty.LastError);
                    Assert.That(fresh.Reset(initial), Is.True, fresh.LastError);
                    AssertNativeValuesAgree(dirty.State, fresh.State);
                    AssertNativeValuesAgree(dirty.Diagnostics, fresh.Diagnostics);
                    Assert.That(dirty.Diagnostics.leftFuelPounds, Is.EqualTo(80).Within(.001));
                    Assert.That(dirty.Diagnostics.flapPosition, Is.Zero.Within(.000001));
                    for (int step = 0; step < 120; step++)
                    {
                        Assert.That(dirty.Advance(1d / 120), Is.True, dirty.LastError);
                        Assert.That(fresh.Advance(1d / 120), Is.True, fresh.LastError);
                        AssertNativeValuesAgree(dirty.State, fresh.State);
                        AssertNativeValuesAgree(dirty.Diagnostics, fresh.Diagnostics);
                    }
                }
            }
#else
            Assert.Ignore("Real Windows native scenario; Android exact-build acceptance is separate.");
#endif
        }

        private static void AssertNativeValuesAgree<T>(T left, T right)
        {
            foreach (var field in typeof(T).GetFields())
                if (field.FieldType == typeof(double))
                    Assert.That((double)field.GetValue(left), Is.EqualTo((double)field.GetValue(right)).Within(1e-7), field.Name);
                else Assert.That(field.GetValue(left), Is.EqualTo(field.GetValue(right)), field.Name);
        }

        [Test]
        public void CurrentApkIdentityRejectsAnInternallyValidPriorPayloadAndActivatesOnlyCompleteData()
        {
            string root = Path.Combine(Path.GetTempPath(), "qfl-payload-" + Guid.NewGuid());
            try
            {
                string a = Path.Combine(root, "a"), b = Path.Combine(root, "b.extracting");
                WriteFixture(a, "model A"); WriteFixture(b, "model B");
                string bIdentity = JSBSimRuntimeData.PayloadIdentity(b);
                Assert.That(JSBSimRuntimeData.ValidateDataRoot(a, out string error), Is.True, error);
                Assert.That(JSBSimRuntimeData.ValidatePayloadIdentity(a, bIdentity, out error), Is.False);
                Assert.That(error, Does.Contain("current APK"));
                Assert.That(JSBSimRuntimeData.TryActivateCache(b, Path.Combine(root, "b"), bIdentity, out error), Is.True, error);
                Assert.That(File.ReadAllText(Path.Combine(root, "b", "model.xml")), Is.EqualTo("model B"));
                Assert.That(Directory.Exists(a), Is.True, "Prior payload is preserved but cannot be selected as B.");
                Assert.That(JSBSimRuntimeData.ValidatePayloadIdentity(Path.Combine(root, "b"), bIdentity, out error), Is.True, error);
                // An interrupted staging tree never replaces the active cache.
                WriteFixture(b, "model B"); File.Delete(Path.Combine(b, "model.xml"));
                Assert.That(JSBSimRuntimeData.TryActivateCache(b, Path.Combine(root, "b"), bIdentity, out error), Is.False);
                Assert.That(JSBSimRuntimeData.ValidateDataRoot(Path.Combine(root, "b"), out error), Is.True, error);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [TestCase("runtime_manifest.txt")]
        [TestCase("runtime_hashes.sha256")]
        [TestCase("model.xml")]
        public void StorageReadFailureReturnsActionableError(string file)
        {
            string root = Path.Combine(Path.GetTempPath(), "qfl-storage-" + Guid.NewGuid());
            try
            {
                WriteFixture(root, "model");
                using (File.Open(Path.Combine(root, file), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Assert.That(JSBSimRuntimeData.ValidateDataRoot(root, out string error), Is.False);
                    Assert.That(error, Does.Contain("storage failed"));
                    Assert.That(error, Does.Contain("restart to retry"));
                }
                Assert.That(JSBSimRuntimeData.ValidateDataRoot(root, out string retryError), Is.True, retryError);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void ActivationFailureLeavesPriorDataAndNeverReturnsAnActivePartialRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "qfl-activation-" + Guid.NewGuid());
            try
            {
                string stage = Path.Combine(root, "stage"), destination = Path.Combine(root, "destination");
                WriteFixture(stage, "model"); File.WriteAllText(destination, "existing file");
                Assert.That(JSBSimRuntimeData.TryActivateCache(stage, destination, JSBSimRuntimeData.PayloadIdentity(stage), out string error), Is.False);
                Assert.That(error, Does.Contain("storage failed"));
                Assert.That(File.ReadAllText(destination), Is.EqualTo("existing file"));
                Assert.That(Directory.Exists(stage), Is.True);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void CorruptResidentIdentityAndUnsafeManifestCannotActivate()
        {
            string root = Path.Combine(Path.GetTempPath(), "qfl-corrupt-" + Guid.NewGuid());
            try
            {
                WriteFixture(root, "model");
                string identity = JSBSimRuntimeData.PayloadIdentity(root);
                File.AppendAllText(Path.Combine(root, "runtime_hashes.sha256"), "corrupt");
                Assert.That(JSBSimRuntimeData.ValidatePayloadIdentity(root, identity, out _), Is.False);
                File.WriteAllText(Path.Combine(root, "runtime_manifest.txt"), "../outside\nSOURCE_REVISION.txt\nruntime_hashes.sha256\n");
                Assert.That(JSBSimRuntimeData.ValidateDataRoot(root, out string error), Is.False);
                Assert.That(error, Does.Contain("Unsafe path"));
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Test]
        public void UnchangedVerifiedPayloadPreparationCompletesExactlyOnce()
        {
            int calls = 0;
            IEnumerator prepare = JSBSimRuntimeData.Prepare((success, root, error) =>
            {
                calls++;
                Assert.That(success, Is.True, error);
                Assert.That(root, Is.EqualTo(JSBSimRuntimeData.EditorPackageDataRoot));
                Assert.That(JSBSimRuntimeData.PreparedPayloadIdentity, Is.EqualTo(JSBSimRuntimeData.PayloadIdentity(root)));
            });
            while (prepare.MoveNext()) { }
            Assert.That(calls, Is.EqualTo(1));
        }

        private static void WriteFixture(string root, string model)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "SOURCE_REVISION.txt"), JSBSimNativeSession.PinnedRevision);
            File.WriteAllText(Path.Combine(root, "model.xml"), model);
            File.WriteAllText(Path.Combine(root, "runtime_manifest.txt"), "SOURCE_REVISION.txt\nmodel.xml\nruntime_hashes.sha256\n");
            string hashes = string.Empty;
            foreach (string file in new[] { "SOURCE_REVISION.txt", "model.xml", "runtime_manifest.txt" })
                using (var sha = SHA256.Create())
                    hashes += BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(root, file)))).Replace("-", "").ToLowerInvariant() + "  " + file + "\n";
            File.WriteAllText(Path.Combine(root, "runtime_hashes.sha256"), hashes);
        }

        [Test]
        public void RuntimePayloadMatchesPinnedHashes()
        {
            Assert.That(JSBSimRuntimeData.ValidateDataRoot(JSBSimRuntimeData.EditorPackageDataRoot,
                out string error), Is.True, error);
        }

        [Test]
        public void AlteredRuntimePayloadIsRejected()
        {
            string temporary = Path.Combine(Path.GetTempPath(), "qfl-jsbsim-hash-test-" + Guid.NewGuid());
            try
            {
                CopyDirectory(JSBSimRuntimeData.EditorPackageDataRoot, temporary);
                File.AppendAllText(Path.Combine(temporary, "SOURCE_REVISION.txt"), "tampered");
                Assert.That(JSBSimRuntimeData.ValidateDataRoot(temporary, out string error), Is.False);
                Assert.That(error, Does.Contain("hash mismatch"));
            }
            finally
            {
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
            }
        }

        [Test]
        public void C172PPrivateLoadStationsAreExplicitSymmetricAndConfigurable()
        {
            XDocument document = LoadAircraftDocument();
            XElement massBalance = document.Root.Element("mass_balance");
            Assert.That(massBalance, Is.Not.Null);
            Assert.That(Number(massBalance.Element("emptywt")),
                Is.EqualTo(JSBSimNativeSession.PinnedEmptyWeightPounds));

            XElement[] pointMasses = massBalance.Elements("pointmass").ToArray();
            Assert.That(pointMasses.Select(element => (string)element.Attribute("name")),
                Is.EquivalentTo(new[] { "PILOT", "CO-PILOT", "REAR-LEFT", "REAR-RIGHT", "BAGGAGE" }));
            Assert.That(pointMasses.Sum(element => Number(element.Element("weight"))),
                Is.EqualTo(340.0));
            Assert.That(pointMasses.Sum(element => Number(element.Element("weight")) *
                Number(element.Element("location").Element("y"))), Is.EqualTo(0.0).Within(0.001));

            XElement[] tanks = document.Root.Element("propulsion").Elements("tank").ToArray();
            Assert.That(tanks, Has.Length.EqualTo(2));
            Assert.That(tanks.Sum(element => Number(element.Element("contents"))),
                Is.EqualTo(260.0));
            Assert.That(tanks.Sum(element => Number(element.Element("contents")) *
                Number(element.Element("location").Element("y"))), Is.EqualTo(0.0).Within(0.001));

            double total = Number(massBalance.Element("emptywt")) +
                pointMasses.Sum(element => Number(element.Element("weight"))) +
                tanks.Sum(element => Number(element.Element("contents")));
            Assert.That(total, Is.EqualTo(JSBSimNativeSession.PinnedInitialWeightPounds));
            Assert.That(total, Is.LessThan(2400.0));
        }

        [Test]
        public void ProductModelDeclaresNoFileOrSocketOutput()
        {
            XDocument document = LoadAircraftDocument();
            Assert.That(document.Root.Elements("output"), Is.Empty);
            Assert.That(File.Exists(Path.Combine(JSBSimRuntimeData.EditorPackageDataRoot,
                "aircraft", "c172x", "output.xml")), Is.False);
        }

        [Test]
        public void ManagedDiagnosticControlsReportClampedSubmittedCommands()
        {
            using (JSBSimNativeSession session = new JSBSimNativeSession())
            {
                session.SetControls(new JSBSimNativeControls
                {
                    aileron = 2.0,
                    elevatorNoseUp = -2.0,
                    rudderYawRight = 0.25,
                    throttle = 4.0,
                    mixture = -1.0,
                    flaps = 0.5
                });
                Assert.That(session.AppliedControls.aileron, Is.EqualTo(1.0));
                Assert.That(session.AppliedControls.elevatorNoseUp, Is.EqualTo(-1.0));
                Assert.That(session.AppliedControls.rudderYawRight, Is.EqualTo(0.25));
                Assert.That(session.AppliedControls.throttle, Is.EqualTo(1.0));
                Assert.That(session.AppliedControls.mixture, Is.EqualTo(0.0));
                Assert.That(session.AppliedControls.flaps, Is.EqualTo(0.5));
            }
        }

        [Test]
        public void NativeAbiVersionTwoProbesOnWindowsEditor()
        {
#if UNITY_EDITOR_WIN
            Assert.That(JSBSimNativeSession.TryProbe(out string error), Is.True, error);
#else
            Assert.Ignore("Native package probe is scoped to the Windows editor and Android ARM64 player.");
#endif
        }

        [Test]
        public void NativePinnedModelCanResetAndAdvanceFiniteState()
        {
#if UNITY_EDITOR_WIN
            using (JSBSimNativeSession session = new JSBSimNativeSession())
            {
                Assert.That(session.Initialize(JSBSimRuntimeData.EditorPackageDataRoot), Is.True,
                    session.LastError);
                Assert.That(session.SetMassConfiguration(JSBSimNativeSession.DefaultMass), Is.True,
                    session.LastError);
                JSBSimNativeInitialConditions initial = new JSBSimNativeInitialConditions
                {
                    latitudeDegrees = 40.03936527,
                    longitudeDegrees = -105.22608958,
                    altitudeMslFeet = 5293.0,
                    terrainElevationMslFeet = 5289.0,
                    headingDegrees = 80.0,
                    engineRunning = 1
                };
                Assert.That(session.Reset(initial), Is.True, session.LastError);
                session.SetControls(new JSBSimNativeControls
                {
                    throttle = 0.2,
                    mixture = 1.0
                });
                for (int step = 0; step < 120; step++)
                    Assert.That(session.Advance(1.0 / 120.0), Is.True, session.LastError);

                Assert.That(session.State.IsFinite, Is.True);
                Assert.That(session.State.simulationTimeSeconds, Is.EqualTo(1.0).Within(0.01));
                Assert.That(session.ConfiguredInitialConditions.terrainElevationMslFeet,
                    Is.EqualTo(initial.terrainElevationMslFeet));
                Assert.That(session.AppliedControls.throttle, Is.EqualTo(0.2));
                Assert.That(session.Diagnostics.weightPounds,
                    Is.EqualTo(JSBSimNativeSession.PinnedInitialWeightPounds).Within(1.0));
                Assert.That(session.Diagnostics.densitySlugsPerCubicFoot, Is.GreaterThan(0.0));
            }
#else
            Assert.Ignore("Native package execution is scoped to the Windows editor and Android ARM64 player.");
#endif
        }

        [Test]
        public void NativePinnedModelProducesAValidLongitudinalTrim()
        {
#if UNITY_EDITOR_WIN
            using (JSBSimNativeSession session = new JSBSimNativeSession())
            {
                Assert.That(session.Initialize(JSBSimRuntimeData.EditorPackageDataRoot), Is.True,
                    session.LastError);
                Assert.That(session.Reset(new JSBSimNativeInitialConditions
                {
                    latitudeDegrees = 40.03936527,
                    longitudeDegrees = -105.22608958,
                    altitudeMslFeet = 6288.0,
                    terrainElevationMslFeet = 5288.0,
                    calibratedAirspeedKnots = 90.0,
                    headingDegrees = 89.972,
                    engineRunning = 1
                }), Is.True, session.LastError);
                Assert.That(session.Trim(JSBSimNativeTrimRequest.StandardLongitudinal,
                    out JSBSimNativeTrimResult trim), Is.True, session.LastError);
                Assert.That(trim.success, Is.EqualTo(1));
                Assert.That(trim.throttle, Is.InRange(0.0, 1.0));
                Assert.That(trim.elevatorNoseUp, Is.InRange(-1.0, 1.0));
            }
#else
            Assert.Ignore("Native package execution is scoped to Windows editor and Android ARM64.");
#endif
        }

        private static XDocument LoadAircraftDocument()
        {
            return XDocument.Load(Path.Combine(JSBSimRuntimeData.EditorPackageDataRoot,
                "aircraft", JSBSimNativeSession.PinnedAircraftModel,
                JSBSimNativeSession.PinnedAircraftModel + ".xml"));
        }

        private static double Number(XElement element)
        {
            return double.Parse(element.Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (string directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
