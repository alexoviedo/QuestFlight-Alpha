Shader "QuestFlight/Production/Propeller Blur"
{
    Properties
    {
        _BlurColor ("Exposure-integrated blade color", Color) = (0.055,0.06,0.065,1)
        _BlurStrength ("Blur strength", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-20" }
        LOD 40
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _BlurColor;
            half _BlurStrength;

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
                half2 bladeAxis : TEXCOORD1;
                half2 trailingAxis : TEXCOORD2;
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

                // True 700-2700 RPM phase aliases at 72 Hz. Animate only an
                // exposure-integrated perceptual cue at a capped, stereo-shared
                // rate so rotation reads correctly without reverse-strobing.
                float perceptualRate = lerp(7.0, 12.0, _BlurStrength);
                float phase = -_Time.y * perceptualRate;
                float sinePhase;
                float cosinePhase;
                sincos(phase, sinePhase, cosinePhase);
                output.bladeAxis = half2(cosinePhase, sinePhase);
                sincos(phase - 0.34, sinePhase, cosinePhase);
                output.trailingAxis = half2(cosinePhase, sinePhase);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 centered = (input.uv - 0.5) * 2.0;
                float radius = length(centered);
                clip(0.995 - radius);
                clip(radius - 0.13);

                // A camera exposure integrates hundreds of blade positions into
                // a faint disc. Radial falloff and a restrained tip band retain
                // a propeller read without a frozen spoke or reverse-strobe.
                half hubFade = smoothstep(0.13, 0.24, radius);
                half edgeFade = 1.0h - smoothstep(0.82, 0.995, radius);
                half tipBand = smoothstep(0.68, 0.88, radius) *
                               (1.0h - smoothstep(0.88, 0.995, radius));
                float2 radialDirection = centered /
                    max(radius, 0.0001);
                half bladeAlignment = abs(dot(
                    radialDirection,
                    normalize(input.bladeAxis)));
                half trailingAlignment = abs(dot(
                    radialDirection,
                    normalize(input.trailingAxis)));
                half bladeExposure = smoothstep(
                    0.70h, 0.97h, bladeAlignment);
                half trailingExposure = smoothstep(
                    0.64h, 0.96h, trailingAlignment) * 0.48h;
                half rotatingExposure = max(
                    bladeExposure,
                    trailingExposure);
                // This range is intentionally above the practical visibility
                // floor after the production cockpit-glass pass. It remains
                // translucent, but is no longer erased by the bright exterior.
                half alpha = (0.13h +
                              tipBand * 0.08h +
                              rotatingExposure * 0.12h) *
                             hubFade * edgeFade *
                             saturate(_BlurStrength);
                return fixed4(_BlurColor.rgb, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
