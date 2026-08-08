using QuestFlightLab.Input;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlightLab.Flight
{
    public class SimpleAircraftPhysics : MonoBehaviour
    {
        private const float LiftoffExcessForceFraction = 0.05f;
        private const float LiftoffMinimumPitchDegrees = 2f;
        public ProductControlInput controls;
        public AircraftState state;
        public C172StyleAircraftConfig config;
        public Vector3 runwayResetPosition = new Vector3(0f, 1.25f, -520f);
        public Vector3 runwayResetEuler = new Vector3(0f, 78f, 0f);
        public float initialForwardSpeedKts = 0f;
        public float groundHeightMeters = 1.25f;

        private Vector3 _velocityWorld;
        private Vector3 _angularVelocityDeg;
        private Vector3 _lastPosition;
        private Vector3 _runwayForward;
        private Vector3 _runwayRight;

        private void Awake()
        {
            if (controls == null || state == null || config == null)
            {
                Debug.LogError("[QuestFlight][PrototypePhysics] Serialized controls, state, and config are required.", this);
                enabled = false;
                return;
            }
            state.config = config;
        }

        private void Start()
        {
            ResetToRunway();
        }

        private void FixedUpdate()
        {
            if (controls != null && controls.ConsumeResetRequest())
            {
                ResetToRunway();
                return;
            }

            if (controls != null && controls.Paused) return;

            AircraftControlState c = controls != null ? controls.Current : AircraftControlState.Neutral();
            StepPhysics(c, Time.fixedDeltaTime);
        }

        public void ResetToRunway()
        {
            transform.SetPositionAndRotation(runwayResetPosition, Quaternion.Euler(runwayResetEuler));
            _velocityWorld = transform.forward * (initialForwardSpeedKts * AircraftUnitConversions.KnotsToMetersPerSecond);
            _angularVelocityDeg = Vector3.zero;
            _lastPosition = transform.position;
            _runwayForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            _runwayRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            state.ResetState(_velocityWorld);
        }

        public void SetStateForTest(Vector3 position, Vector3 euler, Vector3 velocityWorld)
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(euler));
            _velocityWorld = velocityWorld;
            _angularVelocityDeg = Vector3.zero;
            _lastPosition = position;
            _runwayForward = Vector3.ProjectOnPlane(Quaternion.Euler(runwayResetEuler) * Vector3.forward, Vector3.up).normalized;
            _runwayRight = Vector3.ProjectOnPlane(Quaternion.Euler(runwayResetEuler) * Vector3.right, Vector3.up).normalized;
            state.ResetState(_velocityWorld);
            state.onGround = position.y <= groundHeightMeters + 0.05f;
            state.RefreshFromTransform(transform);
        }

        public void StepSimulation(AircraftControlState c, float dt)
        {
            if (dt <= 0f) return;
            c ??= AircraftControlState.Neutral();

            C172ReferenceSpeeds speeds = config.Speeds;
            AeroCoefficients aero = config.Aero;
            EnginePropModelConfig engine = config.Engine;
            ControlStabilityConfig control = config.Control;
            LandingGearConfig gear = config.Gear;

            float mass = Mathf.Max(1f, speeds.maxGrossWeightKg > 0f ? speeds.maxGrossWeightKg : config.massKg);
            float speed = _velocityWorld.magnitude;
            Vector3 localVelocity = transform.InverseTransformDirection(_velocityWorld);
            float forwardSpeed = Mathf.Max(0.1f, Mathf.Abs(localVelocity.z));
            float aoaRad = Mathf.Atan2(-localVelocity.y, forwardSpeed);
            float aoaDeg = aoaRad * Mathf.Rad2Deg;
            float flapDeg = GetFlapDegrees(c.flaps);
            float flapFraction = Mathf.InverseLerp(0f, 30f, flapDeg);
            float trim = Mathf.Clamp(c.trim, -control.maxTrim, control.maxTrim);
            float effectiveAoADeg = aoaDeg
                                    + c.elevator * control.elevatorAoAAuthorityDeg
                                    + trim * control.trimAoAAuthorityDeg;

            float q = 0.5f * aero.airDensityKgM3 * speed * speed;
            float liftCoefficient = ComputeLiftCoefficient(effectiveAoADeg, flapFraction, out bool stalledByAoA);
            float stallKts = Mathf.Lerp(speeds.stallCleanKts, speeds.stallLandingKts, flapFraction);
            bool evaluateStall = !state.onGround || transform.position.y > groundHeightMeters + 0.5f;
            bool lowSpeedWarning = evaluateStall && state.airspeedKts > 8f && state.airspeedKts < stallKts + aero.stallWarningSpeedMarginKts;
            bool aoaWarning = evaluateStall && effectiveAoADeg > aero.stallWarningAoADeg;
            bool stall = evaluateStall && state.airspeedKts > 8f && (state.airspeedKts < stallKts * 0.98f || stalledByAoA);
            float stallIntensity = Mathf.Clamp01(Mathf.Max(
                Mathf.InverseLerp(stallKts + aero.stallWarningSpeedMarginKts, stallKts * 0.85f, state.airspeedKts),
                Mathf.InverseLerp(aero.stallWarningAoADeg, aero.stallBreakAoADeg + 4f, effectiveAoADeg)));
            if (!evaluateStall) stallIntensity = 0f;
            if (stall) liftCoefficient *= Mathf.Lerp(1f, aero.postStallLiftMultiplier, Mathf.Max(0.45f, stallIntensity));

            float sideSlip = Mathf.Clamp(localVelocity.x / Mathf.Max(1f, speed), -1f, 1f);
            float betaRad = Mathf.Asin(sideSlip);
            float dragCoefficient = aero.dragCoefficientBase
                                    + aero.inducedDragFactor * liftCoefficient * liftCoefficient
                                    + flapFraction * aero.flapDragBonus
                                    + Mathf.Abs(sideSlip) * aero.sideSlipDragFactor
                                    + stallIntensity * 0.08f;

            float groundEffect = state.onGround
                ? Mathf.Clamp01(1f - transform.position.y / Mathf.Max(0.1f, gear.groundEffectHeightM)) * 0.08f
                : 0f;
            Vector3 lift = transform.up * (q * config.wingAreaM2 * liftCoefficient * (1f + groundEffect));
            Vector3 drag = speed > 0.01f ? -_velocityWorld.normalized * (q * config.wingAreaM2 * dragCoefficient) : Vector3.zero;
            // The original prototype had drag from sideslip but no lateral
            // aerodynamic force, allowing the aircraft to translate sideways
            // indefinitely. Apply the C172-style stabilizing CY_beta derivative
            // opposite beta. Limit the linear derivative to the source model's
            // documented +/-20 degree table range.
            float boundedBetaRad = Mathf.Clamp(betaRad, -0.349f, 0.349f);
            Vector3 sideForce = transform.right *
                                (-q * config.wingAreaM2 *
                                 aero.sideForceCoefficientPerBetaRad *
                                 boundedBetaRad);
            Vector3 thrust = transform.forward * ComputeThrustNewtons(c, speed);
            Vector3 gravity = Physics.gravity * mass;

            Vector3 force = lift + drag + sideForce + thrust + gravity;

            if (state.onGround)
            {
                float brake = Mathf.Clamp01((c.leftToeBrake + c.rightToeBrake) * 0.5f);
                Vector3 horizontalVelocity = Vector3.ProjectOnPlane(_velocityWorld, Vector3.up);
                Vector3 lateralVelocity = Vector3.Project(horizontalVelocity, transform.right);
                Vector3 rollingResistance = horizontalVelocity.sqrMagnitude > 0.01f
                    ? -horizontalVelocity.normalized * (mass * 9.81f * gear.runwayFrictionCoefficient)
                    : Vector3.zero;
                Vector3 braking = horizontalVelocity.sqrMagnitude > 0.01f
                    ? -horizontalVelocity.normalized * (brake * gear.brakeStrengthNewtons)
                    : Vector3.zero;
                Vector3 lateralFriction = -lateralVelocity * (mass * gear.lateralGroundFriction);
                force += rollingResistance + braking + lateralFriction;

                if (speed < 0.4f && c.throttle < 0.05f && brake > 0.2f)
                {
                    force = new Vector3(0f, force.y, 0f);
                    _velocityWorld = Vector3.zero;
                }
            }

            bool supportedGroundContact = state.onGround;
            bool aerodynamicLiftoff = supportedGroundContact &&
                state.airspeedKts >= speeds.rotationKts - 2f &&
                state.pitchDeg >= LiftoffMinimumPitchDegrees &&
                force.y >= mass * 9.81f * LiftoffExcessForceFraction;

            // The runway supplies whatever upward normal force is necessary while
            // the wheels are loaded. Do not inject an artificial climb velocity:
            // contact is released only after the authored rotation attitude and
            // aerodynamic force can sustain a positive departure.
            if (supportedGroundContact && !aerodynamicLiftoff)
            {
                force.y = 0f;
                _velocityWorld.y = 0f;
                transform.position = new Vector3(
                    transform.position.x,
                    groundHeightMeters,
                    transform.position.z);
            }

            _velocityWorld += force / mass * dt;

            float airAuthority = Mathf.InverseLerp(22f, 75f, state.airspeedKts);
            float groundAuthority = state.onGround ? Mathf.InverseLerp(4f, 28f, state.airspeedKts) : 0f;
            float authority = Mathf.Clamp01(Mathf.Max(airAuthority, groundAuthority * 0.65f));
            float bankDeg = AircraftState.NormalizeAngle(transform.eulerAngles.z);
            float coordinatedYaw = 0f;
            if (speed > 8f)
            {
                coordinatedYaw = Mathf.Sin(-bankDeg * Mathf.Deg2Rad) *
                                 9.81f / speed * Mathf.Rad2Deg *
                                 control.coordinatedTurnCoupling;
            }

            Vector3 localAngularTarget = new Vector3(
                -(c.elevator + trim * 0.35f) * control.pitchRateMaxDegPerSec * authority - flapFraction * control.flapPitchMomentDegPerSec,
                (c.rudder * control.yawRateMaxDegPerSec * Mathf.Max(authority, groundAuthority) + coordinatedYaw),
                -c.aileron * control.rollRateMaxDegPerSec * authority);

            float pitchDeg = state.pitchDeg;
            localAngularTarget.x += pitchDeg * control.pitchAttitudeStability;
            localAngularTarget.z += -bankDeg * control.rollAttitudeStability;

            if (!state.onGround && c.throttle > 0.7f && state.airspeedKts > speeds.rotationKts)
            {
                float target = speeds.bestRateClimbKts;
                float speedError = Mathf.Clamp(state.airspeedKts - target, -20f, 20f);
                localAngularTarget.x += -speedError * 0.16f;
            }
            else if (!state.onGround && c.throttle > 0.85f && flapFraction > 0.2f && state.airspeedKts > speeds.stallLandingKts + 10f)
            {
                localAngularTarget.x += -2.2f;
            }

            if (!state.onGround)
            {
                if (pitchDeg > control.maxPrototypePitchDeg)
                {
                    localAngularTarget.x = Mathf.Max(localAngularTarget.x, (pitchDeg - control.maxPrototypePitchDeg) * 2f);
                }
                else if (pitchDeg < -control.maxPrototypePitchDeg)
                {
                    localAngularTarget.x = Mathf.Min(localAngularTarget.x, (pitchDeg + control.maxPrototypePitchDeg) * 2f);
                }

                if (bankDeg > control.maxPrototypeBankDeg)
                {
                    localAngularTarget.z = Mathf.Min(localAngularTarget.z, -(bankDeg - control.maxPrototypeBankDeg) * 2f);
                }
                else if (bankDeg < -control.maxPrototypeBankDeg)
                {
                    localAngularTarget.z = Mathf.Max(localAngularTarget.z, (-bankDeg - control.maxPrototypeBankDeg) * 2f);
                }
            }

            if (state.onGround)
            {
                bool canRotate = state.airspeedKts >= speeds.rotationKts - 4f &&
                                 c.elevator >= 0.08f;
                localAngularTarget.x = canRotate
                    ? Mathf.Clamp(
                        -c.elevator * control.pitchRateMaxDegPerSec *
                        Mathf.Max(authority, 0.35f),
                        -12f,
                        0f)
                    : 0f;
                localAngularTarget.z = 0f;
            }

            Vector3 damping = new Vector3(control.pitchDamping, control.yawDamping, control.rollDamping);
            _angularVelocityDeg.x = Mathf.Lerp(_angularVelocityDeg.x, localAngularTarget.x, dt * damping.x);
            _angularVelocityDeg.y = Mathf.Lerp(_angularVelocityDeg.y, localAngularTarget.y, dt * damping.y);
            _angularVelocityDeg.z = Mathf.Lerp(_angularVelocityDeg.z, localAngularTarget.z, dt * damping.z);

            if (state.onGround && state.airspeedKts < 45f)
            {
                _angularVelocityDeg.x = Mathf.Min(_angularVelocityDeg.x, 5f);
                _angularVelocityDeg.z *= 0.15f;
                _angularVelocityDeg.y += c.rudder * gear.nosewheelSteerDegPerSec * groundAuthority * dt;
            }

            Quaternion delta = Quaternion.Euler(_angularVelocityDeg * dt);
            transform.rotation = transform.rotation * delta;

            if (state.onGround)
            {
                float pitch = AircraftState.NormalizeAngle(transform.eulerAngles.x);
                pitch = Mathf.Clamp(pitch, -9f, 1.5f);
                transform.rotation = Quaternion.Euler(pitch, transform.eulerAngles.y, 0f);
            }

            transform.position += _velocityWorld * dt;

            if (supportedGroundContact && !aerodynamicLiftoff)
            {
                transform.position = new Vector3(
                    transform.position.x,
                    groundHeightMeters,
                    transform.position.z);
                _velocityWorld.y = 0f;
                state.onGround = true;
            }
            else if (transform.position.y <= groundHeightMeters && _velocityWorld.y <= 0f)
            {
                transform.position = new Vector3(transform.position.x, groundHeightMeters, transform.position.z);
                if (_velocityWorld.y < 0f) _velocityWorld.y = 0f;
                state.onGround = true;
            }
            else
            {
                state.onGround = false;
            }

            state.velocityWorld = _velocityWorld;
            state.angularVelocityDeg = _angularVelocityDeg;
            state.angleOfAttackDeg = aoaDeg;
            state.stallWarning = lowSpeedWarning || aoaWarning || stall;
            state.stallIntensity = stallIntensity;
            state.slipSkid = sideSlip;
            bool approachConfigured = !state.onGround && flapFraction > 0.2f && c.throttle < 0.75f;
            state.referenceSpeedKts = state.onGround
                ? speeds.rotationKts
                : approachConfigured
                    ? speeds.approachKts
                    : c.throttle > 0.7f
                        ? speeds.bestRateClimbKts
                        : speeds.bestGlideKts;
            state.targetSpeedErrorKts = state.airspeedKts - state.referenceSpeedKts;
            state.powerPercent = ComputePowerPercent(c);
            state.engineRpm = state.powerPercent > 0.01f
                ? Mathf.Lerp(engine.idleRpm, engine.maxRpm, Mathf.Clamp01(c.throttle))
                : 0f;
            state.flapDegrees = flapDeg;
            state.trimPercent = trim;
            state.loadFactorG = Mathf.Clamp(lift.magnitude / Mathf.Max(1f, mass * 9.81f), 0f, 4f);
            state.runwayLateralOffsetMeters = Vector3.Dot(transform.position - runwayResetPosition, _runwayRight);
            if (state.onGround)
            {
                state.groundRollMeters += Vector3.Distance(
                    Vector3.ProjectOnPlane(_lastPosition, Vector3.up),
                    Vector3.ProjectOnPlane(transform.position, Vector3.up));
            }
            _lastPosition = transform.position;
            state.RefreshFromTransform(transform);
        }

        private float ComputeLiftCoefficient(float effectiveAoADeg, float flapFraction, out bool stalled)
        {
            AeroCoefficients aero = config.Aero;
            float aoaFromZeroRad = (effectiveAoADeg - aero.zeroLiftAoADeg) * Mathf.Deg2Rad;
            float raw = aero.liftCoefficientBase + aero.liftCurveSlopePerRad * aoaFromZeroRad + flapFraction * aero.flapLiftBonus;
            float critical = aero.stallBreakAoADeg + flapFraction * 2.5f;
            stalled = Mathf.Abs(effectiveAoADeg) > critical;
            float maxCl = Mathf.Lerp(aero.maximumLiftCoefficientClean, aero.maximumLiftCoefficientLanding, flapFraction);
            return Mathf.Clamp(raw, -maxCl * 0.7f, maxCl);
        }

        private float ComputeThrustNewtons(AircraftControlState c, float speedMps)
        {
            EnginePropModelConfig engine = config.Engine;
            float throttle = Mathf.Clamp01(c.throttle);
            float mixture = ComputeMixturePower(c.mixture, engine);
            float carbHeat = 1f - Mathf.Clamp01(c.carbHeat) * engine.carbHeatPowerLoss;
            float powerW = engine.maxEnginePowerHp * AircraftUnitConversions.HorsepowerToWatts * throttle * mixture * carbHeat;
            float propThrust = powerW * engine.propEfficiency / Mathf.Max(14f, speedMps);
            float staticBlend = Mathf.Lerp(engine.staticThrustNewtons, engine.maxThrustNewtons, Mathf.InverseLerp(0f, 32f, speedMps));
            return Mathf.Clamp(propThrust, 0f, staticBlend);
        }

        private float ComputePowerPercent(AircraftControlState c)
        {
            EnginePropModelConfig engine = config.Engine;
            float mixture = ComputeMixturePower(c.mixture, engine);
            float carbHeat = 1f - Mathf.Clamp01(c.carbHeat) * engine.carbHeatPowerLoss;
            return Mathf.Clamp01(c.throttle * mixture * carbHeat) * 100f;
        }

        private static float ComputeMixturePower(
            float normalizedMixture,
            EnginePropModelConfig engine)
        {
            float mixture = Mathf.Clamp01(normalizedMixture);
            if (mixture <= 0.02f)
                return 0f;
            return Mathf.Lerp(
                engine.minimumMixturePower,
                1f,
                Mathf.InverseLerp(0.02f, 1f, mixture));
        }

        public float GetFlapDegrees(float normalizedFlaps)
        {
            if (config.flapSettingsDeg == null || config.flapSettingsDeg.Length == 0)
            {
                return Mathf.Clamp01(normalizedFlaps) * 30f;
            }

            float scaled = Mathf.Clamp01(normalizedFlaps) * (config.flapSettingsDeg.Length - 1);
            int low = Mathf.FloorToInt(scaled);
            int high = Mathf.Min(config.flapSettingsDeg.Length - 1, low + 1);
            return Mathf.Lerp(config.flapSettingsDeg[low], config.flapSettingsDeg[high], scaled - low);
        }

        private void StepPhysics(AircraftControlState c, float dt)
        {
            StepSimulation(c, dt);
        }
    }
}
