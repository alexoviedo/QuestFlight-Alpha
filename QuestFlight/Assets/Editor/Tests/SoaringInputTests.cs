#if UNITY_EDITOR
using NUnit.Framework;
using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Input;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class SoaringInputTests
    {
        [Test]
        public void ReleaseSurvivesFramesUntilTickAndRequiresNeutralAfterSuppression()
        {
            var root=new GameObject("SoaringReleaseInputTest");
            try
            {
                var input=root.AddComponent<ProductControlInput>();
                input.ConfigureAircraft(AssetDatabase.LoadAssetAtPath<FlightAircraftDefinition>(FlightAircraftDefinition.DefinitionFolder+"/SGS233A_Reference.asset"));
                input.SampleTowRelease(true,true);Assert.That(input.ConsumeTowRelease(),Is.False,"Held during entry must not release.");
                input.SampleTowRelease(false,true);input.SampleTowRelease(true,true);
                for(int frame=0;frame<10;frame++)input.SampleTowRelease(true,true);
                Assert.That(input.ConsumeTowRelease(),Is.True);Assert.That(input.ConsumeTowRelease(),Is.False);
                input.SampleTowRelease(true,true);Assert.That(input.ConsumeTowRelease(),Is.False);
                input.SampleTowRelease(false,true);input.SampleTowRelease(true,true);input.RequireTouchRelease();
                Assert.That(input.ConsumeTowRelease(),Is.False,"A session/pause rearm clears pending events.");
                input.SampleTowRelease(true,true);Assert.That(input.ConsumeTowRelease(),Is.False);
                input.SampleTowRelease(false,true);input.SampleTowRelease(true,true);
                typeof(ProductControlInput).GetMethod("OnApplicationFocus",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(input,new object[]{false});
                Assert.That(input.ConsumeTowRelease(),Is.False,"Focus callback must clear the edge before another FixedUpdate.");
                input.ConfigureAircraft(FlightAircraftDefinition.EditorC172);input.SampleTowRelease(false,true);input.SampleTowRelease(true,true);
                Assert.That(input.ConsumeTowRelease(),Is.False);
            }
            finally {Object.DestroyImmediate(root);}
        }
        [TestCase(0,0,0)]
        [TestCase(.425f,.5f,0)]
        [TestCase(.85f,1,0)]
        [TestCase(.925f,1,.5f)]
        [TestCase(1,1,1)]
        public void CombinedLeverBrakesOnlyAtAftEnd(float lever,float expectedAirbrake,float expectedWheelBrake)
        {
            ProductControlInput.ResolveGliderLever(lever,out float air,out float brake);
            Assert.That(air,Is.EqualTo(expectedAirbrake).Within(.00001));Assert.That(brake,Is.EqualTo(expectedWheelBrake).Within(.00001));
        }
    }
}
#endif
