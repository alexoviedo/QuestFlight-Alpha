using UnityEngine;

namespace QuestFlightLab.Flight.Soaring
{
    [CreateAssetMenu(menuName="QuestFlight/Soaring/Towline reference")]
    public sealed class TowlineDefinition : ScriptableObject
    {
        public double naturalLengthMeters=60,stiffnessNewtonsPerMeter=8000,dampingNewtonSecondsPerMeter=700;
        public double maximumExtensionMeters=2,gliderWeakLinkNewtons=4500,tugWeakLinkNewtons=5000;
        public double dampingEngagementExtensionMeters=.02;
        public int gliderAttachmentId,tugAttachmentId;
        public bool referenceConfiguration=true;
        [TextArea] public string source="Engineering fixture: 60 m elastic line, linear stiffness, extension-ramped axial damping. Not measured rope, weak link, or Mile High equipment.";
        public bool IsValid => naturalLengthMeters>5 && naturalLengthMeters<150 && stiffnessNewtonsPerMeter>0 && dampingNewtonSecondsPerMeter>=0 &&
            maximumExtensionMeters>0 && gliderWeakLinkNewtons>0 && tugWeakLinkNewtons>0 && dampingEngagementExtensionMeters>0 && gliderAttachmentId==0 && tugAttachmentId==0 &&
            !double.IsInfinity(naturalLengthMeters+stiffnessNewtonsPerMeter+dampingNewtonSecondsPerMeter+maximumExtensionMeters+gliderWeakLinkNewtons+tugWeakLinkNewtons+dampingEngagementExtensionMeters);
    }
}
