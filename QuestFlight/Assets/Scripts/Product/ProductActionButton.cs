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
        public void Configure(MonoBehaviour target, MonoBehaviour[] sources, TextMesh text, int index)
        { owner = target; pointables = sources; label = text; slot = index; }
        private void OnEnable() { foreach (var p in pointables) if (p != null) p.enabled=false; }
        private void OnDisable() { foreach (var p in pointables) if (p is IPointable v) v.WhenPointerEventRaised -= Pointer; }
        private void Pointer(PointerEvent evt)
        {
            if (!(owner is IProductPanelActions actions)) return;
            if (evt.Type == PointerEventType.Hover) actions.FocusAction(slot);
            if(visual!=null)visual.Present(focused,evt.Type==PointerEventType.Select);
            if (evt.Type == PointerEventType.Select) actions.InvokeAction(slot);
        }
        public void Present(string text, bool selected,bool available=true)
        {
            bool visible = !string.IsNullOrEmpty(text);
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (!visible) return;
            string value = available?text:"<color=#88959D>"+text+"</color>";
            if (label.text != value) label.text = value;
            focused=selected;
            label.color=new Color32(242,244,239,255);
            ProductUiLabel.Sync(label);
            if(visual!=null)visual.Present(selected,false);
        }
    }
}
