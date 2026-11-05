using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Flight.Soaring
{
    public enum SoaringOperationPhase { ControlsCheck, CrewRehook, Ready, Launch, Tow, ReleaseReady, Soaring, Return, Final, Landed, Aborted }
    // Pure state owned and observed on the existing native coordinator clock.
    public sealed class SoaringOperationSession
    {
        public readonly BoulderSoaringOperationDefinition Definition;
        public SoaringOperationPhase Phase {get;private set;}
        public int CheckedControls {get;private set;}
        public bool ReleaseChecked {get;private set;}
        public bool CrewReady {get;private set;}
        public string StopReason {get;private set;}
        public bool RequiresPause {get;private set;}
        public SoaringReturnEstimate ReturnEstimate {get;private set;}
        public bool PreflightSkipped {get;private set;}
        public bool PreflightComplete=>!PreflightSkipped&&CheckedControls==127&&ReleaseChecked;
        public bool PreflightSatisfied=>PreflightComplete||PreflightSkipped;
        public bool LaunchAuthorized=>Phase>=SoaringOperationPhase.Launch&&Phase<SoaringOperationPhase.Landed;
        private bool hadAirborne,previousWheels;
        private float previousSink,stoppedSeconds;
        private double nextReturn;
        public SoaringOperationSession(BoulderSoaringOperationDefinition definition,float referenceTrim=.10626088f){Definition=definition;Phase=SoaringOperationPhase.ControlsCheck;}
        public string Instruction=>Phase switch
        {
            SoaringOperationPhase.ControlsCheck=>ReleaseChecked?"Release test recorded; rope disconnected. Finish remaining stopped checks, or use Menu > Launch preparation > Skip preflight and prepare launch.":"STOPPED CHECKS · Sweep stick/rudder both ways. Open the combined lever fully. Test the red release once (disconnects rope). Finish checks or skip in Menu > Launch preparation.",
            SoaringOperationPhase.CrewRehook=>"Checks complete. Rope disconnected. Menu > Launch preparation > Reconnect and prepare launch explicitly restages both aircraft; it is not self-taxi.",
            SoaringOperationPhase.Ready=>"Crew prepared. Keep the rope connected. Close the combined brake lever and center stick/rudder, then Menu > Launch preparation > Clear and begin launch. Both strips must be clear.",
            SoaringOperationPhase.Launch=>"YOUR CONTROLS · Wing support ends at 23 kt. Keep the tug aligned; both aircraft lift off physically. A / red handle releases.",
            SoaringOperationPhase.Tow=>"YOUR CONTROLS · Tow reference 55 kt. Follow the tug with small coordinated corrections. Release remains available.",
            SoaringOperationPhase.ReleaseReady=>"REFERENCE RELEASE AREA · Check separation and pull the red handle / A when ready. No automatic release.",
            SoaringOperationPhase.Soaring=>"YOUR CONTROLS · Unpowered soaring. Use airspeed, yaw string and pressure VSI. Menu shows conservative return margin.",
            SoaringOperationPhase.Return=>"RETURN PLANNING · Landing margin is low. Leave lift early and plan an unpowered circuit. No powered go-around is available.",
            SoaringOperationPhase.Final=>"UNPOWERED FINAL · Control speed with pitch and descent with airbrakes. Keep the landing surface clear; there is no engine escape.",
            SoaringOperationPhase.Landed=>"LANDED AND STOPPED · Menu > Flight status offers summary or another ground launch. The glider cannot self-taxi.",
            _=>StopReason??"LAUNCH ABORTED · Keep control and assess the landing area. No universal turnback is prescribed."
        };
        public void ObserveChecks(in FlightDynamicsState s,in FlightDynamicsControlSnapshot c,bool release)
        {
            if(Phase!=SoaringOperationPhase.ControlsCheck||!s.weightOnWheels||s.groundSpeedKnots>1)return;
            if(c.aileron<-.35f)CheckedControls|=1;if(c.aileron>.35f)CheckedControls|=2;
            if(c.elevator<-.35f)CheckedControls|=4;if(c.elevator>.35f)CheckedControls|=8;
            if(c.rudder<-.35f)CheckedControls|=16;if(c.rudder>.35f)CheckedControls|=32;
            if(c.airbrake>.9f&&c.wheelBrake>.8f)CheckedControls|=64;
            ReleaseChecked|=release;
            if(PreflightComplete)Phase=SoaringOperationPhase.CrewRehook;
        }
        public bool TrySkipPreflight(in FlightDynamicsState state)
        {
            if((Phase!=SoaringOperationPhase.ControlsCheck&&Phase!=SoaringOperationPhase.CrewRehook)||!state.IsFinite||!state.weightOnWheels||state.groundSpeedKnots>=1)return false;
            PreflightSkipped=true;Phase=SoaringOperationPhase.CrewRehook;return true;
        }
        public bool ConfirmCrewRehook(){if(!PreflightSatisfied||Phase!=SoaringOperationPhase.CrewRehook)return false;CrewReady=true;Phase=SoaringOperationPhase.Ready;return true;}
        public bool TryClear(bool runwayClear,in FlightDynamicsControlSnapshot controls)
        {
            if(Phase!=SoaringOperationPhase.Ready||!CrewReady||!runwayClear||controls.airbrake>.03f||controls.wheelBrake>.03f||Mathf.Abs(controls.aileron)>.15f||Mathf.Abs(controls.elevator)>.15f||Mathf.Abs(controls.rudder)>.15f)return false;
            Phase=SoaringOperationPhase.Launch;return true;
        }
        public void Abort(string reason,bool pause=false){CrewReady=false;Phase=SoaringOperationPhase.Aborted;StopReason=reason;RequiresPause=pause;}
        public void ReconcileConnection(bool connected,bool tugAborted,in FlightDynamicsState state)
        {
            if(Phase==SoaringOperationPhase.Ready && (!connected||tugAborted))
            {CrewReady=false;Phase=SoaringOperationPhase.CrewRehook;StopReason="Rope disconnected. Reconnect and prepare launch while stopped.";}
            else if(LaunchAuthorized && (!connected || tugAborted) && !hadAirborne && state.weightOnWheels)
                Abort("Ground launch stopped: rope disconnected or tug aborted. Apply the combined lever fully to stop. Menu > Launch preparation offers restart once stopped.");
        }
        public void Observe(in FlightDynamicsState s,AerotowSimulation pair,float dt,SoaringReturnEstimate estimate)
        {
            if(pair!=null)ReconcileConnection(pair.RopeConnected,pair.LaunchPhase==GroundLaunchPhase.Aborted,s);
            if(!LaunchAuthorized && Phase!=SoaringOperationPhase.Landed)return;
            if(!s.IsFinite){Abort("Invalid native state. Flight stopped.",true);return;}
            if(s.engineRpm!=0){Abort("Unexpected propulsion in an unpowered model.",true);return;}
            if(!Definition.Contains(s.positionUnityMeters)){Abort("Outside supported development terrain/route. Flight paused.",true);return;}
            if(!s.weightOnWheels&&s.altitudeAglMeters>3)hadAirborne=true;
            if(hadAirborne&&s.weightOnWheels&&!previousWheels && (previousSink<-600 || Mathf.Abs((float)s.bankDegrees)>10 || !Definition.OnLandingSurface(s.positionUnityMeters)))
                Abort("Unsafe or off-runway touchdown. No recovery movement was applied.",true);
            previousWheels=s.weightOnWheels;previousSink=(float)s.verticalSpeedFeetPerMinute;
            if(Phase==SoaringOperationPhase.Aborted)return;
            bool attached=pair!=null&&pair.RopeConnected;
            if(attached){if(s.altitudeAglMeters>5)Phase=Definition.ReleaseAvailable(s)?SoaringOperationPhase.ReleaseReady:SoaringOperationPhase.Tow;}
            else if(hadAirborne)
            {
                if(s.simulationTimeSeconds>=nextReturn){ReturnEstimate=estimate;nextReturn=s.simulationTimeSeconds+1;}
                Phase=ReturnEstimate.supported && ReturnEstimate.marginMeters<80?SoaringOperationPhase.Return:SoaringOperationPhase.Soaring;
                if(s.altitudeAglMeters<130&&Mathf.Abs(s.positionUnityMeters.z-Definition.centerlineNorth)<160)Phase=SoaringOperationPhase.Final;
                stoppedSeconds=s.weightOnWheels&&s.groundSpeedKnots<1?stoppedSeconds+dt:0;
                if(stoppedSeconds>3&&Definition.OnLandingSurface(s.positionUnityMeters))Phase=SoaringOperationPhase.Landed;
            }
        }
    }
}
