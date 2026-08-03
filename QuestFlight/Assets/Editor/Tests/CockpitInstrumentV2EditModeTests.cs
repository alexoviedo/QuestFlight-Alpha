using System;
using NUnit.Framework;
using QuestFlightLab.Aircraft.CockpitDynamics.V2;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace QuestFlight.Editor.Tests
{
    public sealed class CockpitInstrumentV2EditModeTests
    {
        [Test]
        public void Altimeter_UsesPressureAltitudeAndExplicitBaroAuthority()
        {
            CockpitInstrumentV2Snapshot snapshot =
                Valid(CockpitInstrumentV2Availability.AirData |
                      CockpitInstrumentV2Availability.BarometricSetting);
            snapshot.pressureAltitudeFeet = 5432f;
            snapshot.barometricSettingInHg = 30.12f;

            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.AltimeterHundredsNeedleDegrees,
                    snapshot), Is.EqualTo(227.52f).Within(0.01f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.AltimeterThousandsNeedleDegrees,
                    snapshot), Is.EqualTo(202.752f).Within(0.01f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.AltimeterTenThousandsNeedleDegrees,
                    snapshot), Is.EqualTo(20.2752f).Within(0.01f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.AltimeterBarometricDrumInHg,
                    snapshot), Is.EqualTo(30.12f).Within(0.001f));
            Assert.That(
                CockpitInstrumentV2Kinematics.TryEvaluateIndicatedAltitudeFeet(
                    snapshot, out float indicatedFeet),
                Is.True);
            Assert.That(indicatedFeet, Is.EqualTo(5632f).Within(0.01f));
        }

        [Test]
        public void FrozenIasAndVsi_UseTheirAuditedCalibrations()
        {
            CockpitInstrumentV2Snapshot snapshot =
                Valid(CockpitInstrumentV2Availability.AirData);
            snapshot.indicatedAirspeedKnots = 100f;
            snapshot.indicatedVerticalSpeedFeetPerMinute = 1000f;

            Assert.That(Evaluate(
                    CockpitInstrumentV2Function
                        .IndicatedAirspeedNeedleDegrees,
                    snapshot),
                Is.EqualTo(45f).Within(0.001f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function
                        .IndicatedVerticalSpeedNeedleDegrees,
                    snapshot),
                Is.EqualTo(90f).Within(0.001f));
        }

        [Test]
        public void GeneratedLayout_IsOneOpaqueCompleteBatch()
        {
            CockpitInstrumentV2Layout layout =
                AssetDatabase.LoadAssetAtPath<CockpitInstrumentV2Layout>(
                    CockpitInstrumentV2Authoring.LayoutPath);
            Assert.That(
                CockpitInstrumentV2EditorContract.ValidateLayoutIdentity(
                    layout, out string error),
                Is.True, error);
            Assert.That(layout.CombinedDynamicMesh.subMeshCount, Is.EqualTo(1));
            Assert.That(layout.OpaqueOverlayCoversSourceApertures, Is.True);
            Assert.That(layout.StaticOverlayVertexCount, Is.GreaterThan(0));
            Assert.That(layout.Bindings.Length, Is.EqualTo(13));
            Assert.That(layout.AltitudeDigitSlots, Is.Empty,
                "Altitude numerals must be composited in the face shader, not " +
                "drawn as competing geometry planes.");
            Assert.That(layout.AltitudeDigitUvRects, Is.Empty);
            Assert.That(layout.ExistingSourceMaterial.renderQueue,
                Is.LessThan(3000));
            Assert.That(layout.ExistingSourceMaterial.HasProperty(
                "_AltitudeApertureUv"), Is.True);
            Assert.That(layout.ExistingSourceMaterial.HasProperty(
                "_AltitudeDigits"), Is.True);
            Assert.That(layout.ExistingSourceMaterial.HasProperty(
                "_AttitudePivot"), Is.True);
            Assert.That(layout.ExistingSourceMaterial.HasProperty(
                "_AttitudeRotation"), Is.True);
            Assert.That(layout.ExistingSourceMaterial.GetFloat(
                "_LayerDepthBias"), Is.EqualTo(0.00004f).Within(0.000001f));
            var layers = new List<Vector2>();
            layout.CombinedDynamicMesh.GetUVs(3, layers);
            Assert.That(layers.Exists(value => value.y == 1f), Is.True,
                "Authored gauge faces require a deterministic face depth layer.");
            Assert.That(layers.Exists(value => value.y == 2f), Is.True,
                "Moving indications require a deterministic pointer depth layer.");

            CockpitInstrumentV2AttitudeContract attitude =
                layout.AttitudeContract;
            Assert.That(attitude.Validate(out error), Is.True, error);
            CollectionAssert.AreEqual(
                CockpitInstrumentV2EditorContract.AttitudeSphereComponentSeeds,
                attitude.sourceComponentSeeds);
            Assert.That(attitude.sourceVertexCount, Is.EqualTo(998));
            Assert.That(attitude.sourceTriangleCount, Is.EqualTo(1740));
            var sourceSelectors = new List<Vector4>();
            layout.NeutralizedSourceMesh.GetUVs(3, sourceSelectors);
            Assert.That(sourceSelectors.FindAll(value => value.z > 0.5f).Count,
                Is.EqualTo(998));

            CockpitInstrumentV2MeshBinding compass = Array.Find(
                layout.Bindings,
                binding => binding.rotationFunction ==
                    CockpitInstrumentV2Function.MagneticCompassHeadingDegrees);
            Assert.That(compass.label, Is.EqualTo("magnetic_compass_card"));
            Assert.That(compass.sourceTriangleSeed, Is.EqualTo(
                CockpitInstrumentV2EditorContract.MagneticCompassCardSeed));
            Assert.That(compass.vertexCount, Is.EqualTo(
                CockpitInstrumentV2EditorContract
                    .ExpectedMagneticCompassCardTriangleCount * 3));
            Assert.That(compass.rotationAxisLocal.sqrMagnitude,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(compass.authoredRotationValue, Is.EqualTo(
                CockpitInstrumentV2EditorContract
                    .MagneticCompassAuthoredHeadingDegrees));
            Assert.That(compass.rotationDegreesPerUnit, Is.EqualTo(-1f));
            Assert.That(compass.absoluteRegistrationProven, Is.True);
            Assert.That(compass.magneticVariationContractProven, Is.True);
        }

        [Test]
        public void AttitudeGyroBall_CounterRotatesPitchAndBank()
        {
            CockpitInstrumentV2Layout layout =
                AssetDatabase.LoadAssetAtPath<CockpitInstrumentV2Layout>(
                    CockpitInstrumentV2Authoring.LayoutPath);
            CockpitInstrumentV2AttitudeContract contract =
                layout.AttitudeContract;
            Vector3 up = Vector3.Cross(
                contract.bankAxisLocal,
                contract.pitchAxisLocal).normalized;

            Quaternion pitch =
                CockpitInstrumentV2Kinematics.EvaluateAttitudeBallRotation(
                    10f,
                    0f,
                    contract.pitchAxisLocal,
                    contract.bankAxisLocal);
            Assert.That(Vector3.SignedAngle(
                    up,
                    pitch * up,
                    contract.pitchAxisLocal),
                Is.EqualTo(-10f).Within(0.01f));

            Quaternion bank =
                CockpitInstrumentV2Kinematics.EvaluateAttitudeBallRotation(
                    0f,
                    20f,
                    contract.pitchAxisLocal,
                    contract.bankAxisLocal);
            Assert.That(Vector3.SignedAngle(
                    up,
                    bank * up,
                    contract.bankAxisLocal),
                Is.EqualTo(-20f).Within(0.01f));
        }

        [Test]
        public void IndividualChannels_FailClosedWithoutTheirAuthority()
        {
            CockpitInstrumentV2Snapshot snapshot =
                Valid(CockpitInstrumentV2Availability.TrueHeading);
            snapshot.trueHeadingDegrees = 370f;
            snapshot.pitchDegrees = float.NaN;

            Assert.That(CockpitInstrumentV2Kinematics.TryEvaluate(
                CockpitInstrumentV2Function.DirectionalGyroHeadingDegrees,
                snapshot, out float heading), Is.True);
            Assert.That(heading, Is.EqualTo(10f));
            Assert.That(CockpitInstrumentV2Kinematics.TryEvaluate(
                CockpitInstrumentV2Function.AttitudePitchDegrees,
                snapshot, out _), Is.False);
            Assert.That(CockpitInstrumentV2Kinematics.TryEvaluate(
                CockpitInstrumentV2Function.MagneticCompassHeadingDegrees,
                snapshot, out _), Is.False);
        }

        [Test]
        public void MagneticCompass_UsesEastVariationConvention()
        {
            CockpitInstrumentV2Snapshot snapshot =
                Valid(CockpitInstrumentV2Availability.TrueHeading |
                      CockpitInstrumentV2Availability.MagneticVariation);
            snapshot.trueHeadingDegrees = 3f;
            snapshot.magneticVariationEastDegrees = 8f;

            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.MagneticCompassHeadingDegrees,
                snapshot), Is.EqualTo(355f));
        }

        [Test]
        public void MagneticCompassDamping_UsesShortestWrappedPath()
        {
            float result = CockpitInstrumentV2Kinematics.DampHeadingDegrees(
                359f,
                1f,
                0.45f,
                0.45f);
            Assert.That(result, Is.GreaterThan(359f).Or.LessThan(1f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(359f, result)),
                Is.LessThan(2f));
            Assert.That(
                CockpitInstrumentV2Kinematics.DampHeadingDegrees(
                    90f, 180f, float.PositiveInfinity, 0.45f),
                Is.EqualTo(180f));
        }

        [Test]
        public void ActualFlapsBrakePressureAndUtcClock_AreDeterministic()
        {
            CockpitInstrumentV2Snapshot snapshot = Valid(
                CockpitInstrumentV2Availability.ActualFlapPosition |
                CockpitInstrumentV2Availability.BrakeHydraulics |
                CockpitInstrumentV2Availability.ClockUtc);
            snapshot.actualFlapDegrees = 17f;
            snapshot.leftBrakePressurePsi = 500f;
            snapshot.rightBrakePressurePsi = 300f;
            snapshot.clockUtcSeconds = 3 * 3600 + 15 * 60 + 30;

            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.ActualFlapDegrees, snapshot),
                Is.EqualTo(17f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.AverageBrakePressurePsi, snapshot),
                Is.EqualTo(400f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.ClockHourHandDegrees, snapshot),
                Is.EqualTo(97.75f).Within(0.001f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.ClockMinuteHandDegrees, snapshot),
                Is.EqualTo(93f).Within(0.001f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.ClockSecondHandDegrees, snapshot),
                Is.EqualTo(180f).Within(0.001f));
        }

        [Test]
        public void TachometerTurnCoordinatorAndSlip_UseAuthoritativeState()
        {
            CockpitInstrumentV2Snapshot snapshot = Valid(
                CockpitInstrumentV2Availability.EngineRpm |
                CockpitInstrumentV2Availability.TurnAndSlip);
            snapshot.engineRpm = 2350f;
            snapshot.turnRateDegreesPerSecond = -3f;
            snapshot.slipSkidNormalized = 0.4f;

            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.EngineRpmNeedleDegrees,
                    snapshot),
                Is.EqualTo(235f).Within(0.001f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.TurnCoordinatorBankDegrees,
                    snapshot),
                Is.EqualTo(-20f).Within(0.001f));
            Assert.That(Evaluate(
                    CockpitInstrumentV2Function.SlipSkidNormalized,
                    snapshot),
                Is.EqualTo(0.4f).Within(0.001f));
        }

        [Test]
        public void AltitudeReadout_UsesTenFootResolutionAndHysteresis()
        {
            Assert.That(
                CockpitInstrumentV2Kinematics.StabilizeAltitudeReadoutFeet(
                    5284f, 0, false),
                Is.EqualTo(5280));
            Assert.That(
                CockpitInstrumentV2Kinematics.StabilizeAltitudeReadoutFeet(
                    5285.5f, 5280, true),
                Is.EqualTo(5280),
                "Noise inside the Schmitt band must not alternate digit states.");
            Assert.That(
                CockpitInstrumentV2Kinematics.StabilizeAltitudeReadoutFeet(
                    5286f, 5280, true),
                Is.EqualTo(5290));
        }

        [Test]
        public void PureKinematics_SteadyStateAllocatesZeroManagedBytes()
        {
            CockpitInstrumentV2Snapshot snapshot =
                Valid(CockpitInstrumentV2Availability.Attitude);
            snapshot.pitchDegrees = 5f;
            snapshot.bankDegrees = -12f;
            CockpitInstrumentV2Kinematics.TryEvaluate(
                CockpitInstrumentV2Function.AttitudePitchDegrees,
                snapshot, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 4096; index++)
            {
                CockpitInstrumentV2Kinematics.TryEvaluate(
                    CockpitInstrumentV2Function.AttitudePitchDegrees,
                    snapshot, out _);
                CockpitInstrumentV2Kinematics.TryEvaluate(
                    CockpitInstrumentV2Function.AttitudeBankDegrees,
                    snapshot, out _);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.EqualTo(0));
        }

        private static CockpitInstrumentV2Snapshot Valid(
            CockpitInstrumentV2Availability availability)
        {
            return new CockpitInstrumentV2Snapshot
            {
                isValid = true,
                availability = availability
            };
        }

        private static float Evaluate(
            CockpitInstrumentV2Function function,
            CockpitInstrumentV2Snapshot snapshot)
        {
            Assert.That(CockpitInstrumentV2Kinematics.TryEvaluate(
                function, snapshot, out float value), Is.True);
            return value;
        }
    }
}
