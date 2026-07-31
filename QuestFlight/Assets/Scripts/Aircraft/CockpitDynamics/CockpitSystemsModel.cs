using QuestFlightLab.Flight.Backends;
using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics
{
    /// <summary>
    /// Deterministic, backend-neutral cockpit presentation model. It consumes
    /// only value snapshots from the single flight authority and allocates
    /// nothing during Reset or Step.
    /// </summary>
    public sealed class CockpitSystemsModel
    {
        public const float AirspeedResponseSeconds = 0.18f;
        public const float AltimeterResponseSeconds = 0.12f;
        public const float VerticalSpeedResponseSeconds = 6.0f;
        public const float AttitudeResponseSeconds = 0.08f;
        public const float HeadingResponseSeconds = 0.10f;
        public const float TurnRateResponseSeconds = 0.25f;
        public const float SideslipResponseSeconds = 0.35f;
        public const float TachometerResponseSeconds = 0.22f;

        public const CockpitDataAvailability AuthoritativeFlightChannels =
            CockpitDataAvailability.AirData |
            CockpitDataAvailability.Attitude |
            CockpitDataAvailability.Heading |
            CockpitDataAvailability.BodyTurnRate |
            CockpitDataAvailability.AerodynamicSideslip |
            CockpitDataAvailability.EngineRpm |
            CockpitDataAvailability.FlightControlCommands |
            CockpitDataAvailability.WeightOnWheels;

        public CockpitSystemsSnapshot Current => _current;
        public bool IsInitialized => _initialized;

        private CockpitSystemsSnapshot _current;
        private bool _initialized;

        public bool Reset(
            FlightDynamicsState state,
            FlightDynamicsControlSnapshot controls,
            CockpitElectricalPowerInput electricalInput)
        {
            if (!CanConsume(state, controls))
            {
                Invalidate();
                return false;
            }

            CockpitElectricalPowerState electrical =
                CockpitElectricalPowerState.From(electricalInput);
            CockpitDataAvailability availability = AuthoritativeFlightChannels;
            if (electrical.dataAvailable)
                availability |= CockpitDataAvailability.ElectricalPower;

            _current = new CockpitSystemsSnapshot
            {
                isValid = true,
                simulationTimeSeconds = state.simulationTimeSeconds,
                availability = availability,
                indicatedAirspeedKnots = NonNegative(state.calibratedAirspeedKnots),
                indicatedAltitudeMslFeet =
                    (float)(state.altitudeMslMeters * FlightFrameConversions.MetersToFeet),
                indicatedVerticalSpeedFeetPerMinute =
                    (float)state.verticalSpeedFeetPerMinute,
                indicatedPitchDegrees = SignedAngle(state.pitchDegrees),
                indicatedBankDegrees = SignedAngle(state.bankDegrees),
                indicatedHeadingDegrees = Heading(state.headingDegrees),
                indicatedBodyTurnRateDegreesPerSecond =
                    (float)state.angularVelocityBodyDegreesPerSecond.y,
                aerodynamicSideslipDegrees = (float)state.sideslipDegrees,
                indicatedEngineRpm = NonNegative(state.engineRpm),
                weightOnWheels = state.weightOnWheels,
                controlPositions = ControlPositions(controls),
                electricalPower = electrical
            };
            _initialized = true;
            return true;
        }

        public bool Step(
            FlightDynamicsState state,
            FlightDynamicsControlSnapshot controls,
            CockpitElectricalPowerInput electricalInput,
            float deltaTimeSeconds)
        {
            if (!CanConsume(state, controls) || !IsFinite(deltaTimeSeconds) ||
                deltaTimeSeconds <= 0f)
            {
                Invalidate();
                return false;
            }

            if (!_initialized)
                return Reset(state, controls, electricalInput);

            CockpitElectricalPowerState electrical =
                CockpitElectricalPowerState.From(electricalInput);
            CockpitDataAvailability availability = AuthoritativeFlightChannels;
            if (electrical.dataAvailable)
                availability |= CockpitDataAvailability.ElectricalPower;

            float airspeed = NonNegative(state.calibratedAirspeedKnots);
            float altitudeFeet =
                (float)(state.altitudeMslMeters * FlightFrameConversions.MetersToFeet);
            float verticalSpeed = (float)state.verticalSpeedFeetPerMinute;
            float pitch = SignedAngle(state.pitchDegrees);
            float bank = SignedAngle(state.bankDegrees);
            float heading = Heading(state.headingDegrees);
            float bodyTurnRate = state.angularVelocityBodyDegreesPerSecond.y;
            float sideslip = (float)state.sideslipDegrees;
            float engineRpm = NonNegative(state.engineRpm);

            _current.isValid = true;
            _current.simulationTimeSeconds = state.simulationTimeSeconds;
            _current.availability = availability;
            _current.indicatedAirspeedKnots = Response(
                _current.indicatedAirspeedKnots, airspeed,
                AirspeedResponseSeconds, deltaTimeSeconds);
            _current.indicatedAltitudeMslFeet = Response(
                _current.indicatedAltitudeMslFeet, altitudeFeet,
                AltimeterResponseSeconds, deltaTimeSeconds);
            _current.indicatedVerticalSpeedFeetPerMinute = Response(
                _current.indicatedVerticalSpeedFeetPerMinute, verticalSpeed,
                VerticalSpeedResponseSeconds, deltaTimeSeconds);
            _current.indicatedPitchDegrees = AngleResponse(
                _current.indicatedPitchDegrees, pitch,
                AttitudeResponseSeconds, deltaTimeSeconds, false);
            _current.indicatedBankDegrees = AngleResponse(
                _current.indicatedBankDegrees, bank,
                AttitudeResponseSeconds, deltaTimeSeconds, false);
            _current.indicatedHeadingDegrees = AngleResponse(
                _current.indicatedHeadingDegrees, heading,
                HeadingResponseSeconds, deltaTimeSeconds, true);
            _current.indicatedBodyTurnRateDegreesPerSecond = Response(
                _current.indicatedBodyTurnRateDegreesPerSecond, bodyTurnRate,
                TurnRateResponseSeconds, deltaTimeSeconds);
            _current.aerodynamicSideslipDegrees = Response(
                _current.aerodynamicSideslipDegrees, sideslip,
                SideslipResponseSeconds, deltaTimeSeconds);
            _current.indicatedEngineRpm = Response(
                _current.indicatedEngineRpm, engineRpm,
                TachometerResponseSeconds, deltaTimeSeconds);
            _current.weightOnWheels = state.weightOnWheels;
            _current.controlPositions = ControlPositions(controls);
            _current.electricalPower = electrical;
            return true;
        }

        public void Invalidate()
        {
            _current.isValid = false;
            _current.availability = CockpitDataAvailability.None;
            _current.electricalPower = default;
            _initialized = false;
        }

        private static CockpitControlPositions ControlPositions(
            FlightDynamicsControlSnapshot controls)
        {
            return new CockpitControlPositions
            {
                aileron = Mathf.Clamp(controls.aileron, -1f, 1f),
                elevator = Mathf.Clamp(controls.elevator, -1f, 1f),
                rudder = Mathf.Clamp(controls.rudder, -1f, 1f),
                throttle = Mathf.Clamp01(controls.throttle),
                mixture = Mathf.Clamp01(controls.mixture),
                elevatorTrim = Mathf.Clamp(controls.trim, -1f, 1f),
                flapCommand = Mathf.Clamp01(controls.flaps),
                leftToeBrake = Mathf.Clamp01(controls.leftToeBrake),
                rightToeBrake = Mathf.Clamp01(controls.rightToeBrake)
            };
        }

        private static float Response(
            float current,
            float target,
            float responseSeconds,
            float deltaTimeSeconds)
        {
            float blend = responseSeconds <= 0f
                ? 1f
                : 1f - Mathf.Exp(-deltaTimeSeconds / responseSeconds);
            return Mathf.LerpUnclamped(current, target, blend);
        }

        private static float AngleResponse(
            float current,
            float target,
            float responseSeconds,
            float deltaTimeSeconds,
            bool unsigned)
        {
            float blend = responseSeconds <= 0f
                ? 1f
                : 1f - Mathf.Exp(-deltaTimeSeconds / responseSeconds);
            float value = current + Mathf.DeltaAngle(current, target) * blend;
            return unsigned ? Mathf.Repeat(value, 360f) : Mathf.DeltaAngle(0f, value);
        }

        private static float Heading(double degrees)
        {
            return Mathf.Repeat((float)degrees, 360f);
        }

        private static float SignedAngle(double degrees)
        {
            return Mathf.DeltaAngle(0f, (float)degrees);
        }

        private static float NonNegative(double value)
        {
            return Mathf.Max(0f, (float)value);
        }

        private static bool CanConsume(
            FlightDynamicsState state,
            FlightDynamicsControlSnapshot controls)
        {
            return state.IsFinite &&
                   IsFinite(controls.aileron) &&
                   IsFinite(controls.elevator) &&
                   IsFinite(controls.rudder) &&
                   IsFinite(controls.throttle) &&
                   IsFinite(controls.mixture) &&
                   IsFinite(controls.trim) &&
                   IsFinite(controls.flaps) &&
                   IsFinite(controls.leftToeBrake) &&
                   IsFinite(controls.rightToeBrake);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
