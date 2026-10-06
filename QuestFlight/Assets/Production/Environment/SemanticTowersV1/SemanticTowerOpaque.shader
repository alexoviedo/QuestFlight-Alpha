Shader "QuestFlight/Semantic Tower Opaque"
{
    Properties
    {
        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1

        _Tint ("Shared tower tint", Color) = (1,1,1,1)
        [HideInInspector] _SemanticTowerDiagnosticNight ("Editor night diagnostic", Float) = 0
        [HideInInspector] _SemanticTowerDiagnosticTime ("Editor time override", Float) = -1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 60
        Cull Back
        ZWrite On
        Blend Off
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _Tint;
            half _SemanticTowerDiagnosticNight;
            half _SemanticTowerDiagnosticTime;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 lightData : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                half3 normal : TEXCOORD0;
                fixed4 color : COLOR;
                float2 lightData : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.color = input.color * _Tint;
                output.lightData = input.lightData;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normal = normalize(input.normal);
                half diffuse = saturate(dot(normal, normalize(_WorldSpaceLightPos0.xyz)));
                fixed3 ambient = ShadeSH9(half4(normal, 1.0));
                fixed3 baseColor = input.color.rgb *
                    (ambient + _LightColor0.rgb * (0.30 + diffuse * 0.70));

                half timeValue = _SemanticTowerDiagnosticTime >= 0.0h
                    ? _SemanticTowerDiagnosticTime : _Time.y;
                half flashing = step(0.76h, frac(timeValue * 0.5h + input.lightData.y));
                half steady = step(1.5h, input.lightData.x);
                half emitter = step(0.5h, input.color.a) * _SemanticTowerDiagnosticNight;
                half redOn = emitter * max(steady, flashing);
                fixed4 output = fixed4(baseColor + fixed3(3.5h, 0.025h, 0.015h) * redOn, 1.0h);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
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
#define QF_STATIC_WORLD_GEOMETRY 1

            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
#include "Assets/Shaders/ProductSpaceWarpMotion.cginc"
            #include "Lighting.cginc"

            fixed4 _Tint;
            half _SemanticTowerDiagnosticNight;
            half _SemanticTowerDiagnosticTime;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 lightData : TEXCOORD0;
                
float3 qfPreviousVertex : TEXCOORD4;
UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                half3 normal : TEXCOORD0;
                fixed4 color : COLOR;
                float2 lightData : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                
float4 qfCurrent : TEXCOORD14; float4 qfPrevious : TEXCOORD15;
UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f QFColorVertex(appdata input, out float4 qfWorld)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = QFObjectToMotionClip(input.vertex, qfWorld);
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.color = input.color * _Tint;
                output.lightData = input.lightData;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 QFColorFragment(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normal = normalize(input.normal);
                half diffuse = saturate(dot(normal, normalize(_WorldSpaceLightPos0.xyz)));
                fixed3 ambient = ShadeSH9(half4(normal, 1.0));
                fixed3 baseColor = input.color.rgb *
                    (ambient + _LightColor0.rgb * (0.30 + diffuse * 0.70));

                half timeValue = _SemanticTowerDiagnosticTime >= 0.0h
                    ? _SemanticTowerDiagnosticTime : _Time.y;
                half flashing = step(0.76h, frac(timeValue * 0.5h + input.lightData.y));
                half steady = step(1.5h, input.lightData.x);
                half emitter = step(0.5h, input.color.a) * _SemanticTowerDiagnosticNight;
                half redOn = emitter * max(steady, flashing);
                fixed4 output = fixed4(baseColor + fixed3(3.5h, 0.025h, 0.015h) * redOn, 1.0h);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            
#pragma multi_compile_instancing

v2f QFPreviousVertex(appdata input, out float4 qfWorld)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = QFObjectToMotionClip(input.vertex, qfWorld);
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.color = input.color * _Tint;
                output.lightData = input.lightData;
                UNITY_TRANSFER_FOG(output, output.position);
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
    Fallback "Diffuse"
}
