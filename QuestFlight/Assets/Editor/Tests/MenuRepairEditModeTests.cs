#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class MenuRepairEditModeTests
    {
        private GameObject world;
        private ProductAppNavigation nav;
        private ProductFlightModeCoordinator mode;
        private float scale;
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [SetUp] public void SetUp()
        {
            scale=Time.timeScale;Time.timeScale=0;world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);
            mode=world.GetComponentInChildren<ProductFlightModeCoordinator>(true);nav=mode.AuthoredNavigation;
            typeof(ProductFlightModeCoordinator).GetProperty("State").SetValue(mode,ProductFlightModeCoordinator.FlightModeState.Selection);
            nav.EditorVerificationFocus=true;
        }
        [TearDown] public void TearDown(){PrefabUtility.UnloadPrefabContents(world);Time.timeScale=scale;}
        public static IEnumerable Pages=>Enum.GetValues(typeof(ProductAppNavigation.Page)).Cast<ProductAppNavigation.Page>().Where(p=>p!=ProductAppNavigation.Page.None);
        [TestCaseSource(nameof(Pages))] public void EveryPageRendersUniqueKnownActionsAndAnExit(ProductAppNavigation.Page page)
        {
            typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,page);nav.RefreshMenu();
            var actions=Enumerable.Range(0,8).Select(nav.VisibleAction).Where(a=>a!=0).ToArray();
            Assert.IsNotEmpty(actions,page.ToString());Assert.AreEqual(actions.Length,actions.Distinct().Count(),page.ToString());
            Assert.IsTrue(actions.All(ProductAppNavigation.IsKnownAction),page.ToString());
            if(page!=ProductAppNavigation.Page.Home&&page!=ProductAppNavigation.Page.App)
                Assert.IsTrue(actions.Any(a=>a==(int)MenuActionId.Back||a==(int)MenuActionId.EndToHome||a==(int)MenuActionId.LessonList||a==(int)MenuActionId.FinishSetup||a==(int)MenuActionId.DiscardSeat||a==(int)MenuActionId.ContinueSeat),"Missing exit: "+page);
        }
        [Test] public void NamedActionValuesAreUniqueAndUnknownActionsFailClosed()
        {
            var ids=Enum.GetValues(typeof(MenuActionId)).Cast<int>().ToArray();Assert.AreEqual(ids.Length,ids.Distinct().Count());
            Assert.IsFalse(nav.CanExecute(99999,out var reason));Assert.IsNotEmpty(reason);
        }
        [Test] public void SgsPauseKeepsSoaringActionsAndLessonHelpInDistinctSlots()
        {
            var f=mode.AuthoredFlightCoordinator;f.aircraftDefinition=AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(FlightAircraftDefinition.DefinitionFolder+"/SGS233A_Reference.asset");
            typeof(ProductFlightModeCoordinator).GetProperty("State").SetValue(mode,ProductFlightModeCoordinator.FlightModeState.Training);
            typeof(ProductTrainingController).GetProperty("State").SetValue(mode.AuthoredTraining,TrainingSessionState.Paused);
            typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,ProductAppNavigation.Page.App);nav.RefreshMenu();
            Assert.AreEqual((int)MenuActionId.Soaring,nav.VisibleAction(3));Assert.AreEqual((int)MenuActionId.Practice,nav.VisibleAction(5));
        }
        [Test] public void SgsActivitiesAndChecklistHaveNoPoweredAircraftInstructions()
        {
            mode.AuthoredFlightCoordinator.aircraftDefinition=AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(FlightAircraftDefinition.DefinitionFolder+"/SGS233A_Reference.asset");
            typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,ProductAppNavigation.Page.Activities);nav.RefreshMenu();
            Assert.IsTrue(nav.VisibleLabel(0).Contains("unpowered"));Assert.AreEqual(0,nav.VisibleAction(2));Assert.AreEqual(0,nav.VisibleAction(4));
            var experience=world.GetComponentInChildren<ProductFlightExperience>(true);
            Assert.IsTrue(experience.ChecklistText.StartsWith("SGS"));Assert.IsTrue(experience.Reference.StartsWith("SGS"));
            Assert.IsFalse(nav.CanExecute((int)MenuActionId.StartDemo,out _));
        }
        [Test] public void LegacyHistoryIsPreservedWithoutReinterpretingItsLessonIndex()
        {
            var e=world.GetComponentInChildren<ProductFlightExperience>(true);
            var owner=(ProductFlightExperience.History)typeof(ProductFlightExperience).GetField("owner",Private).GetValue(e);
            owner.flights.Add(new ProductFlightExperience.Record{mode="Old flight",recommendedLesson=4});
            Assert.IsFalse(e.HasRecommendedLesson);Assert.AreEqual(4,owner.flights[0].recommendedLesson);
            owner.flights[0].aircraftId="sgs233.reference";owner.flights[0].recommendedLessonId="sgs233.reference.ground_abort";
            Assert.IsTrue(e.TryRecommendedLesson(out var glider,out var id));Assert.IsTrue(glider);Assert.AreEqual("sgs233.reference.ground_abort",id);
        }
        [Test] public void CalibrationCancelChangesOnlySeatDraftAndNotXrOrigin()
        {
            var calibration=mode.AuthoredSeatCalibration;var origin=calibration.AuthoredReferenceFrameRig.XrOrigin;
            calibration.Open();var position=origin.localPosition+new Vector3(.01f,.02f,.03f);origin.localPosition=position;
            calibration.AdjustUp();calibration.Cancel();Assert.AreEqual(position,origin.localPosition);
        }
        [Test] public void SelectedSgsTransitionDoesNotRequireTheDisabledC172Consumer()
        {
            var presentation=mode.AircraftPresentation;Assert.IsTrue(presentation.Apply(presentation.glider));
            Assert.IsFalse(mode.AuthoredModeMenuView.AuthoredCockpitSystems.isActiveAndEnabled);
            Assert.IsTrue(mode.AuthoredModeMenuView.SelectedPresentationAvailable);
        }
        [Test] public void HeldExternalMenuButtonsCannotArmFlightTowRelease()
        {
            var sample=new QuestFlightLab.Runtime.GamepadInputSnapshot{connected=true};
            Assert.IsTrue(QuestFlightLab.Input.ProductControlInput.GamepadMenuButtonsReleased(sample));
            sample.buttonSouth=true;Assert.IsFalse(QuestFlightLab.Input.ProductControlInput.GamepadMenuButtonsReleased(sample));sample.buttonSouth=false;
            sample.startButton=true;Assert.IsFalse(QuestFlightLab.Input.ProductControlInput.GamepadMenuButtonsReleased(sample));sample.startButton=false;
            sample.leftStickY=1;Assert.IsFalse(QuestFlightLab.Input.ProductControlInput.GamepadMenuButtonsReleased(sample));sample.leftStickY=0;
            sample.rightTrigger=1;Assert.IsFalse(QuestFlightLab.Input.ProductControlInput.GamepadMenuButtonsReleased(sample));
        }
        [Test] public void SgsVisibleInstrumentAndBindingTextUsesSailplaneContent()
        {
            mode.AuthoredFlightCoordinator.aircraftDefinition=AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(FlightAircraftDefinition.DefinitionFolder+"/SGS233A_Reference.asset");
            var detail=(TextMesh)typeof(ProductAppNavigation).GetField("detail",Private).GetValue(nav);
            typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,ProductAppNavigation.Page.InstrumentHelp);nav.RefreshMenu();
            Assert.IsTrue(detail.text.Contains("reference lesson target"),detail.text);
            typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,ProductAppNavigation.Page.Bindings);nav.RefreshMenu();
            Assert.IsTrue(detail.text.Contains("airbrake / wheel-brake"),detail.text);Assert.IsTrue(detail.text.Contains("separate trim lever"),detail.text);Assert.IsTrue(nav.VisibleLabel(6).Contains("stick grip"),nav.VisibleLabel(6));
        }
        [Test] public void ModalDepthAndAuthoredInputScopePreventCockpitOcclusion()
        {
            foreach(string name in new[]{"ModalSurface","ModalAction","InterRegularOverlay","InterSemiboldOverlay"})
            {
                var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Production/UI/Interface/"+name+".mat");
                Assert.That(material.renderQueue,Is.GreaterThanOrEqualTo(4900));Assert.AreEqual(8,material.GetFloat(name.StartsWith("Inter")?"_ZTestMode":"_ZTest"));Assert.That(material.FindPass("XRMotionVectors"),Is.GreaterThanOrEqualTo(0));
            }
            Assert.That(nav.MenuInteractionScopeCount,Is.GreaterThan(0));
            var controls=(MonoBehaviour[])typeof(ProductAppNavigation).GetField("cockpitInteractables",Private).GetValue(nav);var states=controls.Select(c=>c.enabled).ToArray();
            typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,ProductAppNavigation.Page.Home);nav.RefreshMenu();Assert.IsTrue(controls.All(c=>!c.enabled));
            typeof(ProductFlightModeCoordinator).GetProperty("State").SetValue(mode,ProductFlightModeCoordinator.FlightModeState.FreeFlight);
            typeof(ProductAppNavigation).GetProperty("CurrentPage").SetValue(nav,ProductAppNavigation.Page.None);nav.RefreshMenu();Assert.AreEqual(states,controls.Select(c=>c.enabled).ToArray());
        }
        [Test] public void RenderingToggleIsUnavailableAndAllSgsReferencesHaveRealContent()
        {
            Assert.IsFalse(nav.CanExecute((int)MenuActionId.ExperimentalRendering,out var reason));Assert.IsNotEmpty(reason);
            for(int n=0;n<ProductAppNavigation.SgsReferenceCount;n++)Assert.That(ProductAppNavigation.SgsReference(n).Length,Is.GreaterThan(40));
        }
    }
}
#endif
