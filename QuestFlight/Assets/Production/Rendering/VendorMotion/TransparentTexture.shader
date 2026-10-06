
// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)
// Unlit shader. Simplest possible textured shader.
// - no lighting
// - no lightmap support
// - no per-material color
Shader "QuestFlight/XR/Unlit/TransparentTexture" {
Properties {
        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1

    _MainTex ("Base (RGB)", 2D) = "white" {}
    _Color ("Base Color", Color) = (1,1,1,1)
}
SubShader {
    Tags { "RenderType"="Opaque" "Queue"="Transparent" }
    LOD 100
    Blend SrcAlpha OneMinusSrcAlpha
    Pass {
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            struct appdata_t {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                fixed4 col = tex2D(_MainTex, i.texcoord);
                col *= _Color;
                UNITY_APPLY_FOG(i.fogCoord, col);
                // UNITY_OPAQUE_ALPHA(col.a);
                return col;
            }
        
#pragma multi_compile_instancing
ENDCG
    }

        Pass
        {
            Name "XRMotionVectors"
            Tags { "LightMode"="XRMotionVectors" }
            Cull Back Blend Off ZWrite On ZTest LEqual ColorMask RGBA
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"
#include "Assets/Shaders/ProductSpaceWarpMotion.cginc"
            struct appdata_t {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                
float3 qfPreviousVertex : TEXCOORD4;
UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                
float4 qfCurrent : TEXCOORD14; float4 qfPrevious : TEXCOORD15;
UNITY_VERTEX_OUTPUT_STEREO
            };
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            v2f QFColorVertex(appdata_t v, out float4 qfWorld)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = QFObjectToMotionClip(v.vertex, qfWorld);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }
            fixed4 QFColorFragment(v2f i) : SV_Target
            {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                fixed4 col = tex2D(_MainTex, i.texcoord);
                col *= _Color;
                UNITY_APPLY_FOG(i.fogCoord, col);
                // UNITY_OPAQUE_ALPHA(col.a);
                return col;
            }
        
#pragma multi_compile_instancing

v2f QFPreviousVertex(appdata_t v, out float4 qfWorld)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = QFObjectToMotionClip(v.vertex, qfWorld);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }
v2f vert(appdata_t v)
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
