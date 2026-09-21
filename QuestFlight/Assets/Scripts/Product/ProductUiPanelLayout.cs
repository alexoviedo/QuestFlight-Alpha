using UnityEngine;

namespace QuestFlight.Product
{
    public sealed class ProductUiPanelLayout : MonoBehaviour
    {
        [SerializeField] private ProductUiLabel title,body,hint;
        [SerializeField] private Transform surface;
        [SerializeField] private ProductActionButton[] buttons;
        private string lastTitle,lastBody,lastHint;
        private int lastCount=-1;
        private bool lastCompact;
        public void Configure(ProductUiLabel h,ProductUiLabel b,ProductUiLabel f,Transform backing,ProductActionButton[] actions)
        { title=h;body=b;hint=f;surface=backing;buttons=actions; }
        public void Apply(bool compact)
        {
            title.Refresh();body.Refresh();hint.Refresh();
            int count=0; foreach(var button in buttons)if(button.gameObject.activeSelf)count++;
            if(lastCount==count&&lastTitle==title.Display.text&&lastBody==body.Display.text&&lastHint==hint.Display.text&&lastCompact==compact)return;
            lastCount=count;lastTitle=title.Display.text;lastBody=body.Display.text;lastHint=hint.Display.text;lastCompact=compact;
            // One coordinate system for visual geometry and ray hit targets. No head following.
            transform.localScale=Vector3.one;
            transform.localPosition=new Vector3(0,compact?.08f:0,0);
            const float left=-.365f,z=1.03f,width=.73f,row=.052f;
            float top=compact?.22f:.31f;
            title.transform.localPosition=new Vector3(left,top,z);
            body.transform.localPosition=new Vector3(left,top-.065f,z);
            float bodyHeight=string.IsNullOrEmpty(lastBody)?0:body.Display.GetPreferredValues(lastBody,width,10).y;
            float rowY=top-.065f-bodyHeight-.044f;
            foreach(var button in buttons)
                if(button.gameObject.activeSelf) { button.transform.localPosition=new Vector3(0,rowY,z); rowY-=row; }
            float footerY=rowY-.016f;
            hint.transform.localPosition=new Vector3(left,footerY,z);
            float bottom=footerY-.057f;
            surface.localPosition=new Vector3(0,(top+.035f+bottom)*.5f,z+.015f);
            surface.localScale=new Vector3(.81f,top+.035f-bottom,1);
        }
    }
}
