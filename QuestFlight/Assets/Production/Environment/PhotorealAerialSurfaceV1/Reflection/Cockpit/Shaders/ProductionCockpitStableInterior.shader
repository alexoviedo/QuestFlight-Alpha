Shader "QuestFlight/Photoreal Aerial Surface V1/Cockpit Stable Interior"
{
    Properties
    {
        baseColorTexture ("Base Color", 2D) = "white" {}
        baseColorFactor ("Base Color Factor", Color) = (1,1,1,1)
        metallicRoughnessTexture ("Metallic Roughness", 2D) = "white" {}
        metallicFactor ("Metallic", Range(0,1)) = 0
        roughnessFactor ("Roughness", Range(0.04,1)) = 0.75
        normalTexture ("Normal", 2D) = "bump" {}
        normalTexture_scale ("Normal Scale", Range(0,1)) = 0.42
        occlusionTexture ("Baked Occlusion", 2D) = "white" {}
        occlusionTexture_strength ("Baked Occlusion Strength", Range(0,1)) = 1
        emissiveTexture ("Emission", 2D) = "black" {}
        emissiveFactor ("Emission Factor", Color) = (0,0,0,1)
        _CabinDepthStrength ("Cabin Depth Strength", Range(0,0.42)) = 0.38
        _DirectionalCabinShadowStrength ("Directional Cabin Shade", Range(0,0.65)) = 0.58
    }

    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        LOD 160
        Cull Off
        ZWrite On
        Blend Off

        Pass
        {
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D baseColorTexture;
            float4 baseColorTexture_ST;
            fixed4 baseColorFactor;
            sampler2D metallicRoughnessTexture;
            half metallicFactor;
            half roughnessFactor;
            sampler2D normalTexture;
            half normalTexture_scale;
            sampler2D occlusionTexture;
            half occlusionTexture_strength;
            sampler2D emissiveTexture;
            fixed4 emissiveFactor;
            half _CabinDepthStrength;
            half _DirectionalCabinShadowStrength;
            float4x4 _WorldToAircraft;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                half4 tangent : TANGENT;
                fixed4 directionalVisibility : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half3 tangentWS : TEXCOORD2;
                half3 bitangentWS : TEXCOORD3;
                float3 worldPosition : TEXCOORD4;
                UNITY_FOG_COORDS(5)
                fixed4 directionalVisibility : TEXCOORD6;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, baseColorTexture);
                output.normalWS = normalize(UnityObjectToWorldNormal(input.normal));
                output.tangentWS = normalize(UnityObjectToWorldDir(input.tangent.xyz));
                output.bitangentWS = normalize(cross(output.normalWS, output.tangentWS) *
                    (input.tangent.w * unity_WorldTransformParams.w));
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.directionalVisibility = input.directionalVisibility;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                fixed4 baseSample = tex2D(baseColorTexture, input.uv) * baseColorFactor;
                half3 tangentNormal = UnpackNormal(tex2D(normalTexture, input.uv));
                tangentNormal.xy *= normalTexture_scale;
                tangentNormal.z = sqrt(saturate(1.0h - dot(tangentNormal.xy, tangentNormal.xy)));
                half3 normalWS = normalize(
                    input.tangentWS * tangentNormal.x +
                    input.bitangentWS * tangentNormal.y +
                    input.normalWS * tangentNormal.z);

                half packedOcclusion = tex2D(occlusionTexture, input.uv).r;
                // The imported combined map contains near-zero legacy texels that read as
                // featureless black in-headset. Keep those cavities deep without allowing
                // them to erase the authored surface response.
                half boundedOcclusion = lerp(0.62h, 1.0h, packedOcclusion);
                half bakedOcclusion = lerp(1.0h, boundedOcclusion, occlusionTexture_strength);
                half skyExposure = saturate(normalWS.y * 0.5h + 0.5h);

                // This is deliberately geometry-bound rather than a projected shadow. Downward
                // and vertical cabin surfaces receive less sky and sun, while their authored AO
                // supplies local crevice depth. The response is deterministic in both eyes and
                // cannot cover the windshield or reveal screen-space triangles.
                half enclosure = lerp(1.0h - _CabinDepthStrength * 0.88h,
                                      1.0h - _CabinDepthStrength * 0.16h,
                                      skyExposure);
                half3 ambient = max(ShadeSH9(half4(normalWS, 1.0h)), half3(0.24h, 0.25h, 0.27h));
                ambient *= lerp(0.58h, 0.92h, skyExposure);
                half3 warmCabinBounce = half3(0.20h, 0.17h, 0.14h) *
                    lerp(0.92h, 0.48h, skyExposure);

                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half nDotL = saturate(dot(normalWS, lightDirection));
                half aperture = lerp(0.28h, 0.82h, skyExposure);
                // RGBA stores soft geometry-ray visibility for sun arriving from aircraft
                // +Z/+X/-Z/-X. Interpolation selects the current authored sun direction.
                // This follows the real cabin shell/panel/seat geometry while remaining a
                // single stable pass with no per-eye projection or realtime shadow map.
                half3 localLight = normalize(mul((half3x3)_WorldToAircraft, lightDirection));
                half4 directionWeights = half4(
                    max(localLight.z, 0.0h), max(localLight.x, 0.0h),
                    max(-localLight.z, 0.0h), max(-localLight.x, 0.0h));
                half weightSum = max(dot(directionWeights, half4(1, 1, 1, 1)), 0.001h);
                directionWeights /= weightSum;
                half bakedSunVisibility = saturate(dot(
                    input.directionalVisibility, directionWeights));
                half sunlightAbove = smoothstep(0.04h, 0.30h, localLight.y);
                half directionalVisibility = lerp(
                    1.0h,
                    lerp(0.38h, 1.0h, bakedSunVisibility),
                    _DirectionalCabinShadowStrength * sunlightAbove);
                ambient *= lerp(1.0h, directionalVisibility, 0.58h);
                warmCabinBounce *= lerp(1.0h, directionalVisibility, 0.42h);
                half3 direct = _LightColor0.rgb * nDotL * aperture * directionalVisibility;

                half4 packedSurface = tex2D(metallicRoughnessTexture, input.uv);
                half metallic = saturate(packedSurface.b * metallicFactor);
                half roughness = saturate(packedSurface.g * roughnessFactor);
                half3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.worldPosition);
                half3 halfDirection = normalize(viewDirection + lightDirection);
                half specularPower = lerp(96.0h, 8.0h, roughness);
                half specular = pow(saturate(dot(normalWS, halfDirection)), specularPower);
                half3 specularColor = lerp(half3(0.025h, 0.025h, 0.025h), baseSample.rgb, metallic);

                half3 emission = tex2D(emissiveTexture, input.uv).rgb * emissiveFactor.rgb;
                half3 readableAlbedo = lerp(baseSample.rgb, sqrt(saturate(baseSample.rgb)), 0.24h);
                half3 color = readableAlbedo * bakedOcclusion * enclosure *
                    (ambient + warmCabinBounce + direct);
                color += specularColor * specular * _LightColor0.rgb * aperture;
                color += emission;
                fixed4 result = fixed4(color, 1.0h);
                UNITY_APPLY_FOG(input.fogCoord, result);
                return result;
            }
            ENDCG
        }
    }
    Fallback Off
}
