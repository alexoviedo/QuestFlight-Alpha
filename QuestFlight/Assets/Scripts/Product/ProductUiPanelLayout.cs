using UnityEngine;

namespace QuestFlight.Product
{
    public sealed class ProductUiPanelLayout : MonoBehaviour
    {
        [SerializeField] private ProductUiLabel title,body,hint;
        [SerializeField] private Transform surface;
        [SerializeField] private ProductActionButton[] buttons;
        [SerializeField] private Transform cockpitAnchor;
        public const float CockpitScale = .42f;
        public void ConfigureCockpitClearance(Transform anchor) { cockpitAnchor=anchor; }
        private string lastTitle,lastBody,lastHint;
        private int lastCount=-1;
        private bool lastCompact;
        private float clearanceHeight,lastTextScale,supplementHeight;
        private bool cockpitVisible=true;
        private Vector3 menuEyeLocal;
        public void SetPresentationContext(bool cockpit, Transform eye)
        {
            if(cockpitVisible!=cockpit || !gameObject.activeInHierarchy)
                lastCount=-1;
            if(cockpitVisible && !cockpit && eye!=null && cockpitAnchor!=null)
                menuEyeLocal=cockpitAnchor.InverseTransformPoint(eye.position);
            cockpitVisible=cockpit;
        }
        [SerializeField] private Transform supplement;
        public void ConfigureSupplement(Transform value){supplement=value;}
        public void SetSupplementHeight(float value) { if(supplementHeight==value)return;supplementHeight=value;lastCount=-1; }
        private void ApplyCockpitPose(bool compact)
        {
            if(!cockpitVisible)
            {
                const float menuScale=.9f;
                transform.localScale=Vector3.one*menuScale;
                Vector3 position=menuEyeLocal+new Vector3(0,-.10f-surface.localPosition.y*menuScale,0);
                if(cockpitAnchor!=null)transform.SetPositionAndRotation(cockpitAnchor.TransformPoint(position),cockpitAnchor.rotation);
                else transform.localPosition=position;
                return;
            }
            transform.localScale=Vector3.one*CockpitScale;
            Vector3 offset=new Vector3(0,Mathf.Max(clearanceHeight,compact?.08f*CockpitScale:0),0);
            if(cockpitAnchor!=null)transform.SetPositionAndRotation(cockpitAnchor.TransformPoint(offset),cockpitAnchor.rotation);
            else transform.localPosition=offset;
        }
        public void Configure(ProductUiLabel h,ProductUiLabel b,ProductUiLabel f,Transform backing,ProductActionButton[] actions)
        { title=h;body=b;hint=f;surface=backing;buttons=actions; }
        public void Apply(bool compact)
        {
            // Scale the whole layout, including depth and hit targets, to retain its
            // angular size inside the free cabin space. Calibration must not move UI
            // through the panel; this anchor is fixed to the aircraft, not the head.
            ApplyCockpitPose(compact);
            title.Refresh();body.Refresh();hint.Refresh();
            int count=0; foreach(var button in buttons)if(button.gameObject.activeSelf)count++;
            if(lastTextScale==ProductComfortPreferences.TextScale&&lastCount==count&&lastTitle==title.Display.text&&lastBody==body.Display.text&&lastHint==hint.Display.text&&lastCompact==compact)return;
            lastTextScale=ProductComfortPreferences.TextScale;
            lastCount=count;lastTitle=title.Display.text;lastBody=body.Display.text;lastHint=hint.Display.text;lastCompact=compact;
            // One coordinate system for visual geometry and ray hit targets. No head following.
            const float left=-.365f,z=1.03f,width=.73f,row=.052f;
            float top=compact?.22f:.31f;
            title.transform.localPosition=new Vector3(left,top,z);
            body.transform.localPosition=new Vector3(left,top-.065f,z);
            float bodyHeight=string.IsNullOrEmpty(lastBody)?0:body.Display.GetPreferredValues(lastBody,width,10).y;
            if(supplement!=null)supplement.localPosition=new Vector3(0,top-.065f-bodyHeight-.185f,z-.006f);
            float rowY=top-.065f-bodyHeight-supplementHeight-.044f;
            foreach(var button in buttons)
                if(button.gameObject.activeSelf) { button.transform.localPosition=new Vector3(0,rowY,z); rowY-=row; }
            float footerY=rowY-.016f;
            hint.transform.localPosition=new Vector3(left,footerY,z);
            float bottom=footerY-.057f;
            // Keep even the longest menu above the rearward-projecting glare
            // shield. Instrument-face depth alone is not a sufficient clearance.
            clearanceHeight=Mathf.Max(0,.025f-bottom*CockpitScale);
            surface.localPosition=new Vector3(0,(top+.035f+bottom)*.5f,z+.015f);
            surface.localScale=new Vector3(.81f,top+.035f-bottom,1);
            ApplyCockpitPose(compact);
        }
    }
}
