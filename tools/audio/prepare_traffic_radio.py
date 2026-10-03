"""Offline radio voicing from generated Kokoro sources; no runtime filter cost."""
from pathlib import Path
import argparse
import hashlib
import json
import numpy as np
import soundfile as sf
import math

def biquad(audio, rate, frequency, highpass=False):
    w = 2 * math.pi * frequency / rate
    c, alpha = math.cos(w), math.sin(w) / math.sqrt(2)
    b0 = (1 + c) / 2 if highpass else (1 - c) / 2
    b1 = -(1 + c) if highpass else 1 - c
    b2 = b0
    a0, a1, a2 = 1 + alpha, -2 * c, 1 - alpha
    b0, b1, b2, a1, a2 = (v / a0 for v in (b0, b1, b2, a1, a2))
    result = np.empty(len(audio), dtype=np.float32)
    x1 = x2 = y1 = y2 = 0.0
    for i, sample in enumerate(audio):
        x = float(sample)
        y = b0*x + b1*x1 + b2*x2 - a1*y1 - a2*y2
        result[i] = y
        x2, x1, y2, y1 = x1, x, y1, y
    return result

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source', type=Path, required=True)
parser.add_argument('--destination', type=Path, required=True)
args = parser.parse_args()
manifest = json.loads((args.source / 'generation.json').read_text(encoding='utf-8'))
args.destination.mkdir(parents=True, exist_ok=True)
for entry in manifest['clips']:
    audio, rate = sf.read(args.source / (entry['id'] + '.wav'), dtype='float32')
    filtered = biquad(biquad(audio, rate, 350, True), rate, 3300)
    peak = float(np.max(np.abs(filtered)))
    if peak > 0:
        filtered *= min(1.6, .80 / peak)
    fade = min(int(.025 * rate), len(filtered) // 2)
    filtered[:fade] *= np.linspace(0, 1, fade)
    filtered[-fade:] *= np.linspace(1, 0, fade)
    target = args.destination / (entry['id'] + '.wav')
    sf.write(target, filtered, rate, subtype='PCM_16')
    entry['source_sha256'] = entry['sha256']
    entry['sha256'] = hashlib.sha256(target.read_bytes()).hexdigest()
    entry['processing'] = '350 Hz highpass / 3300 Hz lowpass biquads; bounded gain; 25 ms edge fades; mono PCM16.'
manifest['purpose'] = 'Fictional actor callsigns; actual actor state/runway. Boulder COM1 CTAF 122.725 MHz; City of Boulder airport master-plan inventory.'
manifest['generator_processing'] = 'tools/audio/prepare_traffic_radio.py'
(args.destination / 'TRAFFIC_KOKORO_SOURCE_MANIFEST.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(f'Prepared {len(manifest["clips"])} offline radio calls.')
