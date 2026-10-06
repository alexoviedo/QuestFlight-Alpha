Shader "QuestFlight/Airport Perimeter Fence"
{
    Properties
    {
        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1

        _MetalColor ("Galvanized Metal", Color) = (0.46,0.48,0.46,1)
        _SlatColor ("Green Privacy Slats", Color) = (0.035,0.29,0.17,1)
        _WoodColor ("Weathered Wood", Color) = (0.20,0.145,0.085,1)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 80
        Cull Off
        ZWrite On
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _MetalColor;
            fixed4 _SlatColor;
            fixed4 _WoodColor;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 worldNormal : TEXCOORD1;
                fixed4 color : COLOR;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.color = input.color;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half coverage = 1.0h;
                half brightness = 1.0h;
                half3 panelAlbedo = _MetalColor.rgb;
                if (input.color.b < 0.5h)
                {
                    float heightMeters = max(input.color.r * 2.0, 1.0);
                    float uMeters = input.uv.x;
                    float vMeters = input.uv.y * heightMeters;
                    float phaseA = frac((uMeters + vMeters) / 0.72);
                    float phaseB = frac((uMeters - vMeters) / 0.72);
                    float wireDistance = min(
                        min(phaseA, 1.0 - phaseA),
                        min(phaseB, 1.0 - phaseB));
                    float wireAa = max(fwidth(wireDistance), 0.015);
                    float wire = 1.0 - smoothstep(
                        0.075,
                        0.075 + wireAa,
                        wireDistance);

                    // Project-authored approximation of the supplied visual
                    // reference: green vertical privacy slats behind galvanized
                    // diamond wire. At flight distance the narrow slats converge
                    // into a stable green field while resolved wire stays silver.
                    float slatPhase = frac(uMeters / 0.18);
                    float slatAa = max(fwidth(slatPhase), 0.025);
                    float slatEdge = smoothstep(
                        0.72 - slatAa,
                        0.72 + slatAa,
                        slatPhase);
                    half slatShade = lerp(0.88h, 1.06h, (half)slatPhase);
                    panelAlbedo = lerp(
                        _SlatColor.rgb * slatShade,
                        _MetalColor.rgb,
                        (half)wire);
                    coverage = max(
                        lerp(0.82h, 0.16h, (half)slatEdge),
                        (half)wire * 0.95h);
                    brightness = lerp(0.98h, 1.12h, (half)wire);
                }

                half3 normal = normalize(input.worldNormal);
                half ndotl = saturate(
                    dot(normal, normalize(_WorldSpaceLightPos0.xyz)));
                half3 albedo = input.color.b < 0.5h
                    ? panelAlbedo
                    : lerp(
                        _MetalColor.rgb,
                        _WoodColor.rgb,
                        saturate(input.color.g));
                half3 ambient = max(
                    ShadeSH9(half4(normal, 1.0)),
                    0.16h) * 0.70h;
                half3 lit =
                    albedo * (ambient + _LightColor0.rgb * (0.20h + ndotl * 0.58h));
                fixed4 output = fixed4(lit * brightness, coverage);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
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
#define QF_STATIC_WORLD_GEOMETRY 1

            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
#include "Assets/Shaders/ProductSpaceWarpMotion.cginc"
            #include "Lighting.cginc"

            fixed4 _MetalColor;
            fixed4 _SlatColor;
            fixed4 _WoodColor;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                
float3 qfPreviousVertex : TEXCOORD4;
UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 worldNormal : TEXCOORD1;
                fixed4 color : COLOR;
                UNITY_FOG_COORDS(2)
                
float4 qfCurrent : TEXCOORD14; float4 qfPrevious : TEXCOORD15;
UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f QFColorVertex(appdata input, out float4 qfWorld)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = QFObjectToMotionClip(input.vertex, qfWorld);
                output.uv = input.uv;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.color = input.color;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 QFColorFragment(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half coverage = 1.0h;
                half brightness = 1.0h;
                half3 panelAlbedo = _MetalColor.rgb;
                if (input.color.b < 0.5h)
                {
                    float heightMeters = max(input.color.r * 2.0, 1.0);
                    float uMeters = input.uv.x;
                    float vMeters = input.uv.y * heightMeters;
                    float phaseA = frac((uMeters + vMeters) / 0.72);
                    float phaseB = frac((uMeters - vMeters) / 0.72);
                    float wireDistance = min(
                        min(phaseA, 1.0 - phaseA),
                        min(phaseB, 1.0 - phaseB));
                    float wireAa = max(fwidth(wireDistance), 0.015);
                    float wire = 1.0 - smoothstep(
                        0.075,
                        0.075 + wireAa,
                        wireDistance);

                    // Project-authored approximation of the supplied visual
                    // reference: green vertical privacy slats behind galvanized
                    // diamond wire. At flight distance the narrow slats converge
                    // into a stable green field while resolved wire stays silver.
                    float slatPhase = frac(uMeters / 0.18);
                    float slatAa = max(fwidth(slatPhase), 0.025);
                    float slatEdge = smoothstep(
                        0.72 - slatAa,
                        0.72 + slatAa,
                        slatPhase);
                    half slatShade = lerp(0.88h, 1.06h, (half)slatPhase);
                    panelAlbedo = lerp(
                        _SlatColor.rgb * slatShade,
                        _MetalColor.rgb,
                        (half)wire);
                    coverage = max(
                        lerp(0.82h, 0.16h, (half)slatEdge),
                        (half)wire * 0.95h);
                    brightness = lerp(0.98h, 1.12h, (half)wire);
                }

                half3 normal = normalize(input.worldNormal);
                half ndotl = saturate(
                    dot(normal, normalize(_WorldSpaceLightPos0.xyz)));
                half3 albedo = input.color.b < 0.5h
                    ? panelAlbedo
                    : lerp(
                        _MetalColor.rgb,
                        _WoodColor.rgb,
                        saturate(input.color.g));
                half3 ambient = max(
                    ShadeSH9(half4(normal, 1.0)),
                    0.16h) * 0.70h;
                half3 lit =
                    albedo * (ambient + _LightColor0.rgb * (0.20h + ndotl * 0.58h));
                fixed4 output = fixed4(lit * brightness, coverage);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            
#pragma multi_compile_instancing

v2f QFPreviousVertex(appdata input, out float4 qfWorld)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                v2f output;
                UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = QFObjectToMotionClip(input.vertex, qfWorld);
                output.uv = input.uv;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.color = input.color;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
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
    o.qfCurrent=o.position;
    o.qfPrevious=mul(_PrevViewProjMatrixStereo[QFEye()],oldWorld);
    
    o.position=o.qfCurrent;
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
    Fallback Off
}
