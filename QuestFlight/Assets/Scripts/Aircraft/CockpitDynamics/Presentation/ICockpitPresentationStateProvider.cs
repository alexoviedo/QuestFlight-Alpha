namespace QuestFlightLab.Aircraft.CockpitDynamics.Presentation
{
    /// <summary>
    /// Optional authored extension boundary for aircraft-system values that are
    /// not part of the core flight-dynamics snapshot. Implementations must be
    /// deterministic and allocation-free when sampled.
    /// </summary>
    public interface ICockpitPresentationStateProvider
    {
        bool TryGetCockpitPresentationValue(
            CockpitPresentationChannel channel,
            out float value);
    }
}
