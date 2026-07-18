Shader "QuestFlight/AirportCoreV4/ContinuousRunway"
{
    Properties
    {
        _MainTex ("Worn Asphalt Albedo", 2D) = "white" {}
        _NormalTex ("Aggregate Normal", 2D) = "bump" {}
        _MRAOTex ("Metallic / Roughness / AO", 2D) = "white" {}
        _Tint ("Orthophoto Match Tint", Color) = (1,1,1,1)
        _RunwayOrigin ("Runway 08 origin XZ", Vector) = (-629.757,-30.671,0,0)
        _RunwayAxis ("Runway longitudinal axis XZ", Vector) = (1,0,0,0)
        _RunwayLengthWidth ("Runway length / width", Vector) = (1250.385,22.86,0,0)
        _WorldTiling ("Fine aggregate tiling per meter", Float) = 0.09
        _SecondaryTiling ("Rotated detail tiling per meter", Float) = 0.031
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
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            sampler2D _NormalTex;
            sampler2D _MRAOTex;
            fixed4 _Tint;
            float4 _RunwayOrigin;
            float4 _RunwayAxis;
            float4 _RunwayLengthWidth;
            half _WorldTiling;
            half _SecondaryTiling;
            half _NormalStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                half4 tangent : TANGENT;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                half3 worldNormal : TEXCOORD0;
                half3 worldTangent : TEXCOORD1;
                half3 worldBitangent : TEXCOORD2;
                float3 worldPosition : TEXCOORD3;
                UNITY_FOG_COORDS(4)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.worldTangent = UnityObjectToWorldDir(input.tangent.xyz);
                output.worldBitangent = cross(output.worldNormal, output.worldTangent) * input.tangent.w;
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 axis = normalize(_RunwayAxis.xy);
                float2 lateralAxis = float2(-axis.y, axis.x);
                float2 from08 = input.worldPosition.xz - _RunwayOrigin.xy;
                float longitudinal = dot(from08, axis);
                float lateral = dot(from08, lateralAxis);
                float normalizedLongitudinal = saturate(longitudinal / max(_RunwayLengthWidth.x, 1.0));
                float normalizedLateral = lateral / max(_RunwayLengthWidth.y, 1.0);

                float2 runwayMeters = float2(longitudinal, lateral);
                float2 primaryUv = runwayMeters * _WorldTiling;
                float2 secondaryUv = float2(
                    runwayMeters.x * 0.8387 - runwayMeters.y * 0.5446,
                    runwayMeters.x * 0.5446 + runwayMeters.y * 0.8387) * _SecondaryTiling;
                fixed3 primary = tex2D(_MainTex, primaryUv).rgb;
                fixed3 secondary = tex2D(_MainTex, secondaryUv).rgb;

                // Treat the photographed asphalt as fine aggregate, not a repeated macro tile.
                // The low-frequency runway character is analytic in one continuous runway-local
                // frame, so mesh/lightmap chart boundaries cannot create slabs.
                half primaryLuma = max(dot(primary, fixed3(0.2126h,0.7152h,0.0722h)), 0.08h);
                half secondaryLuma = max(dot(secondary, fixed3(0.2126h,0.7152h,0.0722h)), 0.08h);
                half3 aggregate = lerp(primary / primaryLuma, secondary / secondaryLuma, 0.37h);
                half macro = sin(longitudinal * 0.0037h + sin(lateral * 0.071h) * 0.73h) * 0.5h +
                    sin(longitudinal * 0.0089h - lateral * 0.043h + 1.71h) * 0.28h;
                half3 albedo = half3(0.285h, 0.278h, 0.258h) * (1.0h + macro * 0.055h);
                albedo *= lerp(half3(1,1,1), aggregate, 0.16h) * _Tint.rgb;

                half centerWeight = 1.0h - smoothstep(0.10h, 0.44h, abs(normalizedLateral));
                half touchdown08 = 1.0h - smoothstep(0.0h, 0.13h, abs(normalizedLongitudinal - 0.18h));
                half touchdown26 = 1.0h - smoothstep(0.0h, 0.13h, abs(normalizedLongitudinal - 0.82h));
                half rubber = centerWeight * max(touchdown08, touchdown26) * 0.20h;
                half patchField = sin(longitudinal * 0.021h + sin(lateral * 0.31h) * 1.9h) *
                    sin(longitudinal * 0.013h - lateral * 0.17h + 0.8h);
                half repairs = smoothstep(0.86h, 0.97h, patchField) * 0.07h;
                half sparseJoint = 1.0h - smoothstep(0.018h, 0.055h,
                    abs(sin(longitudinal * 0.0127h + sin(longitudinal * 0.0031h) * 0.7h)));
                sparseJoint *= smoothstep(0.78h, 0.96h,
                    sin(longitudinal * 0.0049h + lateral * 0.091h) * 0.5h + 0.5h) * 0.08h;
                half edgeDirt = smoothstep(0.34h, 0.49h, abs(normalizedLateral));
                albedo *= 1.0h - rubber - sparseJoint;
                albedo = lerp(albedo, albedo * half3(0.86h,0.83h,0.75h), edgeDirt * 0.24h);
                albedo = lerp(albedo, albedo * 0.93h, repairs);

                half3 mrao = tex2D(_MRAOTex, primaryUv).rgb;
                half roughness = saturate(lerp(0.74h, mrao.g, 0.24h) - rubber * 0.20h +
                    repairs * 0.08h + edgeDirt * 0.08h);
                half ao = lerp(0.72, 1.0, mrao.b);
                half3 tangentNormal = UnpackNormal(tex2D(_NormalTex, primaryUv));
                tangentNormal.xy *= _NormalStrength;
                tangentNormal = normalize(tangentNormal);
                // The authoritative runway is grade-following and segmented. Its legacy
                // per-segment vertex normals encode tiny slope changes that become giant
                // alternating illumination slabs under the low sun. Shade the continuous
                // pavement in one runway-local tangent frame while retaining the exact
                // geometry, collider, markings, and world-space aggregate detail.
                half3 runwayTangent = normalize(half3(axis.x, 0.0h, axis.y));
                half3 runwayBitangent = normalize(half3(lateralAxis.x, 0.0h, lateralAxis.y));
                half3 runwayUp = half3(0.0h, 1.0h, 0.0h);
                half3 normal = normalize(
                    runwayTangent * tangentNormal.x +
                    runwayBitangent * tangentNormal.y +
                    runwayUp * tangentNormal.z);

                half ndotl = saturate(dot(normal, normalize(_WorldSpaceLightPos0.xyz)));
                half3 irradiance = saturate(max(ShadeSH9(half4(normal, 1.0)), 0.0h) * 0.35h +
                    _LightColor0.rgb * (0.08h + ndotl * 0.55h));
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
