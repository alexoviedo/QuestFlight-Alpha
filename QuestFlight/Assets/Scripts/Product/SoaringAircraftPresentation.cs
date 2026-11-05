using QuestFlightLab.Flight.Backends;
using QuestFlightLab.Runtime;
using UnityEngine;

namespace QuestFlight.Product
{
    // All objects and references are serialized by the editor authorer. This
    // component switches presentation only, never creates a player or camera.
    [DisallowMultipleComponent]
    public sealed class SoaringAircraftPresentation : MonoBehaviour
    {
        public FlightAircraftDefinition c172, glider;
        public AircraftReferenceFrameRig frame;
        public GameObject gliderRoot;
        public GameObject[] c172Roots;
        public bool[] c172RootActive;
        public Behaviour[] c172Consumers;
        public bool[] c172ConsumerEnabled;
        public Vector3 c172SeatPosition;
        public Quaternion c172SeatRotation=Quaternion.identity;
        public SoaringInstrumentPresenter instruments;
        public bool Ready => gliderRoot!=null && instruments!=null && frame!=null &&
            c172!=null && glider!=null && c172Roots!=null && c172Roots.Length>0;
        public bool IsGliderVisible => gliderRoot!=null && gliderRoot.activeSelf;
        public bool Apply(FlightAircraftDefinition definition)
        {
            if (!Ready || (definition!=c172 && definition!=glider) || Time.timeScale>0) return false;
            bool selected=definition.IsGlider;
            for(int i=0;i<c172Roots.Length;i++) if(c172Roots[i]!=null)c172Roots[i].SetActive(!selected && c172RootActive[i]);
            for(int i=0;i<c172Consumers.Length;i++) if(c172Consumers[i]!=null)c172Consumers[i].enabled=!selected && c172ConsumerEnabled[i];
            gliderRoot.SetActive(selected);
            frame.SetPilotSeatLocalPose(selected?glider.eyeFromCgMeters:c172SeatPosition,
                selected?Quaternion.identity:c172SeatRotation);
            frame.ResetPresentationHistory();
            return true;
        }
    }
}
