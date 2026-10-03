#if UNITY_INCLUDE_TESTS
using System;
using System.Reflection;
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlight.PlayModeTests
{
    public sealed class ProductNativeMenuFlowPlayModeTests
    {
        private GameObject world;
        private float savedScale;
        private ProductFlightModeCoordinator modes;

        [SetUp]
        public void SetUp()
        {
            savedScale = Time.timeScale;
            Time.timeScale = 1f;
            world = UnityEngine.Object.Instantiate(Resources.Load<GameObject>(
                "QuestFlightLab/Production/ProductionWorldRoot"));
            modes = world.GetComponentInChildren<ProductFlightModeCoordinator>(true);
            // Batch tests have no headset focus. Exercise real native lifecycle and
            // presentation here; focus/input dispatch is covered by separate tests.
            modes.AuthoredNavigation.enabled = false;
            Assert.That(modes.AuthoredFlightCoordinator.InitializeSelectedBackend(), Is.True,
                modes.AuthoredFlightCoordinator.LastError);
            Assert.That(modes.AuthoredFlightCoordinator.IsInitialized, Is.True,
                modes.AuthoredFlightCoordinator.LastError);
        }

        [TearDown]
        public void TearDown()
        {
            if (world != null) UnityEngine.Object.DestroyImmediate(world);
            Time.timeScale = savedScale;
        }

        private void FinishTransition()
        {
            var flight = modes.AuthoredFlightCoordinator;
            var view = modes.AuthoredModeMenuView;
            for (int frame = 0; frame < 80 && view.TransitionPending; frame++)
            {
                if (Time.timeScale > 0f)
                    Assert.That(flight.StepForTest(modes.AuthoredProductInput.Current, 1.0 / 60.0), Is.True, flight.LastError);
                Assert.That(view.AuthoredCockpitSystems.StepForTest(flight.CurrentState,
                    flight.LastAppliedControls, default, Time.timeScale > 0f ? 1f / 60f : 0f), Is.True);
                view.AdvanceTransition(1f / 60f);
            }
            Assert.That(modes.AuthoredModeMenuView.TransitionPending, Is.False,
                "The real player-loop fade must finish; do not bypass it with the visual-QA helper.");
            Assert.That(modes.AuthoredModeMenuView.gameObject.activeSelf, Is.False);
            Assert.That(modes.AuthoredFlightCoordinator.CurrentState.IsFinite, Is.True);
            Assert.That(world.GetComponentsInChildren<FlightDynamicsCoordinator>(true), Has.Length.EqualTo(1));
        }

        [Test]
        public void ColdLaunchAndRepeatedFlightModeChangesFinishTheirRealTransitions()
        {
            modes.SelectFreeFlight();
            Assert.That(modes.State, Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.FreeFlight));
            FinishTransition();
            for (int cycle = 0; cycle < 2; cycle++)
            {
                modes.ReturnToMainMenu();
                modes.SelectDemo();
                Assert.That(modes.State, Is.EqualTo(ProductFlightModeCoordinator.FlightModeState.Demo));
                FinishTransition();
                modes.ReturnToMainMenu();
                modes.SelectTraining();
                FinishTransition();
                modes.AuthoredTraining.OpenLessons();
                Assert.That(modes.AuthoredTraining.State, Is.EqualTo(TrainingSessionState.Menu));
                Assert.That(Time.timeScale, Is.Zero);
                for (int lesson = 0; lesson < modes.AuthoredTraining.Lessons.Length; lesson++)
                {
                    modes.AuthoredTraining.SelectLesson(lesson);
                    Assert.That(modes.AuthoredTraining.State, Is.EqualTo(TrainingSessionState.Briefing));
                    modes.AuthoredTraining.OpenLessons();
                    Assert.That(modes.AuthoredTraining.State, Is.EqualTo(TrainingSessionState.Menu));
                }
                modes.AuthoredTraining.LeaveTraining();
                modes.ReturnToMainMenu();
                modes.SelectFreeFlight();
                FinishTransition();
                Assert.That(Time.timeScale, Is.EqualTo(1f));
            }
        }

        [Test]
        public void NativeFreeFlightFadeCompletesEvenWhenPausedImmediatelyAfterSelection()
        {
            modes.SelectFreeFlight();
            modes.AuthoredNavigation.OpenMenu();
            Assert.That(Time.timeScale, Is.Zero);
            FinishTransition();
            Assert.That(modes.AuthoredNavigation.IsOpen, Is.True);
            Assert.That(modes.AuthoredModeMenuView.AuthoredCockpitSystems.HasValidSnapshot, Is.True);
            Assert.That(Time.timeScale, Is.Zero, "Finishing a fade must not release the navigation pause.");
        }
    }
}
#endif
