import copy
import unittest
from build_grade_separated_road_authority import reconcile_approach_overlaps


def profile(name, points, bridge):
    return {"span_id": name, "upper_route_ids": ["shared-road"],
            "samples_xyz_meters": [v for p in points for v in p],
            "sample_normals_xyz": [0, 1, 0] * len(points),
            "bridge_interval_meters": bridge, "maximum_grade": 0.0}


class ApproachReconciliationTests(unittest.TestCase):
    def test_join_preserves_endpoints_grade_and_is_idempotent(self):
        source = profile("ending", [(100, 1, 0), (300, 2, 0)], [150, 180])
        target = profile("continuing", [(0, 0, 0), (600, 0, 0)], [450, 500])
        original_source = copy.deepcopy(source)
        profiles = [source, target]
        self.assertEqual(len(reconcile_approach_overlaps(profiles)), 2)
        points = list(zip(*[iter(target["samples_xyz_meters"])] * 3))
        self.assertIn((300, 2, 0), points)
        self.assertEqual(points[0], (0, 0, 0))
        self.assertEqual(points[-1], (600, 0, 0))
        self.assertLessEqual(target["maximum_grade"], 0.06)
        self.assertEqual(source, original_source)
        before = copy.deepcopy(profiles)
        reconcile_approach_overlaps(profiles)
        self.assertEqual(profiles, before)

    def test_insufficient_grade_distance_cannot_move_terrain_endpoint(self):
        source = profile("ending", [(100, 1, 0), (300, 2, 0)], [150, 180])
        target = profile("continuing", [(0, 0, 0), (305, 0, 0)], [250, 260])
        with self.assertRaisesRegex(RuntimeError, "endpoint"):
            reconcile_approach_overlaps([source, target])


if __name__ == "__main__":
    unittest.main()
