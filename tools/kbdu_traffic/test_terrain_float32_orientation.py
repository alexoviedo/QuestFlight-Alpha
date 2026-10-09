import importlib.util
import hashlib
import json
from pathlib import Path
import unittest

import numpy as np

MODULE_PATH = Path(__file__).with_name('repair_airport_road_terrain.py')
SPEC = importlib.util.spec_from_file_location('road_terrain_producer', MODULE_PATH)
PRODUCER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PRODUCER)
HISTORICAL_PAYLOAD = MODULE_PATH.parents[2].parent / 'T2-QuestFlight-product-artifacts/road_repair_20260917/terrain_payload.json'


class Float32HeightfieldTests(unittest.TestCase):
    def test_serialized_inversion_is_corrected_and_position_bytes_retained(self):
        # Actual audited road face 36,471: positive in producer doubles,
        # negative after Unity's final float32 coordinate storage.
        # Three project-authored USGS-public-domain DTM/OSM ODbL derivative
        # points from terrain_payload SHA256 d876320e7346a433f2e3648142ef42a4c
        # 94d6ec9ead7352402a283e24d7bdd2e; no raw terrain archive is required.
        source = np.asarray([[-1601.00023, 1.4463993123636845, -465.9519],
                             [-1600.97006, 1.579113834979565, -468.66666],
                             [-1601.00017, 1.4463253464514312, -465.95935]])
        self.assertGreater(np.cross(source[1] - source[0], source[2] - source[0])[1], 0)
        stored = source.astype('<f4')
        self.assertLess(np.cross(stored[1] - stored[0], stored[2] - stored[0])[1], 0)
        result, indices, report = PRODUCER.finalize_float32_heightfield(source, [[0, 1, 2]])
        self.assertEqual(result.astype('<f4').tobytes(), stored.tobytes())
        self.assertEqual(indices.tolist(), [[0, 2, 1]])
        self.assertEqual(report['inverted_faces_corrected'], [0])

    @unittest.skipUnless(HISTORICAL_PAYLOAD.exists(), 'Historical external road authoring payload unavailable; portable counterexamples remain active')
    def test_current_source_payload_only_four_swaps_and_five_recorded_exclusions(self):
        self.assertEqual(hashlib.sha256(HISTORICAL_PAYLOAD.read_bytes()).hexdigest(), 'd876320e7346a433f2e3648142ef42a4c94d6ec9ead7352402a283e24d7bdd2e')
        payload = json.loads(HISTORICAL_PAYLOAD.read_text())
        vertices = np.asarray(payload['vertices']).reshape(-1, 3)
        original = np.asarray(payload['triangles']).reshape(-1, 3)
        with self.assertRaisesRegex(ValueError, 'Zero XZ footprint.*35598'):
            PRODUCER.finalize_float32_heightfield(vertices, original)
        stored, corrected, report = PRODUCER.finalize_float32_heightfield(vertices, original, exclude_collapsed=True)
        self.assertEqual(report['inverted_faces_corrected'], [19097, 36471, 49396, 49400])
        self.assertEqual([record['face'] for record in report['zero_xz_footprint_exclusions']], [35598, 36470, 38881, 38882, 38942])
        self.assertEqual(len(corrected), 104073)
        self.assertEqual(stored.astype('<f4').tobytes(), vertices.astype('<f4').tobytes())
        keep = np.ones(len(original), dtype=bool)
        keep[[record['face'] for record in report['zero_xz_footprint_exclusions']]] = False
        np.testing.assert_array_equal(np.sort(corrected, axis=1), np.sort(original[keep], axis=1))
        _, again, second_report = PRODUCER.finalize_float32_heightfield(stored, corrected)
        np.testing.assert_array_equal(again, corrected)
        self.assertEqual(second_report['inverted_faces_corrected'], [])
        self.assertEqual(second_report['zero_xz_footprint_exclusions'], [])

    def test_projected_collapse_rejected_without_explicit_upstream_policy(self):
        vertices = [[1600, 0, 0], [1600.00003, .2, 2], [1600.00002, .1, 1]]
        self.assertGreater(np.cross(np.subtract(vertices[1], vertices[0]), np.subtract(vertices[2], vertices[0]))[1], 0)
        with self.assertRaisesRegex(ValueError, 'Zero XZ footprint'):
            PRODUCER.finalize_float32_heightfield(vertices, [[0, 1, 2]])

    def test_excluding_every_face_still_fails(self):
        with self.assertRaisesRegex(ValueError, 'no nonzero XZ footprint'):
            PRODUCER.finalize_float32_heightfield([[0, 0, 0], [0, 1, 1], [0, 2, 2]], [[0, 1, 2]], exclude_collapsed=True)

    def test_nonfinite_and_float32_overflow_rejected(self):
        for invalid in [float('nan'), float('inf'), 1e40]:
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                PRODUCER.finalize_float32_heightfield([[invalid, 0, 0], [0, 0, 1], [1, 0, 0]], [[0, 1, 2]])

    def test_invalid_indices_and_shapes_rejected(self):
        for triangles in [[[0, 1, 3]], [[-1, 1, 2]], [[0, 1]], [[0., 1., 2.]], []]:
            with self.subTest(triangles=triangles), self.assertRaises(ValueError):
                PRODUCER.finalize_float32_heightfield([[0, 0, 0], [0, 0, 1], [1, 0, 0]], triangles)

    def test_healthy_stored_payload_is_idempotent(self):
        vertices = np.asarray([[0, 0, 0], [0, 1, 1], [1, 2, 0]])
        stored, indices, report = PRODUCER.finalize_float32_heightfield(vertices, [[0, 1, 2]])
        self.assertEqual(indices.tolist(), [[0, 1, 2]])
        self.assertEqual(stored.tolist(), vertices.tolist())
        self.assertEqual(report['inverted_faces_corrected'], [])


if __name__ == '__main__':
    unittest.main()
