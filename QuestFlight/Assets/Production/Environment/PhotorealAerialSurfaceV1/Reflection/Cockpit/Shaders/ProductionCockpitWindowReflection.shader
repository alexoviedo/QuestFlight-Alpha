Shader "QuestFlight/Photoreal Aerial Surface V1/Cockpit Window Reflection"
{
    Properties
    {
        _WindshieldTint ("Windshield Tint", Color) = (0.77, 0.86, 0.95, 1)
        _SideWindowTint ("Side Window Tint", Color) = (0.70, 0.82, 0.92, 1)
        [NoScaleOffset] _AuthoredReflectionCube ("Authored Ground/Sky Reflection", Cube) = "black" {}
        _WindshieldOpacity ("Windshield Opacity", Range(0, 0.3)) = 0.13
        _SideWindowOpacity ("Side Window Opacity", Range(0, 0.3)) = 0.16
        _FresnelOpacity ("Edge Fresnel Opacity", Range(0, 0.3)) = 0.18
        _ReflectionStrength ("Reflection Strength", Range(0, 0.5)) = 0.24
        _DirectGlintStrength ("Directional Glint Strength", Range(0, 0.12)) = 0.055
        _PerceptualRoughness ("Perceptual Roughness", Range(0.05, 1)) = 0.24
        _SideAxisStart ("Side Axis Start", Range(0, 1)) = 0.52
        _SideAxisEnd ("Side Axis End", Range(0, 1)) = 0.82
    }

    SubShader
    {
        Tags { "Queue"="Transparent+12" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _WindshieldTint;
            fixed4 _SideWindowTint;
            samplerCUBE _AuthoredReflectionCube;
            half _WindshieldOpacity;
            half _SideWindowOpacity;
            half _FresnelOpacity;
            half _ReflectionStrength;
            half _DirectGlintStrength;
            half _PerceptualRoughness;
            half _SideAxisStart;
            half _SideAxisEnd;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                half3 worldNormal : TEXCOORD1;
                half3 objectNormal : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.objectNormal = input.normal;
                return output;
            }

            fixed4 frag(v2f input, fixed facing : VFACE) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half faceSign = facing >= 0 ? 1.0h : -1.0h;
                half3 normal = normalize(input.worldNormal) * faceSign;
                half3 viewDirection = normalize(_WorldSpaceCameraPos - input.worldPosition);
                half ndotv = saturate(abs(dot(normal, viewDirection)));
                half fresnel = pow(1.0h - ndotv, 5.0h);
                half3 localNormal = normalize(input.objectNormal);
                half sideWeight = smoothstep(_SideAxisStart, _SideAxisEnd, abs(localNormal.x));
                half opacity = lerp(_WindshieldOpacity, _SideWindowOpacity, sideWeight) +
                               fresnel * _FresnelOpacity;
                opacity = min(opacity, 0.26h);

                half3 tint = lerp(_WindshieldTint.rgb, _SideWindowTint.rgb, sideWeight);
                half3 reflectionDirection = reflect(-viewDirection, normal);
                half3 authoredReflection = texCUBE(
                    _AuthoredReflectionCube, reflectionDirection).rgb;
                half skyWeight = smoothstep(-0.08h, 0.20h, reflectionDirection.y);
                half3 proceduralGround = half3(0.13h, 0.15h, 0.10h);
                half3 proceduralSky = half3(0.34h, 0.48h, 0.62h);
                half3 proceduralReflection = lerp(proceduralGround, proceduralSky, skyWeight);
                half3 reflection = max(authoredReflection * 0.82h, proceduralReflection);
                half reflectionWeight = saturate(_ReflectionStrength * (0.55h + fresnel * 0.45h));
                half3 color = lerp(tint * 0.22h, reflection, reflectionWeight);
                half3 sunDirection = normalize(_WorldSpaceLightPos0.xyz);
                half sunGlint = pow(saturate(dot(
                    reflect(-sunDirection, normal), viewDirection)), 96.0h);
                color += _LightColor0.rgb * sunGlint * _DirectGlintStrength;
                return fixed4(color, opacity);
            }
            ENDCG
        }
    }
    Fallback Off
}
