Shader "QuestFlightLab/Production Stable Water"
{
    Properties
    {
        [HideInInspector] _MainTex ("Compatibility", 2D) = "white" {}
        [Normal] _BroadNormal ("Broad wave normal", 2D) = "bump" {}
        [Normal] _RippleNormal ("Fine ripple normal", 2D) = "bump" {}
        _DepthShoreRoughness ("Depth (R), shore (G), roughness (B)", 2D) = "black" {}
        _EnvironmentCube ("Selected sky reflection cubemap", Cube) = "" {}

        _Color ("Deep water color", Color) = (0.045,0.17,0.24,1)
        _ShoreColor ("Shallow water color", Color) = (0.19,0.38,0.38,1)
        _ShoreBandColor ("Shore light-band color", Color) = (0.62,0.67,0.52,1)
        _HorizonColor ("Sky reflection tint", Color) = (0.82,0.90,0.94,1)
        _BasinCenterExtent ("Basin center XZ / extent XZ", Vector) = (249.5625,4307.6875,1062.5625,857.1875)
        _EnvironmentRotationSinCos ("Sky rotation sin/cos", Vector) = (0,1,0,0)

        _BroadNormalMeters ("Broad normal repeat meters", Range(64,512)) = 240
        _RippleNormalMeters ("Ripple normal repeat meters", Range(16,192)) = 72
        _BroadNormalScroll ("Broad normal scroll UV/sec", Vector) = (0.0011,0.00045,0,0)
        _RippleNormalScroll ("Ripple normal scroll UV/sec", Vector) = (-0.0022,0.0031,0,0)
        _BroadNormalStrength ("Broad normal strength", Range(0,1)) = 0.48
        _FineNormalStrength ("Fine normal strength", Range(0,1)) = 0.30
        _RippleStrength ("Combined ripple strength", Range(0,1)) = 0.68

        _DepthContrast ("Artistic depth contrast", Range(0,1.5)) = 1
        _SurfaceVariation ("Surface color variation", Range(0,0.25)) = 0.08
        _ShoreBandStrength ("Shore light-band strength", Range(0,0.5)) = 0.16
        _Roughness ("Compatibility roughness center", Range(0,1)) = 0.46
        _RoughnessMin ("Minimum roughness", Range(0.05,1)) = 0.22
        _RoughnessMax ("Maximum roughness", Range(0.05,1)) = 0.58
        _EnvironmentMaxMip ("Maximum reflection mip", Range(0,10)) = 6
        _EnvironmentIntensity ("Sky reflection intensity", Range(0,2)) = 0.90
        _ReflectionFloor ("Minimum sky reflection", Range(0,0.35)) = 0.10
        _FresnelF0 ("Water normal-incidence reflectance", Range(0.01,0.08)) = 0.02
        _GlintStrength ("Directional sun glint", Range(0,1)) = 0.42
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+5" }
        LOD 120
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _BroadNormal;
            sampler2D _RippleNormal;
            sampler2D _DepthShoreRoughness;
            samplerCUBE _EnvironmentCube;

            fixed4 _Color;
            fixed4 _ShoreColor;
            fixed4 _ShoreBandColor;
            fixed4 _HorizonColor;
            float4 _BasinCenterExtent;
            half4 _EnvironmentRotationSinCos;
            half4 _BroadNormalScroll;
            half4 _RippleNormalScroll;
            half _BroadNormalMeters;
            half _RippleNormalMeters;
            half _BroadNormalStrength;
            half _FineNormalStrength;
            half _RippleStrength;
            half _DepthContrast;
            half _SurfaceVariation;
            half _ShoreBandStrength;
            half _Roughness;
            half _RoughnessMin;
            half _RoughnessMax;
            half _EnvironmentMaxMip;
            half _EnvironmentIntensity;
            half _ReflectionFloor;
            half _FresnelF0;
            half _GlintStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                half3 worldNormal : TEXCOORD1;
                half2 basinUv : TEXCOORD2;
                UNITY_FOG_COORDS(3)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half SchlickFresnel(half normalDotView, half f0)
            {
                half oneMinus = 1.0h - saturate(normalDotView);
                half oneMinus2 = oneMinus * oneMinus;
                half oneMinus5 = oneMinus2 * oneMinus2 * oneMinus;
                return f0 + (1.0h - f0) * oneMinus5;
            }

            half3 RotateEnvironmentDirection(half3 direction)
            {
                half sine = _EnvironmentRotationSinCos.x;
                half cosine = _EnvironmentRotationSinCos.y;
                return half3(
                    direction.x * cosine - direction.z * sine,
                    direction.y,
                    direction.x * sine + direction.z * cosine);
            }

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.basinUv =
                    (output.worldPosition.xz - _BasinCenterExtent.xy) /
                    max(_BasinCenterExtent.zw * 2.0, float2(1.0, 1.0)) + 0.5;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // The surface mesh remains immutable. Only two project-authored normal
                // textures scroll, producing stable low-cost wave motion with no geometry work.
                half2 broadUv = input.worldPosition.xz / max(_BroadNormalMeters, 1.0h) +
                    _Time.y * _BroadNormalScroll.xy;
                half2 rippleUv = input.worldPosition.xz / max(_RippleNormalMeters, 1.0h) +
                    _Time.y * _RippleNormalScroll.xy;
                half3 broadNormal = UnpackNormal(tex2D(_BroadNormal, broadUv));
                half3 fineNormal = UnpackNormal(tex2D(_RippleNormal, rippleUv));
                half2 slope = broadNormal.xy * _BroadNormalStrength + fineNormal.xy * _FineNormalStrength;
                slope *= _RippleStrength;
                half vertical = sqrt(saturate(1.0h - min(dot(slope, slope), 0.96h)));
                half3 waveNormal = normalize(half3(slope.x, vertical, slope.y));
                half3 normal = normalize(lerp(normalize(input.worldNormal), waveNormal, 0.92h));

                half3 packedMask = tex2D(_DepthShoreRoughness, saturate(input.basinUv)).rgb;
                half depth = saturate(packedMask.r * _DepthContrast);
                half shoreBand = packedMask.g;
                half roughness = saturate(
                    lerp(_RoughnessMin, _RoughnessMax, packedMask.b) + (_Roughness - 0.5h) * 0.12h);

                half3 viewDirection = normalize(_WorldSpaceCameraPos - input.worldPosition);
                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half3 halfDirection = normalize(viewDirection + lightDirection);
                half normalDotView = saturate(dot(normal, viewDirection));
                half normalDotLight = saturate(dot(normal, lightDirection));
                half normalDotHalf = saturate(dot(normal, halfDirection));

                half fresnel = SchlickFresnel(normalDotView, _FresnelF0);
                half3 reflectionDirection = RotateEnvironmentDirection(reflect(-viewDirection, normal));
                half3 skyReflection = texCUBElod(_EnvironmentCube,
                    half4(reflectionDirection, roughness * _EnvironmentMaxMip)).rgb;
                skyReflection *= _HorizonColor.rgb * _EnvironmentIntensity;

                half colorVariation = lerp(
                    1.0h - _SurfaceVariation,
                    1.0h + _SurfaceVariation,
                    packedMask.b);
                half3 waterColor = lerp(_ShoreColor.rgb, _Color.rgb, smoothstep(0.04h, 0.92h, depth));
                waterColor *= colorVariation;
                waterColor += _ShoreBandColor.rgb * shoreBand * _ShoreBandStrength;

                half3 ambient = max(ShadeSH9(half4(normal, 1.0h)), half3(0.025h, 0.025h, 0.025h));
                half3 litWater = waterColor * (ambient + _LightColor0.rgb * normalDotLight * 0.20h);
                half reflectionWeight = saturate(_ReflectionFloor + fresnel * (1.0h - roughness * 0.35h));
                half3 color = lerp(litWater, skyReflection, reflectionWeight);

                // One bounded main-sun highlight supplies glitter without a probe, screen
                // sample, secondary camera, or transparent layer.
                half specularExponent = exp2(lerp(9.0h, 5.0h, roughness));
                half sunGlint = pow(normalDotHalf, specularExponent) *
                    (1.0h - roughness) * _GlintStrength * normalDotLight;
                color += _LightColor0.rgb * sunGlint;

                fixed4 output = fixed4(color, 1.0h);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback Off
}
