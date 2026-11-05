using System;
using System.Collections.Generic;
using QuestFlight.Product.Training;
using QuestFlightLab.Flight.Soaring;
using UnityEngine;

namespace QuestFlight.Product
{
    // Extends the existing authored navigation owner; no runtime UI or aircraft creation.
    public sealed partial class ProductAppNavigation
    {
        private Action handoverCompletion;
        private Page handoverReturn;
        private int lessonReturnFocus, adjustmentAction;
        private bool safetyMenuPresented,commandPending;
        private readonly Queue<string> actionTrace=new Queue<string>(24);
        public string[] RecentMenuActions=>actionTrace.ToArray();
        public string WorkflowDiagnostic=>$"page={CurrentPage}; focus={focus}; pending={pendingMenuAction}; handover={handoverCompletion!=null}; result={LastActionResult}; operation={modes.AuthoredFlightCoordinator.Operation?.Phase}; rope={modes.AuthoredFlightCoordinator.TowSession?.RopeState}";
        private bool HasLessonReturn=>modes.State==ProductFlightModeCoordinator.FlightModeState.Training &&
            !training.IsFlying && training.State!=TrainingSessionState.Paused && training.State!=TrainingSessionState.Inactive;
        private string LessonReturnLabel=>training.State switch
        {
            TrainingSessionState.Menu=>"Return to lessons",
            TrainingSessionState.Briefing=>"Return to briefing",
            TrainingSessionState.Preparing=>"Continue preparation",
            TrainingSessionState.Debrief=>"Return to results",
            _=>"Return to lesson status"
        };
        private string SoaringPageTitle=>modes.AuthoredFlightCoordinator.Operation==null ||
            modes.AuthoredFlightCoordinator.Operation.Phase>=SoaringOperationPhase.Tow?"Flight status":"Launch preparation";
        private void ReturnLessonView()
        {
            if(!HasLessonReturn){Reject("No lesson view to return to.");return;}
            CurrentPage=Page.None;routes.Clear();focus=lessonReturnFocus;
            if(ownsPause && training.State==TrainingSessionState.Preparing)Time.timeScale=savedTimeScale;
            ownsPause=pausedTraining=false;pauseNotice=null;Consume();Refresh();
        }
        public bool CanTakeManualControl(out string reason)
        {
            reason=null;var f=modes.AuthoredFlightCoordinator;
            if(!FocusAvailable)reason="Put on the headset and restore app focus.";
            else if(!f.IsInitialized||!f.enabled||!f.CurrentState.IsFinite||f.RecoveryHold||recoveryAssistance!=null)reason="Aircraft held or unavailable. Restart or return Home.";
            else if(Application.platform==RuntimePlatform.Android&&!input.AuthoredGamepad.Current.connected&&
                (!OVRInput.IsControllerConnected(OVRInput.Controller.LTouch)||!OVRInput.IsControllerConnected(OVRInput.Controller.RTouch)||
                 !OVRInput.GetControllerOrientationTracked(OVRInput.Controller.LTouch)||!OVRInput.GetControllerOrientationTracked(OVRInput.Controller.RTouch)))reason="Wake both controllers and restore tracking.";
            return reason==null;
        }
        private void BeginHandover(Action complete)
        {
            if(!CanTakeManualControl(out var reason)){Reject(reason);return;}
            handoverReturn=CurrentPage;handoverCompletion=complete;
            input.RequireTouchRelease();input.SetFlightLessonInputSuppressed(true);Time.timeScale=0;
            CurrentPage=Page.Handover;LastActionResult="PENDING: release inputs to continue";Consume();Refresh();
        }
        public void BeginFlightStartHandover()
        {
            if(!IsOpen)OpenMenu();
            Resume();
        }
        private void CancelHandover()
        {
            handoverCompletion=null;CurrentPage=handoverReturn==Page.None?Page.App:handoverReturn;
            pauseNotice="Still paused. No control handover occurred.";Consume();Refresh();
        }
        private void UpdateWorkflow()
        {
            var f=modes.AuthoredFlightCoordinator;
            if(commandPending && handoverCompletion==null && !modes.AuthoredModeMenuView.TransitionPending && training.State!=TrainingSessionState.Preparing)
            {commandPending=false;LastActionResult=modes.State==ProductFlightModeCoordinator.FlightModeState.Blocked||training.State==TrainingSessionState.Blocked?"UNAVAILABLE: "+(modes.LastError??training.BlockedReason):"COMPLETED: pending transition";}
            if(handoverCompletion!=null)
            {
                if(!CanTakeManualControl(out var reason)){pauseNotice=reason;return;}
                if(input.FlightInputAwaitingRelease||selectHeld||menuHeld||backHeld||repeatHeld)return;
                var complete=handoverCompletion;handoverCompletion=null;
                complete();LastActionResult=actionFailure==null?"COMPLETED: control handover":"UNAVAILABLE: "+actionFailure;
            }
            bool held=f.RecoveryHold||(!f.enabled&&f.IsInitialized);
            if(!held)safetyMenuPresented=false;
            if(held&&!safetyMenuPresented&&!IsOpen)
            {
                safetyMenuPresented=true;OpenMenu();
                pauseNotice=f.Operation?.StopReason??f.LastError;
                if(string.IsNullOrEmpty(pauseNotice))pauseNotice="Flight held. Choose an explicit restart or Home.";
                Refresh();
            }
        }
        private bool CanExecuteWorkflow(int action,out string reason)
        {
            reason=null;
            if(action==335&&(!training.DemonstrationPaused&&!training.AssistancePaused))reason="Manual handback is available from a paused demonstration or assistance.";
            else if(action==335)return CanTakeManualControl(out reason);
            else if(action==323&&!HasLessonReturn)reason="No lesson view to return to.";
            else if(action==324&&training.State!=TrainingSessionState.Preparing)reason="No preparation is pending.";
            else if(action==309)return modes.CanContinueSoaringTrainingFlight(out reason);
            else if(action==10 && (modes.State!=ProductFlightModeCoordinator.FlightModeState.Training ||
                (training.State!=TrainingSessionState.Briefing&&training.State!=TrainingSessionState.Paused&&training.State!=TrainingSessionState.Debrief&&training.State!=TrainingSessionState.Demonstration)))reason="Choose a lesson briefing before starting practice.";
            else if((action==11||action==19)&&training.State!=TrainingSessionState.Briefing)reason="Return to a lesson briefing first.";
            else if(action==11&&!training.DemonstrationAvailable)reason="This lesson has no demonstration.";
            else if((action==325||action==326)&&!modes.IsGlider)reason="This action requires the SGS sailplane.";
            else if(action==334&&(experience==null||!experience.HasRepeatActivity))reason="This older record has no reproducible activity. Choose a flight from Home.";
            else if(action==317&&pendingMenuAction==0)reason="No action is pending. Cancel to return.";
            return reason==null;
        }
        private void RestartGroundLaunch()
        {
            int index=Array.IndexOf(modes.AuthoredFlightCoordinator.soaringOperations,modes.AuthoredFlightCoordinator.Operation?.Definition);
            experience?.FinishFlight("Ground launch preparation restarted");ReturnMain();
            StartSoaringOperation(Math.Max(0,index));
        }
        private bool InvokeWorkflowAction(int action)
        {
            switch(action)
            {
                case 323:ReturnLessonView();break;
                case 324:training.CancelPreparation();CurrentPage=Page.None;routes.Clear();ownsPause=pausedTraining=false;break;
                case 325:RestartGroundLaunch();break;
                case 326:StartActivity(-3);break;
                case 327:FinishQuickStart(false);break;
                case 328:StartActivityById(0,modes.IsGlider?"sgs233.reference.preparation":"01_taxi");break;
                case 329:AdjustSetting(adjustmentAction,-1);break;
                case 330:AdjustSetting(adjustmentAction,1);break;
                case 331:ResetSelectedSetting();break;
                case 332:
                    calibration.Cancel();
                    if(CurrentPage==Page.SeatExit)TryPopRoute();
                    if(!TryPopRoute())CurrentPage=calibrationReturnPage;
                    break;
                case 333:if(!TryPopRoute())CurrentPage=Page.Calibration;break;
                case 334:RepeatSelectedActivity();break;
                case 335:BeginHandover(()=>{training.TakeManualControlFromPause();CurrentPage=Page.None;routes.Clear();pausedTraining=ownsPause=false;Refresh();});break;
                case 336:OperationResult(modes.AuthoredFlightCoordinator.TrySkipSoaringPreflight(),"Preflight skipped. Rope connected and pair prepared. No checklist credit. Close brakes and center controls, then Clear and begin launch.","Could not prepare launch. Stop the aircraft and retry, or restart preparation.");break;
                default:return false;
            }
            return true;
        }
        private bool PresentWorkflowPage()
        {
            if(CurrentPage==Page.Handover)
            {
                heading.text="Ready to continue";
                detail.text="Flight is paused. Release A, Menu, both grips, index triggers and thumbsticks. The flight continues after the inputs are released.\n"+input.MenuInputHoldReason;
                Choice(7,"Cancel · stay paused",5);return true;
            }
            if(CurrentPage==Page.SeatExit)
            {
                heading.text="Leave seat setup?";detail.text="Save your seat offset, discard this draft, or continue editing. The aircraft remains paused.";
                Choice(0,"Save seat position",6);Choice(1,"Discard changes",332);Choice(7,"Continue editing",333);return true;
            }
            if(CurrentPage==Page.SettingAdjustment)
            {
                heading.text="Adjust setting";detail.text=SettingValueText(adjustmentAction)+"\nUse Decrease / Increase, or stick left / right. Values stop at the supported limits.\n"+pauseNotice;
                Choice(0,"Decrease",329);Choice(1,"Increase",330);Choice(2,"Restore default",331);Choice(7,"Back",5);return true;
            }
            return false;
        }
        private static bool AdjustableAction(int action)=>action==23||action==24||action==51||action==52||action==53||action==54||action==55||action==57||action==59||action==71||action==131;
        private string SettingValueText(int action)=>action switch
        {
            23=>$"Voice volume: {voiceLevel:P0}"+(voiceLevel==0?" (muted)":""),24=>$"Aircraft sound: {aircraftLevel:P0}"+(aircraftLevel==0?" (muted)":""),
            51=>$"Rudder deadzone: {ProductComfortPreferences.RudderDeadzone:P0}",52=>$"Rudder response: {ProductComfortPreferences.RudderCurve:0.00}",
            53=>$"Pitch travel: {ProductComfortPreferences.PitchTravel:0.00}x",54=>$"Roll travel: {ProductComfortPreferences.RollTravel:0.00}x",
            55=>$"Touch trim speed: {ProductComfortPreferences.TrimRate(true):0.00}x",71=>$"External controller trim speed: {ProductComfortPreferences.TrimRate(false):0.00}x",
            57=>$"Menu text size: {ProductComfortPreferences.TextScale:P0}",59=>$"Vibration: {ProductComfortPreferences.Haptics:P0}",131=>$"Instruction text size: {ProductComfortPreferences.CaptionScale:P0}",_=>"Setting unavailable"
        };
        private void ResetSelectedSetting()
        {
            if(adjustmentAction==23){voiceLevel=1;ApplyAudioLevels();return;}
            if(adjustmentAction==24){aircraftLevel=1;ApplyAudioLevels();return;}
            string key=adjustmentAction switch{51=>"Touch.Deadzone",52=>"Touch.Curve",53=>"Touch.PitchTravel",54=>"Touch.RollTravel",55=>"Touch.Trim",71=>"Gamepad.Trim",57=>"TextScale",59=>"Haptics",131=>"CaptionScale",_=>null};
            if(key!=null){PlayerPrefs.DeleteKey("QuestFlight."+key);PlayerPrefs.Save();}
        }
        private void RepeatSelectedActivity()
        {
            if(!experience.TryRepeatActivity(out bool glider,out string recipe,out string lesson,out int runway)){Reject("This record has no repeatable start.");return;}
            experience.FinishFlight("Session replaced by repeat activity");ReturnMain();
            if(!modes.SelectAircraft(glider)){Reject("Aircraft preparation failed.");return;}
            if(recipe=="ground")StartSoaringOperation(runway);
            else StartActivityById(recipe=="lesson"?0:recipe=="demo"?-2:recipe=="showcase"?-4:-3,lesson);
        }
    }
}
