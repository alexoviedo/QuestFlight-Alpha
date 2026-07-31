#if UNITY_EDITOR
using System.IO;
using NUnit.Framework;
using QuestFlightLab.Aircraft.CockpitDynamics;
using UnityEngine;

namespace QuestFlightLab.Tests.EditMode
{
    public sealed class CockpitTouchInteractionV2EditModeTests
    {
        private GameObject _root;
        private Transform _left;
        private Transform _right;
        private BoxCollider _nearCollider;
        private BoxCollider _farCollider;
        private CockpitTouchInteractionV2Controller _controller;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TouchV2TestRoot");
            _left = Child("LeftRay", _root.transform);
            _right = Child("RightRay", _root.transform);
            _left.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _right.SetPositionAndRotation(
                new Vector3(0.1f, 0f, 0f),
                Quaternion.identity);
            _nearCollider = Collider("NearYoke", 0.3f);
            _farCollider = Collider("FarThrottle", 0.6f);
            _controller =
                _root.AddComponent<CockpitTouchInteractionV2Controller>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void CentralRaycastQueueSelectsNearestBindingAndEmitsGrabLifecycle()
        {
            _controller.ConfigureAuthoredReferences(
                _left,
                _right,
                ~0,
                0.8f,
                new[]
                {
                    Binding(
                        "near_yoke",
                        _nearCollider,
                        CockpitTouchInteractionV2Target.PilotYoke,
                        CockpitTouchInteractionV2Hand.Left),
                    Binding(
                        "far_throttle",
                        _farCollider,
                        CockpitTouchInteractionV2Target.Throttle,
                        CockpitTouchInteractionV2Hand.Right)
                },
                null);
            Assert.That(
                _controller.ValidateAuthoredReferences(out string error),
                Is.True,
                error);
            Physics.SyncTransforms();

            _controller.ProcessPointerForTest(
                CockpitTouchInteractionV2Hand.Left,
                true,
                1f,
                10);
            AssertCommand(
                CockpitTouchInteractionV2Operation.BeginGrab,
                CockpitTouchInteractionV2Target.PilotYoke,
                "near_yoke");

            _controller.ProcessPointerForTest(
                CockpitTouchInteractionV2Hand.Left,
                true,
                1.01f,
                11);
            AssertCommand(
                CockpitTouchInteractionV2Operation.UpdateGrab,
                CockpitTouchInteractionV2Target.PilotYoke,
                "near_yoke");

            _controller.ProcessPointerForTest(
                CockpitTouchInteractionV2Hand.Left,
                false,
                1.02f,
                12);
            AssertCommand(
                CockpitTouchInteractionV2Operation.EndGrab,
                CockpitTouchInteractionV2Target.PilotYoke,
                "near_yoke");
            Assert.That(_controller.QueuedCommandCount, Is.Zero);
        }

        [Test]
        public void HandRestrictionsAndDuplicateCollidersFailClosed()
        {
            CockpitTouchInteractionV2Controller.Binding first =
                Binding(
                    "throttle",
                    _nearCollider,
                    CockpitTouchInteractionV2Target.Throttle,
                    CockpitTouchInteractionV2Hand.Right);
            _controller.ConfigureAuthoredReferences(
                _left,
                _right,
                ~0,
                0.8f,
                new[] { first },
                null);
            Physics.SyncTransforms();
            _controller.ProcessPointerForTest(
                CockpitTouchInteractionV2Hand.Left,
                true,
                1f,
                1);
            Assert.That(_controller.QueuedCommandCount, Is.Zero);

            _controller.ConfigureAuthoredReferences(
                _left,
                _right,
                ~0,
                0.8f,
                new[]
                {
                    first,
                    Binding(
                        "mixture",
                        _nearCollider,
                        CockpitTouchInteractionV2Target.Mixture,
                        CockpitTouchInteractionV2Hand.Right)
                },
                null);
            Assert.That(
                _controller.ValidateAuthoredReferences(out string error),
                Is.False);
            StringAssert.Contains("bound twice", error);
        }

        [Test]
        public void ReauthoringClearsQueuedCommandsAndCaptureState()
        {
            CockpitTouchInteractionV2Controller.Binding binding =
                Binding(
                    "near_yoke",
                    _nearCollider,
                    CockpitTouchInteractionV2Target.PilotYoke,
                    CockpitTouchInteractionV2Hand.Left);
            _controller.ConfigureAuthoredReferences(
                _left,
                _right,
                ~0,
                0.8f,
                new[] { binding },
                null);
            Physics.SyncTransforms();
            _controller.ProcessPointerForTest(
                CockpitTouchInteractionV2Hand.Left,
                true,
                1f,
                1);
            Assert.That(_controller.QueuedCommandCount, Is.EqualTo(1));

            _controller.ConfigureAuthoredReferences(
                _left,
                _right,
                ~0,
                0.8f,
                new[] { binding },
                null);
            Assert.That(_controller.QueuedCommandCount, Is.Zero);

            _controller.ProcessPointerForTest(
                CockpitTouchInteractionV2Hand.Left,
                true,
                2f,
                2);
            AssertCommand(
                CockpitTouchInteractionV2Operation.BeginGrab,
                CockpitTouchInteractionV2Target.PilotYoke,
                "near_yoke");
        }

        [Test]
        public void RuntimeUsesOneNonAllocLoopAndNeverBuildsHierarchy()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Scripts/Aircraft/CockpitDynamics/" +
                "CockpitTouchInteractionV2Controller.cs");
            string source = File.ReadAllText(path);
            StringAssert.Contains("Physics.RaycastNonAlloc", source);
            Assert.That(
                Occurrences(source, "private void Update()"),
                Is.EqualTo(1));
            string[] forbidden =
            {
                "FindObjectOfType",
                "FindFirstObjectByType",
                "FindAnyObjectByType",
                "GameObject.Find",
                "AddComponent<",
                "new GameObject",
                "Camera.main"
            };
            foreach (string value in forbidden)
                StringAssert.DoesNotContain(value, source, value);
        }

        private void AssertCommand(
            CockpitTouchInteractionV2Operation operation,
            CockpitTouchInteractionV2Target target,
            string id)
        {
            Assert.That(
                _controller.TryDequeueCommand(
                    out CockpitTouchInteractionV2Command command),
                Is.True);
            Assert.That(command.operation, Is.EqualTo(operation));
            Assert.That(command.target, Is.EqualTo(target));
            Assert.That(command.bindingId, Is.EqualTo(id));
        }

        private static CockpitTouchInteractionV2Controller.Binding Binding(
            string id,
            Collider collider,
            CockpitTouchInteractionV2Target target,
            CockpitTouchInteractionV2Hand hand)
        {
            return new CockpitTouchInteractionV2Controller.Binding
            {
                id = id,
                targetCollider = collider,
                target = target,
                allowedHand = hand,
                mode = CockpitTouchInteractionV2Mode.GrabPose
            };
        }

        private BoxCollider Collider(string name, float z)
        {
            Transform value = Child(name, _root.transform);
            value.localPosition = new Vector3(0f, 0f, z);
            BoxCollider collider =
                value.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.1f, 0.1f, 0.1f);
            collider.isTrigger = true;
            return collider;
        }

        private static Transform Child(string name, Transform parent)
        {
            GameObject value = new GameObject(name);
            value.transform.SetParent(parent, false);
            return value.transform;
        }

        private static int Occurrences(string source, string value)
        {
            int count = 0;
            int start = 0;
            while ((start = source.IndexOf(
                       value,
                       start,
                       System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                start += value.Length;
            }
            return count;
        }
    }
}
#endif
