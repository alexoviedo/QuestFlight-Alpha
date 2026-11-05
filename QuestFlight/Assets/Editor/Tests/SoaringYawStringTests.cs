using System;
using System.Linq;
using NUnit.Framework;
using QuestFlight.Product;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests
{
    public class SoaringYawStringTests
    {
        static readonly Vector3 Normal = new Vector3(0,1,.4f).normalized;
        static readonly Vector3 Anchor = new Vector3(0,.3f,1.5f);
        static Vector3 Flow(float speed,float beta) => SoaringYawString.LocalAirflow(speed,4,beta,Vector3.zero,Anchor);
        static SoaringYawString Settled(float speed,float beta,int hz=90)
        {
            var yarn=new SoaringYawString();
            for(int i=0;i<=hz*2;i++)yarn.Sample((double)i/hz,Flow(speed,beta),Vector3.down,Anchor,Normal);
            return yarn;
        }
        [TestCase(-20)][TestCase(-5)][TestCase(5)][TestCase(20)]
        public void TailFollowsAirNotAircraftVelocity(float beta)
        { Assert.That(Mathf.Sign((Settled(25,beta).Tip-Anchor).x),Is.EqualTo(-Mathf.Sign(beta))); }
        [Test] public void CoordinatedFlightStaysCenteredAndAft()
        { var tip=Settled(25,0).Tip-Anchor;Assert.That(Mathf.Abs(tip.x),Is.LessThan(.001f));Assert.That(tip.z,Is.LessThan(-.06f)); }
        [Test] public void AtRestLiesDownhillAndDoesNotFlutter()
        {
            var yarn=Settled(0,0);Vector3 before=yarn.Tip;
            yarn.Sample(10,Vector3.zero,Vector3.down,Anchor,Normal);
            Assert.That(Vector3.Distance(before,yarn.Tip),Is.LessThan(1e-6));Assert.That((yarn.Tip-Anchor).y,Is.LessThan(0));
        }
        [Test] public void StationaryAircraftInWindStillIndicatesAirflow()
        { var yarn=new SoaringYawString();yarn.Sample(0,new Vector3(-5,0,-8),Vector3.down,Anchor,Normal);Assert.That(yarn.Tip.x,Is.LessThan(0)); }
        [Test] public void DuplicateTimestampFreezesAndTimeReversalResets()
        {
            var yarn=Settled(25,15);Vector3 before=yarn.Tip;
            Assert.That(yarn.Sample(2,Flow(25,-15),Vector3.down,Anchor,Normal),Is.False);Assert.That(yarn.Tip,Is.EqualTo(before));
            yarn.Sample(0,Flow(25,-15),Vector3.down,Anchor,Normal);Assert.That(yarn.Tip.x,Is.GreaterThan(0));
        }
        [Test] public void TransientBendsWithoutInstantFlipOrStretch()
        {
            var yarn=Settled(25,20);var before=yarn.Tip;
            yarn.Sample(2+1.0/90,Flow(25,-20),Vector3.down,Anchor,Normal);
            Assert.That(yarn.Tip.x,Is.LessThan(0));Assert.That(Vector3.Distance(yarn.Tip,before),Is.LessThan(.02f));
            yarn.Sample(20,Flow(25,-20),Vector3.down,Anchor,Normal);
            Assert.That(yarn.Tip.x,Is.GreaterThan(0));Assert.That(Vector3.Distance(yarn.Tip,Anchor),Is.LessThanOrEqualTo(SoaringYawString.Length+1e-6));
        }
        [Test] public void RenderRatesAgree()
        { Assert.That(Vector3.Distance(Settled(25,10,60).Tip,Settled(25,10,120).Tip),Is.LessThan(.0003f)); }
        [Test] public void NativeYawRateAddsAttachmentVelocity()
        {
            var cg=SoaringYawString.LocalAirflow(25,0,0,Vector3.zero,Anchor);
            var turning=SoaringYawString.LocalAirflow(25,0,0,new Vector3(0,10,0),Anchor);
            Assert.That(turning.x,Is.LessThan(cg.x));
        }
        [Test] public void InvalidDataCannotPoisonGeometry()
        {var yarn=Settled(25,0);var tip=yarn.Tip;Assert.False(yarn.Sample(3,new Vector3(float.NaN,0,0),Vector3.down,Anchor,Normal));Assert.That(yarn.Tip,Is.EqualTo(tip));}
        [Test] public void AuthoredStringIsOnCanopyAndKeepsFrozenResources()
        {
            var root=AssetDatabase.LoadAssetAtPath<GameObject>(SoaringYawStringAuthoring.World);
            var p=root.GetComponentInChildren<SoaringInstrumentPresenter>(true);var part=p.parts.Single(x=>x.name=="YawString");
            Assert.That(part.vertexCount,Is.EqualTo(15));Assert.That(part.rotationAxis.magnitude,Is.EqualTo(1).Within(.001));
            var canopy=AssetDatabase.LoadAssetAtPath<Mesh>(SoaringAircraftAuthoring.Folder+"/SGS_Local_Canopy.asset");
            Assert.True(SoaringYawStringAuthoring.IntersectCanopy(canopy,part.anchor+part.rotationAxis*.1f,-part.rotationAxis,out var hit,out _));
            Assert.That(Vector3.Distance(hit,part.anchor),Is.InRange(.002f,.004f));
            var own=p.GetComponentsInChildren<MeshFilter>(true);Assert.That(own.Sum(x=>x.sharedMesh.triangles.Length/3),Is.EqualTo(7969));
            Assert.That(p.GetComponentsInChildren<MeshRenderer>(true).Length,Is.EqualTo(3));
        }
        [Test] public void WarmSamplingAndMeshDeformationAllocateNoManagedMemory()
        {
            var p=AssetDatabase.LoadAssetAtPath<GameObject>(SoaringYawStringAuthoring.World).GetComponentInChildren<SoaringInstrumentPresenter>(true);
            var part=p.parts.Single(x=>x.name=="YawString");var rest=p.moving.sharedMesh.vertices;var rn=p.moving.sharedMesh.normals;
            var v=(Vector3[])rest.Clone();var n=(Vector3[])rn.Clone();var yarn=new SoaringYawString();
            for(int i=0;i<100;i++){yarn.Sample(i/90.0,Flow(25,10),Vector3.down,part.anchor,part.rotationAxis);yarn.WriteVertices(part,rest,rn,v,n);}
            long before=GC.GetAllocatedBytesForCurrentThread();var watch=new System.Diagnostics.Stopwatch();
            // Allocate stopwatch before the measured section.
            before=GC.GetAllocatedBytesForCurrentThread();watch.Start();
            for(int i=100;i<10100;i++){yarn.Sample(i/90.0,Flow(25,10),Vector3.down,part.anchor,part.rotationAxis);yarn.WriteVertices(part,rest,rn,v,n);}
            watch.Stop();long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            TestContext.WriteLine($"10000 samples: {watch.Elapsed.TotalMilliseconds:F3}ms editor CPU; managed bytes={allocated}. Not Quest timing.");
            Assert.That(allocated,Is.EqualTo(0));
        }
    }
}
