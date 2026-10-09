#ifndef QF_SPACEWARP_MOTION_INCLUDED
#define QF_SPACEWARP_MOTION_INCLUDED
// URP 17.3 XRDepthMotionPass supplies these for the same non-jittered eyes.
float4x4 _NonJitteredViewProjMatrixStereo[2];
float4x4 _PrevViewProjMatrixStereo[2];
float4x4 _QFInverseViewProjectionStereo[2];
float4x4 unity_MatrixPreviousM;
float4 unity_MotionVectorsParams;
float _SpaceWarpNDCModifier, _QFPreviousVertices, _QFResetMotion, _QFResetObjectMotion;
float4 _QFPreviousGroveViewPosition;
float4 _QFPreviousAttitudeRotation;
float _QFHasPreviousInstances;
float _QFHasPreviousModel;
float4x4 _QFPreviousModel;
UNITY_INSTANCING_BUFFER_START(QFHistory)
    UNITY_DEFINE_INSTANCED_PROP(float4, _QFPreviousRow0)
    UNITY_DEFINE_INSTANCED_PROP(float4, _QFPreviousRow1)
    UNITY_DEFINE_INSTANCED_PROP(float4, _QFPreviousRow2)
    UNITY_DEFINE_INSTANCED_PROP(float4, _QFPreviousRow3)
UNITY_INSTANCING_BUFFER_END(QFHistory)
uint QFEye()
{
#if defined(USING_STEREO_MATRICES)
    return unity_StereoEyeIndex;
#else
    return 0;
#endif
}
float4 QFWorldFromClip(float4 clipPosition)
{
    float4 p = mul(_QFInverseViewProjectionStereo[QFEye()], clipPosition);
    return p / p.w;
}
// Motion uses the XR provider's projection. Do not project through the color
// camera and invert it: an intermediate color target can have a different
// projection flip from the provider motion target.
float4 QFWorldToMotionClip(float4 position, out float4 world)
{
    world = position;
    return mul(_NonJitteredViewProjMatrixStereo[QFEye()], world);
}
float4 QFObjectToMotionClip(float4 position, out float4 world)
{
    return QFWorldToMotionClip(mul(unity_ObjectToWorld, position), world);
}
float4 QFObjectToMotionClip(float3 position, out float4 world)
{
    return QFObjectToMotionClip(float4(position, 1), world);
}
// Airport far trees use a vertical CPU LookRotation(view-center, up) TRS.
// Reconstruct that same immutable tree's prior presented orientation without
// storing history by a compacted instance slot. Preserve its authored scale.
float4 QFPreviousVerticalBillboardWorld(float4 localVertex)
{
    float3 center = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
    float3 facing = _QFPreviousGroveViewPosition.xyz - center;
    facing.y = 0;
    facing = dot(facing, facing) < .001 ? float3(0, 0, 1) : normalize(facing);
    float3 right = float3(facing.z, 0, -facing.x);
    float3 scale = float3(length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20)),
        length(float3(unity_ObjectToWorld._m01, unity_ObjectToWorld._m11, unity_ObjectToWorld._m21)),
        length(float3(unity_ObjectToWorld._m02, unity_ObjectToWorld._m12, unity_ObjectToWorld._m22)));
    return float4(center + right * (localVertex.x * scale.x) +
        float3(0, localVertex.y * scale.y, 0) + facing * (localVertex.z * scale.z), 1);
}
float4 QFPreviousWorld(float4 world)
{
#if defined(QF_STATIC_WORLD_GEOMETRY) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    // Fixed scenery and procedural groves are immutable world-space records.
    // Streaming or buffer compaction is not object movement. View-facing
    // deformation is evaluated separately using the previous view position.
    return world;
#else
    float4 local = mul(unity_WorldToObject, world);
    if (_QFHasPreviousModel > .5) return mul(_QFPreviousModel, local);
    if (_QFHasPreviousInstances > .5)
    {
        return float4(dot(UNITY_ACCESS_INSTANCED_PROP(QFHistory,_QFPreviousRow0),local),
            dot(UNITY_ACCESS_INSTANCED_PROP(QFHistory,_QFPreviousRow1),local),
            dot(UNITY_ACCESS_INSTANCED_PROP(QFHistory,_QFPreviousRow2),local),
            dot(UNITY_ACCESS_INSTANCED_PROP(QFHistory,_QFPreviousRow3),local));
    }
#if defined(UNITY_INSTANCING_ENABLED)
    // Static authored trees/grass use their current immutable matrix, not a
    // previous compacted-array slot. Vehicles explicitly provide history.
    return world;
#else
    return mul(unity_MatrixPreviousM, local);
#endif
#endif
}
float4 QFMotion(float4 current, float4 previous, float coverage)
{
    float3 velocity = current.xyz/current.w - previous.xyz/previous.w;
#if UNITY_UV_STARTS_AT_TOP
    velocity.y *= _SpaceWarpNDCModifier;
#endif
    return float4(_QFResetMotion > .5 ? 0 : velocity, coverage);
}
#endif
