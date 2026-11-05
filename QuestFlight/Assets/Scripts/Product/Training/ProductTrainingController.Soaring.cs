using System;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;
using UnityEngine;

namespace QuestFlight.Product.Training
{
    public sealed partial class ProductTrainingController
    {
        [SerializeField] private TrainingLessonDefinition[] soaringLessons=Array.Empty<TrainingLessonDefinition>();
        private readonly SoaringTrainingProgram soaringProgram=new SoaringTrainingProgram();
        public bool IsSoaringProgram=>modes!=null&&modes.IsGlider;
        public void ConfigureSoaringCatalogForAuthoring(TrainingLessonDefinition[] value)=>soaringLessons=value;
        public void SelectAircraftCatalog(){if(State==TrainingSessionState.Inactive){SelectedLesson=IsSoaringProgram?0:1;StageIndex=0;}}
        private bool SelectedCatalogValid=>IsSoaringProgram?
            soaringLessons.Length==7&&Array.TrueForAll(soaringLessons,l=>l!=null&&l.IsValid&&l.program==TrainingProgramKind.Sgs233Reference&&l.lessonId.StartsWith("sgs233.reference.")&&l.initialThrottle==0&&!l.start.engineRunning&&l.goAroundStage==-1):
            lessons.Length==6&&Array.TrueForAll(lessons,l=>l!=null&&l.IsValid&&l.program==TrainingProgramKind.C172);
        private bool ProgramAssisted=>IsSoaringProgram?(soaringProgram.Assisted||flight.Operation?.PreflightSkipped==true):evaluator.Assisted;
        private bool ProgramAllowsAssistance=>!IsSoaringProgram || flight.TowSession==null ||
            (flight.TowSession.Line.state!=TowlineState.Taut&&flight.TowSession.Line.state!=TowlineState.HookedSlack);
        private void MarkProgramAssisted(){if(IsSoaringProgram)soaringProgram.MarkAssisted();else evaluator.MarkAssisted();}
        private void BeginProgramAttempt(float trim,float flaps)
        {if(IsSoaringProgram)soaringProgram.BeginAttempt(flight.CurrentState);else evaluator.BeginAttempt(flight.CurrentState);BeginProgramStage(trim,flaps);}
        private void BeginProgramStage(float trim,float flaps)
        {if(IsSoaringProgram)soaringProgram.BeginStage(flight.CurrentState,trim,flight);else evaluator.BeginStage(trim,flaps);}
        private void ObserveSoaringLesson(in FlightDynamicsState state,in FlightDynamicsControlSnapshot controls,float dt)
        {
            soaringProgram.ObserveNative(state,flight);sampleAccumulator+=dt;
            if(sampleAccumulator<.1f)return;float elapsed=sampleAccumulator;sampleAccumulator=0;
            if(State==TrainingSessionState.Practice)soaringProgram.Step(Stage,state,controls,elapsed,flight);
        }
        private void AdvanceSoaringPractice()
        {
            if(!string.IsNullOrEmpty(soaringProgram.Failure))Finish(false,soaringProgram.Failure);
            else if(soaringProgram.TimedOut)Finish(false,"Practice window expired. "+soaringProgram.Feedback);
            else if(soaringProgram.Complete)AdvanceStage();
        }
        private string SoaringLessonInstruction=>State==TrainingSessionState.Demonstration?"DEMONSTRATION · Native controls hold a 50 kt glide and make a gentle turn. Airbrakes remain yours. Start a fresh practice attempt to receive independent credit.":
            State==TrainingSessionState.Assisting?"ASSISTED GLIDE · Native controls hold speed and heading. Airbrakes remain yours; the aircraft descends. Menu: take control.":
            flight.Operation!=null?flight.Operation.Instruction:CurrentInstruction+(WithoutPrompts?"":"\n"+soaringProgram.Feedback);
        private void ToggleSoaringHelp()
        {
            if(State==TrainingSessionState.Assisting)
            {
                input.DisableFlightLessonGuidance();input.PrepareForFlightLessonRecovery(appliedGuidance);input.SetFlightLessonInputSuppressed(false);
                State=TrainingSessionState.Practice;BeginProgramStage(flight.LastAppliedControls.trim,0);notice="YOUR CONTROLS · assisted attempt";
            }
            else if(State==TrainingSessionState.Practice&&ProgramAllowsAssistance&&!flight.CurrentState.weightOnWheels&&flight.CurrentState.altitudeAglMeters>150)
            {State=TrainingSessionState.Assisting;soaringProgram.MarkAssisted();BeginGuidance();notice="ASSISTED GLIDE · descending without power";}
            else if(State==TrainingSessionState.Practice)PauseExercise("Assistance pauses ground, low-altitude and attached-tow exercises. Review the instruction or resume your controls.");
            RefreshPresentation();
        }
        private void UpdateSoaringGuidance(float dt)
        {
            if(!string.IsNullOrEmpty(soaringProgram.Failure)){Finish(false,soaringProgram.Failure);return;}
            if(!ProgramAllowsAssistance||flight.CurrentState.altitudeAglMeters<120){PauseExercise("Assisted glide reached its terrain margin. Choose a safe manual plan or restart the exercise.");return;}
            guidanceTime+=dt;
            appliedGuidance=soaringProgram.Assist(flight.CurrentState,assistHeading,flight.LastAppliedControls.trim,State==TrainingSessionState.Demonstration,guidanceTime);
            input.SetFlightLessonGuidance(true,appliedGuidance);
            if(State==TrainingSessionState.Demonstration&&guidanceTime>=30)PauseExercise("Demonstration finished. Start practice for a new independent attempt.");
        }
        private void PresentSoaringFreeFlight()
        {
            bool handoff=input.FlightInputAwaitingRelease && modes.State==ProductFlightModeCoordinator.FlightModeState.FreeFlight && (modes.AuthoredNavigation==null||!modes.AuthoredNavigation.IsOpen);
            bool visible=handoff||IsSoaringProgram&&modes.State==ProductFlightModeCoordinator.FlightModeState.FreeFlight&&flight.Operation!=null&&
                (modes.AuthoredNavigation==null||!modes.AuthoredNavigation.IsOpen)&&Time.timeScale>0;
            if(cueRoot!=null&&cueRoot.activeSelf!=visible)cueRoot.SetActive(visible);
            if(!visible||Time.unscaledTime<refreshAt)return;refreshAt=Time.unscaledTime+.5f;
            if(handoff)
            {cueText.text="Release menu buttons, triggers and sticks to take control.";if(polishedInstruction!=null)polishedInstruction.Present("Ready to fly","Release menu inputs",cueText.text,0,1,true,false,null);return;}
            var op=flight.Operation;
            // Use the existing authored caption/plate; no extra renderer or UI hierarchy.
            cueText.text=Wrap(op.Instruction,66);
            if(polishedInstruction!=null)polishedInstruction.Present("SGS reference · Your controls",op.Phase.ToString(),op.Instruction,0,1,true,op.RequiresPause,null);
        }
    }
}
