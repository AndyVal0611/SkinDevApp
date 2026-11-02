"""Export actual Astra synthetic-trained components; no research artifacts."""
import test_contracts as h
import json,pathlib,numpy as np,keras,onnxruntime as ort
report=json.loads((h.root/'astra_tests/synthetic_training_cpu.json').read_text())
out=h.scratch/'synthetic_exports';out.mkdir()
h.ns['export_path']=lambda n:str(out/n)
rng=np.random.default_rng(123)
results=[]
for record in report['tests']:
    p=pathlib.Path(record['details']['checkpoint']);model=keras.models.load_model(p,compile=False)
    models=[(p.stem,model)]
    if p.stem=='global':models.append(('global_features',keras.Model(model.inputs,[model.get_layer('global_embedding').output,model.output])))
    if p.stem=='roi':models.append(('roi_features',keras.Model(model.inputs,[model.get_layer('roi_embedding').output,model.output])))
    for name,m in models:
        feed=[rng.normal(size=[1]+list(i.shape[1:])).astype('float32') for i in m.inputs]
        if name=='fusion':feed[-1]=np.array([[1]+[0]*11],np.float32)
        result=h.ns['to_onnx'](m,'SYNTHETIC_'+name);result['component']=name
        if result['status']=='exported':
            options=ort.SessionOptions();options.intra_op_num_threads=1;options.inter_op_num_threads=1
            session=ort.InferenceSession(result['path'],sess_options=options,providers=['CPUExecutionProvider'])
            expected=m(feed[0] if len(feed)==1 else feed,training=False)
            if not isinstance(expected,(tuple,list)):expected=[expected]
            actual=session.run(None,{i.name:f for i,f in zip(session.get_inputs(),feed)})
            diffs=[float(np.max(np.abs(np.asarray(a,dtype=np.float32)-b))) for a,b in zip(expected,actual)]
            result.update(max_abs_diff=diffs,tolerance=1e-4,parity_pass=bool(len(expected)==len(actual) and max(diffs)<=1e-4))
        results.append(result);print(json.dumps(result),flush=True)
(h.root/'astra_tests/synthetic_export_results.json').write_text(json.dumps(dict(synthetic_only=True,components=results),indent=2))
