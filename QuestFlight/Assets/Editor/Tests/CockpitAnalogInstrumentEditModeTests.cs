using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using QuestFlight.Editor;
using QuestFlightLab.Aircraft.CockpitDynamics;
using QuestFlightLab.Aircraft.CockpitDynamics.Analog;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class CockpitAnalogInstrumentEditModeTests
    {
        [Test]
        public void FrozenMeterClassification_IsDeterministicAndExact()
        {
            CockpitAnalogInstrumentAuthoring.SourceAudit first =
                CockpitAnalogInstrumentAuthoring.AuditFrozenSourceForTests();
            CockpitAnalogInstrumentAuthoring.SourceAudit second =
                CockpitAnalogInstrumentAuthoring.AuditFrozenSourceForTests();

            Assert.That(first.sourceVertexCount,
                Is.EqualTo(CockpitAnalogInstrumentAuthoring.ExpectedSourceVertexCount));
            Assert.That(first.sourceTriangleCount,
                Is.EqualTo(CockpitAnalogInstrumentAuthoring.ExpectedSourceTriangleCount));
            Assert.That(first.apertureCount,
                Is.EqualTo(CockpitAnalogInstrumentAuthoring.ExpectedApertureCount));
            Assert.That(first.supportedBindingCount, Is.EqualTo(2));
            Assert.That(first.dynamicTriangleCount,
                Is.EqualTo(CockpitAnalogInstrumentAuthoring.ExpectedDynamicTriangleCount));
            Assert.That(first.neutralizedTriangleCount,
                Is.EqualTo(CockpitAnalogInstrumentAuthoring.ExpectedNeutralizedTriangleCount));
            Assert.That(first.sourceAssetGuid,
                Is.EqualTo(CockpitAnalogInstrumentAuthoring.ExpectedSourceGuid));
            Assert.That(first.sourceSha256,
                Is.EqualTo(CockpitAnalogInstrumentAuthoring.ExpectedSourceSha256));
            Assert.That(second.deterministicSignature,
                Is.EqualTo(first.deterministicSignature));
            CollectionAssert.AreEqual(
                new[]
                {
                    2533, 2535, 2537, 2539,
                    4147, 4151, 4153, 4157, 4159,
                    5631, 5635, 5639, 5641, 6011
                },
                first.removedTriangleSeeds);
        }

        [Test]
        public void SupportedFunctions_DoNotFabricateUnavailableInstruments()
        {
            AnalogInstrumentFunction[] functions =
                CockpitAnalogInstrumentAuthoring.SupportedDefinitions
                    .Select(definition => definition.Function)
                    .OrderBy(function => function)
                    .ToArray();
            CollectionAssert.AreEqual(
                new[]
                {
                    AnalogInstrumentFunction.IndicatedAirspeedKnots,
                    AnalogInstrumentFunction.IndicatedVerticalSpeedFeetPerMinute
                },
                functions);

            string[] exclusions =
                CockpitAnalogInstrumentAuthoring.ExcludedItemNames.ToArray();
            CollectionAssert.Contains(exclusions, "BRAKE PRESS face");
            CollectionAssert.Contains(exclusions, "ALT faces");
            CollectionAssert.Contains(exclusions, "MACH/AS hybrid faces");
            CollectionAssert.Contains(exclusions, "Clock");
            CollectionAssert.Contains(exclusions, "FLAPS indicator");
            CollectionAssert.Contains(exclusions, "GYRO/LOC face");
            CollectionAssert.Contains(
                exclusions, "Directional gyro heading card");
            CollectionAssert.Contains(exclusions, "Magnetic compass tape");
            CollectionAssert.Contains(exclusions, "Attitude card");
            CollectionAssert.Contains(
                exclusions, "Fuel, oil, tachometer and unlabeled faces");
        }

        [Test]
        public void GeneratedAssets_UseOneSubmeshAndExistingMeterMaterial()
        {
            ProductionAnalogInstrumentLayout layout =
                AssetDatabase.LoadAssetAtPath<ProductionAnalogInstrumentLayout>(
                    CockpitAnalogInstrumentAuthoring.LayoutAssetPath);
            Assert.That(layout, Is.Not.Null,
                "Run 'Build Dynamic Analog Instrument Assets Only' first.");
            Assert.That(layout.Validate(out string error), Is.True, error);
            Assert.That(layout.DynamicInstrumentMesh.subMeshCount, Is.EqualTo(1));
            Assert.That(layout.NeutralizedMeterMesh.subMeshCount, Is.EqualTo(1));
            Assert.That(
                layout.NeutralizedMeterMesh.vertexCount,
                Is.EqualTo(
                    CockpitAnalogInstrumentAuthoring.ExpectedSourceVertexCount),
                "Neutralization must preserve the source vertex order for the " +
                "existing directional-visibility stream.");
            Assert.That(
                layout.DynamicInstrumentMesh.vertexCount,
                Is.EqualTo(
                    CockpitAnalogInstrumentAuthoring.ExpectedDynamicTriangleCount * 3));
            Assert.That(layout.DynamicTriangleCount,
                Is.EqualTo(CockpitAnalogInstrumentAuthoring.ExpectedDynamicTriangleCount));
            Assert.That(layout.NeutralizedTriangleCount,
                Is.EqualTo(CockpitAnalogInstrumentAuthoring.ExpectedNeutralizedTriangleCount));
            Assert.That(
                layout.DynamicTriangleCount + layout.NeutralizedTriangleCount,
                Is.EqualTo(layout.SourceTriangleCount));
            Assert.That(
                AssetDatabase.GetAssetPath(layout.SharedSourceMeterMaterial),
                Is.EqualTo(CockpitAnalogInstrumentAuthoring.SourceMeterMaterialPath));
            Assert.That(
                AssetDatabase.GetAssetPath(layout.DynamicInstrumentMesh),
                Is.EqualTo(CockpitAnalogInstrumentAuthoring.DynamicInstrumentMeshPath));
            Assert.That(
                AssetDatabase.GetAssetPath(layout.NeutralizedMeterMesh),
                Is.EqualTo(CockpitAnalogInstrumentAuthoring.NeutralizedMeterMeshPath));
            Assert.That(layout.Bindings.Length, Is.EqualTo(2));

            Assert.That(
                AssetDatabase.LoadAssetAtPath<Mesh>(
                    "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
                    "Reflection/Cockpit/Meshes/C172_PrincipalInstrumentLensArray.asset"),
                Is.Not.Null,
                "Instrument reflection lenses must remain intact.");
            Assert.That(
                AssetDatabase.LoadAssetAtPath<Mesh>(
                    "Assets/Production/Environment/PhotorealAerialSurfaceV1/" +
                    "Reflection/Cockpit/Meshes/C172_InstrumentDisplayReadouts.asset"),
                Is.Not.Null,
                "Instrument display readouts must remain intact.");
        }

        [Test]
        public void ProductPrefab_UsesExplicitValidatedAnalogIntegration()
        {
            CockpitAnalogInstrumentAuthoring.ValidateProductPrefab();
        }

        [Test]
        public void SystemsSnapshot_ExecutesBeforeAnalogPresentation()
        {
            var systemsOrder = (DefaultExecutionOrder)Attribute.GetCustomAttribute(
                typeof(ProductionCockpitSystemsController),
                typeof(DefaultExecutionOrder));
            var analogOrder = (DefaultExecutionOrder)Attribute.GetCustomAttribute(
                typeof(ProductionAnalogInstrumentController),
                typeof(DefaultExecutionOrder));
            Assert.That(systemsOrder, Is.Not.Null);
            Assert.That(analogOrder, Is.Not.Null);
            Assert.That(systemsOrder.order,
                Is.EqualTo(CockpitSystemsExecutionOrder.Systems));
            Assert.That(analogOrder.order,
                Is.EqualTo(CockpitSystemsExecutionOrder.Presentation));
            Assert.That(systemsOrder.order, Is.LessThan(analogOrder.order));
        }

        [Test]
        public void RuntimeAnalogController_CannotConstructOrDiscoverHierarchy()
        {
            string sourcePath = Path.Combine(
                Application.dataPath,
                "Scripts/Aircraft/CockpitDynamics/Analog/" +
                "ProductionAnalogInstrumentController.cs");
            string source = File.ReadAllText(sourcePath);
            string[] forbidden =
            {
                "Camera.main",
                "FindObjectOfType",
                "FindFirstObjectByType",
                "FindAnyObjectByType",
                "GameObject.Find",
                "GetComponent<",
                "AddComponent<",
                "new GameObject"
            };
            foreach (string pattern in forbidden)
                StringAssert.DoesNotContain(pattern, source, pattern);
        }

        [Test]
        public void Kinematics_RequireTheCorrectAuthoritativeAvailability()
        {
            CockpitSystemsSnapshot snapshot = Snapshot(
                CockpitDataAvailability.AirData |
                CockpitDataAvailability.Heading);
            Assert.That(
                AnalogInstrumentKinematics.TryEvaluateDialAngleDegrees(
                    AnalogInstrumentFunction.IndicatedAirspeedKnots,
                    snapshot, out float airspeed),
                Is.True);
            Assert.That(airspeed, Is.EqualTo(4f).Within(0.001f));
            Assert.That(
                AnalogInstrumentKinematics.TryEvaluateDialAngleDegrees(
                    AnalogInstrumentFunction.IndicatedVerticalSpeedFeetPerMinute,
                    snapshot, out float verticalSpeed),
                Is.True);
            Assert.That(verticalSpeed, Is.EqualTo(90f).Within(0.001f));

            snapshot.availability = CockpitDataAvailability.None;
            Assert.That(
                AnalogInstrumentKinematics.TryEvaluateDialAngleDegrees(
                    AnalogInstrumentFunction.IndicatedAirspeedKnots,
                    snapshot, out _),
                Is.False);
        }

        [Test]
        public void VerticalSpeedScale_IsZeroAtLeftAndSplitAcrossUpperLowerArcs()
        {
            Assert.That(
                AnalogInstrumentKinematics.EvaluateVerticalSpeedAngle(0f),
                Is.EqualTo(180f).Within(0.001f));
            Assert.That(
                AnalogInstrumentKinematics.EvaluateVerticalSpeedAngle(500f),
                Is.EqualTo(135f).Within(0.001f));
            Assert.That(
                AnalogInstrumentKinematics.EvaluateVerticalSpeedAngle(1000f),
                Is.EqualTo(90f).Within(0.001f));
            Assert.That(
                AnalogInstrumentKinematics.EvaluateVerticalSpeedAngle(2000f),
                Is.EqualTo(45f).Within(0.001f));
            Assert.That(
                AnalogInstrumentKinematics.EvaluateVerticalSpeedAngle(6000f),
                Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                AnalogInstrumentKinematics.EvaluateVerticalSpeedAngle(-500f),
                Is.EqualTo(-135f).Within(0.001f));
            Assert.That(
                AnalogInstrumentKinematics.EvaluateVerticalSpeedAngle(-1000f),
                Is.EqualTo(-90f).Within(0.001f));
            Assert.That(
                AnalogInstrumentKinematics.EvaluateVerticalSpeedAngle(-2000f),
                Is.EqualTo(-45f).Within(0.001f));
            Assert.That(
                AnalogInstrumentKinematics.EvaluateVerticalSpeedAngle(-6000f),
                Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void Kinematics_HaveZeroSteadyStateManagedAllocations()
        {
            CockpitSystemsSnapshot snapshot = Snapshot(
                CockpitDataAvailability.AirData |
                CockpitDataAvailability.Heading);
            for (int index = 0; index < 128; index++)
            {
                AnalogInstrumentKinematics.TryEvaluateDialAngleDegrees(
                    AnalogInstrumentFunction.IndicatedAirspeedKnots,
                    snapshot, out _);
                AnalogInstrumentKinematics.TryEvaluateDialAngleDegrees(
                    AnalogInstrumentFunction.IndicatedVerticalSpeedFeetPerMinute,
                    snapshot, out _);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 4096; index++)
            {
                AnalogInstrumentKinematics.TryEvaluateDialAngleDegrees(
                    AnalogInstrumentFunction.IndicatedAirspeedKnots,
                    snapshot, out _);
                AnalogInstrumentKinematics.TryEvaluateDialAngleDegrees(
                    AnalogInstrumentFunction.IndicatedVerticalSpeedFeetPerMinute,
                    snapshot, out _);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.EqualTo(0));
        }

        [Test]
        public void CombinedMeshAnimation_HasZeroSteadyStateManagedAllocations()
        {
            ProductionAnalogInstrumentLayout layout =
                AssetDatabase.LoadAssetAtPath<ProductionAnalogInstrumentLayout>(
                    CockpitAnalogInstrumentAuthoring.LayoutAssetPath);
            Assert.That(layout, Is.Not.Null,
                "Run 'Build Dynamic Analog Instrument Assets Only' first.");
            var animator = new AnalogInstrumentMeshAnimator(
                layout.DynamicInstrumentMesh, layout.Bindings);
            CockpitSystemsSnapshot snapshot = Snapshot(
                CockpitDataAvailability.AirData |
                CockpitDataAvailability.Heading);

            for (int index = 0; index < 128; index++)
                animator.Apply(snapshot, null, false);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 4096; index++)
                animator.Apply(snapshot, null, false);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.EqualTo(0));
        }

        [Test]
        public void CombinedMeshUpload_HasZeroSteadyStateManagedAllocations()
        {
            ProductionAnalogInstrumentLayout layout =
                AssetDatabase.LoadAssetAtPath<ProductionAnalogInstrumentLayout>(
                    CockpitAnalogInstrumentAuthoring.LayoutAssetPath);
            Assert.That(layout, Is.Not.Null,
                "Run 'Build Dynamic Analog Instrument Assets Only' first.");
            Mesh runtime = UnityEngine.Object.Instantiate(
                layout.DynamicInstrumentMesh);
            runtime.MarkDynamic();
            try
            {
                var animator = new AnalogInstrumentMeshAnimator(
                    layout.DynamicInstrumentMesh, layout.Bindings);
                CockpitSystemsSnapshot snapshot = Snapshot(
                    CockpitDataAvailability.AirData |
                    CockpitDataAvailability.Heading);
                for (int index = 0; index < 128; index++)
                    animator.Apply(snapshot, runtime, true);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int index = 0; index < 1024; index++)
                    animator.Apply(snapshot, runtime, true);
                long after = GC.GetAllocatedBytesForCurrentThread();
                Assert.That(after - before, Is.EqualTo(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(runtime);
            }
        }

        private static CockpitSystemsSnapshot Snapshot(
            CockpitDataAvailability availability)
        {
            return new CockpitSystemsSnapshot
            {
                isValid = true,
                availability = availability,
                indicatedAirspeedKnots = 140f,
                indicatedVerticalSpeedFeetPerMinute = 1000f,
                indicatedHeadingDegrees = 275f
            };
        }
    }
}
