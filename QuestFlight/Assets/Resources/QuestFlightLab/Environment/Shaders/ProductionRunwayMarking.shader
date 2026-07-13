Shader "QuestFlightLab/Production Runway Marking"
{
    Properties
    {
        _Color ("Faded marking color", Color) = (0.82,0.81,0.75,1)
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+2" }
        LOD 40
        Cull Back
        ZWrite On
        ZTest LEqual
        Offset -1, -1
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
            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; half3 normal : NORMAL; };
            struct v2f { float4 position : SV_POSITION; half3 worldNormal : TEXCOORD0; UNITY_FOG_COORDS(1) };
            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }
            fixed4 frag(v2f input) : SV_Target
            {
                half3 normal = normalize(input.worldNormal);
                half diffuse = saturate(dot(normal, normalize(_WorldSpaceLightPos0.xyz)));
                fixed4 output = fixed4(_Color.rgb * (ShadeSH9(half4(normal, 1.0)) + _LightColor0.rgb * diffuse), 1.0);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
