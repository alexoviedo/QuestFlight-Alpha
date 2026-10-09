"""Regression guards preserve the activation assumptions for M17 deferrals."""
import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('optional_guard', ROOT/'tools/rendering/check_optional_path_exclusion.py')
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)


class OptionalPathExclusionTests(unittest.TestCase):
    def test_current_product_excludes_optional_paths(self):
        result = guard.inspect(ROOT/'QuestFlight')
        self.assertTrue(result['passed'], result['errors'])

    def test_activation_negative_controls(self):
        current = {
            'build': (ROOT/'QuestFlight/ProjectSettings/EditorBuildSettings.asset').read_text(),
            'defines': (ROOT/'QuestFlight/ProjectSettings/ProjectSettings.asset').read_text(),
            'global': (ROOT/'QuestFlight/Assets/UniversalRenderPipelineGlobalSettings.asset').read_text(),
            'renderer': (ROOT/'QuestFlight/Assets/Production/Rendering/ProductForwardRenderer.asset').read_text(),
            'scene': (ROOT/'QuestFlight/Assets/Scenes/ProductionVerticalSlice.unity').read_text(),
            'volume': (ROOT/'QuestFlight/Assets/DefaultVolumeProfile.asset').read_text(),
            'pipeline': (ROOT/'QuestFlight/Assets/Production/Rendering/ProductQuestURP.asset').read_text(),
            'quality': (ROOT/'QuestFlight/ProjectSettings/QualitySettings.asset').read_text(),
            'editorAssembly': (ROOT/'QuestFlight/Packages/com.unity.render-pipelines.universal/Editor/Unity.RenderPipelines.Universal.Editor.asmdef').read_text(),
        }
        fixtures = [
            ('defines', lambda value: value.replace('scriptingDefineSymbols: {}', 'scriptingDefineSymbols: {Android: SURFACE_CACHE}')),
            ('global', lambda value: value.replace('m_EnableRenderCompatibilityMode: 0', 'm_EnableRenderCompatibilityMode: 1')),
            ('scene', lambda value: value.replace('m_RenderPostProcessing: 0', 'm_RenderPostProcessing: 1')),
            ('volume', lambda value: value.replace('components: []', 'components: [{fileID: 1}]')),
            ('scene', lambda value: value+'\n--- !u!218 &1\nTerrain:\n'),
            ('build', lambda value: value.replace('ProductionVerticalSlice.unity', 'OptionalSample.unity')),
            ('pipeline', lambda value: value.replace('fabfb3b687edb5d4a827e093a6946e78', 'a'*32)),
            ('quality', lambda value: value.replace('0cb9bdcc56d6a2440ae8a4a26346ee70', 'a'*32)),
            ('editorAssembly', lambda value: value.replace('"Editor"', '"Android"')),
        ]
        fixtures += [('renderer', lambda value, feature=feature: value+'\n  m_EditorClassIdentifier: '+feature)
                     for feature in guard.FORBIDDEN_FEATURES]
        for key, mutation in fixtures:
            with self.subTest(key=key, mutation=mutation):
                texts = dict(current)
                texts[key] = mutation(texts[key])
                self.assertNotEqual(texts[key], current[key])
                self.assertTrue(guard.check_configuration(texts))


if __name__ == '__main__': unittest.main()
