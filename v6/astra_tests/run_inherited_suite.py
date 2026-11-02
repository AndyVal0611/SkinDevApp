"""Run retained V6 logic tests against synthetic fixtures only; no notebook stage execution."""
import test_contracts as h
import ast,sys,types,unittest,json,pathlib
module=types.ModuleType('astra_isolated_definitions');sys.modules[module.__name__]=module
ns=module.__dict__;ns.update(h.ns);ns['__name__']=module.__name__
exec('import os, io, re, json, math, time, random, shutil, hashlib, hmac, datetime, itertools, collections, warnings, secrets, glob, tempfile, copy\nfrom dataclasses import dataclass,field,asdict\nfrom types import SimpleNamespace\nimport cv2,onnxruntime as ort,unittest\nfrom PIL import Image\nfrom scipy.optimize import minimize\n',ns)
ns.update(DF=h.pd.DataFrame(dict(image_id=['synthetic_1','synthetic_2'],group_id=[0,1],split=['train','validation'])),
    HOME=str(pathlib.Path.home()),_PKEY=b'SYNTHETIC_KEY_NOT_RESEARCH',FACE_ONNX='',_FACE={'det':None,'kind':None},
    BUNDLE=None,TEST={},CALIB={},GLOBAL_SELECTED=(None,None),ROI_SELECTED_SEED=None,IMG_PX=224,ROI_S=224,SEALED_PATH=str(h.scratch/'sealed.jsonl'))
cfg=ns['CFG'];cfg.EXP_DIR=str(h.scratch)
cfg.DIRS={k:str(h.scratch/k) for k in ['tests','logs','export','metrics','audit']}
ns['AUDIT_PATH']=str(h.scratch/'logs/audit.jsonl')
constants={'STATES','REVIEW_STATUS_PENDING','REVIEW_STATUSES','CLINICAL_STATUS','SCHEMA_VERSION','REVIEW_SCHEMA_VERSION','AI_VERIFICATION_STATEMENT',
    'FINDING_VALUES','REVIEW_RECORD_STATUSES','REVIEW_COLS','FINAL_ASSESSMENT_JSON_SCHEMA','DERMATOLOGIST_REVIEW_JSON_SCHEMA','REVIEW_CONTRACT',
    'COND_COLS','STATE_MAP','STATUSES','ROLES','ANN_COLS','REG_COLS','REG_LABELS','ROI_S','YOLO_FEATURE_NAMES','ROI_FEAT_DIM','_AI_FIELDS','AI_CODE','DERM_CODE','BLOCKING_QUALITY','ADVICE'}
for c in h.nb['cells']:
    if c['cell_type']!='code':continue
    # All setup definitions already available; do not re-execute config or setup.
    source=c.get('metadata',{}).get('source_file','')
    for node in ast.parse(''.join(c['source'])).body:
        allowed=isinstance(node,(ast.FunctionDef,ast.ClassDef))
        if isinstance(node,ast.Assign):allowed=any(isinstance(t,ast.Name) and t.id in constants for t in node.targets)
        if allowed:
            exec(compile(ast.Module(body=[node],type_ignores=[]),f'<Astra {source} definition>','exec'),ns)
result=unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(ns['TestV6']))
report=dict(synthetic_fixtures_only=True,run=result.testsRun,passed=result.testsRun-len(result.failures)-len(result.errors)-len(result.skipped),
    failures=[(str(t),s) for t,s in result.failures],errors=[(str(t),s) for t,s in result.errors],skipped=[(str(t),s) for t,s in result.skipped])
(h.root/'astra_tests/inherited_results.json').write_text(json.dumps(report,indent=2))
raise SystemExit(not result.wasSuccessful())
