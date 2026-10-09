#if UNITY_EDITOR
using System;
using System.Reflection;
using NUnit.Framework;
using T2.QuestFlight.Editor;
using UnityEditor;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class ProductionDependencyClosurePolicyTests
    {
        private static string Classify(string path)
        {
            Type exporter = typeof(ProductionDependencyClosureExporter);
            Type manifestType = exporter.GetNestedType("ExtractionManifest", BindingFlags.NonPublic);
            object manifest = Activator.CreateInstance(manifestType, true);
            return (string)exporter.GetMethod("Classify", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new[] { (object)path, manifest });
        }

        [TestCase("ProductTrainingController")]
        [TestCase("TrainingCoach")]
        [TestCase("TrainingEvaluator")]
        [TestCase("TrainingLessonDefinition")]
        [TestCase("TrainingProgress")]
        public void CurrentCurriculumIsExplicitlyApproved(string name)
        {
            Assert.AreEqual("approved_product_authored", Classify("Assets/Scripts/Product/Training/" + name + ".cs"));
        }

        [TestCase("Assets/Scripts/Training/LegacyResearch.cs")]
        [TestCase("Assets/Scripts/Product/Training/UnreviewedNewSystem.cs")]
        [TestCase("Assets/Scripts/Flight/UnityPrototypeFlightBackend.cs")]
        [TestCase("Assets/Scripts/Flight/SimpleAircraftPhysics.cs")]
        [TestCase("Assets/Scripts/Flight/FlightBackendSelector.cs")]
        [TestCase("Assets/Production/Training/UnreviewedNewLesson.asset")]
        public void LegacyAndUnreviewedTrainingRemainExcluded(string path)
        {
            Assert.AreEqual("excluded_legacy_dependency", Classify(path));
        }

        [TestCase("Assets/Production/Training/01_taxi.asset")]
        [TestCase("Assets/Production/Training/CaptionTimings.json")]
        [TestCase("Assets/Production/Materials/StartupShowroomAccent.mat")]
        [TestCase("Assets/Scripts/Product/ProductAppNavigation.cs")]
        public void CurrentAuthoredCurriculumAssetsArePreciselyRegistered(string path)
        {
            Assert.AreEqual("approved_product_authored", Classify(path));
        }

        [Test]
        public void UnknownProductContentRequiresExplicitReview()
        {
            Assert.AreEqual("unknown_unclassified_dependency", Classify("Assets/Production/UI/UnreviewedNewContent.asset"));
        }

        [Test]
        public void AuthoredSceneCurriculumReferencesClassifyAsApproved()
        {
            var dependencies = AssetDatabase.GetDependencies(ProductionDependencyClosureExporter.ScenePath, true);
            foreach (string name in new[] { "ProductTrainingController", "TrainingCoach" })
            {
                string path = "Assets/Scripts/Product/Training/" + name + ".cs";
                CollectionAssert.Contains(dependencies, path, "Expected serialized curriculum dependency disappeared");
                Assert.AreEqual("approved_product_authored", Classify(path));
            }
        }
    }
}
#endif
