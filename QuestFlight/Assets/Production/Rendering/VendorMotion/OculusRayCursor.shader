/************************************************************************************
Copyright : Copyright (c) Facebook Technologies, LLC and its affiliates. All rights reserved.

Your use of this SDK or tool is subject to the Oculus SDK License Agreement, available at
https://developer.oculus.com/licenses/oculussdk/

Unless required by applicable law or agreed to in writing, the Utilities SDK distributed
under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF
ANY KIND, either express or implied. See the License for the specific language governing
permissions and limitations under the License.
************************************************************************************/

Shader "QuestFlight/XR/Interaction/OculusRayCursor"
{
    Properties
    {
        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1

        _Radius("Radius", FLOAT) = 0.5
        _OutlineThickness("Outline Thickness", FLOAT) = 0.05
        _Color("Inner Color", Color) = (1,1,1,1)
        _OutlineColor("Outline Color", Color) = (0.5,0.5,0.5,1)
    }

    SubShader
    {
        Tags{ "RenderType" = "Transparent"  "Queue" = "Transparent+10" "IgnoreProjector" = "True"  }
        Cull Off
        ZTest LEqual
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Offset -5, -5
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            uniform float _Radius;
            uniform float _OutlineThickness;
            uniform float4 _Color;
            uniform float4 _OutlineColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv: TEXCOORD0;
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float dist = distance(i.uv, float2(0.5, 0.5));
                float2 ddDist = float2(ddx(dist), ddy(dist));
                float ddDistLen = rcp(length(ddDist));

                float4 innerColor = _Color;
                float4 borderColor = _OutlineColor;

                float outerRadius = _Radius;
                float innerRadius = _OutlineThickness - _Radius;

                float outerDist = dist - outerRadius;
                float outerDistOverLen = outerDist * ddDistLen;

                float innerDist = dist + innerRadius;
                float innerDistOverLen = innerDist * ddDistLen;
                
                float colorLerpParam = saturate(innerDistOverLen);
                float4 fragColor = lerp(innerColor, borderColor, colorLerpParam);
                fragColor.a *= (1.0 - saturate(outerDistOverLen));
                return fragColor;
            }
            
#pragma multi_compile_instancing
ENDCG
        }
    
        Pass
        {
            Name "XRMotionVectors"
            Tags { "LightMode"="XRMotionVectors" }
            Cull Off Blend Off ZWrite On ZTest LEqual ColorMask RGBA
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            CGPROGRAM

#pragma target 4.5

            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
#include "Assets/Shaders/ProductSpaceWarpMotion.cginc"

            uniform float _Radius;
            uniform float _OutlineThickness;
            uniform float4 _Color;
            uniform float4 _OutlineColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                
float3 qfPreviousVertex : TEXCOORD4;
UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv: TEXCOORD0;
                float4 vertex : SV_POSITION;
                
float4 qfCurrent : TEXCOORD14; float4 qfPrevious : TEXCOORD15;
UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f QFColorVertex(appdata v, out float4 qfWorld)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = QFObjectToMotionClip(v.vertex, qfWorld);
                o.uv = v.uv;
                return o;
            }

            fixed4 QFColorFragment(v2f i) : SV_Target
            {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float dist = distance(i.uv, float2(0.5, 0.5));
                float2 ddDist = float2(ddx(dist), ddy(dist));
                float ddDistLen = rcp(length(ddDist));

                float4 innerColor = _Color;
                float4 borderColor = _OutlineColor;

                float outerRadius = _Radius;
                float innerRadius = _OutlineThickness - _Radius;

                float outerDist = dist - outerRadius;
                float outerDistOverLen = outerDist * ddDistLen;

                float innerDist = dist + innerRadius;
                float innerDistOverLen = innerDist * ddDistLen;
                
                float colorLerpParam = saturate(innerDistOverLen);
                float4 fragColor = lerp(innerColor, borderColor, colorLerpParam);
                fragColor.a *= (1.0 - saturate(outerDistOverLen));
                return fragColor;
            }
            
#pragma multi_compile_instancing

v2f QFPreviousVertex(appdata v, out float4 qfWorld)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = QFObjectToMotionClip(v.vertex, qfWorld);
                o.uv = v.uv;
                return o;
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
    o.qfCurrent=o.vertex;
    o.qfPrevious=mul(_PrevViewProjMatrixStereo[QFEye()],oldWorld);
    
    o.vertex=o.qfCurrent;
    return o;
}
float4 frag(v2f v) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
    float coverage=QFColorFragment(v).a;
    clip(coverage-0.5);
    return QFMotion(v.qfCurrent,v.qfPrevious,coverage);
}

            ENDCG
        }
}
    Fallback "Diffuse"
}
