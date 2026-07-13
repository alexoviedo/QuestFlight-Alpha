Shader "QuestFlightLab/Production Stable Water"
{
    Properties
    {
        [HideInInspector] _MainTex ("Compatibility", 2D) = "white" {}
        _Color ("Deep water color", Color) = (0.16,0.35,0.43,1)
        _ShoreColor ("Shallow shore color", Color) = (0.30,0.48,0.47,1)
        _HorizonColor ("Grazing-angle color", Color) = (0.53,0.66,0.69,1)
        _BasinCenterExtent ("Basin center XZ / extent XZ", Vector) = (249.5625,4307.6875,1062.5625,857.1875)
        _DepthContrast ("Static basin depth contrast", Range(0,1)) = 0.72
        _SurfaceVariation ("Static surface color variation", Range(0,0.25)) = 0.14
        _GlintStrength ("Sun glint strength", Range(0,1)) = 0.42
        _Roughness ("Roughness", Range(0,1)) = 0.46
        _RippleStrength ("Static ripple strength", Range(0,1)) = 0.72
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+5" }
        LOD 90
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

            fixed4 _Color;
            fixed4 _ShoreColor;
            fixed4 _HorizonColor;
            float4 _BasinCenterExtent;
            half _DepthContrast;
            half _SurfaceVariation;
            half _GlintStrength;
            half _Roughness;
            half _RippleStrength;

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
                // Two static, world-space wave fields break the flat highlight without UV motion,
                // textures, screen-space reflection, or camera-relative state.
                half2 firstWave = half2(
                    sin(input.worldPosition.x * 0.030 + input.worldPosition.z * 0.019),
                    cos(input.worldPosition.x * 0.023 - input.worldPosition.z * 0.033));
                half2 secondWave = half2(
                    cos(input.worldPosition.x * 0.011 - input.worldPosition.z * 0.027),
                    sin(input.worldPosition.x * 0.037 + input.worldPosition.z * 0.013));
                half2 slope = (firstWave * 0.052 + secondWave * 0.027) * _RippleStrength;
                half3 rippleNormal = normalize(half3(-slope.x, 1.0, -slope.y));
                half3 normal = normalize(lerp(normalize(input.worldNormal), rippleNormal, 0.82));
                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half3 viewDirection = normalize(_WorldSpaceCameraPos - input.worldPosition);
                half3 halfDirection = normalize(lightDirection + viewDirection);
                half diffuse = saturate(dot(normal, lightDirection));
                half fresnel = pow(1.0 - saturate(dot(normal, viewDirection)), 3.0);
                half sunAlignment = saturate(dot(normal, halfDirection));
                half tightGlint = pow(sunAlignment, lerp(86.0, 22.0, _Roughness));
                half broadGlint = pow(sunAlignment, lerp(18.0, 6.0, _Roughness));
                half specular = (tightGlint * 0.72 + broadGlint * 0.18) *
                    (1.0 - _Roughness) * _GlintStrength;
                half broadVariation = 0.5 + 0.5 * sin(input.worldPosition.x * 0.0019 + input.worldPosition.z * 0.0027);
                float2 basinCoordinate =
                    (input.worldPosition.xz - _BasinCenterExtent.xy) /
                    max(_BasinCenterExtent.zw, float2(1.0, 1.0));
                half basinDepth = 1.0 - smoothstep(0.18, 1.02, length(basinCoordinate));
                basinDepth = saturate(basinDepth + (broadVariation - 0.5) * 0.10);
                half depthBlend = lerp(0.34, basinDepth, _DepthContrast);

                // Fixed world-space bands read as wind texture and depth variation from altitude;
                // they do not animate, use screen-space data, or depend on camera position.
                half rippleBands = 0.5 +
                    0.25 * sin(input.worldPosition.x * 0.041 + input.worldPosition.z * 0.026) +
                    0.25 * sin(input.worldPosition.x * 0.017 - input.worldPosition.z * 0.053);
                fixed3 baseColor = lerp(_ShoreColor.rgb, _Color.rgb, depthBlend);
                baseColor *= lerp(1.0 - _SurfaceVariation, 1.0 + _SurfaceVariation, saturate(rippleBands));
                half skyResponse = saturate(fresnel * 0.58 + (1.0 - normal.y) * 0.34);
                baseColor = lerp(baseColor, _HorizonColor.rgb, skyResponse);
                half3 ambient = ShadeSH9(half4(normal, 1.0));
                fixed3 color = baseColor * (ambient + _LightColor0.rgb * diffuse * 0.72) +
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
