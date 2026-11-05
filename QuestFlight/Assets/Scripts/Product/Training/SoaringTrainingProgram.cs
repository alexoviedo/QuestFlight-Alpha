using System;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;
using QuestFlightLab.Training.FlightLessonV1;
using UnityEngine;

namespace QuestFlight.Product.Training
{
    // Program-specific policy. No powered evaluator, crash recovery or guidance
    // is called from this adapter. Both programs share the existing UI/progress.
    public sealed class SoaringTrainingProgram
    {
        public bool Complete {get;private set;}
        public bool TimedOut {get;private set;}
        public bool Assisted {get;private set;}
        public string Failure {get;private set;}
        public string Feedback {get;private set;}="";
        public float HeldSeconds {get;private set;}
        public bool Landed {get;private set;}
        public bool UnsafeFlight {get;private set;}
        public bool SlipReadyForRecovery=>slipObserved;
        private float elapsed,startTrim,previousSink,headingSweep,previousHeading,slipSeconds,startHeading,turnDirection;
        private double startEnergy;
        private bool hadAirborne,previousWheels,trimMoved,slipObserved,wasConnected,releaseInArea;
        public bool Prepare(TrainingLessonDefinition lesson,FlightDynamicsCoordinator flight)
        {
            if(lesson.program!=TrainingProgramKind.Sgs233Reference||!flight.aircraftDefinition.IsGlider||flight.aircraftDefinition.Powered||lesson.soaringWeather==null)return false;
            if(lesson.groundTowPreparation)return flight.soaringOperations.Length>0 && flight.TryPrepareSoaringOperation(flight.soaringOperations[0]);
            flight.soaringWeatherDefinition=lesson.soaringWeather;
            var initial=lesson.start;initial.engineRunning=false;
            return flight.TryPrepareAircraft(flight.aircraftDefinition,initial);
        }
        public void BeginAttempt(in FlightDynamicsState state)
        {Assisted=Landed=releaseInArea=UnsafeFlight=false;wasConnected=false;Failure=null;hadAirborne=!state.weightOnWheels&&state.altitudeAglMeters>3;previousWheels=state.weightOnWheels;previousSink=0;}
        public void BeginStage(in FlightDynamicsState state,float trim,FlightDynamicsCoordinator flight)
        {
            Complete=TimedOut=trimMoved=slipObserved=false;HeldSeconds=elapsed=headingSweep=slipSeconds=turnDirection=0;startHeading=(float)state.headingDegrees;startTrim=trim;
            previousHeading=(float)state.headingDegrees;startEnergy=Energy(flight);Feedback="Follow the reference instruction; all motion comes from native flight dynamics.";
        }
        public void MarkAssisted()=>Assisted=true;
        private static double Energy(FlightDynamicsCoordinator flight)
        {double tas=flight.NativeDiagnostics.trueAirspeedKnots*.514444;return flight.SoaringInstruments.PressureAltitudeFeet*.3048+tas*tas/(2*9.80665);}
        public void ObserveNative(in FlightDynamicsState s,FlightDynamicsCoordinator flight)
        {
            if(!s.IsFinite||s.engineRpm!=0){UnsafeFlight=true;Failure="Native glider data invalid or unexpected propulsion. Exercise stopped.";return;}
            bool connected=flight.TowSession!=null&&flight.TowSession.RopeConnected;
            if(wasConnected&&!connected)releaseInArea=flight.Operation!=null&&flight.Operation.Definition.ReleaseAvailable(s)&&flight.TowSession.RopeState==TowlineState.ReleasedByGlider;
            wasConnected=connected;
            if(!s.weightOnWheels&&s.altitudeAglMeters>3)hadAirborne=true;
            var operation=flight.soaringOperations.Length>0?flight.soaringOperations[0]:null;
            if(operation==null||!operation.Contains(s.positionUnityMeters)){UnsafeFlight=true;Failure="Outside the supported development area. No recovery relocation was applied.";return;}
            if(hadAirborne&&s.weightOnWheels&&!previousWheels)
            {
                Landed=true;
                if(previousSink<-600||Math.Abs(s.bankDegrees)>10||!operation.OnLandingSurface(s.positionUnityMeters)){UnsafeFlight=true;Failure="Unsafe or off-runway touchdown. Review the unpowered approach and retry from an explicit checkpoint.";}
            }
            if(hadAirborne&&!s.weightOnWheels&&s.altitudeAglMeters<25 && (Math.Abs(s.bankDegrees)>50||s.calibratedAirspeedKnots<25)){UnsafeFlight=true;Failure="Control lost near terrain. Exercise paused; no universal turnback or powered escape is prescribed.";}
            if(flight.Operation!=null&&flight.Operation.RequiresPause){UnsafeFlight=true;Failure=flight.Operation.StopReason;}
            previousWheels=s.weightOnWheels;previousSink=(float)s.verticalSpeedFeetPerMinute;
        }
        public void Step(TrainingStage stage,in FlightDynamicsState s,in FlightDynamicsControlSnapshot c,float dt,FlightDynamicsCoordinator flight)
        {
            if(Complete||dt<=0||dt>.25f||!string.IsNullOrEmpty(Failure))return;
            elapsed+=dt;bool good=false;var pair=flight.TowSession;var op=flight.Operation;
            if(op!=null&&op.PreflightSkipped)Assisted=true;
            bool airborne=!s.weightOnWheels&&s.altitudeAglMeters>3;
            bool coordinated=Math.Abs(s.sideslipDegrees)<6;
            bool speed=s.calibratedAirspeedKnots>=35&&s.calibratedAirspeedKnots<=65;
            var flow=flight.SampleAtmosphereComponents(s.positionUnityMeters,s.simulationTimeSeconds);
            float heading=(float)s.headingDegrees,change=Mathf.DeltaAngle(previousHeading,heading);previousHeading=heading;
            bool attached=pair!=null&&pair.RopeConnected;
            if(Landed && stage.task!=TrainingTask.SoaringLanding && stage.task!=TrainingTask.SoaringStop)
            {Failure="Landed before the current airborne objective completed. Review the result, restart the lesson or end the session.";return;}
            if((stage.task==TrainingTask.SoaringTowPosition || stage.task==TrainingTask.SoaringLaunch) &&
                ((airborne&&!attached)||(op!=null&&op.Phase==SoaringOperationPhase.Aborted)))
            {Failure="Tow ended before the attached-flight objective completed. Keep a safe unpowered landing plan; restart for another tow attempt.";return;}
            if(stage.task==TrainingTask.SoaringRelease && pair!=null&&!attached&&!releaseInArea)
            {Failure="Tow separated outside the reference release condition. No airborne reconnect is available. Continue manually if safe or restart.";return;}
            if(stage.task==TrainingTask.SoaringGroundAbort && airborne)
            {Failure="Aircraft became airborne before the ground-abort task completed. This exercise does not score an airborne turnback. Continue manually if safe or restart.";return;}
            if((stage.task==TrainingTask.SoaringDownwind||stage.task==TrainingTask.SoaringBase||stage.task==TrainingTask.SoaringFinal)&&!s.weightOnWheels&&s.altitudeAglMeters<25)
            {Failure="Approach segment missed before reaching the low-height margin. Keep control; review the unpowered circuit and retry an explicit checkpoint.";return;}
            switch(stage.task)
            {
                case TrainingTask.SoaringPreparation:
                    if(op!=null&&op.PreflightSkipped)Assisted=true;
                    good=op!=null&&op.PreflightSatisfied;break;
                case TrainingTask.SoaringCrewReady:good=op!=null&&op.CrewReady&&c.airbrake<.03f&&c.wheelBrake<.03f;break;
                case TrainingTask.SoaringLaunch:good=op!=null&&op.LaunchAuthorized&&airborne&&pair!=null&&!pair.TugState.weightOnWheels&&s.altitudeAglMeters>10;break;
                case TrainingTask.SoaringTowPosition:
                    if(pair!=null)
                    {
                        Vector3 separation=pair.TugState.positionUnityMeters-s.positionUnityMeters;
                        Vector3 forward=pair.TugState.velocityUnityMetersPerSecond;forward.y=0;forward.Normalize();
                        float side=Vector3.Dot(separation,new Vector3(forward.z,0,-forward.x));
                        float climb=pair.TugState.velocityUnityMetersPerSecond.y;
                        float behindHeight=pair.TugState.positionUnityMeters.y-climb/Mathf.Max(15,(float)pair.TugState.groundSpeedKnots*.514444f)*66;
                        good=airborne&&pair.Line.state==TowlineState.Taut&&pair.Line.tensionNewtons<3000&&Mathf.Abs(side)<15&&Mathf.Abs(s.positionUnityMeters.y-behindHeight)<10&&coordinated&&speed&&Math.Abs(s.bankDegrees)<30;
                    }
                    break;
                case TrainingTask.SoaringRelease:good=releaseInArea&&pair!=null&&pair.Line.state==TowlineState.ReleasedByGlider&&airborne&&speed&&Vector3.Distance(s.positionUnityMeters,pair.TugState.positionUnityMeters)>65;break;
                case TrainingTask.SoaringSpeed:good=airborne&&Math.Abs(s.calibratedAirspeedKnots-stage.speedKnots)<3&&coordinated&&Math.Abs(s.bankDegrees)<10;break;
                case TrainingTask.SoaringTrim:trimMoved|=Mathf.Abs(c.trim-startTrim)>.02f;good=airborne&&speed&&trimMoved&&Mathf.Abs(c.elevator)<.12f&&coordinated;break;
                case TrainingTask.SoaringAirbrake:good=airborne&&speed&&c.airbrake>.5f&&s.verticalSpeedFeetPerMinute<-300&&Math.Abs(s.bankDegrees)<15;break;
                case TrainingTask.SoaringSlip:
                    bool inSlip=airborne&&speed&&c.airbrake<.03f&&Math.Abs(s.sideslipDegrees)>=4&&Math.Abs(s.sideslipDegrees)<12&&Mathf.Abs(c.rudder)>.2f&&Math.Abs(s.bankDegrees)<20;
                    slipSeconds=inSlip?slipSeconds+dt:0;slipObserved|=slipSeconds>=stage.dwellSeconds;
                    good=slipObserved&&airborne&&speed&&c.airbrake<.03f&&Math.Abs(s.sideslipDegrees)<3&&Math.Abs(s.bankDegrees)<10;
                    break;
                case TrainingTask.SoaringLift:good=airborne&&speed&&flow.thermal>.3f&&flight.SoaringInstruments.IdealTotalEnergyFpm>60;break;
                case TrainingTask.SoaringEnergyGain:
                    if(airborne&&speed&&coordinated&&flow.thermal>.3f&&Math.Abs(s.bankDegrees)>5)
                    {if(turnDirection==0&&Mathf.Abs(change)>.01f)turnDirection=Mathf.Sign(change);headingSweep+=change*turnDirection;}
                    else {headingSweep=turnDirection=0;startEnergy=Energy(flight);}
                    good=headingSweep>=270&&airborne&&speed&&coordinated&&flow.thermal>.3f&&Energy(flight)>=startEnergy+20;break;
                case TrainingTask.SoaringLiftExit:good=airborne&&speed&&flow.thermal<.05f&&Math.Abs(s.bankDegrees)<12;break;
                case TrainingTask.SoaringRidgeTrack:good=airborne&&speed&&coordinated&&flow.terrain>.5f&&s.altitudeAglMeters>=60&&s.positionUnityMeters.x>SoaringLessonReference.RidgeBoundary;break;
                case TrainingTask.SoaringOutwardTurn:
                    // North-side development ridge has rising terrain to west;
                    // the turn is observed through the east-facing half-circle.
                    if(heading>10&&heading<170&&s.velocityUnityMetersPerSecond.x>0&&s.altitudeAglMeters>=60)
                    {if(turnDirection==0&&Mathf.Abs(change)>.01f)turnDirection=Mathf.Sign(change);headingSweep=Mathf.Max(0,headingSweep+change*turnDirection);}
                    good=headingSweep>=140&&Mathf.Abs(Mathf.DeltaAngle(startHeading,heading))>=140&&speed&&s.altitudeAglMeters>=60;break;
                case TrainingTask.SoaringRidgeExit:good=airborne&&speed&&s.positionUnityMeters.x>SoaringLessonReference.RidgeExit&&s.velocityUnityMetersPerSecond.x>2&&s.altitudeAglMeters>=80;break;
                case TrainingTask.SoaringDownwind:good=airborne&&s.positionUnityMeters.z>SoaringLessonReference.DownwindSouth&&s.positionUnityMeters.z<SoaringLessonReference.DownwindNorth&&s.positionUnityMeters.x<0&&Mathf.Abs(Mathf.DeltaAngle(heading,270))<20&&s.positionUnityMeters.y>180&&speed;break;
                case TrainingTask.SoaringBase:good=airborne&&s.positionUnityMeters.x<SoaringLessonReference.BaseEast&&s.positionUnityMeters.x>SoaringLessonReference.BaseWest&&s.positionUnityMeters.z>0&&s.positionUnityMeters.z<700&&Mathf.Abs(Mathf.DeltaAngle(heading,180))<25&&speed;break;
                case TrainingTask.SoaringFinal:good=airborne&&s.positionUnityMeters.x<-600&&Mathf.Abs(s.positionUnityMeters.z+30)<50&&Mathf.Abs(Mathf.DeltaAngle(heading,90))<12&&s.calibratedAirspeedKnots>=42&&s.calibratedAirspeedKnots<=58&&s.verticalSpeedFeetPerMinute>-850;break;
                case TrainingTask.SoaringLanding:good=Landed&&s.weightOnWheels&&string.IsNullOrEmpty(Failure);break;
                case TrainingTask.SoaringStop:good=Landed&&s.weightOnWheels&&s.groundSpeedKnots<1&&c.wheelBrake>.5f;break;
                case TrainingTask.SoaringGroundAbort:good=pair!=null&&pair.LaunchPhase==GroundLaunchPhase.Aborted&&s.weightOnWheels&&s.groundSpeedKnots<1&&c.wheelBrake>.8f;break;
                default:Failure="Unsupported task in the unpowered training catalog.";break;
            }
            HeldSeconds=good?HeldSeconds+dt:0;Complete=HeldSeconds>=stage.dwellSeconds;TimedOut=elapsed>=stage.timeoutSeconds;
            // Static feedback strings keep the native sampling path allocation-free.
            Feedback=stage.task==TrainingTask.SoaringSlip&&slipObserved?"Slip recorded. Recover to centered yaw string, nearly level wings and closed airbrakes.":good?"Within the practice window. Hold the condition.":stage.retryAdvice;
        }
        public FlightLessonControlCommand Assist(in FlightDynamicsState s,float heading,float trim,bool demonstration,float time)
        {
            float targetHeading=heading+(demonstration&&time>12?-30:0);
            float bank=Mathf.Clamp(Mathf.DeltaAngle((float)s.headingDegrees,targetHeading),-20,20);
            float pitch=.2f+Mathf.Clamp(((float)s.calibratedAirspeedKnots-50)*.6f,-8,8);
            return new FlightLessonControlCommand{aileron=Mathf.Clamp(.06f*(bank-(float)s.bankDegrees)+.4f*s.angularVelocityBodyDegreesPerSecond.z*Mathf.Deg2Rad,-.6f,.6f),
                elevator=Mathf.Clamp(.08f*(pitch-(float)s.pitchDegrees)+.8f*s.angularVelocityBodyDegreesPerSecond.x*Mathf.Deg2Rad,-.65f,.65f),
                rudder=Mathf.Clamp(.04f*(float)s.sideslipDegrees-.2f*s.angularVelocityBodyDegreesPerSecond.y*Mathf.Deg2Rad,-.4f,.4f),trim=trim};
        }
    }
}
