Shader "Hidden/QuestFlight/RunwayBandIsolation"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _NormalTex ("Normal", 2D) = "bump" {}
        _MRAOTex ("MRAO", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _RunwayOrigin ("Origin", Vector) = (-629.757,-30.671,0,0)
        _RunwayAxis ("Axis", Vector) = (1,0,0,0)
        _RunwayLengthWidth ("Length Width", Vector) = (1250.385,22.86,0,0)
        _WorldTiling ("World Tiling", Float) = 0.09
        _SecondaryTiling ("Secondary Tiling", Float) = 0.031
        _NormalStrength ("Normal Strength", Float) = 0.62
        _DiagnosticMode ("Diagnostic Mode", Int) = 3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back ZWrite On ZTest LEqual
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex, _NormalTex, _MRAOTex;
            fixed4 _Tint;
            float4 _RunwayOrigin, _RunwayAxis, _RunwayLengthWidth;
            half _WorldTiling, _SecondaryTiling, _NormalStrength;
            int _DiagnosticMode;

            struct appdata { float4 vertex:POSITION; };
            struct v2f { float4 position:SV_POSITION; float3 world:TEXCOORD0; UNITY_FOG_COORDS(1) };

            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.position);
                return o;
            }

            fixed4 frag(v2f i):SV_Target
            {
                float2 axis = normalize(_RunwayAxis.xy);
                float2 lateralAxis = float2(-axis.y, axis.x);
                float2 delta = i.world.xz - _RunwayOrigin.xy;
                float longitudinal = dot(delta, axis);
                float lateral = dot(delta, lateralAxis);
                float nl = saturate(longitudinal / max(_RunwayLengthWidth.x, 1.0));
                float nv = lateral / max(_RunwayLengthWidth.y, 1.0);
                float2 meters = float2(longitudinal, lateral);
                float2 uvA = meters * _WorldTiling;
                float2 uvB = float2(meters.x * 0.8387 - meters.y * 0.5446,
                    meters.x * 0.5446 + meters.y * 0.8387) * _SecondaryTiling;

                if (_DiagnosticMode == 1) return fixed4(1,1,1,1);

                half3 p = tex2D(_MainTex, uvA).rgb;
                half3 s = tex2D(_MainTex, uvB).rgb;
                half pl = max(dot(p, half3(0.2126h,0.7152h,0.0722h)), 0.08h);
                half sl = max(dot(s, half3(0.2126h,0.7152h,0.0722h)), 0.08h);
                half3 aggregate = lerp(p / pl, s / sl, 0.37h);
                half macro = sin(longitudinal * 0.0037h + sin(lateral * 0.071h) * 0.73h) * 0.5h +
                    sin(longitudinal * 0.0089h - lateral * 0.043h + 1.71h) * 0.28h;
                half3 albedo = half3(0.285h,0.278h,0.258h) * (1.0h + macro * 0.055h);
                albedo *= lerp(half3(1,1,1), aggregate, 0.16h) * _Tint.rgb;

                half center = 1.0h - smoothstep(0.10h, 0.44h, abs(nv));
                half touchdown = max(1.0h - smoothstep(0.0h,0.13h,abs(nl-0.18h)),
                    1.0h - smoothstep(0.0h,0.13h,abs(nl-0.82h)));
                half rubber = center * touchdown * 0.20h;
                half patch = sin(longitudinal * 0.021h + sin(lateral * 0.31h) * 1.9h) *
                    sin(longitudinal * 0.013h - lateral * 0.17h + 0.8h);
                half repairs = smoothstep(0.86h,0.97h,patch) * 0.07h;
                half edge = smoothstep(0.34h,0.49h,abs(nv));
                if (_DiagnosticMode == 7 || _DiagnosticMode == 10)
                {
                    albedo *= 1.0h - rubber;
                    albedo = lerp(albedo, albedo * half3(0.86h,0.83h,0.75h), edge * 0.24h);
                    albedo = lerp(albedo, albedo * 0.93h, repairs);
                }

                if (_DiagnosticMode == 4 || _DiagnosticMode == 10)
                {
                    fixed4 unlit = fixed4(albedo,1);
                    UNITY_APPLY_FOG(i.fogCoord, unlit);
                    return unlit;
                }

                half roughness = 0.74h;
                if (_DiagnosticMode == 5)
                    roughness = lerp(0.74h, tex2D(_MRAOTex, uvA).g, 0.24h);
                half3 normal = half3(0,1,0);
                if (_DiagnosticMode == 6)
                {
                    half3 tn = UnpackNormal(tex2D(_NormalTex, uvA));
                    tn.xy *= _NormalStrength;
                    half3 tangent = normalize(half3(axis.x,0,axis.y));
                    half3 bitangent = normalize(half3(lateralAxis.x,0,lateralAxis.y));
                    normal = normalize(tangent * tn.x + bitangent * tn.y + half3(0,1,0) * tn.z);
                }
                half ndotl = saturate(dot(normal, normalize(_WorldSpaceLightPos0.xyz)));
                half3 baseColor = _DiagnosticMode == 3 ? half3(0.285h,0.278h,0.258h) : albedo;
                half3 irradiance = saturate(max(ShadeSH9(half4(normal,1)),0) * 0.35h +
                    _LightColor0.rgb * (0.08h + ndotl * 0.55h));
                half3 color = baseColor * irradiance;
                half3 viewDir = normalize(_WorldSpaceCameraPos - i.world);
                half3 halfDir = normalize(normalize(_WorldSpaceLightPos0.xyz) + viewDir);
                half spec = pow(saturate(dot(normal,halfDir)), lerp(42.0h,5.0h,roughness));
                color += _LightColor0.rgb * spec * (1.0h-roughness) * 0.045h;
                fixed4 output = fixed4(color,1);
                UNITY_APPLY_FOG(i.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
}
