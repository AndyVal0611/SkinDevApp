# Training-stage execution checklist

Tick each box before moving to the next stage. Every blocked stage prints what is missing; none creates data.

## A. Before running anything
- [ ] WSL: `nvidia-smi` shows the RTX 4060; the training venv's TensorFlow sees the GPU (the first cells print this). The author's dry run through a non-login `bash` saw **no GPU** (`Cannot dlopen some GPU libraries`): start Jupyter/VS Code the same way you started the V5 training so the CUDA libraries are on the path.
- [ ] Close other GPU programs (the notebook uses memory growth + mixed precision; batch 32 fits 8 GB; lower `BATCH_SIZE`/`ROI_BATCH` to 16 if you see OOM).
- [ ] `ORIGINAL_IMAGE_ROOT` points at the original full-size photos (otherwise YOLO/ROI use 224×224 images and the notebook flags it).
- [ ] Back up `~/.lumyvue_v6/pseudonym.key` privately.

## B. Parts I–III (no annotations needed)
- [ ] Preflight table: all *required* inputs found. Fingerprint of originals written.
- [ ] Dataset inventory, integrity, leakage groups built; **frozen split** written (`v6/splits/`). Do not edit it.
- [ ] Parity: static contract passes; level-2 parity (Python vs app records) reviewed; level-3 (`--diagnose`) folders configured if you want identical-tensor parity.
- [ ] V5 baseline inference cached; V5 error pairs (source-label) and galleries reviewed.
- [ ] Review queue (`hard_example_review_queue.csv`) triaged.

## C. Annotation (human work)
- [ ] `dataset_provenance.csv` completed for every `source_key`.
- [ ] `multilabel_annotations.csv`: validation, fusion, test annotated first (by `review_rank`), then train. Dermatologist, blinded.
- [ ] `region_annotations.csv`: verified boxes (+ `background_false_positive`, `exhaustive`), train and evaluation splits.
- [ ] Section 12 gate table all `gate_ok = True` (or consciously lower the minimums and accept wider intervals).

## D. Training (run section by section)
- [ ] Part IV: experiments B, C, D, D2 (seeds 42, 7, 123). Check `global_selection.json` and any `regression` flag.
- [ ] Part V: YOLO inference cache for annotated images (about one second per image on CPU; resumable); overlap diagnostics reviewed.
- [ ] Part VI: ROI tables (leakage assertions pass) → ROI MobileNetV2.
- [ ] Part VII: fusion variants cross-fitted; calibration table; thresholds saved.
- [ ] Part VIII: Stage-1/Stage-2 rules saved (`accepts_nothing` is a legitimate, honest result); OOD model fitted.

## E. Evaluation (one-shot)
- [ ] Re-read Part IX's discipline note. Run the classification-evaluation cell **once**. A second run is refused unless `ALLOW_TEST_REEVALUATION=true`.
- [ ] Ablations, verification statistics, robustness, Grad-CAM++, curves, V5-vs-V6.

## F. Export and report
- [ ] Export status reviewed (`FAILED` components fall back to weights JSON). Keras-vs-ONNX differences ≤ 1e-4 or explained.
- [ ] Unit tests: all pass (skips are listed with reasons).
- [ ] `chapter4_report.md` generated; final cell confirms **originals unchanged** and the **audit chain intact**.
- [ ] Do **not** copy any export into the WPF `Model/` folder until the C# parity procedure has been run and approved.
