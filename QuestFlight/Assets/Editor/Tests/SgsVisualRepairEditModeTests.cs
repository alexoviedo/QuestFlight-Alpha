#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using QuestFlight.Product;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class SgsVisualRepairEditModeTests
    {
        private GameObject world;
        private float clock;
        private ProductFlightModeCoordinator mode;
        private ProductControlInput input;
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [SetUp] public void SetUp()
        {
            clock=Time.timeScale;Time.timeScale=0;world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);
            mode=world.GetComponentInChildren<ProductFlightModeCoordinator>(true);input=mode.AuthoredProductInput;
            Assert.IsTrue(mode.AircraftPresentation.Apply(mode.AircraftPresentation.glider));
            input.ConfigureAircraft(mode.AircraftPresentation.glider);Pause(false);
            typeof(ProductControlInput).GetField("_touchControlsRequireRelease",Private).SetValue(input,false);
            typeof(ProductControlInput).GetField("_flightLessonInputSuppressed",Private).SetValue(input,false);
        }
        [TearDown] public void TearDown(){PrefabUtility.UnloadPrefabContents(world);Time.timeScale=clock;}
        [Test] public void AuthoredPresentationKeepsFrozenResourceLimitsAndSmoothNormals()
        {
            var root=mode.AircraftPresentation.gliderRoot;
            var filters=root.GetComponentsInChildren<MeshFilter>(true);
            Assert.AreEqual(3,filters.Length);Assert.LessOrEqual(filters.Sum(f=>f.sharedMesh.triangles.Length/3),8000);
            var materials=root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
            Assert.AreEqual(2,materials.Length);
            foreach(var m in materials)foreach(string property in m.GetTexturePropertyNames())Assert.IsNull(m.GetTexture(property),property);
            Assert.AreEqual("QuestFlight/SGS Reference Surface",filters.Single(f=>f.name=="AirframeAndInstruments").GetComponent<Renderer>().sharedMaterial.shader.name);
            int smoothCorners=0;
            foreach(var f in filters)
            {
                var mesh=f.sharedMesh;var v=mesh.vertices;var n=mesh.normals;var t=mesh.triangles;
                Assert.AreEqual(v.Length,n.Length);
                for(int i=0;i<n.Length;i++)Assert.That(n[i].sqrMagnitude,Is.InRange(.98f,1.02f),mesh.name+" vertex "+i+" position "+v[i]);
                for(int k=0;k<t.Length;k+=3)
                {var face=Vector3.Cross(v[t[k+1]]-v[t[k]],v[t[k+2]]-v[t[k]]).normalized;if(Vector3.Dot(face,n[t[k]])<.999f)smoothCorners++;}
            }
            Assert.Greater(smoothCorners,1000,"Curved surfaces must retain split vertex normals rather than per-triangle faceting.");
        }
        [Test] public void LiftingSurfacesHaveThicknessAndPublishedSpan()
        {
            var all=mode.AircraftPresentation.gliderRoot.GetComponentsInChildren<MeshFilter>(true).SelectMany(f=>f.sharedMesh.vertices).ToArray();
            Assert.That(all.Max(v=>v.x)-all.Min(v=>v.x),Is.EqualTo(15.5448f).Within(.005f));
            var hull=mode.AircraftPresentation.gliderRoot.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name=="AirframeAndInstruments").sharedMesh.vertices;
            var wing=hull.Where(v=>Mathf.Abs(v.x-1.6f)<.0001f).ToArray();
            Assert.Greater(wing.Length,20);Assert.Greater(wing.Max(v=>v.y)-wing.Min(v=>v.y),.12f);
            var mesh=mode.AircraftPresentation.instruments.moving.sharedMesh.vertices;
            foreach(string name in new[]{"Elevator","Rudder"})
            {
                var part=mode.AircraftPresentation.instruments.parts.Single(p=>p.name==name);
                var vertices=mesh.Skip(part.firstVertex).Take(part.vertexCount).ToArray();
                float thickness=name=="Rudder"?vertices.Max(v=>v.x)-vertices.Min(v=>v.x):vertices.Max(v=>v.y)-vertices.Min(v=>v.y);
                Assert.Greater(thickness,.025f,name+" must have a shaped section, not a flat polygon.");
            }
            foreach(string name in new[]{"LeftAileron","RightAileron","Rudder"})
                Assert.Greater(mode.AircraftPresentation.instruments.parts.Single(p=>p.name==name).rotationAxis.sqrMagnitude,.9f);
        }
        [Test] public void BalancedDiveBrakesFollowWingHingesAndOpenOnOppositeSides()
        {
            var presenter=mode.AircraftPresentation.instruments;var vertices=presenter.moving.sharedMesh.vertices;
            var brakes=presenter.parts.Where(p=>p.name.EndsWith("UpperBrake")||p.name.EndsWith("LowerBrake")).ToArray();
            Assert.AreEqual(4,brakes.Length,"Exactly one upper/lower pair per wing; old slabs must be replaced.");
            foreach(var part in brakes)
            {
                bool upper=part.name.Contains("Upper");
                Assert.Greater(part.rotationAxis.x,.99f);
                var points=vertices.Skip(part.firstVertex).Take(part.vertexCount).ToArray();
                Assert.That(points.Max(p=>Mathf.Abs(p.x))-points.Min(p=>Mathf.Abs(p.x)),Is.EqualTo(1.143f).Within(.002f));
                Assert.Less(points.Max(p=>p.z)-points.Min(p=>p.z),.17f,"Narrow sheet, not the old0.28m slab.");
                var tip=points.OrderByDescending(p=>Mathf.Abs(p.z-part.anchor.z)).First();
                float previous=0;
                for(int step=0;step<=10;step++)
                {
                    var q=SoaringInstrumentPresenter.AirbrakeRotation(part,step/10f);
                    var posed=part.anchor+q*(tip-part.anchor);
                    float opening=(posed.y-tip.y)*(upper?1:-1);
                    Assert.GreaterOrEqual(opening,previous-.0001f);previous=opening;
                    Assert.That(Vector3.Distance(posed,part.anchor),Is.EqualTo(Vector3.Distance(tip,part.anchor)).Within(.0001f));
                }
                Assert.Greater(previous,.12f);
                Assert.That(Quaternion.Angle(Quaternion.identity,SoaringInstrumentPresenter.AirbrakeRotation(part,1)),Is.EqualTo(upper?85:75).Within(.01f));
            }
        }
        [TestCase("TrimLever",30f,true)]
        [TestCase("FrontStick",18f,true)]
        public void GripAndVisiblePartStayTogetherAndWithinReferenceReach(string name,float angle,bool signed)
        {
            var part=mode.AircraftPresentation.instruments.parts.Single(p=>p.name==name);
            Assert.NotNull(part.grabTarget);Assert.AreEqual(part.gripRestPosition,part.grabTarget.localPosition);
            var eye=mode.AircraftPresentation.glider.eyeFromCgMeters;
            var shoulder=eye+new Vector3(-.16f,-.20f,-.08f);
            for(int step=0;step<=20;step++)
            {
                float value=Mathf.Lerp(signed?-1:0,1,step/20f);
                Vector3 posed=SoaringInstrumentPresenter.GripPosition(part,Quaternion.Euler(-angle*value,0,0),Vector3.zero);
                Assert.Less(Vector3.Distance(shoulder,posed),.68f,"Engineering reach estimate only; human comfort remains unmeasured.");
            }
            Assert.AreEqual(1,part.grabTarget.GetComponents<Grabbable>().Length);
            Assert.AreEqual(1,part.grabTarget.GetComponentsInChildren<HandGrabInteractable>(true).Length);
            Assert.IsTrue(part.grabTarget.GetComponent<Rigidbody>().isKinematic);
        }
        [TestCase(.155f,30f,true)]
        public void RegrabArcMappingRoundTripsTheFullTravel(float radius,float degrees,bool signed)
        {
            for(int from=0;from<=10;from++)for(int to=0;to<=10;to++)
            {
                float a=Mathf.Lerp(signed?-1:0,1,from/10f),b=Mathf.Lerp(signed?-1:0,1,to/10f);
                float pull=radius*(Mathf.Sin(b*degrees*Mathf.Deg2Rad)-Mathf.Sin(a*degrees*Mathf.Deg2Rad));
                Assert.That(SoaringLeverGrabTransformer.ValueFromPull(a,pull,radius,degrees,signed),Is.EqualTo(b).Within(.00001));
            }
        }
        [Test] public void BrakeHandleIsSidewallMountedLinearAndReachableThroughoutTravel()
        {
            var p=mode.AircraftPresentation.instruments;
            var part=p.parts.Single(x=>x.name=="BrakeLever");
            var points=p.moving.sharedMesh.vertices.Skip(part.firstVertex).Take(part.vertexCount).ToArray();
            Assert.Greater(points.Max(v=>v.z)-points.Min(v=>v.z),.50f,"Fore-aft shaft replaces upright stalk.");
            Assert.Less(points.Max(v=>v.y)-points.Min(v=>v.y),.13f);
            Assert.Less(part.anchor.x,-.28f);Assert.Greater(part.gripRestPosition.x,-.25f);
            Assert.Less(part.gripRestPosition.y,.22f,"Handle center belongs below the panel lower edge.");
            Assert.Less(part.gripRestPosition.y,mode.AircraftPresentation.glider.eyeFromCgMeters.y-.30f);
            var t=part.grabTarget.GetComponent<SoaringLeverGrabTransformer>();
            Assert.IsTrue(t.linearTravel);Assert.AreEqual(SoaringInstrumentPresenter.BrakeLeverTravelMeters,t.fullTravelMeters);
            Assert.AreEqual(1,part.grabTarget.GetComponentsInChildren<HandGrabInteractable>(true).Length);
            var trim=p.parts.Single(x=>x.name=="TrimLever");
            for(int step=0;step<=20;step++)
            {
                float value=step/20f;
                var q=SoaringInstrumentPresenter.BrakeLeverRotation(value);
                var grip=SoaringInstrumentPresenter.GripPosition(part,q,SoaringInstrumentPresenter.BrakeLeverDisplacement(value));
                Assert.That(part.gripRestPosition.z-grip.z,Is.EqualTo(.23f*value).Within(.0001f));
                foreach(float foreAft in new[]{-.04f,0f,.04f})foreach(float height in new[]{-.04f,0f,.04f})
                {
                    var shoulder=mode.AircraftPresentation.glider.eyeFromCgMeters+new Vector3(-.16f,-.20f+height,-.08f+foreAft);
                    Assert.Less(Vector3.Distance(shoulder,grip),.60f,"Reference reach envelope, not human acceptance.");
                }
                for(int trimStep=0;trimStep<=20;trimStep++)
                {
                    var other=SoaringInstrumentPresenter.GripPosition(trim,Quaternion.Euler(-30f*(trimStep/10f-1),0,0),Vector3.zero);
                    Assert.Greater(Vector3.Distance(grip,other),.10f,"Brake and trim hand targets must remain distinct.");
                }
            }
        }
        [Test] public void BrakeLinearRegrabAndWheelBrakeEndTravelStayConsistent()
        {
            var evidence=new List<string>();
            for(int from=0;from<=10;from++)for(int to=0;to<=10;to++)
            {
                float a=from/10f,b=to/10f;
                Assert.That(SoaringLeverGrabTransformer.ValueFromLinearPull(a,(b-a)*.23f,.23f),Is.EqualTo(b).Within(.00001));
            }
            foreach(float target in new[]{.425f,.85f,.925f,1f,.925f,.85f,.425f,0f})
            {
                float baseline=input.GliderLeverPosition;
                var(t,g)=Begin("AirbrakeGrab");var pose=g.GrabPoints[0];
                pose.position-=t.stableFrame.forward*((target-baseline)*t.fullTravelMeters);g.GrabPoints[0]=pose;t.UpdateTransform();
                // World-space SDK poses are floats hundreds of metres from origin;
                // this tolerance is less than0.12mm along the230mm handle stroke.
                Assert.That(input.GliderLeverPosition,Is.EqualTo(target).Within(.0005));t.EndTransform();
                ProductControlInput.ResolveGliderLever(input.GliderLeverPosition,out float air,out float wheel);
                Assert.That(input.Current.airbrake,Is.EqualTo(air).Within(.00001),"Published flight input must follow the actual grab.");
                Assert.That(input.Current.wheelBrake,Is.EqualTo(wheel).Within(.00001));
                Assert.That(air,Is.EqualTo(Mathf.Clamp01(target/.85f)).Within(.0006));
                Assert.That(wheel,Is.EqualTo(Mathf.Clamp01((target-.85f)/.15f)).Within(.004));
                evidence.Add(FormattableString.Invariant($"{{\"lever\":{input.GliderLeverPosition:R},\"airbrake\":{input.Current.airbrake:R},\"wheelBrake\":{input.Current.wheelBrake:R}")+"}");
            }
            string output=System.Environment.GetEnvironmentVariable("QF_SGS_LEVER_MAPPING_EVIDENCE");
            if(!string.IsNullOrWhiteSpace(output))
            {
                Assert.IsTrue(System.IO.Path.IsPathRooted(output));
                System.IO.File.WriteAllText(output,"{\"source\":\"Actual SDK transformer and published ProductControlInput\",\"samples\":["+string.Join(",",evidence)+"]}");
            }
        }
        private sealed class GrabFixture:IGrabbable
        {public List<Pose> GrabPoints{get;}=new List<Pose>();public Transform Transform{get;set;}}
        [Test] public void SidewallTrimRegrabTracksNativeInputInBothDirections()
        {
            var part=mode.AircraftPresentation.instruments.parts.Single(p=>p.name=="TrimLever");
            Assert.Less(part.anchor.x,-.30f);Assert.Less(part.gripRestPosition.x,-.26f);
            Assert.That(part.gripRestPosition.y-part.anchor.y,Is.EqualTo(.155f).Within(.0001));
            Assert.Greater(part.gripRestPosition.x-part.anchor.x,.03f,"Bent grip projects into the cockpit.");
            foreach(float target in new[]{-1f,-.4f,.6f,1f,.1f,-1f})
            {
                float baseline=input.Current.trim;var(t,g)=Begin("TrimGrab");var pose=g.GrabPoints[0];
                float pull=t.gripRadiusMeters*(Mathf.Sin(target*t.maximumAngleDegrees*Mathf.Deg2Rad)-Mathf.Sin(baseline*t.maximumAngleDegrees*Mathf.Deg2Rad));
                pose.position-=t.stableFrame.forward*pull;g.GrabPoints[0]=pose;t.UpdateTransform();
                Assert.That(input.Current.trim,Is.EqualTo(target).Within(.001));t.EndTransform();
            }
        }
        [Test] public void ResearchedPanelUsesCompassCardAndCorrectInstrumentScales()
        {
            var parts=mode.AircraftPresentation.instruments.parts;
            Assert.IsFalse(parts.Any(p=>p.name=="HeadingNeedle"));
            var compass=parts.Single(p=>p.name=="CompassCard");
            Assert.Greater(compass.rotationAxis.y,.99f);
            Assert.Less(parts.Single(p=>p.name=="AltitudeNeedle").anchor.x,0);
            Assert.Less(parts.Single(p=>p.name=="VarioNeedle").anchor.x,0);
            Assert.Greater(parts.Single(p=>p.name=="AsiNeedle").anchor.x,0);
            Assert.AreEqual(1,parts.Count(p=>p.name=="AltitudeTenThousands"));
            var hull=mode.AircraftPresentation.gliderRoot.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name=="AirframeAndInstruments").sharedMesh.vertices;
            Assert.IsTrue(hull.Any(v=>Mathf.Abs(v.x)<.001f&&v.z>1.65f&&v.z<1.72f&&v.y>.39f&&v.y<.42f),"Closed forward coaming must be serialized.");
            // Native knots converted to the MPH face, including above100mph.
            Assert.That(SoaringInstrumentPresenter.AsiAngle(60/1.15077945f),Is.EqualTo(-115).Within(.001));
            Assert.That(SoaringInstrumentPresenter.AsiAngle(120/1.15077945f),Is.EqualTo(-250).Within(.001));
            Assert.AreEqual(90,SoaringInstrumentPresenter.VsiAngle(0));
            Assert.That(SoaringInstrumentPresenter.VsiAngle(500),Is.EqualTo(30).Within(.0001));
            Assert.That(SoaringInstrumentPresenter.VsiAngle(-500),Is.EqualTo(150).Within(.0001));
            foreach(float cardinal in new[]{0f,90f,180f,270f})
            {
                float magnetic=SoaringInstrumentPresenter.CompassHeading(cardinal+7.66f);
                Assert.That(magnetic,Is.EqualTo(cardinal).Within(.001));
                // A heading's printed card sector must rotate to the fixed front window.
                float a=cardinal*Mathf.Deg2Rad;
                var label=new Vector3(Mathf.Sin(a),0,-Mathf.Cos(a));
                Assert.Less(Vector3.Distance(Quaternion.Euler(0,magnetic,0)*label,Vector3.back),.0001f);
            }
        }
        private void Pause(bool value)=>typeof(ProductControlInput).GetProperty("Paused").SetValue(input,value);
        private (SoaringLeverGrabTransformer,GrabFixture) Begin(string grip)
        {
            var t=mode.AircraftPresentation.gliderRoot.transform.Find(grip).GetComponent<SoaringLeverGrabTransformer>();
            t.EditorVerificationFocus=true;var g=new GrabFixture{Transform=t.transform};g.GrabPoints.Add(new Pose(t.transform.position,Quaternion.identity));
            t.Initialize(g);t.BeginTransform();return(t,g);
        }
        [Test] public void SdkTransformerOwnsUpdatesAndReleasesOnFocusPauseOrTrackingLoss()
        {
            foreach(int reason in new[]{0,1,2})
            {
                Pause(false);var(t,g)=Begin("AirbrakeGrab");Assert.AreEqual(1,input.ActiveTouchOverrideCount);
                var pose=g.GrabPoints[0];pose.position-=t.stableFrame.forward*.12f;g.GrabPoints[0]=pose;t.UpdateTransform();
                Assert.Greater(input.GliderLeverPosition,.25f);
                Assert.IsFalse(input.TryBeginTouchOverride(t.channel,999,1),"A second owner cannot steal a selected lever.");
                if(reason==0)t.EditorVerificationFocus=false;else if(reason==1)Pause(true);else g.GrabPoints.Clear();
                t.UpdateTransform();Assert.AreEqual(0,input.ActiveTouchOverrideCount);
            }
        }
        [Test] public void ReleaseGripQueuesOnlyOneEdgeAndCannotOperateWhenPausedOrOnC172()
        {
            var(t,g)=Begin("ReleaseGrab");var pose=g.GrabPoints[0];pose.position-=t.stableFrame.forward*.04f;g.GrabPoints[0]=pose;
            t.UpdateTransform();Assert.IsTrue(input.ConsumeTowRelease());t.UpdateTransform();Assert.IsFalse(input.ConsumeTowRelease());t.EndTransform();
            Pause(true);(t,g)=Begin("ReleaseGrab");pose=g.GrabPoints[0];pose.position-=t.stableFrame.forward*.04f;g.GrabPoints[0]=pose;t.UpdateTransform();Assert.IsFalse(input.ConsumeTowRelease());
            Pause(false);input.ConfigureAircraft(FlightAircraftDefinition.EditorC172);(t,g)=Begin("ReleaseGrab");pose=g.GrabPoints[0];pose.position-=t.stableFrame.forward*.04f;g.GrabPoints[0]=pose;t.UpdateTransform();Assert.IsFalse(input.ConsumeTowRelease());
        }
        [Test] public void BothAuthoredPrefabsHaveTheSameGripsAndNoDuplicateAuthority()
        {
            var other=PrefabUtility.LoadPrefabContents("Assets/Production/Prefabs/ProductionC172AircraftRig.prefab");
            try
            {
                var p=other.GetComponentInChildren<SoaringInstrumentPresenter>(true);
                foreach(var part in mode.AircraftPresentation.instruments.parts.Where(p=>p.grabTarget!=null))
                {var match=p.parts.Single(x=>x.name==part.name);Assert.AreEqual(part.gripRestPosition,match.gripRestPosition);Assert.NotNull(match.grabTarget);}
                Assert.AreEqual(1,world.GetComponentsInChildren<FlightDynamicsCoordinator>(true).Length);
                Assert.AreEqual(1,other.GetComponentsInChildren<FlightDynamicsCoordinator>(true).Length);
            }
            finally{PrefabUtility.UnloadPrefabContents(other);}
        }
    }
}
#endif
