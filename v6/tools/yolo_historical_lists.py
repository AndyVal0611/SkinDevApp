"""Audit actual historical train-list bytes against frozen evaluation originals."""
from pathlib import Path
import hashlib, json
import pandas as pd
home=Path.home()/'PrecisionSkinV1'
out=Path(__file__).resolve().parents[1]/'audit_results'
split=pd.read_csv(home/'v6/splits/v6_split_manifest.csv')
raw=pd.read_csv(home/'metadata/metadata/split_manifest.csv')
d=split.merge(raw,left_on='v5_image_id',right_on='image_id',suffixes=('_v6','_v5'))
# The frozen split is group-disjoint, so only evaluation-role groups can
# contribute to evaluation exposure; avoid hashing unrelated training groups.
d=d[d.split_v6.isin(['validation','fusion','test'])]
orig=Path('/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Classes')
byhash={}; missing=0
for r in d.itertuples():
    p=orig/r.src_label/r.filename
    if not p.is_file(): missing+=1; continue
    h=hashlib.sha256(p.read_bytes()).hexdigest()
    byhash.setdefault(h,[]).append((r.v5_image_id,r.split_v6,int(r.group_id)))
lists=list((home/'lesion_experiments/runs').glob('H25*/train_list.txt'))
records=[]; stats=[]
for listing in lists:
    seen=0; absent=0; groups=set(); exact=[]
    for line in listing.read_text().splitlines():
        p=Path(line.strip())
        if not p.is_file(): absent+=1; continue
        seen+=1; h=hashlib.sha256(p.read_bytes()).hexdigest()
        for iid,sp,g in byhash.get(h,[]):
            groups.add(g)
            if sp in ('validation','fusion','test'):
                exact.append(dict(run=listing.parent.name,image_id=iid,split=sp,group_id=g,evidence='exact_original_file_sha256'))
    records.extend(exact)
    stats.append(dict(run=listing.parent.name,readable_train_files=seen,missing_train_files=absent,
                      exact_unique_eval_images=len({r['image_id'] for r in exact}),
                      proxy_eval_images=int(((d.split_v6.isin(['validation','fusion','test'])) & d.group_id.isin(groups)).sum())))
pd.DataFrame(records,columns=['run','image_id','split','group_id','evidence']).to_csv(out/'yolo_historical_list_exact.csv',index=False)
(out/'yolo_historical_lists_summary.json').write_text(json.dumps(dict(runs=stats,missing_originals=missing,
    limitations='Historical train lists found for H25 only. Current listed file bytes do not authenticate past training snapshots or resolve installed ONNX hash mismatch. No eczema manifest proven. Patient identity unknown.'),indent=2))
print(json.dumps(stats,indent=2),flush=True)
