Shader "QuestFlight/KBDU Terrain Pyramid"
{
    Properties
    {
        _NearAlbedo ("Near 4 km NAIP", 2D) = "gray" {}
        _MidAlbedo ("Mid 12 km NAIP", 2D) = "gray" {}
        _FarAlbedo ("Far 24 km NAIP", 2D) = "gray" {}
        _DetailAlbedo ("CC0 near/mid microdetail", 2D) = "white" {}
        _NearWorldMinInvSize ("Near min XZ / inverse size", Vector) = (-2000,-2000,0.00025,0.00025)
        _MidWorldMinInvSize ("Mid min XZ / inverse size", Vector) = (-6000,-6000,0.0000833333,0.0000833333)
        _FarWorldMinInvSize ("Far min XZ / inverse size", Vector) = (-12000,-12000,0.0000416667,0.0000416667)
        _NearBlendStartEnd ("Near to mid overlap", Vector) = (1650,2000,0,0)
        _MidBlendStartEnd ("Mid to far overlap", Vector) = (5000,6000,0,0)
        _DetailScale ("Detail world scale", Float) = 0.0666667
        _DetailFadeStartEnd ("Detail camera fade", Vector) = (180,1500,0,0)
        _Tint ("Shared terrain tint", Color) = (1,1,1,1)
        _Roughness ("Roughness", Range(0,1)) = 0.92
        [Enum(Off,0,MacroLevel,1,BlendWeight,2,TexelDensityAndBoundaries,3)]
        _DiagnosticMode ("Terrain diagnostic", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
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
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _NearAlbedo;
            sampler2D _MidAlbedo;
            sampler2D _FarAlbedo;
            sampler2D _DetailAlbedo;
            float4 _NearWorldMinInvSize;
            float4 _MidWorldMinInvSize;
            float4 _FarWorldMinInvSize;
            float4 _NearBlendStartEnd;
            float4 _MidBlendStartEnd;
            float4 _DetailFadeStartEnd;
            fixed4 _Tint;
            half _DetailScale;
            half _Roughness;
            half _DiagnosticMode;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                half3 worldNormal : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            half BoundaryLine(half radius, half boundary)
            {
                half width = max(4.0, fwidth(radius) * 1.5);
                return 1.0 - smoothstep(width, width * 2.0, abs(radius - boundary));
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 world = input.worldPosition.xz;
                float2 nearUv = saturate((world - _NearWorldMinInvSize.xy) * _NearWorldMinInvSize.zw);
                float2 midUv = saturate((world - _MidWorldMinInvSize.xy) * _MidWorldMinInvSize.zw);
                float2 farUv = saturate((world - _FarWorldMinInvSize.xy) * _FarWorldMinInvSize.zw);
                fixed3 nearColor = tex2D(_NearAlbedo, nearUv).rgb;
                fixed3 midColor = tex2D(_MidAlbedo, midUv).rgb;
                fixed3 farColor = tex2D(_FarAlbedo, farUv).rgb;
                half radius = max(abs(world.x), abs(world.y));
                half nearWeight = 1.0 - smoothstep(_NearBlendStartEnd.x, _NearBlendStartEnd.y, radius);
                half midWeight = 1.0 - smoothstep(_MidBlendStartEnd.x, _MidBlendStartEnd.y, radius);
                fixed3 macro = lerp(farColor, midColor, midWeight);
                macro = lerp(macro, nearColor, nearWeight);

                fixed3 detailSample = tex2D(_DetailAlbedo, world * _DetailScale).rgb;
                half detailLuminance = max(0.12, dot(detailSample, fixed3(0.2126, 0.7152, 0.0722)));
                fixed3 detailModulation = lerp(fixed3(1,1,1), detailSample / detailLuminance, 0.15);
                half cameraDistance = distance(_WorldSpaceCameraPos, input.worldPosition);
                half detailWeight = 1.0 - smoothstep(
                    _DetailFadeStartEnd.x, _DetailFadeStartEnd.y, cameraDistance);
                detailWeight *= 1.0 - smoothstep(4200.0, 5600.0, radius);
                fixed3 albedo = macro * _Tint.rgb * lerp(fixed3(1,1,1), detailModulation, detailWeight);

                if (_DiagnosticMode > 0.5)
                {
                    fixed3 levelColor = lerp(fixed3(0.12,0.32,0.78), fixed3(0.94,0.58,0.08), midWeight);
                    levelColor = lerp(levelColor, fixed3(0.08,0.72,0.30), nearWeight);
                    if (_DiagnosticMode < 1.5)
                        return fixed4(levelColor, 1.0);
                    half overlap = max(4.0 * nearWeight * (1.0 - nearWeight), 4.0 * midWeight * (1.0 - midWeight));
                    if (_DiagnosticMode < 2.5)
                        return fixed4(lerp(fixed3(0.03,0.03,0.03), fixed3(1.0,0.12,0.02), overlap), 1.0);
                    half boundary = max(
                        max(BoundaryLine(radius, _NearBlendStartEnd.x), BoundaryLine(radius, _NearBlendStartEnd.y)),
                        max(BoundaryLine(radius, _MidBlendStartEnd.x), BoundaryLine(radius, _MidBlendStartEnd.y)));
                    return fixed4(lerp(levelColor * 0.62, fixed3(1,1,1), boundary), 1.0);
                }

                half3 normal = normalize(input.worldNormal);
                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half diffuse = saturate(dot(normal, lightDirection));
                half3 ambient = ShadeSH9(half4(normal, 1.0));
                half3 viewDirection = normalize(_WorldSpaceCameraPos - input.worldPosition);
                half3 halfDirection = normalize(lightDirection + viewDirection);
                half specular = pow(saturate(dot(normal, halfDirection)), lerp(28.0, 5.0, _Roughness)) *
                    (1.0 - _Roughness) * 0.045;
                fixed4 output = fixed4(
                    albedo * (ambient + _LightColor0.rgb * (0.12 + diffuse * 0.88)) + _LightColor0.rgb * specular,
                    1.0);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
