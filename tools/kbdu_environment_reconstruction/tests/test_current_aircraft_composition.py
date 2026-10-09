from __future__ import annotations
import ast,copy,json,subprocess,sys,tempfile,unittest
from pathlib import Path
import jsonschema
ROOT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT));sys.path.insert(0,str(ROOT/'tools/kbdu_environment_reconstruction'))
from tools.kbdu_airport_aircraft.apply_aircraft_placement_contract import build_placement_rows,validate_orthophoto,TerrainSampler,PLACEMENTS,TERRAIN,refresh_reconstruction_composition,placement_manifest_records,sha256
from generate_content_contracts import context_recipe_v2
TOOLS=ROOT/'tools/kbdu_environment_reconstruction'
class CurrentAircraftCompositionTests(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  cls.contract=json.loads(PLACEMENTS.read_text());cls.ortho=validate_orthophoto(cls.contract)
  cls.sampler=TerrainSampler(json.loads(TERRAIN.read_text()))
  cls.current=json.loads((TOOLS/'KBDU_ContextBatchRecipeV2.json').read_text())
  cls.base=json.loads((ROOT/'QuestFlight/Assets/Production/Environment/PhotorealTerrainV1/KBDU_ContextBatchRecipe.json').read_text())
  cls.parking=json.loads((TOOLS/'airport_parking_stand_manifest.json').read_text())
 def test_reviewed_rows_and_final_composition_are_deterministic(self):
  first=build_placement_rows(self.contract,self.ortho,self.sampler)
  self.assertEqual(first,self.current['parked_aircraft_batches'][0]['placements'])
  result=context_recipe_v2(self.base,self.parking,self.current['building_batches'],self.current['vegetation_batches'],first)
  schema=json.loads((TOOLS/'schemas/kbdu_context_batch_recipe_v2.schema.json').read_text());jsonschema.validate(result,schema)
  self.assertEqual(result,self.current)
  payload=json.dumps(result,sort_keys=True,separators=(',',':'))+'\n'
  for _ in range(2):
   with tempfile.TemporaryDirectory() as d:
    path=Path(d)/'KBDU_ContextBatchRecipeV2.json';path.write_text(payload,encoding='utf-8',newline='\n')
    self.assertEqual(path.read_bytes(),(TOOLS/path.name).read_bytes())
 def test_final_manifest_updates_only_owned_aircraft_and_is_idempotent(self):
  rows=build_placement_rows(self.contract,self.ortho,self.sampler)
  with tempfile.TemporaryDirectory() as d:
   out=Path(d)
   baseline={'cell_size_meters':250,'source_license_classes':['CC_BY_4_0_KOG_THORNS_ADAPTATION'],
      'placements':[{'stable_id':'retained-building','category':'building','unchanged':'exact'}, {'stable_id':'old-aircraft','category':'parked_aircraft'}]}
   (out/'production_placement_manifest.json').write_text(json.dumps(baseline))
   (out/'KBDU_ContextBatchRecipeV2.json').write_text(json.dumps(self.current))
   report={'generated_outputs':[{'path':name,'bytes':0,'sha256':'0'*64} for name in ('production_placement_manifest.json','KBDU_ContextBatchRecipeV2.json')], 'input_hashes':{'osm_building_source_6km':'historical'}}
   (out/'content_pipeline_report.json').write_text(json.dumps(report))
   refresh_reconstruction_composition(rows,out)
   first={p.name:p.read_bytes() for p in out.iterdir()}
   refresh_reconstruction_composition(rows,out)
   self.assertEqual(first,{p.name:p.read_bytes() for p in out.iterdir()})
   final=json.loads(first['production_placement_manifest.json']);self.assertEqual(next(row for row in final['placements'] if row['category']=='building'),baseline['placements'][0])
   self.assertEqual(35,sum(row['category']=='parked_aircraft' for row in final['placements']))
   report=json.loads(first['content_pipeline_report.json']);self.assertNotIn('input_hashes',report)
   self.assertIn('Pending pinned external',report['complete_source_pipeline_validation'])
   for record in report['generated_outputs']:
    self.assertEqual((out/record['path']).stat().st_size,record['bytes']);self.assertEqual(sha256(out/record['path']),record['sha256'])
 def test_original_overwrite_and_old_schema_fail_current_contract(self):
  old=subprocess.check_output(['git','show','65d35377e16da50b3a7d8d4fb6200a1860272ba3:tools/kbdu_environment_reconstruction/generate_content_contracts.py'],text=True)
  node=next(n for n in ast.parse(old).body if isinstance(n,ast.FunctionDef) and n.name=='context_recipe_v2')
  env={'copy':copy,'Any':object};exec(compile(ast.Module(body=[node],type_ignores=[]),'<audited transformation>','exec'),env)
  broken=env['context_recipe_v2'](self.current,self.parking,[],[])
  self.assertEqual(3,len(broken['parked_aircraft_batches'][0]['placements']))
  schema=json.loads(subprocess.check_output(['git','show','65d35377e16da50b3a7d8d4fb6200a1860272ba3:tools/kbdu_environment_reconstruction/schemas/kbdu_context_batch_recipe_v2.schema.json']))
  with self.assertRaises(jsonschema.ValidationError):jsonschema.validate(self.current,schema)
 def test_changed_or_incomplete_reviewed_aircraft_is_rejected(self):
  rows=build_placement_rows(self.contract,self.ortho,self.sampler)
  for bad in (rows[:3],rows[:-1],rows[:-1]+[rows[0]]):
   with self.subTest(count=len(bad)),self.assertRaises(ValueError):context_recipe_v2(self.base,self.parking,[],[],bad)
  bad=copy.deepcopy(self.contract);bad['groups'][0]['placements'][1]['id']=bad['groups'][0]['placements'][0]['id']
  with self.assertRaises(ValueError):build_placement_rows(bad,self.ortho,self.sampler)
  bad=copy.deepcopy(self.contract)
  sail=next(g for g in bad['groups'] if g['family']=='sailplane');sail['placements'][0]['wing_down_side']='none'
  with self.assertRaises(ValueError):build_placement_rows(bad,self.ortho,self.sampler)

if __name__=='__main__':unittest.main()
