using System;
using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Flight.Soaring
{
    [CreateAssetMenu(menuName="QuestFlight/Soaring/Boulder operation")]
    public sealed class BoulderSoaringOperationDefinition : ScriptableObject
    {
        public string operationId, displayName;
        [TextArea] public string provenance;
        public int runwayDirection=8;
        public float trueHeading=89.972f, centerlineNorth=-30, launchEast=-555;
        public float minimumRunwayEast=-680, maximumRunwayEast=640, runwayHalfWidth=12;
        public float patternFieldHeight=308.5f, minimumTurnFieldHeight=156.4f;
        public float releaseFieldHeight=914.4f, releaseMinimumTerrainClearance=300, releaseRadius=1500;
        public Vector2 releaseCenter=new Vector2(1400,2200);
        public Vector2 supportedMinimum=new Vector2(-7400,-3000), supportedMaximum=new Vector2(6500,6500);
        public Vector2[] towRoute=Array.Empty<Vector2>();
        public SoaringWeatherDefinition weather;
        public bool terrainVerified;
        public string terrainSha256;
        public int DirectionSign=>runwayDirection==8?1:-1;
        public Vector3 Forward=>new Vector3(Mathf.Sin(trueHeading*Mathf.Deg2Rad),0,Mathf.Cos(trueHeading*Mathf.Deg2Rad));
        public Vector3 Launch=>new Vector3(launchEast,0,centerlineNorth);
        public bool IsValid=>terrainVerified && weather!=null && weather.IsValid(out _) &&
            (runwayDirection==8||runwayDirection==26) && towRoute!=null && towRoute.Length>=3 &&
            !string.IsNullOrEmpty(provenance) && releaseFieldHeight>=600 && releaseMinimumTerrainClearance>=300;
        public bool Contains(Vector3 p)=>p.x>=supportedMinimum.x&&p.x<=supportedMaximum.x&&p.z>=supportedMinimum.y&&p.z<=supportedMaximum.y;
        public bool OnLandingSurface(Vector3 p)=>p.x>=minimumRunwayEast&&p.x<=maximumRunwayEast&&Mathf.Abs(p.z-centerlineNorth)<=runwayHalfWidth;
        public bool WeatherPermits(SoaringAtmosphereSession atmosphere)
        {
            if(atmosphere==null)return false;
            Vector3 wind=atmosphere.Sample(Launch+Vector3.up*10,0);
            float along=Vector3.Dot(wind,Forward),cross=Vector3.Cross(wind,Forward).y;
            // Reference test envelope, not an approved aircraft wind limit.
            return along<=1 && Mathf.Abs(cross)<=3 && (runwayDirection==8 || wind.x>1);
        }
        public bool ReleaseAvailable(in FlightDynamicsState s)=>Contains(s.positionUnityMeters)&&
            s.positionUnityMeters.y>=releaseFieldHeight && s.positionUnityMeters.y<=releaseFieldHeight+180 &&
            s.altitudeAglMeters>=releaseMinimumTerrainClearance &&
            Vector2.Distance(new Vector2(s.positionUnityMeters.x,s.positionUnityMeters.z),releaseCenter)<=releaseRadius;
    }

    public readonly struct SoaringReturnEstimate
    {
        public readonly bool supported,reachable;
        public readonly float marginMeters,calibratedSpeedKnots;
        public SoaringReturnEstimate(bool valid,float margin,float speed){supported=valid;reachable=valid&&margin>=0;marginMeters=margin;calibratedSpeedKnots=speed;}
    }

    // Conservative planning only. Uses the calibrated full polar at several speeds,
    // density, along-track wind/sink and sampled terrain; never adds aircraft energy.
    public static class SoaringReturnPlanner
    {
        public static SoaringReturnEstimate Evaluate(BoulderSoaringOperationDefinition operation,
            ProductionTerrainSurfaceAsset terrain,SoaringAtmosphereSession weather,in FlightDynamicsState state,double massKg)
        {
            if(operation==null||terrain==null||weather==null||!state.IsFinite||double.IsNaN(massKg)||double.IsInfinity(massKg)||massKg<250||!operation.Contains(state.positionUnityMeters))return default;
            Vector3 start=state.positionUnityMeters;
            Vector3 end=new Vector3(operation.DirectionSign*350,operation.patternFieldHeight,600);
            Vector3 direction=end-start;direction.y=0;float distance=direction.magnitude;direction.Normalize();
            float best=float.NegativeInfinity,bestSpeed=0;
            for(int speed=40;speed<=65;speed+=5)
            {
                float height=start.y,minimum=float.PositiveInfinity;double time=state.simulationTimeSeconds;
                for(int i=1;i<=32;i++)
                {
                    Vector3 at=Vector3.Lerp(start,end,i/32f);at.y=height;
                    if(!terrain.TrySample(at.x,at.z,out var ground)||!operation.Contains(at))return default;
                    Vector3 wind=weather.Sample(at,time);
                    double rho=1.225*Math.Exp(-(GeodeticReference.Kbdu.altitudeMslMeters+height)/8500.0);
                    double tas=speed*.514444*Math.Sqrt(1.225/rho),cl=massKg*9.80665/(.5*rho*tas*tas*20.39);
                    double cd=.0417448745-.0745262304*cl+(.0490701859+.027)*cl*cl;
                    double groundSpeed=tas+Vector3.Dot(wind,direction);
                    if(groundSpeed<8){minimum=float.NegativeInfinity;break;}
                    double dt=distance/32/groundSpeed;
                    // 35% sink reserve and 100m terrain margin are development policy.
                    height-=(float)((tas*cd/cl*1.35-Math.Min(0,wind.y))*dt);time+=dt;
                    minimum=Mathf.Min(minimum,height-ground.heightMeters-100);
                }
                float margin=Mathf.Min(minimum,height-operation.patternFieldHeight-40);
                if(margin>best){best=margin;bestSpeed=speed;}
            }
            return new SoaringReturnEstimate(true,best,bestSpeed);
        }
    }
}
