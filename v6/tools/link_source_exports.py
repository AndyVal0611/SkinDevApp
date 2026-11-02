"""Conservative original/export linkage: same basename plus identical SHA-256.

No matches by name alone count as confirmed. Multiple export matches are retained.
"""
from pathlib import Path
import hashlib,json,csv,collections,re
base=Path('C:/Users/Bautista/Downloads'); orig=base/'PrecisionSkin/Classes'
out=Path(__file__).resolve().parents[1]/'audit_results'
ext={'.jpg','.jpeg','.png','.webp','.bmp'}
idx=collections.defaultdict(list)
for p in orig.rglob('*'):
    if p.is_file() and p.suffix.lower() in ext: idx[p.name.lower()].append(p)
cache={}
def sha(p):
    if p not in cache: cache[p]=hashlib.sha256(p.read_bytes()).hexdigest()
    return cache[p]
matches=[]; summary=[]; docs=[]
for source in (base/'Localization Training Datasets').iterdir():
    if not source.is_dir(): continue
    count=0; named=0; exact=0; label_count=0
    for p in source.rglob('*'):
        if p.is_file() and p.suffix.lower() in ext:
            count+=1
            candidates=idx.get(p.name.lower(),[])
            if candidates: named+=1
            for q in candidates:
                if sha(p)==sha(q):
                    exact+=1
                    label=p.parent.parent/'labels'/(p.stem+'.txt')
                    matches.append(dict(source_export=source.name,condition=q.parent.name,original=q.name,
                        source_key=q.parent.name+'/'+('roboflow_export' if '.rf.' in q.name.lower() else 'aug' if q.name.lower().startswith('aug_') else 'other'),
                        source_image=str(p),label_file=str(label) if label.exists() else '',evidence='same_basename_and_exact_sha256'))
        if p.is_file() and p.suffix=='.txt' and p.parent.name=='labels': label_count+=1
    for p in source.glob('README*.txt'):
        docs.append(dict(path=str(p),text=p.read_text(errors='replace')))
    summary.append(dict(source_export=source.name,images=count,label_files=label_count,basename_candidates=named,exact_pairs=exact))
    print(summary[-1],flush=True)
with (out/'original_export_links.csv').open('w',newline='',encoding='utf-8') as f:
    w=csv.DictWriter(f,fieldnames=['source_export','condition','original','source_key','source_image','label_file','evidence']);w.writeheader();w.writerows(matches)
(out/'export_source_evidence.json').write_text(json.dumps(dict(exports=summary,readme_records=docs),indent=2))
