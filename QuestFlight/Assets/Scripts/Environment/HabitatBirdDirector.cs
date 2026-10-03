using QuestFlight.Product;
using QuestFlightLab.Flight.Backends;
using Unity.Profiling;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    [DefaultExecutionOrder(120)]
    public sealed class HabitatBirdDirector : MonoBehaviour
    {
        public ProductFlightModeCoordinator modes;
        public FlightDynamicsCoordinator flight;
        public AmbientTrafficDirector traffic;
        public Camera pilotCamera;
        public HabitatBirdActor[] birds;
        public Vector3 demo08Center=new Vector3(1620,470,1800),demo26Center=new Vector3(-1700,460,1700);
        public bool EncounterStarted { get; private set; }
        public bool EncounterFinished { get; private set; }
        public HabitatBirdActor Eagle => birds[0];
        public int VisibleBirds { get; private set; }
        public int VisibleTriangles { get; private set; }
        private int session=-1;
        private float clock,encounterUntil;
        private Vector3 demoCenter,encounterDirection;
        private bool demoSession;
        private readonly Plane[] left=new Plane[6],right=new Plane[6];
        private readonly int[] order=new int[24];
        private static readonly ProfilerMarker Tick=new ProfilerMarker("QuestFlight.Wildlife");

        private void Start()
        {
            if(modes==null || flight==null || flight.windField==null || pilotCamera==null || traffic==null || birds==null || birds.Length!=24)
            {Debug.LogError("[Wildlife] Incomplete authored population/references.",this);enabled=false;return;}
            foreach(var b in birds)if(b==null || b.animator==null || b.lods==null || b.lods.Length==0)
            {Debug.LogError("[Wildlife] Incomplete authored bird.",this);enabled=false;return;}
        }

        private void Update()
        {
            if(modes.State==ProductFlightModeCoordinator.FlightModeState.Selection || modes.State==ProductFlightModeCoordinator.FlightModeState.Blocked)
            {foreach(var b in birds)b.SetLod(-1);return;}
            if(!flight.IsInitialized || Time.timeScale<=0)return;
            if(session!=modes.FlightSessionVersion)ResetSession();
            Step(Mathf.Min(Time.deltaTime,.05f));
        }

        public void ResetSession()
        {
            session=modes.FlightSessionVersion;clock=0;
            EncounterStarted=false;EncounterFinished=false;
            demoSession=modes.State==ProductFlightModeCoordinator.FlightModeState.Demo;
            demoCenter=flight.windField.Sample(Vector3.zero,0).x>1?demo26Center:demo08Center;
            for(int i=0;i<birds.Length;i++)
            {
                var b=birds[i];Vector3 center=demoSession&&b.hero?demoCenter:b.habitatCenter;
                flight.windField.SampleGrid(center.x,center.z,out float ground,out _);
                b.ResetBird(center,ground);order[i]=i;
                if(b.leader!=null)
                {
                    Vector3 f=b.leader.AirVelocity.normalized,r=Vector3.Cross(Vector3.up,f);
                    b.transform.position=b.leader.transform.position+r*b.formationOffset.x+f*b.formationOffset.y;
                    b.AirVelocity=b.leader.AirVelocity;b.Velocity=b.leader.Velocity;
                }
            }
            Debug.Log("[Wildlife] New session "+session+" mode="+modes.State+" birds="+birds.Length+" eagle="+Eagle.transform.position);
        }

        public void Step(float dt)
        {
            using(Tick.Auto())
            {
                clock+=dt;
                var state=flight.CurrentState;
                var pilot=modes.AuthoredDemoPilot;
                if(demoSession && !EncounterStarted && !EncounterFinished)
                {
                    if(pilot.UserHasControls){demoSession=false;EncounterFinished=true;Eagle.Travelling=true;}
                    else
                    {
                        Vector3 delta=Eagle.transform.position-state.positionUnityMeters;
                        Vector3 direction=Vector3.ProjectOnPlane(state.velocityUnityMetersPerSecond,Vector3.up).normalized;
                        if(state.altitudeAglMeters>180 && Mathf.Abs((float)state.bankDegrees)<15 && delta.magnitude<650 &&
                           Vector3.Dot(delta.normalized,direction)>.6f)
                        {
                            EncounterStarted=true;encounterUntil=clock+28;
                            encounterDirection=direction;
                            Debug.Log("[Wildlife] Eagle pass begins at "+Eagle.transform.position+" aircraft="+state.positionUnityMeters);
                        }
                    }
                }
                if(EncounterStarted&&!EncounterFinished && (clock>=encounterUntil || pilot.UserHasControls))
                {EncounterFinished=true;Eagle.Travelling=true;Eagle.Secondary=false;Debug.Log("[Wildlife] Eagle departing continuously to open-country habitat.");}

                for(int i=0;i<birds.Length;i++)
                {
                    var b=birds[i];
                    if(clock>=b.NextDecision)
                    {b.NextDecision=clock+.17f+(i%3)*.013f;Decide(b,state);}
                    Vector3 desired;
                    if(b.hero && EncounterStarted&&!EncounterFinished)
                    {
                        // Fixed world heading after release, not aircraft pacing or camera following.
                        desired=encounterDirection*b.airspeed;
                        desired.y=Mathf.Clamp((demoCenter.y-b.transform.position.y)*.2f,-1,1.5f)+Mathf.Max(0,b.Lift-b.sink);
                    }
                    else if(b.leader!=null)
                    {
                        Vector3 f=Vector3.ProjectOnPlane(b.leader.Velocity,Vector3.up).normalized;
                        Vector3 r=Vector3.Cross(Vector3.up,f);
                        Vector3 p=b.leader.transform.position+r*b.formationOffset.x+f*b.formationOffset.y;
                        p.y+=Mathf.Sin(clock*.45f+b.phase)*1.5f;
                        desired=b.leader.Velocity+Vector3.ClampMagnitude((p-b.transform.position)*.3f,5);
                    }
                    else
                    {
                        Vector3 to=b.Target-b.transform.position;
                        Vector3 horizontal=Vector3.ProjectOnPlane(to,Vector3.up);
                        if(b.Travelling)
                        {
                            desired=horizontal.normalized*b.airspeed;
                            if(horizontal.magnitude<b.orbitRadius*1.2f)b.Travelling=false;
                        }
                        else
                        {
                            Vector3 radial=-horizontal.normalized;
                            Vector3 tangent=Vector3.Cross(radial,Vector3.up);
                            desired=(tangent+horizontal.normalized*Mathf.Clamp((horizontal.magnitude-b.orbitRadius)/b.orbitRadius,-.8f,.8f)).normalized*b.airspeed;
                        }
                        desired.y=b.goose?Mathf.Clamp(to.y*.12f,-1.5f,1.5f):
                            b.Powered?Mathf.Clamp(to.y*.1f,-1.8f,1.8f):Mathf.Clamp(b.Lift-b.sink,-2,3);
                    }
                    desired+=b.AvoidanceVelocity;
                    desired.y=Mathf.Max(desired.y,b.MinimumClimb);
                    b.Integrate(desired,dt);
                }
                // View admission follows every rendered frame, not the slower habitat decisions.
                // Otherwise a fast head turn could outrun the conservative stereo margin.
                UpdateVisibility();
            }
        }

        private void Decide(HabitatBirdActor b,FlightDynamicsState state)
        {
            b.Wind=flight.windField.Sample(b.transform.position,state.simulationTimeSeconds);
            b.AvoidanceVelocity=Avoidance(b,state.positionUnityMeters,state.velocityUnityMetersPerSecond,100);
            foreach(var plane in traffic.actors)if(!plane.Suppressed)
                b.AvoidanceVelocity+=Avoidance(b,plane.Position,plane.Velocity,80);
            Vector3 ahead=b.transform.position+b.Velocity*4;
            b.MinimumClimb=-3;
            if(flight.terrainSurface.TrySample(ahead.x,ahead.z,out var surface))
                b.MinimumClimb=Mathf.Clamp((surface.heightMeters+55-b.transform.position.y)*.2f,-3,3);
            b.Lift=b.Wind.y;
            Vector3 center=b.Secondary?b.secondaryCenter:b.habitatCenter;
            if(b.hero && demoSession&&!EncounterFinished)center=demoCenter;
            else if(b.thermalIndex>=0)
            {
                var field=flight.windField;
                var thermal=b.thermalIndex==0?field.AirportThermal:field.foothillThermals[Mathf.Min(b.thermalIndex-1,field.foothillThermals.Length-1)];
                Vector2 displaced=BoulderWindFieldProfile.ThermalCenter(thermal,b.transform.position.y,new Vector2(b.Wind.x,b.Wind.z));
                if(!b.Secondary){center.x=displaced.x;center.z=displaced.y;}
                if(b.transform.position.y>thermal.groundHeight+thermal.topAgl*.72f && !b.Secondary)
                {b.Secondary=true;b.Travelling=true;center=b.secondaryCenter;b.NextTravel=clock+100;}
            }
            if(clock>b.NextTravel && !(b.hero&&demoSession&&!EncounterFinished))
            {b.Secondary=!b.Secondary;b.Travelling=true;b.NextTravel=clock+100+b.phase*9;center=b.Secondary?b.secondaryCenter:b.habitatCenter;}
            flight.windField.SampleGrid(center.x,center.z,out float ground,out _);
            center.y=Mathf.Max(center.y,ground+b.cruiseAgl);
            b.Target=center;
            b.Powered=b.goose || b.Lift<b.sink+.15f || b.transform.position.y<center.y-45;
        }

        private static Vector3 Avoidance(HabitatBirdActor bird,Vector3 position,Vector3 velocity,float radius)
        {
            Vector3 delta=bird.transform.position-position,relative=bird.Velocity-velocity;
            float t=Mathf.Clamp(-Vector3.Dot(delta,relative)/Mathf.Max(relative.sqrMagnitude,.1f),0,6);
            Vector3 closest=delta+relative*t;
            if(closest.magnitude>radius)return Vector3.zero;
            Vector3 away=closest.sqrMagnitude>4?closest.normalized:Vector3.Cross(Vector3.up,relative).normalized;
            return away*(1-closest.magnitude/radius)*bird.airspeed*1.4f;
        }

        private void UpdateVisibility()
        {
            if(pilotCamera.stereoEnabled)
            {
                GeometryUtility.CalculateFrustumPlanes(pilotCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left)*pilotCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Left),left);
                GeometryUtility.CalculateFrustumPlanes(pilotCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right)*pilotCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Right),right);
            }
            else{GeometryUtility.CalculateFrustumPlanes(pilotCamera,left);System.Array.Copy(left,right,6);}
            // Stable near-first budget. Never enlarge a distant bird to make it visible.
            for(int i=1;i<order.Length;i++)
            {
                int key=order[i],j=i-1;
                while(j>=0 && Priority(birds[key])<Priority(birds[order[j]])){order[j+1]=order[j];j--;}
                order[j+1]=key;
            }
            int full=0;VisibleBirds=0;VisibleTriangles=0;
            foreach(int index in order)
            {
                var b=birds[index];float distance=Vector3.Distance(pilotCamera.transform.position,b.transform.position);
                float angular=b.span/Mathf.Max(1,distance);
                var bounds=new Bounds(b.transform.position,Vector3.one*(b.span+distance*.18f));
                bool visible=angular>(b.Shown ? .0008f:.0011f) && (GeometryUtility.TestPlanesAABB(left,bounds)||GeometryUtility.TestPlanesAABB(right,bounds));
                int lod=angular>(b.Lod==0 ? .017f:.022f)?0:angular>(b.Lod==1 ? .005f:.007f)?1:2;
                if(b.hero)lod=0;
                if(lod==0&&full>=4&&!b.hero)lod=1;
                lod=Mathf.Min(lod,b.lods.Length-1);
                if(!visible||VisibleBirds>=16||VisibleTriangles+b.triangles[lod]>30000){b.SetLod(-1);continue;}
                b.SetLod(lod);VisibleBirds++;VisibleTriangles+=b.triangles[lod];if(lod==0)full++;
            }
        }
        private float Priority(HabitatBirdActor b)=>b.hero?-1:(b.transform.position-pilotCamera.transform.position).sqrMagnitude*(b.Shown ? .8f:1);

        public bool TryGetEagleCue(out string id)
        {
            id=null;
            if(!EncounterStarted||EncounterFinished||!Eagle.Shown||modes.AuthoredDemoPilot.UserHasControls)return false;
            Vector3 delta=Eagle.transform.position-pilotCamera.transform.position;
            float distance=delta.magnitude;
            if(distance<85||distance>320 || Vector3.Dot(pilotCamera.transform.forward,delta.normalized)<.72f)return false;
            Vector3 local=flight.transform.InverseTransformDirection(delta.normalized);
            if(local.z<.65f || local.y<-.12f || local.y>.5f)return false;
            Vector3 relative=Eagle.Velocity-flight.CurrentState.velocityUnityMetersPerSecond;
            Vector3 later=delta+relative*4;
            if(Vector3.Dot(pilotCamera.transform.forward,later.normalized)<.6f||later.magnitude<65)return false;
            for(int i=1;i<6;i++)
            {Vector3 p=pilotCamera.transform.position+delta*(i/6f);if(!flight.terrainSurface.TrySample(p.x,p.z,out var h)||h.heightMeters>p.y)return false;}
            if(Physics.Raycast(pilotCamera.transform.position,delta.normalized,distance-3,~0,QueryTriggerInteraction.Ignore))return false;
            id=local.x<-.07f?"tour_eagle_left":local.x>.07f?"tour_eagle_right":"tour_eagle_ahead";
            return true;
        }
    }
}
