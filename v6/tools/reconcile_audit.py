"""One-time, non-executing reconciliation; preserve both notebook versions first."""
import json, hashlib, shutil, pathlib, datetime
root = pathlib.Path(__file__).resolve().parents[1]
working = pathlib.Path('/mnt/c/Users/Bautista/Downloads/LUMYVUE_V6_Hybrid_Three_YOLO_MobileNetV2_Training_FIXED.ipynb')
repo = root / 'LUMYVUE_V6_Hybrid_Three_YOLO_MobileNetV2_Training.ipynb'
backup = root / 'backups' / datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
backup.mkdir(parents=True, exist_ok=False)
records = []
for p in (working, repo):
    target = backup / p.name
    shutil.copy2(p, target)
    digest = lambda f: hashlib.sha256(f.read_bytes()).hexdigest()
    assert digest(p) == digest(target)
    records.append(dict(original=str(p), backup=str(target), sha256=digest(p)))
(backup / 'manifest.json').write_text(json.dumps(records, indent=2))
a, b = (json.loads(p.read_text()) for p in (repo, working))
assert len(a['cells']) == len(b['cells'])
changed = [i for i,(x,y) in enumerate(zip(a['cells'], b['cells'])) if x['source'] != y['source']]
assert changed == [82], changed
p = root / 'notebook_src/08_fusion.cellsrc'
s = p.read_text(encoding='utf-8')
old, new = (''.join(n['cells'][82]['source']).rstrip() for n in (a,b))
assert old in s
p.write_text(s.replace(old,new), encoding='utf-8')
print('Verified backups:', backup, '; reconciled source cells:', changed)
