from pathlib import Path
import hashlib,json,re,shutil,tempfile,unittest
from PIL import Image
from tools.provenance.validate_current_package_records import PACKAGE,validate

class CurrentPackageProvenanceTests(unittest.TestCase):
 def test_current_real_payload_and_wrapper_identity(self):
  self.assertTrue(validate()['passed'],validate()['errors'])
 def test_stale_derivative_and_self_updated_record_still_fail(self):
  with tempfile.TemporaryDirectory() as d:
   p=Path(d);shutil.copytree(PACKAGE,p,dirs_exist_ok=True)
   revision=p/'RuntimeData/JSBSim-1.3.1/SOURCE_REVISION.txt'
   revision.write_text(revision.read_text().replace('qfl-c172p-private-fidelity-v2','qfl-c172p-training-2054-v1'))
   # Rehash the changed metadata chain: semantic identity must still reject it.
   data=p/'RuntimeData/JSBSim-1.3.1';hp=data/'runtime_hashes.sha256'
   lines=[]
   for line in hp.read_text().splitlines():
    digest,name=line.split(None,1)
    if name=='SOURCE_REVISION.txt':digest=hashlib.sha256(revision.read_bytes()).hexdigest()
    lines.append(digest+'  '+name)
   hp.write_text('\n'.join(lines)+'\n')
   mp=p/'SOURCE_MANIFEST.json';manifest=json.loads(mp.read_text())
   for record in manifest['runtimeData']:
    if record['path'] in ('SOURCE_REVISION.txt','runtime_hashes.sha256'):
     record['sha256']=hashlib.sha256((data/record['path']).read_bytes()).hexdigest()
   mp.write_text(json.dumps(manifest))
   report=validate(p);self.assertFalse(report['passed']);self.assertIn('SOURCE_REVISION mismatch: aircraft_model_derivative',report['errors'])
 def test_current_text_does_not_claim_old_product_bindings_or_fallback(self):
  root=PACKAGE.parents[2]
  notices=(root/'THIRD_PARTY_NOTICES.md').read_text(encoding='utf-8')
  self.assertNotIn('Release builds remain on the Unity prototype backend',notices)
  self.assertNotIn('frozen optional development-gated',notices)
  docs=(root/'docs/KBDU_ORTHOPHOTO_PROVENANCE.md').read_text(encoding='utf-8')
  self.assertIn('Historical NAIP 2019 baseline',docs)
  material=(root/'QuestFlight/Assets/Production/Environment/PhotorealTerrainV1/KBDU_TerrainPyramid.mat').read_text(encoding='utf-8')
  for name,size,slot in (('KBDU_City2020_Near4km_8192.jpg',8192,'_NearAlbedo'),('KBDU_City2020_MidBase12km_8192.jpg',8192,'_MidAlbedo'),('KBDU_City2020_Far24km_4096.jpg',4096,'_FarAlbedo')):
   self.assertIn(name,docs);image=root/'QuestFlight/Assets/Production/Environment/AerialSurfaceV1/Ground'/name
   with Image.open(image) as texture:self.assertEqual((size,size),texture.size)
   guid=re.search(r'guid: ([0-9a-f]{32})',image.with_suffix('.jpg.meta').read_text())[1]
   self.assertRegex(material,re.escape(slot)+r':\s+m_Texture: \{fileID: 2800000, guid: '+guid)


if __name__=='__main__':unittest.main()
