using UnityEngine;

namespace QuestFlightLab.Environment
{
    public enum AmbientSoaringPhase { Thermal, Ridge, Glide, Exit }
    /// <summary>Serialized scenery actor. Owns only its environment visual, never a flight authority.</summary>
    public sealed class AmbientAircraftActor : MonoBehaviour
    {
        public string aircraftId;
        public bool sailplane, taildragger;
        public bool mountainSoaring;
        public AmbientSoaringPhase SoaringPhase { get; internal set; }
        public AmbientAircraftRoute runway08, runway26;
        public Transform visual;
        public Transform[] propellers = System.Array.Empty<Transform>();
        public Transform[] flaps = System.Array.Empty<Transform>();
        public Renderer[] renderers = System.Array.Empty<Renderer>();
        public GameObject[] lodRoots = System.Array.Empty<GameObject>();
        public float spanMeters = 11, bodyRadius = 7, wheelHeight = 1.3f, initialDelay;
        public float taildraggerRestOffset;
        public int initialSegment;
        public Vector3 soaringCenter;
        public float soaringRadius = 175, airspeed = 27, initialOrbitAngle;
        public Color tint = Color.white;

        public Vector3 Position { get; internal set; }
        public int Generation { get; private set; }
        public Vector3 Velocity { get; internal set; }
        public AmbientFlightPhase Phase => Route != null ? Route.segments[SegmentIndex].phase : AmbientFlightPhase.Circuit;
        public bool OnGround => !sailplane && !Escaping && Route != null && Route.segments[SegmentIndex].onGround;
        public bool Suppressed { get; internal set; }
        public AmbientAircraftRoute Route { get; internal set; }
        public int SegmentIndex { get; internal set; }
        internal float Along, Dwell, Speed, OrbitAngle, EscapeTime;
        internal float EscapeVerticalSpeed;
        internal bool Escaping, Started, Retired;
        internal Vector3 EscapeDirection;
        internal Vector3 AirHeading, PriorAirHeading;
        internal float RidgeDirection = 1;
        internal int Lod = -1;
        internal Quaternion attitude = Quaternion.identity;
        private static readonly int TintId = Shader.PropertyToID("_Tint");

        public void Initialize(int direction, bool returning = false)
        {
            Generation++;
            Velocity=Vector3.zero;
            Route = direction == 26 ? runway26 : runway08;
            SegmentIndex = Route == null || returning ? 0 : Mathf.Clamp(initialSegment, 0, Route.segments.Length - 1);
            Along = 0; Dwell = initialDelay; Speed = 0; OrbitAngle = initialOrbitAngle;
            Escaping = false; Suppressed = false; Started = false; Retired = false; EscapeTime = 0;
            AirHeading = new Vector3(-Mathf.Sin(OrbitAngle),0,Mathf.Cos(OrbitAngle)); PriorAirHeading=AirHeading;
            SoaringPhase=AmbientSoaringPhase.Thermal; RidgeDirection=1;
            attitude=Quaternion.Euler(taildragger ? -10 : 0,0,0);
            if (sailplane) Position = soaringCenter + new Vector3(Mathf.Cos(OrbitAngle), 0, Mathf.Sin(OrbitAngle)) * soaringRadius;
            else if (Route != null) Position = Route.segments[SegmentIndex].points[0];
            if(!sailplane && Route!=null && !Route.segments[SegmentIndex].onGround)
            {
                Speed=Route.segments[SegmentIndex].startSpeed;Dwell=0;
                Route.segments[SegmentIndex].Position(0,out var forward);Velocity=forward*Speed;
                attitude=Quaternion.LookRotation(forward);PriorAirHeading=new Vector3(forward.x,0,forward.z).normalized;
            }
            transform.position = Position;
            var properties = new MaterialPropertyBlock(); properties.SetColor(TintId, tint);
            foreach (Renderer r in renderers) if (r != null) r.SetPropertyBlock(properties);
        }

        internal void SetVisible(bool visible)
        {
            foreach (Renderer r in renderers) if (r != null) r.enabled = visible;
        }

        internal void Present(float dt, Vector3 wind, Vector3 observer)
        {
            if (!Suppressed && Velocity.sqrMagnitude > 0.01f)
            {
                Vector3 air = OnGround ? Velocity : Velocity - wind;
                if (air.sqrMagnitude < 1) air = Velocity;
                Vector3 flat = new Vector3(air.x, 0, air.z).normalized;
                float yawRate = Vector3.SignedAngle(PriorAirHeading, flat, Vector3.up) * Mathf.Deg2Rad;
                float bank = OnGround ? 0 : Mathf.Clamp(-Mathf.Atan2(yawRate * air.magnitude / Mathf.Max(dt, 0.01f), 9.81f) * Mathf.Rad2Deg, -28, 28);
                PriorAirHeading=flat;
                float pitch = OnGround ? (taildragger ? Mathf.Lerp(10, 0, Speed / 20) : 0) : 3;
                Quaternion target = Quaternion.LookRotation(air.normalized, Vector3.up) * Quaternion.Euler(-pitch, 0, bank);
                attitude = Started ? Quaternion.RotateTowards(attitude, target, dt * 35) : target;
                Started = true;
            }
            transform.SetPositionAndRotation(Position, attitude);
            if(visual!=null && visual!=transform)visual.localPosition=Vector3.up*(OnGround && taildragger ? Mathf.Lerp(taildraggerRestOffset,0,Speed/20) : 0);
            float distance = Mathf.Max(1, Vector3.Distance(observer, Position));
            // Span-based angular size keeps thin sailplanes from disappearing on height-only LOD.
            float angular = spanMeters / distance;
            int next = angular > (Lod == 0 ? 0.024f : 0.029f) ? 0 : angular > (Lod <= 1 ? 0.005f : 0.006f) ? 1 : 2;
            if (next != Lod)
            {
                Lod = next;
                for (int i = 0; i < lodRoots.Length; i++) if (lodRoots[i] != null) lodRoots[i].SetActive(i == Lod);
            }
            float rpm = sailplane || Phase == AmbientFlightPhase.Parked ? 0 : Phase == AmbientFlightPhase.Starting ? 600 : Phase == AmbientFlightPhase.Shutdown ? 200 : OnGround ? 850 : 2200;
            foreach (Transform p in propellers) if (p != null) p.Rotate(Vector3.forward, rpm * 6 * dt, Space.Self);
            float flap = Phase == AmbientFlightPhase.Approach || Phase == AmbientFlightPhase.Landing || Phase == AmbientFlightPhase.Rollout ? 25 : Phase == AmbientFlightPhase.Takeoff ? 10 : 0;
            foreach (Transform f in flaps) if (f != null) f.localRotation = Quaternion.RotateTowards(f.localRotation, Quaternion.Euler(-flap, 0, 0), 15 * dt);
            SetVisible(!Suppressed);
        }
    }
}
