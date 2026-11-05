using TMPro;

using UnityEngine;

namespace QuestFlight.Product

{

    /// <summary>Updates explicitly authored, paused-menu illustrations only.</summary>

    public sealed class ProductControlVisuals : MonoBehaviour

    {

        [SerializeField] private GameObject diagram,graph;

        [SerializeField] private LineRenderer curve;

        [SerializeField] private Transform marker;

        [SerializeField] private TextMeshPro assignments;

        private float next;

        public void SetAircraft(bool glider){sgs=glider;}

        private bool sgs;

        public void Configure(GameObject d,GameObject g,LineRenderer c,Transform m,TextMeshPro text)

        {diagram=d;graph=g;curve=c;marker=m;assignments=text;}

        public void Present(bool show,bool showDiagram)

        {gameObject.SetActive(show);if(!show)return;diagram.SetActive(showDiagram);graph.SetActive(!showDiagram);next=0;Update();}

        private void Update()

        {

            if(Time.unscaledTime<next)return;next=Time.unscaledTime+.1f;

            if(diagram.activeSelf)

            {assignments.text=sgs?$"{ProductComfortPreferences.RudderHand} stick: rudder - {ProductComfortPreferences.TrimHand} stick: airbrake/brake\nGrip: stick or lever - Trim: separate lever\nA in flight: release tow - A in menus: select":$"LEFT: {(ProductComfortPreferences.LeftStickSteering?"steering":"trim")}     RIGHT: {(ProductComfortPreferences.LeftStickSteering?"trim":"steering")}\nIndex triggers: {(ProductComfortPreferences.CombinedBrakes?"both brakes":"left / right brake")} · Grip: grab\nA: select · B / Y: back · Menu: pause";return;}

            for(int i=0;i<33;i++){float x=i/16f-1;curve.SetPosition(i,new Vector3(x*.28f,ProductComfortPreferences.Axis(x,ProductComfortPreferences.RudderDeadzone,ProductComfortPreferences.RudderCurve)*.085f,0));}

            float raw=ProductComfortPreferences.RawRudder;

            marker.localPosition=new Vector3(raw*.28f,ProductComfortPreferences.Axis(raw,ProductComfortPreferences.RudderDeadzone,ProductComfortPreferences.RudderCurve)*.085f,-.001f);

        }

    }

}
