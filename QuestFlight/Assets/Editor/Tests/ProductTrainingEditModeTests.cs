#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class ProductTrainingEditModeTests
    {
        private static FlightDynamicsState Air(float heading = 90, float altitude = 7000) => new FlightDynamicsState
        { headingDegrees = heading, altitudeMslMeters = altitude * .3048, altitudeAglMeters = 500,
          calibratedAirspeedKnots = 90, rotationUnity = Quaternion.identity, positionUnityMeters = new Vector3(1200,500,1700) };
        private static TrainingStage Level(float seconds = 3) => new TrainingStage
        { task = TrainingTask.Level, heading = 90, altitudeFeet = 7000, speedKnots = 90, dwellSeconds = seconds, timeoutSeconds = 30 };

        [Test] public void ExcursionRequiresNewStableInterval()
        {
            var e = new TrainingEvaluator(); e.BeginAttempt(Air()); e.BeginStage(0);
            for (int i = 0; i < 25; i++) e.Step(Level(), Air(), default,.1f,true);
            Assert.IsFalse(e.Complete);
            for (int i = 0; i < 8; i++) e.Step(Level(), Air(130),default,.1f,true);
            Assert.AreEqual(0,e.HeldSeconds);
            for (int i = 0; i < 31; i++) e.Step(Level(),Air(),default,.1f,true);
            Assert.IsTrue(e.Complete);
        }

        [Test] public void PausedAndInvalidSamplesCannotPass()
        {
            var e = new TrainingEvaluator(); e.BeginAttempt(Air()); e.BeginStage(0);
            for (int i=0;i<100;i++) e.Step(Level(),Air(),default,.1f,false);
            var invalid = Air(); invalid.headingDegrees = double.NaN;
            e.Step(Level(),invalid,default,.1f,true);
            Assert.AreEqual(0,e.ElapsedSeconds); Assert.IsFalse(e.Complete);
        }

        [Test] public void AssistanceSurvivesStageChangesButNotFreshAttempt()
        {
            var e = new TrainingEvaluator(); e.BeginAttempt(Air()); e.MarkAssisted(); e.BeginStage(0);
            Assert.IsTrue(e.Assisted); e.BeginAttempt(Air()); Assert.IsFalse(e.Assisted);
        }

        [Test] public void BriefNativeContactCapturesPreImpactSinkAndOffRunway()
        {
            var e = new TrainingEvaluator(); var s=Air(); s.positionUnityMeters=new Vector3(-450,2,-30); s.verticalSpeedFeetPerMinute=-900;
            e.BeginAttempt(s); e.ObserveNative(s); s.weightOnWheels=true; s.verticalSpeedFeetPerMinute=0; e.ObserveNative(s);
            s.weightOnWheels=false; e.ObserveNative(s);
            Assert.IsTrue(e.Landed); Assert.IsTrue(e.BadTouchdown); Assert.AreEqual(900,e.TouchdownSinkFpm);
            s.verticalSpeedFeetPerMinute=-200; e.BeginAttempt(s); s.positionUnityMeters.z=50; s.weightOnWheels=true; e.ObserveNative(s);
            Assert.IsTrue(e.BadTouchdown);
        }

        [Test] public void GroundCheckpointSettlingIsNotScoredAsALanding()
        {
            var e=new TrainingEvaluator();var s=Air();s.altitudeAglMeters=1.25;s.calibratedAirspeedKnots=0;s.positionUnityMeters=new Vector3(-535,1,-91);
            e.BeginAttempt(s);e.ObserveNative(s);s.weightOnWheels=true;e.ObserveNative(s);
            Assert.IsFalse(e.Landed);Assert.IsFalse(e.BadTouchdown);
        }

        [Test] public void GoAroundCannotSkipFromFullFlapsStraightToClean()
        {
            var e=new TrainingEvaluator();var s=Air();s.pitchDegrees=6;s.calibratedAirspeedKnots=65;
            var stage=Level(1);stage.task=TrainingTask.GoAroundPower;
            e.BeginAttempt(s);e.BeginStage(0,1);
            var c=new FlightDynamicsControlSnapshot{throttle=1,flaps=0};
            for(int i=0;i<20;i++)e.Step(stage,s,c,.1f,true);
            Assert.IsFalse(e.Complete);c.flaps=.67f;
            for(int i=0;i<11;i++)e.Step(stage,s,c,.1f,true);
            Assert.IsTrue(e.Complete);
        }

        [Test] public void TrimRequiresActualTrimUseAndReducedYokePressure()
        {
            var e=new TrainingEvaluator(); e.BeginAttempt(Air()); e.BeginStage(0);
            var stage=Level(); stage.task=TrainingTask.Trim;
            for(int i=0;i<40;i++)e.Step(stage,Air(),default,.1f,true);
            Assert.IsFalse(e.Complete);
            for(int i=0;i<40;i++)e.Step(stage,Air(),new FlightDynamicsControlSnapshot{trim=.1f,elevator=.4f},.1f,true);
            Assert.IsFalse(e.Complete);
            for(int i=0;i<31;i++)e.Step(stage,Air(),new FlightDynamicsControlSnapshot{trim=.1f,elevator=.03f},.1f,true);
            Assert.IsTrue(e.Complete);
        }

        [Test] public void ControlConfirmationNeedsBothDirectionsAndCenter()
        {
            var e=new TrainingEvaluator(); e.BeginAttempt(Air()); e.BeginStage(0);
            var stage=Level(.5f); stage.task=TrainingTask.RudderControl;
            for(int i=0;i<20;i++)e.Step(stage,Air(),new FlightDynamicsControlSnapshot{rudder=.8f},.1f,true);
            Assert.IsFalse(e.Complete);
            e.Step(stage,Air(),new FlightDynamicsControlSnapshot{rudder=-.8f},.1f,true);
            for(int i=0;i<6;i++)e.Step(stage,Air(),default,.1f,true);
            Assert.IsTrue(e.Complete);
        }

        [Test] public void StableFinalRequiresRunwayGeometryAndAirborneState()
        {
            var e=new TrainingEvaluator(); var s=Air(90,5588); s.positionUnityMeters=new Vector3(-2100,91,-30);
            s.altitudeAglMeters=91; s.calibratedAirspeedKnots=65; s.verticalSpeedFeetPerMinute=-450;
            var stage=Level(2);stage.task=TrainingTask.Final; var c=new FlightDynamicsControlSnapshot{flaps=.67f};
            e.BeginAttempt(s);e.BeginStage(0);
            var bad=s;bad.positionUnityMeters.z=200;
            for(int i=0;i<30;i++)e.Step(stage,bad,c,.1f,true);
            Assert.IsFalse(e.Complete);
            for(int i=0;i<21;i++)e.Step(stage,s,c,.1f,true);
            Assert.IsTrue(e.Complete);
        }

        [Test] public void StoppedOnRunwayCannotPassHoldShort()
        {
            var e=new TrainingEvaluator(); var s=Air();s.weightOnWheels=true;s.positionUnityMeters=new Vector3(-620,0,-30);
            var stage=Level(1);stage.task=TrainingTask.Stop;stage.targetEnu=new Vector2(-620,-72);stage.captureMeters=60;
            e.BeginAttempt(s);e.BeginStage(0);
            for(int i=0;i<20;i++)e.Step(stage,s,default,.1f,true);
            Assert.IsFalse(e.Complete);
        }

        [Test] public void GoAroundRequiresClimbSpeedAndStagedConfiguration()
        {
            var e=new TrainingEvaluator();var s=Air();s.altitudeAglMeters=100;s.calibratedAirspeedKnots=75;s.verticalSpeedFeetPerMinute=300;
            var stage=Level(1);stage.task=TrainingTask.GoAroundClean;
            e.BeginAttempt(s);e.BeginStage(0);
            for(int i=0;i<20;i++)e.Step(stage,s,new FlightDynamicsControlSnapshot{flaps=.67f},.1f,true);
            Assert.IsFalse(e.Complete);
            for(int i=0;i<11;i++)e.Step(stage,s,default,.1f,true);
            Assert.IsTrue(e.Complete);
        }

        [Test] public void LocalProgressPreservesIndependentCompletionAndHandlesCorruption()
        {
            string path=Path.Combine(Path.GetTempPath(),"qfl_training_"+Guid.NewGuid()+".json");
            try
            {
                var p=new TrainingProgress();p.Record("first","done","first",true);Assert.IsTrue(p.Save(path));
                p=TrainingProgress.Load(path);p.Record("first","needs practice","retry",false);Assert.IsTrue(p.Save(path));
                Assert.IsTrue(TrainingProgress.Load(path).Find("first").independentCompletion);
                File.WriteAllText(path,"{}");Assert.IsNotNull(TrainingProgress.Load(path));
            }
            finally { if(File.Exists(path))File.Delete(path); }
        }

        [Test] public void AuthoredSixLessonsHaveNarrationAndOneControllerWithoutExtraCameras()
        {
            var world=AssetDatabase.LoadAssetAtPath<GameObject>(ProductTrainingAuthoring.World);
            var controllers=world.GetComponentsInChildren<ProductTrainingController>(true);
            Assert.That(controllers,Has.Length.EqualTo(1));var t=controllers[0];Assert.IsTrue(t.HasAuthoredReferences);
            Assert.AreSame(t,world.GetComponentInChildren<ProductFlightModeCoordinator>(true).AuthoredTraining);
            Assert.AreEqual(6,t.Lessons.Length);
            foreach(var lesson in t.Lessons)
            {
                Assert.IsTrue(lesson.IsValid,lesson.name);Assert.IsNotNull(lesson.briefingNarration,lesson.name);
                foreach(var stage in lesson.stages)Assert.IsNotNull(stage.narration,lesson.name+"/"+stage.title);
            }
            var presentation=world.transform.Find("AircraftSimulationRoot/"+ProductFlightModesFoundationAuthoring.CalibrationPath+"/FlightTrainingPresentation");
            Assert.IsNotNull(presentation);Assert.IsEmpty(presentation.GetComponentsInChildren<Camera>(true));Assert.IsEmpty(presentation.GetComponentsInChildren<Light>(true));
        }

        [Test] public void ManualAndPausedOwnershipDoesNotReenableGuidance()
        {
            var world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);
            float scale=Time.timeScale;
            try
            {
                var t=world.GetComponentInChildren<ProductTrainingController>(true);
                var input=world.GetComponentInChildren<QuestFlightLab.Input.ProductControlInput>(true);
                var stateField=typeof(ProductTrainingController).GetField("<State>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic);
                stateField.SetValue(t,TrainingSessionState.Practice);
                input.Current.throttle=.73f;input.Current.flaps=.33f;input.Current.trim=.12f;
                t.PauseExercise();Assert.AreEqual(0,Time.timeScale);Assert.IsTrue(input.FlightLessonInputSuppressed);
                t.ResumeExercise();Assert.IsFalse(input.FlightLessonGuidanceActive);Assert.IsFalse(input.FlightLessonInputSuppressed);
                Assert.AreEqual(.73f,input.Current.throttle,.001f);Assert.AreEqual(.33f,input.Current.flaps,.001f);
                Assert.AreEqual(TrainingSessionState.Practice,t.State);
            }
            finally{PrefabUtility.UnloadPrefabContents(world);Time.timeScale=scale;}
        }

        [Test] public void SelectingAnotherLessonClearsThePreviousStage()
        {
            var world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);
            try
            {
                var t=world.GetComponentInChildren<ProductTrainingController>(true);
                typeof(ProductTrainingController).GetField("<State>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(t,TrainingSessionState.Menu);
                typeof(ProductTrainingController).GetField("<StageIndex>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(t,9);
                Assert.DoesNotThrow(()=>t.SelectLesson(1));
                Assert.AreEqual(0,t.StageIndex);
                Assert.AreEqual(TrainingSessionState.Briefing,t.State);
            }
            finally { PrefabUtility.UnloadPrefabContents(world); }
        }

        [Test] public void NativeRetryRestoresAManageableCheckpointAfterADisturbedFlight()
        {
            var world=AssetDatabase.LoadAssetAtPath<GameObject>(ProductTrainingAuthoring.World);
            var flight=world.GetComponentInChildren<FlightDynamicsCoordinator>(true);
            var lesson=world.GetComponentInChildren<ProductTrainingController>(true).Lessons[1];
            using var backend=new JSBSimNativeDevelopmentBackend(AlexOviedo.QuestFlight.JSBSim.JSBSimRuntimeData.EditorPackageDataRoot);
            Assert.IsTrue(backend.Initialize(new FlightDynamicsBackendContext{simulationRoot=flight.simulationRoot,aircraftState=flight.aircraftState,terrainSurface=flight.terrainSurface}),backend.LastError);
            var at=FlightFrameConversions.GeodeticToUnity(lesson.start.latitudeDegrees,lesson.start.longitudeDegrees,lesson.start.altitudeMslMeters,GeodeticReference.Kbdu);
            var controls=QuestFlightLab.Runtime.AircraftControlState.Neutral(lesson.initialThrottle);
            controls.mixture=1;controls.trim=lesson.initialTrim;
            void Advance(int count)
            {
                for(int i=0;i<count;i++)
                {
                    var state=backend.CurrentState;backend.SetControls(controls);
                    backend.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(flight.windField.Sample(state.positionUnityMeters,state.simulationTimeSeconds)));
                    Assert.IsTrue(backend.Advance(1d/120),backend.LastError);
                }
            }
            backend.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(flight.windField.Sample(at,0)));
            Assert.IsTrue(backend.Reset(lesson.start));Advance(960);
            controls.elevator=.5f;controls.aileron=.3f;Advance(240);
            backend.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(flight.windField.Sample(at,0)));
            Assert.IsTrue(backend.Reset(lesson.start));controls.elevator=controls.aileron=0;Advance(960);
            var restored=backend.CurrentState;
            Assert.IsTrue(restored.IsFinite);Assert.Less(Math.Abs(restored.pitchDegrees),10);
            Assert.Less(Math.Abs(restored.bankDegrees),15);Assert.Greater(restored.calibratedAirspeedKnots,70);
            Assert.Less(Math.Abs(restored.verticalSpeedFeetPerMinute),900);
        }
    }
}
#endif
