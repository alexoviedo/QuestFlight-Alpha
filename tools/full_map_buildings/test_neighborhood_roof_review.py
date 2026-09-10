import unittest
from shapely.geometry import Polygon, Point
from shapely.ops import unary_union
from build_neighborhood_roof_review import roof_faces


class NeighborhoodRoofTests(unittest.TestCase):
    def test_concave_roofs_cover_source_and_keep_upward_winding(self):
        source=[(0,0),(12,0),(12,4),(7,4),(7,9),(0,9)]
        for kind in (0,1,2):
            roof=roof_faces(source,kind,2.5,6.)
            vertices=roof['vertices']
            triangles=[]
            for i in range(0,len(roof['indices']),3):
                a,b,c=[vertices[roof['indices'][i+j]] for j in range(3)]
                cross=(b['z']-a['z'])*(c['x']-a['x'])-(b['x']-a['x'])*(c['z']-a['z'])
                self.assertGreater(cross,1e-8)
                triangles.append(Polygon([(p['x'],p['z']) for p in (a,b,c)]))
            self.assertLess(unary_union(triangles).symmetric_difference(Polygon(source)).area,.0001)
            self.assertTrue(all(6.-1e-5<=p['y']<=8.5+1e-5 for p in vertices))
            for p in roof['boundary']:
                self.assertLess(Polygon(source).boundary.distance(Point(p['x'],p['z'])),.00001)

    def test_gable_ridge_is_present_and_wall_boundary_is_split(self):
        roof=roof_faces([(0,0),(16,0),(16,6),(0,6)],1,2,5)
        self.assertEqual(len(roof['boundary']),6)
        self.assertAlmostEqual(max(v['y'] for v in roof['vertices']),7)
        self.assertEqual(sum(abs(v['y']-7)<1e-5 for v in roof['boundary']),2)

    def test_rejects_invalid_source(self):
        with self.assertRaises(ValueError): roof_faces([(0,0),(4,4),(0,4),(4,0)],1,2,3)


if __name__=='__main__': unittest.main()
