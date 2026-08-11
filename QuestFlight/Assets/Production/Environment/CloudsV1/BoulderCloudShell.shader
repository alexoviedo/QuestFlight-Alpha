Shader "QuestFlight/Boulder Cloud Shell"
{
    Properties
    {
        _CloudAtlas ("Authored Cloud Impostor Atlas", 2D) = "black" {}
        _CloudTint ("Atmospheric Cloud Tint", Color) = (0.84,0.87,0.91,1)
        _CoverageCutoff ("Coverage Cutoff", Range(0.005,0.2)) = 0.02
        _EdgeSoftness ("Alpha-to-Coverage Edge", Range(0.01,0.8)) = 0.55
        _AmbientGain ("Sky-relative Ambient Gain", Range(0.4,1.0)) = 0.72
        _SunGain ("Directional Sun Gain", Range(0.0,0.4)) = 0.16
    }

    SubShader
    {
        Tags
        {
            "Queue"="AlphaTest+20"
            "RenderType"="TransparentCutout"
            "IgnoreProjector"="True"
        }
        LOD 90
        Cull Off
        ZWrite On
        ZTest LEqual
        Blend Off
        AlphaToMask On

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _CloudAtlas;
            fixed4 _CloudTint;
            half _CoverageCutoff;
            half _EdgeSoftness;
            half _AmbientGain;
            half _SunGain;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float2 billboardCorner : TEXCOORD1;
                float2 billboardHalfSize : TEXCOORD2;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                half brightness : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 centerWorld = mul(unity_ObjectToWorld, input.vertex).xyz;
                float3 cameraWorld = _WorldSpaceCameraPos;
                #if defined(UNITY_SINGLE_PASS_STEREO)
                    // Both eyes use one midpoint so the impostor has identical
                    // orientation in stereo and remains world locked.
                    cameraWorld = (unity_StereoWorldSpaceCameraPos[0] +
                                   unity_StereoWorldSpaceCameraPos[1]) * 0.5;
                #endif
                float3 viewForward = normalize(centerWorld - cameraWorld);
                float3 referenceUp = abs(viewForward.y) > 0.96
                    ? float3(0.0, 0.0, 1.0)
                    : float3(0.0, 1.0, 0.0);
                float3 billboardRight = normalize(cross(referenceUp, viewForward));
                float3 billboardUp = normalize(cross(viewForward, billboardRight));
                float3 worldPosition = centerWorld +
                    billboardRight * input.billboardCorner.x * input.billboardHalfSize.x +
                    billboardUp * input.billboardCorner.y * input.billboardHalfSize.y;
                output.position = UnityWorldToClipPos(worldPosition);
                output.uv = input.uv;
                output.brightness = input.color.r;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 atlas = tex2D(_CloudAtlas, input.uv);
                clip(atlas.a - _CoverageCutoff);
                half coverage = smoothstep(
                    _CoverageCutoff,
                    _CoverageCutoff + max(_EdgeSoftness, 0.01h),
                    atlas.a);
                half sunElevation = saturate(normalize(_WorldSpaceLightPos0.xyz).y * 0.5h + 0.5h);
                half illumination = _AmbientGain + _SunGain * sunElevation;
                half3 directionalTint = lerp(
                    half3(1.0h, 1.0h, 1.0h),
                    saturate(_LightColor0.rgb),
                    0.22h);
                half3 color = atlas.rgb * _CloudTint.rgb * directionalTint *
                    illumination * input.brightness;
                fixed4 output = fixed4(color, coverage);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback Off
}
