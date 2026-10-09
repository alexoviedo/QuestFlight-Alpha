"""Check the bounded serialized configuration supporting optional URP deferrals.

This is a source/serialization guard, not an APK dependency or visual acceptance
claim. It follows current GUID references like the audit dependency diagnostic,
without rebuilding its exhaustive repository coverage ledger.
"""
import argparse
from collections import deque
import hashlib
import json
from pathlib import Path
import re

SERIALIZED_EXTENSIONS = {'.unity', '.prefab', '.asset', '.mat', '.shadergraph', '.shadersubgraph'}
FORBIDDEN_FEATURES = ('SurfaceCache', 'OnTilePostProcess', 'ScreenSpaceAmbientOcclusion',
                      'DecalRendererFeature', 'BlitToRTHandle', 'DepthBlit', 'MrtRendererFeature')


def check_configuration(texts):
    errors = []
    build = texts['build']
    scene_entries = re.findall(r'- enabled: (\d)\s+path: ([^\r\n]+)', build)
    if scene_entries != [('1', 'Assets/Scenes/ProductionVerticalSlice.unity')]:
        errors.append('Build scenes differ from the sole authored production scene')
    if 'pipeline' in texts:
        renderers = re.search(r'm_RendererDataList:\s*\n((?:\s*- [^\n]*\n)*)', texts['pipeline'])
        if not renderers or re.findall(r'guid: ([0-9a-f]{32})', renderers[1]) != ['fabfb3b687edb5d4a827e093a6946e78']:
            errors.append('Pipeline renderer list differs from ProductForwardRenderer')
    if 'quality' in texts:
        quality_pipelines = re.findall(r'customRenderPipeline: [^\n]*guid: ([0-9a-f]{32})', texts['quality'])
        if not quality_pipelines or set(quality_pipelines) != {'0cb9bdcc56d6a2440ae8a4a26346ee70'}:
            errors.append('Quality levels select a different rendering pipeline')
    if 'editorAssembly' in texts:
        assembly = json.loads(texts['editorAssembly'])
        if assembly.get('includePlatforms') != ['Editor']:
            errors.append('URP optional Editor tooling is no longer excluded from Android')
    if re.search(r'\bSURFACE_CACHE\b', texts['defines']):
        errors.append('SURFACE_CACHE is enabled in PlayerSettings')
    if not re.search(r'm_EnableRenderCompatibilityMode:\s*0\b', texts['global']):
        errors.append('RenderGraph compatibility mode is enabled or unknown')
    if any(feature in texts['renderer'] for feature in FORBIDDEN_FEATURES):
        errors.append('An optional renderer feature entered ProductForwardRenderer')
    feature_section = re.search(r'm_RendererFeatures:\s*\n((?:\s*- [^\n]*\n)*)', texts['renderer'])
    if not feature_section or len(re.findall(r'fileID:', feature_section[1])) != 1:
        errors.append('Product renderer must retain its sole authored motion feature')
    if 'QuestFlight.Rendering.ProductSpaceWarpRenderFeature' not in texts['renderer']:
        errors.append('Product motion feature identity is missing')
    if re.search(r'm_RenderPostProcessing:\s*1\b', texts['scene']):
        errors.append('Product scene enables camera postprocessing')
    if not re.search(r'components:\s*\[\]', texts['volume']):
        errors.append('Default volume profile is no longer empty')
    if re.search(r'^--- !u!(?:218|154|156)\s', texts['scene'], re.M):
        errors.append('Unity Terrain/TerrainCollider/TerrainData entered the product scene')
    return errors


def inspect(project):
    files = {
        'build': 'ProjectSettings/EditorBuildSettings.asset',
        'defines': 'ProjectSettings/ProjectSettings.asset',
        'global': 'Assets/UniversalRenderPipelineGlobalSettings.asset',
        'renderer': 'Assets/Production/Rendering/ProductForwardRenderer.asset',
        'scene': 'Assets/Scenes/ProductionVerticalSlice.unity',
        'volume': 'Assets/DefaultVolumeProfile.asset',
        'pipeline': 'Assets/Production/Rendering/ProductQuestURP.asset',
        'quality': 'ProjectSettings/QualitySettings.asset',
        'editorAssembly': 'Packages/com.unity.render-pipelines.universal/Editor/Unity.RenderPipelines.Universal.Editor.asmdef',
    }
    texts = {key: (project / path).read_text(encoding='utf-8-sig') for key, path in files.items()}
    errors = check_configuration(texts)
    guid_map = {}
    for base in ('Assets', 'Packages'):
        for meta in (project/base).rglob('*.meta'):
            match = re.search(r'^guid: ([0-9a-f]{32})$', meta.read_text(errors='replace'), re.M)
            if match: guid_map[match[1]] = meta.relative_to(project).as_posix()[:-5]
    seeds = [files['scene'], 'Assets/Production/Rendering/ProductQuestURP.asset', files['global']]
    seeds += [path.relative_to(project).as_posix() for path in (project/'Assets').rglob('*')
              if path.is_file() and '/Resources/' in path.as_posix() and path.suffix!='.meta']
    queue = deque(seeds)
    seen, edges, skipped_binary = set(), {}, []
    while queue:
        relative = queue.popleft()
        if relative in seen: continue
        seen.add(relative)
        path = project/relative
        if not path.is_file() or path.suffix not in SERIALIZED_EXTENSIONS: continue
        raw = path.read_bytes()
        if b'\x00' in raw[:100]:
            skipped_binary.append(relative)
            continue
        text = raw.decode('utf-8-sig', errors='replace')
        references = set(re.findall(r'guid["\s]*:[\s"]*([0-9a-f]{32})', text))
        targets = {guid_map[guid] for guid in references if guid in guid_map}
        edges[relative] = sorted(targets)
        queue.extend(targets-seen)
        if path.suffix in {'.unity', '.prefab'} and re.search(r'^--- !u!(?:218|154|156)\s', text, re.M):
            errors.append('Unity Terrain dependency: '+relative)
    samples = [path for path in seen if '/Samples~/' in path]
    if samples: errors.append('Unimported package samples referenced by product: '+', '.join(samples))
    # Catch explicit imported copies even if their GUID changed on copying.
    copied_sample_types = []
    for path in (project/'Assets').rglob('*.cs'):
        if re.search(r'\bclass\s+(?:FirstPersonController|DepthBlitFeature|BlitToRTHandleRendererFeature|MrtRendererFeature)\b',
                     path.read_text(errors='replace')):
            copied_sample_types.append(path.relative_to(project).as_posix())
    if copied_sample_types: errors.append('Sample classes imported into Assets: '+', '.join(copied_sample_types))
    return {'passed': not errors, 'errors': errors, 'seeds': seeds, 'paths': sorted(seen), 'edges': edges,
            'configuration_sha256': {path: hashlib.sha256((project/path).read_bytes()).hexdigest()
                                     for path in files.values()},
            'binary_assets_not_parsed': skipped_binary, 'sample_paths': samples,
            'copied_sample_types': copied_sample_types,
            'limits': 'Conservative serialized/source check only; package Editor resources may be reachable from global settings without runtime use. Binary/import-generated dependency and exact APK acceptance remain separate.'}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--project', type=Path, default=Path('QuestFlight'))
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = inspect(args.project)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({key: result[key] for key in ('passed', 'errors', 'sample_paths', 'copied_sample_types')}))
    return int(not result['passed'])


if __name__ == '__main__': raise SystemExit(main())
