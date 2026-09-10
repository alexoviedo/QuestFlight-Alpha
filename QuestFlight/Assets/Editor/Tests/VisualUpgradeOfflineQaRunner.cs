using System;
using System.IO;
using QuestFlightLab.Environment.Editor.Tests;

namespace QuestFlightLab.Editor.Tests
{
    public static class VisualUpgradeOfflineQaRunner
    {
        public static void Run()
        {
            string root = System.Environment.GetEnvironmentVariable("QFL_UPGRADE_QA_ROOT");
            if (string.IsNullOrEmpty(root)) throw new InvalidOperationException("QFL_UPGRADE_QA_ROOT required");
            Directory.CreateDirectory(root);
            System.Environment.SetEnvironmentVariable("QFL_WORKLOAD_REPORT", Path.Combine(root, "traffic_cpu_candidate.json"));
            VisualUpgradeWorkloadRunner.Run();
            System.Environment.SetEnvironmentVariable("QFL_PRODUCTION_VISUAL_QA_DIR", Path.Combine(root, "standard_qa"));
            ProductionVisualQaBatchRunner.RunProductionVisualQa();
            System.Environment.SetEnvironmentVariable("QFL_TRAFFIC_V2_FULL_MAP_QA_DIR", Path.Combine(root, "traffic_full_map"));
            TrafficV2FullMapVisualQaBatchRunner.Run();
            try
            {
                System.Environment.SetEnvironmentVariable("QFL_TRAFFIC_TEMPORAL_QA", "1");
                foreach (bool baseline in new[] { true, false })
                {
                    System.Environment.SetEnvironmentVariable("QFL_TRAFFIC_CONTINUITY_BASELINE", baseline ? "1" : null);
                    System.Environment.SetEnvironmentVariable("QFL_TRAFFIC_V2_FULL_MAP_QA_DIR", Path.Combine(root, baseline ? "temporal_before" : "temporal_after"));
                    TrafficV2FullMapVisualQaBatchRunner.Run();
                }
            }
            finally
            {
                System.Environment.SetEnvironmentVariable("QFL_TRAFFIC_TEMPORAL_QA", null);
                System.Environment.SetEnvironmentVariable("QFL_TRAFFIC_CONTINUITY_BASELINE", null);
            }
        }
    }
}
