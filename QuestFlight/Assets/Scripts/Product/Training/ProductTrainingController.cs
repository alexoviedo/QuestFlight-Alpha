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
    public sealed class ProductTrainingController : MonoBehaviour
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
        [SerializeField] private TrainingLessonDefinition[] lessons = Array.Empty<TrainingLessonDefinition>();
        public TrainingSessionState State { get; private set; }
        public int SelectedLesson { get; private set; } = 1;
        public int StageIndex { get; private set; }
        public string LastDebrief { get; private set; } = "";
        public TrainingLessonDefinition[] Lessons => lessons;
        public TrainingEvaluator Evaluator => evaluator;
        public bool ReservesRunway => State != TrainingSessionState.Inactive && State != TrainingSessionState.Menu &&
            State != TrainingSessionState.Briefing && State != TrainingSessionState.Debrief && Lesson.airportExercise;
        public bool HasAuthoredReferences => modes != null && flight != null && input != null &&
            calibration != null && panelRoot != null && menuText != null && cueRoot != null &&
            cueText != null && voice != null && demonstrationVoice != null && assistanceVoice != null &&
            handbackVoice != null && goAroundVoice != null && lessons.Length == 6 && Array.TrueForAll(lessons, l => l != null && l.IsValid);
        private readonly TrainingEvaluator evaluator = new TrainingEvaluator();
        private readonly FlightLessonGuidanceController guidance = new FlightLessonGuidanceController();
        private readonly FlightLessonCrashClassifier crash = new FlightLessonCrashClassifier();
        private TrainingProgress progress;
        private string progressPath, pendingFailure, notice = "";
        private float savedTimeScale = 1, sampleAccumulator, refreshAt, preparationTime, guidanceTime;
        private bool ownsPause, captions = true, demoRequested, resetApplied, resetRequested, alternateFinish;
        private bool aHeld, bHeld, xHeld, yHeld, stickHeld, inputNeedsRelease;
        private bool pendingOpen;
        private TrainingSessionState resumedState;
        private float assistHeading, assistAltitude, assistSpeed;
        private float approachFlaps;
        private FlightLessonControlCommand appliedGuidance;
        private FlightLessonControlCommand pausedControls;
        private TrainingLessonDefinition Lesson => lessons[SelectedLesson];
        private TrainingStage Stage => Lesson.stages[StageIndex];
        private bool Flying => State == TrainingSessionState.Practice || State == TrainingSessionState.Demonstration || State == TrainingSessionState.Assisting;

        public void ConfigureForAuthoring(ProductFlightModeCoordinator m, FlightDynamicsCoordinator f,
            ProductControlInput i, ProductionSeatCalibrationController c, GameObject panel, TextMesh text,
            GameObject cue, TextMesh caption, AudioSource audio, TrainingLessonDefinition[] catalog)
        { modes = m; flight = f; input = i; calibration = c; panelRoot = panel; menuText = text; cueRoot = cue; cueText = caption; voice = audio; lessons = catalog; }
        public void ConfigureVoiceForAuthoring(AudioClip demonstration, AudioClip assistance, AudioClip handback, AudioClip goAround)
        { demonstrationVoice = demonstration; assistanceVoice = assistance; handbackVoice = handback; goAroundVoice = goAround; }

        private void Start()
        {
            if (!HasAuthoredReferences) { enabled = false; Debug.LogError("[Training] Authored lesson references incomplete.", this); return; }
            progressPath = Path.Combine(Application.persistentDataPath, "questflight_training_v1.json");
            progress = TrainingProgress.Load(progressPath);
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
            voice.Stop();
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
            if (State != TrainingSessionState.Menu || index < 0 || index >= lessons.Length) return;
            SelectedLesson = index;
            StageIndex = 0;
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
            voice.Stop();
            StageIndex = 0;
            pendingFailure = null;
            alternateFinish = false;
            urgentGoAround = false;
            approachFlaps = Lesson.initialFlaps;
            demoRequested = demonstration;
            preparationTime = 0;
            resetApplied = false;
            State = TrainingSessionState.Preparing;
            RefreshPresentation();
        }

        private void Update()
        {
            if (pendingOpen)
            {
                if (modes.AuthoredModeMenuView.TransitionPending) return;
                pendingOpen = false; OpenLessons();
            }
            if (State == TrainingSessionState.Inactive) return;
            if (!flight.IsInitialized || !flight.enabled) { if (State != TrainingSessionState.Blocked) Block("Native flight system stopped. Return to the application menu and restart."); return; }
            bool hasFocus = Application.isFocused;
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
                if (Flying) PauseExercise("Paused while tracking, focus or calibration is unavailable.");
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
                if (evaluator.BadTouchdown || (evaluator.Landed && urgentGoAround)) Finish(false, "Landing needs practice. An unstable approach needs a go-around; reduce sink rate, level the wings and stay over the runway.");
                else if (evaluator.TimedOut) Finish(false, "This exercise needs another attempt. " + evaluator.Feedback);
                else if (evaluator.Complete) AdvanceStage();
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
                if (!flight.TryRecoverToInitialConditions(Lesson.start)) { SetFade(0); Block("The native lesson checkpoint could not be restored."); return; }
                var command = InitialControls();
                input.PrepareForFlightLessonRecovery(command);
                evaluator.BeginAttempt(flight.CurrentState);
                evaluator.BeginStage(command.trim, command.flaps);
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
            if (demoRequested) { evaluator.MarkAssisted(); BeginGuidance(); }
            else input.DisableFlightLessonGuidance();
            Speak(demoRequested ? demonstrationVoice : Stage.narration);
            RefreshPresentation();
        }

        private FlightLessonControlCommand InitialControls() => new FlightLessonControlCommand
        { throttle = Lesson.initialThrottle, mixture = 1, trim = Lesson.initialTrim, flaps = Lesson.initialFlaps,
          leftToeBrake = Lesson.start.calibratedAirspeedKnots < 1 ? 1 : 0,
          rightToeBrake = Lesson.start.calibratedAirspeedKnots < 1 ? 1 : 0 };

        private void NativeStep(FlightDynamicsState s, FlightDynamicsControlSnapshot c, float dt)
        {
            if (!Flying) return;
            evaluator.ObserveNative(s);
            sampleAccumulator += dt;
            if (sampleAccumulator < .1f) return;
            float elapsed = sampleAccumulator; sampleAccumulator = 0;
            FlightLessonRecoveryRuntimeAdapter.TryMapTerrainSample(flight.terrainSurface, s.positionUnityMeters, out var terrain);
            var reason = crash.Evaluate(FlightLessonRecoveryRuntimeAdapter.BuildSafetyState(s), terrain, elapsed,
                FlightLessonCrashSettings.ConservativeTrainingDefaults);
            if (reason != FlightLessonCrashReason.None) pendingFailure = "Exercise stopped: " + reason + ". Retry from the lesson checkpoint.";
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
            evaluator.Step(Stage, s, c, elapsed, true);
            if (approach) approachFlaps = c.flaps;
            if (approach && !alternateFinish && s.altitudeAglMeters < 91.44 && s.altitudeAglMeters > 12 && !evaluator.Landed &&
                (s.calibratedAirspeedKnots < 55 || s.calibratedAirspeedKnots > 80 || s.verticalSpeedFeetPerMinute < -800 ||
                 Mathf.Abs((float)s.bankDegrees) > 20 || Mathf.Abs(s.positionUnityMeters.z + 30) > 65))
                { if (!urgentGoAround) announceGoAround = true; urgentGoAround = true; }
        }

        private bool announceStage, urgentGoAround, announceGoAround;
        private void AdvanceStage()
        {
            if (Stage.task == TrainingTask.Rollout || Stage.task == TrainingTask.GoAroundClean || StageIndex + 1 >= Lesson.stages.Length)
            { Finish(true, alternateFinish ? "You chose to go around and established a clean climb." : "Exercise complete."); return; }
            StageIndex++;
            // The go-around sequence is an alternative, never an extra landing requirement.
            if (StageIndex == Lesson.goAroundStage) { Finish(true, "Exercise complete."); return; }
            evaluator.BeginStage(flight.LastAppliedControls.trim, flight.LastAppliedControls.flaps);
            Speak(Stage.narration);
        }

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
            if (State != TrainingSessionState.Paused || calibration.IsOpen) return;
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
            if (State == TrainingSessionState.Assisting)
            {
                input.DisableFlightLessonGuidance();
                input.PrepareForFlightLessonRecovery(appliedGuidance);
                input.SetFlightLessonInputSuppressed(false);
                State = TrainingSessionState.Practice;
                evaluator.BeginStage(flight.LastAppliedControls.trim, flight.LastAppliedControls.flaps);
                notice = "YOUR CONTROLS - assisted attempt";
                Speak(handbackVoice);
            }
            else if (State == TrainingSessionState.Practice && !flight.CurrentState.weightOnWheels && flight.CurrentState.altitudeAglMeters > 91.44)
            { State = TrainingSessionState.Assisting; evaluator.MarkAssisted(); BeginGuidance(); notice = "ASSISTANCE HAS CONTROL - X returns control to you"; Speak(assistanceVoice); }
            else if (State == TrainingSessionState.Practice) PauseExercise("Near the ground, assistance pauses the exercise. Repeat the instruction, retry, or resume to fly a go-around.");
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
            guidanceTime += dt;
            bool demonstrateTurn = State == TrainingSessionState.Demonstration && guidanceTime > 12;
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
            if (State == TrainingSessionState.Demonstration && guidanceTime >= 30)
                PauseExercise("Demonstration finished. Restart exercise for independent practice.");
        }

        private void Finish(bool complete, string reason)
        {
            bool independent = complete && !evaluator.Assisted;
            string result = complete ? independent ? "COMPLETED INDEPENDENTLY" : "COMPLETED WITH ASSISTANCE" : "NEEDS PRACTICE";
            LastDebrief = result + "\n" + reason + "\n" +
                $"Manual time in target conditions: {evaluator.GoodSeconds:0} / {evaluator.TotalSeconds:0} seconds.\n" +
                (evaluator.Landed ? $"Touchdown sink: {evaluator.TouchdownSinkFpm:0} ft/min.\n" : "") +
                "This records simulator practice, not pilot proficiency.";
            PauseClock(); input.DisableFlightLessonGuidance(); voice.Stop();
            State = TrainingSessionState.Debrief;
            progress.Record(Lesson.lessonId, result, LastDebrief, independent);
            if (!progress.Save(progressPath)) LastDebrief += "\nProgress could not be saved on this device.";
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
        { PauseClock(); input.DisableFlightLessonGuidance(); voice.Stop(); State = TrainingSessionState.Blocked; notice = reason; SetFade(0); RefreshPresentation(); }

        private void ReadControls()
        {
            bool a = OVRInput.Get(OVRInput.RawButton.A), b = OVRInput.Get(OVRInput.RawButton.B);
            bool x = OVRInput.Get(OVRInput.RawButton.X), y = OVRInput.Get(OVRInput.RawButton.Y);
            float vertical = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick).y;
            bool aDown = a && !aHeld, bDown = b && !bHeld, xDown = x && !xHeld, yDown = y && !yHeld;
            aHeld = a; bHeld = b; xHeld = x; yHeld = y;
            if (inputNeedsRelease) { if (!a && !b && !x && !y && Mathf.Abs(vertical) < .2f) inputNeedsRelease = false; return; }
            if (State == TrainingSessionState.Menu)
            {
                if (Mathf.Abs(vertical) > .6f && !stickHeld) SelectedLesson = Mathf.Clamp(SelectedLesson + (vertical < 0 ? 1 : -1), 0, lessons.Length - 1);
                stickHeld = Mathf.Abs(vertical) > .25f;
                if (aDown) SelectLesson(SelectedLesson);
            }
            else if (State == TrainingSessionState.Briefing)
            { if (aDown) StartPractice(); else if (bDown) OpenLessons(); else if (xDown) StartDemonstration(); else if (yDown) Speak(Lesson.briefingNarration); }
            else if (Flying)
            { if (aDown) PauseExercise(); else if (bDown) Speak(Stage.narration); else if (xDown) ToggleHelp(); else if (yDown) captions = !captions; }
            else if (State == TrainingSessionState.Paused)
            { if (aDown) ResumeExercise(); else if (bDown) OpenLessons(); else if (xDown) BeginExercise(false); else if (yDown) Speak(Stage.narration); }
            else if (State == TrainingSessionState.Debrief)
            { if (aDown) BeginExercise(false); else if (bDown) OpenLessons(); else if (xDown) { State = TrainingSessionState.Menu; SelectLesson(Mathf.Min(SelectedLesson + 1, lessons.Length - 1)); } }
        }

        public void RefreshPresentation()
        {
            if (State == TrainingSessionState.Inactive) return;
            bool showMenu = !Flying && State != TrainingSessionState.Preparing;
            panelRoot.SetActive(showMenu);
            cueRoot.SetActive(Flying || State == TrainingSessionState.Preparing);
            if (announceStage) { announceStage = false; Speak(Stage.narration); }
            if (announceGoAround) { announceGoAround = false; Speak(goAroundVoice); }
            if (State == TrainingSessionState.Menu)
            {
                var text = new StringBuilder("FLIGHT LESSONS\n\n");
                for (int i = 0; i < lessons.Length; i++)
                    text.Append(i == SelectedLesson ? "> " : "  ").Append(i + 1).Append("  ").Append(lessons[i].displayName)
                        .Append(progress?.Find(lessons[i].lessonId)?.independentCompletion == true ? "  [done]" : "").Append('\n');
                menuText.text = text + "\nLEFT STICK select   A briefing\nChoose any lesson. Progress is saved on this headset.";
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
            string instruction = State == TrainingSessionState.Demonstration ? "Watch the horizon and attitude indicator during level flight and a gentle left turn. Pause, then restart for your own practice." :
                State == TrainingSessionState.Assisting ? "Assistance is holding near your current heading and altitude. Press X when ready to take control. This attempt is marked assisted." : Stage.instruction;
            cueText.text = owner + " | " + (StageIndex + 1) + "/" + Lesson.stages.Length + " " + Stage.title +
                (captions ? "\n" + Wrap(instruction, 66) + "\n" + Wrap(urgentGoAround ? "UNSTABLE APPROACH - GO AROUND" : Flying && State != TrainingSessionState.Practice ? "Practice evaluation is suspended during assistance." : evaluator.Feedback, 66) : "") +
                "\nA pause   B repeat   X help / my controls   Y captions";
        }

        private void Speak(AudioClip clip) { if (clip != null) { voice.Stop(); voice.clip = clip; voice.Play(); } }
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
