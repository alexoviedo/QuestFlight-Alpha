Shader "QuestFlight/KBDU Traffic Vehicle"
{
    Properties
    {
        _BodyTint ("Default Body Tint", Color) = (0.35,0.38,0.42,1)
        _SurfaceDetail ("Shared Surface Detail", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 80
        Cull Back
        ZWrite On

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _SurfaceDetail;
            UNITY_INSTANCING_BUFFER_START(TrafficVehicle)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BodyTint)
            UNITY_INSTANCING_BUFFER_END(TrafficVehicle)

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                half3 worldNormal : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
                half3 semantic : TEXCOORD2;
                half2 detailUv : TEXCOORD3;
                half3 bodyTint : TEXCOORD4;
                UNITY_FOG_COORDS(5)
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
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.semantic = saturate(input.color.rgb);
                float4 tint = UNITY_ACCESS_INSTANCED_PROP(
                    TrafficVehicle,
                    _BodyTint);
                output.bodyTint = tint.rgb;
                output.detailUv =
                    input.vertex.xz * half2(0.73, 0.37) +
                    tint.rb * half2(1.91, 2.37);
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Imported CC0 derivatives encode one exclusive surface class:
                // R body, G glass, B tire, RG trim, RB headlamp, GB tail lamp.
                half3 code = step(0.5h, input.semantic);
                half body = code.r * (1.0h - code.g) * (1.0h - code.b);
                half glass = code.g * (1.0h - code.r) * (1.0h - code.b);
                half tire = code.b * (1.0h - code.r) * (1.0h - code.g);
                half trim = code.r * code.g * (1.0h - code.b);
                half headlamp = code.r * code.b * (1.0h - code.g);
                half taillamp = code.g * code.b * (1.0h - code.r);

                fixed4 detail = tex2D(_SurfaceDetail, input.detailUv);
                fixed3 bodyColor =
                    input.bodyTint * lerp(0.88h, 1.08h, detail.r);
                fixed3 baseColor =
                    bodyColor * body +
                    fixed3(0.055h, 0.085h, 0.105h) * glass +
                    fixed3(0.018h, 0.019h, 0.020h) * tire +
                    fixed3(0.24h, 0.25h, 0.26h) * trim +
                    fixed3(0.94h, 0.87h, 0.62h) * headlamp +
                    fixed3(0.52h, 0.025h, 0.018h) * taillamp;

                half3 normal = normalize(input.worldNormal);
                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half diffuse = saturate(dot(normal, lightDirection));
                fixed3 ambient = ShadeSH9(half4(normal, 1.0h));
                fixed3 lighting =
                    ambient +
                    _LightColor0.rgb * (0.18h + diffuse * 0.82h);

                half3 viewDirection =
                    normalize(_WorldSpaceCameraPos.xyz - input.worldPosition);
                half3 halfDirection =
                    normalize(lightDirection + viewDirection);
                half ndh = saturate(dot(normal, halfDirection));
                half bodySpecular = pow(ndh, lerp(18.0h, 36.0h, detail.g));
                half glassSpecular = pow(ndh, 42.0h);
                half fresnel = pow(
                    1.0h - saturate(dot(normal, viewDirection)),
                    3.0h);
                fixed3 specular =
                    _LightColor0.rgb *
                    (
                        body * bodySpecular * 0.20h +
                        trim * bodySpecular * 0.12h +
                        glass * (glassSpecular * 0.42h + fresnel * 0.18h) +
                        (headlamp + taillamp) * bodySpecular * 0.16h
                    );

                fixed4 output = fixed4(baseColor * lighting + specular, 1.0h);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
