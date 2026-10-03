Shader "QuestFlight/UI/CaptionSurface"
{
    Properties { _Color("Color",Color)=(.098,.149,.188,1) _Accent("Accent",Color)=(.47,.78,.91,1) _Focus("Focus",Float)=0 _ZTest("Depth test",Float)=4 _ZWrite("Depth write",Float)=1 }
    SubShader
    {
        Tags { "Queue"="Transparent+500" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest [_ZTest]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            fixed4 _Color,_Accent; float _Focus;
            v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o; }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 q=abs(i.uv-.5)-.475;
                float edge=length(max(q,0))+min(max(q.x,q.y),0)-.025;
                clip(-edge);
                fixed3 color=_Color.rgb*(.98+.04*i.uv.y);
                float stripe=1-step(.006,i.uv.x);
                return fixed4(lerp(color,_Accent.rgb,stripe*_Focus),_Color.a);
            }
            ENDCG
        }
    }
}
