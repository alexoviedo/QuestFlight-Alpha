#if UNITY_INCLUDE_TESTS
using System;
using System.Reflection;
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class TrainingCoachingNavigationEditModeTests
    {
        private static FlightDynamicsState Air(float bank=0,float heading=90) => new FlightDynamicsState
        {bankDegrees=bank,headingDegrees=heading,altitudeMslMeters=7000*.3048,altitudeAglMeters=500,calibratedAirspeedKnots=90,
         rotationUnity=Quaternion.identity,positionUnityMeters=new Vector3(0,500,-30)};
        private static CockpitInstrumentV2Snapshot Indication(float speed=90) => new CockpitInstrumentV2Snapshot
        {isValid=true,availability=CockpitInstrumentV2Availability.AirData|CockpitInstrumentV2Availability.BarometricSetting|CockpitInstrumentV2Availability.TurnAndSlip,
         indicatedAirspeedKnots=speed,pressureAltitudeFeet=7000,barometricSettingInHg=29.92f};
        private static TrainingStage Stage(TrainingTask task=TrainingTask.Level) => new TrainingStage
        {task=task,heading=90,altitudeFeet=7000,speedKnots=90,dwellSeconds=1,timeoutSeconds=30};

        [Test] public void StableFlightIsQuietAndExcessBankGetsDirectionalCorrection()
        {
            Assert.AreEqual(TrainingCue.None,TrainingCoach.Evaluate(Stage(),Air(),default,Indication(),0));
            Assert.AreEqual(TrainingCue.RollLeft,TrainingCoach.Evaluate(Stage(),Air(28),default,Indication(),0));
            Assert.AreEqual(TrainingCue.RollRight,TrainingCoach.Evaluate(Stage(),Air(-28),default,Indication(),0));
            Assert.AreEqual(TrainingCue.None,TrainingCoach.Evaluate(Stage(),Air(2),default,Indication(),0));
        }
        [Test] public void RolloutIsAnticipatedBeforeHeadingReached()
        {
            Assert.AreEqual(TrainingCue.RolloutLeft,TrainingCoach.Evaluate(Stage(TrainingTask.Turn),Air(12,84),default,Indication(),3));
            Assert.AreEqual(TrainingCue.RolloutRight,TrainingCoach.Evaluate(Stage(TrainingTask.Turn),Air(-12,96),default,Indication(),-3));
        }
        [Test] public void UnsafeApproachOutranksOrdinaryCorrections()
        {
            var s=Air(25);s.altitudeAglMeters=65;s.verticalSpeedFeetPerMinute=-950;
            Assert.AreEqual(TrainingCue.GoAround,TrainingCoach.Evaluate(Stage(TrainingTask.Final),s,default,Indication(50),0));
        }
        [Test] public void RecoveringAltitudeDoesNotReceiveStaleAltitudeCorrection()
        {
            var i=Indication(); i.pressureAltitudeFeet=6700;
            Assert.AreEqual(TrainingCue.AltitudeLow,TrainingCoach.Evaluate(Stage(),Air(),default,i,0));
            i.indicatedVerticalSpeedFeetPerMinute=250;
            Assert.AreEqual(TrainingCue.None,TrainingCoach.Evaluate(Stage(),Air(),default,i,0));
        }
        [Test] public void WingsLevelStepDoesNotSecretlyRequireUntaughtHeading()
        {
            var stage=Stage();stage.attitudeOnly=true;
            var s=Air(1,150);var e=new TrainingEvaluator(); e.BeginAttempt(s);e.BeginStage(0);
            for(int n=0;n<12;n++)e.Step(stage,s,default,.1f,true);
            Assert.IsTrue(e.Complete);
        }
        [Test] public void DisplayFollowingUsesIndicatedValuesWithoutChangingPhysicalState()
        {
            var stage=Stage();stage.reference=TrainingReference.Airspeed;
            var s=Air(); s.calibratedAirspeedKnots=65;
            var e=new TrainingEvaluator();e.BeginAttempt(s);e.BeginStage(0);
            for(int n=0;n<12;n++) e.Step(stage,s,default,.1f,true,7000,90,0);
            Assert.IsTrue(e.Complete);Assert.AreEqual(65,s.calibratedAirspeedKnots);
        }
        [Test] public void OneAuthoredNavigationAndCoachWithNoAddedCameraOrLights()
        {
            var world=AssetDatabase.LoadAssetAtPath<GameObject>(ProductTrainingAuthoring.World);
            var nav=world.GetComponentsInChildren<ProductAppNavigation>(true);
            Assert.AreEqual(1,nav.Length);Assert.IsTrue(nav[0].HasAuthoredReferences);
            Assert.AreEqual(1,world.GetComponentsInChildren<TrainingCoach>(true).Length);
            Assert.AreSame(nav[0],world.GetComponentInChildren<ProductFlightModeCoordinator>(true).AuthoredNavigation);
            var p=new SerializedObject(nav[0]).FindProperty("panel").objectReferenceValue as GameObject;
            Assert.IsEmpty(p.GetComponentsInChildren<Camera>(true));Assert.IsEmpty(p.GetComponentsInChildren<Light>(true));
            var coach=world.GetComponentInChildren<TrainingCoach>(true);
            var lines=new SerializedObject(coach).FindProperty("lines");
            for(int n=0;n<lines.arraySize;n++)Assert.IsNotNull(lines.GetArrayElementAtIndex(n).FindPropertyRelative("voice").objectReferenceValue);
        }
        [Test] public void BackCancelsEndSessionConfirmationWithoutEndingTraining()
        {
            var world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);
            try
            {
                var nav=world.GetComponentInChildren<ProductAppNavigation>(true);
                var mode=world.GetComponentInChildren<ProductFlightModeCoordinator>(true);
                var training=world.GetComponentInChildren<ProductTrainingController>(true);
                typeof(ProductFlightModeCoordinator).GetField("<State>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(mode,ProductFlightModeCoordinator.FlightModeState.Training);
                typeof(ProductTrainingController).GetField("<State>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(training,TrainingSessionState.Paused);
                typeof(ProductAppNavigation).GetField("<CurrentPage>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(nav,ProductAppNavigation.Page.ConfirmMain);
                nav.Back();Assert.AreEqual(ProductAppNavigation.Page.App,nav.CurrentPage);
                Assert.AreEqual(ProductFlightModeCoordinator.FlightModeState.Training,mode.State);
                Assert.AreEqual(TrainingSessionState.Paused,training.State);
            }
            finally{PrefabUtility.UnloadPrefabContents(world);}
        }

        [TestCase(ProductFlightModeCoordinator.FlightModeState.FreeFlight)]
        [TestCase(ProductFlightModeCoordinator.FlightModeState.Demo)]
        public void BackInFlightPausesWithoutChangingMode(ProductFlightModeCoordinator.FlightModeState state)
        {
            var world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);float scale=Time.timeScale;
            try
            {
                var mode=world.GetComponentInChildren<ProductFlightModeCoordinator>(true);
                var input=world.GetComponentInChildren<QuestFlightLab.Input.ProductControlInput>(true);
                typeof(ProductFlightModeCoordinator).GetField("<State>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(mode,state);
                Time.timeScale=1;mode.AuthoredNavigation.Back();
                Assert.AreEqual(ProductAppNavigation.Page.App,mode.AuthoredNavigation.CurrentPage);
                Assert.AreEqual(state,mode.State);Assert.AreEqual(0,Time.timeScale);
                Assert.IsTrue(input.FlightLessonInputSuppressed);
            }
            finally{PrefabUtility.UnloadPrefabContents(world);Time.timeScale=scale;}
        }

        [Test] public void MenuHandoffRejectsControlGrabAndPreservesPowerUntilRelease()
        {
            var world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);
            try
            {
                var input=world.GetComponentInChildren<QuestFlightLab.Input.ProductControlInput>(true);
                input.Current.throttle=.63f;input.RequireTouchRelease();
                Assert.IsFalse(input.TryBeginTouchOverride(QuestFlightLab.Input.ProductTouchControlChannel.Throttle,77,.1f));
                Assert.AreEqual(.63f,input.Current.throttle,.001f);
                Assert.AreEqual(0,input.ActiveTouchOverrideCount);
            }
            finally{PrefabUtility.UnloadPrefabContents(world);}
        }
    }
}
#endif
