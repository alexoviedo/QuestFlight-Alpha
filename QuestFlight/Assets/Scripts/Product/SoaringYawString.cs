using UnityEngine;

namespace QuestFlight.Product
{
    // Presentation only. Native JSBSim remains the sole flight authority.
    // Four inextensible links, five triangular rings: same 24 triangles as the
    // old rigid rod, no additional renderer, solver, collider or frame allocation.
    public sealed class SoaringYawString
    {
        public const int Segments = 4, Sides = 3, VertexCount = 15;
        public const float Length = .08f, Radius = .0012f;
        private readonly float[] angles = new float[Segments];
        private readonly Vector3[] nodes = new Vector3[Segments + 1];
        private double lastTime = double.NaN;
        public Vector3 Tip => nodes[Segments];
        public bool HasSample => !double.IsNaN(lastTime);
        public void Reset() { lastTime = double.NaN; }

        // JSBSim beta is positive for body velocity to the right; air (and yarn)
        // goes left. Convert forward/right/down air velocity to Unity axes.
        public static Vector3 LocalAirflow(float tas, float alpha, float beta,
            Vector3 bodyRatesDegrees, Vector3 attachmentFromCg)
        {
            float a = alpha * Mathf.Deg2Rad, b = beta * Mathf.Deg2Rad;
            Vector3 velocity = tas * new Vector3(Mathf.Sin(b), -Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a) * Mathf.Cos(b));
            return -velocity - Vector3.Cross(bodyRatesDegrees * Mathf.Deg2Rad, attachmentFromCg);
        }

        public static Vector3 AftTangent(Vector3 normal) => Vector3.ProjectOnPlane(Vector3.back, normal).normalized;

        public bool Sample(double time, Vector3 airflow, Vector3 gravity, Vector3 anchor, Vector3 normal)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || !Finite(airflow) || !Finite(gravity)) return false;
            bool reset = double.IsNaN(lastTime) || time < lastTime;
            if (!reset && time == lastTime) return false; // pause/hold/repeated render frame
            float dt = reset ? 0 : (float)(time - lastTime);
            lastTime = time;
            normal.Normalize();
            Vector3 aft = AftTangent(normal), right = Vector3.Cross(aft, normal).normalized;
            Vector3 tangentFlow = Vector3.ProjectOnPlane(airflow, normal);
            float speed = tangentFlow.magnitude;
            // Drag scales with speed squared. At rest yarn lies downhill on the
            // glazing; above a few m/s airflow dominates its very small weight.
            Vector3 force = tangentFlow * speed + Vector3.ProjectOnPlane(gravity, normal) * .12f;
            float target = force.sqrMagnitude > 1e-8f ? Mathf.Atan2(Vector3.Dot(force, right), Vector3.Dot(force, aft)) * Mathf.Rad2Deg : 0;
            float tension = Mathf.Clamp01(speed / 12f);
            nodes[0] = anchor;
            float phase = (float)(time % 1000);
            for (int j = 0; j < Segments; j++)
            {
                // Small bounded, zero-mean fibre flutter, not a claimed CFD
                // turbulence solution. No motion at zero flow or paused time.
                float flutter = tension * (j + 1) / Segments *
                    (.55f * Mathf.Sin(phase * 37f - j * .8f) + .25f * Mathf.Sin(phase * 53f - j * 1.1f));
                float wanted = target + flutter;
                float tau = Mathf.Lerp(.24f, .055f, tension) * (1f + j * .45f);
                angles[j] = reset ? target : angles[j] + Mathf.DeltaAngle(angles[j], wanted) * (1f - Mathf.Exp(-dt / tau));
                float angle = angles[j] * Mathf.Deg2Rad;
                nodes[j + 1] = nodes[j] + (aft * Mathf.Cos(angle) + right * Mathf.Sin(angle)) * (Length / Segments);
            }
            return true;
        }

        public void WriteVertices(SoaringMovingPart part, Vector3[] rest, Vector3[] restNormals, Vector3[] vertices, Vector3[] normals)
        {
            if (part.vertexCount != VertexCount) return; // stale authoring fails visibly in editor validation
            Vector3 aft = AftTangent(part.rotationAxis.normalized);
            for (int ring = 0; ring <= Segments; ring++)
            {
                Vector3 direction = ring == Segments ? nodes[ring] - nodes[ring - 1] : nodes[ring + 1] - nodes[ring];
                Quaternion rotation = Quaternion.FromToRotation(aft, direction);
                Vector3 restCenter = part.anchor + aft * (Length * ring / Segments);
                for (int side = 0; side < Sides; side++)
                {
                    int i = part.firstVertex + ring * Sides + side;
                    vertices[i] = nodes[ring] + rotation * (rest[i] - restCenter);
                    normals[i] = rotation * restNormals[i];
                }
            }
        }
        private static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }
}
