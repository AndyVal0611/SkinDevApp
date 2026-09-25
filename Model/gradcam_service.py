"""
PrecisionSkin - Live Grad-CAM++ Explanation Service
===================================================

Loads precisionskin_best.keras ONCE at startup and keeps it in RAM.
Listens on 127.0.0.1:8765 only (loopback, never exposed to the network).

Endpoints
---------
GET  /health    -> service + model status
POST /gradcam   -> {"image_base64": "..."} -> prediction + Grad-CAM++ overlay

Preprocessing is IDENTICAL to the ONNX classifier:
    decode -> BGR -> RGB -> resize 224x224 (bilinear) -> float32 -> (p / 127.5) - 1.0

Run:
    python gradcam_service.py --model "C:/PrecisionSkin/models/precisionskin_best.keras"
"""

from __future__ import annotations

import argparse
import base64
import binascii
import io
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
        self.head_layers = [
            layer
            for layer in model.layers
            if layer is not self.backbone
            and not isinstance(layer, tf.keras.layers.InputLayer)
        ]

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
        eps = 1e-10

        g2 = g1 * g1
        g3 = g2 * g1

        sum_a = conv.sum(axis=(0, 1), keepdims=True)        # (1, 1, K)

        denom = 2.0 * g2 + sum_a * g3

        alpha = np.where(np.abs(denom) > eps, g2 / (denom + eps), 0.0)
        alpha = np.where(np.abs(g1) > eps, alpha, 0.0)      # no gradient -> no weight

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

        self.lock = threading.Lock()
        self.request_count = 0
        self.started_at = time.time()

        self._warmup()

    def _warmup(self) -> None:
        """First call compiles graphs (~1-3 s). Do it now, not on the user's click."""
        dummy = np.zeros((1, IMG_SIZE, IMG_SIZE, 3), dtype=np.float32)
        t0 = time.perf_counter()
        self.cams[self.default_layer].compute(dummy)
        log.info("Warm-up Grad-CAM++ pass: %.0f ms", (time.perf_counter() - t0) * 1000)

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
    ) -> dict:
        """
        return_overlay=False is the LIVE-MODE path: the client gets only the
        raw heatmap and composites it onto its own video frames locally. This
        skips one colormap pass and one full-size PNG encode per request,
        which is the single biggest saving available at a 2 s refresh rate.
        """

        t_start = time.perf_counter()

        img_bgr = decode_image(image_bytes)
        orig_h, orig_w = img_bgr.shape[:2]

        x = preprocess(img_bgr)

        t_pre = time.perf_counter()

        layer = layer_name or self.default_layer

        # Keras/TF graph execution is not re-entrant per model; serialise.
        with self.lock:
            cam_engine = self.get_cam(layer)
            cam, pred_index, probabilities, method_used, notes = cam_engine.compute(
                x, class_index=class_index, method=method
            )
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
            "image_size": [int(orig_w), int(orig_h)],
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

    try:
        return ENGINE.explain(
            image_bytes,
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

    app = FastAPI(title="PrecisionSkin Grad-CAM++ Service", version="1.0")

    @app.get("/health")
    def health():
        return JSONResponse(handle_health())

    @app.post("/gradcam")
    def gradcam(req: GradCamRequest):
        result = handle_gradcam(req.model_dump())
        return JSONResponse(result, status_code=200 if result.get("ok") else 400)

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
            self._send(result, 200 if result.get("ok") else 400)

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
