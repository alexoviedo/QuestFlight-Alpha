"""Geometry/exclusion tests for the external prototype compiler."""
import tempfile
import unittest
from pathlib import Path

import cv2
import numpy as np

from build_hybrid_canopy_prototype import mesh_patch, placements
from compile_vegetation_hlod_v3 import HEADER, PAGE, INSTANCE


class HybridCanopyTests(unittest.TestCase):
    def setUp(self):
        self.mask = np.zeros((48,48), np.uint8)
        cv2.circle(self.mask,(24,24),19,255,-1)
        cv2.circle(self.mask,(24,24),6,0,-1)
        self.records = [dict(id=i,x=x,z=48-y,y=-2.,height=12.,crown=17.,color=[80,110,60],kind=0)
                        for i,(x,y) in enumerate([(13,15),(34,15),(13,34),(34,34)])]

    def test_internal_opening_not_bridged_by_top_triangles(self):
        mesh = mesh_patch(self.mask,self.records,0,48,1)
        vertices = np.array([[v['x'],v['y'],v['z']] for v in mesh['vertices']])
        for indices in np.array(mesh['indices']).reshape(-1,3):
            tri = vertices[indices]
            normal = np.cross(tri[1]-tri[0],tri[2]-tri[0])
            if normal[1] <= 0 or tri[:,1].min() < 8:
                continue
            coverage = np.zeros_like(self.mask)
            pixels = np.rint(np.column_stack((tri[:,0],48-tri[:,2]))).astype(np.int32)
            cv2.fillConvexPoly(coverage,pixels,1)
            self.assertTrue(np.all(self.mask[coverage!=0]!=0))

    def test_deterministic_finite_mesh_with_valid_indices(self):
        first = mesh_patch(self.mask,self.records,0,48,1)
        self.assertEqual(first,mesh_patch(self.mask,self.records,0,48,1))
        self.assertTrue(all(np.isfinite(list(v.values())).all() for v in first['vertices']))
        self.assertEqual(set(first['indices']),set(range(len(first['vertices']))))
        self.assertEqual(len(first['colors']),len(first['vertices']))

    def test_no_continuous_ground_skirt(self):
        mesh = mesh_patch(self.mask,self.records,0,48,1)
        foliage = [v for v,c in zip(mesh['vertices'],mesh['colors']) if c['r']==0]
        self.assertGreaterEqual(min(v['y'] for v in foliage),4)
        self.assertGreater(mesh['trunks'],0)
        self.assertTrue(any(v['y']==-2 for v,c in zip(mesh['vertices'],mesh['colors']) if c['r']==1))

    def test_empty_footprint_fails_closed(self):
        with self.assertRaises(ValueError):
            mesh_patch(np.zeros_like(self.mask),self.records,0,48,1)

    def test_database_identity_coordinates_and_truncation(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/'test.bytes'
            data = HEADER.pack(b'QFFMVG3\0',3,-100.,-200.,100.,.1,1,1,1)+PAGE.pack(0,1)
            data += INSTANCE.pack(10,20,-250,1200,800,0,80,100,60,1,0)
            path.write_bytes(data)
            records = placements(path,[-100,-200,0,0])
            self.assertEqual(records[0]['id'],0)
            self.assertAlmostEqual(records[0]['x'],-99)
            self.assertAlmostEqual(records[0]['z'],-198)
            self.assertEqual(records[0]['y'],-2.5)
            path.write_bytes(data[:-1])
            with self.assertRaises(ValueError):
                placements(path,[-100,-200,0,0])


if __name__=='__main__':
    unittest.main()
