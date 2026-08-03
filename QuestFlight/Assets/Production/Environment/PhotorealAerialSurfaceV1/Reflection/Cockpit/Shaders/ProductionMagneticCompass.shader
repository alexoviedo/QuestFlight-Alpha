Shader "QuestFlight/Cockpit/Isolated Magnetic Compass"
{
    Properties
    {
        [NoScaleOffset] _TapeTex ("Compass Tape", 2D) = "black" {}
        _HeadingDegrees ("Heading", Float) = 330
        _VisibleDegrees ("Visible Degrees", Float) = 72
    }
    SubShader
    {
        Tags { "Queue"="Geometry+4" "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _TapeTex;
            float _HeadingDegrees;
            float _VisibleDegrees;

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
                float heading = frac(
                    (_HeadingDegrees + (input.uv.x - 0.5) * _VisibleDegrees) /
                    360.0);
                fixed4 result = tex2D(_TapeTex, float2(heading, input.uv.y));
                float center = 1.0 - smoothstep(
                    0.004, 0.012, abs(input.uv.x - 0.5));
                float lubber = center * smoothstep(0.62, 0.72, input.uv.y);
                result.rgb = lerp(result.rgb, fixed3(0.95, 0.97, 0.90), lubber);
                result.a = 1.0;
                return result;
            }
            ENDCG
        }
    }
    Fallback Off
}
