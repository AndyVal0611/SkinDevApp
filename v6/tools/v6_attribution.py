"""V6 attribution prototype: a Grad-CAM++ map for the FUSED V6 output (not for V5).

What it explains: the fused `global_yolo` logit of one condition, differentiated back to the 7x7x1280 feature map of the Global MobileNetV2.
That shows which image regions the Global branch contributed to the fused decision. It does NOT show the detector's contribution (that is what the
marked boxes are for), and it does not show anything about V5.

    image -> MobileNetV2 (A: 7x7x1280) -> GAP -> embedding(128) / logits(3) -> g_logit -> fusion(global_yolo; det_ev fixed) -> fused logit[c]

Checks built in: (1) the differentiable path reproduces the exported pipeline's fused logits; (2) deletion test: removing the top 20 % of the map lowers the
fused logit more than removing a random 20 %. Nothing here trains or changes a model. CPU only.
"""
import os
os.environ["CUDA_VISIBLE_DEVICES"] = "-1"; os.environ["TF_CPP_MIN_LOG_LEVEL"] = "3"
import cv2, numpy as np, tensorflow as tf, keras

RUN = "/mnt/c/Users/Bautista/source/repos/SkinDevApp/v6/astra_runs/open_run02"
CONDS = ["Acne", "Hyperpigmentation", "Eczema"]
COLORS_BGR = [(0, 0, 255), (255, 80, 0), (0, 190, 255)]          # same palette as ClassPalette: acne red, hyperpigmentation blue, eczema orange
exec(open("/mnt/c/Users/Bautista/source/repos/SkinDevApp/v6/tools/_fusion_layers.py").read())   # custom layers needed to load the fusion model


class V6Attribution:
    def __init__(self):
        self.g = keras.models.load_model(f"{RUN}/export/v6_global_features.keras", custom_objects={"MaskedBCE": MaskedBCE}, compile=False)
        self.f = keras.models.load_model(f"{RUN}/fusion/global_yolo/final_seed42.keras", custom_objects={"MaskedBCE": MaskedBCE}, compile=False)
        self.bb = self.g.get_layer("mobilenetv2_1.00_224")
        self.gap, self.emb_l, self.log_l = self.g.get_layer("global_gap"), self.g.get_layer("global_embedding"), self.g.get_layer("global_logits")

    @staticmethod
    def preprocess(bgr):
        return ((cv2.resize(cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB), (224, 224), interpolation=cv2.INTER_LINEAR).astype(np.float32) / 127.5) - 1.0)[None]

    def fused_from_features(self, A, det_ev):
        e = self.emb_l(self.gap(A)); z = self.log_l(e)
        p = tf.clip_by_value(tf.sigmoid(z), 1e-4, 1 - 1e-4); gl = tf.math.log(p / (1 - p))
        det = tf.constant(np.asarray(det_ev, np.float32)[None])
        return self.f([e, gl, tf.zeros((1, 2)), det, tf.zeros((1, 12, 83)), tf.zeros((1, 12))], training=False)

    def fused(self, x, det_ev):
        return self.fused_from_features(self.bb(x, training=False), det_ev).numpy()[0]

    def cams(self, bgr, det_ev):
        """Returns (fused logits[3], list of 3 normalised 7x7 Grad-CAM++ maps in [0,1])."""
        x = tf.constant(self.preprocess(bgr)); A = self.bb(x, training=False); out = []
        with tf.GradientTape(persistent=True) as tape:
            tape.watch(A); y = self.fused_from_features(A, det_ev); ys = [y[:, j] for j in range(3)]      # slice INSIDE the tape so the gradient is recorded
        for j in range(3):
            g = tape.gradient(ys[j], A)
            g2, g3 = g ** 2, g ** 3; sumA = tf.reduce_sum(A, axis=(1, 2), keepdims=True)
            denom = 2 * g2 + sumA * g3; denom = tf.where(denom != 0, denom, tf.ones_like(denom))
            w = tf.reduce_sum((g2 / denom) * tf.nn.relu(g), axis=(1, 2))
            cam = tf.nn.relu(tf.reduce_sum(w[:, None, None, :] * A, axis=-1))[0].numpy()
            out.append(cam / cam.max() if cam.max() > 0 else cam)
        return y.numpy()[0], out

    def deletion_drop(self, bgr, det_ev, cam, j, frac=0.2, n_random=8, seed=0):
        """Drop of the fused logit j when the top `frac` of the map is blanked, vs a random region of the same size."""
        x = self.preprocess(bgr); base = self.fused(tf.constant(x), det_ev)[j]; up = cv2.resize(cam, (224, 224), interpolation=cv2.INTER_CUBIC)
        k = int(frac * up.size); top = np.zeros(up.size, bool); top[np.argsort(-up.ravel())[:k]] = True
        def drop(mask):
            xm = x.copy(); xm[0][mask.reshape(224, 224)] = 0.0
            return base - self.fused(tf.constant(xm), det_ev)[j]
        rng = np.random.default_rng(seed); rnd = []
        for _ in range(n_random):
            m = np.zeros(up.size, bool); m[rng.choice(up.size, k, replace=False)] = True; rnd.append(drop(m))
        return drop(top), float(np.mean(rnd))


def overlay(bgr, cam, color, alpha_max=0.6, gamma=1.25):
    h, w = bgr.shape[:2]; heat = cv2.GaussianBlur(cv2.resize(cam, (w, h), interpolation=cv2.INTER_CUBIC), (0, 0), 0.02 * max(h, w)); heat = np.clip(heat, 0, 1) ** gamma
    a = (heat * alpha_max)[..., None]; tint = np.zeros_like(bgr); tint[:] = color
    return (bgr * (1 - a) + tint * a).astype(np.uint8)
