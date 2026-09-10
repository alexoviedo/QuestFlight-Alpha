import unittest
from pathlib import Path
import numpy as np
from build_neighborhood_crowns import build_crowns


class NeighborhoodCrownTests(unittest.TestCase):
    def test_foliage_is_perforated_cards_with_stable_source_and_valid_geometry(self):
        record=dict(id=123,x=-800.,y=-4.,z=-700.,height=12.,crown=8.,color=[100,120,75],kind=0)
        atlas=Path(__file__).resolve().parents[2]/'QuestFlight/Assets/Production/Environment/NeighborhoodFoliageV1/NeighborhoodFoliageAtlas.png'
        mesh=build_crowns([record],atlas)
        self.assertEqual(mesh,build_crowns([record],atlas))
        self.assertEqual(mesh['source_records'],[record])
        self.assertEqual({m['x'] for m in mesh['modes']},{1,2},'No solid canopy mode is permitted')
        points=np.array([[p[k] for k in 'xyz'] for p in mesh['vertices']])
        normals=np.array([[p[k] for k in 'xyz'] for p in mesh['normals']])
        self.assertTrue(np.all(np.isfinite(points)))
        self.assertTrue(np.all(np.abs(np.linalg.norm(normals,axis=1)-1)<.0001))
        self.assertGreaterEqual(points[:,1].min(),record['y']-.001)
        self.assertLessEqual(points[:,1].max(),record['y']+record['height']+.001)
        triangles=points[np.array(mesh['indices']).reshape(-1,3)]
        self.assertTrue(np.all(np.linalg.norm(np.cross(triangles[:,1]-triangles[:,0],triangles[:,2]-triangles[:,0]),axis=1)>1e-6))
        self.assertTrue(all(0<=v<=1 for uv in mesh['uv'] for v in uv.values()))


if __name__=='__main__': unittest.main()
