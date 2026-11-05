using System;
using AlexOviedo.QuestFlight.JSBSim;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Flight.Soaring
{
    // Owned exclusively by FlightDynamicsCoordinator. No MonoBehaviour, tick,
    // player state, transform authority, or fallback dynamics of its own.
    public sealed class AerotowSimulation : IDisposable
    {
        private readonly JSBSimNativeDevelopmentBackend tug;
        private readonly TowlineSolver rope;
        private readonly TowPilotController pilot=new TowPilotController();
        private readonly SoaringAtmosphereSession atmosphere;
        private readonly TowWakeField wake=new TowWakeField();
        private readonly int substeps;
        private readonly double nativeStep;
        public FlightDynamicsState TugState { get; private set; }
        public JSBSimNativeDiagnostics TugDiagnostics { get; private set; }
        public TowlineSnapshot Line { get; private set; }
        public JSBSimNativeAttachmentState GliderHook { get; private set; }
        public JSBSimNativeAttachmentState TugHook { get; private set; }
        public GroundLaunchPhase LaunchPhase=>pilot.Phase;
        public bool RopeConnected=>rope.Connected;
        public TowlineState RopeState=>rope.State;
        public void ConfigureOperation(BoulderSoaringOperationDefinition operation)=>pilot.ConfigureOperation(operation);
        public void SetLaunchClearance(bool clear)=>pilot.LaunchAuthorized=clear;
        public void SetReturnRunwayClear(bool clear)=>pilot.ReturnRunwayClear=clear;
        public string LastError { get; private set; }=string.Empty;
        public bool Faulted { get; private set; }
        public bool WingSupportActive { get; private set; }
        public int PairSubsteps { get; private set; }
        public double PeakTensionNewtons { get; private set; }
        public double PeakExtensionMeters { get; private set; }
        public double NaturalLengthMeters { get; }
        public bool SwapIntegrationOrderForTest { get; set; }
        public AerotowSimulation(string dataRoot,FlightAircraftDefinition definition,TowlineDefinition line,
            ProductionTerrainSurfaceAsset terrain,in FlightDynamicsInitialConditions initial,int nativeRateHz=120,SoaringAtmosphereSession atmosphereSession=null)
        {
            if(nativeRateHz!=120 && nativeRateHz!=240 && nativeRateHz!=480)throw new ArgumentException("Native pair rate must be fixed at120/240/480Hz before initialization.");
            substeps=nativeRateHz/120;nativeStep=1d/nativeRateHz;
            atmosphere=atmosphereSession;
            NaturalLengthMeters=line.naturalLengthMeters;rope=new TowlineSolver(line);
            tug=new JSBSimNativeDevelopmentBackend(dataRoot);
            try
            {
                if(!tug.Initialize(new FlightDynamicsBackendContext{secondaryNativeTug=true,aircraftDefinition=definition,terrainSurface=terrain,localOrigin=GeodeticReference.Kbdu,fixedDeltaTimeSeconds=nativeStep}))
                    throw new InvalidOperationException(tug.LastError);
                if(atmosphere!=null && !tug.NativeSession.SetConditions(atmosphere.Conditions))throw new InvalidOperationException(tug.NativeSession.LastError);
                if(atmosphere!=null)tug.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(atmosphere.Sample(FlightFrameConversions.GeodeticToUnity(initial.latitudeDegrees,initial.longitudeDegrees,initial.altitudeMslMeters,GeodeticReference.Kbdu),0)));
                if(!tug.Reset(initial))throw new InvalidOperationException(tug.LastError);
                TugState=tug.CurrentState;TugDiagnostics=tug.NativeSession.Diagnostics;pilot.Reset(initial.headingDegrees);rope.HookUp();
            }
            catch{tug.Dispose();throw;}
        }
        public void ReleaseTug()=>rope.ReleaseTug();
        public void AbortLaunch(){rope.ReleaseTug();pilot.Abort();}
        public bool Step(JSBSimNativeDevelopmentBackend player,BoulderWindFieldProfile weather,double dt,bool pilotRelease)
        {
            if(Faulted)return false;
            if(player==null || weather==null || Math.Abs(dt-1d/120)>1e-9)return Fail("Aerotow requires the fixed authored 120 Hz outer clock and a valid native pair.");
            if(pilotRelease)rope.ReleaseGlider();
            double h=nativeStep;
            for(int sub=0;sub<substeps;sub++)
            {
                var gliderState=player.CurrentState;var tugState=tug.CurrentState;
                if(Vector3.Distance(gliderState.positionUnityMeters,tugState.positionUnityMeters)<9)return Fail("Pair body separation is unsafe; flight frozen. Rope fouling/collision dynamics are not modeled.");
                double t=gliderState.simulationTimeSeconds;
                if(Math.Abs(t-tugState.simulationTimeSeconds)>1e-8)return Fail("Pair time mismatch before step.");
                if(!player.NativeSession.GetAttachment(out var g)||!tug.NativeSession.GetAttachment(out var a))return Fail("Native hook unavailable.");
                Vector3 gliderWind=atmosphere!=null?atmosphere.Sample(gliderState.positionUnityMeters,t):weather.Sample(gliderState.positionUnityMeters,t);
                Vector3 tugWind=atmosphere!=null?atmosphere.Sample(tugState.positionUnityMeters,t):weather.Sample(tugState.positionUnityMeters,t);
                if(!BoulderWindFieldProfile.Finite(gliderWind)||!BoulderWindFieldProfile.Finite(tugWind)||gliderWind.sqrMagnitude>900||tugWind.sqrMagnitude>900)return Fail("Invalid pair atmosphere.");
                if(!rope.Evaluate(g,a,out var line))return Fail("Nonfinite or out-of-envelope physical rope state.");
                PeakTensionNewtons=Math.Max(PeakTensionNewtons,line.trialTensionNewtons);
                PeakExtensionMeters=Math.Max(PeakExtensionMeters,line.extensionMeters);
                pilot.LocalWind=tugWind;pilot.Evaluate(tugState,line,t);tug.SetControls(pilot.Controls);
                player.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(gliderWind));tug.SetAtmosphere(FlightDynamicsCoordinator.AtmosphereFromWind(tugWind));
                var gf=g.EcefForceToBody(line.forceEcefXNewtons,line.forceEcefYNewtons,line.forceEcefZNewtons);
                var tf=a.EcefForceToBody(-line.forceEcefXNewtons,-line.forceEcefYNewtons,-line.forceEcefZNewtons);
                // An explicitly modeled wing runner supplies only a bounded
                // roll moment while ground support is active, never in flight.
                WingSupportActive=(rope.Connected || !pilot.LaunchAuthorized) && gliderState.weightOnWheels && gliderState.groundSpeedKnots<23 && pilot.Phase!=GroundLaunchPhase.Aborted;
                double wingMoment=WingSupportActive?Math.Max(-180,Math.Min(180,-gliderState.bankDegrees*35+gliderState.angularVelocityBodyDegreesPerSecond.z*12)):0;
                if(atmosphere!=null && atmosphere.WakeEnabled)wake.Record(tugState,TugDiagnostics,tugWind);
                var gliderAirflow=AircraftAirflowSampler.Sample(gliderState,player.NativeSession.Diagnostics,atmosphere,gliderWind,atmosphere!=null&&atmosphere.WakeEnabled?wake:null,true);
                var tugAirflow=AircraftAirflowSampler.Sample(tugState,TugDiagnostics,atmosphere,tugWind,null,false);
                if(!player.NativeSession.SetExternalLoad(gf)||!tug.NativeSession.SetExternalLoad(tf)||
                    !AircraftAirflowSampler.Submit(player.NativeSession,gliderAirflow,wingMoment)||!AircraftAirflowSampler.Submit(tug.NativeSession,tugAirflow))return Fail("Native external-load submission failed.");
                if(!player.PrepareAdvance() || !tug.PrepareAdvance())return Fail("Pair inputs failed: "+player.LastError+" "+tug.LastError);
                // Same snapshot and both submitted inputs regardless of order.
                bool ok=SwapIntegrationOrderForTest?tug.AdvancePrepared(h)&&player.AdvancePrepared(h):player.AdvancePrepared(h)&&tug.AdvancePrepared(h);
                if(!ok)return Fail("Pair advance stopped; no half-updated pair published: "+player.LastError+" "+tug.LastError);
                if(Math.Abs(player.CurrentState.simulationTimeSeconds-(t+h))>1e-8 || Math.Abs(tug.CurrentState.simulationTimeSeconds-(t+h))>1e-8)
                    return Fail("Pair time mismatch after step.");
                if(!player.NativeSession.GetAttachment(out g)||!tug.NativeSession.GetAttachment(out a))return Fail("Post-step hook unavailable.");
                GliderHook=g;TugHook=a;Line=line;TugState=tug.CurrentState;TugDiagnostics=tug.NativeSession.Diagnostics;PairSubsteps++;
            }
            return true;
        }
        private bool Fail(string reason){LastError=reason;Faulted=true;tug.NativeSession.ClearExternalLoads();return false;}
        public void Dispose(){tug.Dispose();}
    }
}
