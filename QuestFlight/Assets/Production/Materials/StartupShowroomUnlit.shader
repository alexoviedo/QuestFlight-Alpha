Shader "QuestFlight/StartupShowroomUnlit"
{
    Properties
    {
        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1

        _MainTex ("Backdrop", 2D) = "white" {}
        _UseMainTex ("Use Backdrop", Range(0, 1)) = 0
        _Color ("Base Color", Color) = (0.02, 0.035, 0.06, 1)
        _TopColor ("Top Color", Color) = (0.05, 0.12, 0.19, 1)
        _GradientStrength ("Gradient Strength", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Cull [_Cull]
        ZTest [_ZTest]
        ZWrite [_ZWrite]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            
UNITY_VERTEX_INPUT_INSTANCE_ID
};

            struct v2f
            {
                float4 position : SV_POSITION;
                float localY : TEXCOORD0;
                float2 uv : TEXCOORD1;
            
UNITY_VERTEX_OUTPUT_STEREO
};

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _UseMainTex;
            fixed4 _Color;
            fixed4 _TopColor;
            float _GradientStrength;

            v2f vert(appdata input)
            {
UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.localY = saturate(input.vertex.y + 0.5);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                fixed4 color = lerp(_Color, _TopColor,
                    input.localY * _GradientStrength);
                color *= lerp(fixed4(1, 1, 1, 1), tex2D(_MainTex, input.uv),
                    _UseMainTex);
                color.a = 1;
                return color;
            }
            
#pragma multi_compile_instancing
ENDCG
        }
    
        Pass
        {
            Name "XRMotionVectors"
            Tags { "LightMode"="XRMotionVectors" }
            Cull [_Cull] Blend Off ZWrite On ZTest LEqual ColorMask RGBA
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            CGPROGRAM

#pragma target 4.5

            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
#include "Assets/Shaders/ProductSpaceWarpMotion.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            

float3 qfPreviousVertex : TEXCOORD4;
UNITY_VERTEX_INPUT_INSTANCE_ID
};

            struct v2f
            {
                float4 position : SV_POSITION;
                float localY : TEXCOORD0;
                float2 uv : TEXCOORD1;
            

float4 qfCurrent : TEXCOORD14; float4 qfPrevious : TEXCOORD15;
UNITY_VERTEX_OUTPUT_STEREO
};

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _UseMainTex;
            fixed4 _Color;
            fixed4 _TopColor;
            float _GradientStrength;

            v2f QFColorVertex(appdata input, out float4 qfWorld)
            {
UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = QFObjectToMotionClip(input.vertex, qfWorld);
                output.localY = saturate(input.vertex.y + 0.5);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            fixed4 QFColorFragment(v2f input) : SV_Target
            {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                fixed4 color = lerp(_Color, _TopColor,
                    input.localY * _GradientStrength);
                color *= lerp(fixed4(1, 1, 1, 1), tex2D(_MainTex, input.uv),
                    _UseMainTex);
                color.a = 1;
                return color;
            }
            
#pragma multi_compile_instancing

v2f QFPreviousVertex(appdata input, out float4 qfWorld)
            {
UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = QFObjectToMotionClip(input.vertex, qfWorld);
                output.localY = saturate(input.vertex.y + 0.5);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }
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
    o.qfCurrent=o.position;
    o.qfPrevious=mul(_PrevViewProjMatrixStereo[QFEye()],oldWorld);
    
    o.position=o.qfCurrent;
    return o;
}
float4 frag(v2f v) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
    float coverage=QFColorFragment(v).a;
    clip(coverage-0.001);
    return QFMotion(v.qfCurrent,v.qfPrevious,coverage);
}

            ENDCG
        }
}
}
