using QuestFlightLab.Flight.Backends;
using QuestFlight.Product;
using Unity.Profiling;
using UnityEngine;

namespace QuestFlightLab.Environment
{
    /// <summary>Bounded traffic scheduling, independent of visibility. All actors and routes are authored.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class AmbientTrafficDirector : MonoBehaviour
    {
        public FlightDynamicsCoordinator player;
        public ProductFlightModeCoordinator flightMode;
        public BoulderWindFieldProfile windField;
        public ProductionTerrainSurfaceAsset terrain;
        public Camera pilotCamera;
        public AmbientAircraftActor[] actors = System.Array.Empty<AmbientAircraftActor>();
        public bool trafficEnabled = true;
        public int EmergencySuppressions { get; private set; }
        public int RunwayOwner => runwayOwner;
        public bool PlayerProtectsRunway { get; private set; }
        private int runwayOwner = -1;
        private int groundMovementOwner = -1;
        private int lastGroundMovementOwner = 2;
        public int GroundMovementOwner => groundMovementOwner;
        private bool initialized;
        private double lastTime;
        private float decisionCountdown;
        private double nextReturnTime;
        private double nextDepartureSlot;
        public bool logDiagnostics;
        private double nextDiagnosticTime;
        private readonly Plane[] frustum = new Plane[6];
        private static readonly ProfilerMarker TickMarker = new ProfilerMarker("QuestFlight.AmbientTraffic");

        private void Start()
        {
            if (!Validate(out string error)) { Debug.LogError("[AmbientTraffic] " + error, this); enabled = false; }
        }

        public bool Validate(out string error)
        {
            error = null;
            if (player == null || windField == null || terrain == null || pilotCamera == null || flightMode == null || actors.Length != 5)
                error = "Missing serialized traffic references or invalid population.";
            else if(windField!=player.windField || !windField.IsValid(out error))
                error="Traffic must share the valid player atmosphere: "+error;
            else foreach (AmbientAircraftActor a in actors)
            {
                if (a == null || a.visual == null || a.renderers.Length == 0 || a.lodRoots.Length != 3)
                { error = "Incomplete authored traffic visual."; break; }
                if (!a.sailplane && (a.runway08 == null || a.runway26 == null || !a.runway08.IsValid(out error) || !a.runway26.IsValid(out error)))
                { error = "Invalid powered traffic route: " + error; break; }
            }
            return error == null;
        }

        private void Update()
        {
                // A lesson pause freezes traffic where it is; it must not hide every aircraft.
                if (Time.timeScale <= 0 && flightMode != null &&
                    flightMode.State == ProductFlightModeCoordinator.FlightModeState.Training) return;
                if (!trafficEnabled || player == null || !player.IsInitialized || !player.CurrentState.IsFinite ||
                    Time.timeScale <= 0 || flightMode.State == ProductFlightModeCoordinator.FlightModeState.Selection ||
                    flightMode.State == ProductFlightModeCoordinator.FlightModeState.Blocked)
                { foreach (var a in actors) if (a != null) a.SetVisible(false); return; }
                StepTraffic(player.CurrentState,Mathf.Min(Time.deltaTime,.1f),player.WeatherPreset);
        }

        // The same deterministic step is used by the scene and accelerated editor scenarios.
        public void StepTraffic(FlightDynamicsState state,float dt,BoulderWeatherPreset weather)
        {
            using (TickMarker.Auto())
            {
                if (dt <= 0 || dt > .1f || !state.IsFinite) return;
                if (!initialized || state.simulationTimeSeconds < lastTime)
                {
                    runwayOwner = -1; groundMovementOwner = -1; lastGroundMovementOwner=2;
                    decisionCountdown=0; nextDepartureSlot=state.simulationTimeSeconds; EmergencySuppressions=0; nextDiagnosticTime=0;
                    Vector3 airportWind = windField.Sample(Vector3.zero, state.simulationTimeSeconds, weather);
                    int direction = airportWind.x > 1 ? 26 : 8;
                    foreach (var a in actors) a.Initialize(direction);
                    initialized = true;
                    Debug.Log($"[AmbientTraffic] Shared atmosphere active; actors={actors.Length}; runway={direction:00}; playerWind={windField.Sample(state.positionUnityMeters,state.simulationTimeSeconds)}");
                }
                lastTime = state.simulationTimeSeconds;
                if(logDiagnostics && lastTime>=nextDiagnosticTime)
                {
                    nextDiagnosticTime=lastTime+10;
                    foreach(var actor in actors)
                        Debug.Log($"[AmbientTraffic] {actor.aircraftId} phase={(actor.sailplane?actor.SoaringPhase.ToString():actor.Phase.ToString())} distance={Vector3.Distance(actor.Position,state.positionUnityMeters):F0}m suppressed={actor.Suppressed} inView={IsPilotVisible(actor)} wind={windField.Sample(actor.Position,lastTime)}");
                }
                decisionCountdown -= dt;
                bool decide = decisionCountdown <= 0;
                if (decide) decisionCountdown = 0.1f;
                PlayerProtectsRunway = ProtectsRunway(state.positionUnityMeters, state.velocityUnityMetersPerSecond, state.weightOnWheels) ||
                    (flightMode != null && flightMode.AuthoredDemoPilot != null && flightMode.AuthoredDemoPilot.ReservesRunway) ||
                    (flightMode != null && flightMode.State == ProductFlightModeCoordinator.FlightModeState.Training &&
                     flightMode.AuthoredTraining != null && flightMode.AuthoredTraining.ReservesRunway);
                if(decide) AllocateGroundMovement();
                for (int i = 0; i < actors.Length; i++)
                {
                    AmbientAircraftActor a = actors[i];
                    if (a.Suppressed)
                    {
                        if (a.Retired && decide && state.simulationTimeSeconds > nextReturnTime)
                        {
                            Vector3 entry = a.sailplane ? a.soaringCenter + new Vector3(Mathf.Cos(a.initialOrbitAngle),0,Mathf.Sin(a.initialOrbitAngle))*a.soaringRadius
                                : a.Route.segments[0].points[0];
                            bool clear = Vector3.Distance(entry,state.positionUnityMeters)>3000 && !IsPilotVisible(entry,a.spanMeters);
                            foreach(var other in actors) if(other!=a && !other.Suppressed && Vector3.Distance(entry,other.Position)<500)clear=false;
                            if(clear) {a.Initialize(a.Route==null?8:a.Route.runwayDirection,true);nextReturnTime=state.simulationTimeSeconds+30;}
                        }
                        continue;
                    }
                    Vector3 before = a.Position;
                    Vector3 wind = windField.Sample(a.Position, state.simulationTimeSeconds, weather);
                    if (!BoulderWindFieldProfile.Finite(wind)) { Suppress(i, "invalid wind"); continue; }
                    if (decide && !a.OnGround && a.Started && PredictConflict(a.Position, a.Velocity, state.positionUnityMeters, state.velocityUnityMetersPerSecond, 30, 250, 75))
                        BeginEscape(a, state.positionUnityMeters);
                    if (decide && !a.OnGround && a.Started)
                        for (int j = 0; j < i; j++)
                            if (!actors[j].Suppressed && !actors[j].OnGround && PredictConflict(a.Position, a.Velocity,
                                actors[j].Position, actors[j].Velocity, 30, 180, 60)) BeginEscape(a, actors[j].Position);
                    if (a.Escaping && runwayOwner == i && !ProtectsRunway(a.Position, a.Velocity, false)) runwayOwner = -1;
                    if (a.Escaping) AdvanceEscape(a, wind, dt);
                    else if (a.sailplane) AdvanceSoaring(a, wind, dt, weather, state.simulationTimeSeconds);
                    else AdvancePowered(i, state, wind, dt);
                    a.Velocity = (a.Position - before) / Mathf.Max(0.0001f, dt);
                    // Last-resort full swept envelopes, not discrete center-point tests.
                    if (SweptOverlap(before, a.Position, state.positionUnityMeters - state.velocityUnityMetersPerSecond * dt,
                        state.positionUnityMeters, a.bodyRadius + 7, a.OnGround ? 4 : 6))
                    { Suppress(i, "unavoidable player intersection"); continue; }
                    for (int j = 0; j < i; j++)
                        if (!actors[j].Suppressed && SweptOverlap(before, a.Position, actors[j].Position - actors[j].Velocity * dt,
                            actors[j].Position, a.bodyRadius + actors[j].bodyRadius, 5)) { Suppress(i, "traffic intersection"); break; }
                    if (terrain.TrySample(a.Position.x, a.Position.z, out ProductionTerrainSample ground) && !a.OnGround && a.Position.y < ground.heightMeters + 15 &&
                        (a.sailplane || a.Escaping)) { BeginEscape(a, state.positionUnityMeters); if (a.Position.y < ground.heightMeters + 3) Suppress(i, "terrain clearance"); }
                    if (a.Escaping && a.EscapeTime > 60 && Vector3.Distance(a.Position, state.positionUnityMeters) > 5000 && !IsPilotVisible(a) && !ProtectsRunway(a.Position,a.Velocity,false))
                    {
                        a.Retired = true; a.Suppressed = true; a.SetVisible(false); nextReturnTime=state.simulationTimeSeconds+30;
                        if(runwayOwner==i)runwayOwner=-1;
                    }
                    a.Present(dt, wind, state.positionUnityMeters, pilotCamera);
                }
            }
        }

        private static bool NeedsTaxiConnector(AmbientAircraftActor a)
        {
            if(a.sailplane || a.Suppressed || a.Escaping || !a.OnGround)return false;
            var leg=a.Route.segments[a.SegmentIndex];
            return leg.sharedTaxiStart>=0 && a.Along>=Mathf.Max(0,leg.sharedTaxiStart-30) && a.Along<leg.sharedTaxiEnd;
        }

        // Serialize the opposing apron/connector only. Same-direction aircraft
        // may follow on taxiway A using the existing swept stopping envelopes.
        private void AllocateGroundMovement()
        {
            if(groundMovementOwner>=0 && !NeedsTaxiConnector(actors[groundMovementOwner]))
            {lastGroundMovementOwner=groundMovementOwner;groundMovementOwner=-1;}
            if(groundMovementOwner>=0)return;
            for(int i=0;i<actors.Length;i++)
                if(NeedsTaxiConnector(actors[i]) && actors[i].Phase==AmbientFlightPhase.TaxiIn)
                {groundMovementOwner=i;return;}
            for(int offset=1;offset<=actors.Length;offset++)
            {
                int i=(lastGroundMovementOwner+offset)%actors.Length;
                var a=actors[i];
                if(!NeedsTaxiConnector(a) || a.Phase!=AmbientFlightPhase.TaxiOut || a.Dwell>0)continue;
                // Runway 26 uses the same narrow taxiway in opposing directions.
                // Do not send an outbound aircraft into an arriving aircraft's exit.
                if(a.Route.runwayDirection==26 && (ArrivalDueWithin(420,i) ||
                    (runwayOwner>=0 && actors[runwayOwner].Phase==AmbientFlightPhase.Rollout)))continue;
                groundMovementOwner=i;return;
            }
        }

        private bool ArrivalDueWithin(float seconds,int except)
        {
            for(int i=0;i<actors.Length;i++)
            {
                var other=actors[i];
                if(i==except || other.sailplane || other.Suppressed || other.Escaping || other.OnGround)continue;
                if(other.Phase==AmbientFlightPhase.Approach || other.Phase==AmbientFlightPhase.Landing)return true;
                if(other.Phase==AmbientFlightPhase.Circuit)
                {
                    var segment=other.Route.segments[other.SegmentIndex];
                    float eta=(segment.Length-other.Along)/Mathf.Max(15,other.Speed);
                    if(eta<seconds)return true;
                }
            }
            return false;
        }

        private void AdvancePowered(int index, FlightDynamicsState state, Vector3 wind, float dt)
        {
            AmbientAircraftActor a = actors[index];
            AmbientAircraftRoute.Segment s = a.Route.segments[a.SegmentIndex];
            bool groundRouteBlocked=NeedsTaxiConnector(a) && groundMovementOwner!=index;
            a.BlockingReason=groundRouteBlocked ? "shared apron connector" : "";
            if (s.protectsRunway && runwayOwner != index)
            {
                int airborne = 0;
                foreach (var other in actors) if (!other.Suppressed && !other.OnGround) airborne++;
                bool opposingExitOccupied=!s.onGround && a.Route.runwayDirection==26 && groundMovementOwner>=0 &&
                    actors[groundMovementOwner].Phase==AmbientFlightPhase.TaxiOut;
                if (runwayOwner != -1 || PlayerProtectsRunway || opposingExitOccupied || (s.onGround && (airborne >= 4 || ArrivalDueWithin(85,index) || (s.phase==AmbientFlightPhase.LineUp && lastTime<nextDepartureSlot))))
                {
                    if (!s.onGround) { BeginEscape(a, state.positionUnityMeters); AdvanceEscape(a,wind,dt); }
                    else { a.Speed = 0; a.BlockingReason="runway protected"; }
                    return;
                }
                runwayOwner = index;
            }
            if (!s.protectsRunway && runwayOwner == index) runwayOwner = -1;
            bool runwayBlocked = s.protectsRunway && PlayerProtectsRunway && (s.phase == AmbientFlightPhase.LineUp ||
                s.phase == AmbientFlightPhase.Takeoff);
            if (a.Dwell > 0) { a.Dwell -= dt; a.Speed = 0; return; }
            float u = Mathf.Clamp01(a.Along / Mathf.Max(1, s.Length));
            float speed = Mathf.Lerp(s.startSpeed, s.endSpeed, BoulderWindFieldProfile.Smooth(u));
            if(s.phase==AmbientFlightPhase.TaxiOut || s.phase==AmbientFlightPhase.TaxiIn)
                speed=Mathf.Min(s.startSpeed,Mathf.Max(.05f,Mathf.Sqrt(1.4f*Mathf.Max(0,s.Length-a.Along))));
            if (s.phase == AmbientFlightPhase.Takeoff) speed = s.endSpeed;
            s.Position(a.Along, out Vector3 tangent);
            if (!s.onGround) speed = Mathf.Max(15, speed + Vector3.Dot(wind, tangent));
            bool blocked = runwayBlocked || groundRouteBlocked;
            if (s.onGround)
            {
                float stopping = a.Speed * a.Speed / 3 + a.Speed * 0.2f + 18;
                Vector3 ahead = s.Position(Mathf.Min(s.Length, a.Along + stopping), out _);
                blocked |= SegmentNear(before: a.Position, after: ahead, state.positionUnityMeters, a.bodyRadius + 9, 5);
                for (int j = 0; j < actors.Length && !blocked; j++)
                    if (j != index && !actors[j].Suppressed && actors[j].OnGround)
                        blocked = SegmentNear(a.Position, ahead, actors[j].Position, a.bodyRadius + actors[j].bodyRadius + 3, 5);
            }
            if (blocked) { speed = 0; if(a.BlockingReason.Length==0)a.BlockingReason="player or ground traffic"; }
            a.Speed = Mathf.MoveTowards(a.Speed, speed, dt * (blocked ? 2.5f : s.onGround ? 1.8f : 2));
            float beforeAlong=a.Along;
            a.Along = Mathf.Min(s.Length, a.Along + a.Speed * dt);
            a.Position = s.Position(a.Along, out _);
            if (a.Along >= s.Length - 0.001f && !blocked)
            {
                a.SegmentIndex = (a.SegmentIndex + 1) % a.Route.segments.Length;
                a.Along = 0; a.Dwell = a.Route.segments[a.SegmentIndex].dwellSeconds;
                if(a.Phase==AmbientFlightPhase.Takeoff) // Reserve roughly 160 seconds between departures including the
                // authored 30-second line-up, enough for the full arrival/rollout.
                nextDepartureSlot=lastTime+130;
                float left=dt-(s.Length-beforeAlong)/Mathf.Max(.001f,a.Speed);
                if(left>.00001f && a.Dwell<=0 && s.Length>.001f)AdvancePowered(index,state,wind,left);
            }
        }

        private void AdvanceSoaring(AmbientAircraftActor a, Vector3 wind, float dt, BoulderWeatherPreset weather, double time)
        {
            var components=windField.SampleComponents(a.Position,time);
            float nominalSink=.67f+Mathf.Pow((a.airspeed-27)/18,2);
            a.SoaringDecisionTime-=dt;
            a.RidgeTurnTime=Mathf.Max(0,a.RidgeTurnTime-dt);
            if(a.SoaringDecisionTime<=0)
            {
                a.SoaringDecisionTime=.2f;
                var thermal=a.mountainSoaring && windField.foothillThermals.Length>0 ? windField.foothillThermals[0] : windField.AirportThermal;
                Vector2 tc=BoulderWindFieldProfile.ThermalCenter(thermal,a.Position.y,new Vector2(components.background.x,components.background.z));
                Vector3 center=new Vector3(tc.x,a.Position.y,tc.y);
                Vector3 radial=a.Position-center;radial.y=0;
                float threshold=a.SoaringPhase==AmbientSoaringPhase.Thermal ? nominalSink-.2f : nominalSink+.2f;
                if(windField.thermalStrength>0 && components.thermal>threshold && radial.magnitude<thermal.radius)
                {
                    a.SoaringPhase=AmbientSoaringPhase.Thermal;
                    Vector3 outward=radial.sqrMagnitude>1?radial.normalized:Vector3.right;
                    a.SoaringDesired=Vector3.Cross(outward,Vector3.up)-outward*Mathf.Clamp((radial.magnitude-a.soaringRadius)/a.soaringRadius,-.8f,.8f);
                }
                else if(a.mountainSoaring && terrain.TrySample(a.Position.x,a.Position.z,out var ground) &&
                    (components.terrain>nominalSink+.15f || (a.SoaringPhase==AmbientSoaringPhase.Ridge && a.LiftLossTime<20)))
                {
                    a.SoaringPhase=AmbientSoaringPhase.Ridge;
                    Vector3 outward=new Vector3(ground.normal.x,0,ground.normal.z).normalized;
                    if(outward.sqrMagnitude<.1f)outward=-new Vector3(components.background.x,0,components.background.z).normalized;
                    Vector3 along=Vector3.Cross(outward,Vector3.up);
                    if(along.z*a.RidgeDirection<0)along=-along;
                    if(a.RidgeTurnTime<=0 && (a.Position.z-a.soaringCenter.z)*a.RidgeDirection>450)
                    {a.RidgeDirection=-a.RidgeDirection;a.RidgeTurnTime=7;a.RidgeTurns++;}
                    if(a.RidgeTurnTime>0) a.SoaringDesired=outward;
                    else
                    {
                        // Select a nearby better lift strip, with terrain clearance checked at the candidate.
                        float best=components.terrain;float offset=0;
                        for(int k=-2;k<=2;k++)
                        {
                            Vector3 probe=a.Position+outward*(k*60);
                            if(!terrain.TrySample(probe.x,probe.z,out var pg) || probe.y-pg.heightMeters<90)continue;
                            float score=windField.SampleComponents(probe,time).terrain-Mathf.Abs(k)*.025f;
                            if(score>best){best=score;offset=k*.25f;}
                        }
                        a.SoaringDesired=along+outward*offset;
                    }
                }
                else
                {
                    a.SoaringPhase=AmbientSoaringPhase.Glide;
                    // A nearby reachable thermal is a navigation target, not permission to invent lift.
                    a.SoaringDesired=windField.thermalStrength>0 && radial.magnitude<1800 ? -radial : Vector3.right;
                }
            }
            a.LiftLossTime=components.terrain<nominalSink-.15f?a.LiftLossTime+dt:0;
            Vector3 desired=a.SoaringDesired;
            Vector3 horizontalWind=new Vector3(wind.x,0,wind.z);
            Vector3 target=(desired.normalized*a.airspeed-horizontalWind).normalized;
            float turnRate=9.81f*Mathf.Tan(30*Mathf.Deg2Rad)/a.airspeed;
            Vector3 heading=Vector3.RotateTowards(a.AirHeading,target,turnRate*dt,0);
            float bank=Mathf.Atan2(Vector3.Angle(a.AirHeading,heading)*Mathf.Deg2Rad*a.airspeed/Mathf.Max(dt,.001f),9.81f);
            a.AirHeading=heading;
            float sink=(.67f+Mathf.Pow((a.airspeed-27)/18,2))/Mathf.Pow(Mathf.Max(.7f,Mathf.Cos(bank)),1.5f);
            a.LastSink=sink;
            a.Position += (heading*a.airspeed+horizontalWind+Vector3.up*(wind.y-sink))*dt;
            a.Speed = a.airspeed;
            Vector3 ahead=a.Position+heading*200;
            bool low=terrain.TrySample(ahead.x,ahead.z,out var aheadGround) && a.Position.y-aheadGround.heightMeters<75;
            if (low || a.Position.y < a.soaringCenter.y - 180 || a.Position.y > a.soaringCenter.y + 400)
                BeginEscape(a, a.soaringCenter + Vector3.left * 1000);
        }

        private void BeginEscape(AmbientAircraftActor a, Vector3 threat)
        {
            if (a.Escaping) return;
            a.Escaping = true; a.EscapeTime = 0; a.EscapeSpeedInitialized=false;
            a.EscapeVerticalSpeed=a.Velocity.y;
            a.SoaringPhase=AmbientSoaringPhase.Exit;
            for(int i=0;i<actors.Length;i++)if(actors[i]==a && groundMovementOwner==i)groundMovementOwner=-1;
            Vector3 away = a.Position - threat; away.y = 0;
            // Prefer eastward open terrain for mountain gliders; never climb them with an engine.
            if (a.sailplane) away = (Vector3.right + away.normalized * 0.4f).normalized;
            a.EscapeDirection = away.sqrMagnitude > .001f ? away.normalized : Vector3.right;
        }

        private void AdvanceEscape(AmbientAircraftActor a, Vector3 wind, float dt)
        {
            a.EscapeTime += dt;
            Vector3 forward = a.Velocity-wind; forward.y = 0;
            if(!a.EscapeSpeedInitialized){a.Speed=forward.magnitude;a.EscapeSpeedInitialized=true;}
            if (forward.sqrMagnitude < 1) forward = a.transform.forward;
            Vector3 heading = Vector3.RotateTowards(forward.normalized, a.EscapeDirection, dt * (a.sailplane ? .24f : .14f), 0);
            a.Speed = Mathf.MoveTowards(a.Speed, a.sailplane ? 30 : 40, dt * 2);
            a.EscapeVerticalSpeed=Mathf.MoveTowards(a.EscapeVerticalSpeed,wind.y+(a.sailplane?-.85f:2.5f),dt*1.5f);
            a.Position += (heading * a.Speed + new Vector3(wind.x,a.EscapeVerticalSpeed,wind.z)) * dt;
        }

        private void Suppress(int index, string reason)
        {
            actors[index].Suppressed = true; actors[index].SetVisible(false);
            if (runwayOwner == index) runwayOwner = -1;
            if (groundMovementOwner == index) groundMovementOwner = -1;
            EmergencySuppressions++;
            Debug.LogWarning("[AmbientTraffic] Emergency suppression " + actors[index].aircraftId + ": " + reason, this);
        }

        private bool IsPilotVisible(AmbientAircraftActor a)
            => IsPilotVisible(a.Position,a.spanMeters);

        private bool IsPilotVisible(Vector3 position,float span)
        {
            Bounds b = new Bounds(position, Vector3.one * (span + 20));
            if (!pilotCamera.stereoEnabled) { GeometryUtility.CalculateFrustumPlanes(pilotCamera, frustum); return GeometryUtility.TestPlanesAABB(frustum, b); }
            for (int eye = 0; eye < 2; eye++)
            {
                var e = (Camera.StereoscopicEye)eye;
                GeometryUtility.CalculateFrustumPlanes(pilotCamera.GetStereoProjectionMatrix(e) * pilotCamera.GetStereoViewMatrix(e), frustum);
                if (GeometryUtility.TestPlanesAABB(frustum, b)) return true;
            }
            return false;
        }

        public static bool ProtectsRunway(Vector3 position, Vector3 velocity, bool onGround)
        {
            if (position.y > 180) return false;
            if (Mathf.Abs(position.x) < 780 && position.z > -110 && position.z < 95 && position.y < 65) return true;
            Vector3 next = position + velocity * 30;
            bool aligned = Mathf.Abs(velocity.x) > Mathf.Abs(velocity.z) * 1.5f;
            return aligned && Mathf.Abs(position.x) < 2700 && SegmentNear(position, next, new Vector3(0, position.y, -30), 180, 200);
        }

        public static bool PredictConflict(Vector3 a, Vector3 va, Vector3 b, Vector3 vb, float horizon, float horizontal, float vertical)
        {
            Vector3 r = a - b, v = va - vb;
            // Intersect the time intervals inside a horizontal cylinder and a vertical slab.
            // Horizontal CPA alone misses a vertically descending aircraft with equal ground velocity.
            float enter = 0, leave = horizon;
            float aa = v.x * v.x + v.z * v.z;
            float bb = 2 * (r.x * v.x + r.z * v.z);
            float cc = r.x * r.x + r.z * r.z - horizontal * horizontal;
            if (aa < 0.000001f) { if (cc > 0) return false; }
            else
            {
                float discriminant = bb * bb - 4 * aa * cc;
                if (discriminant < 0) return false;
                float root = Mathf.Sqrt(discriminant);
                enter = Mathf.Max(enter, (-bb - root) / (2 * aa));
                leave = Mathf.Min(leave, (-bb + root) / (2 * aa));
            }
            if (Mathf.Abs(v.y) < 0.000001f) { if (Mathf.Abs(r.y) > vertical) return false; }
            else
            {
                float t0 = (-vertical - r.y) / v.y, t1 = (vertical - r.y) / v.y;
                enter = Mathf.Max(enter, Mathf.Min(t0, t1));
                leave = Mathf.Min(leave, Mathf.Max(t0, t1));
            }
            return enter <= leave;
        }

        public static bool SweptOverlap(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, float horizontal, float vertical) =>
            PredictConflict(a0, a1 - a0, b0, b1 - b0, 1, horizontal, vertical);

        public static bool SegmentNear(Vector3 before, Vector3 after, Vector3 point, float radius, float vertical) =>
            SweptOverlap(before, after, point, point, radius, vertical);
    }
}
