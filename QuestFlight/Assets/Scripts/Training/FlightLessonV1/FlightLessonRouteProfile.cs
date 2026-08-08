using System;
using UnityEngine;

namespace QuestFlightLab.Training.FlightLessonV1
{
    [CreateAssetMenu(
        fileName = "FlightLessonRouteProfile",
        menuName = "QuestFlight/Training/Flight Lesson Route Profile V1")]
    public sealed class FlightLessonRouteProfile : ScriptableObject
    {
        [SerializeField]
        private FlightLessonRouteDefinition definition =
            new FlightLessonRouteDefinition();

        public FlightLessonRouteDefinition Definition => definition;

        // Authoring/import seam. Product integration must serialize the resulting asset;
        // runtime code must not create or replace the scene hierarchy.
        public void SetDefinitionForAuthoring(FlightLessonRouteDefinition value)
        {
            definition = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
}
