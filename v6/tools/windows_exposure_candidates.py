"""Fallback source-pool matches. Does not assert V6 membership or patient identity."""
from pathlib import Path
import hashlib, json, csv
root=Path(__file__).resolve().parents[1]; out=root/'audit_results'; out.mkdir(exist_ok=True)
base=Path('C:/Users/Bautista/Downloads/PrecisionSkin')
orig=base/'Classes'
hashes={}; names={}
for p in orig.rglob('*'):
    if p.is_file() and p.suffix.lower() in {'.jpg','.jpeg','.png','.webp'}:
        digest=hashlib.md5(p.read_bytes()).hexdigest()
        hashes.setdefault(digest,[]).append(str(p.relative_to(orig)))
        names.setdefault(p.name.lower(),[]).append(str(p.relative_to(orig)))
datasets={'acne_installed_metadata_dataset':base/'Acne_Boxing/acne_dataset_v4/train',
          'hyperpigmentation_candidate_v2_train':base/'Hyperpigmentation_Boxing/V2_Train/boxed_v2_train'}
rows=[]; counts={}
for key,d in datasets.items():
    count=0
    for p in d.rglob('*'):
        if not p.is_file() or p.suffix.lower() not in {'.jpg','.jpeg','.png','.webp'}: continue
        count+=1; digest=hashlib.md5(p.read_bytes()).hexdigest()
        for match in hashes.get(digest,[]):
            rows.append(dict(candidate_dataset=key,training_candidate=str(p.relative_to(base)),original_pool_image=match,evidence='exact_file_md5'))
        if digest not in hashes:
            for match in names.get(p.name.lower(),[]):
                rows.append(dict(candidate_dataset=key,training_candidate=str(p.relative_to(base)),original_pool_image=match,evidence='filename_only_unconfirmed'))
    counts[key]=count
with (out/'yolo_original_pool_candidates.csv').open('w',newline='') as f:
    writer=csv.DictWriter(f,fieldnames=['candidate_dataset','training_candidate','original_pool_image','evidence']); writer.writeheader(); writer.writerows(rows)
(out/'windows_exposure_summary.json').write_text(json.dumps(dict(dataset_files=counts,matches=len(rows),exact=sum(r['evidence']=='exact_file_md5' for r in rows),limitations='Matches concern the original image pool, not confirmed V6 evaluation membership. Hyperpigmentation folder is a candidate; historical run snapshot and installed weight linkage remain unverified. No patient identifiers available.'),indent=2))
print((out/'windows_exposure_summary.json').read_text())
