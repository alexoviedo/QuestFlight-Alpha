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
        _DiagnosticMode ("Editor diagnostic", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 110
        Cull Back
        ZWrite On
        ZTest LEqual

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
            half _DiagnosticMode;

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
                output.wallUv = input.uv;
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
                float2 nearUv = saturate((world - _NearWorldMinInvSize.xy) * _NearWorldMinInvSize.zw);
                fixed3 nearColor = tex2Dgrad(_NearAlbedo, nearUv,
                    worldDx * _NearWorldMinInvSize.zw,
                    worldDy * _NearWorldMinInvSize.zw).rgb;
                half radius = max(abs(world.x), abs(world.y));
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

            fixed3 SampleFacade(
                float3 worldPosition,
                half3 worldNormal,
                fixed4 semanticData,
                float2 wallUv,
                out half roughness,
                out half metallic,
                out half ao,
                out half3 mappedNormal)
            {
                // Offline OSM usage classification is constant across every vertex of a
                // source footprint.  This replaces the former world-cell randomizer, so
                // a structure can never switch between unrelated facade families.
                half tileIndex = floor(semanticData.r * 255.0h + 0.5h);
                half2 cell = half2(fmod(tileIndex, 4.0h), floor(tileIndex * 0.25h));
                // Atlas scale is expressed in repeats per metre. The previous
                // values enlarged brick courses, panels, and corrugation enough
                // to read as a toy skin in-headset.
                half physicalScale = tileIndex < 0.5h ? 0.68h :
                    (tileIndex < 1.5h ? 0.34h :
                    (tileIndex < 6.0h ? 0.22h : 0.38h));
                float2 localUv = wallUv * physicalScale;
                half2 atlasUv = (cell + 0.02h + frac(localUv) * 0.96h) * 0.25h;
                fixed3 albedo = tex2D(_FacadeAtlas, atlasUv).rgb;
                half encodedUsage = floor(semanticData.a * 255.0h + 0.5h);
                half usageCode = encodedUsage - fmod(encodedUsage, 2.0h);
                half residential = step(16.0h, usageCode) * (1.0h - step(48.0h, usageCode));
                half civic = step(48.0h, usageCode) * (1.0h - step(80.0h, usageCode));
                half commercial = step(80.0h, usageCode) * (1.0h - step(112.0h, usageCode));
                half industrial = step(112.0h, usageCode) * (1.0h - step(144.0h, usageCode));
                half seed = semanticData.g;
                fixed3 residentialTint = lerp(fixed3(0.82h, 0.78h, 0.70h),
                                               fixed3(1.05h, 1.02h, 0.94h), seed);
                fixed3 civicTint = lerp(fixed3(0.86h, 0.89h, 0.92h),
                                        fixed3(1.04h, 0.98h, 0.88h), seed);
                fixed3 commercialTint = lerp(fixed3(0.82h, 0.88h, 0.94h),
                                             fixed3(1.02h, 1.03h, 1.0h), seed);
                fixed3 industrialTint = lerp(fixed3(0.76h, 0.79h, 0.80h),
                                             fixed3(1.03h, 0.98h, 0.90h), seed);
                fixed3 familyTint = residentialTint * residential + civicTint * civic +
                    commercialTint * commercial + industrialTint * industrial +
                    fixed3(0.93h, 0.94h, 0.92h) *
                    (1.0h - saturate(residential + civic + commercial + industrial));
                albedo *= familyTint;
                half3 mrao = tex2D(_FacadeMRAO, atlasUv).rgb;
                roughness = saturate(max(0.58h, mrao.g));
                metallic = saturate(mrao.r);
                ao = saturate(mrao.b);
                half3 tangentNormal = UnpackNormal(tex2D(_FacadeNormal, atlasUv));
                tangentNormal.xy *= 0.42h;
                half3 tangent = normalize(cross(half3(0.0h, 1.0h, 0.0h), worldNormal));
                if (dot(tangent, tangent) < 0.1h) tangent = half3(1.0h, 0.0h, 0.0h);
                half3 bitangent = normalize(cross(worldNormal, tangent));
                if (bitangent.y < 0.0h) bitangent = -bitangent;
                mappedNormal = normalize(
                    tangent * tangentNormal.x + bitangent * tangentNormal.y +
                    worldNormal * tangentNormal.z);
                return albedo;
            }

            half SoftInterval(half value, half minimumValue, half maximumValue, half width)
            {
                return smoothstep(minimumValue - width, minimumValue + width, value) *
                       (1.0h - smoothstep(maximumValue - width, maximumValue + width, value));
            }

            fixed3 ApplyFacadeFeatures(
                fixed3 facade,
                float3 worldPosition,
                half3 worldNormal,
                fixed4 semanticData,
                float2 wallUv,
                out half windowMask,
                out half bakedGlint)
            {
                half encodedUsage = floor(semanticData.a * 255.0h + 0.5h);
                half entranceWall = fmod(encodedUsage, 2.0h);
                half usageCode = encodedUsage - entranceWall;
                half residential = step(16.0h, usageCode) * (1.0h - step(48.0h, usageCode));
                half civic = step(48.0h, usageCode) * (1.0h - step(80.0h, usageCode));
                half commercial = step(80.0h, usageCode) * (1.0h - step(112.0h, usageCode));
                half industrial = step(112.0h, usageCode) * (1.0h - step(144.0h, usageCode));
                half stableSeed = semanticData.g;
                half wallLength = max(0.5h, semanticData.b * 64.0h);
                half horizontal = wallUv.x;
                half heightAboveBase = max(0.0h, wallUv.y);
                half moduleWidth = residential * 3.0h + civic * 3.6h +
                    commercial * 3.2h + industrial * 6.8h +
                    (1.0h - saturate(residential + civic + commercial + industrial)) * 3.6h;
                half floorHeight = residential * 2.9h + civic * 3.5h +
                    commercial * 3.35h + industrial * 4.2h +
                    (1.0h - saturate(residential + civic + commercial + industrial)) * 3.2h;
                half cornerClearance = lerp(0.75h, 1.2h, industrial);
                half usableWall = step(cornerClearance, horizontal) *
                    step(horizontal, wallLength - cornerClearance);
                half module = frac((horizontal - cornerClearance) / moduleWidth + stableSeed * 0.17h);
                half storey = frac(heightAboveBase / floorHeight);
                half pixelFootprint = max(fwidth(horizontal), fwidth(heightAboveBase));
                half featureFade = 1.0h - smoothstep(0.10h, 0.30h, pixelFootprint);
                half edge = saturate(pixelFootprint * 0.35h + 0.012h);

                half residentialWindow = SoftInterval(module, 0.30h, 0.70h, edge) *
                    SoftInterval(storey, 0.30h, 0.75h, edge);
                half civicWindow = SoftInterval(module, 0.24h, 0.76h, edge) *
                    SoftInterval(storey, 0.24h, 0.78h, edge);
                half commercialWindow = SoftInterval(module, 0.20h, 0.80h, edge) *
                    SoftInterval(storey, 0.22h, 0.80h, edge);
                half industrialWindow = SoftInterval(module, 0.36h, 0.64h, edge) *
                    SoftInterval(storey, 0.64h, 0.83h, edge) *
                    step(0.58h, frac(stableSeed * 19.17h + floor(horizontal / moduleWidth)));
                half otherWindow = SoftInterval(module, 0.31h, 0.69h, edge) *
                    SoftInterval(storey, 0.31h, 0.74h, edge);
                windowMask = (residentialWindow * residential + civicWindow * civic +
                    commercialWindow * commercial + industrialWindow * industrial +
                    otherWindow * (1.0h - saturate(residential + civic + commercial + industrial))) *
                    featureFade * usableWall;

                half groundStorey = 1.0h - smoothstep(floorHeight, floorHeight + 0.25h,
                    heightAboveBase);
                half doorWidth = lerp(1.05h, 3.4h, industrial);
                half doorHeight = lerp(2.25h, 3.8h, industrial);
                half doorCenter = lerp(cornerClearance + doorWidth * 0.6h,
                    wallLength - cornerClearance - doorWidth * 0.6h, stableSeed);
                half doorMask = (1.0h - smoothstep(doorWidth * 0.48h,
                    doorWidth * 0.55h + pixelFootprint, abs(horizontal - doorCenter))) *
                    SoftInterval(heightAboveBase, 0.06h, doorHeight, edge) *
                    groundStorey * featureFade * entranceWall * usableWall;
                windowMask *= 1.0h - doorMask;

                // Static, opaque sky/ground colors provide reflection cues without an
                // additional transparent pass or realtime probe.  A restrained sunward
                // highlight produces a glint only at useful close-view pixel sizes.
                half3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - worldPosition);
                half3 reflectionDirection = reflect(-viewDirection, normalize(worldNormal));
                fixed3 reflectedEnvironment = texCUBE(_FacadeReflectionCube,
                    reflectionDirection).rgb;
                half windowVertical = frac(heightAboveBase / floorHeight);
                half skyCue = saturate(0.26h + windowVertical * 0.54h + stableSeed * 0.10h);
                fixed3 bakedWindow = lerp(fixed3(0.025h, 0.040h, 0.052h),
                    max(reflectedEnvironment, fixed3(0.16h, 0.25h, 0.34h)), skyCue);
                fixed3 doorColor = industrial > 0.5h
                    ? fixed3(0.19h, 0.21h, 0.22h)
                    : fixed3(0.15h, 0.11h, 0.085h);
                facade = lerp(facade, bakedWindow, windowMask * 0.92h);
                facade = lerp(facade, doorColor, doorMask * 0.90h);
                bakedGlint = windowMask * featureFade;
                return facade;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 geometricNormal = normalize(input.worldNormal);
                half topWeight = smoothstep(0.54h, 0.78h, geometricNormal.y);
                half bottomWeight = smoothstep(0.54h, 0.88h, -geometricNormal.y);
                float2 world = input.worldPosition.xz;
                float2 worldDx = ddx(world);
                float2 worldDy = ddy(world);
                fixed3 topColor = SampleOrthophoto(world, worldDx, worldDy) * _TopTint.rgb;

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

                half roughness;
                half metallic = 0.0h;
                half surfaceAo = 1.0h;
                half facadeWindowMask = 0.0h;
                half facadeGlintMask = 0.0h;
                fixed3 sideColor;
                if (_SemanticMode < 0.5h)
                {
                    half3 facadeNormal;
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
                    topColor *= lerp(0.72h, 1.0h, canopy.a);
                    surfaceAo = canopy.a;
                    // Crown color is anchored to the exact orthophoto below it. This keeps
                    // raised volume visually continuous with every photographed grove
                    // instead of imposing a disconnected generic green. Vertex R=1 marks
                    // the sparse grounded trunks compiled into the same opaque draw.
                    fixed3 localCanopyColor = topColor *
                        lerp(fixed3(0.92h, 0.95h, 0.89h), fixed3(1.0h, 1.0h, 0.96h),
                            input.semanticData.g);
                    // Retain the local photographic luminance while gently restoring
                    // foliage chroma.  This keeps crowns registered to the imagery
                    // without projecting beige soil/roof pixels onto their sides.
                    half localLuminance = max(0.04h,
                        dot(localCanopyColor, fixed3(0.2126h, 0.7152h, 0.0722h)));
                    fixed3 foliageReference = lerp(
                        fixed3(0.46h, 0.56h, 0.30h),
                        fixed3(0.32h, 0.52h, 0.19h), input.semanticData.g);
                    foliageReference *= lerp(0.94h, 1.06h, input.semanticData.b);
                    half foliageLuminance = max(0.04h,
                        dot(foliageReference, fixed3(0.2126h, 0.7152h, 0.0722h)));
                    fixed3 crownColor = lerp(localCanopyColor,
                        foliageReference * (localLuminance / foliageLuminance), 0.46h);
                    fixed3 crownSide = lerp(_CanopySideTint.rgb, crownColor, 0.74h) *
                                       lerp(0.84h, 1.0h, canopy.a);
                    fixed3 trunkColor = lerp(fixed3(0.16h, 0.105h, 0.058h),
                                             fixed3(0.27h, 0.19h, 0.10h),
                                             input.semanticData.b);
                    sideColor = lerp(crownSide, trunkColor, trunkMask);
                    topColor = lerp(crownColor, trunkColor, trunkMask);
                    roughness = lerp(roughness, 0.94h, trunkMask);
                    surfaceAo = lerp(surfaceAo, 0.66h, trunkMask);
                }

                fixed3 albedo = lerp(sideColor, topColor, topWeight);
                albedo = lerp(albedo, sideColor * 0.42h, bottomWeight);
                roughness = lerp(roughness, _TopRoughness, topWeight);

                half3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                half ndotl = saturate(dot(geometricNormal, lightDirection));
                half3 ambient = max(ShadeSH9(half4(geometricNormal, 1.0h)),
                    half3(_AmbientFloor, _AmbientFloor, _AmbientFloor));
                ambient *= lerp(0.48h, 1.0h, surfaceAo);
                half3 direct = _LightColor0.rgb * (0.07h + ndotl * 0.68h);
                half3 color = albedo * (ambient + direct);
                half3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - input.worldPosition);
                half3 halfDirection = normalize(lightDirection + viewDirection);
                half specular = pow(saturate(dot(geometricNormal, halfDirection)),
                    lerp(52.0h, 6.0h, roughness)) * (1.0h - roughness) *
                    lerp(0.08h, 0.24h, metallic);
                color += _LightColor0.rgb * specular;
                if (_SemanticMode < 0.5h && facadeGlintMask > 0.001h)
                {
                    half facadeSun = pow(saturate(dot(geometricNormal, halfDirection)), 28.0h);
                    color += _LightColor0.rgb * facadeSun * facadeGlintMask * 0.62h;
                }
                half roofGlint = topWeight * pow(saturate(dot(geometricNormal, halfDirection)), 34.0h);
                color += _LightColor0.rgb * roofGlint * 0.13h;

                fixed4 output = fixed4(color, 1.0h);
                UNITY_APPLY_FOG(input.fogCoord, output);
                return output;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
