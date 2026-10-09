from __future__ import annotations
import copy
import json
import sys
import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/aerial_surface'))
sys.path.insert(0,str(ROOT/'tools/sky_atmosphere'))
import validate_aerial_surface as aerial
import validate_sky_assets as sky

class CurrentAssetContractTests(unittest.TestCase):
 def test_near_outer_budgets_are_independent(self):
  manifest=json.loads((ROOT/'QuestFlight/Assets/Production/Environment/PhotorealAerialSurfaceV1/Buildings/ProductionBuildingSurfaceLayer_Manifest.json').read_text())
  current=manifest['topology']['footprint_simplification']; measured=current['sampled_hausdorff_maximum_meters_by_band']
  aerial.validate_building_simplification(current,measured)
  self.assertGreater(measured['2-4km'],10) # Original global threshold rejects the valid current outer geometry.
  for measured_bad in ({'0-2km':10.1,'2-4km':29}, {'0-2km':9,'2-4km':30.1}, {'0-2km':float('nan'),'2-4km':29}, {'2-4km':29}):
   with self.subTest(measured=measured_bad),self.assertRaises(aerial.ValidationFailure):aerial.validate_building_simplification(current,measured_bad)
  for limits in ({'0-2km':30.,'2-4km':30.},{'0-2km':10.,'2-4km':31.}):
   bad=copy.deepcopy(current);bad['maximum_allowed_hausdorff_meters']=limits
   with self.subTest(limits=limits),self.assertRaises(aerial.ValidationFailure):aerial.validate_building_simplification(bad,measured)
  bad=copy.deepcopy(current);bad['sampled_hausdorff_maximum_meters_by_band']['0-2km']=0
  with self.assertRaises(aerial.ValidationFailure):aerial.validate_building_simplification(bad,measured)
 def test_far_transition_contract_rejects_old_topology_and_wrong_guid(self):
  p=sky.ENVIRONMENT_PREFAB_PATH.read_text();m=sky.FAR_MESH_PATH.read_text();g=sky.FAR_MESH_PATH.with_suffix('.asset.meta').read_text();a=sky.ENVIRONMENT_AUTHORING_PATH.read_text()
  self.assertEqual([],sky.validate_far_topology(p,m,g,a))
  for altered in ((p.replace('farTerrainSourceVertexCount: 47112','farTerrainSourceVertexCount: 63841'),m,g,a),
                  (p,m.replace('indexCount: 261552','indexCount: 267840'),g,a),
                  (p,m,g.replace(sky.FAR_MESH_GUID,'0'*32),a),
                  (p,m,g,a.replace('TerrainDefectRepairV1Authoring.BuildTransitionMesh(','OldTopology(')),
                  (p.replace('farTerrainSmoothingPassCount: 2','farTerrainSmoothingPassCount: 0'),m,g,a)):
   with self.subTest(altered=altered[0][:60]):self.assertTrue(sky.validate_far_topology(*altered))

if __name__=='__main__':unittest.main()
