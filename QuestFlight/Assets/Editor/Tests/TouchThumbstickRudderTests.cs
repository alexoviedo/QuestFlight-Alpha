using NUnit.Framework;
using QuestFlightLab.Input;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class TouchThumbstickRudderTests
    {
        [Test]
        public void HeldMenuInputMustReturnToNeutralBeforeTakingRudder()
        {
            bool armed = false;
            Assert.AreEqual(.3f, Resolve(.3f, 1f, true, ref armed));
            Assert.AreEqual(.3f, Resolve(.3f, 0f, true, ref armed));
            Assert.AreEqual(1f, Resolve(.3f, 1f, true, ref armed));
            Assert.AreEqual(-1f, Resolve(.3f, -1f, true, ref armed));
            Assert.AreEqual(.3f, Resolve(.3f, 0f, true, ref armed));
        }

        [Test]
        public void LossOfTrackingOrFocusReleasesAndRequiresNeutralAgain()
        {
            bool armed = true;
            Assert.AreEqual(-.2f, Resolve(-.2f, 1f, false, ref armed));
            Assert.IsFalse(armed);
            Assert.AreEqual(-.2f, Resolve(-.2f, 1f, true, ref armed));
            Resolve(0f, 0f, true, ref armed);
            Assert.AreEqual(1f, Resolve(0f, 1f, true, ref armed));
        }

        [Test]
        public void DeadzonePreservesPedalsAndRemainingTravelIsProportional()
        {
            bool armed = true;
            Assert.AreEqual(.4f, Resolve(.4f, .1f, true, ref armed));
            Assert.AreEqual(.5f, Resolve(0f, .56f, true, ref armed), .00001f);
            Assert.AreEqual(-.5f, Resolve(0f, -.56f, true, ref armed), .00001f);
            Assert.AreEqual(.4f, Resolve(.4f, float.NaN, true, ref armed));
            Assert.IsFalse(armed);
        }

        static float Resolve(float baseline, float x, bool available, ref bool armed) =>
            ProductControlInput.ResolveTouchThumbstickRudder(baseline, x, available, ref armed);
    }
}
