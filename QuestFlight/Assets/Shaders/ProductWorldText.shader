Shader "QuestFlight/Interface/World Text"
{
    Properties { _MainTex("Font atlas",2D)="white" {} _UseTextureRGB("Use texture RGB",Float)=0 _Color("Tint",Color)=(1,1,1,1) [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex; float4 _Color; float _UseTextureRGB;
        struct appdata { float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct v2f { float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;float4 current:TEXCOORD1;float4 previous:TEXCOORD2;UNITY_VERTEX_OUTPUT_STEREO };
        v2f ColorVert(appdata v) { UNITY_SETUP_INSTANCE_ID(v);v2f o=(v2f)0;UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color*_Color;return o; }
        half4 ColorFrag(v2f i):SV_Target { half4 sample=tex2D(_MainTex,i.uv);return half4(i.color.rgb*lerp(1,sample.rgb,_UseTextureRGB),i.color.a*sample.a); }
        ENDCG
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma vertex ColorVert
            #pragma fragment ColorFrag
            #pragma multi_compile_instancing
            ENDCG
        }
        Pass
        {
            Name "XRMotionVectors" Tags { "LightMode"="XRMotionVectors" }
            Blend Off ZWrite On ColorMask RGBA
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex MotionVert
            #pragma fragment MotionFrag
            #pragma multi_compile_instancing
            #include "Assets/Shaders/ProductSpaceWarpMotion.cginc"
            v2f MotionVert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);v2f o=ColorVert(v);float4 world=mul(unity_ObjectToWorld,v.vertex);
                o.current=mul(_NonJitteredViewProjMatrixStereo[QFEye()],world);
                o.previous=mul(_PrevViewProjMatrixStereo[QFEye()],QFPreviousWorld(world));o.pos=o.current;return o;
            }
            float4 MotionFrag(v2f i):SV_Target
            { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);clip(ColorFrag(i).a-.5);return QFMotion(i.current,i.previous,1); }
            ENDCG
        }
    }
}
