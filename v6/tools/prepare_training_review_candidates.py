"""Prepare pointers to existing train/validation box drafts, without labels or edits."""
from pathlib import Path
import pandas as pd
root=Path(__file__).resolve().parents[1]; out=root/'audit_results'
home=Path.home()/'PrecisionSkinV1'
r=pd.read_csv(home/'metadata/metadata/split_manifest.csv')
f=pd.read_csv(home/'v6/splits/v6_split_manifest.csv')
d=f.merge(r,left_on='v5_image_id',right_on='image_id',suffixes=('_v6','_v5'))
d=d[d.split_v6.isin(['train','validation'])].copy()
d['original_pool_image']=d.src_label+'/'+d.filename
p=pd.read_csv(out/'yolo_original_pool_candidates.csv')
p['original_pool_image']=p.original_pool_image.str.replace('\\','/',regex=False)
j=d.merge(p,on='original_pool_image')
rows=[]
base=Path('/mnt/c/Users/Bautista/Downloads/PrecisionSkin')
for v in j.itertuples():
    image=base/v.training_candidate.replace('\\','/')
    label=image.parent.parent/'labels'/(image.stem+'.txt')
    if label.is_file(): rows.append(dict(image_id=v.v5_image_id,split=v.split_v6,source=v.candidate_dataset,box_file=str(label),status='draft_only_not_clinically_verified'))
extra=pd.read_csv(out/'frozen_export_annotation_candidates.csv')
for v in extra[extra.split_v6.isin(['train','validation'])].itertuples():
    path=v.label_file.replace('C:\\','/mnt/c/').replace('\\','/')
    rows.append(dict(image_id=v.v5_image_id,split=v.split_v6,source=v.source_export,box_file=path,status='draft_only_not_clinically_verified'))
result=pd.DataFrame(rows).drop_duplicates()
result.to_csv(out/'train_validation_box_review_candidates.csv',index=False)
print(result.groupby(['split','source']).image_id.nunique().to_string())
