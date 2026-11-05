using System;
using AlexOviedo.QuestFlight.JSBSim;
using QuestFlightLab.Flight.Backends;
using UnityEngine;
using Unity.Profiling;

namespace QuestFlightLab.Flight.Soaring
{
    public struct IncrementalAirflowLoad {public Vector3 forceBodyFrdNewtons,momentBodyFrdNewtonMeters;}
    public static class AircraftAirflowSampler
    {
        private static readonly ProfilerMarker Marker=new ProfilerMarker("QuestFlight.Soaring.DifferentialAirflow");
        private static Vector3 Frd(Vector3 unity)=>new Vector3(unity.z,unity.x,-unity.y);
        private static Vector3 Unity(Vector3 frd)=>new Vector3(frd.y,-frd.z,frd.x);
        public static Vector3 SectionForce(Vector3 relativeAirFrd,float rho,float area,float liftSlope)
        {
            float forward=relativeAirFrd.x,vertical=relativeAirFrd.z,squared=forward*forward+vertical*vertical;
            if(squared<1 || forward<=0)return Vector3.zero;
            float alpha=Mathf.Atan2(vertical,forward),cl=1.6f*(float)Math.Tanh(liftSlope*alpha/1.6f),q=.5f*rho*squared*area;
            float inv=1/Mathf.Sqrt(squared);
            // Profile drag only. No second induced-drag model or baseline lift.
            return q*new Vector3(cl*vertical*inv-.02f*forward*inv,0,-cl*forward*inv-.02f*vertical*inv);
        }
        public static IncrementalAirflowLoad Sample(in FlightDynamicsState state,in JSBSimNativeDiagnostics diagnostics,
            SoaringAtmosphereSession atmosphere,Vector3 meanAmbient,TowWakeField wake,bool glider)
        {
            using var scope=Marker.Auto();var result=new IncrementalAirflowLoad();
            if(atmosphere==null||!atmosphere.DifferentialAirflow)return result;
            var inverse=Quaternion.Inverse(state.rotationUnity);
            Vector3 velocity=Frd(inverse*(state.velocityUnityMetersPerSecond-meanAmbient));
            Vector3 omega=new Vector3(-state.angularVelocityBodyDegreesPerSecond.z,-state.angularVelocityBodyDegreesPerSecond.x,state.angularVelocityBodyDegreesPerSecond.y)*Mathf.Deg2Rad;
            float rho=(float)diagnostics.densitySlugsPerCubicFoot*515.3788f,span=glider?15.5448f:10.72f,area=glider?20.39f:16.6f;
            for(int section=0;section<7;section++)
            {
                bool tail=section==6;
                float structuralX=glider?(tail?250.035f:82.035f):(tail?166:25);
                float lateral=tail?0:(section<3?-1:1)*span*.5f*((section%3+.5f)/3);
                Vector3 r=new Vector3(((float)diagnostics.cgXInches-structuralX)*.0254f,lateral,(float)diagnostics.cgZInches*.0254f);
                Vector3 point=state.positionUnityMeters+state.rotationUnity*Unity(r);
                Vector3 localWind=atmosphere.Sample(point,state.simulationTimeSeconds);
                if(wake!=null)localWind+=wake.Sample(point,state.simulationTimeSeconds);
                Vector3 difference=Frd(inverse*(localWind-meanAmbient));
                // Identical local rate velocity on both sides of subtraction.
                Vector3 common=velocity+Vector3.Cross(omega,r);
                float sectionArea=tail?(glider?2.2297f:2.0f):area/6,slope=tail?3.5f:5.5f;
                Vector3 force=SectionForce(common-difference,rho,sectionArea,slope)-SectionForce(common,rho,sectionArea,slope);
                result.forceBodyFrdNewtons+=force;result.momentBodyFrdNewtonMeters+=Vector3.Cross(r,force);
            }
            return result;
        }
        public static bool Submit(JSBSimNativeSession session,in IncrementalAirflowLoad load,double supportRollMoment=0)
        {
            return session.SetExternalLoad(new JSBSimNativeExternalLoad{channel=1,bodyXSi=load.forceBodyFrdNewtons.x,bodyYSi=load.forceBodyFrdNewtons.y,bodyZSi=load.forceBodyFrdNewtons.z}) &&
                session.SetExternalLoad(new JSBSimNativeExternalLoad{channel=2,bodyXSi=load.momentBodyFrdNewtonMeters.x+supportRollMoment,bodyYSi=load.momentBodyFrdNewtonMeters.y,bodyZSi=load.momentBodyFrdNewtonMeters.z});
        }
    }
}
