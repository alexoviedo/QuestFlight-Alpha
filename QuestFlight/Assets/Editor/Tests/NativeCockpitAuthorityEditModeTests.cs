#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AlexOviedo.QuestFlight.JSBSim;
using NUnit.Framework;
using QuestFlight.Editor.FlightDynamicsValidation;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Aircraft.CockpitDynamics.SystemsV2;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class NativeCockpitAuthorityEditModeTests
    {
        [TestCase(40,5,7.125016)]
        [TestCase(10,0,0)] [TestCase(0,10,90)] [TestCase(-10,0,180)] [TestCase(0,-10,270)]
        [TestCase(10,10,45)] [TestCase(-10,10,135)] [TestCase(-10,-10,225)] [TestCase(10,-10,315)]
        [TestCase(40,-.1,359.85676)]
        public void TrackUsesTrueGroundVelocityAndFormatter(double north,double east,double expected)
        {
            var state = new FlightDynamicsState {nativeTelemetryVersion=1,headingDegrees=123,
                groundVelocityNedMetersPerSecond=new Vector3((float)north,(float)east,0)};
            Assert.That(CockpitSystemsAuthorityV2Model.TryGroundTrack(state,out float track),Is.True);
            Assert.That(track,Is.EqualTo(expected).Within(.001));
            var snapshot = new CockpitAvionicsV2Snapshot {isValid=true,avionicsBusPowered=true,
                availability=CockpitAvionicsV2Availability.ElectricalPower|CockpitAvionicsV2Availability.GpsNavigation,
                gpsGroundTrackAvailable=true,gpsGroundTrackDegrees=track};
            var formatter = new CockpitAvionicsV2Formatter();
            Assert.That(formatter.Format(snapshot),Is.True);
            Assert.That(new string(formatter.Characters).Substring(34,3),Is.EqualTo(((int)Math.Round(expected,MidpointRounding.AwayFromZero)%360).ToString("D3")));
        }

        [Test]
        public void TrackAndActuatorsFailUnavailableAndNonfiniteWithoutCommands()
        {
            var state = new FlightDynamicsState {headingDegrees=90};
            Assert.That(CockpitSystemsAuthorityV2Model.TryGroundTrack(state,out _),Is.False);
            state.nativeTelemetryVersion=1;
            state.groundVelocityNedMetersPerSecond=new Vector3(.1f,.1f,0);
            Assert.That(CockpitSystemsAuthorityV2Model.TryGroundTrack(state,out _),Is.False);
            state.actualFlapNormalized=double.NaN;
            Assert.That(state.IsFinite,Is.False);
            Assert.That(ProductionAircraftControlAnimator.NativeFlapNormalized(state),Is.Zero);
            var snapshot=new CockpitAvionicsV2Snapshot {isValid=true,avionicsBusPowered=true,
                availability=CockpitAvionicsV2Availability.ElectricalPower|CockpitAvionicsV2Availability.GpsNavigation};
            var formatter=new CockpitAvionicsV2Formatter();
            Assert.That(formatter.Format(snapshot),Is.True);
            Assert.That(new string(formatter.Characters).Substring(34,3),Is.EqualTo("---"));
        }

        [Test]
        public void NativeFlapClockDrivesCockpitAndBothAuthoredWingBindingsOnExtensionRetractionAndReset()
        {
            GameObject authored=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Production/Prefabs/ProductionC172AircraftRig.prefab");
            var source=authored.GetComponentInChildren<ProductionAircraftControlAnimator>(true);
            var serialized=new SerializedObject(source);var bindingArray=serialized.FindProperty("bindings");
            var root=new GameObject("NativeFlapAuthorityTest");
            try
            {
                var animator=root.AddComponent<ProductionAircraftControlAnimator>();
                var bindings=Enumerable.Range(0,bindingArray.arraySize).Select(i=>bindingArray.GetArrayElementAtIndex(i))
                    .Where(item=>item.FindPropertyRelative("channel").enumValueIndex==3)
                    .Select(item=>new ProductionAircraftControlAnimator.Binding {
                        target=new GameObject(item.FindPropertyRelative("label").stringValue).transform,
                        channel=ProductionAircraftControlAnimator.ControlChannel.Flaps,
                        localRotationAxis=item.FindPropertyRelative("localRotationAxis").vector3Value,
                        minimumAngleDegrees=item.FindPropertyRelative("minimumAngleDegrees").floatValue,
                        maximumAngleDegrees=item.FindPropertyRelative("maximumAngleDegrees").floatValue}).ToArray();
                Assert.That(bindings.Length,Is.EqualTo(2));
                foreach(var binding in bindings){binding.target.SetParent(root.transform);Assert.That(binding.maximumAngleDegrees,Is.EqualTo(-30));}
                animator.ConfigureAuthoredBindings(null,null,bindings);animator.CaptureAuthoredRestPosesForValidation();
                using(var backend=new NativeJsbsimTraceBackend())
                {
                    Assert.That(backend.IsAvailable,Is.True,backend.LastError);
                    var nativeSession=(JSBSimNativeSession)typeof(NativeJsbsimTraceBackend)
                        .GetField("_session",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(backend);
                    Assert.That(nativeSession,Is.Not.Null);
                    Assert.That(backend.Reset(FlightScenarioInitialState.Airborne(80,1000)),Is.True,backend.LastError);
                    var model=new CockpitSystemsAuthorityV2Model();
                    var scenario=new CockpitSystemsScenarioV2 {hasAuthoritativeConfiguration=true,scenarioUtcUnixSeconds=1780000000,
                        ignitionSwitchPosition=CockpitIgnitionSwitchPositionV2.Both,fuelSelectorPosition=CockpitFuelSelectorPositionV2.Both};
                    var input=new CockpitSystemsAuthorityInputV2 {hasAuthoritativeFlightState=true,flightState=backend.State};
                    Assert.That(model.Reset(scenario,input),Is.True);
                    for(int direction=0;direction<2;direction++)
                    {
                        float command=direction==0?1:0;
                        var controls=QuestFlightLab.Runtime.AircraftControlState.Neutral();controls.flaps=command;controls.throttle=.3f;
                        backend.SetControls(controls);
                        int[] ticks={0,120,240,360,480,1200};
                        double[] expected=direction==0?new double[]{0,5,10,20,30,30}:new double[]{30,20,10,5,0,0};
                        for(int tick=0;tick<=1200;tick++)
                        {
                            if(ticks.Contains(tick))
                            {
                                input.flightState=backend.State;input.flightControls.flaps=command;
                                Assert.That(model.Step(input),Is.True);
                                double actual=nativeSession.Diagnostics.flapPosition*30;
                                Assert.That(backend.State.actualFlapNormalized,
                                    Is.EqualTo(nativeSession.Diagnostics.flapPosition).Within(1e-12),
                                    "The presentation snapshot must carry the actual native diagnostic.");
                                Assert.That(actual,Is.EqualTo(expected[Array.IndexOf(ticks,tick)]).Within(.10));
                                Assert.That(model.Current.Has(CockpitSystemsAvailabilityV2.ActualFlapPosition),Is.True);
                                Assert.That(model.Current.actualFlapDegrees,Is.EqualTo(actual).Within(.001));
                                Assert.That(model.Current.Has(CockpitSystemsAvailabilityV2.FuelQuantity|CockpitSystemsAvailabilityV2.FuelFlow),Is.False);
                                animator.ApplyAuthorityStateForValidation(backend.State,input.flightControls);
                                foreach(var binding in bindings)Assert.That(Quaternion.Angle(Quaternion.identity,binding.target.localRotation),Is.EqualTo(actual).Within(.001));
                            }
                            if(tick<1200)Assert.That(backend.Step(1.0/120),Is.True,backend.LastError);
                        }
                    }
                    Assert.That(backend.Reset(FlightScenarioInitialState.Airborne()),Is.True);
                    input.flightState=backend.State;Assert.That(model.Reset(scenario,input),Is.True);
                    Assert.That(model.Current.actualFlapDegrees,Is.Zero);

                    // Start from a genuine clean native reset, then move its actuator.
                    // Reset the SAME cockpit model upward using the actual current
                    // native snapshot; an opposing command must not supply the angle.
                    var extend=QuestFlightLab.Runtime.AircraftControlState.Neutral();
                    extend.flaps=1;extend.throttle=.3f;backend.SetControls(extend);
                    for(int tick=0;tick<480;tick++)
                        Assert.That(backend.Step(1.0/120),Is.True,backend.LastError);
                    Assert.That(model.Current.actualFlapDegrees,Is.Zero,
                        "The model has not consumed the newly extended native state yet.");
                    input.flightState=backend.State;input.flightControls.flaps=0;
                    Assert.That(model.Reset(scenario,input),Is.True);
                    AssertResetPresentation(30);

                    // Reset the ACTUAL native session while deployed, then reset its
                    // subordinate presentation downward from30 to0. Keep a full-flap
                    // command to catch either presentation following commands instead.
                    Assert.That(backend.Reset(FlightScenarioInitialState.Airborne(80,1000)),Is.True,backend.LastError);
                    Assert.That(model.Current.actualFlapDegrees,Is.EqualTo(30).Within(.001),
                        "A backend reset alone must not fabricate a new cockpit sample.");
                    input.flightState=backend.State;input.flightControls.flaps=1;
                    Assert.That(model.Reset(scenario,input),Is.True);
                    AssertResetPresentation(0);

                    void AssertResetPresentation(double expectedDegrees)
                    {
                        double actual=nativeSession.Diagnostics.flapPosition*30;
                        Assert.That(double.IsNaN(actual)||double.IsInfinity(actual),Is.False);
                        Assert.That(actual,Is.EqualTo(expectedDegrees).Within(.001));
                        Assert.That(backend.State.HasNativeTelemetry&&backend.State.IsFinite,Is.True);
                        Assert.That(backend.State.actualFlapNormalized,
                            Is.EqualTo(nativeSession.Diagnostics.flapPosition).Within(1e-12));
                        Assert.That(model.Current.Has(CockpitSystemsAvailabilityV2.ActualFlapPosition),Is.True);
                        Assert.That(model.Current.actualFlapDegrees,Is.EqualTo(actual).Within(.001));
                        Assert.That(model.Current.Has(CockpitSystemsAvailabilityV2.FuelQuantity|CockpitSystemsAvailabilityV2.FuelFlow),Is.False);
                        animator.ApplyAuthorityStateForValidation(backend.State,input.flightControls);
                        foreach(var binding in bindings)
                        {
                            var expected=Quaternion.AngleAxis(-(float)actual,binding.localRotationAxis);
                            Assert.That(Quaternion.Angle(binding.target.localRotation,expected),Is.LessThan(.001),
                                binding.target.name+" must reset to the signed native angle on its serialized axis.");
                        }
                    }
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }

        [Test]
        public void CrosswindTraceExportsRawNativeBetaWithSeparateTrueGroundTrack()
        {
            using(var backend=new NativeJsbsimTraceBackend())
            {
                Assert.That(backend.SetWind(5,0,0),Is.True);
                Assert.That(backend.Reset(FlightScenarioInitialState.Airborne(75,600,90)),Is.True);
                var runner=new FlightDynamicsScenarioSuiteRunner();
                typeof(FlightDynamicsScenarioSuiteRunner).GetMethod("AddTrace",BindingFlags.NonPublic|BindingFlags.Instance)
                    .Invoke(runner,new object[]{backend,"perpendicular_wind",QuestFlightLab.Runtime.AircraftControlState.Neutral(),0.0,999.0});
                Assert.That(runner.Trace.Single().sideslipDegrees,Is.EqualTo(backend.State.sideslipDegrees).Within(1e-9));
                Assert.That(runner.Trace.Single().groundTrackAvailable,Is.True);
                Assert.That(Math.Abs(runner.Trace.Single().groundTrackDegrees-90),Is.GreaterThan(1));
                var matrix=(FlightScenarioEvidence)typeof(FlightDynamicsScenarioSuiteRunner)
                    .GetMethod("WindMatrix",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(runner,new object[]{backend});
                Assert.That(matrix.metrics.Single(metric=>metric.id=="initial_wind_lateral_component").value,Is.EqualTo(5));
                Assert.That(matrix.metrics.Single(metric=>metric.id=="crosswind_track_displacement").status,Is.EqualTo("Unproven"));
            }
        }

        [Test]
        public void ActualNativeIdentityAndScenarioDefinitionReactToIndependentInputMutations()
        {
            using(var backend=new NativeJsbsimTraceBackend())
            {
                Assert.That(backend.IsAvailable,Is.True);
                Assert.That(backend.Reset(FlightScenarioInitialState.Airborne(75)),Is.True);
                NativeFlightInputEvidence identity=NativeFlightEvidenceIdentity.Capture();
                Assert.That(identity.status,Is.EqualTo("Pass"),identity.detail);
                var before=backend.CaptureScenarioInputs("identity",1);
                Assert.That(backend.Reset(FlightScenarioInitialState.Airborne(76)),Is.True);
                Assert.That(backend.CaptureScenarioInputs("identity",1).definitionSha256,Is.Not.EqualTo(before.definitionSha256));
                string xml=Path.Combine(JSBSimRuntimeData.EditorPackageDataRoot,"aircraft/c172x/c172x.xml");
                string temporary=Path.GetTempFileName();
                try
                {
                    string original=File.ReadAllText(xml);string changed=original.Replace("<value>0.347</value>","<value>0.348</value>");
                    Assert.That(changed,Is.Not.EqualTo(original));File.WriteAllText(temporary,changed);
                    Assert.That(NativeFlightEvidenceIdentity.HashFile(temporary),Is.Not.EqualTo(identity.aircraftXmlSha256));
                }
                finally {File.Delete(temporary);}
            }
        }

        [TestCase(true, 0.1)]
        [TestCase(false, 0.0)]
        public void InitialTraceControlsMatchActualResetSubmissionBeforeScheduledControls(bool running, double initialThrottle)
        {
            using (var backend = new NativeJsbsimTraceBackend())
            {
                Assert.That(backend.IsAvailable, Is.True, backend.LastError);
                var runner = new FlightDynamicsScenarioSuiteRunner();
                Func<double, FlightDynamicsState, QuestFlightLab.Runtime.AircraftControlState> schedule = (time, state) =>
                    new QuestFlightLab.Runtime.AircraftControlState { throttle = .55f, mixture = 1f, rudder = .45f };
                typeof(FlightDynamicsScenarioSuiteRunner).GetMethod("Run", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(runner, new object[] { backend, "initial_controls", FlightScenarioInitialState.Airborne(engineRunning: running), .1, schedule, false });
                FlightTraceSample initial = runner.Trace.First();
                Assert.That(initial.timeSeconds, Is.Zero);
                Assert.That(initial.throttle, Is.EqualTo(initialThrottle).Within(1e-7));
                Assert.That(initial.rudder, Is.Zero);
                Assert.That(initial.aileron, Is.Zero);
                Assert.That(initial.elevator, Is.Zero);
                Assert.That(initial.flaps, Is.Zero);
                Assert.That(runner.Trace.Last().throttle, Is.EqualTo(.55f));
                Assert.That(runner.Trace.Last().rudder, Is.EqualTo(.45f));
            }
        }

        [Test]
        public void BetaYawMetadataDescribesNativeBetaAndRetainsUnmetInternalYawBound()
        {
            using (var backend = new NativeJsbsimTraceBackend())
            {
                Assert.That(backend.IsAvailable, Is.True, backend.LastError);
                var runner = new FlightDynamicsScenarioSuiteRunner();
                var evidence = (FlightScenarioEvidence)typeof(FlightDynamicsScenarioSuiteRunner)
                    .GetMethod("BetaYawDamping", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(runner, new object[] { backend });
                FlightEvidenceMetric beta = evidence.metrics.Single(x => x.id == "sideslip_residual_ratio");
                Assert.That(beta.detail, Does.Contain("native aero/beta-deg"));
                Assert.That(beta.detail, Does.Contain("internal engineering criterion"));
                Assert.That(beta.value, Is.EqualTo(.2521028930282049).Within(1e-6));
                FlightEvidenceMetric yaw = evidence.metrics.Single(x => x.id == "yaw_rate_residual_ratio");
                Assert.That(yaw.maximum, Is.EqualTo(.55));
                Assert.That(yaw.value, Is.EqualTo(.7886204146862208).Within(1e-6));
                Assert.That(yaw.status, Is.EqualTo("Fail"));
                Assert.That(evidence.status, Is.EqualTo("Fail"));
            }
        }
    }
}
#endif
