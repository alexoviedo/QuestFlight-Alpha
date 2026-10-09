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
                "wildlife_demo",
                "wildlife_habitat",
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
            Assert.That(MetaXrSimulatorQaSessionCatalog.All.Select(item => item.id).Distinct().Count(), Is.EqualTo(12));
            Assert.That(MetaXrSimulatorQaSessionCatalog.All.Where(item => item.id != "wildlife_demo")
                .All(item => item.captureDelaySeconds > 0f), Is.True);
            Assert.That(MetaXrSimulatorQaSessionCatalog.TryGet("wildlife_demo", out var demo), Is.True);
            Assert.That(demo.captureDelaySeconds, Is.Zero);
            Assert.That(demo.startAuthoredDemoPilot, Is.True);
            Assert.That(MetaXrSimulatorQaSessionCatalog.TryGet("unknown-fixture-session", out _), Is.False);
        }
    }
}
#endif
