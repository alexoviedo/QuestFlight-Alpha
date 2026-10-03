#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using QuestFlight.Product.Training;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class LessonTeachingQualityTests
    {
        private static FlightDynamicsState State(bool ground=false) => new FlightDynamicsState
        {weightOnWheels=ground,rotationUnity=Quaternion.identity,positionUnityMeters=new Vector3(0,500,-30),
         altitudeMslMeters=7000*.3048,altitudeAglMeters=500,headingDegrees=90,calibratedAirspeedKnots=90};
        private static TrainingStage Stage(TrainingTask task) => new TrainingStage
        {task=task,heading=90,altitudeFeet=7000,speedKnots=90,dwellSeconds=.2f,timeoutSeconds=30};
        [TestCase(TrainingTask.RollControl)]
        [TestCase(TrainingTask.PitchControl)]
        [TestCase(TrainingTask.RudderControl)]
        public void GroundCheckRemembersEachMotionAndRequiresNeutral(TrainingTask task)
        {
            var e=new TrainingEvaluator();var s=State(true);s.groundSpeedKnots=0;
            var stage=Stage(task);e.BeginAttempt(s);e.BeginStage(0);
            void Move(float value)
            {
                var c=new FlightDynamicsControlSnapshot();
                if(task==TrainingTask.RollControl)c.aileron=value;else if(task==TrainingTask.PitchControl)c.elevator=value;else c.rudder=value;
                e.Step(stage,s,c,.1f,true);
            }
            Move(.5f);Assert.AreEqual(0,e.ControlPhase,"Opposite motion must not skip the first requested motion.");
            Move(-.5f);Assert.AreEqual(1,e.ControlPhase);
            Move(0);Assert.AreEqual(1,e.ControlPhase);
            Move(.5f);Assert.AreEqual(2,e.ControlPhase);Assert.IsFalse(e.Complete);
            Move(0);Move(0);Assert.IsTrue(e.Complete);
            Assert.That(e.Feedback,Does.Contain("centered"));
        }
        [Test] public void MovingAircraftCannotPassParkedControlCheck()
        {
            var s=State(true);s.groundSpeedKnots=5;var e=new TrainingEvaluator();e.BeginStage(0);
            e.Step(Stage(TrainingTask.RollControl),s,new FlightDynamicsControlSnapshot{aileron=-.7f},.1f,true);
            Assert.AreEqual(0,e.ControlPhase);Assert.That(e.Feedback,Does.Contain("brakes"));
        }
        [Test] public void TurnMustFinishWithWingsNearlyLevel()
        {
            var s=State();s.bankDegrees=20;var e=new TrainingEvaluator();e.BeginStage(0);
            for(int n=0;n<5;n++)e.Step(Stage(TrainingTask.Turn),s,default,.1f,true);
            Assert.IsFalse(e.Complete,"Passing through the target while still banked is not a completed rollout.");
            s.bankDegrees=2;for(int n=0;n<3;n++)e.Step(Stage(TrainingTask.Turn),s,default,.1f,true);
            Assert.IsTrue(e.Complete);
        }
        [Test] public void ClimbHandsOffBeforeOvershootingTarget()
        {
            var s=State();s.altitudeMslMeters=7260*.3048;s.verticalSpeedFeetPerMinute=500;s.calibratedAirspeedKnots=75;
            var stage=Stage(TrainingTask.Climb);stage.altitudeFeet=7300;
            var e=new TrainingEvaluator();e.BeginStage(0);
            for(int n=0;n<3;n++)e.Step(stage,s,default,.1f,true);
            Assert.IsTrue(e.Complete,"At 500 ft/min the next instruction should anticipate level-off.");
        }
        [Test] public void OldGenericYokeNagIsNotAnUnconditionalCoachCue()
        {
            var indication=new QuestFlightLab.Aircraft.CockpitDynamics.V2.CockpitInstrumentV2Snapshot{isValid=true};
            Assert.AreEqual(TrainingCue.None,TrainingCoach.Evaluate(Stage(TrainingTask.PitchControl),State(true),default,indication,0));
        }
        [TestCase(TrainingTask.Climb)]
        [TestCase(TrainingTask.Descend)]
        public void StudentWhoAlreadyLeveledAtTargetIsNotAskedToRepeatTheManeuver(TrainingTask task)
        {
            var s=State();s.calibratedAirspeedKnots=85;s.verticalSpeedFeetPerMinute=0;
            var e=new TrainingEvaluator();e.BeginStage(0);
            for(int n=0;n<3;n++)e.Step(Stage(task),s,default,.1f,true);
            Assert.IsTrue(e.Complete);
        }
    }
}
#endif
