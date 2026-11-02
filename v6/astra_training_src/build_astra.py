"""Deterministic standalone notebook assembly; never executes notebook cells."""
from pathlib import Path
import ast, hashlib, json, re, textwrap
HERE=Path(__file__).resolve().parent
V6=HERE.parent

def parse(text):
    cells=[]
    for part in text.split('#@@ ')[1:]:
        kind,body=part.split('\n',1)
        if body.strip():cells.append((kind.strip(),body.strip()))
    return cells

def assemble():
    import nbformat
    nb=nbformat.v4.new_notebook()
    intro='''# LUMYVUE V6 Astra — Complete Independent Training Pipeline
This notebook is derived from the preserved Codex V6 sources. No research training has been executed by its author.
Read `astra_docs/LUMYVUE_V6_ASTRA_MANUAL_JUPYTER_GUIDE.md` first. **Do not Run All.**
Original models, annotations and frozen splits are read-only. Every new artifact is isolated in `astra_runs/<RUN_ID>`.
Normal is a reviewed all-negative reference and a supported no-target-condition outcome, never ROI Background.
Historical evaluation exposure remains unresolved; no patient-independent generalization is claimed.
**Implementation correction to earlier descriptions:** Stage 1 decisions use global + detector fusion; the full ROI fusion is evaluated in Stage 2 with up to three evidence views. This preserves the actual source architecture. The single-image reference currently precomputes ROI crops; evidence integration is staged, not newly captured imagery.
'''
    nb.cells.append(nbformat.v4.new_markdown_cell(intro))
    nb.cells.append(nbformat.v4.new_code_cell('''# Astra bootstrap — paths only; no training or imports of TensorFlow
from pathlib import Path
import os, re
_cwd = Path.cwd().resolve()
_home = Path.home()
_candidates = ([Path(os.environ['LUMYVUE_V6_DIR'])] if os.environ.get('LUMYVUE_V6_DIR') else []) + [_cwd, _cwd/'v6', *[q/'v6' for q in _cwd.parents]]
_candidates += sorted(Path('/mnt/c/Users').glob('*/source/repos/SkinDevApp/v6')) + [_home/'SkinDevApp'/'v6', _home/'PrecisionSkinV1'/'SkinDevApp'/'v6']
ASTRA_V6_DIR = next((p for p in _candidates if (p/'astra_training_src').is_dir()), None)
if ASTRA_V6_DIR is None: raise RuntimeError('astra_training_src not found. Open the notebook from SkinDevApp/v6 or set LUMYVUE_V6_DIR to that folder. Looked in: ' + ', '.join(str(q) for q in _candidates[:8]))
print('Astra v6 folder:', ASTRA_V6_DIR)
ASTRA_SOURCE_DIR = str(ASTRA_V6_DIR/'astra_training_src')
ASTRA_ACTION = None
ASTRA_EVALUATION_NOTE = ''  # researcher/adviser decision: limited historical cohort or approved independent cohort
# Set os.environ['LUMYVUE_RUN_ID'] to a unique name before the configuration cell.
# Defaults so the configuration cell does not stop; change RUN_ID for each new experiment (resume/checkpoints are tied to it).
os.environ.setdefault('LUMYVUE_RUN_ID', 'astra_run01')
_first = ASTRA_V6_DIR / 'v6_open_training_config.json'  # batch 8, 4096 MiB GPU cap, one seed, researcher-label validation (EVAL_REQUIRES_DERMATOLOGIST=false); source-permission and action-token gates stay on
if not os.environ.get('LUMYVUE_V6_CONFIG') and _first.is_file(): os.environ['LUMYVUE_V6_CONFIG'] = str(_first)
print('RUN_ID:', os.environ['LUMYVUE_RUN_ID'], '| config:', os.environ.get('LUMYVUE_V6_CONFIG', '(defaults)'))
'''))
    supplement=HERE/'astra_evaluation.cellpy'
    def add_supplement():
        for kind,body in parse(supplement.read_text(encoding='utf-8')):
            c=nbformat.v4.new_code_cell(body) if kind=='code' else nbformat.v4.new_markdown_cell(body)
            c.metadata['source_file']=supplement.name
            if kind=='code':ast.parse(body)
            nb.cells.append(c)
    for path in sorted(HERE.glob('*.cellsrc')):
        for kind,body in parse(path.read_text(encoding='utf-8')):
            c=nbformat.v4.new_code_cell(body) if kind=='code' else nbformat.v4.new_markdown_cell(body)
            c.metadata['source_file']=path.name
            if kind=='code': ast.parse(body)
            nb.cells.append(c)
            if path.name=='10_evaluation.cellsrc' and kind=='code' and 'def combo_label(' in body and supplement.exists():add_supplement()
    for i,c in enumerate(nb.cells):c['id']=hashlib.sha256(f'{i}:{c.source}'.encode()).hexdigest()[:12]
    nb.metadata.update(kernelspec=dict(display_name='Python (LUMYVUE WSL)',language='python',name='python3'),language_info=dict(name='python'),lumyvue=dict(variant='Astra',research_training_executed=False))
    nbformat.validate(nb)
    out=V6/'LUMYVUE_V6_Complete_Training_Pipeline_Astra.ipynb'
    nbformat.write(nb,out)
    index=[dict(cell=i+1,type=c.cell_type,source=c.metadata.get('source_file','bootstrap'),heading=c.source.splitlines()[0],action=(re.search(r"astra_require_action\(['\"]([^'\"]+)",c.source).group(1) if c.cell_type=='code' and re.search(r"astra_require_action\(['\"]([^'\"]+)",c.source) else None)) for i,c in enumerate(nb.cells)]
    (V6/'astra_docs/cell_index.json').write_text(json.dumps(index,indent=2))
    print(f'Built {len(nb.cells)} cells -> {out}')

if __name__=='__main__':
    assemble()
