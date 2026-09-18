Shader "QuestFlight/Map Branch Groves"
{
    Properties { _LeafAtlas("Existing branch artwork",2D)="white" {} _NeedleBranchAtlas("CC0 needle branch sprays",2D)="white" {} _Cutoff("Leaf cutoff",Range(0,1))=.3 _Diagnostic("Visibility diagnostic",Float)=0 }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off ZWrite On AlphaToMask On
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma instancing_options procedural:Setup
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            struct Cell {float4 positionHeight,tintRadius,rotationFade;};
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            StructuredBuffer<Cell> _BranchCells;
            #endif
            void Setup() {}
            sampler2D _LeafAtlas,_NeedleBranchAtlas; half _Cutoff,_Diagnostic;
            int _BranchOffset;
            float4 _GroveViewPosition;
            float4 _GroveLodRanges;
            struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;half4 tint:TEXCOORD1;half3 light:TEXCOORD2;UNITY_FOG_COORDS(3) half family:TEXCOORD4; UNITY_VERTEX_OUTPUT_STEREO};
            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                Cell c;
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                c=_BranchCells[unity_InstanceID+_BranchOffset];
                #else
                c.positionHeight=float4(0,0,0,10);c.tintRadius=float4(.4,.5,.25,2.25);c.rotationFade=float4(0,1,1,0);
                #endif
                float3 p=v.vertex.xyz*c.tintRadius.w;
                p.xz=float2(p.x*c.rotationFade.y+p.z*c.rotationFade.x,p.z*c.rotationFade.y-p.x*c.rotationFade.x);
                o.family=floor(c.rotationFade.w/16);
                float upper=max(c.positionHeight.w*.6,c.positionHeight.w-1.6);
                // Heights now follow rounded/tapered crown profiles around real
                // anchors. Lower branch masses fill depth without another layer.
                p.y+=lerp(upper,c.positionHeight.w*(o.family>.5?.58:.62),v.color.r);
                p+=c.positionHeight.xyz;
                o.pos=mul(UNITY_MATRIX_VP,float4(p,1));
                // Reuse branch patches at fixed scale, cropping only at edges.
                float cell=fmod(c.rotationFade.w+v.color.g*255,16);
                float2 center=float2(fmod(cell,4),floor(cell/4))*.25+.125;
                o.uv=center+(v.uv-.5)*.222*(c.tintRadius.w/2.25);
                float distanceToCrown=distance(_GroveViewPosition.xyz,c.positionHeight.xyz+float3(0,c.positionHeight.w*.75,0));
                // Fade redundant cards before the CPU removes their triangles.
                // Use one center-eye distance for identical left/right coverage.
                float cardCoverage=1;
                if(v.color.r>.5)cardCoverage=1-smoothstep(_GroveLodRanges.x,_GroveLodRanges.y,distanceToCrown);
                else if(v.color.g*255>9)cardCoverage=1-smoothstep(_GroveLodRanges.z,_GroveLodRanges.w,distanceToCrown);
                o.tint=half4(GammaToLinearSpace(c.tintRadius.rgb),c.rotationFade.z*cardCoverage);
                half3 normal=normalize(half3(p.x-c.positionHeight.x,2.4,p.z-c.positionHeight.z));
                half diffuse=saturate(dot(normal,_WorldSpaceLightPos0.xyz));
                o.light=(max(ShadeSH9(half4(normal,1)),half3(.32,.32,.32))+_LightColor0.rgb*(.07+diffuse*.68))*lerp(1,.64,v.color.r);
                UNITY_TRANSFER_FOG(o,o.pos);return o;
            }
            half4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(i.tint.a-.001);
                half4 leaf;
                UNITY_BRANCH if(i.family>.5)leaf=tex2D(_NeedleBranchAtlas,i.uv);
                else leaf=tex2D(_LeafAtlas,i.uv);
                clip(leaf.a-_Cutoff);
                half luminance=max(.035,dot(leaf.rgb,half3(.2126,.7152,.0722)));
                half3 color=i.tint.rgb*(leaf.rgb/luminance)*clamp(luminance/.1271,.48,1.6)*.80*i.light;
                half coverage=saturate((leaf.a-_Cutoff)/max(fwidth(leaf.a),.02)+.5)*i.tint.a;
                half4 result=half4(lerp(color,half3(1,0,1),saturate(_Diagnostic)),coverage);
                UNITY_APPLY_FOG(i.fogCoord,result);return result;
            }
            ENDCG
        }
    }
}
