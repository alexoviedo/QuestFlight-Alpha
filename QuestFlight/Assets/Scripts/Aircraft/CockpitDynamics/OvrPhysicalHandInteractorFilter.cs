using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace QuestFlightLab.Aircraft.CockpitDynamics
{
    /// <summary>
    /// Keeps Meta's synthetic controller-driven hand interactor from
    /// selecting the same continuous control as the physical controller grab
    /// interactor. Real tracked-hand interactors remain eligible.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OvrPhysicalHandInteractorFilter :
        MonoBehaviour,
        IGameObjectFilter
    {
        public bool Filter(GameObject interactor)
        {
            if (interactor == null)
                return false;

            if (interactor.GetComponentInParent<
                    ControllerHandDataSource>(true) != null)
                return false;

            for (Transform current = interactor.transform;
                 current != null;
                 current = current.parent)
            {
                if (current.name == "Controller and Hand")
                    return false;
            }
            return true;
        }
    }
}
