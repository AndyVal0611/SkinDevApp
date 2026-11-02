"""No notebook execution: validate structure, source order, guards and integrity."""
from pathlib import Path
import ast,json,hashlib,unittest,nbformat,re
root=Path(__file__).resolve().parents[1];repo=root.parent
nb=nbformat.read(root/'LUMYVUE_V6_Complete_Training_Pipeline_Astra.ipynb',as_version=4)
def sha(p):
    with p.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
class Checks(unittest.TestCase):
    def test_notebook_schema(self):nbformat.validate(nb)
    def test_all_code_syntax(self):
        for c in nb.cells:
            if c.cell_type=='code':ast.parse(c.source)
    def test_no_saved_research_outputs(self):
        for c in nb.cells:
            if c.cell_type=='code':self.assertIsNone(c.execution_count);self.assertEqual(c.outputs,[])
    def test_cell_ids_unique(self):self.assertEqual(len({c.id for c in nb.cells}),len(nb.cells))
    def test_action_guards_present(self):
        code='\n'.join(c.source for c in nb.cells if c.cell_type=='code')
        for action in ['global_A','global_B','roi_A','roi_B','fusion_train','calibration','stage_rules','final_test','export','development_evaluation','roi_validation','yolo_evidence','baseline_development']:
            self.assertIn("astra_require_action('"+action+"')",code)
    def test_definitions_before_use(self):
        snippets=[c.source for c in nb.cells if c.cell_type=='code']
        for definition,use in [('def recovery_fit(',"astra_global_phase('A')"),('def astra_paired_stages(',"astra_require_action('final_test')"),('def astra_require_action(',"astra_require_action('global_A')"),('def astra_refresh_detector_tables(',"astra_require_action('final_test')")]:
            self.assertLess(next(i for i,s in enumerate(snippets) if definition in s),next(i for i,s in enumerate(snippets) if use in s))
    def test_maintained_sources_match_notebook(self):
        import sys;sys.path.insert(0,str(root/'astra_training_src'))
        from build_astra import parse
        for p in (root/'astra_training_src').glob('*.cellsrc'):
            expected=[body for _,body in parse(p.read_text(encoding='utf-8'))]
            actual=[c.source for c in nb.cells if c.metadata.get('source_file')==p.name]
            self.assertEqual(expected,actual,p.name)
    def test_original_files_unchanged(self):
        before=json.loads((root/'astra_docs/original_integrity_before.json').read_text())
        changed=[name for name,h in before.items() if not (repo/name).exists() or sha(repo/name)!=h]
        self.assertEqual(changed,[])
    def test_fusion_group_identity(self):
        code=(root/'astra_training_src/08_fusion.cellsrc').read_text(encoding='utf-8')
        self.assertIn('GroupKFold',code);self.assertIn('set(groups[tr]) & set(groups[te])',code);self.assertIn('groups=dict(all=groups',code)
    def test_safe_builder(self):
        code=(root/'astra_training_src/build_astra.py').read_text(encoding='utf-8')
        self.assertNotIn('model.fit(',code);self.assertNotIn('Hybrid_Three_YOLO',code)
if __name__=='__main__':
    r=unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(Checks))
    report=dict(tests=r.testsRun,passed=r.testsRun-len(r.failures)-len(r.errors),failures=[(str(t),s) for t,s in r.failures],errors=[(str(t),s) for t,s in r.errors],cells=len(nb.cells),code_cells=sum(c.cell_type=='code' for c in nb.cells))
    (root/'astra_tests/static_results.json').write_text(json.dumps(report,indent=2))
    raise SystemExit(not r.wasSuccessful())
