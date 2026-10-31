"""Regenerates preprocess_golden.json: what the notebook / gradcam_service.py preprocessing produces for a fixed synthetic image,
and what the deployed precisionskin.onnx returns for it in Python ONNX Runtime. Run with a Python that has numpy, opencv, onnxruntime."""
import json, hashlib, sys, os
import numpy as np, cv2, onnxruntime as ort

H, W = 37, 53
y, x = np.mgrid[0:H, 0:W]
bgr = np.stack([(x*7 + y*3) % 256, (x*5 + y*11) % 256, (x*13 + y*17) % 256], -1).astype(np.uint8)   # B, G, R

rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
t = (cv2.resize(rgb, (224, 224), interpolation=cv2.INTER_LINEAR).astype(np.float32) / 127.5) - 1.0
t = t[None]
flat = t.reshape(-1)
idx = list(range(0, flat.size, 499))
model = os.path.join(os.path.dirname(__file__), "..", "..", "SkinDevApp", "Model", "precisionskin.onnx")
sess = ort.InferenceSession(model, providers=["CPUExecutionProvider"])
p = sess.run(None, {sess.get_inputs()[0].name: t})[0][0]
h = hashlib.sha256(open(model, "rb").read()).hexdigest()
json.dump({
    "image": "B=(7x+3y)%256 G=(5x+11y)%256 R=(13x+17y)%256, 53x37 BGR",
    "model_sha256": h,
    "sample_stride": 499, "samples": [float(flat[i]) for i in idx],
    "channel_mean_rgb": [float(t[0, :, :, c].mean()) for c in range(3)],
    "probabilities_acne_hyper_eczema_normal": [float(v) for v in p],
}, open(os.path.join(os.path.dirname(__file__), "preprocess_golden.json"), "w"), indent=1)
print("ok", h, p)
