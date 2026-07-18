Shader "QuestFlight/AirportCoreV4/ContinuousRunway"
{
    Properties
    {
        _MainTex ("Worn Asphalt Albedo", 2D) = "white" {}
        _NormalTex ("Aggregate Normal", 2D) = "bump" {}
        _MRAOTex ("Metallic / Roughness / AO", 2D) = "white" {}
        _FeatureMask ("Rubber / Repair / Crack / Edge", 2D) = "black" {}
        _Tint ("Orthophoto Match Tint", Color) = (1,1,1,1)
        _WorldTiling ("World Tiling Per Meter", Float) = 0.08
        _SecondaryTiling ("Secondary Tiling Per Meter", Float) = 0.027
        _NormalStrength ("Normal Strength", Range(0,1)) = 0.62
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 150
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
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            sampler2D _NormalTex;
            sampler2D _MRAOTex;
            sampler2D _FeatureMask;
            fixed4 _Tint;
            half _WorldTiling;
            half _SecondaryTiling;
            half _NormalStrength;

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
                float2 meshUv : TEXCOORD0;
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
                output.meshUv = input.uv;
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
                float2 primaryUv = input.worldPosition.xz * _WorldTiling;
                float2 secondaryUv = float2(
                    input.worldPosition.x * 0.8387 - input.worldPosition.z * 0.5446,
                    input.worldPosition.x * 0.5446 + input.worldPosition.z * 0.8387) * _SecondaryTiling;
                fixed3 primary = tex2D(_MainTex, primaryUv).rgb;
                fixed3 secondary = tex2D(_MainTex, secondaryUv).rgb;
                half variation = dot(secondary, half3(0.2126, 0.7152, 0.0722)) - 0.5;
                half4 feature = tex2D(_FeatureMask, input.meshUv);
                fixed3 albedo = saturate(primary * (1.0 + variation * 0.12)) * _Tint.rgb;
                albedo *= 1.0 - feature.r * 0.12 - feature.g * 0.04 - feature.b * 0.15;

                half3 mrao = tex2D(_MRAOTex, primaryUv).rgb;
                half roughness = saturate(mrao.g - feature.r * 0.10 + feature.b * 0.07 + feature.a * 0.05);
                half ao = lerp(0.72, 1.0, mrao.b);
                half3 tangentNormal = UnpackNormal(tex2D(_NormalTex, primaryUv));
                tangentNormal.xy *= _NormalStrength;
                tangentNormal = normalize(tangentNormal);
                half3 normal = normalize(input.worldNormal);
                UNITY_BRANCH if (dot(input.worldTangent, input.worldTangent) > 0.1)
                {
                    normal = normalize(
                        input.worldTangent * tangentNormal.x +
                        input.worldBitangent * tangentNormal.y +
                        input.worldNormal * tangentNormal.z);
                }

                half3 irradiance;
                #if defined(LIGHTMAP_ON)
                    irradiance = DecodeLightmap(UNITY_SAMPLE_TEX2D(unity_Lightmap, input.lightmapUv));
                #else
                    half ndotl = saturate(dot(normal, normalize(_WorldSpaceLightPos0.xyz)));
                    irradiance = ShadeSH9(half4(normal, 1.0)) + _LightColor0.rgb * (0.08 + ndotl * 0.92);
                #endif
                half3 color = albedo * irradiance * ao;
                half3 viewDirection = normalize(_WorldSpaceCameraPos - input.worldPosition);
                half3 halfDirection = normalize(normalize(_WorldSpaceLightPos0.xyz) + viewDirection);
                half specular = pow(saturate(dot(normal, halfDirection)), lerp(42.0, 5.0, roughness));
                color += _LightColor0.rgb * specular * (1.0 - roughness) * 0.045;
                fixed4 output = fixed4(color, 1.0);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
