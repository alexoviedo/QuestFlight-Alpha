Shader "QuestFlight/Boulder Sparse Volumetric Cloud Field"
{
    Properties
    {
        _DensitySlices ("VDB Density Slice Atlas", 2D) = "black" {}
        _LitColor ("Sky-Matched Lit Color", Color) = (0.94,0.95,0.97,1)
        _AmbientColor ("Ambient Vapor Color", Color) = (0.68,0.72,0.77,1)
        _UndersideColor ("Optically Thick Underside", Color) = (0.40,0.44,0.50,1)
        _DensityGain ("Density Gain", Range(0.25,4.0)) = 1.90
        _Extinction ("Beer-Lambert Extinction", Range(0.2,8.0)) = 4.6
        _SilverLining ("Forward Scattering", Range(0.0,1.5)) = 0.40
        _MaximumDistance ("Maximum Ray Distance Metres", Float) = 18000
        _MinimumStepMetres ("Minimum March Step Metres", Range(60,400)) = 60
        [Toggle] _CloudSamplingLod ("Experimental Distance Sampling LOD", Float) = 0
        [HideInInspector] _KBDUCloudExteriorOpacity ("Interior Transition", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent-20"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }
        LOD 120
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend One OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _DensitySlices;
            half4 _LitColor;
            half4 _AmbientColor;
            half4 _UndersideColor;
            half _DensityGain;
            half _Extinction;
            half _SilverLining;
            float _MaximumDistance;
            half _MinimumStepMetres;
            float _CloudSamplingLod;
            half _KBDUCloudExteriorOpacity;

            static const int MaximumMarchSteps = 48;
            static const float SliceCount = 64.0;
            static const float SliceColumns = 8.0;
            static const float SliceRows = 8.0;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 volumeCenter : TEXCOORD1;
                float3 volumeRadii : TEXCOORD2;
                float2 volumeParameters : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 volumeCenter : TEXCOORD1;
                float3 volumeRadii : TEXCOORD2;
                float2 volumeParameters : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
                float samplingBudget : TEXCOORD4;
            };

            v2f vert(appdata input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.position = UnityWorldToClipPos(output.worldPosition);
                output.volumeCenter = input.volumeCenter;
                output.volumeRadii = input.volumeRadii;
                output.volumeParameters = input.volumeParameters;
                output.samplingBudget = MaximumMarchSteps;
                if (_CloudSamplingLod > 0.5)
                {
                    // One conservative budget per formation, shared by both eyes.
                    float3 lodCamera = _WorldSpaceCameraPos;
                    float projectionScale = abs(UNITY_MATRIX_P[1][1]);
                    #if defined(UNITY_SINGLE_PASS_STEREO)
                        lodCamera = (unity_StereoWorldSpaceCameraPos[0] + unity_StereoWorldSpaceCameraPos[1]) * 0.5;
                        projectionScale = max(abs(unity_StereoMatrixP[0][1][1]), abs(unity_StereoMatrixP[1][1][1]));
                    #endif
                    float3 localCamera = mul(unity_WorldToObject, float4(lodCamera, 1)).xyz;
                    float3 delta = abs(localCamera - input.volumeCenter);
                    float surfaceDistance = length(max(delta - input.volumeRadii, 0));
                    // Bounding-sphere screen fraction protects large distant formations.
                    float radius = length(input.volumeRadii);
                    float projectedFraction = radius * projectionScale / max(length(delta) - radius, 1);
                    float middleWeight = smoothstep(1500, 3500, surfaceDistance) *
                        (1 - smoothstep(0.35, 0.65, projectedFraction));
                    float farWeight = smoothstep(5000, 9000, surfaceDistance) *
                        (1 - smoothstep(0.12, 0.25, projectedFraction));
                    output.samplingBudget = lerp(lerp(48.0, 24.0, middleWeight), 12.0, farWeight);
                }
                return output;
            }

            bool IntersectUnitBox(float3 origin, float3 direction, out float entry, out float exit)
            {
                float3 safeDirection = direction;
                safeDirection += (1.0 - step(1e-6, abs(safeDirection))) * 1e-6;
                float3 inverseDirection = 1.0 / safeDirection;
                float3 first = (-0.5 - origin) * inverseDirection;
                float3 second = (0.5 - origin) * inverseDirection;
                float3 lower = min(first, second);
                float3 upper = max(first, second);
                entry = max(lower.x, max(lower.y, lower.z));
                exit = min(upper.x, min(upper.y, upper.z));
                return exit > max(entry, 0.0);
            }

            float2 SliceUv(float2 xy, float slice)
            {
                float column = fmod(slice, SliceColumns);
                float row = floor(slice / SliceColumns);
                // Half-texel inset prevents bilinear filtering across tiles.
                float2 inset = float2(0.5 / 512.0, 0.5 / 256.0);
                float2 extent = float2(1.0 / SliceColumns, 1.0 / SliceRows) - 2.0 * inset;
                return float2(column / SliceColumns, row / SliceRows) + inset + saturate(xy) * extent;
            }

            half4 SampleDensityVolume(float3 coordinate)
            {
                coordinate = saturate(coordinate);
                float slicePosition = coordinate.z * (SliceCount - 1.0);
                float lowerSlice = floor(slicePosition);
                float upperSlice = min(lowerSlice + 1.0, SliceCount - 1.0);
                float fraction = slicePosition - lowerSlice;
                half4 lower = tex2Dlod(_DensitySlices, float4(SliceUv(coordinate.xy, lowerSlice), 0, 0));
                half4 upper = tex2Dlod(_DensitySlices, float4(SliceUv(coordinate.xy, upperSlice), 0, 0));
                return lerp(lower, upper, fraction);
            }

            half EvaluateDensity(
                float3 localPosition,
                half2 volumeParameters,
                out half lightTransmittance)
            {
                float normalizedHeight = saturate(localPosition.y + 0.5);
                float3 densityCoordinate = saturate(localPosition + 0.5);
                if (volumeParameters.x > 0.5h)
                    densityCoordinate.x = 1.0 - densityCoordinate.x;
                half4 densityData = SampleDensityVolume(densityCoordinate);
                half archetype = lerp(densityData.r, densityData.g, volumeParameters.x);

                half baseRamp = smoothstep(0.015h, 0.10h, normalizedHeight);
                half topStart = lerp(0.60h, 0.76h, volumeParameters.x);
                half topRamp = 1.0h - smoothstep(topStart, 0.98h, normalizedHeight);
                half verticalShape = baseRamp * topRamp;
                half body = smoothstep(0.07h, 0.48h, archetype);
                // Erosion may only remove vapor. A signed term made empty
                // atlas voxels positive and exposed the rectangular proxy.
                half erosion = max(densityData.a - 0.5h, 0.0h) *
                    0.12h * (1.0h - body);
                half density = saturate(
                    (body - erosion) * verticalShape * volumeParameters.y * _DensityGain);
                half verticalIllumination = lerp(
                    0.38h, 1.0h, smoothstep(0.10h, 0.82h, normalizedHeight));
                lightTransmittance = saturate(
                    densityData.b * 0.72h + normalizedHeight * 0.24h) *
                    verticalIllumination;
                return density;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 cameraWorld = _WorldSpaceCameraPos;
                #if defined(UNITY_SINGLE_PASS_STEREO)
                    cameraWorld = unity_StereoWorldSpaceCameraPos[unity_StereoEyeIndex];
                #endif
                float3 rayDirectionWorld = normalize(input.worldPosition - cameraWorld);
                float3 rootOrigin = mul(unity_WorldToObject, float4(cameraWorld, 1.0)).xyz;
                float3 rootDirection = mul((float3x3)unity_WorldToObject, rayDirectionWorld);
                float3 diameter = max(input.volumeRadii * 2.0, 0.001);
                float3 rayOriginLocal = (rootOrigin - input.volumeCenter) / diameter;
                float3 rayDirectionLocal = rootDirection / diameter;

                float entry;
                float exit;
                if (!IntersectUnitBox(rayOriginLocal, rayDirectionLocal, entry, exit))
                    discard;

                bool cameraInside = all(abs(rayOriginLocal) < 0.5001);
                float expectedSurfaceDistance = cameraInside ? exit : max(entry, 0.0);
                float fragmentDistance = dot(input.worldPosition - cameraWorld, rayDirectionWorld);
                float surfaceTolerance = max(4.0, abs(expectedSurfaceDistance) * 0.0025);
                if (abs(fragmentDistance - expectedSurfaceDistance) > surfaceTolerance)
                    discard;

                float rayStart = max(entry, 0.0);
                float rayEnd = min(exit, _MaximumDistance);
                float span = rayEnd - rayStart;
                if (span <= 0.01)
                    discard;

                // Cover the whole ray interval; optical depth below uses this spacing.
                // Keep the exact original lattice when the experiment is disabled.
                float stepLength = max((float)_MinimumStepMetres, span / input.samplingBudget);
                float sampleDistance = rayStart + stepLength * 0.5;
                half transmittance = 1.0h;
                half3 accumulated = 0.0h;
                half3 sunDirection = normalize(_WorldSpaceLightPos0.xyz);
                half sunAlignment = saturate(dot(rayDirectionWorld, sunDirection));
                half phase = 0.72h + _SilverLining * pow(sunAlignment, 8.0h);

                [loop]
                for (int stepIndex = 0; stepIndex < MaximumMarchSteps; stepIndex++)
                {
                    if (sampleDistance >= rayEnd || transmittance < 0.025h)
                        break;
                    // Reuse the affine local ray without changing the march lattice.
                    float3 sampleLocal = rayOriginLocal + rayDirectionLocal * sampleDistance;
                    half lightTransmittance;
                    half density = EvaluateDensity(
                        sampleLocal, input.volumeParameters, lightTransmittance);
                    if (density > 0.001h)
                    {
                        half opticalDepth = density * _Extinction * (stepLength * 0.001h);
                        half sampleOpacity = 1.0h - exp2(-opticalDepth * 1.442695h);
                        half powder = 1.0h - exp2(-density * 2.6h);
                        half3 skyLight = lerp(_UndersideColor.rgb, _AmbientColor.rgb, lightTransmittance);
                        half3 directLight = _LitColor.rgb * lightTransmittance * phase;
                        half3 scattering = lerp(skyLight, directLight, saturate(0.30h + powder * 0.48h));
                        accumulated += transmittance * scattering * sampleOpacity;
                        transmittance *= 1.0h - sampleOpacity;
                    }
                    sampleDistance += stepLength;
                }

                half opacity = saturate((1.0h - transmittance) * _KBDUCloudExteriorOpacity);
                if (opacity < 0.002h)
                    discard;
                return fixed4(accumulated * _KBDUCloudExteriorOpacity, opacity);
            }
            ENDCG
        }
    }
    Fallback Off
}
