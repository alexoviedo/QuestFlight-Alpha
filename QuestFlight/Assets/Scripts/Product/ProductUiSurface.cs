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
        public void Configure(Renderer value,ProductUiTheme style,bool isAction)
        { surface=value; theme=style; action=isAction; state=-1; Present(false,false); }
        public void SetAccentOnly() {accentOnly=true;state=-1;Present(false,false);}
        private void OnEnable() { state=-1; Present(false,false); }
        private void OnDisable() { if(transition!=null)StopCoroutine(transition); transition=null; state=-1; }
        public void Present(bool focused,bool pressed)
        {
            int next=pressed?2:focused?1:0;
            if(next==state||theme==null||surface==null)return;
            target=accentOnly?theme.accent:pressed?Color.Lerp(theme.selected,theme.accent,.30f):focused?theme.selected:action?theme.canvas:theme.panel;
            bool initial=state<0; state=next;
            if(transition!=null)StopCoroutine(transition);
            if(!Application.isPlaying||!isActiveAndEnabled||initial) { shown=target; Apply(); return; }
            transition=StartCoroutine(Ease());
        }
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
