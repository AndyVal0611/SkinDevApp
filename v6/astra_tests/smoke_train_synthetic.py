"""Bounded SYNTHETIC-ONLY training mechanics; never invokes dataset stages/gates.

Loads actual notebook builders via AST. Writes new scratch checkpoints only.
No real image, annotation, final evaluation or original model write is performed.
"""
import os,json,ast,pathlib,tempfile,hashlib,time,gc,traceback
os.environ.setdefault('TF_NUM_INTRAOP_THREADS','1')
os.environ.setdefault('TF_NUM_INTEROP_THREADS','1')
os.environ.setdefault('OMP_NUM_THREADS','1')
os.environ.setdefault('OPENBLAS_NUM_THREADS','1')
import numpy as np
import tensorflow as tf
import keras
root=pathlib.Path(__file__).resolve().parents[1]
artifact_root=root/'astra_tests/synthetic_artifacts';artifact_root.mkdir(exist_ok=True)
scratch=pathlib.Path(tempfile.mkdtemp(prefix='SYNTHETIC_',dir=artifact_root))
report={'synthetic_only':True,'scratch':str(scratch),'tests':[],'started':time.time()}
gpus=tf.config.list_physical_devices('GPU')
report['gpu_devices']=[str(x) for x in gpus]
if gpus:
    tf.config.set_logical_device_configuration(gpus[0],[tf.config.LogicalDeviceConfiguration(memory_limit=4096)])
keras.mixed_precision.set_global_policy('mixed_float16' if gpus else 'float32')
report['policy']=keras.mixed_precision.global_policy().name
nb=json.loads((root/'LUMYVUE_V6_Complete_Training_Pipeline_Astra.ipynb').read_text())
ns={'tf':tf,'keras':keras,'np':np,'os':os}
os.environ['LUMYVUE_RUN_ID']='SYNTHETIC_ONLY'
ns['ASTRA_V6_DIR']=root;ns['ASTRA_SOURCE_DIR']=str(root/'astra_training_src')
cell=next(c for c in nb['cells'] if c['cell_type']=='code' and '# 6.1 Experiment configuration' in ''.join(c['source']))
exec(''.join(cell['source']),ns)
CFG=ns['CFG']
protected=[pathlib.Path(CFG.V5_KERAS),pathlib.Path(CFG.V5_ONNX)]+list(pathlib.Path(CFG.YOLO_MODEL_DIR).glob('*.onnx'))
digests=lambda:{str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in protected if p.exists()}
before=digests()
def require(ok,message,**kw):
    if not ok: raise AssertionError(message)
ns.update(require=require,IMG_PX=224,ROI_S=224,ROI_INIT='imagenet',GLOBAL_SELECTED=(None,None))
wanted={'MaskedBCE','pretrained_backbone','find_backbone','build_head','build_global','set_trainable','build_roi_model',
        'MaskedAttentionPool','MaskFraction','ModalityDropout','build_fusion'}
for c in nb['cells']:
    if c['cell_type']!='code':continue
    for node in ast.parse(''.join(c['source'])).body:
        if isinstance(node,(ast.FunctionDef,ast.ClassDef)) and node.name in wanted:
            exec(compile(ast.Module(body=[node],type_ignores=[]),'<actual-notebook-definition>','exec'),ns)
rng=np.random.default_rng(2026)
batch=int(os.environ.get('LUMYVUE_SYNTHETIC_BATCH','1'))
assert 1<=batch<=8, 'Smoke batch must be bounded to 1..8'
report['cnn_batch_size']=batch
x=rng.uniform(-1,1,(batch,224,224,3)).astype('float32')
y=np.array([[1,0,1,1,1,0]],dtype='float32')  # third synthetic condition masked
custom={k:ns[k] for k in ('MaskedBCE','MaskedAttentionPool','MaskFraction','ModalityDropout')}
def check(name,fn):
    start=time.time()
    try:
        details=fn();report['tests'].append(dict(name=name,status='PASS',seconds=round(time.time()-start,2),details=details))
    except Exception as e:
        traceback.print_exc();report['tests'].append(dict(name=name,status='FAIL',error=repr(e)))
    print(json.dumps(report['tests'][-1]),flush=True)
    keras.backend.clear_session();gc.collect()
def exercise(model,inputs,target,loss,name,base=None):
    # Real GradientTape backward pass, independently of Keras optimizer plumbing.
    with tf.GradientTape() as tape:
        pred=model(inputs,training=True)
        value=tf.reduce_mean(loss(target,pred))+sum(model.losses)
    gradients=tape.gradient(value,model.trainable_variables)
    assert np.isfinite(float(value)) and gradients and all(g is not None and bool(tf.reduce_all(tf.math.is_finite(g))) for g in gradients)
    assert any(bool(tf.reduce_any(g!=0)) for g in gradients)
    device=pred.device
    if gpus: assert 'GPU' in device, device
    model.compile(optimizer=keras.optimizers.Adam(1e-4),loss=loss,jit_compile=False)
    initial=[v.numpy().copy() for v in model.trainable_variables]
    for head_attempts in range(1,9):
        metrics=model.train_on_batch(inputs,target,return_dict=True)
        assert all(np.isfinite(v) for v in metrics.values())
        if any(not np.array_equal(a,v.numpy()) for a,v in zip(initial,model.trainable_variables)):break
    else:raise AssertionError('Head weights did not update within 8 bounded warm-up steps')
    if base is not None:
        ns['set_trainable'](model,base,'B',CFG.FINE_TUNE_AT)
        assert any(l.trainable for l in base.layers)
        assert not any(l.trainable for l in base.layers if isinstance(l,keras.layers.BatchNormalization))
        model.compile(optimizer=keras.optimizers.Adam(1e-5),loss=loss,jit_compile=False)
        trainables=base.trainable_variables
        old=[v.numpy().copy() for v in trainables]
        # LossScaleOptimizer may intentionally skip early steps while reducing
        # its dynamic scale after partial unfreezing. Bound warm-up retries.
        fine_tune_attempts=0
        for fine_tune_attempts in range(1,9):
            result=model.train_on_batch(inputs,target,return_dict=True)
            assert all(np.isfinite(v) for v in result.values())
            if any(not np.array_equal(a,v.numpy()) for a,v in zip(old,trainables)):break
        else:raise AssertionError('Backbone weights did not update within 8 bounded loss-scale warm-up steps')
    pred_before=model(inputs,training=False).numpy()
    optimizer_before=[v.numpy().copy() for v in model.optimizer.variables]
    iteration=int(model.optimizer.iterations.numpy())
    checkpoint=scratch/(name+'.keras');model.save(checkpoint)
    weights=scratch/(name+'.weights.h5');model.save_weights(weights)
    restored=keras.models.load_model(checkpoint,custom_objects=custom)
    np.testing.assert_allclose(pred_before,restored(inputs,training=False).numpy(),rtol=1e-4,atol=1e-5)
    assert int(restored.optimizer.iterations.numpy())==iteration
    assert len(optimizer_before)==len(restored.optimizer.variables)
    for a,v in zip(optimizer_before,restored.optimizer.variables):np.testing.assert_array_equal(a,v.numpy())
    restored.train_on_batch(inputs,target)
    assert int(restored.optimizer.iterations.numpy())==iteration+1
    restored.load_weights(weights)
    np.testing.assert_allclose(pred_before,restored(inputs,training=False).numpy(),rtol=1e-4,atol=1e-5)
    return dict(loss=float(value),output_shape=list(pred.shape),device=device,checkpoint=str(checkpoint),optimizer_iteration_restored=iteration,continued_update=True,weights_restore=True,fine_tune_attempts=(fine_tune_attempts if base is not None else None))
def global_test():
    m,b=ns['build_global']('multilabel3','imagenet')
    return exercise(m,x,np.repeat(y,batch,axis=0),ns['MaskedBCE'](),'global',b)
def roi_test():
    m,b=ns['build_roi_model']()
    return exercise(m,x,np.full(batch,2),keras.losses.SparseCategoricalCrossentropy(),'roi',b)
def fusion_test():
    dims=dict(g_emb=CFG.HEAD_UNITS,det_ev=21,roi_feat=(CFG.ROI_MAX_TOTAL,CFG.ROI_EMB+4+15))
    m=ns['build_fusion'](dims)
    feed=[rng.normal(size=(2,CFG.HEAD_UNITS)).astype('float32'),rng.normal(size=(2,3)).astype('float32'),np.zeros((2,2),'float32'),np.ones((2,21),'float32'),rng.normal(size=(2,CFG.ROI_MAX_TOTAL,83)).astype('float32'),np.array([[0]*12,[1]+[0]*11],'float32')]
    target=np.repeat(y,2,axis=0)
    result=exercise(m,feed,target,ns['MaskedBCE'](),'fusion')
    assert np.isfinite(m(feed,training=False).numpy()).all()
    result['full_size_roi_input']=[12,83];return result
try:
    check('actual_global_MobileNetV2_forward_backward_optimizer_phaseA_phaseB_resume',global_test)
    check('actual_ROI_MobileNetV2_forward_backward_optimizer_phaseA_phaseB_resume',roi_test)
    check('actual_fusion_forward_backward_optimizer_masking_resume',fusion_test)
    if gpus: report['gpu_memory']=tf.config.experimental.get_memory_info('GPU:0')
finally:
    report['original_models_unchanged']=before==digests()
    report['passed']=sum(t['status']=='PASS' for t in report['tests']);report['failed']=sum(t['status']=='FAIL' for t in report['tests'])
    report['elapsed_seconds']=round(time.time()-report['started'],2)
    report['tensorflow']=tf.__version__;report['keras']=keras.__version__
    (root/'astra_tests'/('synthetic_training_'+('gpu' if gpus else 'cpu')+'.json')).write_text(json.dumps(report,indent=2))
    print(json.dumps(report,indent=2),flush=True)
raise SystemExit(bool(report['failed']) or not report['original_models_unchanged'])
