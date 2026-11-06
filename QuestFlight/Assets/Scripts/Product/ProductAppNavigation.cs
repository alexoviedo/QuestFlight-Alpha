using QuestFlight.Product.Training;

using QuestFlightLab.Input;

using QuestFlightLab.Runtime;

using UnityEngine;

namespace QuestFlight.Product

{

    /// <summary>One app-wide Menu/Back owner. All UI is editor-authored.</summary>

    [DefaultExecutionOrder(-9900), DisallowMultipleComponent]

    public sealed partial class ProductAppNavigation : MonoBehaviour, IProductPanelActions

    {

        public enum Page { None, App, ConfirmMain, Calibration, Explanation, Orientation, Settings, QuickStart, Guest, Tools, Controls, Comfort, Briefing, Map, Kneeboard, Activities, History, Summary, Report, Bindings, InputPreview, InstrumentHelp, Captions, ControlDiagram, Checklist, Graphics, Aircraft, Soaring, Home, C172Setup, GliderSetup, Help, ConfirmAction, Handover, SettingAdjustment, SeatExit, SoaringDemo }

        [SerializeField] private ProductFlightModeCoordinator modes;

        [SerializeField] private ProductTrainingController training;

        [SerializeField] private ProductControlInput input;

        [SerializeField] private ProductionSeatCalibrationController calibration;

        [SerializeField] private GameObject panel;

        [SerializeField] private Transform backplate;

        [SerializeField] private TextMesh heading, detail, footer;

        [SerializeField] private ProductActionButton[] buttons;

        public Page CurrentPage { get; private set; }

        public bool IsOpen => CurrentPage != Page.None;

        public bool ConsumedThisFrame => consumedFrame == Time.frameCount;

        public bool InputReady => !releaseRequired;

        public bool HasAuthoredReferences => modes != null && training != null && input != null && calibration != null && panel != null && backplate != null && heading != null && detail != null && buttons != null && buttons.Length == 8;

        private bool menuHeld, backHeld, selectHeld, repeatHeld, releaseRequired;

        private int navigationDirection;

        private float nextNavigationTime;

        private bool pausedTraining, ownsPause;

        private string pauseNotice;

        private bool controllersSeen;

        private float controllerMissingSince=-1;

        private float savedTimeScale = 1, refreshAt;

        private int focus, consumedFrame = -1;

        [SerializeField] private ProductUiPanelLayout polishedLayout;

        public void ConfigurePolishedLayout(ProductUiPanelLayout value) => polishedLayout=value;

        [SerializeField] private AudioSource instructorVoice;

        [SerializeField] private QuestFlightLab.Audio.ProductionC172AudioController aircraftAudio;

        private float voiceLevel=1,aircraftLevel=1;

        public static bool CaptionsEnabled => PlayerPrefs.GetInt("QuestFlight.Captions",1)!=0;

        public static bool LargeInstructionTextEnabled => PlayerPrefs.GetInt("QuestFlight.LargeInstructionText",0)!=0;

        public static bool FeedbackEnabled => PlayerPrefs.GetInt("QuestFlight.InterfaceFeedback",1)!=0;

        [SerializeField] private ProductUiInstruction[] instructionPanels = System.Array.Empty<ProductUiInstruction>();

        private Vector3[] instructionScales;

        private bool firstUsePending;

        private int guideStep;

        private Page calibrationReturnPage=Page.App, guideReturnPage=Page.App;

        public void ConfigureGuestPresentation(ProductUiInstruction[] panels) => instructionPanels=panels;

        public void ConfigureAudio(AudioSource voice,QuestFlightLab.Audio.ProductionC172AudioController aircraft) { instructorVoice=voice;aircraftAudio=aircraft; }

        private string[] labels = new string[8];

        private int[] actions = new int[8];

        private QuestFlightLab.Training.FlightLessonV1.FlightLessonControlCommand pausedControls;

        private TrainingSessionState presentedState;

        public void Configure(ProductFlightModeCoordinator m, ProductTrainingController t, ProductControlInput i,

            ProductionSeatCalibrationController c, GameObject root, Transform surface, TextMesh title, TextMesh body, TextMesh hint, ProductActionButton[] choices)

        { modes=m; training=t; input=i; calibration=c; panel=root; backplate=surface; heading=title; detail=body; footer=hint; buttons=choices; }

        [SerializeField] private ProductFlightExperience experience;

        public void ConfigureExperience(ProductFlightExperience value) => experience=value;

        private void Start()

        {

            if (!HasAuthoredReferences) { Debug.LogError("[Navigation] Authored references missing.",this); enabled=false; return; }

            panel.SetActive(false); releaseRequired=true;

            firstUsePending=PlayerPrefs.GetInt("QuestFlight.QuickStartSeen",0)==0;

            instructionScales=new Vector3[instructionPanels.Length];

            for(int n=0;n<instructionPanels.Length;n++)if(instructionPanels[n]!=null)instructionScales[n]=instructionPanels[n].transform.localScale;

            if(!PlayerPrefs.HasKey("QuestFlight.TextScale") && LargeInstructionTextEnabled)ProductComfortPreferences.Set("TextScale",1.2f);

            ApplyInstructionSize();

            voiceLevel=Mathf.Clamp01(PlayerPrefs.GetFloat("QuestFlight.VoiceVolume",1));aircraftLevel=Mathf.Clamp01(PlayerPrefs.GetFloat("QuestFlight.AircraftVolume",1));ApplyAudioLevels();
            OpenMenu();
        }

        #if UNITY_EDITOR

        // Editor-only fixture input; never compiled into the standalone Quest product.

        public bool? EditorVerificationFocus { get; set; }

#endif

        private bool FocusAvailable

        {

            get

            {

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR

                if(!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_TRAINING_VIEW"))) return true;

#endif

#if UNITY_EDITOR

                if(EditorVerificationFocus.HasValue)return EditorVerificationFocus.Value;

#endif

                return Application.isFocused && (Application.platform!=RuntimePlatform.Android ||

                    (OVRManager.hasInputFocus && (OVRManager.instance==null||OVRManager.instance.isUserPresent)));

            }

        }

        private void Update()

        {

            calibration.DraftEditingEnabled=CurrentPage==Page.Calibration;
            UpdateWorkflow();
            var gamepad=input.AuthoredGamepad.Current;

            bool menu=OVRInput.Get(OVRInput.RawButton.Start)||(gamepad.connected&&gamepad.startButton);

            bool back=OVRInput.Get(OVRInput.RawButton.B)||OVRInput.Get(OVRInput.RawButton.Y)||(gamepad.connected&&gamepad.buttonEast);

            bool select=OVRInput.Get(OVRInput.RawButton.A)||(gamepad.connected&&gamepad.buttonSouth);

            bool repeat=OVRInput.Get(OVRInput.RawButton.X)||(gamepad.connected&&gamepad.buttonWest);

            float leftAxis=OVRInput.Get(OVRInput.RawAxis2D.LThumbstick).y;

            float rightAxis=OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).y;

            float axis=Mathf.Abs(leftAxis)>=Mathf.Abs(rightAxis)?leftAxis:rightAxis;

            if(gamepad.connected&&Mathf.Abs(gamepad.leftStickY)>Mathf.Abs(axis))axis=gamepad.leftStickY;

            bool adjusting=CurrentPage==Page.Calibration && calibration.IsAdjustingSeat;

            if(Application.platform==RuntimePlatform.Android)

            {

                bool connected=OVRInput.IsControllerConnected(OVRInput.Controller.LTouch)&&OVRInput.IsControllerConnected(OVRInput.Controller.RTouch)&&

                    OVRInput.GetControllerOrientationTracked(OVRInput.Controller.LTouch)&&OVRInput.GetControllerOrientationTracked(OVRInput.Controller.RTouch);

                if(connected){controllersSeen=true;controllerMissingSince=-1;}

                else if(controllersSeen && input.AuthoredGamepad.Current.connected==false)

                {

                    if(controllerMissingSince<0)controllerMissingSince=Time.unscaledTime;

                    bool manual=modes.State==ProductFlightModeCoordinator.FlightModeState.FreeFlight||modes.State==ProductFlightModeCoordinator.FlightModeState.Training||

                        modes.State==ProductFlightModeCoordinator.FlightModeState.Demo&&modes.AuthoredDemoPilot.UserHasControls;

                    if(manual&&!IsOpen&&Time.unscaledTime-controllerMissingSince>.75f)

                    {OpenMenu();pauseNotice="A controller lost tracking or disconnected. Wake both controllers, then choose Resume. Flight remains paused.";input.RequireTouchRelease();}

                }

            }

            if(OVRInput.Get(OVRInput.RawAxis1D.LIndexTrigger)<.1f&&OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger)<.1f)pointerRequiresRelease=false;
            float horizontal=OVRInput.Get(OVRInput.RawAxis2D.LThumbstick).x;
            float rightHorizontal=OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).x;
            if(Mathf.Abs(rightHorizontal)>Mathf.Abs(horizontal))horizontal=rightHorizontal;
            if(gamepad.connected&&Mathf.Abs(gamepad.leftStickX)>Mathf.Abs(horizontal))horizontal=gamepad.leftStickX;
            ProcessMenuInput(menu,back,select,repeat,axis,adjusting,horizontal);
        }
        // Device polling and editor fixtures share the exact edge/dispatch path.
        public void ProcessMenuInput(bool menu,bool back,bool select,bool repeat,float axis,bool adjusting=false,float horizontal=0)
        {
            bool md=menu&&!menuHeld, bd=back&&!backHeld, ad=select&&!selectHeld, xd=repeat&&!repeatHeld;

            menuHeld=menu; backHeld=back; selectHeld=select; repeatHeld=repeat;

            if (!FocusAvailable)

            {

                if (!IsOpen && (modes.State==ProductFlightModeCoordinator.FlightModeState.FreeFlight || modes.State==ProductFlightModeCoordinator.FlightModeState.Demo)) OpenMenu();

                if(modes.State==ProductFlightModeCoordinator.FlightModeState.FreeFlight || modes.State==ProductFlightModeCoordinator.FlightModeState.Demo)

                    pauseNotice="Flight paused while the headset or app focus was unavailable. Choose Resume when you are ready.";

                releaseRequired=true; panel.SetActive(false); return;

            }

            if (releaseRequired)

            {

                if (!menu&&!back&&!select&&!repeat) releaseRequired=false;

                md=bd=ad=xd=false;

                navigationDirection=0;

            }

            if (!IsOpen && modes.State==ProductFlightModeCoordinator.FlightModeState.Selection && modes.AuthoredFlightCoordinator.IsInitialized && !modes.AuthoredModeMenuView.TransitionPending)

            { OpenMenu(); }

            if(modes.State==ProductFlightModeCoordinator.FlightModeState.Blocked&&!IsOpen)OpenMenu();
            UpdateSetupChecks();

            if (md) { Consume(); OpenMenu(); }

            else if (bd) { Consume(); Back(); }

            else if (IsOpen || TrainingPanelVisible)

            {

                Navigate(axis,adjusting);

                AdjustFocusedSetting(adjusting||Mathf.Abs(axis)>.35f,horizontal);

                if (ad && !adjusting) InvokeAction(focus);

                if (xd && CurrentPage==Page.Calibration) { Consume(); calibration.ToggleIncrementSize(); }

                else if (xd && modes.State==ProductFlightModeCoordinator.FlightModeState.Training && (CurrentPage==Page.None||CurrentPage==Page.Explanation||CurrentPage==Page.Orientation))

                {

                    Consume();

                    if(CurrentPage==Page.Orientation) training.ShowReference();

                    else if(CurrentPage==Page.Explanation) training.ExplainCurrent();

                    else training.RepeatCurrent();

                }

            }

            else if (modes.State==ProductFlightModeCoordinator.FlightModeState.Selection && !calibration.IsOpen)

            { Navigate(axis,false); if(ad)modes.AuthoredModeMenuView.ActivateFocused(); }

            else if (xd && modes.State==ProductFlightModeCoordinator.FlightModeState.Training && (CurrentPage==Page.None||CurrentPage==Page.Explanation||CurrentPage==Page.Orientation))

            { Consume(); training.RepeatCurrent(); }

            if(adjusting) navigationDirection=0;

            if (CurrentPage==Page.Calibration && !calibration.IsOpen) CurrentPage=calibrationReturnPage;

            if (training.State!=presentedState) { presentedState=training.State; focus=0; releaseRequired=true; }

            if (Time.unscaledTime>=refreshAt) { refreshAt=Time.unscaledTime+.1f; Refresh(); }

        }

        private bool TrainingPanelVisible => modes.State==ProductFlightModeCoordinator.FlightModeState.Training &&

            training.State!=TrainingSessionState.Inactive && training.State!=TrainingSessionState.Preparing && !training.IsFlying;

        private void OnApplicationPause(bool paused)

        {

            if(!paused||!HasAuthoredReferences||modes.State==ProductFlightModeCoordinator.FlightModeState.Selection)return;

            if(!IsOpen)OpenMenu();

            pauseNotice="Flight paused while the app was suspended. Choose Resume when ready.";

            input.RequireTouchRelease();releaseRequired=true;

        }

        private void Consume() { consumedFrame=Time.frameCount; releaseRequired=true; }

        public bool TryConsumeStartupSelection()

        {

            if (IsOpen || !FocusAvailable || releaseRequired || ConsumedThisFrame) return false;

            Consume(); input.RequireTouchRelease(); return true;

        }

        public void OpenMenu()

        {

            if (!HasAuthoredReferences) return;
            if(IsOpen)
            {
                if(CurrentPage==Page.Calibration)NavigateTo(Page.SeatExit);
                Consume();Refresh();return;
            }
            pendingMenuAction=0;lessonReturnFocus=focus;

            if (!IsOpen)

            {

                pausedTraining=training.IsFlying;

                if (pausedTraining) training.PauseExercise();

                else if (Time.timeScale>0)

                {

                    savedTimeScale=Time.timeScale; ownsPause=true;

                    pausedControls=ProductControlInput.ToFlightLessonCommand(input.Current);

                    Time.timeScale=0; input.SetFlightLessonInputSuppressed(true);

                }

            }

            routes.Clear(); CurrentPage=modes.State==ProductFlightModeCoordinator.FlightModeState.Selection||modes.State==ProductFlightModeCoordinator.FlightModeState.Blocked?Page.Home:Page.App; focus=0; Consume(); Refresh();

        }

        public void OpenCalibration()

        {

            calibrationReturnPage=CurrentPage;

            if (!IsOpen) OpenMenu(); NavigateTo(Page.Calibration); calibration.Open(); focus=0; Consume(); Refresh();

        }

        public void OpenQuickStart(bool firstUse=false)

        {

            guideReturnPage=firstUse?Page.None:CurrentPage;

            if(!IsOpen)OpenMenu();

            NavigateTo(Page.QuickStart); guideStep=0;ResetSetupCheck();focus=0;Consume();Refresh();

        }

        private void FinishQuickStart(bool completed=true)

        {

            if(completed)firstUsePending=false;
            if(completed)PlayerPrefs.SetInt("QuestFlight.QuickStartSeen",1);PlayerPrefs.Save();

            if(guideReturnPage==Page.None)
            {
                if(modes.State==ProductFlightModeCoordinator.FlightModeState.Selection||modes.State==ProductFlightModeCoordinator.FlightModeState.Blocked)
                {routes.Clear();CurrentPage=Page.Home;Consume();Refresh();return;}
                Resume();return;
            }

            if(!TryPopRoute())CurrentPage=guideReturnPage;Consume();Refresh();

        }

        public void ShowOrientation()

        { NavigateTo(Page.Orientation); training.ShowReference(); focus=0; Consume(); Refresh(); }

        public void Back()

        {

            if(CurrentPage==Page.Handover){CancelHandover();return;}
            if(CurrentPage==Page.ConfirmAction)pendingMenuAction=0;
            if(IsOpen || modes.State!=ProductFlightModeCoordinator.FlightModeState.Selection)ProductUiFeedback.Play(ProductUiFeedback.Cue.Back);

            if (CurrentPage==Page.Calibration) { NavigateTo(Page.SeatExit); Consume();Refresh();return; }

            else if(CurrentPage==Page.QuickStart) { if(guideStep>0){guideStep--;ResetSetupCheck();}else {FinishQuickStart(false);return;} }

            else if(TryPopRoute()) { pendingMenuAction=0;Consume();Refresh();return; }

            else if(CurrentPage==Page.Aircraft||CurrentPage==Page.Soaring||CurrentPage==Page.C172Setup||CurrentPage==Page.GliderSetup||CurrentPage==Page.Help)CurrentPage=modes.State==ProductFlightModeCoordinator.FlightModeState.Selection?Page.Home:Page.App;

            else if(CurrentPage==Page.Home){pauseNotice="Choose an aircraft to begin, or open Help / setup.";Consume();Refresh();return;}

            else if(CurrentPage==Page.ConfirmAction){pendingMenuAction=0;CurrentPage=Page.App;}

            else if (BackFromPolishPage()) { }

            else if(CurrentPage==Page.Settings) CurrentPage=Page.Tools;

            else if(CurrentPage==Page.Guest) CurrentPage=Page.Settings;

            else if (CurrentPage==Page.Orientation) CurrentPage=Page.None;

            else if (CurrentPage==Page.ConfirmMain || CurrentPage==Page.Explanation) CurrentPage=Page.App;

            else if(CurrentPage==Page.App && HasLessonReturn){ReturnLessonView();return;}
            else if (CurrentPage==Page.App) { pauseNotice="Flight stays paused. Choose Resume when ready.";Consume();Refresh();return; }

            else if (modes.State==ProductFlightModeCoordinator.FlightModeState.Training && !training.IsFlying)

            {

                if (training.State==TrainingSessionState.Menu) { ReturnMain(); return; }

                if (training.State==TrainingSessionState.Paused || training.State==TrainingSessionState.Preparing) { OpenMenu(); return; }

                training.OpenLessons();

            }

            else if (modes.State!=ProductFlightModeCoordinator.FlightModeState.Selection) { OpenMenu(); return; }

            focus=0; Consume(); Refresh();

        }

        public void Resume()
        {
            if(!CanResume(out var reason)){Reject(reason);return;}
            BeginHandover(CompleteResume);
        }
        private void CompleteResume()
        {
            CurrentPage=Page.None;routes.Clear();
            if(training.State==TrainingSessionState.Paused)training.ResumeExercise();
            else if(ownsPause){input.PrepareForFlightLessonRecovery(pausedControls);input.SetFlightLessonInputSuppressed(false);Time.timeScale=savedTimeScale;}
            pausedTraining=ownsPause=false;pauseNotice=null;
            Consume();Refresh();
        }

        private void ReturnMain()

        {

            pendingActivity = -1; pendingLessonId=null;pendingMenuAction=0;handoverCompletion=null;

            recoveryAssistance = null;

            if (calibration.IsOpen) calibration.Cancel();

            CurrentPage=Page.None; routes.Clear(); lessonPage=0; pausedTraining=ownsPause=false;pauseNotice=null;

            training.LeaveTraining();

            modes.ReturnToMainMenu();

            CurrentPage=Page.Home; Consume(); Refresh();

        }

        public void FocusAction(int slot) { if (slot>=0&&slot<labels.Length&&!string.IsNullOrEmpty(labels[slot])&&actions[slot]!=0&&slot!=focus) { focus=slot; ProductUiFeedback.Play(ProductUiFeedback.Cue.Focus); if(CurrentPage==Page.Controls)Refresh();else Paint(); } }

        private void Navigate(float axis,bool suppressed)

        {

            if(releaseRequired||suppressed) { navigationDirection=0; return; }

            int direction=axis>.6f?-1:axis<-.6f?1:0;

            if(direction==0) { if(Mathf.Abs(axis)<.25f)navigationDirection=0; return; }

            bool fresh=direction!=navigationDirection;

            if(!fresh&&Time.unscaledTime<nextNavigationTime)return;

            if(IsOpen||TrainingPanelVisible)MoveFocus(direction);

            else modes.AuthoredModeMenuView.MoveFocus(direction);

            navigationDirection=direction;

            nextNavigationTime=Time.unscaledTime+(fresh?.45f:.20f);

        }

        private void MoveFocus(int direction)

        {

            for(int next=focus+direction;next>=0&&next<labels.Length;next+=direction)

                if(!string.IsNullOrEmpty(labels[next])&&actions[next]!=0) { FocusAction(next); break; }

        }

        private void ExecuteAction(int slot)

        {

            if(slot<0||slot>=8)return;

            int action=actions[slot]; Consume();

            if(action!=5)ProductUiFeedback.Play(ProductUiFeedback.Cue.Select);

            if (action>=100 && action<100+training.Lessons.Length) training.SelectLesson(action-100);

            else if(InvokeWorkflowAction(action)){Refresh();return;}
            else if(InvokeHubAction(action)){Refresh();return;}

            else if (InvokePolishAction(action)) { Refresh();return; }

            else switch(action)

            {

                case (int)MenuActionId.Aircraft: NavigateTo(Page.Aircraft);break;

                case (int)MenuActionId.SelectC172: SelectMenuAircraft(false);break;

                case (int)MenuActionId.SelectSgs: SelectMenuAircraft(true);break;

                case (int)MenuActionId.Runway08: StartSoaringOperation(0);return;

                case (int)MenuActionId.Runway26: StartSoaringOperation(1);return;

                case (int)MenuActionId.Runway08StartNow: StartSoaringOperation(0,true);return;

                case (int)MenuActionId.Runway26StartNow: StartSoaringOperation(1,true);return;

                case (int)MenuActionId.Soaring: NavigateTo(Page.Soaring);break;

                case (int)MenuActionId.CrewReconnect: OperationResult(modes.AuthoredFlightCoordinator.TryCrewReconnect(),"Rope reconnected; pair restaged. Do not repeat release. Return to cockpit, close the combined lever and center controls, then clear launch.","Crew preparation failed. Stop and review Launch preparation.");break;

                case (int)MenuActionId.ClearLaunch: BeginHandover(()=>{if(modes.AuthoredFlightCoordinator.TryClearSoaringLaunch())CompleteResume();else {CurrentPage=Page.Soaring;Reject("Clearance changed. Review the readiness items and retry.");}});return;

                case (int)MenuActionId.GroundAbort: OperationResult(modes.AuthoredFlightCoordinator.TryAbortSoaringGroundLaunch(),"Ground launch aborted. Resume and apply the combined lever fully to stop.","Ground abort requires weight on wheels and speed below20kt. The red release remains available in flight.");break;

                case (int)MenuActionId.ContinueManual: BeginHandover(()=>{if(!modes.ContinueSoaringTrainingFlight()){Reject("Flight can no longer continue. Restart or return Home.");CurrentPage=Page.App;return;}NavigateTo(Page.None);pausedTraining=ownsPause=false;Refresh();});return;

                case (int)MenuActionId.Resume: Resume(); return;

                case (int)MenuActionId.SeatSetup: OpenCalibration(); return;

                case (int)MenuActionId.ConfirmHome: if (modes.State==ProductFlightModeCoordinator.FlightModeState.Selection) ReturnMain(); else NavigateTo(Page.ConfirmMain); break;

                case (int)MenuActionId.EndToHome: experience?.FinishFlight("Flight ended by pilot"); ReturnMain(); return;

                case (int)MenuActionId.Back: Back(); return;

                case (int)MenuActionId.SaveSeat:

                    calibration.Save();
                    if(calibration.IsOpen){Reject(calibration.Status);return;}

                    if(!calibration.IsOpen) { if(CurrentPage==Page.SeatExit)TryPopRoute(); if(!TryPopRoute())CurrentPage=calibrationReturnPage; if(CurrentPage==Page.QuickStart){guideStep=3;ResetSetupCheck();} }

                    break;

                case (int)MenuActionId.ResetSeat: calibration.ResetToDefault(); break;

                case (int)MenuActionId.RecenterSeat: calibration.Recenter(); break;

                case (int)MenuActionId.ExplainStep: NavigateTo(Page.Explanation); training.ExplainCurrent(); break;

                case (int)MenuActionId.Practice: NavigateTo(Page.None); pausedTraining=false; training.StartPractice(); input.RequireTouchRelease(); break;

                case (int)MenuActionId.Demonstration: NavigateTo(Page.None); pausedTraining=false; training.StartDemonstration(); input.RequireTouchRelease(); break;

                case (int)MenuActionId.ResumeLesson: Resume();return;

                case (int)MenuActionId.LessonList: NavigateTo(Page.None); pausedTraining=false; training.OpenLessons(); break;

                case (int)MenuActionId.Captions: training.ToggleCaptions(); break;

                case (int)MenuActionId.Assistance: BeginHandover(()=>{if(training.AssistancePaused){training.TakeManualControlFromPause();CurrentPage=Page.None;routes.Clear();pausedTraining=ownsPause=false;Refresh();}else {CompleteResume();training.ToggleHelp();}});return;

                case (int)MenuActionId.Orientation: ShowOrientation(); return;

                case (int)MenuActionId.NextReference: training.ShowReference(1); break;

                case (int)MenuActionId.PreviousReference: training.ShowReference(-1); break;

                case (int)MenuActionId.WithoutPrompts: NavigateTo(Page.None); pausedTraining=false; training.StartWithoutPrompts(); input.RequireTouchRelease(); break;

                case (int)MenuActionId.TakeControl:

                    BeginHandover(()=>{modes.AuthoredDemoPilot.TakeUserControl(false);CompleteResume();});return;

                case (int)MenuActionId.DemoControl:

                    if(modes.AuthoredDemoPilot.ResumeDemoControl(false)) Resume();

                    else Reject(modes.AuthoredDemoPilot.Phase);

                    return;

                case (int)MenuActionId.DemoReturn:

                    if(modes.AuthoredDemoPilot.ResumeDemoControl(true)) Resume();

                    else Reject(modes.AuthoredDemoPilot.Phase);

                    return;

                case (int)MenuActionId.VoiceVolume: voiceLevel=voiceLevel<=0?1:Mathf.Max(0,voiceLevel-.25f);ApplyAudioLevels();break;

                case (int)MenuActionId.AircraftVolume: aircraftLevel=aircraftLevel<=0?1:Mathf.Max(0,aircraftLevel-.25f);ApplyAudioLevels();break;

                case (int)MenuActionId.Settings: NavigateTo(Page.Settings);focus=0;break;

                case (int)MenuActionId.LargeText:

                    PlayerPrefs.SetInt("QuestFlight.LargeInstructionText",LargeInstructionTextEnabled?0:1);PlayerPrefs.Save();ApplyInstructionSize();break;

                case (int)MenuActionId.Feedback:

                    PlayerPrefs.SetInt("QuestFlight.InterfaceFeedback",FeedbackEnabled?0:1);PlayerPrefs.Save();break;

                case (int)MenuActionId.Guest: NavigateTo(Page.Guest);focus=0;break;

                case (int)MenuActionId.ConfirmGuest:

                    if(modes.State==ProductFlightModeCoordinator.FlightModeState.Selection && training.TrySetGuestSession(!training.IsGuestSession))

                    { if(!TryPopRoute())CurrentPage=Page.Settings;focus=5; }
                    else Reject("Return Home before changing the guest session.");
                    break;

                case (int)MenuActionId.QuickStart: OpenQuickStart();return;

                case (int)MenuActionId.NextSetup: if(guideStep<8){guideStep++;ResetSetupCheck();}else {FinishQuickStart(false);return;} break;

                case (int)MenuActionId.FinishSetup: FinishQuickStart();return;

                case (int)MenuActionId.RunwayRestart: RestartRecoveryFlight(); return;

            }

            Refresh();

        }

        private void Choice(int slot,string name,int action)

        {

            if(slot<0||slot>=8)throw new System.ArgumentOutOfRangeException(nameof(slot));

            if(labels[slot]!=null)throw new System.InvalidOperationException("Duplicate menu slot: "+CurrentPage+"/"+slot);

            labels[slot]=name;actions[slot]=action;

        }

        private void ApplyAudioLevels()

        {

            if(instructorVoice!=null)instructorVoice.volume=voiceLevel;

            if(aircraftAudio!=null)aircraftAudio.AircraftVolume=aircraftLevel;

            PlayerPrefs.SetFloat("QuestFlight.VoiceVolume",voiceLevel);PlayerPrefs.SetFloat("QuestFlight.AircraftVolume",aircraftLevel);PlayerPrefs.Save();

        }

        private void ApplyInstructionSize()

        {

            if(instructionScales==null)return;

            for(int n=0;n<instructionPanels.Length;n++)

                if(instructionPanels[n]!=null)instructionPanels[n].transform.localScale=instructionScales[n];

        }

        private void Refresh()

        {

            bool show=IsOpen||TrainingPanelVisible;
            SetMenuInteractionScope(show);

            experience?.ShowMap(show&&CurrentPage==Page.Map);

            if(polishedLayout!=null)polishedLayout.SetSupplementHeight(CurrentPage==Page.Map?.38f:CurrentPage==Page.ControlDiagram||CurrentPage==Page.InputPreview?.29f:0);

            if(CurrentPage!=Page.InstrumentHelp)training.HideCockpitReference();

            if (panel.activeSelf!=show) panel.SetActive(show);

            modes.SetNavigationOverlayVisible(IsOpen);

            bool selectionScreen=modes.State==ProductFlightModeCoordinator.FlightModeState.Selection;

            bool cockpitPreview=CurrentPage==Page.InstrumentHelp||CurrentPage==Page.Calibration;

            var eye=calibration.AuthoredReferenceFrameRig.TrackedCamera.transform;

            if(selectionScreen)modes.AuthoredModeMenuView.SetSelectionPresentation(cockpitPreview,eye);


            if(polishedLayout!=null)polishedLayout.SetPresentationContext(cockpitPreview||CurrentPage==Page.Orientation||CurrentPage==Page.Explanation,eye);

            if(controlVisuals!=null)controlVisuals.SetAircraft(modes.IsGlider);

            if(controlVisuals!=null)controlVisuals.Present(show&&(CurrentPage==Page.ControlDiagram||CurrentPage==Page.InputPreview),CurrentPage==Page.ControlDiagram);

            if (!show) return;

            bool compact=CurrentPage==Page.Orientation||CurrentPage==Page.Explanation;

            if(polishedLayout==null)

            {

            panel.transform.localPosition=compact?new Vector3(0,.12f,-.06f):new Vector3(0,0,-.06f);

            backplate.localPosition=new Vector3(0,compact?.11f:-.04f,1.08f);

            backplate.localScale=new Vector3(.98f,compact?.70f:1f,.008f);

            footer.transform.localPosition=new Vector3(-.44f,compact?-.215f:-.47f,1.05f);

            }

            for(int n=0;n<8;n++) { labels[n]=null; actions[n]=0; }

            footer.text="Point + trigger / A: select   B / Y: back\nStick up / down: choose   Menu: pause menu";

            if(PresentWorkflowPage()) { }
            else if(PresentHubPage()) { }

            else if (PresentRecoveryAssistance()) { }

            else if(CurrentPage==Page.Soaring)PresentSoaringOperation();

            else if(CurrentPage==Page.Aircraft)

            {

                heading.text="Choose your aircraft";

                detail.text="Choose C172 or the SGS 2-33A reference trainer. Soaring setup offers a ground aerotow; Free Flight alone uses an explicit airborne practice start.";

                Choice(0,"C172",301);Choice(1,"SGS 2-33A reference",302);if(modes.IsGlider&&!choosingLessonAircraft)Choice(2,"Soaring setup / ground launch",305);Choice(7,"Back",5);

            }

            else if (PresentPolishPage()) { }

            else if(CurrentPage==Page.App)

            {

                heading.text="Flight paused - "+AircraftName;

                detail.text=(experience!=null?experience.OwnerLabel+"\n":"")+(!string.IsNullOrEmpty(pauseNotice)?pauseNotice:training.CurrentPauseReason);

                Choice(0,HasLessonReturn?LessonReturnLabel:training.DemonstrationPaused?"Resume demonstration":"Resume flight",HasLessonReturn?323:1);

                Choice(1,"Settings",25);Choice(2,"Seat position",2);

                if(modes.AuthoredDemoPilot.IsSoaringDemo)Choice(3,"Tour options / restart",337);else if(modes.IsGlider)Choice(3,SoaringPageTitle,305);else Choice(3,"Help and flight tools",40);

                if(modes.State==ProductFlightModeCoordinator.FlightModeState.Training)

                {Choice(4,"Lesson help / current step",9);if(training.State!=TrainingSessionState.Menu)Choice(5,training.State==TrainingSessionState.Preparing?"Cancel preparation to briefing":"Restart this exercise",training.State==TrainingSessionState.Preparing?324:10);if(training.DemonstrationPaused)Choice(6,"Take manual control · assisted attempt",335);else if(training.CanUseAssistance)Choice(6,training.AssistancePaused?"Take control":modes.IsGlider?"Assisted glide":"Instructor hold aircraft",15);}

                else if(modes.State==ProductFlightModeCoordinator.FlightModeState.Demo)

                {Choice(4,modes.AuthoredDemoPilot.UserHasControls?"Let demo fly":"Take control",modes.AuthoredDemoPilot.UserHasControls?21:20);Choice(5,"Return demo to airport",22);Choice(6,"End flight / summary",49);}

                else Choice(6,"End flight / summary",49);

                Choice(7,"End session / Home",3);

            }

            else if(CurrentPage==Page.Settings)

            {

                heading.text="Settings";

                detail.text=training.IsGuestSession?"Guest session active. These audio and display preferences apply in every mode.":"Audio and display preferences apply in every mode.";

                if(instructorVoice!=null)Choice(0,"Voice: "+Mathf.RoundToInt(voiceLevel*100)+"%",23);

                if(aircraftAudio!=null)Choice(1,"Aircraft sound: "+Mathf.RoundToInt(aircraftLevel*100)+"%",24);

                Choice(2,CaptionsEnabled?"Narration captions: on":"Narration captions: off",14);

                Choice(3,"Display / readability",195);

                Choice(4,"Controls and comfort",41);

                Choice(5,training.IsGuestSession?"End guest session":"Start guest session",28);

                Choice(6,"Help / map / setup",315); Choice(7,"Back",5);

                footer.text="Stick up / down: choose · left / right: adjust volume\n[A] Increase / select   [B / Y] Back   [Menu] Pause / help";

            }

            else if(CurrentPage==Page.Guest)

            {

                heading.text=training.IsGuestSession?"End guest session?":"Welcome, guest";

                bool canChange=modes.State==ProductFlightModeCoordinator.FlightModeState.Selection;

                detail.text=canChange?(training.IsGuestSession?"Guest lesson results will be cleared. The owner's saved progress will be restored unchanged.":"Try any flight or lesson. Guest lesson results are kept only for this app session and never change the owner's saved progress."):

                    "Finish this flight and return to the main menu before changing the guest session. Your current flight is still paused.";

                if(canChange)Choice(0,training.IsGuestSession?"End guest session":"Start guest session",29);

                else Choice(0,"Return to main menu",3);

                Choice(1,"Back to settings",5);

            }

            else if(CurrentPage==Page.QuickStart)

            {

                PresentSetup();

            }

            else if(CurrentPage==Page.ConfirmMain)

            { heading.text="End this session?"; detail.text=training.IsGuestSession?"This flight ends. Guest lesson results stay available until the guest session ends or the app closes.":"Return to the main menu. This flight ends; completed lesson progress is kept."; Choice(0,"Cancel - keep this session",5); Choice(1,"End session and return to main menu",4);

                if(localRecovery!=null && (modes.State==ProductFlightModeCoordinator.FlightModeState.FreeFlight||modes.State==ProductFlightModeCoordinator.FlightModeState.Demo)) Choice(2,modes.IsGlider?"Restart ground launch preparation":"Restart at runway (manual flight)",modes.IsGlider?325:190); }

            else if(CurrentPage==Page.Calibration)

            {

                heading.text="Seat position";

                detail.text=$"Hold grip to adjust: left stick across / height; right stick forward / back\nRelease grip to navigate. X: {(calibration.UsesSmallIncrement ? "fine" : "large")} adjustments ({calibration.CurrentStepMeters*100:0.#} cm)\nSeat offset: {calibration.CalibrationOffset.x*100:+0;-0;0} cm across, {calibration.CalibrationOffset.y*100:+0;-0;0} cm height, {calibration.CalibrationOffset.z*100:+0;-0;0} cm forward\n{calibration.Status}";

                Choice(0,"Save seat position",6); Choice(1,"Discard changes",332); Choice(2,"Recenter tracking",8); Choice(3,"Reset to aircraft default",7);

            }

            else if(CurrentPage==Page.Explanation)

            { heading.text="Current step"; detail.text=training.CurrentStepHelp; Choice(0,"Back",5); Choice(1,"Cockpit references",125); }

            else if(CurrentPage==Page.Orientation)

            { heading.text="Cockpit references"; detail.text=training.CurrentExplanation; Choice(0,"Next reference",17); Choice(1,"Previous reference",18); Choice(2,"Ready - start practice",10); Choice(3,"Back to briefing",5); }

            else

            {

                heading.text=training.CurrentLessonName; detail.text="";

                switch(training.State)

                {

                    case TrainingSessionState.Menu:

                        heading.text="Flight lessons"; detail.text=training.IsGuestSession?"Guest session · results do not change saved owner progress.":"Choose a lesson. Menu is always available; B / Y returns.";

                        for(int n=lessonPage*5;n<System.Math.Min(training.Lessons.Length,lessonPage*5+5);n++) Choice(n-lessonPage*5,(n+1).ToString("00")+"   "+training.Lessons[n].displayName+(training.IsLessonComplete(n)?"   <color=#9FD2B5>Complete</color>":""),100+n);

                        if(lessonPage>0)Choice(5,"Previous lessons",320);if((lessonPage+1)*5<training.Lessons.Length)Choice(6,"More lessons",319);Choice(7,"Home",4); break;

                    case TrainingSessionState.Briefing:

                        detail.text=training.CurrentBriefing; Choice(0,"Find the cockpit references",16);

                        Choice(1,"Start guided practice",10);

                        if(training.DemonstrationAvailable) Choice(2,"Watch demonstration",11);

                        Choice(3,"Practice without routine coaching",19); Choice(4,"Back to lessons",13); break;

                    case TrainingSessionState.Paused:

                        detail.text=(string.IsNullOrEmpty(training.CurrentPauseReason)?"Exercise paused.":training.CurrentPauseReason)+"\n\n"+(training.WithoutPrompts?training.CurrentStage.title:training.CurrentInstruction);

                        Choice(0,training.DemonstrationPaused?"Resume demonstration":"Resume flight",12); Choice(1,"Explain / show current step",9); Choice(2,"Restart exercise",10); Choice(3,"Lesson list",13);if(training.AssistancePaused||training.DemonstrationPaused)Choice(4,"Take manual control",335); break;

                    case TrainingSessionState.Debrief:

                        detail.text=training.LastDebrief; Choice(0,"Retry exercise",10); Choice(1,"Lesson list",13); Choice(2,"Main menu",4);if(training.IsSoaringProgram)Choice(3,"Continue this flight manually",309); break;

                    case TrainingSessionState.Blocked:

                        detail.text=training.BlockedReason; Choice(0,"Main menu",4); break;

                }

            }

            if(CurrentPage!=Page.Home)footer.text+="\n"+AircraftName+" / "+heading.text;
            if(focus>=0&&focus<8&&actions[focus]!=0&&!CanExecute(actions[focus],out var disabledReason)&&!string.IsNullOrEmpty(disabledReason))detail.text+="\n"+disabledReason;
            if(!string.IsNullOrEmpty(pauseNotice)&&!detail.text.Contains(pauseNotice))detail.text+="\n\n"+pauseNotice;

            Paint();

            if(polishedLayout!=null)polishedLayout.Apply(compact);

        }

        private void Paint()

        {

            if(focus<0||focus>=labels.Length||string.IsNullOrEmpty(labels[focus])||actions[focus]==0)

                for(int n=0;n<labels.Length;n++)if(!string.IsNullOrEmpty(labels[n])&&actions[n]!=0){focus=n;break;}

            for(int n=0;n<buttons.Length;n++)buttons[n].Present(labels[n],n==focus&&actions[n]!=0,actions[n]!=0&&CanExecute(actions[n],out _));

        }

    }

}
