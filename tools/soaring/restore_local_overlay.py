"""Restore the documented local-only product inputs, preserving original bytes.
Personal agent settings are intentionally excluded from the shipping workspace.
"""
import json
from pathlib import Path
import shutil
from reconcile import ROOT, ARTIFACTS, digest, write

preserved=Path('C:/QFBackup20261008/CLOSEOUT_20261012T185048Z')
records=json.loads((preserved/'overlay_manifest.json').read_text(encoding='utf-8-sig'))['entries']
receipt=[]
for item in records:
    relative=Path(item['path'])
    if 'DevAgentSettings' in str(relative):continue
    source=preserved/'local_product_overlay'/relative
    assert digest(source)==item['sha256'],str(source)
    destination=ROOT/relative
    backup=ARTIFACTS/'before_local_overlay'/relative
    if destination.exists() and not backup.exists():
        backup.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(destination,backup)
    destination.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,destination)
    receipt.append(dict(path=str(relative),sha256=digest(destination),source=str(source),commit_allowed=False))
write(ARTIFACTS/'local_overlay_receipt.json',receipt)
print(f'Restored {len(receipt)} hash-verified product files; archived settings untouched.')
