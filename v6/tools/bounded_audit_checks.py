"""No training or final evaluation. Synthetic policy/attention tests and metadata audit."""
import ast, json, pathlib, unittest, hashlib, csv, collections, os
import numpy as np
try:
    import tensorflow as tf
    import keras
except ImportError:
    tf = keras = None
from source_policy import source_issues, REQUIRED
root = pathlib.Path(__file__).resolve().parents[1]
nb = json.loads((root / 'LUMYVUE_V6_Hybrid_Three_YOLO_MobileNetV2_Training.ipynb').read_text())
ns = dict(tf=tf, keras=keras)
for c in nb['cells']:
    if c['cell_type'] == 'code':
        tree = ast.parse(''.join(c['source']))
        for node in tree.body:
            if tf is not None and isinstance(node, ast.ClassDef) and node.name == 'MaskedAttentionPool':
                exec(compile(ast.Module(body=[node], type_ignores=[]), '<attention>', 'exec'), ns)
class Checks(unittest.TestCase):
    def row(self, use='research_training'):
        return dict(source_key='Acne/aug', permissible_use=use, contains_identifiable_faces='yes', **{f:'human evidence fixture' for f in REQUIRED})
    def test_missing_and_duplicates(self):
        for rows in ([], [self.row(),self.row()]):
            self.assertTrue(source_issues(rows,['Acne/aug'],'training'))
    def test_each_missing_evidence_field(self):
        for field in REQUIRED + ('contains_identifiable_faces', 'permissible_use'):
            row = self.row(); row[field]=''
            self.assertTrue(source_issues([row],['Acne/aug'],'evaluation'))
    def test_permission_matrix(self):
        for use in ('', 'not_permitted', 'invented', 'evaluation_only', 'research_training'):
            for purpose in ('training','evaluation'):
                allowed = use == 'research_training' or (use == 'evaluation_only' and purpose == 'evaluation')
                self.assertEqual(not source_issues([self.row(use)],['Acne/aug'],purpose), allowed)
    @unittest.skipIf(tf is None, 'TensorFlow unavailable in this interpreter')
    def test_attention_mixed_precision(self):
        old=keras.mixed_precision.global_policy()
        try:
            keras.mixed_precision.set_global_policy('mixed_float16')
            layer=ns['MaskedAttentionPool']()
            h=tf.ones((2,4,8)); mask=tf.constant([[False]*4,[True,False,False,False]])
            out=layer([h,mask]).numpy()
            self.assertTrue(np.isfinite(out).all()); self.assertTrue(np.all(out[0] == 0))
            h2=tf.concat([h[:,:1],tf.ones((2,3,8))*1000],axis=1)
            np.testing.assert_allclose(out,layer([h2,mask]).numpy(), atol=1e-3)
        finally: keras.mixed_precision.set_global_policy(old)
    def test_all_code_syntax(self):
        for c in nb['cells']:
            if c['cell_type']=='code': ast.parse(''.join(c['source']))
    def test_boundaries_present(self):
        text='\n'.join(''.join(c['source']) for c in nb['cells'])
        self.assertIn('require_source_permission(["fusion"], "training")',text)
        self.assertIn('require_source_permission(["test"], "evaluation")',text)
        self.assertIn('def require_source_permission',text)
    def test_runtime_reloads_permission_and_gate_blocks(self):
        import pandas as pd
        from types import SimpleNamespace
        from unittest.mock import patch
        class Blocked(Exception): pass
        env=dict(pd=pd, CFG=SimpleNamespace(PROVENANCE_CSV='fixture'),
                 DF=pd.DataFrame([dict(split='fusion',source_key='Acne/aug')]),
                 GateError=Blocked, source_issues=source_issues,
                 GATES={'annotations_fusion':dict(ok=True,why='')})
        for c in nb['cells']:
            if c['cell_type']!='code': continue
            for node in ast.parse(''.join(c['source'])).body:
                if isinstance(node,ast.FunctionDef) and node.name in ('gate','require_source_permission'):
                    exec(compile(ast.Module(body=[node],type_ignores=[]),'<boundary>', 'exec'),env)
        with patch.object(pd,'read_csv',return_value=pd.DataFrame([self.row('evaluation_only')])):
            with self.assertRaises(Blocked): env['gate']('annotations_fusion')
        with patch.object(pd,'read_csv',return_value=pd.DataFrame([self.row()])):
            self.assertTrue(env['gate']('annotations_fusion'))

if __name__=='__main__':
    result=unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(Checks))
    out=root/'audit_results'; out.mkdir(exist_ok=True)
    (out/'bounded_tests.json').write_text(json.dumps(dict(run=result.testsRun,failures=len(result.failures),errors=len(result.errors),skipped=len(result.skipped)),indent=2))
    raise SystemExit(not result.wasSuccessful())
