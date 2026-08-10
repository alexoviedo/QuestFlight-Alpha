using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace AlexOviedo.QuestFlight.JSBSim.Tests
{
    public sealed class JSBSimPackageContractTests
    {
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
