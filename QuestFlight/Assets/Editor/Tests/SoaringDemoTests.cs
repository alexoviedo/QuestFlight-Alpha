#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class SoaringDemoTests
    {
        [Test] public void GuidanceCarriesBothGliderBrakesAndLegacyGuidancePreservesManualLever()
        {
            var source=new AircraftControlState();source.SetGliderNeutral();source.airbrake=1;source.wheelBrake=.4f;
            var target=new AircraftControlState();target.SetGliderNeutral();
            var command=ProductControlInput.ToFlightLessonCommand(source);
            Assert.IsTrue(command.controlsGliderBrakes);ProductControlInput.CopyFlightLessonCommandInto(command,target);
            Assert.AreEqual(1,target.airbrake);Assert.AreEqual(.4f,target.wheelBrake);
            ProductControlInput.CopyFlightLessonCommandInto(default(FlightLessonControlCommand),target);
            Assert.AreEqual(1,target.airbrake);Assert.AreEqual(.4f,target.wheelBrake);
            source.airbrake=source.wheelBrake=0;ProductDemoPilot.RateLimitControlsInto(source,target,.1f);
            Assert.That(target.airbrake,Is.EqualTo(.82f).Within(.0001));Assert.That(target.wheelBrake,Is.EqualTo(.22f).Within(.0001));
        }
        [Test] public void TakeoverAndResumeRequireSafeAirborneEnergyAndPhase()
        {
            var profile=AssetDatabase.LoadAssetAtPath<SoaringDemoProfile>("Assets/Production/SoaringV1/Demo/BoulderSoaringTour.asset");Assert.IsTrue(profile.IsValid);
            var guidance=new SoaringDemoGuidance();guidance.Begin(profile,false);
            var state=new FlightDynamicsState{positionUnityMeters=new Vector3(1400,800,2600),rotationUnity=Quaternion.identity,altitudeAglMeters=800,altitudeMslMeters=2400,calibratedAirspeedKnots=48};
            Assert.IsFalse(guidance.CanTakeControl(state));
            Assert.IsFalse(guidance.Resume(state,new SoaringReturnEstimate(true,50,48),false));
            Assert.IsTrue(guidance.Resume(state,new SoaringReturnEstimate(true,300,48),false));Assert.IsTrue(guidance.CanTakeControl(state));
            state.calibratedAirspeedKnots=35;Assert.IsFalse(guidance.Resume(state,new SoaringReturnEstimate(true,300,48),false));
            state.calibratedAirspeedKnots=48;state.weightOnWheels=true;Assert.IsFalse(guidance.CanTakeControl(state));
            guidance.ReturnEarly();Assert.AreEqual(SoaringDemoGuidance.Stage.Descent,guidance.Phase);
            guidance.Hold("occupied");Assert.AreEqual(SoaringDemoGuidance.Stage.Held,guidance.Phase);Assert.AreEqual("occupied",guidance.Failure);
        }
        [Test] public void AuthoredDemoFailsClosedForUnsupportedRouteAndRetainsSinglePilot()
        {
            var source=AssetDatabase.LoadAssetAtPath<SoaringDemoProfile>("Assets/Production/SoaringV1/Demo/BoulderSoaringTour.asset");
            var copy=Object.Instantiate(source);
            try{copy.scenicPoints=new[]{new Vector2(float.NaN,0),Vector2.zero,Vector2.one};Assert.IsFalse(copy.IsValid);}
            finally{Object.DestroyImmediate(copy);}
            var world=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");
            Assert.AreEqual(1,world.GetComponentsInChildren<ProductDemoPilot>(true).Length);
            Assert.AreSame(source,world.GetComponentInChildren<ProductDemoPilot>(true).SoaringProfile);
            Assert.AreEqual(60.96f,world.GetComponentInChildren<FlightDynamicsCoordinator>(true).towlineDefinition.naturalLengthMeters,.001f);
        }
    }
}
#endif
