"""Load the original V5 read-only and exercise supported reuse with synthetic pixels."""
import test_contracts as h
import ast,json,numpy as np,keras
ns=h.ns
for c in h.nb['cells']:
    if c['cell_type']!='code':continue
    for n in ast.parse(''.join(c['source'])).body:
        if isinstance(n,ast.FunctionDef) and n.name in {'find_backbone','build_global','embedding_model','set_trainable'}:
            exec(compile(ast.Module(body=[n],type_ignores=[]),'<actual Astra definition>','exec'),ns)
path=ns['CFG'].V5_KERAS;before=ns['sha256_file'](path)
report=dict(synthetic_only=True,original_model_path=path)
try:
    m,b=ns['build_global']('softmax4','v5_full');fm=ns['embedding_model'](m)
    x=np.zeros((1,224,224,3),np.float32);emb,p=fm(x,training=False)
    assert emb.shape==(1,128) and p.shape==(1,4) and np.isfinite(p.numpy()).all()
    ns['set_trainable'](m,b,'A',ns['CFG'].FINE_TUNE_AT)
    m.compile(optimizer=keras.optimizers.Adam(1e-4),loss='categorical_crossentropy',jit_compile=False)
    old=[v.numpy().copy() for v in m.trainable_variables];m.train_on_batch(x,np.array([[1,0,0,0]],np.float32))
    assert any(not np.array_equal(a,v.numpy()) for a,v in zip(old,m.trainable_variables))
    report.update(status='PASS',embedding_shape=list(emb.shape),output_shape=list(p.shape),synthetic_head_update=True)
except Exception as ex:report.update(status='FAIL',error=repr(ex))
report['original_unchanged']=before==ns['sha256_file'](path)
(h.root/'astra_tests/v5_initialization_results.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
raise SystemExit(report['status']!='PASS' or not report['original_unchanged'])
