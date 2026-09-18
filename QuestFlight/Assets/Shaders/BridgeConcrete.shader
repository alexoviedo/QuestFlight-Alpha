Shader "QuestFlight/Bridge Concrete"
{
 Properties { _Color("Concrete",Color)=(.46,.44,.40,1) }
 SubShader { Tags { "RenderType"="Opaque" } Pass {
 Tags { "LightMode"="ForwardBase" }
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #pragma multi_compile_fog
 #include "UnityCG.cginc"
 #include "Lighting.cginc"
 fixed4 _Color;
 struct Input { float4 vertex:POSITION; float3 normal:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
 struct Output { float4 pos:SV_POSITION; fixed3 light:TEXCOORD0; UNITY_FOG_COORDS(1) UNITY_VERTEX_OUTPUT_STEREO };
 Output vert(Input v) { Output o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(Output,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos=UnityObjectToClipPos(v.vertex); float3 n=UnityObjectToWorldNormal(v.normal); o.light=max(.2,ShadeSH9(float4(n,1)))+_LightColor0.rgb*saturate(dot(n,_WorldSpaceLightPos0.xyz)); UNITY_TRANSFER_FOG(o,o.pos); return o; }
 fixed4 frag(Output i):SV_Target { fixed4 c=fixed4(_Color.rgb*i.light,1); UNITY_APPLY_FOG(i.fogCoord,c); return c; }
 ENDCG
 } }
}
