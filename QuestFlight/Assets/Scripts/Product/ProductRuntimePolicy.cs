using UnityEngine;

namespace QuestFlight.Product
{
    /// <summary>Non-constructive runtime policy for the single Quest production scene.</summary>
    public static class ProductRuntimePolicy
    {
        public const int TargetRefreshRateHz = 72;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            Application.targetFrameRate = TargetRefreshRateHz;
            QualitySettings.vSyncCount = 0;
        }
    }
}
