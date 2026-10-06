Shader "QuestFlight/XR/Unlit/TunnelingEffect"
{
    Properties
    {
        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1

		_ColorInner("Inner Color", COLOR) = (1,0,0,1)
		_ColorOuter("Outer Color", COLOR) = (0,0,0,1)
        _Direction("Direction", VECTOR) = (0,0,1,0)
		_MinRadius("Min Radius",Range(-1,1)) = 0
		_MaxRadius("Max Radius",Range(-1,1)) = 0
		_Alpha("Fade",Range(0,1)) = 1
    }

    SubShader
    {
        Tags
	    {
		    "Queue"="Transparent"
		    "IgnoreProjector"="True"
		    "RenderType"="Transparent"
	    }

        Cull Back
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha, OneMinusDstAlpha One
        
        LOD 100
    
        Pass
        {
            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPosition: TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _ColorInner;
            half4 _ColorOuter;
            float3 _Direction;
            half _MinRadius;
            half _MaxRadius;
            half _Alpha;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.worldPosition = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 viewDir = normalize(i.worldPosition - _WorldSpaceCameraPos);
                float fov = dot(_Direction, viewDir);
                float dirMask = saturate((fov - _MinRadius) /  (_MaxRadius - _MinRadius));

                half4 color = lerp(_ColorInner, _ColorOuter, fov * 0.5 + 0.5);
                dirMask = smoothstep(0,1,dirMask);
                return fixed4(color.xyz, color.w * dirMask * _Alpha);
            }
            
#pragma multi_compile_instancing
ENDCG
        }
    
        Pass
        {
            Name "XRMotionVectors"
            Tags { "LightMode"="XRMotionVectors" }
            Cull Back Blend Off ZWrite On ZTest Always ColorMask RGBA
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
                
float3 qfPreviousVertex : TEXCOORD4;
UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPosition: TEXCOORD0;
                
float4 qfCurrent : TEXCOORD14; float4 qfPrevious : TEXCOORD15;
UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _ColorInner;
            half4 _ColorOuter;
            float3 _Direction;
            half _MinRadius;
            half _MaxRadius;
            half _Alpha;

            v2f QFColorVertex(appdata v, out float4 qfWorld)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.worldPosition = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                o.vertex = QFObjectToMotionClip(v.vertex, qfWorld);
                return o;
            }

            fixed4 QFColorFragment(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 viewDir = normalize(i.worldPosition - _WorldSpaceCameraPos);
                float fov = dot(_Direction, viewDir);
                float dirMask = saturate((fov - _MinRadius) /  (_MaxRadius - _MinRadius));

                half4 color = lerp(_ColorInner, _ColorOuter, fov * 0.5 + 0.5);
                dirMask = smoothstep(0,1,dirMask);
                return fixed4(color.xyz, color.w * dirMask * _Alpha);
            }
            
#pragma multi_compile_instancing

v2f QFPreviousVertex(appdata v, out float4 qfWorld)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.worldPosition = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                o.vertex = QFObjectToMotionClip(v.vertex, qfWorld);
                return o;
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
