#if UNITY_INCLUDE_TESTS
// Keep this fixture under Assets/Editor so Unity's implicit Editor test assembly discovers it.
using System.Linq;
using NUnit.Framework;
using QuestFlightLab.Runtime;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class MetaXrSimulatorQaEditModeTests
    {
        [Test]
        public void SessionCatalog_IsCompleteUniqueAndDeterministic()
        {
            string[] expected =
            {
                "cockpit_neutral",
                "head_pan",
                "rear_view",
                "viewpoint_calibration",
                "control_surface_sweep",
                "taxi",
                "takeoff_roll",
                "rotation_climb",
                "shallow_turns",
                "final_approach"
            };

            CollectionAssert.AreEqual(expected, MetaXrSimulatorQaSessionCatalog.All.Select(item => item.id));
            Assert.That(MetaXrSimulatorQaSessionCatalog.All.Select(item => item.id).Distinct().Count(), Is.EqualTo(10));
            Assert.That(MetaXrSimulatorQaSessionCatalog.All.All(item => item.captureDelaySeconds > 0f), Is.True);
        }
    }
}
#endif
