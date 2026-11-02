"""Gated notebook setup plus existing tests, scratch-only, training/test-open prohibited.

Run under an external timeout. No final test evaluation or model fitting is allowed.
"""
import os, tempfile, json, pathlib, shutil, traceback, ast
root=pathlib.Path(__file__).resolve().parents[1]
scratch=tempfile.mkdtemp(prefix='lumyvue_unit_')
os.environ.update(LUMYVUE_V6_WORKSPACE=scratch, LUMYVUE_HEADLESS='1',
                  LUMYVUE_RUN_ID='bounded_unit', LUMYVUE_OFFLINE_MODE='true')
nb=json.loads((root/'LUMYVUE_V6_Hybrid_Three_YOLO_MobileNetV2_Training.ipynb').read_text())
ns={'__name__':'__main__'}
# Performance galleries, baseline dataset predictions and previews are unrelated
# to unit tests. Keep their function definitions/imports without executing them.
definitions_only={33,34,38,39,41,43,44,45,47,49,63,64,66,67,68,69,72,119,120,121,123}
def prohibited(*args,**kwargs):
    raise RuntimeError('Maintenance runner prohibits training and opening test evaluation')
for i,c in enumerate(nb['cells']):
    if c['cell_type']!='code': continue
    src=''.join(c['source'])
    if i in definitions_only:
        tree=ast.parse(src)
        tree.body=[n for n in tree.body if isinstance(n,(ast.FunctionDef,ast.ClassDef,ast.Import,ast.ImportFrom))]
        src=ast.unparse(tree)
        if i in (47,64):
            import pandas as pd
            if i==47: ns['HARD']=pd.DataFrame()  # no hard mining in this unit-only run
            else:
                ns['DET']=pd.DataFrame(); ns['EVID']=pd.DataFrame()
    print('CELL',i,(src.splitlines() or ['definitions-only empty cell'])[0][:90],flush=True)
    try:
        exec(compile(src,f'<cell {i}>','exec'),ns)
    except Exception as e:
        if type(e).__name__!='GateError':
            traceback.print_exc(); raise
        print('Expected blocked stage:',str(e)[:180],flush=True)
    if 'keras' in ns:
        ns['keras'].Model.fit=prohibited
        ns['keras'].Model.train_on_batch=prohibited
    if 'open_test_once' in ns: ns['open_test_once']=prohibited
    if 'TEST_REPORT' in ns:
        target=root/'audit_results/existing_unit_tests.json'
        target.write_text(json.dumps(ns['TEST_REPORT'],indent=2))
        print('Unit report saved:',target,flush=True)
        break
else:
    raise RuntimeError('Existing unit suite was not reached')
assert not list(pathlib.Path(scratch).rglob('*test*lock*')), 'Unexpected test lock'
assert not ns.get('TEST'), 'Final test evaluation must remain unopened'
print('Scratch workspace:',scratch,flush=True)
