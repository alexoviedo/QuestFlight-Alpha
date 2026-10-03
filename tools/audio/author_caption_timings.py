"""Recover Kokoro token timings only when regenerated audio matches the shipped recording.
No word-count/read-speed timing and no runtime speech inference.
"""
import argparse, hashlib, json
from pathlib import Path

def main():
    p=argparse.ArgumentParser();p.add_argument('--voice-root',type=Path,required=True);p.add_argument('--cache',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--refresh-audio',action='store_true');p.add_argument('--backup',type=Path)
    a=p.parse_args()
    import numpy as np
    import soundfile as sf
    import torch
    from huggingface_hub import snapshot_download
    from kokoro import KModel,KPipeline
    torch.set_num_threads(4);torch.manual_seed(172)
    manifest=json.loads((a.voice_root/'KOKORO_SOURCE_MANIFEST.json').read_text(encoding='utf-8-sig'))
    snap=Path(snapshot_download('hexgrad/Kokoro-82M',revision=manifest['model_revision'],cache_dir=str(a.cache),allow_patterns=['config.json','kokoro-v1_0.pth','voices/am_michael.pt']))
    pipeline=KPipeline(lang_code='a',repo_id='hexgrad/Kokoro-82M',model=KModel(repo_id='hexgrad/Kokoro-82M',config=str(snap/'config.json'),model=str(snap/'kokoro-v1_0.pth')).eval())
    voice=torch.load(snap/'voices/am_michael.pt',map_location='cpu',weights_only=True)
    old=json.loads(a.output.read_text(encoding='utf-8')) if a.output.exists() else {'clips':[]}
    cached={c['id']:c for c in old['clips']};entries=[]
    for entry in manifest['clips']:
        path=a.voice_root/(entry['id']+'.wav');sha=hashlib.sha256(path.read_bytes()).hexdigest()
        if sha!=entry['sha256']:raise ValueError('Stale audio provenance: '+entry['id'])
        if entry['id'] in cached and cached[entry['id']]['sha256']==sha:entries.append(cached[entry['id']]);continue
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
        phrases=[];text='';start=0;end=0
        for w in words:
            if text and (len(text)+len(w['text'])>76 or end-start>4.5):
                phrases.append({'start':start,'end':end,'text':text.strip()});text=''
            if not text:start=w['start']
            text+=w['text']+(' ' if w['space'] else '');end=w['end']
            if w['text'].endswith(('.', '?','!',';')) and len(text)>28:
                phrases.append({'start':start,'end':end,'text':text.strip()});text=''
        if text:phrases.append({'start':start,'end':end,'text':text.strip()})
        if not phrases:raise ValueError('No timed words: '+entry['id'])
        entries.append({'id':entry['id'],'sha256':sha,'seconds':len(actual)/rate,'phrases':phrases})
        a.output.parent.mkdir(parents=True,exist_ok=True)
        a.output.write_text(json.dumps({'schema':1,'method':'Kokoro token durations; regenerated waveform matched actual PCM within two quantization steps','clips':entries},indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
        print(entry['id'],len(phrases),flush=True)
    print('Authenticated',len(entries),'recordings',flush=True)
if __name__=='__main__':main()
