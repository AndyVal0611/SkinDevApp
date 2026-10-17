#!/usr/bin/env python
"""onnx_merge.py - merge several exported YOLOv8 ONNX models (same architecture, same input size, same classes) into ONE ONNX file whose output is the MEAN of the models' outputs.
Every model sees the same input ("images"); the outputs ([1, 4 + classes, anchors]: box centre x/y, width, height, then class scores) are averaged anchor by anchor.
The result has the same input and output layout as a single YOLOv8 export, so the app's existing detector code (decode, threshold, NMS) works with it unchanged.
NOTE: this is an average of the raw predictions, a different (simpler) fusion than the box-level weighted box fusion the app does today with three separate files. Check it on validation before replacing anything.
"""
import copy
import numpy as np
import onnx
from onnx import helper, numpy_helper, TensorProto


def merge_average(paths, out_path, weighted=False):
    """weighted=False: plain mean of the outputs. weighted=True: the box coordinates are averaged with each model's own score as the weight (sum(score_k * box_k) / sum(score_k)), the score is the mean score;
    this mimics the confidence-weighted box fusion done on separate files. Needs a single-class model (output [1, 5, anchors])."""
    models = [onnx.load(str(p)) for p in paths]; n = len(models)
    ref = models[0]; in0 = ref.graph.input[0]; out0 = ref.graph.output[0]
    dims_in = [d.dim_value for d in in0.type.tensor_type.shape.dim]; dims_out = [d.dim_value for d in out0.type.tensor_type.shape.dim]
    for m in models[1:]:
        assert [d.dim_value for d in m.graph.input[0].type.tensor_type.shape.dim] == dims_in, "input sizes differ"
        assert [d.dim_value for d in m.graph.output[0].type.tensor_type.shape.dim] == dims_out, "output shapes differ"
        assert m.opset_import[0].version == ref.opset_import[0].version, "opsets differ"
    nodes, inits, outs = [], [], []
    for k, m in enumerate(models):
        g = copy.deepcopy(m.graph); pre = "m%d_" % k; in_name = g.input[0].name
        rn = lambda t: "images" if t == in_name else pre + t
        for init in g.initializer: init.name = pre + init.name; inits.append(init)
        for node in g.node:
            node.input[:] = [rn(t) for t in node.input]; node.output[:] = [pre + t for t in node.output]; node.name = pre + node.name; nodes.append(node)
        outs.append(pre + g.output[0].name)
    if not weighted:
        nodes.append(helper.make_node("Add", [outs[0], outs[1]], ["sum_01"], name="avg_add_01")); last = "sum_01"
        for k in range(2, n):
            nodes.append(helper.make_node("Add", [last, outs[k]], ["sum_0%d" % k], name="avg_add_%d" % k)); last = "sum_0%d" % k
        inits.append(numpy_helper.from_array(np.array(1.0 / n, dtype=np.float32), name="avg_scale"))
        nodes.append(helper.make_node("Mul", [last, "avg_scale"], ["output0"], name="avg_mul"))
    else:
        assert dims_out[1] == 5, "the weighted merge needs a single-class model (4 box values + 1 score)"
        for nm, vals in (("w_s0", [0]), ("w_e4", [4]), ("w_s4", [4]), ("w_e5", [5]), ("w_ax", [1])): inits.append(numpy_helper.from_array(np.array(vals, dtype=np.int64), name=nm))
        inits.append(numpy_helper.from_array(np.array(1.0 / n, dtype=np.float32), name="avg_scale")); inits.append(numpy_helper.from_array(np.array(1e-9, dtype=np.float32), name="w_eps"))
        boxes_w, scores = [], []
        for k, o in enumerate(outs):
            nodes.append(helper.make_node("Slice", [o, "w_s0", "w_e4", "w_ax"], ["bx_%d" % k], name="w_slice_box_%d" % k))
            nodes.append(helper.make_node("Slice", [o, "w_s4", "w_e5", "w_ax"], ["sc_%d" % k], name="w_slice_sc_%d" % k))
            nodes.append(helper.make_node("Mul", ["bx_%d" % k, "sc_%d" % k], ["bw_%d" % k], name="w_mul_%d" % k)); boxes_w.append("bw_%d" % k); scores.append("sc_%d" % k)
        for tag, items in (("nb", boxes_w), ("ns", scores)):
            nodes.append(helper.make_node("Add", [items[0], items[1]], [tag + "_01"], name=tag + "_add_01")); last = tag + "_01"
            for k in range(2, n): nodes.append(helper.make_node("Add", [last, items[k]], ["%s_0%d" % (tag, k)], name="%s_add_%d" % (tag, k))); last = "%s_0%d" % (tag, k)
            if tag == "nb": num = last
            else: sum_sc = last
        nodes.append(helper.make_node("Add", [sum_sc, "w_eps"], ["den"], name="w_den")); nodes.append(helper.make_node("Div", [num, "den"], ["boxes_out"], name="w_div"))
        nodes.append(helper.make_node("Mul", [sum_sc, "avg_scale"], ["score_out"], name="w_mean_score"))
        nodes.append(helper.make_node("Concat", ["boxes_out", "score_out"], ["output0"], name="w_concat", axis=1))
    graph = helper.make_graph(nodes, "mean_of_%d_yolov8" % n, [helper.make_tensor_value_info("images", TensorProto.FLOAT, dims_in)],
                              [helper.make_tensor_value_info("output0", TensorProto.FLOAT, dims_out)], initializer=inits)
    model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", ref.opset_import[0].version)]); model.ir_version = ref.ir_version
    onnx.checker.check_model(model); onnx.save(model, str(out_path)); return out_path
