using NUnit.Framework;
using System.Reflection;
using QuestFlightLab.Audio;
using QuestFlightLab.Flight.Backends;
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

        [Test]
        public void RuntimeMix_RestoresAuthoredOneShots_AndMutesEffectsWithoutMutingWarning()
        {
            // Keep the probe inactive: this tests real source gains without
            // initializing a native flight backend or starting audio playback.
            var go = new GameObject("AudioGainRegressionProbe");
            go.SetActive(false);
            var profile = ScriptableObject.CreateInstance<ProductionC172AudioProfile>();
            try
            {
                var coordinator = go.AddComponent<FlightDynamicsCoordinator>();
                var controller = go.AddComponent<ProductionC172AudioController>();
                var sources = new AudioSource[10];
                for (int i = 0; i < sources.Length; i++)
                {
                    sources[i] = go.AddComponent<AudioSource>();
                    sources[i].volume = 0f; // Matches the serialized product sources.
                }
                controller.ConfigureAuthoredReferences(profile, coordinator, null, null,
                    sources[0], sources[1], sources[2], sources[3], sources[4],
                    sources[5], sources[6], sources[7], sources[8], sources[9]);
                const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(FlightDynamicsCoordinator).GetField("_currentState", fields).SetValue(coordinator,
                    new FlightDynamicsState { engineRpm = 2400, calibratedAirspeedKnots = 80,
                        angleOfAttackDegrees = 18, rotationUnity = Quaternion.identity });
                typeof(ProductionC172AudioController).GetField("_smoothedRpm", fields).SetValue(controller, 2400f);
                typeof(ProductionC172AudioController).GetField("_smoothedAirspeed", fields).SetValue(controller, 80f);
                typeof(ProductionC172AudioController).GetField("_smoothedAoA", fields).SetValue(controller, 18f);
                MethodInfo update = typeof(ProductionC172AudioController).GetMethod("Update", fields);
                update.Invoke(controller, null);
                Assert.That(sources[8].volume, Is.GreaterThan(0.9f), "Switch/impact source remained inaudible.");
                Assert.That(sources[9].volume, Is.GreaterThan(0.9f), "Starter source remained inaudible.");
                Assert.That(sources[1].volume, Is.GreaterThan(0f));
                Assert.That(sources[7].volume, Is.GreaterThan(0.4f));

                controller.AircraftVolume = 0f;
                // Start at the end of the existing short volume ramp, so this
                // check is independent of editor frame rate.
                typeof(ProductionC172AudioController).GetField("_smoothedAircraftVolume", fields).SetValue(controller, 0f);
                update.Invoke(controller, null);
                foreach (int index in new[] { 0, 1, 2, 3, 4, 5, 8, 9 })
                    Assert.That(sources[index].volume, Is.Zero, "Ordinary aircraft source bypassed the volume setting: " + index);
                Assert.That(sources[7].volume, Is.GreaterThan(0.4f), "Aircraft volume suppressed the stall warning.");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(profile);
            }
        }
    }
}
