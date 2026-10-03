#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlightLab.Environment;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class ReleasePolishEditModeTests
    {
        [Test] public void RudderCurvesRetainNeutralFullThrowAndMonotonicResponse()
        {
            foreach(float zone in new[]{.04f,.12f,.24f})foreach(float curve in new[]{1f,1.5f,2f})
            {
                Assert.That(ProductComfortPreferences.Axis(0,zone,curve),Is.EqualTo(0));
                Assert.That(ProductComfortPreferences.Axis(1,zone,curve),Is.EqualTo(1));
                Assert.That(ProductComfortPreferences.Axis(-1,zone,curve),Is.EqualTo(-1));
                float previous=-1;
                for(int i=-100;i<=100;i++){float v=ProductComfortPreferences.Axis(i/100f,zone,curve);Assert.That(v,Is.GreaterThanOrEqualTo(previous));previous=v;}
                Assert.That(ProductComfortPreferences.Axis(float.NaN,zone,curve),Is.Zero);
            }
        }
        [Test] public void EveryNewPageHasAnExitAndEveryPoweredActorHasBothRunwayCalls()
        {
            var world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);
            float time=Time.timeScale;
            try
            {
                var nav=world.GetComponentInChildren<ProductAppNavigation>(true);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                foreach(var page in new[]{ProductAppNavigation.Page.Tools,ProductAppNavigation.Page.Controls,ProductAppNavigation.Page.Comfort,ProductAppNavigation.Page.Briefing,ProductAppNavigation.Page.Map,ProductAppNavigation.Page.Kneeboard,ProductAppNavigation.Page.Activities,ProductAppNavigation.Page.History,ProductAppNavigation.Page.Summary,ProductAppNavigation.Page.Report})
                {
                    typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,page);
                    typeof(ProductAppNavigation).GetMethod("Refresh",flags).Invoke(nav,null);
                    var labels=(string[])typeof(ProductAppNavigation).GetField("labels",flags).GetValue(nav);
                    var actions=(int[])typeof(ProductAppNavigation).GetField("actions",flags).GetValue(nav);
                    Assert.That(labels.Count(x=>!string.IsNullOrEmpty(x)),Is.InRange(1,8),page.ToString());
                    Assert.That(actions.Any(x=>x==5||x==4),Is.True,"Exit: "+page);
                }
                var experience=world.GetComponentInChildren<ProductFlightExperience>(true);
                Assert.That(experience,Is.Not.Null);
                var radio=world.GetComponentInChildren<ProductTrafficRadio>(true);
                var calls=(ProductTrafficRadio.Call[])typeof(ProductTrafficRadio).GetField("calls",flags).GetValue(radio);
                Assert.That(calls.Length,Is.EqualTo(24));
                foreach(var actor in world.GetComponentInChildren<AmbientTrafficDirector>(true).actors.Where(a=>!a.sailplane))
                foreach(int runway in new[]{8,26})foreach(var phase in new[]{AmbientFlightPhase.HoldShort,AmbientFlightPhase.Takeoff,AmbientFlightPhase.Approach,AmbientFlightPhase.TaxiIn})
                    Assert.That(calls.Count(c=>c.aircraftId==actor.aircraftId&&c.runway==runway&&c.phase==phase&&c.clip!=null),Is.EqualTo(1));
            }
            finally{PrefabUtility.UnloadPrefabContents(world);Time.timeScale=time;}
        }
        [Test] public void GuestFlightHistoryIsBoundedAndDoesNotModifyOwnerHistory()
        {
            var world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);float time=Time.timeScale;
            try
            {
                var experience=world.GetComponentInChildren<ProductFlightExperience>(true);
                var training=world.GetComponentInChildren<QuestFlight.Product.Training.ProductTrainingController>(true);
                typeof(QuestFlight.Product.Training.ProductTrainingController).GetProperty("IsGuestSession").SetValue(training,true);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var owner=(ProductFlightExperience.History)typeof(ProductFlightExperience).GetField("owner",flags).GetValue(experience);
                owner.flights.Add(new ProductFlightExperience.Record{mode="Owner flight",outcome="Keep"});
                for(int n=0;n<25;n++)
                {
                    typeof(ProductFlightExperience).GetField("active",flags).SetValue(experience,new ProductFlightExperience.Record{mode="Guest "+n,seconds=60});
                    experience.FinishFlight("Practice ended");
                }
                var guest=(ProductFlightExperience.History)typeof(ProductFlightExperience).GetField("guest",flags).GetValue(experience);
                Assert.That(guest.flights.Count,Is.EqualTo(20));Assert.That(guest.flights[0].mode,Is.EqualTo("Guest 24"));
                Assert.That(owner.flights.Count,Is.EqualTo(1));Assert.That(owner.flights[0].outcome,Is.EqualTo("Keep"));
            }
            finally {PrefabUtility.UnloadPrefabContents(world);Time.timeScale=time;}
        }
    }
}
#endif
