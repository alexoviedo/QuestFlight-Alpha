#if UNITY_EDITOR
using System;
using NUnit.Framework;
using QuestFlight.Editor;
using QuestFlightLab.Aircraft;
using QuestFlightLab.Flight.Backends;
using UnityEditor;
using UnityEngine;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class ProductionAircraftNoseSteeringAnimatorEditModeTests
    {
        private GameObject _root;
        private ProductionAircraftNoseSteeringAnimator _animator;
        private Transform _pivot;
        private Transform _strut;
        private Transform _noseWheelPant;
        private Transform _propeller;
        private Transform _spinner;
        private Transform _positiveMainGear;
        private Transform _negativeMainGear;
        private Quaternion _pivotRestRotation;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("NoseSteeringAnimatorTestRoot");
            _animator =
                _root.AddComponent<
                    ProductionAircraftNoseSteeringAnimator>();
            _pivot = Child("ProductionNoseSteeringPivot", _root.transform);
            _pivot.localRotation = Quaternion.Euler(1f, 2f, 3f);
            _pivotRestRotation = _pivot.localRotation;
            _strut = Child(
                "Cessna_Exterior_Body_MAT_0.019",
                _pivot);
            _noseWheelPant = Child(
                "Cessna_Exterior_Body_MAT_0.021",
                _pivot);
            _propeller = Child("StaticPropeller", _root.transform);
            _spinner = Child("StaticSpinner", _root.transform);
            _positiveMainGear =
                Child("StaticPositiveXMainGear", _root.transform);
            _negativeMainGear =
                Child("StaticNegativeXMainGear", _root.transform);
            Configure(direction: 1f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);
        }

        [Test]
        public void SteeringUsesRudderWithSpeedFadeAndNeutralizesAirborne()
        {
            FlightDynamicsState state = ValidState();
            FlightDynamicsControlSnapshot controls =
                FlightDynamicsControlSnapshot.NeutralIdle;
            controls.rudder = 1f;

            state.weightOnWheels = true;
            state.groundSpeedKnots = 5.0;
            Assert.That(
                _animator.StepForTest(state, controls, 0.01f), Is.True);
            Assert.That(
                _animator.AppliedSteeringAngleDegrees,
                Is.EqualTo(10f).Within(0.001f));
            Assert.That(
                Quaternion.Angle(
                    _pivotRestRotation,
                    _pivot.localRotation),
                Is.EqualTo(10f).Within(0.001f));

            state.groundSpeedKnots = 20.0;
            Assert.That(
                _animator.StepForTest(state, controls, 0.01f), Is.True);
            Assert.That(
                _animator.AppliedSteeringAngleDegrees,
                Is.EqualTo(5f).Within(0.001f));

            state.groundSpeedKnots = 35.0;
            Assert.That(
                _animator.StepForTest(state, controls, 0.01f), Is.True);
            Assert.That(
                Quaternion.Angle(
                    _pivotRestRotation,
                    _pivot.localRotation),
                Is.LessThan(0.001f));

            state.weightOnWheels = false;
            state.groundSpeedKnots = 5.0;
            Assert.That(
                _animator.StepForTest(state, controls, 0.01f), Is.True);
            Assert.That(
                Quaternion.Angle(
                    _pivotRestRotation,
                    _pivot.localRotation),
                Is.LessThan(0.001f));
        }

        [Test]
        public void InvalidStateAndNonUnitDirectionFailClosed()
        {
            Quaternion before = _pivot.localRotation;
            FlightDynamicsState invalid = ValidState();
            invalid.groundSpeedKnots = double.NaN;
            Assert.That(
                _animator.StepForTest(
                    invalid,
                    FlightDynamicsControlSnapshot.NeutralIdle,
                    1f),
                Is.False);
            Assert.That(_pivot.localRotation, Is.EqualTo(before));

            Configure(direction: 0.99999f);
            Assert.That(
                _animator.StepForTest(
                    ValidState(),
                    FlightDynamicsControlSnapshot.NeutralIdle,
                    1f),
                Is.False);
            Assert.That(_pivot.localRotation, Is.EqualTo(before));
        }

        [Test]
        public void BothAuditedSourcesMustRemainBelowTheSteeringPivot()
        {
            ProductionAircraftNoseSteeringAnimator.SteeringBinding invalid =
                Binding(direction: 1f);
            invalid.noseWheelPant = _positiveMainGear;
            _animator.ConfigureAuthoredBindings(null, invalid);

            Assert.That(
                _animator.StepForTest(
                    ValidState(),
                    FlightDynamicsControlSnapshot.NeutralIdle,
                    1f),
                Is.False);
        }

        [Test]
        public void OnlyNoseYawMovesAndExcludedGeometryRemainsStatic()
        {
            int childCount = _root.transform.childCount;
            Quaternion strutLocal = _strut.localRotation;
            Quaternion nosePantLocal = _noseWheelPant.localRotation;
            Quaternion propeller = _propeller.localRotation;
            Quaternion spinner = _spinner.localRotation;
            Quaternion positiveMain = _positiveMainGear.localRotation;
            Quaternion negativeMain = _negativeMainGear.localRotation;
            FlightDynamicsState state = ValidState();
            state.engineRpm = 2700.0;
            state.weightOnWheels = true;
            state.groundSpeedKnots = 25.0;
            FlightDynamicsControlSnapshot controls =
                FlightDynamicsControlSnapshot.NeutralIdle;
            controls.rudder = 0.7f;

            Assert.That(
                _animator.StepForTest(state, controls, 1f / 72f),
                Is.True);

            Assert.That(_root.transform.childCount, Is.EqualTo(childCount));
            Assert.That(_strut.localRotation, Is.EqualTo(strutLocal));
            Assert.That(
                _noseWheelPant.localRotation,
                Is.EqualTo(nosePantLocal));
            Assert.That(_propeller.localRotation, Is.EqualTo(propeller));
            Assert.That(_spinner.localRotation, Is.EqualTo(spinner));
            Assert.That(
                _positiveMainGear.localRotation,
                Is.EqualTo(positiveMain));
            Assert.That(
                _negativeMainGear.localRotation,
                Is.EqualTo(negativeMain));
            Assert.That(
                _root.GetComponentsInChildren<Renderer>(true),
                Is.Empty,
                "Steering must add no renderer, material, or blur geometry.");
        }

        [Test]
        public void SteadyStateStepDoesNotAllocateManagedMemory()
        {
            FlightDynamicsState state = ValidState();
            state.weightOnWheels = true;
            state.groundSpeedKnots = 12.0;
            FlightDynamicsControlSnapshot controls =
                FlightDynamicsControlSnapshot.NeutralIdle;
            controls.rudder = 0.45f;
            for (int i = 0; i < 32; i++)
            {
                Assert.That(
                    _animator.StepForTest(
                        state,
                        controls,
                        1f / 72f),
                    Is.True);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 4096; i++)
            {
                state.simulationTimeSeconds += 1.0 / 72.0;
                _animator.StepForTest(state, controls, 1f / 72f);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.Zero);
        }

        private void Configure(float direction)
        {
            _animator.ConfigureAuthoredBindings(
                null,
                Binding(direction));
        }

        private ProductionAircraftNoseSteeringAnimator.SteeringBinding
            Binding(float direction)
        {
            return new
                ProductionAircraftNoseSteeringAnimator.SteeringBinding
            {
                steeringPivot = _pivot,
                steeringStrutFairing = _strut,
                noseWheelPant = _noseWheelPant,
                localRotationAxis = Vector3.up,
                maximumSteeringAngleDegrees = 10f,
                fullAuthorityAtOrBelowKnots = 5f,
                zeroAuthorityAtOrAboveKnots = 35f,
                direction = direction
            };
        }

        private static Transform Child(string name, Transform parent)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static FlightDynamicsState ValidState()
        {
            return new FlightDynamicsState
            {
                simulationTimeSeconds = 1.0,
                latitudeDegrees = 40.03936527,
                longitudeDegrees = -105.22608958,
                altitudeMslMeters = 1612.0,
                altitudeAglMeters = 0.5,
                calibratedAirspeedKnots = 0.0,
                groundSpeedKnots = 0.0,
                verticalSpeedFeetPerMinute = 0.0,
                headingDegrees = 89.972,
                pitchDegrees = 0.0,
                bankDegrees = 0.0,
                angleOfAttackDegrees = 0.0,
                sideslipDegrees = 0.0,
                engineRpm = 700.0,
                loadFactorG = 1.0,
                weightOnWheels = true,
                positionUnityMeters = Vector3.zero,
                rotationUnity = Quaternion.identity,
                velocityUnityMetersPerSecond = Vector3.zero,
                angularVelocityBodyDegreesPerSecond = Vector3.zero
            };
        }
    }

    public sealed class
        ProductionAircraftNoseSteeringAuthoringEditModeTests
    {
        [Test]
        public void AuthoringIsIdempotentAndPreservesRenderAssetIdentity()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(
                ProductionAircraftNoseSteeringAuthoring
                    .AircraftPrefabPath);
            try
            {
                Transform strutBefore = FindEither(
                    root,
                    ProductionAircraftNoseSteeringAuthoring
                        .OriginalSteeringStrutPath,
                    ProductionAircraftNoseSteeringAuthoring
                        .AuthoredSteeringStrutPath);
                Transform wheelBefore = FindEither(
                    root,
                    ProductionAircraftNoseSteeringAuthoring
                        .OriginalNoseWheelPantPath,
                    ProductionAircraftNoseSteeringAuthoring
                        .AuthoredNoseWheelPantPath);
                Mesh strutMesh = strutBefore
                    .GetComponentInChildren<MeshFilter>(true)
                    .sharedMesh;
                Material strutMaterial = strutBefore
                    .GetComponentInChildren<MeshRenderer>(true)
                    .sharedMaterial;
                Mesh wheelMesh = wheelBefore
                    .GetComponentInChildren<MeshFilter>(true)
                    .sharedMesh;
                Material wheelMaterial = wheelBefore
                    .GetComponentInChildren<MeshRenderer>(true)
                    .sharedMaterial;
                Matrix4x4 strutMatrix = strutBefore.localToWorldMatrix;
                Matrix4x4 wheelMatrix = wheelBefore.localToWorldMatrix;
                Transform propeller = root.transform.Find(
                    ProductionAircraftNoseSteeringAuthoring
                        .StaticPropellerPath);
                Transform spinner = root.transform.Find(
                    ProductionAircraftNoseSteeringAuthoring
                        .StaticSpinnerPath);
                Transform positiveMain = root.transform.Find(
                    ProductionAircraftNoseSteeringAuthoring
                        .StaticPositiveXMainGearPath);
                Transform negativeMain = root.transform.Find(
                    ProductionAircraftNoseSteeringAuthoring
                        .StaticNegativeXMainGearPath);
                Assert.That(propeller, Is.Not.Null);
                Assert.That(spinner, Is.Not.Null);
                Assert.That(positiveMain, Is.Not.Null);
                Assert.That(negativeMain, Is.Not.Null);
                Matrix4x4 propellerMatrix = propeller.localToWorldMatrix;
                Matrix4x4 spinnerMatrix = spinner.localToWorldMatrix;
                Matrix4x4 positiveMainMatrix =
                    positiveMain.localToWorldMatrix;
                Matrix4x4 negativeMainMatrix =
                    negativeMain.localToWorldMatrix;
                int rendererCount =
                    root.GetComponentsInChildren<Renderer>(true).Length;

                ProductionAircraftNoseSteeringAuthoring
                    .AuthorPrefabContents(root);
                ProductionAircraftNoseSteeringAuthoring
                    .AuthorPrefabContents(root);

                bool valid = ProductionAircraftNoseSteeringAuthoring
                    .ValidateAuthoredPrefabContents(
                        root,
                        out string error);
                Assert.That(valid, Is.True, error);
                Transform strutAfter = root.transform.Find(
                    ProductionAircraftNoseSteeringAuthoring
                        .AuthoredSteeringStrutPath);
                Transform wheelAfter = root.transform.Find(
                    ProductionAircraftNoseSteeringAuthoring
                        .AuthoredNoseWheelPantPath);
                Assert.That(
                    strutAfter.GetComponentInChildren<MeshFilter>(true)
                        .sharedMesh,
                    Is.SameAs(strutMesh));
                Assert.That(
                    strutAfter.GetComponentInChildren<MeshRenderer>(true)
                        .sharedMaterial,
                    Is.SameAs(strutMaterial));
                Assert.That(
                    wheelAfter.GetComponentInChildren<MeshFilter>(true)
                        .sharedMesh,
                    Is.SameAs(wheelMesh));
                Assert.That(
                    wheelAfter.GetComponentInChildren<MeshRenderer>(true)
                        .sharedMaterial,
                    Is.SameAs(wheelMaterial));
                Assert.That(
                    MatrixApproximately(
                        strutMatrix,
                        strutAfter.localToWorldMatrix),
                    Is.True);
                Assert.That(
                    MatrixApproximately(
                        wheelMatrix,
                        wheelAfter.localToWorldMatrix),
                    Is.True);
                Assert.That(
                    MatrixApproximately(
                        propellerMatrix,
                        propeller.localToWorldMatrix),
                    Is.True);
                Assert.That(
                    MatrixApproximately(
                        spinnerMatrix,
                        spinner.localToWorldMatrix),
                    Is.True);
                Assert.That(
                    MatrixApproximately(
                        positiveMainMatrix,
                        positiveMain.localToWorldMatrix),
                    Is.True);
                Assert.That(
                    MatrixApproximately(
                        negativeMainMatrix,
                        negativeMain.localToWorldMatrix),
                    Is.True);
                Assert.That(
                    root.GetComponentsInChildren<Renderer>(true).Length,
                    Is.EqualTo(rendererCount));
                Assert.That(
                    root.GetComponentsInChildren<
                        ProductionAircraftNoseSteeringAnimator>(true)
                        .Length,
                    Is.EqualTo(1));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Transform FindEither(
            GameObject root,
            string originalPath,
            string authoredPath)
        {
            Transform original = root.transform.Find(originalPath);
            Transform authored = root.transform.Find(authoredPath);
            Assert.That(
                original != null ^ authored != null,
                Is.True);
            return original != null ? original : authored;
        }

        private static bool MatrixApproximately(
            Matrix4x4 expected,
            Matrix4x4 actual)
        {
            for (int index = 0; index < 16; index++)
            {
                if (Mathf.Abs(expected[index] - actual[index]) >
                    0.00001f)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
#endif
