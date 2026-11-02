"""Regression: export the selected phase's weights, not always phase B."""
import ast,pathlib,tempfile,os,unittest,json
source=(pathlib.Path(__file__).resolve().parents[1]/'notebook_src/07_roi_branch.cellsrc').read_text()
code=source[source.index('def restore_selected_roi_phase'):source.index('def run_roi_phase')]
def require(ok,msg,**kwargs):
    if not ok:raise ValueError(msg)
ns={'os':os,'require':require};exec(code,ns)
class Tests(unittest.TestCase):
    def test_selected_file(self):
        class Model:
            def load_weights(self,path):self.loaded=path
        with tempfile.TemporaryDirectory() as d:
            for p in ('A','B'):pathlib.Path(d,f'phase{p}_best.weights.h5').touch()
            for a,b,expected in ((.8,.6,'A'),(.6,.8,'B'),(.7,.7,'B')):
                m=Model();self.assertEqual(ns['restore_selected_roi_phase'](m,d,a,b),expected)
                self.assertEqual(pathlib.Path(m.loaded).name,f'phase{expected}_best.weights.h5')
    def test_missing_selected_checkpoint_refused(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaises(ValueError):ns['restore_selected_roi_phase'](object(),d,.8,.6)
if __name__=='__main__':unittest.main(verbosity=2)
