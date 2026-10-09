#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlight.Product;
using QuestFlight.Rendering;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class RecurringCostPolicyTests
    {
        static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Set(object target,string name,object value) { target.GetType().GetField(name,Fields).SetValue(target,value); }

        [Test]
        public void SaturatedAndFailedTraceDoesNotFormatFurtherEvents()
        {
            var fixture=new GameObject("Inactive trace measurement fixture");fixture.SetActive(false);
            var trace=fixture.AddComponent<OvrConcurrentInputTrace>();
            var instance=typeof(OvrConcurrentInputTrace).GetField("_instance",BindingFlags.Static|BindingFlags.NonPublic);
            instance.SetValue(null,trace);
            string path=Path.Combine(Path.GetTempPath(),"QuestFlight_trace_profile_"+Guid.NewGuid().ToString("N"));
            try
            {
                long calibrationBefore=GC.GetAllocatedBytesForCurrentThread();
                byte[] allocationCalibration=new byte[4096];GC.KeepAlive(allocationCalibration);
                bool allocationCounterValidated=GC.GetAllocatedBytesForCurrentThread()>calibrationBefore;
                Set(trace,"_path",path);
                var enabled=typeof(OvrConcurrentInputTrace).GetField("traceRecordingEnabled",Fields);
                enabled?.SetValue(trace,true);
                Set(trace,"acceptedTraceBytes",256L*1024);
                for(int i=0;i<1000;i++)OvrConcurrentInputTrace.RecordTransformer(default,"Move",false,2,.3f,.5f,"rejected");
                long before=GC.GetAllocatedBytesForCurrentThread();
                var clock=System.Diagnostics.Stopwatch.StartNew();
                for(int i=0;i<10000;i++)OvrConcurrentInputTrace.RecordTransformer(default,"Move",false,2,.3f,.5f,"rejected");
                clock.Stop();
                long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                Debug.Log("[M16][HostOnly] saturated10000EventsAllocationCounterValidated="+allocationCounterValidated+" counterDelta="+allocated+" elapsedTicks="+clock.ElapsedTicks+" formattedRecords="+trace.FormattedRecordCount);
                Assert.AreEqual(0,trace.FormattedRecordCount,"Event formatting continues after saturation");
                if(allocationCounterValidated)Assert.LessOrEqual(allocated,128,"Event formatting continues after the trace cap (allow only Stopwatch measurement allocation)");
                Set(trace,"acceptedTraceBytes",0L);Set(trace,"traceWriteFailed",true);
                before=GC.GetAllocatedBytesForCurrentThread();
                for(int i=0;i<10000;i++)OvrConcurrentInputTrace.RecordTransformer(default,"Move",false,2,.3f,.5f,"rejected");
                Assert.AreEqual(0,trace.FormattedRecordCount,"Permanent write failure must be checked before formatting");
                if(allocationCounterValidated)Assert.AreEqual(0,GC.GetAllocatedBytesForCurrentThread()-before);
            }
            finally {instance.SetValue(null,null);UnityEngine.Object.DestroyImmediate(fixture);if(File.Exists(path))File.Delete(path);}
        }

        [Test]
        public void TraceRecordingOptInAndWriteFailureNeverDisableTheInputOwner()
        {
            var fixture=new GameObject("Inactive trace policy fixture");fixture.SetActive(false);
            var trace=fixture.AddComponent<OvrConcurrentInputTrace>();
            var instance=typeof(OvrConcurrentInputTrace).GetField("_instance",BindingFlags.Static|BindingFlags.NonPublic);
            instance.SetValue(null,trace);
            string path=Path.Combine(Path.GetTempPath(),"QuestFlight_trace_policy_"+Guid.NewGuid().ToString("N"));
            try
            {
                Set(trace,"_path",path);
                Assert.IsFalse(trace.DiagnosticsRecordingEnabled);
                for(int i=0;i<1000;i++)OvrConcurrentInputTrace.RecordTransformer(default,"Move",false,2,.3f,.5f,"rejected");
                Assert.AreEqual(0,trace.FormattedRecordCount);Assert.IsFalse(File.Exists(path));Assert.IsTrue(trace.enabled);
                trace.ConfigureDiagnosticsRecording(true);
                Assert.IsTrue(trace.DiagnosticsRecordingEnabled,"Editor is a development environment");
                OvrConcurrentInputTrace.RecordTransformer(default,"Move",false,2,.3f,.5f,"rejected");
                Assert.AreEqual(1,trace.FormattedRecordCount);
                // A directory cannot be appended as a trace file; inject the
                // real filesystem failure without changing input activation.
                Set(trace,"_path",Path.GetTempPath());
                typeof(OvrConcurrentInputTrace).GetMethod("FlushTrace",Fields).Invoke(trace,null);
                long count=trace.FormattedRecordCount;
                for(int i=0;i<1000;i++)OvrConcurrentInputTrace.RecordTransformer(default,"Move",false,2,.3f,.5f,"rejected");
                Assert.AreEqual(count,trace.FormattedRecordCount);Assert.IsTrue(trace.enabled);
                trace.ConfigureDiagnosticsRecording(false);Assert.IsTrue(trace.enabled);
            }
            finally {instance.SetValue(null,null);UnityEngine.Object.DestroyImmediate(fixture);if(File.Exists(path))File.Delete(path);}
        }

        [Test]
        public void ActualAuthoredMenuSuppressesDrawCallbacksPreservesPrepareAndRestoresPreview()
        {
            GameObject world=PrefabUtility.LoadPrefabContents(ProductFlightModesFoundationAuthoring.WorldPrefabPath);
            GameObject cameraObject=new GameObject("Procedural eligibility test camera");
            var camera=cameraObject.AddComponent<Camera>();
            int prepare=0,color=0,motion=0;
            Camera.CameraCallback callback=unused=> {if(ProductRenderSubmission.Preparing)prepare++;else if(ProductRenderSubmission.MotionPass)motion++;else color++;};
            ProductRenderSubmission.Draw+=callback;
            try
            {
                var menu=world.GetComponentInChildren<ProductFlightModeMenuView>(true);
                Assert.IsNotNull(menu);Assert.IsTrue(menu.HasAuthoredReferences);
                menu.ShowMainSelection();
                ProductRenderSubmission.Prepare(camera);ProductRenderSubmission.Execute(null,camera,false);ProductRenderSubmission.Execute(null,camera,true);
                Assert.AreEqual(1,prepare,"Bounded streaming/prewarming is retained");Assert.AreEqual(0,color);Assert.AreEqual(0,motion);
                foreach(string preview in new[]{"Calibration","InstrumentHelp"})
                {
                    menu.SetSelectionPresentation(true,null);
                    Assert.IsTrue(menu.WorldPresentationEligible,preview);
                    ProductRenderSubmission.Execute(null,camera,false);Assert.Greater(color,0,preview);
                    menu.SetSelectionPresentation(false,null);
                    int before=color;ProductRenderSubmission.Execute(null,camera,false);Assert.AreEqual(before,color,preview+" returns to hidden showroom");
                }
                menu.CompleteTransitionForVisualQa();
                int transitionBefore=color;ProductRenderSubmission.Execute(null,camera,false);
                Assert.AreEqual(transitionBefore+1,color,"First revealed cockpit frame is eligible");
            }
            finally
            {
                ProductRenderSubmission.Draw-=callback;ProductRenderSubmission.SetWorldPresentationVisible(true);
                UnityEngine.Object.DestroyImmediate(cameraObject);PrefabUtility.UnloadPrefabContents(world);
            }
        }

        [Test]
        public void TraceInitializationIOExceptionLeavesInputOwnerEnabledAndFormattingDisabled()
        {
            var fixture=new GameObject("Inactive trace init failure fixture");fixture.SetActive(false);
            var trace=fixture.AddComponent<OvrConcurrentInputTrace>();
            var instance=typeof(OvrConcurrentInputTrace).GetField("_instance",BindingFlags.Static|BindingFlags.NonPublic);
            instance.SetValue(null,trace);
            string file=Path.Combine(Path.GetTempPath(),"QuestFlight_trace_init_failure_"+Guid.NewGuid().ToString("N"));
            File.WriteAllText(file,"an existing file cannot be a storage directory");
            try
            {
                Set(trace,"traceRecordingEnabled",true);
                typeof(OvrConcurrentInputTrace).GetMethod("InitializeTraceStorage",Fields).Invoke(trace,new object[]{file});
                Assert.IsTrue(trace.enabled);
                OvrConcurrentInputTrace.RecordTransformer(default,"Move",false,2,.3f,.5f,"rejected");
                Assert.AreEqual(0,trace.FormattedRecordCount);
            }
            finally {instance.SetValue(null,null);UnityEngine.Object.DestroyImmediate(fixture);File.Delete(file);}
        }
    }
}
#endif
