#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using QuestFlight.Product;
using QuestFlight.Product.Training;
using UnityEngine;
using UnityEngine.TestTools;

namespace QuestFlight.PlayModeTests
{
    // Fixture focus is Editor-only; actions, native preparation and transitions are product code.
    public sealed class ProductMenuRepairPlayModeTests
    {
        private GameObject world;
        private ProductFlightModeCoordinator mode;
        private ProductAppNavigation nav;
        private float savedScale;
        private bool hadGuide;private int guideValue;
        [UnitySetUp] public IEnumerator SetUp()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            yield return new EnterPlayMode();
            hadGuide=PlayerPrefs.HasKey("QuestFlight.QuickStartSeen");guideValue=PlayerPrefs.GetInt("QuestFlight.QuickStartSeen");
            savedScale=Time.timeScale;Time.timeScale=0;
            world=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("QuestFlightLab/Production/ProductionWorldRoot"));
            mode=world.GetComponentInChildren<ProductFlightModeCoordinator>(true);nav=mode.AuthoredNavigation;
            nav.EditorVerificationFocus=true;
            string fixtureRoot=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"QuestFlightMenuTests",Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(fixtureRoot);
            mode.AuthoredTraining.EditorVerificationSaveRoot=fixtureRoot;
            world.GetComponentInChildren<ProductFlightExperience>(true).EditorVerificationSaveRoot=fixtureRoot;
            mode.AuthoredSeatCalibration.EditorVerificationSaveRoot=fixtureRoot;
            // Desktop D3D cannot sample Android ASTC arrays. Isolate that graphics-only
            // loader in this software fixture; the shipping scene and visual QA retain it.
            foreach(var clipmap in world.GetComponentsInChildren<QuestFlightLab.Environment.AircraftCenteredAerialClipmapController>(true))clipmap.enabled=false;
            Assert.IsTrue(mode.AuthoredFlightCoordinator.InitializeSelectedBackend(),mode.AuthoredFlightCoordinator.LastError);
            yield return NextFrame();yield return NextFrame();nav.OpenMenu();yield return NextFrame();
            Assert.AreEqual(ProductAppNavigation.Page.Home,nav.CurrentPage);
        }
        [UnityTearDown] public IEnumerator TearDown()
        {if(world!=null)UnityEngine.Object.Destroy(world);yield return NextFrame();Time.timeScale=savedScale;if(hadGuide)PlayerPrefs.SetInt("QuestFlight.QuickStartSeen",guideValue);else PlayerPrefs.DeleteKey("QuestFlight.QuickStartSeen");yield return new ExitPlayMode();}
        private static IEnumerator NextFrame()
        {int frame=Time.frameCount;while(Time.frameCount==frame)yield return null;}
        private IEnumerator Choose(MenuActionId action,bool pointer=false)
        {
            nav.ProcessMenuInput(false,false,false,false,0);yield return NextFrame();
            int slot=-1;for(int n=0;n<8;n++)if(nav.VisibleAction(n)==(int)action)slot=n;
            Assert.That(slot,Is.GreaterThanOrEqualTo(0),$"Missing action {action} on {nav.CurrentPage}");
            nav.FocusAction(slot);
            if(pointer)
            {
                var button=Array.Find(world.GetComponentsInChildren<ProductActionButton>(true),b=>b.gameObject.activeInHierarchy&&b.Slot==slot);
                var ray=button.GetComponent<Oculus.Interaction.RayInteractable>();Assert.IsTrue(ray.enabled);
                ray.PublishPointerEvent(new Oculus.Interaction.PointerEvent(1234,Oculus.Interaction.PointerEventType.Hover,Pose.identity));
                ray.PublishPointerEvent(new Oculus.Interaction.PointerEvent(1234,Oculus.Interaction.PointerEventType.Select,Pose.identity));
            }
            else nav.ProcessMenuInput(false,false,true,false,0);
            yield return NextFrame();nav.ProcessMenuInput(false,false,false,false,0);yield return NextFrame();
        }
        private IEnumerator Back()
        {nav.ProcessMenuInput(false,false,false,false,0);yield return NextFrame();nav.ProcessMenuInput(false,true,false,false,0);yield return NextFrame();nav.ProcessMenuInput(false,false,false,false,0);yield return NextFrame();}
        private IEnumerator Transition()
        {
            for(int n=0;n<240&&mode.AuthoredModeMenuView.TransitionPending;n++)yield return NextFrame();
            Assert.AreNotEqual(ProductFlightModeCoordinator.FlightModeState.Blocked,mode.State,mode.LastError);
            Assert.IsFalse(mode.AuthoredModeMenuView.TransitionPending,"Normal transition must finish without the QA bypass.");
            if(mode.State==ProductFlightModeCoordinator.FlightModeState.Training)
                for(int n=0;n<120&&mode.AuthoredTraining.State==TrainingSessionState.Inactive;n++)yield return NextFrame();
            nav.RefreshMenu();
        }
        private IEnumerator Adjust(MenuActionId action)
        {
            yield return Choose(action);Assert.AreEqual(ProductAppNavigation.Page.SettingAdjustment,nav.CurrentPage);
            yield return Choose(MenuActionId.DecreaseSetting,true);yield return Choose(MenuActionId.IncreaseSetting,true);
            yield return Choose(MenuActionId.ResetSetting,true);yield return Back();
        }
        [UnityTest] public IEnumerator InterruptedCatalogBriefingAndPreparationReturnWithoutReset()
        {
            yield return Choose(MenuActionId.FlySgs);yield return Choose(MenuActionId.AircraftLessons);yield return Transition();
            foreach(var state in new[]{TrainingSessionState.Menu,TrainingSessionState.Briefing})
            {
                if(state==TrainingSessionState.Briefing)yield return Choose((MenuActionId)100);
                nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.Settings);yield return Back();
                yield return Choose(MenuActionId.ReturnLessonView);Assert.AreEqual(state,mode.AuthoredTraining.State);
                Assert.AreEqual(ProductAppNavigation.Page.None,nav.CurrentPage);
            }
            yield return Choose(MenuActionId.Practice);nav.OpenMenu();yield return NextFrame();
            Assert.AreEqual(TrainingSessionState.Preparing,mode.AuthoredTraining.State);
            yield return Choose(MenuActionId.CancelPreparation);Assert.AreEqual(TrainingSessionState.Briefing,mode.AuthoredTraining.State);
            yield return Choose(MenuActionId.Practice);nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.ReturnLessonView);
            for(int n=0;n<600&&(mode.AuthoredTraining.State==TrainingSessionState.Preparing||nav.CurrentPage==ProductAppNavigation.Page.Handover);n++)yield return NextFrame();
            Assert.AreEqual(TrainingSessionState.Practice,mode.AuthoredTraining.State);Assert.Greater(Time.timeScale,0);
        }
        [UnityTest] public IEnumerator SgsPreviousAndSeatDraftHaveExplicitReturns()
        {
            yield return Choose(MenuActionId.FlySgs);yield return Back();yield return Choose(MenuActionId.Help);yield return Choose(MenuActionId.Instruments);
            var field=typeof(ProductAppNavigation).GetField("instrumentHelpIndex",BindingFlags.NonPublic|BindingFlags.Instance);
            for(int n=0;n<8;n++){yield return Choose(MenuActionId.NextInstrument);int index=(int)field.GetValue(nav);yield return Choose(MenuActionId.PreviousInstrument);Assert.AreEqual((index+7)%8,field.GetValue(nav));yield return Choose(MenuActionId.NextInstrument);}
            yield return Back();yield return Choose(MenuActionId.SeatSetup);yield return Back();
            Assert.AreEqual(ProductAppNavigation.Page.SeatExit,nav.CurrentPage);Assert.IsTrue(mode.AuthoredSeatCalibration.IsOpen);
            yield return Choose(MenuActionId.ContinueSeat);yield return Back();yield return Choose(MenuActionId.DiscardSeat);
            Assert.IsFalse(mode.AuthoredSeatCalibration.IsOpen);Assert.AreEqual(ProductAppNavigation.Page.Help,nav.CurrentPage);
        }
        [UnityTest] public IEnumerator SkipPreflightUsesVisibleActionReconnectsAndAllowsNativeLaunch()
        {
            foreach(var runway in new[]{MenuActionId.Runway08,MenuActionId.Runway26})
            {
                yield return Choose(MenuActionId.FlySgs);yield return Choose(MenuActionId.Soaring);yield return Choose(runway);yield return Transition();
                for(int n=0;n<120&&nav.IsOpen;n++)yield return NextFrame();
                var flight=mode.AuthoredFlightCoordinator;flight.EditorVerificationExternalClock=true;
                var c=new QuestFlightLab.Runtime.AircraftControlState();c.SetGliderNeutral();c.airbrake=c.wheelBrake=1;
                // Also exercise skipping AFTER the deliberate release disconnected the rope.
                flight.controls.SampleTowRelease(false,true);flight.controls.SampleTowRelease(true,true);
                for(int tick=0;tick<600;tick++)Assert.IsTrue(flight.StepForTest(c,1d/120),flight.LastError);
                Assert.IsFalse(flight.TowSession.RopeConnected);
                nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.Soaring);
                yield return Choose(MenuActionId.SkipSoaringPreflight,runway==MenuActionId.Runway08);
                Assert.IsTrue(flight.Operation.PreflightSkipped);Assert.IsFalse(flight.Operation.PreflightComplete);
                Assert.IsTrue(flight.TowSession.RopeConnected);Assert.IsTrue(flight.Operation.CrewReady);
                Assert.IsFalse(nav.CanExecute((int)MenuActionId.SkipSoaringPreflight,out _));
                Assert.AreNotEqual(255,flight.Operation.CheckedControls);
                yield return Choose(MenuActionId.Resume);for(int n=0;n<120&&nav.IsOpen;n++)yield return NextFrame();
                for(int tick=0;tick<600;tick++)Assert.IsTrue(flight.StepForTest(c,1d/120),flight.LastError);
                c.airbrake=c.wheelBrake=0;c.trim=flight.aircraftDefinition.referenceTrimNoseUp;
                for(int tick=0;tick<12;tick++){Assert.IsTrue(flight.StepForTest(c,1d/120),flight.LastError);flight.soaringTraffic.StepTraffic(flight.CurrentState,1f/120,flight.WeatherPreset);}
                nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.Soaring);
                Assert.IsTrue(flight.CanClearSoaringLaunch(out var why),why);yield return Choose(MenuActionId.ClearLaunch);
                for(int n=0;n<120&&nav.IsOpen;n++)yield return NextFrame();Assert.IsTrue(flight.Operation.LaunchAuthorized);
                for(int tick=0;tick<2400&&flight.CurrentState.groundSpeedKnots<2;tick++)
                {Assert.IsTrue(flight.StepForTest(c,1d/120),flight.LastError);flight.soaringTraffic.StepTraffic(flight.CurrentState,1f/120,flight.WeatherPreset);}
                Assert.Greater(flight.CurrentState.groundSpeedKnots,1,"Launch phase="+flight.TowSession.LaunchPhase+" rope="+flight.TowSession.RopeState);Assert.IsTrue(flight.TowSession.RopeConnected);
                nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.ConfirmHome);yield return Choose(MenuActionId.EndToHome);
            }
        }
        [UnityTest] public IEnumerator RunwaySkipStartsCockpitDirectlyWithoutResumeMenu()
        {
            yield return Choose(MenuActionId.FlySgs);
            yield return Choose(MenuActionId.Soaring);
            yield return Choose(MenuActionId.Runway08);
            yield return Choose(MenuActionId.SoaringStartSkipPreflight);

            Assert.AreEqual(ProductFlightModeCoordinator.FlightModeState.FreeFlight,mode.State,mode.LastError);
            Assert.AreEqual(ProductAppNavigation.Page.None,nav.CurrentPage,"A runway skip must not route through another menu.");
            Assert.IsFalse(nav.IsOpen,"A runway skip must enter the cockpit directly without requiring Resume.");
            Assert.Greater(Time.timeScale,0f,"The selected flight must be running immediately.");
            Assert.IsTrue(mode.AuthoredFlightCoordinator.Operation.PreflightSkipped);
            yield return Transition();
            Assert.IsFalse(mode.AuthoredModeMenuView.gameObject.activeSelf,"The final cockpit transition must complete with the menu closed.");
            for(int n=0;n<180&&!mode.AuthoredFlightCoordinator.Operation.LaunchAuthorized;n++)yield return NextFrame();
            mode.AuthoredFlightCoordinator.CanClearSoaringLaunch(out var launchBlock);
            Assert.IsTrue(mode.AuthoredFlightCoordinator.Operation.LaunchAuthorized,"The runway launch must start automatically after initial ground contact and when all safety gates pass. Remaining gate: "+launchBlock);
        }
        [UnityTest] public IEnumerator GroundChecksReconnectAndRepeatedReleaseUseRealMenuActions()
        {
            foreach(var runway in new[]{MenuActionId.Runway08,MenuActionId.Runway26})
            {
                yield return Choose(MenuActionId.FlySgs);yield return Choose(MenuActionId.Soaring);yield return Choose(runway);yield return Transition();
                for(int n=0;n<120&&nav.IsOpen;n++)yield return NextFrame();
                var flight=mode.AuthoredFlightCoordinator;flight.EditorVerificationExternalClock=true;var c=new QuestFlightLab.Runtime.AircraftControlState();c.SetGliderNeutral();
                // Native input trajectory, including the same one-event release latch used by Touch.
                c.airbrake=c.wheelBrake=1;c.trim=.17f;
                for(int tick=0;tick<480;tick++)
                {
                    c.aileron=c.elevator=c.rudder=tick<120?-.5f:tick<240?.5f:0;
                    if(tick==20){flight.controls.SampleTowRelease(false,true);flight.controls.SampleTowRelease(true,true);}
                    Assert.IsTrue(flight.StepForTest(c,1d/120),flight.LastError);
                }
                Assert.IsTrue(flight.Operation.PreflightComplete);Assert.IsFalse(flight.TowSession.RopeConnected);
                nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.Soaring);
                Assert.IsFalse(nav.CanExecute((int)MenuActionId.ClearLaunch,out _));
                yield return Choose(MenuActionId.CrewReconnect,true);Assert.IsTrue(flight.TowSession.RopeConnected);
                yield return Choose(MenuActionId.Resume);
                for(int n=0;n<120&&nav.IsOpen;n++)yield return NextFrame();
                flight.controls.SampleTowRelease(false,true);flight.controls.SampleTowRelease(true,true);
                Assert.IsTrue(flight.StepForTest(c,1d/120),flight.LastError);
                Assert.IsFalse(flight.Operation.CrewReady);Assert.IsFalse(flight.CanClearSoaringLaunch(out _));
                for(int tick=0;tick<600;tick++)Assert.IsTrue(flight.StepForTest(c,1d/120),flight.LastError);
                Assert.IsTrue(flight.CanReconnectSoaring(out var reconnectReason),reconnectReason+" WOW="+flight.CurrentState.weightOnWheels+" GS="+flight.CurrentState.groundSpeedKnots);
                nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.Soaring);yield return Choose(MenuActionId.CrewReconnect);
                yield return Choose(MenuActionId.Resume);for(int n=0;n<120&&nav.IsOpen;n++)yield return NextFrame();
                c.SetGliderNeutral();c.trim=flight.aircraftDefinition.referenceTrimNoseUp;
                c.airbrake=c.wheelBrake=1;for(int tick=0;tick<600;tick++)Assert.IsTrue(flight.StepForTest(c,1d/120),flight.LastError);
                c.airbrake=c.wheelBrake=0;
                for(int tick=0;tick<12;tick++){Assert.IsTrue(flight.StepForTest(c,1d/120),flight.LastError);if(tick%2==0)flight.soaringTraffic.StepTraffic(flight.CurrentState,1f/60,flight.WeatherPreset);}
                nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.Soaring);
                Assert.IsTrue(flight.CanClearSoaringLaunch(out var reason),reason+" WOW="+flight.CurrentState.weightOnWheels+" GS="+flight.CurrentState.groundSpeedKnots);yield return Choose(MenuActionId.ClearLaunch,true);
                for(int n=0;n<120&&nav.IsOpen;n++)yield return NextFrame();Assert.IsTrue(flight.Operation.LaunchAuthorized);
                // Deliberate low-speed abort must leave a truthful stopped outcome and explicit restart.
                nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.Soaring);yield return Choose(MenuActionId.GroundAbort);
                Assert.AreEqual(QuestFlightLab.Flight.Soaring.SoaringOperationPhase.Aborted,flight.Operation.Phase);
                yield return Choose(MenuActionId.Resume);for(int n=0;n<120&&nav.IsOpen;n++)yield return NextFrame();
                c.airbrake=c.wheelBrake=1;for(int tick=0;tick<600;tick++)Assert.IsTrue(flight.StepForTest(c,1d/120),flight.LastError);
                nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.Soaring);
                Assert.Less(flight.CurrentState.groundSpeedKnots,1);yield return Choose(MenuActionId.RestartGroundLaunch);yield return Choose(MenuActionId.ConfirmAction);yield return Transition();
                Assert.AreEqual(QuestFlightLab.Flight.Soaring.SoaringOperationPhase.ControlsCheck,flight.Operation.Phase);Assert.IsTrue(flight.TowSession.RopeConnected);
                nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.ConfirmHome);yield return Choose(MenuActionId.EndToHome);
            }
        }
        [UnityTest] public IEnumerator AerotowLessonReachesReleaseResultAndUnpoweredLandingThroughMenu()
        {
            yield return Choose(MenuActionId.FlySgs);yield return Choose(MenuActionId.AircraftLessons);yield return Transition();
            yield return Choose((MenuActionId)101);yield return Choose(MenuActionId.Practice);
            for(int n=0;n<600&&(mode.AuthoredTraining.State==TrainingSessionState.Preparing||nav.CurrentPage==ProductAppNavigation.Page.Handover);n++)yield return NextFrame();
            Assert.AreEqual(TrainingSessionState.Practice,mode.AuthoredTraining.State);
            var flight=mode.AuthoredFlightCoordinator;flight.EditorVerificationExternalClock=true;var c=new QuestFlightLab.Runtime.AircraftControlState();c.SetGliderNeutral();
            c.airbrake=c.wheelBrake=1;c.trim=.17f;
            for(int tick=0;tick<480;tick++)
            {
                c.aileron=c.elevator=c.rudder=tick<120?-.5f:tick<240?.5f:0;
                if(tick==300){flight.controls.SampleTowRelease(false,true);flight.controls.SampleTowRelease(true,true);}
                Assert.IsTrue(flight.StepForTest(c,1d/120),flight.LastError);
                if(tick%120==119)yield return NextFrame();
            }
            nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.Soaring);yield return Choose(MenuActionId.CrewReconnect);yield return Choose(MenuActionId.Resume);
            for(int n=0;n<120&&nav.IsOpen;n++)yield return NextFrame();
            c.SetGliderNeutral();c.trim=flight.aircraftDefinition.referenceTrimNoseUp;c.airbrake=c.wheelBrake=1;
            for(int tick=0;tick<600;tick++){Assert.IsTrue(flight.StepForTest(c,1d/120),flight.LastError);if(tick%120==119)yield return NextFrame();}
            c.airbrake=c.wheelBrake=0;Assert.IsTrue(flight.StepForTest(c,1d/120),flight.LastError);
            nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.Soaring);
            Assert.IsTrue(flight.CanClearSoaringLaunch(out var why),why);yield return Choose(MenuActionId.ClearLaunch);
            for(int n=0;n<120&&nav.IsOpen;n++)yield return NextFrame();
            bool released=false;int cueFrames=0;
            for(int tick=0;tick<120*1000&&mode.AuthoredTraining.State==TrainingSessionState.Practice;tick++)
            {
                var state=flight.CurrentState;
                QuestFlightLab.Editor.SoaringTestPilot.FollowRoute(state,flight.TowSession.TugState,c);
                if(!released&&flight.Operation.Phase==QuestFlightLab.Flight.Soaring.SoaringOperationPhase.ReleaseReady)
                {
                    var panel=(GameObject)typeof(ProductTrainingController).GetField("cueRoot",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(mode.AuthoredTraining);
                    var texts=panel.GetComponentsInChildren<TMPro.TextMeshPro>(true);
                    bool cueVisible=Array.Exists(texts,x=>x.gameObject.activeInHierarchy&&x.text.Contains("RELEASE AREA"));
                    Assert.Less(++cueFrames,1200,"Release cue never displayed: "+string.Join(" | ",Array.ConvertAll(texts,x=>x.text+" active="+x.gameObject.activeInHierarchy)));
                    if(cueVisible)
                    {flight.controls.SampleTowRelease(false,true);flight.controls.SampleTowRelease(true,true);released=true;}
                }
                if(released)QuestFlightLab.Editor.SoaringTestPilot.Follow(state,flight.TowSession.TugState,c,true);
                Assert.IsTrue(flight.StepForTest(c,1d/120),flight.LastError);
                if(tick%2==0)flight.soaringTraffic.StepTraffic(flight.CurrentState,1f/60,flight.WeatherPreset);
                if(tick%12==11)yield return NextFrame();
            }
            Assert.IsTrue(released,mode.AuthoredTraining.LastDebrief+" rope="+flight.TowSession.RopeState);Assert.AreEqual(TrainingSessionState.Debrief,mode.AuthoredTraining.State,mode.AuthoredTraining.LastDebrief);
            Assert.That(mode.AuthoredTraining.LastDebrief,Does.StartWith("Guided practice complete"));
            nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.ReturnLessonView);yield return Choose(MenuActionId.ContinueManual);
            for(int n=0;n<120&&nav.IsOpen;n++)yield return NextFrame();Assert.AreEqual(ProductFlightModeCoordinator.FlightModeState.FreeFlight,mode.State);
            var landing=new QuestFlightLab.Editor.SoaringLandingTestPilot();
            for(int tick=0;tick<120*1500&&flight.Operation.Phase!=QuestFlightLab.Flight.Soaring.SoaringOperationPhase.Landed;tick++)
            {
                var state=flight.CurrentState;landing.Step(state,flight.SampleWind(state.positionUnityMeters,state.simulationTimeSeconds),1,c);
                Assert.IsTrue(flight.StepForTest(c,1d/120),flight.LastError);
                if(tick%2==0)flight.soaringTraffic.StepTraffic(flight.CurrentState,1f/60,flight.WeatherPreset);
                Assert.IsFalse(flight.Operation.RequiresPause,flight.Operation.StopReason);
                if(tick%1200==1199)yield return NextFrame();
            }
            Assert.AreEqual(QuestFlightLab.Flight.Soaring.SoaringOperationPhase.Landed,flight.Operation.Phase);
            nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.Soaring);yield return Choose(MenuActionId.FinishSummary);yield return Choose(MenuActionId.ConfirmAction);
            Assert.AreEqual(ProductAppNavigation.Page.Summary,nav.CurrentPage);yield return Choose(MenuActionId.RepeatActivity);yield return Transition();
            Assert.IsNotNull(flight.Operation);Assert.AreEqual(QuestFlightLab.Flight.Soaring.SoaringOperationPhase.ControlsCheck,flight.Operation.Phase);
        }
        [UnityTest] public IEnumerator HomeAircraftSoaringBackAndPointerSelectionRestoreCaller()
        {
            yield return Choose(MenuActionId.FlySgs,true);Assert.IsTrue(mode.IsGlider);
            Assert.AreEqual(ProductAppNavigation.Page.GliderSetup,nav.CurrentPage);
            yield return Choose(MenuActionId.Soaring);yield return Back();
            Assert.AreEqual(ProductAppNavigation.Page.GliderSetup,nav.CurrentPage);
            Assert.AreEqual((int)MenuActionId.Soaring,nav.VisibleAction(nav.FocusedSlot));
            yield return Back();Assert.AreEqual(ProductAppNavigation.Page.Home,nav.CurrentPage);
            yield return Choose(MenuActionId.FlyC172);Assert.IsFalse(mode.IsGlider);
        }
        [UnityTest] public IEnumerator BothGroundLaunchRoutesFinishTheRealSgsTransition()
        {
            foreach(var runway in new[]{MenuActionId.Runway08,MenuActionId.Runway26})
            {
                yield return Choose(MenuActionId.FlySgs);yield return Choose(MenuActionId.Soaring);yield return Choose(runway);
                yield return Transition();Assert.AreEqual(ProductFlightModeCoordinator.FlightModeState.FreeFlight,mode.State);
                Assert.IsNotNull(mode.AuthoredFlightCoordinator.TowSession);
                nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.Soaring);
                Assert.AreEqual(ProductAppNavigation.Page.Soaring,nav.CurrentPage);
                yield return Back();Assert.AreEqual(ProductAppNavigation.Page.App,nav.CurrentPage);
                yield return Choose(MenuActionId.ConfirmHome);yield return Choose(MenuActionId.EndToHome);
                Assert.AreEqual(ProductAppNavigation.Page.Home,nav.CurrentPage);
            }
        }
        [UnityTest] public IEnumerator SettingsAndHelpPathsReturnToExactCaller()
        {
            yield return Choose(MenuActionId.Settings);yield return Choose(MenuActionId.Graphics);yield return Choose(MenuActionId.Comfort);
            yield return Choose(MenuActionId.CaptionSettings);yield return Back();Assert.AreEqual(ProductAppNavigation.Page.Comfort,nav.CurrentPage);
            yield return Back();Assert.AreEqual(ProductAppNavigation.Page.Graphics,nav.CurrentPage);
            yield return Back();Assert.AreEqual(ProductAppNavigation.Page.Settings,nav.CurrentPage);
            yield return Choose(MenuActionId.Controls);yield return Choose(MenuActionId.Bindings);yield return Choose(MenuActionId.ControlDiagram);
            yield return Back();yield return Back();yield return Choose(MenuActionId.InputPreview);yield return Back();yield return Back();yield return Back();
            Assert.AreEqual(ProductAppNavigation.Page.Home,nav.CurrentPage);
            yield return Choose(MenuActionId.Help);yield return Choose(MenuActionId.Checklist);yield return Choose(MenuActionId.Instruments);
            yield return Choose(MenuActionId.NextInstrument);yield return Back();Assert.AreEqual(ProductAppNavigation.Page.Checklist,nav.CurrentPage);
            yield return Back();yield return Choose(MenuActionId.Briefing);yield return Choose(MenuActionId.Activities);
            yield return Back();Assert.AreEqual(ProductAppNavigation.Page.Briefing,nav.CurrentPage);
            yield return Choose(MenuActionId.Map);yield return Back();Assert.AreEqual(ProductAppNavigation.Page.Briefing,nav.CurrentPage);
        }
        [UnityTest] public IEnumerator AllSevenSgsLessonsReachBriefingThroughPagination()
        {
            yield return Choose(MenuActionId.FlySgs);yield return Choose(MenuActionId.AircraftLessons);yield return Transition();
            for(int index=0;index<7;index++)
            {
                if(index==5)yield return Choose(MenuActionId.NextLessons);
                nav.ProcessMenuInput(false,false,false,false,0);yield return NextFrame();
                int slot=-1;for(int n=0;n<8;n++)if(nav.VisibleAction(n)==100+index)slot=n;
                Assert.That(slot,Is.GreaterThanOrEqualTo(0),$"Lesson {index} missing; page={nav.CurrentPage} state={mode.AuthoredTraining.State}");nav.FocusAction(slot);nav.ProcessMenuInput(false,false,true,false,0);yield return NextFrame();
                Assert.AreEqual(index,mode.AuthoredTraining.SelectedLesson);Assert.AreEqual(TrainingSessionState.Briefing,mode.AuthoredTraining.State);
                nav.ProcessMenuInput(false,false,false,false,0);yield return NextFrame();yield return Choose(MenuActionId.LessonList);
            }
        }
        [UnityTest] public IEnumerator SoaringTourMenuStartsPausesRestartsAndReturnsToPoweredDemo()
        {
            yield return Choose(MenuActionId.FlySgs);yield return Choose(MenuActionId.SoaringDemo);
            Assert.AreEqual(ProductAppNavigation.Page.SoaringDemo,nav.CurrentPage);
            yield return Choose(MenuActionId.StartDemo);yield return Transition();
            Assert.IsTrue(mode.AuthoredDemoPilot.IsSoaringDemo);Assert.IsNotNull(mode.AuthoredFlightCoordinator.TowSession);
            nav.OpenMenu();yield return NextFrame();Assert.AreEqual(0,Time.timeScale);
            Assert.IsFalse(nav.CanExecute((int)MenuActionId.TakeControl,out _));
            int session=mode.FlightSessionVersion;
            yield return Choose(MenuActionId.SoaringDemo);yield return Choose(MenuActionId.StartShowcase);
            Assert.AreEqual(ProductAppNavigation.Page.ConfirmAction,nav.CurrentPage);
            yield return Back();Assert.AreEqual(session,mode.FlightSessionVersion);
            yield return Choose(MenuActionId.StartShowcase);yield return Choose(MenuActionId.ConfirmAction);yield return Transition();
            Assert.IsTrue(mode.AuthoredDemoPilot.IsSoaringDemo);Assert.IsTrue(mode.AuthoredDemoPilot.ShortShowcase);
            Assert.IsNull(mode.AuthoredFlightCoordinator.TowSession);
            nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.ConfirmHome);yield return Choose(MenuActionId.EndToHome);
            yield return Choose(MenuActionId.FlyC172);yield return Choose(MenuActionId.StartDemo);yield return Transition();
            Assert.IsTrue(mode.AuthoredDemoPilot.IsRunning);Assert.IsFalse(mode.AuthoredDemoPilot.IsSoaringDemo);
        }
        [UnityTest] public IEnumerator AirbornePauseAndResumeDoNotReleaseTowOrLoseNativeAuthority()
        {
            yield return Choose(MenuActionId.FlySgs);yield return Choose(MenuActionId.StartFree);yield return Transition();
            nav.OpenMenu();yield return NextFrame();Assert.AreEqual(0,Time.timeScale);
            yield return Choose(MenuActionId.Resume);Assert.AreEqual(1,Time.timeScale);
            Assert.IsFalse(mode.AuthoredProductInput.ConsumeTowRelease());
            Assert.AreEqual(1,world.GetComponentsInChildren<QuestFlightLab.Flight.Backends.FlightDynamicsCoordinator>(true).Length);
        }
        [UnityTest] public IEnumerator ActivityReplacementRequiresConfirmationAndCancelPreservesFlight()
        {
            yield return Choose(MenuActionId.FlyC172);yield return Choose(MenuActionId.StartFree);yield return Transition();
            nav.OpenMenu();yield return NextFrame();yield return Choose(MenuActionId.Tools);yield return Choose(MenuActionId.Activities);
            int version=mode.FlightSessionVersion;yield return Choose(MenuActionId.CircuitActivity);
            Assert.AreEqual(ProductAppNavigation.Page.ConfirmAction,nav.CurrentPage);yield return Back();
            Assert.AreEqual(ProductAppNavigation.Page.Activities,nav.CurrentPage);Assert.AreEqual(version,mode.FlightSessionVersion);Assert.AreEqual(0,Time.timeScale);
            yield return Choose(MenuActionId.CircuitActivity);
            Assert.AreEqual(ProductAppNavigation.Page.ConfirmAction,nav.CurrentPage,"Cancelling must not bypass the next confirmation.");
            yield return Choose(MenuActionId.ConfirmAction);yield return Transition();
            for(int n=0;n<120&&mode.AuthoredTraining.State!=TrainingSessionState.Briefing;n++)yield return NextFrame();
            Assert.AreEqual("05_pattern",mode.AuthoredTraining.Lessons[mode.AuthoredTraining.SelectedLesson].lessonId);
        }
        [UnityTest] public IEnumerator AllSixC172LessonsAndReferenceActionsAreReachable()
        {
            yield return Choose(MenuActionId.FlyC172);yield return Choose(MenuActionId.AircraftLessons);yield return Transition();
            for(int index=0;index<6;index++)
            {
                if(index==5)yield return Choose(MenuActionId.NextLessons);
                yield return Choose((MenuActionId)(100+index));Assert.AreEqual(index,mode.AuthoredTraining.SelectedLesson);
                yield return Choose(MenuActionId.Orientation);yield return Choose(MenuActionId.NextReference);yield return Choose(MenuActionId.PreviousReference);
                yield return Back();Assert.AreEqual(ProductAppNavigation.Page.None,nav.CurrentPage);
                yield return Choose(MenuActionId.LessonList);
            }
        }
        [UnityTest] public IEnumerator GuestGuideCalibrationHistoryAndReportsHaveWorkingReturns()
        {
            yield return Choose(MenuActionId.Settings);yield return Choose(MenuActionId.Guest);yield return Choose(MenuActionId.ConfirmGuest);
            Assert.IsTrue(mode.AuthoredTraining.IsGuestSession);yield return Back();Assert.AreEqual(ProductAppNavigation.Page.Home,nav.CurrentPage);
            yield return Choose(MenuActionId.Settings);yield return Choose(MenuActionId.Guest);yield return Choose(MenuActionId.ConfirmGuest);
            Assert.IsFalse(mode.AuthoredTraining.IsGuestSession);yield return Choose(MenuActionId.Help);yield return Choose(MenuActionId.QuickStart);yield return Choose(MenuActionId.SkipGuide);yield return Back();
            Assert.AreEqual(ProductAppNavigation.Page.Settings,nav.CurrentPage);yield return Back();
            yield return Choose(MenuActionId.FlyC172);yield return Choose(MenuActionId.SeatSetup);yield return Choose(MenuActionId.ResetSeat);yield return Choose(MenuActionId.SaveSeat);
            Assert.AreEqual(ProductAppNavigation.Page.C172Setup,nav.CurrentPage);yield return Back();
            yield return Choose(MenuActionId.History);yield return Back();Assert.AreEqual(ProductAppNavigation.Page.Home,nav.CurrentPage);
            yield return Choose(MenuActionId.Help);yield return Choose(MenuActionId.Report);
            foreach(var category in new[]{MenuActionId.VisualIssue,MenuActionId.AudioIssue,MenuActionId.ControlIssue,MenuActionId.OtherIssue})yield return Choose(category);
            yield return Back();Assert.AreEqual(ProductAppNavigation.Page.Help,nav.CurrentPage);
        }
        [UnityTest] public IEnumerator PreexistingTrainingPauseActuallyResumesAndKeepsLaunchActions()
        {
            yield return Choose(MenuActionId.FlySgs);yield return Choose(MenuActionId.AircraftLessons);yield return Transition();
            yield return Choose((MenuActionId)102);yield return Choose(MenuActionId.Practice);
            for(int n=0;n<600&&(mode.AuthoredTraining.State==TrainingSessionState.Preparing||nav.CurrentPage==ProductAppNavigation.Page.Handover);n++)yield return NextFrame();
            Assert.AreEqual(TrainingSessionState.Practice,mode.AuthoredTraining.State);
            mode.AuthoredTraining.PauseExercise("Software fixture interruption");nav.OpenMenu();yield return NextFrame();
            yield return Choose(MenuActionId.Soaring);yield return Back();yield return Choose(MenuActionId.Resume);
            Assert.AreEqual(TrainingSessionState.Practice,mode.AuthoredTraining.State);Assert.AreEqual(1,Time.timeScale);
        }
        [UnityTest] public IEnumerator EverySettingsControlRespondsThroughTheCommonDispatcher()
        {
            string[] keys={"VoiceVolume","AircraftVolume","Captions","TextScale","HighContrast","Haptics","InterfaceFeedback","TrafficRadio","Gamepad.Trim","CaptionScale","SolidCaptions","Touch.Deadzone","Touch.Curve","Touch.PitchTravel","Touch.RollTravel","Touch.Trim","LeftStickSteering","CombinedBrakes","ReverseTrim","ToggleYoke"};
            var values=new System.Collections.Generic.Dictionary<string,string>();
            // Preserve exact preference storage types using their known contracts.
            string[] flags={"Captions","HighContrast","InterfaceFeedback","TrafficRadio","SolidCaptions","LeftStickSteering","CombinedBrakes","ReverseTrim","ToggleYoke"};
            foreach(var key in keys){string full="QuestFlight."+key;values[key]=!PlayerPrefs.HasKey(full)?null:Array.IndexOf(flags,key)>=0?PlayerPrefs.GetInt(full).ToString():PlayerPrefs.GetFloat(full).ToString(System.Globalization.CultureInfo.InvariantCulture);}
            try
            {
                yield return Choose(MenuActionId.Settings);yield return Adjust(MenuActionId.VoiceVolume);yield return Adjust(MenuActionId.AircraftVolume);yield return Choose(MenuActionId.Captions);
                yield return Choose(MenuActionId.Graphics);yield return Choose(MenuActionId.Comfort);
                foreach(var action in new[]{MenuActionId.TextSize,MenuActionId.Contrast,MenuActionId.Haptics,MenuActionId.MenuSounds,MenuActionId.TrafficRadio,MenuActionId.ExternalTrim}){if(action==MenuActionId.TextSize||action==MenuActionId.Haptics||action==MenuActionId.ExternalTrim)yield return Adjust(action);else yield return Choose(action);}
                yield return Choose(MenuActionId.CaptionSettings);yield return Adjust(MenuActionId.CaptionSize);yield return Choose(MenuActionId.CaptionBackground);yield return Choose(MenuActionId.Captions);yield return Back();yield return Back();yield return Back();
                yield return Choose(MenuActionId.Controls);
                foreach(var action in new[]{MenuActionId.Deadzone,MenuActionId.RudderCurve,MenuActionId.PitchTravel,MenuActionId.RollTravel,MenuActionId.TrimSpeed})yield return Adjust(action);
                yield return Choose(MenuActionId.Bindings);
                foreach(var action in new[]{MenuActionId.GentlePreset,MenuActionId.LeftSteeringPreset,MenuActionId.SteeringHand,MenuActionId.CombinedBrakes,MenuActionId.ReverseTrim,MenuActionId.ResetControls})yield return Choose(action);
                yield return Back();yield return Choose(MenuActionId.InputPreview);yield return Back();yield return Back();yield return Back();
                Assert.AreEqual(ProductAppNavigation.Page.Home,nav.CurrentPage);
            }
            finally
            {
                foreach(var item in values){string full="QuestFlight."+item.Key;if(item.Value==null)PlayerPrefs.DeleteKey(full);else if(Array.IndexOf(flags,item.Key)>=0)PlayerPrefs.SetInt(full,int.Parse(item.Value));else PlayerPrefs.SetFloat(full,float.Parse(item.Value,System.Globalization.CultureInfo.InvariantCulture));}
            }
        }
    }
}
#endif
