Shader "QuestFlight/AirportCoreV4/BakedLit"
{
    Properties
    {
        _MainTex ("Shared Albedo Atlas", 2D) = "white" {}
        _NormalTex ("Shared Normal Atlas", 2D) = "bump" {}
        _MRAOTex ("Metallic / Roughness / AO", 2D) = "white" {}
        _Tint ("Orthophoto Match Tint", Color) = (1,1,1,1)
        _NormalStrength ("Normal Strength", Range(0,1)) = 0.72
        _SpecularStrength ("Specular Strength", Range(0,1)) = 0.42
        _ReflectionStrength ("Reflection Strength", Range(0,1)) = 0.34
        _MetricTerminalWalls ("Terminal Masonry and Cladding in Metres", Float) = 0
        _MasonryTileMeters ("Masonry Atlas Tile Width in Metres", Float) = 1.4
        _CladdingTileMeters ("Cladding Atlas Tile Width in Metres", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 150
        Cull Back
        ZWrite On
        ZTest LEqual

        CGINCLUDE
        float _MetricTerminalWalls;
        float _MasonryTileMeters;
        float _CladdingTileMeters;

        // Opt-in for the terminal only. The source atlas assigns masonry to tile0
        // and vertical cladding to tile1. All roof, glazing, sign, trim and prop
        // tiles retain their authored coordinates. A single dominant wall plane
        // avoids triplanar blending and its additional texture samples.
        float2 TerminalWallUv(float2 atlasUv, float3 worldPosition, float3 worldNormal,
            out float2 unwrappedUv, out float3 wallTangent, out float metricWall)
        {
            unwrappedUv = atlasUv;
            wallTangent = float3(1, 0, 0);
            metricWall = 0;
            float2 tile = floor(atlasUv * 4.0);
            if (_MetricTerminalWalls < 0.5 || abs(worldNormal.y) > 0.7 ||
                tile.y != 0 || tile.x < 0 || tile.x > 1)
                return atlasUv;

            metricWall = 1;
            wallTangent = abs(worldNormal.x) > abs(worldNormal.z)
                ? float3(0, 0, worldNormal.x > 0 ? -1 : 1)
                : float3(worldNormal.z > 0 ? 1 : -1, 0, 0);
            float meters = tile.x < 0.5 ? _MasonryTileMeters : _CladdingTileMeters;
            float2 repeatUv = float2(dot(worldPosition, wallTangent), worldPosition.y) / max(meters, 0.25);
            // Match the source generator's 0.012 atlas gutter. Derivatives are
            // taken BEFORE frac so tile seams do not request unrelated coarse mips.
            unwrappedUv = repeatUv * 0.226;
            return tile * 0.25 + 0.012 + frac(repeatUv) * 0.226;
        }
        ENDCG

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            sampler2D _NormalTex;
            sampler2D _MRAOTex;
            fixed4 _Tint;
            half _NormalStrength;
            half _SpecularStrength;
            half _ReflectionStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                half4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 worldNormal : TEXCOORD1;
                half3 worldTangent : TEXCOORD2;
                half3 worldBitangent : TEXCOORD3;
                float3 worldPosition : TEXCOORD4;
                float2 lightmapUv : TEXCOORD5;
                UNITY_FOG_COORDS(6)
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
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.worldTangent = UnityObjectToWorldDir(input.tangent.xyz);
                output.worldBitangent = cross(output.worldNormal, output.worldTangent) * input.tangent.w;
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.lightmapUv = input.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 unwrappedUv;
                float3 wallTangent;
                float metricWall;
                float2 sampleUv = TerminalWallUv(input.uv, input.worldPosition, input.worldNormal,
                    unwrappedUv, wallTangent, metricWall);
                float2 atlasDx = ddx(unwrappedUv), atlasDy = ddy(unwrappedUv);
                fixed3 albedo = tex2Dgrad(_MainTex, sampleUv, atlasDx, atlasDy).rgb * _Tint.rgb;
                half3 mrao = tex2Dgrad(_MRAOTex, sampleUv, atlasDx, atlasDy).rgb;
                half metallic = saturate(mrao.r);
                half roughness = saturate(mrao.g);
                half ao = lerp(0.58, 1.0, mrao.b);

                half3 normal = normalize(input.worldNormal);
                half3 tangentNormal = UnpackNormal(tex2Dgrad(_NormalTex, sampleUv, atlasDx, atlasDy));
                tangentNormal.xy *= _NormalStrength;
                tangentNormal = normalize(tangentNormal);
                UNITY_BRANCH if (metricWall > 0.5)
                {
                    half3 tangent = normalize(wallTangent - normal * dot(normal, wallTangent));
                    half3 bitangent = cross(normal, tangent);
                    normal = normalize(tangent * tangentNormal.x + bitangent * tangentNormal.y + normal * tangentNormal.z);
                }
                else if (dot(input.worldTangent, input.worldTangent) > 0.1)
                {
                    normal = normalize(
                        input.worldTangent * tangentNormal.x +
                        input.worldBitangent * tangentNormal.y +
                        input.worldNormal * tangentNormal.z);
                }

                half3 bakedOrAmbient;
                half3 realtimeDiffuse = 0;
                #if defined(LIGHTMAP_ON)
                    half4 encodedLightmap = UNITY_SAMPLE_TEX2D(unity_Lightmap, input.lightmapUv);
                    bakedOrAmbient = DecodeLightmap(encodedLightmap);
                    #if defined(DIRLIGHTMAP_COMBINED)
                        half4 encodedDirection = UNITY_SAMPLE_TEX2D_SAMPLER(unity_LightmapInd, unity_Lightmap, input.lightmapUv);
                        bakedOrAmbient = DecodeDirectionalLightmap(bakedOrAmbient, encodedDirection, normal);
                    #endif
                #else
                    bakedOrAmbient = max(ShadeSH9(half4(normal, 1.0)), 0.0h) * 0.35h;
                    half ndotl = saturate(dot(normal, normalize(_WorldSpaceLightPos0.xyz)));
                    realtimeDiffuse = _LightColor0.rgb * (0.08h + ndotl * 0.55h);
                #endif

                half3 viewDirection = normalize(_WorldSpaceCameraPos - input.worldPosition);
                half3 reflectionDirection = reflect(-viewDirection, normal);
                half mip = roughness * 6.0;
                half4 encodedReflection = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, reflectionDirection, mip);
                half3 reflection = DecodeHDR(encodedReflection, unity_SpecCube0_HDR);
                half fresnel = pow(1.0 - saturate(dot(normal, viewDirection)), 5.0);
                half reflectance = lerp(0.04, 0.34, metallic) + fresnel * (1.0 - roughness) * 0.35;
                half3 color = albedo * (bakedOrAmbient * ao + realtimeDiffuse);
                color += reflection * reflectance * (1.0 - roughness * 0.72) * _ReflectionStrength;

                #if !defined(LIGHTMAP_ON)
                    half3 halfDirection = normalize(normalize(_WorldSpaceLightPos0.xyz) + viewDirection);
                    half specular = pow(saturate(dot(normal, halfDirection)), lerp(64.0, 5.0, roughness));
                    color += _LightColor0.rgb * specular * _SpecularStrength * (1.0 - roughness * 0.7);
                #endif
                fixed4 output = fixed4(color, 1.0);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }

        Pass
        {
            Tags { "LightMode"="Meta" }
            Cull Off
            CGPROGRAM
            #pragma vertex vertMeta
            #pragma fragment fragMeta
            #pragma target 3.0
            #pragma shader_feature EDITOR_VISUALIZATION
            #include "UnityCG.cginc"
            #include "UnityMetaPass.cginc"

            sampler2D _MainTex;
            fixed4 _Tint;

            struct appdata_meta_custom
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
            };

            struct v2f_meta_custom
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
            };

            v2f_meta_custom vertMeta(appdata_meta_custom input)
            {
                v2f_meta_custom output;
                output.position = UnityMetaVertexPosition(input.vertex, input.uv1, input.uv2,
                    unity_LightmapST, unity_DynamicLightmapST);
                output.uv = input.uv;
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                return output;
            }

            fixed4 fragMeta(v2f_meta_custom input) : SV_Target
            {
                UnityMetaInput meta;
                UNITY_INITIALIZE_OUTPUT(UnityMetaInput, meta);
                float2 unwrappedUv;
                float3 wallTangent;
                float metricWall;
                float2 sampleUv = TerminalWallUv(input.uv, input.worldPosition, input.worldNormal,
                    unwrappedUv, wallTangent, metricWall);
                meta.Albedo = tex2Dgrad(_MainTex, sampleUv, ddx(unwrappedUv), ddy(unwrappedUv)).rgb * _Tint.rgb;
                meta.Emission = 0;
                return UnityMetaFragment(meta);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
