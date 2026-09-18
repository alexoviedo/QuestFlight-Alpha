using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using QuestFlightLab.Environment;
using QuestFlightLab.Editor;
using QuestFlightLab.Flight.Backends;
public class SharedAtmosphereTests
{
    [Test] public void SharedSamplerHasNoSteadyManagedAllocations()
    {
        var p=AssetDatabase.LoadAssetAtPath<BoulderWindFieldProfile>(BoulderAmbientAircraftAuthoring.WindPath);
        var at=new Vector3(-6150,400,1600);
        for(int i=0;i<100;i++)p.Sample(at,i);
        long before=System.GC.GetAllocatedBytesForCurrentThread();
        Vector3 total=Vector3.zero;
        for(int i=0;i<1000;i++)total+=p.Sample(at,i*.01);
        long allocated=System.GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.AreEqual(0,allocated);Assert.IsTrue(BoulderWindFieldProfile.Finite(total));
    }
    [Test] public void WindDirectionReversesRidgeLiftAndParallelWindHasNoDeflection()
    {
        var p=Object.Instantiate(AssetDatabase.LoadAssetAtPath<BoulderWindFieldProfile>(BoulderAmbientAircraftAuthoring.WindPath));
        try
        {
            p.thermalStrength=0;p.gustAmplitude=0;
            for(int i=0;i<p.gradientEast.Length;i++){p.gradientEast[i]=.3f;p.gradientNorth[i]=0;p.ridgeRelief[i]=500;p.groundHeights[i]=0;}
            for(int i=0;i<p.exposureSectors.Length;i++)p.exposureSectors[i]=1;
            var at=new Vector3(0,100,0);
            p.surfaceWindEastNorth=p.ridgeWindEastNorth=new Vector2(5,0);
            float lift=p.Sample(at,10).y;Assert.Greater(lift,.5);
            p.surfaceWindEastNorth=p.ridgeWindEastNorth=new Vector2(-5,0);
            Assert.That(p.Sample(at,10).y,Is.EqualTo(-lift).Within(.0001));
            p.surfaceWindEastNorth=p.ridgeWindEastNorth=new Vector2(0,5);
            Assert.That(p.Sample(at,10).y,Is.EqualTo(0).Within(.0001));
            p.surfaceWindEastNorth=p.ridgeWindEastNorth=Vector2.zero;
            Assert.AreEqual(Vector3.zero,p.Sample(at,10));
        }
        finally{Object.DestroyImmediate(p);}
    }
    [Test] public void MissingDirectionalDataFailsValidation()
    {
        var p=Object.Instantiate(AssetDatabase.LoadAssetAtPath<BoulderWindFieldProfile>(BoulderAmbientAircraftAuthoring.WindPath));
        try{p.exposureSectors=System.Array.Empty<float>();Assert.IsFalse(p.IsValid(out _));}
        finally{Object.DestroyImmediate(p);}
    }
    [Test] public void TurbulenceHasThreeComponentsAndZeroWindIsNotArtificiallyTurbulent()
    {
        var p=Object.Instantiate(AssetDatabase.LoadAssetAtPath<BoulderWindFieldProfile>(BoulderAmbientAircraftAuthoring.WindPath));
        try
        {
            Vector3 sums=Vector3.zero;for(int t=0;t<100;t++){var g=p.SampleComponents(new Vector3(0,500,0),t).gust;sums+=new Vector3(Mathf.Abs(g.x),Mathf.Abs(g.y),Mathf.Abs(g.z));}
            Assert.Greater(sums.x,1);Assert.Greater(sums.y,1);Assert.Greater(sums.z,1);
            p.surfaceWindEastNorth=p.ridgeWindEastNorth=Vector2.zero;
            Assert.AreEqual(Vector3.zero,p.SampleComponents(new Vector3(0,500,0),10).gust);
        }
        finally{Object.DestroyImmediate(p);}
    }
    [Test] public void CloudRecyclingIsHiddenAndCoveragePersistsForOneHour()
    {
        var world=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");
        var cloud=world.GetComponentInChildren<BoulderCloudField>(true);
        for(int t=0;t<=3600;t+=10)
        {
            int visible=0;
            foreach(var v in cloud.Profile.Volumes)
            {
                var c=BoulderCloudField.WrappedCenter(v.centerLocalMeters,new Vector3(-5.5f*t,0,0),Vector3.zero);
                if(Mathf.Max(Mathf.Abs(c.x),Mathf.Abs(c.z))<12000)visible++;
            }
            Assert.Greater(visible,0,"No full-strength formations at t="+t);
        }
        var before=BoulderCloudField.WrappedCenter(new Vector3(15999,1000,0),Vector3.zero,Vector3.zero);
        var after=BoulderCloudField.WrappedCenter(new Vector3(16001,1000,0),Vector3.zero,Vector3.zero);
        Assert.Greater(Mathf.Abs(before.x),15000);Assert.Greater(Mathf.Abs(after.x),15000);
    }
    [Test] public void ProductAtmosphereIsRequiredAndAirborneEncounterIsAuthored()
    {
        var world=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");
        var player=world.GetComponentInChildren<FlightDynamicsCoordinator>(true);
        Assert.IsNotNull(player.windField);Assert.IsTrue(player.windField.IsValid(out string error),error);
        var actor=world.GetComponentInChildren<AmbientTrafficDirector>(true).actors[0];
        Assert.IsFalse(actor.runway08.segments[actor.initialSegment].onGround);
        Assert.IsFalse(actor.runway26.segments[actor.initialSegment].onGround);
    }
}
