Shader "QuestFlight/Photoreal Aerial Surface V1/Instrument Display Readout"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Authored Readout Atlas", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "Queue"="Geometry+4" "RenderType"="Opaque" }
        LOD 50

        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual
            Offset -2, -2

            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return tex2D(_MainTex, input.uv);
            }
            ENDCG
        }
    }
    Fallback Off
}
