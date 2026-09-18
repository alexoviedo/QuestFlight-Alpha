using NUnit.Framework;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Editor;
using UnityEditor;
using UnityEngine;

public sealed class BoulderAmbientAircraftTests
{
    [Test]
    public void LandingFlapsLowerTheImportedTrailingEdge()
    {
        var world=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");
        foreach(var source in world.GetComponentInChildren<AmbientTrafficDirector>(true).actors)
        {
            if(source.sailplane)continue;
            var go=Object.Instantiate(source.gameObject);
            try
            {
                var a=go.GetComponent<AmbientAircraftActor>();a.Initialize(8);
                int landing=System.Array.FindIndex(a.runway08.segments,s=>s.phase==AmbientFlightPhase.Landing);
                typeof(AmbientAircraftActor).GetProperty("SegmentIndex").SetValue(a,landing);
                typeof(AmbientAircraftActor).GetMethod("Present",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                    .Invoke(a,new object[]{2f,Vector3.zero,a.Position,null});
                int activeFlaps=0;
                foreach(var flap in a.flaps)
                {
                    if(!flap.gameObject.activeInHierarchy)continue;
                    activeFlaps++;
                    var mesh=flap.GetComponent<MeshFilter>().sharedMesh;
                    Vector3 edge=mesh.vertices[0];foreach(var vertex in mesh.vertices)if(vertex.z<edge.z)edge=vertex;
                    Assert.Less((flap.localRotation*edge).y,edge.y-.02f,a.aircraftId+" trailing edge must lower");
                }
                Assert.Greater(activeFlaps,0,a.aircraftId+" must expose a flap in the selected near LOD");
            }
            finally{Object.DestroyImmediate(go);}
        }
    }
    [Test]
    public void RidgeLiftHasSignedResponseSmoothEdgesAndHeightFalloff()
    {
        var source=AssetDatabase.LoadAssetAtPath<BoulderWindFieldProfile>(BoulderAmbientAircraftAuthoring.WindPath);
        var p=Object.Instantiate(source);
        try
        {
            p.minimumEnu=new Vector2(-10000,-10000);p.width=p.height=11;p.spacingMeters=100;
            p.groundHeights=new float[121];p.ridgeResponse=new float[121];p.gradientEast=new float[121];p.gradientNorth=new float[121];p.ridgeRelief=new float[121];p.exposureSectors=new float[121*8];
            for(int i=0;i<121*8;i++)p.exposureSectors[i]=1;for(int i=0;i<121;i++)p.ridgeRelief[i]=500;p.foothillThermals=System.Array.Empty<BoulderWindFieldProfile.Thermal>();p.gustAmplitude=0;
            for(int i=0;i<121;i++)p.gradientEast[i]=-.3f;
            var center=new Vector3(-9500,100,-9500);
            Assert.Greater(p.Sample(center,0,BoulderWeatherPreset.FoothillSoaring).y,.1);
            Assert.AreEqual(0,p.Sample(center+Vector3.up*600,0,BoulderWeatherPreset.FoothillSoaring).y);
            var edge=new Vector3(-10000,100,-9500);
            Assert.Less(Vector3.Distance(p.Sample(edge-Vector3.right*.1f,0,BoulderWeatherPreset.FoothillSoaring),p.Sample(edge+Vector3.right*.1f,0,BoulderWeatherPreset.FoothillSoaring)),.001);
            for(int i=0;i<121;i++)p.gradientEast[i]=.3f;
            Assert.Less(p.Sample(center,0,BoulderWeatherPreset.FoothillSoaring).y,-.1);
            p.surfaceWindEastNorth=p.ridgeWindEastNorth=Vector2.zero;
            Assert.AreEqual(Vector3.zero,p.Sample(center,0,BoulderWeatherPreset.FoothillSoaring));
        }
        finally{Object.DestroyImmediate(p);}
    }

    [Test]
    public void AuthoredWindIsRepeatableAndContinuousAcrossEveryGridSeam()
    {
        var p=AssetDatabase.LoadAssetAtPath<BoulderWindFieldProfile>(BoulderAmbientAircraftAuthoring.WindPath);
        for(int z=1;z<p.height-1;z+=3)for(int x=1;x<p.width-1;x+=3)
        {
            float east=p.minimumEnu.x+x*p.spacingMeters,north=p.minimumEnu.y+z*p.spacingMeters;
            p.SampleGrid(east,north,out float ground,out _);var at=new Vector3(east,ground+120,north);
            var w=p.Sample(at,123.45,BoulderWeatherPreset.FoothillSoaring);
            Assert.AreEqual(w,p.Sample(at,123.45,BoulderWeatherPreset.FoothillSoaring));
            Assert.Less(Vector3.Distance(p.Sample(at-Vector3.right*.01f,123.45,BoulderWeatherPreset.FoothillSoaring),p.Sample(at+Vector3.right*.01f,123.45,BoulderWeatherPreset.FoothillSoaring)),.02);
            Assert.Less(Vector3.Distance(p.Sample(at-Vector3.forward*.01f,123.45,BoulderWeatherPreset.FoothillSoaring),p.Sample(at+Vector3.forward*.01f,123.45,BoulderWeatherPreset.FoothillSoaring)),.02);
        }
    }
    [Test]
    public void DescendingTrafficWithSameGroundVelocityIsAConflict()
    {
        Assert.IsTrue(AmbientTrafficDirector.PredictConflict(new Vector3(0,100,0),new Vector3(20,-5,0),
            Vector3.zero,new Vector3(20,0,0),30,30,10));
        Assert.IsFalse(AmbientTrafficDirector.PredictConflict(new Vector3(40,100,0),new Vector3(20,-5,0),
            Vector3.zero,new Vector3(20,0,0),30,30,10));
    }
    [Test]
    public void HighSpeedCrossingCannotTunnelBetweenFrames()
    {
        Assert.IsTrue(AmbientTrafficDirector.SweptOverlap(new Vector3(-100,0,0),new Vector3(100,0,0),
            new Vector3(0,0,-100),new Vector3(0,0,100),10,5));
        Assert.IsFalse(AmbientTrafficDirector.SweptOverlap(new Vector3(-100,20,0),new Vector3(100,20,0),
            new Vector3(0,0,-100),new Vector3(0,0,100),10,5));
    }
    [Test]
    public void HorizontalAndVerticalNearMissesMustOccurAtSameTime()
    {
        Assert.IsFalse(AmbientTrafficDirector.PredictConflict(new Vector3(-100,0,0),new Vector3(10,10,0),
            Vector3.zero,Vector3.zero,20,10,10));
    }
    [Test]
    public void BothStripsAndUnexpectedOppositeDirectionFinalProtectRunway()
    {
        Assert.IsTrue(AmbientTrafficDirector.ProtectsRunway(new Vector3(0,1,-30),Vector3.zero,true));
        Assert.IsTrue(AmbientTrafficDirector.ProtectsRunway(new Vector3(0,1,30),Vector3.zero,true));
        Assert.IsTrue(AmbientTrafficDirector.ProtectsRunway(new Vector3(-1200,70,-30),new Vector3(35,0,0),false));
        Assert.IsTrue(AmbientTrafficDirector.ProtectsRunway(new Vector3(1200,70,-30),new Vector3(-35,0,0),false));
        Assert.IsFalse(AmbientTrafficDirector.ProtectsRunway(new Vector3(0,600,-30),new Vector3(35,0,0),false));
    }
    [Test]
    public void SharedWindIsIndependentOfLegacyPresetAndMapsSignedEnuToNativeNed()
    {
        var p=AssetDatabase.LoadAssetAtPath<BoulderWindFieldProfile>(BoulderAmbientAircraftAuthoring.WindPath);
        Assert.IsNotNull(p,"Run ambient aircraft authoring first."); Assert.IsTrue(p.IsValid(out string error),error);
        for(int i=0;i<100;i++)
        {
            var at=new Vector3(1500+i*4,200+i*5,2600);
            Assert.AreEqual(p.Sample(at,i,BoulderWeatherPreset.FoothillSoaring),p.Sample(at,i,BoulderWeatherPreset.Calm));
            var wind=p.Sample(at,i,BoulderWeatherPreset.FoothillSoaring);
            Assert.IsTrue(BoulderWindFieldProfile.Finite(wind));Assert.Less(wind.x,0);
            var native=FlightDynamicsCoordinator.AtmosphereFromWind(wind);
            Assert.AreEqual(wind.x,native.windEastMetersPerSecond);
            Assert.AreEqual(wind.z,native.windNorthMetersPerSecond);
            Assert.AreEqual(-wind.y,native.windDownMetersPerSecond);
        }
    }
    [Test]
    public void AuthoredRoutesAreContinuousAndHaveBoundedGeometry()
    {
        var world=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/QuestFlightLab/Production/ProductionWorldRoot.prefab");
        var traffic=world.GetComponentInChildren<AmbientTrafficDirector>(true);
        Assert.IsNotNull(traffic);Assert.IsTrue(traffic.Validate(out string error),error);
        int tris=0,draws=0;
        foreach(var a in traffic.actors)
        {
            foreach(var mesh in a.lodRoots[0].GetComponentsInChildren<MeshFilter>(true)) {tris+=mesh.sharedMesh.triangles.Length/3;draws++;}
            if(a.sailplane)continue;
            foreach(var route in new[]{a.runway08,a.runway26})
            {
                Assert.IsTrue(route.IsValid(out error),error);
                foreach(var s in route.segments)
                {
                    if(s.phase==AmbientFlightPhase.Circuit)foreach(var point in s.points)Assert.GreaterOrEqual(point.z,-31);
                    if(s.onGround)foreach(var point in s.points)
                    { Assert.IsTrue(traffic.terrain.TrySample(point.x,point.z,out var ground)); Assert.That(point.y-ground.heightMeters,Is.EqualTo(a.wheelHeight).Within(.08)); }
                }
            }
        }
        Assert.LessOrEqual(tris,22000);Assert.LessOrEqual(draws,20);
    }
}
