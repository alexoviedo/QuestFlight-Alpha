#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using QuestFlight.Editor;
using QuestFlightLab.Environment;
using UnityEngine;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class EvidenceAcceptancePolicyTests
    {
        private static MountainTemporalStabilityReport Report()
        {
            return new MountainTemporalStabilityReport { realDataRootActive=true,
                expectedRendererCount=2, sampledFrameCount=1, rendererSampleCount=2 };
        }

        private static void Classify(MountainTemporalStabilityReport report)
        {
            typeof(MountainTemporalStabilityProbe).GetMethod("PopulateClassification", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { report });
        }

        [Test]
        public void MountainSingleFrameRequiresActualRendererSamplesAndNoViolations()
        {
            var good=Report();Classify(good);Assert.IsTrue(good.passed);
            foreach(int count in new[]{0,1})
            {
                var empty=Report();empty.expectedRendererCount=count;empty.rendererSampleCount=count;
                Classify(empty);Assert.IsFalse(empty.passed);
            }
            var material=Report();material.violations.Add("Material identity changed for terrain");
            Classify(material);Assert.IsFalse(material.passed, "Previously unclassified material mutation must fail");
            var noSamples=Report();noSamples.rendererSampleCount=0;Classify(noSamples);Assert.IsFalse(noSamples.passed);
        }

        [Test]
        public void EveryStructuredMountainViolationFailsRegardlessOfDiagnosticWording()
        {
            foreach(MountainViolationCode code in Enum.GetValues(typeof(MountainViolationCode)))
            {
                var report=Report();report.violationCodes.Add(code.ToString());
                Classify(report);Assert.IsFalse(report.passed, code.ToString());
                report.violations.Add("Reworded diagnostic with no historical category keywords");
                Classify(report);Assert.IsFalse(report.passed, code.ToString());
            }
        }

        [Test]
        public void EmptyRuntimeMountainProbeDoesNotCertifyASingleFrame()
        {
            var fixture=new GameObject("Empty mountain evidence fixture");
            try
            {
                var probe=fixture.AddComponent<MountainTemporalStabilityProbe>();
                probe.Initialize(fixture.transform,fixture.transform);
                var report=probe.CaptureSingleFrameForTest(null);
                Assert.IsFalse(report.passed);Assert.Less(report.expectedRendererCount,2);
                CollectionAssert.Contains(report.violationCodes,MountainViolationCode.RendererCount.ToString());
            }
            finally {UnityEngine.Object.DestroyImmediate(fixture);}
        }

        [Test]
        public void UnsupportedUrpOverdrawProducesNoOrdinaryColorIntensityEvidence()
        {
            var method=typeof(QuestPerformanceBudgetBatchRunner).GetMethod("CaptureOverdraw",BindingFlags.Static|BindingFlags.NonPublic);
            var captures=(IList)method.Invoke(null,new object[]{null,"unused"});
            Assert.AreEqual(0,captures.Count,"URP coverage must remain explicitly unmeasured until a validated diagnostic pass exists");
        }

        [Test]
        public void StaticBudgetRejectsFailedIncompleteOrUnboundVisualCounters()
        {
            var report=new QuestFlightLab.Editor.ProductionVisualQaBatchRunner.ProductionVisualQaReport
            {passed=true,scenePath="Assets/Scenes/ProductionVerticalSlice.unity",unityVersion=Application.unityVersion,
                requiredShotCount=56,maxEstimatedDrawCalls=120,maxEstimatedVisibleTriangles=500000,sceneMaterialCount=30,
                sourceIdentitySha256=new string('a',64),configurationSha256=new string('b',64)};
            for(int i=0;i<56;i++)report.shots.Add(new QuestFlightLab.Editor.ProductionVisualQaBatchRunner.ProductionVisualQaShot
                {id="fixture_"+i,passed=true,imageNotBlank=true});
            Assert.DoesNotThrow(()=>QuestPerformanceBudgetBatchRunner.ValidateVisualQaReport(report,new string('a',64),new string('b',64)));
            report.passed=false;
            Assert.Throws<System.IO.InvalidDataException>(()=>QuestPerformanceBudgetBatchRunner.ValidateVisualQaReport(report,new string('a',64),new string('b',64)));
            report.passed=true;report.maxEstimatedDrawCalls=0;
            Assert.Throws<System.IO.InvalidDataException>(()=>QuestPerformanceBudgetBatchRunner.ValidateVisualQaReport(report,new string('a',64),new string('b',64)));
            report.maxEstimatedDrawCalls=120;
            Assert.Throws<System.IO.InvalidDataException>(()=>QuestPerformanceBudgetBatchRunner.ValidateVisualQaReport(report,new string('a',64),new string('c',64)));
            Assert.Throws<System.IO.InvalidDataException>(()=>QuestPerformanceBudgetBatchRunner.ValidateVisualQaReport(report,new string('c',64),new string('b',64)));
            report.shots.RemoveAt(0);
            Assert.Throws<System.IO.InvalidDataException>(()=>QuestPerformanceBudgetBatchRunner.ValidateVisualQaReport(report,new string('a',64),new string('b',64)));
        }
    }
}
#endif
