using NUnit.Framework;
using QuestFlightLab.Audio;
using UnityEngine;

namespace QuestFlightLab.Editor.Tests
{
    public sealed class ProductionC172AudioEditModeTests
    {
        [Test]
        public void EngineMix_IsSilentWhenStopped_AndCrossfadesAcrossRpm()
        {
            ProductionC172AudioController.EngineMix stopped =
                ProductionC172AudioController.EvaluateEngineMix(0f, 650f, 2700f);
            ProductionC172AudioController.EngineMix idle =
                ProductionC172AudioController.EvaluateEngineMix(700f, 650f, 2700f);
            ProductionC172AudioController.EngineMix cruise =
                ProductionC172AudioController.EvaluateEngineMix(2400f, 650f, 2700f);
            Assert.That(stopped.idleGain + stopped.cruiseGain + stopped.propellerGain, Is.EqualTo(0f).Within(0.001f));
            Assert.That(idle.idleGain, Is.GreaterThan(idle.cruiseGain));
            Assert.That(cruise.cruiseGain, Is.GreaterThan(cruise.idleGain));
            Assert.That(cruise.propellerGain, Is.GreaterThan(0.5f));
        }

        [Test]
        public void WindAndStallMappings_RespectPhysicalGates()
        {
            Assert.That(ProductionC172AudioController.EvaluateWind(0f, 125f), Is.Zero);
            Assert.That(ProductionC172AudioController.EvaluateWind(125f, 125f), Is.EqualTo(1f));
            Assert.That(ProductionC172AudioController.EvaluateStallCue(true, 18f, 11.5f, 16f, true, 1f), Is.Zero);
            Assert.That(ProductionC172AudioController.EvaluateStallCue(false, 16f, 11.5f, 16f, false, 0f), Is.EqualTo(1f));
        }

        [Test]
        public void ProductPrefab_HasOneBoundedAuthoredSoundscape()
        {
            ProductionC172AudioAuthoring.ValidateProductPrefab();
        }
    }
}
