using UnityEngine;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Flight.Soaring;
using QuestFlightLab.Runtime;
using QuestFlightLab.Input;
using QuestFlightLab.Training.FlightLessonV1;

namespace QuestFlight.Product
{
    public sealed partial class ProductDemoPilot
    {
        [SerializeField] private SoaringDemoProfile soaringProfile;
        private readonly SoaringDemoGuidance soaringGuidance=new SoaringDemoGuidance();
        private bool soaringActive;
        private double soaringLastTime;
        public bool IsSoaringDemo=>soaringActive;
        public SoaringDemoGuidance SoaringGuidance=>soaringGuidance;
        public SoaringDemoProfile SoaringProfile=>soaringProfile;
        public void ConfigureSoaringDemo(SoaringDemoProfile value)=>soaringProfile=value;
        private static readonly string[] SoaringPhaseIds={"sgs_preparation","sgs_tow","sgs_release","sgs_thermal","sgs_sightseeing","sgs_descent","sgs_circuit","sgs_final","sgs_complete","sgs_held"};
        public string SoaringPresentationPhase=>UserHasControls?"Your controls":NeedsAssistance?soaringGuidance.Failure:soaringGuidance.Phase switch
        {
            SoaringDemoGuidance.Stage.Preparation=>"Preparing the ground launch",
            SoaringDemoGuidance.Stage.Tow=>"Following the towplane",
            SoaringDemoGuidance.Stage.Release=>"Tow released · quiet glide",
            SoaringDemoGuidance.Stage.Thermal=>"Circling in rising air",
            SoaringDemoGuidance.Stage.Sightseeing=>"Boulder sightseeing",
            SoaringDemoGuidance.Stage.Descent=>"Returning to the airport",
            SoaringDemoGuidance.Stage.Circuit=>"Unpowered landing circuit",
            SoaringDemoGuidance.Stage.Final=>"Approach and landing",
            SoaringDemoGuidance.Stage.Complete=>"Landed and stopped · open Menu",
            _=>"Demonstration paused"
        };
        public bool StartSoaringDemo(bool shortFlight)
        {
            if(Time.timeScale>0||!HasAuthoredSources||soaringProfile==null||!soaringProfile.IsValid||!flightCoordinator.aircraftDefinition.IsGlider)return false;
            StopDemo();productInput.ReleaseAllTouchOverrides();
            var f=flightCoordinator;
            if(shortFlight)
            {
                var p=soaringProfile.airborneStart;
                if(!f.terrainSurface.TrySample(p.x,p.z,out var ground))return false;
                var geo=FlightFrameConversions.UnityToGeodetic(p,GeodeticReference.Kbdu);
                var initial=f.aircraftDefinition.airborneReference;
                initial.latitudeDegrees=geo.latitudeDegrees;initial.longitudeDegrees=geo.longitudeDegrees;initial.altitudeMslMeters=geo.altitudeMslMeters;
                initial.terrainElevationMslMeters=GeodeticReference.Kbdu.altitudeMslMeters+ground.heightMeters;initial.headingDegrees=soaringProfile.airborneHeading;
                initial.calibratedAirspeedKnots=soaringProfile.airborneSpeed;initial.engineRunning=false;initial.pitchDegrees=2;
                f.soaringWeatherDefinition=soaringProfile.operation.weather;
                if(!f.TryPrepareAircraft(f.aircraftDefinition,initial))return false;
            }
            else if(!f.TryPrepareSoaringOperation(soaringProfile.operation))return false;
            soaringGuidance.Begin(soaringProfile,shortFlight);soaringActive=true;IsRunning=true;ShortShowcase=shortFlight;
            UserHasControls=NeedsAssistance=HadRecovery=_guidanceSuppressed=false;soaringLastTime=f.CurrentState.simulationTimeSeconds;
            Phase=SoaringPhaseIds[(int)soaringGuidance.Phase];_scheduledControls.SetGliderNeutral();_appliedControls.SetGliderNeutral();
            _appliedControls.trim=f.aircraftDefinition.referenceTrimNoseUp;_appliedControls.airbrake=_appliedControls.wheelBrake=shortFlight?0:1;
            productInput.SetDeterministicMode(true);productInput.DemoOwnsControls=true;productInput.SetFlightLessonInputSuppressed(false);
            deterministicInput.SetControls(_appliedControls);productInput.SetFlightLessonGuidance(true,ProductControlInput.ToFlightLessonCommand(_appliedControls));
            return true;
        }
        private void UpdateSoaringDemo()
        {
            if(!IsRunning||Time.timeScale<=0||_guidanceSuppressed)return;
            if(NeedsAssistance){traffic?.flightMode?.AuthoredNavigation?.OpenMenu();return;}
            double now=flightCoordinator.CurrentState.simulationTimeSeconds;float dt=(float)(now-soaringLastTime);soaringLastTime=now;
            if(UserHasControls||dt<=0)return;
            if(EvaluateSoaringDemo(dt,out var command,out var phase))ApplyGuidanceCommand(command,phase);
        }
        public bool EvaluateSoaringDemo(float dt,out FlightLessonControlCommand command,out string phase)
        {
            command=default;phase=Phase;if(!soaringActive||UserHasControls||NeedsAssistance)return false;
            var f=flightCoordinator;
            if(soaringGuidance.Phase==SoaringDemoGuidance.Stage.Preparation&&soaringGuidance.Elapsed>4&&f.CanSkipSoaringPreflight(out _))
            {
                float old=Time.timeScale;Time.timeScale=0;
                try{if(!f.TrySkipSoaringPreflight())soaringGuidance.Hold("Crew preparation failed. Restart the demonstration.");}
                finally{Time.timeScale=old;}
            }
            if(soaringGuidance.Phase==SoaringDemoGuidance.Stage.Preparation&&f.Operation?.CrewReady==true)f.TryClearSoaringLaunch();
            soaringGuidance.Step(f,dt,_scheduledControls);
            if(soaringGuidance.ReleaseRequested){productInput.SampleTowRelease(false,true);productInput.SampleTowRelease(true,true);}
            command=ProductControlInput.ToFlightLessonCommand(_scheduledControls);
            phase=SoaringPhaseIds[(int)soaringGuidance.Phase];Phase=phase;
            if(soaringGuidance.Phase==SoaringDemoGuidance.Stage.Held){NeedsAssistance=true;Time.timeScale=0;traffic?.flightMode?.AuthoredNavigation?.OpenMenu();return false;}
            return true;
        }
        private bool ResumeSoaringDemo(in FlightDynamicsState s,in FlightLessonControlCommand applied,bool returnNow)
        {
            if(flightCoordinator.TowSession?.RopeConnected==true){Phase="Finish the attached tow before requesting a return.";return false;}
            if(!UserHasControls&&!NeedsAssistance)
            {
                if(soaringGuidance.Phase<SoaringDemoGuidance.Stage.Release||soaringGuidance.Phase>=SoaringDemoGuidance.Stage.Complete){Phase="This phase cannot return automatically. Restart or end the tour.";return false;}
                if(returnNow)soaringGuidance.ReturnEarly();return true;
            }
            var margin=SoaringReturnPlanner.Evaluate(soaringProfile.operation,flightCoordinator.terrainSurface,flightCoordinator.SoaringAtmosphere,s,flightCoordinator.NativeDiagnostics.weightPounds*.45359237);
            if(!soaringGuidance.Resume(s,margin,returnNow)){Phase="Cannot safely resume here. Keep control, restart or end flight.";return false;}
            ProductControlInput.CopyFlightLessonCommandInto(applied,_appliedControls);
            UserHasControls=NeedsAssistance=_guidanceSuppressed=false;soaringLastTime=s.simulationTimeSeconds;
            productInput.SetDeterministicMode(true);productInput.DemoOwnsControls=true;productInput.SetFlightLessonGuidance(true,ProductControlInput.ToFlightLessonCommand(_appliedControls));return true;
        }
    }
}
