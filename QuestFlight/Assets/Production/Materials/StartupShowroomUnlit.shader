Shader "QuestFlight/StartupShowroomUnlit"
{
    Properties
    {
        _MainTex ("Backdrop", 2D) = "white" {}
        _UseMainTex ("Use Backdrop", Range(0, 1)) = 0
        _Color ("Base Color", Color) = (0.02, 0.035, 0.06, 1)
        _TopColor ("Top Color", Color) = (0.05, 0.12, 0.19, 1)
        _GradientStrength ("Gradient Strength", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Cull [_Cull]
        ZTest [_ZTest]
        ZWrite [_ZWrite]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float localY : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _UseMainTex;
            fixed4 _Color;
            fixed4 _TopColor;
            float _GradientStrength;

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.localY = saturate(input.vertex.y + 0.5);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 color = lerp(_Color, _TopColor,
                    input.localY * _GradientStrength);
                color *= lerp(fixed4(1, 1, 1, 1), tex2D(_MainTex, input.uv),
                    _UseMainTex);
                color.a = 1;
                return color;
            }
            ENDCG
        }
    }
}
