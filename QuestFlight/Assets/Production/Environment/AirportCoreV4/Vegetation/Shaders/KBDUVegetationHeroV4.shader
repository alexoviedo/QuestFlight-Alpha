Shader "QuestFlight/Airport Core V4/Vegetation Hero Cutout"
{
    Properties
    {
        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1

        _MainTex ("Shared foliage/bark atlas", 2D) = "white" {}
        _Tint ("Global tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Range(0.25,0.75)) = 0.46
        _BackfaceLight ("Backface light", Range(0.25,1)) = 0.62
        _BakedAtlasStrength ("Baked atlas lighting", Range(0,1)) = 0.55
        [PerRendererData] _InstanceTint ("Instance tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        LOD 120
        Cull Off
        ZWrite On
        ZTest LEqual
        Blend Off

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile __ LIGHTMAP_ON
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Tint;
            half _Cutoff, _BackfaceLight, _BakedAtlasStrength;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _InstanceTint)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 normalWorld : TEXCOORD1;
                fixed4 color : COLOR;
                #ifdef LIGHTMAP_ON
                    float2 lightmapUv : TEXCOORD2;
                #endif
                UNITY_FOG_COORDS(3)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normalWorld = UnityObjectToWorldNormal(input.normal);
                output.color = input.color;
                #ifdef LIGHTMAP_ON
                    output.lightmapUv = input.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
                #endif
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                UNITY_SETUP_INSTANCE_ID(input);
                fixed4 atlas = tex2D(_MainTex, input.uv);
                clip(atlas.a - _Cutoff);
                fixed4 instanceTint = UNITY_ACCESS_INSTANCED_PROP(Props, _InstanceTint);
                half3 normal = normalize(input.normalWorld);
                half3 lighting;
                #ifdef LIGHTMAP_ON
                    lighting = DecodeLightmap(UNITY_SAMPLE_TEX2D(unity_Lightmap, input.lightmapUv));
                #else
                    half3 ambient = max(ShadeSH9(half4(normal,1.0h)), 0.0h) * 0.35h;
                    half ndl = saturate(abs(dot(normal, normalize(_WorldSpaceLightPos0.xyz))));
                    half wrap = lerp(_BackfaceLight, 1.0h, ndl);
                    lighting = saturate(ambient + _LightColor0.rgb * (0.06h + wrap * 0.40h));
                #endif
                half atlasBake = lerp(1.0h, dot(atlas.rgb, half3(0.22h,0.68h,0.10h)) * 0.55h + 0.65h,
                                      _BakedAtlasStrength);
                fixed4 output = fixed4(atlas.rgb * _Tint.rgb * instanceTint.rgb * input.color.rgb * lighting * atlasBake, 1.0h);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Off
            ZWrite On
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half _Cutoff;

            struct shadowInput
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct shadowOutput
            {
                V2F_SHADOW_CASTER;
                float2 uv : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            shadowOutput vertShadow(shadowInput v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                shadowOutput output;
                UNITY_INITIALIZE_OUTPUT(shadowOutput, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.uv = TRANSFORM_TEX(v.uv, _MainTex);
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(output);
                return output;
            }

            fixed4 fragShadow(shadowOutput input) : SV_Target
            {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(tex2D(_MainTex, input.uv).a - _Cutoff);
                SHADOW_CASTER_FRAGMENT(input)
            }
            ENDCG
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode"="Meta" }
            Cull Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vertMeta
            #pragma fragment fragMeta
            #include "UnityCG.cginc"
            #include "UnityMetaPass.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Tint;
            half _Cutoff;

            struct metaInput
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                fixed4 color : COLOR;
            
UNITY_VERTEX_INPUT_INSTANCE_ID
};

            struct metaOutput
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            
UNITY_VERTEX_OUTPUT_STEREO
};

            metaOutput vertMeta(metaInput input)
            {
UNITY_SETUP_INSTANCE_ID(input);
                metaOutput output;
UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityMetaVertexPosition(
                    input.vertex, input.uv1, input.uv2, unity_LightmapST, unity_DynamicLightmapST);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                return output;
            }

            fixed4 fragMeta(metaOutput input) : SV_Target
            {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                fixed4 atlas = tex2D(_MainTex, input.uv);
                clip(atlas.a - _Cutoff);
                UnityMetaInput meta;
                UNITY_INITIALIZE_OUTPUT(UnityMetaInput, meta);
                meta.Albedo = atlas.rgb * _Tint.rgb * input.color.rgb;
                meta.Emission = 0;
                return UnityMetaFragment(meta);
            }
            
#pragma multi_compile_instancing
ENDCG
        }
    
        Pass
        {
            Name "XRMotionVectors"
            Tags { "LightMode"="XRMotionVectors" }
            Cull Off Blend Off ZWrite On ZTest LEqual ColorMask RGBA
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            CGPROGRAM
#define QF_STATIC_WORLD_GEOMETRY 1

            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile __ LIGHTMAP_ON
            #include "UnityCG.cginc"
#include "Assets/Shaders/ProductSpaceWarpMotion.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Tint;
            half _Cutoff, _BackfaceLight, _BakedAtlasStrength;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _InstanceTint)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                fixed4 color : COLOR;
                
float3 qfPreviousVertex : TEXCOORD4;
UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 normalWorld : TEXCOORD1;
                fixed4 color : COLOR;
                #ifdef LIGHTMAP_ON
                    float2 lightmapUv : TEXCOORD2;
                #endif
                UNITY_FOG_COORDS(3)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                
float4 qfCurrent : TEXCOORD14; float4 qfPrevious : TEXCOORD15;
UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f QFColorVertex(appdata input, out float4 qfWorld)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = QFObjectToMotionClip(input.vertex, qfWorld);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normalWorld = UnityObjectToWorldNormal(input.normal);
                output.color = input.color;
                #ifdef LIGHTMAP_ON
                    output.lightmapUv = input.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
                #endif
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 QFColorFragment(v2f input) : SV_Target
            {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                UNITY_SETUP_INSTANCE_ID(input);
                fixed4 atlas = tex2D(_MainTex, input.uv);
                clip(atlas.a - _Cutoff);
                fixed4 instanceTint = UNITY_ACCESS_INSTANCED_PROP(Props, _InstanceTint);
                half3 normal = normalize(input.normalWorld);
                half3 lighting;
                #ifdef LIGHTMAP_ON
                    lighting = DecodeLightmap(UNITY_SAMPLE_TEX2D(unity_Lightmap, input.lightmapUv));
                #else
                    half3 ambient = max(ShadeSH9(half4(normal,1.0h)), 0.0h) * 0.35h;
                    half ndl = saturate(abs(dot(normal, normalize(_WorldSpaceLightPos0.xyz))));
                    half wrap = lerp(_BackfaceLight, 1.0h, ndl);
                    lighting = saturate(ambient + _LightColor0.rgb * (0.06h + wrap * 0.40h));
                #endif
                half atlasBake = lerp(1.0h, dot(atlas.rgb, half3(0.22h,0.68h,0.10h)) * 0.55h + 0.65h,
                                      _BakedAtlasStrength);
                fixed4 output = fixed4(atlas.rgb * _Tint.rgb * instanceTint.rgb * input.color.rgb * lighting * atlasBake, 1.0h);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            
v2f QFPreviousVertex(appdata input, out float4 qfWorld)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = QFObjectToMotionClip(input.vertex, qfWorld);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normalWorld = UnityObjectToWorldNormal(input.normal);
                output.color = input.color;
                #ifdef LIGHTMAP_ON
                    output.lightmapUv = input.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
                #endif
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
    Fallback Off
}
