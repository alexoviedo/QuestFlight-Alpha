/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

Shader "QuestFlight/XR/Interaction/OculusHand"
{
    Properties
    {
        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1

        [Header(General)]
        _ColorTop("Color Top", Color) = (0.1960784, 0.2039215, 0.2117647, 1)
        _ColorBottom("Color Bottom", Color) = (0.1215686, 0.1254902, 0.1294117, 1)
        _Opacity("Opacity", Range(0 , 1)) = 0.8

        [Header(Fresnel)]
        _FresnelPower("FresnelPower", Range(0 , 5)) = 0.16

        [Header(Outline)]
        _OutlineColor("Outline Color", Color) = (0.5377358,0.5377358,0.5377358,1)
        _OutlineJointColor("Outline Joint Error Color", Color) = (1,0,0,1)
        _OutlineWidth("Outline Width", Range(0 , 0.005)) = 0.00134
        _OutlineOpacity("Outline Opacity", Range(0 , 1)) = 0.4

        [Header(Wrist)]
        _WristFade("Wrist Fade", Range(0 , 1)) = 0.5

        [Header(Finger Glow)]
        _FingerGlowMask("Finger Glow Mask", 2D) = "white" {}

        [HideInInspector] _texcoord("", 2D) = "white" {}
        _GlowColor("GlowColor", Color) = (1,1,1,1)
        [HideInInspector] _ThumbGlowValue("", Float) = 0
        [HideInInspector] _IndexGlowValue("", Float) = 0
        [HideInInspector] _MiddleGlowValue("", Float) = 0
        [HideInInspector] _RingGlowValue("", Float) = 0
        [HideInInspector] _PinkyGlowValue("", Float) = 0
        [HideInInspector] _GenerateGlow("", Int) = 0
        [HideInInspector] _OcclusionEnabled("", Int) = 0 
    }

    CGINCLUDE
    #include "UnityCG.cginc"

#pragma target 2.0

    // CBUFFER named UnityPerMaterial, SRP can cache the material properties between frames and reduce significantly the cost of each drawcall.
    CBUFFER_START(UnityPerMaterial)
    // General
    uniform float4 _ColorTop;
    uniform float4 _ColorBottom;
    uniform float _Opacity;
    uniform float _FresnelPower;

    // Outline
    uniform float4 _OutlineColor;
    uniform half4 _OutlineJointColor;
    uniform float _OutlineWidth;
    uniform float _OutlineOpacity;

    // Wrist
    uniform half _WristFade; 

    // Finger Glow
    uniform sampler2D _FingerGlowMask;

    uniform float _ThumbGlowValue;
    uniform float _IndexGlowValue;
    uniform float _MiddleGlowValue;
    uniform float _RingGlowValue;
    uniform float _PinkyGlowValue;

    uniform int _FingerGlowIndex;
    
    uniform int _GenerateGlow;
    uniform float3 _GlowColor;
    
    uniform float3 _GlowPosition;
    uniform float _GlowParameter;
    uniform float _GlowMaxLength;
    uniform int _GlowType;

    //Finger Masks
    //Finger Tip To Knuckles
    uniform float4 _ThumbLine;
    uniform float4 _IndexLine;
    uniform float4 _MiddleLine;
    uniform float4 _RingLine;
    uniform float4 _PinkyLine;
    //Finger Tip To Palm
    uniform float4 _PalmThumbLine;
    uniform float4 _PalmIndexLine;
    uniform float4 _PalmMiddleLine;
    uniform float4 _PalmRingLine;
    uniform float4 _PalmPinkyLine;

    CBUFFER_END
    ENDCG

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True"
        }

        Cull Back
        AlphaToMask Off

        Pass
        {
            Name "Depth"
            ZWrite On
            ColorMask 0
        }

        Pass
        {
            Name "HandOutline"
            Tags
            {
                "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True"
            }

            Cull Front
            Blend SrcAlpha OneMinusSrcAlpha, OneMinusDstAlpha One
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex outlineVertex
#pragma fragment outlineFragment
#include "Packages/com.meta.xr.sdk.interaction/Runtime/Shaders/OculusHandOutlineCG.cginc"
            ENDCG
        }

        Pass
        {
            PackageRequirements { "com.unity.render-pipelines.universal": "10.0.0" }
            Name "HandOutlineURP"
            Tags
            {
                "LightMode" = "UniversalForwardOnly" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline"
            }

            Cull Front
            Blend SrcAlpha OneMinusSrcAlpha, OneMinusDstAlpha One
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex outlineVertex
#pragma fragment outlineFragment
#include "Packages/com.meta.xr.sdk.interaction/Runtime/Shaders/OculusHandOutlineCG.cginc"
            ENDCG
        }

        Pass
        {
            Name "HandFill"
            Tags
            {
                "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True"
            }

            Blend SrcAlpha OneMinusSrcAlpha, OneMinusDstAlpha One
            ZWrite Off
            ZTest LEqual
            Stencil 
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex baseVertex
#pragma fragment baseFragment
/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight

#pragma prefer_hlslcc gles
#pragma exclude_renderers d3d11_9x
#pragma target 2.0

//

struct VertexInput {
  float4 vertex : POSITION;
  half3 normal : NORMAL;
  half4 vertexColor : COLOR;
  float4 texcoord : TEXCOORD0;
  float4 texcoord1 : TEXCOORD1;
  UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput {
  float4 vertex : SV_POSITION;
  float3 worldPos : TEXCOORD1;
  float3 worldNormal : TEXCOORD2;
  half4 glowColor : COLOR;
  float4 texcoord1 : TEXCOORD3;
  UNITY_VERTEX_INPUT_INSTANCE_ID
  UNITY_VERTEX_OUTPUT_STEREO
};

VertexOutput baseVertex(VertexInput v) {
  VertexOutput o;
  UNITY_SETUP_INSTANCE_ID(v);
  UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
  UNITY_TRANSFER_INSTANCE_ID(v, o);

  o.worldPos = mul(unity_ObjectToWorld, v.vertex);
  o.worldNormal = UnityObjectToWorldNormal(v.normal);
  o.vertex = UnityObjectToClipPos(v.vertex);
  o.texcoord1 = v.texcoord1;
  half4 maskPixelColor = tex2Dlod( _FingerGlowMask, float4(v.texcoord.xy, 0.0, 0.0));
  o.glowColor.rgb = float3(0.0, 0.0, 0.0);
  o.glowColor.a = saturate(maskPixelColor.a + _WristFade) * _Opacity;
  return o;
}

#include "Packages/com.meta.xr.sdk.interaction/Runtime/Shaders/GlowFunctions.cginc"

void getFingerStrength(out float fingerStrength[5]) {
  float strengthValuesUniforms[5] = {
      _ThumbGlowValue,
      _IndexGlowValue,
      _MiddleGlowValue,
      _RingGlowValue,
      _PinkyGlowValue
  };
  fingerStrength = strengthValuesUniforms;
}

void getFillFingerLines(out float4 lines[5]) {
  float4 _lines[5] = {
      _ThumbLine,
      _IndexLine,
      _MiddleLine,
      _RingLine,
      _PinkyLine
  };
  lines = _lines;
}

float4 getFillFingerLineByIndex(int index) {
  if(index == 0) { return _ThumbLine; }
  if(index == 1) { return _IndexLine; }
  if(index == 2) { return _MiddleLine; }
  if(index == 3) { return _RingLine; }
  if(index == 4) { return _PinkyLine; }
  return float4(0.0,0.0,0.0,0.0);
}

half4 applyGlow(int glowType, float3 color, float alpha, float2 texCoord, float3 worldPosition) {
  if (glowType == 30 || glowType == 32) {
    float4 gradientLine = getFillFingerLineByIndex(_FingerGlowIndex);
    float2 gradientRadius = getFingerRadiusByIndex(_FingerGlowIndex);
    bool useGlow;
    float2 fingerGradient = movingFingerGradient(texCoord, gradientLine, gradientRadius, _GlowParameter, _GlowMaxLength, useGlow);
    if (useGlow) {
      float param = 1.0 - fingerGradient.y;
      float glowValue = saturate(param * param * step(0.0, param));
      return half4(color + _GlowColor * glowValue, alpha);
    }else {
      return half4(color, alpha);
    }
  }
  else if (glowType == 27 || glowType == 29) {
    float fingerStrength[5];
    getFingerStrength(fingerStrength);
    float4 lines[5];
    getFillFingerLines(lines);
    float2 fingerRadius[5];
    getFingerRadius(fingerRadius);
    float glowValue = movingFingersGradient(texCoord, fingerStrength, _GlowParameter, lines, fingerRadius);
    return half4(color + _GlowColor * glowValue, alpha);
  }
  else if (glowType == 16 || glowType == 17) {
    float gradient = invertedSphereGradient(_GlowPosition, worldPosition, _GlowMaxLength);
    float3 glowColor = _GlowColor * gradient * _GlowParameter;
    return float4(saturate(color + glowColor), alpha);
  }
  else if (glowType == 11 || glowType == 15) {
    float gradient = movingSphereGradient(_GlowPosition, worldPosition, _GlowMaxLength, _GlowParameter, 1.5);
    float3 glowColor = _GlowColor * gradient;
    return float4(saturate(color + glowColor), alpha);
  } 
  else {
    return half4(color, alpha);
  }
}

half4 baseFragment(VertexOutput i) : SV_Target {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
  float3 worldViewDir = normalize(UnityWorldSpaceViewDir(i.worldPos));
  float fresnelNdot = dot(i.worldNormal, worldViewDir);
  float fresnel = 1.0 * pow(1.0 - fresnelNdot, _FresnelPower);
  float4 color = lerp(_ColorTop, _ColorBottom, fresnel);
  
  if (_GenerateGlow == 1) {
    return applyGlow(_GlowType, color.rgb, i.glowColor.a, i.texcoord1, i.worldPos);
  } else {
    return half4(color.rgb, i.glowColor.a);
  }
}

            
#pragma multi_compile_instancing
ENDCG
        }

        Pass
        {
            PackageRequirements { "com.unity.render-pipelines.universal": "10.0.0" }
            Name "HandFillURP"
            Tags
            {
                "LightMode" = "UniversalForward" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline"
            }

            Blend SrcAlpha OneMinusSrcAlpha, OneMinusDstAlpha One
            ZWrite Off
            ZTest LEqual
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex baseVertex
#pragma fragment baseFragment
/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight

#pragma prefer_hlslcc gles
#pragma exclude_renderers d3d11_9x
#pragma target 2.0

//

struct VertexInput {
  float4 vertex : POSITION;
  half3 normal : NORMAL;
  half4 vertexColor : COLOR;
  float4 texcoord : TEXCOORD0;
  float4 texcoord1 : TEXCOORD1;
  UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput {
  float4 vertex : SV_POSITION;
  float3 worldPos : TEXCOORD1;
  float3 worldNormal : TEXCOORD2;
  half4 glowColor : COLOR;
  float4 texcoord1 : TEXCOORD3;
  UNITY_VERTEX_INPUT_INSTANCE_ID
  UNITY_VERTEX_OUTPUT_STEREO
};

VertexOutput baseVertex(VertexInput v) {
  VertexOutput o;
  UNITY_SETUP_INSTANCE_ID(v);
  UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
  UNITY_TRANSFER_INSTANCE_ID(v, o);

  o.worldPos = mul(unity_ObjectToWorld, v.vertex);
  o.worldNormal = UnityObjectToWorldNormal(v.normal);
  o.vertex = UnityObjectToClipPos(v.vertex);
  o.texcoord1 = v.texcoord1;
  half4 maskPixelColor = tex2Dlod( _FingerGlowMask, float4(v.texcoord.xy, 0.0, 0.0));
  o.glowColor.rgb = float3(0.0, 0.0, 0.0);
  o.glowColor.a = saturate(maskPixelColor.a + _WristFade) * _Opacity;
  return o;
}

#include "Packages/com.meta.xr.sdk.interaction/Runtime/Shaders/GlowFunctions.cginc"

void getFingerStrength(out float fingerStrength[5]) {
  float strengthValuesUniforms[5] = {
      _ThumbGlowValue,
      _IndexGlowValue,
      _MiddleGlowValue,
      _RingGlowValue,
      _PinkyGlowValue
  };
  fingerStrength = strengthValuesUniforms;
}

void getFillFingerLines(out float4 lines[5]) {
  float4 _lines[5] = {
      _ThumbLine,
      _IndexLine,
      _MiddleLine,
      _RingLine,
      _PinkyLine
  };
  lines = _lines;
}

float4 getFillFingerLineByIndex(int index) {
  if(index == 0) { return _ThumbLine; }
  if(index == 1) { return _IndexLine; }
  if(index == 2) { return _MiddleLine; }
  if(index == 3) { return _RingLine; }
  if(index == 4) { return _PinkyLine; }
  return float4(0.0,0.0,0.0,0.0);
}

half4 applyGlow(int glowType, float3 color, float alpha, float2 texCoord, float3 worldPosition) {
  if (glowType == 30 || glowType == 32) {
    float4 gradientLine = getFillFingerLineByIndex(_FingerGlowIndex);
    float2 gradientRadius = getFingerRadiusByIndex(_FingerGlowIndex);
    bool useGlow;
    float2 fingerGradient = movingFingerGradient(texCoord, gradientLine, gradientRadius, _GlowParameter, _GlowMaxLength, useGlow);
    if (useGlow) {
      float param = 1.0 - fingerGradient.y;
      float glowValue = saturate(param * param * step(0.0, param));
      return half4(color + _GlowColor * glowValue, alpha);
    }else {
      return half4(color, alpha);
    }
  }
  else if (glowType == 27 || glowType == 29) {
    float fingerStrength[5];
    getFingerStrength(fingerStrength);
    float4 lines[5];
    getFillFingerLines(lines);
    float2 fingerRadius[5];
    getFingerRadius(fingerRadius);
    float glowValue = movingFingersGradient(texCoord, fingerStrength, _GlowParameter, lines, fingerRadius);
    return half4(color + _GlowColor * glowValue, alpha);
  }
  else if (glowType == 16 || glowType == 17) {
    float gradient = invertedSphereGradient(_GlowPosition, worldPosition, _GlowMaxLength);
    float3 glowColor = _GlowColor * gradient * _GlowParameter;
    return float4(saturate(color + glowColor), alpha);
  }
  else if (glowType == 11 || glowType == 15) {
    float gradient = movingSphereGradient(_GlowPosition, worldPosition, _GlowMaxLength, _GlowParameter, 1.5);
    float3 glowColor = _GlowColor * gradient;
    return float4(saturate(color + glowColor), alpha);
  } 
  else {
    return half4(color, alpha);
  }
}

half4 baseFragment(VertexOutput i) : SV_Target {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
  float3 worldViewDir = normalize(UnityWorldSpaceViewDir(i.worldPos));
  float fresnelNdot = dot(i.worldNormal, worldViewDir);
  float fresnel = 1.0 * pow(1.0 - fresnelNdot, _FresnelPower);
  float4 color = lerp(_ColorTop, _ColorBottom, fresnel);
  
  if (_GenerateGlow == 1) {
    return applyGlow(_GlowType, color.rgb, i.glowColor.a, i.texcoord1, i.worldPos);
  } else {
    return half4(color.rgb, i.glowColor.a);
  }
}

            
#pragma multi_compile_instancing
ENDCG
        }
    
        Pass
        {
            Name "XRMotionVectors"
            Tags { "LightMode"="XRMotionVectors" }
            Cull Back Blend Off ZWrite On ZTest LEqual ColorMask RGBA
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            CGPROGRAM

#include "UnityCG.cginc"
#include "Assets/Shaders/ProductSpaceWarpMotion.cginc"

            #pragma vertex baseVertex
#pragma fragment baseFragment
/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#pragma multi_compile_fwdbase nolightmap nodirlightmap nodynlightmap novertexlight

#pragma prefer_hlslcc gles
#pragma exclude_renderers d3d11_9x
#pragma target 4.5

//

struct VertexInput {
  float4 vertex : POSITION;
  half3 normal : NORMAL;
  half4 vertexColor : COLOR;
  float4 texcoord : TEXCOORD0;
  float4 texcoord1 : TEXCOORD1;
  
float3 qfPreviousVertex : TEXCOORD4;
UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput {
  float4 vertex : SV_POSITION;
  float3 worldPos : TEXCOORD1;
  float3 worldNormal : TEXCOORD2;
  half4 glowColor : COLOR;
  float4 texcoord1 : TEXCOORD3;
  UNITY_VERTEX_INPUT_INSTANCE_ID
  
float4 qfCurrent : TEXCOORD14; float4 qfPrevious : TEXCOORD15;
UNITY_VERTEX_OUTPUT_STEREO
};

VertexOutput QFColorVertex(VertexInput v, out float4 qfWorld) {
  VertexOutput o;
  UNITY_SETUP_INSTANCE_ID(v);
  UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
  UNITY_TRANSFER_INSTANCE_ID(v, o);

  o.worldPos = mul(unity_ObjectToWorld, v.vertex);
  o.worldNormal = UnityObjectToWorldNormal(v.normal);
  o.vertex = QFObjectToMotionClip(v.vertex, qfWorld);
  o.texcoord1 = v.texcoord1;
  half4 maskPixelColor = tex2Dlod( _FingerGlowMask, float4(v.texcoord.xy, 0.0, 0.0));
  o.glowColor.rgb = float3(0.0, 0.0, 0.0);
  o.glowColor.a = saturate(maskPixelColor.a + _WristFade) * _Opacity;
  return o;
}

#include "Packages/com.meta.xr.sdk.interaction/Runtime/Shaders/GlowFunctions.cginc"

void getFingerStrength(out float fingerStrength[5]) {
  float strengthValuesUniforms[5] = {
      _ThumbGlowValue,
      _IndexGlowValue,
      _MiddleGlowValue,
      _RingGlowValue,
      _PinkyGlowValue
  };
  fingerStrength = strengthValuesUniforms;
}

void getFillFingerLines(out float4 lines[5]) {
  float4 _lines[5] = {
      _ThumbLine,
      _IndexLine,
      _MiddleLine,
      _RingLine,
      _PinkyLine
  };
  lines = _lines;
}

float4 getFillFingerLineByIndex(int index) {
  if(index == 0) { return _ThumbLine; }
  if(index == 1) { return _IndexLine; }
  if(index == 2) { return _MiddleLine; }
  if(index == 3) { return _RingLine; }
  if(index == 4) { return _PinkyLine; }
  return float4(0.0,0.0,0.0,0.0);
}

half4 applyGlow(int glowType, float3 color, float alpha, float2 texCoord, float3 worldPosition) {
  if (glowType == 30 || glowType == 32) {
    float4 gradientLine = getFillFingerLineByIndex(_FingerGlowIndex);
    float2 gradientRadius = getFingerRadiusByIndex(_FingerGlowIndex);
    bool useGlow;
    float2 fingerGradient = movingFingerGradient(texCoord, gradientLine, gradientRadius, _GlowParameter, _GlowMaxLength, useGlow);
    if (useGlow) {
      float param = 1.0 - fingerGradient.y;
      float glowValue = saturate(param * param * step(0.0, param));
      return half4(color + _GlowColor * glowValue, alpha);
    }else {
      return half4(color, alpha);
    }
  }
  else if (glowType == 27 || glowType == 29) {
    float fingerStrength[5];
    getFingerStrength(fingerStrength);
    float4 lines[5];
    getFillFingerLines(lines);
    float2 fingerRadius[5];
    getFingerRadius(fingerRadius);
    float glowValue = movingFingersGradient(texCoord, fingerStrength, _GlowParameter, lines, fingerRadius);
    return half4(color + _GlowColor * glowValue, alpha);
  }
  else if (glowType == 16 || glowType == 17) {
    float gradient = invertedSphereGradient(_GlowPosition, worldPosition, _GlowMaxLength);
    float3 glowColor = _GlowColor * gradient * _GlowParameter;
    return float4(saturate(color + glowColor), alpha);
  }
  else if (glowType == 11 || glowType == 15) {
    float gradient = movingSphereGradient(_GlowPosition, worldPosition, _GlowMaxLength, _GlowParameter, 1.5);
    float3 glowColor = _GlowColor * gradient;
    return float4(saturate(color + glowColor), alpha);
  } 
  else {
    return half4(color, alpha);
  }
}

half4 QFColorFragment(VertexOutput i) : SV_Target {
UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
  float3 worldViewDir = normalize(UnityWorldSpaceViewDir(i.worldPos));
  float fresnelNdot = dot(i.worldNormal, worldViewDir);
  float fresnel = 1.0 * pow(1.0 - fresnelNdot, _FresnelPower);
  float4 color = lerp(_ColorTop, _ColorBottom, fresnel);
  
  if (_GenerateGlow == 1) {
    return applyGlow(_GlowType, color.rgb, i.glowColor.a, i.texcoord1, i.worldPos);
  } else {
    return half4(color.rgb, i.glowColor.a);
  }
}

            
#pragma multi_compile_instancing

VertexOutput QFPreviousVertex(VertexInput v, out float4 qfWorld) {
  VertexOutput o;
  UNITY_SETUP_INSTANCE_ID(v);
  UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
  UNITY_TRANSFER_INSTANCE_ID(v, o);

  o.worldPos = mul(unity_ObjectToWorld, v.vertex);
  o.worldNormal = UnityObjectToWorldNormal(v.normal);
  o.vertex = QFObjectToMotionClip(v.vertex, qfWorld);
  o.texcoord1 = v.texcoord1;
  half4 maskPixelColor = tex2Dlod( _FingerGlowMask, float4(v.texcoord.xy, 0.0, 0.0));
  o.glowColor.rgb = float3(0.0, 0.0, 0.0);
  o.glowColor.a = saturate(maskPixelColor.a + _WristFade) * _Opacity;
  return o;
}
VertexOutput baseVertex(VertexInput v)
{
    UNITY_SETUP_INSTANCE_ID(v);
    float4 world;
    VertexOutput o=QFColorVertex(v,world);
    #if !defined(QF_STATIC_WORLD_GEOMETRY)
    if (_QFHasPreviousModel < .5 && (_QFPreviousVertices > .5 || unity_MotionVectorsParams.x == 1))
        v.vertex.xyz=v.qfPreviousVertex;
    #endif
    float4 previousWorld;
    VertexOutput old=QFPreviousVertex(v,previousWorld);
    float4 oldWorld=QFPreviousWorld(previousWorld);
    if (_QFResetObjectMotion > .5) oldWorld=world;
    o.qfCurrent=o.vertex;
    o.qfPrevious=mul(_PrevViewProjMatrixStereo[QFEye()],oldWorld);
    
    o.vertex=o.qfCurrent;
    return o;
}
float4 baseFragment(VertexOutput v) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
    float coverage=QFColorFragment(v).a;
    clip(coverage-0.5);
    return QFMotion(v.qfCurrent,v.qfPrevious,coverage);
}

            ENDCG
        }
}
}