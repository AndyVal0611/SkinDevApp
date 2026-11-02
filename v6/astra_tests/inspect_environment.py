"""Read-only manifest/model inspection. ONNX receives synthetic black tensors only."""
from pathlib import Path
import os,json,hashlib,sys,importlib,subprocess
import numpy as np,pandas as pd,onnxruntime as ort
root=Path(__file__).resolve().parents[1];project=Path.home()/'PrecisionSkinV1'
def sha(p):return hashlib.file_digest(p.open('rb'),'sha256').hexdigest()
files=[p for d in [project/'v6/annotations',project/'v6/splits',project/'runs/facial_only/models/final'] for p in d.rglob('*') if p.is_file()]
before={str(p):sha(p) for p in files}
s=pd.read_csv(project/'v6/splits/v6_split_manifest.csv');a=pd.read_csv(project/'v6/annotations/multilabel_annotations.csv').fillna('');r=pd.read_csv(project/'v6/annotations/region_annotations.csv').fillna('')
report=dict(synthetic_detector_inputs_only=True,python=sys.version,executable=sys.executable,
    frozen_rows=len(s),category_counts=s.src_label.value_counts().to_dict(),partition_categories=pd.crosstab(s.src_label,s.split).to_dict(),
    annotation_rows=len(a),annotation_status=a.review_status.value_counts().to_dict(),region_rows=len(r),imports={},detectors=[])
for mod in ['numpy','pandas','scipy','sklearn','matplotlib','PIL','cv2','tensorflow','keras','onnxruntime','onnx','tf2onnx','IPython','nbformat']:
    try:m=importlib.import_module(mod);report['imports'][mod]=dict(status='PASS',version=getattr(m,'__version__','available'))
    except Exception as e:report['imports'][mod]=dict(status='FAIL',error=str(e))
state={'present':1,'1':1,'yes':1,'true':1,'absent':0,'0':0,'no':0,'false':0}
y=np.array([[state.get(str(row[c]).strip().lower(),-1) for c in ['acne','hyperpigmentation','eczema']] for row in a.to_dict('records')])
accepted=a.review_status.isin(['verified','partial']) & a.reviewer_role.isin(['researcher','dermatologist'])
report['accepted_fully_decided']=int((accepted & (y>=0).all(1)).sum())
prov=pd.read_csv(project/'v6/annotations/dataset_provenance.csv').fillna('')
report['source_records']=len(prov);report['source_use_counts']=prov.permissible_use.value_counts().to_dict()
for p in sorted((root.parent/'SkinDevApp/Model').glob('lesion_detector_*.onnx')):
    item=dict(name=p.name,sha256=sha(p))
    try:
        opt=ort.SessionOptions();opt.intra_op_num_threads=1;opt.inter_op_num_threads=1
        ss=ort.InferenceSession(str(p),sess_options=opt,providers=['CPUExecutionProvider']);i=ss.get_inputs()[0]
        out=ss.run(None,{i.name:np.zeros(i.shape,np.float32)})
        meta=p.with_name(p.stem.split('_m')[0]+'.json')
        item.update(input_name=i.name,input_shape=i.shape,outputs=[dict(name=v.name,shape=v.shape) for v in ss.get_outputs()],
            synthetic_inference_finite=all(np.isfinite(o).all() for o in out),metadata=json.loads(meta.read_text()) if meta.exists() else None)
        del ss
    except Exception as e:item['error']=repr(e)
    report['detectors'].append(item)
report['nvidia_smi']=subprocess.run(['nvidia-smi','--query-gpu=name,memory.total,memory.used','--format=csv,noheader'],capture_output=True,text=True).stdout.strip()
report['original_wsl_files_before']=before
report['original_wsl_files_unchanged']=before=={str(p):sha(p) for p in files}
(root/'astra_tests/environment_inventory.json').write_text(json.dumps(report,indent=2))
print(json.dumps({k:v for k,v in report.items() if k not in ['detectors','original_wsl_files_before']},indent=2))
