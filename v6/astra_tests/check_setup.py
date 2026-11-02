"""Execute actual Astra setup cells only, output redirected to synthetic test workspace."""
from pathlib import Path
import os,json,tempfile,traceback
root=Path(__file__).resolve().parents[1]
scratch=Path(tempfile.mkdtemp(prefix='astra_SYNTHETIC_SETUP_'))
os.environ['LUMYVUE_RUN_ID']='SYNTHETIC_SETUP';os.environ['LUMYVUE_HEADLESS']='1'
nb=json.loads((root/'LUMYVUE_V6_Complete_Training_Pipeline_Astra.ipynb').read_text())
ns={'__name__':'__main__'};done=[];report=dict(synthetic_only=True,scratch=str(scratch))
try:
    for i,c in enumerate(nb['cells']):
        source=c.get('metadata',{}).get('source_file','bootstrap')
        if source.startswith('03_'):break
        if c['cell_type']!='code':continue
        code=''.join(c['source'])
        print('SETUP CELL',i+1,code.splitlines()[0],flush=True)
        exec(compile(code,f'<actual setup cell {i+1}>','exec'),ns)
        if 'ASTRA_V6_DIR' in ns and source=='bootstrap':
            ns['ASTRA_V6_DIR']=scratch/'v6';ns['ASTRA_V6_DIR'].mkdir()
        done.append(i+1)
    report.update(status='PASS',cells=done,gpu=[str(x) for x in ns['GPUS']],run_contract=ns['_astra_digest'])
except Exception:
    report.update(status='FAIL',cells=done,error=traceback.format_exc());traceback.print_exc()
(root/'astra_tests/setup_results.json').write_text(json.dumps(report,indent=2))
raise SystemExit(report['status']!='PASS')
