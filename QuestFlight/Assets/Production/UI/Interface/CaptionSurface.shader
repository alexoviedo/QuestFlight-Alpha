Shader "QuestFlight/UI/CaptionSurface"
{
    Properties {
        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1
 _Color("Color",Color)=(.098,.149,.188,1) _Accent("Accent",Color)=(.47,.78,.91,1) _Focus("Focus",Float)=0 _ZTest("Depth test",Float)=4 _ZWrite("Depth write",Float)=1 }
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
    
        Pass
        {
            Name "XRMotionVectors"
            Tags { "LightMode"="XRMotionVectors" }
            Cull Off Blend Off ZWrite On ZTest LEqual ColorMask RGBA
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            CGPROGRAM

#pragma target 4.5

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
#include "Assets/Shaders/ProductSpaceWarpMotion.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; 
float3 qfPreviousVertex : TEXCOORD4;
UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; 
float4 qfCurrent : TEXCOORD14; float4 qfPrevious : TEXCOORD15;
UNITY_VERTEX_OUTPUT_STEREO };
            fixed4 _Color,_Accent; float _Focus;
            v2f QFColorVertex(appdata v, out float4 qfWorld) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.vertex=QFObjectToMotionClip(v.vertex, qfWorld);o.uv=v.uv;return o; }
            fixed4 QFColorFragment(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 q=abs(i.uv-.5)-.475;
                float edge=length(max(q,0))+min(max(q.x,q.y),0)-.025;
                clip(-edge);
                fixed3 color=_Color.rgb*(.98+.04*i.uv.y);
                float stripe=1-step(.006,i.uv.x);
                return fixed4(lerp(color,_Accent.rgb,stripe*_Focus),_Color.a);
            }
            
v2f QFPreviousVertex(appdata v, out float4 qfWorld) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.vertex=QFObjectToMotionClip(v.vertex, qfWorld);o.uv=v.uv;return o; }
v2f vert(appdata v)
{
    UNITY_SETUP_INSTANCE_ID(v);
    float4 world;
    v2f o=QFColorVertex(v,world);
    #if !defined(QF_STATIC_WORLD_GEOMETRY)
    if (_QFHasPreviousModel < .5 && (_QFPreviousVertices > .5 || unity_MotionVectorsParams.x == 1))
        v.vertex.xyz=v.qfPreviousVertex;
    #endif
    float4 previousWorld;
    v2f old=QFPreviousVertex(v,previousWorld);
    float4 oldWorld=QFPreviousWorld(previousWorld);
    if (_QFResetObjectMotion > .5) oldWorld=world;
    o.qfCurrent=o.vertex;
    o.qfPrevious=mul(_PrevViewProjMatrixStereo[QFEye()],oldWorld);
    
    o.vertex=o.qfCurrent;
    return o;
}
float4 frag(v2f v) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
    float coverage=QFColorFragment(v).a;
    clip(coverage-0.5);
    return QFMotion(v.qfCurrent,v.qfPrevious,coverage);
}

            ENDCG
        }
}
}
