Shader "Hidden/QuestFlight/SpaceWarp Diagnostic"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        float4 Motion(Varyings input):SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_PointClamp,input.texcoord,0);
        }
        float4 Depth(Varyings input):SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float d=SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_PointClamp,input.texcoord,0).r;
            return float4(d,d,d,1);
        }
        ENDHLSL
        Pass { HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Motion
            #pragma multi_compile_instancing
            ENDHLSL }
        Pass { HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Depth
            #pragma multi_compile_instancing
            ENDHLSL }
    }
}
