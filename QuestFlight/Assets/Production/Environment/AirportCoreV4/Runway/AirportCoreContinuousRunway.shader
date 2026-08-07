Shader "QuestFlight/AirportCoreV4/ContinuousRunway"
{
    Properties
    {
        _MainTex ("Photoreal Asphalt Albedo", 2D) = "white" {}
        _NormalTex ("Aggregate Normal", 2D) = "bump" {}
        _MRAOTex ("Runway Wear / Roughness / Repair", 2D) = "white" {}
        _Tint ("Orthophoto Match Tint", Color) = (1,1,1,1)
        _RunwayOrigin ("Runway 08 origin XZ", Vector) = (-629.757,-30.671,0,0)
        _RunwayAxis ("Runway longitudinal axis XZ", Vector) = (1,0,0,0)
        _RunwayLengthWidth ("Runway length / width", Vector) = (1250.385,22.86,0,0)
        _WorldTiling ("Fine aggregate tiling per meter", Float) = 0.18
        _SecondaryTiling ("Rotated detail tiling per meter", Float) = 0.047
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
            // The authoritative pavement and terrain intentionally share the surveyed grade.
            // A bounded depth bias keeps the single pavement draw visually authoritative while
            // leaving the serialized mesh, collider, reset surface, and marking offsets unchanged.
            Offset -1, -1
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
                float2 macroUv = float2(
                    normalizedLongitudinal,
                    saturate(normalizedLateral + 0.5));

                float2 runwayMeters = float2(longitudinal, lateral);
                float2 primaryUv = runwayMeters * _WorldTiling;
                float2 secondaryUv = float2(
                    runwayMeters.x * 0.8387 - runwayMeters.y * 0.5446,
                    runwayMeters.x * 0.5446 + runwayMeters.y * 0.8387) * _SecondaryTiling;
                fixed3 primary = tex2D(_MainTex, primaryUv).rgb;
                fixed3 secondary = tex2D(_MainTex, secondaryUv).rgb;

                // Preserve the photographic aggregate signal. Centering both
                // scales around the measured source mean prevents either tile
                // from shifting runway exposure while the rotated secondary
                // sample suppresses visible repetition.
                const half3 sourceMean = half3(0.4068h, 0.4049h, 0.4033h);
                half3 aggregate = sourceMean +
                    (primary - sourceMean) * 0.74h +
                    (secondary - sourceMean) * 0.26h;
                half macro = sin(longitudinal * 0.0037h + sin(lateral * 0.071h) * 0.73h) * 0.5h +
                    sin(longitudinal * 0.0089h - lateral * 0.043h + 1.71h) * 0.28h;
                half3 albedo = aggregate * (1.0h + macro * 0.022h) * _Tint.rgb;

                // This compact, runway-length map is sampled once in the
                // unique local frame: R tire polish, G roughness, B repairs and
                // edge weathering. It replaces the former repeating analytic
                // patch/joint waves without adding a texture or material.
                half3 runwaySurface = tex2D(_MRAOTex, macroUv).rgb;
                half tirePolish = runwaySurface.r;
                half roughness = runwaySurface.g;
                half repairs = runwaySurface.b;
                albedo *= 1.0h - tirePolish * 0.105h;
                albedo = lerp(
                    albedo,
                    albedo * half3(0.86h, 0.85h, 0.82h),
                    repairs * 0.18h);
                half3 tangentNormal = UnpackNormal(tex2D(_NormalTex, primaryUv));
                tangentNormal.xy *= _NormalStrength;
                tangentNormal = normalize(tangentNormal);
                // Shade the grade-following pavement in one continuous runway-local tangent
                // frame while retaining the exact geometry, collider, markings, and detail.
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
                half3 color = albedo * irradiance;
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
