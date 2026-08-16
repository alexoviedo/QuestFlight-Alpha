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
        [HideInInspector] _NearAlbedo ("Near orthophoto fallback", 2D) = "gray" {}
        [HideInInspector] _MidAlbedo ("Mid orthophoto fallback", 2D) = "gray" {}
        [HideInInspector] _FarAlbedo ("Far orthophoto fallback", 2D) = "gray" {}
        [HideInInspector] _ClipGlobalAlbedo ("Global orthophoto clipmap", 2D) = "gray" {}
        [HideInInspector] _NearWorldMinInvSize ("Near world mapping", Vector) = (-2000,-2000,0.00025,0.00025)
        [HideInInspector] _MidWorldMinInvSize ("Mid world mapping", Vector) = (-6000,-6000,0.0000833333,0.0000833333)
        [HideInInspector] _FarWorldMinInvSize ("Far world mapping", Vector) = (-12000,-12000,0.0000416667,0.0000416667)
        [HideInInspector] _NearBlendStartEnd ("Near blend", Vector) = (1800,1980,0,0)
        [HideInInspector] _MidBlendStartEnd ("Mid blend", Vector) = (5000,6000,0,0)
        [HideInInspector] _ClipmapEnabled ("Aircraft-centered orthophoto enabled", Float) = 0
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
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            #include "AerialClipmapSampling.cginc"

            sampler2D _MainTex;
            sampler2D _BumpMap;
            sampler2D _MRAO;
            sampler2D _NearAlbedo;
            sampler2D _MidAlbedo;
            sampler2D _FarAlbedo;
            float4 _NearWorldMinInvSize;
            float4 _MidWorldMinInvSize;
            float4 _FarWorldMinInvSize;
            float4 _NearBlendStartEnd;
            float4 _MidBlendStartEnd;
            half _NormalScale;
            half _WindowReflection;
            half _AmbientLift;
            half _ClipmapEnabled;

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

            fixed3 SampleSerializedOrthophoto(float2 world)
            {
                float2 nearUv = saturate((world - _NearWorldMinInvSize.xy) * _NearWorldMinInvSize.zw);
                float2 midUv = saturate((world - _MidWorldMinInvSize.xy) * _MidWorldMinInvSize.zw);
                float2 farUv = saturate((world - _FarWorldMinInvSize.xy) * _FarWorldMinInvSize.zw);
                float2 nearUvDx = ddx(nearUv);
                float2 nearUvDy = ddy(nearUv);
                float2 midUvDx = ddx(midUv);
                float2 midUvDy = ddy(midUv);
                float2 farUvDx = ddx(farUv);
                float2 farUvDy = ddy(farUv);
                half radius = max(abs(world.x), abs(world.y));
                half nearWeight = 1.0 - smoothstep(_NearBlendStartEnd.x, _NearBlendStartEnd.y, radius);
                half midWeight = 1.0 - smoothstep(_MidBlendStartEnd.x, _MidBlendStartEnd.y, radius);
                UNITY_BRANCH if (radius <= _NearBlendStartEnd.x)
                    return tex2Dgrad(_NearAlbedo, nearUv, nearUvDx, nearUvDy).rgb;
                UNITY_BRANCH if (radius < _NearBlendStartEnd.y)
                {
                    fixed3 nearColor = tex2Dgrad(_NearAlbedo, nearUv, nearUvDx, nearUvDy).rgb;
                    fixed3 midColor = tex2Dgrad(_MidAlbedo, midUv, midUvDx, midUvDy).rgb;
                    return lerp(midColor, nearColor, nearWeight);
                }
                UNITY_BRANCH if (radius <= _MidBlendStartEnd.x)
                    return tex2Dgrad(_MidAlbedo, midUv, midUvDx, midUvDy).rgb;
                UNITY_BRANCH if (radius < _MidBlendStartEnd.y)
                {
                    fixed3 midColor = tex2Dgrad(_MidAlbedo, midUv, midUvDx, midUvDy).rgb;
                    fixed3 farColor = tex2Dgrad(_FarAlbedo, farUv, farUvDx, farUvDy).rgb;
                    return lerp(farColor, midColor, midWeight);
                }
                return tex2Dgrad(_FarAlbedo, farUv, farUvDx, farUvDy).rgb;
            }

            fixed3 SampleProjectedRoofOrthophoto(float2 world)
            {
                fixed3 macro = SampleSerializedOrthophoto(world);
                UNITY_BRANCH if (_ClipmapEnabled > 0.5h)
                {
                    float2 farUv = saturate((world - _FarWorldMinInvSize.xy) * _FarWorldMinInvSize.zw);
                    macro = tex2Dgrad(_ClipGlobalAlbedo, farUv, ddx(farUv), ddy(farUv)).rgb;
                    fixed3 clipColor;
                    half clipWeight = QuestFlightSampleAircraftCenteredClipmap(
                        world, _ClipMediumGridMinSize, _ClipMediumUvScaleOffsetBlend,
                        false, clipColor);
                    macro = lerp(macro, clipColor, clipWeight);
                    clipWeight = QuestFlightSampleAircraftCenteredClipmap(
                        world, _ClipHighGridMinSize, _ClipHighUvScaleOffsetBlend,
                        true, clipColor);
                    macro = lerp(macro, clipColor, clipWeight);
                }
                return macro;
            }

            half RoofRoughness(half classId)
            {
                if (classId > 1.5h && classId < 2.5h) return 0.62h;
                if (classId > 3.5h && classId < 4.5h) return 0.64h;
                return classId < 0.5h ? 0.72h : 0.76h;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half roof = step(0.5h, i.parameters.x);
                fixed3 albedo;
                half3 n;
                half roughness;
                half window = 0.0h;
                half3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
                UNITY_BRANCH if (roof > 0.5h)
                {
                    // World X/Z is a vertical projection of the same photographed
                    // roof pixel used by the terrain. Geometry supplies shape only;
                    // generated atlas color, tint, and tangent normal are bypassed.
                    albedo = SampleProjectedRoofOrthophoto(i.worldPos.xz);
                    n = normalize(i.worldNormal);
                    roughness = RoofRoughness(i.parameters.y);
                }
                else
                {
                    float2 scaledUv = i.detailUv * max(0.02, i.parameters.w);
                    float2 atlasUv = AtlasUv(scaledUv, i.atlasCell);
                    albedo = tex2D(_MainTex, atlasUv).rgb * i.color.rgb;
                    fixed3 packed = tex2D(_MRAO, atlasUv).rgb;
                    half3 tangentNormal = UnpackNormal(tex2D(_BumpMap, atlasUv));
                    tangentNormal.xy *= _NormalScale;
                    n = normalize(i.worldTangent * tangentNormal.x +
                        i.worldBitangent * tangentNormal.y + i.worldNormal * tangentNormal.z);
                    roughness = packed.g;
                    window = WindowMask(i.detailUv, i.parameters.y);
                    UNITY_BRANCH if (window > 0.001h)
                    {
                        float2 glassCell = float2(fmod(floor(i.parameters.z * 127.0), 2.0), 3.0);
                        fixed3 glassBase = tex2D(_MainTex, AtlasUv(scaledUv * 0.18, glassCell)).rgb;
                        half3 reflectionDirection = reflect(-viewDir, n);
                        half4 encodedReflection = UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, reflectionDirection);
                        half3 environmentReflection = DecodeHDR(encodedReflection, unity_SpecCube0_HDR);
                        half fresnel = pow(1.0 - saturate(dot(n, viewDir)), 3.0);
                        half reflectionWeight = saturate(_WindowReflection * (0.38 + fresnel * 0.62));
                        albedo = lerp(albedo, lerp(glassBase, environmentReflection, reflectionWeight), window * 0.82);
                        roughness = lerp(roughness, 0.22h, window);
                    }
                }

                half3 lightDir = normalize(UnityWorldSpaceLightDir(i.worldPos));
                half ndl = saturate(dot(n, lightDir));
                half shadow = SHADOW_ATTENUATION(i);
                half3 ambient = ShadeSH9(half4(n, 1.0));
                half3 halfVector = normalize(lightDir + viewDir);
                half3 color;
                UNITY_BRANCH if (roof > 0.5h)
                {
                    // Match the terrain irradiance contract while allowing the retained
                    // pitched geometry to read under the current sun.
                    half3 irradiance = saturate(max(ambient, 0.0h) * 0.35h +
                        _LightColor0.rgb * (0.08h + ndl * 0.55h));
                    half specular = pow(saturate(dot(n, halfVector)), lerp(28.0h, 5.0h, roughness)) *
                        (1.0h - roughness) * 0.045h;
                    color = albedo * irradiance + _LightColor0.rgb * specular;
                }
                else
                {
                    half specular = pow(saturate(dot(n, halfVector)), lerp(10.0h, 72.0h, 1.0h - roughness));
                    color = albedo * (ambient + _AmbientLift + _LightColor0.rgb * ndl * shadow);
                    color += _LightColor0.rgb * specular * shadow * lerp(0.04h, 0.36h, window);
                }
                UNITY_APPLY_FOG(i.fogCoord, color);
                return fixed4(color, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
