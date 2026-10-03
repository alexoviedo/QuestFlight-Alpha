Shader "Hidden/QuestFlight/Grove Cluster Bake"
{
    Properties { _LeafAtlas("Leaf",2D)="white" {} _NeedleBranchAtlas("Needle",2D)="white" {} _BakeNormal("Normal output",Float)=0 }
    SubShader
    {
        Cull Off ZWrite On AlphaToMask On
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:Setup
            #include "UnityCG.cginc"
            struct Cell {float4 positionHeight,tintRadius,rotationFade;};
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            StructuredBuffer<Cell> _BranchCells;
            #endif
            void Setup() {}
            sampler2D _LeafAtlas,_NeedleBranchAtlas;float _BakeNormal;
            struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float3 normal:TEXCOORD1;float family:TEXCOORD2;};
            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);v2f o;Cell c;
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                c=_BranchCells[unity_InstanceID];
                #else
                c.positionHeight=float4(0,0,0,12);c.tintRadius=float4(.5,.5,.5,2.25);c.rotationFade=float4(0,1,1,0);
                #endif
                float3 p=v.vertex.xyz*c.tintRadius.w;
                p.xz=float2(p.x*c.rotationFade.y+p.z*c.rotationFade.x,p.z*c.rotationFade.y-p.x*c.rotationFade.x);
                o.family=floor(c.rotationFade.w/16);
                float upper=max(c.positionHeight.w*.6,c.positionHeight.w-1.6);
                p.y+=lerp(upper,c.positionHeight.w*(o.family>.5?.58:.62),v.color.r);p+=c.positionHeight.xyz;
                o.pos=mul(UNITY_MATRIX_VP,float4(p,1));
                float cell=fmod(c.rotationFade.w+v.color.g*255,16);
                float2 center=float2(fmod(cell,4),floor(cell/4))*.25+.125;
                o.uv=center+(v.uv-.5)*.222*(c.tintRadius.w/2.25);
                o.normal=normalize(float3(p.x-c.positionHeight.x,2.4,p.z-c.positionHeight.z));return o;
            }
            half4 frag(v2f i):SV_Target
            {
                half4 leaf=i.family>.5?tex2D(_NeedleBranchAtlas,i.uv):tex2D(_LeafAtlas,i.uv);clip(leaf.a-.3);
                half luminance=max(.035,dot(leaf.rgb,half3(.2126,.7152,.0722)));
                half3 color=.21404114*(leaf.rgb/luminance)*clamp(luminance/.1271,.48,1.6)*.80;
                half coverage=saturate((leaf.a-.3)/max(fwidth(leaf.a),.02)+.5);
                return half4(_BakeNormal>.5?i.normal*.5+.5:color,coverage);
            }
            ENDCG
        }
    }
}
