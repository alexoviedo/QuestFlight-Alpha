using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics.TouchInputV2
{
    /// <summary>
    /// Keeps the pilot yoke available to Touch controllers while preventing
    /// tracked-hand grab interactors from selecting it. The filter is attached
    /// only to the serialized pilot-yoke interactable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ControllerOnlyYokeGrabFilter :
        MonoBehaviour,
        IGameObjectFilter
    {
        public bool Filter(GameObject interactor)
        {
            return interactor != null &&
                   interactor.TryGetComponent(out ControllerRef _);
        }
    }
}
