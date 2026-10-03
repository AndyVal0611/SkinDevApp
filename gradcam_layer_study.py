import os, sys, glob, json
"""
gradcam_layer_study.py - are the four class Grad-CAM++ maps independent, and does
another target layer help?  Read-only diagnostic; it never changes the service.

  python gradcam_layer_study.py --model precisionskin_best.keras --images "Captures/**/original.png"

Reports, per layer: grid size, mean pairwise Pearson and top-10% IoU between the RAW
(un-normalised) class CAMs, and the max difference between the service's all-class
path and four separate per-class computations (must be ~0 = independent).
"""
import argparse
ap = argparse.ArgumentParser()
ap.add_argument("--model", default="precisionskin_best.keras")
ap.add_argument("--images", required=True, help="glob of face images, e.g. 'Captures/**/original.png'")
ap.add_argument("--last", type=int, default=12, help="use the last N matching images")
ARGS = ap.parse_args()
os.environ["CUDA_VISIBLE_DEVICES"]="-1"; os.environ["TF_CPP_MIN_LOG_LEVEL"]="3"
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, cv2
import gradcam_service as G
import tensorflow as tf

m = tf.keras.models.load_model(ARGS.model, compile=False)
print("TOP-LEVEL LAYERS:", [(l.name, type(l).__name__) for l in m.layers])
cam = G.GradCAMPlusPlus(m, "Conv_1")
print("head:", [(l.name, type(l).__name__) for l in cam.head_layers])
bb = cam.backbone
print("backbone tail:", [l.name for l in bb.layers[-6:]])

W = cam.final_dense.kernel.numpy()                 # (1280, 4)
Wn = W/np.linalg.norm(W,axis=0,keepdims=True)
print("cosine similarity between class weight vectors (final Dense):")
print(np.round(Wn.T@Wn,3))

caps = sorted(glob.glob(ARGS.images, recursive=True))
print(len(caps),"captures")
caps = caps[-ARGS.last:]
layers = ["Conv_1","out_relu","block_13_expand_relu","block_6_expand_relu","block_3_expand_relu"]
names = G.CLASS_NAMES

def pear(a,b):
    a=a.ravel()-a.mean(); b=b.ravel()-b.mean(); d=np.sqrt((a*a).sum()*(b*b).sum()); return float((a*b).sum()/d) if d>1e-12 else float('nan')

def top_iou(a,b,q=90):
    ta=a>=np.percentile(a,q); tb=b>=np.percentile(b,q); return (ta&tb).sum()/max(1,(ta|tb).sum())

res={l:{"pear":[], "iou":[], "grid":None, "std":[]} for l in layers}
rows=[]
for lname in layers:
    cam.set_layer(lname)
    for f in caps:
        img=cv2.imread(f); x=G.preprocess(img)
        cams, pred, probs, meth, notes = cam.compute_all(x) if False else (None,)*5
        # eager, independent per-class gradients (reference implementation = compute(), one tape per class)
        cs=[]
        for c in range(4):
            cm, p, pr, mu, nt = cam.compute(x, class_index=c)
            cs.append(cm)
        # independence check: compute_all (persistent tape) must equal four separate computes
        ca,_,_,_,_ = cam.compute_all(x)
        diff=max(float(np.abs(ca[c]-cs[c]).max()/(np.abs(cs[c]).max()+1e-12)) for c in range(4))
        pp=[pear(cs[i],cs[j]) for i in range(4) for j in range(i+1,4)]
        io=[top_iou(cs[i],cs[j]) for i in range(4) for j in range(i+1,4)]
        res[lname]["pear"].append(np.nanmean(pp)); res[lname]["iou"].append(np.mean(io)); res[lname]["grid"]=cs[0].shape
        res[lname]["maxdiff_all_vs_single"]=max(res[lname].get("maxdiff_all_vs_single",0),diff)
        if lname=="Conv_1" and f==caps[-1]:
            print("\nLAST IMAGE raw Conv_1 CAM peaks:",[float(c.max()) for c in cs])
            print("pairwise pearson (Acne,Hyper,Eczema,Normal):"); 
            print(np.round(np.array([[pear(a,b) for b in cs] for a in cs]),3))
print()
print("%-24s %-8s %-14s %-12s %s"%("layer","grid","mean Pearson","top10% IoU","max rel diff compute_all vs per-class"))
for l in layers:
    r=res[l]; print("%-24s %-8s %-14.3f %-12.3f %.2e"%(l,r["grid"],np.mean(r["pear"]),np.mean(r["iou"]),r["maxdiff_all_vs_single"]))
