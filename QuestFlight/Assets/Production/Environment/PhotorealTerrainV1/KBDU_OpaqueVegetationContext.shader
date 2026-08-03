Shader "QuestFlight/KBDU Opaque Vegetation Context"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _GrassAtlas ("Project-authored grass atlas", 2D) = "white" {}
        _GrassCutoff ("Grass alpha cutoff", Range(0,1)) = 0.34
        [PerRendererData] _InstanceTint ("Instance tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 60
        Cull Back
        ZWrite On
        Blend Off
        AlphaToMask On
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            fixed4 _Tint;
            sampler2D _GrassAtlas;
            half _GrassCutoff;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _InstanceTint)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 position : SV_POSITION;
                half3 normal : TEXCOORD0;
                fixed4 color : COLOR;
                float2 grassUv : TEXCOORD1;
                half grassMask : TEXCOORD2;
                UNITY_FOG_COORDS(3)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            v2f vert(appdata input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                fixed4 instanceTint =
                    UNITY_ACCESS_INSTANCED_PROP(Props, _InstanceTint);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.grassMask = step(1.5, input.uv.x);
                output.grassUv = float2(frac(input.uv.x), input.uv.y);
                output.color = input.color * _Tint *
                    lerp(fixed4(1,1,1,1), instanceTint, output.grassMask);
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                fixed4 grass = tex2D(_GrassAtlas, input.grassUv);
                clip(lerp(1.0h, grass.a - _GrassCutoff, input.grassMask));
                half3 normal = normalize(input.normal);
                half diffuse = saturate(dot(normal, normalize(_WorldSpaceLightPos0.xyz)));
                fixed3 ambient = ShadeSH9(half4(normal, 1.0));
                fixed3 surfaceColor = input.color.rgb *
                    lerp(fixed3(1,1,1), grass.rgb, input.grassMask);
                fixed4 output = fixed4(
                    surfaceColor *
                    (ambient + _LightColor0.rgb * (0.30 + diffuse * 0.70)),
                    1.0);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
