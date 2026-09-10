"""Topology and source-integrity regressions that precede foliage authoring."""
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import numpy as np
from affine import Affine
from pyproj import Transformer
from rasterio.features import rasterize
from shapely.geometry import Polygon, box
from shapely.ops import unary_union

from build_connected_grove_evidence import FOOT, LOCAL_CRS, polygon_id
from fetch_city_canopy import fetch


class ConnectedGroveEvidenceTests(unittest.TestCase):
    def test_height_uses_survey_feet_not_absolute_elevation(self):
        self.assertAlmostEqual(10 * FOOT,3.048006096012192,places=12)
        values=np.array([10.,50.,np.nan])*FOOT
        self.assertTrue(np.isnan(values[2]))
        self.assertAlmostEqual(values[1],15.24003048006096)

    def test_local_frame_has_east_x_north_z(self):
        project=Transformer.from_crs(4326,LOCAL_CRS,always_xy=True)
        x,z=project.transform(-105.22608958,40.03936527)
        self.assertAlmostEqual(x,0,places=5);self.assertAlmostEqual(z,0,places=5)
        self.assertGreater(project.transform(-105.225,40.03936527)[0],0)
        self.assertGreater(project.transform(-105.22608958,40.040)[1],0)

    def test_native_union_stitches_seam_preserves_courtyard(self):
        canopy=Polygon([(0,0),(20,0),(20,12),(0,12)],
                       holes=[[(8,3),(12,3),(12,9),(8,9)]])
        sections=[canopy.intersection(box(0,0,10,12)),canopy.intersection(box(10,0,20,12))]
        joined=unary_union(sections)
        self.assertTrue(joined.equals(canopy))
        self.assertEqual(len(joined.interiors),1)
        self.assertEqual(polygon_id(joined),polygon_id(unary_union(list(reversed(sections)))))
        # IDs are assigned after union; payload chunks inherit their parent ID.
        ids=[polygon_id(joined) for _ in sections]
        self.assertEqual(len(set(ids)),1)

    def test_small_real_gap_is_never_bridged(self):
        joined=unary_union([box(0,0,10,10),box(10.05,0,20,10)])
        self.assertEqual(joined.geom_type,'MultiPolygon')
        self.assertEqual(len(joined.geoms),2)

    def test_raster_support_retains_interior_hole(self):
        canopy=Polygon([(0,0),(10,0),(10,10),(0,10)],
                       holes=[[(3,3),(7,3),(7,7),(3,7)]])
        result=rasterize([(canopy,1)],out_shape=(10,10),transform=Affine(1,0,0,0,-1,10))
        self.assertEqual(int(result.sum()),84)

    def test_cache_tampering_fails_before_network_or_manifest_overwrite(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);source=root/'N3W297_chm.tif';source.write_bytes(b'changed')
            manifest=root/'N3W297_sources.json'
            manifest.write_text(json.dumps({'products':[{'file':source.name,'sha256':'0'*64}]}))
            original=manifest.read_bytes()
            with patch('fetch_city_canopy.requests.get') as request:
                with self.assertRaisesRegex(ValueError,'Cached source hash changed'):
                    fetch(root,'N3W297')
                request.assert_not_called()
            self.assertEqual(manifest.read_bytes(),original)


if __name__=='__main__':unittest.main()
