Shader "QuestFlight/SGS Canopy Glass Reflection"
{
    Properties { _BaseColor("Glazing tint",Color)=(.64,.77,.82,.055) }
    SubShader
    {
        Tags { "Queue"="Transparent+12" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            struct appdata {float4 vertex:POSITION;float3 normal:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 position:SV_POSITION;float3 world:TEXCOORD0;half3 normal:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
            fixed4 _BaseColor;
            v2f vert(appdata v)
            {
                v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position=UnityObjectToClipPos(v.vertex);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;o.normal=UnityObjectToWorldNormal(v.normal);return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 n=normalize(i.normal),view=normalize(_WorldSpaceCameraPos-i.world);
                half edge=pow(1-saturate(abs(dot(n,view))),5);
                half3 reflected=reflect(-view,n);
                half sky=saturate(reflected.y*.5+.5);
                half glint=pow(saturate(dot(reflected,normalize(_WorldSpaceLightPos0.xyz))),96)*.12;
                half3 color=lerp(half3(.22,.27,.26),_BaseColor.rgb,sky)+_LightColor0.rgb*glint;
                return fixed4(color,clamp(.025+edge*.25+glint,0,.36));
            }
            ENDCG
        }
        // Same background-owned motion/depth policy as the existing C172 window
        // reflection layers. A clear canopy must not occlude the world depth.
    }
}
