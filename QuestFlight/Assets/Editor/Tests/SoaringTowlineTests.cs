using System;
using AlexOviedo.QuestFlight.JSBSim;
using NUnit.Framework;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class SoaringTowlineTests
    {
        [Test] public void SlackHasNoDampingForceAndReleaseKeepsCorrectEnd()
        {
            var d=ScriptableObject.CreateInstance<TowlineDefinition>();
            try
            {
                var rope=new TowlineSolver(d);rope.HookUp();
                var g=new JSBSimNativeAttachmentState{positionEcefXM=6378137};
                var t=new JSBSimNativeAttachmentState{positionEcefXM=6378137+59,velocityEcefXMps=50};
                Assert.That(rope.Evaluate(g,t,out var s),Is.True);Assert.That(s.tensionNewtons,Is.Zero);Assert.That(s.state,Is.EqualTo(TowlineState.HookedSlack));
                t.positionEcefXM+=1.1;t.velocityEcefXMps=0;
                Assert.That(rope.Evaluate(g,t,out s),Is.True);Assert.That(s.tensionNewtons,Is.EqualTo(800).Within(.01));Assert.That(s.forceEcefXNewtons,Is.EqualTo(s.tensionNewtons));
                rope.ReleaseGlider();Assert.That(rope.Evaluate(g,t,out s),Is.True);Assert.That(s.tensionNewtons,Is.Zero);Assert.That(s.gliderEndAttached,Is.False);Assert.That(s.tugEndAttached,Is.True);
                Assert.Throws<InvalidOperationException>(()=>rope.HookUp());int id=s.eventId;rope.ReleaseGlider();rope.Evaluate(g,t,out s);Assert.That(s.eventId,Is.EqualTo(id));
            }
            finally{UnityEngine.Object.DestroyImmediate(d);}
        }
        [Test] public void PhysicalWeakLinkPrecedesExtensionGuardAndNanDoesNotMasqueradeAsBreak()
        {
            var d=ScriptableObject.CreateInstance<TowlineDefinition>();
            try
            {
                var rope=new TowlineSolver(d);rope.HookUp();var g=new JSBSimNativeAttachmentState();var t=new JSBSimNativeAttachmentState{positionEcefXM=63};
                Assert.That(rope.Evaluate(g,t,out var s),Is.True);Assert.That(s.state,Is.EqualTo(TowlineState.BrokenAtGlider));Assert.That(s.trialTensionNewtons,Is.EqualTo(24000));Assert.That(s.tugEndAttached,Is.True);Assert.That(s.tensionNewtons,Is.Zero);
                rope=new TowlineSolver(d);rope.HookUp();t.positionEcefXM=double.NaN;Assert.That(rope.Evaluate(g,t,out s),Is.False);Assert.That(rope.State,Is.EqualTo(TowlineState.HookedSlack));
            }
            finally{UnityEngine.Object.DestroyImmediate(d);}
        }
        [Test] public void CheckedClockStopsImmediatelyAndRetainsEveryUnintegratedSecond()
        {
            var a=new FixedStepAccumulator(1d/120,8);int callbacks=0;
            Assert.That(a.ConsumeChecked(.04,h=>++callbacks<2,out int completed),Is.False);
            Assert.That(callbacks,Is.EqualTo(2));Assert.That(completed,Is.EqualTo(1));Assert.That(a.BacklogSeconds,Is.EqualTo(.04-1d/120).Within(1e-12));Assert.That(a.DroppedSeconds,Is.Zero);
            a.Reset();Assert.That(a.ConsumeChecked(.5,h=>true,out completed),Is.False);Assert.That(completed,Is.EqualTo(8));Assert.That(a.CapacityExceeded,Is.True);Assert.That(a.BacklogSeconds,Is.EqualTo(.5-8d/120).Within(1e-12));Assert.That(a.DroppedSeconds,Is.Zero);
        }
        [Test] public void DifferentRenderSchedulesPreserveIdenticalNativeTickCount()
        {
            foreach(int hz in new[]{30,36,60,72,90,120})
            {
                var a=new FixedStepAccumulator(1d/120,8);int ticks=0;
                for(int frame=0;frame<hz*10;frame++)Assert.That(a.ConsumeChecked(1d/hz,h=>{ticks++;return true;},out _),Is.True);
                Assert.That(ticks,Is.EqualTo(1200));Assert.That(a.DroppedSeconds,Is.Zero);Assert.That(a.BacklogSeconds,Is.EqualTo(0).Within(1e-10));
            }
        }
    }
}
