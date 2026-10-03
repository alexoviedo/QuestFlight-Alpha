#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class GuestSessionFinishEditModeTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private GameObject world;
        private ProductFlightModeCoordinator modes;
        private ProductTrainingController training;
        private ProductAppNavigation navigation;
        private float originalTimeScale;
        private bool guideKeyExisted;
        private int guideKeyValue;
        private string temporaryDirectory;

        private static void Set(object target,string name,object value) => target.GetType().GetField(name,Private).SetValue(target,value);
        private static T Get<T>(object target,string name) => (T)target.GetType().GetField(name,Private).GetValue(target);
        private static void State(object target,string name,object value) => Set(target,"<"+name+">k__BackingField",value);
        private static void Call(object target,string name,params object[] args) => target.GetType().GetMethod(name,Private).Invoke(target,args);

        [SetUp] public void SetUp()
        {
            originalTimeScale=Time.timeScale;
            guideKeyExisted=PlayerPrefs.HasKey("QuestFlight.QuickStartSeen");
            guideKeyValue=PlayerPrefs.GetInt("QuestFlight.QuickStartSeen",0);
            temporaryDirectory=Path.Combine(Path.GetTempPath(),"QuestFlightGuestTest_"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            world=PrefabUtility.LoadPrefabContents(ProductTrainingAuthoring.World);
            modes=world.GetComponentInChildren<ProductFlightModeCoordinator>(true);
            training=world.GetComponentInChildren<ProductTrainingController>(true);
            navigation=world.GetComponentInChildren<ProductAppNavigation>(true);
            State(modes,"State",ProductFlightModeCoordinator.FlightModeState.Selection);
            State(training,"State",TrainingSessionState.Inactive);
        }

        [TearDown] public void TearDown()
        {
            if(training!=null)State(training,"State",TrainingSessionState.Inactive);
            if(world!=null)PrefabUtility.UnloadPrefabContents(world);
            Time.timeScale=originalTimeScale;
            if(guideKeyExisted)PlayerPrefs.SetInt("QuestFlight.QuickStartSeen",guideKeyValue);
            else PlayerPrefs.DeleteKey("QuestFlight.QuickStartSeen");
            PlayerPrefs.Save();
            // Only these test-owned files are removed; never touch the headset/owner profile.
            if(temporaryDirectory!=null)
            {
                var file=Path.Combine(temporaryDirectory,"owner.json");
                if(File.Exists(file))File.Delete(file);
                if(File.Exists(file+".tmp"))File.Delete(file+".tmp");
                Directory.Delete(temporaryDirectory);
            }
        }

        [Test] public void GuestLessonCannotMutateOwnerInMemoryOrOnDiskAndOwnerCanResumeSaving()
        {
            string id=training.Lessons[0].lessonId;
            string path=Path.Combine(temporaryDirectory,"owner.json");
            var owner=new TrainingProgress();
            owner.Record(id,"OWNER","Original owner debrief",true);
            Assert.IsTrue(owner.Save(path));
            string originalDisk=File.ReadAllText(path),originalMemory=JsonUtility.ToJson(owner);
            Set(training,"ownerProgress",owner);Set(training,"progress",owner);Set(training,"progressPath",path);
            State(training,"SelectedLesson",0);

            Assert.IsTrue(training.TrySetGuestSession(true));
            var guest=Get<TrainingProgress>(training,"progress");
            Assert.AreNotSame(owner,guest);Assert.IsEmpty(guest.lessons);
            State(modes,"State",ProductFlightModeCoordinator.FlightModeState.Training);
            State(training,"State",TrainingSessionState.Practice);
            Assert.IsFalse(training.TrySetGuestSession(false),"Changing profile during an attempt must be rejected.");
            Call(training,"Finish",false,"Guest practice ended.");
            Assert.AreEqual(1,guest.Find(id).attempts,"Exercise completion must be recorded for this guest.");
            Assert.AreEqual(originalMemory,JsonUtility.ToJson(owner),"Guest exercise altered the owner's live progress.");
            Assert.AreEqual(originalDisk,File.ReadAllText(path),"Guest exercise wrote to the owner's progress file.");
            Assert.IsFalse(File.Exists(path+".tmp"));
            StringAssert.Contains("Guest session",training.LastDebrief);

            training.LeaveTraining();
            State(modes,"State",ProductFlightModeCoordinator.FlightModeState.Selection);
            Assert.IsTrue(training.TrySetGuestSession(false));
            Assert.AreSame(owner,Get<TrainingProgress>(training,"progress"));
            Assert.IsTrue(training.IsLessonComplete(0));
            State(modes,"State",ProductFlightModeCoordinator.FlightModeState.Training);
            State(training,"State",TrainingSessionState.Practice);
            Call(training,"Finish",false,"Owner practice ended.");
            Assert.AreEqual(2,TrainingProgress.Load(path).Find(id).attempts,"Owner saving must resume after the guest session ends.");
            training.LeaveTraining();
            State(modes,"State",ProductFlightModeCoordinator.FlightModeState.Selection);
            Assert.IsTrue(training.TrySetGuestSession(true));
            Assert.IsEmpty(Get<TrainingProgress>(training,"progress").lessons,"A new guest must not inherit the previous guest's attempts.");
        }

        [TestCase(ProductAppNavigation.Page.Tools, ProductAppNavigation.Page.App)]
        [TestCase(ProductAppNavigation.Page.Controls, ProductAppNavigation.Page.Settings)]
        [TestCase(ProductAppNavigation.Page.Comfort, ProductAppNavigation.Page.Settings)]
        [TestCase(ProductAppNavigation.Page.Captions, ProductAppNavigation.Page.Comfort)]
        [TestCase(ProductAppNavigation.Page.ControlDiagram, ProductAppNavigation.Page.Bindings)]
        [TestCase(ProductAppNavigation.Page.Checklist, ProductAppNavigation.Page.Kneeboard)]
        [TestCase(ProductAppNavigation.Page.Bindings, ProductAppNavigation.Page.Controls)]
        [TestCase(ProductAppNavigation.Page.InputPreview, ProductAppNavigation.Page.Controls)]
        [TestCase(ProductAppNavigation.Page.InstrumentHelp, ProductAppNavigation.Page.App)]
        [TestCase(ProductAppNavigation.Page.Map, ProductAppNavigation.Page.Briefing)]
        [TestCase(ProductAppNavigation.Page.Briefing, ProductAppNavigation.Page.Tools)]
        [TestCase(ProductAppNavigation.Page.Kneeboard, ProductAppNavigation.Page.Tools)]
        [TestCase(ProductAppNavigation.Page.Activities, ProductAppNavigation.Page.Tools)]
        [TestCase(ProductAppNavigation.Page.History, ProductAppNavigation.Page.Tools)]
        [TestCase(ProductAppNavigation.Page.Report, ProductAppNavigation.Page.Tools)]
        [TestCase(ProductAppNavigation.Page.Summary, ProductAppNavigation.Page.App)]
        [TestCase(ProductAppNavigation.Page.Settings, ProductAppNavigation.Page.Tools)]
        [TestCase(ProductAppNavigation.Page.Guest, ProductAppNavigation.Page.Settings)]
        [TestCase(ProductAppNavigation.Page.ConfirmMain, ProductAppNavigation.Page.App)]
        [TestCase(ProductAppNavigation.Page.Explanation, ProductAppNavigation.Page.App)]
        [TestCase(ProductAppNavigation.Page.Orientation, ProductAppNavigation.Page.None)]
        public void EverySubmenuBackReturnsToItsParent(ProductAppNavigation.Page page, ProductAppNavigation.Page parent)
        {
            State(navigation,"CurrentPage",page);
            Call(navigation,"Refresh");
            navigation.Back();
            Assert.AreEqual(parent,navigation.CurrentPage);
            Assert.IsFalse(modes.AuthoredSeatCalibration.IsOpen);
            Assert.AreEqual(TrainingSessionState.Inactive,training.State);
        }

        [TestCase(ProductAppNavigation.Page.App, 40, ProductAppNavigation.Page.Tools)]
        [TestCase(ProductAppNavigation.Page.Settings, 41, ProductAppNavigation.Page.Controls)]
        [TestCase(ProductAppNavigation.Page.App, 41, ProductAppNavigation.Page.Controls)]
        [TestCase(ProductAppNavigation.Page.Settings, 42, ProductAppNavigation.Page.Comfort)]
        [TestCase(ProductAppNavigation.Page.Tools, 43, ProductAppNavigation.Page.Briefing)]
        [TestCase(ProductAppNavigation.Page.Briefing, 44, ProductAppNavigation.Page.Map)]
        [TestCase(ProductAppNavigation.Page.History, 44, ProductAppNavigation.Page.Map)]
        [TestCase(ProductAppNavigation.Page.Summary, 44, ProductAppNavigation.Page.Map)]
        [TestCase(ProductAppNavigation.Page.Tools, 45, ProductAppNavigation.Page.Kneeboard)]
        [TestCase(ProductAppNavigation.Page.Tools, 46, ProductAppNavigation.Page.Activities)]
        [TestCase(ProductAppNavigation.Page.Tools, 47, ProductAppNavigation.Page.History)]
        [TestCase(ProductAppNavigation.Page.Tools, 48, ProductAppNavigation.Page.Report)]
        [TestCase(ProductAppNavigation.Page.Controls, 120, ProductAppNavigation.Page.Bindings)]
        public void ToolActionsOpenTheirPageAndBackRestoresTheCallingPage(ProductAppNavigation.Page parent, int action, ProductAppNavigation.Page page)
        {
            State(navigation,"CurrentPage",parent);
            Call(navigation,"InvokePolishAction",action);
            Call(navigation,"Refresh");
            Assert.AreEqual(page,navigation.CurrentPage);
            navigation.Back();
            Assert.AreEqual(parent,navigation.CurrentPage);
        }

        [Test] public void GuideAndCalibrationBackReturnToSettingsWithoutLosingTheOverlay()
        {
            State(navigation,"CurrentPage",ProductAppNavigation.Page.Settings);
            navigation.OpenQuickStart();
            Assert.AreEqual(ProductAppNavigation.Page.QuickStart,navigation.CurrentPage);
            Set(navigation,"guideStep",2);
            navigation.OpenCalibration();
            Assert.IsTrue(modes.AuthoredSeatCalibration.IsOpen);
            navigation.Back();
            Assert.IsFalse(modes.AuthoredSeatCalibration.IsOpen);
            Assert.AreEqual(ProductAppNavigation.Page.QuickStart,navigation.CurrentPage);
            Assert.AreEqual(2,Get<int>(navigation,"guideStep"));
            navigation.Back();navigation.Back();navigation.Back();
            Assert.AreEqual(ProductAppNavigation.Page.Settings,navigation.CurrentPage);
            Assert.AreEqual(1,PlayerPrefs.GetInt("QuestFlight.QuickStartSeen",0));
            Assert.IsTrue(Get<GameObject>(navigation,"panel").activeSelf);
            Assert.IsFalse(modes.AuthoredModeMenuView.SelectionContentRoot.activeSelf);
            var suppressed=Get<Renderer[]>(modes.AuthoredModeMenuView,"aircraftPresentationRenderers");
            foreach(var renderer in Get<GameObject>(navigation,"panel").GetComponentsInChildren<Renderer>(true))
                CollectionAssert.DoesNotContain(suppressed,renderer,"The startup showroom must not suppress its own settings/guide overlay.");
            navigation.Back();
            Assert.AreEqual(ProductAppNavigation.Page.Tools,navigation.CurrentPage);
            navigation.OpenQuickStart(true);
            // This is the same handler the universal Menu button uses inside a guide.
            navigation.OpenMenu();
            Assert.AreEqual(ProductAppNavigation.Page.App,navigation.CurrentPage);
            Assert.IsTrue(Get<GameObject>(navigation,"panel").activeSelf);
        }
    }
}
#endif
