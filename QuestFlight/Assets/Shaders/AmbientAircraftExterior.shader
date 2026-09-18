Shader "QuestFlight/Ambient Aircraft Exterior"
{
    Properties { _Tint ("Paint tint", Color) = (1,1,1,1) _PaintAccent ("Paint stripe",Color) = (.11,.23,.32,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; fixed4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION; fixed3 light:TEXCOORD0; fixed4 color:COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            fixed4 _Tint, _PaintAccent;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos=UnityObjectToClipPos(v.vertex);
                float3 n=UnityObjectToWorldNormal(v.normal);
                o.light=max(0.25,ShadeSH9(float4(n,1))) + _LightColor0.rgb * saturate(dot(n,_WorldSpaceLightPos0.xyz));
                fixed stripe=step(.28,v.color.b)*(1-step(.4,v.color.r));
                o.color=fixed4(lerp(v.color.rgb,_PaintAccent.rgb,stripe),v.color.a) * _Tint; return o;
            }
            fixed4 frag(v2f i):SV_Target { return fixed4(i.color.rgb*i.light,1); }
            ENDCG
        }
    }
}
