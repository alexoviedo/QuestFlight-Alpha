using TMPro;
using UnityEngine;

namespace QuestFlight.Product
{
    public sealed class ProductUiTheme : ScriptableObject
    {
        public TMP_FontAsset regular, semibold;
        public Color canvas = new Color32(16,24,32,255);
        public Color panel = new Color32(25,38,48,255);
        public Color selected = new Color32(38,59,73,255);
        public Color text = new Color32(242,244,239,255);
        public Color secondary = new Color32(187,200,207,255);
        public Color accent = new Color32(120,200,232,255);
        public Color caution = new Color32(230,182,106,255);
        public Color success = new Color32(159,210,181,255);
        public Color urgent = new Color32(241,139,131,255);
        public float focusSeconds = .15f;
    }
}
