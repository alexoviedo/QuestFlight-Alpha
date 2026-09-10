Shader "QuestFlight/Photoreal Aerial V1/Semantic Surface"
{
    Properties
    {
        [Enum(Building,0,Canopy,1)] _SemanticMode ("Semantic mode", Float) = 0
        _NearAlbedo ("Authoritative near orthophoto", 2D) = "gray" {}
        _RecoveryMidNWAlbedo ("Mid NW orthophoto", 2D) = "gray" {}
        _RecoveryMidNEAlbedo ("Mid NE orthophoto", 2D) = "gray" {}
        _RecoveryMidSWAlbedo ("Mid SW orthophoto", 2D) = "gray" {}
        _RecoveryMidSEAlbedo ("Mid SE orthophoto", 2D) = "gray" {}
        _FacadeAtlas ("Shared CC0/project facade atlas", 2D) = "gray" {}
        _FacadeMRAO ("Shared facade MRAO", 2D) = "white" {}
        _FacadeNormal ("Shared facade normal", 2D) = "bump" {}
        _FacadeReflectionCube ("Static ground/sky facade reflection", Cube) = "black" {}
        _CanopyNormalOcclusion ("Canopy normal and occlusion", 2D) = "gray" {}
        _CanopyFoliageAlbedo ("Project-owned canopy foliage detail", 2D) = "gray" {}
        _NearFoliageAtlas ("Project-owned perforated near foliage atlas", 2D) = "white" {}
        _HeroLeafAtlas ("CC0-derived hero tree leaf atlas", 2D) = "white" {}
        _TreeImpostorAtlas ("CC0-derived eight-view tree impostor", 2D) = "white" {}
        _NeighborhoodFoliageAtlas ("Authored perforated branch clusters", 2D) = "white" {}
        _NormalizedSurfaceHeight ("Normalized surface height", 2D) = "black" {}
        _NearWorldMinInvSize ("Near min XZ / inverse size", Vector) = (-2000,-2000,0.00025,0.00025)
        _RecoveryMidNWWorldMinInvSize ("Mid NW min XZ / inverse size", Vector) = (-4032,-32,0.000246063,0.000246063)
        _RecoveryMidNEWorldMinInvSize ("Mid NE min XZ / inverse size", Vector) = (-32,-32,0.000246063,0.000246063)
        _RecoveryMidSWWorldMinInvSize ("Mid SW min XZ / inverse size", Vector) = (-4032,-4032,0.000246063,0.000246063)
        _RecoveryMidSEWorldMinInvSize ("Mid SE min XZ / inverse size", Vector) = (-32,-4032,0.000246063,0.000246063)
        _CanopyWorldMinInvSize ("Canopy min XZ / inverse size", Vector) = (-2048,-2048,0.000244140625,0.000244140625)
        _CanopyProductToSourceRow0 ("Product AEQD to lidar UTM row 0", Vector) = (1,0,0,0)
        _CanopyProductToSourceRow1 ("Product AEQD to lidar UTM row 1", Vector) = (0,1,0,0)
        _CanopySourceToProductRow0 ("Lidar UTM normal to product row 0", Vector) = (1,0,0,0)
        _CanopySourceToProductRow1 ("Lidar UTM normal to product row 1", Vector) = (0,1,0,0)
        _NearBlendStartEnd ("Near/recovery overlap", Vector) = (1800,1980,0,0)
        _TopTint ("Top tint", Color) = (1,1,1,1)
        _SideTint ("Side tint", Color) = (0.48,0.50,0.46,1)
        _CanopySideTint ("Canopy skirt tint", Color) = (0.22,0.30,0.14,1)
        _TopRoughness ("Top roughness", Range(0,1)) = 0.76
        _SideRoughness ("Side roughness", Range(0,1)) = 0.82
        _CanopyNormalStrength ("Canopy normal strength", Range(0,1)) = 0.42
        _AmbientFloor ("Linear ambient floor", Range(0,0.4)) = 0.22
        _FoliageCutoff ("Near foliage alpha cutoff", Range(0,1)) = 0.30
        [PerRendererData] _GroveLeafResponse ("Connected grove leaf coverage and shading", Range(0,1)) = 0
        _BranchFoliageResponse ("Primary branch foliage coverage and texture shading", Range(0,1)) = 0
        [PerRendererData] _InstanceTint ("Close-tree instance tint", Color) = (1,1,1,1)
        [PerRendererData] _InstanceFade ("Close-tree LOD coverage", Range(0,1)) = 1
        [PerRendererData] _ImpostorFrame ("Close-tree impostor frame", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _DiagnosticMode ("Editor diagnostic", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 110
        Cull [_Cull]
        ZWrite On
        ZTest LEqual
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

            sampler2D _NearAlbedo;
            sampler2D _RecoveryMidNWAlbedo;
            sampler2D _RecoveryMidNEAlbedo;
            sampler2D _RecoveryMidSWAlbedo;
            sampler2D _RecoveryMidSEAlbedo;
            sampler2D _FacadeAtlas;
            sampler2D _FacadeMRAO;
            sampler2D _FacadeNormal;
            samplerCUBE _FacadeReflectionCube;
            sampler2D _CanopyNormalOcclusion;
            sampler2D _CanopyFoliageAlbedo;
            sampler2D _NearFoliageAtlas;
            half _GroveLeafResponse;
            half _BranchFoliageResponse;
            sampler2D _HeroLeafAtlas;
            sampler2D _TreeImpostorAtlas;
            sampler2D _NeighborhoodFoliageAtlas;
            sampler2D _NormalizedSurfaceHeight;
            float4 _NearWorldMinInvSize;
            float4 _RecoveryMidNWWorldMinInvSize;
            float4 _RecoveryMidNEWorldMinInvSize;
            float4 _RecoveryMidSWWorldMinInvSize;
            float4 _RecoveryMidSEWorldMinInvSize;
            float4 _CanopyWorldMinInvSize;
            float4 _CanopyProductToSourceRow0;
            float4 _CanopyProductToSourceRow1;
            float4 _CanopySourceToProductRow0;
            float4 _CanopySourceToProductRow1;
            float4 _NearBlendStartEnd;
            fixed4 _TopTint;
            fixed4 _SideTint;
            fixed4 _CanopySideTint;
            half _SemanticMode;
            half _TopRoughness;
            half _SideRoughness;
            half _CanopyNormalStrength;
            half _AmbientFloor;
            half _FoliageCutoff;
            half _DiagnosticMode;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _InstanceTint)
                UNITY_DEFINE_INSTANCED_PROP(half, _InstanceFade)
                UNITY_DEFINE_INSTANCED_PROP(half, _ImpostorFrame)
            UNITY_INSTANCING_BUFFER_END(Props)
            sampler2D _MapGroveOwnership;
            float4 _MapGroveOwnershipBounds;
            half _MapGroveOwnershipActive;

            struct appdata
            {
                float4 vertex : POSITION;
                half3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                half3 worldNormal : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                fixed4 semanticData : TEXCOORD3;
                float2 wallUv : TEXCOORD4;
                half4 treeData : TEXCOORD5;
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
                output.semanticData = input.color;
                half clumpLeaf = step(1.5h, input.uv.x) *
                    (1.0h - step(3.5h, input.uv.x));
                half heroLeaf = step(3.5h, input.uv.x) *
                    (1.0h - step(5.5h, input.uv.x));
                half authoredLeaf = step(7.5h, input.uv.x) * step(0.5h, _SemanticMode);
                half impostorLeaf = step(5.5h, input.uv.x) * (1.0h - authoredLeaf);
                half closeTreeLeaf = saturate(clumpLeaf + heroLeaf + impostorLeaf) *
                    (1.0h - step(0.5h, input.color.r));
                fixed4 instanceTint = UNITY_ACCESS_INSTANCED_PROP(Props, _InstanceTint);
                output.semanticData.gba = lerp(
                    output.semanticData.gba, instanceTint.rgb, closeTreeLeaf);
                output.wallUv = float2(
                    // Far cards use 6..7: frac() on their vertex endpoints maps
                    // both edges to zero. Preserve near-card/building decoding.
                    lerp(input.uv.x, lerp(frac(input.uv.x),
                        input.uv.x - 6.0h, impostorLeaf * step(0.5h, _SemanticMode)), closeTreeLeaf),
                    input.uv.y);
                output.treeData = half4(
                    lerp(heroLeaf, -1.0h, authoredLeaf),
                    impostorLeaf,
                    UNITY_ACCESS_INSTANCED_PROP(Props, _InstanceFade),
                    UNITY_ACCESS_INSTANCED_PROP(Props, _ImpostorFrame));
                output.wallUv = lerp(output.wallUv, float2(input.uv.x - 8.0, input.uv.y), authoredLeaf);
                UNITY_BRANCH if (_MapGroveOwnershipActive > .5h && _SemanticMode > .5h && closeTreeLeaf > .5h)
                {
                    // Replace only leaf vertices of trees whose centers have a
                    // submitted replacement. Trunks retain their original fade.
                    float2 center=float2(unity_ObjectToWorld._m03,unity_ObjectToWorld._m23);
                    float2 ownershipUv=(center-_MapGroveOwnershipBounds.xy)*_MapGroveOwnershipBounds.zw;
                    half inside=step(0,ownershipUv.x)*step(0,ownershipUv.y)*step(ownershipUv.x,1)*step(ownershipUv.y,1);
                    output.treeData.z*=1-tex2Dlod(_MapGroveOwnership,float4(saturate(ownershipUv),0,0)).r*inside;
                }
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed3 SampleTile(
                sampler2D source,
                float2 world,
                float4 worldMinInvSize,
                float2 worldDx,
                float2 worldDy)
            {
                float2 uv = saturate((world - worldMinInvSize.xy) * worldMinInvSize.zw);
                return tex2Dgrad(source, uv,
                    worldDx * worldMinInvSize.zw,
                    worldDy * worldMinInvSize.zw).rgb;
            }

            fixed3 SampleRecoveryNorth(float2 world, float2 worldDx, float2 worldDy)
            {
                UNITY_BRANCH if (world.x <= -32.0)
                    return SampleTile(_RecoveryMidNWAlbedo, world,
                        _RecoveryMidNWWorldMinInvSize, worldDx, worldDy);
                UNITY_BRANCH if (world.x >= 32.0)
                    return SampleTile(_RecoveryMidNEAlbedo, world,
                        _RecoveryMidNEWorldMinInvSize, worldDx, worldDy);
                fixed3 west = SampleTile(_RecoveryMidNWAlbedo, world,
                    _RecoveryMidNWWorldMinInvSize, worldDx, worldDy);
                fixed3 east = SampleTile(_RecoveryMidNEAlbedo, world,
                    _RecoveryMidNEWorldMinInvSize, worldDx, worldDy);
                return lerp(west, east, smoothstep(-32.0, 32.0, world.x));
            }

            fixed3 SampleRecoverySouth(float2 world, float2 worldDx, float2 worldDy)
            {
                UNITY_BRANCH if (world.x <= -32.0)
                    return SampleTile(_RecoveryMidSWAlbedo, world,
                        _RecoveryMidSWWorldMinInvSize, worldDx, worldDy);
                UNITY_BRANCH if (world.x >= 32.0)
                    return SampleTile(_RecoveryMidSEAlbedo, world,
                        _RecoveryMidSEWorldMinInvSize, worldDx, worldDy);
                fixed3 west = SampleTile(_RecoveryMidSWAlbedo, world,
                    _RecoveryMidSWWorldMinInvSize, worldDx, worldDy);
                fixed3 east = SampleTile(_RecoveryMidSEAlbedo, world,
                    _RecoveryMidSEWorldMinInvSize, worldDx, worldDy);
                return lerp(west, east, smoothstep(-32.0, 32.0, world.x));
            }

            fixed3 SampleRecovery(float2 world, float2 worldDx, float2 worldDy)
            {
                UNITY_BRANCH if (world.y <= -32.0)
                    return SampleRecoverySouth(world, worldDx, worldDy);
                UNITY_BRANCH if (world.y >= 32.0)
                    return SampleRecoveryNorth(world, worldDx, worldDy);
                fixed3 south = SampleRecoverySouth(world, worldDx, worldDy);
                fixed3 north = SampleRecoveryNorth(world, worldDx, worldDy);
                return lerp(south, north, smoothstep(-32.0, 32.0, world.y));
            }

            fixed3 SampleOrthophoto(float2 world, float2 worldDx, float2 worldDy)
            {
                half radius = max(abs(world.x), abs(world.y));
                fixed3 nearColor = 0.0h;
                UNITY_BRANCH if (radius < _NearBlendStartEnd.y)
                {
                    float2 nearUv = saturate((world - _NearWorldMinInvSize.xy) * _NearWorldMinInvSize.zw);
                    nearColor = tex2Dgrad(_NearAlbedo, nearUv,
                        worldDx * _NearWorldMinInvSize.zw,
                        worldDy * _NearWorldMinInvSize.zw).rgb;
                }
                if (radius <= _NearBlendStartEnd.x) return nearColor;
                fixed3 recovery = SampleRecovery(world, worldDx, worldDy);
                if (radius >= _NearBlendStartEnd.y) return recovery;
                half nearWeight = 1.0h - smoothstep(
                    _NearBlendStartEnd.x, _NearBlendStartEnd.y, radius);
                return lerp(recovery, nearColor, nearWeight);
            }

            float2 ProductToCanopySource(float2 product)
            {
                return float2(
                    dot(_CanopyProductToSourceRow0.xy, product) + _CanopyProductToSourceRow0.z,
                    dot(_CanopyProductToSourceRow1.xy, product) + _CanopyProductToSourceRow1.z);
            }

            float2 ProductDerivativeToCanopySource(float2 productDerivative)
            {
                return float2(
                    dot(_CanopyProductToSourceRow0.xy, productDerivative),
                    dot(_CanopyProductToSourceRow1.xy, productDerivative));
            }

            half2 CanopySourceNormalToProduct(half2 sourceNormalXZ)
            {
                return half2(
                    dot(_CanopySourceToProductRow0.xy, sourceNormalXZ),
                    dot(_CanopySourceToProductRow1.xy, sourceNormalXZ));
            }

            float3 SampleFacade(
                float3 worldPosition,
                float3 worldNormal,
                float4 semanticData,
                float2 wallUv,
                out float roughness,
                out float metallic,
                out float ao,
                out float3 mappedNormal)
            {
                // Offline OSM usage classification is constant across every vertex of a
                // source footprint.  This replaces the former world-cell randomizer, so
                // a structure can never switch between unrelated facade families.
                float tileIndex = floor(semanticData.r * 255.0 + 0.5);
                float2 cell = float2(frac(tileIndex * 0.25) * 4.0, floor(tileIndex * 0.25));
                // Atlas scale is expressed in repeats per metre. The previous
                // values enlarged brick courses, panels, and corrugation enough
                // to read as a toy skin in-headset.
                float physicalScale = tileIndex < 0.5 ? 0.90 :
                    (tileIndex < 1.5 ? 0.48 :
                    (tileIndex < 6.0 ? 0.34 : 0.56));
                float2 localUv = wallUv * physicalScale;
                float2 atlasUv = (cell + 0.02 + frac(localUv) * 0.96) * 0.25;
                float3 albedo = tex2D(_FacadeAtlas, atlasUv).rgb;
                float encodedUsage = floor(semanticData.a * 255.0 + 0.5);
                float usageCode = floor(encodedUsage / 32.0) * 32.0;
                float residential = step(16.0, usageCode) * (1.0 - step(48.0, usageCode));
                float civic = step(48.0, usageCode) * (1.0 - step(80.0, usageCode));
                float commercial = step(80.0, usageCode) * (1.0 - step(112.0, usageCode));
                float industrial = step(112.0, usageCode) * (1.0 - step(144.0, usageCode));
                float seed = semanticData.g;
                float palette = floor(seed * 4.0);
                float3 residentialTint = palette < 1.0 ? float3(0.91, 0.83, 0.72) :
                    (palette < 2.0 ? float3(1.02, 0.98, 0.90) :
                    (palette < 3.0 ? float3(0.80, 0.88, 0.91) : float3(0.94, 0.90, 0.82)));
                float3 civicTint = palette < 2.0 ? float3(0.90, 0.92, 0.94) :
                    float3(1.02, 0.96, 0.84);
                float3 commercialTint = palette < 1.0 ? float3(0.78, 0.86, 0.94) :
                    (palette < 2.0 ? float3(0.93, 0.95, 0.96) :
                    (palette < 3.0 ? float3(0.82, 0.90, 0.86) : float3(0.94, 0.88, 0.78)));
                float3 industrialTint = palette < 1.0 ? float3(0.72, 0.76, 0.78) :
                    (palette < 2.0 ? float3(0.88, 0.89, 0.86) :
                    (palette < 3.0 ? float3(0.80, 0.85, 0.88) : float3(0.94, 0.88, 0.74)));
                float3 familyTint = residentialTint * residential + civicTint * civic +
                    commercialTint * commercial + industrialTint * industrial +
                    float3(0.93, 0.94, 0.92) *
                    (1.0 - saturate(residential + civic + commercial + industrial));
                albedo *= familyTint;
                float3 mrao = tex2D(_FacadeMRAO, atlasUv).rgb;
                roughness = saturate(max(0.58, mrao.g));
                metallic = saturate(mrao.r);
                ao = saturate(mrao.b);
                float3 tangentNormal = UnpackNormal(tex2D(_FacadeNormal, atlasUv));
                tangentNormal.xy *= 0.42;
                float3 tangent = normalize(cross(float3(0.0, 1.0, 0.0), worldNormal));
                if (dot(tangent, tangent) < 0.1) tangent = float3(1.0, 0.0, 0.0);
                float3 bitangent = normalize(cross(worldNormal, tangent));
                if (bitangent.y < 0.0) bitangent = -bitangent;
                mappedNormal = normalize(
                    tangent * tangentNormal.x + bitangent * tangentNormal.y +
                    worldNormal * tangentNormal.z);
                return albedo;
            }

            float SoftInterval(float value, float minimumValue, float maximumValue, float width)
            {
                return smoothstep(minimumValue - width, minimumValue + width, value) *
                       (1.0h - smoothstep(maximumValue - width, maximumValue + width, value));
            }

            float3 ApplyFacadeFeatures(
                float3 facade,
                float3 worldPosition,
                float3 worldNormal,
                float4 semanticData,
                float2 wallUv,
                out float windowMask,
                out float bakedGlint)
            {
                float encodedUsage = floor(semanticData.a * 255.0 + 0.5);
                float usageCode = floor(encodedUsage / 32.0) * 32.0;
                float semanticDetail = encodedUsage - usageCode;
                float wallRole = fmod(semanticDetail, 4.0);
                float facadePattern = floor(semanticDetail / 4.0);
                float entranceWall = 1.0 - step(0.5, abs(wallRole - 1.0));
                float rearWall = 1.0 - step(0.5, abs(wallRole - 2.0));
                float serviceWall = 1.0 - step(0.5, abs(wallRole - 3.0));
                float residential = step(16.0, usageCode) * (1.0 - step(48.0, usageCode));
                float civic = step(48.0, usageCode) * (1.0 - step(80.0, usageCode));
                float commercial = step(80.0, usageCode) * (1.0 - step(112.0, usageCode));
                float industrial = step(112.0, usageCode) * (1.0 - step(144.0, usageCode));
                float stableSeed = semanticData.g;
                float wallLength = max(0.5, semanticData.b * 64.0);
                float horizontal = wallUv.x;
                float heightAboveBase = max(0.0, wallUv.y);
                float patternOffset = (facadePattern - 3.5) / 3.5;
                float moduleWidth = residential * 3.0 + civic * 3.65 +
                    commercial * 3.35 + industrial * 7.2 +
                    (1.0 - saturate(residential + civic + commercial + industrial)) * 3.6;
                moduleWidth *= 1.0 + patternOffset * 0.075;
                float floorHeight = residential * 2.85 + civic * 3.45 +
                    commercial * 3.30 + industrial * 4.4 +
                    (1.0 - saturate(residential + civic + commercial + industrial)) * 3.2;
                floorHeight *= 1.0 + patternOffset * 0.035;
                float cornerClearance = lerp(0.75, 1.2, industrial);
                float usableWall = step(cornerClearance, horizontal) *
                    step(horizontal, wallLength - cornerClearance);
                float availableWidth = max(moduleWidth, wallLength - cornerClearance * 2.0);
                float bayCount = max(1.0, floor(availableWidth / moduleWidth + 0.5));
                float bayWidth = availableWidth / bayCount;
                float bayCoordinate = (horizontal - cornerClearance) / bayWidth;
                float module = frac(bayCoordinate);
                float bayIndex = floor(bayCoordinate);
                float storey = frac(heightAboveBase / floorHeight);
                float pixelFootprint = max(fwidth(horizontal), fwidth(heightAboveBase));
                float featureFade = 1.0 - smoothstep(0.22, 0.65, pixelFootprint);
                float edge = saturate(pixelFootprint * 0.18 + 0.007);

                float residentialWindow = SoftInterval(module, 0.30, 0.70, edge) *
                    SoftInterval(storey, 0.30, 0.75, edge);
                float civicWindow = SoftInterval(module, 0.24, 0.76, edge) *
                    SoftInterval(storey, 0.24, 0.78, edge);
                float commercialWindow = SoftInterval(module, 0.20, 0.80, edge) *
                    SoftInterval(storey, 0.22, 0.80, edge);
                float industrialBayEnabled = step(0.48,
                    frac((bayIndex + facadePattern * 1.37) * 0.6180339));
                float industrialWindow = SoftInterval(module, 0.36, 0.64, edge) *
                    SoftInterval(storey, 0.64, 0.83, edge) *
                    industrialBayEnabled;
                float otherWindow = SoftInterval(module, 0.31, 0.69, edge) *
                    SoftInterval(storey, 0.31, 0.74, edge);
                float residentialOuter = SoftInterval(module, 0.25, 0.75, edge) *
                    SoftInterval(storey, 0.25, 0.80, edge);
                float civicOuter = SoftInterval(module, 0.19, 0.81, edge) *
                    SoftInterval(storey, 0.18, 0.83, edge);
                float commercialOuter = SoftInterval(module, 0.15, 0.85, edge) *
                    SoftInterval(storey, 0.17, 0.85, edge);
                float industrialOuter = SoftInterval(module, 0.31, 0.69, edge) *
                    SoftInterval(storey, 0.59, 0.88, edge) *
                    industrialBayEnabled;
                float otherOuter = SoftInterval(module, 0.26, 0.74, edge) *
                    SoftInterval(storey, 0.26, 0.79, edge);
                float roleWindowWeight = lerp(1.0, 0.72, rearWall) *
                    lerp(1.0, 0.48, serviceWall);
                windowMask = (residentialWindow * residential + civicWindow * civic +
                    commercialWindow * commercial + industrialWindow * industrial +
                    otherWindow * (1.0h - saturate(residential + civic + commercial + industrial))) *
                    featureFade * usableWall * roleWindowWeight;
                float windowOuter = (residentialOuter * residential + civicOuter * civic +
                    commercialOuter * commercial + industrialOuter * industrial +
                    otherOuter * (1.0 - saturate(residential + civic + commercial + industrial))) *
                    featureFade * usableWall * roleWindowWeight;

                float groundStorey = 1.0 - smoothstep(floorHeight, floorHeight + 0.25,
                    heightAboveBase);
                float doorWidth = lerp(1.05, 3.4, industrial);
                float doorHeight = lerp(2.25, 3.8, industrial);
                float doorCenter = wallLength * 0.5;
                float doorMask = (1.0 - smoothstep(doorWidth * 0.48,
                    doorWidth * 0.55 + pixelFootprint, abs(horizontal - doorCenter))) *
                    SoftInterval(heightAboveBase, 0.06, doorHeight, edge) *
                    groundStorey * featureFade * entranceWall * usableWall;
                float serviceDoorWidth = max(3.2, min(5.4, bayWidth * 0.76));
                float serviceDoor = (1.0 - smoothstep(serviceDoorWidth * 0.48,
                    serviceDoorWidth * 0.54 + pixelFootprint, abs(horizontal - doorCenter))) *
                    SoftInterval(heightAboveBase, 0.04, min(4.2, floorHeight * 0.92), edge) *
                    featureFade * serviceWall * industrial * usableWall;
                doorMask = saturate(doorMask + serviceDoor);
                windowMask *= 1.0 - doorMask;
                windowOuter *= 1.0 - doorMask;
                float windowFrame = saturate(windowOuter - windowMask);

                // Static, opaque sky/ground colors provide reflection cues without an
                // additional transparent pass or realtime probe.  A restrained sunward
                // highlight produces a glint only at useful close-view pixel sizes.
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - worldPosition);
                float3 reflectionDirection = reflect(-viewDirection, normalize(worldNormal));
                float3 reflectedEnvironment = texCUBE(_FacadeReflectionCube,
                    reflectionDirection).rgb;
                float windowVertical = frac(heightAboveBase / floorHeight);
                float skyCue = saturate(0.26 + windowVertical * 0.54 + stableSeed * 0.10);
                float3 bakedWindow = lerp(float3(0.025, 0.040, 0.052),
                    max(reflectedEnvironment, float3(0.16, 0.25, 0.34)), skyCue);
                float3 doorColor = industrial > 0.5
                    ? float3(0.19, 0.21, 0.22)
                    : float3(0.15, 0.11, 0.085);
                float3 frameColor = lerp(float3(0.82, 0.84, 0.82),
                    float3(0.17, 0.18, 0.18), industrial + commercial * 0.65);
                facade = lerp(facade, frameColor, windowFrame * 0.88);
                facade = lerp(facade, bakedWindow, windowMask * 0.98);
                facade = lerp(facade, doorColor, doorMask * 0.90);
                // A soft foundation darkening visually seats the simplified shell in
                // the photographed ground without another decal, skirt, or draw.
                facade *= lerp(0.76, 1.0, smoothstep(0.02, 0.55, heightAboveBase));
                bakedGlint = windowMask * featureFade;
                return facade;
            }

            fixed4 frag(v2f input, fixed facing : VFACE) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                UNITY_BRANCH if (input.treeData.x < -0.5h)
                {
                    // Authored leaf cards share the existing material and pass.
                    // No broad solid crown, extra camera, or transparent layer.
                    half4 detail = tex2D(_NeighborhoodFoliageAtlas, input.wallUv);
                    half alpha = saturate((detail.a - 0.28h) / max(fwidth(detail.a), 0.02h) + 0.5h);
                    clip(alpha - 0.01h);
                    half3 sourceColor = GammaToLinearSpace(input.semanticData.rgb);
                    half sourceLuminance = max(dot(sourceColor, half3(0.2126h,0.7152h,0.0722h)),0.025h);
                    half3 albedo = detail.rgb * lerp(0.78h,1.12h,saturate(sourceLuminance/0.2h));
                    half3 normal = normalize(input.worldNormal);
                    half direct = saturate(dot(normal,normalize(_WorldSpaceLightPos0.xyz))*0.8h+0.2h);
                    half3 ambient = max(ShadeSH9(half4(normal,1)),half3(0.24h,0.24h,0.24h));
                    half3 color = albedo * (ambient + _LightColor0.rgb * direct * 0.68h) * input.semanticData.a;
                    UNITY_APPLY_FOG(input.fogCoord,color);
                    return half4(color,alpha);
                }
                half3 geometricNormal = normalize(input.worldNormal);
                half sourceTreeCard = saturate(input.treeData.x + input.treeData.y);
                half nearFoliageCard = saturate(
                    step(0.0h, input.wallUv.x) * step(0.5h, _SemanticMode) +
                    sourceTreeCard);
                fixed4 nearFoliage = fixed4(1.0h, 1.0h, 1.0h, 1.0h);
                UNITY_BRANCH if (nearFoliageCard > 0.5h)
                {
                    clip(input.treeData.z - .001h);
                    UNITY_BRANCH if (input.treeData.y > 0.5h)
                    {
                        half frame = floor(input.treeData.w + 0.5h);
                        half2 atlasCell = half2(
                            frame - floor(frame * 0.25h) * 4.0h,
                            1.0h - floor(frame * 0.25h));
                        nearFoliage = tex2D(
                            _TreeImpostorAtlas,
                            (atlasCell + input.wallUv) / half2(4.0h, 2.0h));
                    }
                    else if (input.treeData.x > 0.5h)
                    {
                        nearFoliage = tex2D(_HeroLeafAtlas, input.wallUv);
                    }
                    else
                    {
                        nearFoliage = tex2D(_NearFoliageAtlas, input.wallUv);
                    }
                    clip(nearFoliage.a - _FoliageCutoff);
                    if (facing < 0.0h) geometricNormal = -geometricNormal;
                }
                half topWeight = smoothstep(0.54h, 0.78h, geometricNormal.y);
                half bottomWeight = smoothstep(0.54h, 0.88h, -geometricNormal.y);
                float2 world = input.worldPosition.xz;
                float2 worldDx = ddx(world);
                float2 worldDy = ddy(world);
                fixed3 topColor;

                if (_DiagnosticMode > 0.5h)
                {
                    float2 canopySource = ProductToCanopySource(world);
                    float2 canopySourceDx = ProductDerivativeToCanopySource(worldDx);
                    float2 canopySourceDy = ProductDerivativeToCanopySource(worldDy);
                    float2 diagnosticUv = saturate(
                        (canopySource - _CanopyWorldMinInvSize.xy) * _CanopyWorldMinInvSize.zw);
                    half height = tex2Dgrad(_NormalizedSurfaceHeight, diagnosticUv,
                        canopySourceDx * _CanopyWorldMinInvSize.zw,
                        canopySourceDy * _CanopyWorldMinInvSize.zw).r;
                    fixed3 low = fixed3(0.04h, 0.18h, 0.65h);
                    fixed3 high = fixed3(0.95h, 0.82h, 0.12h);
                    return fixed4(lerp(low, high, saturate(height * 4.0h)), 1.0h);
                }

                float roughness;
                float metallic = 0.0;
                float surfaceAo = 1.0;
                float facadeWindowMask = 0.0;
                float facadeGlintMask = 0.0;
                float3 sideColor;
                if (_SemanticMode < 0.5h)
                {
                    // Canopy supplies its own top color; orthophoto work is building-only.
                    topColor = SampleOrthophoto(world, worldDx, worldDy) * _TopTint.rgb;
                    float3 facadeNormal;
                    sideColor = SampleFacade(input.worldPosition, geometricNormal,
                        input.semanticData, input.wallUv, roughness,
                        metallic, surfaceAo, facadeNormal) * _SideTint.rgb;
                    sideColor = ApplyFacadeFeatures(sideColor, input.worldPosition,
                        geometricNormal, input.semanticData, input.wallUv,
                        facadeWindowMask, facadeGlintMask);
                    facadeGlintMask *= 1.0h - topWeight;
                    geometricNormal = normalize(lerp(facadeNormal, geometricNormal, topWeight));
                }
                else
                {
                    roughness = _SideRoughness;
                    half trunkMask = step(0.5h, input.semanticData.r);
                    half semanticSeed = frac(sin(dot(
                        floor(input.worldPosition.xz / 16.0h),
                        half2(12.9898h, 78.233h))) * 43758.5453h);
                    float2 canopySource = ProductToCanopySource(world);
                    float2 canopySourceDx = ProductDerivativeToCanopySource(worldDx);
                    float2 canopySourceDy = ProductDerivativeToCanopySource(worldDy);
                    float2 canopyUv = saturate(
                        (canopySource - _CanopyWorldMinInvSize.xy) * _CanopyWorldMinInvSize.zw);
                    half4 canopy = tex2Dgrad(_CanopyNormalOcclusion, canopyUv,
                        canopySourceDx * _CanopyWorldMinInvSize.zw,
                        canopySourceDy * _CanopyWorldMinInvSize.zw);
                    half2 normalXZ = CanopySourceNormalToProduct(canopy.rg * 2.0h - 1.0h) *
                                     _CanopyNormalStrength;
                    half3 sampledNormal = normalize(half3(normalXZ.x, 1.0h, normalXZ.y));
                    geometricNormal = normalize(lerp(geometricNormal, sampledNormal, topWeight));
                    half microX = sin(input.worldPosition.x * 0.83h +
                        sin(input.worldPosition.y * 1.37h +
                        input.worldPosition.z * 0.61h + semanticSeed * 9.7h));
                    half microZ = sin(input.worldPosition.z * 0.91h +
                        sin(input.worldPosition.x * 0.57h -
                        input.worldPosition.y * 1.21h + semanticSeed * 11.3h));
                    geometricNormal = normalize(geometricNormal +
                        half3(microX, 0.0h, microZ) * (0.075h + 0.035h * topWeight));
                    surfaceAo = canopy.a;
                    // GBA carries the offline robust median of lidar-supported
                    // canopy pixels in the exact ground tile. Sampling it once per
                    // semantic cell prevents pale soil at a broad crown edge from
                    // washing out the complete grove while retaining local hue.
                    fixed3 localCanopyColor = GammaToLinearSpace(input.semanticData.gba);
                    half densityProxy = saturate(canopy.a);
                    half localLuminance = max(0.04h,
                        dot(localCanopyColor, fixed3(0.2126h, 0.7152h, 0.0722h)));
                    // The generator rejects deep photographed shadow pixels and
                    // transports a sunlit local vegetation target. Preserve that
                    // target through the production sun/ambient response instead
                    // of pre-darkening it and then lighting it a second time.
                    half crownLuminance = clamp(localLuminance * 0.92h, 0.042h, 0.27h);
                    fixed3 foliageReference = lerp(
                        fixed3(0.19h, 0.30h, 0.105h),
                        fixed3(0.12h, 0.25h, 0.070h), densityProxy);
                    foliageReference *= lerp(0.94h, 1.06h, semanticSeed);
                    half foliageLuminance = max(0.04h,
                        dot(foliageReference, fixed3(0.2126h, 0.7152h, 0.0722h)));
                    fixed3 registeredLocalCanopy = localCanopyColor *
                        (crownLuminance / localLuminance);
                    float2 foliageTopUv = input.worldPosition.xz * 0.082h +
                        semanticSeed * 13.7h;
                    float2 foliageSideUv = lerp(input.worldPosition.zy, input.worldPosition.xy,
                        step(abs(geometricNormal.z), abs(geometricNormal.x))) * 0.095h +
                        semanticSeed * 9.3h;
                    fixed3 foliageDetail = tex2D(_CanopyFoliageAlbedo,
                        lerp(foliageSideUv, foliageTopUv, topWeight)).rgb;
                    half detailLuminance = max(0.04h,
                        dot(foliageDetail, fixed3(0.2126h, 0.7152h, 0.0722h)));
                    half foliageDetailModulation = clamp(
                        detailLuminance / 0.27h, 0.84h, 1.18h);
                    fixed3 crownColor = lerp(registeredLocalCanopy,
                        foliageReference * (crownLuminance / foliageLuminance), 0.12h);
                    crownColor *= foliageDetailModulation;
                    UNITY_BRANCH if (nearFoliageCard > 0.5h)
                    {
                        half atlasLuminance = max(0.035h,
                            dot(nearFoliage.rgb, fixed3(0.2126h, 0.7152h, 0.0722h)));
                        fixed3 registeredAtlas = nearFoliage.rgb *
                            (crownLuminance / atlasLuminance);
                        UNITY_BRANCH if (_GroveLeafResponse > 0.5h || (_BranchFoliageResponse > 0.5h && sourceTreeCard < 0.5h))
                        {
                            // Retain photographed leaf shading instead of dividing
                            // out every pixel's luminance. Reference is the measured
                            // median opaque-pixel luminance of the reused atlas.
                            registeredAtlas *= clamp(atlasLuminance / 0.1271h,0.48h,1.60h);
                            UNITY_BRANCH if (_GroveLeafResponse > 0.5h)
                            {
                                half crownFraction=input.semanticData.r*(255.0h/124.0h);
                                registeredAtlas *= lerp(0.68h,1.0h,smoothstep(0.46h,0.90h,crownFraction));
                            }
                        }
                        crownColor = lerp(registeredLocalCanopy, registeredAtlas, 0.78h);
                        surfaceAo = lerp(surfaceAo, 0.82h, 0.72h);
                        roughness = 0.86h;
                    }
                    fixed3 crownSide = lerp(_CanopySideTint.rgb, crownColor, 0.94h) *
                                       lerp(0.92h, 1.0h, canopy.a);
                    fixed3 trunkColor = lerp(fixed3(0.16h, 0.105h, 0.058h),
                                             fixed3(0.27h, 0.19h, 0.10h),
                                             semanticSeed);
                    sideColor = lerp(crownSide, trunkColor, trunkMask);
                    topColor = lerp(crownColor, trunkColor, trunkMask);
                    roughness = lerp(roughness, 0.94h, trunkMask);
                    surfaceAo = lerp(surfaceAo, 0.66h, trunkMask);
                }

                fixed3 albedo = lerp(sideColor, topColor, topWeight);
                albedo = lerp(albedo, sideColor * 0.68h, bottomWeight);
                roughness = lerp(roughness, _TopRoughness, topWeight);

                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half ndotl = saturate(dot(geometricNormal, lightDirection));
                half3 ambient = max(ShadeSH9(half4(geometricNormal, 1.0h)),
                    half3(_AmbientFloor, _AmbientFloor, _AmbientFloor));
                ambient *= lerp(0.68h, 1.0h, surfaceAo);
                half3 direct = _LightColor0.rgb * (0.07h + ndotl * 0.68h);
                half3 color = albedo * (ambient + direct);
                half3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.worldPosition);
                half3 halfDirection = normalize(lightDirection + viewDirection);
                half specular = pow(saturate(dot(geometricNormal, halfDirection)),
                    lerp(52.0h, 6.0h, roughness)) * (1.0h - roughness) *
                    lerp(0.08h, 0.24h, metallic);
                color += _LightColor0.rgb * specular;
                if (_SemanticMode < 0.5h && topWeight > 0.001h)
                {
                    half3 roofReflectionDirection = reflect(-viewDirection, geometricNormal);
                    half3 roofReflection = texCUBE(_FacadeReflectionCube,
                        roofReflectionDirection).rgb;
                    color += roofReflection * topWeight * (0.035h + (1.0h - roughness) * 0.055h);
                }
                if (_SemanticMode < 0.5h && facadeGlintMask > 0.001h)
                {
                    half facadeSun = pow(saturate(dot(geometricNormal, halfDirection)), 28.0h);
                    color += _LightColor0.rgb * facadeSun * facadeGlintMask * 0.62h;
                }
                half roofGlint = topWeight * pow(saturate(dot(geometricNormal, halfDirection)), 34.0h);
                color += _LightColor0.rgb * roofGlint * 0.13h;

                half treeCoverage = saturate(input.treeData.z);
                // Reconstruct subpixel edge coverage for primary branch cards.
                // Raw filtered alpha otherwise attenuates interior foliage again
                // through MSAA alpha-to-coverage. Hero/impostor atlases retain
                // their existing response; their texture calibration is separate.
                half branchAlpha=nearFoliage.a;
                UNITY_BRANCH if(nearFoliageCard>0.5h && (_GroveLeafResponse>0.5h || (_BranchFoliageResponse>0.5h && sourceTreeCard<0.5h)))
                    branchAlpha=saturate((nearFoliage.a-_FoliageCutoff)/max(fwidth(nearFoliage.a),0.02h)+0.5h);
                half outputAlpha = nearFoliageCard > 0.5h
                    ? branchAlpha * treeCoverage
                    : treeCoverage;
                fixed4 output = fixed4(color, outputAlpha);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
