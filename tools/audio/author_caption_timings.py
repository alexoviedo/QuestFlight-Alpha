"""Recover Kokoro token timings only when regenerated audio matches the shipped recording.
No word-count/read-speed timing and no runtime speech inference.
"""
import argparse, hashlib, json, math, os, tempfile
from pathlib import Path

def readable(text):
    return any(c.isalnum() for c in text)

def repair_phrases(phrases):
    """Migrate punctuation-only cached cues without changing spoken-word timings."""
    repaired=[]
    for phrase in phrases:
        phrase=dict(phrase)
        if not readable(phrase['text']):
            if not repaired or not phrase['text'].strip():
                raise ValueError('Caption has no readable words')
            repaired[-1]['text']=repaired[-1]['text'].rstrip()+phrase['text'].strip()
            repaired[-1]['end']=max(repaired[-1]['end'],phrase['end'])
        else:
            repaired.append(phrase)
    return repaired

def segment_words(words):
    phrases=[];text='';start=0;end=0
    for w in words:
        # Punctuation belongs to the preceding words even at a duration/length limit.
        if not readable(w['text']):
            if text:
                text=text.rstrip()+w['text']+(' ' if w['space'] else '');end=w['end']
            elif phrases:
                phrases[-1]['text']=phrases[-1]['text'].rstrip()+w['text'].strip()
                phrases[-1]['end']=max(phrases[-1]['end'],w['end'])
            else:
                raise ValueError('Punctuation precedes all timed words')
        else:
            if text and (len(text)+len(w['text'])>76 or end-start>4.5):
                phrases.append({'start':start,'end':end,'text':text.strip()});text=''
            if not text:start=w['start']
            text+=w['text']+(' ' if w['space'] else '');end=w['end']
        if text and w['text'].endswith(('.', '?','!',';')) and len(text)>28:
            phrases.append({'start':start,'end':end,'text':text.strip()});text=''
    if text:phrases.append({'start':start,'end':end,'text':text.strip()})
    return repair_phrases(phrases)

def validate_clip(clip):
    if not math.isfinite(clip['seconds']) or clip['seconds']<=0 or not clip['phrases']:
        raise ValueError('Invalid caption duration/phrases: '+clip['id'])
    previous=-1
    for phrase in clip['phrases']:
        start,end=phrase['start'],phrase['end']
        if (not readable(phrase['text']) or not math.isfinite(start) or not math.isfinite(end)
                or start<0 or start<previous or end<start or end>clip['seconds']+.05):
            raise ValueError('Invalid caption phrase: '+clip['id'])
        previous=start

def save_catalog(output, entries):
    """Accept a complete catalog only; a failed run leaves the old catalog intact."""
    for clip in entries:validate_clip(clip)
    output.parent.mkdir(parents=True,exist_ok=True)
    temporary=None
    try:
        with tempfile.NamedTemporaryFile(mode='w',encoding='utf-8',dir=output.parent,
                prefix=output.name+'.',suffix='.tmp',delete=False) as stream:
            temporary=Path(stream.name)
            json.dump({'schema':1,'method':'Kokoro token durations; regenerated waveform matched actual PCM within two quantization steps','clips':entries},stream,indent=2,ensure_ascii=False)
            stream.write('\n');stream.flush();os.fsync(stream.fileno())
        os.replace(temporary,output)
    finally:
        if temporary is not None and temporary.exists():temporary.unlink()

def main():
    p=argparse.ArgumentParser();p.add_argument('--voice-root',type=Path,required=True);p.add_argument('--cache',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--refresh-audio',action='store_true');p.add_argument('--backup',type=Path);p.add_argument('--cached-only',action='store_true',help='Repair authenticated cached captions; fail if any clip needs synthesis')
    a=p.parse_args()
    import numpy as np
    import soundfile as sf
    manifest=json.loads((a.voice_root/'KOKORO_SOURCE_MANIFEST.json').read_text(encoding='utf-8-sig'))
    if len({e['id'] for e in manifest['clips']})!=len(manifest['clips']):raise ValueError('Duplicate recording ID')
    pipeline=None
    old=json.loads(a.output.read_text(encoding='utf-8')) if a.output.exists() else {'clips':[]}
    cached={c['id']:c for c in old['clips']};entries=[]
    for entry in manifest['clips']:
        path=a.voice_root/(entry['id']+'.wav');sha=hashlib.sha256(path.read_bytes()).hexdigest()
        if sha!=entry['sha256']:raise ValueError('Stale audio provenance: '+entry['id'])
        if entry['id'] in cached and cached[entry['id']]['sha256']==sha:
            clip=dict(cached[entry['id']]);clip['phrases']=repair_phrases(clip['phrases']);validate_clip(clip)
            entries.append(clip);continue
        if a.cached_only:raise ValueError('No authenticated cached timings: '+entry['id'])
        if pipeline is None:
            import torch
            from huggingface_hub import snapshot_download
            from kokoro import KModel,KPipeline
            torch.set_num_threads(4);torch.manual_seed(172)
            snap=Path(snapshot_download('hexgrad/Kokoro-82M',revision=manifest['model_revision'],cache_dir=str(a.cache),allow_patterns=['config.json','kokoro-v1_0.pth','voices/am_michael.pt']))
            pipeline=KPipeline(lang_code='a',repo_id='hexgrad/Kokoro-82M',model=KModel(repo_id='hexgrad/Kokoro-82M',config=str(snap/'config.json'),model=str(snap/'kokoro-v1_0.pth')).eval())
            voice=torch.load(snap/'voices/am_michael.pt',map_location='cpu',weights_only=True)
        chunks=[];words=[];offset=0
        for r in pipeline(entry['spoken_text'],voice=voice,speed=entry['speed']):
            if r.audio is None:continue
            audio=r.audio.detach().cpu().numpy();chunks.append(audio)
            for t in r.tokens or []:
                if t.start_ts is not None and t.end_ts is not None:
                    words.append({'text':t.text,'start':offset+t.start_ts,'end':offset+t.end_ts,'space':bool(t.whitespace)})
            offset+=len(audio)/24000
        audio=np.concatenate(chunks).astype(np.float32)*.85
        peak=float(np.max(np.abs(audio)))
        if peak>.89:audio*=.89/peak
        actual,rate=sf.read(path,dtype='float32')
        if rate!=24000 or len(audio)!=len(actual) or np.max(np.abs(audio-actual))>2/32768:
            if not a.refresh_audio or a.backup is None:raise ValueError('Cannot authenticate timings against actual waveform: '+entry['id'])
            import shutil
            a.backup.mkdir(parents=True,exist_ok=True)
            if not (a.backup/path.name).exists():shutil.copyfile(path,a.backup/path.name)
            sf.write(path,audio,24000,subtype='PCM_16');actual,rate=sf.read(path,dtype='float32')
            sha=hashlib.sha256(path.read_bytes()).hexdigest();entry['sha256']=sha;entry['seconds']=len(actual)/rate;entry['review']='pending listening; refreshed with sample-aligned caption timings'
            (a.voice_root/'KOKORO_SOURCE_MANIFEST.json').write_text(json.dumps(manifest,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
        phrases=segment_words(words)
        if not phrases:raise ValueError('No timed words: '+entry['id'])
        entries.append({'id':entry['id'],'sha256':sha,'seconds':len(actual)/rate,'phrases':phrases})
        print(entry['id'],len(phrases),flush=True)
    save_catalog(a.output,entries)
    print('Authenticated',len(entries),'recordings',flush=True)
if __name__=='__main__':main()
