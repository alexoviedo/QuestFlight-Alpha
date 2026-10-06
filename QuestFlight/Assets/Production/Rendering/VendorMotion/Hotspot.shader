Shader "QuestFlight/XR/Unlit/Hotspot"
{
	Properties
	{
        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1

		_MainTex("Texture", 2D) = "white" {}
		_Color("Color", COLOR) = (1,1,1,1)
		_Progress("Progress",Range(0,1)) = 0
		_Highlight("Highlight Strength",Range(0,1)) = 0
		_HighlightColor("Highlight Color", COLOR) = (1,1,1,1)
		_ShrinkLimit("Shrink Limit",float) = 0.227
		[Toggle(NORMALS_SHRINK)] _NormalsShrink("Shrink along normal", Float) = 0
		
        _OffsetFactor("Offset Factor", float) = 0
        _OffsetUnits("Offset Units", float) = 0
	}
		SubShader
		{
			Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" }
			LOD 100
			Blend SrcAlpha OneMinusSrcAlpha
			Pass
			{
				Offset[_OffsetFactor],[_OffsetUnits]
				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				#pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
				#pragma multi_compile_local __ NORMALS_SHRINK

				#include "UnityCG.cginc"

				struct VertexInput
				{
					float4 vertex : POSITION;
					float2 uv : TEXCOORD0;
#if NORMALS_SHRINK
					float3 normal : NORMAL;
#endif
					UNITY_VERTEX_INPUT_INSTANCE_ID
				};

				struct VertexOutput
				{
					float2 uv : TEXCOORD0;
					float4 vertex : SV_POSITION;
					UNITY_VERTEX_INPUT_INSTANCE_ID
					UNITY_VERTEX_OUTPUT_STEREO
				};

				sampler2D _MainTex;
				half4 _Color;
				half _Progress;
				half _Highlight;
				half4 _HighlightColor;
				half _ShrinkLimit;

				float _OffsetFactor;
				float _OffsetUnits;

				VertexOutput vert(VertexInput v)
				{
					VertexOutput o;
					UNITY_SETUP_INSTANCE_ID(v);
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
					UNITY_TRANSFER_INSTANCE_ID(v, o);

#if NORMALS_SHRINK
					float2 radius = v.normal.xz;
#else
					float2 radius = v.uv - float2(0.5, 0.5);
					radius = normalize(radius);
					_Progress = 1 - _Progress;
#endif
					half2 shrink = radius * _Progress * _ShrinkLimit;
					v.vertex.xz += shrink;
					v.uv -= shrink;
					o.vertex = UnityObjectToClipPos(v.vertex);
					o.uv = v.uv;
					return o;
				}

				half4 frag(VertexOutput i) : SV_Target
				{
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
					half4 col = tex2D(_MainTex, i.uv) * _Color;
					col.rgb = lerp(col.rgb, _HighlightColor.rgb, _Highlight * _HighlightColor.a);
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

#pragma target 4.5

				#pragma vertex vert
				#pragma fragment frag
				#pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
				#pragma multi_compile_local __ NORMALS_SHRINK

				#include "UnityCG.cginc"
#include "Assets/Shaders/ProductSpaceWarpMotion.cginc"

				struct VertexInput
				{
					float4 vertex : POSITION;
					float2 uv : TEXCOORD0;
#if NORMALS_SHRINK
					float3 normal : NORMAL;
#endif
					
float3 qfPreviousVertex : TEXCOORD4;
UNITY_VERTEX_INPUT_INSTANCE_ID
				};

				struct VertexOutput
				{
					float2 uv : TEXCOORD0;
					float4 vertex : SV_POSITION;
					UNITY_VERTEX_INPUT_INSTANCE_ID
					
float4 qfCurrent : TEXCOORD14; float4 qfPrevious : TEXCOORD15;
UNITY_VERTEX_OUTPUT_STEREO
				};

				sampler2D _MainTex;
				half4 _Color;
				half _Progress;
				half _Highlight;
				half4 _HighlightColor;
				half _ShrinkLimit;

				float _OffsetFactor;
				float _OffsetUnits;

				VertexOutput QFColorVertex(VertexInput v, out float4 qfWorld)
				{
					VertexOutput o;
					UNITY_SETUP_INSTANCE_ID(v);
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
					UNITY_TRANSFER_INSTANCE_ID(v, o);

#if NORMALS_SHRINK
					float2 radius = v.normal.xz;
#else
					float2 radius = v.uv - float2(0.5, 0.5);
					radius = normalize(radius);
					_Progress = 1 - _Progress;
#endif
					half2 shrink = radius * _Progress * _ShrinkLimit;
					v.vertex.xz += shrink;
					v.uv -= shrink;
					o.vertex = QFObjectToMotionClip(v.vertex, qfWorld);
					o.uv = v.uv;
					return o;
				}

				half4 QFColorFragment(VertexOutput i) : SV_Target
				{
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
					half4 col = tex2D(_MainTex, i.uv) * _Color;
					col.rgb = lerp(col.rgb, _HighlightColor.rgb, _Highlight * _HighlightColor.a);
					return col;
				}
				
#pragma multi_compile_instancing

VertexOutput QFPreviousVertex(VertexInput v, out float4 qfWorld)
				{
					VertexOutput o;
					UNITY_SETUP_INSTANCE_ID(v);
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
					UNITY_TRANSFER_INSTANCE_ID(v, o);

#if NORMALS_SHRINK
					float2 radius = v.normal.xz;
#else
					float2 radius = v.uv - float2(0.5, 0.5);
					radius = normalize(radius);
					_Progress = 1 - _Progress;
#endif
					half2 shrink = radius * _Progress * _ShrinkLimit;
					v.vertex.xz += shrink;
					v.uv -= shrink;
					o.vertex = QFObjectToMotionClip(v.vertex, qfWorld);
					o.uv = v.uv;
					return o;
				}
VertexOutput vert(VertexInput v)
{
    UNITY_SETUP_INSTANCE_ID(v);
    float4 world;
    VertexOutput o=QFColorVertex(v,world);
    #if !defined(QF_STATIC_WORLD_GEOMETRY)
    if (_QFHasPreviousModel < .5 && (_QFPreviousVertices > .5 || unity_MotionVectorsParams.x == 1))
        v.vertex.xyz=v.qfPreviousVertex;
    #endif
    float4 previousWorld;
    VertexOutput old=QFPreviousVertex(v,previousWorld);
    float4 oldWorld=QFPreviousWorld(previousWorld);
    if (_QFResetObjectMotion > .5) oldWorld=world;
    o.qfCurrent=o.vertex;
    o.qfPrevious=mul(_PrevViewProjMatrixStereo[QFEye()],oldWorld);
    
    o.vertex=o.qfCurrent;
    return o;
}
float4 frag(VertexOutput v) : SV_Target
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
