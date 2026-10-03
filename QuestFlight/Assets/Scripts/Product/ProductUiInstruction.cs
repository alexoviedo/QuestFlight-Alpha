using TMPro;
using UnityEngine;

namespace QuestFlight.Product
{
    public sealed class ProductUiInstruction : MonoBehaviour
    {
        [SerializeField] private TextMeshPro status,title,instruction,hint;
        [SerializeField] private Transform surface,progress;
        [SerializeField] private ProductUiTheme theme;
        [SerializeField] private Transform cockpitAnchor;
        [SerializeField] private TextAsset captionTimings;
        private ProductCaptionTimeline timeline;
        public void ConfigureCaptionTimings(TextAsset value){captionTimings=value;timeline=null;}
        public const float CockpitScale=.46f;
        public void ConfigureCockpitClearance(Transform anchor)
        { cockpitAnchor=anchor;transform.localScale=Vector3.one*CockpitScale;ApplyCockpitPose(); }
        private void ApplyCockpitPose()
        {
            if(cockpitAnchor!=null)transform.SetPositionAndRotation(
                cockpitAnchor.TransformPoint(new Vector3(.29f,.23f,.91f)*CockpitScale),cockpitAnchor.rotation);
        }
        private float baseStatus,baseTitle,baseCue,baseHint,lastScale;
        private string lastInstruction,lastTitle;
        private bool lastCaptions;
        private string lastOwnership;
        private int lastIndex=-1,lastCount=-1;
        public void Configure(TextMeshPro s,TextMeshPro t,TextMeshPro i,TextMeshPro h,Transform backing,Transform bar,ProductUiTheme style)
        {status=s;title=t;instruction=i;hint=h;surface=backing;progress=bar;theme=style;}
        public void Present(string ownership,string step,string cue,int index,int count,bool captions,bool urgent,AudioSource spokenAudio=null)
        {
            if(spokenAudio!=null&&!urgent)
            {if(timeline==null)timeline=new ProductCaptionTimeline(captionTimings);cue=timeline.At(spokenAudio,cue);}
            string controlsHint=ownership.StartsWith("Demo")?"Menu: take control / help · X: repeat":step.StartsWith("Boulder ")&&ownership=="Your controls"?"Menu: pause / let demo fly · X: repeat":"Menu: pause / help · X: repeat";
            if(hint.text!=controlsHint)hint.text=controlsHint;
            ApplyCockpitPose();
            float scale=ProductComfortPreferences.CaptionScale;
            if(baseStatus==0){baseStatus=status.fontSize;baseTitle=title.fontSize;baseCue=instruction.fontSize;baseHint=hint.fontSize;}
            if(lastScale!=scale){lastScale=scale;status.fontSize=baseStatus*scale;title.fontSize=baseTitle*scale;instruction.fontSize=baseCue*scale;hint.fontSize=baseHint*scale;lastInstruction=null;
                title.transform.localPosition=new Vector3(-.24f,-.022f*scale,-.003f);instruction.transform.localPosition=new Vector3(-.24f,-.077f*scale,-.003f);}
            if(lastOwnership!=ownership||lastIndex!=index||lastCount!=count)
            {
                lastOwnership=ownership;lastIndex=index;lastCount=count;
                status.text=ownership+"   /   "+(index+1).ToString("00")+" / "+count.ToString("00");
            }
            if(title.text!=step)title.text=step;
            if(instruction.text!=cue)instruction.text=cue;
            Color color=urgent?theme.urgent:ProductComfortPreferences.HighContrast?Color.white:theme.text;
            if(instruction.color!=color)instruction.color=color;
            if(instruction.gameObject.activeSelf!=captions)instruction.gameObject.SetActive(captions);
            if(lastInstruction!=cue||lastCaptions!=captions||lastTitle!=step)
            {
                lastInstruction=cue;lastCaptions=captions;lastTitle=step;
                float height=captions?instruction.GetPreferredValues(cue,.48f,5).y:0;
                float cueY=title.transform.localPosition.y-title.GetPreferredValues(step,.48f,5).y-.015f*scale;
                instruction.transform.localPosition=new Vector3(-.24f,cueY,-.003f);
                float hintY=cueY-height-.025f*scale;
                float bottom=hintY-hint.GetPreferredValues(controlsHint,.48f,5).y-.025f;
                hint.transform.localPosition=new Vector3(-.24f,hintY,-.003f);
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
