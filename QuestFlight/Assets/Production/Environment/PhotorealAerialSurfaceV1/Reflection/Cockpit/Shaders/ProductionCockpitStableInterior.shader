Shader "QuestFlight/Photoreal Aerial Surface V1/Cockpit Stable Interior"
{
    Properties
    {
        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1

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
        _FootwellEnclosureStrength ("Footwell Enclosure Strength", Range(0,0.72)) = 0.70
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
            Tags { "LightMode"="UniversalForward" }

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
            half _FootwellEnclosureStrength;
            float4x4 _WorldToAircraft;
            sampler2D _CockpitLocalShadowMap;
            float4x4 _CockpitLocalShadowWorldToCamera;
            float4x4 _CockpitLocalShadowWorldToClip;
            float4 _CockpitLocalShadowDepthParams;
            half _CockpitLocalShadowStrength;
            half _CockpitLocalShadowEnabled;

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

            half CockpitLocalShadowOcclusion(float3 worldPosition)
            {
                float4 shadowClip = mul(
                    _CockpitLocalShadowWorldToClip, float4(worldPosition, 1.0));
                float2 shadowUv = shadowClip.xy / max(abs(shadowClip.w), 0.00001);
                shadowUv = shadowUv * 0.5 + 0.5;

                float3 shadowView = mul(
                    _CockpitLocalShadowWorldToCamera, float4(worldPosition, 1.0)).xyz;
                float viewDepth = -shadowView.z;
                half inside =
                    step(0.0, shadowUv.x) * step(shadowUv.x, 1.0) *
                    step(0.0, shadowUv.y) * step(shadowUv.y, 1.0) *
                    step(_CockpitLocalShadowDepthParams.x, viewDepth) *
                    step(viewDepth, _CockpitLocalShadowDepthParams.y);
                half receiverDepth = saturate(
                    (viewDepth - _CockpitLocalShadowDepthParams.x) *
                    _CockpitLocalShadowDepthParams.z);
                // The map stores depth and depth-squared moments. Sampling mip four averages
                // a broad physical footprint before the comparison, producing one continuous
                // penumbra instead of a collection of offset hard PCF edges. The fixed mip and
                // world-space UV are identical in both eyes and contain no temporal jitter.
                float2 moments = tex2Dlod(
                    _CockpitLocalShadowMap, float4(shadowUv, 0.0, 4.0)).rg;
                float meanDepth = moments.x;
                float variance = max(moments.y - meanDepth * meanDepth, 0.00002);
                float depthDelta = receiverDepth - meanDepth -
                    _CockpitLocalShadowDepthParams.w;
                float probability = variance /
                    (variance + max(depthDelta * depthDelta, 0.000001));
                probability = saturate((probability - 0.16) / 0.84);
                half visibility = max(
                    step(receiverDepth, meanDepth + _CockpitLocalShadowDepthParams.w),
                    (half)probability);
                half occluded = 1.0h - visibility;
                return occluded * inside * saturate(_CockpitLocalShadowEnabled);
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

                // The firewall/engine cowl permanently encloses the forward lower cabin.
                // This aircraft-local cavity term is the baked analogue of that blocked
                // skylight: continuous, world-locked, identical in both eyes, and free of
                // the jagged/temporal artifacts produced by extra realtime shadow maps.
                // It is restricted to the footwell so panel labels and avionics remain clear.
                half3 aircraftLocalPosition = mul(
                    _WorldToAircraft, float4(input.worldPosition, 1.0)).xyz;
                half lowerCabin = 1.0h - smoothstep(
                    0.30h, 0.58h, aircraftLocalPosition.y);
                half forwardCavity = smoothstep(
                    0.00h, 0.30h, aircraftLocalPosition.z);
                half lateralCavity = 1.0h - smoothstep(
                    0.52h, 0.72h, abs(aircraftLocalPosition.x));
                half footwellEnclosure = saturate(
                    lowerCabin * forwardCavity * lateralCavity *
                    _FootwellEnclosureStrength);
                ambient *= lerp(1.0h, 0.34h, footwellEnclosure);
                warmCabinBounce *= lerp(1.0h, 0.55h, footwellEnclosure);

                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half nDotL = saturate(dot(normalWS, lightDirection));
                half aperture = lerp(0.28h, 0.82h, skyExposure);
                // RGBA stores low-frequency geometry-ray visibility for sun arriving from
                // aircraft +Z/+X/-Z/-X. It remains only a restrained ambient-depth fallback.
                // Recognizable cabin-frame silhouettes come from the one aircraft-local map
                // sampled identically by both eyes below.
                half3 localLight = normalize(mul((half3x3)_WorldToAircraft, lightDirection));
                half4 directionWeights = half4(
                    max(localLight.z, 0.0h), max(localLight.x, 0.0h),
                    max(-localLight.z, 0.0h), max(-localLight.x, 0.0h));
                half weightSum = max(dot(directionWeights, half4(1, 1, 1, 1)), 0.001h);
                directionWeights /= weightSum;
                half bakedSunVisibility = saturate(dot(
                    input.directionalVisibility, directionWeights));
                half sunlightAbove = smoothstep(0.04h, 0.30h, localLight.y);
                half lowFrequencyVisibility = lerp(
                    1.0h,
                    lerp(0.72h, 1.0h, bakedSunVisibility),
                    _DirectionalCabinShadowStrength * 0.24h * sunlightAbove);
                ambient *= lerp(1.0h, lowFrequencyVisibility, 0.18h);
                warmCabinBounce *= lerp(1.0h, lowFrequencyVisibility, 0.10h);

                half localShadow = CockpitLocalShadowOcclusion(input.worldPosition);
                half localShadowAmount =
                    localShadow * saturate(_CockpitLocalShadowStrength) * sunlightAbove;
                // Direct sunlight is removed strongly while indirect cabin light remains.
                // A small ambient attenuation keeps the silhouette readable without turning
                // the instrument panel, labels, or upholstery into a black cutout.
                half directShadowVisibility = 1.0h - localShadowAmount;
                // Vertical cockpit faces receive little direct sun, so a bounded portion of
                // skylight must also follow the cabin-frame silhouette for the shadow to read.
                // At the authored 0.62 strength this bottoms near 0.83 rather than black; the
                // emissive avionics path below remains completely unattenuated.
                ambient *= lerp(1.0h, 0.72h, localShadowAmount);
                half3 direct =
                    _LightColor0.rgb * nDotL * aperture * directShadowVisibility;
                direct *= lerp(1.0h, 0.04h, footwellEnclosure);

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
                color += specularColor * specular * _LightColor0.rgb * aperture *
                    directShadowVisibility * lerp(1.0h, 0.08h, footwellEnclosure);
                color += emission;
                fixed4 result = fixed4(color, 1.0h);
                UNITY_APPLY_FOG(input.fogCoord, result);
                return result;
            }
            
#pragma multi_compile_instancing
ENDCG
        }
    
        Pass
        {
            Name "XRMotionVectors"
            Tags { "LightMode"="XRMotionVectors" }
            Cull Off Blend Off ZWrite On ZTest LEqual ColorMask RGBA
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            CGPROGRAM

            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
#include "Assets/Shaders/ProductSpaceWarpMotion.cginc"
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
            half _FootwellEnclosureStrength;
            float4x4 _WorldToAircraft;
            sampler2D _CockpitLocalShadowMap;
            float4x4 _CockpitLocalShadowWorldToCamera;
            float4x4 _CockpitLocalShadowWorldToClip;
            float4 _CockpitLocalShadowDepthParams;
            half _CockpitLocalShadowStrength;
            half _CockpitLocalShadowEnabled;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                half4 tangent : TANGENT;
                fixed4 directionalVisibility : COLOR;
                float2 uv : TEXCOORD0;
                
float3 qfPreviousVertex : TEXCOORD4;
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
                
float4 qfCurrent : TEXCOORD14; float4 qfPrevious : TEXCOORD15;
UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f QFColorVertex(appdata input, out float4 qfWorld)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = QFObjectToMotionClip(input.vertex, qfWorld);
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

            half CockpitLocalShadowOcclusion(float3 worldPosition)
            {
                float4 shadowClip = mul(
                    _CockpitLocalShadowWorldToClip, float4(worldPosition, 1.0));
                float2 shadowUv = shadowClip.xy / max(abs(shadowClip.w), 0.00001);
                shadowUv = shadowUv * 0.5 + 0.5;

                float3 shadowView = mul(
                    _CockpitLocalShadowWorldToCamera, float4(worldPosition, 1.0)).xyz;
                float viewDepth = -shadowView.z;
                half inside =
                    step(0.0, shadowUv.x) * step(shadowUv.x, 1.0) *
                    step(0.0, shadowUv.y) * step(shadowUv.y, 1.0) *
                    step(_CockpitLocalShadowDepthParams.x, viewDepth) *
                    step(viewDepth, _CockpitLocalShadowDepthParams.y);
                half receiverDepth = saturate(
                    (viewDepth - _CockpitLocalShadowDepthParams.x) *
                    _CockpitLocalShadowDepthParams.z);
                // The map stores depth and depth-squared moments. Sampling mip four averages
                // a broad physical footprint before the comparison, producing one continuous
                // penumbra instead of a collection of offset hard PCF edges. The fixed mip and
                // world-space UV are identical in both eyes and contain no temporal jitter.
                float2 moments = tex2Dlod(
                    _CockpitLocalShadowMap, float4(shadowUv, 0.0, 4.0)).rg;
                float meanDepth = moments.x;
                float variance = max(moments.y - meanDepth * meanDepth, 0.00002);
                float depthDelta = receiverDepth - meanDepth -
                    _CockpitLocalShadowDepthParams.w;
                float probability = variance /
                    (variance + max(depthDelta * depthDelta, 0.000001));
                probability = saturate((probability - 0.16) / 0.84);
                half visibility = max(
                    step(receiverDepth, meanDepth + _CockpitLocalShadowDepthParams.w),
                    (half)probability);
                half occluded = 1.0h - visibility;
                return occluded * inside * saturate(_CockpitLocalShadowEnabled);
            }

            fixed4 QFColorFragment(v2f input) : SV_Target
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

                // The firewall/engine cowl permanently encloses the forward lower cabin.
                // This aircraft-local cavity term is the baked analogue of that blocked
                // skylight: continuous, world-locked, identical in both eyes, and free of
                // the jagged/temporal artifacts produced by extra realtime shadow maps.
                // It is restricted to the footwell so panel labels and avionics remain clear.
                half3 aircraftLocalPosition = mul(
                    _WorldToAircraft, float4(input.worldPosition, 1.0)).xyz;
                half lowerCabin = 1.0h - smoothstep(
                    0.30h, 0.58h, aircraftLocalPosition.y);
                half forwardCavity = smoothstep(
                    0.00h, 0.30h, aircraftLocalPosition.z);
                half lateralCavity = 1.0h - smoothstep(
                    0.52h, 0.72h, abs(aircraftLocalPosition.x));
                half footwellEnclosure = saturate(
                    lowerCabin * forwardCavity * lateralCavity *
                    _FootwellEnclosureStrength);
                ambient *= lerp(1.0h, 0.34h, footwellEnclosure);
                warmCabinBounce *= lerp(1.0h, 0.55h, footwellEnclosure);

                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half nDotL = saturate(dot(normalWS, lightDirection));
                half aperture = lerp(0.28h, 0.82h, skyExposure);
                // RGBA stores low-frequency geometry-ray visibility for sun arriving from
                // aircraft +Z/+X/-Z/-X. It remains only a restrained ambient-depth fallback.
                // Recognizable cabin-frame silhouettes come from the one aircraft-local map
                // sampled identically by both eyes below.
                half3 localLight = normalize(mul((half3x3)_WorldToAircraft, lightDirection));
                half4 directionWeights = half4(
                    max(localLight.z, 0.0h), max(localLight.x, 0.0h),
                    max(-localLight.z, 0.0h), max(-localLight.x, 0.0h));
                half weightSum = max(dot(directionWeights, half4(1, 1, 1, 1)), 0.001h);
                directionWeights /= weightSum;
                half bakedSunVisibility = saturate(dot(
                    input.directionalVisibility, directionWeights));
                half sunlightAbove = smoothstep(0.04h, 0.30h, localLight.y);
                half lowFrequencyVisibility = lerp(
                    1.0h,
                    lerp(0.72h, 1.0h, bakedSunVisibility),
                    _DirectionalCabinShadowStrength * 0.24h * sunlightAbove);
                ambient *= lerp(1.0h, lowFrequencyVisibility, 0.18h);
                warmCabinBounce *= lerp(1.0h, lowFrequencyVisibility, 0.10h);

                half localShadow = CockpitLocalShadowOcclusion(input.worldPosition);
                half localShadowAmount =
                    localShadow * saturate(_CockpitLocalShadowStrength) * sunlightAbove;
                // Direct sunlight is removed strongly while indirect cabin light remains.
                // A small ambient attenuation keeps the silhouette readable without turning
                // the instrument panel, labels, or upholstery into a black cutout.
                half directShadowVisibility = 1.0h - localShadowAmount;
                // Vertical cockpit faces receive little direct sun, so a bounded portion of
                // skylight must also follow the cabin-frame silhouette for the shadow to read.
                // At the authored 0.62 strength this bottoms near 0.83 rather than black; the
                // emissive avionics path below remains completely unattenuated.
                ambient *= lerp(1.0h, 0.72h, localShadowAmount);
                half3 direct =
                    _LightColor0.rgb * nDotL * aperture * directShadowVisibility;
                direct *= lerp(1.0h, 0.04h, footwellEnclosure);

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
                color += specularColor * specular * _LightColor0.rgb * aperture *
                    directShadowVisibility * lerp(1.0h, 0.08h, footwellEnclosure);
                color += emission;
                fixed4 result = fixed4(color, 1.0h);
                UNITY_APPLY_FOG(input.fogCoord, result);
                return result;
            }
            
#pragma multi_compile_instancing

v2f QFPreviousVertex(appdata input, out float4 qfWorld)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = QFObjectToMotionClip(input.vertex, qfWorld);
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
v2f vert(appdata v)
{
    UNITY_SETUP_INSTANCE_ID(v);
    float4 world;
    v2f o=QFColorVertex(v,world);
    #if !defined(QF_STATIC_WORLD_GEOMETRY)
    if (_QFHasPreviousModel < .5 && (_QFPreviousVertices > .5 || unity_MotionVectorsParams.x == 1))
        v.vertex.xyz=v.qfPreviousVertex;
    #endif
    float4 previousWorld;
    v2f old=QFPreviousVertex(v,previousWorld);
    float4 oldWorld=QFPreviousWorld(previousWorld);
    if (_QFResetObjectMotion > .5) oldWorld=world;
    o.qfCurrent=o.position;
    o.qfPrevious=mul(_PrevViewProjMatrixStereo[QFEye()],oldWorld);
    
    o.position=o.qfCurrent;
    return o;
}
float4 frag(v2f v) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
    float coverage=QFColorFragment(v).a;
    clip(coverage-0.001);
    return QFMotion(v.qfCurrent,v.qfPrevious,coverage);
}

            ENDCG
        }
}
    Fallback Off
}
