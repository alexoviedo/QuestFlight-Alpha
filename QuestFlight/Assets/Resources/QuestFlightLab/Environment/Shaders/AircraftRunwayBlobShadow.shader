Shader "QuestFlightLab/Aircraft Runway Blob Shadow"
{
    Properties
    {
        _Color ("Shadow Color", Color) = (0.055,0.065,0.075,1)
        _Opacity ("Bounded Opacity", Range(0,0.65)) = 0.42
        _CoreRadius ("Core Radius", Range(0.05,0.95)) = 0.32
        _EdgeSoftness ("Edge Softness", Range(0.05,0.95)) = 0.68
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-12" "IgnoreProjector"="True" }
        LOD 35
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Offset -2, -2

        Pass
        {
            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            half _Opacity;
            half _CoreRadius;
            half _EdgeSoftness;

            struct appdata
            {
                float4 vertex : POSITION;
                half2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                half2 centeredUv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.centeredUv = input.uv * 2.0h - 1.0h;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half radius = length(input.centeredUv);
                half edgeStart = saturate(_CoreRadius);
                half edgeEnd = saturate(edgeStart + _EdgeSoftness);
                half alpha = (1.0h - smoothstep(edgeStart, edgeEnd, radius)) * _Opacity;
                fixed4 output = fixed4(_Color.rgb, alpha);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback Off
}
