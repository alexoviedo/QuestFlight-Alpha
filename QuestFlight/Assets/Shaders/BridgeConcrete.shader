Shader "QuestFlight/Bridge Concrete"
{
 Properties {
        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1
 _Color("Concrete",Color)=(.46,.44,.40,1) }
 SubShader { Tags { "RenderType"="Opaque" } Pass {
 Tags { "LightMode"="UniversalForward" }
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #pragma multi_compile_fog
 #include "UnityCG.cginc"
 #include "Lighting.cginc"
 fixed4 _Color;
 struct Input { float4 vertex:POSITION; float3 normal:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
 struct Output { float4 pos:SV_POSITION; fixed3 light:TEXCOORD0; UNITY_FOG_COORDS(1) UNITY_VERTEX_OUTPUT_STEREO };
 Output vert(Input v) { Output o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(Output,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos=UnityObjectToClipPos(v.vertex); float3 n=UnityObjectToWorldNormal(v.normal); o.light=max(.2,ShadeSH9(float4(n,1)))+_LightColor0.rgb*saturate(dot(n,_WorldSpaceLightPos0.xyz)); UNITY_TRANSFER_FOG(o,o.pos); return o; }
 fixed4 frag(Output i):SV_Target {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); fixed4 c=fixed4(_Color.rgb*i.light,1); UNITY_APPLY_FOG(i.fogCoord,c); return c; }
 ENDCG
 } 
        Pass
        {
            Name "XRMotionVectors"
            Tags { "LightMode"="XRMotionVectors" }
            Cull Back Blend Off ZWrite On ZTest LEqual ColorMask RGBA
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            CGPROGRAM
#define QF_STATIC_WORLD_GEOMETRY 1

#pragma target 4.5

 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #pragma multi_compile_fog
 #include "UnityCG.cginc"
#include "Assets/Shaders/ProductSpaceWarpMotion.cginc"
 #include "Lighting.cginc"
 fixed4 _Color;
 struct Input { float4 vertex:POSITION; float3 normal:NORMAL; 
float3 qfPreviousVertex : TEXCOORD4;
UNITY_VERTEX_INPUT_INSTANCE_ID };
 struct Output { float4 pos:SV_POSITION; fixed3 light:TEXCOORD0; UNITY_FOG_COORDS(1) 
float4 qfCurrent : TEXCOORD14; float4 qfPrevious : TEXCOORD15;
UNITY_VERTEX_OUTPUT_STEREO };
 Output QFColorVertex(Input v, out float4 qfWorld) { Output o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(Output,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos=QFObjectToMotionClip(v.vertex, qfWorld); float3 n=UnityObjectToWorldNormal(v.normal); o.light=max(.2,ShadeSH9(float4(n,1)))+_LightColor0.rgb*saturate(dot(n,_WorldSpaceLightPos0.xyz)); UNITY_TRANSFER_FOG(o,o.pos); return o; }
 fixed4 QFColorFragment(Output i):SV_Target {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); fixed4 c=fixed4(_Color.rgb*i.light,1); UNITY_APPLY_FOG(i.fogCoord,c); return c; }
 
Output QFPreviousVertex(Input v, out float4 qfWorld) { Output o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(Output,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos=QFObjectToMotionClip(v.vertex, qfWorld); float3 n=UnityObjectToWorldNormal(v.normal); o.light=max(.2,ShadeSH9(float4(n,1)))+_LightColor0.rgb*saturate(dot(n,_WorldSpaceLightPos0.xyz)); UNITY_TRANSFER_FOG(o,o.pos); return o; }
Output vert(Input v)
{
    UNITY_SETUP_INSTANCE_ID(v);
    float4 world;
    Output o=QFColorVertex(v,world);
    #if !defined(QF_STATIC_WORLD_GEOMETRY)
    if (_QFHasPreviousModel < .5 && (_QFPreviousVertices > .5 || unity_MotionVectorsParams.x == 1))
        v.vertex.xyz=v.qfPreviousVertex;
    #endif
    float4 previousWorld;
    Output old=QFPreviousVertex(v,previousWorld);
    float4 oldWorld=QFPreviousWorld(previousWorld);
    if (_QFResetObjectMotion > .5) oldWorld=world;
    o.qfCurrent=o.pos;
    o.qfPrevious=mul(_PrevViewProjMatrixStereo[QFEye()],oldWorld);
    
    o.pos=o.qfCurrent;
    return o;
}
float4 frag(Output v) : SV_Target
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
