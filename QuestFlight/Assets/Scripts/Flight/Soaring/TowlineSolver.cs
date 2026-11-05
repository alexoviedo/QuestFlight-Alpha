using System;
using AlexOviedo.QuestFlight.JSBSim;

namespace QuestFlightLab.Flight.Soaring
{
    public enum TowlineState { Disconnected,HookedSlack,Taut,ReleasedByGlider,ReleasedByTug,BrokenAtGlider,BrokenAtTug,Aborted }
    [Serializable] public struct TowlineSnapshot
    {
        public double distanceMeters,extensionMeters,tensionNewtons,trialTensionNewtons,elasticEnergyJoules;
        public double forceEcefXNewtons,forceEcefYNewtons,forceEcefZNewtons;
        public TowlineState state;
        public int eventId;
        public bool gliderEndAttached,tugEndAttached;
    }
    // Double-precision Earth-fixed hook math. No Unity transform participates.
    // Damping smoothly engages over the authored first 2 cm of positive
    // extension. It is exactly zero at/below slack, never a force ceiling.
    public sealed class TowlineSolver
    {
        private readonly TowlineDefinition definition;
        public TowlineState State { get; private set; }=TowlineState.Disconnected;
        public int EventId { get; private set; }
        public bool Connected => State==TowlineState.HookedSlack || State==TowlineState.Taut;
        public TowlineSolver(TowlineDefinition value)
        {if(value==null||!value.IsValid)throw new ArgumentException("Invalid authored towline.");definition=value;}
        public void HookUp() {if(State!=TowlineState.Disconnected)throw new InvalidOperationException("A released line requires a new ground session.");State=TowlineState.HookedSlack;EventId++;}
        public void ReleaseGlider() {if(Connected){State=TowlineState.ReleasedByGlider;EventId++;}}
        public void ReleaseTug() {if(Connected){State=TowlineState.ReleasedByTug;EventId++;}}
        public void Abort() {State=TowlineState.Aborted;EventId++;}
        public bool Evaluate(in JSBSimNativeAttachmentState glider,in JSBSimNativeAttachmentState tug,out TowlineSnapshot result)
        {
            result=default;
            double x=tug.positionEcefXM-glider.positionEcefXM,y=tug.positionEcefYM-glider.positionEcefYM,z=tug.positionEcefZM-glider.positionEcefZM;
            double d=Math.Sqrt(x*x+y*y+z*z);
            if(!Finite(d)||d<1e-6)return false;
            double extension=d-definition.naturalLengthMeters,tension=0,trialLoad=0;
            if(Connected && extension>0)
            {
                double speed=((tug.velocityEcefXMps-glider.velocityEcefXMps)*x+(tug.velocityEcefYMps-glider.velocityEcefYMps)*y+(tug.velocityEcefZMps-glider.velocityEcefZMps)*z)/d;
                double engagement=1-Math.Exp(-extension/definition.dampingEngagementExtensionMeters);
                double trial=definition.stiffnessNewtonsPerMeter*extension+definition.dampingNewtonSecondsPerMeter*speed*engagement;
                if(!Finite(trial))return false;
                tension=Math.Max(0,trial);
                trialLoad=tension;
                if(tension>=Math.Min(definition.gliderWeakLinkNewtons,definition.tugWeakLinkNewtons))
                {State=definition.gliderWeakLinkNewtons<=definition.tugWeakLinkNewtons?TowlineState.BrokenAtGlider:TowlineState.BrokenAtTug;EventId++;tension=0;}
                else if(extension>definition.maximumExtensionMeters)return false;
                else if(extension>.001 || State==TowlineState.Taut)State=TowlineState.Taut;
            }
            else if(Connected)State=TowlineState.HookedSlack;
            result=new TowlineSnapshot{distanceMeters=d,extensionMeters=Connected?Math.Max(0,extension):0,tensionNewtons=tension,trialTensionNewtons=trialLoad,
                elasticEnergyJoules=Connected?.5*definition.stiffnessNewtonsPerMeter*Math.Max(0,extension)*Math.Max(0,extension):0,
                forceEcefXNewtons=tension*x/d,forceEcefYNewtons=tension*y/d,forceEcefZNewtons=tension*z/d,
                state=State,eventId=EventId,
                gliderEndAttached=Connected || State==TowlineState.ReleasedByTug || State==TowlineState.BrokenAtTug,
                tugEndAttached=Connected || State==TowlineState.ReleasedByGlider || State==TowlineState.BrokenAtGlider};
            return true;
        }
        private static bool Finite(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
    }
}
