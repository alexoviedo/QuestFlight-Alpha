Shader "QuestFlightLab/Production Far Terrain"
{
    Properties
    {
        _LowColor ("Low elevation prairie", Color) = (0.29,0.31,0.20,1)
        _MidColor ("Mid elevation dry grass", Color) = (0.39,0.35,0.23,1)
        _HighColor ("High elevation rock", Color) = (0.43,0.42,0.37,1)
        _RockColor ("Steep slope rock", Color) = (0.34,0.35,0.34,1)
        _ElevationRange ("Local min/max elevation", Vector) = (-110,980,0,0)
        _VariationScale ("World variation scale", Float) = 0.00072
        _Roughness ("Roughness", Range(0,1)) = 0.91
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 70
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _LowColor;
            fixed4 _MidColor;
            fixed4 _HighColor;
            fixed4 _RockColor;
            float4 _ElevationRange;
            half _VariationScale;
            half _Roughness;

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

            fixed4 frag(v2f input) : SV_Target
            {
                half3 normal = normalize(input.worldNormal);
                half height = saturate((input.worldPosition.y - _ElevationRange.x) /
                    max(1.0, _ElevationRange.y - _ElevationRange.x));
                half middleWeight = saturate(1.0 - abs(height - 0.48) * 2.25);
                fixed3 elevationColor = lerp(_LowColor.rgb, _HighColor.rgb, height);
                elevationColor = lerp(elevationColor, _MidColor.rgb, middleWeight * 0.58);
                half slope = 1.0 - saturate(normal.y);
                half rockWeight = smoothstep(0.18, 0.66, slope) * smoothstep(0.12, 0.76, height);
                fixed3 albedo = lerp(elevationColor, _RockColor.rgb, rockWeight * 0.72);

                // Broad, static world variation gives the real DEM readable planes without
                // a camera-relative effect or another far-zone texture allocation.
                half variation = 0.92 + 0.045 * sin(
                    (input.worldPosition.x + input.worldPosition.z * 0.73) * _VariationScale) +
                    0.035 * cos(
                    (input.worldPosition.x * 0.41 - input.worldPosition.z) * _VariationScale * 1.37);
                albedo *= variation;

                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half diffuse = saturate(dot(normal, lightDirection));
                half3 ambient = max(ShadeSH9(half4(normal, 1.0)), half3(0.20, 0.21, 0.22));
                half3 viewDirection = normalize(_WorldSpaceCameraPos - input.worldPosition);
                half3 halfDirection = normalize(lightDirection + viewDirection);
                half specular = pow(saturate(dot(normal, halfDirection)), lerp(24.0, 5.0, _Roughness)) *
                    (1.0 - _Roughness) * 0.025;
                fixed3 color = albedo * (ambient + _LightColor0.rgb * (0.18 + diffuse * 0.82)) +
                    _LightColor0.rgb * specular;
                fixed4 output = fixed4(color, 1.0);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
