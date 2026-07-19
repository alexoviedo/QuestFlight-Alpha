Shader "QuestFlight/Aerial Scenery V1/Vegetation Atlas Cutout"
{
    Properties
    {
        _Atlas ("Project-authored color/alpha atlas", 2D) = "white" {}
        [NoScaleOffset] _SurfaceAtlas ("Packed normal XY / roughness / AO", 2D) = "bump" {}
        _Cutoff ("Alpha clip threshold", Range(0,1)) = 0.42
        _Tint ("Global tint", Color) = (1,1,1,1)
        _AmbientStrength ("Ambient strength", Range(0,1)) = 0.56
        _SunWrap ("Sun wrap", Range(0,1)) = 0.28
        _SurfaceOcclusionStrength ("Packed AO strength", Range(0,1)) = 0.72
        _VertexOcclusionStrength ("Vertex AO strength", Range(0,1)) = 0.58
        _NormalStrength ("Packed normal strength", Range(0,1)) = 0.44
        [PerRendererData] _InstanceTint ("Instance tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        LOD 110
        Cull Off
        ZWrite On
        ZTest LEqual
        Blend Off
        AlphaToMask On

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _Atlas;
            float4 _Atlas_ST;
            sampler2D _SurfaceAtlas;
            fixed4 _Tint;
            half _Cutoff;
            half _AmbientStrength;
            half _SunWrap;
            half _SurfaceOcclusionStrength;
            half _VertexOcclusionStrength;
            half _NormalStrength;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _InstanceTint)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                half4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 normalWorld : TEXCOORD1;
                half3 tangentWorld : TEXCOORD2;
                half3 bitangentWorld : TEXCOORD3;
                half3 viewDirectionWorld : TEXCOORD4;
                fixed4 color : COLOR;
                UNITY_FOG_COORDS(5)
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
                output.uv = TRANSFORM_TEX(input.uv, _Atlas);
                output.normalWorld = UnityObjectToWorldNormal(input.normal);
                output.tangentWorld = normalize(UnityObjectToWorldDir(input.tangent.xyz));
                half tangentSign = input.tangent.w * unity_WorldTransformParams.w;
                output.bitangentWorld = normalize(cross(output.normalWorld, output.tangentWorld) * tangentSign);
                float3 worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.viewDirectionWorld = _WorldSpaceCameraPos.xyz - worldPosition;
                output.color = input.color;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(v2f input, fixed facing : VFACE) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                fixed4 atlas = tex2D(_Atlas, input.uv);
                clip(atlas.a - _Cutoff);
                half4 surface = tex2D(_SurfaceAtlas, input.uv);
                half2 normalXY = (surface.rg * 2.0h - 1.0h) * _NormalStrength;
                half3 normalTangent = normalize(half3(normalXY, sqrt(saturate(1.0h - dot(normalXY, normalXY)))));
                half3 normalWorld = normalize(
                    input.tangentWorld * normalTangent.x
                    + input.bitangentWorld * normalTangent.y
                    + input.normalWorld * normalTangent.z);
                normalWorld *= facing >= 0.0h ? 1.0h : -1.0h;

                half3 ambient = max(ShadeSH9(half4(normalWorld, 1.0h)), 0.0h);
                half ndl = dot(normalWorld, normalize(_WorldSpaceLightPos0.xyz));
                half wrapped = saturate((ndl + _SunWrap) / (1.0h + _SunWrap));
                half vertexAO = lerp(1.0h, input.color.a, _VertexOcclusionStrength);
                half packedAO = lerp(1.0h, surface.a, _SurfaceOcclusionStrength);
                half3 lighting = max(0.18h, ambient * _AmbientStrength + _LightColor0.rgb * wrapped * 0.64h);
                half roughness = surface.b;
                half3 halfVector = normalize(normalize(input.viewDirectionWorld) + normalize(_WorldSpaceLightPos0.xyz));
                half specular = pow(saturate(dot(normalWorld, halfVector)), lerp(48.0h, 8.0h, roughness));
                fixed4 instanceTint = UNITY_ACCESS_INSTANCED_PROP(Props, _InstanceTint);
                fixed3 color = atlas.rgb * input.color.rgb * _Tint.rgb * instanceTint.rgb;
                color *= lighting * vertexAO * packedAO;
                color += _LightColor0.rgb * specular * lerp(0.10h, 0.025h, roughness);
                fixed4 output = fixed4(color, 1.0h);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback Off
}
