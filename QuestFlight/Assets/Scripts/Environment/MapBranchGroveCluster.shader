Shader "QuestFlight/Distant Branch Clusters"
{
    Properties {
        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1
 _ColorAtlas("Baked branch color and coverage",2D)="black" {} _NormalAtlas("Baked branch normals",2D)="gray" {} }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off ZWrite On AlphaToMask On
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
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
            sampler2D _ColorAtlas,_NormalAtlas;
            float4 _GroveViewPosition;
            struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float3 frames:TEXCOORD1;half3 weights:TEXCOORD2;half4 tint:TEXCOORD3;UNITY_FOG_COORDS(4) float2 topRotation:TEXCOORD5;half middleCoverage:TEXCOORD6; UNITY_VERTEX_OUTPUT_STEREO};
            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                Cell c;
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                c=_BranchCells[unity_InstanceID];
                #else
                c.positionHeight=float4(0,0,0,1);c.tintRadius=float4(.5,.5,.5,0);c.rotationFade=float4(0,0,1,0);
                #endif
                float3 direction=normalize(_GroveViewPosition.xyz-c.positionHeight.xyz);
                o.middleCoverage=1-smoothstep(1000,1250,distance(_GroveViewPosition.xyz,c.positionHeight.xyz));
                float3 right=cross(float3(0,1,0),direction);
                right=dot(right,right)<.00001?float3(1,0,0):normalize(right);
                float3 up=cross(direction,right);
                float3 world=c.positionHeight.xyz+(right*v.vertex.x+up*v.vertex.y)*c.positionHeight.w;
                o.pos=mul(UNITY_MATRIX_VP,float4(world,1));o.uv=v.uv;
                float azimuth=frac(atan2(direction.x,direction.z)/6.283185307+1)*8;
                o.topRotation=float2(cos(azimuth*.785398163),sin(azimuth*.785398163));
                float az=floor(azimuth),u=frac(azimuth),next=fmod(az+1,8);
                float elevation=asin(clamp(direction.y,-1,1))*57.2957795;
                float row=elevation<0?0:1;
                float t=elevation<0?saturate((elevation+35)/35):elevation/45;
                if(elevation>=45)
                {
                    t=saturate((elevation-45)/45);o.frames=float3(16+az,16+next,24);
                    o.weights=half3((1-t)*(1-u),(1-t)*u,t);
                }
                else if(u>=t){o.frames=float3(row*8+az,row*8+next,(row+1)*8+next);o.weights=half3(1-u,u-t,t);}
                else {o.frames=float3(row*8+az,(row+1)*8+az,(row+1)*8+next);o.weights=half3(1-t,t-u,u);}
                o.frames+=c.tintRadius.w*25;
                o.tint=half4(GammaToLinearSpace(c.tintRadius.rgb)/.21404114,c.rotationFade.z);
                UNITY_TRANSFER_FOG(o,o.pos);return o;
            }
            float2 AtlasUv(float2 uv,float frame,float2 rotation)
            {
                // Each frame is a full 64px tile with transparent framing. Keep
                // filtering inside its own tile, including the lowest used mip.
                if(fmod(frame,25)>23.5){uv-=.5;uv=float2(uv.x*rotation.x-uv.y*rotation.y,uv.x*rotation.y+uv.y*rotation.x)+.5;}
                return (float2(fmod(frame,32),floor(frame/32))+(saturate(uv)*63+.5)/64)/32;
            }
            void SampleCluster(v2f i,float offset,out half3 color,out half3 normal,out half alpha)
            {
                float2 a=AtlasUv(i.uv,i.frames.x+offset,i.topRotation),b=AtlasUv(i.uv,i.frames.y+offset,i.topRotation),c=AtlasUv(i.uv,i.frames.z+offset,i.topRotation);
                half4 ca=tex2D(_ColorAtlas,a),cb=tex2D(_ColorAtlas,b),cc=tex2D(_ColorAtlas,c);
                half3 weights=i.weights*half3(ca.a,cb.a,cc.a);
                alpha=dot(weights,half3(1,1,1));
                weights/=max(alpha,.001);
                color=ca.rgb*weights.x+cb.rgb*weights.y+cc.rgb*weights.z;
                normal=(tex2D(_NormalAtlas,a).rgb*weights.x+tex2D(_NormalAtlas,b).rgb*weights.y+tex2D(_NormalAtlas,c).rgb*weights.z)*2-1;
            }
            half4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 color,normal;half alpha;
                UNITY_BRANCH if(i.middleCoverage<=.001)SampleCluster(i,0,color,normal,alpha);
                else if(i.middleCoverage>=.999)SampleCluster(i,25,color,normal,alpha);
                else
                {
                    half3 middleColor,middleNormal;half middleAlpha;
                    SampleCluster(i,0,color,normal,alpha);
                    SampleCluster(i,25,middleColor,middleNormal,middleAlpha);
                    half a=alpha*(1-i.middleCoverage),b=middleAlpha*i.middleCoverage;
                    alpha=a+b;
                    color=(color*a+middleColor*b)/max(alpha,.001);
                    normal=(normal*a+middleNormal*b)/max(alpha,.001);
                }
                clip(alpha-.01);normal=normalize(normal);
                half3 light=max(ShadeSH9(half4(normal,1)),half3(.32,.32,.32))+_LightColor0.rgb*(.07+saturate(dot(normal,_WorldSpaceLightPos0.xyz))*.68);
                half4 result=half4(color*i.tint.rgb*light,alpha*i.tint.a);
                UNITY_APPLY_FOG(i.fogCoord,result);return result;
            }
            ENDCG
        }
    
        Pass
        {
            Name "XRMotionVectors"
            Tags { "LightMode"="XRMotionVectors" }
            Cull Off Blend Off ZWrite On ZTest LEqual ColorMask RGBA
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            CGPROGRAM
#define QF_STATIC_WORLD_GEOMETRY 1

            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma instancing_options procedural:Setup
            #include "UnityCG.cginc"
#include "Assets/Shaders/ProductSpaceWarpMotion.cginc"
            #include "Lighting.cginc"
            struct Cell {float4 positionHeight,tintRadius,rotationFade;};
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            StructuredBuffer<Cell> _BranchCells;
            #endif
            void Setup() {}
            sampler2D _ColorAtlas,_NormalAtlas;
            float4 _GroveViewPosition;
            struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;
float3 qfPreviousVertex : TEXCOORD4;
UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float3 frames:TEXCOORD1;half3 weights:TEXCOORD2;half4 tint:TEXCOORD3;UNITY_FOG_COORDS(4) float2 topRotation:TEXCOORD5;half middleCoverage:TEXCOORD6; 
float4 qfCurrent : TEXCOORD14; float4 qfPrevious : TEXCOORD15;
UNITY_VERTEX_OUTPUT_STEREO};
            v2f QFColorVertex(appdata v, out float4 qfWorld)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                Cell c;
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                c=_BranchCells[unity_InstanceID];
                #else
                c.positionHeight=float4(0,0,0,1);c.tintRadius=float4(.5,.5,.5,0);c.rotationFade=float4(0,0,1,0);
                #endif
                float3 direction=normalize(_GroveViewPosition.xyz-c.positionHeight.xyz);
                o.middleCoverage=1-smoothstep(1000,1250,distance(_GroveViewPosition.xyz,c.positionHeight.xyz));
                float3 right=cross(float3(0,1,0),direction);
                right=dot(right,right)<.00001?float3(1,0,0):normalize(right);
                float3 up=cross(direction,right);
                float3 world=c.positionHeight.xyz+(right*v.vertex.x+up*v.vertex.y)*c.positionHeight.w;
                o.pos=QFWorldToMotionClip(float4(world,1),qfWorld);o.uv=v.uv;
                float azimuth=frac(atan2(direction.x,direction.z)/6.283185307+1)*8;
                o.topRotation=float2(cos(azimuth*.785398163),sin(azimuth*.785398163));
                float az=floor(azimuth),u=frac(azimuth),next=fmod(az+1,8);
                float elevation=asin(clamp(direction.y,-1,1))*57.2957795;
                float row=elevation<0?0:1;
                float t=elevation<0?saturate((elevation+35)/35):elevation/45;
                if(elevation>=45)
                {
                    t=saturate((elevation-45)/45);o.frames=float3(16+az,16+next,24);
                    o.weights=half3((1-t)*(1-u),(1-t)*u,t);
                }
                else if(u>=t){o.frames=float3(row*8+az,row*8+next,(row+1)*8+next);o.weights=half3(1-u,u-t,t);}
                else {o.frames=float3(row*8+az,(row+1)*8+az,(row+1)*8+next);o.weights=half3(1-t,t-u,u);}
                o.frames+=c.tintRadius.w*25;
                o.tint=half4(GammaToLinearSpace(c.tintRadius.rgb)/.21404114,c.rotationFade.z);
                UNITY_TRANSFER_FOG(o,o.pos);return o;
            }
            float2 AtlasUv(float2 uv,float frame,float2 rotation)
            {
                // Each frame is a full 64px tile with transparent framing. Keep
                // filtering inside its own tile, including the lowest used mip.
                if(fmod(frame,25)>23.5){uv-=.5;uv=float2(uv.x*rotation.x-uv.y*rotation.y,uv.x*rotation.y+uv.y*rotation.x)+.5;}
                return (float2(fmod(frame,32),floor(frame/32))+(saturate(uv)*63+.5)/64)/32;
            }
            void SampleCluster(v2f i,float offset,out half3 color,out half3 normal,out half alpha)
            {
                float2 a=AtlasUv(i.uv,i.frames.x+offset,i.topRotation),b=AtlasUv(i.uv,i.frames.y+offset,i.topRotation),c=AtlasUv(i.uv,i.frames.z+offset,i.topRotation);
                half4 ca=tex2D(_ColorAtlas,a),cb=tex2D(_ColorAtlas,b),cc=tex2D(_ColorAtlas,c);
                half3 weights=i.weights*half3(ca.a,cb.a,cc.a);
                alpha=dot(weights,half3(1,1,1));
                weights/=max(alpha,.001);
                color=ca.rgb*weights.x+cb.rgb*weights.y+cc.rgb*weights.z;
                normal=(tex2D(_NormalAtlas,a).rgb*weights.x+tex2D(_NormalAtlas,b).rgb*weights.y+tex2D(_NormalAtlas,c).rgb*weights.z)*2-1;
            }
            half4 QFColorFragment(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 color,normal;half alpha;
                UNITY_BRANCH if(i.middleCoverage<=.001)SampleCluster(i,0,color,normal,alpha);
                else if(i.middleCoverage>=.999)SampleCluster(i,25,color,normal,alpha);
                else
                {
                    half3 middleColor,middleNormal;half middleAlpha;
                    SampleCluster(i,0,color,normal,alpha);
                    SampleCluster(i,25,middleColor,middleNormal,middleAlpha);
                    half a=alpha*(1-i.middleCoverage),b=middleAlpha*i.middleCoverage;
                    alpha=a+b;
                    color=(color*a+middleColor*b)/max(alpha,.001);
                    normal=(normal*a+middleNormal*b)/max(alpha,.001);
                }
                clip(alpha-.01);normal=normalize(normal);
                half3 light=max(ShadeSH9(half4(normal,1)),half3(.32,.32,.32))+_LightColor0.rgb*(.07+saturate(dot(normal,_WorldSpaceLightPos0.xyz))*.68);
                half4 result=half4(color*i.tint.rgb*light,alpha*i.tint.a);
                UNITY_APPLY_FOG(i.fogCoord,result);return result;
            }
            
v2f QFPreviousVertex(appdata v, out float4 qfWorld)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                Cell c;
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                c=_BranchCells[unity_InstanceID];
                #else
                c.positionHeight=float4(0,0,0,1);c.tintRadius=float4(.5,.5,.5,0);c.rotationFade=float4(0,0,1,0);
                #endif
                float3 direction=normalize(_QFPreviousGroveViewPosition.xyz-c.positionHeight.xyz);
                o.middleCoverage=1-smoothstep(1000,1250,distance(_QFPreviousGroveViewPosition.xyz,c.positionHeight.xyz));
                float3 right=cross(float3(0,1,0),direction);
                right=dot(right,right)<.00001?float3(1,0,0):normalize(right);
                float3 up=cross(direction,right);
                float3 world=c.positionHeight.xyz+(right*v.vertex.x+up*v.vertex.y)*c.positionHeight.w;
                o.pos=QFWorldToMotionClip(float4(world,1),qfWorld);o.uv=v.uv;
                float azimuth=frac(atan2(direction.x,direction.z)/6.283185307+1)*8;
                o.topRotation=float2(cos(azimuth*.785398163),sin(azimuth*.785398163));
                float az=floor(azimuth),u=frac(azimuth),next=fmod(az+1,8);
                float elevation=asin(clamp(direction.y,-1,1))*57.2957795;
                float row=elevation<0?0:1;
                float t=elevation<0?saturate((elevation+35)/35):elevation/45;
                if(elevation>=45)
                {
                    t=saturate((elevation-45)/45);o.frames=float3(16+az,16+next,24);
                    o.weights=half3((1-t)*(1-u),(1-t)*u,t);
                }
                else if(u>=t){o.frames=float3(row*8+az,row*8+next,(row+1)*8+next);o.weights=half3(1-u,u-t,t);}
                else {o.frames=float3(row*8+az,(row+1)*8+az,(row+1)*8+next);o.weights=half3(1-t,t-u,u);}
                o.frames+=c.tintRadius.w*25;
                o.tint=half4(GammaToLinearSpace(c.tintRadius.rgb)/.21404114,c.rotationFade.z);
                UNITY_TRANSFER_FOG(o,o.pos);return o;
            }
v2f vert(appdata v)
{
    UNITY_SETUP_INSTANCE_ID(v);
    float4 world;
    v2f o=QFColorVertex(v,world);
    #if !defined(QF_STATIC_WORLD_GEOMETRY)
    if (_QFHasPreviousModel < .5 && (_QFPreviousVertices > .5 || unity_MotionVectorsParams.x == 1))
        v.vertex.xyz=v.qfPreviousVertex;
    #endif
    float4 previousWorld;
    v2f old=QFPreviousVertex(v,previousWorld);
    float4 oldWorld=QFPreviousWorld(previousWorld);
    if (_QFResetObjectMotion > .5) oldWorld=world;
    o.qfCurrent=o.pos;
    o.qfPrevious=mul(_PrevViewProjMatrixStereo[QFEye()],oldWorld);
    
    o.pos=o.qfCurrent;
    return o;
}
float4 frag(v2f v) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
    float coverage=QFColorFragment(v).a;
    clip(coverage-0.001);
    return QFMotion(v.qfCurrent,v.qfPrevious,coverage);
}

            ENDCG
        }
}
}
