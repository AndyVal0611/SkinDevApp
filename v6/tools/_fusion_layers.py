# Custom Keras layers the fusion .keras files need in order to load (copied unchanged from astra_training_src/08_fusion.cellsrc, section 29.2).
# Used only by make_deployment_package.py for the CPU parity check; nothing here trains or alters a model.
@keras.saving.register_keras_serializable(package="lumyvue")
class MaskedAttentionPool(keras.layers.Layer):
    def __init__(self, heads=3, **kw):
        super().__init__(**kw); self.heads = heads; self.score = keras.layers.Dense(heads, name="att_score")
    def call(self, inputs):
        h, m = inputs; h32 = tf.cast(h, tf.float32); mask32 = tf.cast(m, tf.float32); scores = tf.cast(self.score(h), tf.float32)
        scores = tf.where(mask32[..., None] > 0, scores, tf.constant(-1e4, tf.float32))
        weights = tf.nn.softmax(scores, axis=1) * mask32[..., None]
        weights = tf.math.divide_no_nan(weights, tf.reduce_sum(weights, axis=1, keepdims=True))
        pooled = tf.einsum("brk,brh->bkh", weights, h32)
        return tf.reshape(pooled, [tf.shape(h)[0], self.heads * h.shape[-1]])
    def get_config(self):
        c = super().get_config(); c.update(heads=self.heads); return c


@keras.saving.register_keras_serializable(package="lumyvue")
class MaskFraction(keras.layers.Layer):
    def call(self, m): return tf.reduce_sum(m, axis=1, keepdims=True) / tf.cast(tf.shape(m)[1], m.dtype)


@keras.saving.register_keras_serializable(package="lumyvue")
class ModalityDropout(keras.layers.Layer):
    def __init__(self, rate=0.25, **kw):
        super().__init__(**kw); self.rate = rate
    def call(self, x, training=None):
        if not training or self.rate <= 0: return x
        keep = tf.cast(tf.random.uniform([tf.shape(x)[0]] + [1] * (len(x.shape) - 1)) >= self.rate, x.dtype); return x * keep
    def get_config(self):
        c = super().get_config(); c.update(rate=self.rate); return c


class MaskedBCE(keras.losses.Loss):                                  # placeholder so the file loads with compile=False
    def call(self, y_true, y_pred): return y_pred
