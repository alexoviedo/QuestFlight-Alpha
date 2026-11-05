Shader "QuestFlight/SGS Reference Surface"
{
    Properties
    {
        _Tint("Surface tint",Color)=(1,1,1,1)
        _SpecularStrength("Satin highlight",Range(0,0.3))=0.12
        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; fixed4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 position:SV_POSITION; float3 world:TEXCOORD0; half3 normal:TEXCOORD1; half3 ambient:TEXCOORD2; fixed4 color:COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            fixed4 _Tint;
            half _SpecularStrength;
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position=UnityObjectToClipPos(v.vertex);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;
                o.normal=UnityObjectToWorldNormal(v.normal);o.ambient=max(.16,ShadeSH9(float4(o.normal,1)));
                o.color=v.color*_Tint;return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 n=normalize(i.normal),l=normalize(_WorldSpaceLightPos0.xyz);
                half3 view=normalize(_WorldSpaceCameraPos-i.world),h=normalize(l+view);
                half ndl=saturate(dot(n,l));
                // Vertex alpha carries roughness for opaque donor repairs.
                half rough=clamp(i.color.a,.3,1);
                half highlight=pow(saturate(dot(n,h)),lerp(72,10,rough))*ndl*(1-rough)*_SpecularStrength;
                return fixed4(i.color.rgb*(i.ambient+_LightColor0.rgb*ndl)+_LightColor0.rgb*highlight,1);
            }
            ENDCG
        }
        // Opaque matching geometry; preserve the existing stereo motion/depth
        // policy without enabling SpaceWarp or adding a reflection camera.
        UsePass "QuestFlight/Ambient Aircraft Exterior/XRMotionVectors"
    }
}
