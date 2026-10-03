using TMPro;
using UnityEngine;

namespace QuestFlight.Product
{
    // Keep established serialized TextMesh data contracts; only the authored SDF child renders.
    // Synchronization is explicit on content changes, never a per-frame polling component.
    [DisallowMultipleComponent]
    public sealed class ProductUiLabel : MonoBehaviour
    {
        [SerializeField] private TextMesh source;
        [SerializeField] private TextMeshPro display;
        public TextMeshPro Display => display;
        public void Configure(TextMesh value,TextMeshPro view) { source=value; display=view; Refresh(); }
        private float authoredFontSize;
        private void OnEnable() => Refresh();
        public void Refresh()
        {
            if(source==null||display==null)return;
            if(Application.isPlaying) { if(authoredFontSize<=0)authoredFontSize=display.fontSize;display.fontSize=authoredFontSize*ProductComfortPreferences.TextScale; }
            if(display.text!=source.text)display.text=source.text;
            Color color=ProductComfortPreferences.HighContrast?Color.white:source.color;
            if(display.color!=color)display.color=color;
        }
        public static void Sync(TextMesh source)
        { if(source!=null && source.TryGetComponent<ProductUiLabel>(out var label))label.Refresh(); }
    }
}
