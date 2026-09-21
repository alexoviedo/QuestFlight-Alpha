using TMPro;
using UnityEngine;

namespace QuestFlight.Product
{
    public sealed class ProductUiInstruction : MonoBehaviour
    {
        [SerializeField] private TextMeshPro status,title,instruction,hint;
        [SerializeField] private Transform surface,progress;
        [SerializeField] private ProductUiTheme theme;
        private string lastInstruction;
        private bool lastCaptions;
        private string lastOwnership;
        private int lastIndex=-1,lastCount=-1;
        public void Configure(TextMeshPro s,TextMeshPro t,TextMeshPro i,TextMeshPro h,Transform backing,Transform bar,ProductUiTheme style)
        {status=s;title=t;instruction=i;hint=h;surface=backing;progress=bar;theme=style;}
        public void Present(string ownership,string step,string cue,int index,int count,bool captions,bool urgent)
        {
            if(lastOwnership!=ownership||lastIndex!=index||lastCount!=count)
            {
                lastOwnership=ownership;lastIndex=index;lastCount=count;
                status.text=ownership+"   /   "+(index+1).ToString("00")+" / "+count.ToString("00");
            }
            if(title.text!=step)title.text=step;
            if(instruction.text!=cue)instruction.text=cue;
            Color color=urgent?theme.urgent:theme.text;
            if(instruction.color!=color)instruction.color=color;
            if(instruction.gameObject.activeSelf!=captions)instruction.gameObject.SetActive(captions);
            if(lastInstruction!=cue||lastCaptions!=captions)
            {
                lastInstruction=cue;lastCaptions=captions;
                float height=captions?instruction.GetPreferredValues(cue,.48f,5).y:0;
                float bottom=-.079f-height-.054f;
                hint.transform.localPosition=new Vector3(-.24f,bottom+.035f,-.003f);
                surface.localPosition=new Vector3(0,(.04f+bottom)*.5f,.012f);
                surface.localScale=new Vector3(.54f,.04f-bottom,1);
                progress.localPosition=new Vector3(-.24f,bottom+.009f,-.004f);
            }
            float amount=Mathf.Clamp01((index+1f)/Mathf.Max(1,count));
            progress.localScale=new Vector3(.48f*amount,.002f,1);
            var p=progress.localPosition;p.x=-.24f+.24f*amount;progress.localPosition=p;
        }
    }
}
