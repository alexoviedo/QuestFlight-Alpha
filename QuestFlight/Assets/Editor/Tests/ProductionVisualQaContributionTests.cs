using System;
using System.IO;
using NUnit.Framework;
using QuestFlightLab.Editor;
using QuestFlightLab.Runtime;

namespace QuestFlight.Editor.Tests
{
    public sealed class ProductionVisualQaContributionTests
    {
        private const string Header = "renderer\tdraw_slots\tmaterials\tmaterial_assets\tkind\tmaterial_slot\tsubmesh\ttopology\ttriangles\tmesh_asset\tcount_semantics";
        private static string Row(string draws, string triangles) =>
            $"fixture\t{draws}\tmaterial\tasset\trenderer_submesh\t0\t0\tTriangles\t{triangles}\tmesh\tsubmitted indices";

        [Test]
        public void ExactContributionsAreAccepted()
        {
            var budget = new RenderBudgetSnapshot { estimatedInstancedDrawCalls = 3, estimatedFrustumTriangles = 13 };
            Assert.DoesNotThrow(() => ProductionVisualQaBatchRunner.ValidateBudgetContributions(
                budget, new[] { Header, Row("1", "5"), Row("2", "8") }));
        }

        [TestCase("2", "13")]
        [TestCase("3", "12")]
        [TestCase("-1", "13")]
        [TestCase("3", "invalid")]
        public void MismatchOrMalformedCountsFail(string draws, string triangles)
        {
            var budget = new RenderBudgetSnapshot { estimatedInstancedDrawCalls = 3, estimatedFrustumTriangles = 13 };
            Assert.Throws<InvalidDataException>(() => ProductionVisualQaBatchRunner.ValidateBudgetContributions(
                budget, new[] { Header, Row(draws, triangles) }));
        }

        [Test]
        public void MissingOrMalformedHeaderFailsEvenForZeroCounts()
        {
            Assert.Throws<InvalidDataException>(() => ProductionVisualQaBatchRunner.ValidateBudgetContributions(
                new RenderBudgetSnapshot(), Array.Empty<string>()));
            Assert.Throws<InvalidDataException>(() => ProductionVisualQaBatchRunner.ValidateBudgetContributions(
                new RenderBudgetSnapshot(), new[] { "not a contribution table" }));
        }

        [Test]
        public void FailedDiagnosticWriteRestoresPreviousDestination()
        {
            string original = QuestRenderBudgetAudit.EditorDiagnosticPath;
            try
            {
                QuestRenderBudgetAudit.EditorDiagnosticPath = "prior-observer-destination";
                Assert.Catch<Exception>(() => ProductionVisualQaBatchRunner.CaptureBudgetWithContributions(null, Path.GetTempPath()));
                Assert.That(QuestRenderBudgetAudit.EditorDiagnosticPath, Is.EqualTo("prior-observer-destination"));
            }
            finally { QuestRenderBudgetAudit.EditorDiagnosticPath = original; }
        }
    }
}
