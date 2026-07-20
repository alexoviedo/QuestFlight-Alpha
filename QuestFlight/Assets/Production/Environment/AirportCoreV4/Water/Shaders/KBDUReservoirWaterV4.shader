Shader "QuestFlight/Airport Core V4/Reservoir Opaque"
{
    Properties
    {
        [Normal] _WaveNormalA ("Broad low-frequency normal", 2D) = "bump" {}
        [Normal] _WaveNormalB ("Secondary low-frequency normal", 2D) = "bump" {}
        _DepthShoreRoughness ("Depth R / shore G / roughness B", 2D) = "black" {}
        _TerrainMacroAlbedo ("Shared geospatial orthophoto", 2D) = "gray" {}
        _EnvironmentCube ("Selected baked sky reflection", Cube) = "" {}
        _DeepColor ("Deep water", Color) = (0.035,0.125,0.185,1)
        _ShallowColor ("Shallow water", Color) = (0.17,0.34,0.35,1)
        _ShoreBandColor ("Shore light band", Color) = (0.55,0.60,0.43,1)
        _ReflectionTint ("Reflection tint", Color) = (0.86,0.91,0.94,1)
        _BasinCenterExtent ("Basin center XZ / half extent XZ", Vector) = (249.5625,4307.6875,1062.5625,857.1875)
        _TerrainWorldMinInvSize ("Orthophoto min XZ / inverse size", Vector) = (-6000,-6000,0.0000833333,0.0000833333)
        _OrthophotoInfluence ("Orthophoto water-color influence", Range(0,1)) = 0.72
        _EnvironmentRotationSinCos ("Sky rotation sin/cos", Vector) = (0,1,0,0)
        _WaveMetersA ("Broad wave repeat meters", Range(80,640)) = 210
        _WaveMetersB ("Secondary wave repeat meters", Range(24,240)) = 72
        _WaveScrollA ("Broad scroll UV/sec", Vector) = (0.00022,0.00009,0,0)
        _WaveScrollB ("Secondary scroll UV/sec", Vector) = (-0.00038,0.00031,0,0)
        _WaveStrengthA ("Broad strength", Range(0,0.5)) = 0.20
        _WaveStrengthB ("Secondary strength", Range(0,0.5)) = 0.12
        _RoughnessMin ("Deep roughness", Range(0.1,1)) = 0.32
        _RoughnessMax ("Shore roughness", Range(0.1,1)) = 0.68
        _ReflectionIntensity ("Baked reflection intensity", Range(0,1.5)) = 0.82
        _ReflectionFloor ("Minimum reflection", Range(0,0.3)) = 0.07
        _EnvironmentMaxMip ("Cubemap maximum mip", Range(0,10)) = 7
        _FresnelF0 ("Normal-incidence reflectance", Range(0.01,0.08)) = 0.02
        _SunGlitter ("Bounded directional sun glitter", Range(0,0.5)) = 0.16
        _ShoreBandStrength ("Shore light-band strength", Range(0,0.5)) = 0.18
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

            sampler2D _WaveNormalA;
            sampler2D _WaveNormalB;
            sampler2D _DepthShoreRoughness;
            sampler2D _TerrainMacroAlbedo;
            samplerCUBE _EnvironmentCube;
            fixed4 _DeepColor, _ShallowColor, _ShoreBandColor, _ReflectionTint;
            float4 _BasinCenterExtent;
            float4 _TerrainWorldMinInvSize;
            half4 _EnvironmentRotationSinCos, _WaveScrollA, _WaveScrollB;
            half _WaveMetersA, _WaveMetersB, _WaveStrengthA, _WaveStrengthB;
            half _RoughnessMin, _RoughnessMax, _ReflectionIntensity;
            half _ReflectionFloor, _EnvironmentMaxMip, _FresnelF0;
            half _SunGlitter, _ShoreBandStrength;
            half _OrthophotoInfluence;

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

            half FresnelSchlick(half ndv, half f0)
            {
                half x = 1.0h - saturate(ndv);
                half x2 = x * x;
                return f0 + (1.0h - f0) * x2 * x2 * x;
            }

            half3 RotateReflection(half3 direction)
            {
                half s = _EnvironmentRotationSinCos.x;
                half c = _EnvironmentRotationSinCos.y;
                return half3(direction.x * c - direction.z * s,
                             direction.y,
                             direction.x * s + direction.z * c);
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
                output.basinUv = (output.worldPosition.xz - _BasinCenterExtent.xy) /
                    max(_BasinCenterExtent.zw * 2.0, float2(1.0,1.0)) + 0.5;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 packed = tex2D(_DepthShoreRoughness, saturate(input.basinUv));
                half depth = saturate(packed.r);
                half shore = saturate(max(packed.g, 1.0h - smoothstep(0.01h, 0.18h, depth)));
                float2 terrainUv = saturate((input.worldPosition.xz - _TerrainWorldMinInvSize.xy) *
                    _TerrainWorldMinInvSize.zw);
                half3 orthophoto = tex2D(_TerrainMacroAlbedo, terrainUv).rgb;

                half2 uvA = input.worldPosition.xz / max(_WaveMetersA, 1.0h) + _Time.y * _WaveScrollA.xy;
                half2 rotatedB = half2(
                    input.worldPosition.x * 0.7071h - input.worldPosition.z * 0.7071h,
                    input.worldPosition.x * 0.7071h + input.worldPosition.z * 0.7071h);
                half2 uvB = rotatedB / max(_WaveMetersB, 1.0h) + _Time.y * _WaveScrollB.xy;
                half3 nA = UnpackNormal(tex2D(_WaveNormalA, uvA));
                half3 nB = UnpackNormal(tex2D(_WaveNormalB, uvB));
                half shoreDamping = lerp(0.42h, 1.0h, smoothstep(0.02h, 0.28h, depth));
                half2 slope = (nA.xy * _WaveStrengthA + nB.xy * _WaveStrengthB) * shoreDamping;
                half y = sqrt(saturate(1.0h - min(dot(slope,slope), 0.95h)));
                half3 waves = normalize(half3(slope.x, y, slope.y));
                half3 normal = normalize(lerp(normalize(input.worldNormal), waves, 0.90h));

                half roughness = saturate(lerp(_RoughnessMin, _RoughnessMax, packed.b) + shore * 0.10h);
                half3 viewDirection = normalize(_WorldSpaceCameraPos - input.worldPosition);
                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half3 halfDirection = normalize(viewDirection + lightDirection);
                half ndv = saturate(dot(normal, viewDirection));
                half ndl = saturate(dot(normal, lightDirection));
                half ndh = saturate(dot(normal, halfDirection));
                half fresnel = FresnelSchlick(ndv, _FresnelF0);

                half3 reflectionDirection = RotateReflection(reflect(-viewDirection, normal));
                half3 reflected = texCUBElod(_EnvironmentCube,
                    half4(reflectionDirection, roughness * _EnvironmentMaxMip)).rgb;
                reflected *= _ReflectionTint.rgb * _ReflectionIntensity;
                half reflectedPeak = max(reflected.r, max(reflected.g, reflected.b));
                reflected *= rcp(1.0h + reflectedPeak * 0.45h);

                half3 authoredWater = lerp(_ShallowColor.rgb, _DeepColor.rgb, smoothstep(0.02h, 0.86h, depth));
                // Preserve the photographed reservoir color and exposure. Near the shoreline the
                // orthophoto contribution approaches unity, hiding the opaque mesh boundary without
                // a transparent blanket or a terrain-suppression rectangle.
                half orthophotoWeight = saturate(_OrthophotoInfluence + shore * 0.24h);
                half3 baseColor = lerp(authoredWater, orthophoto, orthophotoWeight);
                baseColor = lerp(baseColor, _ShoreBandColor.rgb, shore * _ShoreBandStrength);
                half3 irradiance = saturate(max(ShadeSH9(half4(normal,1.0h)), 0.0h) * 0.35h +
                    _LightColor0.rgb * (0.04h + ndl * 0.22h));
                half3 color = baseColor * max(irradiance, half3(0.025h,0.025h,0.025h));
                half reflectionWeight = saturate(_ReflectionFloor + fresnel * (1.0h - roughness * 0.45h));
                color = lerp(color, reflected, reflectionWeight);

                half exponent = exp2(lerp(9.0h, 5.0h, roughness));
                half glitter = min(pow(ndh, exponent) * (1.0h - roughness) * ndl * _SunGlitter, 0.10h);
                color += _LightColor0.rgb * glitter;

                fixed4 output = fixed4(color, 1.0h);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback Off
}
