"""Read frozen manifests/annotations only; no model inference or label edits."""
from pathlib import Path
import pandas as pd,json
home=Path.home()/'PrecisionSkinV1'; out=Path(__file__).resolve().parents[1]/'audit_results'
r=pd.read_csv(home/'metadata/metadata/split_manifest.csv')
f=pd.read_csv(home/'v6/splits/v6_split_manifest.csv')
eligible=r[(r.used_as=='facial_primary') & ~r.facial_qa_status.isin(['reject','manual_review'])].copy()
eligible['source_key']=eligible.label_class+'/'+eligible.filename.map(lambda n:'roboflow_export' if '.rf.' in n.lower() else 'upscaled' if n.lower().startswith('upscaled') else 'aug' if n.lower().startswith('aug_') else 'other')
j=f.merge(eligible,left_on='v5_image_id',right_on='image_id',suffixes=('_v6','_v5'),validate='one_to_one')
ann=pd.read_csv(home/'v6/annotations/multilabel_annotations.csv',dtype=str).fillna('')
reg=pd.read_csv(home/'v6/annotations/region_annotations.csv',dtype=str).fillna('')
result=dict(raw_manifest_images=r.groupby('label_class').size().to_dict(),
    catalog_facial_images=eligible.groupby('label_class').size().to_dict(),
    frozen_images=j.groupby('label_class').size().to_dict(),
    frozen_split_class_counts=j.groupby(['split_v6','label_class']).size().unstack(fill_value=0).to_dict('index'),
    source_counts_catalog=eligible.groupby('source_key').size().to_dict(),
    source_counts_frozen=j.groupby('source_key').size().to_dict(),
    manifest_source_values=r.source.value_counts(dropna=False).to_dict(),
    original_filepath_prefixes=r.filepath.str.extract(r'(^.*?/Classes/)')[0].value_counts().to_dict(),
    image_types=j.groupby(['label_class','image_type']).size().unstack(fill_value=0).to_dict('index'),
    subject_values=r.subject_id.fillna('unknown').value_counts().to_dict(),
    annotation_status=ann.review_status.value_counts(dropna=False).to_dict(),
    annotation_rows=len(ann),region_rows=len(reg),
    mixed_filename_hints={ 'acne_names_pigment':int(j[(j.label_class=='Acne') & j.filename.str.contains('pigment',case=False)].shape[0]),
                         'acne_names_eczema_dermatitis':int(j[(j.label_class=='Acne') & j.filename.str.contains('eczema|dermatitis',case=False)].shape[0])})
links=out/'original_export_links.csv'
if links.exists():
    l=pd.read_csv(links,encoding='utf-8')
    linked=j.merge(l,left_on=['label_class','filename'],right_on=['condition','original'],suffixes=('','_link'))
    result['confirmed_export_links_frozen']=[dict(source_key=k,source_export=s,images=int(g.v5_image_id.nunique())) for (k,s),g in linked.groupby(['source_key','source_export'])]
    result['linked_box_candidates_by_frozen_split']=[dict(split=sp,source_export=s,images=int(g.v5_image_id.nunique())) for (sp,s),g in linked.groupby(['split_v6','source_export'])]
    linked[['v5_image_id','split_v6','source_key','source_export','label_file']].to_csv(out/'frozen_export_annotation_candidates.csv',index=False)
v5=out/'v5_exposure.csv'; yolo=out/'yolo_frozen_group_candidates.csv'
if v5.exists() and yolo.exists():
    a=pd.read_csv(v5).merge(pd.read_csv(yolo),on=['v5_image_id','split_v6','group_id'])
    hg=out/'yolo_historical_list_exact.csv'
    groups=set(pd.read_csv(hg).group_id) if hg.exists() else set()
    a=a[~a.v5_train_proxy_group & ~a.yolo_candidate_proxy_group & ~a.group_id.isin(groups)]
    a=a.merge(j[['v5_image_id','label_class']],on='v5_image_id')
    result['candidate_no_identified_exposure_by_source_class']=a.groupby(['split_v6','label_class']).size().unstack(fill_value=0).to_dict('index')
(out/'preparation_metadata.json').write_text(json.dumps(result,indent=2,default=str)); print(json.dumps(result,indent=2,default=str))
