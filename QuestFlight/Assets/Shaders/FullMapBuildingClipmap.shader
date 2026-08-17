Shader "QuestFlight/Full Map Building Clipmap"
{
    Properties
    {
        _FacadeAlbedo ("Facade Albedo Array", 2DArray) = "" {}
        _FacadeNormal ("Facade Normal Array", 2DArray) = "" {}
        _FacadeMRAO ("Facade MRAO Array", 2DArray) = "" {}
        _FacadeDetail ("Facade Aperture Detail", 2D) = "gray" {}
        _NormalScale ("Normal Scale", Range(0, 1)) = 0.34
        _WindowReflection ("Window Reflection", Range(0, 1)) = 0.38
        _AmbientLift ("Ambient Lift", Range(0, 1)) = 0.04
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

            UNITY_DECLARE_TEX2DARRAY(_FacadeAlbedo);
            UNITY_DECLARE_TEX2DARRAY(_FacadeNormal);
            UNITY_DECLARE_TEX2DARRAY(_FacadeMRAO);
            sampler2D _FacadeDetail;
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
                float2 atlasCell : TEXCOORD1; // wall: side length, facade role
                float4 parameters : TEXCOORD2; // wall: surface, primary material, eave height, packed style/secondary
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

            float MaterialTileMeters(half materialId)
            {
                if (materialId < 1.0h) return 1.80;
                if (materialId < 2.0h) return 1.57;
                if (materialId < 3.0h) return 2.00;
                if (materialId < 4.0h) return 3.00;
                if (materialId < 5.0h) return 1.00;
                if (materialId < 6.0h) return 2.00;
                if (materialId < 7.0h) return 3.00;
                if (materialId < 8.0h) return 2.00;
                if (materialId < 9.0h) return 2.00;
                if (materialId < 10.0h) return 1.59;
                if (materialId < 11.0h) return 1.12;
                if (materialId < 12.0h) return 2.70;
                if (materialId < 13.0h) return 3.00;
                if (materialId < 14.0h) return 1.80;
                if (materialId < 15.0h) return 2.00;
                return 1.80;
            }

            float2 FacadeUv(float2 uvMeters, half materialId)
            {
                return uvMeters / max(0.25, MaterialTileMeters(materialId));
            }

            void FacadeLayout(half style, out half bayWidth, out half storeyHeight,
                out half sill, out half openingHeight, out half openingWidth, out half topMargin)
            {
                // 0 residential, 1 commercial, 2 industrial, 3 institutional,
                // 4 utility, 5 agricultural/outbuilding.
                bayWidth = 3.1h; storeyHeight = 3.05h; sill = 0.82h;
                openingHeight = 1.35h; openingWidth = 0.62h; topMargin = 0.48h;
                if (style > 0.5h && style < 1.5h)
                { bayWidth = 3.6h; storeyHeight = 3.6h; sill = 0.48h; openingHeight = 2.20h; openingWidth = 0.72h; topMargin = 0.62h; }
                else if (style > 1.5h && style < 2.5h)
                { bayWidth = 5.2h; storeyHeight = 4.3h; sill = 2.35h; openingHeight = 0.85h; openingWidth = 0.58h; topMargin = 0.72h; }
                else if (style > 2.5h && style < 3.5h)
                { bayWidth = 3.4h; storeyHeight = 3.45h; sill = 0.88h; openingHeight = 1.55h; openingWidth = 0.60h; topMargin = 0.62h; }
                else if (style > 3.5h && style < 4.5h)
                { bayWidth = 6.0h; storeyHeight = 4.0h; sill = 2.2h; openingHeight = 0.65h; openingWidth = 0.45h; topMargin = 0.75h; }
                else if (style > 4.5h)
                { bayWidth = 5.4h; storeyHeight = 4.1h; sill = 1.75h; openingHeight = 1.05h; openingWidth = 0.48h; topMargin = 0.70h; }
            }

            half WindowMask(float2 uvMeters, half wallLength, half eaveHeight, half style,
                half facadeRole, out float2 apertureUv)
            {
                half bayWidth, storeyHeight, sill, openingHeight, openingWidth, topMargin;
                FacadeLayout(style, bayWidth, storeyHeight, sill, openingHeight, openingWidth, topMargin);
                half margin = min(1.15h, wallLength * 0.18h);
                half usable = max(0.0h, wallLength - margin * 2.0h);
                half bayCount = floor(usable / bayWidth);
                half hasCompleteBay = step(1.0h, bayCount);
                half slot = usable / max(1.0h, bayCount);
                half localX = uvMeters.x - margin;
                half withinWall = step(0.0h, localX) * step(localX, usable);
                half bayLocal = frac(localX / max(slot, 0.01h));
                half edgeX = max(fwidth(bayLocal), 0.002h) * 1.5h;
                half left = (1.0h - openingWidth) * 0.5h;
                apertureUv.x = saturate((bayLocal - left) / max(openingWidth, 0.01h));
                half horizontal = smoothstep(left, left + edgeX, bayLocal) *
                    (1.0h - smoothstep(1.0h - left - edgeX, 1.0h - left, bayLocal));

                half floorIndex = floor(max(0.0h, uvMeters.y) / storeyHeight);
                half floorLocal = uvMeters.y - floorIndex * storeyHeight;
                apertureUv.y = saturate((floorLocal - sill) / max(openingHeight, 0.01h));
                half clearOpeningHeight = sill + openingHeight + topMargin;
                half canFitFirstFloor = step(clearOpeningHeight, eaveHeight);
                half completeFloors = canFitFirstFloor *
                    (floor(max(0.0h, eaveHeight - clearOpeningHeight) / storeyHeight) + 1.0h);
                half verticalEdge = max(fwidth(floorLocal), 0.003h) * 1.5h;
                half vertical = smoothstep(sill, sill + verticalEdge, floorLocal) *
                    (1.0h - smoothstep(sill + openingHeight - verticalEdge, sill + openingHeight, floorLocal));
                half validFloor = step(0.0h, uvMeters.y) * step(floorIndex + 1.0h, completeFloors);
                half allowed = style > 3.5h && style < 4.5h ? 0.0h : 1.0h;
                // Rear industrial/agricultural faces stay service-like rather than
                // receiving a false office grid.
                if (facadeRole > 1.5h &&
                    ((style > 1.5h && style < 2.5h) || style > 4.5h)) allowed = 0.0h;
                return horizontal * vertical * withinWall * validFloor * hasCompleteBay * allowed;
            }

            half DoorMask(float2 uvMeters, half wallLength, half style, half facadeRole,
                out float2 apertureUv)
            {
                half isFront = 1.0h - step(0.5h, facadeRole);
                half doorWidth = style > 1.5h && style < 2.5h ? 3.4h :
                    (style > 0.5h && style < 1.5h ? 1.8h : 1.05h);
                half doorHeight = style > 1.5h && style < 2.5h ? 3.6h : 2.20h;
                half centerDistance = abs(uvMeters.x - wallLength * 0.5h);
                apertureUv = float2(saturate((uvMeters.x - (wallLength - doorWidth) * 0.5h) /
                    max(doorWidth, 0.01h)), saturate(uvMeters.y / max(doorHeight, 0.01h)));
                half edge = max(fwidth(centerDistance), 0.003h) * 1.5h;
                half horizontal = 1.0h - smoothstep(doorWidth * 0.5h - edge, doorWidth * 0.5h, centerDistance);
                half vertical = step(0.04h, uvMeters.y) *
                    (1.0h - smoothstep(doorHeight - edge, doorHeight, uvMeters.y));
                return horizontal * vertical * isFront * step(doorWidth + 1.2h, wallLength);
            }

            float2 DetailAtlasUv(float2 apertureUv, half cell)
            {
                float2 safeUv = lerp(0.035, 0.965, saturate(apertureUv));
                float2 cellOrigin = float2(fmod(cell, 4.0h), floor(cell * 0.25h));
                return (cellOrigin + safeUv) * 0.25;
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
                half occlusion = 1.0h;
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
                    half primaryMaterial = floor(i.parameters.y + 0.5h);
                    half facadeStyle = floor(i.parameters.w + 0.001h);
                    half secondaryMaterial = floor(frac(i.parameters.w) * 32.0h + 0.5h) - 1.0h;
                    half useSecondary = step(-0.5h, secondaryMaterial) * step(0.0h, i.detailUv.y) *
                        (1.0h - step(1.05h, i.detailUv.y));
                    half materialId = lerp(primaryMaterial, secondaryMaterial, useSecondary);
                    float2 facadeUv = FacadeUv(i.detailUv, materialId);
                    albedo = UNITY_SAMPLE_TEX2DARRAY(_FacadeAlbedo, float3(facadeUv, materialId)).rgb * i.color.rgb;
                    fixed3 packed = UNITY_SAMPLE_TEX2DARRAY(_FacadeMRAO, float3(facadeUv, materialId)).rgb;
                    occlusion = packed.b;
                    half3 tangentNormal = UnpackNormal(
                        UNITY_SAMPLE_TEX2DARRAY(_FacadeNormal, float3(facadeUv, materialId)));
                    tangentNormal.xy *= _NormalScale;
                    n = normalize(i.worldTangent * tangentNormal.x +
                        i.worldBitangent * tangentNormal.y + i.worldNormal * tangentNormal.z);
                    roughness = packed.g;
                    half wallLength = max(0.01h, i.atlasCell.x);
                    float2 windowUv;
                    window = WindowMask(i.detailUv, wallLength, i.parameters.z, facadeStyle,
                        i.atlasCell.y, windowUv);
                    // Sub-pixel reflective openings otherwise collapse into a bright
                    // crawling stipple in aerial views. Fade the semantic mask by
                    // projected metre size, continuously and without LOD dither/pop.
                    half metersPerPixel = max(length(ddx(i.detailUv)), length(ddy(i.detailUv)));
                    half openingScreenFade = 1.0h - smoothstep(0.18h, 0.46h, metersPerPixel);
                    window *= openingScreenFade;
                    float2 doorUv;
                    half door = DoorMask(i.detailUv, wallLength, facadeStyle, i.atlasCell.y, doorUv);
                    window *= 1.0h - door;
                    UNITY_BRANCH if (window > 0.001h)
                    {
                        fixed4 aperture = tex2D(_FacadeDetail,
                            DetailAtlasUv(windowUv, min(5.0h, facadeStyle)));
                        half3 reflectionNormal = normalize(i.worldNormal);
                        half3 reflectionDirection = reflect(-viewDir, reflectionNormal);
                        half4 encodedReflection = UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, reflectionDirection);
                        half3 environmentReflection = DecodeHDR(encodedReflection, unity_SpecCube0_HDR);
                        half fresnel = pow(1.0 - saturate(dot(reflectionNormal, viewDir)), 3.0);
                        half reflectionWeight = saturate(_WindowReflection * (0.22 + fresnel * 0.78));
                        albedo = lerp(albedo, lerp(aperture.rgb, environmentReflection, reflectionWeight),
                            window * aperture.a * 0.92);
                        roughness = lerp(roughness, 0.22h, window);
                    }
                    UNITY_BRANCH if (door > 0.001h)
                    {
                        fixed4 aperture = tex2D(_FacadeDetail,
                            DetailAtlasUv(doorUv, 8.0h + min(5.0h, facadeStyle)));
                        albedo = lerp(albedo, aperture.rgb, door * aperture.a * 0.94h);
                        roughness = lerp(roughness, 0.48h, door);
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
                    half3 irradiance = max(ambient, 0.0h) * 0.35h + _AmbientLift +
                        _LightColor0.rgb * (0.08h + ndl * 0.55h) * shadow;
                    color = albedo * irradiance * lerp(0.76h, 1.0h, occlusion);
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
