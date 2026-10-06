Shader "Hidden/QuestFlight/Neighborhood Canopy Review"
{
    Properties
    {
        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1

        _MainTex ("Existing crown detail", 2D) = "white" {}
        _LeafAtlas ("Existing branch atlas", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off ZWrite On AlphaToMask On
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            sampler2D _MainTex, _LeafAtlas;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float4 color:COLOR; float2 uv:TEXCOORD0; float2 mode:TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 position:SV_POSITION; half3 normal:TEXCOORD0; float2 uv:TEXCOORD1; half4 color:COLOR; half mode:TEXCOORD2; UNITY_FOG_COORDS(3) UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position=UnityObjectToClipPos(v.vertex); o.normal=UnityObjectToWorldNormal(v.normal);
                o.color=half4(GammaToLinearSpace(v.color.rgb),v.color.a); o.uv=v.uv; o.mode=v.mode.x;
                UNITY_TRANSFER_FOG(o,o.position); return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half alpha=1; half3 albedo=i.color.rgb;
                UNITY_BRANCH if(i.mode<1.5h)
                {
                    half4 detail;
                    UNITY_BRANCH if(i.mode>.5h) {
                        detail=tex2D(_LeafAtlas,i.uv); alpha=detail.a;
                        alpha=saturate((alpha-.28h)/max(fwidth(alpha),.02h)+.5h);
                        clip(alpha-.01h);
                    }
                    else detail=tex2D(_MainTex,i.uv);
                    half luminance=dot(detail.rgb,half3(.2126h,.7152h,.0722h));
                    // Retain crown-scale texture contrast instead of flattening
                    // it into a narrow modulation of a solid canopy color.
                    half sourceLuminance=max(dot(albedo,half3(.2126h,.7152h,.0722h)),.025h);
                    albedo=detail.rgb*lerp(.78h,1.12h,saturate(sourceLuminance/.2h));
                }
                half3 n=normalize(i.normal);
                half direct=saturate(dot(n,normalize(_WorldSpaceLightPos0.xyz))*.8h+.2h);
                half3 ambient=max(ShadeSH9(half4(n,1)),half3(.24h,.24h,.24h));
                half3 color=albedo*(ambient+_LightColor0.rgb*direct*.68h)*i.color.a;
                UNITY_APPLY_FOG(i.fogCoord,color); return half4(color,alpha);
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
#define QF_STATIC_WORLD_GEOMETRY 1

            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
#include "Assets/Shaders/ProductSpaceWarpMotion.cginc"
            #include "Lighting.cginc"
            sampler2D _MainTex, _LeafAtlas;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float4 color:COLOR; float2 uv:TEXCOORD0; float2 mode:TEXCOORD1; 
float3 qfPreviousVertex : TEXCOORD4;
UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 position:SV_POSITION; half3 normal:TEXCOORD0; float2 uv:TEXCOORD1; half4 color:COLOR; half mode:TEXCOORD2; UNITY_FOG_COORDS(3) 
float4 qfCurrent : TEXCOORD14; float4 qfPrevious : TEXCOORD15;
UNITY_VERTEX_OUTPUT_STEREO };
            v2f QFColorVertex(appdata v, out float4 qfWorld)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position=QFObjectToMotionClip(v.vertex, qfWorld); o.normal=UnityObjectToWorldNormal(v.normal);
                o.color=half4(GammaToLinearSpace(v.color.rgb),v.color.a); o.uv=v.uv; o.mode=v.mode.x;
                UNITY_TRANSFER_FOG(o,o.position); return o;
            }
            fixed4 QFColorFragment(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half alpha=1; half3 albedo=i.color.rgb;
                UNITY_BRANCH if(i.mode<1.5h)
                {
                    half4 detail;
                    UNITY_BRANCH if(i.mode>.5h) {
                        detail=tex2D(_LeafAtlas,i.uv); alpha=detail.a;
                        alpha=saturate((alpha-.28h)/max(fwidth(alpha),.02h)+.5h);
                        clip(alpha-.01h);
                    }
                    else detail=tex2D(_MainTex,i.uv);
                    half luminance=dot(detail.rgb,half3(.2126h,.7152h,.0722h));
                    // Retain crown-scale texture contrast instead of flattening
                    // it into a narrow modulation of a solid canopy color.
                    half sourceLuminance=max(dot(albedo,half3(.2126h,.7152h,.0722h)),.025h);
                    albedo=detail.rgb*lerp(.78h,1.12h,saturate(sourceLuminance/.2h));
                }
                half3 n=normalize(i.normal);
                half direct=saturate(dot(n,normalize(_WorldSpaceLightPos0.xyz))*.8h+.2h);
                half3 ambient=max(ShadeSH9(half4(n,1)),half3(.24h,.24h,.24h));
                half3 color=albedo*(ambient+_LightColor0.rgb*direct*.68h)*i.color.a;
                UNITY_APPLY_FOG(i.fogCoord,color); return half4(color,alpha);
            }
            
v2f QFPreviousVertex(appdata v, out float4 qfWorld)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position=QFObjectToMotionClip(v.vertex, qfWorld); o.normal=UnityObjectToWorldNormal(v.normal);
                o.color=half4(GammaToLinearSpace(v.color.rgb),v.color.a); o.uv=v.uv; o.mode=v.mode.x;
                UNITY_TRANSFER_FOG(o,o.position); return o;
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
