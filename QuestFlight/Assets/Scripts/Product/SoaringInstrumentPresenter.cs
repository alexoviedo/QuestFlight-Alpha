using System;
using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlight.Product
{
    [Serializable] public struct SoaringMovingPart
    {
        public string name;
        public int firstVertex, vertexCount;
        public Vector3 anchor;
        public Vector3 rotationAxis;
        public Transform grabTarget;
        public Vector3 gripRestPosition;
    }

    // One serialized moving mesh, one set of instrument faces, no cockpit camera.
    // A mesh copy and fixed working arrays are allocated once, never per sample.
    [DefaultExecutionOrder(21000), DisallowMultipleComponent]
    public sealed class SoaringInstrumentPresenter : MonoBehaviour
    {
        public FlightDynamicsCoordinator flight;
        public MeshFilter moving;
        public SoaringMovingPart[] parts;
        public SoaringLeverGrabTransformer releaseHandle;
        public const float PressureSettingHpa=1013.25f;
        private static readonly float[] AsiFaceDegrees={0,35,70,115,160,205,250,295,335};
        public static float AsiAngle(float knots)
        {
            float mph=Mathf.Clamp(knots*1.15077945f,0,160);
            int index=Mathf.Min(7,Mathf.FloorToInt(mph/20));
            return -Mathf.Lerp(AsiFaceDegrees[index],AsiFaceDegrees[index+1],(mph-index*20)/20);
        }
        public static float VsiAngle(float feetPerMinute)=>90-Mathf.Clamp(feetPerMinute,-1500,1500)*.12f;
        // Same declared Boulder magnetic variation used by the C172 instruments.
        // Static magnetic heading only; compass dip/acceleration errors unmodeled.
        public static float CompassHeading(float trueHeading)=>Mathf.Repeat(trueHeading-
            QuestFlightLab.Aircraft.CockpitDynamics.SystemsV2.CockpitSystemsPhysicalConfigurationV2.KbduMagneticVariationEastDegrees,360);
        public const float BrakeLeverTravelMeters=.23f;
        // Reference geometry: release roll followed by straight rearward travel.
        // The input does not simulate physical latch force or require a new gesture.
        public static Quaternion BrakeLeverRotation(float position)
            => Quaternion.AngleAxis(-25f*Mathf.Clamp01(position/.08f),Vector3.forward);
        public static Vector3 BrakeLeverDisplacement(float position)
            => Vector3.back*(BrakeLeverTravelMeters*Mathf.Clamp01(position));
        public bool HasValidSnapshot { get; private set; }
        public float PressureVarioFpm { get; private set; }
        private Mesh mesh;
        private Vector3[] rest, positions, restNormals, normals;
        private readonly SoaringYawString yawString = new SoaringYawString();
        private void OnEnable() { yawString.Reset(); }

        private void Awake()
        {
            if(moving==null || moving.sharedMesh==null || flight==null) { enabled=false;return; }
            mesh=Instantiate(moving.sharedMesh);mesh.name="SGS moving presentation instance";
            mesh.MarkDynamic();moving.sharedMesh=mesh;
            rest=mesh.vertices;positions=(Vector3[])rest.Clone();restNormals=mesh.normals;normals=(Vector3[])restNormals.Clone();
        }
        public static float AileronAngle(double normalized) => -(float)normalized*(normalized<0?38:18);
        // G2EA: upper85deg up/lower75deg down. Lower blade extends FORWARD
        // of its hinge, so both use positive rotation about the rightward axis.
        public static Quaternion AirbrakeRotation(SoaringMovingPart part,float extension)
            => Quaternion.AngleAxis((part.name.Contains("Upper")?85f:75f)*Mathf.Clamp01(extension),PartAxis(part,Vector3.right));
        private void LateUpdate()
        {
            HasValidSnapshot=flight!=null && flight.IsInitialized && flight.aircraftDefinition!=null && flight.aircraftDefinition.IsGlider && flight.CurrentState.IsFinite;
            if(!HasValidSnapshot || mesh==null) { yawString.Reset(); return; }
            var s=flight.CurrentState;var d=flight.NativeDiagnostics;
            PressureVarioFpm=(float)flight.SoaringInstruments.PressureVsiFpm;
            float airbrake=flight.NativeSession!=null?(float)flight.NativeSession.AirData.airbrakePosition:0;
            var c=flight.LastAppliedControls;
            foreach(var part in parts)
            {
                if(part.name=="YawString")
                {
                    if((!yawString.HasSample || (flight.enabled && !flight.RecoveryHold && Time.timeScale>0)) && yawString.Sample(s.simulationTimeSeconds,
                        SoaringYawString.LocalAirflow((float)d.trueAirspeedKnots*.514444f,(float)s.angleOfAttackDegrees,
                            (float)s.sideslipDegrees,s.angularVelocityBodyDegreesPerSecond,part.anchor),
                        Quaternion.Inverse(s.rotationUnity)*Vector3.down,part.anchor,part.rotationAxis))
                        yawString.WriteVertices(part,rest,restNormals,positions,normals);
                    continue;
                }
                Quaternion q=Quaternion.identity;Vector3 delta=Vector3.zero;
                switch(part.name)
                {
                    case "LeftAileron":q=Quaternion.AngleAxis(AileronAngle(d.leftAileronPosition),PartAxis(part,Vector3.right));break;
                    case "RightAileron":q=Quaternion.AngleAxis(AileronAngle(d.rightAileronPosition),PartAxis(part,Vector3.right));break;
                    case "Elevator":q=Quaternion.AngleAxis(-(float)d.elevatorPosition*23,Vector3.right);break;
                    case "Rudder":q=Quaternion.AngleAxis((float)d.rudderPosition*30,PartAxis(part,Vector3.up));break;
                    case "TowRelease":delta=Vector3.back*(releaseHandle!=null?releaseHandle.PullMeters:0);break;
                    case "FrontStick":case "RearStick":q=Quaternion.Euler((float)d.elevatorPosition*18,0,-c.aileron*18);break;
                    case "FrontPedals":case "RearPedals":q=Quaternion.Euler(0,-c.rudder*12,0);break;
                    case "LeftUpperBrake":case "RightUpperBrake":
                    case "LeftLowerBrake":case "RightLowerBrake":q=AirbrakeRotation(part,airbrake);break;
                    case "BrakeLever":q=BrakeLeverRotation(flight.controls.GliderLeverPosition);delta=BrakeLeverDisplacement(flight.controls.GliderLeverPosition);break;
                    case "TrimLever":q=Quaternion.Euler(-c.trim*30,0,0);break;
                    case "AsiNeedle":q=Quaternion.Euler(0,0,AsiAngle((float)s.calibratedAirspeedKnots));break;
                    case "AltitudeNeedle":q=Quaternion.Euler(0,0,-Mathf.Repeat((float)flight.SoaringInstruments.PressureAltitudeFeet,1000)*.36f);break;
                    case "AltitudeThousands":q=Quaternion.Euler(0,0,-Mathf.Repeat((float)flight.SoaringInstruments.PressureAltitudeFeet,10000)*.036f);break;
                    case "AltitudeTenThousands":q=Quaternion.Euler(0,0,-Mathf.Repeat((float)flight.SoaringInstruments.PressureAltitudeFeet,100000)*.0036f);break;
                    case "VarioNeedle":q=Quaternion.Euler(0,0,VsiAngle(PressureVarioFpm));break;
                    case "CompassCard":q=Quaternion.Euler(0,CompassHeading((float)s.headingDegrees),0);break;
                }
                for(int i=part.firstVertex;i<part.firstVertex+part.vertexCount;i++)
                { positions[i]=part.anchor+q*(rest[i]-part.anchor)+delta;normals[i]=q*restNormals[i]; }
                // The SDK selection volume follows the same pose as the visible
                // handle. Otherwise a moved lever cannot be grabbed again.
                if(part.grabTarget!=null)
                {
                    part.grabTarget.localPosition=GripPosition(part,q,delta);
                    part.grabTarget.localRotation=q;
                }
            }
            mesh.vertices=positions;mesh.normals=normals;
        }
        private static Vector3 PartAxis(SoaringMovingPart part,Vector3 fallback)=>part.rotationAxis.sqrMagnitude>.01f?part.rotationAxis.normalized:fallback;
        public static Vector3 GripPosition(SoaringMovingPart part,Quaternion rotation,Vector3 displacement)
            => part.anchor+rotation*(part.gripRestPosition-part.anchor)+displacement;
        private void OnDestroy() { if(mesh!=null)Destroy(mesh); }
    }
}
