Shader "QuestFlight/Cockpit/Instrument V2 Batched Opaque"
{
    Properties
    {
        [NoScaleOffset] _SourceTex ("Frozen Meter Atlas", 2D) = "white" {}
        [NoScaleOffset] _PresentationTex ("Project-authored Gauge Atlas", 2D) = "black" {}
        _AltitudeApertureUv ("Altitude Aperture UV", Vector) = (0.075, 0.205, 0.175, 0.285)
        _AltitudeDigits ("Altitude Digits", Vector) = (5, 2, 8, 0)
        _LayerDepthBias ("Instrument Layer Depth Bias", Float) = 0.00004
        _AttitudePivot ("Attitude Gyro Pivot", Vector) = (0, 0, 0, 0)
        _AttitudeRotation ("Attitude Gyro Rotation", Vector) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "Queue"="Geometry+3" "RenderType"="Opaque" }
        LOD 50

        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual
            // Preserve physical panel/lens ordering. The instrument-lens pass
            // owns glass response; this opaque batch must not receive a
            // forward depth bias.

            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _SourceTex;
            sampler2D _PresentationTex;
            float4 _AltitudeApertureUv;
            float4 _AltitudeDigits;
            float _LayerDepthBias;
            float4 _AttitudePivot;
            float4 _AttitudeRotation;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                // x selects the authored atlas, y owns the face/pointer depth
                // layer, z selects the frozen attitude gyro-ball topology,
                // and w is reserved.
                float4 sourceSelector : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float presentation : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 RotateByQuaternion(float3 value, float4 rotation)
            {
                return value + 2.0 * cross(
                    rotation.xyz,
                    cross(rotation.xyz, value) + rotation.w * value);
            }

            v2f vert(appdata input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 localPosition = input.vertex.xyz;
                if (input.sourceSelector.z > 0.5)
                {
                    localPosition = _AttitudePivot.xyz + RotateByQuaternion(
                        localPosition - _AttitudePivot.xyz,
                        _AttitudeRotation);
                }
                output.position = UnityObjectToClipPos(
                    float4(localPosition, 1.0));
                // Mobile depth precision cannot reliably distinguish the
                // source face, artwork, and pointer at micrometre geometry
                // offsets. TEXCOORD3.y carries authored face/pointer layers
                // 1/2. Map those to depth offsets -2/-1 so both remain behind
                // the measured glass and panel plane while retaining one full
                // stable depth step between face artwork and moving pointers.
                float authoredDepthLayer = input.sourceSelector.y > 0.5
                    ? input.sourceSelector.y - 3.0
                    : 0.0;
#if defined(UNITY_REVERSED_Z)
                output.position.z +=
                    authoredDepthLayer * _LayerDepthBias * output.position.w;
#else
                output.position.z -=
                    authoredDepthLayer * _LayerDepthBias * output.position.w;
#endif
                output.uv = input.uv;
                output.presentation = step(0.5, input.sourceSelector.x);
                return output;
            }

            float IsDigit(float value, float expected)
            {
                return 1.0 - step(0.25, abs(value - expected));
            }

            float BoxMask(float2 coords, float2 center, float2 halfSize)
            {
                float2 outside = abs(coords - center) - halfSize;
                return 1.0 - smoothstep(
                    0.0, 0.025, max(outside.x, outside.y));
            }

            float SevenSegmentDigit(float2 coords, float digit)
            {
                float is0 = IsDigit(digit, 0.0);
                float is1 = IsDigit(digit, 1.0);
                float is2 = IsDigit(digit, 2.0);
                float is3 = IsDigit(digit, 3.0);
                float is4 = IsDigit(digit, 4.0);
                float is5 = IsDigit(digit, 5.0);
                float is6 = IsDigit(digit, 6.0);
                float is7 = IsDigit(digit, 7.0);
                float is8 = IsDigit(digit, 8.0);
                float is9 = IsDigit(digit, 9.0);

                float a = saturate(is0 + is2 + is3 + is5 + is6 + is7 + is8 + is9);
                float b = saturate(is0 + is1 + is2 + is3 + is4 + is7 + is8 + is9);
                float c = saturate(is0 + is1 + is3 + is4 + is5 + is6 + is7 + is8 + is9);
                float d = saturate(is0 + is2 + is3 + is5 + is6 + is8 + is9);
                float e = saturate(is0 + is2 + is6 + is8);
                float f = saturate(is0 + is4 + is5 + is6 + is8 + is9);
                float g = saturate(is2 + is3 + is4 + is5 + is6 + is8 + is9);

                float value = 0.0;
                value = max(value, a * BoxMask(coords, float2(0.5, 0.88), float2(0.25, 0.055)));
                value = max(value, b * BoxMask(coords, float2(0.79, 0.69), float2(0.055, 0.17)));
                value = max(value, c * BoxMask(coords, float2(0.79, 0.31), float2(0.055, 0.17)));
                value = max(value, d * BoxMask(coords, float2(0.5, 0.12), float2(0.25, 0.055)));
                value = max(value, e * BoxMask(coords, float2(0.21, 0.31), float2(0.055, 0.17)));
                value = max(value, f * BoxMask(coords, float2(0.21, 0.69), float2(0.055, 0.17)));
                value = max(value, g * BoxMask(coords, float2(0.5, 0.50), float2(0.25, 0.055)));
                return saturate(value);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                fixed4 frozen = tex2D(_SourceTex, input.uv);
                fixed4 authored = tex2D(_PresentationTex, input.uv);
                fixed4 result = lerp(frozen, authored, input.presentation);

                float2 apertureMin = _AltitudeApertureUv.xy;
                float2 apertureMax = _AltitudeApertureUv.zw;
                float inAperture =
                    step(apertureMin.x, input.uv.x) *
                    step(apertureMin.y, input.uv.y) *
                    step(input.uv.x, apertureMax.x) *
                    step(input.uv.y, apertureMax.y) *
                    input.presentation;
                if (inAperture > 0.5)
                {
                    float2 local = saturate(
                        (input.uv - apertureMin) /
                        max(apertureMax - apertureMin, float2(0.0001, 0.0001)));
                    float column = min(floor(local.x * 4.0), 3.0);
                    float2 digitPoint = float2(frac(local.x * 4.0), local.y);
                    float digit =
                        column < 0.5 ? _AltitudeDigits.x :
                        column < 1.5 ? _AltitudeDigits.y :
                        column < 2.5 ? _AltitudeDigits.z :
                                       _AltitudeDigits.w;
                    float glyph = SevenSegmentDigit(digitPoint, digit);
                    fixed3 readoutBackground = fixed3(0.012, 0.016, 0.015);
                    fixed3 readoutInk = fixed3(0.80, 0.88, 0.76);
                    result.rgb = lerp(readoutBackground, readoutInk, glyph);
                    result.a = 1.0;
                }
                return result;
            }
            ENDCG
        }
    }
    Fallback Off
}
