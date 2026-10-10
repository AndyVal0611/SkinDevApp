"""
PrecisionSkin - Live Grad-CAM++ Explanation Service  (v2.1)
  v1.1: frame_id echo, stale-drop, input fingerprint, concentration diagnostics
  v2.0: ALL-CLASS endpoint - one forward pass yields a Grad-CAM++ map for each of
        the four classes (+ raw_peak / relative_strength / concentration per class),
        optional compiled (tf.function) path, backward-compatible /gradcam.
        Heatmap math for a single class is UNCHANGED.
===================================================

Loads precisionskin_best.keras ONCE at startup and keeps it in RAM.
Listens on 127.0.0.1:8765 only (loopback, never exposed to the network).

Endpoints
---------
GET  /health    -> service + model status
POST /gradcam   -> {"image_base64": "..."} -> prediction + Grad-CAM++ overlay
                   add "all_classes": true for the four class-specific maps

Preprocessing is IDENTICAL to the ONNX classifier:
    decode -> BGR -> RGB -> resize 224x224 (bilinear) -> float32 -> (p / 127.5) - 1.0

Run:
    python gradcam_service.py --model "C:/PrecisionSkin/models/precisionskin_best.keras"
"""

from __future__ import annotations

import argparse
import base64
import binascii
import json
import logging
import os
import sys
import threading
import time
from typing import Dict, List, Optional, Tuple

# Keep TensorFlow quiet and CPU-friendly on the 4-core i5 Mini PC.
os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "2")
os.environ.setdefault("TF_ENABLE_ONEDNN_OPTS", "1")
os.environ.setdefault("CUDA_VISIBLE_DEVICES", "-1")

import cv2
import numpy as np

# ==============================================================================
# CONFIGURATION
# ==============================================================================

DEFAULT_MODEL_PATH = os.environ.get(
    "PRECISIONSKIN_KERAS",
    "precisionskin_best.keras",
)

DEFAULT_LABELS_PATH = os.environ.get(
    "PRECISIONSKIN_LABELS",
    "labels.json",
)

HOST = "127.0.0.1"
PORT = 8765

IMG_SIZE = 224

# Must match CLASS_NAMES in the training notebook (Cell 6) and labels.json.
CLASS_NAMES: List[str] = ["Acne", "Hyperpigmentation", "Eczema", "Normal"]

PRIMARY_LAYER = "Conv_1"
SUPPORTED_LAYERS = ["Conv_1", "out_relu", "block_13_expand_relu"]

# Overlay appearance
OVERLAY_ALPHA = 0.55      # peak opacity of the heatmap
ATTENTION_GAMMA = 1.25    # >1 keeps low-attention regions more transparent
PERCENTILE_LOW = 1.0
PERCENTILE_HIGH = 99.0
BLUR_SIGMA = 3.0          # gaussian smoothing on the upsampled heatmap; 0 = off

MAX_IMAGE_BYTES = 12 * 1024 * 1024   # reject absurd payloads

# 2.1 = 2.0 + raw CAM arrays (include_raw_cam) + class_map_similarity. Purely additive:
# every 2.0 request/response field is unchanged.
SERVICE_VERSION = "2.1"

# All-class maps are returned at most this many pixels on the longer side
# (the client resizes to the frame). Keeps four PNGs small and fast.
ALL_CLASS_MAX_SIDE = 320

# A map whose top-10% pixels hold less than this share of its total mass is
# reported as "diffuse" (attribution is spread out, not focal).
DIFFUSE_TOP10_MASS = 0.28

# How the per-class heat is built for the all-class response (explain_all):
#   "attribution" (default)   the plain per-class Grad-CAM++ map, each class normalised separately.
#   "differential" (EXPERIMENTAL, opt-in)  heat_c = relu(CAM_c - mean of the OTHER classes' CAMs), all four on ONE common scale.
#                             On the 167 hand-boxed acne test photos the acne map put 1.76x chance of its heat inside the boxes
#                             (plain map 1.30x, photo-independent average map 1.18x; gradcam_enrichment.py). BUT on real kiosk captures it is
#                             unstable: the raw CAM scale differs per class, so a class scored 1% can get the strongest map while the 90%
#                             class gets a faint one. Therefore it is NOT the default. Request it with map_mode=differential or the
#                             environment variable PRECISIONSKIN_MAP_MODE=differential, for research only.
DEFAULT_MAP_MODE = os.environ.get("PRECISIONSKIN_MAP_MODE", "attribution").strip().lower()
if DEFAULT_MAP_MODE not in ("differential", "attribution"):
    DEFAULT_MAP_MODE = "attribution"

# Set PRECISIONSKIN_COMPILE=0 to force eager execution (debug).
USE_COMPILED = os.environ.get("PRECISIONSKIN_COMPILE", "1") != "0"

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s | %(levelname)-7s | %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger("gradcam")


# ==============================================================================
# PREPROCESSING  (must stay byte-identical to the ONNX path in C#)
# ==============================================================================

def decode_image(image_bytes: bytes) -> np.ndarray:
    """JPEG/PNG bytes -> BGR uint8 image (original resolution)."""
    buf = np.frombuffer(image_bytes, dtype=np.uint8)
    img_bgr = cv2.imdecode(buf, cv2.IMREAD_COLOR)

    if img_bgr is None:
        raise ValueError("cv2.imdecode failed - not a valid JPEG/PNG image")

    return img_bgr


def preprocess(img_bgr: np.ndarray) -> np.ndarray:
    """
    EXACT inference preprocessing:
        BGR -> RGB -> resize 224x224 -> float32 -> (pixel / 127.5) - 1.0

    Returns a (1, 224, 224, 3) float32 batch.
    """
    img_rgb = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2RGB)

    img_rgb = cv2.resize(
        img_rgb,
        (IMG_SIZE, IMG_SIZE),
        interpolation=cv2.INTER_LINEAR,   # C# must use the same (bilinear)
    )

    x = (img_rgb.astype(np.float32) / 127.5) - 1.0

    return np.expand_dims(x, axis=0)


# ==============================================================================
# GRAD-CAM++  (Keras 3 / TensorFlow 2.21 safe)
# ==============================================================================

class GradCAMPlusPlus:
    """
    Robust Grad-CAM++ for a nested MobileNetV2 functional model.

    Why not the naive approach:
        tf.keras.Model(inputs=model.inputs,
                       outputs=[nested_layer.output, model.output])
    Under Keras 3 the nested backbone's internal tensors do not belong to the
    outer model's graph, so this either raises a disconnected-graph error or
    silently builds a model whose gradient path is broken.

    Instead:
        1. Build a feature model on the BACKBONE's own graph:
               backbone.input -> [target_activation, backbone.output]
        2. Re-execute the EXISTING trained head layers on backbone.output
           inside the GradientTape (same layers, same weights, no cloning,
           no retraining).
    """

    def __init__(self, model, layer_name: str = PRIMARY_LAYER):
        import tensorflow as tf

        self.tf = tf
        self.model = model
        self.backbone = self._find_backbone(model)

        # Head = every top-level layer that is not the InputLayer and not the
        # nested backbone. model.layers is already topologically ordered.
        # Only layers AFTER the backbone: anything before it (Rescaling,
        # augmentation) is not part of the feature->logit path and must not be
        # re-applied to backbone features.
        _layers = list(model.layers)
        _bi = next(i for i, l in enumerate(_layers) if l is self.backbone)
        self.head_layers = [
            layer
            for layer in _layers[_bi + 1:]
            if not isinstance(layer, tf.keras.layers.InputLayer)
        ]
        if any(not isinstance(l, tf.keras.layers.InputLayer) for l in _layers[:_bi]):
            log.warning("Layers exist before the backbone (%s); they are NOT "
                        "re-applied. Parity check at startup will confirm.",
                        [l.name for l in _layers[:_bi]])

        if not self.head_layers:
            raise RuntimeError("No classifier head layers found on the model.")

        self.final_dense = self.head_layers[-1]

        if not hasattr(self.final_dense, "kernel"):
            raise RuntimeError(
                f"Expected the last head layer to be Dense, got "
                f"{type(self.final_dense).__name__}"
            )

        self.layer_name: Optional[str] = None
        self.feature_model = None
        self.feature_path = "direct"
        self.set_layer(layer_name)

    # -------------------------------------------------------------- backbone
    def _find_backbone(self, model):
        tf = self.tf

        for layer in model.layers:
            if isinstance(layer, tf.keras.Model):
                return layer

        raise ValueError(
            "No nested backbone submodel found. Expected a MobileNetV2 "
            "functional model inside the classifier."
        )

    # ------------------------------------------------------------ set_layer
    def set_layer(self, layer_name: str) -> None:
        """Validate the target layer and (re)build the feature extractor."""
        tf = self.tf

        available = [layer.name for layer in self.backbone.layers]

        if layer_name not in available:
            raise ValueError(
                f"Layer '{layer_name}' not found in backbone "
                f"'{self.backbone.name}'. Last 20 layers: {available[-20:]}"
            )

        target = self.backbone.get_layer(layer_name)
        shape = tuple(target.output.shape)

        if len(shape) != 4:
            raise ValueError(
                f"Layer '{layer_name}' output is {shape}, not 4-D spatial "
                "(batch, h, w, channels). Grad-CAM requires a conv feature map."
            )

        if shape[1] is None or shape[1] < 2 or shape[2] < 2:
            raise ValueError(
                f"Layer '{layer_name}' has a degenerate spatial size {shape}."
            )

        # --- primary path: build on the backbone's own graph -----------------
        try:
            feature_model = tf.keras.Model(
                inputs=self.backbone.input,
                outputs=[target.output, self.backbone.output],
                name=f"gradcam_features_{layer_name}",
            )
            path = "direct"

        except Exception as exc:                      # pragma: no cover
            log.warning(
                "Direct feature-model construction failed (%s). "
                "Falling back to weight-transfer rebuild.", exc
            )
            feature_model = self._rebuild_feature_model(layer_name)
            path = "rebuilt"

        self.feature_model = feature_model
        self.feature_path = path
        self.layer_name = layer_name
        self._all_fn = None            # (re)built lazily for the new feature model
        self._all_fn_mode = "eager"

        log.info(
            "Grad-CAM target layer: %s | %s | %s | path=%s",
            layer_name, target.__class__.__name__, shape, path,
        )

    def _rebuild_feature_model(self, layer_name: str):
        """
        Fallback: instantiate a fresh MobileNetV2 skeleton and copy the TRAINED
        weights into it. Same weights, clean graph. Nothing is retrained.
        """
        tf = self.tf

        fresh = tf.keras.applications.MobileNetV2(
            input_shape=(IMG_SIZE, IMG_SIZE, 3),
            include_top=False,
            weights=None,
        )
        fresh.set_weights(self.backbone.get_weights())

        probe = np.random.uniform(-1.0, 1.0, (1, IMG_SIZE, IMG_SIZE, 3)).astype("float32")
        ref = self.backbone(probe, training=False).numpy()
        new = fresh(probe, training=False).numpy()
        max_diff = float(np.abs(ref - new).max())

        if max_diff > 1e-4:
            raise RuntimeError(
                f"Weight-transfer rebuild mismatch (max diff {max_diff:.2e}). "
                "Refusing to produce a Grad-CAM from a different model."
            )

        log.info("Rebuilt backbone verified (max diff %.2e)", max_diff)

        target = fresh.get_layer(layer_name)

        return tf.keras.Model(
            inputs=fresh.input,
            outputs=[target.output, fresh.output],
            name=f"gradcam_features_rebuilt_{layer_name}",
        )

    # -------------------------------------------------------------- forward
    def _forward(self, x, tape=None):
        """
        Run: input -> conv activations + backbone output -> trained head.
        Returns (conv_activations, logits, probabilities).
        """
        tf = self.tf

        conv, features = self.feature_model(x, training=False)

        if tape is not None:
            tape.watch(conv)

        h = features
        for layer in self.head_layers[:-1]:
            h = layer(h, training=False)          # BN/Dropout in inference mode

        dense = self.final_dense
        kernel = tf.cast(dense.kernel, h.dtype)
        bias = tf.cast(dense.bias, h.dtype)

        logits = tf.cast(tf.matmul(h, kernel) + bias, tf.float32)
        probs = tf.nn.softmax(logits)

        return conv, logits, probs

    # ---------------------------------------------------------------- compute
    def compute(
        self,
        x: np.ndarray,
        class_index: Optional[int] = None,
        method: str = "gradcam++",
    ) -> Tuple[np.ndarray, int, np.ndarray, str, List[str]]:
        """
        Returns (cam_2d_raw, predicted_index, probabilities, method_used, notes).

        cam_2d_raw is the un-normalised ReLU'd CAM at feature-map resolution.
        """
        tf = self.tf
        notes: List[str] = []

        x_t = tf.convert_to_tensor(x, dtype=tf.float32)

        with tf.GradientTape() as tape:
            conv, logits, probs = self._forward(x_t, tape=tape)

            pred_index = int(tf.argmax(probs[0]).numpy())
            target_index = pred_index if class_index is None else int(class_index)

            if not 0 <= target_index < len(CLASS_NAMES):
                raise ValueError(f"class_index {target_index} out of range")

            # Use the pre-softmax logit as S^c (standard Grad-CAM++ practice:
            # softmax scores are contaminated by the other classes' logits).
            score = logits[:, target_index]

        grads = tape.gradient(score, conv)

        if grads is None:
            raise RuntimeError(
                f"Gradient is None for layer '{self.layer_name}'. The target "
                "activation is not connected to the classification output."
            )

        conv_np = tf.cast(conv, tf.float32).numpy()[0]      # (H, W, K)
        g1 = tf.cast(grads, tf.float32).numpy()[0]          # (H, W, K)
        probabilities = probs.numpy()[0]

        method_used = "Grad-CAM++"
        cam = None

        if method.lower().replace("_", "").replace("-", "") in (
            "gradcam++", "gradcampp", "gradcamplusplus",
        ):
            cam = self._gradcam_plus_plus(conv_np, g1, notes)

            if cam is None:
                notes.append(
                    "Grad-CAM++ higher-order gradient unavailable; "
                    "using standard Grad-CAM."
                )
                log.warning(
                    "Grad-CAM++ higher-order gradient unavailable; "
                    "using standard Grad-CAM."
                )
                method_used = "Grad-CAM"
        else:
            method_used = "Grad-CAM"

        if cam is None:
            cam = self._standard_gradcam(conv_np, g1)

        if not np.isfinite(cam).all():
            raise RuntimeError("CAM contains NaN/Inf - refusing to return it.")

        return cam, pred_index, probabilities, method_used, notes

    # ------------------------------------------------------------ compute_all
    def _all_impl(self, x_t):
        """
        ONE forward pass, then one gradient per class on a persistent tape.
        For Conv_1 the class-score gradient only has to travel back through the
        short tail of the backbone and the pooling/dense head, so four classes
        cost little more than one.
        Returns (conv (1,H,W,K), grads (N,1,H,W,K), probs (1,N)).
        """
        tf = self.tf
        n = len(CLASS_NAMES)

        with tf.GradientTape(persistent=True) as tape:
            conv, logits, probs = self._forward(x_t, tape=tape)
            scores = [logits[:, i] for i in range(n)]

        grads = []
        for sc in scores:
            g = tape.gradient(sc, conv)
            if g is None:
                g = tf.zeros_like(conv)
            grads.append(g)

        del tape
        return conv, tf.stack(grads, axis=0), probs

    def _get_all_fn(self):
        if self._all_fn is None:
            if USE_COMPILED:
                self._all_fn = self.tf.function(self._all_impl, reduce_retracing=True)
                self._all_fn_mode = "compiled"
            else:
                self._all_fn = self._all_impl
                self._all_fn_mode = "eager"
        return self._all_fn

    def compute_all(
        self,
        x: np.ndarray,
        method: str = "gradcam++",
    ) -> Tuple[List[np.ndarray], int, np.ndarray, str, List[str]]:
        """
        Returns (cams, predicted_index, probabilities, method_used, notes)
        where cams[i] is the raw (un-normalised) ReLU'd CAM for class i.
        Raw values are kept so the caller can compare classes honestly.
        """
        tf = self.tf
        notes: List[str] = []
        x_t = tf.convert_to_tensor(x, dtype=tf.float32)

        try:
            conv, grads, probs = self._get_all_fn()(x_t)
        except Exception as exc:                                 # noqa: BLE001
            if self._all_fn_mode == "compiled":
                log.warning("Compiled all-class path failed (%s: %s). "
                            "Falling back to eager.", type(exc).__name__, exc)
                self._all_fn = self._all_impl
                self._all_fn_mode = "eager"
                conv, grads, probs = self._all_fn(x_t)
            else:
                raise

        conv_np = tf.cast(conv, tf.float32).numpy()[0]           # (H, W, K)
        grads_np = tf.cast(grads, tf.float32).numpy()[:, 0]      # (N, H, W, K)
        probabilities = probs.numpy()[0]
        pred_index = int(np.argmax(probabilities))

        use_pp = method.lower().replace("_", "").replace("-", "") in (
            "gradcam++", "gradcampp", "gradcamplusplus",
        )
        method_used = "Grad-CAM++" if use_pp else "Grad-CAM"

        cams: List[np.ndarray] = []
        for i in range(len(CLASS_NAMES)):
            cam = None
            if use_pp:
                cam = self._gradcam_plus_plus(conv_np, grads_np[i], notes)
                if cam is None:
                    notes.append(f"{CLASS_NAMES[i]}: Grad-CAM++ degenerate; used standard Grad-CAM.")
                    method_used = "Grad-CAM++ (some classes: Grad-CAM fallback)"
            if cam is None:
                cam = self._standard_gradcam(conv_np, grads_np[i])
            if not np.isfinite(cam).all():
                raise RuntimeError(f"CAM for {CLASS_NAMES[i]} contains NaN/Inf.")
            cams.append(cam)

        return cams, pred_index, probabilities, method_used, notes

    # --------------------------------------------------------- cam variants
    @staticmethod
    def _gradcam_plus_plus(
        conv: np.ndarray,
        g1: np.ndarray,
        notes: List[str],
    ) -> Optional[np.ndarray]:
        """
        Grad-CAM++ (Chattopadhyay et al., 2018), Eq. 19.

        With Y^c = exp(S^c) the higher-order derivatives of the class score
        w.r.t. the activations A^k have closed form:

            dY/dA    = exp(S) * g              (first  order)
            d2Y/dA2  = exp(S) * g^2            (second order)
            d3Y/dA3  = exp(S) * g^3            (third  order)

        so

            alpha = d2Y / (2*d2Y + sum_ab(A_ab) * d3Y)

        The exp(S) factor cancels in the ratio, and in the final weight sum it
        is a single positive scalar that disappears under normalisation - so it
        is dropped for numerical stability. This is the paper's formulation,
        NOT a simplification.

        Note on autodiff: MobileNetV2 is piecewise-linear (ReLU6 / linear
        bottlenecks), so a nested-GradientTape Hessian of the LOGIT is
        identically zero almost everywhere. That is exactly why Grad-CAM++
        is defined on Y = exp(S) instead. See verify_higher_order_gradients()
        in the notebook for the diagnostic that demonstrates this.
        """
        gmax = float(np.abs(g1).max())
        if gmax <= 0.0:
            notes.append("all gradients zero")
            return None
        # Scale-relative threshold: absolute 1e-10 zeroed alpha for gradients ~1e-5.
        eps = 1e-8 * gmax * gmax

        g2 = g1 * g1
        g3 = g2 * g1

        sum_a = conv.sum(axis=(0, 1), keepdims=True)        # (1, 1, K)

        denom = 2.0 * g2 + sum_a * g3

        alpha = np.where(np.abs(denom) > eps, g2 / (denom + eps), 0.0)
        alpha = np.where(np.abs(g1) > 1e-8 * gmax, alpha, 0.0)      # no gradient -> no weight

        if not np.isfinite(alpha).all():
            notes.append("alpha map non-finite")
            return None

        weights = (alpha * np.maximum(g1, 0.0)).sum(axis=(0, 1))   # (K,)

        if not np.isfinite(weights).all() or np.abs(weights).max() < eps:
            notes.append("Grad-CAM++ weights degenerate")
            return None

        cam = np.maximum((conv * weights[None, None, :]).sum(axis=-1), 0.0)

        if not np.isfinite(cam).all() or cam.max() <= 0.0:
            notes.append("Grad-CAM++ produced an empty positive map")
            return None

        return cam.astype(np.float32)

    @staticmethod
    def _standard_gradcam(conv: np.ndarray, g1: np.ndarray) -> np.ndarray:
        """Standard Grad-CAM: globally average-pooled gradients as weights."""
        weights = g1.mean(axis=(0, 1))
        cam = np.maximum((conv * weights[None, None, :]).sum(axis=-1), 0.0)
        return cam.astype(np.float32)


# ==============================================================================
# HEATMAP POST-PROCESSING
# ==============================================================================

def postprocess_heatmap(
    cam: np.ndarray,
    out_w: int,
    out_h: int,
    p_low: float = PERCENTILE_LOW,
    p_high: float = PERCENTILE_HIGH,
    blur_sigma: float = BLUR_SIGMA,
) -> np.ndarray:
    """
    cam (small, e.g. 7x7) -> float32 heatmap in [0, 1] at (out_h, out_w).

    Order matters: upsample FIRST, then take percentiles. Percentiles over a
    7x7 = 49-value grid are effectively min/max and give no robustness; over
    the upsampled map they genuinely suppress single-pixel outliers.
    """
    cam = np.nan_to_num(
        np.squeeze(np.asarray(cam, dtype=np.float32)),
        nan=0.0, posinf=0.0, neginf=0.0,
    )

    if cam.ndim != 2:
        raise ValueError(f"CAM must be 2-D, got shape {cam.shape}")

    heat = cv2.resize(
        cam, (int(out_w), int(out_h)),
        interpolation=cv2.INTER_CUBIC,            # bicubic upsampling
    ).astype(np.float32)

    heat = np.maximum(heat, 0.0)                  # bicubic can undershoot < 0

    lo = float(np.percentile(heat, p_low))
    hi = float(np.percentile(heat, p_high))

    if hi - lo < 1e-8:
        peak = float(heat.max())
        heat = heat / peak if peak > 1e-8 else np.zeros_like(heat)
    else:
        heat = (heat - lo) / (hi - lo)

    heat = np.clip(heat, 0.0, 1.0)

    if blur_sigma and blur_sigma > 0:
        k = int(2 * round(3 * blur_sigma) + 1)
        heat = cv2.GaussianBlur(heat, (k, k), blur_sigma)
        heat = np.clip(heat, 0.0, 1.0)

    return np.nan_to_num(heat, nan=0.0, posinf=1.0, neginf=0.0).astype(np.float32)


def differential_heats(cams: List[np.ndarray], out_w: int, out_h: int, blur_sigma: float = BLUR_SIGMA) -> List[np.ndarray]:
    """
    heat_c = relu(CAM_c - mean(CAM_j, j != c)) on the upsampled raw maps, then ONE common scale for all classes
    (the 99th percentile of the strongest class), so a class with no distinctive evidence stays dark instead of being
    stretched to full colour. Returns four float32 maps in [0, 1].
    """
    n = len(cams)
    ups = []
    for c in cams:
        a = np.nan_to_num(np.squeeze(np.asarray(c, dtype=np.float32)), nan=0.0, posinf=0.0, neginf=0.0)
        ups.append(np.maximum(cv2.resize(a, (int(out_w), int(out_h)), interpolation=cv2.INTER_CUBIC), 0.0))
    diffs = [np.maximum(ups[i] - np.mean([ups[j] for j in range(n) if j != i], axis=0), 0.0) for i in range(n)]
    hi = max(float(np.percentile(d, 99.0)) for d in diffs)
    if hi <= 1e-9:
        return [np.zeros_like(d, dtype=np.float32) for d in diffs]
    out = []
    for d in diffs:
        h = np.clip(d / hi, 0.0, 1.0)
        if blur_sigma and blur_sigma > 0:
            k = int(2 * round(3 * blur_sigma) + 1)
            h = np.clip(cv2.GaussianBlur(h, (k, k), blur_sigma), 0.0, 1.0)
        out.append(np.nan_to_num(h, nan=0.0, posinf=1.0, neginf=0.0).astype(np.float32))
    return out


def make_overlay(
    img_bgr: np.ndarray,
    heat: np.ndarray,
    alpha: float = OVERLAY_ALPHA,
    gamma: float = ATTENTION_GAMMA,
) -> np.ndarray:
    """
    Per-pixel alpha blend. Opacity scales with attention, so low-attention
    regions stay nearly transparent instead of washing the whole face red.

    Returns BGR uint8 at the ORIGINAL image resolution.
    """
    if heat.shape[:2] != img_bgr.shape[:2]:
        heat = cv2.resize(
            heat, (img_bgr.shape[1], img_bgr.shape[0]),
            interpolation=cv2.INTER_CUBIC,
        )
        heat = np.clip(heat, 0.0, 1.0)

    color = cv2.applyColorMap(np.uint8(255 * heat), cv2.COLORMAP_JET)

    a = (alpha * np.power(heat, gamma)).astype(np.float32)[..., None]

    blended = img_bgr.astype(np.float32) * (1.0 - a) + color.astype(np.float32) * a

    return np.clip(blended, 0, 255).astype(np.uint8)


def png_base64(img: np.ndarray) -> str:
    ok, buf = cv2.imencode(".png", img)

    if not ok:
        raise RuntimeError("cv2.imencode failed to produce a PNG")

    return base64.b64encode(buf.tobytes()).decode("ascii")



# ==============================================================================
# DIAGNOSTICS  (added v1.1 - read-only; they never alter the heatmap)
# ==============================================================================

# Fixed sample positions (row, col, channel) used to compare the C# tensor with
# this service's tensor value-for-value. Do not change without changing C#.
FINGERPRINT_POINTS = [(0, 0, 0), (0, 223, 1), (112, 112, 2), (223, 0, 0),
                      (223, 223, 1), (56, 168, 2), (168, 56, 0), (112, 60, 1)]


def input_fingerprint(x: np.ndarray) -> dict:
    """
    Fingerprint of the (1,224,224,3) float32 tensor actually fed to the model.
    A C# client computes the same numbers from ITS tensor; any difference means
    the two pipelines are not preprocessing identically.
    """
    t = x[0]
    return {
        "shape": [int(v) for v in x.shape],
        "min": float(t.min()),
        "max": float(t.max()),
        "mean": float(t.mean()),
        "mean_rgb": [float(t[..., c].mean()) for c in range(3)],
        "samples": [float(t[r, c, k]) for r, c, k in FINGERPRINT_POINTS],
    }


def heat_concentration(heat: np.ndarray) -> dict:
    """
    How focused the attention is. Percentile normalisation always stretches the
    map to the full colour range, so a diffuse CAM and a focused CAM look
    equally "hot". These numbers tell the UI (and you) which one it really is.
    """
    total = float(heat.sum()) + 1e-9
    flat = np.sort(heat.ravel())[::-1]
    top10 = float(flat[: max(1, flat.size // 10)].sum() / total)
    ys, xs = np.mgrid[0:heat.shape[0], 0:heat.shape[1]]
    return {
        "top10pct_mass": round(top10, 4),          # 0.10 = uniform, 1.0 = one spot
        "centroid_x": round(float((xs * heat).sum() / total / heat.shape[1]), 4),
        "centroid_y": round(float((ys * heat).sum() / total / heat.shape[0]), 4),
    }

# Mean pairwise Pearson (over the RAW class CAMs) at/above which the four class
# maps are reported as "largely shared". Reporting only - it never changes a map.
SIMILARITY_HIGH = 0.90
SIMILARITY_MODERATE = 0.70


def _pearson(a, b) -> float:
    """Pearson r of two arrays; 0.0 when either is constant (never NaN)."""
    a = np.asarray(a, dtype=np.float64).ravel()
    b = np.asarray(b, dtype=np.float64).ravel()
    a = a - a.mean()
    b = b - b.mean()
    d = float(np.sqrt((a * a).sum() * (b * b).sum()))
    return float((a * b).sum() / d) if d > 1e-12 else 0.0


def class_map_similarity(cams: List[np.ndarray], heats: List[np.ndarray]) -> dict:
    """
    How alike the four class maps are, measured on the RAW (un-normalised,
    pre-colour) CAMs, plus top-10% overlap of the rendered heatmaps.

    This is a measurement for the researcher / UI. It does not alter any map.
    For a GlobalAveragePooling head every class CAM is a re-weighting of the
    same activation maps, so high similarity is expected and is reported
    honestly instead of being hidden by per-class normalisation.
    """
    n = len(cams)
    pairs: Dict[str, float] = {}
    vals: List[float] = []
    ious: List[float] = []

    # '> 0' guard: if >90% of a map is zero the percentile is 0 and every pixel
    # would count as "top 10%", inflating IoU.
    tops = [(h >= np.percentile(h, 90.0)) & (h > 0.0) for h in heats]

    for i in range(n):
        for j in range(i + 1, n):
            r = _pearson(cams[i], cams[j])
            pairs[f"{CLASS_NAMES[i]}|{CLASS_NAMES[j]}"] = round(r, 4)
            vals.append(r)
            union = int((tops[i] | tops[j]).sum())
            ious.append(float((tops[i] & tops[j]).sum()) / union if union else 0.0)

    mean_cam = np.mean([np.asarray(c, dtype=np.float64) for c in cams], axis=0)
    shared = {
        CLASS_NAMES[i]: round(max(0.0, _pearson(cams[i], mean_cam)) ** 2, 4)
        for i in range(n)
    }

    mean_r = float(np.mean(vals)) if vals else 0.0
    level = "high" if mean_r >= SIMILARITY_HIGH else (
        "moderate" if mean_r >= SIMILARITY_MODERATE else "low")

    return {
        "level": level,
        "maps_largely_shared": bool(level == "high"),
        "mean_pairwise_pearson": round(mean_r, 4),
        "min_pairwise_pearson": round(float(min(vals)) if vals else 0.0, 4),
        "max_pairwise_pearson": round(float(max(vals)) if vals else 0.0, 4),
        "mean_top10pct_iou": round(float(np.mean(ious)) if ious else 0.0, 4),
        "pairwise_pearson": pairs,
        "shared_component_r2": shared,
        "basis": "raw ReLU'd Grad-CAM++ maps at feature-map resolution (before percentile "
                 "normalisation, blur and colour)",
    }


# ==============================================================================
# ENGINE  (model held in RAM for the process lifetime)
# ==============================================================================

class GradCamEngine:

    def __init__(self, model_path: str, layer_name: str = PRIMARY_LAYER):
        import tensorflow as tf

        self.tf = tf
        self.model_path = os.path.abspath(model_path)

        if not os.path.exists(self.model_path):
            raise FileNotFoundError(f"Keras model not found: {self.model_path}")

        # Inference is float32 even if training used mixed_float16.
        try:
            tf.keras.mixed_precision.set_global_policy("float32")
        except Exception:
            pass

        # Leave a core free for the camera/UI on a 4-core i5.
        try:
            tf.config.threading.set_intra_op_parallelism_threads(3)
            tf.config.threading.set_inter_op_parallelism_threads(1)
        except Exception:
            pass

        t0 = time.perf_counter()
        log.info("Loading Keras model: %s", self.model_path)

        self.model = tf.keras.models.load_model(self.model_path, compile=False)

        log.info("Model loaded in %.1f s", time.perf_counter() - t0)
        log.info("Input  : %s", self.model.input_shape)
        log.info("Output : %s", self.model.output_shape)

        self.cams: Dict[str, GradCAMPlusPlus] = {}
        self.cams[layer_name] = GradCAMPlusPlus(self.model, layer_name)
        self.default_layer = layer_name

        out_dim = int(self.model.output_shape[-1])
        if out_dim != len(CLASS_NAMES):
            raise RuntimeError(
                f"Model outputs {out_dim} classes but CLASS_NAMES has "
                f"{len(CLASS_NAMES)}. Refusing to start."
            )

        self._parity_check()

        self.lock = threading.Lock()
        self.request_count = 0
        self.dropped_stale = 0
        self._seq_lock = threading.Lock()
        self._seq_counter = 0        # arrival order of requests
        self._latest_seq = 0         # newest arrival seen so far
        self.started_at = time.time()

        self._warmup()

    def _warmup(self) -> None:
        """First call compiles graphs (~1-3 s). Do it now, not on the user's click."""
        dummy = np.zeros((1, IMG_SIZE, IMG_SIZE, 3), dtype=np.float32)
        t0 = time.perf_counter()
        self.cams[self.default_layer].compute(dummy)
        log.info("Warm-up Grad-CAM++ pass: %.0f ms", (time.perf_counter() - t0) * 1000)

        # All-class path: first call traces the graph (compiled mode). Do it now.
        t1 = time.perf_counter()
        eng = self.cams[self.default_layer]
        eng.compute_all(dummy)
        eng.compute_all(dummy)
        t2 = time.perf_counter()
        eng.compute_all(dummy)
        log.info("Warm-up all-class pass: trace+2 runs %.0f ms | steady %.0f ms | mode=%s",
                 (t2 - t1) * 1000, (time.perf_counter() - t2) * 1000, eng._all_fn_mode)

    def _parity_check(self) -> None:
        """The Grad-CAM forward path must reproduce the real model's output."""
        rng = np.random.default_rng(0)
        probe = rng.uniform(-1.0, 1.0, (2, IMG_SIZE, IMG_SIZE, 3)).astype("float32")
        ref = self.model(probe, training=False).numpy()
        cam = self.cams[self.default_layer]
        got = np.concatenate([
            cam._forward(self.tf.convert_to_tensor(probe[i:i + 1]))[2].numpy()
            for i in range(2)
        ])
        diff = float(np.abs(ref - got).max())
        # 1e-4 was too strict: float32 softmax outputs differ by ~3e-4 between CPUs / oneDNN builds (seen on the WSL CPU), which would
        # make the service refuse to start on the mini PC. A genuinely different function differs by orders of magnitude more.
        if diff > 5e-3:
            raise RuntimeError(
                f"Grad-CAM forward path differs from model output (max diff "
                f"{diff:.2e}). Maps would explain a different function. "
                "Refusing to start.")
        log.info("Forward-path parity with model verified (max diff %.2e)", diff)

    def get_cam(self, layer_name: str) -> GradCAMPlusPlus:
        if layer_name not in self.cams:
            if layer_name not in SUPPORTED_LAYERS:
                raise ValueError(
                    f"Unsupported layer '{layer_name}'. Allowed: {SUPPORTED_LAYERS}"
                )
            self.cams[layer_name] = GradCAMPlusPlus(self.model, layer_name)
        return self.cams[layer_name]

    # ------------------------------------------------------------------ run
    def explain(
        self,
        image_bytes: bytes,
        class_index: Optional[int] = None,
        method: str = "gradcam++",
        layer_name: Optional[str] = None,
        return_overlay: bool = True,
        return_heatmap: bool = True,
        frame_id: Optional[str] = None,
        drop_if_stale: bool = False,
    ) -> dict:
        """
        frame_id       : opaque string echoed back unchanged. The client MUST
                         compare it with the id of the frame it is displaying
                         and discard the result on mismatch.
        drop_if_stale  : LIVE mode. If a newer request arrived while this one
                         was waiting for the model, skip it (latest-frame-wins,
                         no queue build-up). Never set for a capture request.

        return_overlay=False is the LIVE-MODE path: the client gets only the
        raw heatmap and composites it onto its own video frames locally. This
        skips one colormap pass and one full-size PNG encode per request,
        which is the single biggest saving available at a 2 s refresh rate.
        """

        t_start = time.perf_counter()

        with self._seq_lock:
            self._seq_counter += 1
            my_seq = self._seq_counter
            self._latest_seq = my_seq

        img_bgr = decode_image(image_bytes)
        orig_h, orig_w = img_bgr.shape[:2]

        x = preprocess(img_bgr)

        t_pre = time.perf_counter()

        layer = layer_name or self.default_layer

        # Keras/TF graph execution is not re-entrant per model; serialise.
        with self.lock:
            if drop_if_stale and self._latest_seq != my_seq:
                self.dropped_stale += 1
                return {
                    "ok": False,
                    "stale": True,
                    "frame_id": frame_id,
                    "error": "superseded by a newer frame",
                }
            cam_engine = self.get_cam(layer)
            # v2.0: the compiled all-class path serves single-class requests too
            # (same maths, ~10-30x faster than eager). Pick the requested class.
            cams_all, pred_index, probabilities, method_used, notes = cam_engine.compute_all(
                x, method=method
            )
            _t = pred_index if class_index is None else int(class_index)
            if not 0 <= _t < len(CLASS_NAMES):
                raise ValueError(f"class_index {_t} out of range")
            cam = cams_all[_t]
            self.request_count += 1

        t_cam = time.perf_counter()

        heat = postprocess_heatmap(cam, orig_w, orig_h)

        heat_png = png_base64(np.uint8(255 * heat)) if return_heatmap else None

        if return_overlay:
            overlay_png = png_base64(make_overlay(img_bgr, heat))
        else:
            overlay_png = None

        t_end = time.perf_counter()

        target_index = pred_index if class_index is None else int(class_index)

        response = {
            "ok": True,
            "predicted_index": int(pred_index),
            "predicted_class": CLASS_NAMES[int(pred_index)],
            "confidence": float(probabilities[int(pred_index)]),
            "probabilities": {
                name: float(probabilities[i]) for i, name in enumerate(CLASS_NAMES)
            },
            "explained_index": int(target_index),
            "explained_class": CLASS_NAMES[int(target_index)],
            "method": method_used,
            "target_layer": layer,
            "frame_id": frame_id,
            "image_size": [int(orig_w), int(orig_h)],
            "heatmap_size": [int(heat.shape[1]), int(heat.shape[0])],
            "cam_grid": [int(cam.shape[0]), int(cam.shape[1])],
            "input_fingerprint": input_fingerprint(x),
            "heat_concentration": heat_concentration(heat),
            "heatmap_min": float(heat.min()),
            "heatmap_max": float(heat.max()),
            "latency_ms": round((t_end - t_start) * 1000, 1),
            "timings_ms": {
                "preprocess": round((t_pre - t_start) * 1000, 1),
                "gradcam": round((t_cam - t_pre) * 1000, 1),
                "render_encode": round((t_end - t_cam) * 1000, 1),
            },
            "overlay_base64": overlay_png,
            "heatmap_base64": heat_png,
        }

        if notes:
            response["notes"] = notes

        return response


    # ------------------------------------------------------------ explain_all
    def explain_all(
        self,
        image_bytes: bytes,
        method: str = "gradcam++",
        max_side: int = ALL_CLASS_MAX_SIDE,
        frame_id: Optional[str] = None,
        drop_if_stale: bool = False,
        include_raw_cam: bool = False,
        map_mode: Optional[str] = None,
    ) -> dict:
        """
        Four class-specific Grad-CAM++ maps from ONE pass.

        map_mode: "differential" (default, see DEFAULT_MAP_MODE) or "attribution" (each plain map normalised separately).

        v2.1: include_raw_cam=True also returns each class's RAW CAM (float32,
        little-endian, row-major, shape raw_cam_shape) so the research team can
        inspect attribution quantitatively; class_map_similarity (always
        returned) reports how alike the four raw maps are.

        HONESTY NOTES (also returned in the response):
          * Each heatmap is normalised PER CLASS (1st->0, 99th->1 percentile),
            so a map's SHAPE is comparable but its brightness is not.
          * raw_peak is the un-normalised CAM maximum for that class and
            relative_strength = raw_peak / max(raw_peak over the 4 classes).
            It says how strongly this class's evidence is expressed compared
            with the others for THIS image. It is NOT a probability.
          * None of this is lesion segmentation or a diagnosis.
        """
        t_start = time.perf_counter()

        with self._seq_lock:
            self._seq_counter += 1
            my_seq = self._seq_counter
            self._latest_seq = my_seq

        img_bgr = decode_image(image_bytes)
        orig_h, orig_w = img_bgr.shape[:2]
        x = preprocess(img_bgr)
        t_pre = time.perf_counter()

        with self.lock:
            if drop_if_stale and self._latest_seq != my_seq:
                self.dropped_stale += 1
                return {"ok": False, "stale": True, "frame_id": frame_id,
                        "error": "superseded by a newer frame"}
            cam_engine = self.get_cam(self.default_layer)
            cams, pred_index, probs, method_used, notes = cam_engine.compute_all(x, method=method)
            self.request_count += 1

        t_cam = time.perf_counter()

        scale = min(1.0, float(max_side) / float(max(orig_w, orig_h)))
        out_w = max(8, int(round(orig_w * scale)))
        out_h = max(8, int(round(orig_h * scale)))
        sigma = BLUR_SIGMA * (out_w / float(orig_w))

        raw_peaks = [float(c.max()) for c in cams]
        global_peak = max(raw_peaks) if raw_peaks else 0.0

        mode = (map_mode or DEFAULT_MAP_MODE).strip().lower()
        if mode not in ("differential", "attribution"):
            mode = DEFAULT_MAP_MODE
        diff_maps = differential_heats(cams, out_w, out_h, blur_sigma=sigma) if mode == "differential" else None

        classes = []
        heats: List[np.ndarray] = []
        for i, name in enumerate(CLASS_NAMES):
            heat = diff_maps[i] if diff_maps is not None else postprocess_heatmap(cams[i], out_w, out_h, blur_sigma=sigma)
            heats.append(heat)
            conc = heat_concentration(heat)
            rel = (raw_peaks[i] / global_peak) if global_peak > 1e-12 else 0.0
            entry = {
                "index": i,
                "class": name,
                "probability": float(probs[i]),
                "is_predicted": bool(i == pred_index),
                "raw_peak": raw_peaks[i],
                "relative_strength": round(float(rel), 4),
                "top10pct_mass": conc["top10pct_mass"],
                "centroid_x": conc["centroid_x"],
                "centroid_y": conc["centroid_y"],
                "diffuse": bool(conc["top10pct_mass"] < DIFFUSE_TOP10_MASS or raw_peaks[i] <= 1e-12),
                "heatmap_base64": png_base64(np.uint8(255 * heat)),
            }
            if include_raw_cam:
                raw = np.ascontiguousarray(np.squeeze(cams[i]), dtype="<f4")
                entry["raw_cam_shape"] = [int(raw.shape[0]), int(raw.shape[1])]
                entry["raw_cam_base64"] = base64.b64encode(raw.tobytes()).decode("ascii")
            classes.append(entry)

        similarity = class_map_similarity(cams, heats)

        t_end = time.perf_counter()

        response = {
            "ok": True,
            "all_classes": True,
            "service_version": SERVICE_VERSION,
            "predicted_index": int(pred_index),
            "predicted_class": CLASS_NAMES[int(pred_index)],
            "confidence": float(probs[int(pred_index)]),
            "probabilities": {n: float(probs[i]) for i, n in enumerate(CLASS_NAMES)},
            "classes": classes,
            "class_map_similarity": similarity,
            "method": method_used,
            "map_mode": mode,
            "target_layer": self.default_layer,
            "exec_mode": cam_engine._all_fn_mode,
            "frame_id": frame_id,
            "image_size": [int(orig_w), int(orig_h)],
            "heatmap_size": [int(out_w), int(out_h)],
            "input_fingerprint": input_fingerprint(x),
            "latency_ms": round((t_end - t_start) * 1000, 1),
            "timings_ms": {
                "preprocess": round((t_pre - t_start) * 1000, 1),
                "gradcam": round((t_cam - t_pre) * 1000, 1),
                "render_encode": round((t_end - t_cam) * 1000, 1),
            },
            "normalization_note": (
                ("Differential maps: each class map shows where that class's evidence is stronger than the average of the other classes "
                 "(shared face attention removed), all four on ONE common scale; a class with no distinctive evidence stays dark. "
                 if mode == "differential" else
                 "Each class heatmap is normalised separately; compare shape, not brightness. ") +
                "relative_strength compares raw CAM peaks across classes and is not a probability."
            ),
        }
        if notes:
            response["notes"] = notes
        return response


ENGINE: Optional[GradCamEngine] = None


def build_response_error(message: str) -> dict:
    return {"ok": False, "error": str(message)}


def handle_gradcam(payload: dict) -> dict:
    """Shared request handler - used by both the FastAPI and stdlib servers."""
    if ENGINE is None:
        return build_response_error("Model not loaded")

    b64 = payload.get("image_base64")

    if not b64 or not isinstance(b64, str):
        return build_response_error("Missing 'image_base64'")

    # Tolerate data-URL prefixes from web clients.
    if b64.startswith("data:"):
        b64 = b64.split(",", 1)[-1]

    try:
        image_bytes = base64.b64decode(b64, validate=False)
    except (binascii.Error, ValueError) as exc:
        return build_response_error(f"Invalid base64: {exc}")

    if not image_bytes:
        return build_response_error("Empty image payload")

    if len(image_bytes) > MAX_IMAGE_BYTES:
        return build_response_error(
            f"Image too large ({len(image_bytes)} bytes, max {MAX_IMAGE_BYTES})"
        )

    class_index = payload.get("class_index", None)

    if class_index is not None:
        try:
            class_index = int(class_index)
        except (TypeError, ValueError):
            return build_response_error("class_index must be an integer or null")

    method = payload.get("method") or "gradcam++"
    layer = payload.get("layer") or None

    # Live mode sends return_overlay=false and blends client-side.
    return_overlay = bool(payload.get("return_overlay", True))
    return_heatmap = bool(payload.get("return_heatmap", True))

    frame_id = payload.get("frame_id", None)
    frame_id = None if frame_id is None else str(frame_id)
    drop_if_stale = bool(payload.get("drop_if_stale", False))

    try:
        if bool(payload.get("all_classes", False)):
            return ENGINE.explain_all(
                image_bytes=image_bytes,
                method=method,
                max_side=int(payload.get("heatmap_max_side") or ALL_CLASS_MAX_SIDE),
                frame_id=frame_id,
                drop_if_stale=drop_if_stale,
                include_raw_cam=bool(payload.get("include_raw_cam", False)),
                map_mode=payload.get("map_mode") or None,
            )

        return ENGINE.explain(
            frame_id=frame_id,
            drop_if_stale=drop_if_stale,
            image_bytes=image_bytes,
            class_index=class_index,
            method=method,
            layer_name=layer,
            return_overlay=return_overlay,
            return_heatmap=return_heatmap,
        )
    except Exception as exc:                                   # noqa: BLE001
        log.exception("Grad-CAM request failed")
        return build_response_error(f"{type(exc).__name__}: {exc}")


def handle_health() -> dict:
    if ENGINE is None:
        return {"ok": False, "status": "loading", "model_loaded": False}

    return {
        "ok": True,
        "status": "ready",
        "model_loaded": True,
        "model_path": ENGINE.model_path,
        "classes": CLASS_NAMES,
        "class_mapping": {n: i for i, n in enumerate(CLASS_NAMES)},
        "service_version": SERVICE_VERSION,
        "features": ["gradcam", "all_classes", "frame_id", "drop_if_stale"],
        "exec_mode": ENGINE.cams[ENGINE.default_layer]._all_fn_mode,
        "dropped_stale": ENGINE.dropped_stale,
        "input_size": [IMG_SIZE, IMG_SIZE, 3],
        "normalization": "(pixel / 127.5) - 1.0",
        "channel_order": "RGB",
        "target_layer": ENGINE.default_layer,
        # A live client blending the raw heatmap itself must use these to
        # match the server-rendered overlay exactly.
        "overlay_alpha": OVERLAY_ALPHA,
        "overlay_attention_gamma": ATTENTION_GAMMA,
        "overlay_colormap": "COLORMAP_JET",
        "supported_layers": SUPPORTED_LAYERS,
        "feature_path": ENGINE.cams[ENGINE.default_layer].feature_path,
        "requests_served": ENGINE.request_count,
        "uptime_seconds": round(time.time() - ENGINE.started_at, 1),
    }


# ==============================================================================
# SERVER  (FastAPI if available, stdlib http.server otherwise)
# ==============================================================================

def run_fastapi(host: str, port: int) -> bool:
    try:
        import uvicorn
        from fastapi import FastAPI
        from fastapi.responses import JSONResponse
        from pydantic import BaseModel
    except ImportError:
        return False

    class GradCamRequest(BaseModel):
        image_base64: str
        class_index: Optional[int] = None
        method: Optional[str] = "gradcam++"
        layer: Optional[str] = None
        return_overlay: Optional[bool] = True
        return_heatmap: Optional[bool] = True
        frame_id: Optional[str] = None
        drop_if_stale: Optional[bool] = False
        all_classes: Optional[bool] = False
        heatmap_max_side: Optional[int] = None
        include_raw_cam: Optional[bool] = False

    # This file uses "from __future__ import annotations", so FastAPI looks the annotation "GradCamRequest"
    # up in the MODULE namespace. The class is defined inside this function, so publish it there; otherwise
    # newer FastAPI treats the body as a missing query field and answers every request with HTTP 422.
    globals()["GradCamRequest"] = GradCamRequest

    app = FastAPI(title="PrecisionSkin Grad-CAM++ Service", version=SERVICE_VERSION)

    @app.get("/health")
    def health():
        return JSONResponse(handle_health())

    @app.post("/gradcam")
    def gradcam(req: GradCamRequest):
        result = handle_gradcam(req.model_dump() if hasattr(req, "model_dump") else req.dict())
        return JSONResponse(result, status_code=200 if (result.get("ok") or result.get("stale")) else 400)

    log.info("Serving with FastAPI/uvicorn on http://%s:%d", host, port)
    uvicorn.run(app, host=host, port=port, log_level="warning", workers=1)
    return True


def run_stdlib(host: str, port: int) -> None:
    from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, fmt, *args):
            log.debug("%s - %s", self.address_string(), fmt % args)

        def _send(self, obj: dict, status: int = 200) -> None:
            body = json.dumps(obj).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            if self.path.rstrip("/") == "/health":
                self._send(handle_health())
            else:
                self._send({"ok": False, "error": "Not found"}, 404)

        def do_POST(self):
            if self.path.rstrip("/") != "/gradcam":
                self._send({"ok": False, "error": "Not found"}, 404)
                return

            if "application/json" not in (self.headers.get("Content-Type") or "").lower():
                self._send({"ok": False, "error": "Content-Type must be application/json"}, 415)
                return

            try:
                length = int(self.headers.get("Content-Length", 0))
            except ValueError:
                self._send({"ok": False, "error": "Bad Content-Length"}, 400)
                return

            if length <= 0 or length > MAX_IMAGE_BYTES * 2:
                self._send({"ok": False, "error": "Bad request size"}, 400)
                return

            raw = self.rfile.read(length)

            try:
                payload = json.loads(raw.decode("utf-8"))
            except (UnicodeDecodeError, json.JSONDecodeError) as exc:
                self._send({"ok": False, "error": f"Invalid JSON: {exc}"}, 400)
                return

            result = handle_gradcam(payload)
            self._send(result, 200 if (result.get("ok") or result.get("stale")) else 400)

    server = ThreadingHTTPServer((host, port), Handler)
    log.info("Serving with stdlib http.server on http://%s:%d", host, port)
    server.serve_forever()


# ==============================================================================
# MAIN
# ==============================================================================

def main() -> int:
    global ENGINE

    parser = argparse.ArgumentParser(description="PrecisionSkin Grad-CAM++ service")
    parser.add_argument("--model", default=DEFAULT_MODEL_PATH,
                        help="Path to precisionskin_best.keras")
    parser.add_argument("--labels", default=DEFAULT_LABELS_PATH,
                        help="Path to labels.json (optional, for class-name check)")
    parser.add_argument("--layer", default=PRIMARY_LAYER, choices=SUPPORTED_LAYERS)
    parser.add_argument("--host", default=HOST)
    parser.add_argument("--port", type=int, default=PORT)
    args = parser.parse_args()

    if args.host not in ("127.0.0.1", "localhost"):
        log.warning(
            "Host %s is not loopback. This service has no authentication - "
            "bind to 127.0.0.1 only.", args.host
        )

    # Cross-check class names against labels.json if present.
    if os.path.exists(args.labels):
        try:
            with open(args.labels, "r", encoding="utf-8") as fh:
                labels = json.load(fh)
            if labels.get("classes") and labels["classes"] != CLASS_NAMES:
                log.error(
                    "CLASS NAME MISMATCH.\n  labels.json : %s\n  service     : %s\n"
                    "Fix CLASS_NAMES in this file before deploying.",
                    labels["classes"], CLASS_NAMES,
                )
                return 2
            log.info("labels.json class order verified: %s", CLASS_NAMES)
        except Exception as exc:                                # noqa: BLE001
            log.warning("Could not read labels.json (%s) - continuing.", exc)

    try:
        ENGINE = GradCamEngine(args.model, layer_name=args.layer)
    except Exception as exc:                                    # noqa: BLE001
        log.error("Startup failed: %s", exc)
        return 1

    log.info("=" * 62)
    log.info("PrecisionSkin Grad-CAM++ service READY")
    log.info("  GET  http://%s:%d/health", args.host, args.port)
    log.info("  POST http://%s:%d/gradcam", args.host, args.port)
    log.info("=" * 62)

    try:
        if not run_fastapi(args.host, args.port):
            log.info("FastAPI not installed - using stdlib server.")
            run_stdlib(args.host, args.port)
    except KeyboardInterrupt:
        log.info("Shutting down.")

    return 0


if __name__ == "__main__":
    sys.exit(main())
