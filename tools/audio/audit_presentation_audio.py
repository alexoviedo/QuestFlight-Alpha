"""Check shipped audio and caption provenance; this does not replace listening."""
import argparse
import hashlib
import json
from pathlib import Path
import numpy as np
import soundfile as sf

p = argparse.ArgumentParser()
p.add_argument('--production', type=Path, required=True)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
voice = a.production / 'Training/Voice'
manifest = json.loads((voice / 'KOKORO_SOURCE_MANIFEST.json').read_text(encoding='utf-8-sig'))
timings = json.loads((a.production / 'Training/CaptionTimings.json').read_text(encoding='utf-8'))
by_id = {c['id']: c for c in timings['clips']}
records, errors = [], []
for entry in manifest['clips']:
    path = voice / (entry['id'] + '.wav')
    samples, rate = sf.read(path)
    sha = hashlib.sha256(path.read_bytes()).hexdigest()
    timing = by_id.get(entry['id'])
    peak = float(np.max(np.abs(samples)))
    if sha != entry['sha256'] or not timing or sha != timing['sha256']:
        errors.append(entry['id'] + ': stale provenance/timing')
    if not np.all(np.isfinite(samples)) or peak >= .999 or len(samples) == 0:
        errors.append(entry['id'] + ': invalid or clipped PCM')
    if timing:
        previous = -1
        for phrase in timing['phrases']:
            if not phrase['text'] or phrase['start'] < previous or phrase['end'] > len(samples)/rate + .05:
                errors.append(entry['id'] + ': invalid phrase boundaries')
            previous = phrase['start']
    records.append({'id': entry['id'], 'seconds': len(samples)/rate, 'peakDbfs': float(20*np.log10(max(peak, 1e-9))), 'rmsDbfs': float(20*np.log10(max(np.sqrt(np.mean(samples*samples)), 1e-9)))})
loops = []
for path in (a.production / 'Audio/C172/Clips').glob('*Loop.wav'):
    samples, rate = sf.read(path)
    loops.append({'id': path.stem, 'peak': float(np.max(np.abs(samples))), 'boundaryStep': float(np.max(np.abs(samples[-1]-samples[0]))), 'seconds': len(samples)/rate})
result = {'passed': not errors, 'errors': errors, 'voices': records, 'loops': loops, 'listeningAcceptance': 'PENDING; numerical checks do not establish audible quality or headset mix'}
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
print(json.dumps({'passed': not errors, 'voices': len(records), 'loops': loops, 'errors': errors}, indent=2))
raise SystemExit(bool(errors))
