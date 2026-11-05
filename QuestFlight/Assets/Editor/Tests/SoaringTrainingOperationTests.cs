using System;
using System.Linq;
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class SoaringTrainingOperationTests
    {
        private const string World="Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab";
        private static BoulderSoaringOperationDefinition Operation()=>AssetDatabase.LoadAssetAtPath<BoulderSoaringOperationDefinition>("Assets/Production/SoaringV1/Operations/DevelopmentRunway8.asset");
        private static FlightDynamicsState Air()=>new FlightDynamicsState{positionUnityMeters=new Vector3(1200,500,2200),rotationUnity=Quaternion.identity,altitudeMslMeters=2112,altitudeAglMeters=500,headingDegrees=90,calibratedAirspeedKnots=50};
        [Test] public void WaitingLineupDoesNotOwnEitherParallelRunway()
        {
            var root=new GameObject("Runway occupancy fixture");var route=ScriptableObject.CreateInstance<AmbientAircraftRoute>();
            try
            {
                var traffic=root.AddComponent<AmbientTrafficDirector>();var actor=root.AddComponent<AmbientAircraftActor>();traffic.actors=new[]{actor};
                route.segments=new[]{new AmbientAircraftRoute.Segment{phase=AmbientFlightPhase.LineUp,onGround=true}};
                typeof(AmbientAircraftActor).GetProperty("Route").SetValue(actor,route);
                typeof(AmbientAircraftActor).GetProperty("Position").SetValue(actor,new Vector3(-620,0,-67));
                Assert.That(traffic.SoaringRunwayClear(),Is.True);
                typeof(AmbientTrafficDirector).GetField("runwayOwner",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(traffic,0);
                Assert.That(traffic.SoaringRunwayClear(),Is.False);
                foreach(float north in new[]{-30f,30f}){typeof(AmbientAircraftActor).GetProperty("Position").SetValue(actor,new Vector3(0,0,north));Assert.That(traffic.SoaringRunwayClear(),Is.False);}
            }
            finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(route);}
        }
        [Test] public void SelectedCatalogPreservesSixC172IdsAndSevenUnpoweredScopedIds()
        {
            var root=PrefabUtility.LoadPrefabContents(World);
            try
            {
                var t=root.GetComponentInChildren<ProductTrainingController>(true);var f=root.GetComponentInChildren<FlightDynamicsCoordinator>(true);
                var before=t.Lessons.Select(l=>l.lessonId).ToArray();Assert.That(before.Length,Is.EqualTo(6));
                var c172=f.aircraftDefinition;f.aircraftDefinition=AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(FlightAircraftDefinition.DefinitionFolder+"/SGS233A_Reference.asset");t.SelectAircraftCatalog();
                Assert.That(t.HasAuthoredReferences,Is.True);Assert.That(t.Lessons.Length,Is.EqualTo(7));
                foreach(var lesson in t.Lessons)
                {
                    Assert.That(before,Does.Not.Contain(lesson.lessonId));Assert.That(lesson.lessonId,Does.StartWith("sgs233.reference."));
                    Assert.That(lesson.initialThrottle,Is.Zero);Assert.That(lesson.initialFlaps,Is.Zero);Assert.That(lesson.start.engineRunning,Is.False);Assert.That(lesson.goAroundStage,Is.EqualTo(-1));
                    Assert.That(lesson.stages.All(s=>(int)s.task>=100),Is.True);
                }
                f.aircraftDefinition=c172;t.SelectAircraftCatalog();Assert.That(t.Lessons.Select(l=>l.lessonId),Is.EqualTo(before));Assert.That(t.HasAuthoredReferences,Is.True);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        [Test] public void ProgressRoundTripDoesNotOverwriteOldOrIndependentCompletion()
        {
            var p=new TrainingProgress();p.Record("02_level","old pass","old debrief",true);
            p.Record("sgs233.reference.thermal","assisted","new debrief",false);
            var loaded=JsonUtility.FromJson<TrainingProgress>(JsonUtility.ToJson(p));
            Assert.That(loaded.Find("02_level").independentCompletion,Is.True);
            Assert.That(loaded.Find("sgs233.reference.thermal").independentCompletion,Is.False);
            loaded.Record("sgs233.reference.thermal","independent","gain observed",true);loaded.Record("sgs233.reference.thermal","retry","failed",false);
            Assert.That(loaded.Find("sgs233.reference.thermal").independentCompletion,Is.True);Assert.That(loaded.Find("02_level").attempts,Is.EqualTo(1));
        }
        [Test] public void ReleaseAndCrewReadinessCannotSkipGroundChecksOrBlockedRunway()
        {
            var op=new SoaringOperationSession(Operation());var s=Air();s.weightOnWheels=true;s.groundSpeedKnots=0;
            op.ObserveChecks(s,new FlightDynamicsControlSnapshot{trim=.10626088f},true);Assert.That(op.PreflightComplete,Is.False);Assert.That(op.ConfirmCrewRehook(),Is.False);
            var c=new FlightDynamicsControlSnapshot{aileron=-.6f,elevator=-.6f,rudder=-.6f,airbrake=1,wheelBrake=1,trim=.2f};
            s.groundSpeedKnots=3;op.ObserveChecks(s,c,true);Assert.That(op.CheckedControls,Is.Zero);
            s.groundSpeedKnots=0;op.ObserveChecks(s,c,true);c.aileron=c.elevator=c.rudder=.6f;op.ObserveChecks(s,c,false);
            Assert.That(op.PreflightComplete,Is.True);Assert.That(op.ConfirmCrewRehook(),Is.True);
            Assert.That(op.TryClear(false,default),Is.False);Assert.That(op.TryClear(true,c),Is.False);
            Assert.That(op.TryClear(true,default),Is.True);Assert.That(op.Phase,Is.EqualTo(SoaringOperationPhase.Launch));
        }
        [Test] public void UnpoweredEvaluatorNeedsStableMeasuredSpeedAndRetainsAssistance()
        {
            var flight=AssetDatabase.LoadAssetAtPath<GameObject>(World).GetComponentInChildren<FlightDynamicsCoordinator>(true);
            var e=new SoaringTrainingProgram();var s=Air();var stage=new TrainingStage{task=TrainingTask.SoaringSpeed,speedKnots=50,dwellSeconds=2,timeoutSeconds=20,retryAdvice="Settle speed."};
            e.BeginAttempt(s);e.BeginStage(s,0,flight);e.MarkAssisted();
            for(int i=0;i<15;i++)e.Step(stage,s,default,.1f,flight);Assert.That(e.Complete,Is.False);
            s.calibratedAirspeedKnots=65;e.Step(stage,s,default,.1f,flight);Assert.That(e.HeldSeconds,Is.Zero);
            s.calibratedAirspeedKnots=50;for(int i=0;i<21;i++)e.Step(stage,s,default,.1f,flight);Assert.That(e.Complete,Is.True);
            e.BeginStage(s,0,flight);Assert.That(e.Assisted,Is.True);e.BeginAttempt(s);Assert.That(e.Assisted,Is.False);
            stage.task=TrainingTask.GoAroundPower;e.Step(stage,s,default,.1f,flight);Assert.That(e.Failure,Does.Contain("Unsupported task"));
        }
        [Test] public void BriefNativeHardContactFailsUnpoweredLessonAndNoAssistAddsPower()
        {
            var flight=AssetDatabase.LoadAssetAtPath<GameObject>(World).GetComponentInChildren<FlightDynamicsCoordinator>(true);var e=new SoaringTrainingProgram();var s=Air();
            e.BeginAttempt(s);s.positionUnityMeters=new Vector3(-400,3,-30);s.verticalSpeedFeetPerMinute=-900;e.ObserveNative(s,flight);
            s.weightOnWheels=true;s.verticalSpeedFeetPerMinute=0;e.ObserveNative(s,flight);s.weightOnWheels=false;e.ObserveNative(s,flight);
            Assert.That(e.Landed,Is.True);Assert.That(e.Failure,Does.Contain("touchdown"));
            var c=e.Assist(Air(),90,.106f,true,20);Assert.That(c.throttle,Is.Zero);Assert.That(c.mixture,Is.Zero);Assert.That(c.flaps,Is.Zero);
        }
        [Test] public void ReturnPlanningUsesAltitudeWindAndCoverageWithoutAllocating()
        {
            var flight=AssetDatabase.LoadAssetAtPath<GameObject>(World).GetComponentInChildren<FlightDynamicsCoordinator>(true);var op=Operation();var weather=new SoaringAtmosphereSession(flight.windField,op.weather);var s=Air();s.positionUnityMeters.y=900;
            var high=SoaringReturnPlanner.Evaluate(op,flight.terrainSurface,weather,s,950*.45359237);s.positionUnityMeters.y=300;
            var low=SoaringReturnPlanner.Evaluate(op,flight.terrainSurface,weather,s,950*.45359237);
            Assert.That(high.supported,Is.True);Assert.That(high.marginMeters,Is.GreaterThan(low.marginMeters+400));Assert.That(low.reachable,Is.False);
            long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<5;i++)SoaringReturnPlanner.Evaluate(op,flight.terrainSurface,weather,s,430);
            Assert.That(GC.GetAllocatedBytesForCurrentThread()-before,Is.Zero);
            s.positionUnityMeters.x=9000;Assert.That(SoaringReturnPlanner.Evaluate(op,flight.terrainSurface,weather,s,430).supported,Is.False);
        }
    }
}
