"""Caption regressions use real files/NumPy and deterministic speech dependency stubs.

The fixtures adapt the audit's caption_cache_diagnostic.py. No model downloads,
speech reconstruction, Unity rendering, or listening acceptance are claimed.
"""
import contextlib
import ast
import hashlib
import importlib.util
import io
import json
import re
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'tools/audio/author_caption_timings.py'
spec = importlib.util.spec_from_file_location('caption_author', SOURCE)
author = importlib.util.module_from_spec(spec)
spec.loader.exec_module(author)


class FakeAudio:
    def detach(self): return self
    def cpu(self): return self
    def numpy(self): return np.full(240, .1, dtype=np.float32)


class FakeModel:
    def __init__(self, **kwargs): pass
    def eval(self): return self


class FakePipeline:
    def __init__(self, **kwargs): pass
    def __call__(self, text, **kwargs):
        if text == 'FAIL': raise ValueError('Injected synthesis failure')
        return [types.SimpleNamespace(audio=FakeAudio(), tokens=[
            types.SimpleNamespace(start_ts=0., end_ts=.009, text=text, whitespace=False)])]


FAKES = {
    'torch': types.SimpleNamespace(set_num_threads=lambda n: None, manual_seed=lambda n: None,
                                  load=lambda *args, **kwargs: None),
    'huggingface_hub': types.SimpleNamespace(snapshot_download=lambda *args, **kwargs: '/unused'),
    'kokoro': types.SimpleNamespace(KModel=FakeModel, KPipeline=FakePipeline),
    'soundfile': types.SimpleNamespace(read=lambda *args, **kwargs:
                                      (np.full(240, .1, dtype=np.float32) * .85, 24000)),
}


def run_fixture(module, identifiers, cached_ids, fail=None):
    with tempfile.TemporaryDirectory(prefix='caption_fixture_') as temporary:
        root = Path(temporary)
        voice = root / 'voice'
        voice.mkdir()
        output = root / 'timings.json'
        entries = []
        cache = []
        for identifier in dict.fromkeys(identifiers + cached_ids):
            payload = ('synthetic-' + identifier).encode()
            (voice / (identifier + '.wav')).write_bytes(payload)
            digest = hashlib.sha256(payload).hexdigest()
            if identifier in identifiers:
                entries.append(dict(id=identifier, sha256=digest, speed=1,
                                    spoken_text='FAIL' if identifier == fail else identifier + ' phrase.'))
            if identifier in cached_ids:
                cache.append(dict(id=identifier, sha256=digest, seconds=.01,
                                  phrases=[dict(start=0, end=.009, text='Cached ' + identifier + '.')]))
        (voice / 'KOKORO_SOURCE_MANIFEST.json').write_text(json.dumps(dict(
            model_revision='stub', clips=entries)), encoding='utf-8')
        output.write_text(json.dumps(dict(schema=1, clips=cache)), encoding='utf-8')
        accepted = output.read_bytes()
        capture = io.StringIO()
        failure = None
        with patch.dict(sys.modules, FAKES), patch.object(sys, 'argv', [
                'author_caption_timings.py', '--voice-root', str(voice), '--cache', str(root/'cache'),
                '--output', str(output)]), contextlib.redirect_stdout(capture):
            try: module.main()
            except ValueError as exception: failure = str(exception)
        return json.loads(output.read_text()), cache, accepted == output.read_bytes(), capture.getvalue(), failure


class CaptionTimingsTests(unittest.TestCase):
    def test_cache_regeneration_orders_removal_and_all_cached(self):
        for changed in ('first', 'middle', 'last', None):
            for ids in (['first', 'middle', 'last'], ['last', 'first', 'middle']):
                with self.subTest(changed=changed, ids=ids):
                    cached = [i for i in ids if i != changed] + ['removed']
                    saved, before, _, stdout, failure = run_fixture(author, ids, cached)
                    self.assertIsNone(failure)
                    self.assertEqual(ids, [c['id'] for c in saved['clips']])
                    self.assertIn('Authenticated 3 recordings', stdout)
                    before = {c['id']: c for c in before}
                    for clip in saved['clips']:
                        if clip['id'] != changed: self.assertEqual(before[clip['id']], clip)

    def test_failure_does_not_replace_accepted_catalog(self):
        saved, _, unchanged, stdout, failure = run_fixture(
            author, ['first', 'broken', 'last'], ['last'], fail='broken')
        self.assertIn('Injected synthesis failure', failure)
        self.assertTrue(unchanged)
        self.assertNotIn('Authenticated', stdout)
        self.assertEqual(['last'], [c['id'] for c in saved['clips']])

    def test_failed_atomic_replace_preserves_catalog_and_cleans_temp(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / 'caption.json'
            output.write_bytes(b'previous accepted catalog')
            with patch.object(author.os, 'replace', side_effect=OSError('storage failure')):
                with self.assertRaises(OSError): author.save_catalog(output, [])
            self.assertEqual(b'previous accepted catalog', output.read_bytes())
            self.assertEqual([output], list(output.parent.iterdir()))

    def test_punctuation_at_length_and_duration_boundaries(self):
        for text, end in [('Readable caption text nearing a duration boundary', 4.6),
                          ('A' * 76, 1.)]:
            with self.subTest(text=text):
                words = [dict(text=text, start=0., end=end, space=False),
                         dict(text='.', start=end, end=end+.175, space=False)]
                self.assertEqual([dict(text=text+'.', start=0., end=end+.175)],
                                 author.segment_words(words))

    def test_invalid_phrases_are_rejected(self):
        for phrase in [dict(text='.', start=0, end=.1), dict(text='word', start=0, end=float('nan')),
                       dict(text='word', start=.2, end=.1)]:
            with self.subTest(phrase=phrase):
                with self.assertRaises(ValueError):
                    author.validate_clip(dict(id='invalid', seconds=1, phrases=[phrase]))

    def test_all_original_trailing_cues_repaired_without_word_or_hash_changes(self):
        baseline = json.loads(subprocess.check_output([
            'git', 'show', '65d35377e16da50b3a7d8d4fb6200a1860272ba3:QuestFlight/Assets/Production/Training/CaptionTimings.json'],
            cwd=ROOT, text=True))
        affected = [clip for clip in baseline['clips'] if not author.readable(clip['phrases'][-1]['text'])]
        self.assertEqual(17, len(affected))
        current = json.loads((ROOT / 'QuestFlight/Assets/Production/Training/CaptionTimings.json').read_text())
        self.assertEqual(107, len(current['clips']))
        by_id = {clip['id']: clip for clip in current['clips']}
        for original in baseline['clips']:
            clip = by_id[original['id']]
            self.assertEqual(original['sha256'], clip['sha256'])
            self.assertEqual(original['seconds'], clip['seconds'])
            self.assertEqual(author.repair_phrases(original['phrases']), clip['phrases'])
            author.validate_clip(clip)
        for original in affected:
            clip = by_id[original['id']]
            for time in [original['phrases'][-1]['start']-.001,
                         original['phrases'][-1]['start'], original['seconds']]:
                # Replay ProductCaptionTimeline.PhraseAt's float32 start-time contract.
                f32 = lambda value: struct.unpack('<f', struct.pack('<f', value))[0]
                index = 0
                while index+1 < len(clip['phrases']) and f32(time) >= f32(clip['phrases'][index+1]['start']):
                    index += 1
                self.assertEqual(original['phrases'][-2]['text']+'.', clip['phrases'][index]['text'])
        manifest = json.loads((ROOT / 'QuestFlight/Assets/Production/Training/Voice/KOKORO_SOURCE_MANIFEST.json')
                              .read_text(encoding='utf-8-sig'))
        def normalized(text):
            return re.sub(r'\s+([.,?!;:])', r'\1', re.sub(r'\s+', ' ', text)).strip()
        for entry in manifest['clips']:
            self.assertEqual(normalized(entry['spoken_text']),
                             normalized(' '.join(p['text'] for p in by_id[entry['id']]['phrases'])))

    def test_audio_audit_rejects_punctuation_only_and_missing_catalog_entries(self):
        import soundfile as sf
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            voice = root / 'Training/Voice'
            voice.mkdir(parents=True)
            path = voice / 'fixture.wav'
            sf.write(path, np.full(2400, .1), 24000, subtype='PCM_16')
            sha = hashlib.sha256(path.read_bytes()).hexdigest()
            (voice / 'KOKORO_SOURCE_MANIFEST.json').write_text(json.dumps(dict(
                clips=[dict(id='fixture', sha256=sha)])))
            timings = root / 'Training/CaptionTimings.json'
            for text, missing in [('Readable.', False), ('.', False), ('Readable.', True)]:
                with self.subTest(text=text, missing=missing):
                    timings.write_text(json.dumps(dict(clips=[] if missing else [dict(
                        id='fixture', sha256=sha, phrases=[dict(start=0, end=.09, text=text)])])))
                    result = subprocess.run([sys.executable, str(ROOT/'tools/audio/audit_presentation_audio.py'),
                        '--production', str(root), '--output', str(root/'report.json')],
                        capture_output=True, text=True)
                    self.assertEqual(int(missing or text=='.'), result.returncode, result.stdout+result.stderr)
                    report = json.loads((root/'report.json').read_text())
                    self.assertEqual(not (missing or text=='.'), report['passed'])

    def test_negative_control_original_main_loses_trailing_cache_and_accepted_file(self):
        baseline = subprocess.check_output(['git', 'show',
            '65d35377e16da50b3a7d8d4fb6200a1860272ba3:tools/audio/author_caption_timings.py'], cwd=ROOT, text=True)
        module = types.ModuleType('baseline_caption_author')
        exec(compile(baseline, 'audited_caption_author.py', 'exec'), module.__dict__)
        saved, _, _, stdout, _ = run_fixture(module, ['first', 'last'], ['last'])
        self.assertEqual(['first'], [c['id'] for c in saved['clips']])
        self.assertIn('Authenticated 2 recordings', stdout)
        _, _, unchanged, _, failure = run_fixture(module, ['first', 'broken', 'last'], ['last'], fail='broken')
        self.assertIsNotNone(failure)
        self.assertFalse(unchanged)
        # Adapt the supplied punctuation diagnostic's isolated original AST block.
        main = next(node for node in ast.parse(baseline).body
                    if isinstance(node, ast.FunctionDef) and node.name=='main')
        loop = next(node for node in main.body if isinstance(node, ast.For)
                    and isinstance(node.target, ast.Name) and node.target.id=='entry')
        statements = [node for node in loop.body if 45 <= node.lineno <= 55]
        scope = dict(words=[dict(text='Readable caption text nearing a duration boundary',
                                start=0., end=4.6, space=False),
                            dict(text='.', start=4.6, end=4.775, space=False)])
        exec(compile(ast.Module(body=statements, type_ignores=[]), 'audited_segmenter.py', 'exec'), scope)
        self.assertEqual('.', scope['phrases'][-1]['text'])


if __name__ == '__main__': unittest.main()
