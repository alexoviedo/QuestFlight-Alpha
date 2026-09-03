Shader "QuestFlight/Semantic Tower Opaque"
{
    Properties
    {
        _Tint ("Shared tower tint", Color) = (1,1,1,1)
        [HideInInspector] _SemanticTowerDiagnosticNight ("Editor night diagnostic", Float) = 0
        [HideInInspector] _SemanticTowerDiagnosticTime ("Editor time override", Float) = -1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 60
        Cull Back
        ZWrite On
        Blend Off
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

            fixed4 _Tint;
            half _SemanticTowerDiagnosticNight;
            half _SemanticTowerDiagnosticTime;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 lightData : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                half3 normal : TEXCOORD0;
                fixed4 color : COLOR;
                float2 lightData : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.color = input.color * _Tint;
                output.lightData = input.lightData;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normal = normalize(input.normal);
                half diffuse = saturate(dot(normal, normalize(_WorldSpaceLightPos0.xyz)));
                fixed3 ambient = ShadeSH9(half4(normal, 1.0));
                fixed3 baseColor = input.color.rgb *
                    (ambient + _LightColor0.rgb * (0.30 + diffuse * 0.70));

                half timeValue = _SemanticTowerDiagnosticTime >= 0.0h
                    ? _SemanticTowerDiagnosticTime : _Time.y;
                half flashing = step(0.76h, frac(timeValue * 0.5h + input.lightData.y));
                half steady = step(1.5h, input.lightData.x);
                half emitter = step(0.5h, input.color.a) * _SemanticTowerDiagnosticNight;
                half redOn = emitter * max(steady, flashing);
                fixed4 output = fixed4(baseColor + fixed3(3.5h, 0.025h, 0.015h) * redOn, 1.0h);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
