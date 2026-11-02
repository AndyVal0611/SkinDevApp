"""Read-only Windows inventory and source-evidence discovery; outputs audit JSON."""
from pathlib import Path
import json,re,collections
base=Path('C:/Users/Bautista/Downloads')
repo=Path(__file__).resolve().parents[2]
out=repo/'v6/audit_results'; out.mkdir(exist_ok=True)
ext={'.jpg','.jpeg','.png','.webp','.bmp','.tif','.tiff'}
def counts(p):
    files=[f for f in p.rglob('*') if f.is_file() and f.suffix.lower() in ext]
    return dict(path=str(p),images=len(files))
inventory=[]
for parent in ('PrecisionSkin/Classes','PrecisionSkin_Raw','FINAL DATASET','Final Dataset V1'):
    p=base/parent
    if p.exists():
        for child in p.iterdir():
            if child.is_dir(): inventory.append(counts(child))
notebooks=list(repo.rglob('*.ipynb'))+list(base.glob('*.ipynb'))
p=base/'Training Essentials (AI models)'
if p.exists(): notebooks+=list(p.rglob('*.ipynb'))
evidence=[]
for p in notebooks:
    if 'backups' in p.parts: continue
    try: nb=json.loads(p.read_text(encoding='utf-8'))
    except (ValueError,OSError): continue
    for i,c in enumerate(nb.get('cells',[])):
        source=''.join(c.get('source',[]))
        for line in source.splitlines():
            if any(s in line.lower() for s in ('api_key','api-key','token=','password')): continue
            urls=re.findall(r'https?://[^\s\)\]\>\"\x27]+',line)
            if urls and any(k in line.lower() for k in ('dataset','roboflow','kaggle','dermnet','github','license','dermatolog')):
                evidence.append(dict(path=str(p),cell=i,urls=[u.split('?')[0] for u in urls],context=line[:300]))
boxes=[]
for parent in ('PrecisionSkin/Acne_Boxing','PrecisionSkin/Hyperpigmentation_Boxing','own_hyperpigmentation.yolov8','Detector_v1_imgsz640_files'):
    p=base/parent
    if not p.exists(): continue
    # Every images/labels pair is counted separately, including repeat exports.
    for labdir in p.rglob('labels'):
        if not labdir.is_dir(): continue
        imgdir=labdir.parent/'images'
        imgs={f.stem for f in imgdir.iterdir() if f.is_file() and f.suffix.lower() in ext} if imgdir.is_dir() else set()
        labels=list(labdir.glob('*.txt')); malformed=0; nboxes=0; empty=0; orphan=0; classes=collections.Counter()
        for f in labels:
            if f.stem not in imgs: orphan+=1
            lines=f.read_text(errors='replace').splitlines()
            if not any(l.strip() for l in lines): empty+=1
            for line in lines:
                if not line.strip(): continue
                try:
                    values=list(map(float,line.split()))
                    assert len(values)==5 and values[0]>=0 and values[0].is_integer()
                    k,x,y,w,h=values
                    assert 0<=x<=1 and 0<=y<=1 and 0<w<=1 and 0<h<=1
                    assert x-w/2>=-1e-5 and y-h/2>=-1e-5 and x+w/2<=1+1e-5 and y+h/2<=1+1e-5
                    nboxes+=1; classes[str(int(k))]+=1
                except (ValueError,AssertionError): malformed+=1
        boxes.append(dict(labels_path=str(labdir),images=len(imgs),label_files=len(labels),valid_boxes=nboxes,empty_labels=empty,
                          malformed_or_out_of_bounds_rows=malformed,orphan_label_files=orphan,images_without_label=len(imgs-{f.stem for f in labels}),classes=dict(classes)))
notes=[]
for parent in ('PrecisionSkin','PrecisionSkin_Raw','own_hyperpigmentation.yolov8','Detector_v1_imgsz640_files'):
    p=base/parent
    if p.exists():
        for f in p.rglob('*'):
            if f.is_file() and re.search(r'readme|licen[cs]e|terms|consent',f.name,re.I) and f.suffix.lower() in {'.txt','.md','.json','.yaml'}:
                notes.append(dict(path=str(f),text=f.read_text(errors='replace')[:12000]))
(out/'dataset_source_inventory.json').write_text(json.dumps(dict(inventory=inventory,notebook_source_evidence=evidence,box_datasets=boxes,source_documents=notes),indent=2))
print(json.dumps(dict(inventory=inventory,evidence_records=len(evidence),box_folders=len(boxes),source_documents=len(notes)),indent=2))
