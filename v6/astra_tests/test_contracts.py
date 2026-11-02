"""Bounded synthetic tests of actual AST-extracted Astra definitions. No dataset stages."""
import os,ast,json,pathlib,tempfile,hashlib,glob,re,shutil,unittest,time
from types import SimpleNamespace
import numpy as np,pandas as pd
import tensorflow as tf,keras
root=pathlib.Path(__file__).resolve().parents[1]
scratch=pathlib.Path(tempfile.mkdtemp(prefix='astra_SYNTHETIC_contracts_'))
gpu=tf.config.list_physical_devices('GPU')
if gpu:tf.config.set_logical_device_configuration(gpu[0],[tf.config.LogicalDeviceConfiguration(memory_limit=4096)])
tf.config.threading.set_inter_op_parallelism_threads(1);tf.config.threading.set_intra_op_parallelism_threads(1)
nb=json.loads((root/'LUMYVUE_V6_Complete_Training_Pipeline_Astra.ipynb').read_text())
ns=dict(np=np,pd=pd,tf=tf,keras=keras,os=os,hashlib=hashlib,json=json,glob=glob,re=re,shutil=shutil,time=time)
os.environ['LUMYVUE_RUN_ID']='SYNTHETIC_ONLY'
ns.update(ASTRA_V6_DIR=root,ASTRA_SOURCE_DIR=str(root/'astra_training_src'))
exec(''.join(next(c for c in nb['cells'] if c['cell_type']=='code' and '# 6.1 Experiment configuration' in ''.join(c['source']))['source']),ns)
cfg=ns['CFG'];cfg.EXP_DIR=str(scratch);cfg.EARLY_STOP_PATIENCE=3;cfg.LR_PATIENCE=2
class Failure(RuntimeError):pass
def require(ok,msg,**kw):
    if not ok:raise Failure(msg)
ns.update(require=require,V6Error=Failure,GateError=Failure,log_event=lambda *a,**kw:None,_norm=lambda p:os.path.realpath(os.path.abspath(p)),
    _protected_roots=lambda:[],write_json=lambda p,d:pathlib.Path(ns['safe_path'](p)).write_text(json.dumps(d,default=str)))
names={'content_digest','atomic_record','recovery_fit','save_selected_model','astra_select_phase','safe_path','is_protected','pack_ml','MaskedBCE',
       'MaskedAttentionPool','MaskFraction','ModalityDropout','build_fusion','sha256_file','astra_normal_metrics','metrics_dec','set_metrics_dec','source_issues','astra_require_action','to_onnx',
       'crossfit_fusion','split_groups','fusion_pos_weights','concat_views','subset','fusion_dims','train_one_fusion','decide'}
# Supplement normal function lives in the authoring fragments and will be inlined too.
for src in [''.join(c['source']) for c in nb['cells'] if c['cell_type']=='code']:
    for node in ast.parse(src).body:
        if isinstance(node,(ast.FunctionDef,ast.ClassDef)) and node.name in names:
            exec(compile(ast.Module(body=[node],type_ignores=[]),'<Astra actual definition>','exec'),ns)
ns['REQUIRED']=('description','license_or_terms','consent_basis','retention_policy','responsible_person')
from sklearn.metrics import average_precision_score,roc_auc_score
ns.update(average_precision_score=average_precision_score,roc_auc_score=roc_auc_score)
def tiny():
    keras.backend.clear_session();keras.utils.set_random_seed(17)
    x=keras.Input((2,),name='input');y=keras.layers.Dense(1,name='output')(x)
    m=keras.Model(x,y,name='tiny');m.compile(optimizer=keras.optimizers.Adam(.01),loss='mse',jit_compile=False)
    return m
X=np.array([[0.,1.],[1.,0.],[1.,1.],[0.,0.]],np.float32);Y=np.array([[1.],[1.],[0.],[0.]],np.float32)
def dataset():
    ds=tf.data.Dataset.from_tensor_slices((X,Y)).batch(2)
    opt=tf.data.Options();opt.threading.private_threadpool_size=1;return ds.with_options(opt)
def fit(name,model=None,**kw):
    return ns['recovery_fit'](model or tiny(),dataset(),dataset(),str(scratch/name),dict(synthetic=True,revision=1),3,verbose=0,**kw)
class Interrupt(keras.callbacks.Callback):
    def on_epoch_begin(self,epoch,logs=None):
        if epoch==1:raise InterruptedError('synthetic interruption after committed epoch')
class Tests(unittest.TestCase):
    def test_target_mask_unknown_and_normal(self):
        y=np.array([[0,0,0],[1,0,-1]],np.float32);p=ns['pack_ml'](y,y>=0)
        np.testing.assert_array_equal(p,[[0,0,0,1,1,1],[1,0,0,1,1,0]])
        loss=ns['MaskedBCE']();z=np.zeros((2,3),np.float32);z[1,2]=100
        self.assertAlmostEqual(float(loss(p,z)),float(loss(p,np.zeros((2,3)))),places=6)
    def test_masks_empty_partial_full(self):
        keras.mixed_precision.set_global_policy('mixed_float16')
        try:
            layer=ns['MaskedAttentionPool'](3);h=np.arange(24,dtype=np.float32).reshape(1,4,6)/24
            for mask in [np.zeros((1,4)),np.array([[1,0,0,0]]),np.ones((1,4))]:
                out=layer([h,mask]).numpy();self.assertTrue(np.isfinite(out).all())
                if mask.sum()==0:np.testing.assert_array_equal(out,0)
                if mask.sum()==1:
                    changed=h.copy();changed[:,1:]=500
                    np.testing.assert_allclose(out,layer([changed,mask]).numpy(),atol=1e-6)
        finally:keras.mixed_precision.set_global_policy('float32')
    def test_fusion_branches_and_padding(self):
        keras.utils.set_random_seed(81)
        m=ns['build_fusion'](dict(g_emb=128,det_ev=21,roi_feat=(12,83)))
        rng=np.random.default_rng(2);f=[rng.normal(size=(2,n)).astype('float32') for n in [128,3,2,21]]+[rng.normal(size=(2,12,83)).astype('float32'),np.array([[1]+[0]*11]*2,dtype='float32')]
        base=m(f,training=False).numpy()
        for idx in [0,3,4]:
            ff=[a.copy() for a in f];ff[idx]+=1
            self.assertGreater(np.max(np.abs(m(ff,training=False).numpy()-base)),1e-6)
        ff=[a.copy() for a in f];ff[4][:,1:]=100
        np.testing.assert_allclose(m(ff,training=False).numpy(),base,atol=1e-6)
        ff[5][:]=0;self.assertTrue(np.isfinite(m(ff,training=False).numpy()).all())
    def test_completed_fold_reuse(self):
        model,rec=fit('reuse',restart_incomplete=True)
        again,state=fit('reuse',restart_incomplete=True)
        self.assertTrue(state['reused']);self.assertEqual(rec['attempt'],state['attempt'])
        np.testing.assert_allclose(model(X).numpy(),again(X).numpy())
    def test_epoch_resume_optimizer(self):
        with self.assertRaises(InterruptedError):fit('resume',metric_callbacks=[Interrupt()])
        state=json.loads((scratch/'resume/attempt_0001/state.json').read_text());self.assertEqual(state['epoch'],1)
        before=keras.models.load_model(scratch/'resume/attempt_0001'/state['latest'])
        self.assertEqual(int(before.optimizer.iterations),2)
        model,rec=fit('resume');self.assertEqual(rec['epoch'],3)
        latest=keras.models.load_model(pathlib.Path(rec['attempt'])/rec['latest'])
        self.assertEqual(int(latest.optimizer.iterations),6)
        self.assertEqual(len(pd.read_csv(pathlib.Path(rec['attempt'])/rec['history'])),3)
    def test_incomplete_fold_restart(self):
        with self.assertRaises(InterruptedError):fit('restart',metric_callbacks=[Interrupt()])
        _,rec=fit('restart',restart_incomplete=True)
        self.assertTrue(rec['attempt'].endswith('attempt_0002'));self.assertEqual(rec['epoch'],3)
    def test_uncommitted_fold_restart(self):
        m=tiny();d=scratch/'empty_attempt'
        # interrupt at first epoch, leaving a contract + empty attempt
        class First(keras.callbacks.Callback):
            def on_epoch_begin(self,e,logs=None):raise InterruptedError()
        with self.assertRaises(InterruptedError):fit('empty_attempt',m,metric_callbacks=[First()])
        _,rec=fit('empty_attempt',restart_incomplete=True)
        self.assertTrue(rec['attempt'].endswith('attempt_0002'))
    def test_config_mismatch(self):
        fit('mismatch')
        with self.assertRaisesRegex(Failure,'Incompatible'):ns['recovery_fit'](tiny(),dataset(),dataset(),str(scratch/'mismatch'),dict(synthetic=True,revision=2),3,verbose=0)
    def test_corrupt_checkpoint(self):
        _,rec=fit('corrupt');p=pathlib.Path(rec['attempt'])/rec['selected'];p.write_bytes(b'corrupted synthetic checkpoint')
        with self.assertRaisesRegex(Failure,'integrity'):fit('corrupt')
    def selection(self,name,a,b,expected,missing=False):
        d=scratch/name;d.mkdir();m=tiny()
        for phase,metric in [('A',a),('B',b)]:
            ad=d/phase;ad.mkdir();m.layers[-1].bias.assign([1 if phase=='A' else 2]);m.save(ad/'selected.keras')
            rec=dict(attempt=str(ad),selected='selected.keras',best_metric=metric,hashes={'selected.keras':ns['sha256_file'](ad/'selected.keras')})
            (d/f'phase{phase}_state.json').write_text(json.dumps(rec))
        if missing:
            (d/expected/'selected.keras').unlink()
            with self.assertRaisesRegex(Failure,'Missing'):ns['astra_select_phase'](str(d))
        else:
            model,phase=ns['astra_select_phase'](str(d));self.assertEqual(phase,expected)
            self.assertEqual(float(model.layers[-1].bias[0]),1 if expected=='A' else 2)
            model.save(d/'result.keras');loaded=keras.models.load_model(d/'result.keras')
            np.testing.assert_allclose(model(X).numpy(),loaded(X).numpy())
    def test_phase_A_selection(self):self.selection('phase_a',.9,.7,'A')
    def test_phase_B_selection(self):self.selection('phase_b',.7,.9,'B')
    def test_phase_tie(self):self.selection('phase_tie',.9,.9,'B')
    def test_missing_selected_checkpoint(self):self.selection('phase_missing',.9,.7,'A',True)
    def test_output_escape(self):
        with self.assertRaises(Failure):ns['safe_path'](str(scratch/'../outside.txt'))
        self.assertEqual(ns['safe_path'](str(scratch/'good/a.txt')),str(scratch/'good/a.txt'))
        (scratch/'link').symlink_to(scratch.parent,target_is_directory=True)
        with self.assertRaises(Failure):ns['safe_path'](str(scratch/'link/escape.txt'))
    def test_original_annotation_protection(self):
        prior=cfg.ANNOTATION_CSV;cfg.ANNOTATION_CSV=str(scratch/'input.csv')
        try:
            with self.assertRaises(Failure):ns['safe_path'](cfg.ANNOTATION_CSV)
        finally:cfg.ANNOTATION_CSV=prior
    def test_one_use_action(self):
        ns['ASTRA_ACTION']=None
        with self.assertRaises(Failure):ns['astra_require_action']('global_A')
        ns['ASTRA_ACTION']='global_A';ns['astra_require_action']('global_A')
        with self.assertRaises(Failure):ns['astra_require_action']('global_A')
    def test_normal_not_abstention(self):
        y=np.array([[0,0,0],[1,0,0],[0,0,0],[-1,0,0]])
        m=ns['astra_normal_metrics'](y,['NO_FINDINGS_SUPPORTED','NO_FINDINGS_SUPPORTED','UNCERTAIN_REVIEW_REQUIRED','NO_FINDINGS_SUPPORTED'])
        self.assertEqual(m['tp'],1);self.assertEqual(m['fp'],1);self.assertEqual(m['reference_n'],3);self.assertEqual(m['uncertain_normal_candidates'],1)
    def test_no_reliability_band_never_normal(self):
        d=ns['decide']([0,0,0],[.9,.9,.9],float('inf'),[True]*3,dict(blocking=[]),False,[])
        self.assertEqual(d['state'],'UNCERTAIN_REVIEW_REQUIRED')
        json.dumps(d,allow_nan=False)
    def test_metric_zero_f1(self):
        y=np.array([[1,0,0],[0,1,1]]);d=1-y
        self.assertTrue((ns['metrics_dec'](y,d.astype(float),d.astype(float),min_pos=1).f1==0).all())
    def test_permissions_fail_closed(self):self.assertTrue(ns['source_issues']([],['unresolved'],'training'))
    def test_synthetic_onnx_parity(self):
        import onnxruntime as ort
        ns['export_path']=lambda n:str(scratch/n)
        m=tiny();result=ns['to_onnx'](m,'synthetic_tiny')
        self.assertEqual(result['status'],'exported',result)
        s=ort.InferenceSession(result['path'],providers=['CPUExecutionProvider'])
        np.testing.assert_allclose(m(X).numpy(),s.run(None,{s.get_inputs()[0].name:X})[0],rtol=1e-5,atol=1e-5)
    def test_crossfit_actual_group_separation(self):
        from sklearn.model_selection import GroupKFold
        groups=np.repeat(np.arange(30),3);ys=np.tile([[1,0,1],[0,1,0],[0,0,0]],(30,1))
        xs=[dict(g_emb=np.zeros((90,128)),g_logit=np.zeros((90,3)),g_extra=np.zeros((90,2)),det_ev=np.zeros((90,21)),roi_feat=np.zeros((90,12,83)),roi_mask=np.ones((90,12)))]
        jobs=[]
        def train(*args,**kwargs):
            grouping=kwargs['groups'];allg=grouping['all'];tr=grouping['train'];va=grouping['val']
            self.assertFalse(set(allg[tr])&set(allg[va]))
            if 'heldout' in grouping:self.assertFalse((set(allg[tr])|set(allg[va]))&set(allg[grouping['heldout']]))
            jobs.append(kwargs['job'])
            class Dummy:
                def predict(self,x,**kw):return np.zeros((len(x[0]),3),np.float32)
            return Dummy()
        old={k:ns.get(k) for k in ['train_one_fusion','save_selected_model','x_list','GroupKFold']}
        dirs=getattr(cfg,'DIRS',{});cfg.DIRS=dict(fusion=str(scratch/'crossfit'))
        ns.update(train_one_fusion=train,save_selected_model=lambda *a:None,x_list=lambda x:list(x.values()),GroupKFold=GroupKFold)
        try:
            oof,paths=ns['crossfit_fusion'](xs,ys,groups,'full',seeds=[42],folds=3)
            self.assertEqual(len(jobs),4);self.assertEqual(oof[0].shape,(90,3));self.assertEqual(len(paths),1)
        finally:ns.update(old);cfg.DIRS=dirs
    def test_selected_epoch_optimizer_consistency(self):
        class Metric(keras.callbacks.Callback):
            def on_epoch_end(self,epoch,logs=None):logs['synthetic_selection']=float(epoch+1)
        m,rec=ns['recovery_fit'](tiny(),dataset(),dataset(),str(scratch/'best_optimizer'),dict(synthetic=True),3,
            monitor='synthetic_selection',mode='min',metric_callbacks=[Metric()],verbose=0)
        self.assertEqual(int(m.optimizer.iterations),2)
        latest=keras.models.load_model(pathlib.Path(rec['attempt'])/rec['latest'])
        self.assertEqual(int(latest.optimizer.iterations),6)
    def test_real_fusion_recovery_wrapper(self):
        rng=np.random.default_rng(4)
        xs=dict(g_emb=rng.normal(size=(4,128)).astype('float32'),g_logit=np.zeros((4,3),'float32'),g_extra=np.zeros((4,2),'float32'),
            det_ev=np.ones((4,21),'float32'),roi_feat=rng.normal(size=(4,12,83)).astype('float32'),roi_mask=np.ones((4,12),'float32'))
        y=np.array([[1,0,0],[0,1,0],[0,0,1],[0,0,0]],'float32')
        old={k:ns.get(k) for k in ['training_identity','set_all_seeds','x_list']};dirs=cfg.DIRS
        ns.update(training_identity=lambda *a:dict(synthetic=True),set_all_seeds=keras.utils.set_random_seed,x_list=lambda x:[x[k] for k in ['g_emb','g_logit','g_extra','det_ev','roi_feat','roi_mask']])
        cfg.DIRS=dict(fusion=str(scratch/'real_fusion'))
        try:
            model=ns['train_one_fusion'](xs,y,xs,y,42,epochs=1,job='SYNTHETIC/fold_0',groups=dict(synthetic=True))
            self.assertTrue(np.isfinite(model(ns['x_list'](xs)).numpy()).all())
            again=ns['train_one_fusion'](xs,y,xs,y,42,epochs=1,job='SYNTHETIC/fold_0',groups=dict(synthetic=True))
            np.testing.assert_allclose(model(ns['x_list'](xs)).numpy(),again(ns['x_list'](xs)).numpy())
        finally:ns.update(old);cfg.DIRS=dirs

if __name__=='__main__':
    suite=unittest.defaultTestLoader.loadTestsFromTestCase(Tests)
    result=unittest.TextTestRunner(verbosity=2).run(suite)
    report=dict(synthetic_only=True,scratch=str(scratch),run=result.testsRun,passed=result.testsRun-len(result.failures)-len(result.errors)-len(result.skipped),
        failures=[(str(t),s) for t,s in result.failures],errors=[(str(t),s) for t,s in result.errors],skipped=[(str(t),s) for t,s in result.skipped],gpu=[str(g) for g in gpu])
    (root/'astra_tests/contracts_results.json').write_text(json.dumps(report,indent=2))
    raise SystemExit(not result.wasSuccessful())
