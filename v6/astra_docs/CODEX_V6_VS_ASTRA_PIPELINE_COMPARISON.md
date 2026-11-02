# Codex V6 vs Astra pipeline comparison

The reference was the actual existing `v6/notebook_src` tree, including its unfinished recovery helper, not a reimplementation from a prose architecture. Original notebook/source files were not regenerated. `source_comparison.json` records every corresponding source hash; `CODEX_TO_ASTRA.patch` provides the exact line-level changes. The self-contained notebook embeds the Astra sources.

## Reused implementation

Global MobileNetV2 builders (ImageNet and V5 comparison initialization), 128-dimensional embedding, masked BCE, input preprocessing, augmentation, BatchNorm freezing, existing optimizer/LR/epoch choices; the three ONNX specialist ensemble interfaces, letterbox/coordinate/NMS/aggregation logic; ROI matching/cropping/classes and 64-dimensional embedding; trainable fusion layers, modality dropout and stable masked attention; grouped outer cross-fitting and grouped inner selection; Platt calibration, thresholds, quality/OOD/reliability functions, bounded verification, immutable/separate dermatologist review contracts, localization and most thesis metrics/plots are retained.

Verified feature contract: `[Acne, Hyperpigmentation, Eczema]`; ROI `[Acne, Hyperpigmentation, Eczema, Background]`; global embedding 128; detector evidence 3×7=21; ROI representation 64+4+15=83, max 12. Normal remains a dataset category and derived no-target-condition outcome.

## Corrections and improvements

- **Output isolation/control:** run-only writing, existing annotation/split/key read-only, unique validated run IDs, one-use researcher action tokens. Setup tested in isolation; no real training or final evaluation executed. Does not change model design.
- **Global phase selection:** original current loop saved Phase B automatically. Astra compares validation-best A/B and restores the selected full checkpoint; B wins ties. Tested A/B/tie/missing file/reload. Changes which checkpoint can represent an experiment, as required.
- **ROI phase selection:** the current original already restored A/B correctly. Astra retains its tie rule and uses the shared full-checkpoint selector; this is not reported as an unfixed original bug.
- **Recovery:** the original helper existed but training functions did not call it. Astra connects it to global/ROI and per-fold fusion; hashes config/data/annotations/splits/source/model contracts; retains optimizer and controller state at committed epochs. It saves the optimizer from the actual selected epoch, avoiding best weights paired with the last epoch's optimizer. Completed jobs reuse; only incomplete fusion jobs restart. Synthetic recovery and mismatch tests passed. No exact stochastic continuation claim.
- **Memory:** requested batches 8/8/2, 4096 MB GPU cap, bounded pipeline concurrency/prefetch, streaming ROI crops. Changes batch-size experiment settings explicitly; architecture unchanged. Full epochs remain untested.
- **Model choice:** B/C four-way classifiers remain comparison experiments; only three-logit multilabel D/D2 are eligible as the selected hybrid global model. This is an explicit scientific selection-policy correction required by independent condition outputs.
- **Frozen split/labels:** no new split creation; regenerated proxy group IDs must match the frozen manifest. Normal is retained. Accepted image reviews require reviewer ID/timestamp; evaluation requires recorded blinding in addition to the original role/status checks. This tightens eligibility, not clinical evidence. Source policy remains fail-closed. Missing fields do not become labels or permissions.
- **Evidence caches:** run identity prevents stale cross-run reuse; ROI keys include model hash and tiers. Detector JSONL repair discards a truncated tail before append. Baseline/test detector inference is deferred until explicit test action. Detector proposals never become reference boxes.
- **Evaluation:** Normal/abstention reporting, actual final Stage 2 probabilities (rather than full-tier scores unrelated to the stopped pass), paired accepted-case corrections/errors, ROI validation outputs, runtime scope and error rows. No clinical metric was calculated on the research dataset.
- **Calibration fallback:** no valid reliability band means no supported decision; the original 0.5 fallback could still be satisfied at asymmetric thresholds. Missing class calibration is explicit. The fallback is not a tuned threshold.
- **Export:** concrete `tf.function` conversion supports the installed Keras 3/tf2onnx path; every fusion seed is exported instead of just the first; JSON weight names remain unique; Keras feature models and run/feature contracts are saved. Five synthetic float32 component exports passed ONNX parity. Real trained mixed-precision parity and native C# fallback remain pending.

## Architecture discrepancies resolved in documentation

The actual Stage 1 decision is global-plus-detector learned fusion, with global-only disagreement evidence. Full ROI fusion is integrated in Stage 2, with up to three meaningful ROI evidence views. The live reference eagerly computes ROI crops/features before its Stage 1 branch. Thus Stage 2 adds evidence to the decision, but does not capture new photographs and does not imply all ROI compute is deferred. This implementation was preserved, rather than silently replacing it with a different stage design. Three Stage 2 views are counted separately from initial Stage 1.

Final fusion models use a grouped inner validation holdout; they are not fitted on every fusion row. Historical V5/YOLO exposure persists even though new V6 optimizers respect frozen groups. Subject identity remains unknown; proxy groups are not patient IDs.

## Verification limits

Read the technical validation report for actual counts. Static compilation, synthetic weight updates and inference/export checks do not establish real-data training success, full-epoch safety, clinical validity, independent generalization or WPF deployment. No frozen split, curated image, original model or original annotation was altered. Dataset/source owners and reviewers must resolve real-data prerequisites.
