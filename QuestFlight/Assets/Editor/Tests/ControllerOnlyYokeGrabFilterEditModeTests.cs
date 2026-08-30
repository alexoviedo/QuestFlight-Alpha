#if UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using QuestFlightLab.Aircraft.CockpitDynamics.TouchInputV2;
using UnityEditor;
using UnityEngine;

namespace QuestFlight.Editor.Tests
{
    public sealed class ControllerOnlyYokeGrabFilterEditModeTests
    {
        private const string AircraftPrefabPath =
            "Assets/Production/Prefabs/ProductionC172AircraftRig.prefab";

        private GameObject _filterObject;
        private GameObject _trackedHandInteractor;
        private GameObject _touchControllerInteractor;

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_filterObject);
            Object.DestroyImmediate(_trackedHandInteractor);
            Object.DestroyImmediate(_touchControllerInteractor);
        }

        [Test]
        public void FilterRejectsTrackedHandAndAcceptsTouchController()
        {
            _filterObject = new GameObject("Filter");
            _trackedHandInteractor = new GameObject("HandGrabInteractor");
            _touchControllerInteractor =
                new GameObject("ControllerHandGrabInteractor");
            ControllerOnlyYokeGrabFilter filter =
                _filterObject.AddComponent<ControllerOnlyYokeGrabFilter>();
            _touchControllerInteractor.AddComponent<ControllerRef>();

            Assert.That(filter.Filter(_trackedHandInteractor), Is.False);
            Assert.That(filter.Filter(_touchControllerInteractor), Is.True);
            Assert.That(filter.Filter(null), Is.False);
        }

        [Test]
        public void SerializedPilotYokeHasExactlyOneControllerOnlyFilter()
        {
            GameObject aircraft = AssetDatabase.LoadAssetAtPath<GameObject>(
                AircraftPrefabPath);
            Assert.That(aircraft, Is.Not.Null);
            Transform yoke = aircraft
                .GetComponentsInChildren<Transform>(true)
                .Single(item => item.name == "OVR_pilot_yoke_grab");
            ControllerOnlyYokeGrabFilter[] filters =
                yoke.GetComponents<ControllerOnlyYokeGrabFilter>();
            Assert.That(filters, Has.Length.EqualTo(1));

            HandGrabInteractable interactable = yoke
                .GetComponentsInChildren<HandGrabInteractable>(true)
                .Single();
            SerializedProperty serializedFilters = new SerializedObject(
                    interactable)
                .FindProperty("_interactorFilters");
            Assert.That(serializedFilters, Is.Not.Null);
            Assert.That(serializedFilters.arraySize, Is.EqualTo(1));
            Assert.That(
                serializedFilters.GetArrayElementAtIndex(0)
                    .objectReferenceValue,
                Is.SameAs(filters[0]));
        }
    }
}
#endif
