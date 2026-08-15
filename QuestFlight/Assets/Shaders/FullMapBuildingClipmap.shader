Shader "QuestFlight/Full Map Building Clipmap"
{
    Properties
    {
        _MainTex ("Albedo Atlas", 2D) = "white" {}
        _BumpMap ("Normal Atlas", 2D) = "bump" {}
        _MRAO ("MRAO Atlas", 2D) = "white" {}
        _NormalScale ("Normal Scale", Range(0, 1)) = 0.45
        _WindowReflection ("Window Reflection", Range(0, 1)) = 0.58
        _AmbientLift ("Ambient Lift", Range(0, 1)) = 0.18
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 180
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Cull Back
            ZWrite On
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _MainTex;
            sampler2D _BumpMap;
            sampler2D _MRAO;
            half _NormalScale;
            half _WindowReflection;
            half _AmbientLift;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 detailUv : TEXCOORD0;
                float2 atlasCell : TEXCOORD1;
                float4 parameters : TEXCOORD2; // x=surface, y=class, z=seed, w=detail scale
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                half3 worldNormal : TEXCOORD1;
                half3 worldTangent : TEXCOORD2;
                half3 worldBitangent : TEXCOORD3;
                float2 detailUv : TEXCOORD4;
                float2 atlasCell : TEXCOORD5;
                float4 parameters : TEXCOORD6;
                fixed4 color : COLOR;
                UNITY_FOG_COORDS(7)
                SHADOW_COORDS(8)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                o.worldBitangent = cross(o.worldNormal, o.worldTangent) * v.tangent.w * unity_WorldTransformParams.w;
                o.detailUv = v.detailUv;
                o.atlasCell = v.atlasCell;
                o.parameters = v.parameters;
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                TRANSFER_SHADOW(o);
                return o;
            }

            float2 AtlasUv(float2 uv, float2 cell)
            {
                // Mirror-repeat inside one atlas cell. The authored material source is
                // photographic rather than mathematically tileable; ordinary frac()
                // exposed both cell edges as a regular roof/facade grid at flight
                // distances. Mirroring keeps the edge value and first derivative
                // continuous without another texture, pass, or sample.
                float2 mirroredPeriod = frac(uv * 0.5) * 2.0;
                float2 wrapped = 1.0 - abs(mirroredPeriod - 1.0);
                // A one-texel inset prevents adjacent atlas cells bleeding at grazing angles.
                wrapped = lerp(1.0 / 256.0, 1.0 - 1.0 / 256.0, wrapped);
                return (cell + wrapped) * 0.25;
            }

            half WindowMask(float2 uvMeters, half classId)
            {
                half period = classId < 0.5 ? 3.6 : (classId < 1.5 ? 3.0 : 4.5);
                half storey = 3.15;
                float2 cell = frac(float2(uvMeters.x / period, uvMeters.y / storey));
                half edge = max(fwidth(cell.x), fwidth(cell.y)) * 1.5;
                half horizontal = smoothstep(0.12, 0.12 + edge, cell.x) * (1.0 - smoothstep(0.78 - edge, 0.78, cell.x));
                half vertical = smoothstep(0.30, 0.30 + edge, cell.y) * (1.0 - smoothstep(0.78 - edge, 0.78, cell.y));
                half allowed = classId < 3.9 ? 1.0 : 0.0;
                // Industrial facades retain sparse clerestory glazing rather than office grids.
                if (classId > 1.5 && classId < 2.5)
                    vertical *= smoothstep(0.62, 0.68, cell.y);
                return horizontal * vertical * allowed;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 scaledUv = i.detailUv * max(0.02, i.parameters.w);
                float2 atlasUv = AtlasUv(scaledUv, i.atlasCell);
                fixed3 albedo = tex2D(_MainTex, atlasUv).rgb * i.color.rgb;
                fixed3 packed = tex2D(_MRAO, atlasUv).rgb;
                half3 tangentNormal = UnpackNormal(tex2D(_BumpMap, atlasUv));
                tangentNormal.xy *= _NormalScale;
                half3 n = normalize(i.worldTangent * tangentNormal.x + i.worldBitangent * tangentNormal.y + i.worldNormal * tangentNormal.z);

                half window = i.parameters.x < 0.5 ? WindowMask(i.detailUv, i.parameters.y) : 0.0;
                float2 glassCell = float2(fmod(floor(i.parameters.z * 127.0), 2.0), 3.0);
                fixed3 glassBase = tex2D(_MainTex, AtlasUv(scaledUv * 0.18, glassCell)).rgb;
                half3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
                half3 reflectionDirection = reflect(-viewDir, n);
                half4 encodedReflection = UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, reflectionDirection);
                half3 environmentReflection = DecodeHDR(encodedReflection, unity_SpecCube0_HDR);
                half fresnel = pow(1.0 - saturate(dot(n, viewDir)), 3.0);
                half reflectionWeight = saturate(_WindowReflection * (0.38 + fresnel * 0.62));
                fixed3 glass = lerp(glassBase, environmentReflection, reflectionWeight);
                albedo = lerp(albedo, glass, window * 0.82);

                half3 lightDir = normalize(UnityWorldSpaceLightDir(i.worldPos));
                half ndl = saturate(dot(n, lightDir));
                half shadow = SHADOW_ATTENUATION(i);
                half3 ambient = ShadeSH9(half4(n, 1.0));
                half roughness = lerp(packed.g, 0.22, window);
                half3 halfVector = normalize(lightDir + viewDir);
                half specular = pow(saturate(dot(n, halfVector)), lerp(10.0, 72.0, 1.0 - roughness));
                half3 color = albedo * (ambient + _AmbientLift + _LightColor0.rgb * ndl * shadow);
                color += _LightColor0.rgb * specular * shadow * lerp(0.04, 0.36, window);
                UNITY_APPLY_FOG(i.fogCoord, color);
                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
