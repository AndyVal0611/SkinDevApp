"""Evidence-only rights manifests. Never edits data, splits or permission registry."""
from pathlib import Path
import pandas as pd,json,hashlib
root=Path(__file__).resolve().parents[1]; out=root/'audit_results/rights_review';out.mkdir(parents=True,exist_ok=True)
home=Path.home()/'PrecisionSkinV1'
inputs=[home/'metadata/metadata/split_manifest.csv',home/'v6/splits/v6_split_manifest.csv',home/'v6/annotations/dataset_provenance.csv',home/'v6/annotations/multilabel_annotations.csv',home/'v6/annotations/region_annotations.csv']
fingerprint=lambda:{str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
before=fingerprint()
r=pd.read_csv(inputs[0]); f=pd.read_csv(inputs[1]); prov=pd.read_csv(inputs[2],dtype=str).fillna('')
assert prov.permissible_use.eq('').all(), 'New permission entries require explicit evidence review; do not reuse zero-clearance conclusion'
d=r[(r.used_as=='facial_primary') & ~r.facial_qa_status.isin(['reject','manual_review'])].copy()
d=d.rename(columns={'image_id':'v5_image_id','split':'historical_v5_split'})
d=d.merge(f[['v5_image_id','group_id','split']],on='v5_image_id',how='left',validate='one_to_one')
d['condition']=d.label_class
d['source_key']=d.condition+'/'+d.filename.map(lambda n:'roboflow_export' if '.rf.' in n.lower() else 'upscaled' if n.lower().startswith('upscaled') else 'aug' if n.lower().startswith('aug_') else 'other')
d['original_image_path']='/mnt/c/Users/Bautista/Downloads/PrecisionSkin/Classes/'+d.condition+'/'+d.filename
d['in_frozen_v6']=d['split'].notna()
d['filename_dermnet_hint']=d.filename.str.contains('dermnet',case=False,na=False)
hint_hash=set(d.loc[d.filename_dermnet_hint,'md5'].dropna())
hint_groups=set(d.loc[d.filename_dermnet_hint,'group_id'].dropna())
d['dermnet_exact_manifest_hash_link']=d.md5.isin(hint_hash)
d['dermnet_proxy_group_link']=d.group_id.notna() & d.group_id.isin(hint_groups)
d['possible_dermnet']=d.filename_dermnet_hint | d.dermnet_exact_manifest_hash_link | d.dermnet_proxy_group_link
links=pd.read_csv(root/'audit_results/original_export_links.csv',encoding='utf-8')
link_map=links.groupby(['condition','original']).source_export.agg(lambda s:'; '.join(sorted(set(s)))).to_dict()
d['exact_export_associations']=[link_map.get((c,n),'') for c,n in zip(d.condition,d.filename)]
d['licence_declaration_evidence']=d.exact_export_associations.map(lambda s:'Local hyperpigmentation.v2i export README states CC BY 4.0; upstream rights unverified' if 'hyperpigmentation.v2i.yolov8' in s else '')
d['ai_training_permission']='unresolved'
d['confirmed_restriction']='not_established_for_individual_image'
d['hold_reason']=d.possible_dermnet.map(lambda x:'Possible DermNet origin: verify owner and any separately granted AI licence; no training/evaluation clearance' if x else 'Image-linked upstream rights, consent basis and responsible approval not substantiated')
cols=['v5_image_id','condition','filename','source_key','original_image_path','in_frozen_v6','split','group_id','historical_v5_split','md5','ai_training_permission','confirmed_restriction','filename_dermnet_hint','dermnet_exact_manifest_hash_link','dermnet_proxy_group_link','possible_dermnet','exact_export_associations','licence_declaration_evidence','hold_reason']
d[cols].to_csv(out/'image_rights_inventory.csv',index=False)
d[d.in_frozen_v6][cols].to_csv(out/'unresolved_frozen_images.csv',index=False)
d[d.possible_dermnet][cols].to_csv(out/'possible_dermnet_review.csv',index=False)
d.iloc[:0][cols].to_csv(out/'verified_training_images.csv',index=False)
d.iloc[:0][cols].to_csv(out/'confirmed_restricted_images.csv',index=False)
summary=[]
for condition,g in d.groupby('condition'):
    q=g[g.in_frozen_v6]
    summary.append(dict(condition=condition,catalog_images=len(g),frozen_images=len(q),verified_ai_training_permission=0,unresolved_frozen=len(q),confirmed_restricted=0,possible_dermnet_catalog=int(g.possible_dermnet.sum()),possible_dermnet_frozen=int(q.possible_dermnet.sum()),dermnet_filename_hints_frozen=int(q.filename_dermnet_hint.sum()),dermnet_group_or_hash_only_frozen=int((q.possible_dermnet & ~q.filename_dermnet_hint).sum())))
pd.DataFrame(summary).to_csv(out/'condition_rights_counts.csv',index=False)
pd.DataFrame(columns=['evidence_id','image_id_or_exact_scope','original_owner','source_url_and_version','licence_or_written_grant','ai_training_allowed','ai_validation_testing_allowed','derivatives_allowed','consent_basis','identifiable_face_handling','retention_policy','responsible_reviewer','verification_date','evidence_document_path']).to_csv(out/'permission_evidence_template.csv',index=False)
assert before==fingerprint(), 'Protected input changed during audit'
(out/'input_integrity.json').write_text(json.dumps({'before':before,'after':fingerprint(),'unchanged':True},indent=2))
(out/'rights_summary.json').write_text(json.dumps({'conditions':summary,'frozen_split_counts':d[d.in_frozen_v6].groupby('split').size().to_dict(),'verified_subset_meets_training_gate':False,'status':'No substantiated image-linked AI-training grants found. Possible DermNet flags are clues, not proven origin or adjudicated restrictions.'},indent=2))
print(json.dumps(summary,indent=2))
