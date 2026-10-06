"""Deterministic migration of accepted color programs; generated MV reuses coverage.

The source is the explicit accepted Git revision, never an already-generated pass.
Run from repository root. Color math remains unchanged; generated previous-position
programs account for view-facing groves, skinning and CPU instrument vertices.
Cloud depth and intentionally background-owned transparency are explicit policies.
"""
from pathlib import Path
import re, subprocess, json, runpy

BASE = 'fdcc160c23360676ca9d87aa3626009785f8e28f'
ROOT = Path('QuestFlight/Assets')
BACKGROUND = {'ProductionCockpitWindowReflection.shader','ProductionInstrumentGlassReflection.shader',
               'ProductionPropellerBlur.shader','AircraftRunwayBlobShadow.shader'}
EXCLUDE = {'ProductionCockpitLocalShadowDepth.shader','BoulderCloudShell.shader'}
# These shaders receive bounded explicit instance submissions. Other scene
# renderers need Unity's per-renderer previous matrix in the motion pass.
BATCH_MOTION = {'ProductionSemanticSurface.shader','KBDU_TrafficVehicle.shader',
                'MapBranchGrove.shader','MapBranchGroveCluster.shader',
                'ProductionSparseVegetation.shader','AerialVegetationOpaque.shader'}
# These materials represent fixed world surfaces, including streamed mesh pages.
# Page replacement is not movement of the old page into the new page. Preserve
# view-facing foliage deformation, but never apply another object's model history.
STATIC_WORLD_MOTION = {
    'BridgeConcrete.shader','FullMapBuildingClipmap.shader',
    'ProductionFarTerrain.shader','ProductionMacroGround.shader','ProductionRunwayMarking.shader',
    'ProductionSparseVegetation.shader','ProductionStableWater.shader','AirportPerimeterFence.shader',
    'MapBranchGrove.shader','MapBranchGroveCluster.shader','NeighborhoodCanopyReview.shader',
    'KBDU_OpaqueVegetationContext.shader','KBDU_OsmBuildingContext.shader','KBDU_TerrainPyramid.shader',
    'SemanticTowerOpaque.shader','ProductionSemanticSurface.shader','AirportCoreBakedLit.shader',
    'AirportCoreContinuousRunway.shader','KBDUReservoirWaterV4.shader','KBDUVegetationHeroV4.shader',
    'KBDU_AerialBuildingShell.shader','AerialVegetationOpaque.shader'
}

def stabilize_facade_windows(source):
    """Filter periodic apertures before wrapping; preserve their average coverage."""
    if 'float FilteredWindowPulse(' in source:
        return source
    marker = '            half WindowMask('
    helper = '''            float WindowPulseIntegral(float coordinate, float lower, float upper)
            {
                return floor(coordinate) * (upper - lower) +
                    clamp(frac(coordinate) - lower, 0.0, upper - lower);
            }

            float FilteredWindowPulse(float coordinate, float lower, float upper)
            {
                // Derivatives AFTER frac/floor alias when a pixel spans one or
                // more bays. Integrate the unwrapped pixel footprint instead.
                // This retains average window coverage at distance, not a fade.
                float footprint = max(fwidth(coordinate), 0.0001);
                float coverage = saturate((WindowPulseIntegral(coordinate + footprint * 0.5, lower, upper) -
                    WindowPulseIntegral(coordinate - footprint * 0.5, lower, upper)) / footprint);
                return lerp(coverage, upper - lower, smoothstep(0.5, 1.0, footprint));
            }

'''
    replacements = {
        marker: helper + marker,
        'half facadeRole, out float2 apertureUv)\n            {\n                half bayWidth':
        'half facadeRole, out float2 apertureUv, out float2 apertureMeters)\n            {\n                half bayWidth',
        '                half slot = usable / max(1.0h, bayCount);':
        '                half slot = usable / max(1.0h, bayCount);\n                apertureMeters = float2(max(slot * openingWidth, 0.01), openingHeight);',
        '                half edgeX = max(fwidth(bayLocal), 0.002h) * 1.5h;\n': '',
        '''                half horizontal = smoothstep(left, left + edgeX, bayLocal) *
                    (1.0h - smoothstep(1.0h - left - edgeX, 1.0h - left, bayLocal));''':
        '''                half horizontal = FilteredWindowPulse(localX / max(slot, 0.01h), left, 1.0h - left);''',
        '''                half verticalEdge = max(fwidth(floorLocal), 0.003h) * 1.5h;
                half vertical = smoothstep(sill, sill + verticalEdge, floorLocal) *
                    (1.0h - smoothstep(sill + openingHeight - verticalEdge, sill + openingHeight, floorLocal));''':
        '''                half vertical = FilteredWindowPulse(uvMeters.y / storeyHeight,
                    sill / storeyHeight, (sill + openingHeight) / storeyHeight);''',
        '''                    float2 windowUv;
                    window = WindowMask''':
        '''                    float2 windowUv, windowMeters;
                    float2 wallDx = ddx(i.detailUv), wallDy = ddy(i.detailUv);
                    window = WindowMask''',
        '                        i.atlasCell.y, windowUv);':
        '                        i.atlasCell.y, windowUv, windowMeters);',
        '''                        fixed4 aperture = tex2D(_FacadeDetail,
                            DetailAtlasUv(windowUv, min(5.0h, facadeStyle)));''':
        '''                        // Use UNWRAPPED metric gradients, including across bay edges.
                        // Once unresolved, sample the center of the cell's own last
                        // mip, never a mip shared with a neighboring atlas style.
                        float2 dx = wallDx / windowMeters * (0.93 * 0.25);
                        float2 dy = wallDy / windowMeters * (0.93 * 0.25);
                        float footprint = max(length(dx), length(dy));
                        float2 detailUv = lerp(windowUv, float2(0.5,0.5),
                            smoothstep(0.125,0.25,footprint));
                        float gradientScale = min(1.0, 0.25 / max(footprint,0.0001));
                        fixed4 aperture = tex2Dgrad(_FacadeDetail,
                            DetailAtlasUv(detailUv, min(5.0h, facadeStyle)),
                            dx * gradientScale, dy * gradientScale);'''
    }
    for old, new in replacements.items():
        if old not in source:
            raise ValueError('Facade window source changed; review filtering patch')
        source = source.replace(old, new)
    return source

def separate_facade_lighting(source):
    """Shade masonry and flat glass separately before filtering their coverage."""
    if 'half3 windowAlbedo' in source:
        return source
    replacements = {
        'float2(fmod(cell, 4.0h), floor(cell * 0.25h))':
        'float2(fmod(cell, 4.0h), 3.0h - floor(cell * 0.25h))',
        '                half window = 0.0h;':
        '                half window = 0.0h;\n                half3 windowAlbedo = 0.0h;\n                half windowCoverage = 0.0h;',
        """                        albedo = lerp(albedo, lerp(aperture.rgb, environmentReflection, reflectionWeight),
                            window * aperture.a * 0.92);
                        roughness = lerp(roughness, 0.22h, window);""":
        """                        windowAlbedo = lerp(aperture.rgb, environmentReflection, reflectionWeight);
                        windowCoverage = window * aperture.a * 0.92h;""",
        '                    color += _LightColor0.rgb * specular * shadow * lerp(0.04h, 0.36h, window);':
        """                    color += _LightColor0.rgb * specular * shadow * 0.04h;
                    // A partially covered pixel contains two surfaces. Filter their
                    // lit colors, not roughness before the nonlinear specular lobe.
                    // Glass uses a flat normal, never the underlying brick relief.
                    half3 glassNormal = normalize(i.worldNormal);
                    half3 glassIrradiance = max(ShadeSH9(half4(glassNormal, 1.0h)), 0.0h) * 0.35h + _AmbientLift +
                        _LightColor0.rgb * (0.08h + saturate(dot(glassNormal, lightDir)) * 0.55h) * shadow;
                    half glassSpecular = pow(saturate(dot(glassNormal, halfVector)), 58.36h);
                    half3 glassColor = windowAlbedo * glassIrradiance * lerp(0.76h, 1.0h, occlusion) +
                        _LightColor0.rgb * glassSpecular * shadow * 0.36h;
                    color = lerp(color, glassColor, windowCoverage);"""
    }
    for old, new in replacements.items():
        if old not in source:
            raise ValueError('Facade lighting source changed; review patch')
        source = source.replace(old, new)
    return source

def block_end(text, start):
    depth=0
    # Shader syntax and these programs have balanced braces in comments too.
    for i in range(start,len(text)):
        if text[i]=='{': depth+=1
        elif text[i]=='}':
            depth-=1
            if depth==0:return i
    raise ValueError('unbalanced block')

def ensure_stereo(program):
    signature=re.search(r'(\w+)\s+(\w+)\s*\(\s*(\w+)\s+(\w+)\s*\)\s*\{',program)
    vertex=re.search(r'#pragma vertex (\w+)',program)
    if not vertex:return program
    signature=re.search(r'(\w+)\s+'+vertex[1]+r'\s*\(\s*(\w+)\s+(\w+)\s*\)',program)
    if not signature:return program
    varying,attrib,param=signature.groups()
    for name,macro in [(attrib,'UNITY_VERTEX_INPUT_INSTANCE_ID'),(varying,'UNITY_VERTEX_OUTPUT_STEREO')]:
        st=re.search(r'struct\s+'+name+r'\s*\{',program)
        if not st:continue
        end=block_end(program,program.index('{',st.start()))
        if macro not in program[st.end():end]:program=program[:end]+'\n'+macro+'\n'+program[end:]
    start=re.search(r'\b'+varying+r'\s+'+vertex[1]+r'\s*\(',program).start()
    body=program.index('{',start);end=block_end(program,body)
    content=program[body:end]
    if 'UNITY_SETUP_INSTANCE_ID' not in content:
        program=program[:body+1]+'\nUNITY_SETUP_INSTANCE_ID('+param+');'+program[body+1:]
    start=re.search(r'\b'+varying+r'\s+'+vertex[1]+r'\s*\(',program).start();body=program.index('{',start);end=block_end(program,body)
    content=program[body:end]
    if 'UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO' not in content:
        out=re.search(r'\b'+varying+r'\s+(\w+)\s*(?:=[^;]+)?;',content)
        if not out:raise ValueError('Cannot find vertex output '+vertex[1])
        idx=body+out.end();program=program[:idx]+'\nUNITY_INITIALIZE_VERTEX_OUTPUT_STEREO('+out[1]+');'+program[idx:]
    fragment=re.search(r'#pragma fragment (\w+)',program)
    if fragment:
        fsig=re.search(r'\b'+fragment[1]+r'\s*\(\s*'+varying+r'\s+(\w+)[^)]*\)\s*:[^{]+\{',program)
        if fsig:
            end=block_end(program,fsig.end()-1)
            if 'UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX' not in program[fsig.end():end]:
                program=program[:fsig.end()]+'\nUNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX('+fsig[1]+');'+program[fsig.end():]
    if '#pragma multi_compile_instancing' not in program:program+='\n#pragma multi_compile_instancing\n'
    return program

def generate(path):
    if path.name in VENDOR:
        original=VENDOR[path.name];source=original.read_text(encoding='utf-8-sig')
        source=re.sub(r'Shader "([^"]+)"',lambda m:'Shader "QuestFlight/XR/'+m[1]+'"',source,count=1)
        if path.name=='OculusHand.shader':
            included=(original.parent/'OculusHandFillCG.cginc').read_text(encoding='utf-8-sig')
            included=included.replace('#include "GlowFunctions.cginc"','#include "Packages/com.meta.xr.sdk.interaction/Runtime/Shaders/GlowFunctions.cginc"')
            source=source.replace('#include "OculusHandFillCG.cginc"',included)
        source=re.sub(r'#include "(Oculus[^"/]+\.cginc)"',r'#include "Packages/com.meta.xr.sdk.interaction/Runtime/Shaders/\1"',source)
    else:source=subprocess.check_output(['git','show',f'{BASE}:{path.as_posix()}']).decode('utf-8-sig')
    if path.name == 'FullMapBuildingClipmap.shader':
        source = separate_facade_lighting(stabilize_facade_windows(source))
    source=re.sub(r'CGPROGRAM(.*?)ENDCG',lambda m:'CGPROGRAM'+ensure_stereo(m[1])+'ENDCG',source,flags=re.S)
    if path.name=='BoulderCloudShell.shader':
        source=source.replace('"ForwardBase"','"UniversalForward"')
        program=re.search(r'CGPROGRAM(.*?)ENDCG',source,re.S)[1]
        program=program.replace('#pragma target 3.5','#pragma target 4.5')
        program=program.replace('#include "Lighting.cginc"','#include "Lighting.cginc"\n#include "Assets/Shaders/ProductSpaceWarpMotion.cginc"\nfloat4 _QFPreviousCloudDrift; float _CloudMotionOpacity;')
        # The color intermediate can be Y-flipped relative to the provider's
        # motion attachment. Raster coverage and fragment depth must use the
        # same provider projection; otherwise cloud vectors land on other pixels.
        program=program.replace('output.position = UnityWorldToClipPos(output.worldPosition);',
            'output.position = mul(_NonJitteredViewProjMatrixStereo[QFEye()],float4(output.worldPosition,1));')
        program=program.replace('fixed4 frag(v2f input) : SV_Target','float4 frag(v2f input, out float depth : SV_Depth) : SV_Target')
        program=program.replace('half3 accumulated = 0.0h;','half3 accumulated = 0.0h; float weightedDistance=0, totalWeight=0;')
        program=program.replace('transmittance *= 1.0h - sampleOpacity;','weightedDistance += sampleDistance*transmittance*sampleOpacity;\n                        totalWeight += transmittance*sampleOpacity;\n                        transmittance *= 1.0h - sampleOpacity;')
        program=program.replace('return fixed4(accumulated * _KBDUCloudExteriorOpacity * input.windVisibility, opacity);','''clip(opacity-_CloudMotionOpacity);
                float3 samplePoint=cameraWorld+rayDirectionWorld*(weightedDistance/max(totalWeight,.0001));
                float3 drift=_CloudDrift.xyz-_QFPreviousCloudDrift.xyz;
                // Periodic wind accumulation can wrap without physical motion.
                drift=frac((drift+16000)/32000)*32000-16000;
                float3 oldPoint=samplePoint-mul((float3x3)unity_ObjectToWorld,drift);
                float4 current=mul(_NonJitteredViewProjMatrixStereo[QFEye()],float4(samplePoint,1));
                float4 previous=mul(_PrevViewProjMatrixStereo[QFEye()],float4(oldPoint,1));
                depth=current.z/current.w;
                return QFMotion(current,previous,1);''')
        sub=source.index('{',source.index('SubShader'));end=block_end(source,sub)
        source=source[:end]+'''\nPass {
            Name "XRMotionVectors" Tags { "LightMode"="XRMotionVectors" }
            Blend Off ZWrite On ZTest LEqual ColorMask RGBA
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            CGPROGRAM
'''+program+'\nENDCG\n}\n'+source[end:]
        source=source.replace('Properties\n    {','Properties\n    {\n        _CloudMotionOpacity("Dominant cloud motion opacity",Range(.1,1))=.5\n        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1')
        source=runpy.run_path(str(Path(__file__).resolve().parents[1]/'clouds'/'optimize_cloud_shader.py'))['transform'](source)
        path.write_text(source,encoding='utf-8');return {'path':str(path),'policy':'opacity-weighted volume depth, wrapped wind displacement; shared optional sparse volume sampling'}
    if path.name=='ProductionCockpitLocalShadowDepth.shader':
        source=source.replace('float4 _CockpitLocalShadowDepthParams;', 'float4 _CockpitLocalShadowDepthParams; float4x4 _QFShadowView, _QFShadowVP;').replace('mul(UNITY_MATRIX_MV, input.vertex)', 'mul(_QFShadowView, mul(unity_ObjectToWorld,input.vertex))').replace('UnityObjectToClipPos(input.vertex)', 'mul(_QFShadowVP,mul(unity_ObjectToWorld,input.vertex))')
    if path.name in EXCLUDE:
        path.write_text(source.replace('"ForwardBase"','"UniversalForward"'),encoding='utf-8')
        return {'path':str(path),'policy':'specialized'}
    source=source.replace('"ForwardBase"','"UniversalForward"')
    if path.name in BACKGROUND:
        path.write_text(source,encoding='utf-8');return {'path':str(path),'policy':'background depth and motion'}
    programs=list(re.finditer(r'CGPROGRAM(.*?)ENDCG',source,re.S))
    prog=next((m for m in programs if '#pragma vertex baseVertex' in m[1]),programs[0] if programs else None)
    if not prog:raise ValueError(f'No color program: {path}')
    code=prog[1]
    if '#include \"UnityCG.cginc\"' not in code:code='\n#include \"UnityCG.cginc\"\n'+code
    vertex=re.search(r'#pragma vertex (\w+)',code)[1];frag=re.search(r'#pragma fragment (\w+)',code)[1]
    signature=re.search(r'(\w+)\s+'+vertex+r'\s*\(\s*(\w+)\s+(\w+)\s*\)',code)
    if not signature:raise ValueError(f'Unsupported vertex {path}')
    varying,attrib,param=signature.groups()
    astr=re.search(r'struct\s+'+attrib+r'\s*\{',code); ae=block_end(code,code.index('{',astr.start()))
    body=code[astr.end():ae]
    vertexfield=re.search(r'(\w+)\s*:\s*POSITION',body)[1]
    if 'TEXCOORD4' in body:raise ValueError(f'Occupied history channel: {path}')
    instance=code.find('UNITY_VERTEX_INPUT_INSTANCE_ID',astr.end(),ae)
    insert=instance if instance>=0 else ae
    code=code[:insert]+'\nfloat3 qfPreviousVertex : TEXCOORD4;\n'+code[insert:]
    vst=re.search(r'struct\s+'+varying+r'\s*\{',code);ve=block_end(code,code.index('{',vst.start()))
    clipfield=re.search(r'(\w+)\s*:\s*SV_POSITION',code[vst.end():ve])[1]
    # Vulkan multiview requires ordinary varying inputs before the generated
    # stereo/system signature fields. Keep Unity's stereo tail last.
    stereo=code.find('UNITY_VERTEX_OUTPUT_STEREO',vst.end(),ve)
    insert=stereo if stereo>=0 else ve
    code=code[:insert]+'\nfloat4 qfCurrent : TEXCOORD14; float4 qfPrevious : TEXCOORD15;\n'+code[insert:]
    code=re.sub(r'#pragma target [\d.]+','#pragma target 4.5',code)
    if '#pragma target' not in code:code='\n#pragma target 4.5\n'+code
    if '#pragma multi_compile_instancing' not in code:code+='\n#pragma multi_compile_instancing\n'
    # Keep the instancing pragma even for ordinary moving renderers: Unity
    # uses it to compile XR stereo variants too. Object batching is disabled
    # on the motion RendererLists, independently of stereo instancing. Explicit
    # vegetation/traffic command-buffer draws retain their history arrays.
    code=code.replace('#include "UnityCG.cginc"','#include "UnityCG.cginc"\n#include "Assets/Shaders/ProductSpaceWarpMotion.cginc"')
    if path.name in STATIC_WORLD_MOTION:
        code='#define QF_STATIC_WORLD_GEOMETRY 1\n'+code
    code=re.sub(r'\b'+vertex+r'\s*\(', 'QFColorVertex(',code)
    code=re.sub(r'\b'+frag+r'\s*\(', 'QFColorFragment(',code)
    oldstart=re.search(r'\b'+varying+r'\s+QFColorVertex\s*\(',code).start()
    oldend=block_end(code,code.index('{',oldstart))+1
    motion_vertex=code[oldstart:oldend]
    motion_vertex=motion_vertex.replace(attrib+' '+param+')',attrib+' '+param+', out float4 qfWorld)')
    # Capture the actual (possibly deformed) world position before projection.
    # Source shaders use either object-space projection or world-space groves.
    def capture_projection(body, function, replacement):
        token=function+'('
        while token in body:
            start=body.index(token); end=start+len(token); depth=1
            while depth:
                if body[end]=='(':depth+=1
                elif body[end]==')':depth-=1
                end+=1
            arg=body[start+len(token):end-1]
            body=body[:start]+replacement+'('+arg+', qfWorld)'+body[end:]
        return body
    motion_vertex=capture_projection(motion_vertex,'UnityObjectToClipPos','QFObjectToMotionClip')
    motion_vertex=motion_vertex.replace('mul(UNITY_MATRIX_VP,float4(p,1))','QFWorldToMotionClip(float4(p,1),qfWorld)')
    motion_vertex=motion_vertex.replace('mul(UNITY_MATRIX_VP,float4(world,1))','QFWorldToMotionClip(float4(world,1),qfWorld)')
    if 'QFObjectToMotionClip' not in motion_vertex and 'QFWorldToMotionClip' not in motion_vertex:
        raise ValueError(f'No direct motion position: {path}')
    code=code[:oldstart]+motion_vertex+code[oldend:]
    oldend=oldstart+len(motion_vertex)
    prev=code[oldstart:oldend].replace('QFColorVertex','QFPreviousVertex')
    prev=prev.replace('_GroveViewPosition','_QFPreviousGroveViewPosition').replace('_AttitudeRotation','_QFPreviousAttitudeRotation')
    setup='UNITY_SETUP_INSTANCE_ID(v);' if 'UNITY_VERTEX_INPUT_INSTANCE_ID' in body else ''
    previous_depth_bias=''
    if 'authoredDepthLayer' in prev:
        previous_depth_bias=f'''// Preserve the same authored instrument layer separation as color.
    float4 oldUnbiased=mul(_NonJitteredViewProjMatrixStereo[QFEye()],previousWorld);
    o.qfPrevious.z+=(old.{clipfield}.z-oldUnbiased.z)/old.{clipfield}.w*o.qfPrevious.w;'''
    # CPU data lives in TEXCOORD4 just like Unity's previous skinned positions.
    wrapper=f'''\n{prev}
{varying} {vertex}({attrib} v)
{{
    {setup}
    float4 world;
    {varying} o=QFColorVertex(v,world);
    #if !defined(QF_STATIC_WORLD_GEOMETRY)
    if (_QFHasPreviousModel < .5 && (_QFPreviousVertices > .5 || unity_MotionVectorsParams.x == 1))
        v.{vertexfield}.xyz=v.qfPreviousVertex;
    #endif
    float4 previousWorld;
    {varying} old=QFPreviousVertex(v,previousWorld);
    float4 oldWorld=QFPreviousWorld(previousWorld);
    if (_QFResetObjectMotion > .5) oldWorld=world;
    o.qfCurrent=o.{clipfield};
    o.qfPrevious=mul(_PrevViewProjMatrixStereo[QFEye()],oldWorld);
    {previous_depth_bias}
    o.{clipfield}=o.qfCurrent;
    return o;
}}
float4 {frag}({varying} v) : SV_Target
{{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
    float coverage=QFColorFragment(v).a;
    clip(coverage-{'0.5' if '"Transparent"' in source else '0.001'});
    return QFMotion(v.qfCurrent,v.qfPrevious,coverage);
}}
'''
    # Fragment signatures with VFACE/out-depth get a dedicated implementation.
    fsig=re.search(r'QFColorFragment\s*\(([^)]*)\)',code)[1]
    if ',' in fsig:
        if 'VFACE' in fsig:
            wrapper=wrapper.replace(f'float4 {frag}({varying} v) : SV_Target',f'float4 {frag}({varying} v, fixed facing : VFACE) : SV_Target').replace('QFColorFragment(v).a','QFColorFragment(v, facing).a')
        else:raise ValueError(f'Special fragment required: {path} {fsig}')
    code+=wrapper
    culls=re.findall(r'\bCull\s+(Off|Back|Front|\[[^]]+\])',source[:prog.start()]); cull=culls[-1] if culls else 'Back'
    extra='''\n        Pass
        {
            Name "XRMotionVectors"
            Tags { "LightMode"="XRMotionVectors" }
            Blend Off ZWrite On ZTest LEqual ColorMask RGBA
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            CGPROGRAM
'''+code+'\n            ENDCG\n        }\n'
    extra=extra.replace('Blend Off ZWrite On',f'Cull {cull} Blend Off ZWrite On')
    sub=source.index('{',source.index('SubShader'));end=block_end(source,sub)
    source=source[:end]+extra+source[end:]
    props=re.search(r'Properties\s*\{',source)
    if props: source=source[:props.end()]+'\n        [HideInInspector] _XRMotionVectorsPass("XR motion",Float)=1\n'+source[props.end():]
    if re.search(r"ZTest\s+Always",source[:source.index('Name "XRMotionVectors"')]):
        offset=source.index('Name "XRMotionVectors"')
        source=source[:offset]+source[offset:].replace("ZTest LEqual","ZTest Always")
    path.write_text(source,encoding='utf-8')
    return {'path':str(path),'policy':('immutable world geometry; ' if path.name in STATIC_WORLD_MOTION else '')+
            'matched coverage, previous geometry and XR depth'}

VENDOR={}
for name in ['OculusRayVisual.shader','OculusRayCursor.shader','OculusHand.shader']:
    VENDOR[name]=next(Path('QuestFlight/Library/PackageCache').glob('com.meta.xr.sdk.interaction@*/Runtime/Shaders/'+name))

for name in ['Hotspot.shader','TransparentVertexTexture.shader','TunnelingEffect.shader']:
    VENDOR[name]=next(Path('QuestFlight/Library/PackageCache').glob('com.meta.xr.sdk.interaction@*/Runtime/Shaders/Locomotion/'+name))
VENDOR['TransparentTexture.shader']=next(Path('QuestFlight/Library/PackageCache').glob('com.meta.xr.sdk.core@*/Editor/BuildingBlocks/BlockData/RoomModel/Shaders/TransparentTexture.shader'))

if __name__=='__main__':
    records=[]
    for path in ROOT.rglob('*.shader'):
        if path.name in {'ProductWorldText.shader','SpaceWarpDiagnostic.shader'} or 'VendorMotion' in path.parts:continue
        if 'Editor' in path.parts:continue
        if 'TextMesh Pro' in path.parts and path.name!='TMP_SDF-Mobile.shader':continue
        records.append(generate(path))
    vendor_dir=ROOT/'Production/Rendering/VendorMotion';vendor_dir.mkdir(parents=True,exist_ok=True)
    for name in VENDOR:records.append(generate(vendor_dir/name))
    target=Path('QuestFlight/Assets/Production/Rendering/ShaderMigrationInventory.json');target.parent.mkdir(parents=True,exist_ok=True)
    target.write_text(json.dumps({'baseline':BASE,'shaders':records},indent=2)+'\n',encoding='utf-8')
    print(f'Ported {len(records)} shader policies')
