using QuestFlightLab.Environment;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlightLab.Flight.Soaring
{
    [DefaultExecutionOrder(22000)]
    public sealed class TowlinePresentation : MonoBehaviour
    {
        public FlightDynamicsCoordinator flight;
        public AircraftReferenceFrameRig playerFrame;
        public AmbientAircraftActor nativeTugVisual;
        public MeshFilter ropeMesh;
        public MeshRenderer ropeRenderer;
        private Mesh mesh;
        private readonly Vector3[] points=new Vector3[13],vertices=new Vector3[52];
        private void Awake()
        {
            if(ropeMesh==null||ropeMesh.sharedMesh==null||ropeRenderer==null||flight==null||playerFrame==null||nativeTugVisual==null){enabled=false;return;}
            mesh=Instantiate(ropeMesh.sharedMesh);mesh.MarkDynamic();ropeMesh.sharedMesh=mesh;
        }
        private static Vector3 Offset(AlexOviedo.QuestFlight.JSBSim.JSBSimNativeAttachmentState hook)=>new Vector3((float)hook.bodyOffsetYM,(float)-hook.bodyOffsetZM,(float)hook.bodyOffsetXM);
        private void LateUpdate()
        {
            var pair=flight.TowSession;
            ropeRenderer.enabled=pair!=null && pair.PairSubsteps>0 && (pair.Line.gliderEndAttached||pair.Line.tugEndAttached);
            if(!ropeRenderer.enabled || mesh==null)return;
            Vector3 g=playerFrame.AircraftVisualRoot.TransformPoint(Offset(pair.GliderHook)),t=nativeTugVisual.transform.TransformPoint(Offset(pair.TugHook));
            double length=pair.NaturalLengthMeters;
            bool connected=pair.Line.gliderEndAttached && pair.Line.tugEndAttached;
            if(!connected)
            {
                // Released line remains on the correct end. This is a bounded
                // trailing presentation, not fracture/fouling dynamics.
                Vector3 anchor=pair.Line.tugEndAttached?t:g;
                Vector3 velocity=pair.Line.tugEndAttached?pair.TugState.velocityUnityMetersPerSecond:flight.CurrentState.velocityUnityMetersPerSecond;
                Vector3 behind=velocity.sqrMagnitude>1?-velocity.normalized:-nativeTugVisual.transform.forward;
                g=anchor+behind*(float)length*.965f+Vector3.down*(float)length*.16f;t=anchor;
            }
            Vector3 delta=t-g;float distance=delta.magnitude;
            Vector3 curve=Vector3.down;
            Vector3 middle=(g+t)*.5f;
            if(flight.terrainSurface.TrySample(middle.x,middle.z,out var ground) && middle.y-ground.heightMeters<2)
                curve=Vector3.Cross(Vector3.up,delta).normalized;
            float amplitude=0;
            if(distance<length)
            {
                float low=0,high=(float)length*.55f;
                for(int n=0;n<9;n++)
                {
                    float candidate=(low+high)*.5f,total=0;Vector3 previous=g;
                    for(int i=1;i<=12;i++){float u=i/12f;Vector3 p=Vector3.Lerp(g,t,u)+curve*(4*u*(1-u)*candidate);total+=Vector3.Distance(previous,p);previous=p;}
                    if(total<length)low=candidate;else high=candidate;
                }
                amplitude=(low+high)*.5f;
            }
            for(int i=0;i<=12;i++)
            {
                float u=i/12f;Vector3 p=Vector3.Lerp(g,t,u)+curve*(4*u*(1-u)*amplitude);
                if(flight.terrainSurface.TrySample(p.x,p.z,out ground))p.y=Mathf.Max(p.y,ground.heightMeters+.015f);
                points[i]=p;
            }
            var bounds=new Bounds(transform.InverseTransformPoint(points[0]),Vector3.one*.03f);
            for(int i=0;i<=12;i++)
            {
                Vector3 tangent=(points[Mathf.Min(i+1,12)]-points[Mathf.Max(i-1,0)]).normalized;
                Vector3 side=Vector3.Cross(tangent,Vector3.up).normalized*.005f;if(side.sqrMagnitude<1e-8f)side=Vector3.right*.005f;
                Vector3 up=Vector3.Cross(side.normalized,tangent)*.005f;
                for(int k=0;k<4;k++){Vector3 p=points[i]+(k==0?side:k==1?-side:k==2?up:-up);vertices[i*4+k]=transform.InverseTransformPoint(p);bounds.Encapsulate(vertices[i*4+k]);}
            }
            mesh.vertices=vertices;mesh.bounds=bounds;
        }
        private void OnDestroy(){if(mesh!=null)Destroy(mesh);}
    }
}
