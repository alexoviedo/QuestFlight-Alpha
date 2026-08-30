#if UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using Oculus.Interaction.Surfaces;
using QuestFlightLab.Aircraft.CockpitDynamics;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class FlapSelectorInteractionEditModeTests
    {
        private const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";

        private GameObject _instance;

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_instance);
        }

        [Test]
        public void FlapSelectorHasComfortableIsolatedAcquisitionVolume()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                AircraftPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            _instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            OvrCockpitPointableAdapter[] adapters = _instance
                .GetComponentsInChildren<OvrCockpitPointableAdapter>(true);
            OvrCockpitPointableAdapter flapAdapter = adapters.Single(
                value => value.Target ==
                    CockpitTouchInteractionV2Target.FlapSelector);
            BoxCollider flapCollider = TargetCollider(flapAdapter);

            Assert.That(flapCollider.center.magnitude,
                Is.LessThan(0.001f),
                "The larger target must stay centered on the visible selector.");
            Assert.That(flapCollider.size.x,
                Is.InRange(0.058f, 0.065f));
            Assert.That(flapCollider.size.y,
                Is.InRange(0.048f, 0.055f));
            Assert.That(flapCollider.size.z,
                Is.InRange(0.080f, 0.090f));

            BoundsClipper clipper = flapAdapter
                .GetComponentInChildren<BoundsClipper>(true);
            Assert.That(clipper, Is.Not.Null);
            SerializedProperty clipperSize = new SerializedObject(clipper)
                .FindProperty("_size");
            Assert.That(clipperSize, Is.Not.Null);
            Assert.That(Vector3.Distance(
                    clipperSize.vector3Value,
                    flapCollider.size),
                Is.LessThan(0.00001f),
                "Meta poke and ray targets must use the same volume.");

            foreach (OvrCockpitPointableAdapter other in adapters.Where(
                         value => value != flapAdapter))
            {
                BoxCollider otherCollider = TargetCollider(other);
                Assert.That(
                    flapCollider.bounds.Intersects(otherCollider.bounds),
                    Is.False,
                    $"Expanded flap target overlaps mapped control " +
                    $"'{other.BindingId}'.");
            }
        }

        private static BoxCollider TargetCollider(
            OvrCockpitPointableAdapter adapter)
        {
            SerializedProperty property = new SerializedObject(adapter)
                .FindProperty("targetCollider");
            Assert.That(property, Is.Not.Null);
            BoxCollider collider = property.objectReferenceValue as BoxCollider;
            Assert.That(collider, Is.Not.Null, adapter.BindingId);
            return collider;
        }
    }
}
#endif
