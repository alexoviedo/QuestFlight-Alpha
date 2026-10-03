#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class TesterReadinessEditModeTests
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [Test] public void CombinedTouchBrakesDoNotRemapExternalControllerBrakes()
        {
            QuestFlightLab.Input.ProductControlInput.ResolveTouchBrakes(.8f,0,0,0,true,out float left,out float right);
            Assert.That(left,Is.EqualTo(.8f));Assert.That(right,Is.Zero);
            QuestFlightLab.Input.ProductControlInput.ResolveTouchBrakes(0,0,.6f,0,true,out left,out right);
            Assert.That(left,Is.EqualTo(.6f));Assert.That(right,Is.EqualTo(.6f));
        }
        [Test] public void AdjustmentStopsAtLimitsAndRudderKeepsFullAuthority()
        {
            Assert.That(ProductComfortPreferences.Adjust(.24f,.04f,.04f,.24f),Is.EqualTo(.24f));
            Assert.That(ProductComfortPreferences.Adjust(.04f,-.04f,.04f,.24f),Is.EqualTo(.04f));
            foreach(float dz in new[]{.04f,.12f,.24f})foreach(float exponent in new[]{1f,1.5f,2f})
            {Assert.That(ProductComfortPreferences.Axis(-1,dz,exponent),Is.EqualTo(-1));Assert.That(ProductComfortPreferences.Axis(1,dz,exponent),Is.EqualTo(1));Assert.That(ProductComfortPreferences.Axis(dz,dz,exponent),Is.Zero);}
        }
        [Test] public void SteeringAssignmentsRemainDistinctAndRestoreTheirSavedValue()
        {
            string key="QuestFlight.LeftStickSteering";bool had=PlayerPrefs.HasKey(key);int previous=PlayerPrefs.GetInt(key);
            try
            {
                foreach(bool left in new[]{false,true})
                {ProductComfortPreferences.SetFlag("LeftStickSteering",left);Assert.That(ProductComfortPreferences.RudderController,Is.Not.EqualTo(ProductComfortPreferences.TrimController));Assert.That(ProductComfortPreferences.BindingSummary,Does.Contain(left?"left stick: rudder":"right stick: rudder"));}
            }
            finally{if(had)PlayerPrefs.SetInt(key,previous);else PlayerPrefs.DeleteKey(key);PlayerPrefs.Save();}
        }
        [Test] public void AuthoredMapAndNewPagesHaveReferencesAndExits()
        {
            var world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);float time=Time.timeScale;
            try
            {
                var nav=world.GetComponentInChildren<ProductAppNavigation>(true);
                foreach(var page in new[]{ProductAppNavigation.Page.Bindings,ProductAppNavigation.Page.InputPreview,ProductAppNavigation.Page.InstrumentHelp})
                {
                    typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,page);
                    typeof(ProductAppNavigation).GetMethod("Refresh",Private).Invoke(nav,null);
                    Assert.That(((int[])typeof(ProductAppNavigation).GetField("actions",Private).GetValue(nav)).Contains(5),Is.True,page.ToString());
                }
                var e=world.GetComponentInChildren<ProductFlightExperience>(true);
                foreach(string name in new[]{"flownPath","selectedRoute","lessonDestination"})Assert.That(typeof(ProductFlightExperience).GetField(name,Private).GetValue(e),Is.Not.Null,name);
            }
            finally{PrefabUtility.UnloadPrefabContents(world);Time.timeScale=time;}
        }
        [Test] public void OffRunwayTouchdownGetsAlignmentAdviceAndTraceRemainsBounded()
        {
            var world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);float time=Time.timeScale;
            try
            {
                var mode=world.GetComponentInChildren<ProductFlightModeCoordinator>(true);
                typeof(ProductFlightModeCoordinator).GetProperty("State").SetValue(mode,ProductFlightModeCoordinator.FlightModeState.FreeFlight);
                typeof(ProductTrainingController).GetProperty("IsGuestSession").SetValue(mode.AuthoredTraining,true);
                var e=world.GetComponentInChildren<ProductFlightExperience>(true);
                typeof(ProductFlightExperience).GetField("active",Private).SetValue(e,new ProductFlightExperience.Record{mode="QA manual flight"});
                var sample=typeof(ProductFlightExperience).GetMethod("SampleFlight",Private);
                for(int n=0;n<300;n++)sample.Invoke(e,new object[]{new FlightDynamicsState{positionUnityMeters=new Vector3(n*30,300,0),altitudeAglMeters=300,calibratedAirspeedKnots=90},.5f});
                typeof(ProductFlightExperience).GetMethod("RecordTouchdown",Private).Invoke(e,new object[]{new FlightDynamicsState{positionUnityMeters=new Vector3(0,0,100),weightOnWheels=true,headingDegrees=90}});
                e.FinishFlight("QA finished");
                Assert.That(e.LastRecord.path.Length,Is.EqualTo(256));Assert.That(e.LastRecord.advice,Does.Contain("alignment"));Assert.That(e.LastRecord.observations,Does.Contain("outside the modeled runway"));
            }
            finally{PrefabUtility.UnloadPrefabContents(world);Time.timeScale=time;}
        }
        [Test] public void ObservationFlightDoesNotReceiveFalsePilotGrade()
        {
            var world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);
            try
            {
                var mode=world.GetComponentInChildren<ProductFlightModeCoordinator>(true);typeof(ProductTrainingController).GetProperty("IsGuestSession").SetValue(mode.AuthoredTraining,true);
                var e=world.GetComponentInChildren<ProductFlightExperience>(true);typeof(ProductFlightExperience).GetField("active",Private).SetValue(e,new ProductFlightExperience.Record{mode="Demo",assisted=true});
                e.FinishFlight("Tour complete");Assert.That(e.LastRecord.observations,Does.Contain("not evaluated"));
            }
            finally{PrefabUtility.UnloadPrefabContents(world);}
        }
    }
}
#endif
