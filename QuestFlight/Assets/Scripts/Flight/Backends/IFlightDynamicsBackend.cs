using System;
using QuestFlightLab.Runtime;

namespace QuestFlightLab.Flight.Backends
{
    /// <summary>
    /// One complete, fixed-step flight-dynamics authority. Implementations must
    /// not also apply Rigidbody forces or integrate the returned pose elsewhere.
    /// </summary>
    public interface IFlightDynamicsBackend : IDisposable
    {
        FlightDynamicsBackendKind Kind { get; }
        string DisplayName { get; }
        bool IsAvailable { get; }
        bool IsInitialized { get; }
        string LastError { get; }
        FlightDynamicsState CurrentState { get; }

        bool Initialize(FlightDynamicsBackendContext context);
        bool Reset(FlightDynamicsInitialConditions initialConditions);
        void SetControls(AircraftControlState controls);
        void SetAtmosphere(FlightDynamicsAtmosphere atmosphere);
        bool Advance(double fixedDeltaTimeSeconds);
    }
}
