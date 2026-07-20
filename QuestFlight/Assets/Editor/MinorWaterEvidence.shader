Shader "Hidden/QuestFlight/MinorWaterEvidence"
{
    Properties
    {
        _NearAlbedo ("Near albedo", 2D) = "gray" {}
        _MidAlbedo ("Mid albedo", 2D) = "gray" {}
        _AirportCoreMinorWaterMask ("Packed near/mid mask", 2D) = "black" {}
        _NearWorldMinInvSize ("Near mapping", Vector) = (-2000,-2000,0.00025,0.00025)
        _MidWorldMinInvSize ("Mid mapping", Vector) = (-6000,-6000,0.0000833333,0.0000833333)
        _AirportCoreMinorWaterNearWorldMinInvSize ("Near mask mapping", Vector) = (-2000,-2000,0.00025,0.00025)
        _AirportCoreMinorWaterMidWorldMinInvSize ("Mid mask mapping", Vector) = (-6000,-6000,0.0000833333,0.0000833333)
        _EvidenceZone ("Near=0 Mid=1", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back ZWrite On ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _NearAlbedo, _MidAlbedo, _AirportCoreMinorWaterMask;
            float4 _NearWorldMinInvSize, _MidWorldMinInvSize;
            float4 _AirportCoreMinorWaterNearWorldMinInvSize, _AirportCoreMinorWaterMidWorldMinInvSize;
            half _EvidenceZone;
            struct appdata { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 position:SV_POSITION; float3 world:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata input)
            {
                UNITY_SETUP_INSTANCE_ID(input); v2f output; UNITY_INITIALIZE_OUTPUT(v2f, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position=UnityObjectToClipPos(input.vertex);
                output.world=mul(unity_ObjectToWorld,input.vertex).xyz;
                return output;
            }
            fixed4 frag(v2f input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 nearUv=(input.world.xz-_NearWorldMinInvSize.xy)*_NearWorldMinInvSize.zw;
                float2 midUv=(input.world.xz-_MidWorldMinInvSize.xy)*_MidWorldMinInvSize.zw;
                float2 nearMaskUv=(input.world.xz-_AirportCoreMinorWaterNearWorldMinInvSize.xy)*_AirportCoreMinorWaterNearWorldMinInvSize.zw;
                float2 midMaskUv=(input.world.xz-_AirportCoreMinorWaterMidWorldMinInvSize.xy)*_AirportCoreMinorWaterMidWorldMinInvSize.zw;
                fixed3 baseColor=_EvidenceZone<0.5h?tex2D(_NearAlbedo,nearUv).rgb:tex2D(_MidAlbedo,midUv).rgb;
                fixed4 packed=_EvidenceZone<0.5h?tex2D(_AirportCoreMinorWaterMask,nearMaskUv):tex2D(_AirportCoreMinorWaterMask,midMaskUv);
                half mask=_EvidenceZone<0.5h?packed.r:packed.b;
                half gray=dot(baseColor,fixed3(0.2126h,0.7152h,0.0722h));
                return fixed4(lerp(gray.xxx,fixed3(0.0h,1.0h,1.0h),smoothstep(0.05h,0.35h,mask)),1);
            }
            ENDCG
        }
    }
}
