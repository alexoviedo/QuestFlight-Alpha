#if UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;
using AlexOviedo.QuestFlight.JSBSim;
using NUnit.Framework;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight;
using QuestFlightLab.Environment;
using QuestFlightLab.Runtime;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class SoaringNativeDefinitionTests
    {
        [TestCase("C172")]
        [TestCase("SGS233A_Reference")]
        [TestCase("PA18_Tug_Reference")]
        public void AuthoredCapabilitiesMatchPackagedModel(string name)
        {
            var d=AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(FlightAircraftDefinition.DefinitionFolder+"/"+name+".asset");
            Assert.That(d,Is.Not.Null);Assert.That(d.IsValid(out string error),Is.True,error);
            using(var s=new JSBSimNativeSession())
            {
                Assert.That(s.Initialize(JSBSimRuntimeData.EditorPackageDataRoot,d.nativeConfiguration),Is.True,s.LastError);
                Assert.That(d.Matches(s.Capabilities,out error),Is.True,error);
            }
        }

        [Test]
        public void EngineFreeSessionRejectsEngineAndResetsAirbrakeWithoutFuelLeakage()
        {
            var d=AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(FlightAircraftDefinition.DefinitionFolder+"/SGS233A_Reference.asset");
            using(var s=new JSBSimNativeSession())
            {
                Assert.That(s.Initialize(JSBSimRuntimeData.EditorPackageDataRoot,d.nativeConfiguration),Is.True,s.LastError);
                var initial=new JSBSimNativeInitialConditions { latitudeDegrees=40.03936527,longitudeDegrees=-105.22608958,
                    altitudeMslFeet=7288,terrainElevationMslFeet=5288,calibratedAirspeedKnots=50,flightPathAngleDegrees=-3,headingDegrees=90 };
                initial.engineRunning=1;Assert.That(s.Reset(initial),Is.False);
                initial.engineRunning=0;Assert.That(s.Reset(initial),Is.True,s.LastError);
                Assert.That(s.SetGliderControls(new JSBSimNativeGliderControls {airbrake=1}),Is.True,s.LastError);
                for(int i=0;i<150;i++)Assert.That(s.Advance(1d/120),Is.True,s.LastError);
                Assert.That(s.CaptureAirData(),Is.True);Assert.That(s.AirData.airbrakePosition,Is.GreaterThan(.9));
                Assert.That(s.Reset(initial),Is.True,s.LastError);Assert.That(s.CaptureAirData(),Is.True);
                Assert.That(s.AirData.airbrakePosition,Is.Zero.Within(1e-8));
                Assert.That(s.Diagnostics.weightPounds,Is.EqualTo(950).Within(.001));
                Assert.That(s.Diagnostics.leftFuelPounds+s.Diagnostics.rightFuelPounds,Is.Zero);
                s.SetControls(new JSBSimNativeControls {throttle=1,mixture=1});Assert.That(s.Advance(1d/120),Is.False);
                Assert.That(s.LastError,Does.Contain("Engine-free"));
            }
        }

        [Test]
        public void ManagedAbiOffsetsMatchNativeContract()
        {
            Assert.That(Marshal.SizeOf<JSBSimNativeCapabilities>(),Is.EqualTo(32));
            Assert.That(Marshal.SizeOf<JSBSimNativeAttachmentState>(),Is.EqualTo(144));
            Assert.That(Marshal.SizeOf<JSBSimNativeTerrainPatch>(),Is.EqualTo(40));
            Assert.That(Marshal.SizeOf<JSBSimNativeStationLoad>(),Is.EqualTo(16));
            Assert.That(Marshal.OffsetOf<JSBSimNativeStationLoad>("pounds").ToInt32(),Is.EqualTo(8));
        }

        [Test]
        public void GliderBackendUsesAuthoredTerrainAndPreservesPatchThroughFreshReset()
        {
            var definition=AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(FlightAircraftDefinition.DefinitionFolder+"/SGS233A_Reference.asset");
            var terrain=AssetDatabase.LoadAssetAtPath<ProductionTerrainSurfaceAsset>("Assets/Production/Environment/ReconstructionV2/Generated/KBDU_ProductionTerrainSurfaceV2.asset");
            var root=new GameObject("SoaringNativeDefinitionTest");
            try
            {
                using(var backend=new JSBSimNativeDevelopmentBackend(JSBSimRuntimeData.EditorPackageDataRoot))
                {
                    Assert.That(backend.Initialize(new FlightDynamicsBackendContext {aircraftDefinition=definition,
                        terrainSurface=terrain,simulationRoot=root.transform,aircraftState=root.AddComponent<AircraftState>()}),Is.True,backend.LastError);
                    var ic=FlightDynamicsInitialConditions.KbduRunway();ic.engineRunning=false;ic.altitudeMslMeters+=200;ic.calibratedAirspeedKnots=50;ic.flightPathAngleDegrees=-3;
                    for(int flight=0;flight<2;flight++)
                    {
                        Assert.That(backend.Reset(ic),Is.True,backend.LastError);
                        var input=new AircraftControlState();input.SetGliderNeutral();backend.SetControls(input);
                        for(int i=0;i<120;i++)Assert.That(backend.Advance(1d/120),Is.True,backend.LastError);
                        Assert.That(backend.CurrentState.IsFinite,Is.True);
                    }
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
#endif
