using System;
using AlexOviedo.QuestFlight.JSBSim;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Flight.Soaring
{
    // Fixed ring of 82 native observations at10Hz (12 native120Hz steps).
    // Its 8.1-second history outlasts the smooth eight-second cutoff.
    // A receiver samples one
    // interpolated nearby vortex-pair section, never a sum of infinite lines.
    // Finite core,8-second age cutoff, actual native lift; no self-wake on tug.
    public sealed class TowWakeField
    {
        private struct Node {public Vector3 position,forward,right,wind;public double time;public float circulation;}
        private readonly Node[] nodes=new Node[82];private int head,count;private double lastTime=-1;
        public const float VortexSpacing=8.42f; // pi/4 * PA18 reference10.72m span.
        public int Count=>count;
        public void Reset(){count=0;head=0;lastTime=-1;}
        public void Record(in FlightDynamicsState tug,in JSBSimNativeDiagnostics diagnostic,Vector3 wind)
        {
            double time=tug.simulationTimeSeconds;if(time<lastTime)Reset();if(lastTime>=0&&time-lastTime<.1-1e-7)return;
            lastTime=time;float speed=(float)diagnostic.trueAirspeedKnots*.514444f,rho=(float)diagnostic.densitySlugsPerCubicFoot*515.3788f;
            float lift=-(float)diagnostic.aeroForceZPounds*4.4482216f*Mathf.Cos((float)tug.angleOfAttackDegrees*Mathf.Deg2Rad)+(float)diagnostic.aeroForceXPounds*4.4482216f*Mathf.Sin((float)tug.angleOfAttackDegrees*Mathf.Deg2Rad);
            float gamma=speed>10&&rho>.1f&&tug.altitudeAglMeters>3?Mathf.Max(0,lift)/(rho*speed*VortexSpacing):0;
            nodes[head]=new Node{position=tug.positionUnityMeters,forward=tug.rotationUnity*Vector3.forward,right=tug.rotationUnity*Vector3.right,wind=wind,time=time,circulation=gamma};head=(head+1)%nodes.Length;count=Math.Min(nodes.Length,count+1);
        }
        private static Vector3 Advected(Node n,double time)
        {float age=(float)(time-n.time);return n.position+n.wind*age-Vector3.up*(n.circulation/(2*Mathf.PI*VortexSpacing)*age);}
        public Vector3 Sample(Vector3 position,double time)
        {
            if(count<2)return Vector3.zero;
            float best=3600;Node chosen=default;Vector3 center=default;bool found=false;
            for(int k=1;k<count;k++)
            {
                Node a=nodes[(head-k+nodes.Length)%nodes.Length],b=nodes[(head-k-1+nodes.Length)%nodes.Length];
                if(time-a.time>8||time<b.time)continue;
                Vector3 pa=Advected(a,time),pb=Advected(b,time),segment=pb-pa;float u=Vector3.Dot(position-pa,segment)/Mathf.Max(.0001f,segment.sqrMagnitude);
                if(u<0||u>1)continue;
                Vector3 point=Vector3.Lerp(pa,pb,u);float d=(position-point).sqrMagnitude;
                if(d>=best)continue;best=d;center=point;chosen=a;chosen.circulation=Mathf.Lerp(a.circulation,b.circulation,u);chosen.time=a.time+(b.time-a.time)*u;
                chosen.forward=Vector3.Lerp(a.forward,b.forward,u).normalized;chosen.right=Vector3.Lerp(a.right,b.right,u).normalized;found=true;
            }
            if(!found)return Vector3.zero;
            float age=(float)(time-chosen.time),coreSquared=1+.35f*age;
            float strength=chosen.circulation/(2*Mathf.PI)*Mathf.Exp(-age/4)*QuestFlightLab.Environment.BoulderWindFieldProfile.Smooth((8-age)/2)*QuestFlightLab.Environment.BoulderWindFieldProfile.Smooth((60-Mathf.Sqrt(best))/15);
            Vector3 right=position-(center+chosen.right*(VortexSpacing*.5f)),left=position-(center-chosen.right*(VortexSpacing*.5f));
            right-=chosen.forward*Vector3.Dot(right,chosen.forward);left-=chosen.forward*Vector3.Dot(left,chosen.forward);
            return strength*(Vector3.Cross(chosen.forward,right)/(right.sqrMagnitude+coreSquared)-Vector3.Cross(chosen.forward,left)/(left.sqrMagnitude+coreSquared));
        }
    }
}
