using System;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlight.Product.Training
{
    public enum TrainingReference { Outside, Airspeed, Attitude, Altimeter, Heading, VerticalSpeed, Slip, Throttle, Yoke, Flaps, Trim, Runway }
    public enum TrainingCue { None, RollLeft, RollRight, RolloutLeft, RolloutRight, TurnLeft, TurnRight,
        AltitudeLow, AltitudeHigh, SpeedLow, SpeedHigh, RudderLeft, RudderRight, TaxiFast, Stop,
        Power, Brakes, Yoke, Rudder, Trim, Flaps, AlignRunway, Approach, GoAround, Recovered, RollCheckLeft, RollCheckRight, RollCheckCenter, PitchCheckForward, PitchCheckBack, PitchCheckCenter, RudderCheckLeft, RudderCheckRight, RudderCheckCenter }
    [Serializable] public struct TrainingCoachLine
    { public TrainingCue cue; public TrainingReference reference; public string text; public AudioClip voice; }
    [Serializable] public struct TrainingReferenceMarker
    { public TrainingReference reference; public GameObject marker; [TextArea] public string explanation; public AudioClip voice; }

    /// <summary>Finite, phase-specific coaching. No AI, flight authority, or runtime hierarchy creation.</summary>
    public sealed class TrainingCoach : MonoBehaviour
    {
        [SerializeField] private ProductTrainingController training;
        [SerializeField] private FlightDynamicsCoordinator flight;
        [SerializeField] private MonoBehaviour instrumentProvider;
        [SerializeField] private TrainingCoachLine[] lines;
        [SerializeField] private TrainingReferenceMarker[] markers;
        [SerializeField] private Transform worldMarker;
        [SerializeField] private Transform worldMarkerPulse;
        public void ConfigureWorldMarker(Transform value, Transform pulse = null)
        { worldMarker=value; worldMarkerPulse=pulse; }
        private float nextWorldRefresh;
        private TrainingCue candidate, lastSpoken;
        private float observedSince, nextSpeech, repeatAfter, nextSample, hideAt;
        private int lastStage=-1;
        private float lastHeading, turnRate;
        private bool hadError;
        private bool inspecting;
        public void ShowForInspection(TrainingReference reference){inspecting=true;Show(reference,3600);}
        public void EndInspection(){if(inspecting){inspecting=false;Hide();}}
        public TrainingReference CurrentReference { get; private set; }
        public void Configure(ProductTrainingController controller, FlightDynamicsCoordinator authority, MonoBehaviour instruments,
            TrainingCoachLine[] authoredLines, TrainingReferenceMarker[] authoredMarkers)
        { training=controller; flight=authority; instrumentProvider=instruments; lines=authoredLines; markers=authoredMarkers; }

        public bool TryIndications(out CockpitInstrumentV2Snapshot indication)
        { indication=default; return instrumentProvider is ICockpitInstrumentV2AuthorityProvider p && p.TryGetInstrumentV2Snapshot(out indication); }
        public void Show(TrainingReference reference, float seconds=8)
        {
            CurrentReference=reference; hideAt=Time.unscaledTime+seconds;
            foreach(var marker in markers) if(marker.marker!=null) marker.marker.SetActive(marker.reference==reference);
            RefreshWorldMarker();
        }
        private void RefreshWorldMarker()
        {
            if(worldMarker==null) return;
            bool flying=training.State==TrainingSessionState.Practice||training.State==TrainingSessionState.Demonstration;
            bool visible=!training.WithoutPrompts && (flying
                ? training.CurrentStage.reference==TrainingReference.Runway
                : CurrentReference==TrainingReference.Runway && Time.unscaledTime<=hideAt &&
                  (training.State==TrainingSessionState.Briefing||training.State==TrainingSessionState.Paused));
            if(visible)
            {
                var target=training.CurrentStage.targetEnu;
                if(target==Vector2.zero) target=new Vector2(-470,-30);
                visible=flight.terrainSurface.TrySample(target.x,target.y,out var terrain);
                if(visible) worldMarker.position=new Vector3(target.x,terrain.heightMeters+.15f,target.y);
                worldMarker.localScale=Vector3.one*(training.CurrentStage.task==TrainingTask.Taxi||training.CurrentStage.task==TrainingTask.Stop?1:5);
            }
            if(worldMarker.gameObject.activeSelf!=visible) worldMarker.gameObject.SetActive(visible);
        }
        public void Hide() { foreach(var marker in markers) if(marker.marker!=null) marker.marker.SetActive(false); if(worldMarker!=null) worldMarker.gameObject.SetActive(false); }
        public string Explain(TrainingReference reference)
        { foreach(var marker in markers) if(marker.reference==reference) return marker.explanation; return "Use the outside horizon and keep scanning for traffic."; }
        public AudioClip ExplanationVoice(TrainingReference reference)
        { foreach(var marker in markers) if(marker.reference==reference) return marker.voice; return null; }
        public void ResetObservation()
        { candidate=lastSpoken=TrainingCue.None; nextSpeech=repeatAfter=0; lastStage=-1; hadError=false; turnRate=0; Hide(); }

        private void Update()
        {
            if(training==null || flight==null) return;
            if(training.IsSoaringProgram){Hide();return;}
            if(inspecting)return;
            if(training.State==TrainingSessionState.Inactive) { Hide(); return; }
            if(Time.unscaledTime>hideAt)
                foreach(var marker in markers) if(marker.marker!=null) marker.marker.SetActive(false);
            // Outdoor objectives persist independently of brief cockpit-reference cues.
            if(Time.unscaledTime>=nextWorldRefresh) { nextWorldRefresh=Time.unscaledTime+.1f; RefreshWorldMarker(); }
            if(worldMarkerPulse!=null && worldMarker.gameObject.activeSelf)
                worldMarkerPulse.localPosition=new Vector3(0,2.5f+1.25f*Mathf.Sin(Time.unscaledTime*1.2f),0);
            if(training.State!=TrainingSessionState.Practice || Time.timeScale<=0 || Time.unscaledTime<nextSample) return;
            nextSample=Time.unscaledTime+.1f;
            var state=flight.CurrentState;
            if(!state.IsFinite) return;
            if(lastStage!=training.StageIndex)
            {
                lastStage=training.StageIndex; candidate=TrainingCue.None; hadError=false;
                observedSince=Time.unscaledTime; nextSpeech=Time.unscaledTime+3; lastHeading=(float)state.headingDegrees;
                if(!training.WithoutPrompts) Show(training.CurrentStage.reference);
            }
            float delta=Mathf.DeltaAngle(lastHeading,(float)state.headingDegrees);
            turnRate=Mathf.Lerp(turnRate,delta/.1f,.25f); lastHeading=(float)state.headingDegrees;
            if(!TryIndications(out var indicated)) return;
            var cue=Evaluate(training.CurrentStage,state,flight.LastAppliedControls,indicated,turnRate);
            bool controlCheck=training.Evaluator.IsControlCheck(training.CurrentStage.task);
            if(controlCheck)
            {
                if(training.Evaluator.Complete) return;
                var first=training.CurrentStage.task==TrainingTask.RollControl?TrainingCue.RollCheckLeft:training.CurrentStage.task==TrainingTask.PitchControl?TrainingCue.PitchCheckForward:TrainingCue.RudderCheckLeft;
                cue=(TrainingCue)((int)first+training.Evaluator.ControlPhase);
                if(!state.weightOnWheels||state.groundSpeedKnots>=1||flight.LastAppliedControls.throttle>=.2f)cue=TrainingCue.Brakes;
            }
            bool urgent=cue==TrainingCue.GoAround;
            bool transition=cue==TrainingCue.RolloutLeft||cue==TrainingCue.RolloutRight;
            if(cue!=candidate) { candidate=cue; observedSince=Time.unscaledTime; }
            if(cue==TrainingCue.None)
            {
                if(hadError && !training.WithoutPrompts && Time.unscaledTime-observedSince>2 && !training.VoicePlaying)
                { Speak(TrainingCue.Recovered,false); hadError=false; }
                return;
            }
            if(training.WithoutPrompts && !urgent) return;
            if((!urgent&&!transition&&Time.unscaledTime-observedSince<1.5f) ||
               (!urgent&&!transition&&Time.unscaledTime<nextSpeech) ||
               (lastSpoken==cue&&Time.unscaledTime<repeatAfter) ||
               (!urgent&&!transition&&training.VoicePlaying)) return;
            Speak(cue,urgent||transition); hadError=!urgent&&!transition;
        }
        private void Speak(TrainingCue cue,bool interrupt)
        {
            foreach(var line in lines)
                if(line.cue==cue)
                {
                    training.PresentCoachCue(line.text,line.voice,interrupt);
                    if(cue!=TrainingCue.Recovered)training.RecordCoaching(cue);
                    Show(line.reference); lastSpoken=cue;
                    nextSpeech=Time.unscaledTime+Mathf.Max(4,line.voice!=null?line.voice.length+3:5);
                    repeatAfter=Time.unscaledTime+Mathf.Max(8,line.voice!=null?line.voice.length+4:8);
                    return;
                }
        }

        public static TrainingCue Evaluate(TrainingStage stage,in FlightDynamicsState s,in FlightDynamicsControlSnapshot c,
            in CockpitInstrumentV2Snapshot indicated,float turnRate)
        {
            if(!s.IsFinite || !indicated.isValid) return TrainingCue.None;
            float speed=indicated.indicatedAirspeedKnots, bank=(float)s.bankDegrees;
            float heading=Mathf.DeltaAngle((float)s.headingDegrees,stage.heading);
            bool approach=stage.task==TrainingTask.Final||stage.task==TrainingTask.Landing;
            if(approach && s.altitudeAglMeters<91.44 && s.altitudeAglMeters>12 &&
               (speed<55||speed>80||s.verticalSpeedFeetPerMinute< -800||Mathf.Abs(bank)>20||Mathf.Abs(s.positionUnityMeters.z+30)>65))
                return TrainingCue.GoAround;
            if(s.weightOnWheels)
            {
                if((stage.task==TrainingTask.Taxi||stage.task==TrainingTask.Stop) && s.groundSpeedKnots>8) return TrainingCue.TaxiFast;
                if(stage.task==TrainingTask.Stop && s.groundSpeedKnots>.7) return TrainingCue.Stop;
                if(stage.task==TrainingTask.BrakeControl && (c.leftToeBrake<.35f||c.rightToeBrake<.35f)) return TrainingCue.Brakes;
                // Control-check coaching is selected from observed movement progress in Update.

                if(stage.task==TrainingTask.TakeoffRoll && c.throttle<.9f) return TrainingCue.Power;
                if(stage.task==TrainingTask.Taxi && Mathf.Abs(heading)>18) return heading>0?TrainingCue.TurnRight:TrainingCue.TurnLeft;
                return TrainingCue.None;
            }
            bool turning=stage.task==TrainingTask.Turn||stage.task==TrainingTask.Base;
            float bankLimit=turning?22:12;
            if(Mathf.Abs(bank)>bankLimit) return bank>0?TrainingCue.RollLeft:TrainingCue.RollRight;
            if(turning && Mathf.Abs(bank)>5 && Mathf.Abs(heading)<Mathf.Clamp(Mathf.Abs(turnRate)*3,5,12))
                return bank>0?TrainingCue.RolloutLeft:TrainingCue.RolloutRight;
            if((turning||stage.task==TrainingTask.Level||stage.task==TrainingTask.Downwind) && !stage.attitudeOnly &&
                (stage.task!=TrainingTask.Level||stage.reference==TrainingReference.Heading||stage.reference==TrainingReference.Outside) &&
                Mathf.Abs(heading)>(stage.task==TrainingTask.Turn?8:12) && (Mathf.Abs(bank)<3||Mathf.Sign(bank)!=Mathf.Sign(heading)))
                return heading>0?TrainingCue.TurnRight:TrainingCue.TurnLeft;
            if(speed<Mathf.Max(55,stage.speedKnots-15)) return TrainingCue.SpeedLow;
            if(speed>stage.speedKnots+20 && !turning) return TrainingCue.SpeedHigh;
            if(Mathf.Abs(indicated.slipSkidNormalized)>.4f && indicated.Has(CockpitInstrumentV2Availability.TurnAndSlip))
                return indicated.slipSkidNormalized>0?TrainingCue.RudderRight:TrainingCue.RudderLeft;
            if((stage.task==TrainingTask.Level||stage.task==TrainingTask.Turn||stage.task==TrainingTask.Trim||stage.task==TrainingTask.Downwind) && !stage.attitudeOnly &&
                stage.reference!=TrainingReference.Airspeed && stage.reference!=TrainingReference.Heading &&
                CockpitInstrumentV2Kinematics.TryEvaluateIndicatedAltitudeFeet(indicated,out float altitude))
            {
                if(altitude<stage.altitudeFeet-150 && indicated.indicatedVerticalSpeedFeetPerMinute<50) return TrainingCue.AltitudeLow;
                if(altitude>stage.altitudeFeet+150 && indicated.indicatedVerticalSpeedFeetPerMinute> -50) return TrainingCue.AltitudeHigh;
            }
            if(stage.task==TrainingTask.Trim && Mathf.Abs(c.elevator)>.18f) return TrainingCue.Trim;
            if(stage.task==TrainingTask.Climb||stage.task==TrainingTask.Departure)
            { if(c.throttle<.8f && s.verticalSpeedFeetPerMinute<100) return TrainingCue.Power; }
            if(approach && Mathf.Abs(s.positionUnityMeters.z+30)>35) return TrainingCue.AlignRunway;
            if(stage.task==TrainingTask.GoAroundClimb && c.flaps>.38f && s.verticalSpeedFeetPerMinute>150) return TrainingCue.Flaps;
            return TrainingCue.None;
        }
    }
}
