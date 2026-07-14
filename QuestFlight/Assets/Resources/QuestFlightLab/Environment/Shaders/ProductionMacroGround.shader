Shader "QuestFlightLab/Production Macro Ground"
{
    Properties
    {
        [HideInInspector] _MainTex ("Compatibility", 2D) = "white" {}
        _MacroAlbedo ("Unique 12 km macro albedo", 2D) = "gray" {}
        _NearOrthophoto ("Unique 4 km orthophoto", 2D) = "gray" {}
        _DryGrassTex ("Dry grass CC0", 2D) = "white" {}
        _GreenGrassTex ("Sparse grass CC0", 2D) = "white" {}
        _SoilTex ("Dry soil CC0", 2D) = "white" {}
        _Tint ("Land-cover tint", Color) = (1,1,1,1)
        _MacroWorldMinInvSize ("Macro min XZ / inverse size", Vector) = (-6000,-6000,0.0000833333,0.0000833333)
        _NearWorldMinInvSize ("Near min XZ / inverse size", Vector) = (-2000,-2000,0.00025,0.00025)
        _NearBlendStartEnd ("Near orthophoto blend start/end", Vector) = (1750,2000,0,0)
        _OrthophotoEnabled ("Orthophoto enabled", Range(0,1)) = 0
        _MacroInfluence ("Macro influence", Range(0,1)) = 1
        _MacroBrightness ("Macro brightness", Range(0.75,1.35)) = 1
        _MacroSaturation ("Macro saturation", Range(0.5,1.6)) = 1
        _RegionalContrast ("Regional contrast", Range(0.5,1.8)) = 1
        _LocalContrast ("Local contrast", Range(0.5,1.8)) = 1
        _MicroScale ("Micro world scale", Float) = 0.0666667
        _GreenBlend ("Green blend", Range(0,1)) = 0.20
        _SoilBlend ("Soil blend", Range(0,1)) = 0.18
        _DetailFadeStart ("Detail fade start", Float) = 280
        _DetailFadeEnd ("Detail fade end", Float) = 1800
        _Roughness ("Roughness", Range(0,1)) = 0.9
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

            sampler2D _MacroAlbedo;
            sampler2D _NearOrthophoto;
            sampler2D _DryGrassTex;
            sampler2D _GreenGrassTex;
            sampler2D _SoilTex;
            fixed4 _Tint;
            float4 _MacroWorldMinInvSize;
            float4 _NearWorldMinInvSize;
            float4 _NearBlendStartEnd;
            half _OrthophotoEnabled;
            half _MacroInfluence;
            half _MacroBrightness;
            half _MacroSaturation;
            half _RegionalContrast;
            half _LocalContrast;
            half _MicroScale;
            half _GreenBlend;
            half _SoilBlend;
            half _DetailFadeStart;
            half _DetailFadeEnd;
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

            float2 Hash22(float2 value)
            {
                value = float2(dot(value, float2(127.1, 311.7)), dot(value, float2(269.5, 183.3)));
                return frac(sin(value) * 43758.5453);
            }

            fixed3 SampleBombed(sampler2D source, float2 world, float scale, float seed)
            {
                const float cellMeters = 53.0;
                float2 cell = floor(world / cellMeters);
                float2 within = frac(world / cellMeters);
                float axis = within.x + within.y < 1.0 ? within.x + within.y : 2.0 - within.x - within.y;
                float2 neighbor = cell + (within.x > within.y ? float2(1, 0) : float2(0, 1));
                float2 firstHash = Hash22(cell + seed);
                float2 secondHash = Hash22(neighbor + seed);
                float2 firstUv = world * scale + firstHash * 19.37;
                float2 secondWorld = float2(-world.y, world.x);
                float2 secondUv = secondWorld * (scale * 0.937) + secondHash * 23.11;
                fixed3 first = tex2D(source, firstUv).rgb;
                fixed3 second = tex2D(source, secondUv).rgb;
                return lerp(first, second, smoothstep(0.26, 0.74, axis));
            }

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
                float2 macroUv = saturate((input.worldPosition.xz - _MacroWorldMinInvSize.xy) * _MacroWorldMinInvSize.zw);
                float2 nearUv = saturate((input.worldPosition.xz - _NearWorldMinInvSize.xy) * _NearWorldMinInvSize.zw);
                fixed3 midOrthophoto = tex2D(_MacroAlbedo, macroUv).rgb;
                fixed3 nearOrthophoto = tex2D(_NearOrthophoto, nearUv).rgb;
                half nearExtent = max(abs(input.worldPosition.x), abs(input.worldPosition.z));
                half nearWeight = (1.0 - smoothstep(
                    _NearBlendStartEnd.x,
                    max(_NearBlendStartEnd.x + 1.0, _NearBlendStartEnd.y),
                    nearExtent)) * _OrthophotoEnabled;
                fixed3 authoredMacro = lerp(midOrthophoto, nearOrthophoto, nearWeight);
                // The same unique, nonrepeating macro map is separated into 3 km, 375 m, and
                // native bands. This restores field/land-cover scale without another texture,
                // runtime tile, camera-relative lookup, or periodic procedural pattern.
                fixed3 globalMacro = tex2Dlod(_MacroAlbedo, float4(macroUv, 0, 8)).rgb;
                fixed3 regionalMacro = tex2Dlod(_MacroAlbedo, float4(macroUv, 0, 5)).rgb;
                fixed3 gradedMacro = globalMacro +
                    (regionalMacro - globalMacro) * _RegionalContrast +
                    (authoredMacro - regionalMacro) * _LocalContrast;
                gradedMacro = max(gradedMacro, fixed3(0.004, 0.004, 0.004));
                half gradedLuminance = dot(gradedMacro, fixed3(0.2126, 0.7152, 0.0722));
                gradedMacro = lerp(
                    fixed3(gradedLuminance, gradedLuminance, gradedLuminance),
                    gradedMacro,
                    _MacroSaturation) * _MacroBrightness;
                half proceduralMacro = 0.93 + 0.07 * sin(input.worldPosition.x * 0.0017 + input.worldPosition.z * 0.0023);
                fixed3 macro = lerp(fixed3(0.52, 0.53, 0.40) * proceduralMacro, gradedMacro, _MacroInfluence);

                fixed3 dry = SampleBombed(_DryGrassTex, input.worldPosition.xz, _MicroScale, 1.7);
                fixed3 green = SampleBombed(_GreenGrassTex, input.worldPosition.xz, _MicroScale * 0.91, 11.3);
                fixed3 soil = SampleBombed(_SoilTex, input.worldPosition.xz, _MicroScale * 1.09, 29.1);
                fixed3 micro = lerp(lerp(dry, green, _GreenBlend), soil, _SoilBlend);
                half luminance = max(0.12, dot(micro, fixed3(0.2126, 0.7152, 0.0722)));
                fixed3 microModulation = lerp(fixed3(1,1,1), micro / luminance, 0.18);
                half microValue = clamp(1.0 + (luminance - 0.22) * 0.55, 0.88, 1.12);
                half distanceToCamera = distance(_WorldSpaceCameraPos, input.worldPosition);
                half detail = 1.0 - smoothstep(_DetailFadeStart, max(_DetailFadeStart + 1.0, _DetailFadeEnd), distanceToCamera);
                fixed3 albedo = macro * _Tint.rgb *
                    lerp(fixed3(1,1,1), microModulation * microValue, detail);

                half3 normal = normalize(input.worldNormal);
                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half diffuse = saturate(dot(normal, lightDirection));
                half3 viewDirection = normalize(_WorldSpaceCameraPos - input.worldPosition);
                half3 halfDirection = normalize(lightDirection + viewDirection);
                half specularPower = lerp(30.0, 5.0, _Roughness);
                half specular = pow(saturate(dot(normal, halfDirection)), specularPower) * (1.0 - _Roughness) * 0.06;
                half3 ambient = ShadeSH9(half4(normal, 1.0));
                fixed4 output = fixed4(albedo * (ambient + _LightColor0.rgb * diffuse) + _LightColor0.rgb * specular, 1.0);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
