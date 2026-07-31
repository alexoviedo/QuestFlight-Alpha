namespace QuestFlightLab.Aircraft.CockpitDynamics
{
    /// <summary>
    /// Stable Unity player-loop ordering for the authored cockpit pipeline.
    /// The systems snapshot is finalized before any visual presenter samples it.
    /// </summary>
    public static class CockpitSystemsExecutionOrder
    {
        public const int Systems = -200;
        public const int Presentation = -100;
    }
}
