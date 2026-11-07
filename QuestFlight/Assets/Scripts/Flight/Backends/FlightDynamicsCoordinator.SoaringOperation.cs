using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Soaring;
using UnityEngine;

namespace QuestFlightLab.Flight.Backends
{
    public sealed partial class FlightDynamicsCoordinator
    {
        public BoulderSoaringOperationDefinition[] soaringOperations=System.Array.Empty<BoulderSoaringOperationDefinition>();
        public AmbientTrafficDirector soaringTraffic;
        public SoaringOperationSession Operation {get;private set;}
        private double _nextReturnPlan;
        private SoaringReturnEstimate _returnPlan;
        public bool TryPrepareSoaringOperation(BoulderSoaringOperationDefinition operation)
        {
            if(Time.timeScale>0||!aircraftDefinition.IsGlider||operation==null||!operation.IsValid)return false;
            soaringWeatherDefinition=operation.weather;
            if(!TryPrepareAircraft(aircraftDefinition,aircraftDefinition.airborneReference))return false;
            if(!operation.WeatherPermits(SoaringAtmosphere))return false;
            return TryPrepareGroundTow(operation);
        }
        public bool TryCrewReconnect()
        {
            if(Time.timeScale>0||!CanReconnectSoaring(out _))return false;
            var checkedSession=Operation;
            if(!TryPrepareGroundTow(checkedSession.Definition))return false;
            Operation=checkedSession;controls.ConfigureAircraft(aircraftDefinition);
            return Operation.ConfirmCrewRehook();
        }
        public bool TryClearSoaringLaunch()
        {
            if(!CanClearSoaringLaunch(out _))return false;
            if(!Operation.TryClear(soaringTraffic.SoaringRunwayClear(),LastAppliedControls))return false;
            TowSession.SetLaunchClearance(true);return true;
        }
        public bool CanSkipSoaringPreflight(out string reason)
        {
            reason=null;
            if(!IsInitialized||!enabled||RecoveryHold||!_currentState.IsFinite)reason="Aircraft held or unavailable. Restart preparation.";
            else if(Operation==null||(Operation.Phase!=SoaringOperationPhase.ControlsCheck&&Operation.Phase!=SoaringOperationPhase.CrewRehook))reason="Skip preflight is available only before launch preparation is complete.";
            else if(_currentState.groundSpeedKnots>=1)reason="Stop the aircraft before skipping preflight.";
            return reason==null;
        }
        public bool TrySkipSoaringPreflight()=>TrySkipSoaringPreflight(out _);
        public bool TrySkipSoaringPreflight(out string reason)
        {
            reason=null;
            if(Time.timeScale>0){reason="Pause the flight before skipping preflight.";return false;}
            if(!CanSkipSoaringPreflight(out reason))return false;
            var previous=Operation;var stoppedState=_currentState;
            bool connectedPair=TowSession!=null&&!TowSession.Faulted&&TowSession.RopeConnected&&TowSession.LaunchPhase==GroundLaunchPhase.Staging;
            // A fresh launch already has a prepared, connected tow pair. Mark it
            // ready directly; rebuilding the native pair here caused the skip
            // action to fail and return to the same menu. A disconnected pair
            // still uses the explicit crew-restaging path.
            if(connectedPair)
            {
                if(!previous.TrySkipPreflight(stoppedState,true)){reason="The glider must be stopped on its wheels before skipping preflight.";return false;}
                return true;
            }
            if(!TryPrepareGroundTow(previous.Definition))
            {reason=string.IsNullOrEmpty(LastError)?"Crew could not restage the tow pair.":LastError;return false;}
            if(!previous.TrySkipPreflight(stoppedState))
            {Operation=previous;reason="The glider must be stopped on its wheels before skipping preflight.";return false;}
            Operation=previous;controls.ConfigureAircraft(aircraftDefinition);
            if(!Operation.ConfirmCrewRehook()){reason="Crew preparation did not complete. Reconnect the rope and retry.";return false;}
            return true;
        }
        public bool CanReconnectSoaring(out string reason)
        {
            reason=null;
            if(!IsInitialized||!enabled||RecoveryHold||!_currentState.IsFinite)reason="Aircraft is held or unavailable. Restart preparation.";
            else if(Operation==null||!Operation.PreflightSatisfied)reason="Finish the stopped checks, or choose Skip preflight and prepare launch.";
            else if(Operation.Phase!=SoaringOperationPhase.CrewRehook)reason="Reconnection is available only during stopped launch preparation.";
            else if(!_currentState.weightOnWheels||_currentState.groundSpeedKnots>=1)reason="Stop with the combined lever fully applied before crew preparation.";
            return reason==null;
        }
        public bool CanClearSoaringLaunch(out string reason)
        {
            reason=null;var c=LastAppliedControls;
            if(!IsInitialized||!enabled||RecoveryHold||!_currentState.IsFinite)reason="Aircraft unavailable or held. Restart preparation.";
            else if(Operation==null||Operation.Phase!=SoaringOperationPhase.Ready||!Operation.CrewReady)reason="Complete checks and crew preparation first.";
            else if(TowSession==null||TowSession.Faulted||!TowSession.RopeConnected||TowSession.LaunchPhase!=GroundLaunchPhase.Staging)reason="Rope disconnected or tug unavailable. Reconnect or restart preparation.";
            else if(!_currentState.weightOnWheels||_currentState.groundSpeedKnots>=1)reason="Return to cockpit and hold the combined brake while the restaged aircraft settles and stops. Then close brakes, center controls and request clearance.";
            else if(soaringTraffic==null||!soaringTraffic.enabled||!soaringTraffic.trafficEnabled)reason="Traffic checks unavailable. Restart preparation.";
            else if(!soaringTraffic.SoaringRunwayClear())reason="Traffic holds launch. Return to cockpit to wait with brakes applied; the menu pauses traffic too.";
            else if(c.airbrake>.03f||c.wheelBrake>.03f||Mathf.Abs(c.aileron)>.15f||Mathf.Abs(c.elevator)>.15f||Mathf.Abs(c.rudder)>.15f)reason="Return to cockpit, close the combined brake lever and center stick/rudder, then reopen Launch preparation.";
            return reason==null;
        }
        public bool CanAbortSoaringGroundLaunch=>Operation!=null&&_currentState.weightOnWheels&&_currentState.calibratedAirspeedKnots<20&&Operation.LaunchAuthorized;
        public void AbortSoaringLaunch()
        {
            if(Operation==null||TowSession==null)return;
            TowSession.AbortLaunch();Operation.Abort("Launch aborted; tug released. Maintain control and stop or land ahead as conditions permit. No automatic turnback.");
        }
        public bool TryAbortSoaringGroundLaunch()
        {
            if(!CanAbortSoaringGroundLaunch)return false;
            AbortSoaringLaunch();return true;
        }
        private bool UpdateSoaringSafety()
        {
            if(soaringTraffic==null)return Fail("Soaring operation has no authored traffic owner.");
            if(TowSession!=null)
            {
                Operation.ReconcileConnection(TowSession.RopeConnected,TowSession.LaunchPhase==GroundLaunchPhase.Aborted,_currentState);
                if(Operation.Phase==SoaringOperationPhase.Aborted && _currentState.weightOnWheels)TowSession.AbortLaunch();
            }
            bool clear=soaringTraffic.SoaringRunwayClear();
            if(Operation.Phase==SoaringOperationPhase.Launch && !clear)
            {
                if(_currentState.altitudeAglMeters<3)AbortSoaringLaunch();
                else {Operation.Abort("Parallel-runway incursion during departure. Flight paused for debrief.",true);RecoveryHold=true;Time.timeScale=0;return false;}
            }
            bool playerFinal=AmbientTrafficDirector.ProtectsRunway(_currentState.positionUnityMeters,_currentState.velocityUnityMetersPerSecond,_currentState.weightOnWheels);
            TowSession?.SetReturnRunwayClear(clear&&!playerFinal);
            return true;
        }
        private void ObserveSoaringOperation(float dt)
        {
            if(Operation==null)return;
            if(_currentState.simulationTimeSeconds>=_nextReturnPlan)
            {
                _returnPlan=SoaringReturnPlanner.Evaluate(Operation.Definition,terrainSurface,SoaringAtmosphere,_currentState,NativeDiagnostics.weightPounds*.45359237);
                _nextReturnPlan=_currentState.simulationTimeSeconds+1;
            }
            Operation.Observe(_currentState,TowSession,dt,_returnPlan);
            if(Operation.RequiresPause)
            {RecoveryHold=true;Time.timeScale=0;}
        }
    }
}
