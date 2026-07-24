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

            float4 _CockpitLocalShadowDepthParams;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float linearDepth : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                float4 viewPosition = mul(UNITY_MATRIX_MV, input.vertex);
                output.position = UnityObjectToClipPos(input.vertex);
                output.linearDepth = saturate(
                    (-viewPosition.z - _CockpitLocalShadowDepthParams.x) *
                    _CockpitLocalShadowDepthParams.z);
                return output;
            }

            half4 frag(v2f input) : SV_Target
            {
                half depth = saturate(input.linearDepth);
                return half4(depth, depth * depth, 0.0h, 1.0h);
            }
            ENDCG
        }
    }
    Fallback Off
}
