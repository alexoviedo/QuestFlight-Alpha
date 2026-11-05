using Oculus.Interaction;
using QuestFlightLab.Input;
using UnityEngine;

namespace QuestFlight.Product
{
    // Uses Meta's existing tracking/selection; ownership and focus gates remain
    // ProductControlInput's. The authored body is kinematic, presentation only.
    public sealed class SoaringLeverGrabTransformer : MonoBehaviour,ITransformer
    {
        public ProductControlInput input;
        public Transform stableFrame;
        public ProductTouchControlChannel channel=ProductTouchControlChannel.GliderAirbrakeLever;
        public bool releaseHandle;
        public bool linearTravel;
        public float fullTravelMeters=.18f;
        public float gripRadiusMeters=.27f;
        public float maximumAngleDegrees=65f;
#if UNITY_EDITOR
        public bool? EditorVerificationFocus { get; set; }
#endif
        private bool HasFocus
        {
            get
            {
#if UNITY_EDITOR
                if(EditorVerificationFocus.HasValue)return EditorVerificationFocus.Value;
#endif
                return Application.isFocused && (Application.platform!=RuntimePlatform.Android ||
                    OVRManager.hasInputFocus && OVRManager.instance!=null && OVRManager.instance.isUserPresent);
            }
        }
        private bool CanManipulate => input!=null && stableFrame!=null && input.GliderMode &&
            input.CanAcceptTouchOverrides && !input.Paused && HasFocus;
        private IGrabbable grabbable;
        private Vector3 start;
        private float baseline;
        private bool active, fired;
        public float PullMeters { get; private set; }
        public void Initialize(IGrabbable value) { grabbable=value; }
        public void BeginTransform()
        {
            EndTransform();
            if(!CanManipulate || grabbable==null || grabbable.GrabPoints.Count!=1)return;
            start=stableFrame.InverseTransformPoint(grabbable.GrabPoints[0].position);
            baseline=channel==ProductTouchControlChannel.GliderTrimLever?input.Current.trim:input.GliderLeverPosition;
            active=releaseHandle || input.TryBeginTouchOverride(channel,GetInstanceID(),baseline);fired=false;
        }
        public void UpdateTransform()
        {
            if(!active)return;
            if(!CanManipulate || grabbable.GrabPoints.Count!=1)
            {EndTransform();return;}
            Vector3 current=stableFrame.InverseTransformPoint(grabbable.GrabPoints[0].position);
            float travel=start.z-current.z;
            PullMeters=Mathf.Clamp(travel,0,.06f);
            if(releaseHandle) { if(!fired && travel>.025f)fired=input.RequestTowRelease();return; }
            float value=linearTravel?ValueFromLinearPull(baseline,travel,fullTravelMeters):
                ValueFromPull(baseline,travel,gripRadiusMeters,maximumAngleDegrees,
                    channel==ProductTouchControlChannel.GliderTrimLever);
            if(!input.TryUpdateTouchOverride(channel,GetInstanceID(),value))EndTransform();
        }
        // Invert the actual grip arc, so hand displacement and displayed travel
        // agree even after regrabbing at a partly deployed position.
        public static float ValueFromLinearPull(float baseline,float pull,float travel)
            => Mathf.Clamp01(baseline+pull/Mathf.Max(.01f,travel));
        public static float ValueFromPull(float baseline,float pull,float radius,float degrees,bool signed)
        {
            float angle=Mathf.Clamp(degrees,1,85)*Mathf.Deg2Rad;
            float tip=Mathf.Sin(Mathf.Clamp(baseline,signed?-1:0,1)*angle)+pull/Mathf.Max(.01f,radius);
            float limit=Mathf.Sin(angle);
            return Mathf.Asin(Mathf.Clamp(tip,signed?-limit:0,limit))/angle;
        }
        public void EndTransform() {if(active&&!releaseHandle && input!=null)input.TryEndTouchOverride(channel,GetInstanceID());active=false;PullMeters=0;}
        private void OnDisable()=>EndTransform();
        private void OnApplicationFocus(bool focused){if(!focused)EndTransform();}
        private void OnApplicationPause(bool paused){if(paused)EndTransform();}
    }
}
