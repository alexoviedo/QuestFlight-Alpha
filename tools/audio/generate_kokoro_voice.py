#!/usr/bin/env python3
"""Offline instructor authoring. Stage first; promotion requires reviewed output hashes."""
from __future__ import annotations
import argparse
import hashlib
import html
import importlib.metadata
import json
import re
import shutil
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
MODEL = 'hexgrad/Kokoro-82M'
REVISION = 'f3ff3571791e39611d31c381e3a41a3af07b4987'
RATE = 24000

def digest(data):
    return hashlib.sha256(data).hexdigest()

def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')

def lesson_copy(project):
    root = project / 'Assets/Production/Training'
    clips = {c['id']: c['text'] for c in json.loads((root/'VoiceScripts.json').read_text(encoding='utf-8-sig'))['clips']}
    teaching = json.loads((root/'LessonTeachingContent.json').read_text(encoding='utf-8-sig'))
    for lesson in teaching['lessons']:
        clips[lesson['id']+'_briefing'] = lesson['briefing']
        for i, stage in enumerate(lesson['stages']):
            clips[f"{lesson['id']}_{i:02}"] = stage['instruction']
    for ref in teaching['references']:
        clips['reference_'+ref['id'].lower()] = ref['text']
    for cue in teaching['cues']:
        clips['coach_'+cue['id'].lower()] = cue['text']
    return clips

AUDITION = {
    'cockpit': 'Find the highlighted altimeter: the round dial with several hands and a number window. Aim for seven thousand feet, then look outside again.',
    'numbers': 'Hold heading zero nine zero. Aim for sixty-five knots. The traffic pattern is six thousand three hundred feet.',
    'correction': 'Ease the nose down slightly. Check your airspeed.',
    'handoff': "You have the controls. When you are ready, let me take over from the pause menu.",
    'landmark': "Boulder Reservoir is a recreation area and part of the city's water supply. The Flatirons rise along the foothills.",
    'goaround': "This approach isn't settled. I'll climb away and try again.",
}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, default=REPO/'QuestFlight')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--cache', type=Path, required=True)
    parser.add_argument('--voice', default='am_michael', choices=['af_heart', 'am_michael'])
    parser.add_argument('--speed', type=float, default=1.0)
    parser.add_argument('--audition', action='store_true')
    parser.add_argument('--only-extra', action='store_true', help='Generate only the supplied extra manifest.')
    parser.add_argument('--extra-manifest', type=Path)
    parser.add_argument('--pronunciations', type=Path)
    parser.add_argument('--promote', action='store_true')
    parser.add_argument('--headset-candidate', action='store_true', help='Explicitly promote for human listening, retaining pending-review status.')
    args = parser.parse_args()
    args.output = args.output.resolve(); args.project = args.project.resolve()
    if args.output.is_relative_to(args.project):
        raise ValueError('Stage recordings outside the Unity project.')
    args.output.mkdir(parents=True, exist_ok=True)
    clips = {} if args.only_extra else dict(AUDITION) if args.audition else lesson_copy(args.project)
    if args.only_extra and not args.extra_manifest: raise ValueError('--only-extra requires --extra-manifest')
    if args.extra_manifest:
        extra_ids = set()
        for clip in json.loads(args.extra_manifest.read_text(encoding='utf-8'))['clips']:
            if clip['id'] in extra_ids: raise ValueError('Duplicate extra cue: '+clip['id'])
            extra_ids.add(clip['id'])
            if clip['id'] in clips and clips[clip['id']] != clip['text']:
                raise ValueError('Conflicting cue: '+clip['id'])
            clips[clip['id']] = clip['text']
    overrides = json.loads(args.pronunciations.read_text(encoding='utf-8')) if args.pronunciations else {}
    if set(overrides)-set(clips): raise ValueError('Pronunciation override has unknown cue ID.')
    for key, text in clips.items():
        if not re.fullmatch('[a-z0-9_]+', key) or not text.strip(): raise ValueError('Invalid cue: '+key)
    manifest_path = args.output/'generation.json'
    if args.promote:
        if args.audition: raise ValueError('Auditions cannot be promoted.')
        generated = json.loads(manifest_path.read_text(encoding='utf-8'))
        review_path=args.output/'reviewed.json'
        reviewed = json.loads(review_path.read_text(encoding='utf-8')) if review_path.exists() else {}
        entries = {c['id']: c for c in generated['clips']}
        if set(entries) != set(clips): raise ValueError('Generated coverage is stale.')
        for key, text in clips.items():
            entry = entries[key]; path = args.output/(key+'.wav')
            if entry['text'] != text or entry['spoken_text'] != overrides.get(key,text): raise ValueError('Stale copy: '+key)
            if digest(path.read_bytes()) != entry['sha256']: raise ValueError('Output hash mismatch: '+key)
            if not args.headset_candidate and reviewed.get(key) != entry['sha256']: raise ValueError('Missing matching listening review: '+key)
        destination = args.project/'Assets/Production/Training/Voice'
        destination.mkdir(parents=True, exist_ok=True)
        for key in clips:
            shutil.copyfile(args.output/(key+'.wav'), destination/(key+'.wav'))
            meta = destination/(key+'.wav.meta')
            if meta.exists():
                text = meta.read_text(encoding='utf-8').replace('sampleRateOverride: 22050','sampleRateOverride: 24000').replace('normalize: 1','normalize: 0')
                meta.write_text(text, encoding='utf-8')
        write_json(destination.parent/'VoiceScripts.json', {'clips':[{'id':k,'text':v} for k,v in clips.items()]})
        write_json(destination/'KOKORO_SOURCE_MANIFEST.json', generated)
        print(f'Promoted {len(clips)} clips; listening review '+('PENDING (headset candidate).' if args.headset_candidate else 'complete.'))
        return

    import numpy as np
    import soundfile as sf
    import torch
    from huggingface_hub import snapshot_download
    from kokoro import KModel, KPipeline
    torch.set_num_threads(4)
    torch.manual_seed(172)
    voices = ['af_heart','am_michael'] if args.audition else [args.voice]
    snapshot = Path(snapshot_download(MODEL, revision=REVISION, cache_dir=str(args.cache), allow_patterns=['config.json','kokoro-v1_0.pth','LICENSE']+[f'voices/{v}.pt' for v in voices]))
    model = KModel(repo_id=MODEL, config=str(snapshot/'config.json'), model=str(snapshot/'kokoro-v1_0.pth')).eval()
    pipeline = KPipeline(lang_code='a', repo_id=MODEL, model=model)
    packages = {p:importlib.metadata.version(p) for p in ['kokoro','misaki','torch','transformers','soundfile','spacy']}
    entries=[]
    old = json.loads(manifest_path.read_text(encoding='utf-8')) if manifest_path.exists() else {'clips':[]}
    old = {c['id']:c for c in old['clips']}
    for voice in voices:
        voice_file = snapshot/f'voices/{voice}.pt'
        voice_tensor = torch.load(voice_file, map_location='cpu', weights_only=True)
        for key,text in clips.items():
            identifier = voice+'_'+key if args.audition else key
            spoken = overrides.get(key,text)
            settings = {'text':text,'spoken_text':spoken,'voice':voice,'voice_sha256':digest(voice_file.read_bytes()),'speed':args.speed,'model_revision':REVISION,'packages':packages}
            fingerprint=digest(json.dumps(settings,sort_keys=True).encode())
            target=args.output/(identifier+'.wav')
            if identifier in old and old[identifier]['fingerprint']==fingerprint and target.exists() and digest(target.read_bytes())==old[identifier]['sha256']:
                entry=old[identifier]
            else:
                chunks=[r.audio.detach().cpu().numpy() for r in pipeline(spoken, voice=voice_tensor, speed=args.speed) if r.audio is not None]
                if not chunks: raise ValueError('No audio: '+identifier)
                audio=np.concatenate(chunks).astype(np.float32)
                if not np.isfinite(audio).all() or len(audio)<RATE*.25: raise ValueError('Invalid audio: '+identifier)
                # Shared conservative gain, peak ceiling only; preserve relative phrase dynamics.
                audio*=.85
                peak=float(np.max(np.abs(audio)))
                if peak>.89: audio*=.89/peak
                sf.write(target,audio,RATE,subtype='PCM_16')
                entry={'id':identifier,**settings,'fingerprint':fingerprint,'seconds':len(audio)/RATE,'sha256':digest(target.read_bytes()),'review':'pending listening'}
            entries.append(entry)
            write_json(manifest_path,{'generator':'tools/audio/generate_kokoro_voice.py','model':MODEL,'model_revision':REVISION,'model_license':'Apache-2.0','source':'https://huggingface.co/hexgrad/Kokoro-82M','sample_rate':RATE,'clips':entries})
            print(f"{identifier}: {entry['seconds']:.1f}s",flush=True)
    cards=''.join(f'<article><h2>{html.escape(e["id"])}</h2><p>{html.escape(e["text"])}</p><audio controls preload="none" src="{e["id"]}.wav"></audio></article>' for e in entries)
    (args.output/'listen.html').write_text('<!doctype html><meta charset="utf-8"><title>QuestFlight voice audition</title><style>body{background:#111820;color:#eef4f8;font:18px system-ui;max-width:900px;margin:40px auto}article{padding:20px;border-bottom:1px solid #456}audio{width:100%}h2{font-size:18px}</style><h1>QuestFlight instructor recordings</h1>'+cards,encoding='utf-8')
    print('Generation complete. Listening review required before promotion.')

if __name__ == '__main__':
    main()
