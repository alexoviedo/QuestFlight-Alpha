// Exact lattice preservation: only skip samples whose entire interpolation cell is empty.
sampler3D _DensityVolume;
sampler3D _CloudOccupancy;
float _CloudVolumeSampling;
float _CloudEmptySkipping;

half4 SampleCloudVolume(float3 coordinate)
{
    // Atlas sampling maps [0,1] to first/last texel centers, not texture edges.
    float3 dimensions = float3(64,32,64);
    return tex3Dlod(_DensityVolume, float4((saturate(coordinate) * (dimensions-1) + .5) / dimensions, 0));
}

int EmptyCloudSamples(float3 local, float3 direction, half shape, float stepLength)
{
    // Atlas compression differs from this offline volume. Never apply its occupancy to the atlas.
    if (_CloudVolumeSampling < .5 || _CloudEmptySkipping < .5) return 0;
    float3 coordinate = saturate(local + .5);
    if (shape > .5h) { coordinate.x = 1-coordinate.x; direction.x = -direction.x; }
    float3 grid = float3(16,8,16);
    float3 cell = min(floor(coordinate * grid), grid-1);
    if (tex3Dlod(_CloudOccupancy,float4((cell+.5)/grid,0)).r > .5) return 0;
    float3 boundary = (cell + step(0, direction)) / grid;
    float3 exitDistance = float3(1e20,1e20,1e20);
    if (abs(direction.x)>1e-10) exitDistance.x=(boundary.x-coordinate.x)/direction.x;
    if (abs(direction.y)>1e-10) exitDistance.y=(boundary.y-coordinate.y)/direction.y;
    if (abs(direction.z)>1e-10) exitDistance.z=(boundary.z-coordinate.z)/direction.z;
    float distance = max(0,min(exitDistance.x,min(exitDistance.y,exitDistance.z)));
    // Stay conservatively short of the next cell; never change occupied sample positions.
    return max(1,(int)floor(distance / stepLength));
}
