using System.Collections;
using UnityEngine;

namespace QuestFlight.Product
{
    [DisallowMultipleComponent]
    public sealed class ProductUiSurface : MonoBehaviour
    {
        [SerializeField] private Renderer surface;
        [SerializeField] private ProductUiTheme theme;
        [SerializeField] private bool action;
        [SerializeField] private bool accentOnly;
        private MaterialPropertyBlock properties;
        private Color shown,target;
        private Coroutine transition;
        private int state=-1;
        private bool contrast;
        [SerializeField] private bool captionBackground;
        private bool solidCaption;
        public void ConfigureCaptionBackground(){captionBackground=true;state=-1;}
        private void LateUpdate(){if(captionBackground&&solidCaption!=ProductComfortPreferences.SolidCaptions)RefreshStyle();}
        public void Configure(Renderer value,ProductUiTheme style,bool isAction)
        { surface=value; theme=style; action=isAction; state=-1; Present(false,false); }
        public void SetAccentOnly() {accentOnly=true;state=-1;Present(false,false);}
        private void OnEnable() { state=-1; Present(false,false); }
        private void OnDisable() { if(transition!=null)StopCoroutine(transition); transition=null; state=-1; }
        public void Present(bool focused,bool pressed)
        {
            int next=pressed?2:focused?1:0;
            if(theme==null||surface==null)return;
            if(next==state&&contrast==ProductComfortPreferences.HighContrast)return;
            contrast=ProductComfortPreferences.HighContrast;
            solidCaption=ProductComfortPreferences.SolidCaptions;
            target=accentOnly?theme.accent:pressed?Color.Lerp(theme.selected,theme.accent,.30f):focused?theme.selected:action?theme.canvas:theme.panel;
            if(contrast&&!accentOnly)target=focused||pressed?new Color(.08f,.24f,.34f,1):new Color(.025f,.035f,.045f,1);
            if(captionBackground)target.a=solidCaption?1:.78f;
            bool initial=state<0; state=next;
            if(transition!=null)StopCoroutine(transition);
            if(!Application.isPlaying||!isActiveAndEnabled||initial) { shown=target; Apply(); return; }
            transition=StartCoroutine(Ease());
        }
        public void RefreshStyle() { bool focus=state==1,pressed=state==2;state=-1;Present(focus,pressed); }
        private IEnumerator Ease()
        {
            Color start=shown;
            for(float elapsed=0;elapsed<theme.focusSeconds;elapsed+=Time.unscaledDeltaTime)
            { shown=Color.Lerp(start,target,Mathf.SmoothStep(0,1,elapsed/theme.focusSeconds)); Apply(); yield return null; }
            shown=target; Apply(); transition=null;
        }
        private void Apply()
        {
            if(properties==null)properties=new MaterialPropertyBlock();
            properties.SetColor("_Color",shown);
            properties.SetColor("_Accent",theme.accent);
            properties.SetFloat("_Focus",action&&state>0?1:0);
            surface.SetPropertyBlock(properties);
        }
    }
}
