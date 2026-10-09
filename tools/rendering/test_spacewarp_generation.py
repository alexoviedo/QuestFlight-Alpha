"""Focused generator state/history regressions; never regenerate product shaders."""
import re
import tempfile
import unittest
from pathlib import Path
from port_spacewarp_shaders import effective_pass_cull, generate, previous_far_billboard_vertex


class SelectedPassStateTests(unittest.TestCase):
    def fixture(self, parent='Back', selected='', outline='Front'):
        return ('Shader "Fixture" { SubShader { Cull ' + parent +
                ' Pass { Name "Outline" Cull ' + outline +
                ' CGPROGRAM\n#pragma vertex outline\nENDCG } '
                'Pass { Name "Fill" ' + selected +
                ' CGPROGRAM\n#pragma vertex baseVertex\nENDCG } } }')

    def test_previous_outline_cull_does_not_inherit(self):
        source = self.fixture()
        selected = source.index('CGPROGRAM', source.index('Name "Fill"'))
        old = re.findall(r'\bCull\s+(Off|Back|Front)', source[:selected])[-1]
        self.assertEqual(old, 'Front')  # Demonstrated original negative control.
        self.assertEqual(effective_pass_cull(source, selected), 'Back')

    def test_selected_override_and_subshader_property(self):
        for parent, override, expected in [('Front', 'Cull Off', 'Off'),
                                           ('[_Cull]', '', '[_Cull]'),
                                           ('Off', 'Cull Back', 'Back')]:
            source = self.fixture(parent, override)
            self.assertEqual(effective_pass_cull(source, source.index('CGPROGRAM', source.index('Name "Fill"'))), expected)

    def test_nested_and_commented_tokens_are_not_direct_state(self):
        source = self.fixture('Back', 'Tags { "Hint"="Cull Front" } /* Cull Off */ // Cull Front\n')
        self.assertEqual(effective_pass_cull(source, source.index('CGPROGRAM', source.index('Name "Fill"'))), 'Back')

    def test_actual_pinned_sdk_hand_regeneration_has_fill_cull_and_history(self):
        with tempfile.TemporaryDirectory(prefix='qfl-hand-regression-') as directory:
            target = Path(directory) / 'OculusHand.shader'
            generate(target)
            source = target.read_text()
            motion = source[source.index('Name "XRMotionVectors"'):]
            self.assertIn('Cull Back Blend Off ZWrite On', motion)
            self.assertIn('v.qfPreviousVertex', motion)
            self.assertIn('clip(coverage-0.5)', motion)
            self.assertIn('Name "HandOutlineURP"', source)

    def test_far_card_generator_only_changes_previous_vertex(self):
        source = 'v2f QFPreviousVertex(appdata input) { output.position = QFObjectToMotionClip(input.vertex, qfWorld); }'
        output = previous_far_billboard_vertex(source)
        self.assertIn('QFPreviousVerticalBillboardWorld(input.vertex)', output)
        self.assertIn('input.uv.x >= 5.5h && input.uv.x < 7.5h', output)
        self.assertIn('input.color.r < .5h', output)
        with self.assertRaises(ValueError): previous_far_billboard_vertex('changed projection')


if __name__ == '__main__':
    unittest.main()
