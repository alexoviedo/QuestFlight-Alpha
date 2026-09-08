import unittest
import numpy as np
from remaster_self_ao import accessibility


class SelfAoTests(unittest.TestCase):
    def test_open_plane_does_not_shadow_itself(self):
        plane = np.array([[[-2., 0, -2], [2, 0, -2], [0, 0, 2]]])
        result = accessibility(np.array([[0., 0, 0]]), np.array([[0., 1, 0]]), plane)
        self.assertEqual(result[0], 1.)

    def test_near_cover_occludes_but_distant_cover_does_not(self):
        cover = np.array([[[-10., .1, -10], [10, .1, -10], [10, .1, 10]],
                          [[-10., .1, -10], [10, .1, 10], [-10, .1, 10]]])
        pos, normal = np.array([[0., 0, 0]]), np.array([[0., 1, 0]])
        self.assertLess(accessibility(pos, normal, cover)[0], .15)
        cover[:, :, 1] = 1.
        self.assertEqual(accessibility(pos, normal, cover)[0], 1.)

    def test_invalid_normal_fails_closed(self):
        with self.assertRaises(ValueError):
            accessibility(np.zeros((1, 3)), np.zeros((1, 3)), np.zeros((1, 3, 3)))


if __name__ == '__main__':
    unittest.main()
