Shader "QuestFlight/KBDU Aerial Building Shell"
{
    Properties
    {
        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1

        _MainTex ("AirportCore V4 albedo atlas", 2D) = "white" {}
        _NormalTex ("AirportCore V4 normal atlas", 2D) = "bump" {}
        _MRAOTex ("AirportCore V4 metallic / roughness / AO", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _NormalStrength ("Normal strength", Range(0,1)) = 0.42
        _SpecularStrength ("Specular strength", Range(0,0.5)) = 0.14
        _GroundOcclusion ("Foundation occlusion", Range(0,0.5)) = 0.2
        _DetailFlattenStart ("Detail flatten start (texels/pixel)", Range(0.25,4)) = 3.0
        _DetailFlattenEnd ("Detail flatten end (texels/pixel)", Range(1,8)) = 8.0
        _AmbientFloor ("Linear ambient floor", Range(0,0.3)) = 0.12
        _NearAlbedoLift ("Near dark-detail lift", Range(0,0.5)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _NormalTex;
            sampler2D _MRAOTex;
            fixed4 _Tint;
            half _NormalStrength;
            half _SpecularStrength;
            half _GroundOcclusion;
            half _DetailFlattenStart;
            half _DetailFlattenEnd;
            half _AmbientFloor;
            half _NearAlbedoLift;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                float2 detailUv : TEXCOORD0;
                float2 atlasCell : TEXCOORD1;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                half3 worldNormal : TEXCOORD0;
                float2 detailUv : TEXCOORD1;
                float2 atlasCell : TEXCOORD2;
                float3 worldPosition : TEXCOORD3;
                fixed4 color : COLOR;
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
                output.detailUv = input.detailUv;
                output.atlasCell = input.atlasCell;
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.color = input.color;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            half3 StableAtlasAverage(half2 atlasCell)
            {
                // Linear-space averages intentionally describe broad material
                // response, not new artwork. They prevent sub-pixel corrugation
                // and the very dark roof cell from collapsing into black moire.
                half tile = atlasCell.x + atlasCell.y * 4.0h;
                if (tile < 0.5h) return half3(0.274h, 0.196h, 0.126h); // tan brick
                if (tile < 1.5h) return half3(0.214h, 0.214h, 0.187h); // light metal
                if (tile < 4.0h) return half3(0.160h, 0.170h, 0.180h); // dark roof
                if (tile < 6.0h) return half3(0.196h, 0.163h, 0.119h); // concrete
                if (tile < 9.0h) return half3(0.214h, 0.214h, 0.187h); // hangar metal
                return half3(0.196h, 0.187h, 0.171h); // weathered corrugated
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                const half atlasScale = 0.25h;
                // Match the proven AirportCore V4 building UV authoring inset:
                // 0.012 atlas UV, or 0.048 in normalized cell coordinates. This
                // avoids adjacent-cell bleed after Android downsampling and mips.
                const half cellGutter = 0.048h;
                half2 wrapped = frac(input.detailUv);
                const half usableCell = 1.0h - 2.0h * cellGutter;
                half2 atlasUv = (input.atlasCell + cellGutter +
                    wrapped * usableCell) * atlasScale;

                // Derive gradients from the unwrapped local coordinates. Using
                // implicit derivatives after frac() creates discontinuities at
                // every repeat and selects unstable mips at aerial angles.
                float2 gradientX = ddx(input.detailUv) * usableCell * atlasScale;
                float2 gradientY = ddy(input.detailUv) * usableCell * atlasScale;
                float2 texelGradientX = gradientX * _MainTex_TexelSize.zw;
                float2 texelGradientY = gradientY * _MainTex_TexelSize.zw;
                half texelsPerPixel = max(length(texelGradientX), length(texelGradientY));
                half farDetail = smoothstep(
                    _DetailFlattenStart, max(_DetailFlattenEnd, _DetailFlattenStart + 0.01h),
                    texelsPerPixel);

                half3 variation = _Tint.rgb * input.color.rgb;
                half3 sampledAlbedo = tex2Dgrad(
                    _MainTex, atlasUv, gradientX, gradientY).rgb * variation;
                half3 stableAlbedo = StableAtlasAverage(input.atlasCell) * variation;
                half3 albedo = lerp(sampledAlbedo, stableAlbedo, farDetail);
                albedo = max(albedo,
                    stableAlbedo * lerp(_NearAlbedoLift, 1.0h, farDetail));
                half3 mrao = tex2Dgrad(_MRAOTex, atlasUv, gradientX, gradientY).rgb;
                half metallic = lerp(saturate(mrao.r), 0.0h, farDetail);
                half roughness = lerp(saturate(mrao.g), 0.82h, farDetail);
                half ao = lerp(lerp(0.68h, 1.0h, saturate(mrao.b)), 1.0h, farDetail);

                half3 geometricNormal = normalize(input.worldNormal);
                half3 referenceAxis = abs(geometricNormal.y) > 0.80h
                    ? half3(0.0h, 0.0h, 1.0h)
                    : half3(0.0h, 1.0h, 0.0h);
                half3 tangent = normalize(cross(referenceAxis, geometricNormal));
                half3 bitangent = normalize(cross(geometricNormal, tangent));
                half3 tangentNormal = UnpackNormal(
                    tex2Dgrad(_NormalTex, atlasUv, gradientX, gradientY));
                tangentNormal.xy *= _NormalStrength * (1.0h - farDetail);
                tangentNormal = normalize(tangentNormal);
                half3 normal = normalize(
                    tangent * tangentNormal.x +
                    bitangent * tangentNormal.y +
                    geometricNormal * tangentNormal.z);

                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half ndotl = saturate(dot(normal, lightDirection));
                half3 ambient = max(
                    ShadeSH9(half4(normal, 1.0h)), half3(_AmbientFloor, _AmbientFloor, _AmbientFloor)) * ao;
                half3 direct = _LightColor0.rgb * (0.09h + ndotl * 0.73h);
                half foundation = lerp(1.0h - _GroundOcclusion, 1.0h, input.color.a);
                half3 color = albedo * foundation * (ambient + direct);

                half3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.worldPosition);
                half3 halfDirection = normalize(lightDirection + viewDirection);
                half exponent = lerp(64.0h, 5.0h, roughness);
                half specular = pow(saturate(dot(normal, halfDirection)), exponent)
                              * _SpecularStrength * (1.0h - roughness * 0.72h)
                              * lerp(1.0h, 0.15h, farDetail);
                half3 specularColor = lerp(half3(0.04h, 0.04h, 0.04h), albedo, metallic);
                color += _LightColor0.rgb * specularColor * specular;

                fixed4 output = fixed4(color, 1.0h);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    
        Pass
        {
            Name "XRMotionVectors"
            Tags { "LightMode"="XRMotionVectors" }
            Cull Back Blend Off ZWrite On ZTest LEqual ColorMask RGBA
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            CGPROGRAM
#define QF_STATIC_WORLD_GEOMETRY 1

            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
#include "Assets/Shaders/ProductSpaceWarpMotion.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _NormalTex;
            sampler2D _MRAOTex;
            fixed4 _Tint;
            half _NormalStrength;
            half _SpecularStrength;
            half _GroundOcclusion;
            half _DetailFlattenStart;
            half _DetailFlattenEnd;
            half _AmbientFloor;
            half _NearAlbedoLift;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                float2 detailUv : TEXCOORD0;
                float2 atlasCell : TEXCOORD1;
                fixed4 color : COLOR;
                
float3 qfPreviousVertex : TEXCOORD4;
UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                half3 worldNormal : TEXCOORD0;
                float2 detailUv : TEXCOORD1;
                float2 atlasCell : TEXCOORD2;
                float3 worldPosition : TEXCOORD3;
                fixed4 color : COLOR;
                UNITY_FOG_COORDS(4)
                
float4 qfCurrent : TEXCOORD14; float4 qfPrevious : TEXCOORD15;
UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f QFColorVertex(appdata input, out float4 qfWorld)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = QFObjectToMotionClip(input.vertex, qfWorld);
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.detailUv = input.detailUv;
                output.atlasCell = input.atlasCell;
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.color = input.color;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            half3 StableAtlasAverage(half2 atlasCell)
            {
                // Linear-space averages intentionally describe broad material
                // response, not new artwork. They prevent sub-pixel corrugation
                // and the very dark roof cell from collapsing into black moire.
                half tile = atlasCell.x + atlasCell.y * 4.0h;
                if (tile < 0.5h) return half3(0.274h, 0.196h, 0.126h); // tan brick
                if (tile < 1.5h) return half3(0.214h, 0.214h, 0.187h); // light metal
                if (tile < 4.0h) return half3(0.160h, 0.170h, 0.180h); // dark roof
                if (tile < 6.0h) return half3(0.196h, 0.163h, 0.119h); // concrete
                if (tile < 9.0h) return half3(0.214h, 0.214h, 0.187h); // hangar metal
                return half3(0.196h, 0.187h, 0.171h); // weathered corrugated
            }

            fixed4 QFColorFragment(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                const half atlasScale = 0.25h;
                // Match the proven AirportCore V4 building UV authoring inset:
                // 0.012 atlas UV, or 0.048 in normalized cell coordinates. This
                // avoids adjacent-cell bleed after Android downsampling and mips.
                const half cellGutter = 0.048h;
                half2 wrapped = frac(input.detailUv);
                const half usableCell = 1.0h - 2.0h * cellGutter;
                half2 atlasUv = (input.atlasCell + cellGutter +
                    wrapped * usableCell) * atlasScale;

                // Derive gradients from the unwrapped local coordinates. Using
                // implicit derivatives after frac() creates discontinuities at
                // every repeat and selects unstable mips at aerial angles.
                float2 gradientX = ddx(input.detailUv) * usableCell * atlasScale;
                float2 gradientY = ddy(input.detailUv) * usableCell * atlasScale;
                float2 texelGradientX = gradientX * _MainTex_TexelSize.zw;
                float2 texelGradientY = gradientY * _MainTex_TexelSize.zw;
                half texelsPerPixel = max(length(texelGradientX), length(texelGradientY));
                half farDetail = smoothstep(
                    _DetailFlattenStart, max(_DetailFlattenEnd, _DetailFlattenStart + 0.01h),
                    texelsPerPixel);

                half3 variation = _Tint.rgb * input.color.rgb;
                half3 sampledAlbedo = tex2Dgrad(
                    _MainTex, atlasUv, gradientX, gradientY).rgb * variation;
                half3 stableAlbedo = StableAtlasAverage(input.atlasCell) * variation;
                half3 albedo = lerp(sampledAlbedo, stableAlbedo, farDetail);
                albedo = max(albedo,
                    stableAlbedo * lerp(_NearAlbedoLift, 1.0h, farDetail));
                half3 mrao = tex2Dgrad(_MRAOTex, atlasUv, gradientX, gradientY).rgb;
                half metallic = lerp(saturate(mrao.r), 0.0h, farDetail);
                half roughness = lerp(saturate(mrao.g), 0.82h, farDetail);
                half ao = lerp(lerp(0.68h, 1.0h, saturate(mrao.b)), 1.0h, farDetail);

                half3 geometricNormal = normalize(input.worldNormal);
                half3 referenceAxis = abs(geometricNormal.y) > 0.80h
                    ? half3(0.0h, 0.0h, 1.0h)
                    : half3(0.0h, 1.0h, 0.0h);
                half3 tangent = normalize(cross(referenceAxis, geometricNormal));
                half3 bitangent = normalize(cross(geometricNormal, tangent));
                half3 tangentNormal = UnpackNormal(
                    tex2Dgrad(_NormalTex, atlasUv, gradientX, gradientY));
                tangentNormal.xy *= _NormalStrength * (1.0h - farDetail);
                tangentNormal = normalize(tangentNormal);
                half3 normal = normalize(
                    tangent * tangentNormal.x +
                    bitangent * tangentNormal.y +
                    geometricNormal * tangentNormal.z);

                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half ndotl = saturate(dot(normal, lightDirection));
                half3 ambient = max(
                    ShadeSH9(half4(normal, 1.0h)), half3(_AmbientFloor, _AmbientFloor, _AmbientFloor)) * ao;
                half3 direct = _LightColor0.rgb * (0.09h + ndotl * 0.73h);
                half foundation = lerp(1.0h - _GroundOcclusion, 1.0h, input.color.a);
                half3 color = albedo * foundation * (ambient + direct);

                half3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.worldPosition);
                half3 halfDirection = normalize(lightDirection + viewDirection);
                half exponent = lerp(64.0h, 5.0h, roughness);
                half specular = pow(saturate(dot(normal, halfDirection)), exponent)
                              * _SpecularStrength * (1.0h - roughness * 0.72h)
                              * lerp(1.0h, 0.15h, farDetail);
                half3 specularColor = lerp(half3(0.04h, 0.04h, 0.04h), albedo, metallic);
                color += _LightColor0.rgb * specularColor * specular;

                fixed4 output = fixed4(color, 1.0h);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            
v2f QFPreviousVertex(appdata input, out float4 qfWorld)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = QFObjectToMotionClip(input.vertex, qfWorld);
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.detailUv = input.detailUv;
                output.atlasCell = input.atlasCell;
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.color = input.color;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }
v2f vert(appdata v)
{
    UNITY_SETUP_INSTANCE_ID(v);
    float4 world;
    v2f o=QFColorVertex(v,world);
    #if !defined(QF_STATIC_WORLD_GEOMETRY)
    if (_QFHasPreviousModel < .5 && (_QFPreviousVertices > .5 || unity_MotionVectorsParams.x == 1))
        v.vertex.xyz=v.qfPreviousVertex;
    #endif
    float4 previousWorld;
    v2f old=QFPreviousVertex(v,previousWorld);
    float4 oldWorld=QFPreviousWorld(previousWorld);
    if (_QFResetObjectMotion > .5) oldWorld=world;
    o.qfCurrent=o.position;
    o.qfPrevious=mul(_PrevViewProjMatrixStereo[QFEye()],oldWorld);
    
    o.position=o.qfCurrent;
    return o;
}
float4 frag(v2f v) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
    float coverage=QFColorFragment(v).a;
    clip(coverage-0.001);
    return QFMotion(v.qfCurrent,v.qfPrevious,coverage);
}

            ENDCG
        }
}
    Fallback "Diffuse"
}
