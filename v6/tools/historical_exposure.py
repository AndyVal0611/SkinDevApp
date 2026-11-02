"""Read historical metadata/images only; write audit artifacts in repository."""
from pathlib import Path
import pandas as pd
import json, hashlib, collections
from source_policy import source_issues
home=Path.home()/'PrecisionSkinV1'
out=Path(__file__).resolve().parents[1]/'audit_results'; out.mkdir(exist_ok=True)
split=pd.read_csv(home/'v6/splits/v6_split_manifest.csv')
raw=pd.read_csv(home/'metadata/metadata/split_manifest.csv')
joined=split.merge(raw, left_on='v5_image_id',right_on='image_id',suffixes=('_v6','_v5'),validate='one_to_one')
train=raw[raw['split']=='train']
train_md5=set(train.md5.dropna())
train_groups=set(joined.loc[(joined.split_v5=='train') | joined.md5.isin(train_md5),'group_id'])
evals=joined[joined.split_v6.isin(['validation','fusion','test'])].copy()
evals['v5_train_same_manifest_image']=evals.v5_image_id.isin(train.image_id)
evals['v5_train_exact_md5']=evals.md5.isin(train_md5)
evals['v5_train_proxy_group']=evals.group_id.isin(train_groups)
cols=['v5_image_id','split_v6','group_id','v5_train_same_manifest_image','v5_train_exact_md5','v5_train_proxy_group']
evals[cols].to_csv(out/'v5_exposure.csv',index=False)
pool_path=out/'yolo_original_pool_candidates.csv'
if pool_path.exists():
    pool=pd.read_csv(pool_path)
    pool['original_pool_image']=pool.original_pool_image.str.replace('\\','/',regex=False)
    # These matches hash original files, not resized classifier tensors.
    joined['original_pool_image']=joined.src_label+'/'+joined.filename
    candidates=joined.merge(pool,on='original_pool_image',how='inner')
    candidate_groups=set(candidates.group_id)
    eval_pairs=candidates[candidates.split_v6.isin(['validation','fusion','test'])]
    eval_pairs[['v5_image_id','split_v6','group_id','candidate_dataset','training_candidate','evidence']].to_csv(out/'yolo_frozen_evaluation_candidates.csv',index=False)
    flags=evals[['v5_image_id','split_v6','group_id']].copy()
    flags['yolo_candidate_proxy_group']=flags.group_id.isin(candidate_groups)
    flags.to_csv(out/'yolo_frozen_group_candidates.csv',index=False)
    # Retain dataset-specific lineage caveats: current folder match is not proof
    # that the installed model was historically trained on every file in it.
    (out/'yolo_frozen_summary.json').write_text(json.dumps({
        'exact_candidate_images_by_split':[{ 'split':sp, 'candidate_dataset':ds, 'images':int(n)} for (sp,ds),n in eval_pairs.groupby(['split_v6','candidate_dataset']).v5_image_id.nunique().items()],
        'proxy_group_images_by_split':flags.groupby('split_v6').yolo_candidate_proxy_group.sum().astype(int).to_dict(),
        'patient_identity':'unknown',
        'lineage':'Current-folder candidates; historical snapshots and model linkage still require verification.'},indent=2))
summary=evals.groupby('split_v6')[cols[3:]].sum().astype(int).to_dict('index')
# Discover historical run configuration, not merely current unrelated datasets.
args=list((home/'lesion_experiments/runs').glob('*/args.yaml'))
run_paths=[]
for p in args:
    if any(k in p.parent.name for k in ['H25','acne_final','NA2']):
        run_paths.append({'run':p.parent.name,'args':str(p),'data_lines':[l.strip() for l in p.read_text().splitlines() if l.startswith('data:')]})
(out/'yolo_training_manifest_candidates.json').write_text(json.dumps(run_paths,indent=2))
(out/'historical_exposure_summary.json').write_text(json.dumps({'v5':summary,'yolo_manifest_candidates':len(run_paths),'limitations':['V5 overlap is manifest/hash/proxy evidence, not patient identity.','YOLO args candidates require linkage to installed model hashes and original historical dataset snapshots; current paths alone cannot prove training membership.']},indent=2))
prov=pd.read_csv(home/'v6/annotations/dataset_provenance.csv',dtype=str).fillna('')
pd.DataFrame(source_issues(prov.to_dict('records'),prov.source_key,'training')).to_csv(out/'source_verification_required.csv',index=False)
print(json.dumps(summary,indent=2)); print('YOLO candidate records:',len(run_paths))
