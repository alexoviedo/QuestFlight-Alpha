Shader "QuestFlight/Training/ObjectiveRibbon"
{
    Properties
    {
        _Color ("Cyan edge", Color) = (0.08, 0.72, 0.95, 1)
        _CoreColor ("Readable core", Color) = (0.72, 0.96, 1, 1)
        [Toggle] _Surface ("Translucent volume", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            fixed4 _Color, _CoreColor;
            half _Surface;
            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_Surface > 0.5)
                {
                    half height = saturate(input.uv.y);
                    half fade = (1-height)*(1-height);
                    half sweep = frac(_Time.y * 0.22);
                    half band = 1-smoothstep(0.015,0.11,abs(height-sweep));
                    // Open-topped volume: subtle at the base, disappearing above it.
                    // A slow, low-contrast sweep adds life without flashing or hiding pavement.
                    half alpha = (0.11*fade + 0.085*band*(1-height)) * input.color.a;
                    return fixed4(_Color.rgb,alpha);
                }
                // Soft cyan shoulders and a bright narrow core, all in one small ribbon.
                // No bloom, texture fetch, light, particle or screen-sized pass.
                half edge = abs(input.uv.y * 2 - 1);
                half core = 1 - smoothstep(0.18, 0.46, edge);
                half feather = 1 - smoothstep(0.48, 1.0, edge);
                half alpha = (0.38 * feather + 0.62 * core) * input.color.a;
                return fixed4(lerp(_Color.rgb, _CoreColor.rgb, core) * input.color.rgb, alpha);
            }
            ENDCG
        }
    }
}
