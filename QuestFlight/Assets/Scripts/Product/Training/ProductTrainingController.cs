using System;

using System.IO;

using System.Text;

using QuestFlightLab.Flight.Backends;

using QuestFlightLab.Input;

using QuestFlightLab.Runtime;

using QuestFlightLab.Training.FlightLessonV1;

using UnityEngine;

namespace QuestFlight.Product.Training

{

    public enum TrainingSessionState { Inactive, Menu, Briefing, Preparing, Practice, Demonstration, Assisting, Paused, Debrief, Blocked }

    /// <summary>Authored lesson orchestration; all movement remains in the sole native flight authority.</summary>

    [DefaultExecutionOrder(-90), DisallowMultipleComponent]

    public sealed partial class ProductTrainingController : MonoBehaviour

    {

        [SerializeField] private ProductFlightModeCoordinator modes;

        [SerializeField] private FlightDynamicsCoordinator flight;

        [SerializeField] private ProductControlInput input;

        [SerializeField] private ProductionSeatCalibrationController calibration;

        [SerializeField] private GameObject panelRoot;

        [SerializeField] private TextMesh menuText;

        [SerializeField] private GameObject cueRoot;

        [SerializeField] private TextMesh cueText;

        [SerializeField] private AudioSource voice;

        [SerializeField] private AudioClip demonstrationVoice, assistanceVoice, handbackVoice, goAroundVoice;

        [SerializeField] private AudioClip demonstrationTurnVoice;

        public void ConfigureDemonstrationTurnForAuthoring(AudioClip value) => demonstrationTurnVoice=value;

        public bool DemonstrationPaused => State==TrainingSessionState.Paused && resumedState==TrainingSessionState.Demonstration;

        [SerializeField] private TrainingCoach coach;

        public void ConfigureCoachForAuthoring(TrainingCoach value) => coach=value;

        public TrainingStage CurrentStage => Stage;

        private NarrationPlayback speech;

        private NarrationPlayback Speech => speech ?? (speech=new NarrationPlayback(voice));

        public bool VoicePlaying => voice != null && Speech.Busy;

        public bool InstructionPanelVisible => cueRoot!=null && cueRoot.activeInHierarchy;

        private string spokenInstruction,spokenTitle;

        private float spokenHold;

        public bool WithoutPrompts { get; private set; }

        private string liveCue;

        private AudioClip liveVoice;

        private float liveCueUntil;

        private bool demoTurnAnnounced;

        private int demonstratedSkills;

        private string lastDemonstrated, practiceAdvice;

        public void RecordCoaching(TrainingCue cue)

        {

            if(evaluator.IsControlCheck(Stage.task)||cue==TrainingCue.None||cue==TrainingCue.Recovered||cue==TrainingCue.RolloutLeft||cue==TrainingCue.RolloutRight)return;

            practiceAdvice=Stage.retryAdvice;

        }

        private int orientationIndex;

        public string CurrentExplanation => IsSoaringProgram?ProductAppNavigation.SgsReference(orientationIndex):coach != null ? coach.Explain(coach.CurrentReference) : CurrentInstruction;

        public string ExplainReference(TrainingReference reference) => coach!=null?BindingAwareInstruction(coach.Explain(reference)):"Reference unavailable. Return to the menu and try again.";

        public void ShowCockpitReference(TrainingReference reference) {if(coach!=null)coach.ShowForInspection(reference);}

        public void HideCockpitReference() {if(coach!=null)coach.EndInspection();}

        public void PresentCoachCue(string text,AudioClip clip,bool interrupt)

        {

            if(urgentGoAround) return; // The safety announcement has sole speech priority.

            if(VoicePlaying&&!interrupt) return;

            liveCue=text; liveVoice=clip; liveCueUntil=Time.unscaledTime+Mathf.Max(8,clip!=null?clip.length+3:8); Speak(clip,interrupt,text); RefreshPresentation();

        }

        public void ShowReference(int direction=0)

        {
            RecordHelpRequest();

            if(IsSoaringProgram){orientationIndex=(orientationIndex+direction+ProductAppNavigation.SgsReferenceCount)%ProductAppNavigation.SgsReferenceCount;return;}

            if(coach==null||Lesson.orientation.Length==0) return;

            orientationIndex=Mathf.Clamp(orientationIndex+direction,0,Lesson.orientation.Length-1);

            var reference=Lesson.orientation[orientationIndex]; coach.Show(reference,60);

            Speak(coach.ExplanationVoice(reference),true,coach.Explain(reference));

        }

        public void RecordHelpRequest()
        {
            if(WithoutPrompts && (Flying||State==TrainingSessionState.Paused))
            {WithoutPrompts=false;notice="Help used; this attempt is recorded as guided practice.";}
        }
        public void ExplainCurrent()

        { WithoutPrompts=false; if(IsSoaringProgram){WithoutPrompts=false;RefreshPresentation();return;} if(coach!=null) { coach.Show(Stage.reference,60); Speak(coach.ExplanationVoice(Stage.reference),true,coach.Explain(Stage.reference)); } }

        public void StartWithoutPrompts() { BeginExercise(false); WithoutPrompts=true; }

        [SerializeField] private TrainingLessonDefinition[] lessons = Array.Empty<TrainingLessonDefinition>();

        public TrainingSessionState State { get; private set; }

        public int SelectedLesson { get; private set; } = 1;

        public int StageIndex { get; private set; }

        public string LastDebrief { get; private set; } = "";

        public TrainingLessonDefinition FindLesson(bool glider,string id)=>Array.Find(glider?soaringLessons:lessons,l=>l.lessonId==id);
        public TrainingLessonDefinition[] Lessons => IsSoaringProgram?soaringLessons:lessons;

        public TrainingEvaluator Evaluator => evaluator;

        public bool ReservesRunway => State != TrainingSessionState.Inactive && State != TrainingSessionState.Menu &&

            State != TrainingSessionState.Briefing && State != TrainingSessionState.Debrief && Lesson.airportExercise;

        public bool HasAuthoredReferences => modes != null && flight != null && input != null &&

            calibration != null && panelRoot != null && menuText != null && cueRoot != null &&

            cueText != null && voice != null && demonstrationVoice != null && assistanceVoice != null &&

            handbackVoice != null && goAroundVoice != null && SelectedCatalogValid;

        private readonly TrainingEvaluator evaluator = new TrainingEvaluator();

        private readonly FlightLessonGuidanceController guidance = new FlightLessonGuidanceController();

        private readonly FlightLessonCrashClassifier crash = new FlightLessonCrashClassifier();

        [SerializeField] private ProductUiInstruction polishedInstruction;

        public void ConfigurePolishedInstruction(ProductUiInstruction view) => polishedInstruction=view;

        public bool IsLessonComplete(int index) => index>=0&&index<Lessons.Length&&progress?.Find(Lessons[index].lessonId)?.independentCompletion==true;

        private TrainingProgress progress, ownerProgress;

        public bool IsGuestSession { get; private set; }

        public bool TrySetGuestSession(bool guest)

        {

            // Switching during an attempt could credit the wrong person; only selection permits it.

            if(modes.State!=ProductFlightModeCoordinator.FlightModeState.Selection || State!=TrainingSessionState.Inactive || ownerProgress==null)return false;

            if(guest==IsGuestSession)return true;

            IsGuestSession=guest;

            progress=guest?new TrainingProgress():ownerProgress;

            LastDebrief="";

            return true;

        }

        private string progressPath, pendingFailure, notice = "";

        private float savedTimeScale = 1, sampleAccumulator, refreshAt, preparationTime, guidanceTime;

        private bool ownsPause, demoRequested, resetApplied, resetRequested, alternateFinish;

        private bool captions => ProductAppNavigation.CaptionsEnabled;

        private bool aHeld, bHeld, xHeld, yHeld, stickHeld, inputNeedsRelease;

        private bool pendingOpen;

        private TrainingSessionState resumedState;

        private float assistHeading, assistAltitude, assistSpeed;

        private float approachFlaps;

        private FlightLessonControlCommand appliedGuidance;

        private FlightLessonControlCommand pausedControls;

        private TrainingLessonDefinition Lesson => Lessons[SelectedLesson];

        private TrainingStage Stage => Lesson.stages[StageIndex];

        private bool Flying => State == TrainingSessionState.Practice || State == TrainingSessionState.Demonstration || State == TrainingSessionState.Assisting;

        public bool IsFlying => Flying;

        public bool Captions => captions;

        public bool AssistancePaused => State==TrainingSessionState.Paused && resumedState==TrainingSessionState.Assisting;

        public bool CanUseAssistance => ProgramAllowsAssistance && (AssistancePaused || (resumedState==TrainingSessionState.Practice && !flight.CurrentState.weightOnWheels && flight.CurrentState.altitudeAglMeters>(IsSoaringProgram?150:91.44)));

        public string BlockedReason=>notice;

        public bool CanResumeAssistance => (!DemonstrationPaused || guidanceTime<30) && (!IsSoaringProgram || (ProgramAllowsAssistance && !flight.CurrentState.weightOnWheels && flight.CurrentState.altitudeAglMeters>=120));
        public bool CanContinueFlight => State==TrainingSessionState.Debrief && flight.IsInitialized && flight.enabled &&
            flight.CurrentState.IsFinite && !flight.RecoveryHold && string.IsNullOrEmpty(pendingFailure) && (flight.Operation==null || !flight.Operation.RequiresPause) &&
            (IsSoaringProgram ? !soaringProgram.UnsafeFlight && (!flight.CurrentState.weightOnWheels || flight.Operation!=null) : !evaluator.BadTouchdown);
        public string CurrentStepHelp => Stage.title+"\n"+CurrentInstruction+"\nGoal: "+Stage.retryAdvice+"\nObserved: "+(IsSoaringProgram?soaringProgram.Feedback:evaluator.Feedback)+
            (IsSoaringProgram && flight.Operation!=null?"\n"+flight.Operation.Instruction:"")+"\nMenu pauses all aircraft and traffic. Return to cockpit to operate controls.";
        public string CurrentLessonId => Lesson.lessonId;
        public void CancelPreparation()
        {
            if(State!=TrainingSessionState.Preparing)return;
            resetRequested=demoRequested=false;pendingFailure=null;SetFade(0);PauseClock();
            input.DisableFlightLessonGuidance();Speech.Stop();State=TrainingSessionState.Briefing;RefreshPresentation();
        }
        public void TakeManualControlFromPause()
        {
            if(!AssistancePaused&&!DemonstrationPaused)return;
            MarkProgramAssisted();resumedState=TrainingSessionState.Practice;
            ResumeExercise();notice="YOUR CONTROLS · assistance used in this attempt";
        }
        public string BindingAwareInstruction(string text)
        {
            text=ProductComfortPreferences.MapInstruction(text);
            if(string.IsNullOrEmpty(text))return text;
            return text.Replace("steering thumbstick",ProductComfortPreferences.RudderHand+" thumbstick")
                .Replace("trim thumbstick",ProductComfortPreferences.TrimHand+" thumbstick")
                .Replace("assigned steering stick",ProductComfortPreferences.RudderHand+" steering stick")
                .Replace("assigned trim stick",ProductComfortPreferences.TrimHand+" trim stick")
                .Replace("Squeeze both index triggers",ProductComfortPreferences.CombinedBrakes?"Squeeze either index trigger":"Squeeze both index triggers");
        }
        public string CurrentInstruction => BindingAwareInstruction(Stage.instruction);

        public string CurrentPauseReason => State==TrainingSessionState.Paused?notice:"";

        public string CurrentBriefing => BindingAwareInstruction(Lesson.briefing)+"\n\nGoal: "+Lesson.learningGoal;

        public string CurrentLessonName => Lesson.displayName;

        public bool DemonstrationAvailable => Lesson.allowDemonstration;

        public void ToggleCaptions() { PlayerPrefs.SetInt("QuestFlight.Captions",captions?0:1);PlayerPrefs.Save();RefreshPresentation(); }

        public void RepeatCurrent()

        {

            if(State==TrainingSessionState.Inactive||State==TrainingSessionState.Menu||State==TrainingSessionState.Debrief) return;

            if(IsSoaringProgram){WithoutPrompts=false;RefreshPresentation();return;}

            if(State==TrainingSessionState.Demonstration) { Speak(demoTurnAnnounced?demonstrationTurnVoice:demonstrationVoice); return; }

            if(State==TrainingSessionState.Briefing) { Speak(Lesson.briefingNarration); return; }

            if(urgentGoAround) { Speak(goAroundVoice,true,"Unstable approach — go around. Apply full power and establish a climb."); return; }

            if(WithoutPrompts) WithoutPrompts=false;

            Speak(evaluator.IsControlCheck(Stage.task)?Stage.narration:liveVoice!=null?liveVoice:Stage.narration);

            if(coach!=null) coach.Show(coach.CurrentReference);

        }

        public void LeaveTraining()

        {

            pendingOpen=resetRequested=false;

            input.DisableFlightLessonGuidance(); Speech.Stop();

            if (ownsPause) { Time.timeScale=savedTimeScale; ownsPause=false; }

            State=TrainingSessionState.Inactive; panelRoot.SetActive(false); cueRoot.SetActive(false);

            coach?.ResetObservation();

            input.SetFlightLessonInputSuppressed(true);

        }

        public void ConfigureForAuthoring(ProductFlightModeCoordinator m, FlightDynamicsCoordinator f,

            ProductControlInput i, ProductionSeatCalibrationController c, GameObject panel, TextMesh text,

            GameObject cue, TextMesh caption, AudioSource audio, TrainingLessonDefinition[] catalog)

        { modes = m; flight = f; input = i; calibration = c; panelRoot = panel; menuText = text; cueRoot = cue; cueText = caption; voice = audio; lessons = catalog; }

        public void ConfigureVoiceForAuthoring(AudioClip demonstration, AudioClip assistance, AudioClip handback, AudioClip goAround)

        { demonstrationVoice = demonstration; assistanceVoice = assistance; handbackVoice = handback; goAroundVoice = goAround; }

#if UNITY_EDITOR
        public string EditorVerificationSaveRoot { get; set; }
#endif
        private string SaveRoot
        {
            get
            {
#if UNITY_EDITOR
                if(!string.IsNullOrEmpty(EditorVerificationSaveRoot))return EditorVerificationSaveRoot;
#endif
                return Application.persistentDataPath;
            }
        }
        private void Start()

        {

            if (!HasAuthoredReferences) { enabled = false; Debug.LogError("[Training] Authored lesson references incomplete.", this); return; }

            progressPath = Path.Combine(SaveRoot, "questflight_training_v1.json");

            ownerProgress = progress = TrainingProgress.Load(progressPath);

            flight.NativeStepCompleted += NativeStep;

            input.TrainingRestartRequested += RequestRestart;

        }

        public void OpenLessons()

        {

            if (!HasAuthoredReferences || !flight.IsInitialized) return;

            // The existing showroom needs live cockpit snapshots to finish its fade.

            // Pause only after that transition, otherwise its readiness gate cannot complete.

            if (modes.AuthoredModeMenuView.TransitionPending)

            { pendingOpen = true; input.SetFlightLessonInputSuppressed(true); return; }

            input.DisableFlightLessonGuidance();

            input.SetDeterministicMode(false);

            Speech.Stop();

            PauseClock();

            State = TrainingSessionState.Menu;

            StageIndex = 0;

            announceStage = announceGoAround = false;

            notice = "";

            inputNeedsRelease = true;

            RefreshPresentation();

        }

        public void SelectLesson(int index)

        {

            if (State != TrainingSessionState.Menu || index < 0 || index >= Lessons.Length) return;

            SelectedLesson = index;

            orientationIndex=0;

            StageIndex = 0;

            WithoutPrompts=false; liveCue=null; liveVoice=null; coach?.ResetObservation();

            State = TrainingSessionState.Briefing;

            RefreshPresentation();

            Speak(Lesson.briefingNarration);

        }

        public void StartPractice() => BeginExercise(false);

        public void StartDemonstration() { if (Lesson.allowDemonstration) BeginExercise(true); }

        public void RequestRestart() { if (State != TrainingSessionState.Inactive) resetRequested = true; }

        private void BeginExercise(bool demonstration)

        {

            if (State != TrainingSessionState.Briefing && State != TrainingSessionState.Paused &&

                State != TrainingSessionState.Debrief && State != TrainingSessionState.Demonstration) return;

            if (!flight.IsInitialized) { Block("Native flight system is unavailable."); return; }

            PauseClock();

            input.DisableFlightLessonGuidance();

            Speech.Stop();

            StageIndex = 0;

            WithoutPrompts=false; liveCue=null; liveVoice=null; coach?.ResetObservation();

            pendingFailure = null;

            alternateFinish = false;

            urgentGoAround = false;

            approachFlaps = Lesson.initialFlaps;

            demoRequested = demonstration;

            demoTurnAnnounced=false;

            demonstratedSkills=0; lastDemonstrated=practiceAdvice=null;

            preparationTime = 0;

            resetApplied = false;

            State = TrainingSessionState.Preparing;

            RefreshPresentation();

        }

        private void Update()

        {

            if (State==TrainingSessionState.Inactive && !pendingOpen) {PresentSoaringFreeFlight();return;}

            if (modes.AuthoredNavigation!=null && modes.AuthoredNavigation.IsOpen) { cueRoot.SetActive(false); return; }

            Speech.Tick(Time.unscaledDeltaTime,State==TrainingSessionState.Paused);

            if(voice.isPlaying) { spokenHold=1.5f; liveCueUntil=Time.unscaledTime+3; }

            else if(State!=TrainingSessionState.Paused)spokenHold=Mathf.Max(0,spokenHold-Time.unscaledDeltaTime);

            if(liveCue!=null && !VoicePlaying && Time.unscaledTime>liveCueUntil) { liveCue=null; liveVoice=null; }

            if (pendingOpen)

            {

                if (modes.AuthoredModeMenuView.TransitionPending) return;

                pendingOpen = false; OpenLessons();

            }

            if (State == TrainingSessionState.Inactive) return;

            if (!flight.IsInitialized || !flight.enabled) { if (State != TrainingSessionState.Blocked) Block("Native flight system stopped. Return to the application menu and restart."); return; }

            bool hasFocus = Application.isFocused;
#if UNITY_EDITOR
            if(modes.AuthoredNavigation!=null&&modes.AuthoredNavigation.EditorVerificationFocus.HasValue)hasFocus=modes.AuthoredNavigation.EditorVerificationFocus.Value;
#endif

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR

            // Hidden Windows capture players have no desktop focus. This exception is

            // confined to the explicit development capture; Android always checks focus.

            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("QFL_META_XR_SIMULATOR_TRAINING_VIEW"))) hasFocus = true;

#endif

            bool unavailable = calibration.IsOpen || !hasFocus ||

                (Application.platform == RuntimePlatform.Android && (!OVRManager.hasInputFocus ||

                    (OVRManager.instance != null && !OVRManager.instance.isUserPresent) ||

                    (OVRInput.IsControllerConnected(OVRInput.Controller.RTouch) && !OVRInput.GetControllerOrientationTracked(OVRInput.Controller.RTouch))));

            if (unavailable)

            {

                if (Flying) PauseExercise("Flight paused while tracking, focus or calibration was unavailable. Make sure the controllers are awake, then choose Resume.");

                if (State == TrainingSessionState.Preparing) { PauseClock(); SetFade(0); }

                inputNeedsRelease = true;

                panelRoot.SetActive(false); cueRoot.SetActive(false);

                return;

            }

            if (State == TrainingSessionState.Preparing) AdvancePreparation(Time.unscaledDeltaTime);

            if (Flying && input.Paused) PauseExercise("Paused by controller.");

            ReadControls();

            if (resetRequested) { resetRequested = false; if (Flying) PauseExercise(""); if (State == TrainingSessionState.Paused || State == TrainingSessionState.Debrief) BeginExercise(false); }

            if (Flying && !string.IsNullOrEmpty(pendingFailure)) Finish(false, pendingFailure);

            if (State == TrainingSessionState.Practice)

            {

                if(IsSoaringProgram)AdvanceSoaringPractice();

                else if (evaluator.BadTouchdown || (evaluator.Landed && urgentGoAround)) Finish(false, "Landing needs practice. An unstable approach needs a go-around; reduce sink rate, level the wings and stay over the runway.");

                else if (evaluator.TimedOut) Finish(false, "This exercise needs another attempt. " + evaluator.Feedback);

                else if (evaluator.Complete && (!VoicePlaying || IsTimeCritical(Stage.task))) AdvanceStage();

            }

            if (State == TrainingSessionState.Demonstration || State == TrainingSessionState.Assisting)

                UpdateGuidance(Time.deltaTime);

            if (Time.unscaledTime >= refreshAt) { refreshAt = Time.unscaledTime + .2f; RefreshPresentation(); }

        }

        private void AdvancePreparation(float dt)

        {

            preparationTime += Mathf.Min(dt, .1f);

            float opacity = preparationTime < .25f ? preparationTime / .25f : 1f;

            if (!resetApplied && preparationTime >= .25f)

            {

                if (!(IsSoaringProgram?soaringProgram.Prepare(Lesson,flight):flight.TryStartNewFlight(Lesson.start))) { SetFade(0); Block("The native lesson checkpoint could not be restored."); return; }

                if(IsSoaringProgram)input.ConfigureAircraft(flight.aircraftDefinition);

                var command = InitialControls();

                input.PrepareForFlightLessonRecovery(command);

                BeginProgramAttempt(command.trim,command.flaps);

                crash.Reset(flight.CurrentState.weightOnWheels);

                sampleAccumulator = 0;

                resetApplied = true;

            }

            if (resetApplied && ownsPause)

            {

                // Settle the checkpoint and nearby traffic under the comfort mask.

                // Keep pilot input suppressed until the fade has finished.

                Time.timeScale = savedTimeScale;

                ownsPause = false;

                preparationTime = .25f;

                opacity = 1f;

            }

            if (preparationTime > .35f) opacity = 1f - Mathf.Clamp01((preparationTime - .35f) / .3f);

            SetFade(opacity);

            if (preparationTime < .65f) return;

            SetFade(0);

            ResumeClock();

            State = demoRequested ? TrainingSessionState.Demonstration : TrainingSessionState.Practice;

            if (demoRequested) { MarkProgramAssisted(); BeginGuidance(); }

            else input.DisableFlightLessonGuidance();

            if(!WithoutPrompts)Speak(demoRequested && !IsSoaringProgram ? demonstrationVoice : Stage.narration);
            if(!demoRequested && modes.AuthoredNavigation!=null)modes.AuthoredNavigation.BeginFlightStartHandover();

            RefreshPresentation();

        }

        private FlightLessonControlCommand InitialControls() => new FlightLessonControlCommand

        { throttle = IsSoaringProgram?0:Lesson.initialThrottle, mixture = IsSoaringProgram?0:1, trim = Lesson.initialTrim, flaps = IsSoaringProgram?0:Lesson.initialFlaps,

          leftToeBrake = Lesson.start.calibratedAirspeedKnots < 1 ? 1 : 0,

          rightToeBrake = Lesson.start.calibratedAirspeedKnots < 1 ? 1 : 0 };

        private void NativeStep(FlightDynamicsState s, FlightDynamicsControlSnapshot c, float dt)

        {

            if (!Flying) return;

            if(IsSoaringProgram){ObserveSoaringLesson(s,c,dt);return;}

            evaluator.ObserveNative(s);

            sampleAccumulator += dt;

            if (sampleAccumulator < .1f) return;

            float elapsed = sampleAccumulator; sampleAccumulator = 0;

            FlightLessonRecoveryRuntimeAdapter.TryMapTerrainSample(flight.terrainSurface, s.positionUnityMeters, out var terrain);

            var reason = crash.Evaluate(FlightLessonRecoveryRuntimeAdapter.BuildSafetyState(s), terrain, elapsed,

                FlightLessonCrashSettings.ConservativeTrainingDefaults);

            if (reason != FlightLessonCrashReason.None) pendingFailure = ExplainStop(reason);

            if (State != TrainingSessionState.Practice) return;

            if (Lesson.lessonId == "01_taxi" && s.positionUnityMeters.z > -60)

                pendingFailure = "You crossed the hold-short boundary. Retry and stop before entering the runway.";

            bool approach = Stage.task == TrainingTask.Final || Stage.task == TrainingTask.Landing;

            if (approach && Lesson.goAroundStage >= 0 && c.throttle > .88f && s.pitchDegrees > 2 && !s.weightOnWheels)

            {

                StageIndex = Lesson.goAroundStage;

                alternateFinish = true;

                urgentGoAround = false;

                evaluator.BeginStage(c.trim, Mathf.Max(approachFlaps,c.flaps));

                // Presentation runs on Update, outside the native integrator callback.

                announceStage = true;

            }

            if(coach!=null && coach.TryIndications(out var indicated) &&

                QuestFlightLab.Aircraft.CockpitDynamics.V2.CockpitInstrumentV2Kinematics.TryEvaluateIndicatedAltitudeFeet(indicated,out float indicatedFeet))

                evaluator.Step(Stage,s,c,elapsed,true,indicatedFeet,indicated.indicatedAirspeedKnots,indicated.indicatedVerticalSpeedFeetPerMinute);

            else if(coach==null) evaluator.Step(Stage,s,c,elapsed,true);

            if (approach) approachFlaps = c.flaps;

            if (approach && !alternateFinish && s.altitudeAglMeters < 91.44 && s.altitudeAglMeters > 12 && !evaluator.Landed &&

                (s.calibratedAirspeedKnots < 55 || s.calibratedAirspeedKnots > 80 || s.verticalSpeedFeetPerMinute < -800 ||

                 Mathf.Abs((float)s.bankDegrees) > 20 || Mathf.Abs(s.positionUnityMeters.z + 30) > 65))

                { if (!urgentGoAround) announceGoAround = true; urgentGoAround = true; }

        }

        private bool announceStage, urgentGoAround, announceGoAround;

        private static string ExplainStop(FlightLessonCrashReason reason) => reason switch

        {

            FlightLessonCrashReason.NonFiniteAuthoritativeState => "The flight system supplied invalid data. This is not a student error; restart the exercise.",

            FlightLessonCrashReason.LessonAreaExit => "You left the practice area. Retry and follow the assigned route and outside markers.",

            FlightLessonCrashReason.HardLanding => "The touchdown was too hard. Retry a stabilized approach and ease the descent during the round-out; go around if it is not safe.",

            FlightLessonCrashReason.WaterImpact => "The airplane contacted water. Retry and keep the approach aligned with the paved runway.",

            FlightLessonCrashReason.UnrecoverableLowAltitudeState => "Control was lost near the ground. Retry with smaller bank and pitch corrections; choose a go-around earlier on approach.",

            _ => "The airplane made unsafe ground contact. Retry, maintain terrain clearance and use a stabilized approach to the runway."

        };

        private void AdvanceStage()

        {

            bool last=Stage.task==TrainingTask.Rollout || Stage.task==TrainingTask.GoAroundClean ||

                StageIndex+1>=Lesson.stages.Length || StageIndex+1==Lesson.goAroundStage;

            if(last && VoicePlaying)return;

            bool skippedPreflight=IsSoaringProgram&&Stage.task==TrainingTask.SoaringPreparation&&flight.Operation?.PreflightSkipped==true;
            if(skippedPreflight)lastDemonstrated="Preflight skipped; no checklist skill credited.";
            else {demonstratedSkills++;lastDemonstrated=Stage.successFeedback;}

            if (Stage.task == TrainingTask.Rollout || Stage.task == TrainingTask.GoAroundClean || StageIndex + 1 >= Lesson.stages.Length)

            { Finish(true, alternateFinish ? "You chose to go around and established a clean climb." : "Exercise complete."); return; }

            StageIndex++;

            // The go-around sequence is an alternative, never an extra landing requirement.

            if (StageIndex == Lesson.goAroundStage) { Finish(true, "Exercise complete."); return; }

            BeginProgramStage(flight.LastAppliedControls.trim, flight.LastAppliedControls.flaps);

            liveCue=null; liveVoice=null;

            if(!WithoutPrompts || IsTimeCritical(Stage.task)) Speak(Stage.narration);

        }

        private static bool IsTimeCritical(TrainingTask task) => task==TrainingTask.RollControl || task==TrainingTask.PitchControl || task==TrainingTask.RudderControl || task==TrainingTask.TakeoffRoll || task==TrainingTask.Rotation ||

            task==TrainingTask.Climb || task==TrainingTask.Descend || task==TrainingTask.Landing || task==TrainingTask.GoAroundPower || task==TrainingTask.GoAroundClimb;

        public void PauseExercise(string reason = "")

        {

            if (!Flying) return;

            resumedState = State;

            pausedControls = ProductControlInput.ToFlightLessonCommand(input.Current);

            PauseClock();

            voice.Pause();

            notice = reason;

            State = TrainingSessionState.Paused;

            RefreshPresentation();

        }

        public void ResumeExercise()

        {

            if (State != TrainingSessionState.Paused || calibration.IsOpen || ((AssistancePaused||DemonstrationPaused)&&!CanResumeAssistance)) return;

            // Preserve actual lever positions instead of restoring checkpoint power/flaps on resume.

            input.PrepareForFlightLessonRecovery(pausedControls);

            State = resumedState;

            ResumeClock(); voice.UnPause();

            if (State == TrainingSessionState.Practice) input.DisableFlightLessonGuidance();

            else BeginGuidance();

            inputNeedsRelease = true;

            RefreshPresentation();

        }

        public void ToggleHelp()

        {

            if(IsSoaringProgram){ToggleSoaringHelp();return;}

            if (State == TrainingSessionState.Assisting)

            {

                input.DisableFlightLessonGuidance();

                input.PrepareForFlightLessonRecovery(appliedGuidance);

                input.SetFlightLessonInputSuppressed(false);

                State = TrainingSessionState.Practice;

                evaluator.BeginStage(flight.LastAppliedControls.trim, flight.LastAppliedControls.flaps);

                notice = "YOUR CONTROLS - assisted attempt";

                Speak(handbackVoice,true);

            }

            else if (State == TrainingSessionState.Practice && !flight.CurrentState.weightOnWheels && flight.CurrentState.altitudeAglMeters > 91.44)

            { State = TrainingSessionState.Assisting; evaluator.MarkAssisted(); BeginGuidance(); notice = "INSTRUCTOR CONTROLS - Menu: Take control"; Speak(assistanceVoice,true); }

            else if (State == TrainingSessionState.Practice) PauseExercise("Near the ground, assistance pauses the exercise. Review the current step, restart, or resume manual control.");

            RefreshPresentation();

        }

        private void BeginGuidance()

        {

            var s = flight.CurrentState;

            assistHeading = (float)s.headingDegrees;

            assistAltitude = (float)(s.altitudeMslMeters * FlightFrameConversions.MetersToFeet);

            assistSpeed = Mathf.Clamp((float)s.calibratedAirspeedKnots, 75, 100);

            appliedGuidance = ProductControlInput.ToFlightLessonCommand(input.Current);

            input.ReleaseAllTouchOverrides();

            guidance.Reset(); guidanceTime = 0;

        }

        private void UpdateGuidance(float dt)

        {

            if (dt <= 0) return;

            if(IsSoaringProgram){UpdateSoaringGuidance(dt);return;}

            guidanceTime += dt;

            bool demonstrateTurn = State == TrainingSessionState.Demonstration && guidanceTime > 12;

            if(demonstrateTurn&&!demoTurnAnnounced&&!VoicePlaying)

            { demoTurnAnnounced=true; Speak(demonstrationTurnVoice); coach?.Show(TrainingReference.Attitude,10); }

            float heading = demonstrateTurn ? assistHeading - 30 : assistHeading;

            var intent = new FlightLessonGuidanceIntent("training_explicit_assistance", FlightLessonSegmentKind.Cruise,

                flight.CurrentState.positionUnityMeters, heading, 0, assistAltitude, assistSpeed, 15, 400, 0);

            if (!guidance.TryStep(intent, ProductDemoPilot.BuildGuidanceAircraftState(flight.CurrentState),

                Mathf.Min(dt, .05f), FlightLessonGuidanceControllerSettings.Default, out var command))

            { Block("Instructional guidance could not produce a valid control command."); return; }

            // Reuse bounded guidance and the same player-input boundary as the existing demo.

            appliedGuidance.aileron = Mathf.MoveTowards(appliedGuidance.aileron, command.aileron, dt * .9f);

            appliedGuidance.elevator = Mathf.MoveTowards(appliedGuidance.elevator, command.elevator, dt * .9f);

            appliedGuidance.rudder = Mathf.MoveTowards(appliedGuidance.rudder, command.rudder, dt * .9f);

            appliedGuidance.throttle = Mathf.MoveTowards(appliedGuidance.throttle, command.throttle, dt * .65f);

            input.SetFlightLessonGuidance(true, appliedGuidance);

            if (State == TrainingSessionState.Demonstration && guidanceTime >= 30 && !VoicePlaying)

                PauseExercise("Demonstration finished. Restart exercise for independent practice.");

        }

        private void Finish(bool complete, string reason)

        {

            bool independent = complete && !ProgramAssisted && WithoutPrompts;

            string result = complete ? ProgramAssisted ? "COMPLETED WITH FLIGHT ASSISTANCE" : independent ? "COMPLETED WITHOUT ROUTINE PROMPTS" : "COMPLETED GUIDED PRACTICE" : "NEEDS PRACTICE";
            bool preflightSkipped=IsSoaringProgram&&flight.Operation?.PreflightSkipped==true;
            if(complete&&preflightSkipped)result="COMPLETED WITH PREFLIGHT SKIPPED";

            string next=complete ? practiceAdvice ?? "Repeat this lesson without routine prompts when you feel ready." : Stage.retryAdvice;

            LastDebrief = (complete ? ProgramAssisted ? "Completed with flight assistance" : independent ? "Completed without routine prompts" : "Guided practice complete" : "Practice this skill again") + "\n" +

                (complete ? $"Demonstrated {demonstratedSkills} skills. " + lastDemonstrated : reason) +

                "\n\nNext practice: " + next + "\n\nReflect: " + Lesson.reflection;
            if(preflightSkipped)LastDebrief="Preflight skipped; checklist not credited.\n"+LastDebrief.Replace("Completed with flight assistance","Completed with preflight skipped");

            PauseClock(); input.DisableFlightLessonGuidance(); Speech.Stop();

            State = TrainingSessionState.Debrief;

            progress.Record(Lesson.lessonId, result, LastDebrief, independent);

            if(IsGuestSession) LastDebrief += "\n\nGuest session: saved owner progress is unchanged.";

            else if (!progress.Save(progressPath)) LastDebrief += "\nProgress could not be saved on this device.";

            Debug.Log("[Training] " + Lesson.lessonId + ": " + LastDebrief, this);

            RefreshPresentation();

        }

        private void PauseClock()

        {

            if (!ownsPause) { savedTimeScale = Time.timeScale > 0 ? Time.timeScale : 1; ownsPause = true; }

            Time.timeScale = 0;

            input.SetFlightLessonInputSuppressed(true);

        }

        private void ResumeClock()

        {

            if (ownsPause) { Time.timeScale = savedTimeScale; ownsPause = false; }

            input.SetFlightLessonInputSuppressed(false);

        }

        private void Block(string reason)

        { PauseClock(); input.DisableFlightLessonGuidance(); Speech.Stop(); State = TrainingSessionState.Blocked; notice = reason; SetFade(0); RefreshPresentation(); }

        private void ReadControls()

        {

            if (modes.AuthoredNavigation != null) return;

            bool a = OVRInput.Get(OVRInput.RawButton.A), b = OVRInput.Get(OVRInput.RawButton.B);

            bool x = OVRInput.Get(OVRInput.RawButton.X), y = OVRInput.Get(OVRInput.RawButton.Y);

            float vertical = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick).y;

            bool aDown = a && !aHeld, bDown = b && !bHeld, xDown = x && !xHeld, yDown = y && !yHeld;

            aHeld = a; bHeld = b; xHeld = x; yHeld = y;

            if (inputNeedsRelease) { if (!a && !b && !x && !y && Mathf.Abs(vertical) < .2f) inputNeedsRelease = false; return; }

            if (State == TrainingSessionState.Menu)

            {

                if (Mathf.Abs(vertical) > .6f && !stickHeld) SelectedLesson = Mathf.Clamp(SelectedLesson + (vertical < 0 ? 1 : -1), 0, Lessons.Length - 1);

                stickHeld = Mathf.Abs(vertical) > .25f;

                if (aDown) SelectLesson(SelectedLesson);

            }

            else if (State == TrainingSessionState.Briefing)

            { if (aDown) StartPractice(); else if (bDown) OpenLessons(); else if (xDown) StartDemonstration(); else if (yDown) Speak(Lesson.briefingNarration); }

            else if (Flying)

            { if (aDown) PauseExercise(); else if (bDown) Speak(Stage.narration); else if (xDown) ToggleHelp(); else if (yDown) ToggleCaptions(); }

            else if (State == TrainingSessionState.Paused)

            { if (aDown) ResumeExercise(); else if (bDown) OpenLessons(); else if (xDown) BeginExercise(false); else if (yDown) Speak(Stage.narration); }

            else if (State == TrainingSessionState.Debrief)

            { if (aDown) BeginExercise(false); else if (bDown) OpenLessons(); else if (xDown) { State = TrainingSessionState.Menu; SelectLesson(Mathf.Min(SelectedLesson + 1, Lessons.Length - 1)); } }

        }

        public void RefreshPresentation()

        {

            if (State == TrainingSessionState.Inactive) return;

            bool showMenu = !Flying && State != TrainingSessionState.Preparing;

            panelRoot.SetActive(showMenu && modes.AuthoredNavigation == null);

            cueRoot.SetActive(Flying || State == TrainingSessionState.Preparing);

            if (announceStage) { announceStage = false; liveCue=null;liveVoice=null; if(!WithoutPrompts || IsTimeCritical(Stage.task))Speak(Stage.narration); }

            if (announceGoAround) { announceGoAround = false; Speak(goAroundVoice,true,"Unstable approach — go around. Apply full power and establish a climb."); }

            if (State == TrainingSessionState.Menu)

            {

                var text = new StringBuilder("FLIGHT LESSONS\n\n");

                for (int i = 0; i < Lessons.Length; i++)

                    text.Append(i == SelectedLesson ? "> " : "  ").Append(i + 1).Append("  ").Append(Lessons[i].displayName)

                        .Append(progress?.Find(Lessons[i].lessonId)?.independentCompletion == true ? "  [done]" : "").Append('\n');

                menuText.text = text + "\nLEFT STICK select   A briefing\n" + (IsGuestSession?"Guest session: results do not change owner progress.":"Choose any lesson. Progress is saved on this headset.");

            }

            else if (State == TrainingSessionState.Briefing)

                menuText.text = Lesson.displayName.ToUpperInvariant() + "\n\n" + Wrap(Lesson.briefing, 58) +

                    "\n\nA practice   B lessons   Y repeat briefing\n" + (Lesson.allowDemonstration ? "X watch a short demonstration\n" : "") + "No training credit. Practice targets are simulator tolerances.";

            else if (State == TrainingSessionState.Paused)

                menuText.text = "EXERCISE PAUSED\n\n" + Wrap(notice, 58) + "\n\nA resume   X restart exercise\nB return to lessons   Y repeat instruction";

            else if (State == TrainingSessionState.Debrief)

                menuText.text = Lesson.displayName.ToUpperInvariant() + "\n\n" + Wrap(LastDebrief, 58) + "\n\nA retry   B lessons   X next lesson";

            else if (State == TrainingSessionState.Blocked) menuText.text = "LESSON STOPPED\n\n" + Wrap(notice, 58);

            string owner = State == TrainingSessionState.Practice ? "YOUR CONTROLS" : State == TrainingSessionState.Preparing ? "PREPARING EXERCISE" : "ASSISTANCE HAS CONTROL";

            string instruction = State == TrainingSessionState.Demonstration ? "Watch the outside horizon; compare the attitude indicator. Menu opens help." :

                State == TrainingSessionState.Assisting ? "Instructor holds the aircraft. Menu offers Take control." : liveCue ?? Stage.instruction;

            if(IsSoaringProgram)instruction=SoaringLessonInstruction;

            if(State==TrainingSessionState.Practice && evaluator.IsControlCheck(Stage.task))

                instruction=Stage.instruction+"\n\nNow: "+evaluator.Feedback;

            // Keep the spoken instruction visible even when the flight evaluator advances.

            // A new stage's objective can update without cutting the previous sentence off.

            bool speaking=voice.isPlaying || spokenHold>0;

            if(speaking && !string.IsNullOrEmpty(spokenInstruction) && !IsSoaringProgram && !WithoutPrompts && captions)instruction=spokenInstruction;
            instruction=BindingAwareInstruction(instruction);
            if(WithoutPrompts && State==TrainingSessionState.Practice)instruction="Practice without routine coaching. Menu opens current-step help."+(IsSoaringProgram&&flight.Operation!=null?"\n"+flight.Operation.Instruction:"");

            if(instruction!=null&&instruction.Contains("steering thumbstick"))instruction+=$"\nSteering: {ProductComfortPreferences.RudderHand} thumbstick.";

            if(instruction!=null&&instruction.Contains("trim thumbstick"))instruction+=$"\nTrim: {ProductComfortPreferences.TrimHand} thumbstick.";

            cueText.text = owner + " | " + (StageIndex + 1) + "/" + Lesson.stages.Length + " " + Stage.title +

                ("\n" + Wrap(urgentGoAround ? "UNSTABLE APPROACH - GO AROUND" : instruction, 66)) +

                "\nMenu: pause / help    X: repeat / show    B / Y: back";

            if(polishedInstruction!=null)polishedInstruction.Present(

                State==TrainingSessionState.Practice?"Your controls":State==TrainingSessionState.Preparing?"Preparing":"Instructor controls",

                Stage.title,urgentGoAround?"Unstable approach — go around":instruction,StageIndex,Lesson.stages.Length,true,urgentGoAround,speaking&&captions&&!WithoutPrompts&&!IsSoaringProgram?voice:null);

        }

        private void Speak(AudioClip clip,bool interrupt=false,string text=null)

        {

            if(clip==null)return;

            int stage=StageIndex;

            bool stageCue=clip==Stage.narration;

            string caption=text ?? (stageCue?Stage.instruction:liveVoice==clip?liveCue:null);

            string title=Stage.title;

            Speech.Request(clip,()=>

            {

                spokenInstruction=caption;spokenTitle=title;spokenHold=clip.length+1.5f;

            },interrupt,()=>!stageCue || stage==StageIndex);

        }

        private static void SetFade(float opacity) => OVRManager.SetColorScaleAndOffset(new Vector4(1 - opacity, 1 - opacity, 1 - opacity, 1), Vector4.zero, true);

        public static string Wrap(string value, int width)

        {

            var result = new StringBuilder(); int column = 0;

            foreach (string line in (value ?? "").Split('\n'))

            {

                if (result.Length > 0) result.Append('\n'); column = 0;

                foreach (string word in line.Split(' '))

                { if (column > 0 && column + word.Length + 1 > width) { result.Append('\n'); column = 0; } if (column > 0) { result.Append(' '); column++; } result.Append(word); column += word.Length; }

            }

            return result.ToString();

        }

        private void OnDestroy()

        {

            if (flight != null) flight.NativeStepCompleted -= NativeStep;

            if (input != null) input.TrainingRestartRequested -= RequestRestart;

            if (ownsPause) Time.timeScale = savedTimeScale;

            if (State != TrainingSessionState.Inactive) SetFade(0);

        }

    }

}
