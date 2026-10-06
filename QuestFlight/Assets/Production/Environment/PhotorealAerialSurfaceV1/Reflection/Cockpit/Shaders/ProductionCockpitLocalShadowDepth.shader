Shader "QuestFlight/Photoreal Aerial Surface V1/Cockpit Local Shadow Depth"
{
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        LOD 40

        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask RG

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _CockpitLocalShadowDepthParams; float4x4 _QFShadowView, _QFShadowVP;

            struct appdata
            {
                float4 vertex : POSITION;
            
UNITY_VERTEX_INPUT_INSTANCE_ID
};

            struct v2f
            {
                float4 position : SV_POSITION;
                float linearDepth : TEXCOORD0;
            
UNITY_VERTEX_OUTPUT_STEREO
};

            v2f vert(appdata input)
            {
UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float4 viewPosition = mul(_QFShadowView, mul(unity_ObjectToWorld,input.vertex));
                output.position = mul(_QFShadowVP,mul(unity_ObjectToWorld,input.vertex));
                output.linearDepth = saturate(
                    (-viewPosition.z - _CockpitLocalShadowDepthParams.x) *
                    _CockpitLocalShadowDepthParams.z);
                return output;
            }

            half4 frag(v2f input) : SV_Target
            {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half depth = saturate(input.linearDepth);
                return half4(depth, depth * depth, 0.0h, 1.0h);
            }
            
#pragma multi_compile_instancing
ENDCG
        }
    }
    Fallback Off
}
