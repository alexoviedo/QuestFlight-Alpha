using System;

using Oculus.Interaction;

using UnityEngine;

namespace QuestFlight.Product

{

    public interface IProductPanelActions

    {

        void FocusAction(int slot);

        void InvokeAction(int slot);

    }

    /// <summary>Serialized pointable button shared by app navigation and lessons.</summary>

    public sealed class ProductActionButton : MonoBehaviour

    {

        [SerializeField] private MonoBehaviour owner;

        [SerializeField] private MonoBehaviour[] pointables = Array.Empty<MonoBehaviour>();

        [SerializeField] private TextMesh label;

        [SerializeField] private int slot;

        [SerializeField] private ProductUiSurface visual;

        public void ConfigureVisual(ProductUiSurface value) => visual=value;

        private bool focused;

        private bool available;

        public int Slot => slot;
        public int ContentVersion {get;private set;}

        public bool Available => available;

        public void Configure(MonoBehaviour target, MonoBehaviour[] sources, TextMesh text, int index)

        { owner = target; pointables = sources; label = text; slot = index; }

        private void OnEnable() { foreach (var p in pointables) if (p != null) { p.enabled=true; if(p is IPointable v) { v.WhenPointerEventRaised-=Pointer; v.WhenPointerEventRaised+=Pointer; } } }

        private void OnDisable() { foreach (var p in pointables) if (p is IPointable v) v.WhenPointerEventRaised -= Pointer; }

        private void Pointer(PointerEvent evt)

        {

            if (!(owner is IProductPanelActions actions)) return;

            if (evt.Type == PointerEventType.Hover) actions.FocusAction(slot);

            if(visual!=null)visual.Present(focused,evt.Type==PointerEventType.Select);

            if (evt.Type == PointerEventType.Select) { if(owner is ProductAppNavigation navigation)navigation.InvokePointerAction(slot);else actions.InvokeAction(slot); }

        }

        public float ApplyRowLayout(float width)
        {
            ProductUiLabel.Sync(label);
            var text=label.GetComponent<ProductUiLabel>();
            float textHeight=text!=null?text.Display.GetPreferredValues(label.text,width,10).y:.032f;
            float height=Mathf.Max(.052f,textHeight+.020f);
            float center=.0165f-textHeight*.5f;
            var box=GetComponent<BoxCollider>();if(box!=null){box.size=new Vector3(width,height-.006f,.012f);box.center=new Vector3(0,center,0);}
            var surface=transform.Find("ActionSurface");if(surface!=null){var scale=surface.localScale;scale.y=height-.006f;surface.localScale=scale;var pos=surface.localPosition;pos.y=center;surface.localPosition=pos;}
            return height;
        }
        public void Present(string text, bool selected,bool available=true)

        {

            this.available=available;

            bool visible = !string.IsNullOrEmpty(text);

            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);

            if (!visible) return;

            string value = available?text:"<color=#88959D>"+text+"</color>";

            if (label.text != value) {label.text = value;ContentVersion++;}

            focused=selected;

            label.color=new Color32(242,244,239,255);

            ProductUiLabel.Sync(label);

            if(visual!=null)visual.Present(selected,false);

        }

    }

}
