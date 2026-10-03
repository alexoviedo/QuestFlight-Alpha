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

        public enum Page { None, App, ConfirmMain, Calibration, Explanation, Orientation, Settings, QuickStart, Guest, Tools, Controls, Comfort, Briefing, Map, Kneeboard, Activities, History, Summary, Report, Bindings, InputPreview, InstrumentHelp, Captions, ControlDiagram, Checklist }

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

        }

        private bool FocusAvailable

        {

            get

            {

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR

                if(!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_TRAINING_VIEW"))) return true;

#endif

                return Application.isFocused && (Application.platform!=RuntimePlatform.Android ||

                    (OVRManager.hasInputFocus && (OVRManager.instance==null||OVRManager.instance.isUserPresent)));

            }

        }



        private void Update()

        {

            bool menu=OVRInput.Get(OVRInput.RawButton.Start);

            bool back=OVRInput.Get(OVRInput.RawButton.B)||OVRInput.Get(OVRInput.RawButton.Y);

            bool select=OVRInput.Get(OVRInput.RawButton.A);

            bool repeat=OVRInput.Get(OVRInput.RawButton.X);

            float leftAxis=OVRInput.Get(OVRInput.RawAxis2D.LThumbstick).y;
            float rightAxis=OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).y;
            float axis=Mathf.Abs(leftAxis)>=Mathf.Abs(rightAxis)?leftAxis:rightAxis;
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

            bool triggers=OVRInput.Get(OVRInput.RawAxis1D.LIndexTrigger)>.1f||OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger)>.1f;

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

                if (!menu&&!back&&!select&&!repeat&&!triggers&&Mathf.Abs(axis)<.2f) releaseRequired=false;

                md=bd=ad=xd=false;
                navigationDirection=0;

            }

            if(firstUsePending && !releaseRequired && !IsOpen && modes.State==ProductFlightModeCoordinator.FlightModeState.Selection && modes.AuthoredFlightCoordinator.IsInitialized && !modes.AuthoredModeMenuView.TransitionPending)
            {
                firstUsePending=false; OpenQuickStart(true); return;
            }

            UpdateSetupChecks();
            if (md) { Consume(); if (CurrentPage==Page.App) Resume(); else OpenMenu(); }

            else if (bd) { Consume(); Back(); }

            else if (IsOpen || TrainingPanelVisible)

            {

                Navigate(axis,adjusting);
                AdjustFocusedSetting(adjusting);

                if (ad && !adjusting) InvokeAction(focus);

                if (xd && CurrentPage==Page.Calibration) { Consume(); calibration.ToggleIncrementSize(); }
                else if (xd && modes.State==ProductFlightModeCoordinator.FlightModeState.Training)

                {

                    Consume();

                    if(CurrentPage==Page.Orientation) training.ShowReference();

                    else if(CurrentPage==Page.Explanation) training.ExplainCurrent();

                    else training.RepeatCurrent();

                }

            }

            else if (modes.State==ProductFlightModeCoordinator.FlightModeState.Selection && !calibration.IsOpen)
            { Navigate(axis,false); if(ad)modes.AuthoredModeMenuView.ActivateFocused(); }

            else if (xd && modes.State==ProductFlightModeCoordinator.FlightModeState.Training)

            { Consume(); training.RepeatCurrent(); }

            if(adjusting) { releaseRequired=true; navigationDirection=0; }

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

            if (calibration.IsOpen) calibration.Cancel();

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

            CurrentPage=Page.App; focus=0; Consume(); Refresh();

        }

        public void OpenCalibration()

        {
            calibrationReturnPage=CurrentPage==Page.QuickStart?Page.QuickStart:Page.App;
            if (!IsOpen) OpenMenu(); CurrentPage=Page.Calibration; calibration.Open(); focus=0; Consume(); Refresh();
        }

        public void OpenQuickStart(bool firstUse=false)
        {
            guideReturnPage=firstUse?Page.None:CurrentPage;
            if(!IsOpen)OpenMenu();
            CurrentPage=Page.QuickStart; guideStep=0;ResetSetupCheck();focus=0;Consume();Refresh();
        }

        private void FinishQuickStart()
        {
            PlayerPrefs.SetInt("QuestFlight.QuickStartSeen",1);PlayerPrefs.Save();
            if(guideReturnPage==Page.None){Resume();return;}
            CurrentPage=guideReturnPage;focus=0;Consume();Refresh();
        }

        public void ShowOrientation()

        { CurrentPage=Page.Orientation; training.ShowReference(); focus=0; Consume(); Refresh(); }

        public void Back()

        {
            if(IsOpen || modes.State!=ProductFlightModeCoordinator.FlightModeState.Selection)ProductUiFeedback.Play(ProductUiFeedback.Cue.Back);

            if (CurrentPage==Page.Calibration) { calibration.Cancel(); CurrentPage=calibrationReturnPage; }

            else if(CurrentPage==Page.QuickStart) { if(guideStep>0){guideStep--;ResetSetupCheck();}else {FinishQuickStart();return;} }

            else if (BackFromPolishPage()) { }
            else if(CurrentPage==Page.Settings) CurrentPage=Page.Tools;

            else if(CurrentPage==Page.Guest) CurrentPage=Page.Settings;

            else if (CurrentPage==Page.Orientation) CurrentPage=Page.None;

            else if (CurrentPage==Page.ConfirmMain || CurrentPage==Page.Explanation) CurrentPage=Page.App;

            else if (CurrentPage==Page.App) { Resume(); return; }

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

            if (modes.State == ProductFlightModeCoordinator.FlightModeState.Blocked)
            { ReturnMain(); return; }
            if (recoveryAssistance != null) { Refresh(); return; }
            if (calibration.IsOpen || !FocusAvailable) return;
            if(Application.platform==RuntimePlatform.Android&&controllerMissingSince>=0&&!input.AuthoredGamepad.Current.connected)
            {pauseNotice="Wake both controllers before resuming. You can still return to the main menu.";Refresh();return;}

            CurrentPage=Page.None;

            if (pausedTraining && training.State==TrainingSessionState.Paused) training.ResumeExercise();

            else if (ownsPause)

            {

                input.PrepareForFlightLessonRecovery(pausedControls);

                input.SetFlightLessonInputSuppressed(false);

                Time.timeScale=savedTimeScale;

            }

            pausedTraining=ownsPause=false;pauseNotice=null;

            input.RequireTouchRelease();

            Consume(); Refresh();

        }

        private void ReturnMain()

        {
            pendingActivity = -1;
            recoveryAssistance = null;

            if (calibration.IsOpen) calibration.Cancel();

            CurrentPage=Page.None; pausedTraining=ownsPause=false;pauseNotice=null;

            training.LeaveTraining();

            modes.ReturnToMainMenu();

            Consume(); Refresh();

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

        public void InvokeAction(int slot)

        {

            if (!FocusAvailable || releaseRequired || ConsumedThisFrame || slot<0 || slot>=8 || actions[slot]==0 || string.IsNullOrEmpty(labels[slot])) return;

            int action=actions[slot]; Consume();
            if(action!=5)ProductUiFeedback.Play(ProductUiFeedback.Cue.Select);

            if (action>=100 && action<106) training.SelectLesson(action-100);

            else if (InvokePolishAction(action)) { Refresh();return; }
            else switch(action)

            {

                case 1: Resume(); return;

                case 2: OpenCalibration(); return;

                case 3: if (modes.State==ProductFlightModeCoordinator.FlightModeState.Selection) ReturnMain(); else CurrentPage=Page.ConfirmMain; break;

                case 4:
                    if(CurrentPage!=Page.Summary && (modes.State==ProductFlightModeCoordinator.FlightModeState.FreeFlight||modes.State==ProductFlightModeCoordinator.FlightModeState.Demo))
                    {experience?.FinishFlight("Flight ended by pilot");ShowFlightSummary();return;}
                    ReturnMain(); return;

                case 5: Back(); return;

                case 6:
                    calibration.Save();
                    if(!calibration.IsOpen) { CurrentPage=calibrationReturnPage; if(CurrentPage==Page.QuickStart){guideStep=3;ResetSetupCheck();} }
                    break;

                case 7: calibration.ResetToDefault(); break;

                case 8: calibration.Recenter(); break;

                case 9: CurrentPage=Page.Explanation; training.ExplainCurrent(); break;

                case 10: CurrentPage=Page.None; pausedTraining=false; training.StartPractice(); input.RequireTouchRelease(); break;

                case 11: CurrentPage=Page.None; pausedTraining=false; training.StartDemonstration(); input.RequireTouchRelease(); break;

                case 12: CurrentPage=Page.None; pausedTraining=false; if(training.DemonstrationPaused) training.StartPractice(); else training.ResumeExercise(); input.RequireTouchRelease(); break;

                case 13: CurrentPage=Page.None; pausedTraining=false; training.OpenLessons(); break;

                case 14: training.ToggleCaptions(); break;

                case 15: CurrentPage=Page.None; pausedTraining=false; training.ResumeExercise(); training.ToggleHelp(); input.RequireTouchRelease(); break;

                case 16: ShowOrientation(); return;

                case 17: training.ShowReference(1); break;

                case 18: training.ShowReference(-1); break;

                case 19: CurrentPage=Page.None; pausedTraining=false; training.StartWithoutPrompts(); input.RequireTouchRelease(); break;
                case 20:
                    modes.AuthoredDemoPilot.TakeUserControl(); Resume(); return;
                case 21:
                    if(modes.AuthoredDemoPilot.ResumeDemoControl(false)) Resume();
                    else Refresh();
                    return;
                case 22:
                    if(modes.AuthoredDemoPilot.ResumeDemoControl(true)) Resume();
                    else Refresh();
                    return;
                case 23: voiceLevel=voiceLevel<=0?1:Mathf.Max(0,voiceLevel-.25f);ApplyAudioLevels();break;
                case 24: aircraftLevel=aircraftLevel<=0?1:Mathf.Max(0,aircraftLevel-.25f);ApplyAudioLevels();break;
                case 25: CurrentPage=Page.Settings;focus=0;break;
                case 26:
                    PlayerPrefs.SetInt("QuestFlight.LargeInstructionText",LargeInstructionTextEnabled?0:1);PlayerPrefs.Save();ApplyInstructionSize();break;
                case 27:
                    PlayerPrefs.SetInt("QuestFlight.InterfaceFeedback",FeedbackEnabled?0:1);PlayerPrefs.Save();break;
                case 28: CurrentPage=Page.Guest;focus=0;break;
                case 29:
                    if(modes.State==ProductFlightModeCoordinator.FlightModeState.Selection && training.TrySetGuestSession(!training.IsGuestSession))
                    { CurrentPage=Page.Settings;focus=5; }
                    break;
                case 30: OpenQuickStart();return;
                case 31: if(guideStep<8){guideStep++;ResetSetupCheck();}else {FinishQuickStart();return;} break;
                case 32: FinishQuickStart();return;
                case 190: RestartRecoveryFlight(); return;

            }

            if(CurrentPage!=Page.Settings)focus=0; Refresh();

        }

        private void Choice(int slot,string name,int action) { labels[slot]=name; actions[slot]=action; }
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
            if(CurrentPage!=Page.InstrumentHelp)training.HideCockpitReference();

            if (panel.activeSelf!=show) panel.SetActive(show);

            modes.SetNavigationOverlayVisible(IsOpen);
            bool selectionScreen=modes.State==ProductFlightModeCoordinator.FlightModeState.Selection;
            bool cockpitPreview=CurrentPage==Page.InstrumentHelp||CurrentPage==Page.Calibration;
            var eye=calibration.AuthoredReferenceFrameRig.TrackedCamera.transform;
            if(selectionScreen)modes.AuthoredModeMenuView.SetSelectionPresentation(cockpitPreview,eye);
            if(polishedLayout!=null)polishedLayout.SetPresentationContext(!selectionScreen||cockpitPreview,eye);

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

            footer.text="<b>[A]</b> Select   <b>[B / Y]</b> Back   <b>[Menu]</b> Pause / help\nThumbstick up / down moves selection";

            if (PresentRecoveryAssistance()) { }
            else if (PresentPolishPage()) { }
            else if(CurrentPage==Page.App)

            {

                bool selection=modes.State==ProductFlightModeCoordinator.FlightModeState.Selection;
                heading.text=selection?"Options and help":"Flight paused";
                detail.text=training.IsGuestSession?"Guest session · lesson results stay separate from the owner's progress.":selection?"Prepare your seat, choose your preferences, then select a flight.":"Take a moment. Resume when you are ready.";

                Choice(0,selection?"Choose a flight":"Resume flight",1); Choice(1,"Seat calibration",2);
                if(!selection)Choice(2,"Restart / main menu",3);
                Choice(7,"Flight tools / settings",40);
                Choice(5,"Controls / sensitivity",41);
                Choice(6,"Cockpit guide / instruments",125);
                if(!string.IsNullOrEmpty(pauseNotice))detail.text=pauseNotice;
                if(modes.State==ProductFlightModeCoordinator.FlightModeState.Demo)
                {
                    var demo=modes.AuthoredDemoPilot;
                    detail.text=demo.OwnershipLabel+"\n"+(!string.IsNullOrEmpty(pauseNotice)?pauseNotice:demo.PresentationPhase);
                    Choice(3,demo.UserHasControls?"Let the demo fly":"Take control",demo.UserHasControls?21:20);
                    Choice(4,"Return to airport",22);
                }

                if(modes.State==ProductFlightModeCoordinator.FlightModeState.Training && training.State!=TrainingSessionState.Menu)

                {

                    Choice(3,"Explain / show current step",9); Choice(4,"Restart this exercise",10);

                    Choice(5,training.Captions?"Captions: on":"Captions: off",14);

                    if(training.State==TrainingSessionState.Paused && training.CanUseAssistance)

                        Choice(6,training.AssistancePaused?"Take control":"Let instructor hold aircraft",15);

                }

            }

            else if(CurrentPage==Page.Settings)
            {
                heading.text="Settings";
                detail.text=training.IsGuestSession?"Guest session active. These audio and display preferences apply in every mode.":"Audio and display preferences apply in every mode.";
                if(instructorVoice!=null)Choice(0,"Voice: "+Mathf.RoundToInt(voiceLevel*100)+"%",23);
                if(aircraftAudio!=null)Choice(1,"Aircraft sound: "+Mathf.RoundToInt(aircraftLevel*100)+"%",24);
                Choice(2,CaptionsEnabled?"Narration captions: on":"Narration captions: off",14);
                Choice(3,"Readability / feedback",42);
                Choice(4,"Controls and comfort",41);
                Choice(5,training.IsGuestSession?"End guest session":"Start guest session",28);
                Choice(6,"Quick start guide",30); Choice(7,"Back",5);
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
                if(localRecovery!=null && (modes.State==ProductFlightModeCoordinator.FlightModeState.FreeFlight||modes.State==ProductFlightModeCoordinator.FlightModeState.Demo)) Choice(2,"Restart at runway (manual flight)",190); }

            else if(CurrentPage==Page.Calibration)

            {

                heading.text="Seat position";

                detail.text=$"Hold grip to adjust: left stick across / height; right stick forward / back\nRelease grip to navigate. X: {(calibration.UsesSmallIncrement ? "fine" : "large")} adjustments ({calibration.CurrentStepMeters*100:0.#} cm)\nSeat offset: {calibration.CalibrationOffset.x*100:+0;-0;0} cm across, {calibration.CalibrationOffset.y*100:+0;-0;0} cm height, {calibration.CalibrationOffset.z*100:+0;-0;0} cm forward\n{calibration.Status}";

                Choice(0,"Save seat position",6); Choice(1,"Cancel changes",5); Choice(2,"Recenter tracking",8); Choice(3,"Reset to aircraft default",7);

            }

            else if(CurrentPage==Page.Explanation)

            { heading.text="Current step"; detail.text=training.CurrentExplanation; Choice(0,"Back to paused menu",5); Choice(1,"Repeat / show reference",9); }

            else if(CurrentPage==Page.Orientation)

            { heading.text="Cockpit references"; detail.text=training.CurrentExplanation; Choice(0,"Next reference",17); Choice(1,"Previous reference",18); Choice(2,"Ready - start practice",10); Choice(3,"Back to briefing",5); }

            else

            {

                heading.text=training.CurrentLessonName; detail.text="";

                switch(training.State)

                {

                    case TrainingSessionState.Menu:

                        heading.text="Flight lessons"; detail.text=training.IsGuestSession?"Guest session · results do not change saved owner progress.":"Choose a lesson. Menu is always available; B / Y returns.";

                        for(int n=0;n<training.Lessons.Length;n++) Choice(n,(n+1).ToString("00")+"   "+training.Lessons[n].displayName+(training.IsLessonComplete(n)?"   <color=#9FD2B5>Complete</color>":""),100+n);

                        Choice(6,"Main menu",4); break;

                    case TrainingSessionState.Briefing:

                        detail.text=training.CurrentBriefing; Choice(0,"Find the cockpit references",16);

                        Choice(1,"Start guided practice",10);

                        if(training.DemonstrationAvailable) Choice(2,"Watch demonstration",11);

                        Choice(3,"Try without prompts",19); Choice(4,"Back to lessons",13); break;

                    case TrainingSessionState.Paused:

                        detail.text=(string.IsNullOrEmpty(training.CurrentPauseReason)?"Exercise paused.":training.CurrentPauseReason)+"\n\n"+training.CurrentInstruction;

                        Choice(0,training.DemonstrationPaused?"Take control - start practice":"Resume flight",12); Choice(1,"Explain / show current step",9); Choice(2,"Restart exercise",10); Choice(3,"Lesson list",13); break;

                    case TrainingSessionState.Debrief:

                        detail.text=training.LastDebrief; Choice(0,"Retry exercise",10); Choice(1,"Lesson list",13); Choice(2,"Main menu",4); break;

                    case TrainingSessionState.Blocked:

                        detail.text="Lesson stopped. Use the app menu to return."; Choice(0,"Main menu",4); break;

                }

            }

            Paint();

            if(polishedLayout!=null)polishedLayout.Apply(compact);

        }

        private void Paint()
        {
            if(focus<0||focus>=labels.Length||string.IsNullOrEmpty(labels[focus])||actions[focus]==0)
                for(int n=0;n<labels.Length;n++)if(!string.IsNullOrEmpty(labels[n])&&actions[n]!=0){focus=n;break;}
            for(int n=0;n<buttons.Length;n++)buttons[n].Present(labels[n],n==focus&&actions[n]!=0,actions[n]!=0);
        }

    }

}
