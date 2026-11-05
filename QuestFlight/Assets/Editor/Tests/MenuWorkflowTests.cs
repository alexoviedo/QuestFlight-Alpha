using NUnit.Framework;
using QuestFlight.Product.Training;
using QuestFlightLab.Flight.Soaring;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;
namespace QuestFlight.Editor.Tests
{
    public sealed class MenuWorkflowTests
    {
        static FlightDynamicsCoordinator Flight=>AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab").GetComponentInChildren<FlightDynamicsCoordinator>(true);
        static FlightDynamicsState State=>new FlightDynamicsState{positionUnityMeters=new Vector3(1200,500,2200),rotationUnity=Quaternion.identity,altitudeMslMeters=2112,altitudeAglMeters=500,headingDegrees=0,calibratedAirspeedKnots=50};
        static SoaringOperationSession Ready()
        {
            var op=new SoaringOperationSession(Flight.soaringOperations[0]);var s=State;s.weightOnWheels=true;
            op.ObserveChecks(s,new FlightDynamicsControlSnapshot{aileron=-1,elevator=-1,rudder=-1,airbrake=1,wheelBrake=1,trim=.3f},true);
            op.ObserveChecks(s,new FlightDynamicsControlSnapshot{aileron=1,elevator=1,rudder=1},false);
            Assert.IsTrue(op.ConfirmCrewRehook());return op;
        }
        [Test] public void ReleasingAgainInvalidatesReadinessAndCannotClear()
        {
            var op=Ready();var s=State;s.weightOnWheels=true;op.ReconcileConnection(false,false,s);
            Assert.IsFalse(op.CrewReady);Assert.AreEqual(SoaringOperationPhase.CrewRehook,op.Phase);
            Assert.IsFalse(op.TryClear(true,default));Assert.That(op.Instruction,Does.Contain("Reconnect"));
        }
        [Test] public void ExplicitPreflightSkipIsStoppedOnlyAndDoesNotFabricateChecks()
        {
            var op=new SoaringOperationSession(Flight.soaringOperations[0]);var s=State;
            Assert.IsFalse(op.TrySkipPreflight(s));s.weightOnWheels=true;s.groundSpeedKnots=1;
            Assert.IsFalse(op.TrySkipPreflight(s));s.groundSpeedKnots=0;
            Assert.IsTrue(op.TrySkipPreflight(s));Assert.IsTrue(op.PreflightSatisfied);
            Assert.IsFalse(op.PreflightComplete);Assert.AreEqual(0,op.CheckedControls);Assert.IsFalse(op.ReleaseChecked);
            Assert.IsTrue(op.ConfirmCrewRehook());Assert.IsFalse(op.TrySkipPreflight(s));
            Assert.IsFalse(op.TryClear(false,default));Assert.IsFalse(op.TryClear(true,new FlightDynamicsControlSnapshot{airbrake=1}));
            Assert.IsTrue(op.TryClear(true,default));
            var fresh=new SoaringOperationSession(op.Definition);Assert.IsFalse(fresh.PreflightSkipped);Assert.IsFalse(fresh.PreflightSatisfied);
        }
        [Test] public void ControlsChecklistDoesNotRequireArbitraryTrimMovement()
        {
            var op=new SoaringOperationSession(Flight.soaringOperations[0]);var s=State;s.weightOnWheels=true;
            var c=new FlightDynamicsControlSnapshot{aileron=-1,elevator=-1,rudder=-1,airbrake=1,wheelBrake=1,trim=.10626088f};
            op.ObserveChecks(s,c,true);c.aileron=c.elevator=c.rudder=1;op.ObserveChecks(s,c,false);
            Assert.IsTrue(op.PreflightComplete);Assert.AreEqual(127,op.CheckedControls);
        }
        [Test] public void SkippedPreparationAdvancesLessonWithoutIndependentCredit()
        {
            var root=PrefabUtility.LoadPrefabContents("Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");
            try
            {
                var f=root.GetComponentInChildren<FlightDynamicsCoordinator>(true);var s=State;s.weightOnWheels=true;
                var op=new SoaringOperationSession(f.soaringOperations[0]);Assert.IsTrue(op.TrySkipPreflight(s));Assert.IsTrue(op.ConfirmCrewRehook());
                typeof(FlightDynamicsCoordinator).GetProperty("Operation").SetValue(f,op);
                var p=new SoaringTrainingProgram();p.BeginAttempt(s);p.BeginStage(s,0,f);
                var stage=new TrainingStage{task=TrainingTask.SoaringPreparation,dwellSeconds=.2f,timeoutSeconds=20};
                p.Step(stage,s,default,.1f,f);p.Step(stage,s,default,.1f,f);
                Assert.IsTrue(p.Complete);Assert.IsTrue(p.Assisted);Assert.IsFalse(op.PreflightComplete);
                p.BeginStage(s,0,f);Assert.IsTrue(p.Assisted);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        [TestCase(false)] [TestCase(true)] public void SeparationOrTugAbortOnGroundEndsLaunch(bool tugAborted)
        {
            var op=Ready();Assert.IsTrue(op.TryClear(true,default));var s=State;s.weightOnWheels=true;s.groundSpeedKnots=8;
            op.ReconcileConnection(tugAborted,tugAborted,s);Assert.AreEqual(SoaringOperationPhase.Aborted,op.Phase);Assert.IsFalse(op.LaunchAuthorized);
        }
        [Test] public void SlipRequiresClosedBrakesThenRecovery()
        {
            var f=Flight;var p=new SoaringTrainingProgram();var s=State;s.sideslipDegrees=7;s.bankDegrees=12;
            var c=new FlightDynamicsControlSnapshot{rudder=.4f,airbrake=1};var stage=new TrainingStage{task=TrainingTask.SoaringSlip,dwellSeconds=1,timeoutSeconds=30};
            p.BeginAttempt(s);p.BeginStage(s,0,f);for(int i=0;i<40;i++)p.Step(stage,s,c,.1f,f);Assert.IsFalse(p.Complete);
            c.airbrake=0;for(int i=0;i<12;i++)p.Step(stage,s,c,.1f,f);Assert.IsFalse(p.Complete,"Holding a slip alone must not score recovery.");
            s.sideslipDegrees=s.bankDegrees=0;c.rudder=0;for(int i=0;i<12;i++)p.Step(stage,s,c,.1f,f);Assert.IsTrue(p.Complete);
        }
        [Test] public void RidgeHeadingOscillationDoesNotCountAsReversal()
        {
            var f=Flight;var p=new SoaringTrainingProgram();var s=State;s.headingDegrees=90;s.velocityUnityMetersPerSecond=Vector3.right*20;
            var stage=new TrainingStage{task=TrainingTask.SoaringOutwardTurn,dwellSeconds=1,timeoutSeconds=30};p.BeginAttempt(s);p.BeginStage(s,0,f);
            for(int i=0;i<120;i++){s.headingDegrees=i%2==0?60:120;p.Step(stage,s,default,.1f,f);}Assert.IsFalse(p.Complete);
        }
        [Test] public void EarlySafeTouchdownEndsAirborneGoalWithoutCallingItAircraftFailure()
        {
            var f=Flight;var p=new SoaringTrainingProgram();var s=State;p.BeginAttempt(s);p.BeginStage(s,0,f);
            s.positionUnityMeters=new Vector3(-400,3,-30);s.weightOnWheels=true;p.ObserveNative(s,f);
            p.Step(new TrainingStage{task=TrainingTask.SoaringSpeed,dwellSeconds=1,timeoutSeconds=20},s,default,.1f,f);
            Assert.That(p.Failure,Does.Contain("Landed before"));Assert.IsFalse(p.UnsafeFlight);
        }
    }
}
